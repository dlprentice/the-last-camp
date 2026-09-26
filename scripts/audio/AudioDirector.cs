#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using Environment = Godot.Environment;
using Range = Godot.Range;
using LastCamp;
using LastCamp.Construction;

namespace LastCamp;

/// Recorded nature and runtime-synthesised audio layer for The Last Camp.
///
/// Fire, wind and water crossings use credited recordings; birds, insects and
/// interaction details use the [Synth] toolbox. The director owns the buses, the positional
/// emitters (fire, shore, canopy) and the non-positional beds/one-shots, and it
/// smooths every level change per frame so the mix never jumps.
///
/// Children created by [method setup] (3D emitters use world-space positions):
/// Fire, FireFeed, Shore0..N, Canopy0..N, Crickets, Owl, Bird0..3,
/// Footstep0..3, Wading, Interact0..1.
public partial class AudioDirector : Node
{
    public static readonly StringName BUS_MASTER = "Master";
    public static readonly StringName BUS_AMBIENCE = "Ambience";
    public static readonly StringName BUS_SFX = "SFX";
    public static readonly StringName BUS_WATER_FOLEY = "WaterFoley";
    public static readonly StringName BUS_SCORE = "Score";

    public static readonly Godot.Collections.Array<StringName> SURFACES = new Godot.Collections.Array<StringName> { "grass", "dirt", "wood", "water", "rock" };
    public static readonly Godot.Collections.Array<StringName> INTERACTIONS = new Godot.Collections.Array<StringName> { "log_added", "lantern_toggle", "pickup", "ui" };

    public const long FOOTSTEP_VARIANTS = 3;
    public const long FOOTSTEP_POOL_SIZE = 4;
    public const long BIRD_POOL_SIZE = 4;
    public const long INTERACT_POOL_SIZE = 2;
    public const long OWL_VARIANTS = 2;
    public const double BIRD_BANK_COOLDOWN_SECONDS = 20.0;

    /// Base seed for all generation; change it to get a different (still deterministic) bank.
    public const long BANK_SEED = 20260902;
    /// Independent generation jobs (see _generate_group), balanced to similar cost.
    public const long GENERATION_GROUPS = 5;
    /// Run the generation groups on WorkerThreadPool; disable to debug recipes serially.
    public static readonly bool USE_WORKER_THREADS = true;
    /// Fixed seed for runtime scheduling so headless runs are reproducible.
    public const long SCHEDULER_SEED = 7;
    /// The whole mix rises from silence over this long when the loops start, so
    /// the first thing heard is never a full-volume gust.
    public const double START_FADE_SECONDS = 2.5;

    /// Floor used while fading a layer out completely (not a nominal mix level).
    public const double SILENT_DB = -80.0;
    public const double FIRE_DB = -3.0;
    public const double FIRE_FEED_DB = -15.0;
    public const double WATER_DB = -19.0;
    /// Sparse canopy gusts sit below the water, birds and fire.
    /// The recording has strong peaks, so both wind layers use conservative
    /// levels to leave room for focal sounds and quiet stretches.
    public const double WIND_MIN_DB = -52.0;
    public const double WIND_MAX_DB = -40.0;
    public const double CANOPY_MIN_DB = -58.0;
    public const double CANOPY_MAX_DB = -40.0;
    public const double CRICKETS_MIN_DB = -36.0;
    public const double CRICKETS_MAX_DB = -24.0;
    public const double BIRDS_MIN_DB = -34.0;
    public const double BIRDS_MAX_DB = -20.0;
    public const double OWL_DB = -19.0;
    public const double WADING_MIN_DB = -24.0;
    public const double WADING_MAX_DB = -12.0;

    public static readonly Godot.Collections.Dictionary<StringName, double> FOOTSTEP_DB = new Godot.Collections.Dictionary<StringName, double> { { "grass", -17.0 }, { "dirt", -15.0 }, { "wood", -11.0 }, { "water", -12.0 }, { "rock", -13.0 } };
    public static readonly Godot.Collections.Dictionary<StringName, double> INTERACT_DB = new Godot.Collections.Dictionary<StringName, double> { { "log_added", -13.0 }, { "lantern_toggle", -14.0 }, { "pickup", -16.0 }, { "ui", -22.0 } };

