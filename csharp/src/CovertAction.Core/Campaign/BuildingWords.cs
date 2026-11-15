using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The texts of a building's menu: what the player may do at the building of an organisation, the arrival at the CIA building and the stakeout of a building. Texts of the program, read from the player's GAME.EXE.
/// </summary>
// orig: GAME DS:21c6..23fd of building_menu (game_00f7_1e5a.c), arrive_at_cia_building (game_00f7_231a.c) and watch_building_exit (game_00f7_23c9.c)
public sealed class BuildingWords
{
    /// <summary>GAME DS:21c6: the start of the building's box ("You are at the" and a line break), before the organisation's name.</summary>
    public string YouAreAt { get; set; } = "";

    /// <summary>GAME DS:21d6: the building's name when the player has not identified it.</summary>
    public string UnknownBuilding { get; set; } = "";

    /// <summary>GAME DS:21e7: the hint for an alert level of 16 to 31: a few guards.</summary>
    public string FewGuards { get; set; } = "";

    /// <summary>GAME DS:2216: the hint for an alert level of 32 to 47: extra guards.</summary>
    public string ExtraGuards { get; set; } = "";

    /// <summary>GAME DS:2241: the hint for an alert level of 48 and above: the building swarms with guards.</summary>
    public string SwarmingGuards { get; set; } = "";

    /// <summary>GAME DS:2268: the question and the first choice, "Place wiretap".</summary>
    public string DoYouPlaceWiretap { get; set; } = "";

    /// <summary>GAME DS:2284: the second choice.</summary>
    public string BreakIntoBuilding { get; set; } = "";

    /// <summary>GAME DS:229a: the last three choices: watch the building, the data files, leave.</summary>
    public string WatchCheckLeave { get; set; } = "";

    /// <summary>GAME DS:22c2: the name of the animation behind the box (a template: the digit in it is replaced by the building's level).</summary>
    public string BuildingScene { get; set; } = "";

    /// <summary>GAME DS:22cf: the picture of a break-in about to begin.</summary>
    public string BreakInPicture { get; set; } = "";

    /// <summary>GAME DS:22db: the picture of a wiretap about to be placed.</summary>
    public string WiretapPicture { get; set; } = "";

    /// <summary>GAME DS:22e7: the start of the wiretap box ("The guards are "), before the alertness word.</summary>
    public string GuardsAre { get; set; } = "";

    /// <summary>GAME DS:22f7: after the alertness word, before the minutes the tap has.</summary>
    public string YouWillHave { get; set; } = "";

    /// <summary>GAME DS:2308: after the minutes.</summary>
    public string MinutesEnd { get; set; } = "";

    /// <summary>GAME DS:2313: the animation of the agent being followed on leaving.</summary>
    public string FollowedScene { get; set; } = "";

    /// <summary>GAME DS:2320: the tag of the prose text of being followed ("*followed"); the reader may change it.</summary>
    public TextBuffer FollowedProseKey { get; } = new(10);

    /// <summary>GAME DS:232a: the animation of the CIA building ("hq.pan").</summary>
    public string HeadquartersScene { get; set; } = "";

    /// <summary>GAME DS:2331: the box on arriving at the CIA building at the easiest level.</summary>
    public string ArriveAtCia { get; set; } = "";

    /// <summary>GAME DS:236e: the animation of the stakeout through binoculars.</summary>
    public string BinocularsScene { get; set; } = "";

    /// <summary>GAME DS:2379: the start of the stakeout box ("You notice someone").</summary>
    public string YouNotice { get; set; } = "";

    /// <summary>GAME DS:238c: the rest of the first line ("leaving the building").</summary>
    public string LeavingTheBuilding { get; set; } = "";

    /// <summary>GAME DS:23a4: the question and the choices of the stakeout: wait, follow the car, trace the car, the data files.</summary>
    public string WatchMenu { get; set; } = "";

    /// <summary>GAME DS:23db: the start of the box about the man leaving.</summary>
    public string HeLooks { get; set; } = "";

    /// <summary>GAME DS:23e5: the start of the box about the woman leaving.</summary>
    public string SheLooks { get; set; } = "";

    /// <summary>GAME DS:23f0: the end of that box (a line break and a full stop).</summary>
    public string PeriodLine { get; set; } = "";

    /// <summary>GAME DS:23f3: the animation of the car tracer.</summary>
    public string TraceScene { get; set; } = "";
}
