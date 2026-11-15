using CovertAction.Core;
using CovertAction.Core.Electronics;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Electronics;

/// <summary>
/// The electronics minigame on the screen (the original's BUG program): the circuit board the player rewires to tap
/// phone lines or to install a car tracer. The rules of the board are <see cref="CircuitRules"/>; this class holds the
/// functions that draw it and read the player's keys.
/// </summary>
/// <remarks>
/// Ported so far: the pieces of the board (wires, chip slots, the clock), the evaluation of the board with the current
/// running along the wires and the result, building the board and the player's moves
/// (<c>ElectronicsWorkbench.Board.cs</c>), and what a wiretap teaches the player
/// (<c>ElectronicsWorkbench.Intelligence.cs</c>) and its picture loading, and the part's start and end (<c>bug_init</c>,
/// <c>ElectronicsWorkbench.Start.cs</c>).
/// </remarks>
public sealed partial class ElectronicsWorkbench
{
    /// <summary>The clock's place on the screen: the dark field under the board, and the text inside it.</summary>
    private const int ClockX = 0x100, ClockY = 0xb8, ClockWidth = 0x30, ClockHeight = 9, ClockTextX = 0x104, ClockTextY = 0xb9;

    /// <summary>Colours of the clock: grey letters on black.</summary>
    private const int Black = 0, Grey = 7;

    private const int SecondsPerHour = 3600, SecondsPerMinute = 60;

    private readonly ElectronicsState state;
    private readonly Boundaries world;
    private readonly SharedState shared;

    /// <summary>The part on its variables, the hand-off block and the outside world.</summary>
    /// <param name="state">The part's variables.</param>
    /// <param name="shared">The hand-off block (Alt-Q writes into it).</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="behaviour">Which of the original's bugs to keep (the library's text reader has one).</param>
    public ElectronicsWorkbench(ElectronicsState state, SharedState shared, Boundaries world, OriginalBehaviour behaviour)
    {
        this.state = state;
        this.world = world;
        this.shared = shared;
        Output = new LibraryOutput(state, world);
        Input = new LibraryInput(state, world);
        Menus = new LibraryMenus(state, shared, world, Output, Input);
        Text = new LibraryText(state, state, world, Output, state.Random, behaviour);
        Screens = new LibraryScreens(state, state, world, Output, Input, Text);
        Pictures = new LibraryPictures(world);
        HandOff = new LibraryHandOff(state, shared, world, (text, x, y) => Menus.QuitPrompt(text, x, y));
    }

    /// <summary>The library's routines that take the campaign over from the hand-off block and give it back.</summary>
    public LibraryHandOff HandOff { get; }

    /// <summary>The library's routines that load pictures onto pages.</summary>
    public LibraryPictures Pictures { get; }

    /// <summary>The output routines every part has, on this part's variables.</summary>
    public LibraryOutput Output { get; }

    /// <summary>The input routines every part has, on this part's variables.</summary>
    public LibraryInput Input { get; }

    /// <summary>The message box, disk prompt and animation routines every part has.</summary>
    public LibraryMenus Menus { get; }

    /// <summary>The library's routines that read, measure and draw texts.</summary>
    public LibraryText Text { get; }

    /// <summary>The library's screens of the campaign (faces, photos, waiting for a key).</summary>
    public LibraryScreens Screens { get; }

    /// <summary>Shows a message box with the text at a place of the screen and waits for the player to close it.</summary>
    /// <param name="text">The text; lines end in '\n'.</param>
    /// <param name="x">Left edge of the box.</param>
    /// <param name="y">Top edge of the box.</param>
    // orig: BUG 00e8:15ff message_box (src/c/bug/bug_00e8_15ff.c) matched; tests: BugOracleTests
    public void MessageBox(string text, int x, int y) => Menus.QuitPrompt(text, x, y);

    /// <summary>Shows a message box with the text at the box routine's own place (x and y 0).</summary>
    /// <param name="text">The text; lines end in '\n'.</param>
    // orig: BUG 00e8:0288 message_box_default (src/c/bug/bug_00e8_0288.c) matched; tests: BugOracleTests
    public void MessageBoxDefault(string text) => Menus.QuitPrompt(text, 0, 0);

    /// <summary>The page bugs.pic is loaded onto to be cut up (the same page the sheets of faces use).</summary>
    private const int SheetPage = 2;

    /// <summary>The wire pieces on bugs.pic: two rows of sixteen, 8 x 8 pixels, from row 0x10; they fill entries 0..0x1f.</summary>
    private const int WireRows = 2, WiresPerRow = 16, WireSize = 8, WiresY = 0x10;

