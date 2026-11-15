namespace CovertAction.Core.Menus;

/// <summary>
/// The variables of the "Please insert Disk X" prompt. The original came on several floppy disks; before a part of
/// the game is loaded, the running part looks for a file that only the right disk has and, when it is missing, asks
/// for the disk until it is there. On a hard disk every file is found and the prompt never shows.
/// </summary>
// orig: CODE DS:1042, DS:11ce..DS:124d (src/c/include/code.h: default_disk, disk_file_names and the prompt's texts)
public sealed class DiskPromptState
{
    /// <summary>The number of disks the prompt knows.</summary>
    public const int Disks = 4;

    /// <summary>CODE DS:1042: the disk asked for when a quiet check fails (2 at start).</summary>
    public short DefaultDisk { get; set; }

    /// <summary>CODE DS:11ce, 11d8, 11e1, 11e9: for each disk (entry 0 is disk 1), the name of a file only that disk has.</summary>
    public string[] DiskFileNames { get; } = ["", "", "", ""];

    /// <summary>CODE DS:11f0: the start of the prompt, up to the disk's letter.</summary>
    public string StrPleaseInsertDisk { get; set; } = "";

    /// <summary>CODE DS:1204: the start of the second line, which names the 3.5 inch disk that holds the same files.</summary>
    public string StrIf35InsertDisk { get; set; } = "";

    /// <summary>CODE DS:121b: the closing bracket of the second line.</summary>
    public string StrCloseParen { get; set; } = "";

    /// <summary>CODE DS:121d: the end of the prompt: a full stop, an empty line and "Press Return to continue.".</summary>
    public string StrPressReturn { get; set; } = "";
}
