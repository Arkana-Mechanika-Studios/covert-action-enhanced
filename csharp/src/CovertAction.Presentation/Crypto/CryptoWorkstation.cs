using System.Globalization;
using CovertAction.Core.Crypto;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Crypto;

/// <summary>
/// The cryptography minigame as the player meets it: the "Crypto Workstation" screen with the enciphered message, a
/// column of the 26 cipher letters with how often each occurs, the player's guesses under the text, and a timer.
/// The player presses a cipher letter and then the plain letter it stands for; F1 buys a hint for an hour of game
/// time, F10 clears the guesses, Esc gives up. This class holds the functions of the original's CODE program that
/// are the game itself; the rules they apply are in <see cref="CryptoRules"/>.
/// </summary>
public sealed class CryptoWorkstation
{
    private const int ScreenWidth = 320;
    private const int ScreenHeight = 200;

    // The letter column at the left edge: for each cipher letter a row with the player's guess, the letter, and
    // how often it occurs in the message.
    private const int GuessColumnX = 4;
    private const int LetterColumnX = 12;
    private const int CountColumnX = 20;
    private const int CountWidth = 12;
    private const int ColumnTop = 9;
    private const int ColumnRowHeight = 7;

    // The cipher text: cells of one letter, 42 to a line; under each letter the player's guess for it.
    private const int TextLeft = 44;
    private const int TextTop = 16;
    private const int CellWidth = 6;
    private const int TextLineHeight = 16;
    private const int GuessBelowLetter = 7;
    private const int TextColumns = 42;

    /// <summary>The most characters of a message the screen has room for: 11 lines of 42.</summary>
    private const int MaxMessageLength = 462;

    // fonts
    private const int MenuFont = 1;
    private const int SmallFont = 2;
    private const int CipherFont = 3;

    // palette indices
    private const int Black = 0;
    private const int FrameColor = 2;
    private const int ScreenGreen = 0xa;    // the cipher text, the letter column, the title bar
    private const int GuessColor = 0xb;
    private const int BlinkBackground = 0xe;
    private const int White = 0xf;

    /// <summary>The timer counts one "second" for every ten display frames, and a hint adds an hour.</summary>
    private const int FramesPerTimerSecond = 10;

    private const int HintCostSeconds = 3600;

    /// <summary>The "next part" codes this part hands back: the campaign, or the menus after a practice mission.</summary>
    private const sbyte CampaignPart = 0, MenusPart = 0x11;

    private readonly CryptoState _state;
    private readonly SharedState _shared;
    private readonly Boundaries _world;

    /// <summary>Wires the part's routines to its state and to the outside world.</summary>
    /// <param name="state">The part's variables.</param>
    /// <param name="shared">The hand-off block: the message and the options come in through it, the result goes out.</param>
    /// <param name="world">The drivers and the machine.</param>
    public CryptoWorkstation(CryptoState state, SharedState shared, Boundaries world)
    {
        _state = state;
        _shared = shared;
        _world = world;
        Output = new LibraryOutput(state, world);
        Input = new LibraryInput(state, world);
        Menus = new LibraryMenus(state, shared, world, Output, Input);
    }

    /// <summary>The part's drawing and sound routines.</summary>
    public LibraryOutput Output { get; }

    /// <summary>The part's key, joystick and mouse routines.</summary>
    public LibraryInput Input { get; }

    /// <summary>The part's message box, disk prompt and animation hooks.</summary>
    public LibraryMenus Menus { get; }

