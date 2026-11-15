using CovertAction.Core.Intro;
using CovertAction.Core.Shared;
using CovertAction.Platform;

namespace CovertAction.Presentation.Intro;

/// <summary>
/// The set-up screen the game opens with (the original's INTRO.EXE): under a framed title with the version and the
/// copyright, the player answers with a digit key which sound card the PC has and whether he plays with a joystick.
/// The answers go into the hand-off block, where the menus part and the others read them: the sound driver's letter
/// ('N' turns the sound off) and the joystick flag (the menus part then has the joystick calibrated).
/// </summary>
/// <remarks>
/// The screen is the PC's 80 x 25 text page (video mode 3), printed with the program's own texts at their rows and
/// columns and in their colours; the host shows it in the VGA's 8 x 16 character set. The graphics card is not asked
/// for: the rewrite has one graphics driver (D-600). The player's game shows only the title (<see cref="ShowTitle"/>):
/// the sound card and the controls are the launcher's settings (D-603). The original's questions
/// (<see cref="HardwareSetupMenu"/>) are kept for the proof, which feeds them the recorded keys.
/// </remarks>
/// <param name="state">The texts and driver letters of the player's INTRO.EXE.</param>
/// <param name="shared">The hand-off block the answers are written into.</param>
/// <param name="world">The keyboard the answers come from.</param>
/// <param name="page">The text page the screen is printed on; the caller has the host show it.</param>
public sealed class HardwareSetup(IntroState state, SharedState shared, Boundaries world, TextScreen page)
{
    // the page rows of the framed title: from row 1 to row 10, a row of the frame, framed rows and blank framed rows
    private const int FrameTopRow = 1;

    // the rows and columns the questions are printed at: the graphics prompt and its menu lines, the sound prompt and its
    // menu lines, the control lines, the last words
    private const int GraphicsPromptRow = 13, FirstGraphicsRow = 15, GraphicsAnswerColumn = 0x31;
    private const int SoundPromptRow = 12, FirstSoundRow = 14, MenuColumn = 0x1b;
    private const int FirstControlRow = 13;
    private const int ColourRow = 13, WaitRow = 16, WaitColumn = 0x1d;

    // where the cursor waits for an answer
    private const int AnswerRow = 13, AnswerColumn = 0x32;

    // the blank rows before each question: from the row the prompt of the sound question is on, as many as the original prints
    private const int QuestionTop = 12, QuestionBlankRows = 12;

    // the blank rows before the graphics question: ten from row 13, then two from row 11
    private const int GraphicsBlankTop = 13, GraphicsBlankRows = 10, GraphicsSecondTop = 11, GraphicsSecondRows = 2;

    /// <summary>The graphics menu's entry the rewrite's driver is: "2) MCGA (256 color)".</summary>
    private const int McgaChoice = 1;

    /// <summary>The control the first answer of the last question chooses: "1. Joystick and Keyboard".</summary>
    private const int JoystickChoice = 0;

    /// <summary>How long the title stays up without the questions: two seconds of the display's 70 frames a second.</summary>
    public const int TitleFrames = 140;

    private readonly TextConsole _console = new(page);

    /// <summary>
    /// The set-up screen as the player's game shows it: the framed title and "One Moment Please..." as the original
    /// leaves the page after the last answer, for about two seconds (<see cref="TitleFrames"/> frames of the display),
    /// without a question and without reading a key. The answers are the launcher's settings: the MCGA driver, the sound
    /// card and the controls go into the hand-off block as the questions would put them there.
    /// </summary>
    /// <param name="soundLetter">The sound driver's letter of the launcher's sound card (N, I, T, A, R), as the sound question's answer.</param>
    /// <param name="joystick">The launcher's controls: true "keyboard and joystick" (the question's first answer), false "keyboard only".</param>
    // DIVERGENCE: D-600 the graphics card is not asked for: the letter is the MCGA driver's, the only one the rewrite has.
    // DIVERGENCE: D-603 the sound card and the controls are not asked for: they are the launcher's settings.
    public void ShowTitle(char soundLetter, bool joystick)
    {
        page.Clear();
        DrawTitle();
        _console.PrintAt(ColourRow, 0, state.WaitColour);
        _console.PrintAt(WaitRow, WaitColumn, state.OneMoment);
        shared.GfxDriver = (sbyte)state.GraphicsLetters[McgaChoice];
        shared.SndDriver = (sbyte)soundLetter;
        shared.Joystick = (sbyte)(joystick ? 1 : 0);
        for (var frame = 0; frame < TitleFrames; frame++)
        {
            world.Timer.WaitForFrame();
        }
    }

