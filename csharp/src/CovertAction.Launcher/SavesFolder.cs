namespace CovertAction.Launcher;

/// <summary>
/// Where the game writes what it saves (the campaigns CV0.SVE..CV3.SVE, the Hall of Fame FAME.DTA): the original
/// writes them into its own folder; the rewrite leaves the player's copy as it is and writes into a folder of its own,
/// where they are read back from. A file the saves folder does not hold is read from the player's copy, so the saves
/// the player made with the original are there to load until a save in the same slot replaces them.
/// </summary>
/// <remarks>New code: the original has no such choice.</remarks>
public static class SavesFolder
{
    /// <summary>
    /// The folder the saves go to when none is named: "Saves" beside the program, so that the program's folder carries
    /// everything with it (a portable program, as the original kept its saves in its own folder); when the program's
    /// folder cannot be written to (under Program Files, say), "CovertAction/Saves" in the account's local application
    /// data.
    /// </summary>
    public static string Default => For(AppContext.BaseDirectory);

    /// <summary>The default saves folder of a program in <paramref name="programFolder"/>.</summary>
    public static string For(string programFolder)
    {
        var beside = Path.Combine(programFolder, "Saves");
        return Directory.Exists(beside) || CanWriteIn(programFolder) ? beside : InAccount;
    }

    /// <summary>"CovertAction/Saves" in the account's local application data.</summary>
    public static string InAccount =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "CovertAction", "Saves");

    /// <summary>Whether a file can be made in the folder: a probe file is made and deleted at once.</summary>
    private static bool CanWriteIn(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".covertaction-write-test-{Environment.ProcessId}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