    /// <summary>
    /// One session at the workstation, from the hand-off block to the hand-off block: takes level, skill, options and
    /// the message, runs the puzzle until it is solved or left, and writes back whether it was solved, the minutes
    /// it took and which part of the game runs next.
    /// </summary>
    // orig: CODE 0000:0b5a DoCode (src/c/code/code_0000_0b5a.c) matched; tests: CodeOracleTests
    public void DoCode()
    {
        _shared.NextModule = -1; // "leave the game" until the session ends normally

        // DIVERGENCE: D-2 the three attach_driver_segment calls (CODE 0000:052d) that patch the driver thunks are left out.
        _world.Graphics.SubCode0208(0);
        _world.Graphics.SetColorMaps(_state.ColorMapStart, _state.ColorMapStart);

        // the agent's cryptography skill (0..3, bits 4 and 5 of the skills word) makes the puzzle easier
        var cryptoSkill = _shared.SkillBits >> 4 & 3;
        _state.CODEdiff = (short)GameLibrary.Clamp(_shared.Level - cryptoSkill + 1, -3, 5);
        _state.CODEstatus = _shared.Flags;
        _state.GfxLetter = _shared.GfxLetter;
        _state.Rp1.FontNr = MenuFont;
        _state.Rp1.BPen = White;

        // every session shuffles a different cipher
        _state.RandomSeed = LibraryRandom.SeedRandom(_state.Random, _world.Timer);
        CryptoRules.SetupCipher(_state, _shared);
        DrawCryptoScreen();
        do
        {
            CryptoKeyStep();
        }
        while (_state.CODEQuit == 0);

        var practice = _state.CODEstatus.HasFlag(Options.Practice);
        _shared.Value = (sbyte)(_state.CODESuccess ? 1 : 0);
        _shared.Elapsed = (sbyte)_state.CODERtime; // the hand-off has one byte for the minutes
        _shared.NextModule = practice ? MenusPart : CampaignPart;
        CryptoRules.GoodBye(_shared, quit: false);
        _state.Rp1.FontNr = MenuFont;
        // the next part is on disk 1 (the menus) or disk 2 (the campaign)
        Menus.CheckForDisk(practice ? 1 : 2, 1);
    }

    /// <summary>
    /// Draws the whole workstation and prepares the puzzle for play: frame and title, the letter column, and the
    /// cipher text letter by letter (two frames apart, so the text appears to be typed), counting how often each
    /// cipher letter occurs.
    /// </summary>
    /// <remarks>
    /// On difficulty 2 and above 3 the message is first made harder with homophones (<see cref="AddHomophones"/>). On
    /// difficulty 0 and below the most frequent letter is given away, and one more random letter per step below 0.
    /// </remarks>
    // orig: CODE 0000:0e2d draw_crypto_screen (src/c/code/code_0000_0e2d.c) matched; tests: CodeOracleTests
    public void DrawCryptoScreen()
    {
        // Three working values that the steps below hand on to each other. The original does not initialise them;
        // each is set before it is read as long as the message has a letter and the cipher has all 26 letters.
        // Both always hold in play: every message the campaign or the practice sends holds paragraphs of TEXT.DTA,
        // and the cipher starts as A..Z in CODE.EXE (DS:105a) and is only shuffled by swaps (SetupCipher).
        // DIVERGENCE: D-3 where the original would read one of them unset (stack garbage), the C# throws.
        int? mostFrequent = null;   // a cipher letter
        int? spare = null;          // the alphabet position of an unused cipher letter
        int? letter = null;         // a character of the message, or a letter

        DrawFrame();
        DrawTitle();
        _state.Rp1.FontNr = CipherFont;
        CountLetters(ref letter);
        if (_state.CODEdiff == 2 || _state.CODEdiff > 3)
        {
            AddHomophones(ref mostFrequent, ref spare, ref letter);
        }
        DrawLetterColumn(ref mostFrequent);
        if (_state.MessageText.Length > MaxMessageLength)
        {
            _state.MessageText.Truncate(MaxMessageLength);
        }
        DrawCipherText();
        if (_state.CODEdiff <= 0)
        {
            GiveHeadStart(Set(mostFrequent, "the most frequent letter, when no letter occurs"));
        }
    }

    /// <summary>The empty screen: black, a green title bar, and the letter column's frame with a thick top and bottom.</summary>
    private void DrawFrame()
    {
        const int columnRight = 32, bottom = ScreenHeight - 1;
        Output.FillRectCorners(0, 0, ScreenWidth, ScreenHeight, Black);
        Output.FillRectCorners(columnRight + 2, 0, ScreenWidth, 8, ScreenGreen);
        Output.DrawLine(1, 0, columnRight - 1, 0, FrameColor);
        Output.DrawLine(1, bottom, columnRight - 1, bottom, FrameColor);
        Output.DrawLine(0, 1, 0, bottom - 1, FrameColor);
        Output.DrawLine(columnRight, 1, columnRight, bottom - 1, FrameColor);
        _world.Graphics.FillRect(_state.Rp1, 2, 2, 29, 4, FrameColor);
        _world.Graphics.FillRect(_state.Rp1, 2, bottom - 5, 29, 4, FrameColor);
    }

    /// <summary>
    /// The title in the bar. It ends in a three-digit "message number", which is made up from three characters of
    /// the message (the low three bits of each, added to the digits 0, 1 and 2) so that it differs from message to message.
    /// </summary>
    private void DrawTitle()
    {
        var message = _state.MessageText;
        var number = new string([(char)('0' + (message[13] & 7)), (char)('1' + (message[21] & 7)), (char)('2' + (message[7] & 7))]);
        var title = _state.StrCryptoTitle[..^number.Length] + number;
        _state.Rp1.FontNr = SmallFont;
        Output.DrawTextFlag0(title, 64, 1, Black);
    }

