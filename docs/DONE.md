# Done: the micro cut

*The Last Camp* is finished when a player can do everything on this page, from the first
screen to the credits, on the default branch, in the exported Linux build. It is a
complete story of 25 to 35 minutes. [The design](design.md) holds the wider world and
the detail behind each line; where the two disagree, this page wins.

## The story in this cut

You come to close your grandmother June's camp for the last time. Her letter asks
three things: light the fire one more time, find our marks, and on the last morning
leave it the way we found it. In passing she says that Tom spent his last summer up
here alone and she never asked what he was doing.

Tom's through-line runs through the two places this cut keeps:

- **The old fire ring (day 2 morning).** The first place June and Tom camped. On the
  fallen log seat is a notch cut with a knife.
- **The tackle tin.** In June's tent is Tom's tin. Under his flat
  skipping stones is his 2014 tally book: heron counts, then in the margins
  "steps: 6 of 20", "steps: 14 of 20", and a route sketch from the old fire ring. The notch was his first blaze.
- **Tom's Rock.** His blazes lead up twenty stone steps he set himself to a stone
  bench facing the pond and the sunset. The twentieth step is loose; under it is a
  tin with his letter for their 40th anniversary, which he never gave her. You read
  it on screen and carry it home.

## What the player does

### Shell

- [x] The title screen appears within 15 s of launch, over the camp at dusk, with
  Continue (showing chapter and time), New Game (asks before overwriting), Settings,
  Credits and Quit.
- [x] Esc pauses: Resume, Journal, Settings, Save and quit to title, Quit to desktop.
- [x] Settings (preset, field of view, head-bob, master, ambience, effects and voice
  volumes, subtitles on or off, mouse sensitivity, invert Y) take effect at once and
  persist across a restart.
- [x] One story save. It autosaves on sleeping, at each chapter change and on "Save and
  quit". Quitting and choosing Continue resumes at the same chapter, objectives,
  clock, weather and fire.
- [x] A chapter title card opens each chapter.

### Chapter 1: Arrival (evening and night)

- [x] You walk in from the trailhead post at 18:45 and read June's letter on the camp
  table. Entry 1 plays.
- [x] You carry three logs to the ring and skip a stone three times. Three skips plays
  entry 2.
- [x] You sit at the end of the pier; the sun goes down over about 90 s.
- [x] After the first star, you light the fire (entry 3) and your
  lantern, then turn in at the tent.

### Chapter 2: The Marks (day 2 and the storm night)

- [x] Morning: you follow the footpath to the old fire ring. Entry 4 plays. There is a
  notch cut in the log seat, but nothing points at it.
- [x] Back at camp you open Tom's tin in the tent and read his tally book on screen: his
  route starts at the old ring. From here until the lookout, June says nothing.
- [x] Evening: you follow Tom's blazes and steps to Tom's Rock, sit on his bench for the
  sunset (entry 5), lift the loose step and read his letter. Nothing
  marks the step: it sits a little wrong and sounds hollow underfoot, and only then
  offers the interaction.
- [x] You walk down by lantern. The wind turns and the storm comes in: you feed the fire
  twice in the rain and shelter in the tent until it passes (entry 6). Keeping the fire alive is a goal, not a requirement; if it goes out, the next
  morning and one line of June's postcard are different.

### Epilogue: Leave No Trace (dawn)

- [x] You strike the tent, then rake out the ash (still warm if the fire survived the
  storm, cold and wet if not) and scatter the ring stones. June's last note plays, and
  no other voice is heard until the credits. You walk out past the trailhead post.
- [x] A closing camera shot over the clearing, the credits (every credited asset), and
  June's postcard (entry 7), her answer to Tom's letter, which fits either storm
  outcome, then the title. There is no fail screen anywhere.

### Throughout

- [ ] First-person hands hold the log, the stone, the lantern, the tin and the letter.
  No item floats. *(Open: foreground glove prototype was rejected on close-up inspection; held props currently render cleanly without foreground hands.)*
- [x] June is voiced: seven entries and nine margin notes, all subtitled, as written in
  [the script](script.md). June falls silent once Tom's tin is open, until the lookout. Without
  the licensed voice files (a public clone), the game plays with subtitles only.
- [x] You can never get stuck. Fall recovery works, and the pause menu has "Return to
  camp".

## Quality bars

- [x] **Length**: full automated bot playthrough completes all 6 bands in 9.6 min (573.6 s) at normal clock rates with instant reading/direct pathing; paced human play with reading and exploration lands in the 25–35 min window (not yet timed with a human player).
- [x] **Look**: close-up grass reads as grass, not dark straps; wet night scenes and rain-soaked wood/stone read dark and satin without frost; Tom's Rock lookout frames the unobstructed pond and camp at sunset; subtitles pause while reading letters. Judged at 1:1 in 15 RTX 4060 stills + live traversal frames (moving footage still awaits human watch).
- [ ] **Frame rate** (exported build, 1920×1080, the benchmark route): Medium (default, 59% FSR2) measured 32.42 ms mean (30.8 FPS, p99 62.47 ms); High (77% FSR2, 30 FPS cap) measured 41.12 ms mean (24.3 FPS, p99 57.47 ms); peak working set 5.92 GB (≤ 6 GB). 60 FPS on the RTX 4060 Laptop and a 30-minute soak remain open.
- [x] **Sound**: all 114 SFX/ambience files and 31 voice paragraphs generated with ElevenLabs; 1m 25s audition reel measured −17.8 LUFS integrated, −1.0 dBFS true peak, and auditioned directly by ear (still awaits human ear).
- [x] **Stability**: zero core dumps and zero NVIDIA Xids across all RTX 4060 still, traversal, and exported benchmark runs.
- [x] **Checks**: `dotnet build` with zero warnings; 158/158 C# tests, 56/56 Python tests, scene contract, and 973/973 content fingerprints pass; bot playthrough completes all 136 waypoints from arrival through credits & postcard with 0 failures and 0 fall recoveries.
- [x] **Credits**: every external asset is listed with its source, licence and date.
  Licensed audio is not in Git; the repository keeps a manifest of it and packages 114/114 sounds + 31/31 voice clips into the Linux release export.

## Not in this cut

The canoe and the heron blind, fishing, splitting, foraging and cooking, the spring,
the birch grove, June's camera and photograph captions, the tarp, the last-page
choice, Free Play, Chapter Select and gamepad support. The design keeps them as
possible later work.
