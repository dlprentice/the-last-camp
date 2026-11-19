# Credits

**The Last Camp — created by David Prentice.**

Built with [Godot Engine 4.8 beta4 .NET](https://godotengine.org/license/) (the published film and reel were rendered
with 4.7.2), using
Forward+ Vulkan, C# and native Jolt physics. The main film and reel
contain environmental sound and Foley; there is no background music.

Every external asset is listed by title, creator, source link, license and
alterations in these canonical tables:

| Category | Complete attribution | License |
| --- | --- | --- |
| Nine PBR material sets | [Texture sources](textures/SOURCES.md) | CC0 1.0 |
| 35 photoscanned models | [Model sources](models/SOURCES.md) | CC0 1.0 |
| All game audio, generated with ElevenLabs | [Sound sources](audio/SOURCES.md) | Licensed; not redistributed in the repository |
| Adapted shader hashes and engine | [Third-party notices](THIRD_PARTY_NOTICES.md) | MIT and engine component licenses |

## Audio

The game's audio, its ambience, footsteps, wildlife, weather, action sounds and
June's voice, is generated with ElevenLabs and licensed for this game. It is not
covered by the MIT license and is not redistributed in this repository; builds
carry it. Each sound is listed by id in
[`audio/generated/manifest.json`](audio/generated/manifest.json) and June's lines in
[`audio/generated/voice.json`](audio/generated/voice.json).

The remaining generated geometry and textures are project work. The source includes three credited root-ball models
that are retained as optional assets but not placed in the scene.

For a video upload, retain the end credits and link this page so the source
and license links travel with the film. For a playable build, distribute its
`LICENSE`, `THIRD_PARTY_NOTICES.md`, `GODOT-NOTICES.txt`,
`DOTNET-RUNTIME-LICENSE.TXT`, `DOTNET-RUNTIME-THIRD-PARTY-NOTICES.TXT` and
`credits.md` files
with the executable and its `data_LastCamp_linuxbsd_x86_64/` runtime directory. Original code uses [MIT](LICENSE); third-party files
keep their asset-specific terms.
