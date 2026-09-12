"""Capture-tool tests. Synthetic media tests are NOT evidence of game rendering."""
from __future__ import annotations

import argparse
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("showcase_render", ROOT / "tools/showcase_render.py")
render = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(render)


def delivery_info() -> dict:
    return {"streams": [
        {"codec_type": "video", "codec_name": "h264", "width": 320, "height": 180,
         "avg_frame_rate": "30/1", "duration": "3.0", "nb_frames": "90",
         "color_range": "tv", "color_space": "bt709", "color_transfer": "bt709",
         "color_primaries": "bt709", "pix_fmt": "yuv420p"},
        {"codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "duration": "3.0"}]}


class CaptureContracts(unittest.TestCase):
    def setUp(self):
        base = ROOT / "local-data" / "render-work" / "tool-tests"
        base.mkdir(parents=True, exist_ok=True)
        (ROOT / "local-data" / ".gdignore").touch()
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)  # Only this test's own isolated temporary directory.
        self.path = Path(self.temp.name)

    def test_output_is_optional_and_default_quality_is_high(self):
        options = render.parse_args(["video"])
        self.assertIsNone(options.out)
        self.assertEqual(options.quality, "high")
        self.assertEqual(options.driver, "vulkan")

    def test_shot_names_trim_and_deduplicate(self):
        self.assertEqual(render.shot_names("pond, arrival,pond"), "pond,arrival")

    def test_shot_names_reject_paths_empty_values_and_shell_characters(self):
        for bad in ("../pond", "pond/arrival", "", "pond,", "pond;echo", "C:\\movie"):
            with self.subTest(bad=bad), self.assertRaises(argparse.ArgumentTypeError):
                render.shot_names(bad)

    def test_indices_and_timeouts_reject_invalid_values(self):
        self.assertEqual(render.shot_indices("0, 2,6,7,8"), "0,2,6,7,8")
        for bad in ("-1", "0,", "1.5", "", "\u0661"):
            with self.subTest(bad=bad), self.assertRaises(argparse.ArgumentTypeError):
                render.shot_indices(bad)
        for bad in ("0", "-5"):
            with self.assertRaises(argparse.ArgumentTypeError):
                render.positive_seconds(bad)

    def test_screenshots_reject_video_selection(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            render.parse_args(["screenshots", "--cinematic-shots", "0"])

    def test_platform_specific_driver_is_checked_before_render(self):
        with mock.patch.object(sys, "platform", "linux"), contextlib.redirect_stderr(io.StringIO()):
            for driver in ("d3d12", "metal"):
                with self.assertRaises(SystemExit):
                    render.parse_args(["video", "--driver", driver])

    def test_repeated_default_output_paths_are_unique_and_ignored(self):
        with mock.patch.object(render, "ROOT", self.path):
            first = render.create_output("video", None)
            second = render.create_output("video", None)
            self.assertNotEqual(first, second)
            self.assertEqual(first.parent, self.path / "local-data/renders")
            self.assertTrue((first / ".gdignore").exists())
            self.assertTrue((self.path / "local-data/.gdignore").exists())

    def test_explicit_output_never_overwrites(self):
        target = self.path / "existing"
        target.mkdir()
        (target / "keep.txt").write_text("do not overwrite")
        with self.assertRaises(FileExistsError):
            render.create_output("video", target)
        self.assertEqual((target / "keep.txt").read_text(), "do not overwrite")

    def test_marker_trim_preserves_native_frame_time(self):
        value = render.movie_markers("CINEMATIC_START frames_drawn=15\nCINEMATIC_DONE frames_drawn=105\n", 30)
        self.assertEqual(value, (15, 105, 0.5, 3.0))

    def test_marker_parser_rejects_incomplete_ambiguous_or_reversed_markers(self):
        for text in ("", "CINEMATIC_START frames_drawn=1", "CINEMATIC_START frames_drawn=5\nCINEMATIC_DONE frames_drawn=4",
                     "CINEMATIC_START frames_drawn=1\nCINEMATIC_START frames_drawn=2\nCINEMATIC_DONE frames_drawn=4"):
            with self.subTest(text=text), self.assertRaises(RuntimeError):
                render.movie_markers(text, 30)

    def test_missing_executable_fails_preflight(self):
        with mock.patch.object(shutil, "which", return_value=None), self.assertRaisesRegex(RuntimeError, "not found"):
            render.executable("missing-godot")

    def test_wrong_engine_version_fails_before_output_creation(self):
        options = render.parse_args(["screenshots", "--out", str(self.path / "output")])
        with mock.patch.object(render, "executable", side_effect=lambda name: name), \
             mock.patch.object(subprocess, "check_output", return_value="4.6.stable"), \
             self.assertRaisesRegex(RuntimeError, "4.7.2 required"):
            render.preflight(options)
        self.assertFalse((self.path / "output").exists())

    def test_video_codecs_are_checked_before_launching_godot(self):
        options = render.parse_args(["video"])
        with mock.patch.object(render, "executable", side_effect=lambda name: name), \
             mock.patch.object(subprocess, "check_output", return_value=" V..... other_codec description") as run, \
             self.assertRaisesRegex(RuntimeError, "libx264"):
            render.preflight(options)
        self.assertEqual(run.call_count, 1)
        self.assertIn("-encoders", run.call_args.args[0])

    def test_real_subprocess_output_is_retained(self):
        log = self.path / "pass.log"
        text = render.command([sys.executable, "-c", "print('contract passed')"], log, 10)
        self.assertIn("contract passed", text)
        self.assertTrue(log.exists())

    def test_nonzero_exit_is_failure(self):
        with self.assertRaisesRegex(RuntimeError, "exit 3"):
            render.command([sys.executable, "-c", "raise SystemExit(3)"], self.path / "exit.log", 10)

    def test_zero_exit_with_engine_errors_is_still_failure(self):
        for index, message in enumerate(("ERROR: broken", "SCRIPT ERROR: broken", "WARNING: 14 ObjectDB instances were leaked at exit")):
            script = f"print('\\x1b[31m' + {message!r} + '\\x1b[0m')"
            with self.subTest(message=message), self.assertRaises(RuntimeError):
                render.command([sys.executable, "-c", script], self.path / f"error{index}.log", 10)

    def test_command_timeout_reaps_owned_process_and_keeps_log(self):
        log = self.path / "timeout.log"
        with self.assertRaisesRegex(RuntimeError, "timed out"):
            render.command([sys.executable, "-u", "-c", "import time; print('started'); time.sleep(60)"], log, 1)
        self.assertIn("started", log.read_text())

    def test_completed_process_is_not_signaled(self):
        proc = mock.Mock()
        proc.poll.return_value = 0
        render.stop_owned_process(proc)
        proc.send_signal.assert_not_called()
        proc.kill.assert_not_called()

    def test_unfocused_pointer_warning_is_retained_without_hiding_real_errors(self):
        warning = "ERROR: NO GRAB"
        text = render.command([sys.executable, "-c", f"print({warning!r})"], self.path / "grab.log", 10)
        self.assertIn(warning, text)
        for index, error in enumerate(("ERROR: NO GRAB plus another failure", "SHADER ERROR: NO GRAB", "ERROR: broken scene")):
            with self.subTest(error=error), self.assertRaises(RuntimeError):
                render.command([sys.executable, "-c", f"print({warning!r});print({error!r})"],
                               self.path / f"grab-error-{index}.log", 10)

    def test_linux_display_driver_reads_environment_and_allows_explicit_override(self):
        with mock.patch.object(sys, "platform", "linux"), mock.patch.dict(os.environ, {"DISPLAY_DRIVER": "x11"}):
            self.assertEqual(render.parse_args(["screenshots"]).display_driver, "x11")
            self.assertEqual(render.parse_args(["screenshots", "--display-driver", "wayland"]).display_driver, "wayland")
        with mock.patch.object(sys, "platform", "win32"), contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            render.parse_args(["screenshots", "--display-driver", "x11"])

    def test_windows_cleanup_targets_owned_pid_not_unrelated_godot_processes(self):
        proc = mock.Mock(pid=81234)
        proc.poll.return_value = None
        proc.send_signal.side_effect = OSError("no attached console")
        with mock.patch.object(os, "name", "nt"), \
             mock.patch.object(render.signal, "CTRL_BREAK_EVENT", 1, create=True), \
             mock.patch.object(subprocess, "run") as stop:
            render.stop_owned_process(proc)
        self.assertEqual(stop.call_args.args[0], ["taskkill", "/PID", "81234", "/T", "/F"])
        proc.kill.assert_called_once()
        proc.wait.assert_called_once()

    def test_valid_delivery_metadata_is_accepted(self):
        render.validate_movie(delivery_info(), 3.0, 30, (320, 180))

    def test_delivery_rejects_missing_audio(self):
        info = delivery_info()
        info["streams"].pop()
        with self.assertRaisesRegex(RuntimeError, "audio stream"):
            render.validate_movie(info, 3.0, 30, (320, 180))

    def test_delivery_rejects_wrong_codec_color_resolution_rate_and_frame_count(self):
        for key, value in (("codec_name", "mjpeg"), ("width", 640), ("avg_frame_rate", "24/1"),
                           ("avg_frame_rate", "0/0"), ("nb_frames", "12"), ("color_range", "pc"), ("color_space", "unknown")):
            info = delivery_info()
            info["streams"][0][key] = value
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                render.validate_movie(info, 3.0, 30, (320, 180))

    def test_delivery_rejects_nonfinite_or_out_of_sync_audio(self):
        for value in ("nan", "inf", "0.7"):
            info = delivery_info()
            info["streams"][1]["duration"] = value
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                render.validate_movie(info, 3.0, 30, (320, 180))

    def test_failure_manifest_and_credits_are_retained(self):
        out = self.path / "failed"
        checked = {"tools": {}, "engine": "4.7.2.stable", "source_sha": "test-sha", "source_dirty": False}
        with mock.patch.object(render, "preflight", return_value=checked), \
             mock.patch.object(render, "capture", side_effect=RuntimeError("intentional fixture failure")), \
             self.assertRaisesRegex(RuntimeError, "intentional"):
            render.main(["video", "--out", str(out)])
        self.assertEqual(json.loads((out / "manifest.json").read_text())["status"], "failed")
        self.assertTrue((out / "credits.md").exists())

    def test_check_mode_never_creates_output_or_renders(self):
        checked = {"tools": {}, "engine": "4.7.2.stable", "source_sha": "test-sha", "source_dirty": False}
        with mock.patch.object(render, "preflight", return_value=checked), mock.patch.object(render, "capture") as capture:
            self.assertEqual(render.main(["video", "--check", "--out", str(self.path / "check")]), 0)
        self.assertFalse((self.path / "check").exists())
        capture.assert_not_called()

    @unittest.skipUnless(shutil.which("ffmpeg") and shutil.which("ffprobe"), "ffmpeg/ffprobe not installed")
    def test_synthetic_native_movie_encodes_with_correct_trim_audio_and_review_frames(self):
        # This deliberately mocks ONLY the engine capture. All audio measurement,
        # encoding, ffprobe inspection and frame extraction are real subprocesses.
        avi = self.path / "fixture.avi"
        subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30",
                        "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "4",
                        "-c:v", "mjpeg", "-q:v", "5", "-threads", "1", "-c:a", "pcm_s16le", str(avi)],
                       check=True, timeout=30)
        out = self.path / "encoded"
        out.mkdir()
        options = render.parse_args(["video", "--fps", "30", "--display-driver", "x11", "--cinematic-shots", "7,8"])
        checked = {"tools": {"godot": "fixture-godot", "ffmpeg": shutil.which("ffmpeg"), "ffprobe": shutil.which("ffprobe")}}
        original = render.command
        def controlled(args, log, timeout):
            if args[0] == "fixture-godot":
                self.assertEqual(args[args.index("--display-driver") + 1], "x11")
                self.assertLess(args.index("--display-driver"), args.index("--"))
                self.assertIn("--cinematic-shots=7,8", args)
                shutil.copyfile(avi, out / "showcase.avi")
                text = "Vulkan fixture - Forward+ - Using Device #0: synthetic\nCINEMATIC_START frames_drawn=15\nCINEMATIC_DONE frames_drawn=105\n"
                log.write_text(text)
                return text
            return original(args, log, timeout)
        manifest = {}
        with mock.patch.object(render, "command", side_effect=controlled):
            render.capture(options, checked, out, manifest)
        self.assertEqual(manifest["duration_seconds"], 3.0)
        self.assertEqual(manifest["width"], 320)
        self.assertEqual(len(list(out.glob("frame_*.png"))), 6)
        self.assertTrue((out / "audio-mastering.json").exists())
        render.validate_movie(json.loads((out / "ffprobe.json").read_text()), 3.0, 30, (320, 180))

        # Actual review images must agree with their timestamps. ffmpeg's default
        # fps rounding used to select half an interval later than the sheet label.
        from PIL import Image
        review = self.path / "review"
        review_command = [sys.executable, str(ROOT / "tools/film_review.py"),
                          str(out / "showcase.mp4"), "--interval", "1", "--window", "2",
                          "--out", str(review)]
        subprocess.run(review_command, check=True, capture_output=True, timeout=30)
        for second in (0, 1):
            reference = subprocess.check_output([
                "ffmpeg", "-v", "error", "-ss", str(second), "-i", str(out / "showcase.mp4"),
                "-frames:v", "1", "-vf", "scale=640:-1", "-f", "image2pipe", "-c:v", "png", "-"], timeout=30)
            with Image.open(io.BytesIO(reference)) as expected, Image.open(review / "frames" / f"f_{second + 1:04d}.png") as actual:
                self.assertEqual(actual.tobytes(), expected.tobytes())
        before = (review / "audio.json").read_bytes()
        repeated = subprocess.run(review_command, capture_output=True, text=True, timeout=30)
        self.assertNotEqual(repeated.returncode, 0)
        self.assertIn("already exists", repeated.stderr)
        self.assertEqual((review / "audio.json").read_bytes(), before)
        for flag in ("--interval", "--window"):
            invalid = subprocess.run(review_command + [flag, "0"], capture_output=True, text=True, timeout=30)
            self.assertNotEqual(invalid.returncode, 0)
            self.assertIn("greater than zero", invalid.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
