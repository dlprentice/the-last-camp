using System;
using System.Collections.Generic;
using System.Linq;

namespace LastCamp.Story;

/// What happens when a band ends: where the story goes and how it gets there.
public sealed record BandTransition(
    Chapter FromChapter, Band FromBand, Chapter? ToChapter, Band? ToBand, EndAct Act)
{
    public bool ChapterChanged => ToChapter != null && ToChapter != FromChapter;
    public bool Finished => ToBand == null;
}

/// The weather a storm band asks for: a `CampWeather.apply_chapter` call.
public readonly record struct WeatherPhase(string Chapter, double Seconds, double Duration);

/// The world around the story, written into the save and restored from it.
/// The director fills it from the live scene before every save.
public sealed class WorldSnapshot
{
    public double Clock = StoryScript.Chapters[0].Bands[0].Start;
    public string WeatherChapter = "";
    public double WeatherSeconds, WeatherDuration = 1, Cloud, Rain, Wet;
    public double FireIntensity = 1.0;
    public bool HasPosition;
    public double X, Y, Z, Yaw;
}

/// The story's state machine. It holds no engine objects, so the whole story can
/// run in a unit test: events go in through `Raise`, bands end through `Advance`,
/// and June's lines come out through `LineTriggered`, each once.
public sealed class StoryState
{
    public int ChapterIndex { get; private set; }
    public int BandIndex { get; private set; }
    /// Progress per objective, keyed `chapter/band/objective`.
    public readonly Dictionary<string, int> Progress = new();
    /// Every event base name raised so far (for line rules and later bands).
    public readonly HashSet<string> Facts = new();
    public readonly HashSet<string> LinesPlayed = new();
    /// Lines the player heard to the end (the voice player records them for the journal).
    public readonly HashSet<string> LinesHeard = new();
    /// Things the player carries home: `june_letter`, `tally_book`, `tom_letter`.
    public readonly List<string> Items = new();
    /// "", then "survived" or "went_out" once the storm band has ended.
    public string StormOutcome = "";
    /// Continuous seconds the fire has been below its visible floor in the storm's rain.
    public double StormFireOutSeconds;
    /// Latched once the fire has stayed out for `StoryScript.StormFireOutSeconds`.
    public bool StormFireWentOut;
    /// A sunset sit ran the clock to here; the band holds here rather than earlier.
    public double ClockHeldAt = -1;
    public double BandSeconds;
    public double ShelterSeconds;
    public double ClearingElapsed = -1;
    public bool Finished { get; private set; }
    public int BestSkips;
    public bool RecordsMigrated;
    public readonly WorldSnapshot World = new();

    /// When false (the game), objectives whose event nothing raises yet are not
    /// required. Tests set it true to run the finished story's rules.
    public bool RequireUnbuilt;

    /// (line id, kind, variant). The variant is empty except for e7, which
    /// carries the storm outcome.
    public event Action<string, LineKind, string>? LineTriggered;

    public Chapter Chapter => StoryScript.Chapters[ChapterIndex];
    public Band Band => Chapter.Bands[BandIndex];

    public static string Key(Chapter chapter, Band band, Objective objective) => $"{chapter.Id}/{band.Id}/{objective.Id}";

    public void SetPosition(int chapter, int band)
    {
        if (chapter < 0 || chapter >= StoryScript.Chapters.Length) throw new ArgumentOutOfRangeException(nameof(chapter));
        if (band < 0 || band >= StoryScript.Chapters[chapter].Bands.Length) throw new ArgumentOutOfRangeException(nameof(band));
        ChapterIndex = chapter;
        BandIndex = band;
    }

    public void SetFinished(bool finished) => Finished = finished;

    public bool Counts(string ev) => RequireUnbuilt || StoryEvents.IsBuilt(ev);

    public IEnumerable<Objective> Required(Band band) => band.Objectives.Where(o => Counts(o.Event));

    public int ProgressOf(Objective objective) => ProgressOf(Chapter, Band, objective);

    public int ProgressOf(Chapter chapter, Band band, Objective objective) =>
        Progress.TryGetValue(Key(chapter, band, objective), out int value) ? value : 0;

