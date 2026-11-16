using CovertAction.Core.Electronics;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Electronics;

/// <summary>Setting up the board and the player's moves on it.</summary>
public sealed partial class ElectronicsWorkbench
{
    /// <summary>The cursor's slot when the board is ready: the middle of the board.</summary>
    private const int StartCol = 3, StartRow = 2;

    /// <summary>The last chip column and row the cursor can reach (the last column, always shown, cannot be swapped).</summary>
    private const int LastCursorCol = 5, LastCursorRow = 4;

    /// <summary>The sound of a chip swapped.</summary>
    private const int SwapSound = 0x1c;

    /// <summary>
    /// Sets up the puzzle's board on the background picture: the wires the picture shows are read into the board, the
    /// wiring between the chip columns is laid out at random for the difficulty, chips are drawn until the board is a
    /// fair puzzle (<see cref="CircuitRules"/>), some are shown and some fixed, the fixed ones get a grey frame and the
    /// shown ones their pictures; then the player gets a random chip to carry, the current runs through the board for
    /// the first time and the cursor is put in the middle.
    /// </summary>
    // orig: BUG 00e8:0395 build_circuit_board (src/c/bug/bug_00e8_0395.c) matched; tests: BugOracleTests
    public void BuildCircuitBoard()
    {
        // C: the original first sets the difficulty to the word behind RpSprite, which is the difficulty itself
        state.ChipTypeLimit = (short)GameLibrary.Clamp(2 * state.Diff + 6, 0, ElectronicsState.ChipTypes - 1);
        ReadWiresFromPicture();
        CircuitRules.LayOutWires(state);
        CircuitRules.ResetSignals(state);
        do
        {
            CircuitRules.PlaceRandomChips(state);
            BugStatusMessage(false);
        }
        while (!CircuitRules.AssignTerminals(state));
        CircuitRules.RevealChips(state);
        CircuitRules.LockChips(state);
        DrawBoardChips();
        state.CarriedChip = CircuitRules.RandomChipType(state);
        CircuitRules.ResetSignals(state);
        BugStatusMessage(true);
        state.CursorCol = StartCol;
        state.CursorRow = StartRow;
        DrawSlot(state.CursorCol, state.CursorRow, selected: true);
    }

    /// <summary>
    /// Reads the wires of the background picture into the board: a cell has a piece of wire in a direction when the
    /// pixel two steps from its centre that way has the wire colour (3 on CGA, else 10).
    /// </summary>
    private void ReadWiresFromPicture()
    {
        const int cellSize = 8, cellCentre = 4, screenPage = 0;
        var wireColour = state.CGA ? 3 : 10;
        for (var x = 0; x < ElectronicsState.BoardWidth; x++)
        {
            for (var y = 0; y < ElectronicsState.BoardHeight; y++)
            {
                var centreX = cellSize * x + cellCentre;
                var centreY = cellSize * y + cellCentre;
                var cell = 0;
                for (var direction = 0; direction < ElectronicsState.Directions; direction++)
                {
                    if (wireColour == world.Graphics.GetPixel(screenPage, 2 * state.BUGMoveX[direction] + centreX, 2 * state.BUGMoveY[direction] + centreY))
                    {
                        cell += 1 << direction;
                    }
                }
                state.Board[x][y] = (short)cell;
            }
        }
    }

    /// <summary>Draws the chips of the new board: a grey frame around each fixed chip, the picture of each shown one.</summary>
    private void DrawBoardChips()
    {
        const int grey = 8, frameWidth = 0x12, frameHeight = 0x17;
        for (var col = 0; col < ElectronicsState.Columns; col++)
        {
            for (var row = 0; row < ElectronicsState.Rows; row++)
            {
                var chip = state.BUGchip[col][row];
                var (x, y) = SlotPosition(col, row);
                if ((chip & ElectronicsState.ChipFixed) != 0)
                {
                    // the slot's frame (colours 2 and 3) turns grey, with a grey bar down each side
                    world.Graphics.RecolorRect(state.Rp1, x - 1, y - 3, frameWidth, frameHeight, 2, grey);
                    world.Graphics.RecolorRect(state.Rp1, x - 1, y - 3, frameWidth, frameHeight, 3, grey);
                    Output.DrawLine(x, y - 3, x, y + 0x10, grey);
                    Output.DrawLine(x + 0xf, y - 3, x + 0xf, y + 0x10, grey);
                }
                if ((chip & ElectronicsState.ChipShown) != 0)
                {
                    DrawChipSlot(chip, x, y, false);
                }
            }
        }
    }

