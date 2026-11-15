namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the efficiency report at the end of a case (<see cref="FinalState.ReportTexts"/>), in the order the
/// original's data holds them. The texts themselves are the player's FINAL.EXE's; character 0x89 of the game's font
/// stands before the slash of every "earned/possible" pair.
/// </summary>
// orig: FINAL DS:2a1a to DS:2af8, the string literals of show_efficiency_report (src/c/final/final_0100_2e06.c)
public enum ReportText
{
    /// <summary>"Efficiency Report": the screen's title.</summary>
    Title,

    /// <summary>", " between a person's name and rank.</summary>
    Comma,

    /// <summary>"/" between a person's organisation and city.</summary>
    Slash,

    /// <summary>"At Large".</summary>
    AtLarge,

    /// <summary>"ARRESTED".</summary>
    Arrested,

    /// <summary>"TURNED".</summary>
    Turned,

    /// <summary>"EP:" before a member's earned points.</summary>
    MemberPoints,

    /// <summary>"\x89/" before a member's possible points.</summary>
    MemberPossible,

    /// <summary>"EP:0": an object not found.</summary>
    ObjectNotEarned,

    /// <summary>"CONFISCATED".</summary>
    Confiscated,

    /// <summary>"EP:50": an object confiscated.</summary>
    ObjectEarned,

    /// <summary>"Not found".</summary>
    NotFound,

    /// <summary>"\x89/50": an object's possible points.</summary>
    ObjectPossible,

    /// <summary>" " between an event's name and the member who acts in it.</summary>
    Blank,

    /// <summary>"EP: 0/": an event that happened earns nothing.</summary>
    EventPoints,

    /// <summary>"Prevented crime".</summary>
    PreventedCrime,

    /// <summary>"EP:100\x89/100".</summary>
    PreventedPoints,

    /// <summary>"": the start of the Double Agents' card.</summary>
    Empty,

    /// <summary>" Double Agent(s)".</summary>
    DoubleAgents,

    /// <summary>"remained undetected".</summary>
    RemainedUndetected,

    /// <summary>"within the CIA.".</summary>
    WithinTheCia,

    /// <summary>"EP:0/": the Double Agents earn nothing.</summary>
    DoubleAgentPoints,

    /// <summary>"Efficiency Rating".</summary>
    Rating,

    /// <summary>"--- " before the rating.</summary>
    RatingOpen,

    /// <summary>" ---" after the rating.</summary>
    RatingClose,

    /// <summary>"EP:" before the case's earned points.</summary>
    TotalPoints,

    /// <summary>"\x89/" before the case's possible points.</summary>
    TotalPossible,

    /// <summary>The number of texts.</summary>
    Count,
}