    public bool IsDone(Objective objective) => ProgressOf(objective) >= objective.Count;

    public bool BandDone => !Finished && Required(Band).All(IsDone);

    /// June says nothing at all, entries included, from the opening of Tom's tin
    /// until the lookout (the tin's own note m7 and the lookout's entry e5 are the edges).
    public bool JuneSilent => Facts.Contains(StoryEvents.TinOpened) && !Facts.Contains(StoryEvents.SatLookout);

    /// Her margin notes stay silent longer: from the tin until she wakes you after
    /// the storm (m8 is that edge).
    public bool MarginsSilent => JuneSilent || Facts.Contains(StoryEvents.TinOpened) && !Facts.Contains(StoryEvents.WokeAfterStorm);

    /// Tom's tin can be opened only after the old fire ring (while that place is
    /// not built, nothing gates it).
    public bool TinAvailable => !Counts(StoryEvents.PlaceOldRing) || Facts.Contains(StoryEvents.PlaceOldRing) &&
        StoryScript.ChapterIndex(StoryScript.Marks) <= ChapterIndex;

    /// Records an event. Returns true when it moved an objective of the active band.
    public bool Raise(string ev)
    {
        if (string.IsNullOrEmpty(ev)) return false;
        if (ev == StoryEvents.TinOpened && !TinAvailable) return false;
        bool tinOpen = Facts.Contains(StoryEvents.TinOpened);
        Facts.Add(ev);
        Facts.Add(StoryEvents.BaseName(ev));
        bool moved = false;
        // Tom's first blaze means nothing until his tally book has been read.
        if (!Finished && !(ev == StoryEvents.BlazeFound && !tinOpen))
        {
            foreach (Objective objective in Band.Objectives)
            {
                if (!StoryEvents.Satisfies(ev, objective.Event)) continue;
                string key = Key(Chapter, Band, objective);
                int now = Progress.TryGetValue(key, out int value) ? value : 0;
                if (now >= objective.Count) continue;
                Progress[key] = now + 1;
                moved = true;
            }
        }
        TriggerLines(ev);
        return moved;
    }

    private void TriggerLines(string ev)
    {
        foreach (LineTrigger line in StoryScript.Lines)
        {
            if (LinesPlayed.Contains(line.Id) || !StoryEvents.Satisfies(ev, line.Event)) continue;
            if (line.Chapter.Length > 0 && (line.Chapter != Chapter.Id || Finished)) continue;
            if (line.Band.Length > 0 && line.Band != Band.Id) continue;
            // Nothing follows June's last note (m9) until the postcard.
            if (line.Id != "e7" && LinesPlayed.Contains("m9")) continue;
            if (ev != StoryEvents.TinOpened && (line.Kind == LineKind.Entry ? JuneSilent : MarginsSilent)) continue;
            LinesPlayed.Add(line.Id);
            string variant = line.Id == "e7" ? (StormOutcome.Length > 0 ? StormOutcome : FireSurvivingStorm ? "survived" : "went_out") : "";
            LineTriggered?.Invoke(line.Id, line.Kind, variant);
        }
    }

    /// The end acts this band accepts now (built ones only in the game).
    public IEnumerable<EndAct> EndActs => Finished ? Enumerable.Empty<EndAct>() :
        Band.EndActs.Where(a => Counts(a.Event) && (a.FallbackFor.Length == 0 || !Counts(a.FallbackFor)));

    public EndAct? EndActFor(string ev) => EndActs.FirstOrDefault(a => a.Event == ev);

    public bool CanEndBy(string ev) => BandDone && EndActFor(ev) != null;

