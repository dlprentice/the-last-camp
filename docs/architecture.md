# Architecture

The scene builds from a deterministic plan when the project starts. Most
geometry is generated in C#; converted photoscans supply selected
materials, plants, deadwood and small props. `scenes/main.tscn` is a one-node bootstrap. `scripts/content/MainScene.cs`
constructs the world, camp, player, camera and UI in code, preserving their ready
order. There are no editor tools, authored resource files or GDScript components.

## Source map

| Location | Responsibility |
| --- | --- |
| `scripts/content/` | Root scene builder and native generated-asset loading |
| `scripts/runtime/` | Local C# helpers for engine numeric, collection and Variant semantics |
| `scripts/core/` | Loading, application modes, quality presets, cinematics, camera paths, capture, traversal and profiling tools |
| `scripts/world/` | Terrain field and mesh, atmosphere, time of day, weather, rain contacts, underwater effects and post processing |
| `scripts/generation/` | Mesh builders, tree species and branching, grass and meadow generation |
| `scripts/camp/` | Scene plan, forest, ridge coverage, understory, pond, camp props, scanned dressing, wildlife and interactions |
| `scripts/player/` | First-person movement, carried items, stone flight, the evening activity loop and photo camera |
| `scripts/props/` | Generated prop meshes and shared materials |
| `scripts/audio/` | Environmental sound direction, recordings and synthesis |
| `shaders/` | Materials, water, atmosphere, clouds, particles, compute shaders and post effects |
| `tests/`, `tools/` | Local regressions, asset preparation, build, capture and media inspection |

## Landscape and lighting

A baked terrain height grid is shared by the rendered mesh, collision and
plant placement. Its sampling is dense near camp and coarser on the hills.
Render chunks use the same triangle grid and border attributes, allowing camera
and shadow passes to cull unseen sections. Static collision follows that grid
across the complete landscape; distant trunks receive pooled collision bodies
only in cells around the player.
The player's walking area is a 120 m radius around `(-10, 8)` in the ground
plane, enclosing the camp, pond and approach trail. Outward movement is limited
at this boundary while tangential and return movement remain available. An
unexpected fall below the terrain restores the last supported position; the
traversal harness counts recoveries so this cannot conceal a broken normal route.
Habitat fields use moisture, shade and foot traffic to distribute grass,
flowers, ferns, deadwood and shoreline plants. Spatially grouped MultiMeshes
and authored LODs reduce submission and distant geometry cost.

Trees use generated branching tubes and leaf/needle cards. The surrounding
ridges reuse those species at lower generator detail; they are not a painted
skyline. Wind uses a shared field so neighbouring materials respond coherently.
Lit, six-view canopy atlases replace distant ridge geometry by default, with
full trees returning near the player. Near-forest atlases remain an optional
`--impostors` comparison; the camp's nearest trees keep their full geometry.
Grass buffers retain their prepared capacity when quality decreases. A stable,
spatially distributed instance prefix lowers density without moving surviving
plants; returning to the prepared tier reuses the buffers. Higher density or
coverage than already prepared still requires a worker plan and upload.
Playable presets select native grass mesh LOD earlier. These LODs retain every
blade's root and tip, reducing the number of curve segments rather than removing
plants. Ultra and Film keep the finer distance setting.

The sky integrates an authored single-scattering atmosphere and volumetric
clouds. The same day/night state drives sun, moon, fog, ambient light and
material response. The normal presets use shadow maps, sky lighting, SSAO and
(on High/Ultra) SSIL, with volumetric fog, particles and antialiasing. SDFGI is
disabled after repeatable device loss on the tested engine/driver combination;
its diagnostic opt-in is not a validated play configuration. These are raster rendering techniques, not
hardware ray tracing or a path-traced final image.

## Pond and weather

Water uses dedicated mirrored views, refraction, Fresnel blending, depth
attenuation and an underwater post stack. Entry and exit combine a moving
waterline, foam and draining lens water with distinct sound cues. These are
art-directed approximations; the camera does not simulate a human eye.

Pond contacts drive a 256×256 GPU height/velocity disturbance field with
reflecting obstacles, damping and advected foam. The moored canoe uses native
Jolt physics and six buoyancy samples from analytic waves plus the residual
field. Sparse GPU readback introduces a few frames of feedback latency.
This is a local wave-equation approximation, not a mass-conserving shallow-water
solver or full 3D fluid simulation.

The thunderstorm drives cloud cover, directional flashes, delayed thunder,
world-space rain and material moisture. Rain impacts sample water, terrain and
camp surfaces. Moisture fades after rainfall; the scene does not simulate
physical water accumulation on every mesh.

## Camp, wildlife and sound

The fire combines a ray-marched flame volume, particles, coals, light and
heat distortion. Cloth uses native soft bodies. Shared placement constraints
keep props supported and reserve walking/camera routes.

Birds, fish and insects have authored behaviours and paths; they are not a
complete ecological simulation. The audio director combines credited field
recordings with generated wildlife calls and Foley, applying distance,
shelter and underwater filtering. `CampScore.cs` is a retained synthesis
study; the film does not construct its music player.

Normal play starts directly on the trail. `CampSession` advances an unhurried
evening around fire tending, lantern use and scored stone skipping, with a paused
journal and sleep to a playable morning. Stone throws use fixed-step ballistic
integration, incidence-dependent water contacts and energy loss on each skip.
The held mesh becomes the flying stone at release. Scores persist locally;
there is no resource countdown or survival failure condition.

## Cameras and output

The `one_night` timeline has 17 shots: four grounded walks and shorter
observations. Walking gait and footfalls follow travelled distance. Separate
locations fade through black; selected moves remain continuous. A shared
clock shows the world time.

Godot Movie Maker advances fixed 60 FPS simulation steps even when rendering
is slower than realtime. The film preset applies supersampling and expensive
lighting settings. The export tools trim loading/shutdown frames, encode
BT.709 H.264 video, and master native audio without adding music.

See [validation](validation.md) for verified contracts and pending runtime checks,
and [development](development.md) for the commands.