    /// <summary>Counts how often each cipher letter occurs in the message.</summary>
    /// <param name="letter">Left holding the last character looked at (its cipher letter, if it was a letter).</param>
    private void CountLetters(ref int? letter)
    {
        var message = _state.MessageText;
        for (var i = 0; i < message.Length; i++)
        {
            letter = message[i];
            if (IsLetter(message[i]))
            {
                var cipher = _state.CipherAlphabet[message[i] - 'A'];
                _state.LetterCount[cipher - 'A']++;
                letter = cipher;
            }
        }
    }

    /// <summary>
    /// Makes the puzzle harder with homophones: every cipher letter the message does not use takes over every second
    /// occurrence of the letter that is the most frequent at that moment, so that one plain letter is written with
    /// several cipher letters and letter counts tell less.
    /// </summary>
    /// <remarks>
    /// For each unused cipher letter, in order: the most frequent cipher letter is found (the first of equals), then
    /// the plain letter it stands for, then the alphabet position of the unused letter. In the message, every
    /// occurrence of that plain letter at an odd position becomes the plain letter of the unused letter's position
    /// (so it will be enciphered as the unused letter), and that position is noted to stand for the frequent plain
    /// letter. The three values are kept between the steps as the original keeps them: a search that finds nothing
    /// leaves the value of the step before.
    /// </remarks>
    private void AddHomophones(ref int? mostFrequent, ref int? spare, ref int? plainLetter)
    {
        var message = _state.MessageText;
        for (var unused = 0; unused < _state.AlphabetLen; unused++)
        {
            if (_state.LetterCount[unused] != 0)
            {
                continue;
            }
            var highest = 0;
            for (var cipher = 0; cipher < _state.AlphabetLen; cipher++)
            {
                if (_state.LetterCount[cipher] > highest)
                {
                    mostFrequent = cipher;
                    highest = _state.LetterCount[cipher];
                }
            }
            if (FirstPositionOf(Set(mostFrequent, "the most frequent letter, when no letter occurs")) is { } frequentPosition)
            {
                plainLetter = _state.PlainAlphabet[frequentPosition];
            }
            if (FirstPositionOf(unused) is { } unusedPosition)
            {
                spare = unusedPosition;
            }
            for (var i = 0; i < message.Length; i++)
            {
                var odd = (i & 1) != 0;
                if (odd && message[i] == Set(plainLetter, "the plain letter, when the message is empty"))
                {
                    var position = Set(spare, "the spare position, when the cipher lacks a letter");
                    message[i] = (char)('A' + position);
                    _state.LetterCount[unused]++;
                    _state.LetterCount[mostFrequent!.Value]--;
                    _state.PlainAlphabet[position] = (char)plainLetter!.Value;
                }
            }
        }
    }

    /// <summary>A working value that must have been set by now; where the original would read it unset, this throws.</summary>
    private static int Set(int? value, string what) => value ?? throw new UninitialisedReadException(what);

    /// <summary>The first position of the alphabet whose cipher letter is the given one, or null when none has it.</summary>
    private int? FirstPositionOf(int cipher)
    {
        for (var position = 0; position < _state.AlphabetLen; position++)
        {
            if (CryptoRules.CipherLetterOf(_state, position) == cipher)
            {
                return position;
            }
        }
        return null;
    }

    /// <summary>
    /// Draws the letter column, every cipher letter with the count "0", and sets all counts back to 0: they are
    /// counted up again, visibly, as the text is drawn.
    /// </summary>
    /// <param name="mostFrequent">Receives the cipher letter that occurred most often (the first of equals); unchanged when no letter occurred.</param>
    private void DrawLetterColumn(ref int? mostFrequent)
    {
        var highest = 0;
        for (var cipher = 0; cipher < _state.AlphabetLen; cipher++)
        {
            _state.Rp1.FontNr = CipherFont;
            Output.DrawTextFlag0(LetterText('A' + cipher), LetterColumnX, ColumnY(cipher), ScreenGreen);
            _state.Rp1.FontNr = SmallFont;
            Output.DrawTextFlag0(_state.StrZero, CountColumnX, ColumnY(cipher), ScreenGreen);
            if (_state.LetterCount[cipher] > highest)
            {
                highest = _state.LetterCount[cipher];
                mostFrequent = cipher;
            }
            _state.LetterCount[cipher] = 0;
        }
    }

