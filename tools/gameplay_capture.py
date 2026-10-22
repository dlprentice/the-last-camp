#!/usr/bin/env python3
"""Record gameplay footage: a scripted run under Movie Maker at a fixed 60 FPS.

The run drives the real player controller (the traversal harness) or a named
camera review route, writes Movie Maker's AVI with the engine's own audio and
encodes the recorded window to H.264/AAC in a fresh directory under
local-data/captures/. The encode's frame count and duration are checked against
the game's CAPTURE_START/CAPTURE_DONE markers, then the AVI is removed.

This is offline footage for review. Fixed-FPS output says nothing about frame
rate; measure performance with the exported build's --benchmark instead.

  python3 tools/gameplay_capture.py traversal --seconds 60
  python3 tools/gameplay_capture.py review:grass -- --dense-distant-grass
  python3 tools/gameplay_capture.py traversal --check

Build the C# assembly first (`dotnet build`); the tool refuses a stale one so the
GPU slot is not spent compiling. The finished MP4 is sampled for flat edge bands,
which mean the game viewport did not fill the frame.

Arguments after `--` are passed to the game. Uses the optional godot-offscreen
machine wrapper when installed (isolated display, serialized GPU access).
"""
from __future__ import annotations

import argparse
import hashlib
from datetime import datetime, timezone
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
_SPEC = importlib.util.spec_from_file_location("showcase_render", ROOT / "tools/showcase_render.py")
render = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(render)

FPS = 60
SIZE = (1920, 1080)
# Movie Maker's MJPEG AVI measured about 0.45 MB per 1080p frame of this scene,
# with PCM audio; the H.264 encode is far smaller. Loading frames before the
# capture marker are recorded as well. The estimate keeps a wide margin.
AVI_BYTES_PER_FRAME = 600_000
LOADING_SECONDS = 20
ENCODE_BYTES_PER_SECOND = 6_000_000
RESERVE_BYTES = 2_000_000_000
LOCK_WAIT_SECONDS = 21600
# Length of an uncapped run, for the disk estimate only (the engine decides).
SCRIPT_SECONDS = {"traversal": 420, "review": 60}
# A letterboxed or undersized viewport leaves a flat band at the frame's edge.
# Real scene content varies along each edge; a band whose luma spans (1st to
# 99th percentile) no more than this on every sampled frame is a defect. On the
# October 21 review clip the unfilled right band spanned 0 and the scene's
# edges 60-210.
EDGE_BAND = 16
EDGE_FLAT_RANGE = 4
EDGE_SAMPLES = 5
ASSEMBLY = ROOT / ".godot" / "mono" / "temp" / "bin" / "Debug" / "LastCamp.dll"


def script_name(value: str) -> str:
    if value == "traversal" or re.fullmatch(r"review:[a-z][a-z0-9_]*", value):
        return value
    raise argparse.ArgumentTypeError("script must be 'traversal' or 'review:<route>'")


def capture_seconds(value: str) -> float:
    result = float(value)
    if not math.isfinite(result) or result <= 0:
        raise argparse.ArgumentTypeError("seconds must be a positive number")
    return result


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    argv = list(sys.argv[1:] if argv is None else argv)
    extra: list[str] = []
    if "--" in argv:
        split = argv.index("--")
        argv, extra = argv[:split], argv[split + 1:]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("script", type=script_name, help="traversal, or review:<route> (for example review:grass)")
    parser.add_argument("--seconds", type=capture_seconds, help="stop after this many seconds of footage")
    parser.add_argument("--quality", choices=["low", "medium", "high", "ultra"], default="high")
    parser.add_argument("--out-root", type=Path, default=ROOT / "local-data" / "captures")
    parser.add_argument("--godot", default=os.environ.get("GODOT", "godot"))
    parser.add_argument("--timeout", type=render.positive_seconds, default=3600,
                        help="maximum seconds for the engine process")
    parser.add_argument("--check", action="store_true", help="check dependencies, arguments and disk space only")
    options = parser.parse_args(argv)
    for arg in extra:
        if not arg.startswith("--") or any(arg == f or arg.startswith(f + "=") for f in (
                "--traverse", "--review-route", "--capture-seconds", "--quality", "--write-movie", "--fixed-fps",
                "--movie-size", "--resolution")):
            parser.error(f"game argument {arg!r} is set by this tool or is not a --flag")
    options.extra = extra
    return options


