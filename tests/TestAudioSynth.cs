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
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

public partial class TestAudioSynth : TestCase
{
    public void test_recorded_transitions_have_clean_edges_and_mix_headroom()
    {
        foreach (Variant path in new Godot.Collections.Array { "res://audio/water_entry.wav", "res://audio/water_exit.wav", "res://audio/thunder.wav" })
        {
            // Read the source PCM; imported samples may use Godot's QOA codec,
            // whose bytes are not signed 16-bit PCM for Synth.decode().
            AudioStreamWav stream = AudioStreamWav.LoadFromFile(path.AsString(), new Godot.Collections.Dictionary { { "compress/mode", 0 } });
            List<float> samples = Synth.decode(stream);
            assert_gt(Synth.peak(samples), 0.3, G.format("%s contains the recorded cue", path));
            assert_lt(Synth.peak(samples), 0.9, G.format("%s leaves summing headroom", path));
            assert_near(samples[0], 0.0, 0.0001, G.format("%s starts without a click", path));
            assert_near(samples[(int)((long)samples.Count - 1)], 0.0, 0.0001, G.format("%s ends without a click", path));
            assert_gt(stream.GetLength(), 2.0, G.format("%s retains its water or thunder tail", path));
        }
        AudioStreamOggVorbis rain = Content.Load<AudioStreamOggVorbis>("res://audio/rain.ogg");
        assert_gt(rain.GetLength(), 40.0, "the storm uses a sustained recording without a short repeated hiss");
    }

    public void test_recorded_nature_keeps_loop_seams_and_headroom()
    {
        foreach (Variant path in new Godot.Collections.Array { "res://audio/fire_loop.wav", "res://audio/wind_trees.wav" })
        {
            AudioStreamWav pcm = AudioStreamWav.LoadFromFile(path.AsString(), new Godot.Collections.Dictionary { { "compress/mode", 0 } });
            List<float> samples = Synth.decode(pcm);
            assert_gt(pcm.GetLength(), 25.0, "recorded nature is not a short synthetic loop");
            assert_lt(Synth.peak(samples), 0.8, "recorded nature leaves mix headroom");
            long channels = pcm.Stereo ? 2 : 1;
            for (long channel = 0; channel < channels; channel++)
            {
                assert_near(samples[(int)channel], samples[(int)((long)samples.Count - channels + channel)], 0.04, "the recorded loop seam has no large discontinuity");
            }
            AudioStreamWav loop = AudioDirector._recorded_loop(path.AsString());
            assert_eq((long)loop.LoopMode, (long)AudioStreamWav.LoopModeEnum.Forward, "imported recording loops");
            assert_eq(loop.LoopEnd, (long)round(pcm.GetLength() * pcm.MixRate), "loop covers the complete recording after import");
        }
        AudioStreamWav feed = AudioStreamWav.LoadFromFile("res://audio/fire_feed.wav", new Godot.Collections.Dictionary { { "compress/mode", 0 } });
        List<float> accent = Synth.decode(feed);
        assert_false(feed.Stereo, "feeding the fire uses a positional mono take");
        assert_near(accent[0], 0.0, 0.0001, "feed accent starts cleanly");
        assert_near(accent[(int)((long)accent.Count - 1)], 0.0, 0.0001, "feed accent fades cleanly");
        assert_lt(Synth.peak(accent), 0.6, "feed accent has summing headroom");
    }

    public void test_original_score_has_headroom_and_fades()
    {
        AudioStreamWav score = CampScore.build();
        List<float> samples = Synth.decode(score);
        assert_true(score.Stereo, "score is stereo");
        assert_gt(Synth.peak(samples), 0.05, "score contains audible music");
        assert_lt(Synth.peak(samples), 0.8, "score leaves headroom for nature and effects");
        assert_near(samples[0], 0.0, 0.0001, "score opens without a click");
        assert_near(samples[(int)((long)samples.Count - 1)], 0.0, 0.0001, "score closes without a click");
        assert_gt(Synth.rms(samples), 0.001, "score has sustained musical energy");
    }

