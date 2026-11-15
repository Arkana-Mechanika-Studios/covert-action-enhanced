using CovertAction.Platform;

namespace CovertAction.Presentation;

/// <summary>
/// The outside world as one value: the six boundary interfaces a module's code reaches the drivers, the timer and
/// the files through. The game builds it from its own drivers; a test from recording fakes.
/// </summary>
/// <param name="graphics">The graphics library and driver.</param>
/// <param name="sound">The sound driver.</param>
/// <param name="input">The keyboard and joystick driver.</param>
/// <param name="mouse">The mouse driver.</param>
/// <param name="timer">The time of day and the display's frames.</param>
/// <param name="files">The game's files.</param>
public sealed class Boundaries(IGraphicsDriver graphics, ISoundDriver sound, IInputDriver input, IMouseDriver mouse, IGameTimer timer, IFiles files)
{
    /// <summary>The graphics library and driver.</summary>
    public IGraphicsDriver Graphics { get; } = graphics;

    /// <summary>The sound driver.</summary>
    public ISoundDriver Sound { get; } = sound;

    /// <summary>The keyboard and joystick driver.</summary>
    public IInputDriver Input { get; } = input;

    /// <summary>The mouse driver.</summary>
    public IMouseDriver Mouse { get; } = mouse;

    /// <summary>The time of day and the display's frames.</summary>
    public IGameTimer Timer { get; } = timer;

    /// <summary>The game's files.</summary>
    public IFiles Files { get; } = files;
}
