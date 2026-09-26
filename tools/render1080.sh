#!/usr/bin/env bash
# Render one cinematic sequence at 1080p60 for upload.
# Movie Maker writes an MJPEG AVI with audio at the window size; the loading
# frames before the sequence starts and after its end card are trimmed, and the result is
# encoded to H.264 High with faststart.
#   tools/render1080.sh one_night|afterglow|arrival|pond|nightfall|showreel [OUT_ROOT]
# The root gets a unique per-run directory; all files are retained for review.
# QUALITY=high (or medium/ultra) selects a playable preset instead of Film.
# Rebuilds C# first. Uses the optional godot-offscreen machine wrapper when installed.
set -euo pipefail
NAME=${1:?sequence name}
case "$NAME" in one_night|afterglow|arrival|pond|nightfall|showreel|storm|showcase) ;; *) echo "Unknown sequence: $NAME" >&2; exit 2 ;; esac
cd "$(dirname "$0")/.."
mkdir -p local-data
touch local-data/.gdignore
OUT_ROOT=${2:-$PWD/local-data/renders}
mkdir -p "$OUT_ROOT"
OUT=$(mktemp -d "$OUT_ROOT/${NAME}-1080.XXXXXX")
trap 'echo "Render files retained: $OUT"' EXIT
GODOT=${GODOT:-godot}
case "$("$GODOT" --version)" in 4.8.dev6.mono.*) ;; *) echo "Godot 4.8 dev6 .NET required" >&2; exit 2 ;; esac
export GODOT
dotnet build --nologo > "$OUT/build.log" 2>&1
ENGINE=("$GODOT" --path "$PWD")
if command -v godot-offscreen >/dev/null; then
  ENGINE=(godot-offscreen --path "$PWD" --driver "${DISPLAY_DRIVER:-x11}" --timeout "${RENDER_TIMEOUT:-14400}" --done-marker '^CINEMATIC_DONE' --)
elif [[ -n "${DISPLAY_DRIVER:-}" ]]; then
  ENGINE+=(--display-driver "$DISPLAY_DRIVER")
fi
echo "[$(date +%T)] render $NAME"
"${ENGINE[@]}" --rendering-driver vulkan --disable-vsync --fullscreen --write-movie "$OUT/$NAME.avi" --fixed-fps 60 \
  -- --cinematic="$NAME" ${QUALITY:+--quality="$QUALITY"} > "$OUT/$NAME.render.log" 2>&1
echo "[$(date +%T)] render exit $?"
# "ERROR: NO GRAB" is the window system refusing a pointer grab while the
# fullscreen window is unfocused; it never touches the frames.
if rg 'SCRIPT ERROR:|SHADER ERROR:|^ERROR:' "$OUT/$NAME.render.log" | rg -v '^ERROR: NO GRAB[[:space:]]*$' > /dev/null || ! rg -q '^CINEMATIC_DONE' "$OUT/$NAME.render.log"; then
  echo "Godot reported a render failure; inspect $OUT/$NAME.render.log" >&2
  exit 1
fi
bash tools/encode_render.sh "$OUT" "$NAME"
