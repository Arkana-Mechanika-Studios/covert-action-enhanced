using CovertAction.Msc;

namespace CovertAction.Core.Shared;

/// <summary>
/// The library's routines that prepare a text in a part's text buffer before it is shown: filling in placeholders
/// ("$NAME"), making the tag of a message text ("*MSG0412"), and breaking a text into the lines of a box. The menus,
/// the campaign, the raid and the electronics part each carry a copy; the comment on a method lists the copies it
/// stands for (docs/source-map/shared-functions.csv, under the common name of twin-names.csv).
/// </summary>
public static class LibraryTexts
{
    /// <summary>A forced line break in the game's texts.</summary>
    private const char ForcedBreak = '^';

    /// <summary>The case number of the messages every case shares (the tags "*MSG99nn").</summary>
    private const int GenericCase = 99;

    /// <summary>Where the four digits of a message tag stand: "*MSG" then case and message, two digits each.</summary>
    private const int CaseTens = 4, CaseUnits = 5, MessageTens = 6, MessageUnits = 7;

    /// <summary>
    /// Fills placeholders into a text: for each of the first <paramref name="count"/> placeholders, its first
    /// occurrence in the text (if any) is replaced by its value. Later occurrences stay; a value may itself contain a
    /// later placeholder, which is then filled in too.
    /// </summary>
    /// <remarks>
    /// The rest of the text behind the placeholder is kept in <paramref name="tail"/> meanwhile, and stays there
    /// afterwards: the original's scratch buffer (<c>token_tail_buf</c>, <c>macro_scratch</c>) is one of the part's
    /// variables.
    /// </remarks>
    /// <param name="text">The text, changed in place (a line read from a text file, or the part's text buffer).</param>
    /// <param name="tokens">The placeholders ("$KEY", "$NAME" ...).</param>
    /// <param name="replacements">The value of each placeholder.</param>
    /// <param name="count">How many of them to fill in.</param>
    /// <param name="tail">The part's scratch buffer for the rest of the text (256 bytes).</param>
    /// <exception cref="MemoryOverrunException">The rest of the text, or the text with its value, does not fit its buffer: the original writes past it.</exception>
    // orig: BUG 00e8:3c2f replace_tokens (src/c/bug/bug_00e8_3c2f.c) matched; also TAC 00e8:a4b3, FINAL 0100:75f9, GAME 00f7:7c85; tests: LibraryTextOracleTests, GameOracleTests
    public static void ReplaceTokens(TextBuffer text, IReadOnlyList<string> tokens, IReadOnlyList<string> replacements, int count, TextBuffer tail)
    {
        for (var i = 0; i < count; i++)
        {
            var found = text.Text.IndexOf(tokens[i], StringComparison.Ordinal);
            if (found < 0)
            {
                continue;
            }
            tail.Text = text.Text[found..];
            text.Truncate(found);
            text.Text += replacements[i];
            text.Text += tail.Text[tokens[i].Length..];
        }
    }

    /// <summary>
    /// Makes the tag of a message text in TEXT.DTA: the template "*MSG0000" with the case number and the message's
    /// number written over its four zeros. A negative message number stands for a message every case has: case 99,
    /// message half the number.
    /// </summary>
    /// <remarks>
    /// The digits are added to the template's characters, so a number outside 0..99 gives characters other than
    /// digits, as in the original.
    /// </remarks>
    /// <param name="tag">The part's tag buffer, overwritten.</param>
    /// <param name="template">The template, "*MSG0000" in the game.</param>
    /// <param name="caseNumber">The case (the crime file's number).</param>
    /// <param name="message">The message's number within the case; negative for a shared message.</param>
    // orig: BUG 00e8:406d load_message_text (src/c/bug/bug_00e8_406d.c) matched; also FINAL 0100:7a37, GAME 00f7:80df, TAC 00e8:a8f1; tests: LibraryTextOracleTests
    public static void LoadMessageText(TextBuffer tag, string template, int caseNumber, int message)
    {
        tag.Text = template;
        if (message < 0)
        {
            caseNumber = GenericCase;
            // C: the negation of -32768 stays -32768
            message = C16.Div(C16.Neg((short)message), 2);
        }
        AddDigit(tag, CaseTens, caseNumber / 10);
        AddDigit(tag, CaseUnits, caseNumber % 10);
        AddDigit(tag, MessageTens, message / 10);
        AddDigit(tag, MessageUnits, message % 10);
    }

    /// <summary>Adds a number to the character at a position of a text, keeping the low byte as the original's char does.</summary>
    private static void AddDigit(TextBuffer text, int position, int digit) => text[position] = (char)C16.UChar(text[position] + digit);

    /// <summary>
    /// Breaks the text buffer into lines of about <paramref name="width"/> characters for a box: a blank after more
    /// than that many characters of a line becomes a line break (unless another blank follows it), and a '^' becomes a
    /// blank with a line break behind it. <paramref name="lineEnd"/> is added at the end when the last line is not empty.
    /// </summary>
    /// <remarks>
    /// The break after a '^' is written over the character that follows it, which is lost. A '^' at the very end
    /// writes its break over the text's end mark, and the text then runs on into whatever the buffer held behind it.
    /// </remarks>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="width">The width of a line, in characters.</param>
    /// <param name="lineEnd">The line break text appended at the end (the part's variable, a newline in the game).</param>
    // orig: BUG 00e8:4696 wrap_text_buffer (src/c/bug/bug_00e8_4696.c) matched; also GAME 00f7:03ec, FINAL 0100:8060, TAC 00e8:af1a; tests: LibraryTextOracleTests, FinalOracleTests
    public static void WrapTextBuffer(TextBuffer text, int width, string lineEnd)
    {
        var column = 0;
        // C: the length is taken again before every character, so a '^' at the end lengthens the text
        for (var i = 0; text.Length > i; i++)
        {
            if (text[i] == ForcedBreak)
            {
                text[i] = ' ';
                text[i + 1] = '\n';
                column = -1;
            }
            if (text[i] == '\n')
            {
                column = -1;
            }
            column++;
            if (column > width && text[i] == ' ' && text[i + 1] != ' ')
            {
                text[i] = '\n';
                column = 0;
            }
        }
        if (column > 0)
        {
            text.Text += lineEnd;
        }
    }

