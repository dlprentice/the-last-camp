#!/usr/bin/env bash
# Trim and encode a Movie Maker render that already exists: DIR/NAME.avi with
# DIR/NAME.render.log carrying the CINEMATIC_START/CINEMATIC_DONE markers.
# render1080.sh calls this after the engine exits; run it by hand to re-encode
# or re-master a render without rendering again.
#   tools/encode_render.sh DIR NAME
set -euo pipefail
OUT=${1:?render directory}
NAME=${2:?sequence name}
cd "$(dirname "$0")/.."
LOG="$OUT/$NAME.render.log"
START=$(sed -nE 's/^CINEMATIC_START.*frames_drawn=([0-9]+).*/\1/p' "$LOG")
[[ "$START" =~ ^[0-9]+$ ]] || { echo "Missing or ambiguous movie start marker" >&2; exit 1; }
END=$(sed -nE 's/^CINEMATIC_DONE.*frames_drawn=([0-9]+).*/\1/p' "$LOG")
[[ "$END" =~ ^[0-9]+$ && "$END" -gt "$START" ]] || { echo "Missing or invalid movie end marker" >&2; exit 1; }
OFFSET=$(python3 -c 'import sys; print(int(sys.argv[1])/60.0)' "$START")
DURATION=$(python3 -c 'import sys; print((int(sys.argv[2])-int(sys.argv[1]))/60.0)' "$START" "$END")
echo "[$(date +%T)] trimming ${START:-0} loading frames (${OFFSET}s)"
rm -f "$OUT/audio-mastering.json"
AUDIO_FILTER=$(python3 tools/movie_audio_filter.py "$OUT/$NAME.avi" "$OFFSET" "$OUT/audio-mastering.json")
# Convert the full-range JPEG samples as well as tagging the output range;
# changing only -pix_fmt leaves ambiguous full-range H.264 in some players.
ffmpeg -y -hide_banner -loglevel error -ss "$OFFSET" -i "$OUT/$NAME.avi" \
  -t "$DURATION" \
  -vf "scale=in_range=pc:out_range=tv:in_color_matrix=bt601:out_color_matrix=bt709,format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709" \
  -c:v libx264 -preset slow -crf 18 -maxrate 24M -bufsize 48M -profile:v high -level 4.2 \
  -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 \
  -pix_fmt yuv420p -r 60 -x264-params keyint=120:min-keyint=60 -af "$AUDIO_FILTER" -c:a aac -b:a 256k -ar 48000 -movflags +faststart \
  "$OUT/$NAME.mp4"
echo "[$(date +%T)] encode exit $?"
python3 - "$OUT/credits.md" <<'PY'
from pathlib import Path
import sys
intro = "# The Last Camp — film credits\n\nCaptured in Godot 4.7.2. Environmental sound and Foley only; no background music.\n\n"
Path(sys.argv[1]).write_text(intro + Path("textures/SOURCES.md").read_text() + "\n" + Path("audio/SOURCES.md").read_text() + "\n" + Path("models/SOURCES.md").read_text() + "\n" + Path("THIRD_PARTY_NOTICES.md").read_text())
PY
ffprobe -v error -select_streams v:0 -show_entries stream=width,height,nb_frames:format=duration,size -of default=nw=1 "$OUT/$NAME.mp4"
echo "[$(date +%T)] done $NAME"
