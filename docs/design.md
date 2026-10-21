# The finished game

This is the design for the complete *The Last Camp*: a short, relaxed first-person
story game of about 90 minutes, built on the existing camp, pond and woodland. It
describes the target. [The roadmap](roadmap.md) orders the work and says how each
step is accepted; [the README](../README.md) describes what plays today.

## What the game is

Three days and nights at a lakeside camp in a northern mixed forest in late
September. You come to close your grandmother's camp for the last time. You light
her fire, follow her old journal to the places she and your grandfather loved,
weather a storm, and on the last morning you leave the site as you found it.

The tone is quiet, warm and a little sad. There is no survival timer, no hunger,
no damage and no fail state. Challenge is light and physical (a good stone throw,
a clean paddle stroke, a fish landed), never punishing. The game ends.

Existing work survives in place. The `one_night` film and showcase reel stay as
offline renders built from the same world. Their camera tools supply the title
background, the chapter cards and the closing cutscene.

## Story

**Setting.** Halloway Pond, a small kettle pond about 34 m across, with a pier, a
canoe and a clearing on its east bank. The camp stands on a state-forest lease that
June and Tom Halloway held from 1974. The lease ends on October 1. After that the
clearing goes back to the forest.

**People.**

- **You**: June's grandchild. You are never named, seen or voiced; June calls you "love".
- **June Halloway** (78): she camped here every autumn for 52 years. Her knees no
  longer manage the trail. She is heard only through her journal entries, read in
  her voice.
- **Tom Halloway**: June's husband, who died in 2014. He built the pier and the
  heron blind and held the stone-skipping record of nine. He is present only in
  June's words and in what he made.

**What June asks.** Her letter, left on the camp table with her journal and her old
film camera, asks three things: "Light the fire one more time. Find our marks and
bring me pictures. And on the last morning, leave it the way we found it."

**Arc.**

| Part | Chapter | What happens |
| --- | --- | --- |
| Beginning | 1. Arrival (evening, day 1) | You walk in at 17:24 past the trailhead post, read June's letter, and do what she did on every first night: firewood, fire, lantern, stones. You sleep in her tent. |
| Middle | 2. The Marks (day and night 2) | You follow the journal to the spring, the old fire ring and, by canoe, Tom's heron blind. You catch and cook supper. A storm comes in after dark: you rig the tarp, keep the fire alive and wait it out in the tent while June tells of the 1996 storm that flattened their tent. |
| End | 3. Leave No Trace (day 3 and the last night) | The birch grove where Tom carved their initials, then a climb to Tom's Rock for the sunset, where June scattered his ashes. The last fire. June's journal has one blank page left, and you write it. |
| Close | Epilogue: Dawn | You douse the fire, pack the kit, take the fire ring apart into a cairn and walk out. Looking back from the trailhead starts the closing cutscene over the empty clearing. Credits roll, then June's reply postcard is read. |

**Journal entries.** There are ten, each 30 to 60 seconds long, voiced and subtitled,
and readable again in the journal.

| # | Entry | Unlocked by | What June tells |
| --- | --- | --- | --- |
| 1 | The Letter | reading the journal on the table | why you are here; her three requests |
| 2 | First Fire | feeding the fire on night 1 | their first fire in 1974, lit in the rain with a whole box of matches |
| 3 | Nine Skips | a throw of 3 or more skips | Tom's record, and how he picked his stones a week ahead |
| 4 | The Spring | reaching the spring | the cold water; the enamel mug that still hangs there |
| 5 | The First Ring | reaching the old fire ring | where they camped before they knew where the wind came from |
| 6 | The Blind | reaching the heron blind | Tom building it in 1988; the heron that came back every year |
| 7 | The Storm of '96 | securing the tarp in the night-2 storm | the night the tent came down and they slept under the canoe |
| 8 | J + T | reaching the birch grove | the initials Tom carved in 1971, before there was a camp |
| 9 | Tom's Rock | reaching the lookout at sunset | 2014; letting go; "the last page is yours" |
| 10 | The Postcard | finishing the game | June's reply to your last page and the pictures |

**The last page.** On the last night you write the final journal page by choosing
one of three closing lines: one about the fire, one about Tom, one about coming
back. June's postcard has a matching variant of its last paragraph. That is the
only branch: it changes words, not content.

## Minute-to-minute loop

1. **Know what to do.** The objective tracker shows one or two current lines; the
   journal (J) holds the full list and June's map.
