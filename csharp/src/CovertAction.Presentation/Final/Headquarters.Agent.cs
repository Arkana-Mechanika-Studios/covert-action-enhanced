using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Final;

/// <summary>The creation of the agent: the training of the skills, the level of play, the field in which the code name is typed.</summary>
public sealed partial class Headquarters
{
    /// <summary>The page the menus' pictures are loaded onto before they are copied to the screen.</summary>
    private const int PicturePage = 1;

    /// <summary>The highest rating of a skill (Awesome), and the training points the agent spends on the skills.</summary>
    private const int BestRating = 3, TrainingPoints = 4;

    /// <summary>
    /// The level of play for a practice mission (the main menu's "Practice a skill"), then the practice menu.
    /// </summary>
    /// <returns>The skill chosen in the practice menu, 0..3, or -1 when the player gave up.</returns>
    // orig: FINAL 0100:0c40 choose_difficulty (src/c/final/final_0100_0c40.c) matched; tests: FinalOracleTests
    public int ChooseDifficulty()
    {
        _state.Txt.Text = _state.StrWhichLevel;
        // C: Esc answers -1, which becomes the level as it is
        _state.Diff = (short)MenuChoice(_state.Txt, 0x50, 0x80);
        // C: the original has no return statement and gives back what the practice menu left in AX, its choice
        return TrainingMenu(1);
    }

