namespace CovertAction.Core.Shared;

/// <summary>
/// The words a line of the message log is put together from ("Sent msg to", " someone", " of the " ...) and the
/// month names of its date: texts of the part's program, filled in from it (never spelled in the C#).
/// </summary>
public sealed class MessageLogTexts
{
    /// <summary>The months of the year, "Jan" .. "Dec".</summary>
    public string[] MonthNames { get; } = new string[12];

    /// <summary>What a received message without an event says: "Rcvd msg fm".</summary>
    public string Received { get; set; } = "";

    /// <summary>What a sent message without an event says: "Sent msg to".</summary>
    public string Sent { get; set; } = "";

    /// <summary>A blank: before a known name, and between month and day.</summary>
    public string Space { get; set; } = "";

    /// <summary>For an unknown other party: " someone".</summary>
    public string Someone { get; set; } = "";

    /// <summary>Before the organisation: " of the ".</summary>
    public string OfThe { get; set; } = "";

    /// <summary>Before the city: " in ".</summary>
    public string In { get; set; } = "";

    /// <summary>Before the date: " on ".</summary>
    public string On { get; set; } = "";

    /// <summary>The line's end: ".".</summary>
    public string End { get; set; } = "";
}
