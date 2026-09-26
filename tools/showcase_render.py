#!/usr/bin/env python3
"""Capture the real showcase; retain unique outputs, errors and source provenance.

Run from a desktop, not a headless/dummy renderer. --check checks dependencies
without launching the scene. GODOT or --godot selects the pinned executable.
No cloud service, addon, pip package or automatic Git mutation is used.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from fractions import Fraction
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
ERROR = re.compile(
    r"SCRIPT ERROR:|SHADER ERROR:|Parse Error:|Shader compilation failed|^\s*ERROR:(?![ \t]+NO GRAB[ \t]*$)"
    r"|ObjectDB instances were leaked|resources still in use", re.M)


def stop_owned_process(proc: subprocess.Popen) -> None:
    """Stop only the process/group created by this invocation, including on Windows."""
    if proc.poll() is not None:
        return
    try:
        if os.name == "nt":
            proc.send_signal(signal.CTRL_BREAK_EVENT)
        else:
            os.killpg(proc.pid, signal.SIGTERM)
        proc.wait(timeout=10)
        return
    except (OSError, subprocess.TimeoutExpired):
        pass
    if os.name == "nt":
        try:
            subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           check=False, timeout=10)
        except (OSError, subprocess.TimeoutExpired):
            pass
        if proc.poll() is None:
            proc.kill()
    else:
        try:
            os.killpg(proc.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    proc.wait(timeout=10)


def command(args: list[str], log: Path, timeout: int) -> str:
    started = time.monotonic()
    print(f"Running {Path(args[0]).name}; log: {log}", flush=True)
    launch = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with log.open("x", encoding="utf-8") as stream:
        proc = subprocess.Popen(args, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, **launch)
        try:
            status = proc.wait(timeout=timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
            stop_owned_process(proc)
            if isinstance(error, KeyboardInterrupt):
                raise
            raise RuntimeError(f"Command timed out; incomplete output retained at {log}") from error
    text = ANSI.sub("", log.read_text(encoding="utf-8", errors="replace"))
    print(f"{log.name}: exit={status}, wall_seconds={time.monotonic() - started:.1f}", flush=True)
    match = ERROR.search(text)
    if status or match:
        detail = text[match.start():].splitlines()[0] if match else f"exit {status}"
        raise RuntimeError(f"{detail}; inspect {log}")
    return text


def positive_seconds(value: str) -> int:
    result = int(value)
    if result < 1:
        raise argparse.ArgumentTypeError("timeout must be a positive number of seconds")
    return result


def shot_names(value: str) -> str:
    names = [name.strip() for name in value.split(",")]
    if not names or any(not re.fullmatch(r"[A-Za-z0-9_]+", name) for name in names):
        raise argparse.ArgumentTypeError("shots must be comma-separated names, without paths or empty entries")
    return ",".join(dict.fromkeys(names))


def shot_indices(value: str) -> str:
    values = [v.strip() for v in value.split(",")]
    # Cinematic.play validates the upper bound against the actual shot list.
    # Do not reject later shots when that authored sequence grows.
    if any(not v.isascii() or not v.isdigit() for v in values):
        raise argparse.ArgumentTypeError("showcase shot indices must be nonnegative integers")
    return ",".join(str(int(v)) for v in values)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["screenshots", "video"])
    parser.add_argument("--out", type=Path, help="new directory; otherwise create a unique directory under local-data")
    parser.add_argument("--godot", default=os.environ.get("GODOT", "godot"), help="Godot 4.8 dev6 .NET executable path")
    parser.add_argument("--driver", choices=["vulkan", "d3d12", "metal"], default="vulkan")
    parser.add_argument("--display-driver", choices=["x11", "wayland"],
                        default=os.environ.get("DISPLAY_DRIVER") or None,
                        help="Linux window driver; x11 keeps captures progressing behind a locked Wayland session")
    parser.add_argument("--check", action="store_true", help="check dependencies and arguments only; do not render")
    parser.add_argument("--quality", choices=["low", "medium", "high", "ultra"], default="high")
    parser.add_argument("--fps", type=int, choices=[12, 24, 30, 60], default=30)
    parser.add_argument("--shots", type=shot_names, default="pond,arrival,canopy,kitchen,night_tent")
    parser.add_argument("--cinematic-shots", type=shot_indices, default=None)
    parser.add_argument("--timeout", type=positive_seconds, default=2400, help="maximum seconds for the engine process")
    options = parser.parse_args(argv)
    if options.cinematic_shots is None:
        options.cinematic_shots = ""
    if options.mode == "screenshots" and options.cinematic_shots:
        parser.error("--cinematic-shots applies to video, not screenshots")
    if options.driver == "d3d12" and sys.platform != "win32":
        parser.error("d3d12 requires native Windows")
    if options.driver == "metal" and sys.platform != "darwin":
        parser.error("metal requires macOS")
    if options.display_driver and (sys.platform != "linux" or options.display_driver not in ("x11", "wayland")):
        parser.error("display-driver must be x11 or wayland on native Linux")
    return options


def executable(name: str) -> str:
    resolved = shutil.which(str(Path(name).expanduser()))
    if resolved is None:
        raise RuntimeError(f"Required executable not found: {name}")
    return resolved


def preflight(options: argparse.Namespace) -> dict:
    tools = {"godot": executable(options.godot), "git": executable("git"), "dotnet": executable("dotnet")}
    if sys.platform == "linux" and (offscreen := shutil.which("godot-offscreen")):
        tools["offscreen"] = offscreen
    if options.mode == "video":
        tools.update({name: executable(name) for name in ("ffmpeg", "ffprobe")})
        # Check codecs before an expensive scene render, not after it.
        codecs = subprocess.check_output([tools["ffmpeg"], "-hide_banner", "-encoders"],
                                         text=True, stderr=subprocess.STDOUT, timeout=20)
        for codec in ("libx264", "aac"):
            if not re.search(r"\s" + codec + r"\s", codecs):
                raise RuntimeError(f"ffmpeg is missing the required {codec} encoder")
    version = subprocess.check_output([tools["godot"], "--version"], text=True, timeout=20).strip()
    if not version.startswith("4.8.dev6.mono."):
        raise RuntimeError(f"Pinned Godot 4.8 dev6 required (.NET build); received {version}")
    for path in ("project.godot", "textures/SOURCES.md", "audio/SOURCES.md", "models/SOURCES.md", "THIRD_PARTY_NOTICES.md", "tools/movie_audio_filter.py"):
        if not (ROOT / path).is_file():
            raise RuntimeError(f"Incomplete project checkout: missing {path}")
    if options.out is not None and options.out.expanduser().resolve().exists():
        raise RuntimeError(f"Output directory already exists; choose a fresh path or omit --out: {options.out}")
    sha = subprocess.check_output([tools["git"], "rev-parse", "HEAD"], cwd=ROOT, text=True, timeout=20).strip()
    status = subprocess.check_output([tools["git"], "status", "--porcelain"], cwd=ROOT, text=True, timeout=20)
    return {"tools": tools, "engine": version, "source_sha": sha, "source_dirty": bool(status.strip())}


def create_output(mode: str, requested: Path | None) -> Path:
    if requested is not None:
        out = requested.expanduser().resolve()
        out.mkdir(parents=True, exist_ok=False)
    else:
        base = ROOT / "local-data" / ("renders" if mode == "video" else "render-work")
        base.mkdir(parents=True, exist_ok=True)
        (ROOT / "local-data" / ".gdignore").touch(exist_ok=True)
        out = Path(tempfile.mkdtemp(prefix=f"showcase-{mode}-", dir=base))
    (out / ".gdignore").touch()
    print(f"All output retained: {out}", flush=True)
    return out


def movie_markers(text: str, fps: int) -> tuple[int, int, float, float]:
    starts = re.findall(r"^CINEMATIC_START.*frames_drawn=(\d+)", text, re.M)
    ends = re.findall(r"^CINEMATIC_DONE.*frames_drawn=(\d+)", text, re.M)
    if len(starts) != 1 or len(ends) != 1 or int(ends[0]) <= int(starts[0]) or fps <= 0:
        raise RuntimeError("Missing, ambiguous or invalid movie frame markers")
    start, end = int(starts[0]), int(ends[0])
    return start, end, start / fps, (end - start) / fps


def media_info(path: Path, ffprobe: str) -> dict:
    return json.loads(subprocess.check_output([ffprobe, "-v", "error", "-show_streams", "-show_format",
                                              "-of", "json", str(path)], text=True, timeout=30))


def validate_movie(info: dict, duration: float, fps: int, dimensions: tuple[int, int]) -> None:
    videos = [s for s in info.get("streams", []) if s.get("codec_type") == "video"]
    audios = [s for s in info.get("streams", []) if s.get("codec_type") == "audio"]
    if len(videos) != 1 or len(audios) != 1:
        raise RuntimeError("Expected exactly one video stream and one native audio stream")
    video, audio = videos[0], audios[0]
    if video.get("codec_name") != "h264" or audio.get("codec_name") != "aac":
        raise RuntimeError("Delivery codecs must be H.264 and AAC")
    if (int(video["width"]), int(video["height"])) != dimensions:
        raise RuntimeError("Encoded resolution differs from the native capture")
    try:
        recorded_fps = float(Fraction(str(video.get("avg_frame_rate", "0/0"))))
    except (ValueError, ZeroDivisionError) as error:
        raise RuntimeError("Encoded movie has an invalid frame rate") from error
    if abs(recorded_fps - fps) > 0.001:
        raise RuntimeError("Encoded frame rate differs from the recorded frame rate")
    tolerance = max(0.15, 2.0 / fps)
    for stream in (video, audio):
        length = float(stream.get("duration", "nan"))
        if not math.isfinite(length) or abs(length - duration) > tolerance:
            raise RuntimeError("Truncated or out-of-sync video/audio duration")
    if abs(int(video.get("nb_frames", "0")) - round(duration * fps)) > 2:
        raise RuntimeError("Encoded movie frame count is incomplete")
    if any(video.get(key) != value for key, value in {
        "color_range": "tv", "color_space": "bt709", "color_transfer": "bt709",
        "color_primaries": "bt709", "pix_fmt": "yuv420p"}.items()):
        raise RuntimeError(f"Encoded movie has unexpected color/range metadata: {video}")
    if int(audio.get("sample_rate", 0)) != 48000:
        raise RuntimeError("Encoded audio must be 48 kHz")


def capture(options: argparse.Namespace, checked: dict, out: Path, manifest: dict) -> None:
    tools = checked["tools"]
    command([tools["dotnet"], "build", "--nologo"], out / "build.log", options.timeout)
    base = [tools["godot"], "--path", str(ROOT), "--fullscreen", "--rendering-method", "forward_plus",
            "--rendering-driver", options.driver, "--disable-vsync"]
    if options.display_driver:
        base += ["--display-driver", options.display_driver]
    if options.mode == "screenshots":
        args = base + ["--audio-driver", "Dummy", "--", f"--quality={options.quality}",
                       f"--capture={out}", f"--shots={options.shots}"]
    else:
        avi = out / "showcase.avi"
        args = base + ["--write-movie", str(avi), "--fixed-fps", str(options.fps), "--",
                       "--cinematic=showcase", f"--quality={options.quality}",
                       f"--capture-quality={options.quality}", "--skip-cards"]
        if options.cinematic_shots:
            args.append(f"--cinematic-shots={options.cinematic_shots}")
    if "offscreen" in tools:
        # The optional machine wrapper owns display isolation and the GPU lock.
        marker = "^CAPTURE_DONE" if options.mode == "screenshots" else "^CINEMATIC_DONE"
        args = ["env", f"GODOT={tools['godot']}", tools["offscreen"], "--path", str(ROOT),
                "--driver", options.display_driver or "x11", "--timeout", str(options.timeout),
                "--qa", str(out / "offscreen"), "--done-marker", marker, "--", *args[1:]]
    manifest["engine_command"] = args
    text = command(args, out / "render.log", options.timeout)
    device = re.search(r"^(?:Vulkan|D3D12|Direct3D|Metal).*Using Device.*$", text, re.M)
    manifest["device"] = device.group(0) if device else "see render.log"
    if "Forward+" not in text:
        raise RuntimeError("Forward+ was not reported; refusing fallback-renderer evidence")
    if options.mode == "screenshots":
        if "CAPTURE_DONE" not in text:
            raise RuntimeError("Missing screenshot completion marker")
        dimensions = {}
        for name in options.shots.split(","):
            path = out / (name + ".png")
            with path.open("rb") as image:
                header = image.read(24)
            if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
                raise RuntimeError(f"Invalid requested PNG header: {name}")
            width, height = struct.unpack(">II", header[16:24])
            if width < 16 or height < 16 or path.stat().st_size < 1000:
                raise RuntimeError(f"Missing/empty requested screenshot: {name}")
            dimensions[name] = [width, height]
        manifest["screenshot_dimensions"] = dimensions
        return
    start, end, offset, duration = movie_markers(text, options.fps)
    raw = media_info(avi, tools["ffprobe"])
    (out / "native-ffprobe.json").write_text(json.dumps(raw, indent=2), encoding="utf-8")
    streams = raw.get("streams", [])
    native_video = next((s for s in streams if s.get("codec_type") == "video"), None)
    if native_video is None or not any(s.get("codec_type") == "audio" for s in streams):
        raise RuntimeError("Native movie must contain real video and engine audio")
    dimensions = (int(native_video["width"]), int(native_video["height"]))
    if any(v < 16 or v % 2 for v in dimensions):
        raise RuntimeError("Native capture dimensions must be positive and even for H.264")
    if int(native_video.get("nb_frames", 0)) < end:
        raise RuntimeError("Native AVI ended before the cinematic's completion marker")
    audio_filter = command([sys.executable, "tools/movie_audio_filter.py", str(avi), str(offset),
                            str(out / "audio-mastering.json")], out / "audio.log", 300).strip()
    movie = out / "showcase.mp4"
    vf = ("scale=in_range=pc:out_range=tv:in_color_matrix=bt601:out_color_matrix=bt709,format=yuv420p,"
          "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709")
    command([tools["ffmpeg"], "-n", "-hide_banner", "-loglevel", "warning", "-ss", str(offset),
             "-i", str(avi), "-t", str(duration), "-map", "0:v:0", "-map", "0:a:0", "-vf", vf,
             "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-profile:v", "high",
             "-pix_fmt", "yuv420p", "-r", str(options.fps), "-color_range", "tv", "-colorspace", "bt709",
             "-color_primaries", "bt709", "-color_trc", "bt709", "-af", audio_filter + ",apad",
             "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", str(movie)],
            out / "encode.log", max(300, options.timeout))
    info = media_info(movie, tools["ffprobe"])
    (out / "ffprobe.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
    validate_movie(info, duration, options.fps, dimensions)
    manifest.update({"fps": options.fps, "width": dimensions[0], "height": dimensions[1],
                     "duration_seconds": duration, "loading_frames_trimmed": start,
                     "cinematic_shots": options.cinematic_shots or "all", "offline_movie_maker": True})
    for number, fraction in enumerate([0.08, 0.25, 0.42, 0.60, 0.78, 0.92]):
        image = out / f"frame_{number}.png"
        command([tools["ffmpeg"], "-n", "-v", "error", "-ss", str(duration * fraction), "-i", str(movie),
                 "-frames:v", "1", str(image)], out / f"frame_{number}.log", 30)
        if not image.is_file() or image.stat().st_size == 0:
            raise RuntimeError(f"Review frame is missing: {image}")
    print(f"Movie: {movie} ({dimensions[0]}x{dimensions[1]}, {options.fps} fps, {duration:.2f}s)", flush=True)


def main(argv: list[str] | None = None) -> int:
    options = parse_args(argv)
    checked = preflight(options)
    print(f"Engine: {checked['engine']}\nSource: {checked['source_sha']}\nWorking tree dirty: {checked['source_dirty']}")
    if options.check:
        print("PREFLIGHT_PASS — dependencies checked; GPU and scene rendering not tested.")
        return 0
    out = create_output(options.mode, options.out)
    manifest = {key: value for key, value in checked.items() if key != "tools"}
    manifest.update({"preset": options.quality, "renderer": "forward_plus", "driver": options.driver,
                     "mode": options.mode, "status": "running", "real_time_performance_claim": False,
                     "started_utc": datetime.now(timezone.utc).isoformat()})
    manifest_path = out / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (out / "credits.md").write_text("# The Last Camp — captured engine evidence\n\n"
        "Environmental sound and Foley; no music. Offline footage is not a real-time frame-rate measurement.\n\n"
        + (ROOT / "textures/SOURCES.md").read_text(encoding="utf-8") + "\n"
        + (ROOT / "audio/SOURCES.md").read_text(encoding="utf-8") + "\n"
        + (ROOT / "models/SOURCES.md").read_text(encoding="utf-8") + "\n"
        + (ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8"), encoding="utf-8")
    try:
        capture(options, checked, out, manifest)
        manifest["status"] = "complete"
    except (OSError, RuntimeError, ValueError, KeyError, subprocess.SubprocessError, KeyboardInterrupt) as error:
        manifest.update({"status": "interrupted" if isinstance(error, KeyboardInterrupt) else "failed",
                         "error": str(error) or "Interrupted by user"})
        raise
    finally:
        manifest["finished_utc"] = datetime.now(timezone.utc).isoformat()
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(f"Retained output ({manifest['status']}): {out}", flush=True)
    print(f"SHOWCASE_EVIDENCE_DONE {out}", flush=True)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Capture interrupted; partial files and logs retained.", file=sys.stderr)
        raise SystemExit(130)
    except (OSError, RuntimeError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(f"Evidence failed: {error}", file=sys.stderr)
        raise SystemExit(1)