    /// <summary>The chips on bugs.pic: twelve, 16 x 16 pixels, side by side along the top; they fill entries 0x20..0x2b.</summary>
    private const int Chips = 12, ChipSize = 16, FirstChipImage = 0x20;

    /// <summary>
    /// Loads the pictures of the part: the two sheets of faces into <see cref="ElectronicsState.Sprite"/>, then the
    /// puzzle's sheet, bugs.pic, cut into <see cref="ElectronicsState.PuzzleImages"/> (the wire pieces and the chips).
    /// </summary>
    // orig: BUG 00e8:02dc load_bugs_pic (src/c/bug/bug_00e8_02dc.c) matched; tests: BugOracleTests
    public void LoadBugsPic()
    {
        world.Graphics.BeginImageCuts();
        Pictures.LoadFacesPic(state.Sprite, state.FacesWomenName, state.FacesMenName);
        world.Graphics.SetColorMaps(state.BoardColorMap, state.BoardColorMap);
        Pictures.LoadPictureSwapped(state.BugsSheetName, SheetPage);
        for (var row = 0; row < WireRows; row++)
        {
            for (var i = 0; i < WiresPerRow; i++)
            {
                state.PuzzleImages[row * WiresPerRow + i] = world.Graphics.CutImage(SheetPage, i * WireSize, row * WireSize + WiresY, WireSize, WireSize);
            }
        }
        for (var i = 0; i < Chips; i++)
        {
            state.PuzzleImages[FirstChipImage + i] = world.Graphics.CutImage(SheetPage, i * ChipSize, 0, ChipSize, ChipSize);
        }
        world.Graphics.EndImageCuts();
    }

    /// <summary>Draws one of the puzzle's images (a piece of wire, a chip) on the screen.</summary>
    /// <param name="n">The image, 0..0x2b (see <see cref="ElectronicsState.PuzzleImages"/>).</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    // orig: BUG 00e8:02bc draw_puzzle_image (src/c/bug/bug_00e8_02bc.c) matched; tests: BugOracleTests
    public void DrawPuzzleImage(int n, int x, int y) => world.Graphics.DrawImagePort(state.Rp1, x, y, state.PuzzleImages[n]);

    /// <summary>
    /// Draws the wire pieces of one board cell, one image per direction that has a piece. In a powered cell the pieces
    /// are bright, and the piece pointing back the way the current came from is drawn in its own bright variant.
    /// </summary>
    /// <param name="col">The cell's column, 0..39.</param>
    /// <param name="row">The cell's row, 0..24.</param>
    /// <param name="dir">The direction the current came in by (0..7, north first, clockwise); only its low byte counts.</param>
    // orig: BUG 00e8:0ff1 draw_wire_cell (src/c/bug/bug_00e8_0ff1.c) matched; tests: BugOracleTests
    public void DrawWireCell(int col, int row, int dir)
    {
        const int cellSize = 8, poweredImages = 0x10, poweredBackImages = 0x18;
        var cell = state.Board[col][row];
        // C: the direction is a char: (dir + 4) & 7, taken as a byte, is the direction pointing back
        var back = (byte)(dir + 4) & 7;
        for (var direction = 0; direction < ElectronicsState.Directions; direction++)
        {
            if ((cell & 1 << direction) == 0)
            {
                continue;
            }
            var image = (cell & (short)Wire.Powered) == 0
                ? direction
                : (back == direction ? poweredImages : poweredBackImages) + direction;
            DrawPuzzleImage(image, col * cellSize, row * cellSize);
        }
    }

    /// <summary>
    /// Draws a chip slot of the board: its frame, highlighted when the cursor is on it, and the chip's picture unless
    /// <paramref name="chip"/> is -1 (an empty or hidden slot).
    /// </summary>
    /// <param name="chip">The chip word (its type is what is drawn), or -1 for none.</param>
    /// <param name="x">The chip picture's left edge.</param>
    /// <param name="y">The chip picture's top edge.</param>
    /// <param name="selected">Whether the cursor is on the slot.</param>
    // orig: BUG 00e8:14e8 draw_chip_slot (src/c/bug/bug_00e8_14e8.c) matched; tests: BugOracleTests
    public void DrawChipSlot(int chip, int x, int y, bool selected)
    {
        // the two slot frames on the picture sheet, plain and highlighted, 14 x 19 pixels
        const int frameX = 1, selectedFrameX = 0x11, frameY = 0x37, frameWidth = 0xe, frameHeight = 0x13;
        world.Graphics.CopyRect(state.RpSprite, selected ? selectedFrameX : frameX, frameY, frameWidth, frameHeight, state.Rp1, x + 1, y - 1);
        if (chip != -1)
        {
            DrawPuzzleImage((chip & ElectronicsState.ChipTypeMask) + ElectronicsState.FirstChipImage, x, y);
        }
    }