def estimate_bytes(seconds: float | None, script: str) -> int:
    length = seconds if seconds is not None else SCRIPT_SECONDS[script.split(":")[0]]
    frames = (length + LOADING_SECONDS) * FPS
    return int(frames * AVI_BYTES_PER_FRAME + length * ENCODE_BYTES_PER_SECOND + RESERVE_BYTES)


def check_space(path: Path, needed: int) -> int:
    probe = path
    while not probe.exists():
        probe = probe.parent
    free = shutil.disk_usage(probe).free
    if free < needed:
        raise RuntimeError(f"Not enough free disk at {probe}: {free / 1e9:.1f} GB free, "
                           f"{needed / 1e9:.1f} GB estimated")
    return free


def capture_markers(text: str) -> tuple[int, int, str]:
    starts = re.findall(r"^CAPTURE_START.*frames_drawn=(\d+)", text, re.M)
    ends = re.findall(r"^CAPTURE_DONE reason=(.*) frames_drawn=(\d+)", text, re.M)
    if len(starts) != 1 or len(ends) != 1 or int(ends[0][1]) <= int(starts[0]):
        raise RuntimeError("Missing, ambiguous or invalid CAPTURE_START/CAPTURE_DONE markers")
    return int(starts[0]), int(ends[0][1]), ends[0][0].strip()


def game_args(options: argparse.Namespace, out: Path) -> list[str]:
    args = ["--skip-intro", f"--quality={options.quality}", f"--movie-size={SIZE[0]}x{SIZE[1]}"]
    if options.script == "traversal":
        args.append(f"--traverse={out / 'traversal'}")
    else:
        args.append(f"--review-route={options.script.split(':', 1)[1]}")
    if options.seconds is not None:
        args.append(f"--capture-seconds={options.seconds:g}")
    return args + options.extra


def verify_encode(info: dict, frames: int) -> None:
    render.validate_movie(info, frames / FPS, FPS, SIZE)
    video = next(s for s in info["streams"] if s.get("codec_type") == "video")
    if abs(int(video.get("nb_frames", "0")) - frames) > 1:
        raise RuntimeError(f"Encoded {video.get('nb_frames')} frames; the game recorded {frames}")


def stale_sources(assembly: Path = ASSEMBLY, root: Path = ROOT) -> list[Path]:
    """C# sources newer than the built game assembly (all of them when it is missing)."""
    sources = [*root.glob("*.csproj"), *(path for folder in ("scripts", "tests") for path in (root / folder).rglob("*.cs"))]
    if not assembly.exists():
        return sources
    built = assembly.stat().st_mtime
    return [path for path in sources if path.stat().st_mtime > built]


def edge_ranges(gray: bytes, width: int = SIZE[0], height: int = SIZE[1], band: int = EDGE_BAND) -> dict:
    """Luma span (1st to 99th percentile) of each edge band of one 8-bit frame."""
    if len(gray) != width * height:
        raise RuntimeError(f"Expected a {width}x{height} gray frame, got {len(gray)} bytes")
    rows = [gray[y * width:(y + 1) * width] for y in range(height)]
    left = [v for row in rows for v in row[:band]]
    right = [v for row in rows for v in row[width - band:]]
    top = [v for row in rows[:band] for v in row]
    bottom = [v for row in rows[height - band:] for v in row]

    def span(values: list[int]) -> int:
        values = sorted(values)
        return values[max(0, int(len(values) * 0.99) - 1)] - values[int(len(values) * 0.01)]
    return {name: span(values) for name, values in
            (("left", left), ("right", right), ("top", top), ("bottom", bottom))}


