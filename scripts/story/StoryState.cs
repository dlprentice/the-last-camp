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
    /// Things the player carries home: `june_letter`, `tally_book`, `tom_letter`.
    public readonly List<string> Items = new();
    /// "", then "survived" or "went_out" once the storm band has ended.
    public string StormOutcome = "";
    public double StormMinFire = double.PositiveInfinity;
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
    /// until the lookout (the tin's own note m7 and the lookout's entry e5 are the
    /// edges), and nothing after her last note until the postcard.
    public bool JuneSilent => Facts.Contains(StoryEvents.TinOpened) && !Facts.Contains(StoryEvents.SatLookout);

    /// Records an event. Returns true when it moved an objective of the active band.
    public bool Raise(string ev)
    {
        if (string.IsNullOrEmpty(ev)) return false;
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
            if (line.Id != "e7" && Facts.Contains(StoryEvents.AshRaked) && ev != StoryEvents.AshRaked) continue;
            if (JuneSilent && ev != StoryEvents.TinOpened) continue;
            LinesPlayed.Add(line.Id);
            string variant = line.Id == "e7" ? (StormOutcome.Length > 0 ? StormOutcome : FireSurvivingStorm ? "survived" : "went_out") : "";
            LineTriggered?.Invoke(line.Id, line.Kind, variant);
        }
    }

    /// The end acts this band accepts now (built ones only in the game).
    public IEnumerable<EndAct> EndActs => Finished ? Enumerable.Empty<EndAct>() : Band.EndActs.Where(a => Counts(a.Event));

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
        if (toBand != null && toBand.Storm) StormMinFire = double.PositiveInfinity;
        if (toChapter != null && toChapter.Id == StoryScript.Epilogue && StormOutcome.Length > 0)
            Raise(StoryEvents.WokeAfterStorm);
        return new BandTransition(fromChapter, fromBand, toChapter, toBand, endAct);
    }

    /// The clock the band allows: it stops at `Hold` until the band is done, then
    /// at `Limit`. The clock never runs backwards here; jumps come from transitions.
    public double AllowedClock(double hour)
    {
        if (Finished) return hour;
        return Math.Min(hour, BandDone ? Band.Limit : Band.Hold);
    }

    public bool FireSurvivingStorm => double.IsPositiveInfinity(StormMinFire) || StormMinFire > StoryScript.StormFireThreshold;

    public bool Sheltered => !Finished && Band.Storm && Band.Objectives.Any(o => o.Event == StoryEvents.StormSheltered && IsDone(o));

    /// Advances a storm band by `delta` seconds: tracks the fire, counts the time
    /// spent inside the tent in the rain, and says which weather to show.
    public WeatherPhase StormTick(double delta, double fireIntensity, bool inTent)
    {
        if (Finished || !Band.Storm) return new WeatherPhase("", 0, 1);
        BandSeconds += delta;
        StormMinFire = Math.Min(StormMinFire, fireIntensity);
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
