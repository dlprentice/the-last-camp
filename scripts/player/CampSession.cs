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
        var shape_64 = new BoxShape3D { Size = new Vector3(0.72f, 0.40f, 0.55f) };
        _bed.AddChild(new CollisionShape3D { Shape = shape_64 });
        GC.KeepAlive(shape_64);
        Game.Instance.camp.campsite.tent.AddChild(_bed);
        Reading = new ReadingSheet { Name = "Reading" };
        AddChild(Reading);
        WireStoryProps();
        ApplyStoryToWorld();
        // Main hands control to the player right after this node is added, and the
        // first hand-over snaps to the terrain; the saved position goes on after it.
        Callable.From(RestorePosition).CallDeferred();
    }

    /// The sheet the story's texts are read on.
    public ReadingSheet Reading { get; private set; } = null!;
    public Interactable PierSeat { get; private set; } = null!;
    public Interactable? BenchSeat { get; private set; }
    public Interactable? StepLift { get; private set; }

    /// Connects the world's story props to the story, and adds the seats and the
    /// step's lift: plain colliders on the interaction layer, nothing visible.
    private void WireStoryProps()
    {
        Campsite site = Game.Instance.camp.campsite;
        site.kitchen.letter_body.on_interact = Callable.From<Player>(p => ReadLetter(p));
        site.tent.tin_body.on_interact = Callable.From<Player>(p => OpenTin(p));
        site.tent.strike_body.on_interact = Callable.From<Player>(p => StrikeTent(p));
        // Restoring a saved story puts back what was already done.
        if (Story.Facts.Contains(StoryEvents.LetterRead)) site.kitchen.take_letter();
        if (Story.Facts.Contains(StoryEvents.TinOpened)) site.tent.open_tin();
        if (Story.Facts.Contains(StoryEvents.TentStruck)) site.tent.restore_struck();
        bool ashRaked = Story.Facts.Contains(StoryEvents.AshRaked);
        bool stonesScattered = Story.Facts.Contains(StoryEvents.StonesScattered);
        if (ashRaked || stonesScattered)
            site.firepit.restore_epilogue(ashRaked, Story.StormOutcome != "went_out", stonesScattered);

        Dock dock = site.dock;
        PierSeat = new Interactable { Name = "PierEndSeat", CollisionLayer = 1u << 1, CollisionMask = 0,
            Position = new Vector3(0, 0.18f, (float)(-dock.total_length() + 0.25)), prompt_text = "Sit",
            on_interact = Callable.From<Player>(SitAtPierEnd) };
        PierSeat.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.3f, 0.4f) } });
        dock.AddChild(PierSeat);

        Places? places = Game.Instance.camp.places;
        if (places?.rock == null) return;
        places.Arrived += ReachPlace;
        places.LooseStepPressed += OnLooseStep;
        TomsRock rock = places.rock;
        Node3D bench = rock.bench_body.GetParent<Node3D>();
        BenchSeat = new Interactable { Name = "BenchSeat", CollisionLayer = 1u << 1, CollisionMask = 0,
            Position = new Vector3(0, (float)PlacesLayout.BENCH_SEAT_HEIGHT + 0.08f, 0), prompt_text = "Sit",
            on_interact = Callable.From<Player>(SitAtLookout) };
        BenchSeat.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(TomsRock.BENCH_SEAT.X, 0.16f, TomsRock.BENCH_SEAT.Z) } });
        bench.AddChild(BenchSeat);
        StepLift = new Interactable { Name = "LooseStepLift", CollisionLayer = 0, CollisionMask = 0, Transform = rock.loose_rest, prompt_text = "Lift",
            on_interact = Callable.From<Player>(p => LiftStep(p)) };
        StepLift.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = TomsRock.LOOSE_SIZE + new Vector3(0.06f, 0.08f, 0.06f) } });
        rock.AddChild(StepLift);
        if (Story.Facts.Contains(StoryEvents.StepLifted)) { places.LiftStep(); if (places.Tin != null) places.Tin.Visible = false; }
    }

    /// The loose step knocks hollow when it takes the player's weight.
    private void OnLooseStep() => Game.Instance.audio?.footstep("hollow", false);

    private void OnRingNotch(Player player)
    {
        Places? places = Game.Instance.camp.places;
        if (places?.ring == null || !NotchCounts(Story)) { _notchInView = false; return; }
        bool inView = NotchInView(player.camera.GlobalPosition, -player.camera.GlobalBasis.Z, places.ring.notch_world, places.ring.notch_face);
        if (inView && !_notchInView) FindBlaze();
        _notchInView = inView;
    }

    /// Looking at the notch: closer than `NotchFindDistance`, near the centre of
    /// view, from the side its cut faces.
    public static bool NotchInView(Vector3 eye, Vector3 forward, Vector3 notch, Vector3 face)
    {
        Vector3 to = notch - eye;
        float d = to.Length();
        return d < NotchFindDistance && d > 0.05f
            && Mathf.RadToDeg(forward.AngleTo(to)) < NotchFindConeDeg
            && face.Dot(-to / d) > 0.1f;
    }

    private bool _notchInView;
    /// Tom's notch is found by looking at it from closer than this, near the centre of view.
    public const float NotchFindDistance = 2.0f;
    public const float NotchFindConeDeg = 25.0f;

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
        Campsite site = Game.Instance.camp.campsite;
        Firepit fire = site.firepit;
        site.tent.offer_tin(Story.TinAvailable && !site.tent.struck);
        site.tent.offer_strike(Epilogue);
        if (StepLift != null) StepLift.CollisionLayer = LiftOffered ? 1u << 1 : 0;
        if (player != null)
        {
            OnRingNotch(player);
            Vector2 feet = new(player.GlobalPosition.X, player.GlobalPosition.Z);
            if (Epilogue && Story.BandDone && feet.DistanceTo(WalkOutPoint) < WalkOutRadius) WalkOut(player);
            if (_shelterRest && !player.resting) _shelterRest = false;
        }
        // Resting in the tent through the rain is sheltering; once the storm has
        // passed and the night's asks are done, it is turning in.
        bool restingInTent = _shelterRest && player != null && player.GlobalPosition.DistanceTo(_bed.GlobalPosition) < 3.6f;
        if (restingInTent && Story.Sheltered && Story.CanEndBy(StoryEvents.TurnedIn) && !_ending) _ = EndBand(StoryEvents.TurnedIn, player);
        // A struck tent is gone: its bare pitch is not inside anything.
        bool inTent = player != null && !site.tent.struck && (restingInTent || CampWeather.tent_shelter(site.tent.ToLocal(player.GlobalPosition + Vector3.Up * 0.5f)));
        if (inTent && !_inTent)
        {
            Game.Instance.audio?.play_interact(Sounds.TentEnter);
            Story.Raise(StoryEvents.TentEntered);
        }
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

    private bool _shelterRest;

    private string BedPrompt()
    {
        if (Story.Band.Storm && !Story.Finished && !Story.Sheltered) return "Rest";
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
        // Once sheltered the rain is clearing; a log still counts, so the storm can
        // always be finished.
        if (Story.Band.Storm && (Game.Instance.world.weather.rain > 0.3 || Story.Sheltered))
        {
            Story.Raise(StoryEvents.FireFedInRain);
            Game.Instance.audio?.fire_hiss();
        }
    }

    public void LightFire()
    {
        // The match is struck once; later logs that night only feed the fire.
        if (!Story.Facts.Contains(StoryEvents.FireLit)) Sound(Sounds.FireLight);
        Story.Raise(StoryEvents.FireLit);
    }

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

    // ---- The story's places and props --------------------------------------------
    // Each is a real interaction in the world: the props carry the meshes and the
    // colliders, the session decides when each offers itself and what it raises.

    /// June's letter on the camp table: the sheet comes up to read and she begins.
    public async void ReadLetter(Player? player)
    {
        if (Reading.IsOpen) return;
        Sound(Sounds.LetterUnfold);
        Game.Instance.camp?.campsite?.kitchen?.take_letter();
        Story.AddItem("june_letter");
        Story.Raise(StoryEvents.LetterRead);
        await Reading.Read(StoryTexts.JuneLetter, player, june: true);
    }

    public void ReachPlace(string place) => Story.Raise("place:" + place);
    public void ReachTrailhead() => ReachPlace("trailhead");

    /// Sitting at the end of the pier. On the first evening, once its objectives
    /// are done, it is the sunset sequence into the night; otherwise just a sit.
    public void SitAtPierEnd(Player player)
    {
        SeatPlayer(player, -Game.Instance.camp.campsite.dock.GlobalBasis.Z);
        if (Story.Chapter.Id == StoryScript.Arrival && Story.Band.Id == "evening")
        {
            _ = Sit(StoryEvents.SatPier, player);
            if (!Story.BandDone) Game.Instance.hud?.notify("A few things left first. J opens your journal.");
        }
        else Sound(Sounds.SitDown);
    }

    /// Whether Tom's tin can be opened yet: only after the old fire ring. The tin's
    /// interaction offers itself only while this is true.
    public bool TinAvailable => Story.TinAvailable;

    /// The tin's lid, then the pages of Tom's tally book. Refused (false, nothing
    /// raised) before the old ring. Once open it can be read again.
    public bool OpenTin(Player? player = null)
    {
        if (!Story.TinAvailable || Reading.IsOpen) return false;
        Tent tent = Game.Instance.camp.campsite.tent;
        if (tent.tin_open)
        {
            Sound(Sounds.PageTurn);
            _ = Reading.Read(StoryTexts.TallyBook, player, june: false, StoryTexts.TallySketchPage);
            return true;
        }
        Sound(Sounds.TinOpen);
        tent.open_tin();
        Story.AddItem("tally_book");
        Story.Raise(StoryEvents.TinOpened);
        _ = ReadAfter(StoryTexts.TallyBook, player, 0.8, Sounds.PageTurn);
        return true;
    }

    private async Task ReadAfter(string[] pages, Player? player, double seconds, string sound)
    {
        player?.suspend();
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree()) return;
        Sound(sound);
        await Reading.Read(pages, player, june: false, StoryTexts.TallySketchPage);
    }

    /// Tom's first notch, seen up close: counts only once the tally book has
    /// shown what it is. Before the tin is open it raises nothing at all.
    public bool FindBlaze()
    {
        if (!NotchCounts(Story)) return false;
        Story.Raise(StoryEvents.BlazeFound);
        return true;
    }

    /// Sitting on Tom's bench. On the climb's evening it is the lookout: June
    /// speaks again and the sun goes down; any other time it is just a sit.
    public void SitAtLookout(Player player)
    {
        SeatPlayer(player, new Vector3(PlacesLayout.FACE.X, 0, PlacesLayout.FACE.Y));
        Game.Instance.camp.places?.SitOnBench();
        Objective? lookout = Story.Band.Objectives.FirstOrDefault(o => o.Event == StoryEvents.SatLookout);
        if (lookout != null && !Story.IsDone(lookout)) _ = Sit(StoryEvents.SatLookout, player);
        else Sound(Sounds.SitDown);
    }

    /// The loose step offers "Lift" only after the player has stood on it, and only
    /// when lifting it is the evening's last act (the notch and the bench done).
    public bool LiftOffered
    {
        get
        {
            Places? places = Game.Instance.camp?.places;
            return places != null && !_lifting && LiftAllowed(Story, places.loose_step_stood_on, places.StepIsLifted);
        }
    }

    /// The loose step's rule: never before the player has stood on it, never once
    /// lifted, and only on the climb's evening with the notch and the bench done.
    public static bool LiftAllowed(StoryState story, bool stoodOn, bool lifted)
    {
        if (!stoodOn || lifted) return false;
        if (story.Finished || story.Chapter.Id != StoryScript.Marks || story.Band.Id != "evening") return false;
        return story.Required(story.Band).Where(o => o.Event != StoryEvents.StepLifted).All(story.IsDone);
    }

    /// Tom's notch means something only once his tally book has been read.
    public static bool NotchCounts(StoryState story) => story.Facts.Contains(StoryEvents.TinOpened);

    private bool _lifting;

    /// The stone set aside, then the small tin pulled from the soil under it and
    /// Tom's letter read. Refused (false) while the step does not offer itself.
    public bool LiftStep(Player player)
    {
        if (!LiftOffered) return false;
        _ = LiftAndRead(player);
        return true;
    }

    private async Task LiftAndRead(Player player)
    {
        _lifting = true;
        Places places = Game.Instance.camp.places;
        player.suspend();
        Sound(Sounds.StepLift);
        places.LiftStep();
        await ToSignal(GetTree().CreateTimer(TomsRock.LIFT_SECONDS + 0.2), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree()) return;
        Sound(Sounds.TinLift);
        if (places.Tin != null) places.Tin.Visible = false;
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree()) return;
        await Reading.Read(StoryTexts.TomLetter, player, june: false);
        Story.AddItem("tom_letter");
        Story.Raise(StoryEvents.StepLifted);
        _lifting = false;
        await EndBand(StoryEvents.StepLifted, player);
    }

    /// The last morning's acts, offered only in the epilogue.
    private bool Epilogue => !Story.Finished && Story.Chapter.Id == StoryScript.Epilogue;

    /// The pegs come out, then the canvas falls and the tent is packed away.
    public bool StrikeTent(Player? player = null)
    {
        Tent tent = Game.Instance.camp.campsite.tent;
        if (!Epilogue || tent.struck) return false;
        Sound(Sounds.TentPeg);
        _ = StrikeAndPack(tent);
        return true;
    }

    private async Task StrikeAndPack(Tent tent)
    {
        await tent.strike(pegs_out: () => Sound(Sounds.TentFall));
        Story.Raise(StoryEvents.TentStruck);
    }

    /// What the camp fire offers on the last morning: rake the ash, then move the
    /// ring's stones. Null when it offers nothing of its own.
    public string? FireAct()
    {
        if (!Epilogue) return null;
        Firepit fire = Game.Instance.camp.campsite.firepit;
        if (!fire.ash_raked) return "Rake";
        if (!fire.stones_scattered) return "Move";
        return null;
    }

    /// Runs the fire's act; false when there is none (the fire's own interaction runs).
    public bool DoFireAct(Player player)
    {
        string? act = FireAct();
        if (act == "Rake") return RakeAsh();
        if (act == "Move") return ScatterStones();
        return false;
    }

    /// Rakes out the ash: still warm if the fire survived the storm, cold and wet if not.
    public bool RakeAsh()
    {
        Firepit fire = Game.Instance.camp.campsite.firepit;
        if (!Epilogue || fire.ash_raked) return false;
        Sound(Sounds.AshRake);
        fire.rake_ash(Story.StormOutcome != "went_out");
        Story.Raise(StoryEvents.AshRaked);
        return true;
    }

    /// Stones moved by one "Move": the ring is scattered in three.
    public const int StonesPerMove = 5;

    /// Lifts the next few ring stones out and sets them down beyond the ring.
    /// Refused before the ash is raked.
    public bool ScatterStones()
    {
        Firepit fire = Game.Instance.camp.campsite.firepit;
        if (!Epilogue || !fire.ash_raked || fire.stones_scattered) return false;
        int moved = fire.scatter_stones(StonesPerMove);
        for (int i = 0; i < moved; i++)
            SoundLater(Sounds.RingStone, i * Firepit.STONE_STAGGER + Firepit.STONE_SECONDS * 0.9);
        if (fire.stones_scattered) Story.Raise(StoryEvents.StonesScattered);
        return moved > 0;
    }

    /// Where the walk out counts: on the trail itself, just past the trailhead post
    /// (which stands beside the trail), away from camp.
    public static Vector2 WalkOutPoint
    {
        get
        {
            Vector2 away = (TerrainField.TRAIL[0] - TerrainField.TRAIL[1]).Normalized();
            return TerrainField.TRAIL[0] + away * 1.5f;
        }
    }
    public const float WalkOutRadius = 2.5f;

    /// Puts the player in a seated pose looking along `facing`.
    private static void SeatPlayer(Player player, Vector3 facing)
    {
        player.resting = true;
        player.Velocity = Vector3.Zero;
        if (facing.LengthSquared() > 1e-4) player.yaw = Math.Atan2(-facing.X, -facing.Z);
        player.pitch = -0.08;
    }

    private static void Sound(string id) => Game.Instance.audio?.play_interact(id);

    private void SoundLater(string id, double seconds)
    {
        if (!IsInsideTree()) return;
        GetTree().CreateTimer(seconds).Timeout += () => Sound(id);
    }
    /// Walking out past the trailhead post ends the story once the last morning's
    /// acts are done; then the closing sequence, and the postcard after it.
    public async void WalkOut(Player player)
    {
        if (!await EndBand(StoryEvents.WalkedOut, player)) return;
        RunEnding();
    }

    public bool EndingRunning { get; private set; }

    /// The HUD's closing sequence (the shot, the credits); `credits_finished` when it ends.
    /// The HUD's closing sequence: the credits roll, then June's postcard, which
    /// raises `credits_finished` through `FinishCredits` as it begins.
    private void RunEnding()
    {
        if (EndingRunning || Story.Facts.Contains(StoryEvents.CreditsFinished)) return;
        EndingRunning = true;
        bool fireKept = Story.StormOutcome != "went_out";
        GD.Print($"STORY_ENDING fire_kept={fireKept}");
        Game.Instance.hud?.ShowEnding(fireKept);
    }

    public void FinishCredits() => Story.Raise(StoryEvents.CreditsFinished);

    /// Sitting: an objective where the band asks for it, the sunset sequence
    /// where the band ends by it.
    private async Task Sit(string ev, Player player)
    {
        Sound(Sounds.SitDown);
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
        // In the storm, resting at the bedroll is sheltering: the player crawls in
        // out of the rain and stays until it passes (moving gets them up again).
        if (Story.Band.Storm && !Story.Finished && (!Story.Sheltered || !Story.BandDone))
        {
            player.resting = true;
            player.Velocity = Vector3.Zero;
            _shelterRest = true;
            Game.Instance.hud?.notify(Story.Sheltered ? "A few things left first. J opens your journal." : "In out of the rain. Move to get up.");
            if (Story.Sheltered) { player.resting = false; _shelterRest = false; }
            return;
        }
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
            if (transition.ChapterChanged) Sound(Sounds.CardIn);
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
        Places? places = Game.Instance?.camp?.places;
        if (places != null)
        {
            places.Arrived -= ReachPlace;
            places.LooseStepPressed -= OnLooseStep;
        }
        if (Game.Instance?.session == this) Game.Instance.session = null;
    }
}
