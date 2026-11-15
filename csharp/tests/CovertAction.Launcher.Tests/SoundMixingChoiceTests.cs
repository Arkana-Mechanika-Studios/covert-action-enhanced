using CovertAction.Audio;
using CovertAction.Audio.Adlib;
using CovertAction.Audio.Speaker;
using CovertAction.Audio.Tandy;
using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The music and the effects on two drivers (D-620) with the player's own drivers: off, the one driver writes to its
/// device what it always did; on, each driver writes exactly what it would write alone with the commands of its part,
/// and the host hears the sum of the two. A Roland without ROMs plays the AdLib driver for its part (D-20). Skipped
/// without the game files; the MT-32 test also without COVERT_MT32_ROMS.
/// </summary>
public sealed class SoundMixingChoiceTests
{
    private const int Rate = 44100;

    /// <summary>A host whose sound device keeps the source it is given; the frames are rendered by the test.</summary>
    private sealed class DeviceHost : IHost, IPcmOutput, IClock
    {
        public IPcmSource? Source { get; private set; }

        public IVideo Video => throw new NotSupportedException();

        public IInput Input => throw new NotSupportedException();

        public IPcmOutput Audio => this;

        public IClock Clock => this;

        public int SampleRate => Rate;

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

    /// <summary>
    /// A stretch of play: commands (none waits, no 28) with frames rendered between them, the theme and the selection
    /// tune among effects, the engines, the gunfire, a pause, the end of the scene and the stop.
    /// </summary>
    private static readonly (int[] Command, int Frames)[] Script =
    [
        ([2], 4410), ([6], 4410), ([26, 2], 2205), ([3], 4410), ([44, 3, 2], 4410), ([39], 4410), ([13], 4410),
        ([37], 1000), ([38], 4410), ([35], 4410), ([36], 2205), ([1], 4410), ([0], 2205), ([41], 8820), ([7], 4410),
    ];

