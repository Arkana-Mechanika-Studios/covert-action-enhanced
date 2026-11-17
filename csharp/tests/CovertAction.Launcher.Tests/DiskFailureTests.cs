using CovertAction.Content;
using CovertAction.Core.Campaign;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Game;
using CovertAction.Presentation.Library;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// What a player of the rewrite meets when Windows refuses a file (csharp/docs/SESSION-PROOF.md, item 14): a save that
/// cannot be made, a saves folder that cannot be written, a file another program holds, a game file missing, a saved
/// game that is not whole. The game answers as the original does where the original has an answer (its "File Error"
/// box, its empty Hall of Fame, its fatal error naming the file), and never with an exception of the host's file system.
/// </summary>
public sealed class DiskFailureTests : IDisposable
{
    private readonly DirectoryInfo _game = Directory.CreateTempSubdirectory("covert-game-");
    private readonly DirectoryInfo _saves = Directory.CreateTempSubdirectory("covert-saves-");
    private readonly List<GameFolderFiles> _made = [];

    public void Dispose()
    {
        _made.ForEach(files => files.Dispose());
        foreach (var file in _saves.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }
        _game.Delete(recursive: true);
        _saves.Delete(recursive: true);
    }

    private GameFolderFiles Files(string? writeFolder = null)
    {
        _made.Add(new GameFolderFiles(new GameFolder(_game.FullName), writeFolder ?? _saves.FullName));
        return _made[^1];
    }

    private const OpenMode MakeEmpty = OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary;

