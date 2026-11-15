using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The set-up's Roland choice (<see cref="SoundDriverChoice"/>): with the player's MT-32 ROMs the Roland driver plays on
/// the emulation; without them one line says so and the AdLib driver plays instead (D-20). Skipped without the game
/// files; the ROM test also without COVERT_MT32_ROMS.
/// </summary>
public sealed class SoundChoiceTests
{
    /// <summary>A host whose sound device keeps the source it is given, and renders it on demand.</summary>
    private sealed class RecordingHost : IHost, IPcmOutput, IClock
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

        /// <summary>The loudest sample of a second of the source.</summary>
        public int Loudest()
        {
            var buffer = new short[2 * SampleRate];
            Source!.Render(buffer);
            return buffer.Max(s => Math.Abs((int)s));
        }
    }

    private static GameFolder? GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "RSOUND.CA")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder;
    }

    [SkippableFact]
    public void TheTandyChoicePlaysTheThemeOnTheSoundChip()
    {
        var folder = GameFiles();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder.Path, "TSOUND.CA")), "no game files");
        var host = new RecordingHost();
        var choice = new SoundDriverChoice(folder!, host, _ => { });

        choice.Choose('T');
        choice.PlaySound(2);

        Assert.True(host.Loudest() > 1000, "the Tandy driver plays the theme");
    }

    [SkippableFact]
    public void TheRolandWithoutRomsSaysSoInOneLineAndPlaysTheAdlibDriver()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var host = new RecordingHost();
        var lines = new List<string>();
        var choice = new SoundDriverChoice(folder!, host, lines.Add, romFolder: Path.Combine(Path.GetTempPath(), "covert-no-such-rom-folder"));

        choice.Choose('R');
        choice.PlaySound(2);

        var line = Assert.Single(lines);
        Assert.StartsWith("warning: ", line, StringComparison.Ordinal);
        Assert.Contains("AdLib", line, StringComparison.Ordinal);
        Assert.True(host.Loudest() > 3000, "the AdLib driver plays the theme");
    }

    [SkippableFact]
    public void TheRolandWithRomsPlaysTheThemeOnTheMt32()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var roms = Environment.GetEnvironmentVariable("COVERT_MT32_ROMS");
        Skip.If(string.IsNullOrEmpty(roms) || !Directory.Exists(roms), "no MT-32 ROMs (COVERT_MT32_ROMS)");
        var host = new RecordingHost();
        var lines = new List<string>();
        var choice = new SoundDriverChoice(folder!, host, lines.Add, romFolder: roms);

        choice.Choose('R');
        choice.PlaySound(2);

        Assert.Empty(lines);
        Assert.True(host.Loudest() > 1000, "the MT-32 plays the theme");
    }
}
