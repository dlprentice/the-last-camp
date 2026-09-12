#!/usr/bin/env bash
# Export a standalone Linux build into a fresh directory, including all notices.
#   tools/export_linux.sh [FRESH_OUT_DIR]
# First import once on a new clone: godot --headless --path . --import
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p local-data/builds
touch local-data/.gdignore
if [[ $# -gt 0 ]]; then
  OUT_DIR=$(realpath -m -- "$1")
  mkdir -p "$(dirname "$OUT_DIR")"
  mkdir "$OUT_DIR" # Deliberately refuse to overwrite a prior build.
else
  OUT_DIR=$(mktemp -d "$PWD/local-data/builds/linux.XXXXXX")
fi
trap 'echo "Build files retained: $OUT_DIR"' EXIT
BIN="$OUT_DIR/the-last-camp.x86_64"
echo "[$(date +%T)] export Linux -> $BIN"
godot --headless --path . --export-release "Linux" "$BIN" > "$OUT_DIR/export.log" 2>&1 || {
  echo "Export failed; inspect $OUT_DIR/export.log" >&2; exit 1; }
if rg -q 'ERROR:|SCRIPT ERROR' "$OUT_DIR/export.log"; then
  echo "Godot reported export errors; inspect $OUT_DIR/export.log" >&2; exit 1
fi
# Run outside the source tree to check the executable's own embedded resources.
(cd "$OUT_DIR" && timeout 60 "$BIN" --headless --verbose -- --package-check \
  "--notices=$OUT_DIR/GODOT-NOTICES.txt") > "$OUT_DIR/package-check.log" 2>&1
if rg -q 'ERROR:|SCRIPT ERROR' "$OUT_DIR/package-check.log" || ! rg -q '^EXPORT_CHECK result=PASS' "$OUT_DIR/package-check.log"; then
  echo "Exported package check failed; inspect $OUT_DIR/package-check.log" >&2; exit 1
fi
cp LICENSE THIRD_PARTY_NOTICES.md "$OUT_DIR/"
cat > "$OUT_DIR/README.md" <<'EOF'
# The Last Camp — Linux x86-64

Run `./the-last-camp.x86_64 --fullscreen` from this directory.
The Godot runtime and project assets are embedded; installing the editor is
not required. A Vulkan-capable GPU and graphical desktop session are required.
Use `-- --quality=medium` after the engine arguments for a fixed lighter preset.

WASD walks, Shift runs, C crouches, E interacts, L toggles the hand lantern,
P opens photo mode, F12 saves a screenshot, and Esc pauses.

Source, full controls and rendering instructions:
https://github.com/dlprentice/the-last-camp

Keep LICENSE, THIRD_PARTY_NOTICES.md, GODOT-NOTICES.txt and credits.md with this
executable when redistributing the package. The project uses MIT; external
assets retain their listed CC0 / CC BY licenses and engine component terms.
EOF
python3 - "$OUT_DIR/credits.md" <<'PY'
from pathlib import Path
import sys
intro = "# The Last Camp — credits\n\nBuilt with [Godot Engine](https://godotengine.org/license/). See GODOT-NOTICES.txt for this binary's engine and third-party notices.\n\n"
parts = [Path(p).read_text() for p in ("textures/SOURCES.md", "audio/SOURCES.md", "models/SOURCES.md", "THIRD_PARTY_NOTICES.md")]
Path(sys.argv[1]).write_text(intro + "\n".join(parts))
PY
ls -la "$BIN"
echo "[$(date +%T)] done"