    /// <summary>
    /// One step of the puzzle: the clock ticks, then one key or joystick move is handled. Esc gives up (the phones cut
    /// off so far are the result); Return or Space swaps the carried chip with the chip under the cursor (not a fixed
    /// one; a hidden one is shown for ten ticks first) and evaluates the board; the arrows and the keys around them
    /// move the cursor; Alt-Q asks whether to quit the game, Alt-S switches the sound, Alt-P pauses. Then it waits 5
    /// frames (20 after a joystick move).
    /// </summary>
    // orig: BUG 00e8:10bc puzzle_input_step (src/c/bug/bug_00e8_10bc.c) matched; tests: BugOracleTests
    public void PuzzleInputStep()
    {
        const int none = -1, firstButton = 0, keyFrames = 5, joystickFrames = 0x14;
        AdvanceTimer(1);
        var key = none;
        var joystickMoved = false;
        if ((state.Status & Options.Joystick) != 0)
        {
            // C: the original passes two locals it has not set; the routine only writes them
            int dx = 0, dy = 0;
            key = Input.ReadJoystickDir(ref dx, ref dy);
            if (Input.JoystickButtonDown(firstButton))
            {
                key = Keys.Return;
                Input.FlushInput();
            }
            if (key != none)
            {
                joystickMoved = true;
            }
        }
        // C: the driver is asked whether a key waits even when the joystick has given one
        if (!world.Input.NoKeyWaiting() || key != none)
        {
            if (key == none)
            {
                key = Input.ReadKey();
            }
            HandleKey(key);
        }
        else if (Input.PointerOn)
        {
            // DIVERGENCE: D-634 with the mouse option on, the pointer on a slot moves the cursor there; a left click on one
            // swaps the chip (Return), a right click gives up (Esc).
            HandlePointer(Input.ReadPointer(SlotAt));
        }
        Output.WaitVsync(joystickMoved ? joystickFrames : keyFrames);
    }

    /// <summary>The cursor keys besides <see cref="Keys.Up"/> and <see cref="Keys.Down"/>, with the joystick's direction codes 1 (up) to 8, clockwise.</summary>
    private const int KeyPgUp = 0x4900, KeyRight = 0x4d00, KeyPgDn = 0x5100, KeyEnd = 0x4f00, KeyLeft = 0x4b00, KeyHome = 0x4700;

    private void HandleKey(int key)
    {
        switch (key)
        {
            case Keys.Esc:
                state.ResultPhones = state.PhonesOk;
                state.DoneFlag = 1;
                break;
            case Keys.Return or Keys.Space:
                SwapChip();
                break;
            case 1 or Keys.Up:
                MoveCursor(0, -1);
                break;
            case 2 or KeyPgUp:
                MoveCursor(1, -1);
                break;
            case 3 or KeyRight:
                MoveCursor(1, 0);
                break;
            case 4 or KeyPgDn:
                MoveCursor(1, 1);
                break;
            case 5 or Keys.Down:
                MoveCursor(0, 1);
                break;
            case 6 or KeyEnd:
                MoveCursor(-1, 1);
                break;
            case 7 or KeyLeft:
                MoveCursor(-1, 0);
                break;
            case 8 or KeyHome:
                MoveCursor(-1, -1);
                break;
            case Keys.AltQ:
                AskToQuit();
                break;
            case Keys.AltS:
                if ((state.Status & Options.Sound) != 0)
                {
                    Output.DoSound(0);
                }
                state.Status ^= Options.Sound;
                break;
            case Keys.AltP:
                Pause();
                break;
        }
    }

    /// <summary>The slots the pointer can take: chips are 32 pixels apart, a slot's cell is the 32 by 32 pixels around its picture.</summary>
    private static readonly HitArea SlotGrid = new(SlotPosition(0, 0).X - 8, SlotPosition(0, 0).Y - 8, 32, 32);

    /// <summary>The mouse's hit test: the slot (row * 6 + column) under a screen pixel, -1 for none.</summary>
    public static int SlotAt(int x, int y)
        => PointerHit.Cell(x, y, SlotGrid, LastCursorCol + 1, LastCursorRow + 1) is { } cell ? cell.Row * (LastCursorCol + 1) + cell.Column : -1;