    /// <summary>
    /// Lets the current run along a wire of the board (or switches it off): from a cell, entered from a direction,
    /// every cell the wire passes gets or loses its powered mark and is drawn again. Where the wire branches, each
    /// further branch is followed first (recursively), then the first one. While the puzzle runs, each step also
    /// takes a tick of the clock and waits for the next frame, so the player sees the current flow.
    /// </summary>
    /// <param name="col">The cell's column, 0..39.</param>
    /// <param name="row">The cell's row, 0..24.</param>
    /// <param name="dir">The direction the current moves in when it enters the cell (0..7, north first, clockwise).</param>
    /// <param name="powered">Whether the wire now carries current.</param>
    // orig: BUG 00e8:0efd power_wire (src/c/bug/bug_00e8_0efd.c) matched; tests: BugOracleTests
    public void PowerWire(int col, int row, int dir, bool powered)
    {
        const int deadEnd = -1;
        while (dir != deadEnd)
        {
            var cell = state.Board[col][row];
            if (cell == 0)
            {
                return;
            }
            state.Board[col][row] = (short)((cell & 0xff) + (powered ? (int)Wire.Powered : 0));
            DrawWireCell(col, row, dir);

            // C: the direction is a char: (dir + 4) & 7, taken as a byte, is the way back
            var back = (byte)(dir + 4) & 7;
            var next = deadEnd;
            for (var direction = 0; direction < ElectronicsState.Directions; direction++)
            {
                if (direction == back || (cell & 1 << direction) == 0)
                {
                    continue;
                }
                if (next == deadEnd)
                {
                    next = direction;
                }
                else
                {
                    PowerWire(state.BUGMoveX[direction] + col, state.BUGMoveY[direction] + row, direction, powered);
                }
            }
            dir = next;
            // C: at a dead end the original steps by the words in front of the step tables (BUGMoveX[-1]) and reads
            // the cell there; the loop ends on the direction whatever the cell holds, so the step is left out
            if (next != deadEnd)
            {
                col += state.BUGMoveX[next];
                row += state.BUGMoveY[next];
            }
            if (state.TickCount != 0 && state.TimeLeft > 1)
            {
                AdvanceTimer(1);
                Output.WaitVsync(1);
            }
        }
    }

    /// <summary>
    /// Runs the puzzle's clock: each tick moves the powered wires' flicker on by one palette, and every eighth takes a
    /// second off the time left and draws the clock. When the time runs out the board is evaluated a last time,
    /// which ends the puzzle.
    /// </summary>
    /// <param name="ticks">The ticks to run.</param>
    // orig: BUG 00e8:1077 advance_timer (src/c/bug/bug_00e8_1077.c) matched; tests: BugOracleTests
    public void AdvanceTimer(int ticks)
    {
        const int ticksPerSecond = 8, flickerPalettes = 4;
        for (var tick = 0; tick < ticks; tick++)
        {
            state.TickCount++;
            if (state.TickCount % ticksPerSecond == 0)
            {
                state.TimeLeft--;
                DrawTimer();
                if (state.TimeLeft == 0)
                {
                    // C: the original calls bug_status_message without its argument: what it takes for "draw" is the
                    // word on top of advance_timer's stack, this loop's counter
                    BugStatusMessage(tick != 0);
                }
            }
            SelectPalette(state.TickCount & (flickerPalettes - 1));
        }
    }

