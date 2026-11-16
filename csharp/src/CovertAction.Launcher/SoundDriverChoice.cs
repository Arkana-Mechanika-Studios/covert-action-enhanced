using CovertAction.Audio;
using CovertAction.Audio.Adlib;
using CovertAction.Audio.Amiga;
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
/// As a player's option the tunes and the sound effects play on two drivers at once (D-620, csharp/docs/SOUND-MIXING.md),
/// and either may be the Amiga version's, from the player's own Amiga files (D-640 the music, D-641 the sound effects).
/// </remarks>
/// <param name="folder">The player's game folder.</param>
/// <param name="host">The host, whose sound device plays the driver.</param>
/// <param name="report">Where the one line about missing MT-32 ROMs goes; null: the console's error output.</param>
/// <param name="romFolder">The MT-32 ROMs' folder; null: COVERT_MT32_ROMS, else the folder RomFolderSearch finds (MT32_ROMS next to the program, in or by the game folder, DOSBox's ...).</param>
/// <param name="musicLetter">The driver of the tunes when the music and the effects play on two cards (D-620); null: the chosen card's.</param>
/// <param name="effectsLetter">The driver of the sound effects on two cards (D-620); null: the chosen card's. Both null: one card plays all, as the original.</param>
/// <param name="amigaFolder">The folder with the player's Amiga files, for <see cref="AmigaMusic"/> as the music's or the effects' letter (D-640, D-641); null: found beside the program or the game.</param>
/// <param name="amigaOnlyWhereOriginal">The Amiga music and effects play only where the Amiga game had them (D-640, D-641): the music's commands 39 to 43 and the effects the Amiga has no twin for play nothing.</param>
/// <param name="fixTandySecondPause">The Tandy driver ignores a second pause with no resume between (CA-55, the option "Fix the original's bugs").</param>
internal sealed class SoundDriverChoice(GameFolder folder, IHost host, Action<string>? report = null, string? romFolder = null,
    char? musicLetter = null, char? effectsLetter = null, string? amigaFolder = null, bool amigaOnlyWhereOriginal = false,
    bool fixTandySecondPause = false) : ISoundDriver
{
    private const char Ibm = 'I';
    private const string IbmDriverFile = "ISOUND.CA";
    private const char Tandy = 'T';
    private const string TandyDriverFileName = "TSOUND.CA";
    private const char Adlib = 'A';
    private const string AdlibDriverFile = "ASOUND.CA";
    private const char Roland = 'R';

    /// <summary>The letter for the Amiga version's music (D-640) and sound effects (D-641): not a sound card of the set-up, a driver of its own.</summary>
    public const char AmigaMusic = 'G';

    private const string RolandDriverFile = "RSOUND.CA";

    /// <summary>The ROM folder's variable; else MT32_ROMS next to the program (Darklands: DARKLANDS_MT32_ROMS).</summary>
    private const string RomFolderVariable = "COVERT_MT32_ROMS";

    private ISoundDriver _driver = new SilentSoundDriver();
    private bool _chosen;
    private AmigaEffectsRouter? _amigaEffects;
    private AmigaBank? _amigaBank;

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
            ChooseTwo(musicLetter ?? letter, effectsLetter ?? letter, letter);
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
    /// <param name="music">The letter of the music's driver; <see cref="AmigaMusic"/> for the Amiga version's music.</param>
    /// <param name="effects">The letter of the effects' driver; <see cref="AmigaMusic"/> for the Amiga version's effects.</param>
    /// <param name="card">The sound card's letter: it plays when the Amiga files cannot be read, and the effects the Amiga has no twin for.</param>
    private void ChooseTwo(char music, char effects, char card)
    {
        // DIVERGENCE: D-620 a player's option: the music and the sound effects on two drivers at once, mixed.
        ISoundDriver? musicDriver = music == AmigaMusic ? MakeAmigaMusic(card, PartWatch(SoundRole.Music)) : Make(music, PartWatch(SoundRole.Music));
        var effectsDriver = effects == AmigaMusic ? MakeAmigaEffects(card, PartWatch(SoundRole.Effect)) : Make(effects, PartWatch(SoundRole.Effect));
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
        Tandy => TandySoundDriver.OnChip(folder.Read(TandyDriverFileName), host.Audio.SampleRate, host.Clock, watch, fixTandySecondPause),
        Adlib => OnAdlib(watch),
        Roland => OnRoland(watch),
        // DIVERGENCE: D-18 the Custom choice plays nothing.
        _ => null,
    };

    /// <summary>
    /// The Amiga version's music from the player's files, or, when they cannot be found or read, the sound card's driver in its
    /// place with one line to say so (as the MT-32 without ROMs, D-20).
    /// </summary>
    private ISoundDriver? MakeAmigaMusic(char card, Action<SoundDeviceEvent>? watch)
    {
        // DIVERGENCE: D-640 a player's option: the music is the Amiga version's, played from the player's own Amiga files.
        try
        {
            var found = amigaFolder is { Length: > 0 } named ? AmigaFolderSearch.Resolve(named) : AmigaFolderSearch.Automatic(folder.Path)?.Folder;
            if (found is null)
            {
                throw new FileNotFoundException(amigaFolder is { Length: > 0 } ? $"{AmigaTunes.ThemeFile} is not in {amigaFolder}" : $"{AmigaTunes.ThemeFile} is not beside the program or the game");
            }
            return new AmigaMusicDriver(AmigaTunes.Read(found), host.Audio.SampleRate, amigaOnlyWhereOriginal);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            (report ?? Console.Error.WriteLine)($"warning: the Amiga music needs your Amiga files, but {e.Message}; the sound card's music plays instead.");
            return card == AmigaMusic ? null : Make(card, watch);
        }
    }

    /// <summary>
    /// The Amiga version's sound effects from the player's files, the commands without an Amiga twin on the sound card's driver
    /// (or silent), or, when the files cannot be found or read, the sound card's driver for all, with one line to say so.
    /// </summary>
    private ISoundDriver? MakeAmigaEffects(char card, Action<SoundDeviceEvent>? watch)
    {
        // DIVERGENCE: D-641 a player's option: the sound effects are the Amiga version's, played from the player's own Amiga files.
        try
        {
            var banks = amigaFolder is { Length: > 0 } named ? AmigaFolderSearch.ResolveBanks(named) : AmigaFolderSearch.AutomaticBanks(folder.Path)?.Folder;
            if (banks is null)
            {
                throw new FileNotFoundException(amigaFolder is { Length: > 0 } ? $"{AmigaSoundBanks.FileNames[0]} is not in {amigaFolder}" : $"{AmigaSoundBanks.FileNames[0]} is not beside the program or the game");
            }
            var read = AmigaSoundBanks.Read(name => File.ReadAllBytes(AmigaFolderSearch.BankFile(banks, name) ?? throw new FileNotFoundException($"{name} is not around {banks}")));
            var fillIn = amigaOnlyWhereOriginal || card == AmigaMusic ? null : Make(card, watch);
            return _amigaEffects = new AmigaEffectsRouter(new AmigaEffectsDriver(read, host.Audio.SampleRate) { Bank = _amigaBank }, fillIn);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            (report ?? Console.Error.WriteLine)($"warning: the Amiga sound effects need your Amiga files, but {e.Message}; the sound card's effects play instead.");
            return card == AmigaMusic ? null : Make(card, watch);
        }
    }

    /// <summary>
    /// The part of the game that plays now, for the Amiga sound effects (D-641): the part's bank gives the PC's sound commands their
    /// Amiga meaning, and a part change silences the Amiga's voices. Null: a part the Amiga plays no effects in (FINAL, the menus).
    /// </summary>
    public AmigaBank? AmigaBank
    {
        get => _amigaBank;
        set
        {
            _amigaBank = value;
            if (_amigaEffects is not null)
            {
                _amigaEffects.Bank = value;
            }
        }
    }

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