    /// <summary>
    /// Shows the set-up screen and asks its questions: the sound card (six choices, 1 to 6) and the controls (1
    /// joystick and keyboard, 2 keyboard only). A key that is not one of the digits offered is ignored. Ends with
    /// "One Moment Please..." on the page, as the launcher then loads the drivers.
    /// </summary>
    // orig: INTRO 0000:0010 hardware_setup_menu (src/c/intro/intro_0000_0010.c) matched; tests: HardwareSetupTests, IntroOracleTests
    // DIVERGENCE: D-2 the input driver the original loads first is the rewrite's own.
    public void HardwareSetupMenu()
    {
        // the mode set (int86 with AX = 3 in the original): a blank page in the default colour, the cursor at the top left
        page.Clear();
        DrawTitle();

        // DIVERGENCE: D-600 the graphics card is not asked for: the letter is the MCGA driver's, the only one the rewrite has.
        // The question is printed as in the original, without waiting for its key: the blank rows of the next question
        // print over its texts, but the places keep the colours the prompt and the menu gave them.
        PrintGraphicsQuestion();
        shared.GfxDriver = (sbyte)state.GraphicsLetters[McgaChoice];

        // DORMANT: the original asks for the sound card only while a flag of its own is 1, and sets it to 1 just
        // before; and a seventh and later sound card would take its letter from another table, but the menu has six.
        BlankQuestionRows();
        _console.PrintAt(SoundPromptRow, 0, state.SoundPrompt);
        for (var choice = 0; choice < state.SoundChoices.Length; choice++)
        {
            // the menu line is the choice's number and its text, "1) Shhh! No sounds please"
            _console.PrintAt(FirstSoundRow + choice, MenuColumn, $"{choice + 1}{state.SoundChoices[choice]}");
        }
        shared.SndDriver = (sbyte)state.SoundLetters[AskForDigit(state.SoundChoices.Length)];

        BlankQuestionRows();
        for (var choice = 0; choice < state.ControlChoices.Length; choice++)
        {
            _console.PrintAt(FirstControlRow + choice, MenuColumn, state.ControlChoices[choice]);
        }
        shared.Joystick = (sbyte)(AskForDigit(state.ControlChoices.Length) == JoystickChoice ? 1 : 0);

        BlankQuestionRows();
        _console.PrintAt(ColourRow, 0, state.WaitColour);
        _console.PrintAt(WaitRow, WaitColumn, state.OneMoment);
    }

    /// <summary>
    /// The graphics question as the original prints it, up to where it waits for the key: blank rows, the prompt in
    /// yellow on row 13 and the menu of the five cards from row 15, each its number and its text, the cursor at the answer.
    /// </summary>
    // The statements of hardware_setup_menu (INTRO 0000:0010) from set_cursor(0xd, 0) to the first get_key.
    private void PrintGraphicsQuestion()
    {
        _console.SetCursor(GraphicsBlankTop, 0);
        for (var row = 0; row < GraphicsBlankRows; row++)
        {
            _console.PrintString(state.BlankRow);
        }
        _console.SetCursor(GraphicsSecondTop, 0);
        for (var row = 0; row < GraphicsSecondRows; row++)
        {
            _console.PrintString(state.BlankRow);
        }
        _console.PrintAt(GraphicsPromptRow, 0, state.GraphicsPrompt);
        _console.PrintString(state.BlankRow);
        for (var choice = 0; choice < state.GraphicsChoices.Length; choice++)
        {
            _console.PrintAt(FirstGraphicsRow + choice, MenuColumn, $"{choice + 1}{state.GraphicsChoices[choice]}");
        }
        _console.SetCursor(GraphicsPromptRow, GraphicsAnswerColumn);
    }

    /// <summary>Waits for a digit key from 1 to <paramref name="choices"/>, with the cursor at the answer's place; other keys are ignored.</summary>
    /// <returns>The choice, from 0 for the key 1.</returns>
    private int AskForDigit(int choices)
    {
        while (true)
        {
            _console.SetCursor(AnswerRow, AnswerColumn);
            // the original takes the character of the key, the low byte of the keyboard driver's answer (the high
            // byte is the key's scan code)
            const int characterBits = 0xff;
            var key = (char)(world.Input.ReadKeyRaw() & characterBits);
            if (key >= '1' && key < '1' + choices)
            {
                return key - '1';
            }
        }
    }

    /// <summary>
    /// The framed title: the title's colour, then rows 1 to 10 of the page, each a whole row of the program's own text:
    /// the frame's top, rows of the frame with and without text, and the frame's bottom.
    /// </summary>
    private void DrawTitle()
    {
        _console.PrintAt(0, 0, state.TitleColour);
        string[] rows =
        [
            state.TopFrame, state.BorderRow, state.TitleRows[0], state.BorderRow, state.TitleRows[1], state.BorderRow,
            state.TitleRows[2], state.TitleRows[3], state.BorderRow, state.BottomFrame,
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            _console.PrintAt(FrameTopRow + i, 0, rows[i]);
        }
    }

    /// <summary>
    /// Blanks the rows below the title before a question: DOS prints its blank row (79 blanks and a carriage return
    /// and line feed, one row exactly) from the top of the questions down, which leaves the colour of the places.
    /// </summary>
    // The loops of print_string(menu_blank_row) in hardware_setup_menu, before each question.
    private void BlankQuestionRows()
    {
        _console.SetCursor(QuestionTop, 0);
        for (var row = 0; row < QuestionBlankRows; row++)
        {
            _console.PrintString(state.BlankRow);
        }
    }
}