    /// Historical drone recipe retained for standalone DSP checks. These partials
    /// are not generated or played by the scene's ambient sound bank.
    public static readonly Godot.Collections.Array<Godot.Collections.Dictionary> DRONE_PARTIALS = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { "ratio", 1.0 }, { "pan", 0.0 }, { "db", 0.0 }, { "detune", 1.0 }, { "saw", false } }, new Godot.Collections.Dictionary { { "ratio", 1.5 }, { "pan", -0.45 }, { "db", -5.0 }, { "detune", 1.0 }, { "saw", false } }, new Godot.Collections.Dictionary { { "ratio", 2.0 }, { "pan", 0.45 }, { "db", -6.0 }, { "detune", 1.004 }, { "saw", false } }, new Godot.Collections.Dictionary { { "ratio", 1.0 }, { "pan", 0.15 }, { "db", -9.0 }, { "detune", 0.9985 }, { "saw", true } } };
    public const double DRONE_BASE_HZ = 56.0;

    /// Bird "species": each call is a sequence of syllables (seconds/Hz), optionally
    /// repeated, panned to a fixed direction as if the bird sat in one tree.
    public static readonly Godot.Collections.Array<Godot.Collections.Dictionary> BIRD_SPECIES = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { "pan", -0.7 }, { "level", 0.9 }, { "repeat", 3 }, { "gap", 0.09 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.06 }, { "f0", 6500.0 }, { "f1", 4200.0 } } } } }, new Godot.Collections.Dictionary { { "pan", 0.4 }, { "level", 0.8 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.28 }, { "f0", 2400.0 }, { "f1", 4300.0 }, { "vib", 9.0 }, { "vib_depth", 0.03 } } } } }, new Godot.Collections.Dictionary { { "pan", -0.3 }, { "level", 0.7 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.42 }, { "f0", 3900.0 }, { "f1", 3500.0 }, { "trill", 42.0 } } } } }, new Godot.Collections.Dictionary { { "pan", 0.8 }, { "level", 0.85 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.15 }, { "f0", 3200.0 }, { "f1", 3300.0 }, { "gap", 0.06 } }, new Godot.Collections.Dictionary { { "dur", 0.22 }, { "f0", 4700.0 }, { "f1", 4400.0 } } } } }, new Godot.Collections.Dictionary { { "pan", 0.1 }, { "level", 0.75 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.5 }, { "f0", 3000.0 }, { "f1", 3200.0 }, { "vib", 12.0 }, { "vib_depth", 0.12 } } } } }, new Godot.Collections.Dictionary { { "pan", -0.85 }, { "level", 0.5 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.35 }, { "f0", 6800.0 }, { "f1", 8200.0 } } } } }, new Godot.Collections.Dictionary { { "pan", 0.55 }, { "level", 0.8 }, { "repeat", 6 }, { "gap", 0.05 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.045 }, { "f0", 5200.0 }, { "f1", 3600.0 } } } } }, new Godot.Collections.Dictionary { { "pan", -0.5 }, { "level", 0.85 }, { "repeat", 1 }, { "gap", 0.0 }, { "syllables", new Godot.Collections.Array { new Godot.Collections.Dictionary { { "dur", 0.3 }, { "f0", 5200.0 }, { "f1", 2600.0 } } } } } };

    /// First-order smoother for dB and pitch targets; avoids zipper noise and hard cuts.
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
    public partial class Countdown
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
    public partial class VoicePool
    {
        public Godot.Collections.Array<AudioStreamPlayer> players = new Godot.Collections.Array<AudioStreamPlayer>();
        public long _cursor = 0;

        public AudioStreamPlayer next()
        {
            AudioStreamPlayer player = players[(int)_cursor];
            _cursor = (_cursor + 1) % (long)players.Count;
            return player;
        }
    }

    public partial class VoicePool3D
    {
        public Godot.Collections.Array<AudioStreamPlayer3D> players = new Godot.Collections.Array<AudioStreamPlayer3D>();
        public long _cursor = 0;

        public AudioStreamPlayer3D next()
        {
            AudioStreamPlayer3D player = players[(int)_cursor];
            _cursor = (_cursor + 1) % (long)players.Count;
            return player;
        }
    }

    /// Left/right-foot panned variants of one surface.
    public partial class FootstepSet : RefCounted
    {
        public Godot.Collections.Array<AudioStreamWav> left = new Godot.Collections.Array<AudioStreamWav>();
        public Godot.Collections.Array<AudioStreamWav> right = new Godot.Collections.Array<AudioStreamWav>();
    }

    /// Milliseconds spent generating the sound bank in the last [method setup] call.
    public long last_generation_msec = 0;

    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public bool _is_setup = false;
    public bool _loops_started = false;
    public double _time = 0.0;
    public double _fade_elapsed = -1.0;
    public double _master_user_db = 0.0;
    public Vector3 _fire_position = Vector3.Zero;
    public AudioStreamPlayer _score;
    public double _submersion = 0.0;
    public Godot.Collections.Array<AudioEffectLowPassFilter> _underwater_filters = new Godot.Collections.Array<AudioEffectLowPassFilter>();
    public AudioDirector.VoicePool3D _water_impacts = new AudioDirector.VoicePool3D();
    public AudioStreamWav _splash;
    public AudioStreamPlayer _water_entry;
    public AudioStreamPlayer _water_exit;
    public double _film_gain = 1.0;

    // Targets set through the public API.
    public double _daylight = 1.0;
    public double _fire_intensity = 1.0;
    public double _wind = 0.3;
    public bool _wading_active = false;
    public double _wading_speed = 0.0;
    public double _bird_activity = 1.0;
    public double _cricket_activity = 0.0;

    // Recorded loops and generated streams.
    public AudioStreamWav _fire_roar;
    public AudioStreamWav _water_loop;
    public AudioStreamWav _canopy_loop;
    public AudioStreamWav _wind_loop;
    public AudioStreamWav _cricket_bed;
    public Godot.Collections.Array<AudioStreamWav> _owl_calls = new Godot.Collections.Array<AudioStreamWav>();
    public Godot.Collections.Array<AudioStreamWav> _bird_calls = new Godot.Collections.Array<AudioStreamWav>();
    public AudioStreamWav _wading_loop;
    public Godot.Collections.Dictionary<StringName, AudioDirector.FootstepSet> _footsteps = new Godot.Collections.Dictionary<StringName, AudioDirector.FootstepSet>();
    public Godot.Collections.Dictionary<StringName, AudioStreamWav> _interactions = new Godot.Collections.Dictionary<StringName, AudioStreamWav>();

    // Players.
    public AudioStreamPlayer3D _fire_player;
    public AudioStreamPlayer3D _fire_feed_player;
    public Godot.Collections.Array<AudioStreamPlayer3D> _shore_players = new Godot.Collections.Array<AudioStreamPlayer3D>();
    public Godot.Collections.Array<AudioStreamPlayer3D> _canopy_players = new Godot.Collections.Array<AudioStreamPlayer3D>();
    public List<float> _canopy_phases = new List<float>();
    public AudioStreamPlayer _wind_player;
    public AudioStreamPlayer _crickets_player;
    public AudioStreamPlayer _owl_player;
    public AudioDirector.VoicePool _bird_pool = new AudioDirector.VoicePool();
    public AudioDirector.VoicePool _footstep_pool = new AudioDirector.VoicePool();
    public AudioStreamPlayer _wading_player;
    public AudioDirector.VoicePool _interact_pool = new AudioDirector.VoicePool();

    // Smoothed mix parameters.
    public AudioDirector.SmoothedValue _fire_db = new AudioDirector.SmoothedValue(FIRE_DB, 0.6);
    public AudioDirector.SmoothedValue _fire_pitch = new AudioDirector.SmoothedValue(1.0, 0.8);
    public double _fire_flare_db = 0.0;
    public AudioDirector.SmoothedValue _canopy_db = new AudioDirector.SmoothedValue(CANOPY_MIN_DB, 1.2);
    public AudioDirector.SmoothedValue _wind_db = new AudioDirector.SmoothedValue(WIND_MIN_DB, 2.0);
    public AudioDirector.SmoothedValue _water_db = new AudioDirector.SmoothedValue(WATER_DB, 1.5);
    public AudioDirector.SmoothedValue _crickets_db = new AudioDirector.SmoothedValue(SILENT_DB, 2.0);
    public AudioDirector.SmoothedValue _birds_db = new AudioDirector.SmoothedValue(BIRDS_MAX_DB, 2.0);
    public AudioDirector.SmoothedValue _wading_db = new AudioDirector.SmoothedValue(SILENT_DB, 0.35);
    public AudioDirector.SmoothedValue _wading_pitch = new AudioDirector.SmoothedValue(1.0, 0.35);

    // One-shot schedulers.
    public AudioDirector.Countdown _wind_gust_timer = new AudioDirector.Countdown();
    public double _wind_gust_age = -1.0;
    public double _wind_gust_duration = 12.0;
    public double _wind_envelope = 0.0;
    public AudioDirector.Countdown _owl_timer = new AudioDirector.Countdown();
    public AudioDirector.Countdown _bird_burst_timer = new AudioDirector.Countdown();
    public AudioDirector.Countdown _bird_call_timer = new AudioDirector.Countdown();
    public long _bird_calls_left = 0;
    public long _last_bird_bank = -1;
    public List<double> _bird_last_played = new List<double>();
    public long _step_parity = 0;
    public long _last_footstep_variant = -1;

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
        _update_start_fade(delta);
        _update_fire(delta);
        _update_wind(delta);
        _update_canopy(delta);
        _update_water(delta);
        _update_beds(delta);
        _update_wading(delta);
        _schedule_owl(delta);
        _schedule_birds(delta);
    }

    public void setup(Vector3 fire_position, Godot.Collections.Array<Vector3> shore_points, Godot.Collections.Array<Vector3> canopy_points)
    {
        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------
        /// Builds buses, synthesises the sound bank and creates all players. Safe to call
        /// again: the previous players are discarded and rebuilt.
        if (_is_setup)
        {
            _clear_players();
        }
        _rng.Seed = unchecked((ulong)(SCHEDULER_SEED));
        _fire_position = fire_position;
        _ensure_buses();

        long started = (long)Time.GetTicksMsec();
        _generate_bank();
        last_generation_msec = (long)Time.GetTicksMsec() - started;

        _build_players(shore_points, canopy_points);
        _is_setup = true;
        _refresh_targets();
        _snap_all();
        _reset_schedulers();
        if (IsInsideTree())
        {
            _start_loops();
        }
    }

    public void set_daylight(double daylight)
    {
        /// 0 = deep night, 1 = full day. Birds fade in over the morning, crickets and the
        /// owl fade in at dusk; both are present in the 0.3..0.65 band.
        _daylight = clampf(daylight, 0.0, 1.0);
        _refresh_targets();
    }

    public void set_fire_intensity(double intensity)
    {
        /// 0 = out, 1 = normal, 2 = roaring.
        _fire_intensity = clampf(intensity, 0.0, 2.0);
        _refresh_targets();
    }

    public void set_wind(double strength)
    {
        /// 0..1 scales the canopy rustle and the depth/speed of gust swells.
        _wind = clampf(strength, 0.0, 1.0);
        _refresh_targets();
    }

    public void footstep(StringName surface, bool running, double gain_db = 0.0)
    {
        /// Plays one footstep for [param surface] (see [constant SURFACES]), alternating a
        /// small left/right pan per foot and randomising pitch and level.
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        if (!_footsteps.ContainsKey(surface))
        {
            G.push_warning(G.format("AudioDirector: unknown footstep surface '%s'", (StringName)surface));
            return;
        }
        AudioDirector.FootstepSet sounds = _footsteps[surface];
        long variant = _rng.RandiRange(0, (int)(FOOTSTEP_VARIANTS - 1));
        if (variant == _last_footstep_variant)
        {
            variant = (variant + 1) % FOOTSTEP_VARIANTS;
        }
        _last_footstep_variant = variant;
        _step_parity = 1 - _step_parity;

        AudioStreamPlayer player = _footstep_pool.next();
        player.Stream = _step_parity == 0 ? sounds.left[(int)variant] : sounds.right[(int)variant];
        player.PitchScale = (float)(_rng.RandfRange(0.92f, 1.08f) * (running ? 1.07 : 1.0));
        player.VolumeDb = (float)(FOOTSTEP_DB[surface] + _rng.RandfRange(-3.0f, 3.0f) + (running ? 3.0 : 0.0) + gain_db);
        player.Play();
    }

    public void set_wading(bool active, double speed)
    {
        /// Continuous slosh while moving through water; fades in and out.
        _wading_active = active;
        _wading_speed = clampf(speed, 0.0, 1.0);
        _refresh_targets();
    }

    public void play_interact(StringName kind)
    {
        /// Kinds: log_added, lantern_toggle, pickup, ui (see [constant INTERACTIONS]).
        if (!_is_setup || !IsInsideTree())
        {
            return;
        }
        if (!_interactions.ContainsKey(kind))
        {
            G.push_warning(G.format("AudioDirector: unknown interaction '%s'", (StringName)kind));
            return;
        }
        AudioStreamPlayer player = _interact_pool.next();
        player.Stream = _interactions[kind];
        player.PitchScale = _rng.RandfRange(0.96f, 1.04f);
        player.VolumeDb = (float)(INTERACT_DB[kind] + _rng.RandfRange(-1.5f, 1.5f));
        player.Play();
        if (kind == "log_added")
        {
            _fire_feed_player.PitchScale = _rng.RandfRange(0.97f, 1.03f);
            _fire_feed_player.Play();
            _fire_flare_db = 1.8;
        }
    }

    public void set_master_volume(double linear)
    {
        double clamped = clampf(linear, 0.0, 1.0);
        _master_user_db = clamped < 1e-4 ? SILENT_DB : linear_to_db(clamped);
        _apply_master();
    }

    public void _apply_master()
    {
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(BUS_MASTER), (float)_master_user_db);
    }

    public void _apply_start_fade()
    {
        /// The director's own buses rise from silence when the loops start; the
        /// Master bus stays the user's setting.
        double fade_db = 0.0;
        if (_fade_elapsed >= 0.0 && _fade_elapsed < START_FADE_SECONDS)
        {
            fade_db = lerpf(-48.0, 0.0, smoothstep(0.0, 1.0, _fade_elapsed / START_FADE_SECONDS));
        }
        foreach (Variant bus_item in new Godot.Collections.Array { (StringName)BUS_AMBIENCE, (StringName)BUS_SFX })
        {
            StringName bus = bus_item.AsStringName();
            long index = AudioServer.GetBusIndex(bus);
            if (index != -1)
            {
                AudioServer.SetBusVolumeDb((int)index, (float)(fade_db + linear_to_db(maxf(_film_gain, 0.0001)) - _submersion * 5.0));
            }
        }
        long score_index = AudioServer.GetBusIndex(BUS_SCORE);
        if (score_index != -1)
        {
            AudioServer.SetBusVolumeDb((int)score_index, (float)linear_to_db(maxf(_film_gain, 0.0001)));
        }
        long water_index = AudioServer.GetBusIndex(BUS_WATER_FOLEY);
        if (water_index != -1)
        {
            AudioServer.SetBusVolumeDb((int)water_index, (float)(fade_db + linear_to_db(maxf(_film_gain, 0.0001))));
        }
    }

    public void _update_start_fade(double delta)
    {
        if (_fade_elapsed < 0.0 || _fade_elapsed >= START_FADE_SECONDS)
        {
            return;
        }
        _fade_elapsed += delta;
        _apply_start_fade();
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
        _apply_start_fade();
    }

    public bool is_ready()
    {
        return _is_setup;
    }

    public void water_crossing(bool entering)
    {
        /// Close water is heard at the lens. These takes already contain the muffled
        /// immersion tail, so do not route them through the exterior low-pass twice.
        if (!_is_setup)
        {
            return;
        }
        if (Game.Instance.has_flag("film-quality") || Game.Instance.has_flag("cinematic"))
        {
            G.print("WATER_CROSSING entering=", entering, " frames_drawn=", Engine.GetFramesDrawn());
        }
        if (entering)
        {
            _water_exit.Stop();
            _water_entry.Play();
        }
        else
        {
            _water_entry.Stop();
            _water_exit.Play();
        }
    }

    public static AudioStreamWav make_water_impact()
    {
        long n = Synth.frames(0.52);
        List<float> sound = Synth.noise_burst(n, 8724, 1700.0, 0.65, 0.07, 0.003);
        Synth.mix_into(sound, Synth.decaying_tone(n, 650.0, 165.0, 0.10, 0.65, 0.002));
        Synth.mix_into(sound, Synth.decaying_tone(n, 1300.0, 370.0, 0.055, 0.24, 0.003), Synth.frames(0.035));
        Synth.fade_edges(sound, 2.0, 150.0);
        return Synth.encode(Synth.normalize(sound, 0.75), false);
    }

    public void water_impact(Vector3 at, long hop)
    {
        if (!_is_setup)
        {
            return;
        }
        AudioStreamPlayer3D emitter = _water_impacts.next();
        emitter.Position = at;
        emitter.PitchScale = (float)(1.0 + (double)hop * 0.18);
        emitter.VolumeDb = (float)(-9.0 - (double)hop * 2.0);
        emitter.Play();
    }

    public void begin_film()
    {
        _film_gain = 1.0;
        _rng.Seed = unchecked((ulong)(SCHEDULER_SEED));
        _time = 0.0;
        _refresh_targets();
        _snap_all();
        _reset_schedulers();
        _start_loops();
    }

    public void film_fade(double gain)
    {
        _film_gain = clampf(gain, 0.0, 1.0);
        _apply_start_fade();
    }

    public void _ensure_buses()
    {
        // ---------------------------------------------------------------------------
        // Buses
        // ---------------------------------------------------------------------------
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
            reverb.Wet = 0.10f;
            reverb.Dry = 1.0f;
            reverb.Spread = 0.8f;
            reverb.Hipass = 0.2f;
            reverb.PredelayMsec = 40.0f;
            AudioServer.AddBusEffect((int)index, reverb);
            AudioEffectLowPassFilter low_pass = new AudioEffectLowPassFilter();
            low_pass.CutoffHz = 7000.0f;
            low_pass.Resonance = 0.5f;
            AudioServer.AddBusEffect((int)index, low_pass);
            AudioServer.AddBusEffect((int)index, _make_limiter());
        }

        if (AudioServer.GetBusIndex(BUS_SFX) == -1)
        {
            long index2 = _add_bus(BUS_SFX);
            AudioEffectReverb reverb2 = new AudioEffectReverb();
            reverb2.RoomSize = 0.5f;
            reverb2.Damping = 0.6f;
            reverb2.Wet = 0.08f;
            reverb2.Dry = 1.0f;
            reverb2.Hipass = 0.25f;
            reverb2.PredelayMsec = 25.0f;
            AudioServer.AddBusEffect((int)index2, reverb2);
            AudioServer.AddBusEffect((int)index2, _make_limiter());
        }
        if (AudioServer.GetBusIndex(BUS_SCORE) == -1)
        {
            long index3 = _add_bus(BUS_SCORE);
            AudioEffectReverb reverb3 = new AudioEffectReverb();
            reverb3.RoomSize = 0.8f;
            reverb3.Damping = 0.72f;
            reverb3.Wet = 0.30f;
            reverb3.Dry = 1.0f;
            AudioServer.AddBusEffect((int)index3, reverb3);
        }
        _underwater_filters.Clear();
        foreach (Variant bus_item in new Godot.Collections.Array { (StringName)BUS_AMBIENCE, (StringName)BUS_SFX })
        {
            StringName bus = bus_item.AsStringName();
            long index4 = AudioServer.GetBusIndex(bus);
            AudioEffectLowPassFilter filter = null;
            for (long effect = 0, effect_end = AudioServer.GetBusEffectCount((int)index4); effect < effect_end; effect++)
            {
                AudioEffect existing = AudioServer.GetBusEffect((int)index4, (int)effect);
                if (existing.ResourceName == "PondSubmersion")
                {
                    filter = existing as AudioEffectLowPassFilter;
                }
            }
            if (filter == null)
            {
                filter = new AudioEffectLowPassFilter();
                filter.ResourceName = "PondSubmersion";
                AudioServer.AddBusEffect((int)index4, filter);
            }
            filter.CutoffHz = (float)lerpf(18000.0, 1100.0, _submersion);
            _underwater_filters.Add(filter);
        }
        // The ambience and effects can peak simultaneously. Limit their sum too.
        long master = AudioServer.GetBusIndex(BUS_MASTER);
        bool has_limiter = false;
        for (long i2 = 0, i_end = AudioServer.GetBusEffectCount((int)master); i2 < i_end; i2++)
        {
            if (AudioServer.GetBusEffect((int)master, (int)i2).ResourceName == "CampMasterLimiter")
            {
                has_limiter = true;
            }
        }
        if (!has_limiter)
        {
            AudioEffectHardLimiter limiter = _make_limiter();
            limiter.ResourceName = "CampMasterLimiter";
            AudioServer.AddBusEffect((int)master, limiter);
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
        /// Safety net so summed layers can never clip the bus output.
        AudioEffectHardLimiter limiter = new AudioEffectHardLimiter();
        limiter.CeilingDb = -1.0f;
        return limiter;
    }

    public void _generate_bank()
    {
        // ---------------------------------------------------------------------------
        // Bank generation
        // ---------------------------------------------------------------------------
        /// Synthesises the whole bank. The recipes are split into independent groups of
        /// roughly equal cost that run on the worker thread pool; each group writes only
        /// into its own result dictionary, and the main thread adopts everything afterwards.
        Godot.Collections.Array<Godot.Collections.Dictionary> results = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        for (long i = 0; i < GENERATION_GROUPS; i++)
        {
            results.Add(new Godot.Collections.Dictionary());
        }
        if (USE_WORKER_THREADS)
        {
            Godot.Collections.Array<long> task_ids = new Godot.Collections.Array<long>();
            for (long i2 = 0; i2 < GENERATION_GROUPS; i2++)
            {
                task_ids.Add(WorkerThreadPool.AddTask(G.bind(new Callable(this, AudioDirector.MethodName._generate_group), i2, results[(int)i2]), true, G.format("AudioDirector bank %d", i2)));
            }
            foreach (long task_id in task_ids)
            {
                WorkerThreadPool.WaitForTaskCompletion(task_id);
            }
        }
        else
        {
            for (long i3 = 0; i3 < GENERATION_GROUPS; i3++)
            {
                _generate_group(i3, results[(int)i3]);
            }
        }
        _adopt_bank(results);
    }

    public void _generate_group(long index, Godot.Collections.Dictionary into)
    {
        long seed_value = BANK_SEED;
        switch (index)
        {
            case 0:
                into["water"] = make_water_lapping(seed_value + 300);
                break;
            case 1:
                into["canopy"] = make_canopy_rustle(seed_value + 400);
                into["crickets"] = make_cricket_bed(seed_value + 500);
                break;
            case 2:
                into["wading"] = make_wading_loop(seed_value + 800);
                break;
            case 3:
                Godot.Collections.Dictionary<StringName, AudioDirector.FootstepSet> footsteps = new Godot.Collections.Dictionary<StringName, AudioDirector.FootstepSet>();
                for (long s = 0, s_end = (long)SURFACES.Count; s < s_end; s++)
                {
                    StringName surface = SURFACES[(int)s];
                    AudioDirector.FootstepSet sounds = new AudioDirector.FootstepSet();
                    for (long v = 0; v < FOOTSTEP_VARIANTS; v++)
                    {
                        List<float> mono = footstep_mono(surface, seed_value + 1000 + s * 50 + v * 7);
                        sounds.left.Add(Synth.encode(Synth.pan(mono, -0.14), true));
                        sounds.right.Add(Synth.encode(Synth.pan(mono, 0.14), true));
                    }
                    footsteps[surface] = sounds;
                }
                into["footsteps"] = footsteps;
                Godot.Collections.Dictionary<StringName, AudioStreamWav> interactions = new Godot.Collections.Dictionary<StringName, AudioStreamWav>();
                for (long k = 0, k_end = (long)INTERACTIONS.Count; k < k_end; k++)
                {
                    interactions[INTERACTIONS[(int)k]] = make_interaction(INTERACTIONS[(int)k], seed_value + 1500 + k * 7);
                }
                into["interactions"] = interactions;
                break;
            case 4:
                List<float> owl = owl_call_mono(seed_value + 600);
                Godot.Collections.Array<AudioStreamWav> owls = new Godot.Collections.Array<AudioStreamWav>();
                for (long i = 0; i < OWL_VARIANTS; i++)
                {
                    owls.Add(Synth.encode(Synth.pan(owl, i % 2 == 0 ? -0.55 : 0.6), true));
                }
                into["owls"] = owls;
                Godot.Collections.Array<AudioStreamWav> birds = new Godot.Collections.Array<AudioStreamWav>();
                for (long i2 = 0, i_end = (long)BIRD_SPECIES.Count; i2 < i_end; i2++)
                {
                    birds.Add(make_bird_call(i2, seed_value + 700 + i2 * 7));
                }
                into["birds"] = birds;
                break;
            default:
                G.push_error(G.format("AudioDirector: no generation group %d", index));
                break;
        }
    }

    public void _adopt_bank(Godot.Collections.Array<Godot.Collections.Dictionary> results)
    {
        _fire_roar = _recorded_loop("res://audio/fire_loop.wav");
        _wind_loop = _recorded_loop("res://audio/wind_trees.wav");
        _water_loop = results[0]["water"].As<AudioStreamWav>();
        _canopy_loop = results[1]["canopy"].As<AudioStreamWav>();
        _cricket_bed = results[1]["crickets"].As<AudioStreamWav>();
        _wading_loop = results[2]["wading"].As<AudioStreamWav>();
        _footsteps = results[3]["footsteps"].AsGodotDictionary<StringName, AudioDirector.FootstepSet>();
        _interactions = results[3]["interactions"].AsGodotDictionary<StringName, AudioStreamWav>();
        _owl_calls = results[4]["owls"].AsGodotArray<AudioStreamWav>();
        _bird_calls = results[4]["birds"].AsGodotArray<AudioStreamWav>();
    }

    public static AudioStreamWav _recorded_loop(string path)
    {
        AudioStreamWav stream = Content.Load(path).Duplicate() as AudioStreamWav;
        stream.LoopBegin = 0;
        stream.LoopEnd = (int)(long)round(stream.GetLength() * stream.MixRate);
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        return stream;
    }

    public static AudioStreamWav make_fire_roar(long seed_value)
    {
        /// Historical synthesis recipes below remain available to DSP tests; the live
        /// fire and wind players use the credited recordings adopted above.
        /// The body of a wood fire: a low brown-noise rumble that pulses at 5..12 Hz
        /// (air drawn into the flames), a 250..700 Hz "flapping" band under its own
        /// slower bursts, and sparse bright sizzle. Mono, 6 s, seamless.
        long n = Synth.frames(6.0);
        List<float> roar = Synth.low_pass(Synth.brown_noise(n, seed_value), 210.0, 0.9);
        Synth.multiply(roar, Synth.random_walk(n, seed_value + 1, 0.5, 0.45, 1.0));
        Synth.multiply(roar, Synth.offset(Synth.gain(Synth.random_walk(n, seed_value + 5, 8.0, 0.0, 1.0), 0.65), 0.35));
        List<float> white = Synth.white_noise(n, seed_value + 2);
        List<float> body = Synth.band_pass(new List<float>(white), 420.0, 0.6);
        Synth.multiply(body, Synth.power(Synth.random_walk(n, seed_value + 3, 4.5, 0.0, 1.0), 2.0));
        List<float> texture = Synth.high_pass(white, 2600.0);
        Synth.multiply(texture, Synth.power(Synth.random_walk(n, seed_value + 4, 14.0, 0.0, 1.0), 5.0));
        Synth.mix_into(roar, body, 0, 0.32);
        Synth.mix_into(roar, texture, 0, 0.1);
        Synth.soft_clip(roar, 1.3);
        Synth.normalize(roar, 0.85);
        return Synth.loop_stream(roar, false, 400.0);
    }

    public static AudioStreamWav make_crackle(long seed_value, long variant = 0)
    {
        /// Burning wood makes three kinds of noise: tiny ticks, mid pops and the odd
        /// loud snap. The variant index picks the kind (0-2 ticks, 3-5 pops, 6-7 snaps);
        /// pitch and length still vary per seed.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        List<float> pop = new();
        if (variant < 3)
        {
            double duration = rng.RandfRange(0.006f, 0.014f);
            long n = Synth.frames(duration);
            pop = Synth.noise_burst(n, seed_value + 1, rng.RandfRange(3800.0f, 7000.0f), 1.4, duration * 0.5, 0.0003);
            Synth.mix_into(pop, Synth.click(seed_value + 2, 0.002, 3500.0), 0, 1.0);
            Synth.fade_edges(pop, 0.1, 3.0);
            Synth.normalize(pop, 0.75);
        }
        else if (variant < 6)
        {
            double duration2 = rng.RandfRange(0.025f, 0.055f);
            long n2 = Synth.frames(duration2);
            double f = rng.RandfRange(1500.0f, 2600.0f);
            pop = Synth.noise_burst(n2, seed_value + 1, rng.RandfRange(1100.0f, 2400.0f), rng.RandfRange(2.5f, 4.5f), duration2 * 0.45, 0.0005);
            Synth.mix_into(pop, Synth.decaying_tone(n2, f, f * 0.6, duration2 * 0.35, 0.6, 0.0005), 0, 0.7);
            Synth.mix_into(pop, Synth.click(seed_value + 2, 0.0025, 2500.0), 0, 0.8);
            Synth.fade_edges(pop, 0.2, 6.0);
            Synth.normalize(pop, 0.88);
        }
        else
        {
            double duration3 = rng.RandfRange(0.09f, 0.15f);
            long n3 = Synth.frames(duration3);
            double f2 = rng.RandfRange(900.0f, 1400.0f);
            pop = Synth.decaying_tone(n3, f2, f2 * 0.55, 0.045, 0.8, 0.0004);
            Synth.mix_into(pop, Synth.noise_burst(n3, seed_value + 1, 1800.0, 1.2, 0.06, 0.0005), 0, 0.9);
            Synth.mix_into(pop, Synth.noise_burst(n3, seed_value + 3, 170.0, 1.5, 0.05, 0.001), 0, 0.55);
            Synth.mix_into(pop, Synth.click(seed_value + 2, 0.003, 2000.0), 0, 1.0);
            Synth.fade_edges(pop, 0.2, 12.0);
            Synth.normalize(pop, 0.95);
        }
        return Synth.encode(pop, false);
    }

    public static AudioStreamWav make_hiss(long seed_value)
    {
        /// Steam escaping from a log: a swelling high band with fast sizzle.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        double duration = rng.RandfRange(0.55f, 0.9f);
        long n = Synth.frames(duration);
        List<float> hiss = Synth.band_pass(Synth.white_noise(n, seed_value + 1), rng.RandfRange(3800.0f, 5200.0f), 0.6);
        Synth.multiply(hiss, Synth.exp_decay(n, duration * 0.7, 0.03));
        Synth.multiply(hiss, Synth.random_walk(n, seed_value + 2, 25.0, 0.3, 1.0));
        Synth.fade_edges(hiss, 5.0, 40.0);
        Synth.normalize(hiss, 0.7);
        return Synth.encode(hiss, false);
    }

    public static AudioStreamWav make_water_lapping(long seed_value)
    {
        /// Low lapping with space between swells and restrained splash detail. The longer
        /// take and independently offset shore emitters avoid a short recurring hiss.
        double seconds = 18.0;
        long n = Synth.frames(seconds);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        List<float> white = Synth.white_noise(n, seed_value + 1);
        List<float> lap = Synth.one_pole_low_pass(Synth.band_pass(new List<float>(white), 480.0, 0.65), 1800.0);
        List<float> envelope = Synth.silence(n);
        Godot.Collections.Array<long> splash_frames = new Godot.Collections.Array<long>();
        Godot.Collections.Array<double> splash_levels = new Godot.Collections.Array<double>();
        double t = rng.RandfRange(0.0f, 0.5f);
        while (t < seconds)
        {
            double width = rng.RandfRange(0.55f, 1.1f);
            double level = rng.RandfRange(0.35f, 1.0f);
            Synth.mix_into(envelope, Synth.hann(Synth.frames(width)), Synth.frames(t), level, true);
            splash_frames.Add(Synth.frames(t + width * 0.45));
            splash_levels.Add(level);
            t += rng.RandfRange(1.3f, 3.2f);
        }
        Synth.multiply(lap, envelope);
        List<float> gurgle = Synth.band_pass(white, 220.0, 1.2);
        Synth.multiply(gurgle, envelope);
        Synth.mix_into(lap, gurgle, 0, 0.7);
        for (long i = 0, i_end = (long)splash_frames.Count; i < i_end; i++)
        {
            List<float> burst = Synth.noise_burst(Synth.frames(rng.RandfRange(0.04f, 0.09f)), seed_value + 10 + i, rng.RandfRange(800.0f, 1900.0f), 0.7, 0.05, 0.003);
            Synth.mix_into(lap, burst, splash_frames[(int)i], splash_levels[(int)i] * 0.12, true);
        }
        Synth.normalize(lap, 0.8);
        return Synth.loop_stream(lap, false, 400.0);
    }

    public static AudioStreamWav make_canopy_rustle(long seed_value)
    {
        /// Subtle local leaves, gated by the same gusts as the recorded wind. The broad
        /// band moves slowly; it does not sweep like a short repeating noise effect.
        long n = Synth.frames(14.0);
        List<float> wind = Synth.white_noise(n, seed_value);
        List<float> leaves = new List<float>(wind);
        Synth.band_pass_sweep(wind, Synth.random_walk(n, seed_value + 1, 0.13, 450.0, 950.0), 0.55);
        List<float> gust = Synth.random_walk(n, seed_value + 2, 0.12, 0.0, 1.0);
        Synth.multiply(wind, gust);
        Synth.band_pass(leaves, 2100.0, 0.5);
        List<float> rustle_envelope = Synth.multiply(new List<float>(gust), gust);
        Synth.multiply(rustle_envelope, Synth.random_walk(n, seed_value + 3, 9.0, 0.15, 1.0));
        Synth.multiply(leaves, rustle_envelope);
        Synth.mix_into(wind, leaves, 0, 0.16);
        Synth.normalize(wind, 0.8);
        return Synth.loop_stream(wind, false, 500.0);
    }

    public static AudioStreamWav make_wind_bed(long seed_value)
    {
        /// Open-air wind as heard in a clearing: a brown-noise body under slow gust
        /// swells, a broad mid "whoosh" that follows the gusts, and a faint airy top
        /// that only appears in the strongest of them. Stereo, 8 s, seamless.
        long n = Synth.frames(8.0);
        List<float> gust = Synth.random_walk(n, seed_value + 1, 0.16, 0.0, 1.0);
        List<float> body = Synth.low_pass(Synth.brown_noise(n, seed_value), 260.0, 0.8);
        Synth.multiply(body, Synth.offset(Synth.gain(new List<float>(gust), 0.7), 0.3));
        List<float> whoosh = Synth.white_noise(n, seed_value + 2);
        Synth.band_pass_sweep(whoosh, Synth.random_walk(n, seed_value + 3, 0.4, 300.0, 700.0), 0.6);
        Synth.multiply(whoosh, Synth.power(new List<float>(gust), 2.0));
        List<float> air = Synth.band_pass(Synth.white_noise(n, seed_value + 4), 1400.0, 0.4);
        Synth.multiply(air, Synth.power(new List<float>(gust), 3.0));
        Synth.multiply(air, Synth.random_walk(n, seed_value + 5, 6.0, 0.2, 1.0));
        Synth.mix_into(body, whoosh, 0, 0.5);
        Synth.mix_into(body, air, 0, 0.12);
        Synth.soft_clip(body, 1.1);
        Synth.normalize(body, 0.8);
        return Synth.loop_stream(Synth.widen(body, 14.0, 0.6), true, 600.0);
    }

    public static AudioStreamWav make_cricket_bed(long seed_value)
    {
        /// 4..6 cricket "individuals", each a pulsed 4..5.3 kHz tone with its own chirp
        /// rhythm, pan and distance, stamped with wrap-around so the 8 s stereo loop is
        /// seamless by construction.
        double seconds = 8.0;
        long n = Synth.frames(seconds);
        List<float> bed = Synth.silence(n * 2);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        long individuals = rng.RandiRange(4, 6);
        for (long k = 0; k < individuals; k++)
        {
            long pulse_frames = Synth.frames(rng.RandfRange(0.012f, 0.024f));
            List<float> pulse = Synth.multiply(Synth.sine(pulse_frames, rng.RandfRange(4000.0f, 5300.0f)), Synth.hann(pulse_frames));
            double pulse_period = rng.RandfRange(0.028f, 0.045f);
            long pulses_per_chirp = rng.RandiRange(3, 6);
            double chirp_period = rng.RandfRange(0.35f, 0.9f);
            bool is_trill = rng.Randf() < 0.3;
            double pan = rng.RandfRange(-0.85f, 0.85f);
            double level = rng.RandfRange(0.25f, 1.0f);
            double t = rng.RandfRange(0.0f, (float)chirp_period);
            while (t < seconds)
            {
                if (rng.Randf() < 0.08)
                {
                    t += rng.RandfRange(0.8f, 2.5f);
                    continue;
                }
                long count = is_trill ? rng.RandiRange(8, 16) : pulses_per_chirp;
                double chirp_level = level * rng.RandfRange(0.7f, 1.0f);
                for (long p = 0; p < count; p++)
                {
                    Synth.stamp_stereo(bed, pulse, Synth.frames(t + p * pulse_period), pan, chirp_level, true);
                }
                t += chirp_period * rng.RandfRange(0.85f, 1.15f) + (is_trill ? count * pulse_period : 0.0);
            }
        }
        Synth.normalize(bed, 0.8);
        return Synth.loop_stream(bed, true, 0.0);
    }

    public static AudioStreamWav make_owl_call(long seed_value, double pan_position)
    {
        /// Stereo owl call panned to [param pan_position]; see [method owl_call_mono].
        return Synth.encode(Synth.pan(owl_call_mono(seed_value), pan_position), true);
    }

    public static List<float> owl_call_mono(long seed_value)
    {
        /// Two-note "hoo-hoo": slow-attack sines with a slight downward glide, a breathy
        /// band of noise and a low-pass for distance. Mono, 1.25 s.
        long n = Synth.frames(1.25);
        List<float> phrase = Synth.silence(n);
        List<List<float>> notes = new List<List<float>> { new List<float>(new List<float> { 0.0f, 0.32f, 355.0f, 338.0f, 1.0f }), new List<float>(new List<float> { 0.46f, 0.55f, 330.0f, 300.0f, 0.9f }) };
        for (long i = 0, i_end = (long)notes.Count; i < i_end; i++)
        {
            List<float> note = notes[(int)i];
            long m = Synth.frames(note[1]);
            List<float> curve = Synth.exp_curve(m, note[2], note[3]);
            List<float> tone = Synth.sine_from_curve(curve);
            Synth.mix_into(tone, Synth.sine_from_curve(Synth.gain(new List<float>(curve), 2.0), 0.18));
            List<float> envelope = Synth.adsr(m, 0.09, 0.05, 0.85, 0.16);
            Synth.multiply(tone, envelope);
            List<float> breath = Synth.band_pass(Synth.white_noise(m, seed_value + i), note[2] * 2.3, 2.5);
            Synth.multiply(breath, envelope);
            Synth.mix_into(tone, breath, 0, 0.12);
            Synth.mix_into(phrase, tone, Synth.frames(note[0]), note[4]);
        }
        Synth.low_pass(phrase, 2200.0);
        return Synth.normalize(phrase, 0.7);
    }

    public static AudioStreamWav make_bird_call(long species, long seed_value)
    {
        /// One call of [constant BIRD_SPECIES][species]: exponential frequency sweeps with
        /// optional vibrato (FM) or trill (AM), a soft second harmonic, fixed pan.
        Godot.Collections.Dictionary spec = BIRD_SPECIES[(int)(species % (long)BIRD_SPECIES.Count)];
        Godot.Collections.Array syllables = spec["syllables"].AsGodotArray();
        long repeat = spec["repeat"].AsInt64();
        double repeat_gap = spec["gap"].AsDouble();
        double pattern_seconds = 0.0;
        foreach (Variant syllable_item in syllables)
        {
            Godot.Collections.Dictionary syllable = syllable_item.AsGodotDictionary();
            pattern_seconds += G.to_float(syllable["dur"]) + G.to_float(G.get(syllable, "gap", 0.0));
        }
        long total = Synth.frames(pattern_seconds * repeat + repeat_gap * (repeat - 1) + 0.05);
        List<float> phrase = Synth.silence(total);
        double cursor = 0.0;
        for (long r = 0; r < repeat; r++)
        {
            foreach (Variant syllable_item2 in syllables)
            {
                Godot.Collections.Dictionary syllable2 = syllable_item2.AsGodotDictionary();
                double duration = syllable2["dur"].AsDouble();
                List<float> tone = _bird_syllable(duration, G.to_float(syllable2["f0"]), G.to_float(syllable2["f1"]), G.to_float(G.get(syllable2, "trill", 0.0)), G.to_float(G.get(syllable2, "vib", 0.0)), G.to_float(G.get(syllable2, "vib_depth", 0.0)));
                Synth.mix_into(phrase, tone, Synth.frames(cursor));
                cursor += duration + G.to_float(G.get(syllable2, "gap", 0.0));
            }
            cursor += repeat_gap;
        }
        Synth.fade_edges(phrase, 1.0, 10.0);
        Synth.normalize(phrase, 0.8 * G.to_float(spec["level"]));
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        return Synth.encode(Synth.pan(phrase, G.to_float(spec["pan"]) + rng.RandfRange(-0.05f, 0.05f)), true);
    }

    public static List<float> _bird_syllable(double duration, double f0, double f1, double trill_hz, double vibrato_hz, double vibrato_depth)
    {
        long m = Synth.frames(duration);
        List<float> curve = Synth.exp_curve(m, f0, f1);
        if (vibrato_hz > 0.0)
        {
            Synth.multiply(curve, Synth.lfo(m, vibrato_hz, 1.0, vibrato_depth));
        }
        List<float> tone = Synth.sine_from_curve(curve);
        Synth.mix_into(tone, Synth.sine_from_curve(Synth.gain(new List<float>(curve), 2.0), 0.15));
        List<float> envelope = Synth.adsr(m, 0.006, duration * 0.3, 0.6, duration * 0.3);
        if (trill_hz > 0.0)
        {
            Synth.multiply(envelope, Synth.lfo(m, trill_hz, 0.55, 0.45));
        }
        return Synth.multiply(tone, envelope);
    }

    public static AudioStreamWav make_drone_partial(long index)
    {
        /// One drone partial as a 1 s loop-synchronous stereo stream (every partial is an
        /// integer number of Hz). Sine partials are pure; the "saw" partial is low-passed
        /// cyclically (filter warmed up over one period) so the loop point stays continuous.
        Godot.Collections.Dictionary spec = DRONE_PARTIALS[(int)(index % (long)DRONE_PARTIALS.Count)];
        long n = Synth.frames(1.0);
        double frequency = DRONE_BASE_HZ * G.to_float(spec["ratio"]);
        List<float> mono = new();
        if (G.truthy(spec["saw"]))
        {
            List<float> doubled = Synth.saw(n * 2, frequency);
            Synth.low_pass(doubled, 240.0, 0.8);
            mono = G.slice(doubled, n);
        }
        else
        {
            mono = Synth.sine(n, frequency);
        }
        Synth.normalize(mono, 0.6);
        return Synth.loop_stream(Synth.pan(mono, G.to_float(spec["pan"])), true, 0.0);
    }

    public static AudioStreamWav make_wading_loop(long seed_value)
    {
        /// Continuous slosh for wading: a wandering mid band with churning amplitude, a
        /// low body and scattered splash transients. Mono, 4 s, seamless.
        long n = Synth.frames(4.0);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        List<float> slosh = Synth.white_noise(n, seed_value + 1);
        Synth.band_pass_sweep(slosh, Synth.random_walk(n, seed_value + 2, 3.0, 500.0, 1900.0), 0.9);
        List<float> churn = Synth.power(Synth.random_walk(n, seed_value + 3, 2.5, 0.0, 1.0), 1.5);
        Synth.multiply(slosh, Synth.offset(Synth.gain(churn, 0.7), 0.3));
        List<float> body = Synth.band_pass(Synth.white_noise(n, seed_value + 4), 180.0, 1.0);
        Synth.multiply(body, Synth.random_walk(n, seed_value + 5, 1.5, 0.2, 1.0));
        Synth.mix_into(slosh, body, 0, 0.5);
        for (long i = 0; i < 10; i++)
        {
            List<float> burst = Synth.noise_burst(Synth.frames(rng.RandfRange(0.03f, 0.07f)), seed_value + 20 + i, rng.RandfRange(1500.0f, 3500.0f), 1.0, 0.04, 0.002);
            Synth.mix_into(slosh, burst, rng.RandiRange(0, (int)(n - 1)), rng.RandfRange(0.3f, 0.6f), true);
        }
        Synth.normalize(slosh, 0.8);
        return Synth.loop_stream(slosh, false, 250.0);
    }

    public static List<float> footstep_mono(StringName surface, long seed_value)
    {
        /// Mono footstep for one surface; see [method make_footstep] for the stereo wrapper.
        List<float> @out = new();
        if (surface == "grass")
        {
            @out = _footstep_grass(seed_value);
        }
        else if (surface == "dirt")
        {
            @out = _footstep_dirt(seed_value);
        }
        else if (surface == "wood")
        {
            @out = _footstep_wood(seed_value);
        }
        else if (surface == "water")
        {
            @out = _footstep_water(seed_value);
        }
        else if (surface == "rock")
        {
            @out = _footstep_rock(seed_value);
        }
        else
        {
            @out = _footstep_grass(seed_value);
        }
        Synth.fade_edges(@out, 0.5, 15.0);
        return Synth.normalize(@out, 0.85);
    }

    public static AudioStreamWav make_footstep(StringName surface, long seed_value, double pan_position = 0.0)
    {
        /// Stereo footstep one-shot for [param surface] panned to [param pan_position].
        return Synth.encode(Synth.pan(footstep_mono(surface, seed_value), pan_position), true);
    }

    public static List<float> _footstep_grass(long seed_value)
    {
        /// Heel, then toe: a soft low thump under a crushed-grass swish, and 70..110 ms
        /// later a lighter, brighter brush as the toe leaves. One burst read as a
        /// single generic "swish"; two events with a slow attack read as a step.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        long n = Synth.frames(0.32);
        List<float> heel = Synth.low_pass(Synth.noise_burst(n, seed_value, 900.0, 0.7, 0.11, 0.020), 2200.0);
        Synth.mix_into(heel, Synth.decaying_tone(n, 90.0, 55.0, 0.08, 0.5, 0.004), 0, 0.8);
        long toe_offset = Synth.frames(rng.RandfRange(0.07f, 0.11f));
        List<float> toe = Synth.low_pass(Synth.noise_burst(n - toe_offset, seed_value + 7, 1500.0, 0.8, 0.07, 0.012), 3200.0);
        Synth.mix_into(heel, toe, toe_offset, 0.5);
        return heel;
    }

    public static List<float> _footstep_dirt(long seed_value)
    {
        /// Dry crunch with grit crackles and a short thump, then the toe's lighter grit.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        long n = Synth.frames(0.3);
        List<float> crunch = Synth.noise_burst(n, seed_value, 2100.0, 0.8, 0.08, 0.006);
        for (long i = 0; i < 7; i++)
        {
            Synth.mix_into(crunch, Synth.click(seed_value + 1 + i, 0.002, 3000.0), rng.RandiRange(0, (int)Synth.frames(0.08)), rng.RandfRange(0.25f, 0.6f));
        }
        Synth.mix_into(crunch, Synth.decaying_tone(n, 80.0, 50.0, 0.06, 0.5, 0.004), 0, 0.9);
        long toe_offset = Synth.frames(rng.RandfRange(0.07f, 0.11f));
        List<float> toe = Synth.noise_burst(n - toe_offset, seed_value + 9, 2600.0, 0.9, 0.05, 0.008);
        for (long i2 = 0; i2 < 3; i2++)
        {
            Synth.mix_into(toe, Synth.click(seed_value + 20 + i2, 0.0015, 3500.0), rng.RandiRange(0, (int)Synth.frames(0.04)), rng.RandfRange(0.2f, 0.45f));
        }
        Synth.mix_into(crunch, toe, toe_offset, 0.45);
        return crunch;
    }

    public static List<float> _footstep_wood(long seed_value)
    {
        /// Resonant hollow knock around 180 Hz with a click and a short body resonance.
        long n = Synth.frames(0.3);
        List<float> knock = Synth.decaying_tone(n, 200.0, 175.0, 0.16, 1.0, 0.001);
        Synth.mix_into(knock, Synth.decaying_tone(n, 340.0, 330.0, 0.07, 0.35, 0.001));
        Synth.mix_into(knock, Synth.noise_burst(n, seed_value, 420.0, 3.0, 0.035, 0.001), 0, 0.35);
        Synth.mix_into(knock, Synth.click(seed_value + 1, 0.003, 1800.0), 0, 0.6);
        return knock;
    }

    public static List<float> _footstep_water(long seed_value)
    {
        /// Splash whose band-pass drops in pitch, a low plunk and a few rising bubble blips.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        long n = Synth.frames(0.4);
        List<float> splash = Synth.white_noise(n, seed_value);
        Synth.band_pass_sweep(splash, Synth.exp_curve(n, 2600.0, 900.0), 1.0);
        Synth.multiply(splash, Synth.exp_decay(n, 0.22, 0.006));
        Synth.mix_into(splash, Synth.decaying_tone(n, 160.0, 90.0, 0.1, 0.45, 0.002));
        long blip_frames = Synth.frames(0.04);
        for (long i = 0; i < 5; i++)
        {
            List<float> blip = Synth.decaying_tone(blip_frames, rng.RandfRange(380.0f, 520.0f), rng.RandfRange(800.0f, 1000.0f), 0.03, 0.3, 0.002);
            Synth.mix_into(splash, blip, Synth.frames(rng.RandfRange(0.1f, 0.32f)));
        }
        return splash;
    }

    public static List<float> _footstep_rock(long seed_value)
    {
        /// Sharp click followed by a short grainy scrape and a small knock.
        long n = Synth.frames(0.2);
        long scrape_offset = Synth.frames(0.012);
        List<float> scrape = Synth.noise_burst(n - scrape_offset, seed_value, 3600.0, 1.4, 0.07, 0.008);
        Synth.multiply(scrape, Synth.random_walk(n - scrape_offset, seed_value + 1, 70.0, 0.2, 1.0));
        List<float> step = Synth.silence(n);
        Synth.mix_into(step, Synth.click(seed_value + 2, 0.004, 3000.0), 0, 1.0);
        Synth.mix_into(step, scrape, scrape_offset, 0.5);
        Synth.mix_into(step, Synth.decaying_tone(n, 260.0, 240.0, 0.04, 0.3, 0.001));
        return step;
    }

    public static AudioStreamWav make_interaction(StringName kind, long seed_value)
    {
        /// Mono one-shot for an interaction kind (see [constant INTERACTIONS]).
        List<float> @out = new();
        if (kind == "log_added")
        {
            @out = _interaction_log_added(seed_value);
        }
        else if (kind == "lantern_toggle")
        {
            @out = _interaction_lantern(seed_value);
        }
        else if (kind == "pickup")
        {
            @out = _interaction_pickup(seed_value);
        }
        else if (kind == "ui")
        {
            @out = _interaction_ui(seed_value);
        }
        else
        {
            @out = _interaction_ui(seed_value);
        }
        Synth.fade_edges(@out, 0.5, 12.0);
        Synth.normalize(@out, 0.85);
        return Synth.encode(@out, false);
    }

    public static List<float> _interaction_log_added(long seed_value)
    {
        /// Wood placement thud; the recorded fire accent is played separately in space.
        long n = Synth.frames(0.45);
        List<float> thud = Synth.noise_burst(n, seed_value, 180.0, 0.7, 0.10, 0.004);
        Synth.mix_into(thud, Synth.noise_burst(n, seed_value + 1, 720.0, 0.8, 0.04, 0.002), 0, 0.35);
        return thud;
    }

    public static List<float> _interaction_lantern(long seed_value)
    {
        /// Small metallic click: two high partials, a click and a tiny body.
        long n = Synth.frames(0.14);
        List<float> @out = Synth.decaying_tone(n, 2100.0, 2050.0, 0.05, 0.6, 0.0005);
        Synth.mix_into(@out, Synth.decaying_tone(n, 3300.0, 3250.0, 0.035, 0.4, 0.0005));
        Synth.mix_into(@out, Synth.decaying_tone(n, 600.0, 580.0, 0.02, 0.3, 0.0005));
        Synth.mix_into(@out, Synth.click(seed_value, 0.002, 3000.0), 0, 0.8);
        return @out;
    }

    public static List<float> _interaction_pickup(long seed_value)
    {
        /// Soft cloth/metal rustle with a faint ping.
        long n = Synth.frames(0.3);
        List<float> rustle = Synth.noise_burst(n, seed_value, 1800.0, 0.8, 0.14, 0.01);
        Synth.multiply(rustle, Synth.random_walk(n, seed_value + 1, 45.0, 0.2, 1.0));
        Synth.mix_into(rustle, Synth.decaying_tone(Synth.frames(0.1), 2600.0, 2550.0, 0.06, 0.18, 0.0005), Synth.frames(0.05));
        return rustle;
    }

    public static List<float> _interaction_ui(long seed_value)
    {
        /// Very subtle tick.
        long n = Synth.frames(0.06);
        List<float> @out = Synth.decaying_tone(n, 1500.0, 1500.0, 0.025, 0.5, 0.0005);
        Synth.mix_into(@out, Synth.click(seed_value, 0.0015, 2500.0), 0, 0.6);
        return @out;
    }

    public void _build_players(Godot.Collections.Array<Vector3> shore_points, Godot.Collections.Array<Vector3> canopy_points)
    {
        // ---------------------------------------------------------------------------
        // Players
        // ---------------------------------------------------------------------------
        _water_entry = _make_player("WaterEntry", _preload_water_entry, BUS_WATER_FOLEY, -4.0);
        _water_exit = _make_player("WaterExit", _preload_water_exit, BUS_WATER_FOLEY, -5.0);
        _splash = make_water_impact();
        _water_impacts.players.Clear();
        for (long i = 0; i < 3; i++)
        {
            _water_impacts.players.Add(_make_emitter(G.format("WaterImpact%d", i), _splash, BUS_SFX, Vector3.Zero, 5.0, 35.0, -12.0));
        }
        _score = null;
        _fire_player = _make_emitter("Fire", _fire_roar, BUS_SFX, _fire_position, 2.5, 32.0, FIRE_DB);
        _fire_player.AttenuationFilterCutoffHz = 6500.0f;

        _fire_feed_player = _make_emitter("FireFeed", _preload_fire_feed, BUS_SFX, _fire_position, 2.0, 26.0, FIRE_FEED_DB);
        _fire_feed_player.AttenuationFilterCutoffHz = 6500.0f;

        _shore_players.Clear();
        for (long i2 = 0, i_end = (long)shore_points.Count; i2 < i_end; i2++)
        {
            _shore_players.Add(_make_emitter(G.format("Shore%d", i2), _water_loop, BUS_AMBIENCE, shore_points[(int)i2], 2.6, 22.0, WATER_DB));
        }

        _canopy_players.Clear();
        _canopy_phases.Clear();
        for (long i3 = 0, i_end2 = (long)canopy_points.Count; i3 < i_end2; i3++)
        {
            AudioStreamPlayer3D emitter = _make_emitter(G.format("Canopy%d", i3), _canopy_loop, BUS_AMBIENCE, canopy_points[(int)i3], 5.0, 34.0, CANOPY_MIN_DB);
            emitter.AttenuationFilterCutoffHz = 7000.0f;
            _canopy_players.Add(emitter);
            // Phase from position so gusts travel across the trees instead of pulsing in unison.
            _canopy_phases.Add((float)(canopy_points[(int)i3].X * 0.09 + canopy_points[(int)i3].Z * 0.05));
        }

        _crickets_player = _make_player("Crickets", _cricket_bed, BUS_AMBIENCE, SILENT_DB);
        _wind_player = _make_player("Wind", _wind_loop, BUS_AMBIENCE, WIND_MIN_DB);
        _owl_player = _make_player("Owl", null, BUS_AMBIENCE, OWL_DB);

        _bird_pool.players.Clear();
        for (long i4 = 0; i4 < BIRD_POOL_SIZE; i4++)
        {
            _bird_pool.players.Add(_make_player(G.format("Bird%d", i4), null, BUS_AMBIENCE, BIRDS_MAX_DB));
        }

        _footstep_pool.players.Clear();
        for (long i5 = 0; i5 < FOOTSTEP_POOL_SIZE; i5++)
        {
            _footstep_pool.players.Add(_make_player(G.format("Footstep%d", i5), null, BUS_SFX, -12.0));
        }

        _wading_player = _make_player("Wading", _wading_loop, BUS_SFX, SILENT_DB);

        _interact_pool.players.Clear();
        for (long i6 = 0; i6 < INTERACT_POOL_SIZE; i6++)
        {
            _interact_pool.players.Add(_make_player(G.format("Interact%d", i6), null, BUS_SFX, -12.0));
        }
    }

    public AudioStreamPlayer3D _make_emitter(string node_name, AudioStreamWav stream, StringName bus, Vector3 at, double unit_size, double max_distance, double volume_db)
    {
        AudioStreamPlayer3D emitter = new AudioStreamPlayer3D();
        emitter.Name = node_name;
        emitter.Stream = stream;
        emitter.Bus = bus;
        emitter.Position = at;
        emitter.UnitSize = (float)unit_size;
        emitter.MaxDistance = (float)max_distance;
        emitter.VolumeDb = (float)volume_db;
        emitter.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
        AddChild(emitter);
        return emitter;
    }

    public AudioStreamPlayer _make_player(string node_name, AudioStreamWav stream, StringName bus, double volume_db)
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
        _fire_player.Play(_rng.RandfRange(0.0f, (float)_fire_roar.GetLength()));
        foreach (AudioStreamPlayer3D player in _shore_players)
        {
            // Offsets and slight detune decorrelate shore points that share one loop.
            player.PitchScale = _rng.RandfRange(0.985f, 1.015f);
            player.Play(_rng.RandfRange(0.0f, (float)_water_loop.GetLength()));
        }
        foreach (AudioStreamPlayer3D player2 in _canopy_players)
        {
            player2.Play(_rng.RandfRange(0.0f, (float)_canopy_loop.GetLength()));
        }
        _wind_player.Stop();
        _wind_envelope = 0.0;
        _wind_gust_age = -1.0;
        _crickets_player.Play(_rng.RandfRange(0.0f, (float)_cricket_bed.GetLength()));
        _fade_elapsed = 0.0;
        _apply_start_fade();
    }

    public void _clear_players()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _loops_started = false;
        _is_setup = false;
    }

    public void _refresh_targets()
    {
        // ---------------------------------------------------------------------------
        // Per-frame mixing
        // ---------------------------------------------------------------------------
        /// Recomputes every smoothed target from the current API state.
        double rain = 0.0;
        if (GodotObject.IsInstanceValid(Game.Instance.world) && GodotObject.IsInstanceValid(Game.Instance.world.weather))
        {
            rain = Game.Instance.world.weather.rain;
        }
        double wildlife = 1.0 - smoothstep(0.08, 0.5, rain);
        _bird_activity = smoothstep(0.3, 0.75, _daylight) * wildlife;
        _cricket_activity = (1.0 - smoothstep(0.25, 0.65, _daylight)) * wildlife;

        _birds_db.target = _bird_activity < 0.05 ? SILENT_DB : lerpf(BIRDS_MIN_DB, BIRDS_MAX_DB, _bird_activity);
        _crickets_db.target = _cricket_activity < 0.02 ? SILENT_DB : lerpf(CRICKETS_MIN_DB, CRICKETS_MAX_DB, _cricket_activity);

        if (_fire_intensity < 0.01)
        {
            _fire_db.target = SILENT_DB;
        }
        else
        {
            _fire_db.target = FIRE_DB + 3.0 * log(_fire_intensity) / log(2.0);
        }
        _fire_pitch.target = 0.985 + 0.015 * _fire_intensity;

        _canopy_db.target = lerpf(CANOPY_MIN_DB, CANOPY_MAX_DB, _wind);
        _wind_db.target = lerpf(WIND_MIN_DB, WIND_MAX_DB, _wind);
        _water_db.target = WATER_DB + 3.0 * _wind;

        _wading_db.target = _wading_active ? lerpf(WADING_MIN_DB, WADING_MAX_DB, _wading_speed) : SILENT_DB;
        _wading_pitch.target = 0.85 + 0.3 * _wading_speed;
    }

    public void _snap_all()
    {
        foreach (Variant smoothed_item in new Godot.Collections.Array { _fire_db, _fire_pitch, _canopy_db, _wind_db, _water_db, _crickets_db, _birds_db, _wading_db, _wading_pitch })
        {
            AudioDirector.SmoothedValue smoothed = smoothed_item.As<AudioDirector.SmoothedValue>();
            smoothed.snap();
        }
    }

    public void _reset_schedulers()
    {
        _wind_gust_timer.reset(_rng.RandfRange(2.0f, 6.0f));
        _wind_gust_age = -1.0;
        _wind_envelope = 0.0;
        _owl_timer.reset(_rng.RandfRange(8.0f, 25.0f));
        _bird_burst_timer.reset(_rng.RandfRange(12.0f, 25.0f));
        _bird_calls_left = 0;
        _last_bird_bank = -1;
        G.resize(_bird_last_played, (int)(long)_bird_calls.Count);
        G.fill(_bird_last_played, -BIRD_BANK_COOLDOWN_SECONDS);
    }

    public void _update_fire(double delta)
    {
        _fire_flare_db = lerpf(_fire_flare_db, 0.0, 1.0 - exp(-delta / 2.0));
        // The recording supplies its own air draw and transients. Additional periodic
        // amplitude modulation made the old noise bed sound like rustling foliage.
        _fire_player.VolumeDb = (float)(_fire_db.step(delta) + _fire_flare_db);
        _fire_player.PitchScale = (float)_fire_pitch.step(delta);
    }

    public void _update_canopy(double delta)
    {
        double base_db = _canopy_db.step(delta);
        for (long i = 0, i_end = (long)_canopy_players.Count; i < i_end; i++)
        {
            double phase = _canopy_phases[(int)i];
            double local_swell = 0.75 + 0.25 * sin(_time * 0.13 + phase);
            double envelope = _wind_envelope * local_swell;
            AudioStreamPlayer3D player = _canopy_players[(int)i];
            player.VolumeDb = (float)maxf(SILENT_DB, base_db + linear_to_db(maxf(envelope, 0.0001)));
            player.PitchScale = 1.0f;
        }
    }

    public void _update_wind(double delta)
    {
        /// A recorded gust rises and falls, followed by a real rest. Random offsets in
        /// the long filtered take avoid bringing back a recognizable short noise loop.
        double level = _wind_db.step(delta);
        if (_wind_gust_age < 0.0)
        {
            if (_wind_gust_timer.tick(delta))
            {
                _wind_gust_age = 0.0;
                _wind_gust_duration = _rng.RandfRange(6.0f, 12.0f);
                _wind_player.Play(_rng.RandfRange(0.0f, (float)(_wind_loop.GetLength() - _wind_gust_duration)));
            }
        }
        else
        {
            _wind_gust_age += delta;
            if (_wind_gust_age >= _wind_gust_duration)
            {
                _wind_gust_age = -1.0;
                _wind_gust_timer.reset(_rng.RandfRange(18.0f, 40.0f) * lerpf(1.0, 0.7, _wind));
                _wind_player.Stop();
            }
        }
        _wind_envelope = 0.0;
        if (_wind_gust_age >= 0.0)
        {
            _wind_envelope = smoothstep(0.0, 3.5, _wind_gust_age) * (1.0 - smoothstep(_wind_gust_duration - 4.5, _wind_gust_duration, _wind_gust_age));
        }
        _wind_player.VolumeDb = (float)maxf(SILENT_DB, level + linear_to_db(maxf(_wind_envelope, 0.0001)));
    }

    public void _update_water(double delta)
    {
        double level = _water_db.step(delta);
        foreach (AudioStreamPlayer3D player in _shore_players)
        {
            player.VolumeDb = (float)level;
        }
    }

    public void _update_beds(double delta)
    {
        _crickets_player.VolumeDb = (float)(_crickets_db.step(delta) + 1.5 * sin(_time * 0.21));
        _birds_db.step(delta);
    }

    public void _update_wading(double delta)
    {
        double level = _wading_db.step(delta);
        _wading_player.PitchScale = (float)_wading_pitch.step(delta);
        if (_wading_active && !_wading_player.Playing)
        {
            _wading_player.Play(_rng.RandfRange(0.0f, (float)_wading_loop.GetLength()));
        }
        else if (!_wading_active && _wading_player.Playing && level < SILENT_DB + 20.0)
        {
            _wading_player.Stop();
        }
        _wading_player.VolumeDb = (float)level;
    }

    public void _schedule_owl(double delta)
    {
        if (!_owl_timer.tick(delta))
        {
            return;
        }
        _owl_timer.reset(_rng.RandfRange(20.0f, 60.0f));
        if (_cricket_activity < 0.6 || !IsInsideTree())
        {
            return;
        }
        _owl_player.Stream = _owl_calls[_rng.RandiRange(0, (int)((long)_owl_calls.Count - 1))];
        _owl_player.PitchScale = _rng.RandfRange(0.95f, 1.05f);
        _owl_player.VolumeDb = (float)(OWL_DB + _rng.RandfRange(-3.0f, 1.0f));
        _owl_player.Play();
    }

    public void _schedule_birds(double delta)
    {
        /// Each bank is already a whole phrase, sometimes with several syllables. Leave
        /// room after one or two phrases; never restart a pending burst through a storm.
        if (_bird_activity < 0.05)
        {
            _bird_calls_left = 0;
            _bird_burst_timer.remaining = maxf(_bird_burst_timer.remaining, 12.0);
            return;
        }
        if (_bird_calls_left > 0)
        {
            if (_bird_call_timer.tick(delta))
            {
                _bird_calls_left -= 1;
                _bird_call_timer.reset(_rng.RandfRange(0.9f, 2.4f));
                _play_bird();
            }
            return;
        }
        if (!_bird_burst_timer.tick(delta))
        {
            return;
        }
        _bird_burst_timer.reset(_rng.RandfRange(12.0f, 35.0f) / _bird_activity);
        _bird_calls_left = _rng.RandiRange(1, 2);
        _bird_call_timer.reset(0.0);
    }

    public void _play_bird()
    {
        if (!IsInsideTree())
        {
            return;
        }
        long bank = _next_bird_bank();
        if (bank < 0)
        {
            return;
        }
        AudioStreamPlayer player = _bird_pool.next();
        player.Stream = _bird_calls[(int)bank];
        player.PitchScale = _rng.RandfRange(0.94f, 1.06f);
        player.VolumeDb = (float)(_birds_db.value + _rng.RandfRange(-3.0f, 2.0f));
        player.Play();
    }

    public long _next_bird_bank()
    {
        Godot.Collections.Array<long> eligible = new Godot.Collections.Array<long>();
        for (long bank = 0, bank_end = (long)_bird_calls.Count; bank < bank_end; bank++)
        {
            if (bank != _last_bird_bank && _time - _bird_last_played[(int)bank] >= BIRD_BANK_COOLDOWN_SECONDS)
            {
                eligible.Add(bank);
            }
        }
        if ((eligible.Count == 0))
        {
            return -1;
        }
        long selected = eligible[_rng.RandiRange(0, (int)((long)eligible.Count - 1))];
        _last_bird_bank = selected;
        _bird_last_played[(int)selected] = _time;
        return selected;
    }
    private static AudioStreamWav _preload_water_entry => _preload_water_entry_cache ??= Content.Load<AudioStreamWav>("res://audio/water_entry.wav");
    private static AudioStreamWav _preload_water_entry_cache;
    private static AudioStreamWav _preload_water_exit => _preload_water_exit_cache ??= Content.Load<AudioStreamWav>("res://audio/water_exit.wav");
    private static AudioStreamWav _preload_water_exit_cache;
    private static AudioStreamWav _preload_fire_feed => _preload_fire_feed_cache ??= Content.Load<AudioStreamWav>("res://audio/fire_feed.wav");
    private static AudioStreamWav _preload_fire_feed_cache;
    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.audio, this)) Game.Instance.audio = null;
    }

}
