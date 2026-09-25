# Development and rendering

## Dependencies

Playing from source requires standard Godot 4.8 dev6 and a Vulkan-capable desktop
GPU. Use the matching 4.8 dev6 export templates to build a Linux executable.
The project uses GDScript and native Jolt; Mono, .NET, addons and GDExtensions
are not required.

The optional Python tools need Python 3.10 or newer, NumPy and Pillow. The
release checks used Python 3.14. Install these in your preferred isolated
Python environment. Movie tools also need FFmpeg/ffprobe with libx264,
ripgrep, Bash and standard Linux utilities. Repacking models additionally
requires `gltf-transform`. Dependencies are not installed automatically.

## First import and checks

Run from the repository root:

```bash
godot --headless --path . --import
godot --headless --path . --script res://tests/run_tests.gd
python3 -m unittest discover -s tests -p 'test_*.py'
godot --headless --path . -- --quality=high --scene-smoke
```

Headless checks cover source contracts and physics, not rendered appearance
or hardware performance. Before visual changes, capture the full scene;
afterward inspect affected views at full size. For a changed cinematic,
inspect the resulting movie in motion and check audio as well as frames.
All project validation is local; no hosted CI workflow is included.

## Capture, traversal and performance

```bash
python3 tools/showcase_render.py screenshots --display-driver x11 --quality high --shots arrival,pond,night_tent
```

The wrapper creates a fresh output directory and retains complete logs. On
Wayland, omit `--display-driver` to use the engine default, or select
`--display-driver wayland`. On a locked or unfocused Wayland desktop, XWayland
may avoid presentation throttling. A graphical session is still required.

The following paths must be fresh; direct engine capture/benchmark flags do
not all guard against replacing existing output:

```bash
godot --path . --fullscreen -- --skip-intro --quality=high --traverse="$PWD/local-data/traversal/run-1"
godot --path . --fullscreen -- --benchmark=high --out="$PWD/local-data/benchmark-high.json"
```

Create `local-data/` first for direct commands. The traversal tool moves the
actual controller through 15 waypoints, then checks photo mode, the hand
lantern, fire feeding and a quality change. Benchmarking must run without
Movie Maker or a fixed FPS. Record the GPU, driver, output resolution and
internal scale with any reported result.

## Rendering

```bash
DISPLAY_DRIVER=x11 tools/render1080.sh one_night
DISPLAY_DRIVER=x11 tools/render1080.sh showcase
```

`one_night` produces the 5:37 film, including credits. `showcase` produces the
1:51 reel, including credits. Both retain native environmental audio.
`QUALITY=high` selects the High preset instead of the offline Film preset.
Final full-forest masters took 77–135 minutes to render on an RTX 4060 Laptop,
before encoding; timing depends on the selected sequence and preset.

For the nine-shot sequence without title/credits (68 seconds):

```bash
python3 tools/showcase_render.py video --display-driver x11 --quality ultra --fps 60
```

`--cinematic-shots 7,8` captures selected zero-based shots. Use this for
focused checks, not as a substitute for inspecting the full film.

A 4K render uses viewport frame dumps because the Movie Maker window can be
limited by display resolution:

```bash
DISPLAY_DRIVER=x11 tools/render4k.sh one_night
```

Renders use fresh directories under `local-data/renders/`, with 4K work in
`local-data/render-work/`. Allow substantial disk space; avoid RAM-backed
scratch directories. Review an MP4 with:

```bash
python3 tools/film_review.py PATH_TO_FILM.mp4 --out local-data/reviews/fresh-review
```

This writes timestamped contact sheets, EBU R128 loudness and audio windows.
It refuses existing output. Meter checks do not establish whether a sound
mix is pleasant or natural. Keep the complete `credits.md` with uploads.

## Export a Linux package

```bash
tools/export_linux.sh
```

The script prints a fresh `local-data/builds/linux.XXXXXX/` directory. An
optional argument selects a fresh directory elsewhere. It exports an x86-64
binary with embedded assets, checks the actual packaged credit tables, and
writes engine/component notices from the executable. Distribute the whole
package, including its license and credits, rather than the binary alone.

## Asset preparation

The shipped assets are ready to import. These commands are for intentional
asset changes, not normal installation:

```bash
python3 tools/bake_textures.py --only leaves_oak,water
python3 tools/import_textures.py --only grass,path
python3 tools/import_models.py --only fern_02,rock_07
```

Run Godot's import step after changing assets. The baker defaults to generated
atlases and utility maps, preserving downloaded PBR materials. Experimental
fallback PBR sets require an explicit maker and a separate output:

```bash
python3 tools/bake_textures.py --only wood --out local-data/wood-study --check-tiling
```

Review sheets and tiling checks go to fresh `local-data/bakes/` directories,
or a fresh `--review-out`. Never relabel generated fallback maps as photoscans.
Update the relevant `SOURCES.md` table and verify the film credits whenever
asset provenance changes. Partial model imports preserve untouched credit rows. Model conversion validates
a staged candidate before replacement; conversion logs, failed candidates and
the previous model are retained under `local-data/model-imports/`.
