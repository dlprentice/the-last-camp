#!/usr/bin/env bash
# Render one cinematic sequence at 4K60 for upload.
#
# Godot's Movie Maker writes frames at the window size, which a 1080p display
# cannot exceed, so the frames come from the render viewport instead
# (--movie-size + --frames-dir) and the movie writer contributes the audio.
# The two are muxed and encoded to H.264 High 5.1 with faststart. The work
# directory must be on a real disk: a full showreel is ~7 GB of frames plus a
# ~2 GB audio carrier, more than a RAM-backed /tmp holds.
#
#   tools/render4k.sh one_night|afterglow|arrival|pond|nightfall|showreel [OUT_ROOT] [WORK_ROOT]
# Each root gets a unique per-run directory; all files are retained for review.
set -euo pipefail
NAME=${1:?sequence name}
case "$NAME" in one_night|afterglow|arrival|pond|nightfall|showreel|storm|showcase) ;; *) echo "Unknown sequence: $NAME" >&2; exit 2 ;; esac
cd "$(dirname "$0")/.."
mkdir -p local-data
touch local-data/.gdignore
OUT_ROOT=${2:-$PWD/local-data/renders}
WORK_ROOT=${3:-$PWD/local-data/render-work}
mkdir -p "$OUT_ROOT" "$WORK_ROOT"
OUT=$(mktemp -d "$OUT_ROOT/${NAME}-4k.XXXXXX")
WORK=$(mktemp -d "$WORK_ROOT/${NAME}-4k.XXXXXX")
trap 'echo "Render files retained: $WORK; output: $OUT"' EXIT
echo "[$(date +%T)] render $NAME"
# Keep desktop presentation from pacing this fixed-step offline render.
godot --path . ${DISPLAY_DRIVER:+--display-driver "$DISPLAY_DRIVER"} --rendering-driver vulkan --disable-vsync --fullscreen --write-movie "$WORK/audio.avi" --fixed-fps 60 \
  -- --cinematic="$NAME" ${QUALITY:+--quality="$QUALITY"} --movie-size=3840x2160 --frames-dir="$WORK/frames" > "$WORK/render.log" 2>&1
echo "[$(date +%T)] render exit $? frames=$(ls "$WORK/frames" | wc -l)"
# Frames start at the first drawn frame after the loading screen; the movie
# writer's audio starts at frame zero, so skip the loading frames.
if rg 'SCRIPT ERROR:|SHADER ERROR:|^ERROR:' "$WORK/render.log" | rg -v '^ERROR: NO GRAB[[:space:]]*$' > /dev/null || ! rg -q '^CINEMATIC_DONE' "$WORK/render.log"; then
  echo "Godot reported a render failure; inspect $WORK/render.log" >&2
  exit 1
fi
START=$(sed -nE 's/^CINEMATIC_START.*frames_drawn=([0-9]+).*/\1/p' "$WORK/render.log")
[[ "$START" =~ ^[0-9]+$ ]] || { echo "Missing or ambiguous movie start marker" >&2; exit 1; }
END=$(sed -nE 's/^CINEMATIC_DONE.*frames_drawn=([0-9]+).*/\1/p' "$WORK/render.log")
[[ "$END" =~ ^[0-9]+$ && "$END" -gt "$START" ]] || { echo "Missing or invalid movie end marker" >&2; exit 1; }
OFFSET=$(python3 -c 'import sys; print(int(sys.argv[1])/60.0)' "$START")
DURATION=$(python3 -c 'import sys; print((int(sys.argv[2])-int(sys.argv[1]))/60.0)' "$START" "$END")
AUDIO_FILTER=$(python3 tools/movie_audio_filter.py "$WORK/audio.avi" "$OFFSET" "$OUT/audio-mastering.json")
# The viewport JPEGs need the same explicit range conversion as Movie Maker.
ffmpeg -n -hide_banner -loglevel error -framerate 60 -i "$WORK/frames/%06d.jpg" -ss "$OFFSET" -i "$WORK/audio.avi" \
  -t "$DURATION" \
  -vf "scale=in_range=pc:out_range=tv:in_color_matrix=bt601:out_color_matrix=bt709,format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709" \
  -map 0:v:0 -map 1:a:0 -c:v libx264 -preset medium -crf 18 -maxrate 45M -bufsize 90M -profile:v high -level 5.1 \
  -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 \
  -pix_fmt yuv420p -r 60 -x264-params keyint=120:min-keyint=60 -af "$AUDIO_FILTER,apad" -c:a aac -b:a 256k -ar 48000 -movflags +faststart \
  -shortest "$OUT/${NAME}_4k.mp4"
echo "[$(date +%T)] encode exit $? -> $OUT/${NAME}_4k.mp4"
python3 - "$OUT/credits.md" <<'PY'
from pathlib import Path
import sys
intro = "# The Last Camp — film credits\n\nCaptured in Godot 4.8. Environmental sound and Foley only; no background music.\n\n"
Path(sys.argv[1]).write_text(intro + Path("textures/SOURCES.md").read_text() + "\n" + Path("audio/SOURCES.md").read_text() + "\n" + Path("models/SOURCES.md").read_text() + "\n" + Path("THIRD_PARTY_NOTICES.md").read_text())
PY
