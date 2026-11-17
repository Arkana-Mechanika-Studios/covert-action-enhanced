using CovertAction.Content;

namespace CovertAction.Launcher.Tests;

/// <summary>The case-insensitive lookup of the player's files, independent of the host's own file-name rules.</summary>
public sealed class HostFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-hostfiles-" + Guid.NewGuid().ToString("N"));

    public HostFilesTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "FAME.DTA"), [1]);
        File.WriteAllBytes(Path.Combine(_folder, "CapTrd01.Pan"), [2]);
        File.WriteAllBytes(Path.Combine(_folder, "exact.bin"), [3]);
        Directory.CreateDirectory(Path.Combine(_folder, "Disk1", "Banks"));
        File.WriteAllBytes(Path.Combine(_folder, "Disk1", "Banks", "SOUND.BNK"), [4]);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void MatchPrefersTheExactSpellingThenTheSmallestOrdinal()
    {
        Assert.Equal("fame.dta", HostFiles.Match(["FAME.DTA", "fame.dta", "Fame.Dta"], "fame.dta"));
        Assert.Equal("FAME.DTA", HostFiles.Match(["fame.dta", "Fame.Dta", "FAME.DTA"], "Fame.dta"));
        Assert.Null(HostFiles.Match(["FAME.DTA"], "fame.dt"));
    }

    [Fact]
    public void FindsAFileInAnyLetterCase()
    {
        Assert.Equal(Path.Combine(_folder, "FAME.DTA"), HostFiles.FindByListing(_folder, "fame.dta", false));
        Assert.Equal(Path.Combine(_folder, "CapTrd01.Pan"), HostFiles.FindByListing(_folder, "CAPTRD01.PAN", false));
        Assert.Equal(Path.Combine(_folder, "exact.bin"), HostFiles.Find(_folder, "exact.bin"));
        Assert.True(HostFiles.Exists(_folder, "Fame.Dta"));
        Assert.Equal([1], HostFiles.ReadAllBytes(_folder, "fame.dta"));
    }

    [Fact]
    public void MissingFilesAndFoldersGiveNull()
    {
        Assert.Null(HostFiles.Find(_folder, "nothing.dat"));
        Assert.Null(HostFiles.Find(Path.Combine(_folder, "absent"), "fame.dta"));
        Assert.False(HostFiles.Exists(_folder, "disk1")); // a folder is not a file
        Assert.Throws<FileNotFoundException>(() => HostFiles.ReadAllBytes(_folder, "nothing.dat"));
    }

    [Fact]
    public void FindsAFolderAndWalksFolders()
    {
        Assert.Equal(Path.Combine(_folder, "Disk1"), HostFiles.FindByListing(_folder, "DISK1", true));
        Assert.Null(HostFiles.FindDirectory(_folder, "FAME.DTA"));
        var walked = HostFiles.CombineDirectories(_folder, ["disk1", "banks"]);
        Assert.Equal(Path.Combine(_folder, "Disk1", "Banks"), walked, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(Path.Combine(walked, "SOUND.BNK"), HostFiles.FindByListing(walked, "sound.bnk", false));
        Assert.Equal(Path.Combine(_folder, "Disk1", "gone", "more"), HostFiles.CombineDirectories(_folder, ["disk1", "gone", "more"]), ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void ANewFileIsSeenAfterTheFolderChanges()
    {
        Assert.Null(HostFiles.FindByListing(_folder, "late.dat", false));
        File.WriteAllBytes(Path.Combine(_folder, "LATE.DAT"), [5]);
        Directory.SetLastWriteTimeUtc(_folder, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(Path.Combine(_folder, "LATE.DAT"), HostFiles.FindByListing(_folder, "late.dat", false));
    }
}
