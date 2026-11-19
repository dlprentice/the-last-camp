#!/usr/bin/env python3
"""Level the generated takes into the game's audio files, offline.

For every file the manifest names (sfx/<id>/take-N.wav), this reads the raw take
from --source (the ElevenLabs output, default local-data/audio/sfx), brings it to its
category's loudness target and writes it to --out (default audio/generated/sfx):

* two-pass ffmpeg loudnorm: pass one measures, pass two applies the measured values
  as one linear gain (linear=true), true peak ceiling -1.5 dBTP. Loudnorm refuses
  linear mode for a take whose loudness range measures 0 (very short one-shots); the
  same linear gain is then applied with ffmpeg's volume filter.
* a take whose crest factor keeps it from the target under that ceiling (a loud
  crackle in a quiet bed, a sharp knock) first goes through a gentle look-ahead
  limiter (ffmpeg alimiter, latency compensated so loops stay seamless), stepped down
  until the linear pass fits. These takes are logged.

The result is measured again (ebur128, true peak) and written into the manifest as
each file's `lufs` and `true_peak`, with its `target` and `processing`. The game then
only trims. June's raw voice takes (--voice-source, default local-data/audio/voice,
<id>.mp3) are levelled the same way to the voice target, -16 LUFS, re-encoded as MP3
into audio/generated/voice and measured into voice.json (`lufs`, `true_peak`); a
paragraph without a take is recorded as unmeasured. The targets mirror
AudioDirector.TARGET_LUFS (a test compares them).

The audio is licensed: --source and --out stay out of Git.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CEILING_TP = -1.5
# Limiter ceilings (dBFS) tried in turn on a take raised to its target.
LIMITS_DB = (-2.0, -4.0, -6.0)

TARGETS = {
    "ambience": -19.0, "wind": -22.0, "water": -24.0, "fire": -19.0, "rain": -19.0, "wading": -22.0,
    "footsteps": -22.0, "wildlife": -27.0, "thunder": -17.0, "fire_hiss": -25.0, "water_foley": -20.0,
    "actions": -20.0, "ui": -27.0, "story": -25.0, "voice": -16.0,
}
CATEGORY = {
    "bed_day_woods": "ambience", "bed_evening": "ambience", "bed_night": "ambience",
    "bed_wind_canopy": "wind", "bed_pond_lap": "water", "bed_fire": "fire",
    "bed_rain": "rain", "bed_rain_canvas": "rain", "bed_storm_wind": "rain", "bed_wading": "wading",
    "birds_day": "wildlife", "owl": "wildlife", "loon": "wildlife", "thunder": "thunder",
    "fire_hiss": "fire_hiss", "water_in": "water_foley", "water_out": "water_foley",
    "ui_move": "ui", "ui_select": "ui", "journal_open": "ui", "journal_close": "ui", "card_in": "story",
}


def category_of(sound_id: str) -> str:
    return CATEGORY.get(sound_id, "footsteps" if sound_id.startswith("step_") else "actions")


def ffmpeg(args: list[str]) -> str:
    done = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-nostats", *args], capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"ffmpeg {' '.join(args)} failed:\n{done.stderr[-1500:]}")
    return done.stderr


def loudnorm_json(stderr: str) -> dict:
    return json.loads(stderr[stderr.rindex("{"):stderr.rindex("}") + 1])


def measure_pass(path: Path, target: float) -> dict:
    """Loudnorm's first pass."""
    out = ffmpeg(["-i", str(path), "-af", f"loudnorm=I={target}:TP={CEILING_TP}:LRA=50:print_format=json", "-f", "null", "-"])
    return {k: float(v) for k, v in loudnorm_json(out).items() if k.startswith("input_") or k == "target_offset"}


def ebur(path: Path) -> tuple[float, float]:
    out = ffmpeg(["-i", str(path), "-af", "ebur128=peak=true", "-f", "null", "-"])
    i = re.findall(r"I:\s+(-?[\d.]+|-inf) LUFS", out)
    tp = re.findall(r"Peak:\s+(-?[\d.]+|-inf) dBFS", out)
    return float(i[-1]), float(tp[-1])