    /// <summary>
    /// Draws the cipher text cell by cell, two frames apart. Each letter also counts up its cipher letter in the
    /// letter column. Where a line ends inside a word, a hyphen is drawn in the margin.
    /// </summary>
    private void DrawCipherText()
    {
        var message = _state.MessageText;
        int column = 0, line = 0;
        for (var i = 0; i < message.Length; i++)
        {
            int shown = message[i];
            if (IsTableCharacter(message[i]))
            {
                shown = _state.CipherAlphabet[shown - 'A'];
                CountUp(shown - 'A');
            }
            _state.Rp1.FontNr = CipherFont;
            Output.DrawTextFlag0(LetterText(shown), CellX(column), CellY(line), ScreenGreen);
            column++;
            if (column >= TextColumns)
            {
                if (IsLetter(message[i]) && IsLetter(message[i + 1]))
                {
                    Output.DrawTextFlag0(_state.StrDash, CellX(column), CellY(line), ScreenGreen);
                }
                column = 0;
                line++;
            }
            Output.WaitVsync(2);
        }
    }

    /// <summary>Counts one more occurrence of a cipher letter and redraws its count in the letter column.</summary>
    private void CountUp(int cipher)
    {
        _state.Rp1.FontNr = SmallFont;
        _world.Graphics.FillRect(_state.Rp1, CountColumnX, ColumnY(cipher), CountWidth, ColumnRowHeight, Black);
        var count = ++_state.LetterCount[cipher];
        Output.DrawTextFlag0(count.ToString(CultureInfo.InvariantCulture), CountColumnX, ColumnY(cipher), ScreenGreen);
    }

    /// <summary>
    /// The head start of the easy difficulties: the most frequent cipher letter is solved from the beginning, and
    /// below difficulty 0 one more random letter for each step.
    /// </summary>
    private void GiveHeadStart(int mostFrequent)
    {
        for (var position = 0; position < _state.AlphabetLen; position++)
        {
            if (CryptoRules.CipherLetterOf(_state, position) == mostFrequent)
            {
                _state.GuessTable[mostFrequent] = (char)('A' + position);
                _state.LetterRevealed[mostFrequent] = true;
            }
        }
        for (var extra = _state.CODEdiff; extra < 0; extra++)
        {
            CryptoRules.RevealHintLetter(_state);
        }
        RedrawGuesses();
    }

    /// <summary>
    /// The show before a hint (F1): the success sound, then 200 random cells of the text blink once each, as if the
    /// workstation were searching. It only draws; the hint itself follows.
    /// </summary>
    // orig: CODE 0000:1409 hint_scan_animation (src/c/code/code_0000_1409.c) matched; tests: CodeOracleTests
    public void HintScanAnimation()
    {
        const int blinks = 200;
        var message = _state.MessageText;
        Output.DoSound(Sounds.HintDone);
        _state.Rp1.FontNr = CipherFont;
        for (var blink = 0; blink < blinks; blink++)
        {
            var position = GameLibrary.RandomBelow(_state.Random, message.Length);
            int shown = message[position];
            if (IsLetter(message[position]))
            {
                shown = _state.CipherAlphabet[shown - 'A'];
            }
            int x = CellX(position % TextColumns), y = CellY(position / TextColumns);
            _world.Graphics.FillRect(_state.Rp1, x, y, CellWidth, 6, Black);
            Output.WaitVsync(2);
            Output.DrawTextFlag0(LetterText(shown), x, y, ScreenGreen);
        }
        _state.Rp1.FontNr = SmallFont;
    }

    /// <summary>
    /// The player has pressed a cipher letter: blinks that letter in the column (five frames off, five on) until the
    /// next key is pressed. The timer runs on during the wait: one "second" per blink.
    /// </summary>
    /// <param name="cipher">The cipher letter, 0 = A.</param>
    // orig: CODE 0000:14d7 blink_selected_letter (src/c/code/code_0000_14d7.c) matched; tests: CodeOracleTests
    public void BlinkSelectedLetter(int cipher)
    {
        var letter = LetterText('A' + cipher);
        do
        {
            _state.ElapsedSeconds++;
            DrawTimer();
            _state.Rp1.FontNr = CipherFont;
            _state.Rp1.BPen = BlinkBackground;
            _world.Graphics.FillRect(_state.Rp1, LetterColumnX, ColumnY(cipher), CellWidth, 6, Black);
            Output.WaitVsync(5);
            Output.DrawTextFlag0(letter, LetterColumnX, ColumnY(cipher), ScreenGreen);
            _state.Rp1.FontNr = SmallFont;
            Output.WaitVsync(5);
        }
        while (!_world.Input.Kbhit());
    }

