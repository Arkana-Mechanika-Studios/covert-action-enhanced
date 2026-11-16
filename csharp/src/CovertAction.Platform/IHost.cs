// Reused from the Darklands Restoration Project, src/Darklands.Platform/IHost.cs; same author.
// Adapted: cut down to what Covert Action's drivers use so far (a picture, key presses, the time); the picture is handed
// over as pixels and colours instead of a Screen object; the clock also tells the time of day; sound is a PCM output (Pcm.cs);
// the system clock is with the hosts. Added: the text screen (TextScreen.cs) shown through IVideo.PresentText, as in Darklands;
// the mouse over the picture (IInput.Pointer, IPointer.cs), a player option;
// the joystick as the game port shows it (IInput.ReadJoystick, JoystickReading.cs).
namespace CovertAction.Platform;

/// <summary>
/// Everything the game needs from the program that hosts it: a place to show its screen, the player's key presses,
/// a sound device and real time. The game's own drivers (CovertAction.Drivers) turn the game's calls into these; a
/// host (the SDL window, the headless host of the tests) only implements this interface and knows nothing of the game.
/// </summary>
public interface IHost
{
    /// <summary>Where the game's screen is shown.</summary>
    IVideo Video { get; }

    /// <summary>The keyboard and the joystick.</summary>
    IInput Input { get; }

    /// <summary>The sound device.</summary>
    IPcmOutput Audio { get; }

    /// <summary>Real time.</summary>
    IClock Clock { get; }
}

/// <summary>Shows the game screen.</summary>
public interface IVideo
{
    /// <summary>Shows a picture of the game's screen.</summary>
    /// <param name="pixels">320x200 palette indices, row by row from the top.</param>
    /// <param name="palette">The 256 colours as 0xRRGGBB.</param>
    void Present(ReadOnlySpan<byte> pixels, ReadOnlySpan<uint> palette);

    /// <summary>
    /// Shows the PC's 80x25 text page (video mode 3: 9x16 cells, 720x400 pixels, shown at the 4:3 shape of the
    /// picture) in place of the game's picture, until the next <see cref="Present"/>.
    /// </summary>
    void PresentText(TextScreen screen);
}

/// <summary>The keyboard, in game terms, and the joystick as the PC's game port shows it.</summary>
public interface IInput
{
    /// <summary>Processes pending host events. Call often.</summary>
    void Pump();

    /// <summary>The player closed the window or asked the host to quit.</summary>
    bool QuitRequested { get; }

    /// <summary>Takes the next key press, as a key code (<see cref="HostKeys"/>).</summary>
    bool TryReadKey(out int key);

    /// <summary>
    /// The mouse over the picture, when the player turned it on (the settings' input.mouse); null: no mouse, as on the
    /// original's PC without one. New code (csharp/docs/MOUSE-SUPPORT.md; D-630).
    /// </summary>
    IPointer? Pointer => null;

    /// <summary>
    /// Sets how a held key repeats: the PC's power-on setting (500 ms, then 10.9 a second) or the one the building raid
    /// means to set (250 ms, then 15 a second, INT 16h function 3). The raid asks for the second only with the player's
    /// option of CA-63.
    /// </summary>
    /// <param name="raidRate">True for the faster setting, false for the PC's.</param>
    /// <remarks>New code (D-650). Without its own: nothing changes.</remarks>
    void SetTypematic(bool raidRate)
    {
    }

    /// <summary>
    /// The joystick as the game port shows it right now: its buttons and its two timers' counts. The game reads it
    /// in tight loops (waiting for a button to be let go), so a host gives the controller's present state here
    /// without waiting for <see cref="Pump"/>.
    /// </summary>
    /// <remarks>New code (the host's side of the game port). Without its own: <see cref="JoystickReading.None"/>, no joystick.</remarks>
    JoystickReading ReadJoystick() => JoystickReading.None;
}

/// <summary>Real time.</summary>
public interface IClock
{
    /// <summary>Milliseconds since the host started.</summary>
    long Milliseconds { get; }

    /// <summary>The time of day: whole seconds since 1 January 1970.</summary>
    long UnixSeconds { get; }

    /// <summary>Waits.</summary>
    void Sleep(int milliseconds);

    /// <summary>
    /// Milliseconds since the host started, with their fraction, for keeping the display's pace (a frame of the
    /// original's display is 14.27 ms: whole milliseconds would put each frame up to a millisecond early or late).
    /// </summary>
    /// <remarks>New code (csharp/docs/PERFORMANCE.md). Without its own: <see cref="Milliseconds"/>.</remarks>
    double PreciseMilliseconds => Milliseconds;

    /// <summary>
    /// Waits until <see cref="PreciseMilliseconds"/> reaches <paramref name="deadline"/>: the wait for the display's
    /// next frame. A host with a precise timer ends it on time; without its own, the whole milliseconds left are slept
    /// (a host whose time only moves when the game sleeps, the headless one, keeps the time it always kept).
    /// </summary>
    /// <remarks>New code (csharp/docs/PERFORMANCE.md).</remarks>
    void WaitUntil(double deadline)
    {
        var wait = (int)(deadline - Milliseconds);
        if (wait > 0)
        {
            Sleep(wait);
        }
    }
}
