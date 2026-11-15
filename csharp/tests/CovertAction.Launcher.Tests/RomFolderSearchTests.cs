namespace CovertAction.Launcher.Tests;

/// <summary>Where the MT-32 ROMs are looked for when no folder is chosen (<see cref="RomFolderSearch"/>), with temporary folders and a fake environment.</summary>
public sealed class RomFolderSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "covert-romsearch-" + Guid.NewGuid().ToString("N"));

    public RomFolderSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Folder(params string[] parts)
    {
        var folder = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A folder holding an MT-32 control and PCM ROM (empty files: the search only checks the names).</summary>
    private string Roms(params string[] parts)
    {
        var folder = Folder(parts);
        File.WriteAllText(Path.Combine(folder, "MT32_CONTROL.ROM"), "");
        File.WriteAllText(Path.Combine(folder, "MT32_PCM.ROM"), "");
        return folder;
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    private static bool Holds(string folder) => SoundCheck.Roms(folder) is (true, _) or (_, true);

    [Fact]
    public void TheFolderBesideTheProgramComesFirstWhenItHoldsASet()
    {
        var program = Folder("program");
        var beside = Roms("program", "MT32_ROMS");
        Roms("game", "MT32_ROMS");

        var found = RomFolderSearch.Find(Folder("game"), program, Environment(), Holds);

        Assert.Equal((beside, "beside the program"), found);
    }

    [Fact]
    public void AnEmptyFolderBesideTheProgramIsPassedOverForTheGamesAndTheStoresFolders()
    {
        var program = Folder("program", "x");
        Folder("program", "x", "MT32_ROMS");
        var install = Folder("install");
        var game = Folder("install", "Covert");
        var roms = Roms("install", "mt32-roms");

        Assert.Equal((roms, "next to the game"), RomFolderSearch.Find(game, program, Environment(), Holds));

        var inGame = Roms("install", "Covert", "roms");
        Assert.Equal((inGame, "in the game's folder"), RomFolderSearch.Find(game, program, Environment(), Holds));
        Assert.NotEqual(install, game);
    }

    [Fact]
    public void ACm32lSetCountsToo()
    {
        var folder = Folder("cm", "roms");
        File.WriteAllText(Path.Combine(folder, "CM32L_CONTROL.ROM"), "");
        File.WriteAllText(Path.Combine(folder, "CM32L_PCM.ROM"), "");

        Assert.True(Holds(folder));
        Assert.Equal((folder, "next to the game"), RomFolderSearch.Find(Folder("cm", "Covert"), Folder("program"), Environment(), Holds));
    }

    [Fact]
    public void DosboxStagingsFoldersAreLookedInThroughTheEnvironment()
    {
        var local = Folder("local");
        var roaming = Folder("roaming");
        var staging = Roms("local", "DOSBox", "mt32-roms");
        var env = Environment(("LOCALAPPDATA", local), ("APPDATA", roaming));

        Assert.Equal((staging, "DOSBox Staging's"), RomFolderSearch.Find(null, Folder("program"), env, Holds));

        var older = Roms("roaming", "dosbox-staging", "mt32-roms");
        Directory.Delete(staging, true);
        Assert.Equal((older, "DOSBox Staging's"), RomFolderSearch.Find(null, Folder("program"), env, Holds));
    }

    [Fact]
    public void MuntsAndLinuxFoldersAreLookedInAndNothingFoundGivesNull()
    {
        var home = Folder("home");
        Assert.Null(RomFolderSearch.Find(null, Folder("program"), Environment(("HOME", home)), Holds));

        var linux = Roms("home", ".config", "dosbox", "mt32-roms");
        Assert.Equal((linux, "DOSBox Staging's"), RomFolderSearch.Find(null, Folder("program"), Environment(("HOME", home)), Holds));

        var munt = Roms("appdata", "Munt");
        Assert.Equal((munt, "Munt's"), RomFolderSearch.Find(null, Folder("program"), Environment(("APPDATA", Folder("appdata"))), Holds));
    }

    [Fact]
    public void AFolderThatCannotBeReadHoldsNothing()
    {
        var program = Folder("program");
        var roms = Roms("program", "MT32_ROMS");

        var found = RomFolderSearch.Find(null, program, Environment(), f => f == roms ? throw new UnauthorizedAccessException() : false);

        Assert.Null(found);
    }

    [Fact]
    public void WithoutAGameFolderAndWithoutSetsTheDefaultFolderIsUsedAndHasNoSource()
    {
        // not the real search over this machine's folders: only that the default is the folder beside the program
        Assert.EndsWith("MT32_ROMS", RomFolderSearch.DefaultFolder, StringComparison.Ordinal);
        Assert.Equal(RomFolderSearch.DefaultFolder, SoundCheck.DefaultRomFolder);
    }
}
