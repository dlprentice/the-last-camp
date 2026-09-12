# Contributor and agent guide

Read README.md, CONTRIBUTING.md and docs/development.md before changing the project.
This is a Godot 4.7.2 standard GDScript project using native Jolt and Forward+ Vulkan.

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
