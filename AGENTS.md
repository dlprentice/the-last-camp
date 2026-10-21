# The Last Camp: agent guide

A first-person camping game in Godot 4.8 dev6 .NET (C#, Forward+ Vulkan, native
Jolt), being built into the story game in [the design](docs/design.md); the README
says what plays today. This repository is **public** under MIT. The maintainer's
machine and game-dev guides apply on top of this page; it adds only what is specific
here.

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
- Credits: every third-party file keeps its row in `textures/`, `models/` or
  `audio/SOURCES.md`, with `CREDITS.md` and `THIRD_PARTY_NOTICES.md` in step. Converted
  files and their rows change together. Default atlas rebaking must not replace the
  credited PBR sets. A credited asset is retired only when a locally made replacement
  is at least as good in play and costs no more frame time; then its credit goes too.
- `tests/fixtures/` is the content baseline. Change it only through the rebaseline
  procedure in [its README](tests/fixtures/README.md), never to hide a mismatch.
- Placement and generated content stay seeded and deterministic. A change that moves
  props, trails or places updates the traversal route and camera paths with it, and
  re-renders the affected film shots for review.
- Frame-rate claims come from the exported build; Movie Maker output is not frame rate.
  Keep SDFGI off in play presets and keep the shutdown and resource-lifetime
  mitigations recorded in [validation](docs/validation.md).
- No addons, GDExtensions or new runtime dependencies. No hosted CI workflows; all
  checks run locally.
- Executable builds ship the engine, .NET runtime and asset notices;
  `tools/export_linux.py` copies and checks them.

## Branches

`master` is the default branch. `work/vegetation-optimization` (pushed) holds the
September 30 grass-density and grass-bounds candidates and is already preserved as
the `archive/vegetation-optimization` tag. `work/m0-grass-merge` (pushed) is the
merge of its accepted parts, waiting for roadmap milestone M0's footage and
benchmark review before it goes to `master`. Both branches are removed when it does.
