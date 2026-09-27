# Validation and known limits

The source targets **Godot 4.8 dev6 .NET**, C#, Forward+ Vulkan and native Jolt.
The conversion checks began on September 26, 2026 with
`4.8.dev6.mono.official.8898c2b3d` and .NET SDK 8.0.424 on Linux x86-64.

## Checked without rendering

| Check | Observed result |
| --- | --- |
| C# compilation and Godot import | Passed without errors or warnings |
| C# regression suite | 101 passed, zero failed on September 27: conversion contracts, runtime readback, prop geometry, stone flight and grass batching included |
| Python tooling suite | 47 passed, zero failed |
| Main-scene builder | Canonical dump matches the original scene: root plus nine children, native stored properties, owners, groups and persistent connections |
| Deterministic content and audio | All 1,065 SHA-256 fingerprints match the GDScript reference byte for byte |
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
These are sampled contracts, not proof of every runtime state.

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

An exported High baseline averaged **73.82 ms/frame (13.5 FPS)** over the 22-second
benchmark route, with a 101.07 ms 99th percentile. That predates the canopy
optimization and is not the current build's performance claim. Controlled GPU
profiles identified distant tree geometry, vegetation shading and shadows as the
largest costs. Lit ridge atlases reduced the measured High pond view from about
68 to 42 ms/frame in development builds. Final exported performance, long-run
hitches and memory still need to be measured after the selected changes settle.

Aggressive diagnostic tree-LOD settings triggered repeatable NVIDIA device-loss
faults on this engine/driver combination. They are excluded from the default
profiling sweep. Ordinary traversal and the selected canopy profiles exited
cleanly; that does not prove other drivers or all play sessions are unaffected.

Moving effects, full audio mixes and close prop views need fresh review after
the final changes. No AAA quality grade or 60 FPS gameplay claim is established.
Offline Movie Maker FPS is not interactive performance. Other operating systems
and GPUs have not been tested; Godot's .NET build cannot make a web export.

The [v1.0.0 release](https://github.com/dlprentice/the-last-camp/releases/tag/v1.0.0)
movies, screenshots and executable were produced before the C# conversion using
Godot 4.7.2. They remain historical release artifacts, not validation of this
build. Visual limitations visible in that release are not claimed fixed by a
language migration.

See [development](development.md) for the checks and the separate rendering,
traversal, listening and performance procedures.
