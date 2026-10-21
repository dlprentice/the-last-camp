# Roadmap

This roadmap gets from today's one-evening build to the finished game in
[the design](design.md). The milestones run in order. Each milestone is done only
when every acceptance line has been checked first-hand.

Acceptance rules that apply to every milestone:

- **Build and tests.** `dotnet build` passes with zero warnings. The C# suite and the
  Python suite pass, and their counts only go up.
- **Footage.** Fresh offscreen gameplay footage of the milestone's features is
  captured at 1920×1080 with sound and watched at 1:1, including frame by frame
  for FX and animation. The review notes say what was seen and heard, and what
  was not.
- **Bot playthrough.** A scripted playthrough uses the real player controller
  (the existing traversal harness, extended). It passes with zero failures and zero
  ordinary fall recoveries.
- **Audio check.** Wherever sound changed, it is measured with ffmpeg `ebur128`
  (true peak on), short sounds by their loudest 100 ms. A spectrogram is reviewed
  for clicks, whine and drones, and the result is listened to.
- **Stability.** No new core dump and no NVIDIA Xid after the GPU runs.
- **Docs.** README, architecture, development and validation describe the new
  present state. Superseded claims are removed.

Evidence goes in ignored `local-data/`. Only the conclusions go into
[validation](validation.md).

## Milestones

### M0: Baseline and branch decision

The September 30 grass candidates on `work/vegetation-optimization` get decided.
The distant-grass floor of 0.045 is a measured saving of about 0.5–0.8 ms. It is
adopted only if moving footage at close, grazing, distant, boundary and wet or night
views shows no visible loss. The 8 m cells with adaptive bounds stay opt-in (or
are dropped): they raised render-thread CPU, draw calls, memory and p99. The
accepted parts merge into `master`, and the rest becomes an
`archive/vegetation-optimization` tag.

Acceptance:

- [ ] `master` holds the decision, and the AGENTS.md branch note is gone.
- [ ] A fresh export is made from `master`, and its High, Medium and Low benchmark
  JSON is kept.
- [ ] The exported `--session-check` passes all 15 waypoints.
- [ ] 60 s of the trail-to-pond walk is watched at 1:1, with sound.

### M1: Game shell, chapters and saves (lane A)

- [ ] A title menu, pause menu and settings menu, as specified in the design.
- [ ] A data-driven chapter and objective system that replaces CampSession's
  hard-coded list. Chapter 1 is ported to it with its current content.
- [ ] Clock pacing and its holds, as specified in the design.
- [ ] Chapter title cards.
- [ ] The versioned story save with atomic writes and migration of the stone record.
- [ ] Settings persist.

Acceptance:

- [ ] Headless tests round-trip every save field, reject a corrupt or partial
  file without losing the old one, and migrate `camp-records.cfg`.
- [ ] Offscreen bot run: New Game, complete chapter 1, sleep, Save and quit,
  relaunch, Continue. Day 2 starts at 06:18 with the same state.
- [ ] Footage of the title, menus, settings changes taking effect, and the chapter
  card, watched at 1:1.
- [ ] Interface sounds pass the audio check.

### M2: First-person hands and held items (lane D)

- [ ] A Blender-scripted hands and forearms rig, with its poses and animations.
- [ ] `Viewmodel.cs`, which replaces the floating held meshes for the log, stone
  and lantern.
- [ ] Hold poses ready for the paddle, rod, camera, hatchet and bucket that M4
  will use.

Acceptance:

- [ ] Frame-by-frame review of each held state while walking, running,
  crouching and interacting. There is no clipping, no transparency smear (the
  Sept 30 `fire_fed` defect) and no swimming during head-bob.
- [ ] The stone throw still releases the held mesh into flight.
- [ ] The cost is at most 0.5 ms on High, measured in the export.

### M3: Places, paths and June's map (lane C)

- [ ] The trailhead post, spring, old fire ring, heron blind, birch grove and Tom's
  Rock, each with a committed generator.
- [ ] Footpaths to each place, as polylines in the trail field.
- [ ] The tree blazes.
- [ ] The journal's ink map.
- [ ] The photograph objective check.
- [ ] Tent interior dressing.

Acceptance:

- [ ] A traversal route reaches every place with no recovery.
- [ ] Each place is reachable in both directions on foot. The blind is also
  reachable by the canoe landing (stubbed until M4).
- [ ] Boundary sprints still pass.
- [ ] Full-size day and night stills of each place, plus a 1:1 walk-in clip of
  each, are reviewed.
- [ ] The exported route p99 regresses by no more than 10% against M0.
- [ ] Content fingerprints are updated deliberately, with the change noted.

### M4: Water and wood activities (lane B)

- [ ] Canoe boarding and paddling.
- [ ] Fishing (3 species).
- [ ] Log splitting.
- [ ] The bucket and the spring.
- [ ] Fire intensity decay and relighting.

Acceptance:

- [ ] Each activity has C# unit tests and a bot step that completes it through
  the real controller. The canoe step paddles from the pier to the blind landing
  and back.
- [ ] A 1:1 clip of each activity with sound is reviewed.
- [ ] The new paddle, reel, splash and chop sounds pass the audio check.
- [ ] Canoe buoyancy stays stable for a 5-minute bot paddle: no hull clipping
  and no jitter in the footage.

### M5: Camp life, the storm and the epilogue mechanics (lanes B and A)

- [ ] Foraging (12 spots).
- [ ] Cooking.
- [ ] The tarp and the storm event, triggered by the chapter and not by the
  7-minute timer.
