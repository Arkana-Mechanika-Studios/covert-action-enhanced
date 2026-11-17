using CovertAction.Audio.Amiga;
using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga sound effects option (D-641) with the player's own files and folders: the five banks read whole (skipped without
/// COVERT_AMIGA_DIR, the folder with the Amiga disks' folders, or with the banks), where the banks are looked for, and the
/// fall back to the sound card without them. Nothing of an Amiga file is kept in the repository.
/// </summary>
public sealed class AmigaEffectsFilesTests : IDisposable
{
    private const string FolderVariable = "COVERT_AMIGA_DIR";

    /// <summary>Where a test may write the effects as WAV files to listen to (outside the repository); unset: none are written.</summary>
    private const string WavFolderVariable = "COVERT_AMIGA_WAV_DIR";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "covert-amiga-fx-" + Guid.NewGuid().ToString("N"));

    public AmigaEffectsFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

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

    /// <summary>The five banks as the Amiga's disks hold them: four on disk 2, the chase's on disk 3.</summary>
    private string MakeDisks(params string[] below)
    {
        var disk2 = Make([.. below, "disk2"]);
        Touch(disk2, "gamesnd.dat", "tacsnd.dat", "codesnd.dat", "bugsnd.dat");
        Touch(Make([.. below, "disk3"]), "chasesnd.dat");
        return disk2;
    }

    // where the banks are

    [Fact]
    public void TheBanksAreFoundBesideTheProgramBelowTheDisksFolderOrFlat()
    {
        var program = Make("program");
        Assert.Null(AmigaFolderSearch.FindBanks(null, program));
        var disk2 = MakeDisks("program", "amiga", "files");
        Assert.Equal((disk2, "beside the program"), AmigaFolderSearch.FindBanks(null, program));
        Assert.Empty(AmigaFolderSearch.MissingBanks(disk2));
        var flat = Make("other", "amiga");
        Touch(flat, AmigaSoundBanks.FileNames.ToArray());
        Assert.Equal((flat, "beside the program"), AmigaFolderSearch.FindBanks(null, Path.Combine(_root, "other")));
    }

    [Fact]
    public void TheBanksAreFoundNearTheGame()
    {
        var program = Make("program");
        var game = Make("store", "Covert Action", "Covert");
        Assert.Null(AmigaFolderSearch.FindBanks(game, program));
        var disk2 = MakeDisks("store", "amiga", "files");
        Assert.Equal((disk2, "next to the game"), AmigaFolderSearch.FindBanks(game, program));
    }

    [Fact]
    public void AFolderTheUserNamesCanBeTheDisksFolderADiskOrTheBanksThemselves()
    {
        var disk2 = MakeDisks("mine");
        Make("mine", "disk1");
        // the folder with the disks' folders in it
        Assert.Equal(disk2, AmigaFolderSearch.ResolveBanks(Path.Combine(_root, "mine")));
        // disk 2 itself, and disk 1 beside it
        Assert.Equal(disk2, AmigaFolderSearch.ResolveBanks(disk2));
        Assert.Equal(disk2, AmigaFolderSearch.ResolveBanks(Path.Combine(_root, "mine", "disk1")));
        // a folder that holds none
        Assert.Null(AmigaFolderSearch.ResolveBanks(Make("empty")));
        Assert.Null(AmigaFolderSearch.ResolveBanks(Path.Combine(_root, "nothing")));
    }

    [Fact]
    public void TheChaseBankIsFoundOnDisk3BesideDisk2AndMissingBanksAreNamed()
    {
        var disk2 = MakeDisks("mine");
        Assert.Equal(Path.Combine(_root, "mine", "disk3", "chasesnd.dat"), AmigaFolderSearch.BankFile(disk2, "chasesnd.dat"));
        File.Delete(Path.Combine(_root, "mine", "disk3", "chasesnd.dat"));
        File.Delete(Path.Combine(disk2, "bugsnd.dat"));
        Assert.Equal(["bugsnd.dat", "chasesnd.dat"], AmigaFolderSearch.MissingBanks(disk2));
        Assert.Equal(AmigaSoundBanks.FileNames, AmigaFolderSearch.MissingBanks(null));
    }

    // the choice of the driver

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

    /// <summary>A bank of <paramref name="count"/> effects, each a loud click, written as the Amiga's files are.</summary>
    private static byte[] SyntheticBank(int count)
    {
        var table = 4 + 10 * count;
        var data = table + 8 * count;
        var file = new List<byte>();
        void Word(int value) => file.AddRange([(byte)(value >> 8), (byte)value]);
        void Long(int value) => file.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        Word(count);
        Word(count);
        for (var i = 0; i < count; i++)
        {
            Long(table + 8 * i - (4 + 10 * i));
            Long(table + 8 * i - (4 + 10 * i));
            Word(50);
        }
        for (var i = 0; i < count; i++)
        {
            Word(300);
            Long(data + i * 204 - (table + 8 * i));
            Word(2);
        }
        for (var i = 0; i < count; i++)
        {
            Long(200);
            file.AddRange(Enumerable.Repeat((byte)100, 200));
        }
        return [.. file];
    }

    private string MakeSyntheticBanks()
    {
        var disk2 = Make("synthetic", "disk2");
        var disk3 = Make("synthetic", "disk3");
        foreach (var name in new[] { "gamesnd.dat", "tacsnd.dat", "codesnd.dat", "bugsnd.dat" })
        {
            File.WriteAllBytes(Path.Combine(disk2, name), SyntheticBank(14));
        }
        File.WriteAllBytes(Path.Combine(disk3, "chasesnd.dat"), SyntheticBank(14));
        return Path.Combine(_root, "synthetic");
    }

    private static bool Loud(IPcmSource source)
    {
        var heard = new short[4000];
        source.Render(heard);
        return heard.Any(sample => Math.Abs(sample) > 500);
    }

    [Fact]
    public void TheAmigaEffectsPlayFromTheFilesInThePartsBankAndTheOtherCommandsAreLeftToTheCard()
    {
        var reports = new List<string>();
        var host = new DeviceHost();
        var sound = new SoundDriverChoice(new GameFolder(_root), host, reports.Add, musicLetter: 'N', effectsLetter: SoundDriverChoice.AmigaMusic,
            amigaFolder: MakeSyntheticBanks(), amigaOnlyWhereOriginal: true)
        {
            AmigaBank = CovertAction.Audio.Amiga.AmigaBank.Tac,
        };
        sound.Choose('N');
        Assert.Empty(reports);
        Assert.False(Loud(host.Source!));
        // the camera is tac[7]
        sound.PlaySound(9);
        Assert.True(Loud(host.Source!));
        sound.PlaySound(0);
        Assert.False(Loud(host.Source!));
        // gunfire has no Amiga twin: with the card "none" and "only where the Amiga had it" nothing plays
        sound.PlaySound(44, 1, 1);
        Assert.False(Loud(host.Source!));
        // another part: the same number is another sound, or none (14 is TAC's door, nothing in the electronics)
        sound.AmigaBank = CovertAction.Audio.Amiga.AmigaBank.Bug;
        sound.PlaySound(14);
        Assert.False(Loud(host.Source!));
        sound.PlaySound(28);
        Assert.True(Loud(host.Source!));
    }

    [Fact]
    public void WithoutTheBanksTheSoundCardPlaysTheEffectsAndTheUserIsTold()
    {
        var reports = new List<string>();
        var sound = new SoundDriverChoice(new GameFolder(_root), new DeviceHost(), reports.Add, musicLetter: 'N', effectsLetter: SoundDriverChoice.AmigaMusic,
            amigaFolder: Path.Combine(_root, "nothing"));
        sound.Choose('N');
        Assert.Single(reports);
        Assert.Contains("gamesnd.dat", reports[0]);
        Assert.Contains("sound card's effects play instead", reports[0]);
    }

    [Fact]
    public void ABankMissingFromAFolderIsReportedByName()
    {
        var folder = MakeSyntheticBanks();
        File.Delete(Path.Combine(folder, "disk3", "chasesnd.dat"));
        var reports = new List<string>();
        var sound = new SoundDriverChoice(new GameFolder(_root), new DeviceHost(), reports.Add, musicLetter: 'N', effectsLetter: SoundDriverChoice.AmigaMusic, amigaFolder: folder);
        sound.Choose('N');
        Assert.Single(reports);
        Assert.Contains("chasesnd.dat", reports[0]);
    }

    // the real files

    private static string? RealFolder() => Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    private static AmigaSoundBanks ReadReal(string folder)
    {
        var banks = AmigaFolderSearch.ResolveBanks(folder);
        Assert.NotNull(banks);
        return AmigaSoundBanks.Read(name => File.ReadAllBytes(AmigaFolderSearch.BankFile(banks!, name)!));
    }

    [SkippableFact]
    public void TheFiveBanksParseAndEndExactlyAtTheirLastSample()
    {
        var folder = RealFolder();
        Skip.If(folder is null, "no COVERT_AMIGA_DIR");
        var banks = AmigaFolderSearch.ResolveBanks(folder!);
        Assert.NotNull(banks);
        Assert.Empty(AmigaFolderSearch.MissingBanks(banks));
        // the table of csharp/docs/AMIGA-EFFECTS.md: the effects and the samples of each bank
        var expected = new[] { ("gamesnd.dat", 4, 6), ("tacsnd.dat", 14, 14), ("codesnd.dat", 4, 4), ("bugsnd.dat", 3, 3), ("chasesnd.dat", 9, 11) };
        foreach (var (name, effects, samples) in expected)
        {
            var path = AmigaFolderSearch.BankFile(banks!, name)!;
            var bytes = File.ReadAllBytes(path);
            var bank = AmigaSoundBank.Parse(bytes);
            Assert.Equal((effects, samples), (bank.Effects.Count, bank.Samples.Count));
            Assert.Equal(bytes.Length, bank.End);
            Assert.All(bank.Effects, effect => Assert.InRange(effect.Priority, 1, 255));
            Assert.All(bank.Samples, sample => Assert.True(sample.Period is >= 124 and <= 1000 && sample.Data.Length > 0 && sample.Repeats is 0 or 2 or 3));
        }
    }

    [SkippableFact]
    public void EveryRowOfTheTableNamesAnEffectTheBankHas()
    {
        var folder = RealFolder();
        Skip.If(folder is null, "no COVERT_AMIGA_DIR");
        var banks = ReadReal(folder!);
        foreach (var row in AmigaEffectMap.Rows.Where(r => r.Match != AmigaMatch.None))
        {
            Assert.True(row.Index < banks.Of(row.Bank).Effects.Count, $"{row.Bank} {row.Command} {row.Name}");
        }
    }

    [SkippableFact]
    public void TheMappedEffectsAreWrittenAsWavFilesWhenAskedTo()
    {
        var folder = RealFolder();
        var target = Environment.GetEnvironmentVariable(WavFolderVariable);
        Skip.If(folder is null || string.IsNullOrEmpty(target), "no COVERT_AMIGA_DIR or COVERT_AMIGA_WAV_DIR");
        var banks = ReadReal(folder!);
        Directory.CreateDirectory(target!);
        const int rate = 44100;
        foreach (var row in AmigaEffectMap.Rows.Where(r => r.Match != AmigaMatch.None))
        {
            var driver = new AmigaEffectsDriver(banks, rate) { Bank = row.Bank };
            driver.PlaySound(row.Command);
            // up to three seconds (an endless effect is cut), then a short tail
            var frames = new List<short>();
            var chunk = new short[2 * rate / 20];
            while (driver.Playing && frames.Count < 2 * rate * 3)
            {
                driver.Render(chunk);
                frames.AddRange(chunk);
            }
            driver.Render(chunk);
            frames.AddRange(chunk);
            WriteWav(Path.Combine(target!, $"{row.Command:00}-{row.Bank.ToString().ToLowerInvariant()}{row.Index}.wav"), [.. frames], rate);
        }
    }

    [SkippableFact]
    public void ARaidsEffectsInOrderAreWrittenAsOneStereoWavFileWhenAskedTo()
    {
        var folder = RealFolder();
        var target = Environment.GetEnvironmentVariable(WavFolderVariable);
        Skip.If(folder is null || string.IsNullOrEmpty(target), "no COVERT_AMIGA_DIR or COVERT_AMIGA_WAV_DIR");
        var banks = ReadReal(folder!);
        Directory.CreateDirectory(target!);
        const int rate = 44100;
        var driver = new AmigaEffectsDriver(banks, rate) { Bank = AmigaBank.Tac };
        // a door, the camera, a shot that misses the player (x = 100, a little to the left), a guard hit, the siren, a blast
        // with the player's own hit over it, as the Amiga's calls name them (D-680)
        var sequence = new (int Command, double Seconds)[] { (13, 0.8), (9, 0.8), (33, 0.5), (6, 0.4), (4, 0.6), (18, 1.0), (7, 1.0), (3, 0.5), (14, 1.0) };
        var frames = new List<short>();
        foreach (var (command, seconds) in sequence)
        {
            driver.PlaySound(command);
            var chunk = new short[2 * (int)(rate * seconds)];
            driver.Render(chunk);
            frames.AddRange(chunk);
        }
        var tail = new short[2 * rate];
        driver.Render(tail);
        frames.AddRange(tail);
        WriteWav(Path.Combine(target!, "raid-sequence.wav"), [.. frames], rate);
    }

    private static void WriteWav(string path, short[] frames, int rate)
    {
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
}
