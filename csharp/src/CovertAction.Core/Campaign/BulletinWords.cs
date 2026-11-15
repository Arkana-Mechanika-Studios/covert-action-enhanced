namespace CovertAction.Core.Campaign;

/// <summary>
/// The bulletins the plot's progress brings the player (a suspect gone into or come out of hiding, a plot object
/// spotted, message traffic, a wiretap's or a double agent's report, an airport sighting) and the box of the full
/// message log: texts of the program, read from the player's GAME.EXE.
/// </summary>
/// <remarks>
/// The original has some of these texts twice, one copy per place that uses it ("Surveillance Team" at DS:3b45,
/// 3cab and 3ce2; "Airport Surveillance" at 3c38 and 3c6f; the line breaks): the rewrite keeps one of each.
/// </remarks>
// orig: GAME DS:3b22..3c83, the literals of advance_world_clock (src/c/game/game_00f7_8755.c); DS:3c86..3cf3 of event_ready (game_00f7_92b9.c); DS:3886 of queue_message
public sealed class BulletinWords
{
    /// <summary>GAME DS:3886 message_log_full_text: the box shown when the message log is full.</summary>
    public string LogFull { get; set; } = "";

    /// <summary>GAME DS:3b22: the start of a bulletin on a suspect gone into hiding.</summary>
    public string GoneIntoHidingStart { get; set; } = "";

    /// <summary>GAME DS:3b2d: after the suspect's name.</summary>
    public string GoneIntoHiding { get; set; } = "";

    /// <summary>GAME DS:3b45: the heading of a surveillance team's bulletin.</summary>
    public string SurveillanceTeam { get; set; } = "";

    /// <summary>GAME DS:3c86: the start of a bulletin on a suspect come out of hiding.</summary>
    public string OutOfHidingStart { get; set; } = "";

    /// <summary>GAME DS:3c91: after the suspect's name.</summary>
    public string OutOfHiding { get; set; } = "";

    /// <summary>GAME DS:3b57: the file the campaign is saved to when the case ends ("env.sve").</summary>
    public string EndOfCaseSave { get; set; } = "";

    /// <summary>GAME DS:3b5f: the start of INTERPOL's bulletin on a plot object.</summary>
    public string InterpolStart { get; set; } = "";

    /// <summary>GAME DS:3b73: after the object's name, before the city.</summary>
    public string SpottedIn { get; set; } = "";

    /// <summary>GAME DS:3b80: the end of that bulletin.</summary>
    public string Exclamation { get; set; } = "";

    /// <summary>GAME DS:3b83: the heading of INTERPOL's bulletin.</summary>
    public string Interpol { get; set; } = "";

    /// <summary>GAME DS:3b8d: the start of a satellite intercept.</summary>
    public string MessageTraffic { get; set; } = "";

    /// <summary>GAME DS:3bac: before the city the traffic came from.</summary>
    public string From { get; set; } = "";

    /// <summary>GAME DS:3bb3: before the city the traffic went to.</summary>
    public string To { get; set; } = "";

    /// <summary>GAME DS:3bb8: the end of the intercept.</summary>
    public string TrafficEnd { get; set; } = "";

    /// <summary>GAME DS:3bbe: the heading of a satellite intercept.</summary>
    public string SatelliteIntercept { get; set; } = "";

    /// <summary>GAME DS:3bd2: the start of a double agent's report.</summary>
    public string DoubleAgent { get; set; } = "";

    /// <summary>GAME DS:3be1: the start of a wiretap's report.</summary>
    public string WiretapAt { get; set; } = "";

    /// <summary>GAME DS:3bed: between the organisation and the city.</summary>
    public string Slash { get; set; } = "";

    /// <summary>GAME DS:3bef: after the place.</summary>
    public string Reveals { get; set; } = "";

    /// <summary>GAME DS:3bf9: the double agent's man.</summary>
    public string He { get; set; } = "";

    /// <summary>GAME DS:3bfd: the double agent's woman.</summary>
    public string She { get; set; } = "";

    /// <summary>GAME DS:3c02: the space after the suspect's name.</summary>
    public string Space { get; set; } = "";

    /// <summary>GAME DS:3c04: a line break.</summary>
    public string LineBreak { get; set; } = "";

    /// <summary>GAME DS:3c07: the start of an airport's report on a return.</summary>
    public string AirportFrom { get; set; } = "";

    /// <summary>GAME DS:3c12: after the suspect's name, before the city he went to.</summary>
    public string ReturnedFrom { get; set; } = "";

    /// <summary>GAME DS:3c35: the full stop and line break at the end of an airport's report.</summary>
    public string FullStop { get; set; } = "";

    /// <summary>GAME DS:3c38: the heading of an airport's report.</summary>
    public string AirportSurveillance { get; set; } = "";

    /// <summary>GAME DS:3c4e: after the suspect's name, before the city he left.</summary>
    public string DepartedFrom { get; set; } = "";
}