    /// <summary>
    /// One round of the workstation: waits for a key while the timer runs, carries out what the key means, and
    /// checks whether the message is now decoded.
    /// </summary>
    /// <remarks>
    /// Esc leaves. F1 gives a hint and adds an hour to the timer. F10 clears the guesses. Alt-Q asks whether to quit
    /// the game. Alt-S switches sound. Alt-P shows "PAUSED" until a key. A letter key selects that cipher letter; the
    /// next key is its plain letter, and any key that is no letter clears the guess. Other keys are ignored.
    /// </remarks>
    // orig: CODE 0000:155a crypto_key_step (src/c/code/code_0000_155a.c) matched; tests: CodeOracleTests
    public void CryptoKeyStep()
    {
        var key = WaitForKey();
        switch (key)
        {
            case Keys.Esc:
                _state.CODEQuit = 1;
                return;
            case Keys.F10:
                CryptoRules.ResetGuesses(_state);
                RedrawGuesses();
                return;
            case Keys.F1:
                HintScanAnimation();
                CryptoRules.RevealHintLetter(_state);
                RedrawGuesses();
                _state.ElapsedSeconds += HintCostSeconds;
                break;
            case Keys.AltQ:
                AskToQuit();
                break;
            case Keys.AltS:
                _state.CODEstatus ^= Options.Sound;
                break;
            case Keys.AltP:
                Pause();
                break;
            default:
                GuessLetter(key - 'a');
                break;
        }
        if (CryptoRules.CheckSolution(_state))
        {
            ShowSolution();
        }
    }

    /// <summary>
    /// Waits for a key the workstation acts on, with the timer running: one "second" for every ten frames. Keys that
    /// mean nothing here (neither a command nor a letter) are read and dropped.
    /// </summary>
    private int WaitForKey()
    {
        while (true)
        {
            Output.WaitVsync(FramesPerTimerSecond);
            _state.ElapsedSeconds++;
            DrawTimer();
            if (!_world.Input.Kbhit())
            {
                continue;
            }
            var key = Input.ReadKey();
            var isCommand = key is Keys.Esc or Keys.F1 or Keys.F10 or Keys.AltQ or Keys.AltS or Keys.AltP;
            var cipher = key - 'a';
            if (isCommand || (cipher >= 0 && cipher < _state.AlphabetLen))
            {
                return key;
            }
        }
    }

    /// <summary>The player chose a cipher letter: waits for the plain letter and notes it as the guess; any other key clears the guess.</summary>
    private void GuessLetter(int cipher)
    {
        BlinkSelectedLetter(cipher);
        var plain = Input.ReadKey() - 'a';
        var isLetter = plain >= 0 && plain < _state.AlphabetLen;
        _state.GuessTable[cipher] = isLetter ? (char)('A' + plain) : CryptoState.NoGuess;
        Output.DoSound(Sounds.LetterSet);
        RedrawGuesses();
    }

    /// <summary>Alt-Q: asks whether to quit over a saved copy of the screen; "Yes" ends the whole game.</summary>
    private void AskToQuit()
    {
        const int yes = 1;
        _world.Graphics.CopyRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, _state.RpBack, 0, 0);
        if (Menus.QuitPrompt(_state.StrQuitConfirm2, 80, 80) == yes)
        {
            CryptoRules.GoodBye(_shared, quit: true);
            // the C library's exit(): the program ends here, whoever started the part takes over
            // DIVERGENCE: D-4 exit() is called without an exit code (a leftover word of the stack goes to DOS); here 0.
            throw new ProgramExitException(0);
        }
        _world.Graphics.CopyRect(_state.RpBack, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
    }

