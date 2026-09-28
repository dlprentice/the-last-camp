# Validation and known limits

The source targets **Godot 4.8 dev6 .NET**, C#, Forward+ Vulkan and native Jolt.
The conversion checks began on September 26, 2026 with
`4.8.dev6.mono.official.8898c2b3d` and .NET SDK 8.0.424 on Linux x86-64.

## Checked without rendering

| Check | Observed result |
| --- | --- |
| C# compilation and Godot import | Passed without errors or warnings |
| C# regression suite | 106 passed, zero failed on September 27: conversion contracts, runtime readback, prop geometry, stone flight, grass batching/quality restoration, terrain seams and cached-mesh lifetime included |
| Python tooling suite | 47 passed, zero failed |
| Main-scene builder | Canonical dump matches the original scene: root plus nine children, native stored properties, owners, groups and persistent connections |
| Deterministic content and audio | At the conversion checkpoint, all 1,065 SHA-256 fingerprints matched the GDScript reference byte for byte |
| Capture dependency preflight and shell syntax | Passed |
| Blender conversion fixture | Separate LOD object names and alpha preserved; 448 triangles reduced to 224, and a 64×64 texture resized to 32×32 |
| Linux .NET release package | Exported successfully with its self-contained runtime and required license notices |
| Standalone package metadata | Zero world children constructed; nine material, 35 model and six recording-source credit entries verified from embedded resources |

The [conversion fixtures](../tests/fixtures/README.md) come from source commit
`265d2f4a9cc59bc69306d2cb43b45aba8f0f3937`, measured on the same engine revision.
The scene comparison normalizes script identity and excludes script-defined
fields. Content fingerprints cover sampled terrain, vegetation plans, generated
mesh channels, dressing transforms, meadow/grass batches, camera routes, analytic
waves and the generated audio bank. They also check the no-music film cues.
These are sampled contracts, not proof of every runtime state. Subsequent gameplay,
terrain partitioning and asset improvements intentionally change parts of that
checkpoint; the old hashes are not claimed as a fingerprint of the entire current game.

