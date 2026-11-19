using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using LastCamp.Story;

namespace LastCamp.Tests;

/// The story spine without the world: the chapters run from events alone, the
/// clock holds at a band's end, the storm has both outcomes and the one save
/// survives corrupt files and failed writes.
public partial class TestStory : TestCase
{
    private const string Dir = "user://story-tests";

    private static StorySave FreshSlot(string name)
    {
        var slot = new StorySave($"{Dir}/{name}");
        string real = ProjectSettings.GlobalizePath(slot.Directory);
        if (System.IO.Directory.Exists(real)) System.IO.Directory.Delete(real, true);
        System.IO.Directory.CreateDirectory(real);
        return slot;
    }

    private static void Remove(StorySave slot)
    {
        string real = ProjectSettings.GlobalizePath(slot.Directory);
        if (System.IO.Directory.Exists(real)) System.IO.Directory.Delete(real, true);
    }

    private static StoryState Full()
    {
        var s = new StoryState { RequireUnbuilt = true };
        foreach (string ev in new[] { StoryEvents.LetterRead, StoryEvents.LogCarried, StoryEvents.LogCarried, StoryEvents.LogCarried, "stone_skips:4" })
            s.Raise(ev);
        s.Advance(StoryEvents.SatPier);
        s.Raise(StoryEvents.FireLit);
        s.AddItem("june_letter");
        s.AddItem("tally_book");
        s.StormOutcome = "went_out";
        s.StormFireOutSeconds = 1.75;
        s.StormFireWentOut = true;
        s.ClockHeldAt = 20.25;
        s.BandSeconds = 12.5;
        s.ShelterSeconds = 4.25;
        s.ClearingElapsed = 3.5;
        s.BestSkips = 6;
        s.RecordsMigrated = true;
        WorldSnapshot w = s.World;
        w.Clock = 21.25;
        w.WeatherChapter = "shelter_rain";
        w.WeatherSeconds = 7.5;
        w.WeatherDuration = 30;
        w.Cloud = 1;
        w.Rain = 0.8;
        w.Wet = 0.6;
        w.FireIntensity = 0.73;
        w.HasPosition = true;
        w.X = -12.5;
        w.Y = 1.25;
        w.Z = 4.75;
        w.Yaw = 1.5;
        return s;
    }

    public void test_save_round_trip_keeps_every_field()
    {
        StorySave slot = FreshSlot("round-trip");
        StoryState saved = Full();
        assert_true(slot.Save(saved), "the save is written: " + slot.LastError);
        StoryState? loaded = slot.Load();
        assert_true(loaded != null, "the save loads back: " + slot.LastError);
        if (loaded == null) return;
        assert_eq(StorySave.Serialize(loaded), StorySave.Serialize(saved), "every saved field reads back the same");
        assert_eq(loaded.Chapter.Id, StoryScript.Arrival, "chapter");
        assert_eq(loaded.Band.Id, "night", "band");
        assert_eq(loaded.ProgressOf(StoryScript.Chapters[0], StoryScript.Chapters[0].Bands[0], StoryScript.Chapters[0].Bands[0].Objectives[1]), 3, "objective progress");
        assert_true(loaded.IsDone(loaded.Band.Objectives[0]), "the night's fire objective");
        assert_false(loaded.IsDone(loaded.Band.Objectives[1]), "the night's lantern objective");
        assert_near(loaded.World.Clock, 21.25, 0, "clock");
        assert_eq(loaded.World.WeatherChapter, "shelter_rain", "weather chapter");
        assert_near(loaded.World.WeatherSeconds, 7.5, 0, "weather seconds");
        assert_near(loaded.World.Rain, 0.8, 0, "rain");
        assert_near(loaded.World.Wet, 0.6, 0, "wetness");
        assert_near(loaded.World.FireIntensity, 0.73, 0, "fire intensity");
        assert_eq(string.Join(",", loaded.Items), "june_letter,tally_book", "story items");
        assert_true(loaded.LinesPlayed.SetEquals(saved.LinesPlayed) && loaded.LinesPlayed.Contains("e1") && loaded.LinesPlayed.Contains("e3"), "lines played");
        assert_eq(loaded.StormOutcome, "went_out", "storm outcome");
        assert_near(loaded.StormFireOutSeconds, 1.75, 0, "seconds the storm fire has been out");
        assert_true(loaded.StormFireWentOut, "the storm fire's latch");
        assert_near(loaded.ClockHeldAt, 20.25, 0, "the sunset hold");
        assert_true(loaded.World.HasPosition && loaded.World.X == -12.5 && loaded.World.Y == 1.25 && loaded.World.Z == 4.75 && loaded.World.Yaw == 1.5, "last safe position");
        assert_eq(loaded.BestSkips, 6, "stone record");
        assert_true(loaded.RecordsMigrated, "records migration flag");
        assert_true(loaded.Facts.SetEquals(saved.Facts), "facts");
        assert_false(File.Exists(ProjectSettings.GlobalizePath(slot.TempPath)), "no temporary file is left behind");
        Remove(slot);
    }