    /// <summary>Alt-P: blanks the screen (the puzzle cannot be studied while paused), shows "PAUSED", and puts the screen back at the next key.</summary>
    private void Pause()
    {
        _world.Graphics.CopyRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, _state.RpBack, 0, 0);
        _world.Graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, Black);
        _state.Menu.MenuPauseMode = true;
        Menus.QuitPrompt(_state.StrPaused, 128, 81);
        Input.ReadKey();
        _world.Graphics.CopyRect(_state.RpBack, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
    }

    /// <summary>The message is decoded: ends the session as a success, fills in every letter, and says so.</summary>
    private void ShowSolution()
    {
        _state.CODEQuit = 1;
        _state.CODESuccess = true;
        for (var position = 0; position < _state.AlphabetLen; position++)
        {
            _state.GuessTable[CryptoRules.CipherLetterOf(_state, position)] = _state.PlainAlphabet[position];
        }
        Output.DoSound(Sounds.HintDone);
        RedrawGuesses();
        Menus.QuitPrompt(_state.StrMessageDecoded, 100, 2);
    }

    /// <summary>
    /// Draws the player's guesses again: the guess column on the left, and the whole text, each cell with its cipher
    /// letter and, on the line below, the guess for it (a blank while there is none). Cells that are no letters show
    /// their character twice.
    /// </summary>
    // orig: CODE 0000:175b redraw_guesses (src/c/code/code_0000_175b.c) matched; tests: CodeOracleTests
    public void RedrawGuesses()
    {
        var port = _state.Rp1;
        var message = _state.MessageText;
        port.BPen = Black;
        for (var cipher = 0; cipher < _state.AlphabetLen; cipher++)
        {
            Output.DrawText(LetterText(_state.GuessTable[cipher]), GuessColumnX, ColumnY(cipher), GuessColor);
        }
        int column = 0, line = 0;
        for (var i = 0; i < message.Length; i++)
        {
            int shown = message[i];
            int below = message[i];
            if (IsTableCharacter(message[i]))
            {
                shown = _state.CipherAlphabet[shown - 'A'];
                below = _state.GuessTable[shown - 'A'];
            }
            port.FontNr = CipherFont;
            _world.Graphics.FillRect(port, CellX(column), CellY(line), CellWidth, 7, Black);
            Output.WaitVsync(0);
            Output.DrawTextFlag0(LetterText(shown), CellX(column), CellY(line), ScreenGreen);
            port.FontNr = SmallFont;
            Output.DrawText(LetterText(below), CellX(column), CellY(line) + GuessBelowLetter, GuessColor);
            column++;
            if (column >= TextColumns)
            {
                column = 0;
                line++;
            }
        }
    }

    /// <summary>
    /// Brings the timer up to date and draws it in the screen's top right corner as hours:minutes:seconds. Whole
    /// minutes move from the seconds counter to the minutes counter (which is what the campaign is charged).
    /// </summary>
    // orig: CODE 0000:192e draw_timer (src/c/code/code_0000_192e.c) matched; tests: CodeOracleTests
    public void DrawTimer()
    {
        _state.CODERtime += (short)(_state.ElapsedSeconds / 60);
        _state.ElapsedSeconds %= 60;
        int hours = _state.CODERtime / 60, minutes = _state.CODERtime % 60, seconds = _state.ElapsedSeconds;

        // the timer's text keeps its colons; only the six digits are replaced
        var text = _state.TimerText.ToCharArray();
        (text[0], text[1]) = (Digit(hours / 10), Digit(hours % 10));
        (text[3], text[4]) = (Digit(minutes / 10), Digit(minutes % 10));
        (text[6], text[7]) = (Digit(seconds / 10), Digit(seconds % 10));
        _state.TimerText = new string(text);

        _state.Rp1.FontNr = SmallFont;
        _state.Rp1.BPen = ScreenGreen;
        Output.DrawText(_state.TimerText, 256, 1, FrameColor);
    }

    /// <summary>The character for a digit of the timer: '0' plus the value, as one byte (a value that is no digit, from 100 hours on, gives another character, as in the original).</summary>
    private static char Digit(int value) => (char)(byte)('0' + value);

    /// <summary>A one-character text for drawing a single letter; the character is one byte.</summary>
    private static string LetterText(int character) => ((char)(byte)character).ToString();

    /// <summary>A capital letter A..Z.</summary>
    private static bool IsLetter(char character) => character is >= 'A' and <= 'Z';

    /// <summary>A capital letter or one of the two characters after 'Z', for which the letter tables also have entries.</summary>
    private static bool IsTableCharacter(char character) => character is >= 'A' and <= '\\';

    /// <summary>The top of a cipher letter's row in the letter column.</summary>
    private static int ColumnY(int cipher) => ColumnTop + ColumnRowHeight * cipher;

    /// <summary>The left edge of a cell of the cipher text.</summary>
    private static int CellX(int column) => TextLeft + CellWidth * column;

    /// <summary>The top of a line of the cipher text.</summary>
    private static int CellY(int line) => TextTop + TextLineHeight * line;
}
