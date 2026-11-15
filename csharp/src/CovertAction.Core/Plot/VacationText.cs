namespace CovertAction.Core.Final;

/// <summary>
/// The texts of the vacation after a case (<see cref="FinalState.VacationTexts"/>), in the order the original's data
/// holds them: the captions of the four places, the archives and the parts of the pictures' file names.
/// </summary>
// orig: FINAL DS:2de6 to DS:2f30, the string literals of post_case_vacation (src/c/final/final_0100_3bcc.c)
public enum VacationText
{
    /// <summary>The caption of the first place, the LaundroMat; the other three follow in the order of the places.</summary>
    LaundromatCaption,

    /// <summary>The caption of the secretarial pool.</summary>
    PoolCaption,

    /// <summary>The caption of the beach.</summary>
    BeachCaption,

    /// <summary>The caption of the casino.</summary>
    CasinoCaption,

    /// <summary>"final4.cat": the archive of the first two places' pictures.</summary>
    Archive4,

    /// <summary>"final3.cat": the archive of the beach's and the casino's pictures.</summary>
    Archive3,

    /// <summary>"back.pic": after the place's prefix, its background.</summary>
    Background,

    /// <summary>"hand": after the place's prefix, the agent's hand ...</summary>
    Hand,

    /// <summary>"f.pic": ... Maxine's ...</summary>
    FemaleHand,

    /// <summary>"m.pic": ... or Max's.</summary>
    MaleHand,

    /// <summary>"laudude1.pic": the man who may stand in the first two places.</summary>
    ExtraMan,

    /// <summary>"dude": after the place's prefix, the men who come to Maxine ...</summary>
    Dude,

    /// <summary>"babe": ... or the women who come to Max; their number follows.</summary>
    Babe,

    /// <summary>".pic": the end of their file names.</summary>
    PictureEnd,

    /// <summary>The number of texts.</summary>
    Count,
}
