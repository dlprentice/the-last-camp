"""Asset-tool regressions using isolated fixtures, never production downloads."""
from __future__ import annotations

import contextlib
import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("import_models", ROOT / "tools/import_models.py")
models = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(models)
BAKE_SPEC = importlib.util.spec_from_file_location("asset_test_baker", ROOT / "tools/bake_textures.py")
baker = importlib.util.module_from_spec(BAKE_SPEC)
sys.modules[BAKE_SPEC.name] = baker  # dataclass type resolution needs the module registered.
BAKE_SPEC.loader.exec_module(baker)


def credit_row(name: str, creator: str = "Original Creator", date: str = "2026-09-10") -> str:
    return (f"| `{name}/` | [{name}](https://polyhaven.com/a/{name}) | {creator} | Poly Haven | "
            f"{models.LICENSE} | {date} | 10 tris | Original alterations. |")


class ModelCredits(unittest.TestCase):
    def setUp(self):
        base = ROOT / "local-data" / "asset-tool-tests"
        base.mkdir(parents=True, exist_ok=True)
        (ROOT / "local-data" / ".gdignore").touch()
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.output = self.path / "models"
        self.output.mkdir()
        self.cache = self.path / "tools/downloads/models"
        self.manifest = {name: models.Entry(name, "fixture") for name in ("untouched", "selected")}
        for name, value in (("ROOT", self.path), ("MODELS", self.output),
                            ("CACHE", self.cache), ("MANIFEST", self.manifest)):
            self.enterContext(mock.patch.object(models, name, value))
        self.sources = self.output / "SOURCES.md"
        self.original_rows = [credit_row(name) for name in self.manifest]
        self.sources.write_text("# Fixture credits\n\n" + "\n".join(self.original_rows) + "\n")

    def metadata(self, name: str, date: str | None = None):
        folder = self.cache / name
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "info.json").write_text(json.dumps({
            "name": "Verified " + name, "authors": {"Verified Creator": "Photography"},
        }))
        if date is not None:
            (folder / "retrieved.json").write_text(json.dumps({"asset": name, "date": date}))

    def geometry(self, path: Path):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({
            "asset": {"version": "2.0"}, "accessors": [{"count": 6}, {"count": 6}],
            "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
        }))
        # A checkout timestamp must never become the retrieval date.
        os.utime(path, (4102444800, 4102444800))

    def test_partial_import_preserves_unselected_rows_without_their_cache(self):
        def download(entry):
            self.metadata(entry.asset, "2026-09-12")
            source = self.cache / entry.asset / "source.gltf"
            self.geometry(source)
            return source

        def convert(entry, source, out):
            self.geometry(out)

        with mock.patch.object(sys, "argv", ["import_models.py", "--only", "selected"]), \
             mock.patch.object(models.shutil, "which", return_value="fixture-converter"), \
             mock.patch.object(models, "download", side_effect=download) as fetched, \
             mock.patch.object(models, "convert", side_effect=convert) as converted, \
             contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(models.main(), 0)
        self.assertEqual(fetched.call_count, 1)
        self.assertEqual(converted.call_count, 1)
        self.assertFalse((self.cache / "untouched").exists())
        rows = models.retained_source_rows()
        self.assertEqual(rows["untouched"], self.original_rows[0])
        self.assertIn("Verified Creator (Photography)", rows["selected"])
        self.assertIn("| 2026-09-12 | 2 tris |", rows["selected"])
        self.assertNotIn("2100-01-01", rows["selected"])

    def test_credits_only_preserves_rows_and_dates_without_any_private_cache(self):
        with mock.patch.object(sys, "argv", ["import_models.py", "--credits-only"]), \
             mock.patch.object(models, "download") as fetched, \
             mock.patch.object(models, "convert") as converted, \
             contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(models.main(), 0)
        fetched.assert_not_called()
        converted.assert_not_called()
        self.assertFalse(self.cache.exists())
        self.assertEqual(list(models.retained_source_rows().values()), self.original_rows)

    def test_refresh_from_legacy_cache_keeps_recorded_date_instead_of_checkout_mtime(self):
        self.metadata("selected")
        self.geometry(self.output / "selected/selected.gltf")
        models.write_sources(list(self.manifest), {"selected"})
        row = models.retained_source_rows()["selected"]
        self.assertIn("Verified Creator (Photography)", row)
        self.assertIn("| 2026-09-10 |", row)

    def test_missing_provenance_does_not_fabricate_a_date_or_replace_existing_credits(self):
        self.sources.write_text("# Fixture credits\n\n" + self.original_rows[0] + "\n")
        original = self.sources.read_text()
        self.metadata("selected")
        self.geometry(self.output / "selected/selected.gltf")
        with self.assertRaisesRegex(RuntimeError, "No recorded retrieval date"):
            models.write_sources(list(self.manifest), {"selected"})
        self.assertEqual(self.sources.read_text(), original)

    def test_missing_creator_metadata_does_not_silently_credit_the_provider(self):
        self.metadata("selected", "2026-09-12")
        (self.cache / "selected/info.json").write_text('{"name": "No author data"}')
        original = self.sources.read_text()
        with self.assertRaisesRegex(RuntimeError, "no creator credits"):
            models.write_sources(list(self.manifest), {"selected"})
        self.assertEqual(self.sources.read_text(), original)

    def test_real_source_fetch_records_a_date_and_cache_reuse_preserves_it(self):
        entry = self.manifest["selected"]
        variant = {"url": "https://example.invalid/selected.gltf"}
        files = {"gltf": {"1k": {"gltf": variant}}}

        def metadata(path, dest):
            dest.parent.mkdir(parents=True, exist_ok=True)
            data = files if path.startswith("files/") else {"authors": {"Test": "All"}}
            dest.write_text(json.dumps(data))
            return data

        def fetch(url, dest, md5=None):
            if not dest.exists():
                self.geometry(dest)
            return dest

        with mock.patch.object(models, "api_json", side_effect=metadata), \
             mock.patch.object(models, "fetch", side_effect=fetch), \
             mock.patch.object(models, "compose_cutouts", side_effect=lambda entry, files, path: path):
            models.download(entry)
            receipt = self.cache / "selected/retrieved.json"
            self.assertEqual(json.loads(receipt.read_text()), {
                "asset": "selected", "date": dt.date.today().isoformat(),
            })
            receipt.write_text('{"asset": "selected", "date": "2026-09-01"}\n')
            models.download(entry)
            self.assertEqual(json.loads(receipt.read_text())["date"], "2026-09-01")


