#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using GdRuntime;
using static GdRuntime.G;

namespace LastCamp;

/// The game's sound, played from the ElevenLabs bank in `audio/generated/`.
///
/// Every file is normalised from its measured loudness (the manifest's LUFS) to its
/// category's target in [constant TARGET_LUFS], so the mix is set by category, not by
/// how loud each take came out. The director owns the buses, the beds (time of day,
/// wind, rain, the fire and the pond), footsteps, wildlife, weather one-shots and the
/// action sounds, and smooths every level change per frame. A sound whose file is
/// missing plays nothing: a public clone without the licensed files is silent.
///
/// Children created by [method setup]: Fire, FireHiss, PondLap0..N, StoneImpact0..2
/// (3D, world space); DayWoods, Evening, Night, WindCanopy, Rain, RainCanvas,
/// StormWind, Wading, WaterIn, WaterOut, Wildlife0..3, Footstep0..3, Interact0..2,
/// Thunder0..1, Interface0..1.
public partial class AudioDirector : Node
{
    public static readonly StringName BUS_MASTER = "Master";
    public static readonly StringName BUS_AMBIENCE = "Ambience";
    public static readonly StringName BUS_SFX = "SFX";
    public static readonly StringName BUS_WATER_FOLEY = "WaterFoley";
    public static readonly StringName BUS_VOICE = "Voice";

    /// Integrated loudness each category is normalised to (LUFS; 3D sounds at their
    /// unit distance). Ambience is the floor of the mix; June's voice sits on top and
    /// ducks the ambience by [constant VOICE_DUCK_DB]. Together they put the gameplay
    /// mix near -16 LUFS integrated; the bus limiters hold peaks at the ceiling.
    public static readonly IReadOnlyDictionary<string, double> TARGET_LUFS = new Dictionary<string, double>
    {
        ["ambience"] = -19.0, // the time-of-day beds
        ["wind"] = -22.0,     // canopy wind at full strength
        ["water"] = -24.0,    // pond lap at the pier
        ["fire"] = -19.0,     // the campfire bed at its unit distance
        ["rain"] = -19.0,     // rain, rain on canvas, storm wind
        ["wading"] = -22.0,
        ["footsteps"] = -22.0,
        ["wildlife"] = -27.0, // birds, owl, loon: distant
        ["thunder"] = -17.0,
        ["fire_hiss"] = -25.0,
        ["water_foley"] = -20.0,
        ["actions"] = -20.0,
        ["ui"] = -27.0,
        ["story"] = -25.0,
        ["voice"] = -16.0,
    };

    public const double VOICE_DUCK_DB = -6.0;
    /// The bus limiters are a safety net only: files are levelled offline and no
    /// single sound may play above [constant PEAK_CEILING_DBTP].
    public const double LIMITER_CEILING_DB = -1.0;
    /// No file plays above this true peak, whatever gain the mix asks for.
    public const double PEAK_CEILING_DBTP = -1.0;
    public const double SILENT_DB = -80.0;
    public const double START_FADE_SECONDS = 2.5;
    public const long SCHEDULER_SEED = 7;
    public const long FOOTSTEP_POOL_SIZE = 4;
    public const long INTERACT_POOL_SIZE = 3;
    public const long WILDLIFE_POOL_SIZE = 4;
    public const long THUNDER_POOL_SIZE = 2;
    public const long IMPACT_POOL_SIZE = 3;

    /// Small trims on top of the footstep category, so grass stays softer than planks.
    public static readonly IReadOnlyDictionary<string, double> FOOTSTEP_TRIM_DB = new Dictionary<string, double>
    {
        ["grass"] = -2.0, ["dirt"] = 0.0, ["wood"] = 1.0, ["rock"] = 0.0, ["water"] = 0.0, ["hollow"] = 2.0,
    };

    public static readonly IReadOnlyDictionary<string, string> CATEGORY = new Dictionary<string, string>
    {
        [Sounds.BedDayWoods] = "ambience", [Sounds.BedEvening] = "ambience", [Sounds.BedNight] = "ambience",
        [Sounds.BedWindCanopy] = "wind", [Sounds.BedPondLap] = "water", [Sounds.BedFire] = "fire",
        [Sounds.BedRain] = "rain", [Sounds.BedRainCanvas] = "rain", [Sounds.BedStormWind] = "rain",
        [Sounds.BedWading] = "wading", [Sounds.BirdsDay] = "wildlife", [Sounds.Owl] = "wildlife",
        [Sounds.Loon] = "wildlife", [Sounds.Thunder] = "thunder", [Sounds.FireHiss] = "fire_hiss",
        [Sounds.WaterIn] = "water_foley", [Sounds.WaterOut] = "water_foley",
        [Sounds.UiMove] = "ui", [Sounds.UiSelect] = "ui", [Sounds.JournalOpen] = "ui", [Sounds.JournalClose] = "ui",
        [Sounds.CardIn] = "story",
    };