    public void test_corrupt_or_partial_files_are_rejected_without_losing_the_slot()
    {
        StorySave slot = FreshSlot("corrupt");
        assert_true(slot.Save(Full()), "first save");
        string real = ProjectSettings.GlobalizePath(slot.SlotPath);
        string good = File.ReadAllText(real);

        // An interrupted write leaves only a partial temporary file: the slot still loads.
        File.WriteAllText(ProjectSettings.GlobalizePath(slot.TempPath), good[..(good.Length / 2)]);
        StoryState? kept = slot.Load();
        assert_true(kept != null && kept.Band.Id == "night", "a partial temporary file never replaces the slot");

        // A slot cut short, garbled or missing a field is rejected and left as it is.
        foreach (string bad in new[] { good[..(good.Length / 2)], "{ not json", good.Replace("\"fire_intensity\"", "\"fire_heat\""), good.Replace("\"version\": 1", "\"version\": 99"), "" })
        {
            File.WriteAllText(real, bad);
            assert_true(slot.Load() == null, "a damaged slot is rejected");
            assert_true(slot.LastError.Length > 0, "the rejection says why");
            assert_eq(File.ReadAllText(real), bad, "the rejected file is not touched");
        }
        string copy = slot.PreserveRejected();
        assert_true(copy.Length > 0 && File.Exists(copy), "a rejected slot can be kept beside the new one");
        Remove(slot);
    }

