using System;
using System.Collections.Generic;

namespace LastCamp.Story;

/// One thing a band asks of the player. It is done once its event has been
/// raised `Count` times while the band is active. An event written `name:N`
/// (for example `stone_skips:3`) is done by any raise of `name:M` with M >= N.
public sealed record Objective(string Id, string Event, int Count = 1, string Journal = "");

/// A deliberate act that ends a band: resting, sitting or turning in. With
/// `Fade` the screen goes through black and the clock jumps to the next band's
/// start; `Sunset` first runs the clock to the next band's start over
/// `StoryScript.SunsetSeconds` (the sunset sequence) and then carries on without a fade.
public sealed record EndAct(string Event, bool Fade = true, bool Sunset = false);

/// A stretch of a chapter's clock. The clock starts at `Start`, runs at the rate
/// that shows the whole arc to `Hold` in about `ExpectedSeconds` and stops at
/// `Hold` until the required objectives are done. Once they are, it may carry on
/// to `Limit` while the player chooses when to end the band.
public sealed record Band(
    string Id,
    string Title,
    double Start,
    double Hold,
    double Limit,
    double ExpectedSeconds,
    Objective[] Objectives,
    EndAct[] EndActs,
    bool Storm = false)
{
    public double ClockRate => Math.Max(Hold - Start, 0.05) / Math.Max(ExpectedSeconds, 1.0);
}

public sealed record Chapter(string Id, string Title, Band[] Bands);

public enum LineKind { Entry, MarginNote }

/// A line June recorded before you came, played once on the first matching event.
/// `Chapter`/`Band` narrow the trigger to one part of the story when set.
public sealed record LineTrigger(string Id, LineKind Kind, string Event, string Chapter = "", string Band = "");

/// The micro cut's story as plain data: [DONE](docs/DONE.md) is the authority,
/// with the band clock from the design's "How time works", adapted to the cut.
public static class StoryScript
{
    public const string Arrival = "arrival";
    public const string Marks = "marks";
    public const string Epilogue = "epilogue";

    /// Real seconds the sunset sequence takes to run the clock to the next band.
    public const double SunsetSeconds = 90.0;
    /// The fire counts as alive through the storm while it stays above this.
    public const double StormFireThreshold = 0.05;
    /// Extra fire decay per second at full rain (the fire itself decays slowly anyway).
    public const double RainFireDecay = 0.0045;
    /// Storm weather phases, in band seconds. The rain then holds until the
    /// player has sheltered, and clears over `ClearingSeconds` after.
    public const double GatheringSeconds = 30.0;
    public const double StormSeconds = 42.0;
    public const double ShelterRainLoop = 30.0;
    public const double ClearingSeconds = 120.0;
    /// Continuous seconds inside the tent in the rain that count as sheltering.
    public const double ShelterSeconds = 20.0;