2. **Go there.** Walk a faint footpath for one to two minutes. Tree blazes, cairns
   and a hand-drawn map guide you; there is no waypoint marker.
3. **Do the thing.** A short, tactile activity of 30 seconds to 3 minutes: skip, cast,
   paddle, split, carry, rig.
4. **Get the reward.** A June entry plays, the journal stamps the place, a photograph
   is added and a camp item unlocks.
5. **Close the day.** Camp chores (wood, fire, water, supper), then rest. Sleep
   autosaves and opens the next chapter.

## Objectives by chapter

The required objectives gate the chapter. Optional ones fill the journal's collections.

**Chapter 1, Arrival** (17:24 to night, about 15 minutes)

- Required: read June's letter at the table; carry a log and feed the fire; light the
  hand lantern; skip a stone at least 3 times; turn in at the tent.
- Optional: photograph the camp at sunset.

**Chapter 2, The Marks** (06:18 to night, about 40 minutes)

- Required, by day: take the paddle and June's tackle tin from the tent; fill the
  bucket at the spring; find the old fire ring; paddle to the heron blind; catch a
  fish; split three logs at the chopping block; cook supper on the tripod (water,
  plus a fish or mushrooms).
- Required, in the storm (starts after supper once it is 20:00 or later): string the
  tarp over the fire (three tie-offs); feed the fire twice while it rains; shelter in
  the tent until it clears.
- Optional: photograph the spring, the ring and the blind; skip 5; forage 6 items;
  catch each of the three species.

**Chapter 3, Leave No Trace** (06:18 to the last night, about 25 minutes)

- Required: find the birch grove; reach Tom's Rock by sunset and photograph the pond
  from it; light the last fire; write the last page; sleep.
