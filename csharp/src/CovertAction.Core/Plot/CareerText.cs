namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the career summary (<see cref="FinalState.CareerTexts"/>), in the order the original's data holds them.
/// </summary>
// orig: FINAL DS:2c91 to DS:2d47, the string literals of show_career_summary (src/c/final/final_0100_35d6.c)
public enum CareerText
{
    /// <summary>"Career": the screen's title on its first page.</summary>
    Title,

    /// <summary>"The Career of".</summary>
    CareerOf,

    /// <summary>"Max '" before the code name.</summary>
    BeforeCodeName,

    /// <summary>"' Remington" after it.</summary>
    AfterCodeName,

    /// <summary>"Case #".</summary>
    Case,

    /// <summary>", " before the month.</summary>
    BeforeMonth,

    /// <summary>" " before the year.</summary>
    BeforeYear,

    /// <summary>".  Arrests: ".</summary>
    Arrests,

    /// <summary>" EP: ".</summary>
    Points,

    /// <summary>"... couldn't stop ": the crime was carried out.</summary>
    CouldNotStop,

    /// <summary>"... prevented ".</summary>
    Prevented,

    /// <summary>"/" between the crime and its target.</summary>
    Slash,

    /// <summary>"... arrested ".</summary>
    ArrestedMastermind,

    /// <summary>" MasterMind!".</summary>
    MastermindEnd,

    /// <summary>"Career": the title of a following page.</summary>
    NextPageTitle,

    /// <summary>"Arrested, MasterMinds: ".</summary>
    TotalMasterminds,

    /// <summary>"  Agents: ".</summary>
    TotalAgents,

    /// <summary>"Career": the title of the gallery.</summary>
    GalleryTitle,

    /// <summary>"MasterMinds Arrested".</summary>
    Gallery,

    /// <summary>The number of texts.</summary>
    Count,
}
