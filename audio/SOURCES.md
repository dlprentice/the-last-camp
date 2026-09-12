# Recorded sound sources

These recordings supplement the demo's generated environmental ambience and
Foley. The current film contains no music. `tools/import_audio.py` produces the edited takes from the public source
files. The originals are retained in ignored `local-data/`.

| Used in | Recording and author | License | Changes |
| --- | --- | --- | --- |
| `water_entry.wav` | [Water Splash — Mike Koenig](https://soundbible.com/1460-Water-Splash.html) | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) | Filtered, mixed with churning water, resampled, faded and level adjusted. |
| `water_entry.wav`, `water_exit.wav` | [Water Churning — Mark DiAngelo](https://soundbible.com/1790-Water-Churning.html) | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) | Selected excerpts, filtered entry tail, separate immersion/surfacing envelopes, resampled, mixed and level adjusted. |
| `thunder.wav` | [Thunder HD — Mark DiAngelo](https://soundbible.com/1913-Thunder-HD.html) | [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) | Removed 0.43 seconds of lead-in, high-pass at 32 Hz, edge fades and peak adjusted. |
| `rain.ogg` | [Rain (loopable) — Ylmir](https://opengameart.org/content/rain-loopable), take 3 | [CC0](https://creativecommons.org/publicdomain/zero/1.0/) | Source OGG unchanged; runtime level and shelter filtering. Author recorded mono at a window and processed stereo width. |
| `fire_loop.wav`, `fire_feed.wav` | [Fireplace Sound loop — PagDev](https://opengameart.org/content/fireplace-sound-loop) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Converted to mono 48 kHz/16-bit; high-pass at 65 Hz, −7 dB low shelf at 200 Hz (two poles, Q 0.707), low-pass at 6500 Hz and DC removal before peak normalization. Continuous take has a one-second equal-power loop crossfade (28.264 seconds), peak 0.74. Feed accent uses 11.0–13.2 seconds with a 5 ms attack and 650 ms release, peak 0.56. |
| `wind_trees.wav` | [Trees in Wind — naturenotesuk](https://freesound.org/people/naturenotesuk/sounds/457428/) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | Edited from the freely available [HQ preview](https://cdn.freesound.org/previews/457/457428_7455632-hq.mp3), source seconds 80–140. High-pass at 70 Hz, two cascaded two-pole low-pass filters at 1.4 kHz (about 40 dB down in the great tit's band while keeping the branch and leaf rustle that the earlier 750 Hz triple cascade muffled into a dull roar), DC removal, two-second equal-power crossfade; 58-second stereo 48 kHz/16-bit loop at peak -5.2 dBFS. Source includes branch sounds and a great tit; filtering is not manual isolation. |

Film credit: “Water and thunder: Mark DiAngelo and Mike Koenig, SoundBible.com, CC BY 3.0; edited and mixed. Rain: Ylmir. Fireplace: PagDev. OpenGameArt.org, CC0 1.0. Trees in Wind: naturenotesuk, Freesound.org, CC0 1.0; edited and filtered.”

The current film has no music player or music cues. The original procedural
score study is retained in `scripts/audio/camp_score.gd` for source history and
standalone tests; it is not part of this film. Bird, insect, owl, footstep and
other generated environmental sounds come from `scripts/audio/`; no external
bird recording is used. Do not infer an individual composer's identity from the
Git author field.

The render wrappers include these source and license links, together with every
external texture credit, in `credits.md` beside each new film. Include that text
with any upload. `tools/import_audio.py --nature-only` reproduces the new fire
and wind takes without touching the previously reviewed water/weather recordings.
