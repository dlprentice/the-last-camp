using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using LastCamp.Story;

namespace LastCamp.Tests;

/// The manifest-driven sound layer and June's voice player. The licensed files may
/// be absent (a public clone): every check here holds either way, and the checks
/// that need the files say so when they are not run.
public partial class TestAudio : TestCase
{
    private static readonly Vector3 Fire = new(0, 0.5f, 0);
    private static readonly Godot.Collections.Array<Vector3> Lap = new() { new Vector3(4, 0, -8), new Vector3(5, 0, -9) };

    private static IEnumerable<string> ReferencedIds() =>
        typeof(Sounds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Concat(Sounds.Footsteps.Values).Concat(Sounds.Interactions).Concat(AudioDirector.CATEGORY.Keys);

    public void test_manifest_loads()
    {
        SoundBank bank = SoundBank.Load();
        assert_gt(bank.Entries.Count, 40, "the manifest lists the sound list's sounds");
        foreach (SoundBank.Entry entry in bank.Entries.Values)
        {
            assert_true(entry.Kind is "bed" or "oneshot", $"{entry.Id} is a bed or a one-shot");
            assert_gt(entry.Variants, 0, $"{entry.Id} names at least one file");
            assert_eq(entry.Lufs.Length, entry.Variants, $"{entry.Id} has a loudness per file");
            assert_true(entry.Lufs.All(l => l is < -10 and > -70), $"{entry.Id} loudness is a measured LUFS value");
        }
        assert_true(bank.Entries.Values.Where(e => e.IsBed).All(e => e.Id.StartsWith("bed_", StringComparison.Ordinal)), "only beds loop");
        assert_true(SoundBank.Parse("{ not json").Entries.Count == 0, "an unreadable manifest is an empty bank");
    }

    public void test_every_referenced_sound_is_in_the_manifest()
    {
        SoundBank bank = SoundBank.Load();
        foreach (string id in ReferencedIds().Distinct())
            assert_true(bank.Has(id), $"sound '{id}' used by the code is in the manifest");
        foreach (string id in typeof(Sounds).GetFields().Where(f => f.Name.StartsWith("Bed", StringComparison.Ordinal)).Select(f => (string)f.GetRawConstantValue()!))
            assert_true(bank.Entries.TryGetValue(id, out SoundBank.Entry? entry) && entry.IsBed, $"{id} is a bed");
        foreach (string category in ReferencedIds().Select(AudioDirector.category_of).Distinct())
            assert_true(AudioDirector.TARGET_LUFS.ContainsKey(category), $"category {category} has a loudness target");
    }

    public void test_files_are_levelled_offline_to_the_game_targets()
    {
        SoundBank bank = SoundBank.Load();
        foreach (SoundBank.Entry entry in bank.Entries.Values)
        {
            assert_near(entry.Target, AudioDirector.TARGET_LUFS[AudioDirector.category_of(entry.Id)], 1e-9,
                $"{entry.Id} was levelled to its category's target (tools/prepare_audio.py agrees with the game)");
            assert_eq(entry.TruePeak.Length, entry.Variants, $"{entry.Id} has a true peak per file");
            assert_true(entry.TruePeak.All(p => double.IsFinite(p) && p <= -1.4), $"{entry.Id} files peak at or under -1.5 dBTP");
        }
    }

    public void test_no_file_can_play_above_the_true_peak_ceiling()
    {
        var director = new AudioDirector { bank = SoundBank.Load() };
        foreach (SoundBank.Entry entry in director.bank.Entries.Values)
            for (int v = 0; v < entry.Variants; v++)
                foreach (double extra in new[] { -6.0, 0.0, 3.0, 9.0, 24.0 })
                    assert_true(director.play_db(entry.Id, v, extra) + entry.TruePeak[v] <= AudioDirector.PEAK_CEILING_DBTP + 1e-9,
                        $"{entry.Id} take {v + 1} with {extra:+0;-0} dB stays at or under -1 dBTP");
        // Unmeasured peaks count as full scale; null measurements are read as unmeasured.
        SoundBank unmeasured = SoundBank.Parse("""{ "sounds": { "ui_move": { "kind": "oneshot", "files": ["sfx/x.wav"], "lufs": [null], "true_peak": [null] } } }""");
        assert_eq(unmeasured.Entries.Count, 1, "a null measurement does not break the manifest");
        assert_near(unmeasured.Lufs(Sounds.UiMove, 0, -27.0), -27.0, 1e-9, "a null loudness falls back to the target");
        assert_near(unmeasured.TruePeak(Sounds.UiMove, 0), 0.0, 1e-9, "a null true peak counts as full scale");
        director.bank = unmeasured;
        assert_true(director.play_db(Sounds.UiMove, 0, 12.0) <= AudioDirector.PEAK_CEILING_DBTP, "an unmeasured file is never raised past the ceiling");
        assert_near(VoiceScript.GainDb(new VoiceParagraph("a", "a", "", "", "", -20.0, -6.0)), 4.0, 1e-9, "voice is levelled to -16 LUFS");
        assert_near(VoiceScript.GainDb(new VoiceParagraph("a", "a", "", "", "", -30.0, -3.0)), 2.0, 1e-9, "voice gain stops at the true-peak ceiling");
        assert_near(VoiceScript.GainDb(new VoiceParagraph("a", "a", "", "", "", double.NaN, double.NaN)), -1.0, 1e-9, "an unmeasured voice file is held under the ceiling");
        director.Free();
    }

    public void test_every_action_sound_has_a_caller()
    {
        var source = new System.Text.StringBuilder();
        var dirs = new Stack<string>(new[] { "res://scripts" });
        while (dirs.Count > 0)
        {
            string dir = dirs.Pop();
            if (dir == "res://scripts/audio") continue; // the director itself is not a caller
            foreach (string sub in DirAccess.GetDirectoriesAt(dir)) dirs.Push($"{dir}/{sub}");
            foreach (string file in DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
                source.Append(FileAccess.GetFileAsString($"{dir}/{file}"));
        }
        string text = source.ToString();
        var names = typeof(Sounds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral)
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name);
        foreach (string id in Sounds.Interactions)
        {
            // Skips and the sink play through water_impact, which the pond calls.
            bool called = text.Contains("Sounds." + names[id], StringComparison.Ordinal)
                || (id is Sounds.StoneSkip or Sounds.StoneSink && text.Contains("water_impact(", StringComparison.Ordinal));
            assert_true(called, $"action sound {id} has a caller");
        }
    }

    public void test_levels_normalise_by_measured_loudness()
    {
        var director = new AudioDirector { bank = SoundBank.Load() };
        double lufs = director.bank.Lufs(Sounds.StepWood, 2, double.NaN);
        assert_near(director.level_db(Sounds.StepWood, 2), AudioDirector.TARGET_LUFS["footsteps"] - lufs, 1e-9, "a footstep is brought to the footstep target");
        double bed = director.bank.Lufs(Sounds.BedDayWoods, 0, double.NaN);
        assert_near(director.level_db(Sounds.BedDayWoods, 0), AudioDirector.TARGET_LUFS["ambience"] - bed, 1e-9, "a bed is brought to the ambience target");
        assert_near(AudioDirector.VOICE_DUCK_DB, -6.0, 1e-9, "the voice ducks the ambience by about 6 dB");
        director.Free();
    }

    public void test_footsteps_never_repeat_a_variant()
    {
        var director = new AudioDirector { bank = SoundBank.Load() };
        foreach (string id in Sounds.Footsteps.Values)
        {
            int count = director.bank.Variants(id);
            var seen = new HashSet<int>();
            int last = -1;
            for (int i = 0; i < 300; i++)
            {
                int variant = director.pick_variant(id);
                assert_true(variant >= 0 && variant < count, $"{id} picks a listed variant");
                if (count > 1 && variant == last)
                {
                    assert_true(false, $"{id} repeated variant {variant}");
                    break;
                }
                last = variant;
                seen.Add(variant);
            }
            assert_eq(seen.Count, count, $"{id} uses every variant");
        }
        director.Free();
    }

    public void test_missing_files_play_silence()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        SoundBank bank = SoundBank.Parse("""
            { "sounds": {
              "bed_day_woods": { "kind": "bed", "files": ["sfx/none/take-1.wav"], "lufs": [-40.0] },
              "bed_fire": { "kind": "bed", "files": ["sfx/none/take-2.wav"], "lufs": [-40.0] },
              "step_grass": { "kind": "oneshot", "files": ["sfx/none/a.wav", "sfx/none/b.wav"], "lufs": [-30.0, -31.0] },
              "log_pickup": { "kind": "oneshot", "files": ["sfx/none/c.wav"], "lufs": [-30.0] }
            } }
            """);
        assert_true(bank.Stream(Sounds.StepGrass, 0) == null, "a missing file has no stream");
        assert_eq(bank.PresentFiles(), 0, "no listed file is present");
        var director = new AudioDirector { Name = "SilentAudio" };
        tree.Root.AddChild(director);
        director.setup(Fire, Lap, bank);
        director.set_daylight(0.2);
        director.set_weather(0.9, 0.5);
        director.set_wading(true, 0.5);
        foreach (string surface in Sounds.Footsteps.Keys) director.footstep(surface, true);
        foreach (string kind in Sounds.Interactions) director.play_interact(kind);
        director.water_crossing(true);
        director.water_impact(Vector3.Zero, 1, true);
        director.thunder(1.0, false);
        director.set_voice_ducking(true);
        for (int i = 0; i < 200; i++) director._Process(0.25);
        bool any = director.GetChildren().Any(c => c is AudioStreamPlayer { Playing: true } || c is AudioStreamPlayer3D { Playing: true });
        assert_false(any, "nothing plays without the files");
        assert_true(director.is_ready(), "the director runs without its files");
        tree.Root.RemoveChild(director);
        director.Free();
    }

