using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>The game's file calls on a folder made up for the test: reading the game's files, writing the game's own.</summary>
public sealed class GameFolderFilesTests : IDisposable
{
    private readonly DirectoryInfo _game = Directory.CreateTempSubdirectory("covert-game-");
    private readonly DirectoryInfo _saves = Directory.CreateTempSubdirectory("covert-saves-");

    private readonly List<GameFolderFiles> _made = [];

    public void Dispose()
    {
        _made.ForEach(files => files.Dispose());
        _game.Delete(recursive: true);
        _saves.Delete(recursive: true);
    }

    private GameFolderFiles Files(bool canWrite = true)
    {
        _made.Add(new GameFolderFiles(new GameFolder(_game.FullName), canWrite ? _saves.FullName : null));
        return _made[^1];
    }

    [Fact]
    public void AFileIsReadInPiecesUnderTheNumberItWasOpenedWith()
    {
        File.WriteAllBytes(Path.Combine(_game.FullName, "DATA.BIN"), [1, 2, 3, 4, 5]);
        var files = Files();
        var handle = files.Open("data.bin", OpenMode.Read | OpenMode.Binary);
        Assert.Equal(5, handle);                                // the first file, as under DOS
        var buffer = new byte[3];
        Assert.Equal(3, files.Read(handle, buffer));
        Assert.Equal([1, 2, 3], buffer);
        Assert.Equal(2, files.Read(handle, buffer));            // the file's end: fewer than asked for
        Assert.Equal(0, files.Read(handle, buffer));
        Assert.Equal(1, files.Seek(handle, 1, 0));
        Assert.Equal(4, files.Seek(handle, -1, 2));
        Assert.Equal(1, files.Read(handle, buffer));
        Assert.Equal(5, buffer[0]);
        Assert.Equal(6, files.Open("DATA.BIN", OpenMode.Read)); // a second file gets the next number
        Assert.Equal(0, files.Close(handle));
        Assert.Equal(-1, files.Close(handle));
        Assert.Equal(-1, files.Read(handle, buffer));
        Assert.Equal(5, files.Open("DATA.BIN", OpenMode.Read)); // a closed file's number is given out again
    }

    [Fact]
    public void APlayersFileOpenedToWriteKeepsItsBytesAndThePlayersCopyIsNotChanged()
    {
        // the Hall of Fame (FINAL 0100:4cd0) opens the player's FAME.DTA for reading and writing: under DOS that is the
        // player's file, with its entries; found by the session proof (tools/session/sessions/final/options-fame.json)
        var players = Path.Combine(_game.FullName, "FAME.DTA");
        File.WriteAllBytes(players, [7, 8, 9]);
        var files = Files();
        var handle = files.Open("FAME.DTA", OpenMode.Binary | OpenMode.Create | OpenMode.ReadWrite, 0x180);
        var buffer = new byte[8];
        Assert.Equal(3, files.Read(handle, buffer));
        Assert.Equal([7, 8, 9], buffer[..3]);
        Assert.Equal(1, files.Write(handle, [1]));
        files.Close(handle);
        Assert.Equal([7, 8, 9], File.ReadAllBytes(players));
        Assert.Equal([7, 8, 9, 1], File.ReadAllBytes(Path.Combine(_saves.FullName, "FAME.DTA")));
        // opened to be emptied, it starts empty
        handle = files.Open("FAME.DTA", OpenMode.Binary | OpenMode.Create | OpenMode.Truncate | OpenMode.ReadWrite, 0x180);
        files.Close(handle);
        Assert.Empty(File.ReadAllBytes(Path.Combine(_saves.FullName, "FAME.DTA")));
    }

    [Fact]
    public void EveryDriveIsReadyForSavedGames()
    {
        // the saves are in a folder: a floppy drive's disk can always be read, at the first try
        var files = Files();
        Assert.Equal(0, files.VerifyDisk(0));
        Assert.True(CovertAction.Presentation.Library.LibraryDisks.DriveReady(files, 0));
        Assert.True(CovertAction.Presentation.Library.LibraryDisks.DriveReady(files, 1));
        Assert.True(CovertAction.Presentation.Library.LibraryDisks.DriveReady(files, 2));
    }

