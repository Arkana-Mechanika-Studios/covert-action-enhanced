namespace CovertAction.Platform;

/// <summary>
/// The mouse as the game's code asks for it: the nine services of the PC's mouse driver the original uses, each as
/// a call with named values. Every part of the original carries the same small routines in front of them.
/// </summary>
/// <remarks>
/// Positions are in the driver's own units: 0..639 across and 0..199 down for the game's screen.
/// </remarks>
public interface IMouseDriver
{
    /// <summary>Resets the mouse: pointer hidden, in the middle of the screen, no limits.</summary>
    void Reset();

    /// <summary>Shows the pointer.</summary>
    void ShowCursor();

    /// <summary>Hides the pointer.</summary>
    void HideCursor();

    /// <summary>Which buttons are down and where the pointer is.</summary>
    MouseStatus GetStatus();

    /// <summary>Moves the pointer.</summary>
    /// <param name="x">Horizontal position.</param>
    /// <param name="y">Vertical position.</param>
    void SetPosition(int x, int y);

    /// <summary>How often a button was pressed since this was last asked, and where the pointer was at the last press.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    MouseButtonEvents ButtonPresses(int button);

    /// <summary>How often a button was released since this was last asked, and where the pointer was at the last release.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    MouseButtonEvents ButtonReleases(int button);

    /// <summary>Keeps the pointer between two columns.</summary>
    /// <param name="minimum">The left-most position.</param>
    /// <param name="maximum">The right-most position.</param>
    void SetHorizontalRange(int minimum, int maximum);

    /// <summary>Keeps the pointer between two rows.</summary>
    /// <param name="minimum">The top-most position.</param>
    /// <param name="maximum">The bottom-most position.</param>
    void SetVerticalRange(int minimum, int maximum);
}

/// <summary>The mouse at one moment.</summary>
/// <param name="Buttons">The buttons that are down: bit 0 the left, bit 1 the right.</param>
/// <param name="X">The pointer's horizontal position.</param>
/// <param name="Y">The pointer's vertical position.</param>
public readonly record struct MouseStatus(int Buttons, int X, int Y);

/// <summary>What happened to one mouse button since the game last asked.</summary>
/// <param name="Buttons">The buttons that are down now: bit 0 the left, bit 1 the right.</param>
/// <param name="Count">How often the button was pressed (or released).</param>
/// <param name="X">The pointer's horizontal position at the last press (or release).</param>
/// <param name="Y">The pointer's vertical position at the last press (or release).</param>
public readonly record struct MouseButtonEvents(int Buttons, int Count, int X, int Y);
