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
godot --headless --path . --import --quit-after 120 --max-fps 30
godot --headless --path . --script res://tests/RunTests.cs
# One focused C# test class:
godot --headless --path . --script res://tests/RunTests.cs -- --test=TestTerrainChunks
godot --headless --path . --script res://tests/SceneContractProbe.cs
godot --headless --path . --script res://tests/FingerprintProbe.cs
python3 -m unittest discover -s tests -p 'test_*.py'
```

Keep `--quit-after` after `--import`: 4.8 dev6's default one-frame import exit
can race its background help-cache worker. The extra headless iterations allow
that work to finish before shutdown; they do not launch gameplay or GPU rendering.

Where installed, the optional `godot-headless` machine wrapper builds C# first
and isolates user data. A plain Godot invocation needs an explicit rebuild
when source changes. The main scene is a one-node bootstrap; `MainScene.cs`
creates its children in code. No `.gd` files or authored `.tres` files remain.

The fixture probes do not enter the gameplay scene, render or play audio.
Their reference hashes and scope are documented in [tests/fixtures](../tests/fixtures/README.md).
A passing CPU check does not establish rendered appearance, audio mix, traversal
or hardware performance.

The terrain shader has separate hardware checks. They render the production
parallax function against an analytic height field at five detail settings,
and verify that height blending preserves excluded layers and small mask weights:

```bash
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-parallax --probe-out="$PWD/local-data/parallax-check"
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-terrain-weights --probe-out="$PWD/local-data/terrain-weight-check"
```

The grass draw-prefix check compares the production shader with every instance
submitted against the conservative native draw count at three camera positions.
It also requires an unchanged reference to repeat exactly. Wind, temporal AA,
debanding and the light's stochastic screen-space contact shadows are disabled
in this geometry fixture; the game's contact shadows remain enabled on High/Ultra:

```bash
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-grass-density --probe-out="$PWD/local-data/grass-density-check"
```

The canopy check compares individually textured cards with the array batch,
verifies native tree transforms/tints after spatial partitioning, checks that
MultiMesh shaders do not allocate unused per-object uniform blocks, and releases
the source atlases before checking the retained array again:

```bash
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-canopy-batches --probe-out="$PWD/local-data/canopy-check"
```

Two probes exercise the native dev6 rendering additions:

```bash
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-texture-streaming --probe-out="$PWD/local-data/streaming-check"
godot-offscreen --timeout 90 -- --script res://tests/ShowcaseRenderProbe.cs -- --probe-contact-shadows --probe-out="$PWD/local-data/contact-shadow-check"
```

The streaming check compares a resident texture with its full-resolution source,
lets hidden mips retire under the default inactivity policy, and verifies their
return against the reference image. The contact-shadow check renders supported
objects with and without the directional screen-space effect. Neither fixture
establishes complete-scene quality or performance.

Use fresh output directories. These are small shader probes, not the game world.
Adding `--probe-termination` leaves it drawing after `TERMINATION_PROBE_READY`;
sending SIGTERM to that printed PID exercises the production shutdown handler.
A handled termination logs `PROCESS_STOP SIGTERM` and exits with status 143.

## Capture, traversal and performance

See [validation](validation.md) for actual coverage. Capture named views with:

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
godot-offscreen --timeout 600 -- -- --skip-intro --quality=high --session-check --traverse="$PWD/local-data/traversal/run-1"
godot-offscreen --timeout 600 -- -- --benchmark=high --out="$PWD/local-data/benchmark-high.json"
```

For a controlled feature comparison, `--profile` supports named views and cases:

```bash
godot-offscreen --timeout 300 -- -- --quality=high --profile --profile-views=arrival,night_fire --profile-cases="all on,control before restore,control after restore" --out="$PWD/local-data/profile-controls.json"
```

`--profile-images` also saves full-size stills. `--profile-check-state` compares
native render settings before and immediately after a no-op restoration and
exits nonzero on a mismatch. It checks settings, not the renderer's internal
caches; the repeated controls still need comparable timing and geometry counts.
`--full-grass-density` disables the distance budget for a reference capture.
`--unbatched-canopy` retains the old per-variant ridge groups for comparison.
`--resident-textures` loads all streamed maps at full detail for comparison.
`--no-contact-shadows` disables the High/Ultra directional contact pass; the
profiler also has `contact shadows off` and `contact shadows on` cases.
Each view uses its authored hour and FOV. Avoid interpreting a changed control
as a feature optimization, or the main-view GPU counter as total frame cost.

The traversal moves the controller through 15 waypoints and checks photo mode,
the hand lantern, firewood pickup/feeding, charged stone throws, pause/resume on
the dock, a quality change, distant terrain support and local trunk collision.
It also sprints toward the walking boundary in four directions and injects one
fall-through to exercise recovery. Any recovery during normal walking or
interactions fails the run.
With `--session-check`, it also opens the journal and completes rest/sleep through
the tent's actual interaction ray. Benchmark without Movie
Maker or fixed FPS, recording the GPU, driver, resolution, internal scale,
CPU/GPU frame times, memory and hitches. Measure the exported C# build; the
editor host's JIT settings can differ. See [validation](validation.md) for the
measured results and their limits.

### Optimization references and decisions

The September 30 research pass cross-checked the following primary sources
against the exported game and Godot **4.8 dev6, `8898c2b3d`**. The `latest` manual
can describe later engine changes: verify API and renderer behavior in that
pinned source before using them. See [validation](validation.md) for measured
results, rather than treating an optimization technique as a promised saving.

