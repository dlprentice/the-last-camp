# Roadmap

This roadmap gets from today's one-evening build to the finished game in
[the design](design.md). The milestones run in order, except performance (M8),
which runs alongside from M0. Each milestone is done only when every acceptance line
has been checked first-hand. David's game-completion goal (2026-10-20) authorizes
the design, implementation, offscreen renders, commits, pushes and releases in this
plan.

Acceptance rules that apply to every milestone:

- **Build and tests.** `dotnet build` passes with zero warnings. The C# suite and the
  Python suite pass, and their counts, first taken on `master` at M0, only go up.
- **Footage.** Fresh offscreen gameplay footage of the milestone's features, made with
  the M0 capture tool at 1920×1080 and 60 FPS with sound, is watched at 1:1, frame by
  frame for FX and animation. The review notes say what was seen and heard, and what
  was not.
- **Feel.** Part of that footage is driven through the real input path (injected key,
  mouse and pad events into the real player controller, not teleports), and the lead
  reviews it for input response, camera motion, hand animation and audio feedback. A
  bot pass is not evidence of fun.
- **Bot playthrough.** A scripted playthrough uses the real player controller (the
  existing traversal harness, extended). It passes with zero failures and zero
  ordinary fall recoveries.
- **Audio check.** Wherever sound changed, it is measured with ffmpeg `ebur128`
  (true peak on), short sounds by their loudest 100 ms. A spectrogram is reviewed for
  clicks, whine and drones, and the result is listened to.