    public static string category_of(string id)
    {
        if (CATEGORY.TryGetValue(id, out string category)) return category;
        return id.StartsWith("step_", StringComparison.Ordinal) ? "footsteps" : "actions";
    }

    /// The trim that plays one file of `id` at its category's target. The files are
    /// levelled offline (tools/prepare_audio.py), so this is small.
    public double level_db(string id, int variant) => bank.GainDb(id, variant, TARGET_LUFS[category_of(id)]);

    /// The level to play one file at: its trim plus `extra_db` (jitter, fades,
    /// intensity), capped so the file's true peak never passes [constant PEAK_CEILING_DBTP].
    public double play_db(string id, int variant, double extra_db = 0.0) =>
        Math.Min(level_db(id, variant) + extra_db, PEAK_CEILING_DBTP - bank.TruePeak(id, variant));

    /// First-order smoother for levels and pitch; avoids zipper noise and hard cuts.
    public partial class SmoothedValue : RefCounted
    {
        public double value;
        public double target;
        public double time_constant;

        public SmoothedValue(double initial, double smoothing_seconds)
        {
            value = initial;
            target = initial;
            time_constant = maxf(smoothing_seconds, 0.001);
        }

        public SmoothedValue()
        {
        }

        public double step(double delta)
        {
            value = lerpf(value, target, 1.0 - exp(-delta / time_constant));
            return value;
        }

        public void snap()
        {
            value = target;
        }
    }

    /// Countdown for randomly scheduled one-shots.
    public sealed class Countdown
    {
        public double remaining = 0.0;

        public bool tick(double delta)
        {
            remaining -= delta;
            return remaining <= 0.0;
        }

        public void reset(double seconds)
        {
            remaining = maxf(seconds, 0.0);
        }
    }

    /// Round-robin voice allocation so overlapping one-shots never cut each other off.
    public sealed class Pool<T> where T : Node
    {
        public readonly List<T> players = new List<T>();
        public int _cursor = 0;

        public T next()
        {
            T player = players[_cursor];
            _cursor = (_cursor + 1) % players.Count;
            return player;
        }
    }

    /// One looping bed: its player, sound id and smoothed amplitude (0..1).
    public sealed class Bed
    {
        public Node player;
        public string id;
        public SmoothedValue amp;
        public double trim_db;

        public void set_level(double db)
        {
            if (player is AudioStreamPlayer flat) flat.VolumeDb = (float)db;
            else if (player is AudioStreamPlayer3D spatial) spatial.VolumeDb = (float)db;
        }
    }

    public SoundBank bank = new SoundBank();
    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public bool _is_setup = false;
    public bool _loops_started = false;
    public double _time = 0.0;
    public double _fade_elapsed = -1.0;
    public double _master_user_db = 0.0;
    public double _film_gain = 1.0;
    public double _submersion = 0.0;
    public Vector3 _fire_position = Vector3.Zero;
    public readonly List<AudioEffectLowPassFilter> _underwater_filters = new List<AudioEffectLowPassFilter>();

    // Inputs from the world.
    public double _daylight = 1.0;
    public double _hour = 12.0;
    public double _fire_intensity = 1.0;
    public double _wind = 0.3;
    public double _rain = 0.0;
    public double _shelter = 0.0;
    public bool _wading_active = false;
    public double _wading_speed = 0.0;
    public bool _voice_active = false;

    // Derived weights, kept for the schedulers and tests.
    public double _day_weight = 1.0;
    public double _evening_weight = 0.0;
    public double _night_weight = 0.0;
    public double _wildlife = 1.0;

    // Beds.
    public Bed _day_bed, _evening_bed, _night_bed, _wind_bed, _rain_bed, _canvas_bed, _storm_bed, _fire_bed, _wading_bed;
    public readonly List<Bed> _lap_beds = new List<Bed>();
    public readonly List<Bed> _beds = new List<Bed>();
    public SmoothedValue _fire_pitch = new SmoothedValue(1.0, 0.8);
    public SmoothedValue _wading_pitch = new SmoothedValue(1.0, 0.35);
    public SmoothedValue _duck_db = new SmoothedValue(0.0, 0.25);
    public double _fire_flare_db = 0.0;

    // One-shots.
    public AudioStreamPlayer3D _fire_hiss;
    public AudioStreamPlayer _water_in;
    public AudioStreamPlayer _water_out;
    public Pool<AudioStreamPlayer> _footstep_pool = new Pool<AudioStreamPlayer>();
    public Pool<AudioStreamPlayer> _interact_pool = new Pool<AudioStreamPlayer>();
    public Pool<AudioStreamPlayer> _ui_pool = new Pool<AudioStreamPlayer>();
    public Pool<AudioStreamPlayer> _wildlife_pool = new Pool<AudioStreamPlayer>();
    public Pool<AudioStreamPlayer> _thunder_pool = new Pool<AudioStreamPlayer>();
    public Pool<AudioStreamPlayer3D> _impact_pool = new Pool<AudioStreamPlayer3D>();
    public readonly Dictionary<string, int> _last_variant = new Dictionary<string, int>();

