using CovertAction.Platform;

namespace CovertAction.Audio.Amiga;

/// <summary>The three tunes of the Amiga version, read from the player's own files.</summary>
/// <param name="Theme">thememusic.dat: the title and the menus.</param>
/// <param name="Final">finalmusic.dat: FINAL's other scenes; the same samples as the theme, another order list.</param>
/// <param name="Spy">spy6music.dat: the second tune.</param>
public sealed record AmigaTunes(GmcTune Theme, GmcTune Final, GmcTune Spy)
{
    /// <summary>File name of <see cref="Theme"/>.</summary>
    public const string ThemeFile = "thememusic.dat";

    /// <summary>File name of <see cref="Final"/>.</summary>
    public const string FinalFile = "finalmusic.dat";

    /// <summary>File name of <see cref="Spy"/>.</summary>
    public const string SpyFile = "spy6music.dat";

    /// <summary>Reads the three tunes from a folder that holds the three files.</summary>
    /// <param name="folder">The folder with thememusic.dat, finalmusic.dat and spy6music.dat.</param>
    /// <exception cref="FileNotFoundException">A file is not there.</exception>
    /// <exception cref="InvalidDataException">A file is not a Game Music Creator module.</exception>
    public static AmigaTunes Read(string folder)
    {
        GmcTune Load(string name)
        {
            try
            {
                return GmcTune.Parse(File.ReadAllBytes(Path.Combine(folder, name)));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{name}: {e.Message}", e);
            }
        }
        return new AmigaTunes(Load(ThemeFile), Load(FinalFile), Load(SpyFile));
    }
}

/// <summary>
/// The Amiga version's music as the game's music driver (D-640): the game's sound commands for the tunes start the Amiga's
/// tunes, rendered by <see cref="GmcPlayer"/>. Command 2 (the theme: the title, the menus, the debriefing) plays the
/// theme; 39 (the selection screens' tune) plays the second tune; the four vacations' tunes 40 to 43 alternate the second
/// tune and FINAL's tune. 0 and 1 stop; 37 and 38 pause and resume. The tunes repeat until stopped, as the PC's theme does.
/// </summary>
/// <remarks>
/// New code: the Amiga game plays its music on title and main menus (the theme), on FINAL's other scenes (FINAL's tune) and on one
/// of FINAL's pop-ups (the second tune); TAC, CHASE, BUG and CODE have none (csharp/docs/AMIGA-MUSIC.md). The PC's commands 39 to 43
/// have no twin there: they are mapped as above, or, with <c>onlyWhereTheAmigaHadMusic</c>, left silent. FINAL's debriefing sends the
/// same command 2 as its menus, so the driver cannot tell it apart and the theme plays there. The device's thread renders; the game's
/// commands take turns with it under one lock.
/// </remarks>
public sealed class AmigaMusicDriver : ISoundDriver, IPcmSource
{
    private readonly object _lock = new();
    private readonly AmigaTunes _tunes;
    private readonly int _sampleRate;
    private readonly bool _onlyWhereTheAmigaHadMusic;
    private readonly bool _lowPass;
    private GmcPlayer? _player;
    private bool _paused;

    /// <summary>Makes the driver; nothing plays until the game asks for a tune.</summary>
    /// <param name="tunes">The Amiga's tunes.</param>
    /// <param name="sampleRate">The sound device's frames a second.</param>
    /// <param name="onlyWhereTheAmigaHadMusic">The commands 39 to 43, which the Amiga has no music for, play nothing.</param>
    /// <param name="lowPass">The A500's low-pass filter is applied.</param>
    public AmigaMusicDriver(AmigaTunes tunes, int sampleRate, bool onlyWhereTheAmigaHadMusic = false, bool lowPass = true)
    {
        _tunes = tunes;
        _sampleRate = sampleRate;
        _onlyWhereTheAmigaHadMusic = onlyWhereTheAmigaHadMusic;
        _lowPass = lowPass;
    }

    /// <summary>A tune plays (not stopped, not finished); a pause does not end it.</summary>
    public bool Playing
    {
        get
        {
            lock (_lock)
            {
                return _player is { IsFinished: false };
            }
        }
    }

    /// <summary>The tune a command plays, or null when it plays none.</summary>
    /// <param name="command">The game's sound command.</param>
    public GmcTune? TuneOf(int command) => command switch
    {
        SoundCommands.Theme => _tunes.Theme,
        _ when _onlyWhereTheAmigaHadMusic => null,
        SoundCommands.SelectionTune => _tunes.Spy,
        >= SoundCommands.SelectionTune + 1 and <= SoundCommands.LastVacationTune => command % 2 == 0 ? _tunes.Spy : _tunes.Final,
        _ => null,
    };

    /// <inheritdoc/>
    public void PlaySound(int effect) => Command(effect);

    /// <inheritdoc/>
    public void PlaySound(int effect, int parameter) => Command(effect);

    /// <inheritdoc/>
    public void PlaySound(int effect, int mode, int variant) => Command(effect);

    private void Command(int command)
    {
        lock (_lock)
        {
            switch (command)
            {
                case SoundCommands.StopAll or SoundCommands.EndOfScene:
                    _player = null;
                    _paused = false;
                    break;
                case SoundCommands.Pause:
                    _paused = true;
                    break;
                case SoundCommands.Resume:
                    _paused = false;
                    break;
                case SoundCommands.Theme or (>= SoundCommands.SelectionTune and <= SoundCommands.LastVacationTune):
                    // a tune replaces the one playing; a tune the Amiga had none for plays nothing
                    _player = TuneOf(command) is { } tune ? new GmcPlayer(tune, _sampleRate, loops: true, _lowPass) : null;
                    _paused = false;
                    break;
            }
        }
    }

    /// <inheritdoc/>
    public void Render(Span<short> interleavedStereo)
    {
        lock (_lock)
        {
            if (_player is null || _paused)
            {
                interleavedStereo.Clear();
                return;
            }
            _player.Render(interleavedStereo);
        }
    }
}
