#!/usr/bin/env python3
"""Render voice lines with the ElevenLabs text-to-speech API.

The rendered audio is licensed, not MIT: it is written only under the ignored
local-data/ tree (or another ignored directory) and is never committed. Every call
is appended to a JSON-lines ledger with its text, voice, model, character count and
output file, so credit use can be audited.

  python3 tools/elevenlabs_tts.py --key-file KEY --lines lines.json \\
      --voice VOICE_ID --model eleven_v3 --out local-data/audio/audition/carol

lines.json is a list of {"id": "...", "text": "..."}. The key file holds the API key
on one line; the key is sent only in the request header and never printed.
"""

from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

API = "https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format={fmt}"
ROOT = Path(__file__).resolve().parent.parent


def render(key: str, voice: str, model: str, text: str, fmt: str, settings: dict | None) -> bytes:
    body = {"text": text, "model_id": model}
    if settings:
        body["voice_settings"] = settings
    request = urllib.request.Request(API.format(voice=voice, fmt=fmt), data=json.dumps(body).encode(),
                                     headers={"xi-api-key": key, "Content-Type": "application/json",
                                              "Accept": "audio/*"}, method="POST")
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:400]
            if error.code in (429, 500, 502, 503) and attempt < 2:
                time.sleep(5 * (attempt + 1))
                continue
            raise SystemExit(f"ElevenLabs HTTP {error.code}: {detail}") from None
    raise SystemExit("ElevenLabs: retries exhausted")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--key-file", type=Path, required=True)
    parser.add_argument("--lines", type=Path, required=True)
    parser.add_argument("--voice", required=True)
    parser.add_argument("--model", default="eleven_v3")
    parser.add_argument("--format", default="mp3_44100_192")
    parser.add_argument("--settings", help="JSON voice_settings, for example '{\"stability\":0.5}'")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--ledger", type=Path, default=ROOT / "local-data/audio/ledger.jsonl")
    parser.add_argument("--label", default="", help="free text recorded in the ledger")
    options = parser.parse_args()
    out = options.out.resolve()
    if ROOT in out.parents and not str(out.relative_to(ROOT)).startswith("local-data/"):
        raise SystemExit("licensed audio must be written under local-data/")
    key = options.key_file.expanduser().read_text(encoding="utf-8").strip()
    lines = json.loads(options.lines.read_text(encoding="utf-8"))
    settings = json.loads(options.settings) if options.settings else None
    out.mkdir(parents=True, exist_ok=True)
    options.ledger.parent.mkdir(parents=True, exist_ok=True)
    extension = options.format.split("_")[0].replace("pcm", "pcm")
    for line in lines:
        target = out / f"{line['id']}.{extension}"
        if target.exists():
            print(f"skip (exists) {target}")
            continue
        audio = render(key, options.voice, options.model, line["text"], options.format, settings)
        target.write_bytes(audio)
        entry = {"utc": datetime.now(timezone.utc).isoformat(), "kind": "tts", "voice": options.voice,
                 "model": options.model, "id": line["id"], "text": line["text"],
                 "characters": len(line["text"]), "credits_estimate": len(line["text"]),
                 "settings": settings, "label": options.label, "output": str(target), "bytes": len(audio)}
        with options.ledger.open("a", encoding="utf-8") as ledger:
            ledger.write(json.dumps(entry) + "\n")
        print(f"wrote {target} ({len(audio)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
