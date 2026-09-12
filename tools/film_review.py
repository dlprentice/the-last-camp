#!/usr/bin/env python3
"""Review sheets and audio statistics for a rendered film (ffmpeg + Pillow).

Extracts one frame every INTERVAL seconds, tiles them into labelled contact
sheets (12 per sheet), and writes audio loudness (EBU R128 integrated,
range, true peak) plus a per-window RMS timeline so silent gaps or clipped
passages stand out without listening to the whole film.

  python3 tools/film_review.py local-data/renders/one_night-1080.XXXXXX/one_night.mp4 [--interval 5]

Output goes to a fresh `review/` folder beside the film, or --out DIR.
"""
import argparse
import json
import math
import re
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw

parser = argparse.ArgumentParser(description=__doc__)

def positive_interval(value):
    seconds = float(value)
    if not math.isfinite(seconds) or seconds <= 0:
        raise argparse.ArgumentTypeError("sampling interval must be finite and greater than zero")
    return seconds

parser.add_argument("film", type=Path)
parser.add_argument("--interval", type=positive_interval, default=5.0, help="seconds between frames")
parser.add_argument("--window", type=positive_interval, default=10.0, help="seconds per audio RMS window")
parser.add_argument("--out", type=Path, help="fresh review directory; existing evidence is never overwritten")
args = parser.parse_args()
film = args.film.resolve()
out = args.out.resolve() if args.out else film.parent / "review"
if not film.is_file():
    parser.error(f"film does not exist: {film}")
if out.exists():
    parser.error(f"review directory already exists; choose a fresh --out: {out}")
frames = out / "frames"
frames.mkdir(parents=True, exist_ok=False)

probe = json.loads(subprocess.check_output([
    "ffprobe", "-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,r_frame_rate",
    "-of", "json", str(film)]))
duration = float(probe["format"]["duration"])
video = next(s for s in probe["streams"] if s["codec_type"] == "video")
print(f"{film.name}: {duration:.1f} s, {video['width']}x{video['height']} @ {video['r_frame_rate']}")

# Default fps rounding selects the middle of each interval (about +2.5 s at
# five-second sampling). Round up so images match the labelled interval starts.
subprocess.run(["ffmpeg", "-n", "-hide_banner", "-loglevel", "error", "-i", str(film),
                "-vf", f"fps=1/{args.interval}:start_time=0:round=up,scale=640:-1", str(frames / "f_%04d.png")], check=True)
shots = sorted(frames.glob("f_*.png"))
print(f"{len(shots)} frames every {args.interval:g} s")

cols, rows = 4, 3
tile_w, tile_h = 640, 360
label_h = 22
sheet_index = 0
for start in range(0, len(shots), cols * rows):
    sheet_index += 1
    canvas = Image.new("RGB", (cols * tile_w, rows * (tile_h + label_h)), (22, 25, 26))
    draw = ImageDraw.Draw(canvas)
    for i, path in enumerate(shots[start:start + cols * rows]):
        x = (i % cols) * tile_w
        y = (i // cols) * (tile_h + label_h)
        with Image.open(path) as source:
            image = source.convert("RGB")
            image.thumbnail((tile_w, tile_h), Image.Resampling.LANCZOS)
            canvas.paste(image, (x, y + label_h))
        seconds = (start + i) * args.interval
        draw.text((x + 8, y + 4), f"{int(seconds // 60):02d}:{seconds % 60:05.2f}", fill=(235, 232, 221))
    canvas.save(out / f"sheet_{sheet_index:02d}.jpg", quality=92)
print(f"{sheet_index} contact sheets in {out}")

loud = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(film), "-map", "0:a:0",
                       "-af", "ebur128=peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, check=True).stderr
summary = loud[loud.rfind("Summary:"):] if "Summary:" in loud else loud[-800:]
integrated = re.search(r"I:\s+(-?[\d.]+) LUFS", summary)
lra = re.search(r"LRA:\s+([\d.]+) LU", summary)
peak = re.search(r"Peak:\s+(-?[\d.]+) dBFS", summary)
audio = {"integrated_lufs": float(integrated.group(1)) if integrated else None,
         "loudness_range_lu": float(lra.group(1)) if lra else None,
         "true_peak_dbfs": float(peak.group(1)) if peak else None, "windows": []}

# Per-window RMS: silence or clipping shows up as a window far from the rest.
window = args.window
t = 0.0
while t < duration:
    stats = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-ss", f"{t:.2f}", "-t", f"{window:.2f}",
                            "-i", str(film), "-map", "0:a:0", "-af", "astats=measure_overall=RMS_level+Peak_level:measure_perchannel=none",
                            "-f", "null", "-"], capture_output=True, text=True, check=True).stderr
    rms = re.search(r"RMS level dB:\s+(-?[\d.]+|-inf)", stats)
    pk = re.search(r"Peak level dB:\s+(-?[\d.]+|-inf)", stats)
    audio["windows"].append({"start": round(t, 1),
                             "rms_db": float(rms.group(1)) if rms and rms.group(1) != "-inf" else None,
                             "peak_db": float(pk.group(1)) if pk and pk.group(1) != "-inf" else None})
    t += window
(out / "audio.json").write_text(json.dumps(audio, indent=1))
print(f"audio: I={audio['integrated_lufs']} LUFS LRA={audio['loudness_range_lu']} LU peak={audio['true_peak_dbfs']} dBTP")
quiet = [w for w in audio["windows"] if w["rms_db"] is None or w["rms_db"] < -45.0]
hot = [w for w in audio["windows"] if w["peak_db"] is not None and w["peak_db"] > -0.5]
print(f"quiet windows (< -45 dB RMS): {[w['start'] for w in quiet]}")
print(f"hot windows (peak > -0.5 dB): {[w['start'] for w in hot]}")
