using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;
using CovertAction.Platform;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The save screens: asking for the drive of the saved games, listing the four slots of a drive, choosing a slot to load
/// and writing the game to a slot. The menus part uses them at the title and at the end of a case; the campaign has
/// the same code under other addresses.
/// </summary>
/// <param name="state">The part's campaign, text buffer and save variables.</param>
/// <param name="world">The drivers and the files.</param>
/// <param name="input">The part's key routines.</param>
/// <param name="menus">The part's message box and disk prompt.</param>
/// <param name="loadGame">The part's own routine that reads a save file into the campaign (the path): FINAL's <c>read_env_state</c>, GAME's <c>load_game</c>.</param>
/// <param name="menuChoice">The part's own box (text, left edge, top edge; the answer): FINAL's <c>menu_choice</c>.</param>
/// <param name="writeSaveGame">The part's own routine that makes a save file and writes the campaign to it (the path; whether the file could be made): FINAL's <c>write_save_game</c>.</param>
public sealed class LibrarySaves(ILibrarySaveState state, Boundaries world, LibraryInput input, LibraryMenus menus, Action<string> loadGame, Func<TextBuffer, int, int, int> menuChoice, Func<string, bool> writeSaveGame)
{
    private const int ScreenWidth = 320, ScreenHeight = 200, ScreenBlack = 0, ScreenBackdrop = 5;
    private const int DriveBoxX = 0x50, DriveBoxY = 0x50, NoDiskBoxX = 0x64, NoDiskBoxY = 0x50;
    private const int ListBoxX = 4, ListBoxY = 0x10, LinesPerSlot = 3;
    private const int ReportBoxX = 4, ReportBoxY = 0x80;
    private const int MonthsPerYear = 12, MinutesPerDay = 0x5a0, FirstYear = 1990;
    private const int EscapeKey = 0x1b, ReturnKey = 0x0d, CannotOpen = -1;
    private const int DeniedError = 0x0d, DiskFullError = 0x1c;
    private const int DriveLetterAt = 0, SlotDigitAt = 4, ExtensionAt = 6;

    private SaveScreenState Saves => state.Saves;

    private string Text(SaveText text) => Saves.Texts[(int)text];

