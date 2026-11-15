namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the case's action log in the debriefing (<see cref="FinalState.LogTexts"/>), in the order the original's
/// data holds them; several are the same words, each the original's own copy.
/// </summary>
// orig: FINAL DS:28c8 to DS:29f7, the string literals of case_action_log (src/c/final/final_0100_1da2.c)
public enum LogText
{
    /// <summary>"": the start of the line of the person who acted.</summary>
    ActorStart,

    /// <summary>" (" before the actor's role.</summary>
    ActorRoleOpen,

    /// <summary>") " after it.</summary>
    ActorRoleClose,

    /// <summary>" " after a person without a role.</summary>
    ActorNoRole,

    /// <summary>"/" between the actor's organisation and city.</summary>
    ActorSlash,

    /// <summary>" sent message to " (a message of no event).</summary>
    SentMessageTo,

    /// <summary>"^" before the message.</summary>
    MessageOpen,

    /// <summary>"^" after it.</summary>
    MessageClose,

    /// <summary>"": the start of the event's line.</summary>
    EventStart,

    /// <summary>"sent message to".</summary>
    EventMessage,

    /// <summary>"sent package to".</summary>
    EventPackage,

    /// <summary>"": the start of the line of the person acted upon.</summary>
    TargetStart,

    /// <summary>" (" before the target's role.</summary>
    TargetRoleOpen,

    /// <summary>") " after it.</summary>
    TargetRoleClose,

    /// <summary>"/" between the target's organisation and city.</summary>
    TargetSlash,

    /// <summary>"^" before the event's message.</summary>
    EventMessageOpen,

    /// <summary>"^" after it.</summary>
    EventMessageClose,

    /// <summary>" (" before the holder's role.</summary>
    HolderRoleOpen,

    /// <summary>") " after it.</summary>
    HolderRoleClose,

    /// <summary>"now has the ".</summary>
    NowHas,

    /// <summary>"": the start of a "left" line.</summary>
    LeftStart,

    /// <summary>" left ".</summary>
    Left,

    /// <summary>"You traveled to ".</summary>
    Traveled,

    /// <summary>"You broke in to ".</summary>
    BrokeIn,

    /// <summary>"/" between the building's organisation and city.</summary>
    BrokeInSlash,

    /// <summary>"You arrested ".</summary>
    Arrested,

    /// <summary>"You confiscated ".</summary>
    Confiscated,

    /// <summary>" went into hiding.".</summary>
    WentIntoHiding,

    /// <summary>"Mission completed.".</summary>
    MissionCompleted,

    /// <summary>"Unable to".</summary>
    UnableTo,

    /// <summary>"complete mission.".</summary>
    CompleteMission,

    /// <summary>" came out of hiding.".</summary>
    CameOutOfHiding,

    /// <summary>" escaped from jail.".</summary>
    EscapedFromJail,

    /// <summary>" released by exchange.".</summary>
    ReleasedByExchange,

    /// <summary>"You turned ".</summary>
    Turned,

    /// <summary>The number of texts.</summary>
    Count,
}
