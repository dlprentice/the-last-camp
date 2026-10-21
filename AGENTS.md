# The Last Camp: agent guide

A first-person story game in Godot 4.8 dev6 .NET (C#, Forward+ Vulkan, native
Jolt). This repository is **public** under MIT. The maintainer's machine and
game-dev guides apply on top of this page; it adds only what is specific here.

## Read for the task

- [README.md](README.md): what the game is today, controls and the player commands.
- [docs/design.md](docs/design.md): the finished game: story, loop, places, menus, art, audio, targets.
- [docs/roadmap.md](docs/roadmap.md): ordered milestones with acceptance, and the parallel lanes.
- [docs/architecture.md](docs/architecture.md): source map and how the systems fit.
- [docs/development.md](docs/development.md): build, tests, capture, profiling, rendering, export, assets.
- [docs/validation.md](docs/validation.md): what has been measured and inspected, and the open limits.
- [CONTRIBUTING.md](CONTRIBUTING.md): issue reports, review evidence and attribution.

## Rules for this repository

- Public repository: tracked files carry no machine paths, private notes or session
  notes. `CLAUDE.local.md` and everything in `local-data/` stay untracked.
- Direction: a relaxed story game with no survival penalty and no fail state, and
  no music; environment, Foley and June's voice carry the sound ([design](docs/design.md)).
- Credits: every third-party file keeps its row in `textures/`, `models/` or
  `audio/SOURCES.md`, with `CREDITS.md` and `THIRD_PARTY_NOTICES.md` in step. Converted
  files and their rows change together. Default atlas rebaking must not replace the
  credited PBR sets. A credited asset is retired only when a locally made replacement
  is at least as good in play and costs no more frame time; then its credit goes too.
- `tests/fixtures/` is the GDScript-to-C# conversion contract. Update it only for an
  intentional, reviewed content change, never to hide a mismatch.
- Placement and generated content stay seeded and deterministic. A change that moves
  props, trails or places updates the traversal route and camera paths with it.
- Frame-rate claims come from the exported build; Movie Maker output is not frame rate.
  Keep SDFGI off in play presets and keep the shutdown and resource-lifetime
  mitigations recorded in [validation](docs/validation.md).
- No addons, GDExtensions or new runtime dependencies. No hosted CI workflows; all
  checks run locally.
- Executable builds ship the engine, .NET runtime and asset notices;
  `tools/export_linux.py` copies and checks them.

## Branches

`master` is the default branch. `work/vegetation-optimization` (pushed) holds the
September 30 grass-density and grass-bounds candidates; roadmap milestone M0 decides
what merges and what becomes an `archive/` tag.
