# Validation and known limits

The source targets **Godot 4.8 dev6 .NET**, C#, Forward+ Vulkan and native Jolt.
The conversion checks began on September 26, 2026 with
`4.8.dev6.mono.official.8898c2b3d` and .NET SDK 8.0.424 on Linux x86-64.

## Checked without rendering

| Check | Observed result |
| --- | --- |
| C# compilation and Godot import | Passed without errors or warnings |
| C# regression suite | 109 passed, zero failed on September 29: conversion contracts, runtime readback, prop geometry, stone flight, grass batching/quality restoration, canopy partitioning, terrain seams, player-boundary motion and cached-mesh lifetime included |
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
Walking is now confined to the 120 m camping radius. The exported hardware
traversal sprinted outward at four points on that boundary without leaving it
or losing floor support. One deliberately injected fall-through recovered to
the last supported position; the normal 15-waypoint route and all interactions
needed no recovery. Full-size boundary frames were inspected. This is an
artificial movement limit; the existing hills and forest remain scenery.

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

Nearby grass now retains its full prepared population within ten metres, then
smoothly reduces clump density and widens survivors with distance. A conservative
cell budget omits only instances whose shader geometry has fully disappeared.
Three small hardware views submitted 46–67% fewer instances and were pixel-identical
to the same density shader with all instances submitted. That verifies native
culling against the shader, not equivalence to the older full-density scene.
Four complete High 1080p views then saved 4.1–4.8 ms of main-view GPU time.
Full-size before/after images and sequential walking frames were inspected; the
complete exported 15-waypoint interaction, preset-switch and sleep route passed
again with no ordinary fall recovery or new core/kernel GPU fault.
`--full-grass-density` retains the old population for comparisons.

Distant canopy atlases now frame the actual rest meshes, because their baking
passes freeze wind and bypass leaf LOD. The former animated cull bounds left
excessive empty texture space and enlarged both billboard footprints and their
native transition ranges. Separate exported High runs saved 1.8–2.5 ms of
main-view GPU time at the arrival, pond and night-fire views. Full-size arrival,
pond, elevated woodland and dawn comparisons retained forest coverage.

Ridge canopies now share texture arrays across 64-metre spatial cells. The
21,017 placements are unchanged; the distant representation uses 354 cells
instead of 2,360 variant groups. Near geometry and its matching cards share
culling bounds and transition ranges. A hardware fixture checks partitioned
native transforms/tints, zero unused instance-uniform allocations in MultiMesh
shaders, and pixel-identical array/individual rendering at three angles. The
CPU test checks the partition records; the headless renderer cannot read back
the native buffers. Complete High arrival, pond and night-fire views saved
1.1–2.9 ms of GPU time beyond the tighter atlases, with full-size images inspected.
`--unbatched-canopy` retains the previous grouping for comparison. Arrays trade
additional texture memory for fewer draws; the route measurements below include
both canopy changes.
The exported High gameplay route subsequently passed all 15 waypoints,
interactions, preset changes and the evening-to-morning session with zero
failures. A 96-second 1080p/24 diagnostic recording with native audio was
retained; its offline frame rate is not a gameplay-performance measurement.

The feature profiler now preserves the fire's original shadow projection,
takes each view's lighting snapshot at its authored hour, and avoids reassigning
unchanged geometry shadow flags. The old reset changed native shadow workload,
so apparent savings after that reset are not valid optimization evidence.
A readback check caught the projection mismatch before the fix. Subsequent day,
pond and night no-op controls retained comparable geometry counts and GPU times;
moonlit background vegetation also remained visible after restoration.

### Measured performance

An exported High baseline before the canopy changes averaged **73.82 ms/frame
(13.5 FPS)** over the 22-second benchmark route, with a 101.07 ms 99th percentile.
The September 29 exported build, with normal preset defaults and no optional near-tree
impostors, measured as follows at a fixed **1920×1080 output** on
an RTX 4060 Laptop GPU, NVIDIA 610.57.04, with VSync disabled and
`DOTNET_TieredCompilation=0`. Each preset follows the same 22-second route after
warm-up; High, Medium and Low run sequentially after any grass rebuild finishes.