- Optional: photograph the carving; skip 7 (Tom's 9 is the stretch goal); forage the
  remaining items.

**Epilogue, Dawn** (about 8 minutes)

- Required: douse the fire with the bucket; pack five things into the crate
  (lantern, pot, rod, tarp, journal); take the fire ring apart into a cairn of five
  stones; walk out to the trailhead.

**Pacing.** The clock runs at 120 s per game hour by day (90 s now) and 90 s at night.
It holds at 18:30 until the day's required objectives are done ("the afternoon
lingers"), and at 23:00 until the evening's are done. Nobody runs out of light.
Time scrubbing (T, [ ]) moves into photo mode only.

## Activities

All of them use the real player controller and the first-person hands.

| Activity | Controls | Built on | Soft setback, never a failure |
| --- | --- | --- | --- |
| Carry and feed firewood | E to take, E at the fire | existing woodpile and firepit | none |
| Split logs | hold left mouse to raise, release on the beat | chopping block and the credited hatchet | a glancing blow; swing again |
| Fire | feeding keeps its intensity up; it dims slowly | existing firepit intensity | it dims; feed it again; in the storm it can go out and needs relighting |
| Hand lantern | L | existing | none |
| Skip stones | hold, aim and release (existing) | StoneFlight, Pond | the stone sinks; take another |
| Paddle the canoe | E at the pier to board; left and right mouse for strokes on each side; S to back-paddle | Canoe's Jolt buoyancy | a drifting canoe returns to its mooring when out of sight; it cannot capsize |
| Fish | hold to cast and release; wait for the bobber; hold to reel against a tension meter | pond disturbance field, wildlife fish | the fish slips off; cast again |
| Fetch water | E at the spring with the bucket | credited bucket model | none |
| Forage | E on mushrooms and blueberries (12 spots) | habitat fields | none |
| Cook | at the tripod: add, stir, wait 20 s | firepit tripod and pot | none |
| Rig the tarp | carry the tarp, E at three tie points | soft-body cloth (as the tent) | none |
| Photograph | photo mode (P) is June's camera; a place counts when its mark is in frame within 25 m and not hidden | PhotoRig | a hint says what is missing |
| Build the cairn | carry five ring stones and place them; they snap to stable spots | Jolt rigid bodies | a stone rolls off and is placed again |

## Places

The walking area stays the existing 120 m radius around (-10, 8). Everything below is
inside it, so no boundary changes are needed. New footpaths join the existing trail
as polylines in the terrain's trail field, and placement keeps the existing support
and route constraints.

| Place | Approximate centre (x, z) | Status | What is there |
| --- | --- | --- | --- |
| Camp clearing | (0, 0) | exists | fire, tent, table, woodpile, chopping block |
| Pond and pier | pond (-34, 4), pier from (-15.9, 6.3) | exists | stones, moored canoe |
| Trailhead | (4, 70), the trail's north end | new post | carved post "HALLOWAY 1974"; the epilogue walk-out |
| The Spring | (30, -40) | new | moss boulder, rock basin, a slow seep, the enamel mug on a nail |
| Old fire ring | (-30, -45) | new | overgrown ring of stones and a fallen log seat |
| Heron blind | far shore, about (-56, 10) | new | a plank hide with a slot window on the reeds; reached by canoe or the long way round |
| Birch grove | (-60, -60) | new | about a dozen birches; one carved "J + T 1971" |
| Tom's Rock | highest walkable ground in the north-east sector, 60–110 m out, near (40, 60) | new | granite outcrop looking west over the pond into the sunset |

Tom's Rock is placed by a probe that finds the highest supported point with a clear
view of the pond. If no point inside the radius sees the water, the radius gets one
corridor along the new footpath, and the boundary checks are extended to match.

## Progression and unlocks

- **Chapters** gate in order: Arrival, The Marks, Leave No Trace, Dawn. Places can be
  visited at any time; their entries play when you first arrive.
- **Camp items** unlock with the story: the paddle and tackle tin (rod and bobber) on
  day 2; the hatchet at the block on day 2; the tarp on night 2; the bucket on day 2
  (needed again at dawn).
- **Collections** are shown in the journal: 10 entries, 7 photographs, 3 fish species
  (perch, sunfish, brook trout), 12 forage finds and the stone-skip best against Tom's 9.
- **After the credits**, "Return to Halloway Pond" opens free play at any time of day
  with every activity unlocked and the collections kept. Chapter select replays any
  chapter from its start.

## Win and lose

- **Win**: finish the epilogue. The closing cutscene, credits and June's postcard
  play, then a summary card (entries, photos, species, forage, best skip) and the
  title menu, with free play and chapter select unlocked.
- **No lose state**, by design. The setbacks are the soft ones listed under
  activities. Nothing is lost permanently and nothing is missable: every optional
  collectible stays reachable in free play.
- **Never stuck.** Fall recovery restores the last supported position, as now. The
  canoe returns to its mooring. The pause menu has "Return to camp", which fades to
  the fire.

## Title, menus, pause, settings and saves

- **Title**: a live view of the camp at dusk on an existing camera path, with the title
  lettering, the fire's sound and wind. Menu: Continue (with the chapter and in-game
  time), New Game (asks before overwriting), Chapter Select and Free Play (after
  finishing), Settings, Credits, Quit.
- **Pause** (Esc): Resume, Journal, Settings, Return to camp, Save and quit to title,
  Quit to desktop.
- **Settings**, saved to `user://settings.cfg`:
  - Graphics: preset (Low, Medium, High, Ultra), adaptive quality on or off, field of
    view 60–90°, motion blur and head-bob on or off.
  - Audio: master, ambience, effects and voice volumes.
  - Subtitles on or off, at three sizes.
  - Controls: mouse sensitivity and invert Y.
  - Key rebinding is out of scope; the controls page lists the bindings.
- **Saves**: one story slot, `user://save/story.json`, versioned. It holds the chapter,
  each objective's state, the clock, weather, fire intensity, unlocked items,
  collections, placed cairn stones, the chosen last line and the last safe position.
  It autosaves on sleep, at each chapter start, on discovering a place and on "Save
  and quit". A write goes to a temporary file and is then renamed, so a crash never
  corrupts the slot. The stone-skip record moves into the save (migrated once from
  `user://camp-records.cfg`). Continue resumes at the last safe position: camp, the
  pier or a place.

## Interface

- **Journal**: a two-page book. The left page holds the objectives and entry text;
  the right page holds June's ink map (rendered in code from the terrain field and
  the trail polylines, with her marks stamped as you find them) and a photo strip.
  It pauses the game, as now.
- **Objective tracker**: one or two lines at the top left that fade after 6 s and
  come back on change.
- Interaction prompts with key glyphs; a reel-tension arc and a throw-charge arc;
  the camera viewfinder with a "mark in frame" tick; chapter title cards; subtitles.
