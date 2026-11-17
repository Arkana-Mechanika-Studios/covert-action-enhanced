namespace CovertAction.Settings.Tests;

/// <summary>
/// Where the game is looked for when no folder is named (<see cref="GameFolderSearch"/>): GOG's registration, Steam's
/// libraries, the installed-programs list and the usual folders, with temporary folders and a fake registry and
/// environment so the tests do not depend on the machine.
/// </summary>
public sealed class GameFolderSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "covert-gamesearch-" + Guid.NewGuid().ToString("N"));

    public GameFolderSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>A machine of the test: registry values by (root, key, name), subkeys by key, variables, drives and text files.</summary>
    private sealed class FakeLookup : IMachineLookup
    {
        public Dictionary<(RegistryRoot, string, string), string> Values { get; } = [];

        public Dictionary<(RegistryRoot, string), string[]> SubKeys { get; } = [];

        public Dictionary<string, string> Variables { get; } = [];

        public List<string> Drives { get; } = [];

        public List<string> Reads { get; } = [];

        public string? RegistryValue(RegistryRoot root, string key, string name)
        {
            Reads.Add(key);
            return Values.GetValueOrDefault((root, key, name));
        }

        public IReadOnlyList<string> RegistrySubKeys(RegistryRoot root, string key)
        {
            Reads.Add(key);
            return SubKeys.GetValueOrDefault((root, key)) ?? [];
        }

        public string? Environment(string name) => Variables.GetValueOrDefault(name);

        public IEnumerable<string> FixedDrives() => Drives;

        public string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private string Game(params string[] parts)
    {
        var folder = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(folder);
        foreach (var file in GameFolderSearch.RequiredFiles)
        {
            File.WriteAllText(Path.Combine(folder, file), "");
        }
        return folder;
    }

    private string Empty(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void LibraryFoldersAreReadFromSteamsListWithDoubledBackslashes()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                    "apps" { "327390" "123" }
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameFolderSearch.SteamLibraries(vdf));
    }

    [Fact]
    public void GogsRegistrationNamesTheFolderAndTheGameInItsSubfolderIsFound()
    {
        var install = Empty("gog-install");
        var game = Game("gog-install", "Covert");
        var lookup = new FakeLookup();
        lookup.Values[(RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games\" + GameFolderSearch.GogId, "path")] = install;

        var found = GameFolderSearch.Found(Empty("program"), Empty("current"), lookup);

        Assert.Equal([(game, "GOG's registration")], found);
    }

    [Fact]
    public void SteamsLibrariesAreSearchedWithTheNameOfTheFolderOrAnyFolderNamedLikeTheGame()
    {
        var steam = Empty("steam");
        var other = Empty("library2");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(steam, "steamapps")).FullName, "libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" { \"path\" \"" + other.Replace(@"\", @"\\") + "\" } }");
        // the Steam folder name is unconfirmed: a name the search does not know but that holds "Covert Action"
        var game = Game("library2", "steamapps", "common", "Covert Action Classic Edition", "Covert");
        var lookup = new FakeLookup();
        lookup.Values[(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath")] = steam;

        var found = GameFolderSearch.Found(Empty("program"), Empty("current"), lookup);

        Assert.Equal([(game, "Steam's library")], found);
    }

    [Fact]
    public void TheInstalledProgramsListIsMatchedByNameAndFallsBackToTheUninstallersFolder()
    {
        var withLocation = Game("with-location");
        var withUninstaller = Game("with-uninstaller");
        var uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var lookup = new FakeLookup();
        lookup.SubKeys[(RegistryRoot.LocalMachine, uninstall)] = ["{A}", "{B}", "{C}"];
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{A}", "DisplayName")] = "Sid Meier's Covert Action";
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{A}", "InstallLocation")] = withLocation;
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{B}", "DisplayName")] = "Sid Meiers Covert Action (Classic)";
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{B}", "UninstallString")] = "\"" + Path.Combine(withUninstaller, "unins000.exe") + "\" /SILENT";
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{C}", "DisplayName")] = "Some other program";
        lookup.Values[(RegistryRoot.LocalMachine, uninstall + @"\{C}", "InstallLocation")] = Game("other");

        var found = GameFolderSearch.Found(Empty("program"), Empty("current"), lookup);

        Assert.Equal([(withLocation, "Windows' installed programs"), (withUninstaller, "Windows' installed programs")], found);
    }

    [SkippableFact]
    public void TheUninstallersFolderIsReadFromQuotedAndUnquotedCommands()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows paths and a Windows registry value: nothing here runs on Linux");
        Assert.Equal(@"C:\GOG Games\X", GameFolderSearch.UninstallerFolder("\"C:\\GOG Games\\X\\unins000.exe\" /SILENT"));
        Assert.Equal(@"C:\GOG\X", GameFolderSearch.UninstallerFolder(@"C:\GOG\X\unins000.exe /SILENT"));
        Assert.Null(GameFolderSearch.UninstallerFolder("msiexec /x {GUID}"));
    }

    [Fact]
    public void TheUsualFoldersOfAFixedDriveAreLookedIn()
    {
        var game = Game("GOG Games", "Sid Meiers Covert Action", "Covert");
        var lookup = new FakeLookup();
        lookup.Drives.Add(_root + Path.DirectorySeparatorChar);

        var found = GameFolderSearch.Found(Empty("program"), Empty("current"), lookup);

        Assert.Equal([(game, "a usual install folder")], found);
    }

    [Fact]
    public void AGameInTheProgramsFolderIsFoundFirstAndTheRegistryIsNotRead()
    {
        var program = Game("program");
        var lookup = new FakeLookup();
        lookup.Values[(RegistryRoot.LocalMachine, @"SOFTWARE\GOG.com\Games\" + GameFolderSearch.GogId, "path")] = Game("gog");

        var first = GameFolderSearch.FindFirst(program, Empty("current"), lookup);

        Assert.Equal((program, "the program's folder"), first);
        Assert.Empty(lookup.Reads);
    }

    [Fact]
    public void ABrokenOrMissingRegistryGivesNoFolderAndNoFailure()
    {
        var lookup = new FakeLookup();
        lookup.Values[(RegistryRoot.LocalMachine, @"SOFTWARE\GOG.com\Games\" + GameFolderSearch.GogId, "path")] = Path.Combine(_root, "not-there");

        Assert.Empty(GameFolderSearch.Found(Empty("program"), Empty("current"), lookup));
        Assert.Null(GameFolderSearch.FindFirst(Empty("program2"), Empty("current2"), lookup));
    }

    [Fact]
    public void AStoreFolderWithTheGameInAnyFolderNamedCovertIsAccepted()
    {
        var game = Game("store", "Game Files Covert Action", "Covert");
        Assert.Equal(game, GameFolderSearch.GameFolderIn(Path.Combine(_root, "store")));
        Assert.Null(GameFolderSearch.GameFolderIn(Empty("nothing")));
        Assert.Null(GameFolderSearch.GameFolderIn(null));
    }
}