class ModelConversionSafety(unittest.TestCase):
    def setUp(self):
        base = ROOT / "local-data" / "asset-tool-tests"
        base.mkdir(parents=True, exist_ok=True)
        (ROOT / "local-data" / ".gdignore").touch()
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.enterContext(mock.patch.object(models, "ROOT", self.path))
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))
        self.entry = models.Entry("selected", "fixture")
        self.out = self.path / "models/selected/selected.gltf"
        self.out.parent.mkdir(parents=True)
        self.out.write_text("previous model")
        (self.out.parent / "selected.bin").write_bytes(b"previous buffer")
        (self.out.parent / "selected_diff.png").write_bytes(b"previous texture")
        (self.out.parent / "unused.png").write_bytes(b"previous unused texture")
        (self.out.parent / "selected.gltf.import").write_text('[remap]\nuid="uid://modelold"\n')
        (self.out.parent / "selected_diff.png.import").write_text('[remap]\nuid="uid://textureold"\n')
        (self.out.parent / "NOTICE.txt").write_text("retain this notice")
        self.before = self.snapshot(self.out.parent)
        self.source = self.path / "source.gltf"
        self.source.write_text("input fixture")

    @staticmethod
    def snapshot(path):
        return {p.relative_to(path).as_posix(): p.read_bytes() for p in path.rglob("*") if p.is_file()}

    @staticmethod
    def converted_fixture(path):
        (path.parent / "selected.bin").write_bytes(bytes(42))
        baker.Image.new("RGB", (8, 8), (70, 120, 30)).save(path.parent / "selected_diff.png")
        path.write_text(json.dumps({
            "asset": {"version": "2.0"},
            "buffers": [{"uri": "selected.bin", "byteLength": 42}],
            "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 36},
                            {"buffer": 0, "byteOffset": 36, "byteLength": 6}],
            "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"},
                          {"bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR"}],
            "images": [{"uri": "selected_diff.png"}],
            "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
        }))

    def test_converter_failure_leaves_original_model_and_imports_untouched(self):
        def fail(args, **kwargs):
            Path(args[3]).write_text("partial candidate")
            kwargs["stdout"].write("fixture converter failure\n")
            raise models.subprocess.CalledProcessError(1, args)

        with mock.patch.object(models.subprocess, "run", side_effect=fail), \
             self.assertRaises(models.subprocess.CalledProcessError):
            models.convert(self.entry, self.source, self.out)
        self.assertEqual(self.snapshot(self.out.parent), self.before)
        work = next((self.path / "local-data/model-imports").iterdir())
        self.assertEqual((work / "candidate/selected.gltf").read_text(), "partial candidate")
        self.assertIn("fixture converter failure", (work / "convert.log").read_text())

    def test_missing_or_truncated_dependencies_fail_before_replacing_original(self):
        for mode in ("missing", "truncated"):
            def incomplete(args, **kwargs):
                path = Path(args[3])
                self.converted_fixture(path)
                buffer = path.parent / "selected.bin"
                if mode == "missing":
                    buffer.unlink()  # This invocation's synthetic candidate only.
                else:
                    buffer.write_bytes(b"short")

            with self.subTest(mode=mode), \
                 mock.patch.object(models.subprocess, "run", side_effect=incomplete), \
                 self.assertRaisesRegex(RuntimeError, "missing or empty|truncated"):
                models.convert(self.entry, self.source, self.out)
            self.assertEqual(self.snapshot(self.out.parent), self.before)

    def test_external_dependency_path_is_rejected_before_promotion(self):
        for mode in ("parent", "absolute", "remote"):
            def escaped(args, **kwargs):
                path = Path(args[3])
                self.converted_fixture(path)
                doc = json.loads(path.read_text())
                doc["buffers"][0]["uri"] = {
                    "parent": "../elsewhere.bin", "absolute": str(path.parent / "selected.bin"),
                    "remote": "https://example.invalid/selected.bin",
                }[mode]
                (path.parent.parent / "elsewhere.bin").write_bytes(bytes(42))
                path.write_text(json.dumps(doc))

            with self.subTest(mode=mode), \
                 mock.patch.object(models.subprocess, "run", side_effect=escaped), \
                 self.assertRaisesRegex(RuntimeError, "escapes its directory|not a local file"):
                models.convert(self.entry, self.source, self.out)
            self.assertEqual(self.snapshot(self.out.parent), self.before)

    def test_success_promotes_complete_model_preserving_uids_notices_and_previous_copy(self):
        with mock.patch.object(models.subprocess, "run", side_effect=lambda args, **kw: self.converted_fixture(Path(args[3]))):
            models.convert(self.entry, self.source, self.out)
        models.validate_conversion(self.out)
        self.assertEqual(models.glb_stats(self.out), (1, 3))
        self.assertEqual((self.out.parent / "selected.gltf.import").read_bytes(), self.before["selected.gltf.import"])
        texture_import = (self.out.parent / "selected_diff.png.import").read_text()
        self.assertIn('uid="uid://textureold"', texture_import)
        self.assertIn('source_file="res://models/selected/selected_diff.png"', texture_import)
        self.assertNotIn("local-data", texture_import)
        self.assertEqual((self.out.parent / "NOTICE.txt").read_bytes(), self.before["NOTICE.txt"])
        self.assertFalse((self.out.parent / "unused.png").exists())
        work = next((self.path / "local-data/model-imports").iterdir())
        self.assertEqual(self.snapshot(work / "previous"), self.before)

    def test_failed_promotion_restores_original_directory(self):
        original_rename = Path.rename

        def rename(path, target):
            if path.name == "candidate":
                raise OSError("fixture promotion failure")
            return original_rename(path, target)

        with mock.patch.object(models.subprocess, "run", side_effect=lambda args, **kw: self.converted_fixture(Path(args[3]))), \
             mock.patch.object(Path, "rename", new=rename), \
             self.assertRaisesRegex(OSError, "promotion failure"):
            models.convert(self.entry, self.source, self.out)
        self.assertEqual(self.snapshot(self.out.parent), self.before)