    // Schedulers.
    public Countdown _bird_burst_timer = new Countdown();
    public Countdown _bird_call_timer = new Countdown();
    public long _bird_calls_left = 0;
    public Countdown _owl_timer = new Countdown();
    public Countdown _loon_timer = new Countdown();
    public Countdown _hiss_timer = new Countdown();

    public override void _Ready()
    {
        if (_is_setup && !_loops_started)
        {
            _start_loops();
        }
    }

    public override void _Process(double delta)
    {
        if (!_is_setup)
        {
            return;
        }
        _time += delta;
        _update_submersion(delta);
        _update_buses(delta);
        _update_beds(delta);
        _schedule_birds(delta);
        _schedule_night(delta);
        _schedule_hiss(delta);
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// Builds the buses, reads the manifest and creates every player. Safe to call
    /// again: the previous players are discarded and rebuilt. `lap_points` are where
    /// the pond laps at the pier posts.
    public void setup(Vector3 fire_position, Godot.Collections.Array<Vector3> lap_points, SoundBank sound_bank = null)
    {
        if (_is_setup)
        {
            _clear_players();
        }
        bank = sound_bank ?? SoundBank.Load();
        _rng.Seed = unchecked((ulong)SCHEDULER_SEED);
        _fire_position = fire_position;
        _ensure_buses();
        _build_players(lap_points);
        _is_setup = true;
        _refresh_targets();
        _snap_all();
        _reset_schedulers();
        G.print(G.format("AUDIO_BANK sounds=%d files=%d present=%d", new Godot.Collections.Array { bank.Entries.Count, System.Linq.Enumerable.Count(bank.AllFiles()), bank.PresentFiles() }));
        if (IsInsideTree())
        {
            _start_loops();
        }
    }

    public bool is_ready()
    {
        return _is_setup;
    }

    /// 0 = deep night, 1 = full day.
    public void set_daylight(double daylight)
    {
        _daylight = clampf(daylight, 0.0, 1.0);
        _refresh_targets();
    }

    /// The clock hour tells dusk (the evening bed) from dawn (the day woods waking).
    public void set_hour(double hour)
    {
        _hour = hour;
        _refresh_targets();
    }

    /// 0 = out, 1 = normal, 2 = roaring.
    public void set_fire_intensity(double intensity)
    {
        _fire_intensity = clampf(intensity, 0.0, 2.0);
        _refresh_targets();
    }

    /// 0..1 scales the canopy wind.
    public void set_wind(double strength)
    {
        _wind = clampf(strength, 0.0, 1.0);
        _refresh_targets();
    }

    /// Rain 0..1 and how far the listener is inside the tent (0 outside, 1 inside):
    /// the rain moves from the leaves to the canvas, and the storm wind comes with
    /// heavy rain.
    public void set_weather(double rain, double shelter)
    {
        _rain = clampf(rain, 0.0, 1.0);
        _shelter = clampf(shelter, 0.0, 1.0);
        _refresh_targets();
    }

    /// Continuous wading while moving through water; fades in and out.
    public void set_wading(bool active, double speed)
    {
        _wading_active = active;
        _wading_speed = clampf(speed, 0.0, 1.0);
        _refresh_targets();
    }

    /// While June speaks, the ambience dips by [constant VOICE_DUCK_DB].
    public void set_voice_ducking(bool active)
    {
        _voice_active = active;
        _duck_db.target = active ? VOICE_DUCK_DB : 0.0;
    }

    /// Picks a variant of `id`, never the one it played last.
    public int pick_variant(string id)
    {
        int count = bank.Variants(id);
        if (count <= 0)
        {
            return -1;
        }
        int last = _last_variant.TryGetValue(id, out int previous) ? previous : -1;
        int variant;
        if (count == 1)
        {
            variant = 0;
        }
        else if (last < 0)
        {
            variant = _rng.RandiRange(0, count - 1);
        }
        else
        {
            variant = _rng.RandiRange(0, count - 2);
            if (variant >= last)
            {
                variant += 1;
            }
        }
        _last_variant[id] = variant;
        return variant;
    }

    /// One footstep on `surface` (a key of [member Sounds.Footsteps]), with pitch and
    /// level jitter and a little more weight when running.
    public void footstep(StringName surface, bool running, double gain_db = 0.0)
    {
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        if (!Sounds.Footsteps.TryGetValue(surface, out string id))
        {
            G.push_warning(G.format("AudioDirector: unknown footstep surface '%s'", (StringName)surface));
            return;
        }
        int variant = pick_variant(id);
        AudioStream stream = bank.Stream(id, variant);
        float pitch = (float)(_rng.RandfRange(0.92f, 1.08f) * (running ? 1.07 : 1.0));
        float jitter = _rng.RandfRange(-3.0f, 3.0f);
        if (stream == null)
        {
            return;
        }
        AudioStreamPlayer player = _footstep_pool.next();
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = (float)play_db(id, variant, FOOTSTEP_TRIM_DB[surface] + jitter + (running ? 3.0 : 0.0) + gain_db);
        player.Play();
    }

    /// One action, interface or story sound; `kind` is one of [member Sounds.Interactions].
    public void play_interact(StringName kind)
    {
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        string id = kind;
        if (Array.IndexOf(Sounds.Interactions, id) < 0)
        {
            G.push_warning(G.format("AudioDirector: unknown interaction '%s'", (StringName)kind));
            return;
        }
        if (id == Sounds.LogIntoFire)
        {
            _fire_flare_db = 1.8;
        }
        int variant = pick_variant(id);
        AudioStream stream = bank.Stream(id, variant);
        float pitch = _rng.RandfRange(0.97f, 1.03f);
        float jitter = _rng.RandfRange(-1.0f, 1.0f);
        if (stream == null)
        {
            return;
        }
        // Interface sounds play while the tree is paused (the journal pauses play).
        AudioStreamPlayer player = category_of(id) == "ui" ? _ui_pool.next() : _interact_pool.next();
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = (float)play_db(id, variant, jitter);
        player.Play();
    }

    /// The lens or the player's head crossing the pond surface.
    public void water_crossing(bool entering)
    {
        if (!_is_setup)
        {
            return;
        }
        if (Game.Instance.has_flag("film-quality") || Game.Instance.has_flag("cinematic"))
        {
            G.print("WATER_CROSSING entering=", entering, " frames_drawn=", Engine.GetFramesDrawn());
        }
        AudioStreamPlayer start = entering ? _water_in : _water_out;
        AudioStreamPlayer stop = entering ? _water_out : _water_in;
        stop.Stop();
        if (start.Stream != null && start.IsInsideTree())
        {
            start.Play();
        }
    }

    /// A thrown stone touching the water: a skip, or the last plop when it sinks.
    public void water_impact(Vector3 at, long hop, bool sink = false)
    {
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        string id = sink ? Sounds.StoneSink : Sounds.StoneSkip;
        int variant = pick_variant(id);
        AudioStream stream = bank.Stream(id, variant);
        if (stream == null)
        {
            return;
        }
        AudioStreamPlayer3D emitter = _impact_pool.next();
        emitter.Stream = stream;
        emitter.Position = at;
        emitter.PitchScale = (float)(1.0 + Math.Min(hop, 6) * 0.05);
        emitter.VolumeDb = (float)play_db(id, variant, -Math.Min(hop, 6) * 1.5);
        emitter.Play();
    }

    /// One thunder roll. `power` is the strike's 0..1 strength; inside the tent it
    /// is a little quieter.
    public void thunder(double power, bool sheltered)
    {
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        int variant = pick_variant(Sounds.Thunder);
        AudioStream stream = bank.Stream(Sounds.Thunder, variant);
        float pitch = _rng.RandfRange(0.94f, 1.04f);
        if (stream == null)
        {
            return;
        }
        AudioStreamPlayer player = _thunder_pool.next();
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = (float)play_db(Sounds.Thunder, variant, linear_to_db(maxf(power, 0.3)) + (sheltered ? -4.0 : 0.0));
        player.Play();
    }

    public void set_master_volume(double linear)
    {
        double clamped = clampf(linear, 0.0, 1.0);
        _master_user_db = clamped < 1e-4 ? SILENT_DB : linear_to_db(clamped);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(BUS_MASTER), (float)_master_user_db);
    }