    public void test_film_score_leaves_the_nature_passages_silent()
    {
        Godot.Collections.Array<Godot.Collections.Dictionary> cues = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"start", 0.0 }, { (StringName)"length", 10.0 }, { (StringName)"gain", 0.4 }, { (StringName)"melody", true }, { (StringName)"pads", false } } };
        List<float> samples = Synth.decode(CampScore.build(20.0, cues));
        assert_gt(Synth.rms(G.slice(samples, CampScore.RATE * 2, CampScore.RATE * 10)), 0.001, "the authored cue is audible");
        assert_near(Synth.peak(G.slice(samples, CampScore.RATE * 24, CampScore.RATE * 36)), 0.0, 1e-6, "uncued nature time contains no music");
        Godot.Collections.Array<Godot.Collections.Dictionary> film_cues = Cinematic.score_cues("one_night");
        assert_true((film_cues.Count == 0), "the current film contains no music anywhere");
    }

    public void test_empty_authored_score_does_not_start_the_demo_arrangement()
    {
        Godot.Collections.Array<Godot.Collections.Dictionary> cues = Cinematic.score_cues("storm");
        assert_true((cues.Count == 0), "the storm edit deliberately contains no music cues");
        List<float> samples = Synth.decode(CampScore.build(3.0, cues));
        assert_near(Synth.peak(samples), 0.0, 1e-6, "an explicit empty arrangement remains silent instead of composing the demo score");
        Godot.Collections.Array<Godot.Collections.Dictionary> no_voices = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"start", 0.0 }, { (StringName)"length", 3.0 }, { (StringName)"gain", 1.0 }, { (StringName)"melody", false }, { (StringName)"pads", false } } };
        List<float> without_pads = Synth.decode(CampScore.build(3.0, no_voices));
        assert_near(Synth.peak(without_pads), 0.0, 1e-6, "disabling the pad voices adds no sustained hum");
    }

    /// Headless smoke tests for the Synth toolbox and the AudioDirector.
    public static readonly Vector3 FIRE_POSITION = new Vector3(0.0f, 0.3f, 0.0f);
    public static readonly Godot.Collections.Array<Vector3> SHORE_POINTS = new Godot.Collections.Array<Vector3> { new Vector3(6.0f, 0.0f, 2.0f), new Vector3(8.0f, 0.0f, -3.0f) };
    public static readonly Godot.Collections.Array<Vector3> CANOPY_POINTS = new Godot.Collections.Array<Vector3> { new Vector3(-4.0f, 10.0f, 3.0f), new Vector3(5.0f, 10.0f, 7.0f), new Vector3(0.0f, 11.0f, -6.0f) };
    public const long GENERATION_BUDGET_MSEC = 2000;

    public void test_loops_end_at_frame_count()
    {
        Godot.Collections.Dictionary<string, AudioStreamWav> loops = new Godot.Collections.Dictionary<string, AudioStreamWav> { { "fire_roar", AudioDirector.make_fire_roar(1) }, { "water", AudioDirector.make_water_lapping(2) }, { "canopy", AudioDirector.make_canopy_rustle(3) }, { "crickets", AudioDirector.make_cricket_bed(4) }, { "drone_sine", AudioDirector.make_drone_partial(0) }, { "drone_saw", AudioDirector.make_drone_partial(3) }, { "wading", AudioDirector.make_wading_loop(5) } };
        foreach (string loop_name in loops.Keys)
        {
            AudioStreamWav stream = loops[loop_name];
            long frame_count = Synth.stream_frame_count(stream);
            assert_true(stream.Format == AudioStreamWav.FormatEnum.Format16Bits, G.format("%s is 16-bit", loop_name));
            assert_eq(stream.MixRate, Synth.SAMPLE_RATE, G.format("%s mix rate", loop_name));
            assert_true(stream.LoopMode == AudioStreamWav.LoopModeEnum.Forward, G.format("%s loops forward", loop_name));
            assert_eq(stream.LoopBegin, 0, G.format("%s loop_begin", loop_name));
            assert_eq(stream.LoopEnd, frame_count, G.format("%s loop_end equals frame count", loop_name));
            assert_true(frame_count >= Synth.frames(1.0), G.format("%s is at least one second long", loop_name));
            assert_lt(Synth.peak(Synth.decode(stream)), 1.0 + 1e-6, G.format("%s never clips", loop_name));
        }
        assert_true(loops["crickets"].Stereo, "cricket bed is stereo");
        assert_false(loops["fire_roar"].Stereo, "fire roar is mono");
        // The crossfade shortens the loop by exactly the fade length.

        double seconds = 1.0;
        double fade_ms = 100.0;
        AudioStreamWav direct = Synth.loop_stream(Synth.white_noise(Synth.frames(seconds), 9), false, fade_ms);
        long expected_frames = Synth.frames(seconds) - Synth.frames(fade_ms * 0.001);
        assert_eq(direct.LoopEnd, expected_frames, "crossfaded loop_end accounts for the fade");
        assert_eq(Synth.stream_frame_count(direct), expected_frames, "crossfaded frame count");
    }

    public void test_normalized_samples_stay_within_full_scale()
    {
        List<float> buffer = Synth.gain(Synth.white_noise(Synth.frames(0.5), 11), 4.0);
        assert_gt(Synth.peak(buffer), 1.0, "test signal exceeds full scale before normalisation");
        Synth.normalize(buffer, 0.9);
        assert_near(Synth.peak(buffer), 0.9, 1e-5, "normalised peak");

        List<float> decoded = Synth.decode(Synth.encode(buffer, false));
        assert_eq((long)decoded.Count, (long)buffer.Count, "decoded sample count");
        double highest = 0.0;
        for (long i = 0, i_end = (long)decoded.Count; i < i_end; i++)
        {
            highest = maxf(highest, absf(decoded[(int)i]));
        }
        assert_lt(highest, 1.0 + 1e-6, "no decoded sample exceeds +-1.0");
        assert_near(highest, 0.9, 2.0 / Synth.INT16_SCALE, "decoded peak survives 16-bit quantisation");
        // Soft clipping keeps even hot signals inside full scale.

        List<float> hot = Synth.soft_clip(Synth.gain(Synth.white_noise(4096, 12), 3.0), 1.5);
        assert_lt(Synth.peak(hot), 1.0 + 1e-6, "soft clip output stays within +-1.0");
    }

    public void test_pink_noise_has_less_high_frequency_energy_than_white()
    {
        long count = Synth.frames(1.0);
        double white_ratio = _difference_energy_ratio(Synth.white_noise(count, 21));
        double pink_ratio = _difference_energy_ratio(Synth.pink_noise(count, 21));
        double brown_ratio = _difference_energy_ratio(Synth.brown_noise(count, 21));
        assert_lt(pink_ratio, white_ratio * 0.5, "pink noise is darker than white noise");
        assert_lt(brown_ratio, pink_ratio, "brown noise is darker than pink noise");
    }

    public void test_filters_shape_the_spectrum()
    {
        long count = Synth.frames(0.5);
        double baseline = _difference_energy_ratio(Synth.white_noise(count, 31));
        double low = _difference_energy_ratio(Synth.low_pass(Synth.white_noise(count, 31), 1000.0));
        double high = _difference_energy_ratio(Synth.high_pass(Synth.white_noise(count, 31), 6000.0));
        double one_pole = _difference_energy_ratio(Synth.one_pole_low_pass(Synth.white_noise(count, 31), 500.0));
        assert_lt(low, baseline * 0.25, "biquad low-pass removes high-frequency energy");
        assert_gt(high, baseline * 1.2, "biquad high-pass removes low-frequency energy");
        assert_lt(one_pole, baseline * 0.5, "one-pole low-pass removes high-frequency energy");

        List<float> swept = Synth.band_pass_sweep(Synth.white_noise(count, 31), Synth.linear_curve(count, 300.0, 3000.0), 2.0);
        assert_gt(Synth.rms(swept), 0.0, "swept band-pass produces output");
        assert_lt(Synth.peak(swept), 2.0, "swept band-pass stays stable");
    }

    public void test_seamless_loop_removes_boundary_discontinuity()
    {
        // 100.25 cycles: the raw buffer ends a quarter cycle out of phase with its start.
        long count = Synth.frames(0.5);
        List<float> tone = Synth.sine(count, 200.5, 0.9);
        double raw_step = absf((double)tone[0] - tone[(int)(count - 1)]);
        assert_gt(raw_step, 0.5, "raw tone has a large loop discontinuity");

        double fade_ms = 50.0;
        List<float> seamless = Synth.make_seamless(tone, fade_ms, 1, Synth.SAMPLE_RATE, false);
        assert_eq((long)seamless.Count, count - Synth.frames(fade_ms * 0.001), "seamless buffer is shortened by the fade");
        double boundary_step = absf((double)seamless[0] - seamless[(int)((long)seamless.Count - 1)]);
        assert_lt(boundary_step, 0.05, "loop boundary step is below the click threshold");
        // The step at the loop point is no larger than the natural slope of the tone.
        assert_lt(boundary_step, absf((double)tone[1] - tone[0]) * 1.5, "boundary step is comparable to adjacent samples");

        List<float> noisy = Synth.make_seamless(Synth.low_pass(Synth.white_noise(count, 41), 800.0), 200.0);
        double noisy_step = absf((double)noisy[0] - noisy[(int)((long)noisy.Count - 1)]);
        assert_lt(noisy_step, 0.1, "noise loop boundary is continuous");
    }

    public void test_stereo_helpers_and_wrapped_stamping()
    {
        Vector2 gains = Synth.pan_gains(0.0);
        assert_near(gains.X, gains.Y, 1e-6, "centre pan is symmetric");
        assert_near((double)gains.X * gains.X + (double)gains.Y * gains.Y, 1.0, 1e-6, "pan is constant power");
        assert_near(Synth.pan_gains(-1.0).Y, 0.0, 1e-6, "hard left has no right signal");

        List<float> dest = Synth.silence(20);
        List<float> grain = Synth.constant(4, 1.0);
        Synth.stamp_stereo(dest, grain, 8, 0.0, 1.0, true);
        assert_gt(dest[16], 0.0, "stamp writes frame 8");
        assert_gt(dest[19], 0.0, "stamp writes frame 9 (right channel)");
        assert_gt(dest[0], 0.0, "stamp wraps to frame 0");
        assert_gt(dest[3], 0.0, "stamp wraps to frame 1 (right channel)");
        assert_eq(dest[4], 0.0, "stamp does not touch frame 2");

        List<float> mono = Synth.sine(1000, 100.0, 0.5);
        List<float> wide = Synth.widen(mono, 5.0, 0.5);
        assert_eq((long)wide.Count, 2000, "widen produces interleaved stereo");
        assert_lt(Synth.peak(wide), 1.0, "widen stays within full scale");
        List<float> left = new List<float>();
        List<float> right = new List<float>();
        for (long i = 0; i < 1000; i++)
        {
            left.Add(wide[(int)(i * 2)]);
            right.Add(wide[(int)(i * 2 + 1)]);
        }
        assert_eq(Variant.From(Synth.interleave(left, right).ToArray()), Variant.From(wide.ToArray()), "interleave inverts channel split");
    }

    public void test_envelopes_are_bounded()
    {
        List<float> envelope = Synth.adsr(1000, 0.002, 0.003, 0.5, 0.005);
        assert_near(envelope[0], 0.0, 1e-6, "adsr starts at zero");
        assert_lt(Synth.peak(envelope), 1.0 + 1e-6, "adsr never exceeds one");
        assert_near(envelope[999], 0.0, 0.01, "adsr ends near zero");
        List<float> decay = Synth.exp_decay(Synth.frames(0.1), 0.05);
        assert_near(decay[0], 1.0, 1e-6, "exp decay starts at one");
        assert_near(decay[(int)Synth.frames(0.05)], 0.001, 1e-4, "exp decay reaches -60 dB at the decay time");
    }

    public void test_generation_is_deterministic()
    {
        assert_eq(Variant.From(Synth.pink_noise(2048, 42).ToArray()), Variant.From(Synth.pink_noise(2048, 42).ToArray()), "pink noise is reproducible for a seed");
        assert_true(!G.ArrayEquals(Synth.pink_noise(2048, 42), Synth.pink_noise(2048, 43)), "different seeds differ");
        assert_eq(AudioDirector.make_crackle(5).Data, AudioDirector.make_crackle(5).Data, "crackle bytes are reproducible");
        assert_eq(AudioDirector.make_bird_call(2, 7).Data, AudioDirector.make_bird_call(2, 7).Data, "bird call bytes are reproducible");
    }

    public void test_footstep_surfaces_produce_streams()
    {
        List<List<byte>> seen = new List<List<byte>>();
        foreach (StringName surface in AudioDirector.SURFACES)
        {
            AudioStreamWav stream = AudioDirector.make_footstep(surface, 3, 0.1);
            assert_true(stream != null, G.format("%s footstep returns a stream", (StringName)surface));
            if (stream == null)
            {
                continue;
            }
            assert_gt((long)stream.Data.Length, 0, G.format("%s footstep has data", (StringName)surface));
            assert_true(stream.Stereo, G.format("%s footstep is stereo", (StringName)surface));
            List<float> decoded = Synth.decode(stream);
            double decoded_peak = Synth.peak(decoded);
            assert_gt(decoded_peak, 0.3, G.format("%s footstep has audible level", (StringName)surface));
            assert_lt(decoded_peak, 1.0 + 1e-6, G.format("%s footstep does not clip", (StringName)surface));
            assert_false(seen.Contains(new List<byte>(stream.Data)), G.format("%s footstep differs from other surfaces", (StringName)surface));
            seen.Add(new List<byte>(stream.Data));
        }

        foreach (StringName kind in AudioDirector.INTERACTIONS)
        {
            AudioStreamWav stream2 = AudioDirector.make_interaction(kind, 4);
            assert_gt((long)stream2.Data.Length, 0, G.format("%s interaction has data", (StringName)kind));
        }
        for (long species = 0, species_end = (long)AudioDirector.BIRD_SPECIES.Count; species < species_end; species++)
        {
            assert_gt((long)AudioDirector.make_bird_call(species, 1).Data.Length, 0, G.format("bird species %d has data", species));
        }
        assert_gt((long)AudioDirector.make_owl_call(1, -0.5).Data.Length, 0, "owl call has data");
        assert_gt((long)AudioDirector.make_hiss(1).Data.Length, 0, "hiss has data");
    }

    public void test_audio_director_setup_and_api()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        assert_true(tree != null, "tests run inside a SceneTree");
        if (tree == null)
        {
            return;
        }

        AudioDirector director = new AudioDirector();
        director.Name = "Audio";
        tree.Root.AddChild(director);
        director.setup(FIRE_POSITION, SHORE_POINTS, CANOPY_POINTS);
        G.print(G.format("  bank generation took %d ms", director.last_generation_msec));
        assert_true(director.is_ready(), "director reports ready after setup");
        assert_lt(director.last_generation_msec, GENERATION_BUDGET_MSEC, "bank generation fits the time budget");

        _assert_buses();
        _assert_players(director);

        director.set_daylight(0.5);
        director.set_fire_intensity(1.6);
        director.set_wind(0.8);
        director.set_master_volume(0.8);
        director.set_wading(true, 0.6);
        foreach (StringName surface in AudioDirector.SURFACES)
        {
            director.footstep(surface, false);
            director.footstep(surface, true);
        }
        foreach (StringName kind in AudioDirector.INTERACTIONS)
        {
            director.play_interact(kind);
        }
        assert_true(_any_playing(director, "Footstep", AudioDirector.FOOTSTEP_POOL_SIZE), "a footstep voice is playing");
        assert_true(_any_playing(director, "Interact", AudioDirector.INTERACT_POOL_SIZE), "an interaction voice is playing");
        assert_true(director.GetNode<AudioStreamPlayer3D>("FireFeed").Playing, "adding a log starts the recorded fire accent");
        assert_near(AudioServer.GetBusVolumeDb(0), linear_to_db(0.8), 1e-4, "master volume applied");

        _simulate(director, 4.0);
        assert_true(director.GetNodeOrNull("FireCrackle0") == null, "no synthetic tonal crackles are layered over the recording");
        AudioStreamPlayer wading = director.GetNode<AudioStreamPlayer>("Wading");
        assert_true(wading.Playing, "wading loop starts when wading");
        assert_gt(wading.VolumeDb, -30.0, "wading loop faded in");

        director.set_wading(false, 0.0);
        _simulate(director, 4.0);
        assert_false(wading.Playing, "wading loop stops after fading out");

        AudioStreamPlayer crickets = director.GetNode<AudioStreamPlayer>("Crickets");
        director.set_daylight(0.0);
        director._bird_calls_left = 2;
        director._schedule_birds(0.1);
        assert_eq(director._bird_calls_left, 0, "night clears a queued daytime bird burst");
        _simulate(director, 12.0);
        assert_gt(crickets.VolumeDb, AudioDirector.CRICKETS_MAX_DB - 4.0, "crickets fade up at night");
        director.set_daylight(1.0);
        _simulate(director, 12.0);
        assert_lt(crickets.VolumeDb, AudioDirector.CRICKETS_MIN_DB, "crickets fade out in daylight");

        AudioStreamPlayer3D fire = director.GetNode<AudioStreamPlayer3D>("Fire");
        director.set_fire_intensity(0.0);
        _simulate(director, 6.0);
        assert_lt(fire.VolumeDb, -60.0, "fire fades to silence when out");
        director.set_fire_intensity(2.0);
        _simulate(director, 6.0);
        assert_gt(fire.VolumeDb, AudioDirector.FIRE_DB, "roaring fire is louder than normal");
        assert_gt(fire.PitchScale, 1.0, "roaring fire is pitched up slightly");
        // Exercising the picker faster than normal scheduling proves the bank cooldown
        // and no-repeat policy independently of one lucky random film schedule.

        director._reset_schedulers();
        director._time = 100.0;
        Godot.Collections.Array<long> used = new Godot.Collections.Array<long>();
        for (long i = 0, i_end = (long)director._bird_calls.Count; i < i_end; i++)
        {
            long bank = director._next_bird_bank();
            assert_true(bank >= 0 && !used.Contains(bank), "a recent bird bank is not immediately reused");
            used.Add(bank);
        }
        assert_eq(director._next_bird_bank(), -1, "exhausted bird banks rest instead of repeating");
        director._time += AudioDirector.BIRD_BANK_COOLDOWN_SECONDS + 0.1;
        long last_bank = used[(int)((long)used.Count - 1)];
        assert_true(director._next_bird_bank() != last_bank, "bank reuse after cooldown still avoids consecutive duplicates");

        WorldController saved_world = Game.Instance.world;
        WorldController wet_world = new WorldController();
        wet_world.weather = new CampWeather();
        wet_world.weather.rain = 0.8;
        Game.Instance.world = wet_world;
        director.set_daylight(1.0);
        director._bird_calls_left = 2;
        director._schedule_birds(0.1);
        assert_eq(director._bird_calls_left, 0, "rain clears the last queued bird phrase");
        assert_near(director._bird_activity, 0.0, 0.0001, "day birds are suppressed in a storm");
        director.set_daylight(0.0);
        assert_near(director._cricket_activity, 0.0, 0.0001, "night insects are suppressed in a storm");
        Game.Instance.world = saved_world;
        wet_world.weather.Free();
        wet_world.Free();
        director.set_daylight(1.0);

        long rest_frames = 0;
        long gust_frames = 0;
        for (long i2 = 0; i2 < 600; i2++)
        {
            director._Process(0.1);
            if (director._wind_envelope <= 0.0)
            {
                rest_frames += 1;
            }
            else
            {
                gust_frames += 1;
            }
        }
        assert_gt(rest_frames, 50, "wind has real quiet intervals");
        assert_gt(gust_frames, 50, "wind still supplies gradual gusts between rests");

        director.set_master_volume(1.0);
        tree.Root.RemoveChild(director);
        director.Free();
    }

    public void _assert_buses()
    {
        foreach (Variant bus_name_item in new Godot.Collections.Array { (StringName)AudioDirector.BUS_AMBIENCE, (StringName)AudioDirector.BUS_SFX })
        {
            StringName bus_name = bus_name_item.AsStringName();
            long index = AudioServer.GetBusIndex(bus_name);
            assert_true(index != -1, G.format("%s bus exists", (StringName)bus_name));
            if (index == -1)
            {
                continue;
            }
            assert_eq((StringName)AudioServer.GetBusSend((int)index), (StringName)AudioDirector.BUS_MASTER, G.format("%s routes to Master", (StringName)bus_name));
            assert_gt(AudioServer.GetBusEffectCount((int)index), 0, G.format("%s has effects", (StringName)bus_name));
            assert_true(AudioServer.GetBusEffect((int)index, 0) is AudioEffectReverb, G.format("%s starts with reverb", (StringName)bus_name));
        }
    }

    public void _assert_players(AudioDirector director)
    {
        Godot.Collections.Array<string> expected_3d = new Godot.Collections.Array<string> { "Fire", "FireFeed", "Shore0", "Shore1", "Canopy0", "Canopy1", "Canopy2" };
        foreach (string node_name in expected_3d)
        {
            assert_true(director.GetNodeOrNull(node_name) is AudioStreamPlayer3D, G.format("%s is a 3D emitter", node_name));
        }
        Godot.Collections.Array<string> expected_2d = new Godot.Collections.Array<string> { "Crickets", "Owl", "Bird0", "Bird3", "Footstep0", "Footstep3", "Wading", "Interact0", "Interact1" };
        foreach (string node_name2 in expected_2d)
        {
            assert_true(director.GetNodeOrNull(node_name2) is AudioStreamPlayer, G.format("%s is a stream player", node_name2));
        }
        assert_true(director.GetNodeOrNull("Shore2") == null, "only one emitter per shore point");
        assert_true(director.GetNodeOrNull("Canopy3") == null, "only one emitter per canopy point");
        for (long i = 0, i_end = (long)AudioDirector.DRONE_PARTIALS.Count; i < i_end; i++)
        {
            assert_true(director.GetNodeOrNull(G.format("Drone%d", i)) == null, "the live ambience has no bass drone player");
        }

        AudioStreamPlayer3D fire = director.GetNode<AudioStreamPlayer3D>("Fire");
        assert_true(fire.Playing, "fire loop is playing");
        assert_eq(fire.Position, FIRE_POSITION, "fire emitter sits at the fire");
        assert_eq((StringName)fire.Bus, (StringName)AudioDirector.BUS_SFX, "fire uses the SFX bus");
        assert_near(fire.MaxDistance, 32.0, 1e-6, "fire is audible to about 30 m");
        AudioStreamPlayer3D shore = director.GetNode<AudioStreamPlayer3D>("Shore1");
        assert_eq(shore.Position, SHORE_POINTS[1], "shore emitter sits on its shore point");
        assert_true(shore.Playing, "water lapping is playing");
        AudioStreamPlayer3D canopy = director.GetNode<AudioStreamPlayer3D>("Canopy2");
        assert_eq(canopy.Position, CANOPY_POINTS[2], "canopy emitter sits on its canopy point");
        assert_eq((StringName)canopy.Bus, (StringName)AudioDirector.BUS_AMBIENCE, "canopy uses the Ambience bus");
        AudioStreamPlayer crickets = director.GetNode<AudioStreamPlayer>("Crickets");
        assert_true(crickets.Playing, "cricket bed is playing");
        assert_true((crickets.Stream as AudioStreamWav).Stereo, "cricket bed is stereo");
    }

    public void _simulate(AudioDirector director, double seconds)
    {
        double step = 0.1;
        long steps = (long)round(seconds / step);
        for (long i = 0; i < steps; i++)
        {
            director._Process(step);
        }
    }

    public bool _any_playing(AudioDirector director, string prefix, long count)
    {
        for (long i = 0; i < count; i++)
        {
            Node player = director.GetNodeOrNull(G.format("%s%d", new Godot.Collections.Array { prefix, i }));
            if (player is AudioStreamPlayer && ((AudioStreamPlayer)player).Playing)
            {
                return true;
            }
            if (player is AudioStreamPlayer3D && ((AudioStreamPlayer3D)player).Playing)
            {
                return true;
            }
        }
        return false;
    }

    public double _difference_energy_ratio(List<float> samples)
    {
        /// Energy of the first difference relative to the signal energy: a cheap proxy
        /// for how much high-frequency content a buffer holds.
        double diff_energy = 0.0;
        double energy = 0.0;
        for (long i = 1, i_end = (long)samples.Count; i < i_end; i++)
        {
            double d = (double)samples[(int)i] - samples[(int)(i - 1)];
            diff_energy += d * d;
            energy += (double)samples[(int)i] * samples[(int)i];
        }
        return diff_energy / maxf(energy, 1e-12);
    }
}
