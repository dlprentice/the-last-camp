#!/usr/bin/env python3
"""Measure the trimmed movie, then print fixed gain and a conservative peak limiter."""
import argparse
import json
import math
from pathlib import Path
import re
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("movie", type=Path)
parser.add_argument("offset", type=float)
parser.add_argument("report", type=Path)
args = parser.parse_args()
if not math.isfinite(args.offset) or args.offset < 0:
    parser.error("offset must be a finite, nonnegative number of seconds")

# loudnorm is used only as a meter. Its dynamic fallback raised quiet beds and
# shortened the intended nature/rest contrast when the source LRA exceeded 11.
# A fixed gain preserves that contrast. The loudest events (the water entry,
# thunder, the storm bed) sit 25-30 dB above the film's integrated level, so a
# fixed gain that respected the peak ceiling left the whole film near -25 LUFS.
# A compressor that only engages above a high post-gain threshold tames those
# few events; quiet beds stay untouched and the limiter handles the residue.
target = "loudnorm=I=-20:TP=-1.5:LRA=11"
result = subprocess.run([
    "ffmpeg", "-hide_banner", "-nostats", "-ss", str(args.offset),
    "-i", str(args.movie), "-vn", "-af", target + ":print_format=json",
    "-f", "null", "-",
], capture_output=True, text=True)
if result.returncode:
    raise SystemExit(result.stderr)
blocks = re.findall(r'\{\s*"input_i".*?\}', result.stderr, flags=re.S)
if len(blocks) != 1:
    raise SystemExit("Expected one loudness measurement in ffmpeg output")
stats = json.loads(blocks[0])
measurement = {key: float(stats[key]) for key in ("input_i", "input_lra", "input_tp", "input_thresh")}
if not all(math.isfinite(value) for value in measurement.values()):
    raise SystemExit("Movie audio is silent or has invalid loudness measurements")
target_lufs = -19.0
limiter_ceiling_db = -2.0  # Leave 0.5 dB for resampling/AAC below the -1.5 dBTP delivery target.
maximum_peak_reduction_db = 5.0
compressor_threshold_db = -12.0  # post-gain; only the storm, the splash and thunder cross it
compressor_ratio = 4.0
loudness_gain_db = target_lufs - measurement["input_i"]
# Peaks above the compressor threshold come out reduced by the ratio; the
# limiter then needs at most its budget on what is left.
compressed_peak_headroom_db = compressor_ratio * (limiter_ceiling_db + maximum_peak_reduction_db - compressor_threshold_db)
peak_budget_gain_db = compressor_threshold_db - measurement["input_tp"] + compressed_peak_headroom_db
gain_db = min(loudness_gain_db, peak_budget_gain_db)
limit = 10 ** (limiter_ceiling_db / 20)
threshold = 10 ** (compressor_threshold_db / 20)
# alimiter defaults to automatic output makeup and uncompensated lookahead;
# disable the former and compensate the latter so quiet gaps and cue timing stay.
# Four-times oversampling catches inter-sample peaks before returning to 48 kHz.
audio_filter = (
    f"volume={gain_db:.6f}dB:precision=double,"
    f"acompressor=threshold={threshold:.6f}:ratio={compressor_ratio:g}:attack=5:release=220:knee=4:makeup=1,"
    "aresample=192000,"
    f"alimiter=limit={limit:.9f}:attack=5:release=80:level=false:latency=true:asc=false,"
    "aresample=48000"
)
with args.report.open("x") as report:
    json.dump({
        "mode": "fixed_gain_high_threshold_compressor_peak_limiter", "target_lufs": target_lufs,
        "true_peak_db": -1.5, "limiter_ceiling_db": limiter_ceiling_db,
        "maximum_peak_reduction_db": maximum_peak_reduction_db,
        "compressor_threshold_db": compressor_threshold_db, "compressor_ratio": compressor_ratio,
        "gain_db": gain_db, "estimated_lufs_before_limiter": measurement["input_i"] + gain_db,
        "measurement": measurement, "filter": audio_filter,
    }, report, indent=2)
    report.write("\n")
print(audio_filter)
