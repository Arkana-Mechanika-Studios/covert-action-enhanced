namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the Hall of Fame screen (<see cref="FinalState.FameTexts"/>), in the order the original's data holds
/// them. Characters 0x80 and 0x83 of the game's font are narrow spaces.
/// </summary>
// orig: FINAL DS:32b2 to DS:331e, the string literals of hall_of_fame_screen (src/c/final/final_0100_4d67.c)
public enum FameText
{
    /// <summary>"Hall of Fame": the screen's title.</summary>
    Title,

    /// <summary>"COVERT ACTION".</summary>
    Heading,

    /// <summary>"HALL OF FAME".</summary>
    Subheading,

    /// <summary>" ---": an empty place.</summary>
    EmptyPlace,

    /// <summary>". " after the place's number.</summary>
    AfterPlace,

    /// <summary>"Max '" before the code name.</summary>
    BeforeCodeName,

    /// <summary>"' Remington,  Case #" after it.</summary>
    AfterCodeName,

    /// <summary>" " before the month.</summary>
    BeforeMonth,

    /// <summary>" " before the year.</summary>
    BeforeYear,

    /// <summary>"/" between the crime and its target.</summary>
    Slash,

    /// <summary>"EP: " before the efficiency points.</summary>
    Points,

    /// <summary>"    \x80--- SCORE: " before the score.</summary>
    Score,

    /// <summary>" ---" after the score.</summary>
    AfterScore,

    /// <summary>"C\x83lear": the button that clears the Hall of Fame.</summary>
    Clear,

    /// <summary>The number of texts.</summary>
    Count,
}
