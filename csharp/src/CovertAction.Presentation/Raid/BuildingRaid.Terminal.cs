using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Raid;

/// <summary>The computer terminals: the password's letters, and the line the player types the password into.</summary>
public sealed partial class BuildingRaid
{
    private const int KeyEnter = 0xd;
    private const int KeyEscape = 0x1b;
    private const int KeyBackspace = 8;
    private const int KeyLeft = 0x4b00;
    private const int KeyRight = 0x4d00;
    private const int KeyInsert = 0x5200;
    private const int KeyDelete = 0x5300;

    /// <summary>The font of the input line.</summary>
    private const int InputFont = 1;

    /// <summary>A cell of the input line: 8 x 8 pixels.</summary>
    private const int InputCell = 8;

    /// <summary>The password's letters: 5 to 9, and the longest word the text file is asked for.</summary>
    private const int ShortestPassword = 5, LongestPassword = 9, LongestWord = 32;

    /// <summary>The places of the text file a password is looked for from: the first 20000 bytes.</summary>
    private const int PasswordPlaces = 20000;

    /// <summary>
    /// Picks the building's computer password: a word of the game's own texts. The text file is opened at a random
    /// place, the first word there is dropped (it may be cut), and the next word of 5 to 9 letters that is all
    /// small letters is taken. The mask the player sees starts as one dot per letter.
    /// </summary>
    // orig: TAC 00e8:38bb read_text_line (src/c/tac/tac_00e8_38bb.c) matched; tests: TacOracleTests, RaidPasswordTests
    public void ReadTextLine()
    {
        // C: the original does not look at what fopen answers; without the file it reads through a null stream
        var file = TextStream.Open(world.Files, state.TextDtaName)
            ?? throw new Msc.UninitialisedReadException($"the stream of the text file {state.TextDtaName}, which cannot be opened");
        file.Seek(GameLibrary.RandomBelow(state.Random, PasswordPlaces));
        var word = state.PasswordWord;
        file.ScanWord(LongestWord, ref word);
        // C: at the file's end fscanf leaves the last word, which is looked at again: with no fitting word left the
        // original never comes back, and neither does this
        do
        {
            file.ScanWord(LongestWord, ref word);
        }
        while (word.Length < ShortestPassword || word.Length > LongestPassword || !word.All(letter => letter is >= 'a' and <= 'z'));
        state.PasswordWord = word;
        state.PasswordDots.CopyTo(state.PasswordMask);
        state.PasswordMask[state.PasswordDots.Length] = '\0';
        state.PasswordMask[word.Length] = '\0';
        file.Close();
    }

    /// <summary>
    /// A terminal gives away a letter of the password: up to 99 random places of the word are tried for a letter still
    /// shown as a dot, which is then shown; the password box of the status panel is drawn again.
    /// </summary>
    // orig: TAC 00e8:39a1 reveal_password_letter (src/c/tac/tac_00e8_39a1.c) matched; tests: TacOracleTests
    public void RevealPasswordLetter()
    {
        const int Tries = 0x63;
        var tries = 0;
        do
        {
            var place = GameLibrary.RandomBelow(state.Random, state.PasswordWord.Length);
            if (state.PasswordWord[place] != ' ' && state.PasswordMask[place] == '.')
            {
                state.PasswordMask[place] = state.PasswordWord[place];
                tries = Tries;
            }
            tries++;
        }
        while (tries < Tries);
        world.Graphics.FillRect(state.Rp1, 0xf8, 0x58, 0x40, 9, LightGreen);
        Output.DrawRectOutline(0xf8, 0x58, 0x40, 9, Green);
        Output.DrawTextFlag0(MaskText(), 0xfa, 0x59, Magenta);
    }

    /// <summary>
    /// Draws the input line's <paramref name="cells"/> cells at (<paramref name="x"/> + 2, <paramref name="y"/> + 2): each
    /// cleared, then the text's character in it, one character after the other at their widths. The place and width of
    /// the character under the cursor are noted for the cursor's blinking.
    /// </summary>
    /// <param name="x">Screen x of the line.</param>
    /// <param name="y">Screen y of the line.</param>
    /// <param name="cells">The line's length in characters.</param>
    // orig: TAC 00e8:42d6 draw_input_field (src/c/tac/tac_00e8_42d6.c) matched; tests: TacOracleTests
    public void DrawInputField(int x, int y, int cells)
    {
        x += 2;
        y += 2;
        for (var i = 0; i < cells; i++)
        {
            world.Graphics.FillRect(state.Rp1, x, y, InputCell, InputCell, Magenta);
            if (state.MessageText.Length > i)
            {
                // the original cuts the text after this character for a moment and draws it alone
                var character = state.MessageText[i];
                Output.DrawText(character.ToString(), x, y, LightGreen);
                if (i == state.Willy)
                {
                    state.WillyX = (short)x;
                    state.WillyW = (short)world.Graphics.CharWidth(InputFont, character);
                }
                x += world.Graphics.CharWidth(InputFont, character);
            }
        }
    }