class BakerSafety(unittest.TestCase):
    def setUp(self):
        base = ROOT / "local-data" / "asset-tool-tests"
        base.mkdir(parents=True, exist_ok=True)
        (ROOT / "local-data" / ".gdignore").touch()
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.production = self.path / "textures"
        self.production.mkdir()
        self.photoscan = self.production / "grass_albedo.png"
        baker.Image.new("RGB", (8, 8), (80, 100, 60)).save(self.photoscan)
        self.original = self.photoscan.read_bytes()
        self.enterContext(mock.patch.object(baker, "PROJECT_ROOT", self.path))
        self.enterContext(mock.patch.object(baker, "DEFAULT_OUT_DIR", self.production))
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))

    def fixture_bake(self, name, cfg):
        path = cfg.out_dir / (name + ("_albedo.png" if name in baker.FALLBACK_PBR_NAMES else ".png"))
        baker.Image.new("RGB", (8, 8), (120, 40, 20)).save(path)
        return name, [path], 0.0

    def test_default_rebake_generates_atlases_without_replacing_photoscans(self):
        with mock.patch.object(baker, "bake_job", side_effect=self.fixture_bake) as baked:
            self.assertEqual(baker.main(["--size", "64", "--jobs", "1", "--no-sheet"]), 0)
        names = {call.args[0] for call in baked.call_args_list}
        self.assertIn("leaves_oak", names)
        self.assertIn("foam", names)
        self.assertTrue(names.isdisjoint(baker.FALLBACK_PBR_NAMES))
        self.assertEqual(self.photoscan.read_bytes(), self.original)

    def test_fallback_cannot_replace_production_even_through_a_symlink(self):
        alias = self.path / "texture-alias"
        alias.symlink_to(self.production, target_is_directory=True)
        for output in (self.production, alias):
            with self.subTest(output=output), \
                 mock.patch.object(baker, "bake_job") as baked, \
                 self.assertRaisesRegex(SystemExit, "production photoscans"):
                baker.main(["--only", "grass", "--out", str(output), "--no-sheet"])
            baked.assert_not_called()
        self.assertEqual(self.photoscan.read_bytes(), self.original)

    def test_fallback_studies_are_allowed_outside_production(self):
        output = self.path / "study"
        with mock.patch.object(baker, "bake_job", side_effect=self.fixture_bake):
            self.assertEqual(baker.main(["--only", "grass", "--out", str(output), "--jobs", "1", "--no-sheet"]), 0)
        self.assertTrue((output / "grass_albedo.png").exists())
        self.assertNotEqual((output / "grass_albedo.png").read_bytes(), self.original)
        self.assertEqual(self.photoscan.read_bytes(), self.original)

    def test_default_review_directories_are_unique_and_retain_previous_sheets(self):
        with mock.patch.object(baker, "bake_job", side_effect=self.fixture_bake):
            args = ["--only", "foam", "--size", "64", "--jobs", "1", "--check-tiling"]
            baker.main(args)
            first = next((self.path / "local-data/bakes").iterdir())
            original_sheet = (first / "texture_sheet.png").read_bytes()
            self.assertTrue((first / "tiling/foam_rolled.png").exists())
            baker.main(args)
        self.assertEqual(len(list((self.path / "local-data/bakes").iterdir())), 2)
        self.assertEqual((first / "texture_sheet.png").read_bytes(), original_sheet)
        self.assertTrue((self.path / "local-data/.gdignore").exists())

    def test_existing_explicit_review_is_rejected_before_baking(self):
        review = self.path / "review"
        review.mkdir()
        prior = review / "keep.txt"
        prior.write_text("retained evidence")
        with mock.patch.object(baker, "bake_job") as baked, self.assertRaises(FileExistsError):
            baker.main(["--only", "foam", "--review-out", str(review)])
        baked.assert_not_called()
        self.assertEqual(prior.read_text(), "retained evidence")


if __name__ == "__main__":
    unittest.main()
