using System.Collections.Generic;

namespace LastCamp;

/// Every sound id the game plays, as named in `audio/generated/manifest.json` and
/// docs/sound-list.md. Code refers to sounds only through these constants, so a test
/// can prove each one exists in the manifest.
public static class Sounds
{
    // Ambience beds.
    public const string BedDayWoods = "bed_day_woods";
    public const string BedEvening = "bed_evening";
    public const string BedNight = "bed_night";
    public const string BedWindCanopy = "bed_wind_canopy";
    public const string BedPondLap = "bed_pond_lap";
    public const string BedFire = "bed_fire";
    public const string BedRain = "bed_rain";
    public const string BedRainCanvas = "bed_rain_canvas";
    public const string BedStormWind = "bed_storm_wind";
    public const string BedWading = "bed_wading";

    // Wildlife and weather.
    public const string BirdsDay = "birds_day";
    public const string Owl = "owl";
    public const string Loon = "loon";
    public const string Thunder = "thunder";
    public const string FireHiss = "fire_hiss";

    // The player.
    public const string StepGrass = "step_grass";
    public const string StepDirt = "step_dirt";
    public const string StepWood = "step_wood";
    public const string StepRock = "step_rock";
    public const string StepWater = "step_water";
    public const string StepHollow = "step_hollow";
    public const string WaterIn = "water_in";
    public const string WaterOut = "water_out";

    // Actions.
    public const string LogPickup = "log_pickup";
    public const string LogIntoFire = "log_into_fire";
    public const string StonePickup = "stone_pickup";
    public const string StoneThrow = "stone_throw";
    public const string StoneSkip = "stone_skip";
    public const string StoneSink = "stone_sink";
    public const string LanternOn = "lantern_on";
    public const string LanternOff = "lantern_off";
    public const string FireLight = "fire_light";
    public const string TentEnter = "tent_enter";
    public const string TinOpen = "tin_open";
    public const string PageTurn = "page_turn";
    public const string LetterUnfold = "letter_unfold";
    public const string StepLift = "step_lift";
    public const string TinLift = "tin_lift";
    public const string AshRake = "ash_rake";
    public const string RingStone = "ring_stone";
    public const string TentPeg = "tent_peg";
    public const string TentFall = "tent_fall";
    public const string SitDown = "sit_down";

    // Interface and story.
    public const string UiMove = "ui_move";
    public const string UiSelect = "ui_select";
    public const string JournalOpen = "journal_open";
    public const string JournalClose = "journal_close";
    public const string CardIn = "card_in";

    /// Footstep surface name (what the player and the film raise) to its sound set.
    public static readonly IReadOnlyDictionary<string, string> Footsteps = new Dictionary<string, string>
    {
        ["grass"] = StepGrass, ["dirt"] = StepDirt, ["wood"] = StepWood, ["rock"] = StepRock,
        ["water"] = StepWater, ["hollow"] = StepHollow,
    };

    /// The kinds `AudioDirector.play_interact` accepts: every action, interface and
    /// story one-shot in the sound list.
    public static readonly string[] Interactions =
    {
        LogPickup, LogIntoFire, StonePickup, StoneThrow, StoneSkip, StoneSink, LanternOn, LanternOff,
        FireLight, TentEnter, TinOpen, PageTurn, LetterUnfold, StepLift, TinLift, AshRake, RingStone,
        TentPeg, TentFall, SitDown, UiMove, UiSelect, JournalOpen, JournalClose, CardIn,
    };
}
