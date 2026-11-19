# Third-party notices

The project's original code and documentation are covered by [LICENSE](LICENSE).
That license does not replace the following third-party licenses.

## Assets

The complete source tables identify each asset, creator, provider, license and
alterations. Keep them with redistributed copies and derivative films:

- [Texture sources](textures/SOURCES.md): eight Poly Haven PBR sets and ambientCG Fabric061, CC0 1.0.
- [Model sources](models/SOURCES.md): 35 Poly Haven models, CC0 1.0.
- [Sound sources](audio/SOURCES.md): no third-party recordings.
- [Font sources](fonts/SOURCES.md): Noto Serif, Caveat and Architects Daughter, SIL Open Font License 1.1.

## Fonts

The game bundles three fonts, unchanged, under the SIL Open Font License, Version 1.1.
The full licence text, with each font's copyright line, is in `fonts/` and is copied
beside the executable in builds:

- Noto Serif: Copyright 2022 The Noto Project Authors
  (https://github.com/notofonts/latin-greek-cyrillic). `fonts/NotoSerif-OFL.txt`.
- Caveat: Copyright 2014 The Caveat Project Authors (https://github.com/googlefonts/caveat).
  `fonts/Caveat-OFL.txt`.
- Architects Daughter: Copyright (c) 2010, Kimberly Geswein (kimberlygeswein.com).
  `fonts/ArchitectsDaughter-OFL.txt`.

The fonts may be used, bundled and redistributed with the game; they may not be sold
on their own. The OFL, not the project's MIT license, covers the font files.

## Generated audio

All of the game's audio is generated with ElevenLabs and licensed for this game. It
is not covered by the project's MIT license, is not redistributed in this
repository and ships only inside builds. Every sound is listed by id in
[`audio/generated/manifest.json`](audio/generated/manifest.json) and June's lines in
[`audio/generated/voice.json`](audio/generated/voice.json).

The original generated textures are project work. No background music is used in
the game or the released film. The asset-specific notices
above take precedence over the project's MIT license for third-party files.

## Hash without Sine

The hash functions in `shaders/inc/noise.gdshaderinc` and
`shaders/firefly.gdshader` include adapted Hash without Sine formulas by David
Hoskins. [Original publication](https://www.shadertoy.com/view/4djSRW);
[published port retaining the notice](https://compute.toys/view/15).

Copyright (c) 2014 David Hoskins

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Godot Engine

Built with Godot Engine, distributed under the MIT license:

Copyright (c) 2014-present Godot Engine contributors.
Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.

[Godot's license and third-party information](https://godotengine.org/license/).
The engine is a separate dependency of the source project. Linux release
packages include `GODOT-NOTICES.txt`, extracted from the actual exported
executable, with its engine license, third-party copyrights and license texts.
Distribute that file, `LICENSE`, this notice and `credits.md` alongside the binary.

## .NET runtime

The C# project uses .NET 10. Self-contained Linux packages also carry
`DOTNET-RUNTIME-LICENSE.TXT` and `DOTNET-RUNTIME-THIRD-PARTY-NOTICES.TXT`, copied
from the exact runtime pack identified by the exported dependency manifest.
Keep both with the executable and its `data_LastCamp_linuxbsd_x86_64/` directory.
The exporter refuses a package if either runtime notice is missing.
