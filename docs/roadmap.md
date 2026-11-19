# Roadmap

This roadmap gets from today's build to the micro cut in [DONE](DONE.md): a complete
story of 25 to 35 minutes. [The direction](direction.md) judges every step; [the
design](design.md) holds the wider world the cut draws on. The steps run in order,
except performance, which runs alongside. A step is done only when every acceptance
line has been checked first-hand.

## Where we stopped (November 19, 2026)

Work paused when the machine was given to other games; it resumes here.

- **On `master`:** Godot 4.8 beta4 with the C# engine workarounds (script pins, collection
  keep-alive, an audio-thread volume rule); the story spine from arrival to the ending,
  the save, and every story interaction; the places (trailhead post, old fire ring, Tom's
  Rock with its twenty steps); the paper-and-ink title, pause, settings, journal and
  credits; and all audio from ElevenLabs, including June's voice (licensed, not in Git).
  The build is clean, and the C# suite, the Python suite and the scene contract pass.
  Open limits are in [validation](validation.md): the content fingerprints await a
  rebaseline after the places review.
- **On branches (pushed, unfinished):**
  - `work/look`: camp ground, night, tent, daytime fire light and canopy. The reviewed
    part is merged; a fourth round (wet ground, firelight reach, path detail, tent hem
    and interior, day shadows) is coded but not reviewed or looked at. Its tip fails one
    meadow test (grass inside the walking ribbon, from the ragged path edge). Unseen risk: the
    pond reflection has no TAA, so distant leaf dither may flicker in the water.
  - `work/places-look`: a site search for moving Tom's Rock so the seated view shows the
    whole pond. The rock has not moved; the best site found fills too little of the
    frame, and the probe test fails.
  - `work/notch`: Tom's blaze carved as real geometry. It builds but has not been run or
    seen.
  - `work/perf`: preset levers measured on a loaded machine and the distant-grass floor
    made a preset value. No default reaches 60 fps yet (about 25–30 ms a frame).
- **Next three steps, in order:**
  1. Move Tom's Rock so the seated view at sunset shows the pond filling the lower third
     to half of the frame (terrain may need to change), reroute the steps so the view
     appears only at the top, and finish the carved notch.
  2. Rain and firelight: wet ground darker and never frosty, and the fire lighting the
     circle and the nearest trees with a flicker.
  3. The 60 fps default preset, with High as a capped 30 fps option, measured on a quiet
     card; then the fingerprint rebaseline.
- **Not yet seen or heard by anyone:** June's voice and every generated sound in play,
  the full story played at length, the look round 4 stills, and the notch.

Acceptance rules for every step:

- **Build and tests.** `dotnet build` passes with zero warnings; the C# and Python
  suites pass with no skips, and their counts only go down when code is removed.
- **Footage.** Fresh offscreen footage of the step's features at 1920×1080 and 60 FPS
  with sound (`tools/gameplay_capture.py`), watched at 1:1, plus full-size stills. The
  notes say what was seen and heard, and what was not.
- **Bot playthrough.** The traversal harness drives the real player controller through
  the step's content with zero failures and zero ordinary fall recoveries.
- **Audio check.** Changed sound is measured with ffmpeg `ebur128` (true peak on) and
  reviewed as a spectrogram, then listened to. The gameplay mix sits about −16 LUFS
  integrated with true peak at or below −1 dBTP.