    private static GameFolder? GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "ASOUND.CA")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder;
    }

    private static string NoRoms => Path.Combine(Path.GetTempPath(), "covert-no-such-rom-folder");

    /// <summary>Plays the script's commands that <paramref name="keep"/> keeps on <paramref name="sound"/>; returns every frame the host's device got.</summary>
    private static short[] Play(ISoundDriver sound, Func<IPcmSource?> source, Func<int, bool> keep)
    {
        var heard = new List<short>();
        foreach (var (command, frames) in Script)
        {
            if (keep(command[0]))
            {
                switch (command.Length)
                {
                    case 1: sound.PlaySound(command[0]); break;
                    case 2: sound.PlaySound(command[0], command[1]); break;
                    default: sound.PlaySound(command[0], command[1], command[2]); break;
                }
            }
            var buffer = new short[2 * frames];
            source()?.Render(buffer);
            heard.AddRange(buffer);
        }
        return [.. heard];
    }

    /// <summary>The one card's driver as the set-up's choice (the option off), with what it does to its device.</summary>
    private static (List<SoundDeviceEvent> Events, short[] Heard) Alone(GameFolder folder, char letter, Func<int, bool> keep)
    {
        var host = new DeviceHost();
        var choice = new SoundDriverChoice(folder, host, _ => { }, NoRoms);
        var events = new List<SoundDeviceEvent>();
        choice.DeviceEvent += events.Add;
        choice.Choose(letter);
        return (events, Play(choice, () => host.Source, keep));
    }

    private static bool All(int command) => true;

    private static bool MusicPart(int command) => SoundCommands.RoleOf(command) != SoundRole.Effect;

    private static bool EffectsPart(int command) => SoundCommands.RoleOf(command) != SoundRole.Music;

    private static bool IsDeviceWrite(SoundDeviceEvent e) => e.Kind is not (SoundDeviceEventKind.TimerRate or SoundDeviceEventKind.TimerTick or SoundDeviceEventKind.TimerTickEnd);

    [SkippableTheory]
    [InlineData('I')]
    [InlineData('T')]
    [InlineData('A')]
    public void OffTheOneDriverWritesToItsDeviceWhatItAlwaysDid(char letter)
    {
        var folder = GameFiles();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder.Path, "TSOUND.CA")), "no game files");
        var (events, _) = Alone(folder!, letter, All);

        // the driver as the launcher made it before the option: the factory of its letter, watched
        var direct = new List<SoundDeviceEvent>();
        var clock = new DeviceHost();
        var driver = letter switch
        {
            'I' => IbmSoundDriver.OnSpeaker(SpeakerDriverFile.Read(folder!.Read("ISOUND.CA")), Rate, clock, direct.Add),
            'T' => TandySoundDriver.OnChip(folder!.Read("TSOUND.CA"), Rate, clock, direct.Add),
            _ => AdlibSoundDriver.OnOpl(AdlibDriverFile.Read(folder!.Read("ASOUND.CA")), Rate, clock, direct.Add),
        };
        Play(driver, () => driver, All);

        Assert.True(events.Count(IsDeviceWrite) > 100, "the script makes the driver write to its device");
        Assert.Equal(direct, events);
    }

    [SkippableFact]
    public void OnTheMusicAndTheEffectsReachOnlyTheirOwnDriversDeviceAndAreHeardTogether()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var host = new DeviceHost();
        var choice = new SoundDriverChoice(folder!, host, _ => { }, NoRoms, musicLetter: 'A', effectsLetter: 'I');
        var music = new List<SoundDeviceEvent>();
        var effects = new List<SoundDeviceEvent>();
        choice.PartDeviceEvent += (part, e) => (part == SoundRole.Music ? music : effects).Add(e);
        choice.Choose('N');
        var mixed = Play(choice, () => host.Source, All);

        // each driver did exactly what it does alone with the commands of its part
        var adlib = Alone(folder!, 'A', MusicPart);
        var speaker = Alone(folder!, 'I', EffectsPart);
        Assert.Equal(adlib.Events, music);
        Assert.Equal(speaker.Events, effects);
        Assert.Contains(music, e => e.Kind == SoundDeviceEventKind.OplRegister);
        Assert.DoesNotContain(music, e => e.Kind is SoundDeviceEventKind.SpeakerDivisor or SoundDeviceEventKind.SpeakerOn);
        Assert.Contains(effects, e => e.Kind == SoundDeviceEventKind.SpeakerDivisor);
        Assert.DoesNotContain(effects, e => e.Kind == SoundDeviceEventKind.OplRegister);

        // and the host heard both, added
        var sum = adlib.Heard.Zip(speaker.Heard, (a, b) => (short)Math.Clamp(a + b, short.MinValue, short.MaxValue)).ToArray();
        Assert.Equal(sum, mixed);
        Assert.True(adlib.Heard.Max(s => Math.Abs((int)s)) > 1000 && speaker.Heard.Max(s => Math.Abs((int)s)) > 1000, "both cards play");
    }

    [SkippableFact]
    public void AMusicCardOfItsOwnTakesTheSoundCardForTheEffects()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var host = new DeviceHost();
        var choice = new SoundDriverChoice(folder!, host, _ => { }, NoRoms, musicLetter: 'N');
        var effects = new List<SoundDeviceEvent>();
        choice.PartDeviceEvent += (part, e) => Assert.Equal(SoundRole.Effect, part);
        choice.PartDeviceEvent += (_, e) => effects.Add(e);
        // the set-up's answer is the AdLib: it plays the effects, and no driver the music
        choice.Choose('A');
        Play(choice, () => host.Source, All);
        Assert.Equal(Alone(folder!, 'A', EffectsPart).Events, effects);
    }

    [SkippableFact]
    public void ARolandWithoutRomsPlaysTheAdlibDriverForTheMusic()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var host = new DeviceHost();
        var lines = new List<string>();
        var choice = new SoundDriverChoice(folder!, host, lines.Add, NoRoms, musicLetter: 'R', effectsLetter: 'I');
        var music = new List<SoundDeviceEvent>();
        choice.PartDeviceEvent += (part, e) =>
        {
            if (part == SoundRole.Music)
            {
                music.Add(e);
            }
        };
        choice.Choose('N');
        Play(choice, () => host.Source, All);

        var line = Assert.Single(lines);
        Assert.StartsWith("warning: ", line, StringComparison.Ordinal);
        Assert.Contains("AdLib", line, StringComparison.Ordinal);
        Assert.Equal(Alone(folder!, 'A', MusicPart).Events, music);
        Assert.DoesNotContain(music, e => e.Kind is SoundDeviceEventKind.MidiByte or SoundDeviceEventKind.MpuCommand);
    }

    [SkippableFact]
    public void ARolandWithRomsPlaysTheMusicOnTheMt32AndTheEffectsOnTheAdlib()
    {
        var folder = GameFiles();
        Skip.If(folder is null, "no game files");
        var roms = Environment.GetEnvironmentVariable("COVERT_MT32_ROMS");
        Skip.If(string.IsNullOrEmpty(roms) || !Directory.Exists(roms), "no MT-32 ROMs (COVERT_MT32_ROMS)");
        var host = new DeviceHost();
        var lines = new List<string>();
        var choice = new SoundDriverChoice(folder!, host, lines.Add, roms, musicLetter: 'R', effectsLetter: 'A');
        var parts = new List<(SoundRole Part, SoundDeviceEvent Event)>();
        choice.PartDeviceEvent += (part, e) => parts.Add((part, e));
        choice.Choose('N');
        var heard = Play(choice, () => host.Source, All);

        Assert.Empty(lines);
        Assert.Contains(parts, p => p.Part == SoundRole.Music && p.Event.Kind == SoundDeviceEventKind.MidiByte);
        Assert.DoesNotContain(parts, p => p.Part == SoundRole.Music && p.Event.Kind == SoundDeviceEventKind.OplRegister);
        Assert.Contains(parts, p => p.Part == SoundRole.Effect && p.Event.Kind == SoundDeviceEventKind.OplRegister);
        Assert.DoesNotContain(parts, p => p.Part == SoundRole.Effect && p.Event.Kind == SoundDeviceEventKind.MidiByte);
        Assert.True(heard.Max(s => Math.Abs((int)s)) > 1000, "the mix is heard");
    }
}
