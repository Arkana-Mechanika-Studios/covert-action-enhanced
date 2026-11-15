namespace CovertAction.Core.Campaign;

/// <summary>
/// The words of a person's dossier card ("Name:", "Rank:", "Recruited by ... in ...", "Role:", "(Under Arrest)" ...):
/// texts of the program, read from the player's GAME.EXE. Character 0x80 in a label is the font's gap between a
/// label and its value.
/// </summary>
// orig: GAME DS:3208..32ae and DS:2d48..2d9a, the literals of draw_person_dossier (src/c/game/game_00f7_58dc.c) and rank_name_table (DS:3022)
public sealed class DossierWords
{
    /// <summary>Ranks a person can have in a plot, Recruit to MasterMind.</summary>
    public const int Ranks = 8;

    /// <summary>GAME DS:3209: the label of the name.</summary>
    public string Name { get; set; } = "";

    /// <summary>GAME DS:3210: the label of the rank.</summary>
    public string Rank { get; set; } = "";

    /// <summary>GAME DS:3217: the label of the organisation.</summary>
    public string Org { get; set; } = "";

    /// <summary>GAME DS:321e: the label of the city.</summary>
    public string City { get; set; } = "";

    /// <summary>GAME DS:3226: "Recruited", the start of the line on who recruited the person and where.</summary>
    public string Recruited { get; set; } = "";

    /// <summary>GAME DS:3230: " by " before the recruiter's name.</summary>
    public string By { get; set; } = "";

    /// <summary>GAME DS:3235: " in " before the city of the recruiting.</summary>
    public string In { get; set; } = "";

    /// <summary>GAME DS:323a: the full stop of that line.</summary>
    public string FullStop { get; set; } = "";

    /// <summary>GAME DS:323c: the label of a role the person plays in the plot.</summary>
    public string Role { get; set; } = "";

    /// <summary>GAME DS:3244: the role line of a person known to have nothing to do with the plot.</summary>
    public string RoleNotInvolved { get; set; } = "";

    /// <summary>GAME DS:3258: the role line of a person whose role is not known.</summary>
    public string RoleUnknown { get; set; } = "";

    /// <summary>GAME DS:3267: the line of a person under arrest.</summary>
    public string UnderArrest { get; set; } = "";

    /// <summary>GAME DS:3276: the line of a person in hiding.</summary>
    public string InHiding { get; set; } = "";

    /// <summary>GAME DS:3282: the line of a person turned double agent.</summary>
    public string Turned { get; set; } = "";

    /// <summary>GAME DS:3290: what stands for a message the player has not decoded.</summary>
    public string MessageNotDecoded { get; set; } = "";

    /// <summary>GAME DS:32a6: the note that the card goes on, on the next page.</summary>
    public string More { get; set; } = "";

    /// <summary>GAME DS:328c: the quote before a decoded event's text.</summary>
    public string OpenQuote { get; set; } = "";

    /// <summary>GAME DS:328e: the quote after it.</summary>
    public string CloseQuote { get; set; } = "";

    /// <summary>GAME DS:3022 rank_name_table: the ranks' names, Recruit (0) to MasterMind (7).</summary>
    public string[] RankNames { get; } = new string[Ranks];
}