- **Fixtures.** Content fingerprints and the scene dump change only in the groups the
  [fixture plan](#fixture-plan) names for the step, through the
  [rebaseline procedure](../tests/fixtures/README.md).
- **Stability.** No new core dump and no NVIDIA Xid after the GPU runs.
- **Docs.** README, architecture, development and validation describe the new present.

Evidence goes in ignored `local-data/`; only conclusions go into
[validation](validation.md).

## Steps

### 1. Engine and baseline

- [x] Godot 4.8 beta4 .NET, with the C# script pins for godotengine/godot#109711.
- [ ] Exported High benchmark on beta4 recorded in validation.
- [ ] The grass decision: the distant floor of 0.045 against 0.18, captured on beta4 in
  motion (close, grazing, distant, boundary, wet and night views) and in the exported
  benchmark, chosen by look and frame time and applied directly on the default branch.
  The two September grass branches are then tagged and deleted.

### 2. Story and shell

- [ ] The story spine: chapters, bands, objectives, June's line triggers and the save.
- [ ] The places: the trailhead post, the old fire ring and Tom's Rock with its twenty
  steps, and the paths between them.
- [ ] Every story interaction, from June's letter to the walk out.
- [ ] Title, pause, settings, journal, chapter cards, credits and June's postcard in
  the direction's paper-and-ink kit.
- [ ] A bot playthrough from New Game to the credits through the real controller, with
  its time per band; the total is 25 to 35 minutes.

### 3. Look

- [ ] Close-up grass that reads as soft blades, not dark straps; backlit grass and leaves
  at golden hour.
- [ ] Wet nights: blue-black dark, the fire as the brightest and warmest thing on screen,
  even wet gloss without blotches.
- [ ] First-person hands for every held item, with grip poses; nothing floats.
- [ ] Every screen and transition reviewed against the direction, with the Game
  Overseer's fix list answered.

### 4. Sound and voice

- [ ] Every sound generated with ElevenLabs and listed in `audio/generated/manifest.json`;
  no synthesis code remains.
- [ ] June's seven entries and nine margin notes voiced and subtitled; a 2-minute
  audition reel for David.
- [ ] A 10-minute gameplay capture meets the mix target, and its spectrogram is reviewed.

### 5. Performance (alongside from step 1)

Targets in the exported build at 1920×1080 on the benchmark route: High at 60 FPS if it
can be done without a visible loss, otherwise the best measured trade with its cost
reported; no frame over 100 ms in a 30-minute soak; peak working set at or below 6 GB.
David's standing direction holds: no hard tree culling, no visible fakes.

Already tried and not to be repeated without new evidence: a native leaf-budget storage
scheme, a cached wind-field texture, sky-reference caching, manual grass LOD and closer
tree impostors, aggressive tree LOD and SDFGI (NVIDIA Xid faults), an early-alpha leaf
shader, an early exit for collapsed grass, reflection LOD 4→8 and a 250 m far plane,
uniform 2×2 VRS, and terrain-bank occlusion of the reflection.

### 6. Release

- [ ] A Linux package with the licensed audio inside the build only, its export check
  passing without `--allow-silent`.
- [ ] A full playthrough captured in chapter-sized offscreen runs and watched with
  sound; the Game Overseer accepts the footage.
- [ ] README rewritten for the finished game with new screenshots; a GitHub release with
  credits and checksums.

## Fixture plan

The fixtures cover terrain samples, vegetation plans, generated meshes, dressing
transforms, meadow and grass batches, camera routes, waves and the main scene.

| Step | Expected to change | Why |
| --- | --- | --- |
| 1 | grass and meadow batches, only if the distant floor changes | the grass decision |
| 2 | terrain samples, grass and meadow batches, vegetation plans, dressing transforms, `main.scene.txt` | footpaths feed the trail field; places displace plants; new props and UI nodes |
| 3 | held-item meshes, grass batches, `main.scene.txt` | hands replace the floating held meshes; the grass look |
| 4 | none | audio has no fingerprints |
| 5 | vegetation plans and batches, generated tree meshes | performance work |

Any key outside its step's groups is a regression until proved otherwise.

## Who writes what

Work runs in parallel worktrees, each writer in its own file scope. The lead owns
`scripts/core/Game.cs`, `Main.cs`, `TraversalCheck.cs`, `Cinematic.cs`,
`scripts/content/MainScene.cs`, `project.godot`, `tests/fixtures/`, all Markdown, and
merges to the default branch. GPU runs queue on the machine-wide GPU lock; benchmarks
need the card to themselves.
