using CovertAction.Platform;

namespace CovertAction.Presentation.Library;

/// <summary>The library's check of a disk drive, which the parts that list and write saved games carry (the menus and the campaign).</summary>
public static class LibraryDisks
{
    /// <summary>The last of the floppy drives (A is 0, B is 1); a drive above it is a hard disk.</summary>
    private const int LastFloppyDrive = 1;

    /// <summary>How often a floppy drive is asked before it counts as empty.</summary>
    private const int Tries = 3;

    /// <summary>
    /// Whether a drive can be used for saved games: a hard disk always; a floppy drive when its disk can be read,
    /// which is tried up to three times, with the drives set back after each failure.
    /// </summary>
    /// <param name="files">The game's files.</param>
    /// <param name="drive">The drive: 0 for A, 1 for B, 2 for C ...</param>
    /// <returns>Whether the drive is ready.</returns>
    // orig: FINAL 0100:a295 drive_ready (src/c/final/final_0100_a295.c) matched; also GAME 00f7:aa73; tests: LibraryOracleTests, GameFolderFilesTests
    public static bool DriveReady(IFiles files, int drive)
    {
        if (drive > LastFloppyDrive)
        {
            return true;
        }
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            if (files.VerifyDisk(drive) == 0)
            {
                return true;
            }
            files.ResetDisks();
        }
        return false;
    }
}
