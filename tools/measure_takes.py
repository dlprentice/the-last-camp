#!/usr/bin/env python3
"""Measure generated sound takes and pick one per sound.

For every <sfx>/<id>/take-*.wav it records integrated loudness and true peak
(ffmpeg ebur128), duration, onset delay (time to -40 dBFS), tail (time from the last
-40 dBFS sample to the end) and, for loops, the seam jump: the RMS level difference
between the last and first 50 ms, plus the sample-step at the wrap point. It picks
per sound the take with no clipping and the lowest penalty, and writes picks.json
and measurements.json. A machine pick is a shortlist, not a listening verdict.
"""
import argparse, json, re, subprocess, wave
from pathlib import Path
import numpy as np

def load(path):
    with wave.open(str(path)) as w:
        n, ch, sw, sr = w.getnframes(), w.getnchannels(), w.getsampwidth(), w.getframerate()
        data = np.frombuffer(w.readframes(n), dtype={2: np.int16, 4: np.int32}[sw]).astype(np.float64)
    data = data.reshape(-1, ch) / (2 ** (8 * sw - 1))
    return data, sr

def ebur(path):
    out = subprocess.run(["ffmpeg", "-nostats", "-i", str(path), "-af", "ebur128=peak=true", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    i = re.findall(r"I:\s+(-?[\d.]+) LUFS", out); tp = re.findall(r"Peak:\s+(-?[\d.]+) dBFS", out)
    return (float(i[-1]) if i else None), (float(tp[-1]) if tp else None)

def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument("sfx", type=Path)
    p.add_argument("--sounds", type=Path, required=True); o = p.parse_args()
    spec = {s["id"]: s for s in json.loads(o.sounds.read_text())}
    meas, picks = {}, {}
    for sid, s in spec.items():
        rows = []
        for f in sorted((o.sfx / sid).glob("take-*.wav")):
            x, sr = load(f); mono = np.abs(x).max(axis=1); lufs, tp = ebur(f)
            loud = np.nonzero(mono > 10 ** (-40 / 20))[0]
            onset = loud[0] / sr if len(loud) else None; tail = (len(mono) - loud[-1]) / sr if len(loud) else None
            r = {"file": f.name, "seconds": round(len(x) / sr, 3), "lufs": lufs, "true_peak": tp,
                 "onset_s": round(onset, 3) if onset is not None else None, "tail_s": round(tail, 3) if tail is not None else None}
            if s.get("loop"):
                k = int(0.05 * sr); rms = lambda a: 20 * np.log10(np.sqrt(np.mean(a ** 2)) + 1e-9)
                r["seam_db"] = round(abs(rms(x[-k:]) - rms(x[:k])), 2)
                r["seam_step"] = round(float(np.abs(x[0] - x[-1]).max()), 4)
            pen = 0.0
            if tp is None or tp > -0.5: pen += 100
            if lufs is None or lufs < -60: pen += 100
            if not s.get("loop") and onset is not None: pen += 10 * max(0, onset - 0.05)
            if s.get("loop"): pen += r["seam_db"] + 50 * r["seam_step"]
            r["penalty"] = round(pen, 2); rows.append(r)
        meas[sid] = rows
        if rows: picks[sid] = min(rows, key=lambda r: r["penalty"])["file"]
    (o.sfx / "measurements.json").write_text(json.dumps(meas, indent=1))
    (o.sfx / "picks.json").write_text(json.dumps(picks, indent=1))
    bad = {k: v for k, v in meas.items() if all(r["penalty"] >= 100 for r in v)}
    print(f"{len(picks)} sounds measured; no usable take: {sorted(bad)}")

if __name__ == "__main__":
    main()
