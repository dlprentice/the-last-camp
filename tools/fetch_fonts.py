#!/usr/bin/env python3
"""Fetch the game's three typefaces and their licences into fonts/.

Every file is pinned by URL (a fixed commit of the Google Fonts repository) and
SHA-256. The fonts are under the SIL Open Font License 1.1, which allows them to
be bundled and redistributed with the game, so the fetched files are committed;
this script is how they were obtained and how to check them.

    python3 tools/fetch_fonts.py           # download any missing file, verify all
    python3 tools/fetch_fonts.py --check   # verify only, never download
"""

from __future__ import annotations

import argparse
import hashlib
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FONTS = ROOT / "fonts"
COMMIT = "5bd87dfc525c8a0d1d8529d3684716e00c3fc59a"
BASE = f"https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl"

# (file under fonts/, path under ofl/ at the pinned commit, SHA-256)
PINS = [
    ("NotoSerif.ttf", "notoserif/NotoSerif%5Bwdth,wght%5D.ttf",
     "4d8e6761424656867019081a1a01336f3cb086982682698714054fc33f782713"),
    ("NotoSerif-Italic.ttf", "notoserif/NotoSerif-Italic%5Bwdth,wght%5D.ttf",
     "e87acbc6c0efd0d9a20d6a8cbbda2b266c14be3a3a6f5af8ec9d7b2460570ad1"),
    ("NotoSerif-OFL.txt", "notoserif/OFL.txt",
     "cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a"),
    ("Caveat.ttf", "caveat/Caveat%5Bwght%5D.ttf",
     "0bdb6b660482d31531b3945849fba5916b3ef8695da7024a9e6b9ee3c4157988"),
    ("Caveat-OFL.txt", "caveat/OFL.txt",
     "1f9d81d094273d82f3898a1ee8b598a717d050ecbf5ff7bede105b704880157b"),
    ("ArchitectsDaughter.ttf", "architectsdaughter/ArchitectsDaughter-Regular.ttf",
     "6159718a08898e34bc1cb7354086141a5f9a70b73e54dbec27ead0d59a697359"),
    ("ArchitectsDaughter-OFL.txt", "architectsdaughter/OFL.txt",
     "0e441ae9f18ad0c294cc5b6ae3974c2db1614cab220598578b1b58c4cc334485"),
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="verify only")
    args = parser.parse_args()
    FONTS.mkdir(exist_ok=True)
    failed = 0
    for name, source, expected in PINS:
        target = FONTS / name
        url = f"{BASE}/{source}"
        if not target.exists() and not args.check:
            request = urllib.request.Request(url, headers={"User-Agent": "fetch-fonts"})
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read()
            tmp = target.with_suffix(target.suffix + ".part")
            tmp.write_bytes(data)
            tmp.replace(target)
        if not target.exists():
            print(f"MISSING {name}  {url}")
            failed += 1
            continue
        actual = sha256(target)
        if actual != expected:
            print(f"MISMATCH {name}  expected {expected or '(unpinned)'}  got {actual}")
            failed += 1
        else:
            print(f"OK {name}  {actual}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
