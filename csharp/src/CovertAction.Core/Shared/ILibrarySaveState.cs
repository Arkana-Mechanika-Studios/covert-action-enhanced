namespace CovertAction.Core.Shared;

/// <summary>
/// What a part needs to list, load and write saved games with the library's save screens: the library's own variables
/// and the screen's texts. Implemented by the menus part and by the campaign (which loads a game by its own routine,
/// so it is no <see cref="ICampaignCopy"/>).
/// </summary>
// orig: FINAL DGROUP DS:4834..4a1b (the save screens' variables and texts); also GAME DGROUP DS:3f8e..418b and DS:3fa4 (GAME's copies of the same routines)
public interface ILibrarySaveState : ILibraryState
{
    /// <summary>The variables and texts of the save screens.</summary>
    SaveScreenState Saves { get; }

    /// <summary>The names of the twelve months (FINAL DS:367c month_names), which a slot's date is written with.</summary>
    string[] MonthNames { get; }
}

/// <summary>The texts of the save screens, in the order the original has them in its data.</summary>
public enum SaveText
{
    /// <summary>"Select Load File..." and its line break: the heading of the load menu.</summary>
    SelectLoadFile,
    /// <summary>"Select Save File..." and its line break.</summary>
    SelectSaveFile,
    /// <summary>The extension "sve" that list_save_slot puts on the path.</summary>
    ListExtension,
    /// <summary>" Max '": before the agent's code name in a slot's description.</summary>
    SlotNameStart,
    /// <summary>"' Remington: 00": after the code name, before the rank.</summary>
    SlotNameEnd,
    /// <summary>" rank" and the line break and dots that start the second line of a slot.</summary>
    SlotRankEnd,
    /// <summary>": " between the city and the date.</summary>
    ColonSpace,
    /// <summary>" " between the month and the day.</summary>
    Space,
    /// <summary>", " between the day and the year.</summary>
    CommaSpace,
    /// <summary>" (Local)".</summary>
    LevelLocal,
    /// <summary>" (National)".</summary>
    LevelNational,
    /// <summary>" (Regional)".</summary>
    LevelRegional,
    /// <summary>" (Global)".</summary>
    LevelGlobal,
    /// <summary>The end of a slot's description: a full stop and two more lines.</summary>
    SlotEnd,
    /// <summary>The description of a slot that has no file: " (EMPTY)" and two blank lines.</summary>
    SlotEmpty,
    /// <summary>"Game has been saved.".</summary>
    GameSaved,
    /// <summary>"Game NOT saved.".</summary>
    GameNotSaved,
    /// <summary>"Write access denied." (DOS error 13).</summary>
    WriteDenied,
    /// <summary>"Disk Full." (DOS error 28).</summary>
    DiskFull,
    /// <summary>"Press key to continue.".</summary>
    PressKey,
    /// <summary>The extension "sve" that write_save_game puts on the path.</summary>
    WriteExtension,
    /// <summary>"File Error: " in front of the name of a file that cannot be made.</summary>
    FileError,
    /// <summary>A line break.</summary>
    LineBreak,
    /// <summary>The line break and "... save in progress." shown while a game is written.</summary>
    SaveInProgress,
    /// <summary>"Which drive contains your saved game files?" with the marker that the drive letter replaces.</summary>
    DriveQuestionLoad,
    /// <summary>"Which drive contains your Save Game disk?" with the marker that the drive letter replaces.</summary>
    DriveQuestionSave,
    /// <summary>The instruction behind the drive letter: ":" and "Press drive letter and Return when disk is inserted.".</summary>
    DriveInstruction,
    /// <summary>"Press Escape to cancel.".</summary>
    DriveCancel,
    /// <summary>"No Disk in Drive A." (the letter is replaced by the drive's).</summary>
    NoDisk,
    /// <summary>Number of texts.</summary>
    Count,
}

/// <summary>The variables of the save screens (the original keeps them next to its other screen variables).</summary>
public sealed class SaveScreenState
{
    /// <summary>The slots of the load and save menus.</summary>
    public const int SlotCount = 4;

    /// <summary>Bytes of a save file's plot head, which the slot list reads to describe a slot.</summary>
    public const int SlotBlockLength = 654;

    /// <summary>FINAL DS:4834 save_path: the file's name, "x:cvN.sve" (the drive's letter, the slot's digit).</summary>
    public string SavePath { get; set; } = "";

    /// <summary>FINAL DS:5df0 loading_flag: true in the load screen, false in the save screen (picks the wording of the drive question).</summary>
    public bool Loading { get; set; }

    /// <summary>FINAL DS:5df2 save_error: the DOS error that made a write fail, 0 while none did.</summary>
    public short SaveError { get; set; }

    /// <summary>FINAL DS:9f8e slot_exists_mask: one bit per slot whose file could be opened.</summary>
    public short SlotExistsMask { get; set; }

    /// <summary>FINAL DS:b07c save_slot_choice: the slot chosen in the load or save menu, -1 for none.</summary>
    public short SlotChoice { get; set; }

    /// <summary>FINAL DS:5df4 slot_file_handle: the file number of the slot being described or written.</summary>
    public short SlotFileHandle { get; set; }

    /// <summary>FINAL DS:5df6 slot_first_word: the difficulty word read from a save file.</summary>
    public short SlotLevel { get; set; }

    /// <summary>FINAL DS:9bf0 slot_block: the plot head read from a save file.</summary>
    public byte[] SlotBlock { get; } = new byte[SlotBlockLength];

    /// <summary>FINAL DS:5de0 digits_slot: the buffer the numbers of a slot's description are written into.</summary>
    public TextBuffer Digits { get; } = new(16);

    /// <summary>The texts, by <see cref="SaveText"/>.</summary>
    public string[] Texts { get; } = [.. Enumerable.Repeat("", (int)SaveText.Count)];
}