    /// <summary>
    /// Lets the player edit a line of <paramref name="length"/> characters in the text buffer at (<paramref name="x"/>,
    /// <paramref name="y"/>), the cursor blinking. Left and right move the cursor, Insert opens a blank, Delete and Backspace
    /// remove a character, a printable key types (the first one clears the old text), Enter accepts and Escape cancels.
    /// Num Lock is off while the line is edited (and back on after Enter).
    /// </summary>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    /// <param name="length">The line's length in characters.</param>
    /// <returns>1 on Enter, with the text's trailing blanks removed; -1 on Escape.</returns>
    // orig: TAC 00e8:4078 input_line (src/c/tac/tac_00e8_4078.c) matched; tests: TacOracleTests
    public int InputLine(int x, int y, int length)
    {
        var text = state.MessageText;
        world.Graphics.FillRect(state.Rp1, x, y + 1, 7 * length, 9, Magenta);
        state.Rp1.BPen = Magenta;
        state.Willy = 0;
        var firstKey = true;
        for (var i = text.Length; i < length; i++)
        {
            text[i] = ' ';
        }
        text.Truncate(length);
        DrawInputField(x, y, length);
        Input.FlushInput();
        world.Input.SetNumLock(false);
        while (true)
        {
            BlinkCursorUntilAKey(y);
            var key = ReadKey();
            if (key == KeyEnter)
            {
                world.Input.SetNumLock(true);
                break;
            }
            EditLine(key, length, firstKey);
            if (key == KeyEscape)
            {
                return -1;
            }
            firstKey = false;
            DrawInputField(x, y, length);
        }
        for (var i = length - 1; i >= 0 && text[i] == ' '; i--)
        {
            text[i] = '\0';
        }
        return 1;
    }

    /// <summary>The cursor's cell blinks while no key is waiting (input_line).</summary>
    private void BlinkCursorUntilAKey(int y)
    {
        const int Blink = 10;
        while (world.Input.NoKeyWaiting())
        {
            world.Graphics.RecolorRect(state.Rp1, state.WillyX, y + 1, state.WillyW, 10, Magenta, Green);
            Output.WaitVsync(Blink);
            world.Graphics.RecolorRect(state.Rp1, state.WillyX, y + 1, state.WillyW, 10, Green, Magenta);
            if (world.Input.NoKeyWaiting())
            {
                Output.WaitVsync(Blink);
            }
        }
    }

    /// <summary>One key's edit of the line (input_line).</summary>
    private void EditLine(int key, int length, bool firstKey)
    {
        var text = state.MessageText;
        if (key == KeyLeft && state.Willy != 0)
        {
            state.Willy--;
        }
        if (key == KeyRight && length - 1 > state.Willy)
        {
            state.Willy++;
        }
        if (key == KeyInsert)
        {
            InsertAt(state.Willy);
            text[state.Willy] = ' ';
            text.Truncate(length);
        }
        if (key == KeyDelete)
        {
            text.RemoveAt(state.Willy);
            text[state.Willy] = ' ';
            text[length - 1] = ' ';
            text.Truncate(length);
        }
        if (key == KeyBackspace && state.Willy != 0)
        {
            text.RemoveAt(state.Willy - 1);
            text[length - 1] = ' ';
            text.Truncate(length);
            state.Willy--;
        }
        if (key < 0x80 && key >= ' ')
        {
            if (firstKey)
            {
                for (var i = 0; i < length; i++)
                {
                    text[i] = ' ';
                }
            }
            text[state.Willy] = (char)key;
            if (length - 1 > state.Willy)
            {
                state.Willy++;
            }
        }
    }

    /// <summary>Moves the text from <paramref name="at"/> on, end mark included, one character to the right (input_line copies it through a buffer of its own).</summary>
    private void InsertAt(int at)
    {
        var bytes = state.MessageText.Bytes;
        var end = state.MessageText.Length;
        Array.Copy(bytes, at, bytes, at + 1, end - at + 1);
    }
}
