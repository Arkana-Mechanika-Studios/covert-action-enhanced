using CovertAction.Platform;

namespace CovertAction.Presentation.Intro;

/// <summary>
/// The printing routines of the original's set-up program on the PC's text page: the cursor, a text with colour
/// changes put at a row and column through the video BIOS, and a text printed through DOS from the cursor on. Only
/// what the set-up uses is here: characters, the carriage return and line feed of DOS's blank rows, and the line
/// wrap at the end of a row.
/// </summary>
/// <remarks>
/// A character printed through the BIOS's teletype or DOS keeps the colour the place on the page already has; a
/// text put by <see cref="PrintAt"/> sets the colour of each of its places first. The page's colour for the next
/// <see cref="PrintAt"/> is kept between calls, in the program's code segment in the original.
/// </remarks>
/// <param name="page">The text page printed on (the PC's 80 x 25 screen of text mode 3).</param>
public sealed class TextConsole(TextScreen page)
{
    /// <summary>The byte of a text that starts a colour change; the byte after it is the new colour (0 ends the text).</summary>
    private const char ColourChange = '\xff';

    private const char CarriageReturn = '\r', LineFeed = '\n';

    /// <summary>The character that ends a text printed through DOS's string function.</summary>
    private const char DosStringEnd = '$';

    /// <summary>The colour the code segment holds before the first colour change: white on black.</summary>
    private const byte InitialColour = 0x0f;

    private byte _colour = InitialColour;

    /// <summary>Places the cursor.</summary>
    // orig: INTRO 0000:044a set_cursor (src/c/intro/intro_0000_044a.c) matched; tests: HardwareSetupTests
    public void SetCursor(int row, int column)
    {
        page.CursorRow = row;
        page.CursorColumn = column;
    }

    /// <summary>
    /// Puts a text on the page from a row and column on, in the present colour; a colour change in the text sets the
    /// colour of the characters after it (and of the texts put later).
    /// </summary>
    // orig: INTRO 0000:0767 print_at asm; tests: HardwareSetupTests
    public void PrintAt(int row, int column, string text)
    {
        SetCursor(row, column);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ColourChange)
            {
                if (++i == text.Length || text[i] == '\0')
                {
                    return;
                }
                _colour = (byte)text[i];
            }
            else
            {
                // the BIOS first writes a blank with the colour at the cursor, then the teletype writes the character over it
                page.Put(page.CursorRow, page.CursorColumn, (byte)' ', _colour);
                Teletype(text[i]);
            }
        }
    }

    /// <summary>Prints a text from the cursor on, up to its "$", through DOS: the places keep their colour.</summary>
    // orig: INTRO 0000:07b2 print_string asm; tests: HardwareSetupTests
    public void PrintString(string text)
    {
        foreach (var character in text.TakeWhile(c => c != DosStringEnd))
        {
            Teletype(character);
        }
    }

    /// <summary>
    /// The BIOS's teletype output: the character goes to the cursor's place, keeping that place's colour, and the
    /// cursor moves right, to the start of the next row after the last column. A carriage return moves it to the
    /// start of its row, a line feed to the next row.
    /// </summary>
    // The BIOS scrolls the page when the cursor leaves the last row. The set-up never gets there (its blank rows are 79
    // characters and a CR LF, one row each, and it prints nothing on the last row), so a print that would is refused.
    private void Teletype(char character)
    {
        switch (character)
        {
            case CarriageReturn:
                page.CursorColumn = 0;
                return;
            case LineFeed:
                page.CursorRow++;
                break;
            default:
                page.Characters[page.CursorRow * TextScreen.Columns + page.CursorColumn] = (byte)character;
                if (++page.CursorColumn == TextScreen.Columns)
                {
                    page.CursorColumn = 0;
                    page.CursorRow++;
                }
                break;
        }
        if (page.CursorRow == TextScreen.Rows)
        {
            throw new InvalidOperationException("The set-up screen printed below the last row of the text page.");
        }
    }
}