- **Fixtures.** Content fingerprints and the scene dump change only in the groups
  listed for the milestone in [the fixture plan](#fixture-plan), through the rebaseline
  procedure in [tests/fixtures](../tests/fixtures/README.md).
- **Films.** Where places, paths or vegetation changed, the affected `one_night` and
  `showcase` shots are re-rendered (`--cinematic-shots`) and watched at 1:1 for
  cameras clipping into new paths, places or plants.
- **Stability.** No new core dump and no NVIDIA Xid after the GPU runs.
- **Docs.** README, architecture, development and validation describe the new present
  state. Superseded claims are removed.

Evidence goes in ignored `local-data/`. Only the conclusions go into
[validation](validation.md).

## Milestones

### M0: Baseline, tools and branch decision

The September 30 grass candidates on `work/vegetation-optimization` get decided.
The distant-grass floor of 0.045 is a measured saving of about 0.5–0.8 ms. It is
adopted only if moving footage at close, grazing, distant, boundary and wet or night
views shows no visible loss. The 8 m cells with adaptive bounds stay opt-in (or are
dropped): they raised render-thread CPU, draw calls, memory and p99. The accepted
parts merge into `master`, and the rest becomes an `archive/vegetation-optimization`
tag.

Then the baseline every later milestone is measured against:

- [ ] The C# and Python suite counts on `master`.
- [ ] `SceneContractProbe` and `FingerprintProbe` run on `master`. The result is
  recorded, every mismatch is traced to the commit that caused it, and the fixtures
  are rebaselined to the current game under the written procedure. They stop being
  the conversion contract and become the current-game baseline.
- [ ] A gameplay capture tool, `tools/gameplay_capture.py`: it runs the traversal (or
  a named chapter script) under Movie Maker at `--fixed-fps 60`, writes the AVI and an
  H.264 encode with sound to a fresh `local-data/captures/` directory, verifies the
  encode's frame count and duration, removes its own AVI, and checks free disk space
  against its size estimate before it starts. It is kept apart from the exported
  real-time runs that measure performance.
- [ ] The traversal harness passes under `--fixed-fps 60` as well as in real time.
- [ ] The full-loop benchmark route is committed beside `BENCH_PATH` (its first
  version covers the existing trail, camp and pier; M4 extends it), and each benchmark
  JSON records the internal scale and the TieredCompilation setting.

Acceptance:

- [ ] `master` holds the grass decision, and the AGENTS.md branch note is gone.
- [ ] A fresh export from `master` gives High, Medium and Low benchmark JSON on both
  routes; Low and Medium replace the pre-streaming figures in validation.
- [ ] The exported `--session-check` passes all 15 waypoints.
- [ ] 60 s of the trail-to-pond walk, captured with the new tool, is watched at 1:1
  with sound.

### M1: Game shell, bands, saves and input (lane A)

- [ ] A title menu shown while the world builds, then the live title view; pause and
  settings menus, as specified in the design.
- [ ] Tool runs (`--session-check`, `--benchmark`, `--profile`, `--traverse`,
  `--cinematic` and the other capture flags) bypass the title.
- [ ] A data-driven chapter, band and objective system that replaces CampSession's
  hard-coded list. Chapter 1 is ported to it with its current content and the
  design's two bands.
- [ ] The band clock: per-band rates, holds, and player-driven transitions (rest,
  sit, turn in). A unit test checks every story time in the design against
  `Atmosphere.sun_elevation_deg`, so the story and the sky cannot drift apart. The
  sun model stays as it is (decision recorded in the design).
- [ ] Chapter title cards.
- [ ] The versioned story save with atomic writes, a kept old slot on a failed write,
  and migration of the stone record.
- [ ] Settings persist, including the accessibility options.
- [ ] Gamepad bindings for every action, and prompts that follow the last device.

Acceptance:

- [ ] Headless tests round-trip every save field, reject a corrupt or partial file
  without losing the old one, keep the old slot when the write fails, and migrate
  `camp-records.cfg`.
- [ ] Offscreen bot run: New Game, complete chapter 1, sleep, Save and quit,
  relaunch, Continue. Day 2 starts at 06:18 with the same state.
- [ ] A bot that finishes each band's objectives at once is never left waiting, and
  one that idles stays at the hold.
- [ ] Footage of the title, menus, settings changes taking effect and the chapter
  card, watched at 1:1; menu navigation also shown on the pad path.
- [ ] Every existing tool flag still starts straight into the world.
- [ ] The title menu responds within 15 s of launch in the export.
- [ ] Interface sounds pass the audio check.

### M2: First-person hands and held items (lane D)

- [ ] A Blender-scripted hands and forearms rig, with its poses and animations.
- [ ] `Viewmodel.cs`, which replaces the floating held meshes for the log, stone and
  lantern.
- [ ] Hold poses ready for the paddle, rod, fish, camera, axe, bucket, tarp, ring
  stone, tin and letter.

Acceptance:

- [ ] Frame-by-frame review of each held state while walking, running, crouching
  and interacting. There is no clipping, no transparency smear (the Sept 30
  `fire_fed` defect) and no swimming during head-bob.
- [ ] Full-size stills of each pose beside a committed pose reference sheet, and David's
  sign-off on the hands before M5 builds on them.
- [ ] The stone throw still releases the held mesh into flight.
- [ ] The cost is at most 0.5 ms on High, measured in the export.

### M3: Chapter 1 vertical slice (lanes A, D and E, lead integrates)

Chapter 1 is finished at shipping quality before anything else widens.

- [ ] `tools/voice.py` (lane E, started during M1), with June's voice designed once
  and committed with its settings.
- [ ] Voice pilot: entries 1 and 9 rendered and listened to in full. Entry 9 is the
  hardest emotional peak, so it is tested first. If it fails the voice bar below, the
  fallback is chosen here, before more lines are made.
- [ ] Chapter 1 complete: shell, save, hands, the trailhead post (lane C's first
  place), entries 1–3 and Chapter 1's margin notes voiced and subtitled, the journal
  book in its final style, the sunset sequence, the unlit-until-night fire, final
  interface sounds.

Acceptance:

- [ ] Chapter 1 footage from the trailhead to sleep, captured with the tool and
  watched at 1:1 with sound, including the subtitle sync.
- [ ] The voice bar: natural delivery with no TTS artefacts (warble, clipped breaths,
  pitch jumps, wrong stress) anywhere, judged by listening, and especially at the
  emotional peaks. Whisper matching, speaker similarity and loudness are necessary
  but do not pass a line on their own.
- [ ] **David plays Chapter 1** and his notes are answered before M4 starts.

### M4: Places, paths and June's map (lane C)

- [ ] The spring, old fire ring, heron blind, birch grove and Tom's Rock with its
  twenty steps and bench, each with a committed generator.
- [ ] Footpaths to each place at the design's lengths, and Tom's route, as polylines
  in the trail field.
- [ ] June's painted blazes and Tom's notched blazes.
- [ ] The journal's ink map with the north arrow on the engine's convention, the
  optional player mark and the canoe marker.
- [ ] June's handheld camera and the photograph check.
- [ ] Tent interior dressing.

Acceptance:

- [ ] A traversal route reaches every place with no recovery, and the full-loop
  benchmark route is extended through every place.
- [ ] Each place is reachable in both directions on foot. The blind is also
  reachable by the canoe landing (stubbed until M5).
- [ ] Boundary sprints still pass.
- [ ] The walks match the design's time budget within 20%.
- [ ] Full-size day and night stills of each place, plus a 1:1 walk-in clip of each,
  are reviewed; the sunset from Tom's bench is checked at the designed bearing.
- [ ] The camera cannot reach a mark from beyond 25 m of the player's eye; a test
  covers it.
- [ ] The exported route p99 regresses by no more than 10% against M0.

### M5: Water and wood activities (lane B)

- [ ] Canoe seating and paddling.
- [ ] Fishing (3 species, seeded bites) with the held fish.
- [ ] Log splitting with the assisted timing option.
- [ ] The bucket and the spring.
- [ ] Fire intensity decay, going out and relighting.
- [ ] The records: biggest fish, clean-split streak, quiet crossing.

Acceptance:

- [ ] Each activity has C# unit tests and a bot step that completes it through the
  real controller. The canoe step paddles from the pier to the blind landing and back.
- [ ] A 1:1 clip of each activity with sound is reviewed, including a real-input
  clip for feel.
- [ ] The new paddle, reel, splash and chop sounds pass the audio check.
- [ ] A 5-minute bot paddle shows no hull clipping and no camera jitter in the footage.

### M6: Camp life, the storm and the epilogue mechanics (lanes B and A)

- [ ] Foraging (12 spots).
- [ ] Cooking.
- [ ] The tarp and the storm event, triggered by the band and not by the 7-minute
  timer, with both storm outcomes (fire kept, fire lost) and their next morning.
- [ ] Closing the ring, striking the tent, taking down the tarp line, packing.
- [ ] Per-chapter stone-skip targets.
- [ ] Chapters 2 and 3 and the Epilogue, wired to the objective system.

Acceptance:

- [ ] A bot playthrough of chapter 1 through the Epilogue passes every objective at
  normal clock rates, using the band transitions rather than an accelerated clock,
  and logs its time per band. The design's time budget is replaced with these
  measured times.
- [ ] Storm footage is reviewed at 1:1: rain, tarp cloth, a fire under the tarp,
  thunder and sheltering in the tent; both outcomes.
- [ ] Epilogue footage is reviewed at 1:1: the stones set down, the ash raked, the
  tent's cloth falling.
- [ ] The storm mix passes the audio check.

### M7: Story and voice (lane E, with lane A for the cutscene)

- [ ] All ten entries, the margin notes and the postcard pieces, committed with their
  scripts, or the fallback chosen at M3.
- [ ] Subtitles, journal entry text and the found texts.
- [ ] The photograph captions, the last-page choice and the postcard assembly.
- [ ] The `farewell` closing cutscene.
- [ ] In-game credits that list every credit table's contents.
- [ ] The summary card.
- [ ] Free Play (with the time selector and the free-flying photo rig) and Chapter
  Select.

Acceptance:

- [ ] Every line passes the Whisper word check and the speaker similarity gate.
- [ ] Each line is −19 ±1 LUFS with true peak at or below −3 dBTP.
- [ ] Every line is listened to in full against the M3 voice bar, with notes.
- [ ] Subtitle sync is checked in footage of at least three entries and ten margin notes.
- [ ] The cutscene and the credits are watched at 1:1.
- [ ] A bot run reaches the summary card and the title, and Free Play loads.
- [ ] Postcard assembly is tested for every combination.

### M8: Performance (lane F; runs from M0 alongside M1–M7)

The work reaches the design's targets at no less than the pinned internal scales:

- Low at 75 FPS mean with p99 ≤ 18 ms (50% FSR2);
- Medium at 60 FPS mean with p99 ≤ 22 ms (67% FSR2);
- High at 45 FPS mean with p99 ≤ 30 ms (77% FSR2);
- peak working set ≤ 6 GB and renderer memory ≤ 3 GB.

Medium becomes the default if David agrees. High and Ultra keep the full view
distance. The order of attack is:

1. the reflection (resolution, rate and what it renders);
2. grass and understory submission;
3. forest and understory shadow casters;
4. near-tree geometry (only Medium and Low may shorten view distance);
5. a compute-culling probe for grass through the indirect MultiMesh buffers, only
   after a small exactness probe;
6. managed and texture memory, for the working-set target.

Acceptance:

- [ ] The exported benchmark meets each target on the 22 s route and on the full-loop
  route.
- [ ] Footage of the changed vegetation in motion at the arrival, pond and night-fire
  views shows no visible coverage loss.
- [ ] Anything that loses quality goes to David with numbers.

Already tried and not to be repeated without new evidence:

- a native leaf-budget storage scheme (under 0.2 ms);
- a cached wind-field texture (slower, and less accurate);
- sky-reference caching (neutral);
- manual grass LOD and closer tree impostors (hitches or slower);
- aggressive tree LOD and SDFGI (NVIDIA Xid faults);
- an early-alpha leaf shader (under 0.1 ms);
- an early exit for collapsed grass (about 0 ms);
- reflection LOD 4→8 and a far plane of 700→250 m (neutral);
- uniform 2×2 VRS (neutral);
- terrain-bank occlusion of the reflection (no consistent saving).

### M9: Polish and release

- [ ] Re-baked broadleaf canopy cards.
- [ ] A gameplay audio mix pass.
- [ ] The interface finish.
- [ ] A 30-minute soak in the export.
- [ ] The README rewritten for the finished game, with new screenshots taken from
  this build.
- [ ] A Linux package.
- [ ] A GitHub release v2.0.0 with credits and checksums.

Acceptance:

- [ ] The whole game is captured as four chapter-sized Movie Maker runs (Chapter 1,
  Chapter 2, Chapter 3, Epilogue with credits), each under its own GPU lock, and
  every section is watched with sound at 1:1. Movie Maker is not real time: at
  today's frame rates each minute of 60 FPS footage holds the GPU for two minutes or
  more, so a chapter is planned as up to an hour or more of rendering, and the tool's
  disk check sets aside space for each AVI before its encode.
- [ ] Performance comes from separate real-time exported runs: both benchmark routes
  and the soak.
- [ ] A 10-minute capture of the mix is −20 ±2 LUFS with true peak at or below −1 dBTP,
  and its spectrogram is reviewed.
- [ ] The soak has no frame over 100 ms, and peak memory is within target.
- [ ] The title responds within 15 s and a new game is ready within 45 s of launch.
- [ ] No core and no Xid across all runs.
- [ ] **David plays the whole game**, and his notes are answered before the release.
- [ ] The release package passes the export checks and starts a new game.

## Cut order

If milestones slip, these go first, in this order. Each cut updates the design.

1. Foraging and cooking: supper becomes the fish cooked on a stick over the fire.
2. Fishing species: three cut to one (perch).
3. Splitting: the logs come split.
4. The tarp cloth: a simpler rigged tarp replaces the soft-body cloth.
5. The canoe to the blind: the blind is reached on foot only, and the canoe stays a
   free-play activity.
6. Photograph captions.

Never cut: Tom's through-line, the storm, Tom's Rock, the ending and the voiced
entries (or their M3 fallback).

## Fixture plan

The fixtures cover terrain samples, vegetation plans, generated meshes, dressing
transforms, meadow and grass batches, camera routes, waves, the generated audio bank
(`content.json`) and the main scene (`main.scene.txt`). Expected changes:

| Milestone | Expected to change | Why |
| --- | --- | --- |
| M0 | whatever has drifted since 265d2f4a | rebaseline to the current game, each drift traced to its commit |
| M1 | `main.scene.txt` | title, menus and the story nodes join the main scene |
| M2 | held-item meshes, `main.scene.txt` | the viewmodel replaces the floating held meshes |
| M3, M4 | terrain samples, grass and meadow batches, vegetation plans, dressing transforms, camera routes | footpaths change `TRAIL` and `TRAIL_BOUNDS`, which feed terrain and grass; places displace plants |
| M5, M6, M7 | audio bank | new `Synth.cs` sounds |
| M8, M9 | vegetation plans and batches, generated tree meshes | performance work and the canopy re-bake |

Any key outside its milestone's groups is a regression until proved otherwise.

## Parallel lanes

Each lane works in its own worktree, writes only within its file scope, and asks the
owner for any change to a file it does not own. The lead owns these shared files:

- `scripts/core/Game.cs`, `scripts/core/Main.cs`, `scripts/content/MainScene.cs`;
- `scripts/core/TraversalCheck.cs` (every lane's bot steps are requested from the lead);
- `scripts/player/Player.cs` (canoe seating, the fishing stance and viewmodel hooks are
  requested from the lead);
- `scripts/player/PhotoRig.cs` (free play only);
- `scripts/camp/Pond.cs` and `scripts/world/PondSimulation.cs` (the bobber and the
  reflection settings are requested from the lead);
- `scripts/camp/ScenePlan.cs` and the place and trail constants in
  `scripts/world/TerrainField.cs`;
- `scripts/core/Cinematic.cs`;
- `project.godot` (input map and settings);
- `tests/RunTests.cs` registration and `tests/fixtures/`;
- all Markdown;
- merges to `master`.

| Lane | Owns | Milestones |
| --- | --- | --- |
| A. Shell and story | `scripts/story/` (new: chapters, bands, objectives, save, settings, story data); `scripts/player/CampSession.cs`; `scripts/ui/` including `Hud.cs` (title, pause, settings, journal book, tracker, prompts and arcs, subtitles, chapter card, credits, summary); `tests/TestStory*.cs`, `tests/TestSave*.cs` | M1, M3, M6 wiring, M7 cutscene and credits |
| B. Activities | `scripts/activities/` (new: paddling, fishing, splitting, bucket, forage, cooking, tarp, ring, tent strike); `scripts/world/CampWeather.cs` (the band-driven storm); `scripts/camp/Wildlife.cs` (catchable fish, loon, heron); small agreed hooks in `scripts/camp/Canoe.cs`, `Firepit.cs`, `Dock.cs` and `CampKitchen.cs`, and the tent-strike hook requested from lane C; `tests/TestActivities*.cs` | M5, M6 |
| C. Places and world | `scripts/camp/places/` (new); `scripts/camp/HabitatDiversity.cs` (forage spots); `scripts/player/JuneCamera.cs` (new handheld camera and photo check); `tools/blender/places_*.py`; `models/generated/places/`; `scripts/camp/Tent.cs` (interior and struck state); the place texture generators in `tools/` | M3 trailhead, M4 |
| D. Hands | `tools/blender/hands.py` and `tools/blender/held_props.py`; `models/generated/hands/` and `models/generated/held/`; `scripts/player/Viewmodel.cs` (new) | M2, M3 |
| E. Voice and audio | `tools/voice.py`; `audio/voice/`; `scripts/audio/` (`Synth.cs`, `AudioDirector.cs`, new `VoicePlayer.cs`); a gameplay-audio review mode in `tools/film_review.py` | M1–M3 voice, M5–M7 sounds, M7, M9 mix |
| F. Performance and canopy | `scripts/camp/Understory.cs`, `Forest.cs`, `RidgeForest.cs`, `TreeImpostors.cs`; `scripts/generation/` (`GrassPlanter.cs`, `TreeGenerator.cs`, `TreeSpecies.cs`); `shaders/`; `scripts/core/QualityPreset.cs`, `Quality.cs`, `CaptureTool.cs`, `FrameMetrics.cs`; `tools/gameplay_capture.py`; `tools/bake_textures.py` leaf atlases | M0, M8, M9 canopy |

The GPU runs of every lane queue on the one machine-wide GPU lock, and lane F's
benchmarks need the GPU to themselves. Plan for waits, especially for the long
chapter captures.