    [SkippableFact]
    public void AReadOnlySaveCannotBeMadeAndStaysAsItWas()
    {
        Skip.If(OperatingSystem.IsLinux() && Environment.UserName == "root", "root writes to read-only files (the test containers run as root)");
        // a save the player marked read-only: DOS's create fails with "access denied" (GAME 00f7:a833 then says "File Error")
        var save = Path.Combine(_saves.FullName, "CV0.SVE");
        File.WriteAllBytes(save, [1, 2, 3]);
        File.SetAttributes(save, FileAttributes.ReadOnly);
        var files = Files();

        Assert.Equal(-1, files.Open("c:cv0.sve", MakeEmpty, 0x80));
        Assert.Equal(-1, files.Access("CV0.SVE", 2));
        Assert.Equal(0, files.Access("CV0.SVE", 4));
        // still there to read, as under DOS
        var handle = files.Open("CV0.SVE", OpenMode.Read | OpenMode.Binary);
        Assert.NotEqual(-1, handle);
        files.Close(handle);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(save));
    }

    [Fact]
    public void ASavesFolderThatCannotBeMadeRefusesEveryWriteAndTheHallOfFameReadsEmpty()
    {
        // the saves folder's place is taken by a file: nothing can be made there (as a write-protected disk under DOS)
        File.WriteAllBytes(Path.Combine(_game.FullName, "FAME.DTA"), [7, 8, 9]);
        var blocked = Path.Combine(_saves.FullName, "Saves");
        File.WriteAllBytes(blocked, []);
        var files = Files(blocked);

        Assert.Equal(-1, files.Open("c:cv0.sve", MakeEmpty, 0x80));
        // the Hall of Fame is opened to read and write (FINAL 0100:4cd0): it fails, and the table stays as cleared
        var fame = files.Open("FAME.DTA", OpenMode.Binary | OpenMode.Create | OpenMode.ReadWrite, 0x180);
        Assert.Equal(-1, fame);
        Assert.Equal(-1, files.Read(fame, new byte[8]));
        Assert.Equal(-1, files.Write(fame, [1]));
        Assert.Equal(-1, files.Close(fame));
        // the player's own copy is read as before
        var handle = files.Open("FAME.DTA", OpenMode.Binary);
        Assert.Equal(3, files.Read(handle, new byte[8]));
    }

    [Fact]
    public void AFileAnotherProgramHoldsFailsToOpenWithoutAnException()
    {
        var save = Path.Combine(_saves.FullName, "CV1.SVE");
        using var held = new FileStream(save, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var files = Files();

        Assert.Equal(-1, files.Open("c:cv1.sve", MakeEmpty, 0x80));
        Assert.Equal(-1, files.Open("c:cv1.sve", OpenMode.Read | OpenMode.Binary));
    }

    [Fact]
    public void ASeekBeforeTheStartFailsAsUnderDos()
    {
        File.WriteAllBytes(Path.Combine(_game.FullName, "DATA.BIN"), [1, 2, 3]);
        var files = Files();
        var handle = files.Open("DATA.BIN", OpenMode.Read | OpenMode.Binary);
        Assert.Equal(-1, files.Seek(handle, -5, 0));
        Assert.Equal(3, files.Seek(handle, 0, 2));
    }

    [SkippableFact]
    public void WhatTheGamePrintsOnTheConsoleIsKeptForTheLauncher()
    {
        var files = Files();
        Assert.Equal(6, files.Write(2, "hq.pic"u8));
        Assert.Equal("hq.pic", files.TakeConsoleOutput());
        Assert.Equal("", files.TakeConsoleOutput());
    }

    // ---- with the player's game files ----

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "GAME.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>A copy of the player's game folder without the files <paramref name="left"/> picks.</summary>
    private GameFolder CopyWithout(GameFolder players, Func<string, bool> left)
    {
        foreach (var file in Directory.EnumerateFiles(players.Path).Where(f => !left(Path.GetFileName(f))))
        {
            File.Copy(file, Path.Combine(_game.FullName, Path.GetFileName(file)));
        }
        return new GameFolder(_game.FullName);
    }

    [SkippableFact]
    public void TheCampaignsSaveScreenSaysFileErrorForASaveThatCannotBeMade()
    {
        Skip.If(OperatingSystem.IsLinux() && Environment.UserName == "root", "root writes to read-only files (the test containers run as root)");
        // GAME 00f7:a833 save_to_named_file: the file is made first to see that it can be; if not, "File Error: <path>"
        var players = GameFiles();
        var state = new CampaignState();
        GameDgroup.Layout.Load(Executable.Read(players.Read("GAME.EXE")).DataSegment(), state);
        var save = Path.Combine(_saves.FullName, "CV2.SVE");
        File.WriteAllBytes(save, [5]);
        File.SetAttributes(save, FileAttributes.ReadOnly);
        _made.Add(new GameFolderFiles(players, _saves.FullName));
        var world = new Boundaries(null!, null!, null!, null!, null!, _made[^1]);
        string? shown = null;

        var saved = new CampaignSaves(state, new SharedState(), world).SaveToNamedFile("c:cv2.sve", (text, x, y) =>
        {
            shown = text;
            return 0;
        });

        Assert.False(saved);
        Assert.Equal("File Error: c:cv2.sve\n", shown);
        Assert.Equal([5], File.ReadAllBytes(save));
    }

    [SkippableFact]
    public void ASaveThatIsNotWholeIsListedAsAnEmptySlot()
    {
        // D-105: the original reads any save it can open (a short head with what its stack held, counts past its tables,
        // the missing tables as the part had them); the rewrite lists such a file as an empty slot, so it is not loaded
        var players = GameFiles();
        var state = new Core.Final.FinalState();
        FinalDgroup.Layout.Load(Executable.Read(players.Read("FINAL.EXE")).DataSegment(), state);
        _made.Add(new GameFolderFiles(players, _saves.FullName));
        var world = new Boundaries(null!, null!, null!, null!, null!, _made[^1]);
        var saves = new LibrarySaves(state, world, null!, null!, _ => { }, (_, _, _) => 0, _ => true);
        var empty = state.Saves.Texts[(int)SaveText.SlotEmpty];
        ILibrarySaveState library = state;

        var whole = WholeSave(cast: 3, events: 5);
        var save = Path.Combine(_saves.FullName, "CV0.SVE");
        bool Listed(byte[] bytes)
        {
            File.WriteAllBytes(save, bytes);
            library.MessageText.Text = "";
            state.Saves.SavePath = "c:cv0.sve";
            var exists = saves.ListSaveSlot(describe: true);
            Assert.Equal(!exists, library.MessageText.Text == empty);
            return exists;
        }

        Assert.True(Listed(whole));
        Assert.True(Listed([.. whole, 0]));                                   // longer: the original reads what it needs
        Assert.False(Listed(whole[..^1]));                                    // a table cut short
        Assert.False(Listed(whole[..(SaveFileCodec.HeaderSize - 1)]));       // the head cut short
        Assert.False(Listed([]));
        var tooMany = WholeSave(cast: 3, events: 5);
        tooMany[0x3e - 0x10] = 40;                                           // n_cast beyond GAME's 32 records
        Assert.False(Listed([.. tooMany, .. new byte[0x4000]]));
    }

    /// <summary>A save file with a campaign of the given cast and events.</summary>
    private static byte[] WholeSave(int cast, int events)
    {
        var shared = new SharedState { NCast = (short)cast, NEvents = (short)events };
        for (var i = 0; i < cast; i++)
        {
            shared.Campaign.Cast.Add(new Activity());
            shared.Campaign.CastProgress.Add(new Activity2());
        }
        for (var i = 0; i < events; i++)
        {
            shared.Campaign.Events.Add(new Event());
        }
        return SaveFileCodec.Write(shared);
    }

    [SkippableFact]
    public void APictureMissingFromTheGameFolderEndsThePlayNamingIt()
    {
        // the raid's pictures gone: the part gives up as the original's fatal_error does (exit code 99, D-12), and the
        // launcher is told which file; before, the raid's runner took the exit for the player's leaving and went on
        var folder = CopyWithout(GameFiles(), name => name.EndsWith(".PIC", StringComparison.OrdinalIgnoreCase));
        var game = new Game(folder, new HeadlessHost([], unixSeconds: 600_000_000));

        var failure = Assert.Throws<InvalidDataException>(() => game.PlayRaidPractice(0));

        Assert.Contains("the building raid stopped on a file it could not read", failure.Message, StringComparison.Ordinal);
        Assert.Contains(".pic: No such file or directory", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(99, Assert.IsType<Msc.ProgramExitException>(failure.InnerException).Code);
    }
}
