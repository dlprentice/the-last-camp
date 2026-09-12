#!/usr/bin/env python3
"""Fetch credited CC0 photoscanned models from Poly Haven and pack them for Godot.

Each manifest entry downloads the glTF at the chosen texture resolution into the
ignored ``tools/downloads/models/`` cache, then ``gltf-transform optimize`` writes
``models/<name>/<name>.gltf`` with its ``.bin`` and JPEG textures beside it:
uncompressed geometry (Godot 4.7 has no Draco or meshopt decoder), textures capped
at the requested size and, where requested, meshoptimizer simplification for
scan-density meshes. External textures let Godot import them as ordinary VRAM
compressed, mipmapped textures (the script writes their ``.import`` settings), and
avoid the duplicate files Godot extracts from a ``.glb``. The script also writes
``models/SOURCES.md`` with creators, licence, retrieval date, triangle counts and the
alterations made, which the render tools concatenate into every ``credits.md``.
Run ``godot --headless --path . --import`` afterwards.

Usage:
    python3 tools/import_models.py                 # every manifest entry
    python3 tools/import_models.py --only fern_02,rock_07
    python3 tools/import_models.py --credits-only  # preserve retained rows; fill missing rows from cached info
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import pathlib
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODELS = ROOT / "models"
CACHE = ROOT / "tools" / "downloads" / "models"
USER_AGENT = "the-last-camp-importer/1.0 (Godot tech demo)"
API = "https://api.polyhaven.com"
LICENSE = "[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)"


class Entry:
    def __init__(self, asset: str, role: str, res: str = "1k", texture_size: int = 1024,
                 simplify_ratio: float = 0.0, simplify_error: float = 0.0005) -> None:
        self.asset = asset
        self.role = role
        self.res = res
        self.texture_size = texture_size
        self.simplify_ratio = simplify_ratio
        self.simplify_error = simplify_error


# Project name -> Poly Haven asset. All Poly Haven assets are CC0.
MANIFEST: dict[str, Entry] = {
    # deadwood and stumps
    "tree_stump_01": Entry("tree_stump_01", "stump", "2k", 2048),
    "tree_stump_02": Entry("tree_stump_02", "stump"),
    "dead_tree_trunk": Entry("dead_tree_trunk", "fallen trunk", "2k", 2048),
    "dead_tree_trunk_02": Entry("dead_tree_trunk_02", "fallen trunk"),
    "dry_branches_medium_01": Entry("dry_branches_medium_01", "branch litter"),
    # roots
    "root_cluster_01": Entry("root_cluster_01", "surface roots", simplify_ratio=0.5, simplify_error=0.002),
    "root_cluster_02": Entry("root_cluster_02", "surface roots", simplify_ratio=0.4, simplify_error=0.002),
    "single_root": Entry("single_root", "surface root"),
    # rocks
    "rock_moss_set_01": Entry("rock_moss_set_01", "mossy rocks"),
    "rock_moss_set_02": Entry("rock_moss_set_02", "mossy rocks"),
    "boulder_01": Entry("boulder_01", "boulder", "2k", 2048),
    "rock_07": Entry("rock_07", "rock"),
    "rock_09": Entry("rock_09", "rock"),
    "stone_01": Entry("stone_01", "stone"),
    # plants
    "fern_02": Entry("fern_02", "fern"),
    "shrub_01": Entry("shrub_01", "shrub", simplify_ratio=0.5, simplify_error=0.002),
    "shrub_02": Entry("shrub_02", "shrub"),
    "shrub_03": Entry("shrub_03", "shrub"),
    "shrub_04": Entry("shrub_04", "shrub"),
    "nettle_plant": Entry("nettle_plant", "nettle"),
    "weed_plant_02": Entry("weed_plant_02", "weed"),
    "grass_medium_01": Entry("grass_medium_01", "grass clump"),
    "grass_medium_02": Entry("grass_medium_02", "grass clump"),
    "moss_01": Entry("moss_01", "moss patch"),
    # camp props: authored placements near the table, tent, woodpile and fire
    "hatchet": Entry("hatchet", "camp prop"),
    "wooden_bucket_01": Entry("wooden_bucket_01", "camp prop"),
    "wooden_crate_01": Entry("wooden_crate_01", "camp prop"),
    "wicker_basket_01": Entry("wicker_basket_01", "camp prop"),
    "pot_enamel_01": Entry("pot_enamel_01", "camp prop"),
    "brass_pot_01": Entry("brass_pot_01", "camp prop"),
    "handsaw_wood": Entry("handsaw_wood", "camp prop"),
    "modified_thermos": Entry("modified_thermos", "camp prop"),
    "wooden_lantern_01": Entry("wooden_lantern_01", "camp prop"),
    # saplings (scan density; simplified hard for real-time use)
    "fir_sapling": Entry("fir_sapling", "fir sapling", simplify_ratio=0.08, simplify_error=0.02),
    "pine_sapling_small": Entry("pine_sapling_small", "pine sapling", simplify_ratio=0.08, simplify_error=0.02),
}


def cache_matches(dest: pathlib.Path, md5: str | None = None) -> bool:
    return dest.is_file() and dest.stat().st_size > 0 and (md5 is None or file_md5(dest) == md5)


def fetch(url: str, dest: pathlib.Path, md5: str | None = None) -> pathlib.Path:
    if cache_matches(dest, md5):
        return dest
    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"  fetching {url}", flush=True)
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=300) as response, open(dest, "wb") as out:
        shutil.copyfileobj(response, out, 1 << 20)
    if md5 is not None and file_md5(dest) != md5:
        raise RuntimeError(f"MD5 mismatch for {url}")
    return dest


def file_md5(path: pathlib.Path) -> str:
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def api_json(path: str, dest: pathlib.Path) -> dict:
    return json.loads(fetch(f"{API}/{path}", dest).read_text())


def download(entry: Entry) -> pathlib.Path:
    CACHE.mkdir(parents=True, exist_ok=True)
    # Keep the source cache out of Godot's import scan (it lives inside the project tree).
    (CACHE.parent / ".gdignore").touch()
    files = api_json(f"files/{entry.asset}", CACHE / entry.asset / "files.json")
    api_json(f"info/{entry.asset}", CACHE / entry.asset / "info.json")
    variant = files["gltf"][entry.res]["gltf"]
    base = CACHE / entry.asset / entry.res
    sources = [(variant, base / pathlib.Path(variant["url"]).name)]
    sources.extend((meta, base / rel) for rel, meta in variant.get("include", {}).items())
    fetched_source = any(not cache_matches(path, meta.get("md5")) for meta, path in sources)
    for meta, path in sources:
        fetch(meta["url"], path, meta.get("md5"))
    gltf = compose_cutouts(entry, files, sources[0][1])
    if fetched_source:
        # Record a real source retrieval, never the mtime of a checked-in output.
        # Legacy caches without a receipt retain their checked-in credit date.
        (CACHE / entry.asset / "retrieved.json").write_text(json.dumps({
            "asset": entry.asset, "date": dt.date.today().isoformat(),
        }, indent=2) + "\n")
    return gltf


def compose_cutouts(entry: Entry, files: dict, gltf: pathlib.Path) -> pathlib.Path:
    """Poly Haven's JPEG glTF variants carry no alpha although their leaf
    materials are cutouts, so every card would render as a full rectangle.
    When the asset publishes an alpha (or mask) map, fetch it, put it in the
    alpha channel of an RGBA PNG copy of the diffuse map and point the glTF at
    that file. The rewritten glTF sits beside the original."""
    doc = json.loads(gltf.read_text())
    cutout_images: set[int] = set()
    for material in doc.get("materials", []):
        if material.get("alphaMode") in ("MASK", "BLEND"):
            texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture", {})
            if "index" in texture:
                cutout_images.add(doc["textures"][texture["index"]]["source"])
            material["alphaMode"] = "MASK"
            material.setdefault("alphaCutoff", 0.5)
    if not cutout_images:
        return gltf
    alpha_key = next((k for k in ("Alpha", "Mask") if k in files and entry.res in files[k]), None)
    if alpha_key is None:
        print(f"  no alpha map published for {entry.asset}; cutouts stay opaque", flush=True)
        return gltf
    formats = files[alpha_key][entry.res]
    meta = formats.get("png") or formats.get("jpg")
    alpha_path = fetch(meta["url"], gltf.parent / "textures" / pathlib.Path(meta["url"]).name, meta.get("md5"))
    from PIL import Image
    alpha = Image.open(alpha_path).convert("L")
    for index in cutout_images:
        image = doc["images"][index]
        source = gltf.parent / image["uri"]
        rgb = Image.open(source).convert("RGB")
        if alpha.size != rgb.size:
            alpha = alpha.resize(rgb.size, Image.LANCZOS)
        rgba = rgb.copy()
        rgba.putalpha(alpha)
        target = source.with_suffix(".png")
        rgba.save(target, optimize=True)
        image["uri"] = target.relative_to(gltf.parent).as_posix()
        image["mimeType"] = "image/png"
        print(f"  cutout alpha composed into {target.name} from {alpha_path.name}", flush=True)
    rewritten = gltf.with_name(gltf.stem + "_cutout.gltf")
    rewritten.write_text(json.dumps(doc))
    return rewritten


def convert(entry: Entry, gltf: pathlib.Path, out: pathlib.Path) -> None:
    # Conversion must finish before any live model files are replaced. Both a
    # failed candidate and a successfully replaced model remain available here.
    out.relative_to(ROOT)
    if out.parent.is_symlink():
        raise RuntimeError(f"Model output directory must not be a symlink: {out.parent}")
    work_root = ROOT / "local-data" / "model-imports"
    work_root.mkdir(parents=True, exist_ok=True)
    (work_root.parent / ".gdignore").touch()
    work = pathlib.Path(tempfile.mkdtemp(prefix=f"{out.stem}-", dir=work_root))
    staged = work / "candidate"
    staged.mkdir()
    staged_out = staged / out.name
    args = ["gltf-transform", "optimize", str(gltf), str(staged_out),
            "--compress", "false", "--texture-compress", "false",
            "--texture-size", str(entry.texture_size),
            "--palette", "false", "--instance", "false", "--join", "false",
            "--simplify", "true" if entry.simplify_ratio > 0 else "false"]
    if entry.simplify_ratio > 0:
        args += ["--simplify-ratio", str(entry.simplify_ratio), "--simplify-error", str(entry.simplify_error)]
    print("  " + " ".join(args[:4]) + " ...", flush=True)
    print(f"  conversion files and previous model retained in {work}", flush=True)
    with (work / "convert.log").open("w") as log:
        subprocess.run(args, check=True, stdout=log, stderr=subprocess.STDOUT)
    validate_conversion(staged_out)

    # Retain model import settings and texture UIDs only for resources that
    # still exist in the converted model. Keep unrelated notices/files, too.
    generated_suffixes = {".gltf", ".bin", ".jpg", ".jpeg", ".png", ".webp"}
    if out.parent.exists():
        for old in out.parent.rglob("*"):
            if old.is_symlink():
                raise RuntimeError(f"Model directory contains a symlink: {old}")
            if not old.is_file():
                continue
            target = staged / old.relative_to(out.parent)
            if old.name.endswith(".import"):
                retain = target.with_name(target.name[:-len(".import")]).is_file()
            else:
                retain = old.suffix.lower() not in generated_suffixes and not target.exists()
            if retain:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(old, target)
    for image in staged.rglob("*"):
        if image.is_file() and image.suffix.lower() in (".jpg", ".jpeg", ".png", ".webp"):
            write_texture_import(image, out.parent / image.relative_to(staged))

    previous = work / "previous"
    out.parent.parent.mkdir(parents=True, exist_ok=True)
    if out.parent.exists():
        out.parent.rename(previous)
    try:
        staged.rename(out.parent)
    except BaseException:
        if previous.exists() and not out.parent.exists():
            previous.rename(out.parent)
        raise


def validate_conversion(path: pathlib.Path) -> None:
    """Reject missing, truncated or non-local dependencies before promotion."""
    root = path.parent.resolve()
    if any(item.is_symlink() for item in path.parent.rglob("*")):
        raise RuntimeError("Converted model contains a symlink")
    doc = json.loads(path.read_text())
    if doc.get("asset", {}).get("version") != "2.0":
        raise RuntimeError("Converted model is not glTF 2.0")
    for group in ("buffers", "images"):
        for item in doc.get(group, []):
            uri = item.get("uri")
            if uri is None and group == "images" and "bufferView" in item:
                continue
            if not isinstance(uri, str) or not uri:
                raise RuntimeError(f"Converted model has a missing {group} URI")
            if uri.startswith("data:"):
                continue
            parsed = urllib.parse.urlsplit(uri)
            local_path = pathlib.Path(urllib.parse.unquote(parsed.path))
            if parsed.scheme or parsed.netloc or parsed.query or parsed.fragment or "\\" in uri or local_path.is_absolute():
                raise RuntimeError(f"Converted model dependency is not a local file: {uri}")
            dependency = (root / local_path).resolve()
            if not dependency.is_relative_to(root):
                raise RuntimeError(f"Converted model dependency escapes its directory: {uri}")
            if not dependency.is_file() or dependency.stat().st_size == 0:
                raise RuntimeError(f"Converted model dependency is missing or empty: {uri}")
            if group == "buffers" and dependency.stat().st_size < item.get("byteLength", 0):
                raise RuntimeError(f"Converted model buffer is truncated: {uri}")
    tris, verts = glb_stats(path)
    if tris <= 0 or verts <= 0:
        raise RuntimeError("Converted model has no mesh geometry")


def texture_kind(name: str) -> str:
    lower = name.lower()
    if "nor_gl" in lower or "normal" in lower or "_nor" in lower:
        return "normal"
    if "diff" in lower or "albedo" in lower or "color" in lower:
        return "albedo"
    return "data"


def write_texture_import(image: pathlib.Path, project_image: pathlib.Path | None = None) -> None:
    """VRAM compressed (BC7), mipmapped import settings, matching textures/."""
    import_path = image.with_name(image.name + ".import")
    uid = ""
    if import_path.exists():
        match = re.search(r'^uid="([^"]+)"', import_path.read_text(), re.M)
        if match:
            uid = match.group(1)
    source = "res://" + (project_image or image).relative_to(ROOT).as_posix()
    digest = hashlib.md5(source.encode()).hexdigest()
    dest = f"res://.godot/imported/{image.name}-{digest}.ctex"
    kind = texture_kind(image.name)
    uid_line = f'uid="{uid}"\n' if uid else ""
    import_path.write_text(
        "[remap]\n\n"
        'importer="texture"\n'
        'type="CompressedTexture2D"\n'
        f"{uid_line}"
        f'path="{dest}"\n'
        "metadata={\n"
        '"vram_texture": true\n'
        "}\n\n"
        "[deps]\n\n"
        f'source_file="{source}"\n'
        f'dest_files=["{dest}"]\n\n'
        "[params]\n\n"
        "compress/mode=2\n"
        "compress/high_quality=true\n"
        "compress/lossy_quality=0.7\n"
        "compress/uastc_level=0\n"
        "compress/rdo_quality_loss=0.0\n"
        "compress/hdr_compression=1\n"
        f"compress/normal_map={1 if kind == 'normal' else 2}\n"
        "compress/channel_pack=0\n"
        "mipmaps/generate=true\n"
        "mipmaps/limit=-1\n"
        "roughness/mode=0\n"
        'roughness/src_normal=""\n'
        "process/channel_remap/red=0\n"
        "process/channel_remap/green=1\n"
        "process/channel_remap/blue=2\n"
        "process/channel_remap/alpha=3\n"
        "process/fix_alpha_border=true\n"
        "process/premult_alpha=false\n"
        "process/normal_map_invert_y=false\n"
        "process/hdr_as_srgb=false\n"
        "process/hdr_clamp_exposure=false\n"
        "process/size_limit=0\n"
        "detect_3d/compress_to=0\n")


def glb_stats(path: pathlib.Path) -> tuple[int, int]:
    """Return (triangles, vertices) from a .gltf or .glb without a glTF library."""
    if path.suffix == ".gltf":
        doc = json.loads(path.read_text())
    else:
        with open(path, "rb") as f:
            magic, _version, _length = struct.unpack("<III", f.read(12))
            assert magic == 0x46546C67, "not a GLB"
            chunk_length, chunk_type = struct.unpack("<II", f.read(8))
            assert chunk_type == 0x4E4F534A
            doc = json.loads(f.read(chunk_length))
    accessors = doc.get("accessors", [])
    tris = verts = 0
    for mesh in doc.get("meshes", []):
        for prim in mesh.get("primitives", []):
            verts += accessors[prim["attributes"]["POSITION"]]["count"]
            if "indices" in prim:
                tris += accessors[prim["indices"]]["count"] // 3
            else:
                tris += accessors[prim["attributes"]["POSITION"]]["count"] // 3
    return tris, verts


def alterations(entry: Entry) -> str:
    parts = [f"glTF {entry.res} repacked with gltf-transform (flattened, welded, pruned, uncompressed geometry, external JPEG textures with the published alpha map composed into a PNG diffuse for cutout materials; variant and LOD nodes kept separate)",
             f"textures capped at {entry.texture_size} px"]
    if entry.simplify_ratio > 0:
        parts.append(f"mesh simplified with meshoptimizer to about {entry.simplify_ratio:.0%} of the vertices (error {entry.simplify_error})")
    return "; ".join(parts) + "."


def retained_source_rows() -> dict[str, str]:
    """Keep unaffected attribution rows verbatim, including their recorded dates."""
    path = MODELS / "SOURCES.md"
    if not path.exists():
        return {}
    rows = {}
    for line in path.read_text().splitlines():
        match = re.match(r"^\|\s*`([^`/]+)/`\s*\|", line)
        if match and len(line.split("|")) == 10:
            rows[match.group(1)] = line
    return rows


def retrieval_date(entry: Entry, retained_row: str | None) -> str:
    receipt = CACHE / entry.asset / "retrieved.json"
    if receipt.exists():
        record = json.loads(receipt.read_text())
        if record.get("asset") != entry.asset:
            raise RuntimeError(f"Retrieval receipt is for the wrong asset: {receipt}")
        value = record.get("date", "")
    elif retained_row is not None:
        value = retained_row.split("|")[6].strip()
    else:
        raise RuntimeError(f"No recorded retrieval date for {entry.asset}; import its source files first")
    try:
        return dt.date.fromisoformat(value).isoformat()
    except (ValueError, TypeError) as error:
        raise RuntimeError(f"Invalid recorded retrieval date for {entry.asset}: {value!r}") from error


def write_sources(names: list[str], refreshed: set[str] | None = None) -> None:
    retained = retained_source_rows()
    refreshed = refreshed or set()
    rows = []
    for name in names:
        if name not in refreshed and name in retained:
            rows.append(retained[name])
            continue
        entry = MANIFEST[name]
        info_path = CACHE / entry.asset / "info.json"
        if not info_path.exists():
            raise RuntimeError(f"Missing source metadata for {name}; run --only {name} before refreshing its credits")
        info = json.loads(info_path.read_text())
        if not isinstance(info.get("authors"), dict) or not info["authors"]:
            raise RuntimeError(f"Source metadata has no creator credits: {info_path}")
        authors = ", ".join(f"{who} ({what})" for who, what in info["authors"].items())
        out = MODELS / name / f"{name}.gltf"
        tris, verts = glb_stats(out)
        retrieved = retrieval_date(entry, retained.get(name))
        rows.append(f"| `{name}/` | [{info.get('name', entry.asset)}](https://polyhaven.com/a/{entry.asset}) | {authors} | Poly Haven | {LICENSE} | {retrieved} | {tris:,} tris | {alterations(entry)} |")
    (MODELS / "SOURCES.md").write_text(
        "# Model sources\n\n"
        "Photoscanned models used alongside the project's generated geometry. Every file in this folder is "
        "fetched and packed by `tools/import_models.py`; the originals stay in the ignored download cache. "
        "All are Poly Haven assets released under CC0 1.0; creator names come from Poly Haven's asset metadata "
        "and are credited here and in the film credits even though CC0 does not require it.\n\n"
        "| Folder | Asset | Creator credit | Provider | License | Retrieved | Size | Alterations |\n"
        "|---|---|---|---|---|---|---|---|\n" + "\n".join(rows) + "\n\n"
        "Provider license statement: [Poly Haven](https://polyhaven.com/license). "
        "Film credit: “Photoscanned models: Poly Haven and their contributors, CC0 1.0; converted and simplified.”\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--only", help="comma-separated manifest names")
    parser.add_argument("--credits-only", action="store_true", help="preserve retained credit rows; --only refreshes selected rows from cached metadata")
    args = parser.parse_args()
    names = [n for n in (args.only.split(",") if args.only else MANIFEST) if n]
    unknown = [n for n in names if n not in MANIFEST]
    if unknown:
        parser.error(f"unknown manifest names: {', '.join(unknown)}")
    refreshed = set(names) if args.credits_only and args.only else set()
    if not args.credits_only:
        if shutil.which("gltf-transform") is None:
            parser.error("gltf-transform is required (pinned toolchain, ~/.local/bin)")
        for name in names:
            entry = MANIFEST[name]
            print(f"{name} <- polyhaven/{entry.asset} @ {entry.res}", flush=True)
            gltf = download(entry)
            out = MODELS / name / f"{name}.gltf"
            convert(entry, gltf, out)
            tris, verts = glb_stats(out)
            print(f"  {out.relative_to(ROOT)}: {out.stat().st_size / 1e6:.1f} MB, {tris:,} triangles, {verts:,} vertices", flush=True)
            refreshed.add(name)
    # Credits always cover the whole manifest; a partial run must not drop rows.
    write_sources(list(MANIFEST), refreshed)
    print(f"wrote {MODELS / 'SOURCES.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