    /// <summary>
    /// The training screen: the four skills in a menu over the training picture. For a practice mission
    /// (<paramref name="practice"/> not 0) the skill chosen is left in <see cref="FinalState.MenuSelection"/>'s place
    /// for the moment of the choice and the menu ends. For a new agent each choice raises a skill by one (to Awesome at
    /// most) until four points are spent; Esc spends the points left on the first skills, one each, where not yet Awesome. The ratings are
    /// added to <see cref="FinalState.Skills"/>; at level 0 all four skills are Good whatever was chosen.
    /// </summary>
    /// <param name="practice">0 for a new agent's training, else the practice menu.</param>
    /// <returns>For the practice menu the skill chosen, 0..3, or -1 for Esc (the original leaves its answer in AX); for a new agent's training nothing meaningful (0).</returns>
    // orig: FINAL 0100:0c70 training_menu (src/c/final/final_0100_0c70.c) matched; tests: FinalOracleTests
    public int TrainingMenu(int practice)
    {
        const int menuX = 0x5c, menuY = 0x19, screenColour = 5, settleFrames = 0x3c;
        const short allGood = 0x55, trainedFlags = 0x500;
        var ratings = new int[FinalState.SkillCount];
        var spent = 0;
        Pictures.LoadPictureSwapped(_state.TrainingPicture, PicturePage);
        _world.Graphics.CopyRect(_state.RpBack, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
        _world.Graphics.SetColorMaps(_state.ColorMapA, _state.ColorMapB);
        _state.MenuSelection = 0;
        int choice;
        do
        {
            _state.Rp1.BPen = screenColour;
            if (practice == 0)
            {
                AppendSkillRating(ratings);
            }
            var menu = _state.StrSkillMenu[0];
            foreach (var skill in _state.SkillNames)
            {
                menu += skill + _state.StrSkillMenu[practice != 0 ? 1 : 2];
            }
            _state.Txt.Text = menu;
            choice = MenuChoice(_state.Txt, menuX, menuY);
            _state.MenuSelection = (short)choice;
            if (practice != 0)
            {
                _state.MenuSelection = 0;
                return choice;
            }
            if (choice != -1 && ratings[choice] != BestRating)
            {
                spent++;
                ratings[choice]++;
            }
        }
        while (choice != -1 && spent != TrainingPoints);

        var left = TrainingPoints - spent;
        for (var skill = 0; skill < left; skill++)
        {
            if (ratings[skill] < BestRating)
            {
                ratings[skill]++;
            }
        }
        AppendSkillRating(ratings);
        // C: the original then tests the rating behind the last one it raised (index 4 - points spent, the first when
        // all four were chosen) and lowers it from Good to Average, after it was shown. With nothing spent the index is
        // 4, one past the table: the original reads a word of its stack there, which holds no 2 (the other locals hold
        // 0, -1, 4, 40 and 100), so nothing changes.
        if (left < FinalState.SkillCount && ratings[left] == 2)
        {
            ratings[left] = 1;
        }
        for (var skill = 0; skill < FinalState.SkillCount; skill++)
        {
            // C: added to what the variable held, two bits per skill
            _state.Skills = (short)(_state.Skills + (ratings[skill] << 2 * skill));
        }
        Output.WaitVsync(settleFrames);
        if (_state.Diff == 0)
        {
            _state.Skills = allGood;
        }
        _state.Skills |= trainedFlags;
        // C: the training's end leaves nothing defined in AX, and no caller reads it
        return 0;
    }

    /// <summary>
    /// Shows the ratings of the agent's four skills under their columns of the training screen: for each, a box in
    /// the skill's colour and the rating's name ("Average" ... "Awesome") centred in it. On the four-colour display
    /// the last skill's colour is made black first.
    /// </summary>
    /// <remarks>A rating other than 0..3 leaves the text buffer as it was, and the box shows that text.</remarks>
    /// <param name="ratings">The four ratings, 0..3.</param>
    // orig: FINAL 0100:0e1a append_skill_rating (src/c/final/final_0100_0e1a.c) matched; tests: FinalOracleTests
    public void AppendSkillRating(IReadOnlyList<int> ratings)
    {
        const int columnWidth = 0x50, boxLeft = 8, boxWidth = 0x40, boxHeight = 8, top = 0xbc;
        const int letters = 0xe, cgaLetters = 0xf, lastSkill = FinalState.SkillCount - 1;
        if (_state.CGA)
        {
            _state.SkillColors[lastSkill] = 0;
        }
        for (var skill = 0; skill < FinalState.SkillCount; skill++)
        {
            if (ratings[skill] is >= 0 and < FinalState.SkillCount)
            {
                _state.Txt.Text = _state.RatingNames[ratings[skill]];
            }
            _world.Graphics.FillRect(_state.Rp1, columnWidth * skill + boxLeft, top, boxWidth, boxHeight, _state.SkillColors[skill]);
            Text.DrawTextCentered(_state.Txt.Text, columnWidth * skill + boxLeft + boxWidth / 2, top, _state.CGA ? cgaLetters : letters);
        }
    }

    /// <summary>
    /// Leaves the menus for a practice mission: the part to run next is the practice (<paramref name="practiceKind"/>
    /// plus 0x0d: the building raid, the car chase, the cryptography, the electronics), the agent is a practising one
    /// with every skill Good, at a random organisation (4 and on) in a random city of a random world; a cryptography
    /// practice gets its message. The campaign is handed on.
    /// </summary>
    /// <param name="practiceKind">The skill chosen, 0..3 (the order of the practice menu).</param>
    // orig: FINAL 0100:04a7 start_practice_module (src/c/final/final_0100_04a7.c) matched; tests: FinalOracleTests
    public void StartPracticeModule(int practiceKind)
    {
        const int firstPracticePart = 0xd, cryptography = 2, firstForeignOrg = 4;
        const short allGood = 0x555;
        // C: the part's number is a char
        _shared.NextModule = (sbyte)(practiceKind + firstPracticePart);
        _shared.Entered = 1;
        _state.Status |= Options.Practice;
        _state.Skills = allGood;
        _state.AtLoc = (short)(CaseSetup.Random(_state, 0xc) + firstForeignOrg);
        _state.InCity = (short)CaseSetup.Random(_state, CampaignTables.WorldSize);
        LoadWorldFile(CaseSetup.Random(_state, 3));
        if (practiceKind == cryptography)
        {
            MakePracticeMessage();
        }
        HandOff.WriteEnvState(_state.PracticeHandOffFile);
    }

    /// <summary>
    /// The message of a cryptography practice: two random organisations (4 and on) and two random cities become the
    /// message's placeholders, then its five parts (from where, to where, from whom, to whom, the rest) are read in a
    /// random order, each a random variant, into the text buffer, whose first 512 characters go into the hand-off block
    /// for the cryptography part.
    /// </summary>
    // orig: FINAL 0100:053a make_practice_message (src/c/final/final_0100_053a.c) matched; tests: FinalOracleTests
    public void MakePracticeMessage()
    {
        const int senderOrg = 1, receiverOrg = 2, senderCity = 3, receiverCity = 4, firstForeignOrg = 4, allParts = 0x1f;
        int[] variants = [8, 8, 8, 8, 0xc];
        var values = _state.VariableValueTexts;
        // C: the original takes the name 0x90 bytes, four records, behind the organisation drawn
        values[senderOrg] = _state.Org[CaseSetup.Random(_state, 0xc) + firstForeignOrg].Name;
        values[receiverOrg] = _state.Org[CaseSetup.Random(_state, 0xc) + firstForeignOrg].Name;
        values[senderCity] = _state.Ct[CaseSetup.Random(_state, CampaignTables.WorldSize)].Name;
        values[receiverCity] = _state.Ct[CaseSetup.Random(_state, CampaignTables.WorldSize)].Name;
        var partsRead = 0;
        _state.Txt.Text = "";
        do
        {
            var part = CaseSetup.Random(_state, FinalState.PracticeMessageParts);
            if ((partsRead & 1 << part) == 0)
            {
                partsRead |= 1 << part;
                Text.ReadRandomVariant(_state.PracticeTagFiles[part], _state.PracticeTags[part], variants[part]);
            }
        }
        while (partsRead != allParts);
        _state.Txt.Bytes.AsSpan(0, _shared.Text.Bytes.Length).CopyTo(_shared.Text.Bytes);
    }

    /// <summary>
    /// Asks for the agent's code name: a framed box with "Max's code name is:" and a field of 15 characters, in which the
    /// player types it. The name typed becomes the career's code name.
    /// </summary>
    // orig: FINAL 0100:0af4 ask_code_name (src/c/final/final_0100_0af4.c) matched; tests: FinalOracleTests
    public void AskCodeName()
    {
        const int frameGrey = 8, boxColour = 5, lightLine = 7, promptColour = 2, nameLength = 0xf;
        _world.Graphics.SetColorMaps(_state.CodeNameColorMap, _state.CodeNameColorMap);
        FillRectCorners(0x63, 0x55, 0xdd, 0x72, frameGrey);
        FillRectCorners(0x64, 0x57, 0xdc, 0x71, boxColour);
        FillRectCorners(0x65, 0x58, 0xdb, 0x70, frameGrey);
        FillRectCorners(0x69, 0x65, 0xd7, 0x6e, boxColour);
        // the box's edges: light above and at the field's sides, dark below the prompt and the field
        Output.DrawLine(0x65, 0x56, 0xdb, 0x56, lightLine);
        Output.DrawLine(0x67, 0x63, 0xd9, 0x63, boxColour);
        Output.DrawLine(0x66, 0x70, 0xda, 0x70, boxColour);
        Output.DrawLine(0x68, 0x65, 0x68, 0x6e, lightLine);
        Output.DrawLine(0xd8, 0x65, 0xd8, 0x6e, lightLine);
        Text.DrawTextCentered(_state.StrCodeNamePrompt, 0xa1, 0x5a, promptColour);
        _state.Txt.Text = "";
        EditTextField(0x6a, 0x64, nameLength);
        _state.Pl.Handle = _state.Txt.Text;
        _state.Rp1.BPen = boxColour;
        _world.Graphics.SetColorMaps(_state.ColorMapA, _state.ColorMapB);
    }

    /// <summary>
    /// Lets the player edit the text buffer as a field of <paramref name="width"/> characters at
    /// (<paramref name="x"/>, <paramref name="y"/>), padded with blanks: a blinking cursor; Left and Right move it,
    /// Insert opens a blank, Delete and Backspace take a character out, a character typed replaces the one under the
    /// cursor (the first one typed clears the field first), Return ends. Blanks at the end are cut off, except the first
    /// character's.
    /// </summary>
    /// <param name="x">Left edge of the field, less 2.</param>
    /// <param name="y">Top edge of the field, less 2.</param>
    /// <param name="width">The number of characters.</param>
    // orig: FINAL 0100:0ed2 edit_text_field (src/c/final/final_0100_0ed2.c) matched; tests: FinalOracleTests
    public void EditTextField(int x, int y, int width)
    {
        const int fieldColour = 5, cursorColour = 2, cursorHeight = 0xa, blinkFrames = 0xa, cellWidth = 7, fieldHeight = 9;
        const int left = 0x4b00, right = 0x4d00, insert = 0x5200, delete = 0x5300, backspace = 8;
        var text = _state.Txt;
        _world.Graphics.FillRect(_state.Rp1, x, y + 1, width * cellWidth, fieldHeight, fieldColour);
        _state.Rp1.BPen = fieldColour;
        _state.Willy = 0;
        var firstKey = true;
        for (var i = text.Length; i < width; i++)
        {
            text[i] = ' ';
        }
        text.Truncate(width);
        DrawEditField(x, y, width);
        while (true)
        {
            while (_world.Input.NoKeyWaiting())
            {
                _world.Graphics.RecolorRect(_state.Rp1, _state.WillyX, y + 1, _state.WillyW, cursorHeight, fieldColour, cursorColour);
                Output.WaitVsync(blinkFrames);
                _world.Graphics.RecolorRect(_state.Rp1, _state.WillyX, y + 1, _state.WillyW, cursorHeight, cursorColour, fieldColour);
                if (_world.Input.NoKeyWaiting())
                {
                    Output.WaitVsync(blinkFrames);
                }
            }
            var key = Input.ReadKey();
            if (key == Keys.Return)
            {
                break;
            }
            var cursor = _state.Willy;
            if (key == left && cursor != 0)
            {
                _state.Willy--;
            }
            if (key == right && width - 1 > cursor)
            {
                _state.Willy++;
            }
            if (key == insert)
            {
                var rest = CopyOfText(text, cursor);
                PutText(text, cursor + 1, rest);
                text[cursor] = ' ';
                text.Truncate(width);
            }
            if (key == delete)
            {
                // C: the character that moves under the cursor is blanked too: Delete takes out two characters
                PutText(text, cursor, CopyOfText(text, cursor + 1));
                text[cursor] = ' ';
                text[width - 1] = ' ';
                text.Truncate(width);
            }
            if (key == backspace && cursor != 0)
            {
                PutText(text, cursor - 1, CopyOfText(text, cursor));
                text[width - 1] = ' ';
                text.Truncate(width);
                _state.Willy--;
            }
            if (key is >= ' ' and < 0x80)
            {
                if (firstKey)
                {
                    for (var i = 0; i < width; i++)
                    {
                        text[i] = ' ';
                    }
                }
                text[_state.Willy] = (char)key;
                if (width - 1 > _state.Willy)
                {
                    _state.Willy++;
                }
            }
            firstKey = false;
            DrawEditField(x, y, width);
        }
        for (var i = width - 1; i > 0 && text[i] == ' '; i--)
        {
            text[i] = '\0';
        }
    }

    /// <summary>The text of a buffer from a place to its end mark (the C's <c>Txt + n</c> as a string).</summary>
    private static string CopyOfText(TextBuffer text, int from) => text.Text.Length > from ? text.Text[from..] : "";

    /// <summary>Writes a text and its end mark into a buffer from a place on (the C's <c>strcpy(Txt + n, ...)</c>).</summary>
    private static void PutText(TextBuffer text, int at, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            text[at + i] = value[i];
        }
        text[at + value.Length] = '\0';
    }