    /// <summary>
    /// Evaluates the board after a change and tells the player the result. Draws the carried chip in its field, works
    /// out the signals (<see cref="CircuitRules.PropagateSignals"/>) and, when <paramref name="draw"/> is set, lets the
    /// current run along every wire whose signal changed and draws the ten terminals: a dead phone counts, a live alarm
    /// counts against the player. Each new best count of a wiretap reveals intelligence (outside practice); the car
    /// tracer counts 20 per cent a phone. The puzzle ends, with a message box, on an alarm, when the time is out or
    /// when enough phones are dead.
    /// </summary>
    /// <param name="draw">Whether to draw the current and the terminals and count them (the build of the board does not).</param>
    // orig: BUG 00e8:09cc bug_status_message (src/c/bug/bug_00e8_09cc.c) matched; tests: BugOracleTests
    public void BugStatusMessage(bool draw)
    {
        const int carriedChipX = 0x98, carriedChipY = 0xb1, carriedFieldColor = 8;
        // the wire from the supply below the board into the first column, entered going north
        const int supplyCellX = 7, supplyCellY = 0x14, north = 0, east = 2;

        world.Graphics.FillRect(state.Rp1, carriedChipX + 2, carriedChipY, 0xc, 0x10, carriedFieldColor);
        DrawPuzzleImage((state.CarriedChip & ElectronicsState.ChipTypeMask) + ElectronicsState.FirstChipImage, carriedChipX, carriedChipY);
        if (draw && state.TickCount == 0)
        {
            PowerWire(supplyCellX, supplyCellY, north, true);
        }
        CircuitRules.PropagateSignals(state, draw ? (x, y, signal) => PowerWire(x, y, east, signal != 0) : null);
        if (draw)
        {
            DrawTerminals();
        }

        state.Rp1.BPen = 1;
        if (state.AlarmCount != 0)
        {
            SelectPalette(0);
            Output.DoSound(AlarmSound);
            state.MessageText.Text = state.StrAlarm;
            Menus.QuitPrompt(state.MessageText, ResultBoxX, ResultBoxY);
            state.ResultPhones = 0;
            state.DoneFlag = 1;
        }
        if (CircuitRules.PhoneTarget(state) > state.PhonesOk && state.TimeLeft != 0)
        {
            return;
        }
        ShowResult();
    }

    /// <summary>The sound of a phone line going dead, of an alarm, and of a finished board.</summary>
    private const int PhoneCutSound = 0x1d, AlarmSound = 0x1e, SuccessSound = 0x2e;

    /// <summary>Where the result's message box stands.</summary>
    private const int ResultBoxX = 0x64, ResultBoxY = 0x51;

    /// <summary>
    /// The ten terminals right of the board, top to bottom, alternately in two columns: each drawn as it now is (an
    /// alarm off or ringing, a phone live or dead), the dead phones and the live alarms counted.
    /// </summary>
    private void DrawTerminals()
    {
        const int terminalX = 0x10a, oddTerminalShift = 0x18, terminalSpacing = 0x10;
        const int pictureY = 0x20, pictureWidth = 0xc, pictureHeight = 0x10;
        // the pictures on the sheet, left edges: alarm ringing / quiet, phone live / dead, tracer phone live / dead
        const int alarmOn = 0x12, alarmOff = 2, phoneOn = 0x22, phoneOff = 0x32, tracerOn = 0x42, tracerOff = 0x52;
        state.PhonesOk = 0;
        for (var terminal = 0; terminal < ElectronicsState.Lines; terminal++)
        {
            var x = terminalX + (terminal % 2 == 1 ? oddTerminalShift : 0);
            var y = terminal * terminalSpacing;
            var live = state.Pin[ElectronicsState.PinRows - 1][terminal] != 0;
            void Draw(int pictureX) => world.Graphics.CopyRect(state.RpSprite, pictureX, pictureY, pictureWidth, pictureHeight, state.Rp1, x, y);

            if (state.TerminalFlags[terminal])
            {
                Draw(live ? alarmOn : alarmOff);
                if (live)
                {
                    state.AlarmCount++;
                }
                continue;
            }
            if (state.PhonesOk == state.BestPhones && !live)
            {
                Output.DoSound(PhoneCutSound);
            }
            if (state.Mode == ElectronicsMode.PhoneTaps)
            {
                Draw(live ? phoneOn : phoneOff);
                if (!live && CountDeadPhone())
                {
                    if ((state.Status & Options.Practice) == 0)
                    {
                        const int newBestStrength = 8;
                        WiretapRevealClue(newBestStrength);
                    }
                }
            }
            if (state.Mode == ElectronicsMode.CarTracer)
            {
                Draw(live ? tracerOn : tracerOff);
                if (!live && CountDeadPhone())
                {
                    ComposeTracerProgress();
                }
            }
        }
    }

    /// <summary>Counts a dead phone; says whether the count is a new best (and records it).</summary>
    private bool CountDeadPhone()
    {
        state.PhonesOk++;
        if (state.PhonesOk <= state.BestPhones)
        {
            return false;
        }
        state.BestPhones = state.PhonesOk;
        return true;
    }

