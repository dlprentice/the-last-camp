using System;
using System.Threading.Tasks;
using Godot;

namespace LastCamp;

/// A quiet evening with a beginning and an end. Free exploration continues
/// after dawn; the journal is guidance, not a timer or a survival penalty.
public partial class CampSession : Node
{
    public bool FireTended { get; private set; }
    public bool LanternUsed { get; private set; }
    public bool MorningReached { get; private set; }
    public int EveningBest { get; private set; }
    public int BestSkips { get; private set; }
    public bool Resting { get; private set; }
    public bool ReadyForNight => FireTended && LanternUsed && EveningBest >= 3;
    public int Completed => (FireTended ? 1 : 0) + (LanternUsed ? 1 : 0) + (EveningBest >= 3 ? 1 : 0) + (MorningReached ? 1 : 0);
    private double _elapsed;
    private bool _welcomed;
    private Interactable _bed = null!;

    public override void _Ready()
    {
        Game.Instance.session = this;
        WorldController world = Game.Instance.world;
        world.hour = 17.4;
        world.cycle_hours_per_second = 1.0 / 90.0;
        world.cycle_running = true;
        using var save = new ConfigFile();
        if (save.Load("user://camp-records.cfg") == Error.Ok)
            BestSkips = Math.Clamp(save.GetValue("pond", "best_skips", 0).AsInt32(), 0, StoneFlight.MaxSkips);
        Game.Instance.camp.pond.best_skips = BestSkips;

        // A narrow trigger just inside the opening can be reached from outside,
        // without stealing interactions with the lantern or blocking the door.
        _bed = new Interactable { Name = "BedrollRest", CollisionLayer = 2, CollisionMask = 0,
            Position = new Vector3(-0.30f, 0.35f, -1.45f),
            prompt_text = "Rest until dusk", on_interact = Callable.From<Player>(Rest) };
        _bed.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.72f, 0.40f, 0.55f) } });
        Game.Instance.camp.campsite.tent.AddChild(_bed);
    }

    public override void _Process(double delta)
    {
        if (Game.Instance.mode != Game.Mode.PLAY || Resting) return;
        _elapsed += delta;
        if (!_welcomed && _elapsed > 7)
        {
            _welcomed = true;
            Game.Instance.hud?.notify("An evening of your own. J opens your camp journal.");
        }
        _bed.prompt_text = MorningReached ? "Rest at camp" : Game.Instance.world.hour < 20 ? "Rest until dusk" : "Turn in for the night";
        var weather = Game.Instance.world.weather;
        if (MorningReached) return;
        // Give the player several quiet minutes before clouds gather. These are
        // the same rain, surface wetness and thunder systems used by the world.
        if (_elapsed < 420) return;
        if (_elapsed < 480) weather.apply_chapter("gathering", _elapsed - 420, 60);
        else if (_elapsed < 522) weather.apply_chapter("storm", _elapsed - 480, 42);
        else if (_elapsed < 642) weather.apply_chapter("shelter_rain", (_elapsed - 522) % 30, 30);
        else weather.apply_chapter("clearing", _elapsed - 642, 120);
    }

    public void TendFire() => FireTended = true;
    public void UseLantern() => LanternUsed = true;

    public void RecordSkips(int skips)
    {
        EveningBest = Math.Max(EveningBest, skips);
        if (skips <= BestSkips) return;
        BestSkips = skips;
        using var save = new ConfigFile();
        save.SetValue("pond", "best_skips", BestSkips);
        Error result = save.Save("user://camp-records.cfg");
        if (result != Error.Ok) GD.PushWarning($"Could not save the stone-skipping record: {result}");
    }

    public string Journal()
    {
        static string Check(bool done) => done ? "✓" : "○";
        return $"{Check(FireTended)}  Feed the fire with a log from beside the tent.\n\n" +
            $"{Check(EveningBest >= 3)}  Make a stone skip at least three times.\n     Stones wait at the end of the pier. Aim low.\n\n" +
            $"{Check(LanternUsed)}  Light your hand lantern with L.\n\n" +
            $"{Check(MorningReached)}  Turn in at the tent when you are ready.\n\n" +
            $"Best this evening: {EveningBest}     Personal best: {BestSkips}\n\n" +
            (MorningReached ? "A new morning. Stay and explore as long as you like." : "No rush. The weather and the light will find their own pace.");
    }

    public async void Rest(Player player)
    {
        if (Resting) return;
        if (MorningReached)
        {
            player.resting = !player.resting;
            Game.Instance.hud?.notify("Resting at camp. Move to stand.");
            return;
        }
        if (!ReadyForNight)
        {
            Game.Instance.hud?.notify("A few things left for this evening. J opens your journal.");
            return;
        }
        bool morning = Game.Instance.world.hour >= 20 || Game.Instance.world.hour < 6;
        Resting = true;
        player.suspend();
        await Game.Instance.hud.FadeRest(true);
        if (!IsInsideTree()) return;
        Game.Instance.world.hour = morning ? 6.3 : 20.4;
        if (morning)
        {
            MorningReached = true;
            Game.Instance.world.weather.apply_chapter("morning", 0, 120, false);
            Game.Instance.camp.campsite.firepit.intensity = 0.32;
        }
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await Game.Instance.hud.FadeRest(false);
        if (!IsInsideTree()) return;
        player.begin();
        Resting = false;
        Game.Instance.hud?.notify(morning ? "Morning at the last camp. Your evening is complete." : "Dusk settles in. Explore, or turn in at the tent.");
    }

    public override void _ExitTree()
    {
        if (Game.Instance?.session == this) Game.Instance.session = null;
    }
}
