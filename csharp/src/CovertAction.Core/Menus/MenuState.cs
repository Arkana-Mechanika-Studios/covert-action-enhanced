namespace CovertAction.Core.Menus;

/// <summary>
/// The working variables of the game's message box and menu: every part shows its texts ("Are you sure you want to
/// Quit?", "PAUSED", a list of choices) through the same box routine, which measures the text, draws the box and
/// lets the player move a highlight bar over the lines that are choices. A line of the text that starts with a
/// blank is a choice; the other lines are title lines.
/// </summary>
/// <remarks>
/// Names are those of the reconstructed code.h; <c>NoHi</c> is MicroProse's own ("no highlight"). Colours are palette
/// indices, sizes are pixels. Every part has these variables at its own addresses; the coordinates given are CODE's.
/// </remarks>
// orig: CODE DS:1b16..DS:1de5, the menu_* variables (src/c/include/code.h); BUG, FINAL, GAME, TAC and CHASE have their own copies
public sealed class MenuState
{
    /// <summary>The most lines a box text can have.</summary>
    public const int MaxLines = 32;

    /// <summary>CODE DS:1b16: for each line of the box text, the position of its first character in the text.</summary>
    public short[] MenuLineStart { get; } = new short[MaxLines];

    /// <summary>CODE DS:1b56: colour of the title lines.</summary>
    public short MenuTextColor { get; set; }

    /// <summary>CODE DS:1b58: colour of the choices that are not selected.</summary>
    public short MenuItemColor { get; set; }

    /// <summary>CODE DS:1b5a: number of text lines in the box.</summary>
    public short MenuLineCount { get; set; }

    /// <summary>CODE DS:1b5c: colour of the selected choice.</summary>
    public short MenuSelectedColor { get; set; }

    /// <summary>CODE DS:1d7c: whether the box gets an "OK" button in its corner; such a box has no selection.</summary>
    public bool MenuOkButton { get; set; }

    /// <summary>CODE DS:1d92: the number of the first line that is a choice, -1 while none has been found.</summary>
    public short MenuFirstItem { get; set; }

    /// <summary>CODE DS:1d96: "no highlight": the box is a pure message (the insert-disk text); nothing is selected and any closing key continues.</summary>
    public bool NoHi { get; set; }

    /// <summary>CODE DS:1d98: number of choices.</summary>
    public short MenuItemCount { get; set; }

    /// <summary>CODE DS:1d9a: true while the whole box has to be drawn; false for later updates, which only redraw the lines next to the selection.</summary>
    public bool MenuFirstDraw { get; set; }

    /// <summary>CODE DS:1d9c: colour of the box under the text; the highlight bar is made by recolouring it.</summary>
    public short MenuHiliteColor { get; set; }

    /// <summary>CODE DS:1d9e: width of the widest line.</summary>
    public short MenuTextWidth { get; set; }

    /// <summary>CODE DS:1dae: one bit per choice: marked choices are drawn with a '^' in front.</summary>
    public short MenuMarkedMask { get; set; }

    /// <summary>CODE DS:1db2: set for the pause message: the box is drawn and the routine returns at once.</summary>
    public bool MenuPauseMode { get; set; }

    /// <summary>CODE DS:1dc0: for each choice, the character after its leading blank (its hot key).</summary>
    public char[] MenuHotkeys { get; } = new char[MaxLines];

    /// <summary>CODE DS:1de0: the choice selected when the next box opens; back to 0 after each box.</summary>
    public short MenuStartItem { get; set; }

    /// <summary>CODE DS:1de2: right edge of the box.</summary>
    public short MenuRight { get; set; }

    /// <summary>CODE DS:1de4: bottom edge of the box.</summary>
    public short MenuBottom { get; set; }

    /// <summary>CODE DS:1128: the text shown while an animation loads from a floppy disk.</summary>
    public string StrOneMoment { get; set; } = "";

    /// <summary>CODE DS:113d: the quit question the box itself asks on Alt-Q: two title lines and the choices No and Yes.</summary>
    public string StrQuitConfirm { get; set; } = "";

    /// <summary>CODE DS:1197: the label of the OK button.</summary>
    public string StrOk { get; set; } = "";
}
