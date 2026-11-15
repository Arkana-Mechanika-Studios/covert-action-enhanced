namespace CovertAction.Core.Campaign;

/// <summary>
/// The boxes that tell the player what he has learned of a person ("... has been positively identified as ...",
/// "We have acquired a photograph of ...") and the words of an organisation's alertness ("quiet", "cautious" ...):
/// texts of the program, read from the player's GAME.EXE.
/// </summary>
/// <remarks>
/// The original has a line break of its own after each box's name (DS:2c32, 2c70, 2c9c, 2cd2, 2cf4, 2d1d, 2d46),
/// all the same text; the rewrite keeps the first.
/// </remarks>
// orig: GAME DS:2bd8..2d47, the literals of announce_new_knowledge (src/c/game/game_00f7_4a04.c); DS:2769..27a4, of append_alert_level (game_00f7_3a95.c)
public sealed class KnowledgeWords
{
    /// <summary>Words of an organisation's alertness, unsuspecting to very nervous.</summary>
    public const int AlertLevels = 5;

    /// <summary>GAME DS:2bd9: after the name of a person identified as a participant in the plot.</summary>
    public string Participant { get; set; } = "";

    /// <summary>GAME DS:2c0e: after the code name of a person positively identified, before the real name.</summary>
    public string Identified { get; set; } = "";

    /// <summary>GAME DS:2c32: the line break after a name.</summary>
    public string LineBreak { get; set; } = "";

    /// <summary>GAME DS:2c34: before the name of a person the player has a photograph of.</summary>
    public string Photograph { get; set; } = "";

    /// <summary>GAME DS:2c56: the full stop and line break after it.</summary>
    public string PhotographEnd { get; set; } = "";

    /// <summary>GAME DS:2c5a: after the name of a person spotted in a city, before the city.</summary>
    public string Spotted { get; set; } = "";

    /// <summary>GAME DS:2c73: after the name of a person identified as a member of an organisation.</summary>
    public string Member { get; set; } = "";

    /// <summary>GAME DS:2c9e: before the name of a person the player has learned more of.</summary>
    public string AdditionalInformation { get; set; } = "";

    /// <summary>GAME DS:2cd4: before the name of a person whose rank the player has determined.</summary>
    public string Rank { get; set; } = "";

    /// <summary>GAME DS:2cf6: before the name of a person the player has learned where he was recruited.</summary>
    public string RecruitedWhere { get; set; } = "";

    /// <summary>GAME DS:2d1f: before the name of a person the player has learned who recruited him (the same words).</summary>
    public string RecruitedByWhom { get; set; } = "";

    /// <summary>GAME DS:2769: the alertness of an organisation that has nobody in the city.</summary>
    public string Quiet { get; set; } = "";

    /// <summary>GAME DS:276f, 277c, 2785, 2790, 2798: the alertness by level, unsuspecting to very nervous.</summary>
    public string[] Alertness { get; } = new string[AlertLevels];
}