def check_edges(samples: list[dict]) -> None:
    """Fail when an edge band is flat on every sampled frame (an unfilled viewport)."""
    for edge in ("left", "right", "top", "bottom"):
        if samples and all(sample[edge] <= EDGE_FLAT_RANGE for sample in samples):
            raise RuntimeError(f"The {edge} {EDGE_BAND}-pixel band is a flat colour on every sampled frame; "
                               "the game viewport did not fill the recorded frame")


def sample_edges(movie: Path, ffmpeg: str, seconds: float) -> list[dict]:
    samples = []
    for index in range(EDGE_SAMPLES):
        at = seconds * (index + 0.5) / EDGE_SAMPLES
        gray = subprocess.run([ffmpeg, "-v", "error", "-ss", f"{at:.3f}", "-i", str(movie), "-frames:v", "1",
                               "-f", "rawvideo", "-pix_fmt", "gray", "-"],
                              check=True, capture_output=True, timeout=120).stdout
        samples.append({"seconds": round(at, 3), **edge_ranges(gray)})
    return samples


def capture(options: argparse.Namespace, tools: dict, out: Path, manifest: dict) -> None:
    avi = out / "capture.avi"
    # --resolution sizes the window and --movie-size (game) renders the root
    # viewport at exactly 1920x1080 whatever size the window gets, so the
    # movie frame is filled edge to edge.
    args = [tools["godot"], "--path", str(ROOT), "--fullscreen", "--resolution", f"{SIZE[0]}x{SIZE[1]}",
            "--rendering-method", "forward_plus",
            "--rendering-driver", "vulkan", "--disable-vsync", "--write-movie", str(avi),
            "--fixed-fps", str(FPS), "--", *game_args(options, out)]
    if "offscreen" in tools:
        args = ["env", f"GODOT={tools['godot']}", tools["offscreen"], "--path", str(ROOT), "--driver", "x11",
                "--timeout", str(options.timeout), "--qa", str(out / "offscreen"),
                "--done-marker", "^CAPTURE_DONE", "--", *args[1:]]
    manifest["engine_command"] = args
    # The offscreen wrapper applies --timeout to Godot itself, after it may have
    # waited up to its six-hour limit for the machine's GPU lock.
    limit = options.timeout + (LOCK_WAIT_SECONDS if "offscreen" in tools else 0) + 120
    try:
        text = render.command(args, out / "capture.log", limit)
    finally:
        if avi.exists():
            manifest["avi_bytes"] = avi.stat().st_size
        placement = out / "offscreen" / "window-placement.txt"
        if placement.exists():
            manifest["offscreen_window_placement"] = placement.read_text(encoding="utf-8").strip()
    if "Forward+" not in text:
        raise RuntimeError("Forward+ was not reported; refusing fallback-renderer footage")
    start, end, reason = capture_markers(text)
    frames = end - start
    manifest.update({"loading_frames_trimmed": start, "frames": frames, "duration_seconds": frames / FPS,
                     "end_reason": reason})
    native = render.media_info(avi, tools["ffprobe"])
    streams = native.get("streams", [])
    video = next((s for s in streams if s.get("codec_type") == "video"), None)
    if video is None or not any(s.get("codec_type") == "audio" for s in streams):
        raise RuntimeError("Movie Maker output must contain video and engine audio")
    if (int(video["width"]), int(video["height"])) != SIZE:
        raise RuntimeError(f"Movie Maker recorded {video['width']}x{video['height']}, not 1920x1080")
    if int(video.get("nb_frames", 0)) < end:
        raise RuntimeError("The AVI ended before the capture's end marker")
    if options.script == "traversal" and options.seconds is None:
        report = json.loads((out / "traversal" / "report.json").read_text(encoding="utf-8"))
        manifest["traversal_passed"] = bool(report.get("passed"))
        manifest["traversal_failures"] = report.get("failures", [])
    movie = out / "capture.mp4"
    vf = ("scale=in_range=pc:out_range=tv:in_color_matrix=bt601:out_color_matrix=bt709,format=yuv420p,"
          "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709")
    # The game's own mix, unmastered: this footage is for judging the game.
    render.command([tools["ffmpeg"], "-n", "-hide_banner", "-loglevel", "warning", "-ss", f"{start / FPS:.6f}",
                    "-i", str(avi), "-t", f"{frames / FPS:.6f}", "-map", "0:v:0", "-map", "0:a:0", "-vf", vf,
                    "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-profile:v", "high",
                    "-pix_fmt", "yuv420p", "-r", str(FPS), "-color_range", "tv", "-colorspace", "bt709",
                    "-color_primaries", "bt709", "-color_trc", "bt709", "-af", "apad", "-c:a", "aac",
                    "-b:a", "256k", "-ar", "48000", "-shortest", "-movflags", "+faststart", str(movie)],
                   out / "encode.log", max(600, options.timeout))
    info = render.media_info(movie, tools["ffprobe"])
    (out / "ffprobe.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
    verify_encode(info, frames)
    manifest["edge_samples"] = sample_edges(movie, tools["ffmpeg"], frames / FPS)
    check_edges(manifest["edge_samples"])
    manifest["mp4_bytes"] = movie.stat().st_size
    avi.unlink()
    manifest["avi_removed"] = True
    print(f"Footage: {movie} ({frames} frames, {frames / FPS:.2f} s, ended by {reason})", flush=True)
    if manifest.get("traversal_passed") is False:
        raise RuntimeError(f"Traversal failed: {manifest['traversal_failures']}")


def main(argv: list[str] | None = None) -> int:
    options = parse_args(argv)
    tools = {"godot": render.executable(options.godot), "git": render.executable("git"),
             "ffmpeg": render.executable("ffmpeg"),
             "ffprobe": render.executable("ffprobe")}
    if sys.platform == "linux" and (offscreen := shutil.which("godot-offscreen")):
        tools["offscreen"] = offscreen
    version = subprocess.check_output([tools["godot"], "--version"], text=True, timeout=20).strip()
    if not version.startswith("4.8.beta1.mono."):
        raise RuntimeError(f"Pinned Godot 4.8 beta1 .NET required; found {version}")
    stale = stale_sources()
    if stale:
        raise RuntimeError(f"The game assembly is missing or older than {len(stale)} C# source(s) "
                           f"(first: {stale[0].relative_to(ROOT)}); run `dotnet build` first")
    needed = estimate_bytes(options.seconds, options.script)
    free = check_space(options.out_root, needed)
    print(f"Disk: {free / 1e9:.1f} GB free, {needed / 1e9:.1f} GB estimated", flush=True)
    if options.check:
        print("PREFLIGHT_PASS: dependencies, arguments and disk space checked; nothing rendered.")
        return 0
    options.out_root.mkdir(parents=True, exist_ok=True)
    (ROOT / "local-data" / ".gdignore").touch(exist_ok=True)
    label = options.script.replace(":", "-")
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    out = Path(tempfile.mkdtemp(prefix=f"{label}-{stamp}-", dir=options.out_root))
    (out / ".gdignore").touch()
    print(f"Output: {out}", flush=True)
    status = subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True)
    diff = subprocess.check_output(["git", "diff", "HEAD", "--binary"], cwd=ROOT)
    if diff:
        (out / "source.diff").write_bytes(diff)
    manifest = {
        "script": options.script, "seconds": options.seconds, "quality": options.quality, "fps": FPS,
        "size": list(SIZE), "extra_game_args": options.extra, "engine": version,
        "source_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "source_dirty": bool(status.strip()), "source_status": status.splitlines(),
        "source_diff_sha256": hashlib.sha256(diff).hexdigest() if diff else None,
        "estimated_bytes": needed, "free_bytes_at_start": free, "offline_movie_maker": True,
        "real_time_performance_claim": False, "started_utc": datetime.now(timezone.utc).isoformat(),
        "status": "running"}
    path = out / "manifest.json"
    try:
        capture(options, tools, out, manifest)
        manifest["status"] = "passed"
        return 0
    except BaseException as error:
        manifest["status"] = "failed"
        manifest["error"] = str(error)
        raise
    finally:
        manifest["finished_utc"] = datetime.now(timezone.utc).isoformat()
        path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as error:
        print(f"gameplay_capture: {error}", file=sys.stderr)
        sys.exit(1)
