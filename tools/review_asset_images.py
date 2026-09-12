#!/usr/bin/env python3
"""Build labelled contact sheets from --review-assets captures (Pillow only)."""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
args = parser.parse_args()
root = args.directory.resolve()
out = root / "sheets"
out.mkdir(exist_ok=True)
names = sorted({p.name.split("__")[0] for p in root.glob("*__placed_2.png")})
views = ["isolated_1", "isolated_2", "isolated_3", "isolated_4", "placed_1", "placed_2"]

def tile(path, size):
    with Image.open(path) as source:
        image = source.convert("RGB")
        image.thumbnail(size, Image.Resampling.LANCZOS)
        return image

for name in names:
    canvas = Image.new("RGB", (1920, 764), (22, 25, 26))
    draw = ImageDraw.Draw(canvas)
    for i, view in enumerate(views):
        x, y = (i % 3) * 640, (i // 3) * 382
        canvas.paste(tile(root / f"{name}__{view}.png", (640, 360)), (x, y + 22))
        draw.text((x + 10, y + 4), f"{name} / {view}", fill=(235, 232, 221))
    canvas.save(out / f"{name}.jpg", quality=94)

for start in range(0, len(names), 4):
    canvas = Image.new("RGB", (2100, 872), (22, 25, 26))
    draw = ImageDraw.Draw(canvas)
    for row, name in enumerate(names[start:start + 4]):
        for col, view in enumerate(views):
            x, y = col * 350, row * 218
            canvas.paste(tile(root / f"{name}__{view}.png", (350, 197)), (x, y + 21))
            draw.text((x + 5, y + 4), f"{name} / {view}", fill=(235, 232, 221))
    canvas.save(out / f"page_{start // 4 + 1:02}.jpg", quality=94)
print(f"{len(names)} assets, {(len(names) + 3) // 4} overview pages: {out}")