    public static readonly Chapter[] Chapters =
    {
        new(Arrival, "Arrival", new Band[]
        {
            new("evening", "Evening", 18.75, 19.0 + 50.0 / 60.0, 20.5, 420,
                new Objective[]
                {
                    new("letter", StoryEvents.LetterRead, 1, "Read June's letter on the camp table."),
                    new("logs", StoryEvents.LogCarried, 3, "Carry three logs to the fire ring."),
                    new("skips", StoryEvents.StoneSkips + ":3", 1, "Skip a stone three times. Stones wait at the end of the pier."),
                },
                new EndAct[] { new(StoryEvents.SatPier, Fade: false, Sunset: true), new(StoryEvents.RestBedroll) }),
            new("night", "Night", 20.5, 23.5, 23.75, 300,
                new Objective[]
                {
                    new("fire", StoryEvents.FireLit, 1, "Light the fire once the first star is out."),
                    new("lantern", StoryEvents.LanternLit, 1, "Light your lantern."),
                },
                new EndAct[] { new(StoryEvents.TurnedIn) }),
        }),
        new(Marks, "The Marks", new Band[]
        {
            new("morning", "Morning", 6.3, 12.0, 13.0, 420,
                new Objective[]
                {
                    new("old_ring", StoryEvents.PlaceOldRing, 1, "Follow the footpath to the old fire ring."),
                    new("tin", StoryEvents.TinOpened, 1, "Open Tom's tackle tin in the tent."),
                },
                new EndAct[] { new(StoryEvents.RestFire), new(StoryEvents.RestBedroll) }),
            new("evening", "Evening", 18.25, 19.0 + 40.0 / 60.0, 20.5, 420,
                new Objective[]
                {
                    // The notch on the old ring's log counts only once the tin's
                    // tally book has shown what it is.
                    new("blaze", StoryEvents.BlazeFound, 1, "Find Tom's first mark."),
                    new("lookout", StoryEvents.SatLookout, 1, "Follow Tom's marks up to the rock and sit on his bench."),
                    new("step", StoryEvents.StepLifted, 1, "Read what Tom left."),
                },
                // Lifting the step is the band's last act; the walk down continues
                // into the storm without a fade. Rest is the way on while the
                // lookout is not built yet.
                new EndAct[] { new(StoryEvents.StepLifted, Fade: false), new(StoryEvents.RestBedroll) }),
            new("storm", "The Storm", 20.5, 23.5, 23.75, 360,
                new Objective[]
                {
                    new("fed_in_rain", StoryEvents.FireFedInRain, 2, "Feed the fire in the rain."),
                    new("sheltered", StoryEvents.StormSheltered, 1, "Shelter in the tent until the storm passes."),
                },
                new EndAct[] { new(StoryEvents.TurnedIn) }, Storm: true),
        }),
        new(Epilogue, "Leave No Trace", new Band[]
        {
            new("dawn", "Dawn", 6.0, 7.5, 8.0, 360,
                new Objective[]
                {
                    new("tent", StoryEvents.TentStruck, 1, "Strike the tent."),
                    new("ash", StoryEvents.AshRaked, 1, "Rake out the ash."),
                    new("stones", StoryEvents.StonesScattered, 1, "Scatter the ring stones."),
                },
                new EndAct[] { new(StoryEvents.WalkedOut, Fade: false) }),
        }),
    };

    /// June's entries (e1..e7) and margin notes (m1..m9), with the script's triggers.
    public static readonly LineTrigger[] Lines =
    {
        new("e1", LineKind.Entry, StoryEvents.LetterRead),
        new("e2", LineKind.Entry, StoryEvents.StoneSkips + ":3"),
        new("e3", LineKind.Entry, StoryEvents.FireLit, Arrival, "night"),
        new("e4", LineKind.Entry, StoryEvents.PlaceOldRing),
        new("e5", LineKind.Entry, StoryEvents.SatLookout),
        new("e6", LineKind.Entry, StoryEvents.StormSheltered),
        new("e7", LineKind.Entry, StoryEvents.CreditsFinished),
        new("m1", LineKind.MarginNote, StoryEvents.PlaceTrailhead),
        new("m2", LineKind.MarginNote, StoryEvents.LogCarried),
        new("m3", LineKind.MarginNote, StoryEvents.StoneSunk),
        new("m4", LineKind.MarginNote, StoryEvents.PlacePierEnd),
        new("m5", LineKind.MarginNote, StoryEvents.LanternLit),
        new("m6", LineKind.MarginNote, StoryEvents.TentEntered),
        new("m7", LineKind.MarginNote, StoryEvents.TinOpened),
        new("m8", LineKind.MarginNote, StoryEvents.WokeAfterStorm),
        new("m9", LineKind.MarginNote, StoryEvents.AshRaked),
    };

    public static Chapter ChapterById(string id) =>
        Array.Find(Chapters, c => c.Id == id) ?? throw new ArgumentException($"Unknown chapter '{id}'");

    public static int ChapterIndex(string id) => Array.FindIndex(Chapters, c => c.Id == id);

    public static IEnumerable<(Chapter chapter, Band band)> AllBands()
    {
        foreach (Chapter chapter in Chapters)
            foreach (Band band in chapter.Bands)
                yield return (chapter, band);
    }
}
