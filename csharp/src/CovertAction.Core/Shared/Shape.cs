namespace CovertAction.Core.Shared;

/// <summary>
/// One record of a figure drawn from a table (the banner behind the "Incoming Msg" screen): a line, a filled or an
/// outlined rectangle, a repeat of the records before it, or the table's end. Six words in the original.
/// </summary>
/// <remarks>
/// A repeat record uses the fields otherwise: <see cref="X1"/> and <see cref="Y1"/> are the step added to the
/// repeated records on each pass, <see cref="X2"/> the number of passes, <see cref="Y2"/> how many records before it
/// are repeated, and <see cref="Color"/> marks that a repeat is running (the drawing changes it, and leaves it 0).
/// </remarks>
// orig: struct R, 12 bytes (src/c/bug/bug_00e8_5727.c); GAME DS:1e22 and BUG DS:2b62 incoming_banner_shapes
public sealed class Shape
{
    /// <summary>A record's kinds.</summary>
    public const short Line = 0, FilledRectangle = 1, Repeat = 2, Rectangle = 3, End = -1;

    /// <summary>What the record draws: <see cref="Line"/>, <see cref="FilledRectangle"/>, <see cref="Repeat"/>, <see cref="Rectangle"/> or <see cref="End"/>.</summary>
    public short Kind { get; set; }

    /// <summary>The first corner's x (a repeat: the step in x).</summary>
    public short X1 { get; set; }

    /// <summary>The first corner's y (a repeat: the step in y).</summary>
    public short Y1 { get; set; }

    /// <summary>The second corner's x (a repeat: the number of passes).</summary>
    public short X2 { get; set; }

    /// <summary>The second corner's y (a repeat: how many records before it are repeated).</summary>
    public short Y2 { get; set; }

    /// <summary>The palette index (a repeat: 1 while it runs, else 0).</summary>
    public short Color { get; set; }
}