    /// <summary>
    /// Asks which drive holds the saved games (loading) or the Save Game disk (saving): A to D, Return to confirm,
    /// Escape to cancel; checks that a disk is in the drive. The drive's letter goes into <see cref="SaveScreenState.SavePath"/>.
    /// </summary>
    /// <returns>The drive, 0 (A) to 3 (D), or -1.</returns>
    // orig: FINAL 0100:a12e ask_save_drive (src/c/final/final_0100_a12e.c) matched; also GAME 00f7:a90c; tests: LibraryOracleTests, GameOracleTests
    public int AskSaveDrive()
    {
        var graphics = world.Graphics;
        // the drive the game runs from is the first answer (DOS counts from 1)
        var drive = world.Files.CurrentDrive() - 1;
        graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBlack);
        int key;
        do
        {
            // the question ends in a blank that the drive's letter replaces
            var question = Text(Saves.Loading ? SaveText.DriveQuestionLoad : SaveText.DriveQuestionSave);
            state.MessageText.Text = question[..^1] + (char)('A' + drive) + Text(SaveText.DriveInstruction) + Text(SaveText.DriveCancel);
            state.Menu.MenuPauseMode = true;
            menuChoice(state.MessageText, DriveBoxX, DriveBoxY);
            key = input.ReadKey();
            if (key is >= 'A' and <= 'D' or >= 'a' and <= 'd')
            {
                drive = char.ToUpperInvariant((char)key) - 'A';
            }
            if (key == EscapeKey)
            {
                drive = -1;
            }
        }
        while (key != ReturnKey && key != EscapeKey);
        graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBlack);
        if (drive != -1 && !LibraryDisks.DriveReady(world.Files, drive))
        {
            var message = Text(SaveText.NoDisk);
            // the letter stands three places from the end: "A.", line break
            state.MessageText.Text = message[..^3] + (char)('A' + drive) + message[^2..];
            menuChoice(state.MessageText, NoDiskBoxX, NoDiskBoxY);
            return -1;
        }
        SetPathChar(DriveLetterAt, (char)('a' + drive));
        return drive;
    }

    /// <summary>
    /// The load screen: asks for the drive, lists the four slots and lets the player choose one; a slot that has no file
    /// is no choice. The game of the slot is read into the campaign.
    /// </summary>
    /// <returns>The slot, 0..3, or -1 when the player gave up or the slot is empty.</returns>
    // orig: FINAL 0100:9c60 select_load_file (src/c/final/final_0100_9c60.c) matched; also GAME 00f7:a43e; tests: LibraryOracleTests, GameOracleTests
    public int SelectLoadFile()
    {
        Saves.Loading = true;
        if (AskSaveDrive() == -1)
        {
            return -1;
        }
        state.MessageText.Text = Text(SaveText.SelectLoadFile);
        Saves.SlotExistsMask = 0;
        for (var slot = 0; slot < SaveScreenState.SlotCount; slot++)
        {
            SetPathChar(SlotDigitAt, (char)('0' + slot));
            if (ListSaveSlot(describe: true))
            {
                Saves.SlotExistsMask |= (short)(1 << slot);
            }
        }
        var choice = menuChoice(state.MessageText, ListBoxX, ListBoxY);
        // each slot is three lines of the menu
        Saves.SlotChoice = (short)(choice >= 0 ? choice / LinesPerSlot : choice);
        // C: the shift count is a char
        if ((Saves.SlotExistsMask & 1 << C16.Char(Saves.SlotChoice)) == 0)
        {
            Saves.SlotChoice = -1;
        }
        if (Saves.SlotChoice != -1)
        {
            SetPathChar(SlotDigitAt, (char)('0' + Saves.SlotChoice));
            if (!ListSaveSlot(describe: false))
            {
                Saves.SlotChoice = -1;
            }
        }
        menus.CheckForDisk(1, 1);
        return Saves.SlotChoice;
    }

    /// <summary>
    /// One save slot, named by <see cref="SaveScreenState.SavePath"/>. Describing it adds its description to the menu
    /// text (the agent's code name, rank, city, date and level, or "(EMPTY)"); otherwise the slot's game is loaded.
    /// </summary>
    /// <param name="describe">True to describe the slot, false to load it.</param>
    /// <returns>Whether the slot exists (always true when loading).</returns>
    // orig: FINAL 0100:9d3b list_save_slot (src/c/final/final_0100_9d3b.c) matched; also GAME 00f7:a519; tests: LibraryOracleTests, GameOracleTests
    // DIVERGENCE: D-105 a save file that does not hold a whole campaign (shorter than its head and its counts' tables, or counts beyond the tables) is listed as an empty slot, so that it cannot be loaded.
    public bool ListSaveSlot(bool describe)
    {
        SetPathExtension(Text(SaveText.ListExtension));
        if (!describe)
        {
            loadGame(Saves.SavePath);
            state.Status &= ~Options.Cleared;
            return true;
        }
        var files = world.Files;
        Saves.SlotFileHandle = (short)files.Open(Saves.SavePath, OpenMode.Binary);
        var exists = Saves.SlotFileHandle != CannotOpen && HoldsWholeCampaign(Saves.SlotFileHandle);
        var text = state.MessageText;
        if (exists)
        {
            // the plot head, which follows the file's head, and the level of play, a word of the head
            files.Seek(Saves.SlotFileHandle, SaveFileCodec.HeaderSize, 0);
            files.Read(Saves.SlotFileHandle, Saves.SlotBlock);
            var head = new Plot();
            CampaignLayouts.Plot.Load(Saves.SlotBlock, head);
            Span<byte> word = stackalloc byte[2];
            files.Seek(Saves.SlotFileHandle, SaveFileCodec.LevelOffset, 0);
            files.Read(Saves.SlotFileHandle, word);
            Saves.SlotLevel = (short)(word[0] | word[1] << 8);
            // the career counts months from January 1990; the plot's clock is in minutes, 1440 to the day
            text.Text += Text(SaveText.SlotNameStart) + head.Handle + Text(SaveText.SlotNameEnd) + Number(head.Rank)
                + Text(SaveText.SlotRankEnd) + head.City.Text + Text(SaveText.ColonSpace)
                + state.MonthNames[head.Ncase % MonthsPerYear] + Text(SaveText.Space) + Number(head.Date / MinutesPerDay + 1)
                + Text(SaveText.CommaSpace) + Number(head.Ncase / MonthsPerYear + FirstYear);
            text.Text += Saves.SlotLevel switch
            {
                0 => Text(SaveText.LevelLocal),
                1 => Text(SaveText.LevelNational),
                2 => Text(SaveText.LevelRegional),
                3 => Text(SaveText.LevelGlobal),
                _ => "",
            } + Text(SaveText.SlotEnd);
        }
        else
        {
            text.Text += Text(SaveText.SlotEmpty);
        }
        files.Close(Saves.SlotFileHandle);
        return exists;
    }

    /// <summary>
    /// The save screen: asks for the drive, lists the four slots and writes the game to the one chosen; then says
    /// whether it was saved, and asks for the game disk again.
    /// </summary>
    // orig: FINAL 0100:9f20 select_save_file (src/c/final/final_0100_9f20.c) matched; also GAME 00f7:a6fe; tests: LibraryOracleTests, GameOracleTests
    // DIVERGENCE: D-101 the original installs a handler for DOS's critical errors around the write (FINAL 0100:0019); the rewrite has none.
    public void SelectSaveFile()
    {
        Saves.Loading = false;
        Saves.SaveError = 0;
        if (AskSaveDrive() != -1)
        {
            state.MessageText.Text = Text(SaveText.SelectSaveFile);
            for (var slot = 0; slot < SaveScreenState.SlotCount; slot++)
            {
                SetPathChar(SlotDigitAt, (char)('0' + slot));
                ListSaveSlot(describe: true);
            }
            state.Menu.MenuStartItem = Saves.SlotChoice;
            var choice = menuChoice(state.MessageText, ListBoxX, ListBoxY);
            if (choice >= 0)
            {
                choice /= LinesPerSlot;
            }
            if (choice != -1)
            {
                Saves.SlotChoice = (short)choice;
                SetPathChar(SlotDigitAt, (char)('0' + choice));
                if (writeSaveGame(Saves.SavePath))
                {
                    state.MessageText.Text = Saves.SaveError == 0 ? Text(SaveText.GameSaved) : Text(SaveText.GameNotSaved);
                    if (Saves.SaveError == DeniedError)
                    {
                        state.MessageText.Text += Text(SaveText.WriteDenied);
                    }
                    if (Saves.SaveError == DiskFullError)
                    {
                        state.MessageText.Text += Text(SaveText.DiskFull);
                    }
                    state.MessageText.Text += Text(SaveText.PressKey);
                    menuChoice(state.MessageText, ReportBoxX, ReportBoxY);
                }
            }
        }
        world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBackdrop);
        menus.CheckForDisk(state.Disk.DefaultDisk, 1);
    }

    /// <summary>A write that remembers DOS's reason when it fails (nothing in the menus part calls it).</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="dosError">The error DOS gives for a failed write (the C library's errno).</param>
    /// <returns>The error code when the write failed, else 0.</returns>
    // orig: FINAL 0100:a111 write_checked (src/c/final/final_0100_a111.c) matched; also GAME 00f7:a8ef; tests: LibraryOracleTests
    // DORMANT: no code of FINAL or GAME calls it. C: the original has no return statement after the test and gives back what is left in AX.
    public int WriteChecked(int handle, ReadOnlySpan<byte> data, int dosError)
    {
        if (world.Files.Write(handle, data) == -1)
        {
            return Saves.SaveError = (short)dosError;
        }
        return 0;
    }

    /// <summary>Whether the open save file holds a whole campaign (<see cref="SaveFileCodec.IsWhole"/>, D-105).</summary>
    private bool HoldsWholeCampaign(int handle)
    {
        var files = world.Files;
        var header = new byte[SaveFileCodec.HeaderSize];
        var read = files.Read(handle, header);
        var length = files.Seek(handle, 0, 2);
        files.Seek(handle, 0, 0);
        return read == header.Length && SaveFileCodec.IsWhole(header, length);
    }

    private string Number(int value)
    {
        Saves.Digits.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Saves.Digits.Text;
    }

    private void SetPathChar(int at, char value)
    {
        var path = Saves.SavePath.ToCharArray();
        path[at] = value;
        Saves.SavePath = new string(path);
    }

    /// <summary>Puts the extension behind the path's six characters ("x:cvN.").</summary>
    private void SetPathExtension(string extension) => Saves.SavePath = Saves.SavePath[..ExtensionAt] + extension;
}
