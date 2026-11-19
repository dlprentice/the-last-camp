#!/usr/bin/env python3
"""Generate sound effects and ambience beds with the ElevenLabs sound-generation API.

The generated audio is licensed, not MIT: it is written only under the ignored
local-data/ tree and never committed. Every call is appended to a JSON-lines ledger
with its prompt, length, loop flag, estimated credits and output file.

  python3 tools/elevenlabs_sfx.py --key-file KEY --sounds sounds.json \\
      --out local-data/audio/sfx

sounds.json is a list of {"id", "prompt", "seconds", "loop": bool, "takes": int,
"influence": 0..1 (optional)}. Take n of id is written to <out>/<id>/take-<n>.<ext>;
existing takes are skipped, so a rerun only fills gaps. The key file holds the API
key on one line; it is sent only in the request header and never printed.
"""

from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

API = "https://api.elevenlabs.io/v1/sound-generation?output_format={fmt}"
MODEL = "eleven_text_to_sound_v3"
ROOT = Path(__file__).resolve().parent.parent
CREDITS_PER_SECOND = 40


def generate(key: str, body: dict, fmt: str) -> bytes:
    request = urllib.request.Request(API.format(fmt=fmt), data=json.dumps(body).encode(),
                                     headers={"xi-api-key": key, "Content-Type": "application/json"},
                                     method="POST")
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=300) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:400]
            if error.code in (429, 500, 502, 503) and attempt < 2:
                time.sleep(8 * (attempt + 1))
                continue
            raise RuntimeError(f"ElevenLabs HTTP {error.code}: {detail}") from None
    raise RuntimeError("ElevenLabs: retries exhausted")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--key-file", type=Path, required=True)
    parser.add_argument("--sounds", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--format", default="wav_48000")
    parser.add_argument("--only", help="comma-separated ids to generate")
    parser.add_argument("--ledger", type=Path, default=ROOT / "local-data/audio/ledger.jsonl")
    parser.add_argument("--budget", type=int, default=0, help="stop before estimated credits exceed this")
    options = parser.parse_args()
    out = options.out.resolve()
    if not str(out).startswith(str(ROOT / "local-data")):
        raise SystemExit("licensed audio must be written under local-data/")
    key = options.key_file.expanduser().read_text(encoding="utf-8").strip()
    sounds = json.loads(options.sounds.read_text(encoding="utf-8"))
    only = set(options.only.split(",")) if options.only else None
    extension = "wav" if options.format.startswith(("wav", "pcm")) else "mp3"
    spent = 0
    failures = 0
    options.ledger.parent.mkdir(parents=True, exist_ok=True)
    for sound in sounds:
        if only and sound["id"] not in only:
            continue
        folder = out / sound["id"]
        folder.mkdir(parents=True, exist_ok=True)
        for take in range(1, int(sound.get("takes", 3)) + 1):
            target = folder / f"take-{take}.{extension}"
            if target.exists():
                continue
            cost = int(round(float(sound["seconds"]) * CREDITS_PER_SECOND))
            if options.budget and spent + cost > options.budget:
                print(f"budget reached before {sound['id']} take {take}")
                return 0
            body = {"text": sound["prompt"], "model_id": MODEL, "duration_seconds": float(sound["seconds"]),
                    "loop": bool(sound.get("loop", False))}
            if "influence" in sound:
                body["prompt_influence"] = float(sound["influence"])
            try:
                audio = generate(key, body, options.format)
            except RuntimeError as error:
                print(f"FAILED {sound['id']} take {take}: {error}")
                failures += 1
                continue
            target.write_bytes(audio)
            spent += cost
            entry = {"utc": datetime.now(timezone.utc).isoformat(), "kind": "sfx", "id": sound["id"],
                     "take": take, "prompt": sound["prompt"], "seconds": sound["seconds"],
                     "loop": bool(sound.get("loop", False)), "credits_estimate": cost,
                     "output": str(target), "bytes": len(audio)}
            with options.ledger.open("a", encoding="utf-8") as ledger:
                ledger.write(json.dumps(entry) + "\n")
            print(f"wrote {target} ({len(audio)} bytes, ~{cost} credits)", flush=True)
    print(f"estimated credits this run: {spent}; failures: {failures}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