- The style is paper, ink and canvas: warm off-white, charcoal and a single rust
  accent. The text uses the engine's default font, set as ink on paper. The current
  HUD text becomes this system.

## Art direction

Semi-realistic autumn woodland, lit by sun, fire and lantern. It stays grounded:
props rest on real supports and wear matches use. Every new asset comes from a
committed generator.

- **Blender generators** (`tools/blender/*.py`, output in `models/generated/`):
  - first-person hands and forearms, rigged, with poses and animations: carry a log,
    hold a stone, hold the lantern, paddle, rod, camera, hatchet swing, bucket;
  - canoe paddle; rod, reel and bobber; tackle tin;
  - trailhead post with the carved lettering; heron blind; spring basin rocks;
    old fire ring; cairn stones;
  - tent interior: bedroll, sleeping bag, June's kit bag;
  - the carved birch trunk section.
- **Python texture generators** (alongside `tools/bake_textures.py`): canvas,
  weathered planks, carving normal and ambient-occlusion maps, the journal paper and
  ink map, and the enamel chips on the mug.
- **Rework**: broadleaf canopies read as chunky leaf cards up close. Re-bake the oak
  and birch leaf atlases with smaller clusters, and raise the near-tree card count
  within the performance budget. Light the tent interior from the lantern.
- **Credited photoscans** (35 models, 9 PBR sets) stay until a generated replacement
  is at least as good in play.
- **Cutscenes**: the closing cutscene is a new Cinematic timeline (`farewell`, about
  70 s). The title view and chapter cards reuse IntroDolly and the existing camera paths.

## Audio direction

- **No music.** The soundscape (wind, water, fire, birds, insects, rain) and June's
  voice carry the game. The title and credits use the fire and the pond at dusk.
  This keeps the film's direction and suits a game about a quiet place.
- **Voice**: June's ten entries, about 7 minutes in all, come from the local voice kit
  through a new `tools/voice.py`, modelled on the kids' games. It renders, transcribes
  with Whisper and checks the words against the script, then writes OGG files and
  subtitle timings to `audio/voice/`. The voice is warm and elderly with a slight
  rasp; one fixed voice design is committed with its generation settings.
- **New synthesised sounds**, made in `Synth.cs` and AudioDirector:
  - activities: paddle strokes and drips, the canoe's knock on the hull, reel ratchet,
    line whip, bobber plop, a fish splash, axe chop and split, tarp flap and
    tie-off, bucket fill, pot simmer;
  - interface: page turn, camera shutter and wind-on, soft wooden clicks;
  - wildlife: loon and heron calls.
  The existing credited recordings stay.
- **Mix**: voice ducks ambience by 6 dB. Everything goes through the shared room and
  distance model already used for the world. Over a 10-minute gameplay capture the
  mix targets −20 LUFS integrated (±2) with true peak at or below −1 dBTP. Voice
  lines are levelled to −19 LUFS (±1) each, with true peak at or below −3 dBTP.

## Performance targets

On the RTX 4060 Laptop at 1920×1080 output, in the exported build, along the 22 s
benchmark route and a new full-loop route:

| Preset | Mean | 99th percentile | Use |
| --- | --- | --- | --- |
| Medium (new default) | 60 FPS (16.7 ms) | ≤ 22 ms | shipping default |
| High | 45 FPS (22.2 ms) | ≤ 30 ms | quality option |
| Low | 75 FPS (13.3 ms) | ≤ 18 ms | low-power |

Today High is 34.6 ms and Medium 28.8 ms ([measured](validation.md#measured-performance)),
so Medium needs about 12 ms back. The measured levers are grass and understory
submission (4–8 ms), the planar reflection (3–6 ms of total frame time), forest
shadow casters (2–4 ms) and near-tree geometry.

Also required:

- No frame over 100 ms in a 30-minute soak.
- Peak process working set at or below 6 GB and renderer memory at or below 3 GB.
- Under 45 s from launch to the title menu.
- A new game playable under 20 s after the title.
- No crash, core dump or NVIDIA Xid in any acceptance run.

If 60 FPS on Medium needs a visible loss in quality, the options and their measured
costs go to David before a default is cut.

## Scope cuts

Out of scope, on purpose:

- multiple save slots;
- key rebinding;
- swimming in deep water;
- capsizing;
- crafting;
- inventory management beyond the carried item;
- other weather events;
- more than the places listed;
- other platforms than Linux x86-64;
- a web build;
- music;
- a voiced player.
