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
/// <param name="pictures">The pictures shown in place of the game's own (a player's option, D-642); null, the default: the game's.</param>
/// <param name="mouseChase">The car chase takes the mouse as well (a player's option, D-670; needs a mouse driver with a mouse); false, the default: it does not.</param>
public sealed class Boundaries(IGraphicsDriver graphics, ISoundDriver sound, IInputDriver input, IMouseDriver mouse, IGameTimer timer, IFiles files,
    IPictureReplacements? pictures = null, bool mouseChase = false)
{
    /// <summary>The car chase takes the mouse as well (D-670); false: it never asks the mouse driver anything.</summary>
    public bool MouseChase { get; } = mouseChase;

    /// <summary>The pictures shown in place of the game's own (D-642); null: none, the game's pictures show.</summary>
    public IPictureReplacements? PictureReplacements { get; } = pictures;

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