- [ ] The cairn.
- [ ] Per-chapter stone-skip targets.
- [ ] The epilogue chores.
- [ ] Chapters 2 and 3 and the Epilogue, wired to the objective system.

Acceptance:

- [ ] A bot playthrough of chapter 1 through the Epilogue runs with an
  accelerated clock and passes every objective.
- [ ] Storm footage is reviewed at 1:1: rain, tarp cloth, a fire under the tarp,
  thunder and sheltering in the tent.
- [ ] The storm mix passes the audio check.

### M6: Story and voice (lane E, with lane A for the cutscene)

- [ ] `tools/voice.py`.
- [ ] June's ten entries, committed with their scripts.
- [ ] Subtitles and journal entry text.
- [ ] The last-page choice and the three postcard variants.
- [ ] The `farewell` closing cutscene.
- [ ] In-game credits that list every credit table's contents.
- [ ] The summary card.
- [ ] Free Play and Chapter Select.

Acceptance:

- [ ] Every line passes the Whisper word check.
- [ ] Each line is −19 ±1 LUFS with true peak at or below −3 dBTP.
- [ ] Every line is listened to in full, with notes.
- [ ] The cutscene and the credits are watched at 1:1.
- [ ] A bot run reaches the summary card and the title, and Free Play loads.

### M7: Performance (lane F; runs from M0 alongside M1–M6)

The work reaches the design's targets:

- Medium at 60 FPS mean with p99 ≤ 22 ms;
- High at 45 FPS with p99 ≤ 30 ms;
- Low at 75 FPS.

Medium becomes the default. The order of attack is:

1. the reflection (resolution, rate and what it renders);
2. grass and understory submission;
3. forest and understory shadow casters;
4. near-tree geometry;
5. a compute-culling probe for grass through the indirect MultiMesh buffers, only
   after a small exactness probe.

Acceptance:

- [ ] The exported benchmark meets each target on the 22 s route and on a new full
  walk route that passes through every place.
- [ ] Footage of the changed vegetation in motion at the arrival, pond and
  night-fire views shows no visible coverage loss.
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

### M8: Polish and release

- [ ] Re-baked broadleaf canopy cards.
- [ ] A gameplay audio mix pass.
- [ ] The interface finish.
- [ ] A 30-minute soak.
- [ ] The README rewritten for the finished game, with new screenshots taken
  from this build.
- [ ] A Linux package.
- [ ] A GitHub release v2.0.0 with credits and checksums.

Acceptance:

- [ ] A full real-time playthrough (about 90 minutes) by bot is captured and
  watched with sound at 1:1, in sections.
- [ ] A 10-minute capture of the mix is −20 ±2 LUFS with true peak at or below
  −1 dBTP, and its spectrogram is reviewed.
- [ ] The soak has no frame over 100 ms, and peak memory is within target.
- [ ] Launch to title takes under 45 s.
- [ ] No core and no Xid across all runs.
- [ ] The release package passes the export checks and starts a new game.

## Parallel lanes

Each lane works in its own worktree, writes only within its file scope, and asks
the lead for any change to a shared file. The lead owns these shared files:

- `scripts/core/Game.cs`, `scripts/core/Main.cs` and `scripts/content/MainScene.cs`;
- `scripts/camp/ScenePlan.cs` and the place and trail constants in
  `scripts/world/TerrainField.cs`;
- `scripts/core/Cinematic.cs`;
- `project.godot` (input map and settings);
- `tests/RunTests.cs` registration and `tests/fixtures/`;
- all Markdown;
- merges to `master`.

| Lane | Owns | Milestones |
| --- | --- | --- |
| A. Shell and story | `scripts/story/` (new: chapters, objectives, save, settings, story data); `scripts/player/CampSession.cs`; `scripts/ui/` (title, pause, settings, journal book, tracker, subtitles, chapter card, credits, summary); `tests/TestStory*.cs`, `tests/TestSave*.cs` | M1, M5 wiring, M6 cutscene and credits |
| B. Activities | `scripts/activities/` (new: paddling, fishing, splitting, bucket, forage, cooking, tarp, cairn); small agreed hooks in `scripts/camp/Canoe.cs`, `Firepit.cs`, `Dock.cs` and `CampKitchen.cs`; `tests/TestActivities*.cs` | M4, M5 |
| C. Places and world | `scripts/camp/places/` (new); `tools/blender/places_*.py`; `models/generated/places/`; `scripts/camp/Tent.cs` interior; the place texture generators in `tools/` | M3 |
| D. Hands | `tools/blender/hands.py` and `tools/blender/held_props.py`; `models/generated/hands/` and `models/generated/held/`; `scripts/player/Viewmodel.cs` (new); agreed hooks in `scripts/player/Player.cs` | M2 |
| E. Voice and audio | `tools/voice.py`; `audio/voice/`; `scripts/audio/` (`Synth.cs`, `AudioDirector.cs`, new `VoicePlayer.cs`); a gameplay-audio review mode in `tools/film_review.py` | M4–M6 sounds, M6, M8 mix |
| F. Performance and canopy | `scripts/camp/Understory.cs`, `Forest.cs`, `RidgeForest.cs`, `TreeImpostors.cs`, `Pond.cs` reflection settings; `scripts/generation/` (`GrassPlanter.cs`, `TreeGenerator.cs`, `TreeSpecies.cs`); `shaders/`; `scripts/core/QualityPreset.cs`, `Quality.cs`, `CaptureTool.cs`, `FrameMetrics.cs`; `tools/bake_textures.py` leaf atlases | M0, M7, M8 canopy |

The GPU runs of every lane queue on the one machine-wide GPU lock, and lane F's
benchmarks need the GPU to themselves. Plan for waits.
