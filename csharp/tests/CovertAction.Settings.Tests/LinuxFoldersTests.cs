using CovertAction.Launcher;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The Linux release's folders: where the game is looked for (Steam's libraries, Heroic's list, Wine prefixes; temporary
/// folders stand for the home folder) and where the settings and the saves go when the program's folder cannot be written
/// to (XDG_CONFIG_HOME and XDG_DATA_HOME). The search's tests need no Linux; the folders' tests run on Linux only.
/// </summary>
public sealed class LinuxFoldersTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "covert-linux-" + Guid.NewGuid().ToString("N"));

    public LinuxFoldersTests() => Directory.CreateDirectory(_home);

    public void Dispose() => Directory.Delete(_home, true);

    private static void MakeGame(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in GameFolderSearch.RequiredFiles)
        {
            File.WriteAllText(Path.Combine(folder, file), "");
        }
    }

    private static string? Find(string home, Func<string, string?> environment) =>
        GameFolderSearch.LinuxCandidates(home, environment, []).Select(c => GameFolderSearch.GameFolderIn(c.Folder)).FirstOrDefault(f => f is not null);

    [Fact]
    public void SteamsLibraryOnAnSdCardIsFoundThroughItsList()
    {
        var steam = Path.Combine(_home, ".local", "share", "Steam");
        var card = Path.Combine(_home, "card");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + card.Replace("\\", "\\\\") + "\" } }");
        MakeGame(Path.Combine(card, "steamapps", "common", "Sid Meier's Covert Action (Classic)", "Covert"));

        Assert.Equal(Path.Combine(card, "steamapps", "common", "Sid Meier's Covert Action (Classic)", "Covert"), Find(_home, _ => null));
    }

    [Fact]
    public void HeroicsListNamesTheGogFolder()
    {
        var game = Path.Combine(_home, "elsewhere", "Covert");
        MakeGame(game);
        Directory.CreateDirectory(Path.Combine(_home, ".config", "heroic", "gog_store"));
        File.WriteAllText(Path.Combine(_home, ".config", "heroic", "gog_store", "installed.json"),
            "{\"installed\":[{\"appName\":\"" + GameFolderSearch.GogId + "\",\"install_path\":\"" + game.Replace("\\", "\\\\") + "\"}]}");

        Assert.Equal(game, Find(_home, _ => null));
        Assert.Null(GameFolderSearch.HeroicInstallPath("{\"installed\":[{\"appName\":\"1\",\"install_path\":\"/x\"}]}", GameFolderSearch.GogId));
        Assert.Null(GameFolderSearch.HeroicInstallPath("not json", GameFolderSearch.GogId));
    }

    [Fact]
    public void AGameInAWinePrefixIsFoundAndXdgDataHomeIsHonoured()
    {
        var prefix = Path.Combine(_home, "prefixes", "gog");
        MakeGame(Path.Combine(prefix, "drive_c", "GOG Games", "Sid Meiers Covert Action", "Covert"));
        Assert.Null(Find(_home, _ => null));
        Assert.Equal(Path.Combine(prefix, "drive_c", "GOG Games", "Sid Meiers Covert Action", "Covert"),
            Find(_home, name => name == "WINEPREFIX" ? prefix : null));

        var data = Path.Combine(_home, "data");
        MakeGame(Path.Combine(data, "Steam", "steamapps", "common", "Covert Action"));
        Assert.Equal(Path.Combine(data, "Steam", "steamapps", "common", "Covert Action"), Find(_home, name => name == "XDG_DATA_HOME" ? data : null));
    }

    [SkippableFact]
    public void SettingsAndSavesGoUnderXdgWhenTheProgramFolderCannotBeWrittenTo()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "XDG folders are Linux's");
        var (config, data) = (Path.Combine(_home, "cfg"), Path.Combine(_home, "dat"));
        var (oldConfig, oldData) = (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);
            // /proc takes no new file, whoever asks (root included)
            Assert.Equal(Path.Combine(config, "CovertAction", SettingsFile.FileName), SettingsFile.WritablePath("/proc"));
            Assert.Equal(Path.Combine(data, "CovertAction", "Saves"), SavesFolder.For("/proc"));
            Assert.Equal(Path.Combine(_home, "Saves"), SavesFolder.For(_home));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldConfig);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
        }
    }
}