    [Fact]
    public void AFileThatIsNotThereCannotBeOpened()
    {
        var files = Files();
        Assert.Equal(-1, files.Open("ENV.SVE", OpenMode.Read | OpenMode.Binary));
        Assert.False(files.FindFileOnDisk("ENV.SVE", 0));
        Assert.Equal(3, files.CurrentDrive());
    }

    [Fact]
    public void ATextFileLosesItsCarriageReturnsAndEndsAtCtrlZ()
    {
        File.WriteAllBytes(Path.Combine(_game.FullName, "TEXT.DTA"), "ab\r\ncd\re\r\n\u001aXYZ"u8.ToArray());
        var files = Files();
        var handle = files.Open("TEXT.DTA", OpenMode.Read | OpenMode.Text, 420);
        var buffer = new byte[64];
        var read = files.Read(handle, buffer);
        Assert.Equal("ab\ncd\re\n"u8.ToArray(), buffer[..read]);   // a carriage return alone stays
        Assert.Equal(0, files.Read(handle, buffer));
    }

    [Fact]
    public void WhatTheGameWritesGoesToItsOwnFolderAndIsReadBackFromThere()
    {
        File.WriteAllBytes(Path.Combine(_game.FullName, "CV0.SVE"), [9, 9]);
        var files = Files();
        var handle = files.Open("cv0.sve", OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, 0x80);
        Assert.Equal(3, files.Write(handle, [1, 2, 3]));
        files.Close(handle);
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(_game.FullName, "CV0.SVE")));   // the player's copy is untouched
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_saves.FullName, "CV0.SVE")));

        handle = files.Open("CV0.SVE", OpenMode.Read | OpenMode.Binary);
        var buffer = new byte[8];
        Assert.Equal(3, files.Read(handle, buffer));
        Assert.Equal(-1, files.Write(handle, [4]));             // opened to read

        Assert.Equal(4, files.Write(2, "oops"u8));              // the console swallows what is printed
        Assert.Equal(-1, Files(canWrite: false).Open("NEW.SVE", OpenMode.Write | OpenMode.Create));
    }
    [Fact]
    public void ASaveMadeWithTheOriginalIsLoadedFromThePlayersCopyUntilASaveInItsSlotReplacesIt()
    {
        File.WriteAllBytes(Path.Combine(_game.FullName, "CV1.SVE"), [5, 6]);
        var files = Files();
        Assert.Equal(0, files.Access("CV1.SVE", 0));
        var handle = files.Open("CV1.SVE", OpenMode.Read | OpenMode.Binary);
        var buffer = new byte[8];
        Assert.Equal(2, files.Read(handle, buffer));
        Assert.Equal([5, 6], buffer[..2]);
        files.Close(handle);

        handle = files.Open("CV1.SVE", OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, 0x80);
        files.Write(handle, [7]);
        files.Close(handle);
        handle = files.Open("CV1.SVE", OpenMode.Read | OpenMode.Binary);
        Assert.Equal(1, files.Read(handle, buffer));
        Assert.Equal(7, buffer[0]);
        Assert.Equal([5, 6], File.ReadAllBytes(Path.Combine(_game.FullName, "CV1.SVE")));
    }

    [Fact]
    public void TheSavesGoBesideTheProgramWhenItsFolderCanBeWrittenTo()
    {
        Assert.Equal(Path.Combine(_game.FullName, "Saves"), SavesFolder.For(_game.FullName));
        Assert.Empty(_game.GetFiles());                         // the write probe leaves nothing behind
        Assert.Equal(SavesFolder.InAccount, SavesFolder.For(Path.Combine(_game.FullName, "no such folder")));
    }
}
