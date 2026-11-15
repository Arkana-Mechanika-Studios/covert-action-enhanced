namespace CovertAction.Audio;

/// <summary>What a command of the game's sound driver is about: a tune, a sound effect, or everything that plays.</summary>
public enum SoundRole
{
    /// <summary>A tune: the command starts a song on all (or most) of the driver's voices.</summary>
    Music,

    /// <summary>A sound effect: the command starts one or two voices for one sound, or drives the engines, the aeroplane or the gunfire.</summary>
    Effect,

    /// <summary>The command concerns whatever plays (stop, end, pause, resume), or its meaning is not known: both drivers get it.</summary>
    Both,
}

/// <summary>
/// The game's sound commands sorted into music and effects, for playing the two on different cards (D-620). The split is
/// the drivers' own: in all four command tables (ISOUND, TSOUND, ASOUND, RSOUND) only the songs start every voice
/// (csharp/docs/SOUND-MIXING.md gives the evidence, command by command).
/// </summary>
/// <remarks>New code: the original has one driver, which plays both.</remarks>
public static class SoundCommands
{
    /// <summary>The theme (FINAL's title, menus and debriefing).</summary>
    public const int Theme = 2;

    /// <summary>The selection screens' tune (TAC's equipment, CHASE's car choice).</summary>
    public const int SelectionTune = 39;

    /// <summary>The last of the four vacations' tunes (40-43, FINAL's vacation scene: <c>DoSound(a + 0x28)</c>).</summary>
    public const int LastVacationTune = 43;

    /// <summary>Stop everything (the end of BUG, CHASE, TAC; Alt-S turning sound off).</summary>
    public const int StopAll = 0;

    /// <summary>The end of a scene or menu: what plays ends (at its note's end, or at once on the speaker).</summary>
    public const int EndOfScene = 1;

    /// <summary>Alt-P's pause.</summary>
    public const int Pause = 37;

    /// <summary>The resume after Alt-P.</summary>
    public const int Resume = 38;

    /// <summary>The last command of the drivers' common table (the speaker's, Tandy's and AdLib's 47 commands); RSOUND adds 47-54, restarting a voice.</summary>
    public const int LastCommonCommand = 46;

    /// <summary>The role of a command: the songs are music; stop, end, pause, resume and the commands outside the common table go to both; the rest are effects.</summary>
    /// <param name="command">The command's number, the first value of the game's <c>play_sound</c>.</param>
    public static SoundRole RoleOf(int command) => command switch
    {
        Theme or (>= SelectionTune and <= LastVacationTune) => SoundRole.Music,
        StopAll or EndOfScene or Pause or Resume => SoundRole.Both,
        < 0 or > LastCommonCommand => SoundRole.Both,
        _ => SoundRole.Effect,
    };
}
