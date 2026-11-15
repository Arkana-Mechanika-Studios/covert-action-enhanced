using CovertAction.Audio;
using CovertAction.Audio.Adlib;
using CovertAction.Audio.Mt32;
using CovertAction.Audio.Roland;
using CovertAction.Audio.Speaker;
using CovertAction.Audio.Tandy;
using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Platform;

namespace CovertAction.Launcher;

/// <summary>
/// The sound driver the set-up chose: silent until the choice is made, then the driver of the chosen letter (the
/// hand-off block's sound letter, SHARED+0x03), whose sounds are read from the player's driver file and played on
/// the host's sound device.
/// </summary>
/// <remarks>
/// New code: the original's launcher loads the program ?SOUND.CA of that letter and calls its start (D-602). Of the
/// set-up's choices the PC speaker, the Tandy chip, the AdLib card and the Roland MT-32 are reconstructed; Custom plays
/// nothing (D-18). The MT-32 is Munt's emulation with the player's ROMs; without them the AdLib driver plays (D-20).
/// As a player's option the tunes and the sound effects play on two drivers at once (D-620, csharp/docs/SOUND-MIXING.md).
/// </remarks>
/// <param name="folder">The player's game folder.</param>
/// <param name="host">The host, whose sound device plays the driver.</param>
/// <param name="report">Where the one line about missing MT-32 ROMs goes; null: the console's error output.</param>
/// <param name="romFolder">The MT-32 ROMs' folder; null: COVERT_MT32_ROMS, else the folder RomFolderSearch finds (MT32_ROMS next to the program, in or by the game folder, DOSBox's ...).</param>
/// <param name="musicLetter">The driver of the tunes when the music and the effects play on two cards (D-620); null: the chosen card's.</param>
/// <param name="effectsLetter">The driver of the sound effects on two cards (D-620); null: the chosen card's. Both null: one card plays all, as the original.</param>
internal sealed class SoundDriverChoice(GameFolder folder, IHost host, Action<string>? report = null, string? romFolder = null,
    char? musicLetter = null, char? effectsLetter = null) : ISoundDriver
{
    private const char Ibm = 'I';
    private const string IbmDriverFile = "ISOUND.CA";
    private const char Tandy = 'T';
    private const string TandyDriverFileName = "TSOUND.CA";
    private const char Adlib = 'A';
    private const string AdlibDriverFile = "ASOUND.CA";
    private const char Roland = 'R';
    private const string RolandDriverFile = "RSOUND.CA";

    /// <summary>The ROM folder's variable; else MT32_ROMS next to the program (Darklands: DARKLANDS_MT32_ROMS).</summary>
    private const string RomFolderVariable = "COVERT_MT32_ROMS";

    private ISoundDriver _driver = new SilentSoundDriver();
    private bool _chosen;

    /// <summary>Loads the driver of a sound letter; only the first choice counts (the host's device is started once).</summary>
    /// <param name="letter">The set-up's letter: N no sound, I IBM (PC speaker), T Tandy, A AdLib, R Roland, X Custom.</param>
    /// <exception cref="FileNotFoundException">The driver file is not in the game folder.</exception>
    /// <exception cref="InvalidDataException">The driver file is not the driver.</exception>
    public void Choose(char letter)
    {
        if (_chosen)
        {
            return;
        }
        _chosen = true;
        if (musicLetter is not null || effectsLetter is not null)
        {
            ChooseTwo(musicLetter ?? letter, effectsLetter ?? letter);
            return;
        }
        var driver = Make(letter, Watch);
        if (driver is not null)
        {
            host.Audio.Start(driver);
            _driver = driver;
        }
    }

    /// <summary>
    /// The tunes on the driver of one letter, the effects on the driver of another, each on a device of its own, the
    /// two mixed into the host's sound device; the game's commands go to one, the other or both by their role.
    /// </summary>
    private void ChooseTwo(char music, char effects)
    {
        // DIVERGENCE: D-620 a player's option: the music and the sound effects on two drivers at once, mixed.
        var musicDriver = Make(music, PartWatch(SoundRole.Music));
        var effectsDriver = Make(effects, PartWatch(SoundRole.Effect));
        var sources = new[] { musicDriver, effectsDriver }.OfType<IPcmSource>().ToArray();
        if (sources.Length > 0)
        {
            host.Audio.Start(sources.Length == 1 ? sources[0] : new PcmMixer(sources));
        }
        _driver = new SplitSoundDriver(musicDriver, effectsDriver);
    }

    /// <summary>The driver of a letter, started on a device of its own, or null for none (N) and Custom.</summary>
    private ClockedSoundDriver? Make(char letter, Action<SoundDeviceEvent>? watch) => letter switch
    {
        Ibm => IbmSoundDriver.OnSpeaker(SpeakerDriverFile.Read(folder.Read(IbmDriverFile)), host.Audio.SampleRate, host.Clock, watch),
        Tandy => TandySoundDriver.OnChip(folder.Read(TandyDriverFileName), host.Audio.SampleRate, host.Clock, watch),
        Adlib => OnAdlib(watch),
        Roland => OnRoland(watch),
        // DIVERGENCE: D-18 the Custom choice plays nothing.
        _ => null,
    };

    private ClockedSoundDriver OnAdlib(Action<SoundDeviceEvent>? watch) =>
        AdlibSoundDriver.OnOpl(Content.AdlibDriverFile.Read(folder.Read(AdlibDriverFile)), host.Audio.SampleRate, host.Clock, watch);

    /// <summary>The Roland driver on the MT-32 emulation with the player's ROMs (an MT-32's, else a CM-32L's).</summary>
    private ClockedSoundDriver OnRoland(Action<SoundDeviceEvent>? watch)
    {
        var driverFile = Content.RolandDriverFile.Read(folder.Read(RolandDriverFile));
        var roms = romFolder ?? RomFolder();
        var rate = host.Audio.SampleRate;
        var synth = Mt32Synth.TryOpen(roms, RolandModule.Mt32, rate, out var message) ?? Mt32Synth.TryOpen(roms, RolandModule.Cm32l, rate, out _);
        if (synth is null)
        {
            // DIVERGENCE: D-20 without MT-32 ROMs the AdLib driver plays (as the Darklands Restoration Project does).
            (report ?? Console.Error.WriteLine)($"warning: the Roland MT-32 needs the player's MT-32 ROMs, but {message}; the AdLib driver plays instead.");
            return OnAdlib(watch);
        }
        return RolandSoundDriver.OnMidi(driverFile, synth, rate, host.Clock, watch);
    }

    /// <summary>COVERT_MT32_ROMS, else the folder <see cref="RomFolderSearch.Automatic"/> finds (MT32_ROMS next to the program when none holds a set).</summary>
    private string RomFolder() =>
        Environment.GetEnvironmentVariable(RomFolderVariable) is { Length: > 0 } chosen ? chosen : RomFolderSearch.Automatic(folder.Path).Folder;

    /// <summary>Raised with the arguments of every sound the game asks for, whatever driver plays it (for tests and tools: the session proof compares them with the original's).</summary>
    public event Action<int[]>? Requested;

    /// <summary>
    /// Raised with everything the chosen driver does to its device and to the PC timer (for tests and tools: the session
    /// proof compares it with the original's port writes). Only a driver chosen while something listens tells it.
    /// </summary>
    public event Action<SoundDeviceEvent>? DeviceEvent;

    /// <summary>What the factories are given to tell <see cref="DeviceEvent"/>: null while nothing listens.</summary>
    private Action<SoundDeviceEvent>? Watch => DeviceEvent is null ? null : e => DeviceEvent?.Invoke(e);

    /// <summary>
    /// With the music and the effects on two drivers (D-620): raised with everything each of them does to its device and
    /// its timer, and which of the two did it (for tests and tools); <see cref="DeviceEvent"/> is told the same, untagged.
    /// Only drivers chosen while something listens tell it.
    /// </summary>
    public event Action<SoundRole, SoundDeviceEvent>? PartDeviceEvent;

    /// <summary>What the factory of one of the two drivers is given: null while nothing listens.</summary>
    private Action<SoundDeviceEvent>? PartWatch(SoundRole part) => DeviceEvent is null && PartDeviceEvent is null ? null : e =>
    {
        PartDeviceEvent?.Invoke(part, e);
        DeviceEvent?.Invoke(e);
    };

    /// <inheritdoc/>
    public void PlaySound(int effect)
    {
        Requested?.Invoke([effect]);
        _driver.PlaySound(effect);
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter)
    {
        Requested?.Invoke([effect, parameter]);
        _driver.PlaySound(effect, parameter);
    }

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant)
    {
        Requested?.Invoke([effect, mode, variant]);
        _driver.PlaySound(effect, mode, variant);
    }
}