    /// <summary>
    /// Draws the text field being edited, which is the text buffer: <paramref name="width"/> cells from
    /// (<paramref name="x"/> + 2, <paramref name="y"/> + 2), each cleared as an 8 x 8 box, and the buffer's characters
    /// one by one over them, each as wide as the font makes it. Notes where the character under the cursor was drawn
    /// and how wide it is (<c>willyX</c>, <c>willyW</c>), for the cursor.
    /// </summary>
    /// <param name="x">Left edge of the field, less 2.</param>
    /// <param name="y">Top edge of the field, less 2.</param>
    /// <param name="width">The number of cells.</param>
    // orig: FINAL 0100:10fb draw_edit_field (src/c/final/final_0100_10fb.c) matched; tests: FinalOracleTests
    public void DrawEditField(int x, int y, int width)
    {
        const int cellSize = 8, fieldColour = 5, letterColour = 0xa, font = 1;
        x += 2;
        y += 2;
        var text = _state.Txt;
        for (var i = 0; i < width; i++)
        {
            _world.Graphics.FillRect(_state.Rp1, x, y, cellSize, cellSize, fieldColour);
            if (text.Length > i)
            {
                // the original draws the one character by putting an end mark behind it for the call, and takes it away
                Output.DrawText(text[i].ToString(), x, y, letterColour);
                if (i == _state.Willy)
                {
                    _state.WillyX = (short)x;
                    _state.WillyW = (short)_world.Graphics.CharWidth(font, text[i]);
                }
                // C: the cursor's character is measured twice, as in the original; x is a 16-bit int
                x = C16.Int(x + _world.Graphics.CharWidth(font, text[i]));
            }
        }
    }
}