| Preset | Internal resolution scale | Mean frame time | Mean FPS | 99th percentile |
| --- | --- | --- | --- | --- |
| High | 77%, FSR2 | 34.63 ms | 28.9 | 46.08 ms |
| Medium | 67%, FSR2 | 28.84 ms | 34.7 | 37.31 ms |
| Low | 50%, FSR2 | 19.17 ms | 52.2 | 22.07 ms |

The preceding grass-culling build, before tighter canopy atlases and spatial
batching, measured 41.84 / 36.97 / 22.39 ms for High / Medium / Low. These are
separate route runs, not matched frame pairs; fixed-view comparisons isolate
the individual changes more closely.
SDFGI is disabled. Grass uses native curve LOD and the gradual density reduction
described above. All three presets had zero frame intervals over 100 ms.
Renderer memory peaked at roughly 2.50–2.73 GB, and process peak working set
reached 6.12 GB across the sequential presets. No generation-two managed
collection occurred during these short measurements.
Godot's static-memory and water-subview timing counters returned zero and are
unavailable measurements, not zero cost. A manual grass-LOD experiment added
hitches without improving mean frame rate and was removed. A conservative
terrain-occlusion candidate produced negligible savings in the tested camp
views and was also left out. Vegetation and shadow rendering remain the largest
costs. The walking-area limit does not itself cull the surrounding scenery.
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

A failed shader experiment was stopped with SIGTERM and then crashed in the
NVIDIA native library. Another thread was in process-exit cleanup while a GPU
compiler worker was still active; no new Xid or OOM kill accompanied that run.
This is evidence of overlapping termination and rendering, not a proven driver
or engine root cause. SIGTERM now requests the game's normal audio/engine cleanup
from the main thread. A small Forward+ probe completed controlled termination
with status 143 and no new core or kernel GPU fault. Full exported checks then
exposed a separate shutdown hang, both with SIGTERM and ordinary capture
completion. Native inspection found `exit_languages_threads()` waiting for
worker acknowledgements: in one capture, both queues were empty and all 24
workers were idle, but only eight had acknowledged the pre-exit state.

Normal shutdown now stops drawing, unloads the scene, drains managed resource
finalizers and flushes rendering commands while the main loop is still alive,
then allows a short settling interval before quitting. The subsequent exported
High 1080p ground capture exited normally with status zero and no engine error.
This is a project-side mitigation for the observed shutdown case, not an engine
patch. Follow-up checks on the same exported build handled SIGTERM both after
the complete scene was ready and during forest construction. Both logged the
termination handler, exited with status 143, and produced no new core or kernel
GPU fault. Shutdown took approximately eight seconds in the ready scene and
two seconds during loading. The separate earlier device-loss failures remain
unexplained.

The terrain relief shader had a signed-denominator error that prevented
interpolation between its last two height samples. An analytic GPU probe failed
at four of five detail settings before the fix and passed all five afterward,
within the HDR target's rounding tolerance. Full-size ground and camp views were
inspected. An invisible-foliage-work experiment showed no material saving in
three controlled views and was removed; its first-view apparent gain coincided
with changed geometry counts and is not accepted as an optimization result.

Full-size prop views were inspected for canoe ends/seats, pier stones, stacked
wood, table joins, fire detail and the tent. Fresh daylight and dawn route views
were also inspected. Grass still has visible fine-detail aliasing, and complete
motion/lighting/weather acceptance remains open. A foreground glove prototype was rejected after close-up inspection and is not
part of the playable build.

Audio was measured and inspected as a spectrogram, but **not auditioned** in
this validation pass. Those measurements cannot establish that the mix sounds
natural. Full listening, longer gameplay/GC stress, continuous exploration along
the entire walking boundary and other GPUs/operating systems still need coverage.
No AAA quality grade is claimed.
Offline Movie Maker FPS is not interactive performance. Godot's .NET build cannot
make a web export.

The [v1.0.0 release](https://github.com/dlprentice/the-last-camp/releases/tag/v1.0.0)
movies, screenshots and executable were produced before the C# conversion using
Godot 4.7.2. They remain historical release artifacts, not validation of this
build. Visual limitations visible in that release are not claimed fixed by a
language migration.

See [development](development.md) for the checks and the separate rendering,
traversal, listening and performance procedures.