    /// <summary>
    /// Writes the car tracer's progress, "Car tracer 40% complete", into the text buffer, between saving the screen
    /// and putting it back.
    /// </summary>
    private void ComposeTracerProgress()
    {
        const int percentPerPhone = 20;
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        // the original writes the progress and shows nothing: there is no box between saving and restoring the
        // screen (a quirk proposed for docs/original-bugs.md); the text stays in the buffer
        state.MessageText.Text = state.StrCarTracer + (percentPerPhone * state.PhonesOk) + state.StrPercentComplete;
        world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
    }

    /// <summary>
    /// The end of the puzzle: the message box with its result ("Phone taps completed.", "Car tracer installed." or
    /// "Time expired." with the phones cut off, then the alarms), the phones reported to the campaign, the part done.
    /// A wiretap finished in time first reveals intelligence by the campaign's difficulty (outside practice).
    /// </summary>
    private void ShowResult()
    {
        var text = state.MessageText;
        if (state.TimeLeft != 0)
        {
            if (state.Mode == ElectronicsMode.PhoneTaps)
            {
                if ((state.Status & Options.Practice) == 0)
                {
                    WiretapRevealClue(C16.Int(state.BaseLevel << 4));
                }
                text.Text = state.StrTapsCompleted;
            }
            else
            {
                text.Text = state.StrTracerInstalled;
            }
            Output.DoSound(SuccessSound);
        }
        else
        {
            text.Text = state.StrTimeExpired;
            if (state.Mode == ElectronicsMode.PhoneTaps)
            {
                text.Text += state.PhonesOk + state.StrPhonesTapped;
            }
        }
        text.Text += (state.AlarmCount != 0 ? state.AlarmCount.ToString() : state.StrNo) + state.StrAlarms;
        state.Rp1.BPen = 0xf;
        Menus.QuitPrompt(text, ResultBoxX, ResultBoxY);
        state.ResultPhones = state.PhonesOk;
        state.DoneFlag = 1;
    }

    /// <summary>The screen's size in pixels.</summary>
    private const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>Draws the time left as "hh:mm:ss" in its field under the board.</summary>
    // orig: BUG 00e8:153e draw_timer (src/c/bug/bug_00e8_153e.c) matched; tests: BugOracleTests
    // Other code under the same name: CODE 0000:1a7a (not this method; docs/source-map/shared-functions.csv).
    public void DrawTimer()
    {
        var hours = state.TimeLeft / SecondsPerHour;
        var rest = state.TimeLeft - SecondsPerHour * hours;
        var minutes = rest / SecondsPerMinute;
        var seconds = rest - SecondsPerMinute * minutes;
        var text = state.ClockStr.ToCharArray();
        if (text.Length < "hh:mm:ss".Length)
        {
            throw new InvalidOperationException($"the clock text \"{state.ClockStr}\" is shorter than hh:mm:ss");
        }
        (text[0], text[1]) = TwoDigits(hours);
        (text[3], text[4]) = TwoDigits(minutes);
        (text[6], text[7]) = TwoDigits(seconds);
        state.ClockStr = new string(text);

        world.Graphics.FillRect(state.Rp1, ClockX, ClockY, ClockWidth, ClockHeight, Black);
        state.Rp1.BPen = Black;
        Output.DrawText(state.ClockStr, ClockTextX, ClockTextY, Grey);
    }

    /// <summary>The tens and the units of a number as characters, each a byte '0' plus the digit (a negative time gives the characters before '0').</summary>
    private static (char Tens, char Units) TwoDigits(int value) => ((char)(byte)(value / 10 + '0'), (char)(byte)(value % 10 + '0'));

    /// <summary>
    /// Switches the display to one of the part's palettes. The clock steps through palettes 0..3 (tick by tick) to make
    /// the current in the powered wires flicker; an alarm shows palette 0; palette 4 is set when the part ends.
    /// </summary>
    /// <param name="n">The palette, 0..4.</param>
    // orig: BUG 00e8:186b select_palette (src/c/bug/bug_00e8_186b.c) matched; tests: BugOracleTests
    public void SelectPalette(int n) => world.Graphics.SetPalette(state.Palettes[n]);

    /// <summary>
    /// The hook the part calls to stop its sound before it ends (before leaving for DOS on Alt-Q, and at the end of the
    /// puzzle). Empty in the original: the sound driver needs no shutting down here.
    /// </summary>
    // orig: BUG 00e8:020d shutdown_sound (src/c/bug/bug_00e8_020d.c) matched; tests: BugOracleTests
    public static void ShutdownSound()
    {
    }
}