    /// <summary>The size of the buffer the kept words are gathered in, end mark included.</summary>
    private const int KeptWordsCapacity = 512;

    /// <summary>
    /// Cuts the text buffer down to the words the player knows of a message: the separators (blanks and line ends)
    /// are counted from 0, and the word behind separator n is kept when bit n modulo <paramref name="period"/> of
    /// <paramref name="mask"/> is set. Runs of kept words stay; where a dropped stretch follows a run,
    /// <paramref name="ellipsis"/> (" ... ") stands for it.
    /// </summary>
    /// <remarks>The text before the first separator is never kept. The separator that ends a run is put back as a blank, also where it was a line end.</remarks>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="mask">One bit per word that is kept.</param>
    /// <param name="period">How many bits of the mask are used, round and round.</param>
    /// <param name="ellipsis">What stands for dropped words.</param>
    /// <exception cref="DivideErrorException">A period of 0.</exception>
    /// <exception cref="MemoryOverrunException">The kept words do not fit the original's buffer of 512 bytes.</exception>
    // orig: BUG 00e8:4805 keep_marked_words (src/c/bug/bug_00e8_4805.c) matched; also FINAL 0100:81cf, GAME 00f7:055b, TAC 00e8:b089; tests: LibraryTextOracleTests
    public static void KeepMarkedWords(TextBuffer text, int mask, int period, string ellipsis)
    {
        var kept = new System.Text.StringBuilder();
        int runStart = 0, separator = 0, words = 0;
        var inRun = false;
        var found = NextSeparator(text, ref separator, advance: false);
        while (found)
        {
            // C: the bit's number is narrowed to a char before the shift, which the 8086 takes as it is
            var bit = C16.Shl((short)1, (sbyte)C16.Rem((short)words, (short)period));
            if ((mask & bit) == 0)
            {
                if (inRun)
                {
                    kept.Append(text.Text.AsSpan(runStart, separator - runStart));
                    text[separator] = ' ';
                    kept.Append(ellipsis);
                }
                inRun = false;
            }
            else
            {
                if (!inRun)
                {
                    runStart = separator + 1;
                }
                inRun = true;
            }
            words++;
            found = NextSeparator(text, ref separator, advance: true);
        }
        if (inRun)
        {
            kept.Append(text.Text.AsSpan(runStart));
        }
        if (kept.Length >= KeptWordsCapacity)
        {
            throw new MemoryOverrunException($"{kept.Length} characters of kept words into a buffer of {KeptWordsCapacity} bytes");
        }
        text.Text = kept.ToString();
    }

    /// <summary>Moves to the next blank or line end of a text (from the position itself, or from the one after it); false at the text's end.</summary>
    private static bool NextSeparator(TextBuffer text, ref int position, bool advance)
    {
        if (advance)
        {
            position++;
        }
        for (; position < text.Length; position++)
        {
            if (text[position] is ' ' or '\n')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Seconds in a minute, minutes in an hour and in a day of the campaign's clock.</summary>
    private const short SecondsPerMinute = 60, MinutesPerHour = 60;

    /// <summary>Minutes in a day of the campaign's clock, for an unsigned remainder.</summary>
    private const ushort MinutesInADay = 1440;

    /// <summary>
    /// Writes a time of day as "hh:mm:ss" into a part's clock text: the campaign's clock plus a number of seconds,
    /// on a 24-hour clock. Only the six digits are written; the colons are the template's.
    /// </summary>
    /// <param name="clockText">The part's clock text (eight characters, "00:00:00" in the game).</param>
    /// <param name="gtime">The campaign's clock, in minutes.</param>
    /// <param name="seconds">The seconds to add.</param>
    /// <returns>The clock text.</returns>
    // orig: GAME 00f7:0e3b format_clock_time (src/c/game/game_00f7_0e3b.c) matched; also FINAL 0100:8aaf, TAC 00e8:b969; tests: LibraryModuleOracleTests, TacOracleTests, RaidRulesTests
    // BUG's format_game_time is the same code, ported for now as IntelligenceRules.FormatGameTime: to be replaced by this method.
    public static string FormatClockTime(char[] clockText, int gtime, int seconds)
    {
        // C: 16-bit division; the remainder of the day is unsigned (a clock before minute 0 wraps to 65536)
        var time = C16.Int(seconds);
        var minuteOfDay = (short)C16.URem(C16.Unsigned(C16.Div(time, SecondsPerMinute) + gtime), MinutesInADay);
        PutTwoDigits(clockText, 0, C16.Div(minuteOfDay, MinutesPerHour));
        PutTwoDigits(clockText, 3, C16.Rem(minuteOfDay, MinutesPerHour));
        PutTwoDigits(clockText, 6, C16.Rem(time, SecondsPerMinute));
        return new string(clockText);
    }

    /// <summary>Writes a number's tens and units as two characters ('0' plus the digit, a byte as in the original).</summary>
    private static void PutTwoDigits(char[] text, int position, int value)
    {
        text[position] = (char)C16.UChar(C16.Div((short)value, 10) + '0');
        text[position + 1] = (char)C16.UChar(C16.Rem((short)value, 10) + '0');
    }
}