    public void begin_film()
    {
        _film_gain = 1.0;
        _rng.Seed = unchecked((ulong)SCHEDULER_SEED);
        _time = 0.0;
        _last_variant.Clear();
        _refresh_targets();
        _snap_all();
        _reset_schedulers();
        _start_loops();
    }

    public void film_fade(double gain)
    {
        _film_gain = clampf(gain, 0.0, 1.0);
        _apply_bus_levels();
    }

    // ---------------------------------------------------------------------------
    // Buses
    // ---------------------------------------------------------------------------

    public void _ensure_buses()
    {
        if (AudioServer.GetBusIndex(BUS_WATER_FOLEY) == -1)
        {
            AudioServer.AddBusEffect((int)_add_bus(BUS_WATER_FOLEY), _make_limiter());
        }
        if (AudioServer.GetBusIndex(BUS_AMBIENCE) == -1)
        {
            long index = _add_bus(BUS_AMBIENCE);
            AudioEffectReverb reverb = new AudioEffectReverb();
            reverb.RoomSize = 0.45f;
            reverb.Damping = 0.5f;
            reverb.Wet = 0.06f;
            reverb.Dry = 1.0f;
            reverb.Spread = 0.8f;
            reverb.Hipass = 0.2f;
            reverb.PredelayMsec = 40.0f;
            AudioServer.AddBusEffect((int)index, reverb);
            AudioServer.AddBusEffect((int)index, _make_limiter());
        }
        if (AudioServer.GetBusIndex(BUS_SFX) == -1)
        {
            long index2 = _add_bus(BUS_SFX);
            AudioEffectReverb reverb2 = new AudioEffectReverb();
            reverb2.RoomSize = 0.5f;
            reverb2.Damping = 0.6f;
            reverb2.Wet = 0.06f;
            reverb2.Dry = 1.0f;
            reverb2.Hipass = 0.25f;
            reverb2.PredelayMsec = 25.0f;
            AudioServer.AddBusEffect((int)index2, reverb2);
            AudioServer.AddBusEffect((int)index2, _make_limiter());
        }
        if (AudioServer.GetBusIndex(BUS_VOICE) == -1)
        {
            // June is close and dry: no reverb, only the safety limiter.
            AudioServer.AddBusEffect((int)_add_bus(BUS_VOICE), _make_limiter());
        }
        _underwater_filters.Clear();
        foreach (StringName bus in new[] { BUS_AMBIENCE, BUS_SFX })
        {
            int index4 = AudioServer.GetBusIndex(bus);
            AudioEffectLowPassFilter filter = null;
            for (int effect = 0, effect_end = AudioServer.GetBusEffectCount(index4); effect < effect_end; effect++)
            {
                if (AudioServer.GetBusEffect(index4, effect) is AudioEffectLowPassFilter existing && existing.ResourceName == "PondSubmersion")
                {
                    filter = existing;
                }
            }
            if (filter == null)
            {
                filter = new AudioEffectLowPassFilter();
                filter.ResourceName = "PondSubmersion";
                AudioServer.AddBusEffect(index4, filter);
            }
            filter.CutoffHz = (float)lerpf(18000.0, 1100.0, _submersion);
            _underwater_filters.Add(filter);
        }
        // The buses can peak together. Limit their sum too.
        int master = AudioServer.GetBusIndex(BUS_MASTER);
        bool has_limiter = false;
        for (int i = 0, i_end = AudioServer.GetBusEffectCount(master); i < i_end; i++)
        {
            if (AudioServer.GetBusEffect(master, i).ResourceName == "CampMasterLimiter")
            {
                has_limiter = true;
            }
        }
        if (!has_limiter)
        {
            AudioEffectHardLimiter limiter = _make_limiter();
            limiter.ResourceName = "CampMasterLimiter";
            AudioServer.AddBusEffect(master, limiter);
        }
    }

