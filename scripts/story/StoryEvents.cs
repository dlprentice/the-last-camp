using System.Collections.Generic;

namespace LastCamp.Story;

/// Every event the story listens for. The game raises them through
/// `CampSession.Story.Raise(...)` or the named hook methods on `CampSession`.
public static class StoryEvents
{
    // Chapter 1.
    public const string LetterRead = "letter_read";
    public const string LogCarried = "log_carried";
    /// Raised as `stone_skips:N` with the throw's skip count.
    public const string StoneSkips = "stone_skips";
    public const string StoneSunk = "stone_sunk";
    public const string PlaceTrailhead = "place:trailhead";
    public const string PlacePierEnd = "place:pier_end";
    public const string SatPier = "sat:pier";
    public const string FireLit = "fire_lit";
    public const string LanternLit = "lantern_lit";
    public const string TentEntered = "tent_entered";
    public const string RestBedroll = "rest:bedroll";
    public const string RestFire = "rest:fire";
    public const string TurnedIn = "turned_in";
    // Chapter 2.
    public const string TinOpened = "tin_opened";
    public const string PlaceOldRing = "place:old_ring";
    public const string BlazeFound = "blaze_found";
    public const string SatLookout = "sat:lookout";
    public const string StepLifted = "step_lifted";
    public const string FireFedInRain = "fire_fed_in_rain";
    public const string StormSheltered = "storm_sheltered";
    // Epilogue.
    public const string WokeAfterStorm = "woke_after_storm";
    public const string TentStruck = "tent_struck";
    public const string AshRaked = "ash_raked";
    public const string StonesScattered = "stones_scattered";
    public const string WalkedOut = "walked_out";
    public const string CreditsFinished = "credits_finished";

    /// Events something in today's world raises. An objective or end act whose
    /// event is not here waits for its place or prop to be built: until then the
    /// game does not require it, so a band is never held by something that
    /// cannot happen. Add an event here in the change that wires it.
    public static readonly HashSet<string> Built = new()
    {
        LetterRead, LogCarried, StoneSkips, StoneSunk, PlaceTrailhead, PlacePierEnd, SatPier, FireLit,
        LanternLit, TentEntered, RestBedroll, RestFire, TurnedIn,
        TinOpened, PlaceOldRing, BlazeFound, SatLookout, StepLifted, FireFedInRain, StormSheltered,
        WokeAfterStorm, TentStruck, AshRaked, StonesScattered, WalkedOut, CreditsFinished,
    };

    /// The name part of `name:value`, which is what `Built` lists for counted events.
    public static string BaseName(string ev)
    {
        int colon = ev.IndexOf(':');
        if (colon < 0) return ev;
        return int.TryParse(ev[(colon + 1)..], out _) ? ev[..colon] : ev;
    }

    public static bool IsBuilt(string ev) => Built.Contains(BaseName(ev)) || Built.Contains(ev);

    /// True when a raised event satisfies a wanted one: equal, or the same
    /// `name:N` with a raised value at least the wanted one.
    public static bool Satisfies(string raised, string wanted)
    {
        if (raised == wanted) return true;
        int a = raised.IndexOf(':'), b = wanted.IndexOf(':');
        if (a < 0 || b < 0 || raised[..a] != wanted[..b]) return false;
        return int.TryParse(raised[(a + 1)..], out int have) && int.TryParse(wanted[(b + 1)..], out int need) && have >= need;
    }
}