| Question | Primary reference | Consequence for this scene |
| --- | --- | --- |
| Geometry or pixel work? | [Godot GPU optimization](https://docs.godotengine.org/en/latest/tutorials/performance/gpu_optimization.html), [NVIDIA GPU bottleneck analysis](https://developer.nvidia.com/blog/the-peak-performance-analysis-method-for-optimizing-any-gpu-workload/) | Compare resolution, shader work and geometry independently. A high GPU utilization number does not identify the limiting hardware unit. Preserve colour, depth and shadow pass timings, not only FPS. |
| Why can a large batch remain expensive? | [Godot mesh LOD](https://docs.godotengine.org/en/latest/tutorials/3d/mesh_lod.html), [3D optimization](https://docs.godotengine.org/en/latest/tutorials/performance/optimizing_3d_performance.html) | MultiMesh instances share culling and an LOD selected from the nearest part of the batch AABB. Keep spatial cells; fewer draw calls alone do not prove less GPU work. |
| How should distant woodland change representation? | [Godot visibility ranges](https://docs.godotengine.org/en/latest/tutorials/3d/visibility_ranges.html) | Retain continuous silhouettes and reduce material/geometry cost together. Built-in alpha fades invoke transparent rendering; evaluate dither or opaque-compatible transitions in motion. Existing canopy arrays already address this; closer impostors previously lost performance. |
| Would occlusion solve the forest? | [Godot occlusion culling](https://docs.godotengine.org/en/latest/tutorials/3d/occlusion_culling.html) | A whole AABB must be behind an actual solid occluder. Leaf canopies cannot be replaced with opaque occlusion walls. Terrain occlusion was tested and gave negligible benefit in the camp views; do not keep it merely because it is available. |
| How do shadows multiply foliage cost? | [Godot lights and shadows](https://docs.godotengine.org/en/latest/tutorials/3d/lights_and_shadows.html), [dev6 release](https://godotengine.org/article/dev-snapshot-godot-4-8-dev-6/) | Cascades can submit geometry repeatedly. High already uses two cascades, an 80 m shadow range and a 2048 atlas. Measure caster families before reducing these further. Contact shadows supplement small visible geometry but cannot replace offscreen casters. |
| Should cutout shaders discard earlier? | [AMD RDNA performance guide, pixel shaders](https://gpuopen.com/learn/rdna-performance-guide/#pixel-shaders) | Discard and divergent texture access have tradeoffs; do not assume an early return is faster. Evaluate transparent card area and an early-alpha variant separately, preserving texture derivatives and silhouette coverage. AMD-specific throughput figures are not measurements of the tested NVIDIA GPU. |
| Can antialiasing be simplified? | [SpeedTree rendering, alpha-to-coverage](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-4-next-generation-speedtree-rendering) | Evaluate moving leaves and grass, not just a still. This historical reference explains the technique, not current hardware performance. The local FSR1/MSAA trial saved time but increased foliage noise; default FSR2 remains. |
| Would VRS or streaming be the main answer? | [Godot VRS](https://docs.godotengine.org/en/latest/tutorials/3d/variable_rate_shading.html), [dev5 texture streaming](https://godotengine.org/article/dev-snapshot-godot-4-8-dev-5/) | VRS reduces fragment shading, not submitted geometry. Streaming controls texture residency. Neither is evidence that repeated foliage depth/shadow work disappears. Streaming is already enabled on supported PBR maps; its measured route FPS was unchanged. |

The pinned [Forward+ renderer](https://github.com/godotengine/godot/blob/8898c2b3d/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp)
puts animated foliage's colour rendering in the motion-vector list. Therefore
the profiler's **Render Motion Pass is not a separable motion-blur cost**.
Removing motion vectors by disguising time-dependent deformation would also
invalidate temporal reconstruction. The same source counts LOD primitives
without the MultiMesh instance multiplier in that branch; use native timings
and explicit instance counts rather than claiming actual triangle savings from
that counter alone.

Representative warm High arrival samples put depth prepass around 8.6 ms,
directional/spot shadows around 6.5 ms and animated colour/motion around 11.3 ms.
SSAO, SSIL, SSR, contact shadows and fog integration each cost less than 0.5 ms;
FSR2 costs roughly 1 ms. These labels can aggregate work across views, and their
sum is not the main viewport's timing. Feature-disable savings are also not
additive: removing vegetation changes the shadow and reflection workloads too.
The priority is preserving foliage coverage while reducing waste in those
repeated passes, then validating shadow/reflection budgets and the complete
playable route. A shader-only improvement must survive the exported comparison.

The GodotCon [million-tree presentation abstract](https://talks.godotengine.org/godotcon-ams-2026/talk/DL7SJJ/)
also describes voxel aggregation and MultiMesh LOD limitations. Only its abstract
was reviewed; its tree-count claim is not a benchmark for this game or a reason
to replace the current renderer wholesale. The Horizon vegetation slide deck
could not be retrieved (HTTP 403), so its contents are not used as evidence.

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

The nine opaque PBR sets use Godot's `StreamedTexture2D` importer. Their custom
shaders provide `STREAMING_UV` from the actual projected texture coordinates,
with a conservative mip reserve for upscaling. Alpha-cutout atlases and utility
maps retain the ordinary importer. Runtime-generated canopy arrays are separate
and do not stream. Refresh metadata without repacking images or changing credits:

```bash
python3 tools/import_textures.py --imports-only
```

The dev6 loader and streaming override must agree about the initial mip. Atlas
baking first initializes streaming feedback, then pins full detail and flushes
again before drawing; otherwise a newly loaded coarse mip can be reported as
fully resident. The prior override is restored after baking. The hardware probe
checks actual pixels independently of the streamer's memory counter.

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

Prefer original content, but compare replacements in the complete scene and
measure their runtime cost before retiring an existing asset and its credit.

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