An additional primary-checkout import exposed a shutdown abort with a null
editor singleton. Its native worker stack is consistent with the engine's
[background script-documentation path](https://github.com/godotengine/godot/blob/8898c2b3d/editor/doc/editor_help.cpp#L2996).
A fresh, isolated user-cache import completed cleanly using
`--import --quit-after 120 --max-fps 30`, with no new crash. The setup commands
include this shutdown-race mitigation; it is not a patch to the engine.

No shipped model, texture, recording or shader was changed during the conversion.
The Blender check used a synthetic fixture; existing asset provenance remains
unchanged. The executable package includes the exact .NET runtime pack's license
and third-party notices, alongside Godot and project notices. Its metadata check
does not enter the game world.

## Runtime checks and remaining limits

September 27 checks used native Linux, Forward+ Vulkan and an NVIDIA RTX 4060
Laptop GPU. Runs used an isolated offscreen display; the renderer identified
the hardware device. The complete High scene was captured and inspected at
1920×1080. Runtime testing found and fixed an invalid GPU probe callback argument,
missing root meshes, duplicate mugs and a player-resume bug that dropped the
player from the pier onto the lake bed.

The actual player controller completed 15 walking waypoints around the camp,
pier and shallows on both headless physics and the hardware renderer. Interaction
checks cover firewood pickup and consumption, fire feeding, resting and standing,
lantern input, aimed stone throws, photo mode, pause/resume on the pier and quality
changes. The evening journal and sleep sequence have additional traversal checks.
The regression suite alone does not establish how these interactions feel.

The full exported High interaction route also completed with SDFGI disabled,
including a real High/Medium/High preset round trip, the journal and sleep to a
playable dawn. Extra physics probes found terrain support beyond the former
210 m collision limit and active local ridge-trunk collisions. The distant
collision pool stays bounded instead of constructing a body for every tree.

Forest construction's spatial search changed from native Variant containers to
managed collections. The placement digest remained
`21c4d31d14e9a420e2836177bb08867560035abdb258f3b712c9621cacd7406a`:
all 21,017 ridge trees retain their positions. Measured forest construction fell
from about 34.7 seconds to 3.4–4.4 seconds in the subsequent runs. This is a loading
improvement, separate from frame rate. Geometry returns near the player; a count
of planned trees is not a claim that every tree renders at full resolution.

Grass quality reductions now retain the uploaded buffers and draw a spatially
distributed subset. Returning to the prepared quality restores every instance;
only an increase beyond the prepared density or extent starts a new plan.
Focused checks verify unchanged native buffer identities, complete instance
records and coverage across merged cells. This removes unnecessary rebuilding;
it does not establish a frame-rate improvement by itself.

### Measured performance

An exported High baseline before the canopy changes averaged **73.82 ms/frame
(13.5 FPS)** over the 22-second benchmark route, with a 101.07 ms 99th percentile.
The later exported build measured as follows at a fixed **1920×1080 output** on
an RTX 4060 Laptop GPU, NVIDIA 610.57.04, with VSync disabled and
`DOTNET_TieredCompilation=0`. Each preset follows the same 22-second route after
warm-up; High, Medium and Low run sequentially after any grass rebuild finishes.

| Preset | Internal resolution scale | Mean frame time | Mean FPS | 99th percentile |
| --- | --- | --- | --- | --- |
| High | 77%, FSR2 | 50.51 ms | 19.8 | 66.27 ms |
| Medium | 67%, FSR2 | 40.30 ms | 24.8 | 53.09 ms |
| Low | 50%, FSR2 | 24.91 ms | 40.1 | 28.90 ms |

These numbers precede the SDFGI default change. All three
runs had zero frame intervals over 100 ms. Renderer memory peaked at roughly
2.38–2.59 GB; process peak working set reached 6.91 GB across the sequential
runs, including preset rebuilds. Godot's static-memory and water-subview timing
counters returned zero and are unavailable measurements, not zero cost.
A manual grass-LOD experiment added hitches without improving mean frame rate
and was removed. Vegetation and shadow rendering remain the largest costs.
The opt-in near-tree impostor comparison measured 45.61 / 37.93 / 22.89 ms
for High / Medium / Low on a later exported run. It remains optional pending
visual acceptance; these are not default-preset numbers.
**Performance is unfinished; no 60 FPS gameplay claim is established.**

### Stability and visual limits

High startup repeatedly produced Vulkan device loss and an NVIDIA Xid 13 fault
with SDFGI enabled. A separate aggressive tree-LOD experiment also triggered
that fault. SDFGI is now disabled in normal presets; the same full exported
interaction route passed with it disabled. That comparison supports the
workaround for that startup case, not a proven engine/driver root cause or a
promise for other hardware. A later SDFGI-disabled traversal failed during the
quality-change section, with an Xid 13 illegal-instruction fault and a native
driver segmentation fault. Its wrapper was interrupted; the record does not
establish an ordinary timeout or prove that quality switching caused the crash.
The build is not considered generally stable from the earlier passing route.
The `--sdfgi` opt-in remains diagnostic and unvalidated.

The first attempted Vulkan validation run lacked the layer. A subsequent run
loaded the Khronos validation layer and completed a High/Medium/High round trip
in the exported build without a validation error or new kernel GPU fault. Its
instrumented timings are not performance measurements. This does not explain or
invalidate the earlier failure. Native failure and validation logs were retained.

A separate startup run failed while retrieving a cached tree mesh through the
native Variant bridge. Tree-generation records now use strong, typed managed
ownership; a regression exercises collections while those resources are cached.
Subsequent exported loading and traversal passed. The original managed failure's
exact cause remains unproven.

Another exported startup stopped inside `ImporterMesh.GetMesh`, with invalid
mesh-storage errors and a native bounds assertion. No new kernel GPU fault
accompanied this case. Mesh generation now holds the importer in a `using` scope
through return marshalling, so its finalizer cannot release its mesh during that
last call. A focused test repeatedly creates LOD meshes while forcing concurrent
collections; it passes headless and on hardware Vulkan. The subsequent full
exported benchmark and complete 15-waypoint interaction route also completed
without errors or a new core/kernel GPU fault. This fixes a lifetime risk at the observed
call; it is not a claim that all earlier native failures share that cause.

Full-size prop views were inspected for canoe ends/seats, pier stones, stacked
wood, table joins, fire detail and the tent. Fresh daylight and dawn route views
were also inspected. Grass still has visible fine-detail aliasing, and complete
motion/lighting/weather acceptance remains open. A foreground glove prototype was rejected after close-up inspection and is not
part of the playable build.

Audio was measured and inspected as a spectrogram, but **not auditioned** in
this validation pass. Those measurements cannot establish that the mix sounds
natural. Full listening, longer gameplay/GC stress, the outer world boundary and
other GPUs/operating systems still need coverage. No AAA quality grade is claimed.
Offline Movie Maker FPS is not interactive performance. Godot's .NET build cannot
make a web export.

The [v1.0.0 release](https://github.com/dlprentice/the-last-camp/releases/tag/v1.0.0)
movies, screenshots and executable were produced before the C# conversion using
Godot 4.7.2. They remain historical release artifacts, not validation of this
build. Visual limitations visible in that release are not claimed fixed by a
language migration.

See [development](development.md) for the checks and the separate rendering,
traversal, listening and performance procedures.
