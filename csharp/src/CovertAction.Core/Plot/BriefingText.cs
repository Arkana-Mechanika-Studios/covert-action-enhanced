namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the briefing's first pages (<see cref="FinalState.BriefingTexts"/>), in the order the original's data
/// holds them. The texts themselves are the player's FINAL.EXE's.
/// </summary>
// orig: FINAL DS:301c to DS:3236, the string literals of briefing_intro_text (src/c/final/final_0100_499e.c)
public enum BriefingText
{
    /// <summary>"We have indications that ".</summary>
    Indications,

    /// <summary>"an operation is in preparation somewhere in ".</summary>
    OperationSomewhere,

    /// <summary>"Europe.": the first of the three regions, in the order of the worlds (0..2).</summary>
    Europe,

    /// <summary>"Africa/Middle East.".</summary>
    AfricaMiddleEast,

    /// <summary>"Central America.".</summary>
    CentralAmerica,

    /// <summary>" The ", before the name of the organisation known to be involved.</summary>
    The,

    /// <summary>" is known to be involved.".</summary>
    KnownToBeInvolved,

    /// <summary>"Your mission is to prevent this operation from succeeding ".</summary>
    Mission,

    /// <summary>"and to capture as many of the participants as possible.".</summary>
    CaptureParticipants,

    /// <summary>" Caution, one or more Double Agents are believed to be active within the CIA.".</summary>
    DoubleAgents,

    /// <summary>"I'm afraid you're not quite up to speed yet Max. ": the copy-protection question was answered wrong.</summary>
    NotUpToSpeed,

    /// <summary>"Perhaps you should practice your skills before you take this case.".</summary>
    PracticeFirst,

    /// <summary>"OK, you're cleared. ": the first case after the question was answered.</summary>
    Cleared,

    /// <summary>"Here is some info we've picked up over the last few days. ".</summary>
    InfoPickedUp,

    /// <summary>"We think it's linked to this operation...\n".</summary>
    LinkedToOperation,

    /// <summary>The number of texts.</summary>
    Count,
}
