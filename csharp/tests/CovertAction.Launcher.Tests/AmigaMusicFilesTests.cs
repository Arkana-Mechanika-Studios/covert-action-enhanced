using CovertAction.Audio.Amiga;
using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga music option (D-640) with the player's own files and folders: the three tunes read whole (skipped without
/// COVERT_AMIGA_DIR, the folder with thememusic.dat, finalmusic.dat and spy6music.dat), where the files are looked for,
/// and the fall back to the sound card without them. Nothing of an Amiga file is kept in the repository.
/// </summary>
public sealed class AmigaMusicFilesTests : IDisposable
{
    private const string FolderVariable = "COVERT_AMIGA_DIR";

    /// <summary>Where a test may write the tunes as WAV files to listen to (outside the repository); unset: none are written.</summary>
    private const string WavFolderVariable = "COVERT_AMIGA_WAV_DIR";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "covert-amiga-" + Guid.NewGuid().ToString("N"));

    public AmigaMusicFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The folder with the music files: COVERT_AMIGA_DIR itself, or its disk1 folder (the folder with the disks' folders in it also serves the sound effects, D-641).</summary>
    private static string? RealFolder() => Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } folder && Directory.Exists(folder) ? AmigaFolderSearch.Resolve(folder) : null;

    private string Make(params string[] path)
    {
        var folder = Path.Combine([_root, .. path]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Touch(string folder, params string[] names)
    {
        foreach (var name in names)
        {
            File.WriteAllBytes(Path.Combine(folder, name), []);
        }
    }

    [SkippableFact]
    public void TheThreeTunesParseWithNoBytesLeft()
    {
        var folder = RealFolder();
        Skip.If(folder is null, "no COVERT_AMIGA_DIR");
        var tunes = AmigaTunes.Read(folder!);
        foreach (var tune in new[] { tunes.Theme, tunes.Final, tunes.Spy })
        {
            Assert.Equal(0, tune.TrailingBytes);
            Assert.Equal(15, tune.Samples.Count);
        }
        // the table of csharp/docs/AMIGA-MUSIC.md
        Assert.Equal((8, 4), (tunes.Theme.Orders.Count, tunes.Theme.PatternCount));
        Assert.Equal(GmcTune.EndPattern, tunes.Theme.Orders[^1]);
        Assert.Equal((7, 4), (tunes.Final.Orders.Count, tunes.Final.PatternCount));
        Assert.Equal((12, 5), (tunes.Spy.Orders.Count, tunes.Spy.PatternCount));
        Assert.Equal(6, tunes.Theme.Samples.Count(s => s.Data.Length > 0));
        Assert.Equal(6, tunes.Final.Samples.Count(s => s.Data.Length > 0));
        Assert.Equal(8, tunes.Spy.Samples.Count(s => s.Data.Length > 0));
        Assert.Equal(50462, new FileInfo(Path.Combine(folder!, AmigaTunes.ThemeFile)).Length);
        Assert.Equal(75742, new FileInfo(Path.Combine(folder!, AmigaTunes.SpyFile)).Length);
    }

    [SkippableFact]
    public void TheTunesPlayForTheirLengthAndEndOnTheEndOrder()
    {
        var folder = RealFolder();
        Skip.If(folder is null, "no COVERT_AMIGA_DIR");
        var tunes = AmigaTunes.Read(folder!);
        const int rate = 44100;
        foreach (var (name, tune, minSeconds, maxSeconds) in new[] { ("theme", tunes.Theme, 40, 50), ("final", tunes.Final, 35, 50), ("spy6", tunes.Spy, 70, 85) })
        {
            var player = new GmcPlayer(tune, rate, loops: false);
            var buffer = new short[2 * rate];
            var seconds = 0;
            var loud = 0;
            while (!player.IsFinished && seconds < 200)
            {
                player.Render(buffer);
                seconds++;
                loud += buffer.Count(s => Math.Abs(s) > 1000);
            }
            Assert.True(player.IsFinished, name);
            Assert.InRange(seconds, minSeconds, maxSeconds);
            Assert.True(loud > rate, name + " is silent");
        }
    }

    [SkippableFact]
    public void TheTunesAreWrittenAsWavFilesWhenAskedTo()
    {
        var folder = RealFolder();
        var target = Environment.GetEnvironmentVariable(WavFolderVariable);
        Skip.If(folder is null || string.IsNullOrEmpty(target), "no COVERT_AMIGA_DIR or COVERT_AMIGA_WAV_DIR");
        var tunes = AmigaTunes.Read(folder!);
        Directory.CreateDirectory(target!);
        foreach (var (name, tune) in new[] { ("thememusic", tunes.Theme), ("finalmusic", tunes.Final), ("spy6music", tunes.Spy) })
        {
            WriteWav(Path.Combine(target!, $"csharp-{name}.wav"), tune, lowPass: true);
            WriteWav(Path.Combine(target!, $"csharp-{name}-nofilter.wav"), tune, lowPass: false);
        }
    }

    /// <summary>About 20 seconds of a tune as a 16-bit stereo WAV at 44.1 kHz.</summary>
    private static void WriteWav(string path, GmcTune tune, bool lowPass)
    {
        const int rate = 44100, seconds = 20;
        var player = new GmcPlayer(tune, rate, loops: true, lowPass);
        var frames = new short[2 * rate * seconds];
        player.Render(frames);
        using var file = new BinaryWriter(File.Create(path));
        var bytes = frames.Length * 2;
        file.Write("RIFF"u8);
        file.Write(36 + bytes);
        file.Write("WAVEfmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)2);
        file.Write(rate);
        file.Write(rate * 4);
        file.Write((short)4);
        file.Write((short)16);
        file.Write("data"u8);
        file.Write(bytes);
        foreach (var sample in frames)
        {
            file.Write(sample);
        }
    }

    [Fact]
    public void TheFilesAreFoundBesideTheProgramInTheirFolderOrInDisk1()
    {
        var program = Make("program");
        Assert.Null(AmigaFolderSearch.Find(null, program));
        var files = Make("program", "amiga", "files");
        Assert.Null(AmigaFolderSearch.Find(null, program));
        Touch(files, AmigaTunes.ThemeFile);
        Assert.Equal((files, "beside the program"), AmigaFolderSearch.Find(null, program));
        File.Delete(Path.Combine(files, AmigaTunes.ThemeFile));
        var disk1 = Make("program", "amiga", "files", "disk1");
        Touch(disk1, AmigaTunes.ThemeFile);
        Assert.Equal((disk1, "beside the program"), AmigaFolderSearch.Find(null, program));
    }

    [Fact]
    public void TheFilesAreFoundNearTheGame()
    {
        var program = Make("program");
        var game = Make("store", "Covert Action", "Covert");
        Assert.Null(AmigaFolderSearch.Find(game, program));
        var files = Make("store", "amiga", "files");
        Touch(files, AmigaTunes.ThemeFile);
        Assert.Equal((files, "next to the game"), AmigaFolderSearch.Find(game, program));
        var inside = Make("store", "Covert Action", "Covert", "amiga");
        Touch(inside, AmigaTunes.ThemeFile);
        Assert.Equal((inside, "in the game's folder"), AmigaFolderSearch.Find(game, program));
    }

    [Fact]
    public void AFolderTheUserNamesIsTakenAsItIsOrByItsDisk1()
    {
        var named = Make("mine");
        Assert.Null(AmigaFolderSearch.Resolve(named));
        Assert.Null(AmigaFolderSearch.Resolve(Path.Combine(_root, "nothing")));
        var disk = Make("mine", "adf-1");
        Touch(disk, AmigaTunes.ThemeFile);
        Assert.Equal(disk, AmigaFolderSearch.Resolve(named));
        Touch(named, AmigaTunes.ThemeFile);
        Assert.Equal(named, AmigaFolderSearch.Resolve(named));
    }

    [Fact]
    public void TheMissingFilesAreNamed()
    {
        var folder = Make("some");
        Assert.Equal([AmigaTunes.ThemeFile, AmigaTunes.FinalFile, AmigaTunes.SpyFile], AmigaFolderSearch.Missing(folder));
        Touch(folder, AmigaTunes.ThemeFile, AmigaTunes.SpyFile);
        Assert.Equal([AmigaTunes.FinalFile], AmigaFolderSearch.Missing(folder));
    }

    /// <summary>A host whose sound device keeps the source it is given.</summary>
    private sealed class DeviceHost : IHost, IPcmOutput, IClock
    {
        public IPcmSource? Source { get; private set; }

        public IVideo Video => throw new NotSupportedException();

        public IInput Input => throw new NotSupportedException();

        public IPcmOutput Audio => this;

        public IClock Clock => this;

        public int SampleRate => 44100;

        public long FramesPlayed => 0;

        public int LatencyFrames => 0;

        public long Milliseconds => 0;

        public long UnixSeconds => 0;

        public void Start(IPcmSource source) => Source = source;

        public void Sleep(int milliseconds)
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A module of one pattern and one looping instrument, its first row a note on voice 0.</summary>
    private static byte[] SyntheticModule()
    {
        var bytes = new byte[444 + 1024 + 16];
        bytes[5] = 8;    // sample 1: 8 words
        bytes[7] = 64;   // its volume
        bytes[13] = 4;   // a loop of 4 words
        bytes[243] = 1;  // one order, pattern 0
        bytes[444] = 428 >> 8;
        bytes[445] = 428 & 0xFF;
        bytes[446] = 0x10;
        Array.Fill(bytes, (byte)64, 444 + 1024, 16);
        return bytes;
    }

    [Fact]
    public void TheAmigaMusicPlaysFromTheFilesAndTheOtherCommandsAreLeftToTheEffectsCard()
    {
        var files = Make("synthetic");
        foreach (var name in new[] { AmigaTunes.ThemeFile, AmigaTunes.FinalFile, AmigaTunes.SpyFile })
        {
            File.WriteAllBytes(Path.Combine(files, name), SyntheticModule());
        }
        var reports = new List<string>();
        var host = new DeviceHost();
        var sound = new SoundDriverChoice(new GameFolder(_root), host, reports.Add, musicLetter: SoundDriverChoice.AmigaMusic, effectsLetter: 'N', amigaFolder: files);
        sound.Choose('I');
        Assert.Empty(reports);
        var heard = new short[2000];
        host.Source!.Render(heard);
        Assert.All(heard, sample => Assert.Equal(0, sample));
        sound.PlaySound(2);
        host.Source!.Render(heard);
        Assert.Contains(heard, sample => sample != 0);
        sound.PlaySound(1);
        host.Source!.Render(heard);
        Assert.All(heard, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void WithoutTheFilesTheSoundCardPlaysTheMusicAndTheUserIsTold()
    {
        var reports = new List<string>();
        var host = new DeviceHost();
        var sound = new SoundDriverChoice(new GameFolder(_root), host, reports.Add, musicLetter: SoundDriverChoice.AmigaMusic, effectsLetter: 'N',
            amigaFolder: Path.Combine(_root, "nothing"));
        // the sound card is "none", whose driver plays nothing and needs no file
        sound.Choose('N');
        Assert.Single(reports);
        Assert.Contains("thememusic.dat", reports[0]);
        Assert.Contains("sound card's music plays instead", reports[0]);
    }

    [Fact]
    public void WithoutAFolderNamedTheFilesAreLookedForAndTheirAbsenceIsReported()
    {
        var reports = new List<string>();
        var sound = new SoundDriverChoice(new GameFolder(Make("nogame")), new DeviceHost(), reports.Add, musicLetter: SoundDriverChoice.AmigaMusic, effectsLetter: 'N');
        sound.Choose('N');
        Assert.Single(reports);
        Assert.Contains("not beside the program or the game", reports[0]);
    }
}