    /// Ends the band by a deliberate act. Returns null (and changes nothing) while
    /// the band's objectives are not done or the act does not end this band.
    public BandTransition? Advance(string act)
    {
        if (!CanEndBy(act)) return null;
        EndAct endAct = EndActFor(act)!;
        Chapter fromChapter = Chapter;
        Band fromBand = Band;
        Facts.Add(act);
        if (fromBand.Storm)
            StormOutcome = FireSurvivingStorm ? "survived" : "went_out";
        Chapter? toChapter = null;
        Band? toBand = null;
        if (BandIndex + 1 < fromChapter.Bands.Length)
        {
            BandIndex++;
        }
        else if (ChapterIndex + 1 < StoryScript.Chapters.Length)
        {
            ChapterIndex++;
            BandIndex = 0;
        }
        else
        {
            Finished = true;
        }
        if (!Finished)
        {
            toChapter = Chapter;
            toBand = Band;
        }
        BandSeconds = 0;
        ShelterSeconds = 0;
        ClearingElapsed = -1;
        ClockHeldAt = -1;
        if (toBand != null && toBand.Storm)
        {
            StormFireOutSeconds = 0;
            StormFireWentOut = false;
        }
        if (toChapter != null && toChapter.Id == StoryScript.Epilogue && StormOutcome.Length > 0)
            Raise(StoryEvents.WokeAfterStorm);
        return new BandTransition(fromChapter, fromBand, toChapter, toBand, endAct);
    }

    /// The clock the band allows: it stops at `Hold` until the band is done, then
    /// at `Limit`; after a sunset sit it holds at the later time the sit reached.
    /// It only ever lowers a clock that ran past the hold, never below the sit.
    public double AllowedClock(double hour)
    {
        if (Finished) return hour;
        return Math.Min(hour, Math.Max(BandDone ? Band.Limit : Band.Hold, ClockHeldAt));
    }

    /// Records that a sunset sit carried the clock to `hour`.
    public void HoldAt(double hour) => ClockHeldAt = Math.Max(ClockHeldAt, hour);

    public bool FireSurvivingStorm => !StormFireWentOut;

    /// The rain has started falling in this storm band (the fire reading runs from here).
    public bool StormRaining => Band.Storm && (Sheltered || BandSeconds >= StoryScript.GatheringSeconds + StoryScript.StormRainStartSeconds);

    public bool Sheltered => !Finished && Band.Storm && Band.Objectives.Any(o => o.Event == StoryEvents.StormSheltered && IsDone(o));

    /// Advances a storm band by `delta` seconds: tracks the fire, counts the time
    /// spent inside the tent in the rain, and says which weather to show.
    public WeatherPhase StormTick(double delta, double fireIntensity, bool inTent)
    {
        if (Finished || !Band.Storm) return new WeatherPhase("", 0, 1);
        BandSeconds += delta;
        if (StormRaining && !StormFireWentOut)
        {
            StormFireOutSeconds = fireIntensity < StoryScript.StormFireFloor ? StormFireOutSeconds + delta : 0;
            if (StormFireOutSeconds >= StoryScript.StormFireOutSeconds) StormFireWentOut = true;
        }
        WeatherPhase phase = StormWeather();
        bool raining = phase.Chapter is "storm" or "shelter_rain";
        if (!Sheltered && raining && inTent)
        {
            ShelterSeconds += delta;
            if (ShelterSeconds >= StoryScript.ShelterSeconds) Raise(StoryEvents.StormSheltered);
        }
        else if (!Sheltered)
        {
            ShelterSeconds = 0;
        }
        if (Sheltered) ClearingElapsed = Math.Max(ClearingElapsed, 0) + delta;
        return StormWeather();
    }

    public WeatherPhase StormWeather()
    {
        if (Sheltered) return new WeatherPhase("clearing", Math.Max(ClearingElapsed, 0), StoryScript.ClearingSeconds);
        double s = BandSeconds;
        if (s < StoryScript.GatheringSeconds) return new WeatherPhase("gathering", s, StoryScript.GatheringSeconds);
        s -= StoryScript.GatheringSeconds;
        if (s < StoryScript.StormSeconds) return new WeatherPhase("storm", s, StoryScript.StormSeconds);
        s -= StoryScript.StormSeconds;
        return new WeatherPhase("shelter_rain", s % StoryScript.ShelterRainLoop, StoryScript.ShelterRainLoop);
    }

    public void AddItem(string item)
    {
        if (!Items.Contains(item)) Items.Add(item);
    }
}
