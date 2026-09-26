# Development and rendering

## Toolchain

Use **Godot 4.8 dev6 .NET**, the matching .NET export templates, and the **.NET 8
SDK** pinned by `global.json`. Godot is the engine; C# constructs the complete
world, UI, materials and behavior. No editor workflow, addons or GDExtensions
are required. GPU shaders remain shader source; offline asset tools use Python
and Blender.

Optional media/asset tools need Python 3.10+, NumPy, Pillow, FFmpeg/ffprobe with
libx264 and AAC, Bash and ripgrep. Model conversion uses Blender's bundled glTF
importer/exporter (checked with Blender 5.2.1). Nothing installs dependencies or
downloads art automatically. All checks run locally; there is no hosted CI.

## Build and CPU-only checks

Run from the repository root. `godot` below must resolve to the .NET build.

```bash
dotnet build --nologo
godot --headless --path . --import
godot --headless --path . --script res://tests/RunTests.cs
godot --headless --path . --script res://tests/SceneContractProbe.cs
godot --headless --path . --script res://tests/FingerprintProbe.cs
python3 -m unittest discover -s tests -p 'test_*.py'
```

Where installed, the optional `godot-headless` machine wrapper builds C# first
and isolates user data. A plain Godot invocation needs an explicit rebuild
when source changes. The main scene is a one-node bootstrap; `MainScene.cs`
creates its children in code. No `.gd` files or authored `.tres` files remain.

The fixture probes do not enter the gameplay scene, render or play audio.
Their reference hashes and scope are documented in [tests/fixtures](../tests/fixtures/README.md).
A passing CPU check does not establish rendered appearance, audio mix, traversal
or hardware performance.

## Rendering hold

The C# conversion currently has a maintainer-requested rendering hold. Do not
run the GPU, gameplay or Movie Maker commands below until that hold is explicitly
lifted. Compilation, imports, CPU-only probes and the metadata-only package check
can run during the hold. See [validation](validation.md) for actual coverage.

## Capture, traversal and performance

After the hold is lifted:

```bash
python3 tools/showcase_render.py screenshots --quality high --shots arrival,pond,night_tent
```

`--check` performs dependency/argument checks without building or launching the
scene. Real capture builds C# first, retains a source/device manifest and logs,
and refuses an existing output directory. If `godot-offscreen` is installed,
the capture tools use it for an isolated display and serialized GPU access.
Otherwise they open an ordinary game window in the caller's graphical session.
`GODOT` selects the .NET engine executable; `DISPLAY_DRIVER=x11` selects XWayland.

On machines providing the offscreen wrapper, direct checks use fresh paths:

```bash
godot-offscreen --timeout 600 -- -- --skip-intro --quality=high --traverse="$PWD/local-data/traversal/run-1"
godot-offscreen --timeout 600 -- -- --benchmark=high --out="$PWD/local-data/benchmark-high.json"
```

The traversal moves the controller through 15 waypoints and checks photo mode,
the hand lantern, fire feeding and a quality change. Benchmark without Movie
Maker or fixed FPS, recording the GPU, driver, resolution, internal scale,
CPU/GPU frame times, memory and hitches. Measure the exported C# build; the
editor host's JIT settings can differ. No current performance claim is made.

## Rendering

```bash
tools/render1080.sh one_night
tools/render1080.sh showcase
python3 tools/showcase_render.py video --quality ultra --fps 60
tools/render4k.sh one_night
```

The authored `one_night` film is 5:37 including credits; `showcase` is 1:51.
The Python showcase capture omits cards for a nine-shot, 68-second sequence.
All retain native environmental audio, with no music. `--cinematic-shots 7,8`
selects a focused subset for inspection. `QUALITY=high` on the shell tools uses
the playable High preset instead of the expensive offline Film preset.

Each run builds C#, writes fresh directories under `local-data/renders/` or
`local-data/render-work/`, and retains failure evidence. The optional offscreen
wrapper uses completion markers and a bounded timeout (`RENDER_TIMEOUT` for the
shell tools). 4K uses viewport frame dumps plus Movie Maker audio. Allow ample
disk-backed storage; no bulk scratch belongs in a RAM-backed temporary directory.

```bash
python3 tools/film_review.py PATH_TO_FILM.mp4 --out local-data/reviews/fresh-review
```

This produces contact sheets, loudness measurements and audio windows. Inspect
moving footage at full size and listen to the mix; stills and meters alone cannot
establish quality. Keep the complete `credits.md` with uploads.

## Linux .NET package

```bash
tools/export_linux.sh
# Or choose a fresh directory under local-data:
python3 tools/export_linux.py --output-dir local-data/build-candidate
```

The exporter retains logs, builds and exports with isolated user data, validates
an ELF x86-64 executable and its self-contained .NET payload, and runs only the
package metadata check. That path constructs **zero world children** and cannot
start gameplay. It checks embedded credit tables and writes the actual engine's
notices. Runtime licenses are copied from the exact .NET pack named in the build's
`LastCamp.deps.json`; missing notices fail the build.

Distribute the complete `game/` directory, including
`data_LastCamp_linuxbsd_x86_64/`, all licenses/notices and credits. The SDK/editor
is not needed by players. Export success does not substitute for a subsequent
rendered and interactive check.

## Asset preparation

Shipped textures, models and recordings are ready to import. Build new content
locally using committed generators. The historical photoscan conversion tools
work from retained source caches; they do not fetch new art:

```bash
python3 tools/bake_textures.py --only leaves_oak,water
python3 tools/import_textures.py --only grass,path
python3 tools/import_models.py --only fern_02,rock_07
```

The baker defaults to generated atlases/utility maps and protects credited PBR
sets. Experimental fallback materials need a separate output:

```bash
python3 tools/bake_textures.py --only wood --out local-data/wood-study --check-tiling
```

Blender repacks models with separate objects/LOD nodes, PNG PBR maps and optional
Decimate reduction. It does not reproduce the earlier meshoptimizer topology;
reconverted assets need a visual check. A validated staging directory is promoted
only after conversion succeeds. Logs, failed candidates and previous model files
stay under `local-data/model-imports/`. No shipped models were reconverted during
the language migration.

Run Godot's import step after changing assets. Preserve unselected credit rows
and actual retrieval dates. Existing `gltf-transform` entries in `models/SOURCES.md`
are historical provenance of those shipped files, not a current tool dependency.
Do not relabel them as Blender output without actually reconverting the files.