    /// <summary>What a mouse action does: the keys it stands for (a move onto a slot moves the cursor, a click on a slot swaps, a right click is Esc).</summary>
    private void HandlePointer(PointerEvent pointed)
    {
        switch (pointed.Action)
        {
            case PointerAction.Cancel:
                HandleKey(Keys.Esc);
                break;
            case PointerAction.Point or PointerAction.Accept when pointed.Choice >= 0:
                MoveCursorTo(pointed.Choice % (LastCursorCol + 1), pointed.Choice / (LastCursorCol + 1));
                if (pointed.Action == PointerAction.Accept)
                {
                    HandleKey(Keys.Return);
                }
                break;
        }
    }

    /// <summary>Puts the cursor on a slot, as the cursor keys would reach it.</summary>
    private void MoveCursorTo(int col, int row)
    {
        if (col != state.CursorCol || row != state.CursorRow)
        {
            MoveCursor(col - state.CursorCol, row - state.CursorRow);
        }
    }

    /// <summary>Swaps the carried chip with the chip under the cursor, unless that one is fixed, and evaluates the board.</summary>
    private void SwapChip()
    {
        const int peekTicks = 10;
        int col = state.CursorCol, row = state.CursorRow;
        var chip = state.BUGchip[col][row];
        if ((chip & ElectronicsState.ChipFixed) != 0)
        {
            return;
        }
        var (x, y) = SlotPosition(col, row);
        if ((chip & ElectronicsState.ChipShown) == 0)
        {
            // a hidden chip is shown for a moment before it is taken
            DrawChipSlot(chip, x, y, true);
            AdvanceTimer(peekTicks);
        }
        var carried = state.CarriedChip;
        state.CarriedChip = (short)(state.BUGchip[col][row] & ElectronicsState.ChipTypeMask);
        state.BUGchip[col][row] = (short)(carried + ElectronicsState.ChipShown);
        DrawChipSlot(carried + ElectronicsState.ChipShown, x, y, true);
        Output.DoSound(SwapSound);
        BugStatusMessage(true);
    }

    /// <summary>Moves the cursor by a step, within the chips it can reach, and draws the slot it leaves and the one it reaches.</summary>
    private void MoveCursor(int dx, int dy)
    {
        DrawSlot(state.CursorCol, state.CursorRow, selected: false);
        state.CursorCol = (short)GameLibrary.Clamp(dx + state.CursorCol, 0, LastCursorCol);
        state.CursorRow = (short)GameLibrary.Clamp(dy + state.CursorRow, 0, LastCursorRow);
        DrawSlot(state.CursorCol, state.CursorRow, selected: true);
    }

    /// <summary>Draws a chip slot of the board: with its chip when the chip is shown, empty otherwise.</summary>
    private void DrawSlot(int col, int row, bool selected)
    {
        var chip = state.BUGchip[col][row];
        var (x, y) = SlotPosition(col, row);
        DrawChipSlot((chip & ElectronicsState.ChipShown) != 0 ? chip : -1, x, y, selected);
    }

    /// <summary>Where a chip's picture lies on the screen: chips are 32 pixels apart, the first at (40, 8).</summary>
    private static (int X, int Y) SlotPosition(int col, int row) => (32 * col + 0x28, 32 * row + 8);

    /// <summary>Alt-Q: asks over a saved copy of the screen whether to quit; "Yes" ends the whole game.</summary>
    private void AskToQuit()
    {
        const int yes = 1, boxX = 0x50, boxY = 0x51;
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        state.Rp1.BPen = 0xf;
        if (Menus.QuitPrompt(state.StrQuitConfirm, boxX, boxY) == yes)
        {
            ShutdownSound();
            // the first byte of the hand-off block (reached through the far pointer at 0000:04f0): the game is over
            shared.NextModule = -1;
            // the C library's exit(): the program ends here, whoever started the part takes over
            // DIVERGENCE: D-4 exit() is called without an exit code (a leftover word of the stack goes to DOS); here 0.
            throw new ProgramExitException(0);
        }
        world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
    }

    /// <summary>Alt-P: blanks the screen (the board cannot be studied while paused), shows "PAUSED", and puts the screen back at the next key.</summary>
    private void Pause()
    {
        const int black = 0, boxX = 0x80, boxY = 0x51;
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, black);
        state.Menu.MenuPauseMode = true;
        Menus.QuitPrompt(state.StrPaused, boxX, boxY);
        Input.ReadKey();
        world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
    }
}
