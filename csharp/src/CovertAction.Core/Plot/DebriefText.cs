namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the debriefing after a case (<see cref="FinalState.DebriefTexts"/>), in the order the original's data
/// holds them: the file names, what the CIA chief says, the promotion and retirement texts and the end-of-case menus.
/// </summary>
/// <remarks>
/// The program has four copies of the animation's file name and one of the plot file's; each call of the debriefing
/// reads its own, and they all hold the same text, so one entry of each stands for them.
/// </remarks>
// orig: FINAL DS:2504 to DS:289e, the string literals of debrief_wrapup (src/c/final/final_0100_175e.c)
public enum DebriefText
{
    /// <summary>"briefing.pan": the animation of the CIA chief that runs beside every box of the debriefing.</summary>
    AnimationFile,

    /// <summary>"It looks like that about wraps it up, Max. ": the chief's first words.</summary>
    ChiefClosingA,

    /// <summary>"We've been able to put together a pretty good chronology of this case, take a look.": the rest of them.</summary>
    ChiefClosingB,

    /// <summary>"icons.pic": the picture the case log's roster takes its icons from.</summary>
    IconsPicture,

    /// <summary>"*PL0000": the template of the tag of the plot's closing text in the plot file; its digits are the plot, the variant and the page.</summary>
    PlotTagTemplate,

    /// <summary>"plot.txt": the file of the plots' texts.</summary>
    PlotFile,

    /// <summary>"Interrogation of the suspects has revealed some additional information about Mr.Big."</summary>
    InterrogationRevealed,

    /// <summary>"You have captured the MasterMind controlling the ": before the organisation's name.</summary>
    CapturedBefore,

    /// <summary>"! The country has been saved from a dangerous plot. ": behind the organisation's name.</summary>
    CapturedMiddle,

    /// <summary>"Congratulations, the President commends you.", with its line break.</summary>
    CapturedAfter,

    /// <summary>"Congratulations Max, we've decided to promote you from 00": before the old rank.</summary>
    PromotionBefore,

    /// <summary>" rank to 00": between the old and the new rank.</summary>
    PromotionMiddle,

    /// <summary>" rank. Here's a commendation from the President.": behind the new rank.</summary>
    PromotionAfter,

    /// <summary>"INCREDIBLE: You have arrested all 26 Masterminds!", in two lines.</summary>
    AllMasterMinds,

    /// <summary>"You have completed a ten year career with the CIA.", in two lines.</summary>
    TenYears,

    /// <summary>"You retire to a well-deserved life of relaxation.", in two lines: the end of both retirement texts.</summary>
    Retire,

    /// <summary>"Do you want to..." with its choices: Continue Game, Save Game, End Game.</summary>
    EndMenu,

    /// <summary>" Change Difficulty Level": the fourth choice, when the player may take another level.</summary>
    ChangeLevelItem,

    /// <summary>"Which difficulty level?" and the four levels' names.</summary>
    LevelMenu,

    /// <summary>"Do you want to..." with the choices Continue Game, End Game: asked after the game was saved.</summary>
    EndMenuAfterSave,

    /// <summary>The number of texts.</summary>
    Count,
}
