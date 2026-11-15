using CovertAction.Core;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The library's routines that bring a text to the screen: reading an entry of one of the game's text files (TEXT.DTA,
/// CLUES.TXT, PLOT.TXT, PROSE.DTA) into the part's text buffer with its placeholders filled in, measuring a text in the
/// current font, and drawing it centred or broken into lines. The menus, the campaign, the raid and the electronics
/// part each carry a copy; the comment on a method lists the copies it stands for (docs/source-map/shared-functions.csv,
/// under the common name of twin-names.csv). The text buffer is the part's <see cref="ILibraryState.MessageText"/>
/// (MicroProse's <c>Txt</c>); all drawing goes to the screen port <see cref="ILibraryState.Rp1"/>.
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="text">The part's variables and texts of the text routines.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="output">The part's drawing routines.</param>
/// <param name="random">The part's random numbers.</param>
/// <param name="behaviour">Which of the original's bugs to reproduce.</param>
public sealed class LibraryText(ILibraryState state, ILibraryTextState text, Boundaries world, LibraryOutput output, IRandomSource random, OriginalBehaviour behaviour)
{
    /// <summary>A text file's tag lines start with this character ("*MSG0001").</summary>
    private const char TagMark = '*';

    /// <summary>The height of a line of text, in pixels.</summary>
    private const int LineHeight = 8;

    /// <summary>A character's nominal width, in pixels: a box's width in characters times this is its width in pixels.</summary>
    private const int CharacterWidth = 8;

    /// <summary>Wrapped text stops at this line of the screen.</summary>
    private const int BottomLine = 0xc0;

    /// <summary>Where the two digits of a random variant stand in its tag ("*SLOC07").</summary>
    private const int VariantTens = 5, VariantUnits = 6;

    /// <summary>
    /// Appends the entry filed under a tag in a text file to the text buffer, line by line, each line with its
    /// placeholders filled in and a blank behind it. A file without the tag gives the notice "This information
    /// requires security clearance: " and the tag instead.
    /// </summary>
    /// <remarks>
    /// The tag must be a whole line of the file; the first one wins, and tag lines right under it are skipped. The
    /// entry ends at the next line that starts with '*'.
    /// </remarks>
    /// <param name="fileName">The file's DOS name ("text.dta").</param>
    /// <param name="tag">The tag, with its '*'.</param>
    /// <param name="tokens">The placeholders to fill in.</param>
    /// <param name="replacements">The value of each placeholder.</param>
    /// <param name="count">How many placeholders there are.</param>
    /// <returns>Whether the file has the tag.</returns>
    /// <exception cref="MemoryOverrunException">The text does not fit the text buffer, or a line its buffer: the original writes past them.</exception>
    // orig: BUG 00e8:3b43 read_cleared_text (src/c/bug/bug_00e8_3b43.c) matched; also FINAL 0100:750d, GAME 00f7:7b99, TAC 00e8:a3c7; tests: LibraryTextOracleTests
    public bool ReadClearedText(string fileName, string tag, IReadOnlyList<string> tokens, IReadOnlyList<string> replacements, int count)
    {
        var file = TextStream.Open(world.Files, fileName);
        if (file is null)
        {
            // DIVERGENCE: D-16 a file that cannot be opened reads as a file without the tag; the original reads on
            // through a null stream
            AppendClearanceNotice(tag);
            return false;
        }
        var line = "";
        int scanned;
        do
        {
            scanned = file.ScanLine(ref line);
        }
        while (line != tag && scanned != TextStream.EndOfFile);
        bool found;
        if (scanned != TextStream.EndOfFile)
        {
            CopyEntry(file, line, tokens, replacements, count);
            found = true;
        }
        else
        {
            AppendClearanceNotice(tag);
            found = false;
        }
        file.Close();
        return found;
    }

    /// <summary>Appends the notice that stands for a missing entry, and the entry's tag.</summary>
    private void AppendClearanceNotice(string tag)
    {
        state.MessageText.Text += text.ClearanceNotice;
        state.MessageText.Text += tag;
    }

    /// <summary>Copies the lines of an entry, from the line after its tag up to the next tag, into the text buffer.</summary>
    private void CopyEntry(TextStream file, string line, IReadOnlyList<string> tokens, IReadOnlyList<string> replacements, int count)
    {
        // BUG: CA-17 the original does not look for the file's end here: an entry at the very end of a file is read
        // again and again (OriginalBehaviour.FixTextReaderPastEndOfFile)
        do
        {
            if (file.ScanLine(ref line) == TextStream.EndOfFile && behaviour.FixTextReaderPastEndOfFile)
            {
                return;
            }
        }
        while (line[0] == TagMark);
        var current = new TextBuffer(ILibraryTextState.LineCapacity) { Text = line };
        do
        {
            LibraryTexts.ReplaceTokens(current, tokens, replacements, count, text.TokenTail);
            state.MessageText.Text += current.Text;
            state.MessageText.Text += text.LineSeparator;
            line = current.Text;
            if (file.ScanLine(ref line) == TextStream.EndOfFile && behaviour.FixTextReaderPastEndOfFile)
            {
                return;
            }
            current.Text = line;
        }
        while (line[0] != TagMark);
    }

    /// <summary>
    /// Appends the entry filed under a tag in a text file to the text buffer, with the message texts' seven
    /// placeholders ($VICTIM ... $OBJECT) filled in from the part's current values.
    /// </summary>
    /// <param name="fileName">The file's DOS name.</param>
    /// <param name="tag">The tag, with its '*'.</param>
    /// <returns>Whether the file has the tag (the menus part reads the pages of a debriefing until one is missing).</returns>
    // orig: BUG 00e8:3b26 read_text_with_vars (src/c/bug/bug_00e8_3b26.c) matched; also GAME 00f7:7b7c, TAC 00e8:a3aa, FINAL 0100:74f0; tests: LibraryTextOracleTests
    // C: the original ends without a return statement; its callers read what the reader left in AX, the reader's answer
    public bool ReadTextWithVars(string fileName, string tag) =>
        ReadClearedText(fileName, tag, text.VariableNames, [.. text.VariableValues], ILibraryTextState.MessageVariables);

    /// <summary>
    /// Appends one of several variants of an entry to the text buffer: a random number below
    /// <paramref name="variants"/> is written as two digits over the end of the tag ("*SLOC00" becomes "*SLOC07"),
    /// and the entry under that tag is read with the message placeholders filled in.
    /// </summary>
    /// <param name="fileName">The file's DOS name.</param>
    /// <param name="tag">The tag's buffer, a variable of the part; its digits stay changed.</param>
    /// <param name="variants">How many variants the entry has, 1..100.</param>
    // orig: BUG 00e8:3f93 read_random_variant (src/c/bug/bug_00e8_3f93.c) matched; also GAME 00f7:7fe9, TAC 00e8:a817, FINAL 0100:795d; tests: LibraryTextOracleTests
    public void ReadRandomVariant(string fileName, TextBuffer tag, int variants)
    {
        var variant = GameLibrary.RandomBelow(random, variants);
        tag[VariantUnits] = (char)C16.UChar(variant % 10 + '0');
        tag[VariantTens] = (char)C16.UChar(variant / 10 + '0');
        ReadTextWithVars(fileName, tag.Text);
    }

    /// <summary>The width of a text in the screen port's current font: the sum of its characters' widths.</summary>
    /// <param name="line">The text.</param>
    /// <returns>The width in pixels.</returns>
    // orig: BUG 00e8:5171 text_width (src/c/bug/bug_00e8_5171.c) matched; also FINAL 0100:8b3b, GAME 00f7:0ec7, TAC 00e8:b9f5; tests: LibraryTextOracleTests
    public int TextWidth(string line)
    {
        var font = state.Rp1.FontNr;
        var width = 0;
        foreach (var character in line)
        {
            // C: the sum is a 16-bit int, which wraps with widths no font has (a fuzzed font table)
            width = C16.Int(width + world.Graphics.CharWidth(font, character));
        }
        return width;
    }

    /// <summary>Draws a text centred on a column, letters only (the cells around them are left as they are).</summary>
    /// <param name="line">The text.</param>
    /// <param name="x">The column the text is centred on.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="color">Palette index of the letters.</param>
    // orig: BUG 00e8:5227 draw_text_centered (src/c/bug/bug_00e8_5227.c) matched; also FINAL 0100:8bf1, GAME 00f7:0f7d, TAC 00e8:baab; tests: LibraryTextOracleTests
    public void DrawTextCentered(string line, int x, int y, int color) =>
        output.DrawTextFlag0(line, x - TextWidth(line) / 2, y, color);

    /// <summary>
    /// Draws the text buffer broken into lines of at most <paramref name="columns"/> times 8 pixels, letters only, one
    /// line every 8 pixels from <paramref name="y"/> down to line 192 of the screen. A line breaks at its last blank or
    /// at a line end; a word too wide for a line is drawn with the rest of the text and drawn again on every following
    /// line, as in the original.
    /// </summary>
    /// <param name="columns">The width of a line in characters of 8 pixels.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge of the first line.</param>
    /// <param name="color">Palette index of the letters.</param>
    /// <returns>The top edge of the line after the last one drawn.</returns>
    // orig: BUG 00e8:471a draw_wrapped_text (src/c/bug/bug_00e8_471a.c) matched; also FINAL 0100:80e4, GAME 00f7:0470, TAC 00e8:af9e; tests: LibraryTextOracleTests
    public int DrawWrappedText(int columns, int x, int y, int color)
    {
        var txt = state.MessageText;
        int lineStart = 0, width = 0, lastBreak = 0;
        for (var i = 0; txt.Length > i; i++)
        {
            // C: the line's width and the box's width in pixels are 16-bit ints
            width = C16.Int(width + world.Graphics.CharWidth(state.Rp1.FontNr, txt[i]));
            if (txt[i] is ' ' or '\n')
            {
                lastBreak = i;
            }
            if (txt[i] == '\n' || C16.Int(columns * CharacterWidth) < width)
            {
                // the line ends at the last break: an end mark there for the drawing, the character put back after it
                var kept = txt[lastBreak];
                txt[lastBreak] = '\0';
                output.DrawTextFlag0(TextFrom(txt, lineStart), x, y, color);
                txt[lastBreak] = kept;
                y += LineHeight;
                width = 0;
                lineStart = lastBreak + 1;
                i = lastBreak;
                if (y >= BottomLine)
                {
                    break;
                }
            }
        }
        if (width > 0 && y < BottomLine)
        {
            output.DrawTextFlag0(TextFrom(txt, lineStart), x, y, color);
            y += LineHeight;
        }
        return y;
    }

    /// <summary>The characters of a buffer from a position up to the next end mark, as the original's pointer into the buffer reads them.</summary>
    private static string TextFrom(TextBuffer buffer, int start)
    {
        var end = Array.IndexOf(buffer.Bytes, (byte)0, start);
        if (end < 0)
        {
            throw new MemoryOverrunException($"no end mark behind position {start} of a text of {buffer.Bytes.Length} bytes");
        }
        return System.Text.Encoding.Latin1.GetString(buffer.Bytes, start, end - start);
    }

    /// <summary>Shortens the text buffer one character at a time from its end until it is at most a width wide in the current font.</summary>
    /// <param name="maxWidth">The width in pixels.</param>
    /// <exception cref="MemoryOverrunException">The text is empty and still too wide (a negative width): the original writes in front of the buffer.</exception>
    // orig: GAME 00f7:780c trim_text_to_width (src/c/game/game_00f7_780c.c) matched; also FINAL 0100:3a22; tests: LibraryModuleOracleTests
    public void TrimTextToWidth(int maxWidth)
    {
        var txt = state.MessageText;
        while (TextWidth(txt.Text) > maxWidth)
        {
            if (txt.Length == 0)
            {
                throw new MemoryOverrunException("an empty text is cut in front of its buffer");
            }
            txt.Truncate(txt.Length - 1);
        }
    }
}