    public void test_failed_write_keeps_the_old_slot()
    {
        StorySave slot = FreshSlot("failed-write");
        StoryState first = Full();
        assert_true(slot.Save(first), "first save");
        string before = File.ReadAllText(ProjectSettings.GlobalizePath(slot.SlotPath));
        // A directory where the temporary file must go makes the write fail.
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(slot.TempPath));
        StoryState second = Full();
        second.BestSkips = 7;
        second.World.Clock = 22.5;
        assert_false(slot.Save(second), "the write reports failure");
        assert_true(slot.LastError.Length > 0, "the failure is reported");
        assert_eq(File.ReadAllText(ProjectSettings.GlobalizePath(slot.SlotPath)), before, "the old slot is unchanged");
        StoryState? loaded = slot.Load();
        assert_true(loaded != null && loaded.BestSkips == 6 && loaded.World.Clock == 21.25, "the old slot still loads");
        Remove(slot);
    }

    public void test_stone_record_migrates_once()
    {
        StorySave slot = FreshSlot("records");
        string records = slot.Directory + "/camp-records.cfg";
        using (var cfg = new ConfigFile())
        {
            cfg.SetValue("pond", "best_skips", 5);
            assert_eq((long)cfg.Save(records), (long)Error.Ok, "old record written");
        }
        var state = new StoryState();
        assert_true(StorySave.MigrateRecords(state, records), "the old record is read");
        assert_eq(state.BestSkips, 5, "the best throw comes across");
        assert_true(state.RecordsMigrated, "migration is recorded");
        state.BestSkips = 2;
        assert_false(StorySave.MigrateRecords(state, records), "a second migration does nothing");
        assert_eq(state.BestSkips, 2, "a migrated story is not overwritten by the old file");
        assert_true(File.Exists(ProjectSettings.GlobalizePath(records)), "the old record file is left in place");
        var none = new StoryState();
        assert_false(StorySave.MigrateRecords(none, slot.Directory + "/missing.cfg"), "no old file, nothing to migrate");
        assert_true(none.RecordsMigrated && none.BestSkips == 0, "a missing record still counts as migrated");
        Remove(slot);
    }

    /// Plays the finished story's events in order, as a bot would.
    private static (StoryState state, List<string> lines) PlayThrough(bool keepFire)
    {
        var s = new StoryState { RequireUnbuilt = true };
        var lines = new List<string>();
        var variants = new Dictionary<string, string>();
        s.LineTriggered += (id, _, variant) => { lines.Add(id); if (variant.Length > 0) lines.Add(id + ":" + variant); };
        void Step(string ev) => s.Raise(ev);
        bool End(string act) => s.Advance(act) != null;

        Step(StoryEvents.PlaceTrailhead);
        Step(StoryEvents.LetterRead);
        for (int i = 0; i < 3; i++) Step(StoryEvents.LogCarried);
        Step("stone_skips:0");
        Step(StoryEvents.StoneSunk);
        Step(StoryEvents.PlacePierEnd);
        Step("stone_skips:3");
        End(StoryEvents.SatPier);
        Step(StoryEvents.FireLit);
        Step(StoryEvents.LanternLit);
        Step(StoryEvents.TentEntered);
        End(StoryEvents.TurnedIn);

        Step(StoryEvents.PlaceOldRing);
        Step(StoryEvents.BlazeFound);
        Step(StoryEvents.TinOpened);
        End(StoryEvents.RestFire);
        Step(StoryEvents.BlazeFound);
        Step(StoryEvents.SatLookout);
        Step(StoryEvents.StepLifted);
        End(StoryEvents.StepLifted);

        // The storm: the rain comes, the fire is fed twice, the player shelters.
        double fire = 1.0;
        for (int i = 0; i < 80; i++)
        {
            if (!keepFire && i == 60) fire = 0.0;
            s.StormTick(1.0, fire, inTent: i >= 75);
        }
        Step(StoryEvents.FireFedInRain);
        Step(StoryEvents.FireFedInRain);
        for (int i = 0; i < (int)StoryScript.ShelterSeconds + 1; i++) s.StormTick(1.0, fire, inTent: true);
        End(StoryEvents.TurnedIn);

        Step(StoryEvents.TentStruck);
        Step(StoryEvents.StonesScattered);
        Step(StoryEvents.AshRaked);
        End(StoryEvents.WalkedOut);
        Step(StoryEvents.CreditsFinished);
        return (s, lines);
    }

    public void test_bot_plays_every_chapter_to_the_epilogue_end()
    {
        (StoryState s, List<string> lines) = PlayThrough(keepFire: true);
        assert_true(s.Finished, "the epilogue ends with the walk out");
        assert_eq(s.Chapter.Id, StoryScript.Epilogue, "the story ends in the epilogue");
        foreach ((Chapter chapter, Band band) in StoryScript.AllBands())
            foreach (Objective objective in band.Objectives)
                assert_true(s.ProgressOf(chapter, band, objective) >= objective.Count, $"{StoryState.Key(chapter, band, objective)} done");
        foreach (LineTrigger line in StoryScript.Lines)
            assert_eq(lines.Count(l => l == line.Id), 1, $"{line.Id} plays exactly once");
        assert_eq(string.Join(" ", lines.Where(l => !l.Contains(':'))),
            "m1 e1 m2 m3 m4 e2 e3 m5 m6 e4 m7 e5 e6 m8 m9 e7", "June's lines come in story order");
        assert_true(lines.Contains("e7:survived"), "the postcard records the storm outcome");
        assert_eq(s.Items.Count, 0, "the pure state adds items only through the session's hooks");
    }

    public void test_no_june_line_fires_between_the_tin_and_the_lookout()
    {
        var s = new StoryState { RequireUnbuilt = true };
        var lines = new List<string>();
        s.LineTriggered += (id, _, _) => lines.Add(id);
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        s.Raise(StoryEvents.PlaceOldRing);
        s.Raise(StoryEvents.TinOpened);
        assert_eq(string.Join(" ", lines), "e4 m7", "the old ring's entry, then the tin's own note");
        // Every other trigger June has, entries and notes, from every band.
        foreach (LineTrigger line in StoryScript.Lines)
        {
            if (line.Event == StoryEvents.SatLookout || line.Event == StoryEvents.TinOpened) continue;
            foreach ((Chapter chapter, Band band) in StoryScript.AllBands())
            {
                s.SetPosition(StoryScript.ChapterIndex(chapter.Id), Array.IndexOf(chapter.Bands, band));
                s.Raise(line.Event);
            }
        }
        assert_eq(string.Join(" ", lines), "e4 m7", "no entry or note plays while the tin is open");
        assert_false(s.Facts.Contains(StoryEvents.SatLookout), "the lookout has not been reached");
    }

    public void test_blaze_counts_only_after_the_tin()
    {
        var s = new StoryState { RequireUnbuilt = true };
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 1);
        Objective blaze = s.Band.Objectives.First(o => o.Event == StoryEvents.BlazeFound);
        s.Raise(StoryEvents.BlazeFound);
        assert_eq(s.ProgressOf(blaze), 0, "the notch means nothing before the tally book");
        s.Raise(StoryEvents.PlaceOldRing);
        s.Raise(StoryEvents.TinOpened);
        s.Raise(StoryEvents.BlazeFound);
        assert_eq(s.ProgressOf(blaze), 1, "after the tin it is Tom's first blaze");
    }

    public void test_entries_resume_at_the_lookout_and_notes_after_the_storm()
    {
        var s = new StoryState { RequireUnbuilt = true };
        var lines = new List<string>();
        s.LineTriggered += (id, _, _) => lines.Add(id);
        int marks = StoryScript.ChapterIndex(StoryScript.Marks);
        s.SetPosition(marks, 0);
        s.Raise(StoryEvents.PlaceOldRing);
        s.Raise(StoryEvents.TinOpened);
        s.SetPosition(marks, 1);
        s.Raise(StoryEvents.SatLookout);
        s.SetPosition(marks, 2);
        s.Raise(StoryEvents.StormSheltered);
        assert_eq(string.Join(" ", lines), "e4 m7 e5 e6", "entries come back at the lookout");
        s.Raise(StoryEvents.TentEntered);
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Epilogue), 0);
        s.Raise(StoryEvents.AshRaked);
        assert_eq(string.Join(" ", lines), "e4 m7 e5 e6", "margin notes stay silent until June wakes you after the storm");
        s.Raise(StoryEvents.WokeAfterStorm);
        assert_eq(lines.Last(), "m8", "m8 is the first note after the storm");
        // m9 was raised early and stayed silent; it can still play once, then nothing.
        s.Raise(StoryEvents.AshRaked);
        assert_eq(lines.Last(), "m9", "June's last note");
        s.Raise(StoryEvents.LanternLit);
        s.Raise(StoryEvents.PlaceOldRing);
        assert_eq(lines.Last(), "m9", "nothing follows June's last note until the postcard");
        s.Raise(StoryEvents.CreditsFinished);
        assert_eq(lines.Last(), "e7", "the postcard still plays");
    }

    public void test_margin_notes_expire_with_their_chapter()
    {
        var s = new StoryState { RequireUnbuilt = true };
        var lines = new List<string>();
        s.LineTriggered += (id, _, _) => lines.Add(id);
        foreach (int chapter in new[] { StoryScript.ChapterIndex(StoryScript.Marks), StoryScript.ChapterIndex(StoryScript.Epilogue) })
        {
            s.SetPosition(chapter, 0);
            foreach (string ev in new[] { StoryEvents.StoneSunk, StoryEvents.PlacePierEnd, StoryEvents.PlaceTrailhead, StoryEvents.LogCarried, StoryEvents.LanternLit, StoryEvents.TentEntered })
                s.Raise(ev);
        }
        assert_eq(lines.Count, 0, "Arrival's notes (m1 to m6) cannot play in a later chapter");
        s.SetPosition(0, 0);
        s.Raise(StoryEvents.StoneSunk);
        s.Raise(StoryEvents.PlacePierEnd);
        assert_eq(string.Join(" ", lines), "m3 m4", "they play in their own chapter");
    }

    public void test_tin_waits_for_the_old_ring()
    {
        var s = new StoryState { RequireUnbuilt = true };
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        assert_false(s.TinAvailable, "the tin is not offered before the old ring");
        s.Raise(StoryEvents.TinOpened);
        assert_false(s.Facts.Contains(StoryEvents.TinOpened), "opening it early does nothing");
        assert_false(s.LinesPlayed.Contains("m7"), "and plays nothing");
        s.Raise(StoryEvents.PlaceOldRing);
        assert_true(s.TinAvailable, "after the old ring the tin is offered");
        s.Raise(StoryEvents.TinOpened);
        assert_true(s.BandDone, "ring then tin completes the morning");
        var game = new StoryState();
        game.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        assert_true(game.TinAvailable, "while the old ring is not built, nothing gates the tin");
    }

    public void test_rest_ends_a_band_only_while_its_real_act_is_unbuilt()
    {
        var finished = new StoryState { RequireUnbuilt = true };
        assert_true(finished.EndActFor(StoryEvents.RestBedroll) == null, "once the pier seat exists, rest does not end the evening");
        assert_true(finished.EndActFor(StoryEvents.SatPier) != null, "the evening ends by sitting at the pier end");
        finished.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 1);
        assert_true(finished.EndActFor(StoryEvents.RestBedroll) == null, "once the step exists, rest does not end the climb");
        var game = new StoryState();
        assert_true(game.EndActFor(StoryEvents.RestBedroll) != null, "while the pier seat is unbuilt, rest is the way on");
    }

    public void test_sunset_sit_moves_the_clock_forward_and_holds_there()
    {
        var s = new StoryState { RequireUnbuilt = true };
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 1);
        Band evening = s.Band;
        s.Raise(StoryEvents.PlaceOldRing);
        s.Raise(StoryEvents.TinOpened);
        s.Raise(StoryEvents.BlazeFound);
        s.Raise(StoryEvents.SatLookout);
        for (int i = 0; i <= 20; i++)
        {
            double hour = Mathf.Lerp(evening.Hold - 0.1, evening.Limit, i / 20.0);
            s.HoldAt(hour);
            assert_true(s.AllowedClock(hour) >= hour - 1e-9, $"the sunset's clock {hour:F2} is never pulled back");
        }
        assert_false(s.BandDone, "the step is not lifted yet");
        assert_near(s.AllowedClock(23.0), evening.Limit, 1e-9, "after the sit the band holds at the later time");
        assert_near(s.AllowedClock(evening.Limit), evening.Limit, 1e-9, "not at the earlier hold");
        s.Raise(StoryEvents.StepLifted);
        s.Advance(StoryEvents.StepLifted);
        assert_near(s.ClockHeldAt, -1, 0, "the next band starts with its own holds");
    }

    public void test_storm_fire_reading_starts_with_the_rain_and_needs_three_seconds_out()
    {
        var s = new StoryState { RequireUnbuilt = true };
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 2);
        double rainStarts = StoryScript.GatheringSeconds + StoryScript.StormRainStartSeconds;
        for (double t = 0; t < rainStarts - 1; t += 0.5) s.StormTick(0.5, 0.0, false);
        assert_false(s.StormFireWentOut, "a fire that is out before the rain does not count");
        while (s.BandSeconds < rainStarts + 1) s.StormTick(0.5, 1.0, false);
        for (int i = 0; i < 5; i++) s.StormTick(0.5, 0.01, false);
        s.StormTick(0.5, 0.5, false);
        assert_false(s.StormFireWentOut, "2.5 s below the floor is a flicker, not out");
        s.StormTick(0.5, StoryScript.StormFireFloor, false);
        assert_false(s.StormFireWentOut, "at the visible floor the fire is still alive");
        for (int i = 0; i < 6; i++) s.StormTick(0.5, 0.0, false);
        assert_true(s.StormFireWentOut, "three seconds below the floor in the rain is out");
        for (int i = 0; i < 4; i++) s.StormTick(0.5, 1.5, false);
        assert_true(s.StormFireWentOut, "relighting does not undo it");
    }

    public void test_malformed_saves_are_rejected_kept_and_never_overwritten()
    {
        StorySave slot = FreshSlot("fuzz");
        assert_true(slot.Save(Full()), "a good save to start from");
        string real = ProjectSettings.GlobalizePath(slot.SlotPath);
        string good = File.ReadAllText(real);
        var bad = new List<string>
        {
            good.Replace("\"facts\": [", "\"facts\": [null, "),
            good.Replace("\"items\": [", "\"items\": [null, "),
            good.Replace("\"lines_played\": [", "\"lines_played\": [null, "),
            good.Replace("\"lines_played\": [", "\"lines_played\": [7, "),
            good.Replace("\"facts\": [", "\"facts\": [{}, "),
            good.Replace("\"clock\": 21.25", "\"clock\": \"late\""),
            good.Replace("\"clock\": 21.25", "\"clock\": null"),
            good.Replace("\"chapter\": \"arrival\"", "\"chapter\": 3"),
            good.Replace("\"finished\": false", "\"finished\": \"no\""),
            good.Replace("\"position\": {", "\"position\": [1, 2], \"unused\": {"),
            good.Replace("\"weather\": {", "\"weather\": null, \"unused\": {"),
            good.Replace("\"objectives\": {", "\"objectives\": [], \"unused\": {"),
            good.Replace("\"version\": 1", "\"version\": 2"),
            good.Replace("\"version\": 1", "\"version\": \"1\""),
            good[..(good.Length * 3 / 4)],
            good[..1],
            "[]",
            "null",
            "\u0000\u0001",
        };
        int index = 0;
        foreach (string text in bad)
        {
            index++;
            assert_false(text == good, $"shape {index} really differs from the good save");
            File.WriteAllText(real, text);
            StoryState? loaded = null;
            try { loaded = slot.Load(); }
            catch (Exception error) { _fail($"shape {index}: Load threw {error.GetType().Name}"); }
            assert_true(loaded == null, $"shape {index} is rejected");
            assert_true(slot.Rejected.Length > 0 && File.Exists(slot.Rejected) && File.ReadAllText(slot.Rejected) == text,
                $"shape {index} is copied aside before anything saves");
            assert_true(slot.Save(new StoryState()), $"a fresh story saves after shape {index}");
            assert_true(slot.Load() != null, $"the fresh story loads after shape {index}");
        }
        Remove(slot);
    }

    public void test_band_clock_holds_until_its_objectives_are_done()
    {
        var s = new StoryState { RequireUnbuilt = true };
        Band evening = s.Band;
        assert_near(s.AllowedClock(evening.Start + 0.1), evening.Start + 0.1, 1e-9, "the clock runs inside the band");
        assert_near(s.AllowedClock(23.0), evening.Hold, 1e-9, "the clock stops at the band's end");
        assert_true(s.Advance(StoryEvents.SatPier) == null, "the band cannot end before its objectives");
        s.Raise(StoryEvents.LetterRead);
        s.Raise(StoryEvents.LogCarried);
        s.Raise(StoryEvents.LogCarried);
        s.Raise("stone_skips:2");
        assert_false(s.BandDone, "two logs and two skips are not enough");
        assert_true(s.Advance(StoryEvents.SatPier) == null, "still held");
        assert_near(s.AllowedClock(23.0), evening.Hold, 1e-9, "still holding");
        s.Raise(StoryEvents.LogCarried);
        s.Raise("stone_skips:5");
        assert_true(s.BandDone, "the objectives are done");
        assert_near(s.AllowedClock(23.0), evening.Limit, 1e-9, "the clock may carry on to the sunset");
        assert_true(s.Advance(StoryEvents.TurnedIn) == null, "an act that does not end this band does nothing");
        BandTransition? t = s.Advance(StoryEvents.SatPier);
        assert_true(t != null && t.ToBand!.Id == "night" && t.Act.Sunset && !t.Act.Fade, "sitting on the pier plays the sunset into the night");
        assert_eq(s.ProgressOf(s.Band.Objectives[0]), 0, "an event from the evening does not complete the night");
    }

    public void test_unbuilt_places_do_not_hold_the_game()
    {
        var s = new StoryState();
        assert_eq(string.Join(",", s.Required(s.Band).Select(o => o.Id)), "logs,skips", "the letter waits for its table");
        assert_true(s.EndActFor(StoryEvents.SatPier) == null && s.EndActFor(StoryEvents.RestBedroll) != null, "rest ends the evening until the pier seat exists");
        assert_true(s.Advance(StoryEvents.RestBedroll) == null, "but not before the evening's objectives");
        foreach ((Chapter chapter, Band band) in StoryScript.AllBands())
        {
            if (chapter.Id == StoryScript.Epilogue) continue;
            assert_true(band.EndActs.Any(a => StoryEvents.IsBuilt(a.Event)), $"{chapter.Id}/{band.Id} can end in today's world");
        }
    }

    public void test_storm_outcomes()
    {
        (StoryState kept, List<string> keptLines) = PlayThrough(keepFire: true);
        (StoryState lost, List<string> lostLines) = PlayThrough(keepFire: false);
        assert_eq(kept.StormOutcome, "survived", "a fire kept alive survives the storm");
        assert_eq(lost.StormOutcome, "went_out", "a fire that dies in the rain is out");
        assert_true(keptLines.Contains("e7:survived") && lostLines.Contains("e7:went_out"), "the postcard line follows the outcome");
        assert_true(kept.Finished && lost.Finished, "neither outcome stops the story");

        var s = new StoryState { RequireUnbuilt = true };
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 2);
        assert_true(s.Band.Storm, "the storm band");
        var phases = new List<string>();
        for (int i = 0; i < 120; i++)
        {
            WeatherPhase p = s.StormTick(1.0, 1.0, inTent: false);
            if (phases.Count == 0 || phases[^1] != p.Chapter) phases.Add(p.Chapter);
        }
        assert_eq(string.Join(" ", phases), "gathering storm shelter_rain", "the band drives the storm and the rain holds");
        assert_false(s.Sheltered, "the storm does not pass on its own");
        for (int i = 0; i < 10; i++) s.StormTick(1.0, 1.0, inTent: true);
        s.StormTick(1.0, 1.0, inTent: false);
        assert_false(s.Sheltered, "leaving the tent starts the shelter over");
        for (int i = 0; i <= (int)StoryScript.ShelterSeconds; i++) s.StormTick(1.0, 1.0, inTent: true);
        assert_true(s.Sheltered, "sheltering in the tent lets it pass");
        assert_eq(s.StormTick(1.0, 1.0, inTent: true).Chapter, "clearing", "then the sky clears");
    }
}
