using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using LastCamp.Story;

namespace LastCamp;

/// Runs the story spine in the live world: it feeds the player's acts into
/// `StoryState`, drives the clock and the storm from the active band, fades
/// between bands and keeps the one story save. There is no fail state; the
/// journal is guidance, not a timer.
public partial class CampSession : Node
{
    public StoryState Story { get; private set; } = new();
    public StorySave Slot { get; private set; } = new();
    /// False in tool runs: they start a fresh story and never touch the player's save.
    public bool Saving { get; private set; }
    public bool Resting { get; private set; }
    public int BestSkips => Story.BestSkips;
    public int Completed => Story.Required(Story.Band).Count(Story.IsDone);
    public int Total => Story.Required(Story.Band).Count();
    public bool BandDone => Story.BandDone;

    /// One of June's lines should play now: (line id, entry or margin note, variant).
    /// A voice and subtitle player subscribes; the spine only decides when.
    public event Action<string, LineKind, string>? LinePlayed;
    /// A band (and possibly a chapter) has started, after any fade.
    public event Action<BandTransition>? BandStarted;

    private Interactable _bed = null!;
    private bool _inTent;
    private bool _atPierEnd;
    private double _safeTimer;
    private double _welcomeTimer;
    private bool _welcomed;

    public override void _Ready()
    {
        Game.Instance.session = this;
        Saving = !Game.Instance.is_tool_run();
        if (Saving)
        {
            StoryState? loaded = Slot.Load();
            if (loaded == null && Slot.LastError.Length > 0)
            {
                // The slot copied itself aside (or blocked saving) before anything saves.
                GD.PushWarning(Slot.LastError + (Slot.Rejected.Length > 0 ? $" (kept as {Slot.Rejected})" : " (could not be kept; saving is off)"));
                if (Slot.Blocked) Saving = false;
                Game.Instance.hud?.notify("The saved story could not be read. Starting fresh; the old file is kept.");
            }
            Story = loaded ?? new StoryState();
        }
        if (StorySave.MigrateRecords(Story)) GD.Print($"STORY_RECORDS migrated best_skips={Story.BestSkips}");
        Story.LineTriggered += OnLine;
        Game.Instance.camp.pond.best_skips = Story.BestSkips;

        // A narrow trigger just inside the opening can be reached from outside,
        // without stealing interactions with the lantern or blocking the door.
        _bed = new Interactable { Name = "BedrollRest", CollisionLayer = 2, CollisionMask = 0,
            Position = new Vector3(-0.30f, 0.35f, -1.45f),
            prompt_text = "Rest", on_interact = Callable.From<Player>(Rest) };
        _bed.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.72f, 0.40f, 0.55f) } });
        Game.Instance.camp.campsite.tent.AddChild(_bed);
        ApplyStoryToWorld();
        // Main hands control to the player right after this node is added, and the
        // first hand-over snaps to the terrain; the saved position goes on after it.
        Callable.From(RestorePosition).CallDeferred();
    }

    private void RestorePosition()
    {
        WorldSnapshot w = Story.World;
        Player player = Game.Instance.player;
        if (!w.HasPosition || player == null) return;
        player.GlobalPosition = new Vector3((float)w.X, (float)w.Y, (float)w.Z);
        player.Velocity = Vector3.Zero;
        player.yaw = w.Yaw;
    }

    /// Puts the clock, weather, fire and player where the story says.
    private void ApplyStoryToWorld()
    {
        WorldController world = Game.Instance.world;
        WorldSnapshot w = Story.World;
        world.hour = w.Clock;
        world.cycle_hours_per_second = Story.Band.ClockRate;
        world.cycle_running = true;
        Game.Instance.camp.campsite.firepit.intensity = w.FireIntensity;
        if (w.WeatherChapter.Length > 0)
            ApplyWeather(w.WeatherChapter, w.WeatherSeconds, Math.Max(w.WeatherDuration, 0.001), false);
    }

    /// Copies the live world into the story's snapshot before a save.
    private void CaptureWorld()
    {
        WorldSnapshot w = Story.World;
        WorldController world = Game.Instance.world;
        CampWeather weather = world.weather;
        w.Clock = world.hour;
        w.WeatherChapter = weather._chapter;
        w.WeatherSeconds = Math.Max(weather._previous_time, 0);
        w.WeatherDuration = _weatherDuration;
        w.Cloud = weather.cloud;
        w.Rain = weather.rain;
        w.Wet = weather.wet;
        w.FireIntensity = Math.Clamp(Game.Instance.camp.campsite.firepit.intensity, 0, Firepit.MAX_INTENSITY);
    }

    private double _weatherDuration = 1;

    private void ApplyWeather(string chapter, double seconds, double duration, bool sound = true)
    {
        _weatherDuration = duration;
        Game.Instance.world.weather.apply_chapter(chapter, seconds, duration, sound);
    }

    public override void _Process(double delta)
    {
        if (Game.Instance.mode != Game.Mode.PLAY || Resting) return;
        WorldController world = Game.Instance.world;
        if (!_welcomed && (_welcomeTimer += delta) > 7)
        {
            _welcomed = true;
            Game.Instance.hud?.notify("J opens your camp journal.");
        }
        double allowed = Story.AllowedClock(world.hour);
        if (!_sunsetRunning && world.hour > allowed && world.hour - allowed < 12) world.hour = allowed;
        world.cycle_hours_per_second = Story.Band.ClockRate;
        _bed.prompt_text = BedPrompt();

        Player player = Game.Instance.player;
        Firepit fire = Game.Instance.camp.campsite.firepit;
        bool inTent = player != null && CampWeather.tent_shelter(Game.Instance.camp.campsite.tent.ToLocal(player.GlobalPosition + Vector3.Up * 0.5f));
        if (inTent && !_inTent) Story.Raise(StoryEvents.TentEntered);
        _inTent = inTent;
        if (player != null)
        {
            Dock dock = Game.Instance.camp.campsite.dock;
            Vector3 end = dock.ToGlobal(new Vector3(0, 0, (float)(-dock.total_length() + 1.0)));
            bool atEnd = new Vector2(player.GlobalPosition.X - end.X, player.GlobalPosition.Z - end.Z).Length() < 1.6f;
            if (atEnd && !_atPierEnd) Story.Raise(StoryEvents.PlacePierEnd);
            _atPierEnd = atEnd;
            if ((_safeTimer += delta) > 1.0)
            {
                _safeTimer = 0;
                if (player.IsOnFloor() && Player.InsidePlayArea(player.GlobalPosition) && !player.in_water)
                {
                    Story.World.HasPosition = true;
                    Story.World.X = player.GlobalPosition.X;
                    Story.World.Y = player.GlobalPosition.Y;
                    Story.World.Z = player.GlobalPosition.Z;
                    Story.World.Yaw = player.yaw;
                }
            }
        }
        if (Story.Band.Storm && !Story.Finished)
        {
            WeatherPhase phase = Story.StormTick(delta, fire.intensity, inTent);
            ApplyWeather(phase.Chapter, phase.Seconds, phase.Duration);
            double rain = world.weather.rain;
            if (rain > 0 && fire.intensity > 0)
                fire.intensity = Math.Max(fire.intensity - StoryScript.RainFireDecay * rain * delta, 0);
        }
    }

    private string BedPrompt()
    {
        if (Story.EndActFor(StoryEvents.TurnedIn) != null) return "Turn in for the night";
        if (Story.EndActFor(StoryEvents.RestBedroll) != null) return Story.Band.Id == "evening" && Story.Chapter.Id == StoryScript.Arrival ? "Rest until dusk" : "Rest";
        return "Rest at camp";
    }

    private void OnLine(string id, LineKind kind, string variant)
    {
        GD.Print($"STORY_LINE id={id} kind={kind}{(variant.Length > 0 ? " variant=" + variant : "")}");
        LinePlayed?.Invoke(id, kind, variant);
    }

    // ---- Events from today's world -------------------------------------------------

    /// A log carried to the ring. In the night band of Arrival this is also the
    /// act that lights the fire; in the storm's rain it is feeding the fire in the rain.
    public void TendFire()
    {
        Story.Raise(StoryEvents.LogCarried);
        if (Story.Chapter.Id == StoryScript.Arrival && Story.Band.Id == "night") LightFire();
        if (Story.Band.Storm && Game.Instance.world.weather.rain > 0.3) Story.Raise(StoryEvents.FireFedInRain);
    }

    public void LightFire() => Story.Raise(StoryEvents.FireLit);

    public void UseLantern() => Story.Raise(StoryEvents.LanternLit);

    public void RecordSkips(int skips)
    {
        Story.Raise($"{StoryEvents.StoneSkips}:{skips}");
        if (skips == 0) Story.Raise(StoryEvents.StoneSunk);
        if (skips <= Story.BestSkips) return;
        Story.BestSkips = skips;
        Save();
    }

    /// Empty-handed rest by the fire: ends a band that ends by resting at the fire.
    public void RestByFire(Player player)
    {
        if (player.resting && Story.CanEndBy(StoryEvents.RestFire)) _ = EndBand(StoryEvents.RestFire, player);
    }

    /// Being in the tent through the rain counts as sheltering by itself; this
    /// is the hook for anything that shelters the player directly.
    public void ShelterFromStorm() => Story.Raise(StoryEvents.StormSheltered);

    // ---- Hooks for places and props that are not built yet ----------------------
    // Each raises its event; nothing in the world calls them until the thing exists.

    public void ReadLetter() { Story.AddItem("june_letter"); Story.Raise(StoryEvents.LetterRead); }
    public void ReachPlace(string place) => Story.Raise("place:" + place);
    public void ReachTrailhead() => ReachPlace("trailhead");
    public void SitAtPierEnd(Player player) => _ = Sit(StoryEvents.SatPier, player);
    /// Whether Tom's tin can be opened yet: only after the old fire ring. The tin's
    /// interaction should offer itself only while this is true.
    public bool TinAvailable => Story.TinAvailable;
    public bool OpenTin()
    {
        if (!Story.TinAvailable) return false;
        Story.AddItem("tally_book");
        Story.Raise(StoryEvents.TinOpened);
        return true;
    }
    public void FindBlaze() => Story.Raise(StoryEvents.BlazeFound);
    public void SitAtLookout(Player player) => _ = Sit(StoryEvents.SatLookout, player);
    public void LiftStep(Player player) { Story.AddItem("tom_letter"); Story.Raise(StoryEvents.StepLifted); _ = EndBand(StoryEvents.StepLifted, player); }
    public void StrikeTent() => Story.Raise(StoryEvents.TentStruck);
    public void RakeAsh() => Story.Raise(StoryEvents.AshRaked);
    public void ScatterStones() => Story.Raise(StoryEvents.StonesScattered);
    public void WalkOut(Player player) => _ = EndBand(StoryEvents.WalkedOut, player);
    public void FinishCredits() => Story.Raise(StoryEvents.CreditsFinished);

    /// Sitting: an objective where the band asks for it, the sunset sequence
    /// where the band ends by it.
    private async Task Sit(string ev, Player player)
    {
        Story.Raise(ev);
        if (ev == StoryEvents.SatLookout) await RunSunset(Story.Band.Limit);
        if (Story.CanEndBy(ev)) await EndBand(ev, player);
    }

    private bool _sunsetRunning;

    /// Runs the clock to `target` over the sunset sequence's 90 s.
    private async Task RunSunset(double target)
    {
        if (_sunsetRunning) return;
        _sunsetRunning = true;
        WorldController world = Game.Instance.world;
        double from = world.hour, elapsed = 0;
        bool running = world.cycle_running;
        world.cycle_running = false;
        while (elapsed < StoryScript.SunsetSeconds && IsInsideTree())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Game.Instance.mode != Game.Mode.PLAY) continue;
            elapsed += GetProcessDeltaTime();
            world.hour = Mathf.Lerp(from, Math.Max(from, target), Math.Min(elapsed / StoryScript.SunsetSeconds, 1.0));
            // The band now holds at the later time; the clock never goes back.
            Story.HoldAt(world.hour);
        }
        world.cycle_running = running;
        _sunsetRunning = false;
    }

    // ---- Band changes ---------------------------------------------------------------

    public async void Rest(Player player)
    {
        if (Resting) return;
        string act = Story.EndActFor(StoryEvents.TurnedIn) != null ? StoryEvents.TurnedIn : StoryEvents.RestBedroll;
        if (Story.EndActFor(act) == null)
        {
            player.resting = !player.resting;
            Game.Instance.hud?.notify("Resting at camp. Move to stand.");
            return;
        }
        if (!Story.BandDone)
        {
            Game.Instance.hud?.notify("A few things left first. J opens your journal.");
            return;
        }
        await EndBand(act, player);
    }

    /// Ends the active band by `act` if its objectives are done: the sunset
    /// sequence or a fade through black, the clock jump, then an autosave.
    public async Task<bool> EndBand(string act, Player? player)
    {
        if (_ending || Resting || !Story.CanEndBy(act)) return false;
        EndAct endAct = Story.EndActFor(act)!;
        _ending = true;
        try
        {
            Band next = NextBand() ?? Story.Band;
            // The sunset is play: the pause menu and the journal still open, and
            // pausing pauses the sunset. Only the fade itself blocks input.
            if (endAct.Sunset) await RunSunset(next.Start);
            if (!IsInsideTree() || !Story.CanEndBy(act)) return false;
            if (endAct.Fade)
            {
                Resting = true;
                player?.suspend();
                await Game.Instance.hud.FadeRest(true);
                if (!IsInsideTree()) return false;
            }
            BandTransition transition = Story.Advance(act)!;
            EnterBand(transition, endAct.Fade);
            if (endAct.Fade)
            {
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                await Game.Instance.hud.FadeRest(false);
                if (!IsInsideTree()) return false;
                player?.begin();
            }
            Save();
            BandStarted?.Invoke(transition);
            Game.Instance.hud?.notify(transition.Finished ? "The camp is quiet. Stay as long as you like."
                : transition.ChapterChanged ? $"{transition.ToChapter!.Title}. {transition.ToBand!.Title}." : $"{transition.ToBand!.Title}.");
            return true;
        }
        finally
        {
            Resting = false;
            _ending = false;
        }
    }

    private bool _ending;

    private Band? NextBand()
    {
        if (Story.BandIndex + 1 < Story.Chapter.Bands.Length) return Story.Chapter.Bands[Story.BandIndex + 1];
        int chapter = Story.ChapterIndex + 1;
        return chapter < StoryScript.Chapters.Length ? StoryScript.Chapters[chapter].Bands[0] : null;
    }

    private void EnterBand(BandTransition transition, bool jumpClock)
    {
        if (transition.ToBand is not Band band) return;
        WorldController world = Game.Instance.world;
        // Forward only: a new chapter is a new day; within a day the clock never goes back.
        bool later = band.Start > world.hour && band.Start - world.hour < 12;
        if (later || jumpClock && transition.ChapterChanged) world.hour = band.Start;
        world.cycle_hours_per_second = band.ClockRate;
        world.cycle_running = true;
        Firepit fire = Game.Instance.camp.campsite.firepit;
        if (transition.ChapterChanged)
        {
            bool afterStorm = Story.StormOutcome.Length > 0;
            // A new day: the night's fire is embers by morning, or cold after a lost storm.
            fire.intensity = Story.StormOutcome == "went_out" ? 0.0 : 0.32;
            ApplyWeather(afterStorm ? "dawn" : "morning", 0, 120, false);
        }
        if (Game.Instance.player?.lantern_on == true) Story.Raise(StoryEvents.LanternLit);
    }

    // ---- Saving ---------------------------------------------------------------------

    /// Writes the story slot. A failed write keeps the old slot and is reported.
    public bool Save()
    {
        if (!Saving) return false;
        CaptureWorld();
        if (Slot.Save(Story)) return true;
        GD.PushWarning(Slot.LastError);
        Game.Instance.hud?.notify("The story could not be saved. Your last save is kept.");
        return false;
    }

    /// "Save and quit": saves, then shuts down through the engine.
    public void SaveAndQuit()
    {
        Save();
        Game.Instance.quit_cleanly();
    }

    public string Journal()
    {
        var text = new StringBuilder();
        text.Append($"{Story.Chapter.Title}  ·  {Story.Band.Title}\n\n");
        foreach (Objective objective in Story.Required(Story.Band))
        {
            string count = objective.Count > 1 ? $"  ({Story.ProgressOf(objective)}/{objective.Count})" : "";
            text.Append($"{(Story.IsDone(objective) ? "✓" : "○")}  {objective.Journal}{count}\n\n");
        }
        if (Story.Finished) text.Append("The camp is left the way you found it.\n\n");
        else if (Story.BandDone) text.Append("When you are ready, " + EndHint() + ".\n\n");
        text.Append($"Personal best: {Story.BestSkips} skips\n\n");
        text.Append("No rush. The weather and the light will find their own pace.");
        return text.ToString();
    }

    private string EndHint()
    {
        EndAct? act = Story.EndActs.FirstOrDefault();
        return act?.Event switch
        {
            StoryEvents.TurnedIn => "turn in at the tent",
            StoryEvents.RestBedroll => "rest at the tent",
            StoryEvents.RestFire => "rest by the fire",
            StoryEvents.SatPier => "sit at the end of the pier",
            StoryEvents.WalkedOut => "walk out past the trailhead post",
            _ => "move on",
        };
    }

    public override void _ExitTree()
    {
        Story.LineTriggered -= OnLine;
        if (Game.Instance?.session == this) Game.Instance.session = null;
    }
}