    public void test_director_mixes_by_time_of_day_and_weather()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        var director = new AudioDirector { Name = "TestAudio" };
        tree.Root.AddChild(director);
        director.setup(Fire, Lap);
        assert_true(director.GetNodeOrNull("PondLap1") is AudioStreamPlayer3D, "one pond-lap emitter per pier point");
        assert_true(director.GetNodeOrNull("Fire") is AudioStreamPlayer3D, "the fire is positional");
        director.set_hour(14);
        director.set_daylight(1.0);
        Run(director, 10);
        assert_gt(director._day_bed.amp.value, 0.9, "the day woods carry the afternoon");
        assert_lt(director._night_bed.amp.value, 0.05, "no night bed by day");
        director.set_hour(19.5);
        director.set_daylight(0.3);
        Run(director, 10);
        assert_gt(director._evening_bed.amp.value, 0.5, "dusk brings the evening bed");
        director.set_hour(6.0);
        Run(director, 10);
        assert_lt(director._evening_bed.amp.value, 0.05, "dawn is not the evening bed");
        director.set_daylight(0.0);
        Run(director, 10);
        assert_gt(director._night_bed.amp.value, 0.9, "night insects at night");
        director.set_weather(1.0, 0.0);
        Run(director, 10);
        assert_gt(director._rain_bed.amp.value, 0.9, "rain outside");
        assert_gt(director._storm_bed.amp.value, 0.9, "storm wind in heavy rain");
        assert_lt(director._night_bed.amp.value, 0.3, "wildlife goes quiet in the rain");
        director.set_weather(1.0, 1.0);
        Run(director, 10);
        assert_gt(director._canvas_bed.amp.value, 0.9, "rain on canvas inside the tent");
        assert_lt(director._rain_bed.amp.value, 0.05, "the leaves' rain fades inside the tent");
        director.set_fire_intensity(0.0);
        Run(director, 10);
        assert_lt(director._fire_bed.amp.value, 0.01, "an out fire is silent");
        director.set_wind(1.0);
        Run(director, 10);
        assert_gt(director._wind_bed.amp.value, 0.9, "the canopy follows the wind");
        director.set_voice_ducking(true);
        Run(director, 3);
        assert_near(director._duck_db.value, AudioDirector.VOICE_DUCK_DB, 0.1, "June's voice ducks the ambience");
        director.set_voice_ducking(false);
        Run(director, 3);
        if (director.bank.PresentFiles() > 0)
        {
            AudioStream? bed = director.bank.Stream(Sounds.BedNight, 0);
            assert_true(bed is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }, "beds loop over the whole take");
            assert_true(director.GetNode<AudioStreamPlayer>("Night").Playing, "the beds play");
        }
        else
        {
            GD.Print("  TestAudio: audio/generated files absent; bed playback checks not run");
        }
        tree.Root.RemoveChild(director);
        director.Free();
    }

    private static void Run(AudioDirector director, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1) director._Process(0.1);
    }

    public void test_voice_script_orders_paragraphs_and_picks_the_storm_variant()
    {
        VoiceScript script = VoiceScript.Load();
        assert_eq(string.Join(",", script.For("e1", "").Select(p => p.Id)), "e1-p1,e1-p2,e1-p3", "e1 plays its paragraphs in order");
        assert_eq(string.Join(",", script.For("e7", "survived").Select(p => p.Id)), "e7-p1,e7-p2,e7-p3-kept,e7-p5", "a kept fire picks the kept paragraph");
        assert_eq(string.Join(",", script.For("e7", "went_out").Select(p => p.Id)), "e7-p1,e7-p2,e7-p4-lost,e7-p5", "a lost fire picks the lost paragraph");
        foreach (LineTrigger line in StoryScript.Lines)
            assert_gt(script.For(line.Id, "survived").Count, 0, $"line {line.Id} has text");
        assert_true(script.Paragraphs.All(p => p.Text.Length > 0 && p.File.StartsWith("voice/", StringComparison.Ordinal)), "every paragraph has text and a voice file name");
        assert_gt(VoiceScript.ReadingSeconds("one two three four five six seven eight nine ten eleven twelve"), VoiceScript.ReadingSeconds("Three."), "longer text stays up longer");
    }

    public void test_voice_player_plays_a_line_through_and_records_it_heard()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        var story = new StoryState { StormOutcome = "went_out" };
        // Subtitle timing only: stepping by hand does not advance a playing stream.
        var player = new VoicePlayer { Name = "TestVoice", Script = VoiceScript.Load(VoiceScript.ScriptPath, "res://local-data/no-voice/") };
        tree.Root.AddChild(player);
        story.LineTriggered += (id, kind, variant) => player.Enqueue(id, variant);
        story.Raise(StoryEvents.CreditsFinished);
        var shown = new List<string>();
        for (int i = 0; i < 2000 && (player.Speaking || player.Pending.Count > 0); i++)
        {
            player.Step(0.1);
            if (player.Current != null && (shown.Count == 0 || shown[^1] != player.Current.Id)) shown.Add(player.Current.Id);
        }
        assert_eq(string.Join(",", shown), "e7-p1,e7-p2,e7-p4-lost,e7-p5", "the postcard plays in order with the storm's paragraph");
        assert_true(player.Heard.Contains("e7"), "a finished line is recorded as heard");
        player.Enqueue("m3", "");
        for (int i = 0; i < 10; i++) player.Step(0.1);
        assert_true(player.Speaking, "a margin note starts within a second when June is quiet");
        tree.Root.RemoveChild(player);
        player.Free();

        var voiced = new VoicePlayer { Name = "TestVoiced" };
        tree.Root.AddChild(voiced);
        VoiceParagraph first = voiced.Script.For("m3", "")[0];
        if (ResourceLoader.Exists(voiced.Script.PathOf(first)))
        {
            assert_true(double.IsFinite(first.Lufs) && double.IsFinite(first.TruePeak), "a present voice file has its measured loudness");
            voiced.Enqueue("m3", "");
            for (int i = 0; i < 3; i++) voiced.Step(0.05);
            assert_true(voiced.GetNode<AudioStreamPlayer>("June").Playing, "a present voice file plays");
            assert_eq((string)voiced.GetNode<AudioStreamPlayer>("June").Bus, (string)AudioDirector.BUS_VOICE, "June plays on the voice bus");
        }
        else
        {
            GD.Print("  TestAudio: voice files absent; voiced playback check not run");
        }
        tree.Root.RemoveChild(voiced);
        voiced.Free();
    }

    public void test_steady_bed_levels_are_not_rewritten_every_frame()
    {
        // Each VolumeDb write swaps the playback's bus details under the audio
        // thread; a steady bed must not write at all (the headless mixer abort).
        var player = new AudioStreamPlayer();
        var bed = new AudioDirector.Bed { player = player, id = "test" };
        bed.set_level(-12.0);
        assert_eq(player.VolumeDb, -12.0f, "the first level is written");
        player.VolumeDb = 3.0f;
        for (int i = 0; i < 100; i++) bed.set_level(-12.0 + i * 0.0003);
        assert_eq(player.VolumeDb, 3.0f, "a level that barely moves is not rewritten");
        bed.set_level(-11.0);
        assert_eq(player.VolumeDb, -11.0f, "a real change is");
        bed.set_level(AudioDirector.SILENT_DB);
        assert_eq(player.VolumeDb, (float)AudioDirector.SILENT_DB, "and going silent always is");
        player.Free();
        assert_eq(Main.HEADLESS_MAX_FPS, 120, "headless runs are frame-capped");
    }
}
