namespace CovertAction.Content;

/// <summary>
/// The player's own copy of Covert Action: the folder with the original's files (pictures, animations, texts, crime
/// files, drivers). The rewrite contains none of them; everything the game shows or reads comes from here at run time.
/// </summary>
/// <remarks>
/// New code, not part of the original. The original opens files by their DOS names in the current directory; DOS
/// names have no letter case, so a name is matched here whatever its case on the host's disk.
/// </remarks>
/// <param name="path">The folder's path on the host.</param>
public sealed class GameFolder(string path)
{
    /// <summary>The environment variable that names the game folder for tools and tests.</summary>
    public const string EnvironmentVariable = "COVERT_GAME_DIR";

    /// <summary>The folder's path on the host.</summary>
    public string Path { get; } = path;

    /// <summary>The folder named by <see cref="EnvironmentVariable"/>, or null when the variable is not set or names no folder.</summary>
    public static GameFolder? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? new GameFolder(path) : null;
    }

    /// <summary>The host path of the game file with the DOS name <paramref name="dosName"/> (for example "CRIME1.DTA"), or null when the folder has no such file.</summary>
    public string? Find(string dosName) =>
        HostFiles.Find(Path, dosName);

    /// <summary>All bytes of a game file.</summary>
    /// <exception cref="FileNotFoundException">The folder has no file of that name.</exception>
    public byte[] Read(string dosName) =>
        File.ReadAllBytes(Find(dosName) ?? throw new FileNotFoundException($"{dosName} is not in the game folder {Path}", dosName));
}
