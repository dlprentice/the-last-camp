#!/usr/bin/env bash
# Render one cinematic sequence at 1080p60 for upload.
# Movie Maker writes an MJPEG AVI with audio at the window size; the loading
# frames before the sequence starts and after its end card are trimmed, and the result is
# encoded to H.264 High with faststart.
#   tools/render1080.sh one_night|afterglow|arrival|pond|nightfall|showreel [OUT_ROOT]
# The root gets a unique per-run directory; all files are retained for review.
# QUALITY=high (or medium/ultra) renders with that playable preset instead of
# the offline Film preset. Final ridge-tree masters took 77–135 minutes
# on the RTX 4060 Laptop, before encoding. Judge progress by the CINEMATIC_SHOT
# timestamps in the render log; AVI growth misleads during the black cards.
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
echo "[$(date +%T)] render $NAME"
# Movie frames have fixed simulation steps; desktop presentation must not pace
# the writer when the fullscreen window is on an inactive workspace.
# DISPLAY_DRIVER=x11 renders through XWayland. Behind a locked Hyprland
# session the native Wayland window gets no frame callbacks and the engine
# drops to one frame per second; the X11 path keeps full speed.
godot --path . ${DISPLAY_DRIVER:+--display-driver "$DISPLAY_DRIVER"} --rendering-driver vulkan --disable-vsync --fullscreen --write-movie "$OUT/$NAME.avi" --fixed-fps 60 \
  -- --cinematic="$NAME" ${QUALITY:+--quality="$QUALITY"} > "$OUT/$NAME.render.log" 2>&1
echo "[$(date +%T)] render exit $?"
# "ERROR: NO GRAB" is the window system refusing a pointer grab while the
# fullscreen window is unfocused; it never touches the frames.
if rg 'SCRIPT ERROR:|SHADER ERROR:|^ERROR:' "$OUT/$NAME.render.log" | rg -v '^ERROR: NO GRAB[[:space:]]*$' > /dev/null || ! rg -q '^CINEMATIC_DONE' "$OUT/$NAME.render.log"; then
  echo "Godot reported a render failure; inspect $OUT/$NAME.render.log" >&2
  exit 1
fi
bash tools/encode_render.sh "$OUT" "$NAME"
