namespace CovertAction.Core.Shared;

/// <summary>
/// A part's variables and texts for the library's text routines (reading an entry of a text file, filling in its
/// placeholders, breaking it into lines). Each program that has these routines carries its own copy among its data;
/// a part's state implements this interface, and fills the texts from its program (they are never spelled in the C#).
/// </summary>
public interface ILibraryTextState
{
    /// <summary>The number of placeholders of the game's message texts ($VICTIM, $SNDORG, ... $OBJECT).</summary>
    const int MessageVariables = 7;

    /// <summary>The size of a line of a text file in the readers' buffer, and of the placeholder routine's scratch buffer, end mark included.</summary>
    const int LineCapacity = 256;

    /// <summary>The scratch buffer in which the placeholder routine keeps the rest of a line (<c>token_tail_buf</c>); what it held last stays.</summary>
    TextBuffer TokenTail { get; }

    /// <summary>The names of the message texts' placeholders, "$VICTIM" ... "$OBJECT" (seven).</summary>
    IReadOnlyList<string> VariableNames { get; }

    /// <summary>The current value of each placeholder (seven): the part's buffers, which the event text reader also fills.</summary>
    IList<string> VariableValues { get; }

    /// <summary>What a reader writes when a file has no entry under the tag: "This information requires security clearance: ".</summary>
    string ClearanceNotice { get; }

    /// <summary>What a reader puts behind every line it copies: a blank.</summary>
    string LineSeparator { get; }
}
