# The Last Camp: agent guide

A first-person story game in Godot 4.8 beta4 .NET (C#, Forward+ Vulkan, native
Jolt), being built to the micro cut in [DONE](docs/DONE.md) under
[the direction](docs/direction.md), which judges everything; the README says what
plays today. This repository is **public** under MIT. The maintainer's
machine and game-dev guides apply on top of this page; it adds only what is specific
here.

## Read for the task

- [README.md](README.md): what the game is today, controls and the player commands.
- [docs/DONE.md](docs/DONE.md): the definition of done; it wins over every other page.
- [docs/direction.md](docs/direction.md): feeling, light, UI kit, motion, sound and the five nevers.
- [docs/script.md](docs/script.md): every line June speaks and every text read on screen.
- [docs/roadmap.md](docs/roadmap.md): the ordered steps with acceptance, and the fixture plan.
- [docs/sound-list.md](docs/sound-list.md): every sound and what it tells the player.
- [docs/design.md](docs/design.md): the wider world the cut draws on.
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
- Audio is generated with ElevenLabs and licensed: it lives in ignored
  `audio/generated/` and `local-data/`, never in Git, and ships only in builds. The
  tracked `audio/generated/manifest.json` and `voice.json` list every file; a clone
  without the files must build, pass its tests and play silently. No sound is made in
  code.
- Keep only the newest build, the previous verified build, and evidence a current open claim or defect
  cites; everything else in local-data is disposable.
- No addons, GDExtensions or new runtime dependencies. No hosted CI workflows; all
  checks run locally.
- Executable builds ship the engine, .NET runtime and asset notices;
  `tools/export_linux.py` copies and checks them.

## Branches

`master` is the default branch. Work happens on short-lived branches that merge back
when reviewed. `work/vegetation-optimization` (also the `archive/vegetation-optimization`
tag) and `work/m0-grass-merge` hold the September grass candidates until roadmap step 1
decides them; they are then tagged and deleted.
