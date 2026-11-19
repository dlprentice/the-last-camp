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

- **The tackle tin (day 2 morning).** In June's tent is Tom's tin. Under his flat
  skipping stones is his 2014 tally book: heron counts, then in the margins
  "steps: 6 of 20", "steps: 14 of 20", and a route sketch from the old fire ring.
- **The old fire ring.** The first place June and Tom camped. On the fallen log seat
  is Tom's first notched blaze, the start of his route.
- **Tom's Rock.** His blazes lead up twenty stone steps he set himself to a stone
  bench facing the pond and the sunset. The twentieth step is loose; under it is a
  tin with his letter for their 40th anniversary, which he never gave her. You read
  it on screen and carry it home.

## What the player does

### Shell

- [ ] The title screen appears within 15 s of launch, over the camp at dusk, with
  Continue (showing chapter and time), New Game (asks before overwriting), Settings,
  Credits and Quit.
- [ ] Esc pauses: Resume, Journal, Settings, Save and quit to title, Quit to desktop.
- [ ] Settings (preset, field of view, head-bob, master, ambience, effects and voice
  volumes, subtitles on or off, mouse sensitivity, invert Y) take effect at once and
  persist across a restart.
- [ ] One story save. It autosaves on sleeping, at each chapter change and on "Save and
  quit". Quitting and choosing Continue resumes at the same chapter, objectives,
  clock, weather and fire.
- [ ] A chapter title card opens each chapter.

### Chapter 1: Arrival (evening and night)

- [ ] You walk in from the trailhead post at 18:45 and read June's letter on the camp
  table. Entry 1 plays.
- [ ] You carry three logs to the ring and skip a stone three times. Three skips plays
  entry 2.
- [ ] You sit at the end of the pier; the sun goes down over about 90 s.
- [ ] After the first star, you light the fire (entry 3) and your
  lantern, then turn in at the tent.

### Chapter 2: The Marks (day 2 and the storm night)

- [ ] Morning: you open Tom's tin in the tent and read his tally book on screen.
- [ ] You follow the footpath to the old fire ring. Entry 4 plays,
  and you find Tom's first blaze.
- [ ] Evening: you follow Tom's blazes and steps to Tom's Rock, sit on his bench for the
  sunset (entry 5), lift the loose step and read his letter. Nothing
  marks the step: it sits a little wrong and sounds hollow underfoot, and only then
  offers the interaction.
- [ ] You walk down by lantern. The wind turns and the storm comes in: you feed the fire
  twice in the rain and shelter in the tent until it passes (entry 6). Keeping the fire alive is a goal, not a requirement; if it goes out, the next
  morning and one line of June's postcard are different.

### Epilogue: Leave No Trace (dawn)

- [ ] You strike the tent, then rake out the ash (still warm if the fire survived the
  storm, cold and wet if not) and scatter the ring stones. June's last note plays, and
  no other voice is heard until the credits. You walk out past the trailhead post.
- [ ] A closing camera shot over the clearing, the credits (every credited asset), and
  June's postcard (entry 7), her answer to Tom's letter, which fits either storm
  outcome, then the title. There is no fail screen anywhere.

### Throughout

- [ ] First-person hands hold the log, the stone, the lantern, the tin and the letter.
  No item floats.
- [ ] June is voiced: seven entries and nine margin notes, all subtitled, as written in
  [the script](script.md). June falls silent once Tom's tin is open, until the lookout. Without
  the licensed voice files (a public clone), the game plays with subtitles only.
- [ ] You can never get stuck. Fall recovery works, and the pause menu has "Return to
  camp".

## Quality bars

- [ ] **Length**: a first-time bot playthrough at normal clock rates takes 25 to 35
  minutes of play, measured.
- [ ] **Look**: close-up grass reads as grass, not dark straps. Wet night scenes show a
  fire that lights the clearing, not a murky wash, and wet ground without blotches.
  Judged at 1:1 in fresh offscreen footage and full-size stills.
- [ ] **Frame rate** (exported build, 1920×1080, the benchmark route): High holds 60 FPS
  if it can be done without a visible loss. Every lever's cost and saving is reported.
  No frame over 100 ms in a 30-minute soak.
- [ ] **Sound**: gameplay mix about −16 LUFS integrated, true peak at or below −1 dBTP;
  voice lines levelled alike. A 2-minute audition reel is ready for David.
- [ ] **Stability**: no core dump and no NVIDIA Xid in any acceptance run.
- [ ] **Checks**: `dotnet build` with zero warnings; the C# and Python suites pass; a bot
  playthrough from New Game to the credits passes through the real player controller.
- [ ] **Credits**: every external asset is listed with its source, licence and date.
  Licensed audio is not in Git; the repository keeps a manifest of it.

## Not in this cut

The canoe and the heron blind, fishing, splitting, foraging and cooking, the spring,
the birch grove, June's camera and photograph captions, the tarp, the last-page
choice, Free Play, Chapter Select and gamepad support. The design keeps them as
possible later work.
