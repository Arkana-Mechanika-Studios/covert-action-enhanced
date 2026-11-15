namespace CovertAction.Core.Electronics;

/// <summary>
/// The texts with which the electronics part tells the player what a wiretap revealed (the intercepted message's
/// heading, the joining words of the sentence). Loaded from the player's BUG.EXE like every text of the part.
/// </summary>
// orig: BUG DS:19ba..1a3f, the string literals of gather_intelligence (src/c/bug/bug_00e8_1882.c)
public sealed class IntelligenceTexts
{
    /// <summary>BUG DS:19ba: "       CODED MESSAGE:\n", the heading of an intercepted coded message.</summary>
    public string CodedMessage { get; set; } = "";

    /// <summary>BUG DS:19d1: "       MEETING NOTES:\n", the heading of the notes of a meeting.</summary>
    public string MeetingNotes { get; set; } = "";

    /// <summary>BUG DS:19e8: "       NEW INFORMATION\n", when nothing of the message was known.</summary>
    public string NewInformation { get; set; } = "";

    /// <summary>BUG DS:1a00: "    ADDITIONAL INFORMATION\n", when part of it was.</summary>
    public string AdditionalInformation { get; set; } = "";

    /// <summary>BUG DS:1a1c: " ", between the person's name and the message's direction.</summary>
    public string AfterName { get; set; } = "";

    /// <summary>BUG DS:1a1e: "\n... ", before what was learned.</summary>
    public string Learned { get; set; } = "";

    /// <summary>BUG DS:1a24: "on ", before the date.</summary>
    public string On { get; set; } = "";

    /// <summary>BUG DS:1a28: " ", between the month and the day.</summary>
    public string BeforeDay { get; set; } = "";

    /// <summary>BUG DS:1a2a: "of the ", before the other party's organisation.</summary>
    public string OfThe { get; set; } = "";

    /// <summary>BUG DS:1a32: "in ", before the other party's city.</summary>
    public string In { get; set; } = "";

    /// <summary>BUG DS:1a36: "\n", the end of the message.</summary>
    public string LineEnd { get; set; } = "";

    /// <summary>BUG DS:1a38: "", the empty text the single clue starts from.</summary>
    public string Empty { get; set; } = "";

    /// <summary>BUG DS:1a39: " ", the text of a clue that is only a face (the popup shows the face).</summary>
    public string FaceOnly { get; set; } = "";

    /// <summary>BUG DS:1a3b: " in ", between a building and its city.</summary>
    public string BuildingIn { get; set; } = "";
}