    public long _add_bus(StringName bus_name)
    {
        AudioServer.AddBus();
        long index = (long)AudioServer.GetBusCount() - 1;
        AudioServer.SetBusName((int)index, (string)bus_name);
        AudioServer.SetBusSend((int)index, BUS_MASTER);
        return index;
    }

    public AudioEffectHardLimiter _make_limiter()
    {
        /// Safety net so summed layers stay under the ceiling.
        AudioEffectHardLimiter limiter = new AudioEffectHardLimiter();
        limiter.CeilingDb = (float)LIMITER_CEILING_DB;
        return limiter;
    }

    /// The director's buses rise from silence when the loops start, follow the film
    /// fade, dip under water and (the ambience) under June's voice. The Master bus
    /// stays the player's setting.
    public void _apply_bus_levels()
    {
        double fade_db = 0.0;
        if (_fade_elapsed >= 0.0 && _fade_elapsed < START_FADE_SECONDS)
        {
            fade_db = lerpf(-48.0, 0.0, smoothstep(0.0, 1.0, _fade_elapsed / START_FADE_SECONDS));
        }
        double film_db = linear_to_db(maxf(_film_gain, 0.0001));
        _set_bus_db(BUS_AMBIENCE, fade_db + film_db - _submersion * 5.0 + _duck_db.value);
        _set_bus_db(BUS_SFX, fade_db + film_db - _submersion * 5.0);
        _set_bus_db(BUS_WATER_FOLEY, fade_db + film_db);
        _set_bus_db(BUS_VOICE, film_db);
    }

