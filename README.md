# The Last Camp

A quiet first-person story game made in **Godot**. You come to close your
grandmother June's lakeside camp for the last time. Her letter and her voice send
you to the places she and your grandfather Tom loved, and on the way you find what
Tom was doing out here alone in his last summer. The complete two-day story
(arrival evening, the old fire ring, Tom's Rock at sunset, the night storm, and
the closing dawn through credits and June's postcard) is playable on `master`
([what "done" means](docs/DONE.md)).

![The camp and its wooded approach](docs/images/arrival.png)

The project combines procedural terrain, trees, grass and camp geometry with
credited photoscanned materials and dressing. All game logic, diagnostics and scene construction use C#, native Jolt
physics and Forward+ Vulkan. There are no addons or GDExtensions.

## Start here

Install **Godot 4.8 beta4 .NET**, its matching .NET export templates, and the **.NET 10 SDK**. The engine is a beta build from
[godotengine.org](https://godotengine.org/download/archive/4.8-beta4/). The project needs a Vulkan-capable
GPU and Forward+; the Compatibility renderer and web/mobile exports are not
supported. The C# source is tested on Linux x86-64, including offscreen GPU
captures and scripted traversal with the actual player controller. Performance
and visual polishing are ongoing; other desktop platforms are not validated.

```bash
git clone https://github.com/dlprentice/the-last-camp.git
cd the-last-camp
dotnet build
godot --headless --path . --import --quit-after 120 --max-fps 30
godot --path . --fullscreen
```

The first import builds Godot's local asset cache. Rebuild the C# assembly with
`dotnet build` after changing source. Everything is constructed in code; opening
the editor is optional, and the one-node main scene is only a bootstrap. The converted
textures and models are included. The audio is not: it is licensed, ships only
in builds, and a clone plays silently with subtitles. After the loading screen, you
start on the trail with control of the player. The optional `-- --intro` camera arrival
can be skipped with Space or a click.

The default preset starts at Medium (59% FSR2) for smoother pacing, with High
capped at 30 FPS. Use `godot --path . --fullscreen -- --quality=high` to select a
fixed preset.
The distant woodland uses lit canopy atlases, with full geometry returning near
the player. Dense vegetation and shadows still make this a demanding scene.
See [validation and limits](docs/validation.md) for measured results and the
difference between interactive performance and offline movie output.

Press **J** for June's journal, which holds what is left to do. There is no timer
and no way to fail. Walking stays within a woodland area roughly 240 metres across
around the camp and pond; the hills and forest beyond continue as scenery.

## Where it is going

The finished game runs over two days and a last morning. You light June's fire on the
first night, follow her journal to the old fire ring where she and Tom first camped,
and open Tom's tackle tin to find his tally book and a route June never knew about.
His blazes lead up twenty stone steps to a lookout over the pond. You keep the fire
alive through a night storm, and on the last morning you strike the tent, rake out
the ash and walk out. It has a title screen, one save, settings, voiced journal
entries and an ending. See [DONE](docs/DONE.md), [the direction](docs/direction.md)
and [the roadmap](docs/roadmap.md).

## Controls

| Input | Action |
| --- | --- |
| W A S D | Walk |
| Shift / C | Run / crouch |
| E or left click | Interact with logs, fire, lanterns and skipping stones |
| Hold/release left click with a stone | Charge and throw; aim low over the water |
| Right click while charging | Cancel the throw |
| L | Hand lantern |
| J | Camp journal; pauses the game while open |
| P | Photo mode; mouse wheel changes flight speed |
| F12 | Save a screenshot |
| T / [ / ] | Run or scrub the time of day |
| Tab / 1–4 | Quality panel / presets |
| F3 / H / F11 | Stats / hide HUD / fullscreen |
| Esc | Pause |

## Inside the scene

- A generated wooded landscape, layered meadow growth and shoreline habitat,
  with wind shared by grass, foliage and cloth.
- A changing atmosphere, volumetric clouds and fog, daylight, moonlight,
  indirect illumination and a scripted thunderstorm.
- Water reflection and transmission passes, distance-dependent underwater
  visibility, surface crossings, GPU wave disturbances and native canoe buoyancy.
- Wet materials, rain impacts and canopy drips; a campfire with smoke, sparks
  and heat distortion; native soft-body cloth.
- Birds, fish, butterflies, dragonflies, gnats and fireflies, with restrained
  environmental audio and surface-aware footsteps.
- A first-person controller, photo mode, deterministic cinematic paths,
  screenshot tools and repeatable local checks.

![Reflections and the pier](docs/images/pond.png)

[Architecture](docs/architecture.md) explains how the effects fit together and
where they use approximations. [Development and rendering](docs/development.md)
contains the build, test, capture and asset-tool commands.

## Films and Linux build

The published film is **5:37 at 1920×1080, 60 FPS**, including credits. A **1:51 showcase
reel** offers a shorter tour. Both use environmental sound and Foley with no
background music. Movie Maker renders them offline; their output FPS is not
the game's interactive frame rate.

Download the full film, shorter reel and Linux package from the
[v1.0.0 release page](https://github.com/dlprentice/the-last-camp/releases/tag/v1.0.0).
Those are the earlier Godot 4.7.2 release's artifacts, not captures or a binary of
the current C# source. They are distributed with credits and SHA-256 checksums.
The repository holds the film's generators, not rendered movies. Render it again with:

```bash
tools/render1080.sh one_night
```

This needs a graphical Linux session, Godot .NET, the .NET SDK, FFmpeg with libx264, Python,
NumPy, Pillow and ripgrep. It writes a fresh directory under
`local-data/renders/` and prints the exact output path. Expect substantially
longer than the film's running time. See [the rendering guide](docs/development.md#rendering).

![The camp at night](docs/images/night_tent.png)

## License and credits

Created by **David Prentice**. Original project code and documentation use the
[MIT license](LICENSE). Third-party shader code and assets retain their own
licenses. The game's audio is generated with ElevenLabs and licensed: it ships in
builds, not in this repository, so a clone plays silently with subtitles.

[Complete credit index](CREDITS.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

If you share the film, include a link to the credits alongside it. The source
contains all nine material sets and 35 credited model assets; the audio manifest
lists every sound by id. The README screenshots are unaltered captures of the earlier release; they are
not new evidence for the C# conversion.
