"""Gameplay-capture tool tests. Synthetic inputs only: NOT evidence of rendered footage."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("gameplay_capture", ROOT / "tools/gameplay_capture.py")
capture = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(capture)


def encoded(frames: int, audio_seconds: float | None = None) -> dict:
    seconds = frames / 60
    return {"streams": [
        {"codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080,
         "avg_frame_rate": "60/1", "duration": f"{seconds}", "nb_frames": str(frames),
         "color_range": "tv", "color_space": "bt709", "color_transfer": "bt709",
         "color_primaries": "bt709", "pix_fmt": "yuv420p"},
        {"codec_type": "audio", "codec_name": "aac", "sample_rate": "48000",
         "duration": f"{seconds if audio_seconds is None else audio_seconds}"}]}


class GameplayCaptureContracts(unittest.TestCase):
    def test_scripts_and_reserved_game_arguments(self):
        self.assertEqual(capture.parse_args(["traversal"]).script, "traversal")
        self.assertEqual(capture.parse_args(["review:grass", "--", "--dense-distant-grass"]).extra,
                         ["--dense-distant-grass"])
        for bad in (["film"], ["review:"], ["review:../x"]):
            with self.assertRaises(SystemExit), mock.patch("sys.stderr"):
                capture.parse_args(bad)
        for reserved in ("--fixed-fps=30", "--traverse=/x", "--capture-seconds=5", "positional"):
            with self.assertRaises(SystemExit), mock.patch("sys.stderr"):
                capture.parse_args(["traversal", "--", reserved])
        with self.assertRaises(SystemExit), mock.patch("sys.stderr"):
            capture.parse_args(["traversal", "--seconds", "0"])

    def test_game_arguments_drive_the_requested_script(self):
        out = Path("/fresh/out")
        args = capture.game_args(capture.parse_args(["traversal", "--seconds", "60"]), out)
        self.assertIn(f"--traverse={out / 'traversal'}", args)
        self.assertIn("--capture-seconds=60", args)
        self.assertIn("--skip-intro", args)
        args = capture.game_args(capture.parse_args(["review:grass", "--quality", "low", "--", "--x"]), out)
        self.assertEqual(args, ["--skip-intro", "--quality=low", "--movie-size=1920x1080",
                                "--review-route=grass", "--x"])
        with self.assertRaises(SystemExit), mock.patch("sys.stderr"):
            capture.parse_args(["traversal", "--", "--movie-size=640x360"])

    def test_disk_estimate_grows_with_length_and_is_enforced(self):
        short = capture.estimate_bytes(10, "traversal")
        self.assertGreater(capture.estimate_bytes(60, "traversal"), short)
        self.assertGreater(capture.estimate_bytes(None, "traversal"), capture.estimate_bytes(60, "traversal"))
        usage = mock.Mock(free=short - 1)
        with mock.patch.object(capture.shutil, "disk_usage", return_value=usage):
            with self.assertRaises(RuntimeError):
                capture.check_space(ROOT, short)
            usage.free = short
            self.assertEqual(capture.check_space(ROOT / "local-data" / "not-yet-made", short), short)

    def test_markers_must_be_unique_and_ordered(self):
        log = "x\nCAPTURE_START script=traversal frames_drawn=300\nCAPTURE_DONE reason=time limit frames_drawn=3900\n"
        self.assertEqual(capture.capture_markers(log), (300, 3900, "time limit"))
        for bad in ("CAPTURE_START script=t frames_drawn=3\n",
                    "CAPTURE_START script=t frames_drawn=9\nCAPTURE_DONE reason=r frames_drawn=9\n",
                    log + log):
            with self.assertRaises(RuntimeError):
                capture.capture_markers(bad)

    def test_encode_frame_count_and_duration_are_verified(self):
        capture.verify_encode(encoded(3600), 3600)
        with self.assertRaises(RuntimeError):
            capture.verify_encode(encoded(3590), 3600)
        with self.assertRaises(RuntimeError):
            capture.verify_encode(encoded(3600, audio_seconds=50.0), 3600)

    def test_flat_edge_band_fails_and_scene_content_passes(self):
        width, height = 64, 48
        def frame(flat_right: bool) -> bytes:
            rows = []
            for y in range(height):
                row = bytes((x * 3 + y * 5) % 200 for x in range(width))
                if flat_right:
                    row = row[:width - 8] + bytes([73] * 8)
                rows.append(row)
            return b"".join(rows)
        good = [capture.edge_ranges(frame(False), width, height, 8) for _ in range(3)]
        capture.check_edges(good)
        bad = [capture.edge_ranges(frame(True), width, height, 8) for _ in range(3)]
        self.assertEqual(bad[0]["right"], 0)
        with self.assertRaisesRegex(RuntimeError, "right"):
            capture.check_edges(bad)
        # One dark frame is not a defect; the band must be flat on every sample.
        capture.check_edges([bad[0], good[0]])
        with self.assertRaises(RuntimeError):
            capture.edge_ranges(b"\0" * 10, width, height, 8)

    def test_stale_or_missing_assembly_is_reported(self):
        import os, tempfile
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "scripts").mkdir()
            source = root / "scripts" / "A.cs"
            source.write_text("class A {}")
            assembly = root / "LastCamp.dll"
            self.assertEqual(capture.stale_sources(assembly, root), [source])
            assembly.write_bytes(b"x")
            os.utime(source, (1, 1))
            self.assertEqual(capture.stale_sources(assembly, root), [])
            os.utime(source, None)
            os.utime(assembly, (1, 1))
            self.assertEqual(capture.stale_sources(assembly, root), [source])


if __name__ == "__main__":
    unittest.main()