    public static void _set_bus_db(StringName bus, double db)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index != -1)
        {
            AudioServer.SetBusVolumeDb(index, (float)db);
        }
    }

    public void _update_buses(double delta)
    {
        bool fading = _fade_elapsed >= 0.0 && _fade_elapsed < START_FADE_SECONDS;
        if (fading)
        {
            _fade_elapsed += delta;
        }
        double duck = _duck_db.value;
        _duck_db.step(delta);
        if (fading || !is_equal_approx(duck, _duck_db.value))
        {
            _apply_bus_levels();
        }
    }

    public void _update_submersion(double delta)
    {
        double target = Game.Instance.world != null && Game.Instance.world.underwater ? 1.0 : 0.0;
        double value = move_toward(_submersion, target, delta * 4.0);
        if (is_equal_approx(value, _submersion))
        {
            return;
        }
        _submersion = value;
        foreach (AudioEffectLowPassFilter filter in _underwater_filters)
        {
            filter.CutoffHz = (float)lerpf(18000.0, 1100.0, _submersion);
        }
        _apply_bus_levels();
    }

    // ---------------------------------------------------------------------------
    // Players
    // ---------------------------------------------------------------------------

    public void _build_players(Godot.Collections.Array<Vector3> lap_points)
    {
        _beds.Clear();
        _day_bed = _flat_bed("DayWoods", Sounds.BedDayWoods, BUS_AMBIENCE);
        _evening_bed = _flat_bed("Evening", Sounds.BedEvening, BUS_AMBIENCE);
        _night_bed = _flat_bed("Night", Sounds.BedNight, BUS_AMBIENCE);
        _wind_bed = _flat_bed("WindCanopy", Sounds.BedWindCanopy, BUS_AMBIENCE);
        _rain_bed = _flat_bed("Rain", Sounds.BedRain, BUS_AMBIENCE);
        _canvas_bed = _flat_bed("RainCanvas", Sounds.BedRainCanvas, BUS_AMBIENCE);
        _storm_bed = _flat_bed("StormWind", Sounds.BedStormWind, BUS_AMBIENCE);
        _wading_bed = _flat_bed("Wading", Sounds.BedWading, BUS_SFX);
        _wading_bed.amp.time_constant = 0.35;

        AudioStreamPlayer3D fire = _make_emitter("Fire", bank.Stream(Sounds.BedFire, 0), BUS_AMBIENCE, _fire_position, 2.5, 32.0);
        fire.AttenuationFilterCutoffHz = 6500.0f;
        _fire_bed = _add_bed(fire, Sounds.BedFire, 0.6);
        _fire_hiss = _make_emitter("FireHiss", null, BUS_SFX, _fire_position, 2.0, 24.0);

        _lap_beds.Clear();
        for (int i = 0; i < lap_points.Count; i++)
        {
            AudioStreamPlayer3D lap = _make_emitter(G.format("PondLap%d", i), bank.Stream(Sounds.BedPondLap, 0), BUS_AMBIENCE, lap_points[i], 2.0, 18.0);
            _lap_beds.Add(_add_bed(lap, Sounds.BedPondLap, 1.5));
        }

        _water_in = _make_player("WaterIn", bank.Stream(Sounds.WaterIn, 0), BUS_WATER_FOLEY, play_db(Sounds.WaterIn, 0));
        _water_out = _make_player("WaterOut", bank.Stream(Sounds.WaterOut, 0), BUS_WATER_FOLEY, play_db(Sounds.WaterOut, 0));

        _fill_pool(_wildlife_pool, "Wildlife", WILDLIFE_POOL_SIZE, BUS_AMBIENCE);
        _fill_pool(_footstep_pool, "Footstep", FOOTSTEP_POOL_SIZE, BUS_SFX);
        _fill_pool(_interact_pool, "Interact", INTERACT_POOL_SIZE, BUS_SFX);
        _fill_pool(_thunder_pool, "Thunder", THUNDER_POOL_SIZE, BUS_SFX);
        _fill_pool(_ui_pool, "Interface", 2, BUS_SFX);
        foreach (AudioStreamPlayer ui in _ui_pool.players)
        {
            ui.ProcessMode = ProcessModeEnum.Always;
        }
        _impact_pool.players.Clear();
        for (int i = 0; i < IMPACT_POOL_SIZE; i++)
        {
            _impact_pool.players.Add(_make_emitter(G.format("StoneImpact%d", i), null, BUS_SFX, Vector3.Zero, 4.0, 35.0));
        }
    }

    public void _fill_pool(Pool<AudioStreamPlayer> pool, string prefix, long count, StringName bus)
    {
        pool.players.Clear();
        pool._cursor = 0;
        for (int i = 0; i < count; i++)
        {
            pool.players.Add(_make_player(G.format("%s%d", new Godot.Collections.Array { prefix, i }), null, bus, SILENT_DB));
        }
    }

    public Bed _flat_bed(string node_name, string id, StringName bus)
    {
        return _add_bed(_make_player(node_name, bank.Stream(id, 0), bus, SILENT_DB), id, 2.0);
    }

    public Bed _add_bed(Node player, string id, double smoothing_seconds)
    {
        Bed bed = new Bed { player = player, id = id, amp = new SmoothedValue(0.0, smoothing_seconds) };
        _beds.Add(bed);
        return bed;
    }

    public AudioStreamPlayer3D _make_emitter(string node_name, AudioStream stream, StringName bus, Vector3 at, double unit_size, double max_distance)
    {
        AudioStreamPlayer3D emitter = new AudioStreamPlayer3D();
        emitter.Name = node_name;
        emitter.Stream = stream;
        emitter.Bus = bus;
        emitter.Position = at;
        emitter.UnitSize = (float)unit_size;
        emitter.MaxDistance = (float)max_distance;
        emitter.VolumeDb = (float)SILENT_DB;
        emitter.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
        // Never louder than at the unit distance, so the true-peak cap holds up close.
        emitter.MaxDb = 0.0f;
        AddChild(emitter);
        return emitter;
    }

    public AudioStreamPlayer _make_player(string node_name, AudioStream stream, StringName bus, double volume_db)
    {
        AudioStreamPlayer player = new AudioStreamPlayer();
        player.Name = node_name;
        player.Stream = stream;
        player.Bus = bus;
        player.VolumeDb = (float)volume_db;
        AddChild(player);
        return player;
    }

    public void _start_loops()
    {
        _loops_started = true;
        foreach (Bed bed in _beds)
        {
            if (bed == _wading_bed)
            {
                continue;
            }
            _play_bed(bed);
        }
        _fade_elapsed = 0.0;
        _apply_bus_levels();
    }

    /// Starts a bed at a random point so beds sharing a take never line up.
    public void _play_bed(Bed bed)
    {
        float offset = _rng.Randf();
        AudioStream stream = bed.player is AudioStreamPlayer flat ? flat.Stream : ((AudioStreamPlayer3D)bed.player).Stream;
        if (stream == null || !bed.player.IsInsideTree())
        {
            return;
        }
        float from = (float)(offset * stream.GetLength());
        if (bed.player is AudioStreamPlayer flat2) flat2.Play(from);
        else ((AudioStreamPlayer3D)bed.player).Play(from);
    }

    public static bool _bed_playing(Bed bed) =>
        bed.player is AudioStreamPlayer flat ? flat.Playing : ((AudioStreamPlayer3D)bed.player).Playing;

    public void _clear_players()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _beds.Clear();
        _lap_beds.Clear();
        _loops_started = false;
        _is_setup = false;
    }

    // ---------------------------------------------------------------------------
    // Per-frame mixing
    // ---------------------------------------------------------------------------

    /// Recomputes every bed's target amplitude from the current inputs.
    public void _refresh_targets()
    {
        if (!_is_setup)
        {
            return;
        }
        double day = smoothstep(0.45, 0.8, _daylight);
        double night = 1.0 - smoothstep(0.15, 0.4, _daylight);
        double dusk = maxf(0.0, 1.0 - day - night);
        bool evening = _hour >= 12.0;
        _day_weight = day + (evening ? 0.0 : dusk);
        _evening_weight = evening ? dusk : 0.0;
        _night_weight = night;
        // Birds and insects go quiet in the rain.
        _wildlife = 1.0 - 0.8 * smoothstep(0.1, 0.6, _rain);
        // Amplitudes of uncorrelated beds: the square root keeps the crossfade's power constant.
        _day_bed.amp.target = Math.Sqrt(_day_weight) * _wildlife;
        _evening_bed.amp.target = Math.Sqrt(_evening_weight) * _wildlife;
        _night_bed.amp.target = Math.Sqrt(_night_weight) * _wildlife;
        _wind_bed.amp.target = lerpf(0.12, 1.0, _wind);
        _rain_bed.amp.target = _rain * (1.0 - _shelter);
        _canvas_bed.amp.target = _rain * _shelter;
        _storm_bed.amp.target = smoothstep(0.35, 0.9, _rain) * (1.0 - 0.45 * _shelter);
        _fire_bed.amp.target = _fire_intensity < 0.01 ? 0.0 : Math.Min(Math.Pow(_fire_intensity, 0.5), 1.42);
        _fire_pitch.target = 0.985 + 0.015 * _fire_intensity;
        foreach (Bed lap in _lap_beds)
        {
            lap.amp.target = lerpf(0.8, 1.2, _wind);
        }
        _wading_bed.amp.target = _wading_active ? lerpf(0.45, 1.0, _wading_speed) : 0.0;
        _wading_pitch.target = 0.9 + 0.2 * _wading_speed;
    }

    public void _snap_all()
    {
        foreach (Bed bed in _beds)
        {
            bed.amp.snap();
        }
        _fire_pitch.snap();
        _wading_pitch.snap();
        _duck_db.snap();
        _fire_flare_db = 0.0;
    }

    public void _update_beds(double delta)
    {
        _fire_flare_db = lerpf(_fire_flare_db, 0.0, 1.0 - exp(-delta / 2.0));
        double canopy_swell = 0.85 + 0.15 * sin(_time * 0.13);
        foreach (Bed bed in _beds)
        {
            double amp = bed.amp.step(delta);
            if (bed == _wind_bed)
            {
                amp *= canopy_swell;
            }
            double flare = bed == _fire_bed ? _fire_flare_db : 0.0;
            double db = amp < 0.003 ? SILENT_DB : play_db(bed.id, 0, linear_to_db(amp) + flare);
            bed.set_level(db);
        }
        ((AudioStreamPlayer3D)_fire_bed.player).PitchScale = (float)_fire_pitch.step(delta);
        AudioStreamPlayer wading = (AudioStreamPlayer)_wading_bed.player;
        wading.PitchScale = (float)_wading_pitch.step(delta);
        if (_wading_active && !wading.Playing)
        {
            _play_bed(_wading_bed);
        }
        else if (!_wading_active && wading.Playing && _wading_bed.amp.value < 0.01)
        {
            wading.Stop();
        }
    }

    public void _reset_schedulers()
    {
        _bird_burst_timer.reset(_rng.RandfRange(6.0f, 16.0f));
        _bird_calls_left = 0;
        _owl_timer.reset(_rng.RandfRange(15.0f, 35.0f));
        _loon_timer.reset(_rng.RandfRange(20.0f, 45.0f));
        _hiss_timer.reset(_rng.RandfRange(3.0f, 8.0f));
    }

    /// Daytime songbirds: one or two phrases, then a real rest; none in the rain.
    public void _schedule_birds(double delta)
    {
        double activity = _day_weight * _wildlife;
        if (activity < 0.1)
        {
            _bird_calls_left = 0;
            _bird_burst_timer.remaining = maxf(_bird_burst_timer.remaining, 10.0);
            return;
        }
        if (_bird_calls_left > 0)
        {
            if (_bird_call_timer.tick(delta))
            {
                _bird_calls_left -= 1;
                _bird_call_timer.reset(_rng.RandfRange(1.5f, 4.0f));
                _play_wildlife(Sounds.BirdsDay, linear_to_db(activity) + _rng.RandfRange(-4.0f, 1.0f));
            }
            return;
        }
        if (!_bird_burst_timer.tick(delta))
        {
            return;
        }
        _bird_burst_timer.reset(_rng.RandfRange(10.0f, 30.0f) / activity);
        _bird_calls_left = _rng.RandiRange(1, 2);
        _bird_call_timer.reset(0.0);
    }

    /// The owl far off at night; the loon across the water at dusk and at night.
    public void _schedule_night(double delta)
    {
        if (_owl_timer.tick(delta))
        {
            _owl_timer.reset(_rng.RandfRange(30.0f, 75.0f));
            if (_night_weight > 0.6 && _wildlife > 0.6)
            {
                _play_wildlife(Sounds.Owl, _rng.RandfRange(-4.0f, 0.0f));
            }
        }
        if (_loon_timer.tick(delta))
        {
            _loon_timer.reset(_rng.RandfRange(50.0f, 130.0f));
            if ((_evening_weight > 0.35 || _night_weight > 0.5) && _wildlife > 0.6)
            {
                _play_wildlife(Sounds.Loon, _rng.RandfRange(-5.0f, 0.0f));
            }
        }
    }

    /// One hiss now: a log going onto the fire in the rain. Played only while
    /// it rains on a burning fire, like the scheduled ones.
    public void fire_hiss()
    {
        if (!_is_setup || !IsInsideTree() || _rain < 0.25 || _fire_intensity < 0.05)
        {
            return;
        }
        int variant = pick_variant(Sounds.FireHiss);
        AudioStream stream = bank.Stream(Sounds.FireHiss, variant);
        if (stream == null)
        {
            return;
        }
        _fire_hiss.Stream = stream;
        _fire_hiss.PitchScale = _rng.RandfRange(0.95f, 1.05f);
        _fire_hiss.VolumeDb = (float)play_db(Sounds.FireHiss, variant, linear_to_db(maxf(_rain, 0.3)));
        _fire_hiss.Play();
        _hiss_timer.reset(_rng.RandfRange(3.0f, 6.0f));
    }

    /// Rain hissing on the coals while the fire burns in the rain.
    public void _schedule_hiss(double delta)
    {
        if (!_hiss_timer.tick(delta))
        {
            return;
        }
        _hiss_timer.reset(_rng.RandfRange(3.0f, 9.0f) / maxf(_rain, 0.3));
        if (_rain < 0.25 || _fire_intensity < 0.05 || !IsInsideTree())
        {
            return;
        }
        int variant = pick_variant(Sounds.FireHiss);
        AudioStream stream = bank.Stream(Sounds.FireHiss, variant);
        if (stream == null)
        {
            return;
        }
        _fire_hiss.Stream = stream;
        _fire_hiss.PitchScale = _rng.RandfRange(0.95f, 1.05f);
        _fire_hiss.VolumeDb = (float)play_db(Sounds.FireHiss, variant, linear_to_db(maxf(_rain, 0.3)));
        _fire_hiss.Play();
    }

    public void _play_wildlife(string id, double gain_db)
    {
        if (!IsInsideTree())
        {
            return;
        }
        int variant = pick_variant(id);
        AudioStream stream = bank.Stream(id, variant);
        float pitch = _rng.RandfRange(0.97f, 1.03f);
        if (stream == null)
        {
            return;
        }
        AudioStreamPlayer player = _wildlife_pool.next();
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = (float)play_db(id, variant, gain_db);
        player.Play();
    }

    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.audio, this)) Game.Instance.audio = null;
    }
}
