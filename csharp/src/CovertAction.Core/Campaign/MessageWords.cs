namespace CovertAction.Core.Campaign;

/// <summary>
/// The words a line about an intercepted message is put together from ("Sent msg to someone of the ... in ... on
/// ..."): texts of the program, read from the player's GAME.EXE.
/// </summary>
// orig: GAME DS:32af..32e8, the literals of append_message_text (src/c/game/game_00f7_5f21.c)
public sealed class MessageWords
{
    /// <summary>GAME DS:32af: the line of a received message that is no event of the plot.</summary>
    public string ReceivedFrom { get; set; } = "";

    /// <summary>GAME DS:32bb: the line of a sent message that is no event of the plot.</summary>
    public string SentTo { get; set; } = "";

    /// <summary>GAME DS:32c7: the space before the other party's name.</summary>
    public string BeforeName { get; set; } = "";

    /// <summary>GAME DS:32c9: the other party when the player does not know who it is.</summary>
    public string Someone { get; set; } = "";

    /// <summary>GAME DS:32d2: before the other party's organisation.</summary>
    public string OfThe { get; set; } = "";

    /// <summary>GAME DS:32db: before the other party's city.</summary>
    public string In { get; set; } = "";

    /// <summary>GAME DS:32e0: before the date.</summary>
    public string On { get; set; } = "";

    /// <summary>GAME DS:32e5: between the month and the day.</summary>
    public string BeforeDay { get; set; } = "";

    /// <summary>GAME DS:32e7: the end of the line.</summary>
    public string End { get; set; } = "";
}
