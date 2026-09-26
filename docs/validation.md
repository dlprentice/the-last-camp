# Validation and known limits

The source targets **Godot 4.8 dev6 .NET**, C#, Forward+ Vulkan and native Jolt.
The conversion checks below were run on September 26, 2026 with
`4.8.dev6.mono.official.8898c2b3d` and .NET SDK 8.0.424 on Linux x86-64.

## Checked without rendering

| Check | Observed result |
| --- | --- |
| C# compilation and Godot import | Passed without errors or warnings |
| C# regression suite | 96 passed, zero failed: the original 92 tests plus four conversion checks |
| Python tooling suite | 47 passed, zero failed |
| Main-scene builder | Canonical dump matches the original scene: root plus nine children, native stored properties, owners, groups and persistent connections |
| Deterministic content and audio | All 1,065 SHA-256 fingerprints match the GDScript reference byte for byte |
| Capture dependency preflight and shell syntax | Passed; no capture was launched |
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

## Validation still on hold

Rendering and gameplay are on hold until explicitly authorized by the maintainer.
The converted build has **not** been rendered, played, captured, benchmarked or
subjectively listened to. Full-scene construction, interactions, traversal,
camera motion, shader behavior, audio timing and long-running cleanup still need
runtime verification. GPU compute and worker-thread behavior under the complete
scene are not established by the CPU-only suite.

There is no current FPS claim or demonstrated performance improvement from C#.
CPU/GPU frame times, memory, collection pauses and hitches need measurement in the
exported build on the actual hardware. Offline Movie Maker output FPS is not
interactive performance. Other operating systems and GPUs have not been tested;
Godot's .NET build does not support the project's web export.

The [v1.0.0 release](https://github.com/dlprentice/the-last-camp/releases/tag/v1.0.0)
movies, screenshots and executable were produced before the C# conversion using
Godot 4.7.2. They remain historical release artifacts, not validation of this
build. Visual limitations visible in that release are not claimed fixed by a
language migration.

See [development](development.md) for the checks and the separate rendering,
traversal, listening and performance procedures to use after the hold is lifted.