def write(src: Path, dst: Path, chain: str) -> str:
    return ffmpeg(["-y", "-i", str(src), "-af", chain, "-ar", "48000", "-c:a", "pcm_s16le", "-map_metadata", "-1", str(dst)])


def linear_pass(src: Path, dst: Path, target: float, m: dict) -> bool:
    """Loudnorm's second pass as one linear gain. False when the true peak would pass the ceiling."""
    gain = target - m["input_i"]
    if m["input_tp"] + gain > CEILING_TP:
        return False
    if m["input_lra"] > 0:
        chain = (f"loudnorm=I={target}:TP={CEILING_TP}:LRA=50:linear=true:print_format=json:"
                 f"measured_I={m['input_i']}:measured_TP={m['input_tp']}:measured_LRA={m['input_lra']}:"
                 f"measured_thresh={m['input_thresh']}:offset={m['target_offset']}")
        if loudnorm_json(write(src, dst, chain)).get("normalization_type") == "linear":
            return True
    write(src, dst, f"volume={gain:.3f}dB")
    return True


def level(src: Path, dst: Path, target: float, bed: bool, times: tuple[int, int] | None = None) -> tuple[str, float]:
    """Returns (processing, limiter ceiling used or 0)."""
    m = measure_pass(src, target)
    if linear_pass(src, dst, target, m):
        return "linear", 0.0
    gain = target - m["input_i"]
    # Beds release slowly so the limiter never pumps; one-shots release fast so a
    # knock keeps its body.
    attack, release = times or ((5, 80) if bed else (1, 20))
    with tempfile.TemporaryDirectory(dir=dst.parent) as scratch:
        limited = Path(scratch) / "limited.wav"
        for limit_db in LIMITS_DB:
            # Look-ahead limiting on the take raised to its target; latency
            # compensation keeps a loop's first sample where it was.
            write(src, limited, f"volume={gain:.3f}dB,alimiter=limit={10 ** (limit_db / 20):.5f}:attack={attack}:"
                                f"release={release}:level=0:latency=1")
            lm = measure_pass(limited, target)
            if linear_pass(limited, dst, target, lm):
                return "limited", limit_db
        # Still too peaky for the target without heavy limiting: stop at the true-peak
        # ceiling and fall short of the target instead.
        write(limited, dst, f"volume={CEILING_TP - lm['input_tp'] - 0.1:.3f}dB")
        return "limited-short", LIMITS_DB[-1]


def level_voice(src: Path, dst: Path) -> str:
    """Levels a take to the voice target like any other file (speech that is too
    peaky goes through the limiter with speech timings), then encodes it as MP3.
    Encoding can lift the true peak a little, so the gain steps down until the
    encoded file fits."""
    with tempfile.TemporaryDirectory(dir=dst.parent) as scratch:
        wav = Path(scratch) / "level.wav"
        processing, _ = level(src, wav, TARGETS["voice"], False, (3, 60))
        gain = 0.0
        for _ in range(6):
            ffmpeg(["-y", "-i", str(wav), "-af", f"volume={gain:.3f}dB", "-ar", "44100", "-c:a", "libmp3lame",
                    "-b:a", "192k", "-map_metadata", "-1", str(dst)])
            _, tp = ebur(dst)
            if tp <= CEILING_TP:
                return processing
            gain -= tp - CEILING_TP + 0.1
    raise RuntimeError(f"{src}: could not encode under {CEILING_TP} dBTP")


