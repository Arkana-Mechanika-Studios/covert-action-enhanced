namespace CovertAction.Core.Campaign;

/// <summary>
/// The texts of the search's findings: the messages the player learns of and the hints he is given. Texts of the program, read from the player's GAME.EXE.
/// </summary>
// orig: GAME DS:2ab8..2b39 of search_yield (game_00f7_3fa6.c)
public sealed class InvestigationWords
{
    /// <summary>GAME DS:2ab8: the heading of a coded message.</summary>
    public string CodedMessageHeading { get; set; } = "";

    /// <summary>GAME DS:2acf: the heading of the notes of a meeting.</summary>
    public string MeetingNotesHeading { get; set; } = "";

    /// <summary>GAME DS:2ae6: the heading of a message the player has not seen a part of.</summary>
    public string NewInformation { get; set; } = "";

    /// <summary>GAME DS:2afe: the heading of one he has.</summary>
    public string AdditionalInformation { get; set; } = "";

    /// <summary>GAME DS:2b1a: between the name and the message.</summary>
    public string NameSpace { get; set; } = "";

    /// <summary>GAME DS:2b1c: the start of the line of what the player learned of the message.</summary>
    public string DotsLine { get; set; } = "";

    /// <summary>GAME DS:2b22: before the date.</summary>
    public string On { get; set; } = "";

    /// <summary>GAME DS:2b26: between the month and the day.</summary>
    public string DateSpace { get; set; } = "";

    /// <summary>GAME DS:2b28: before the other person's organisation.</summary>
    public string OfThe { get; set; } = "";

    /// <summary>GAME DS:2b30: before the other person's city.</summary>
    public string In { get; set; } = "";

    /// <summary>GAME DS:2b34: the end of the line.</summary>
    public string LineEnd { get; set; } = "";

    /// <summary>GAME DS:2b37: the text of a hint that only shows a face.</summary>
    public string LooksHint { get; set; } = "";

    /// <summary>GAME DS:2b39: between a building's kind and its city.</summary>
    public string BuildingIn { get; set; } = "";
}
