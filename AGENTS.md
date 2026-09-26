# Contributor and agent guide

This is a Godot 4.8 dev6 .NET C# project using native Jolt and Forward+ Vulkan.

Game logic, tests, diagnostics and capture harnesses are C#. Build the world,
materials and UI in code; retain only the minimal main-scene bootstrap. No editor
authoring, previews or tool scripts. Python/Blender asset tooling and GPU shaders
keep their native languages. Keep the project self-contained.

Read the documentation relevant to the task:

- [README.md](README.md) for setup, controls, supported platforms and release contents.
- [CONTRIBUTING.md](CONTRIBUTING.md) when preparing an issue report or contribution,
  including review evidence and attribution requirements.
- [docs/development.md](docs/development.md) for the applicable import, test, capture,
  performance, rendering, export or asset-preparation procedure.

- Run checks locally. Do not add or dispatch hosted workflows without maintainer approval.
- Preserve source changes, credits and existing outputs. Use fresh output directories.
  Generated media, build output, downloads and logs belong in ignored local-data/.
- Use the first-import step on a clean clone. Run the affected tests after source changes.
  A headless pass does not establish GPU appearance or realtime performance.
- Capture and inspect affected views after visual changes. Review changed cinematics in
  motion with their sound; do not infer audio quality from loudness alone.
- Keep deterministic placement, authored walking/camera routes, grounded props, continuous
  forest coverage, the shared wind field and the no-music film direction.
- Keep converted asset files and the corresponding SOURCES.md rows consistent. Default
  atlas rebaking must not replace the credited photoscan PBR sets.
- Third-party code and assets need redistribution permission and complete attribution.
  Include engine and component notices when distributing executable builds.
- Avoid broad rewrites, unrequested runtime dependencies and performance claims based on
  offline Movie Maker output. Describe measurements and remaining limitations accurately.
- Do not commit credentials, caches, generated movies, private machine notes or prior
  development history. master is the default branch of the clean public source edition.

The C# conversion has a rendering hold: do not render, launch gameplay, capture or
benchmark until the maintainer explicitly authorizes it. Compilation, imports,
CPU-only test scripts and the metadata-only export check are allowed. Keep visual
and performance validation marked pending while this hold is in effect.