def prepare(job: tuple[str, int, str, Path, Path]) -> dict:
    sound_id, variant, name, source, out = job
    target = TARGETS[category_of(sound_id)]
    src = source / sound_id / Path(name).name
    dst = out / sound_id / Path(name).name
    dst.parent.mkdir(parents=True, exist_ok=True)
    processing, limit_db = level(src, dst, target, sound_id.startswith("bed_"))
    lufs, tp = ebur(dst)
    return {"id": sound_id, "variant": variant, "target": target, "lufs": round(lufs, 1), "true_peak": round(tp, 1),
            "processing": processing, "limit_db": limit_db}


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--source", type=Path, default=ROOT / "local-data/audio/sfx", help="raw takes: <id>/take-N.wav")
    p.add_argument("--out", type=Path, default=ROOT / "audio/generated/sfx")
    p.add_argument("--manifest", type=Path, default=ROOT / "audio/generated/manifest.json")
    p.add_argument("--voice", type=Path, default=ROOT / "audio/generated/voice.json")
    p.add_argument("--voice-source", type=Path, default=ROOT / "local-data/audio/voice", help="raw voice takes: <id>.mp3")
    p.add_argument("--jobs", type=int, default=8)
    o = p.parse_args()
    manifest = json.loads(o.manifest.read_text(encoding="utf-8"))
    jobs = []
    for sound_id, entry in manifest["sounds"].items():
        for variant, name in enumerate(entry["files"]):
            if not (o.source / sound_id / Path(name).name).is_file():
                print(f"missing raw take: {o.source / sound_id / Path(name).name}", file=sys.stderr)
                return 1
            jobs.append((sound_id, variant, name, o.source, o.out))
    with ThreadPoolExecutor(max_workers=max(1, o.jobs)) as pool:
        results = list(pool.map(prepare, jobs))
    for sound_id, entry in manifest["sounds"].items():
        rows = sorted((r for r in results if r["id"] == sound_id), key=lambda r: r["variant"])
        entry["target"] = rows[0]["target"]
        entry["lufs"] = [r["lufs"] for r in rows]
        entry["true_peak"] = [r["true_peak"] for r in rows]
        entry["processing"] = [r["processing"] for r in rows]
    o.manifest.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    limited = [r for r in results if r["processing"] != "linear"]
    for r in limited:
        print(f"{r['processing']}: {r['id']} take {r['variant'] + 1} (limiter {r['limit_db']:.0f} dBFS) -> "
              f"{r['lufs']} LUFS (target {r['target']}), {r['true_peak']} dBTP")
    worst = max(results, key=lambda r: r["true_peak"])
    off = max(results, key=lambda r: abs(r["lufs"] - r["target"]))
    print(f"{len(results)} files levelled, {len(limited)} limited; highest true peak {worst['true_peak']} dBTP "
          f"({worst['id']}); largest miss {off['lufs'] - off['target']:+.1f} LU ({off['id']})")

    if o.voice.is_file():
        voice = json.loads(o.voice.read_text(encoding="utf-8"))
        levelled = []
        for paragraph in voice["paragraphs"]:
            name = paragraph.get("file", "")
            src = o.voice_source / Path(name).name
            dst = o.voice.parent / name
            if name and src.is_file():
                dst.parent.mkdir(parents=True, exist_ok=True)
                processing = level_voice(src, dst)
                paragraph["lufs"], paragraph["true_peak"] = (round(v, 1) for v in ebur(dst))
                levelled.append((paragraph["id"], processing, paragraph["lufs"], paragraph["true_peak"]))
            else:
                paragraph["lufs"] = paragraph["true_peak"] = None
        o.voice.write_text(json.dumps(voice, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
        for pid, processing, lufs, tp in levelled:
            if processing != "linear":
                print(f"voice {processing}: {pid} -> {lufs} LUFS, {tp} dBTP")
        if levelled:
            print(f"voice: {len(levelled)} of {len(voice['paragraphs'])} levelled; LUFS {min(r[2] for r in levelled)} to "
                  f"{max(r[2] for r in levelled)}, highest true peak {max(r[3] for r in levelled)} dBTP")
        else:
            print(f"voice: 0 of {len(voice['paragraphs'])} takes found in {o.voice_source}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
