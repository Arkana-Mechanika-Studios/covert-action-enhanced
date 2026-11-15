using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// The mouse of a PC with a mouse: the nine services of the PC's mouse driver on the host's pointer (the player's
/// mouse over the window). Positions are the driver's units of the game's 320-pixel modes: 0..639 across (two units
/// a game pixel) and 0..199 down. The game's limits (<see cref="SetHorizontalRange"/>, <see cref="SetVerticalRange"/>)
/// keep what the game reads inside a rectangle; the host's own pointer is the player's and is never moved.
/// </summary>
/// <remarks>
/// New code: a player option (the settings' input.mouse, off by default; docs/faithfulness/DIVERGENCES.md D-630). The
/// original's mouse routines are never called by the original; the rewrite's menus read the pointer through them.
/// </remarks>
/// <param name="pointer">The host's pointer.</param>
public sealed class HostMouseDriver(IPointer pointer) : IMouseDriver
{
    /// <summary>The driver's units across for one game pixel (int 33h counts 640 across in the 320-pixel modes).</summary>
    private const int UnitsPerPixel = 2;

    private const int MaxX = 639, MaxY = 199;

    private int _minX, _maxX = MaxX, _minY, _maxY = MaxY;

    /// <inheritdoc/>
    public bool Installed => true;

    /// <inheritdoc/>
    // DIVERGENCE: D-630 the mouse is a player option: with it on, the mouse driver's services read the host's pointer.
    public void Reset()
    {
        (_minX, _maxX, _minY, _maxY) = (0, MaxX, 0, MaxY);
    }

    /// <inheritdoc/>
    /// <remarks>The host shows its own pointer over the picture while the mouse option is on; nothing is drawn into the picture.</remarks>
    public void ShowCursor()
    {
    }

    /// <inheritdoc/>
    public void HideCursor()
    {
    }

    /// <inheritdoc/>
    public MouseStatus GetStatus()
    {
        var state = pointer.State;
        var (x, y) = Units(state.X, state.Y);
        return new MouseStatus(state.Buttons & 3, x, y);
    }

    /// <inheritdoc/>
    /// <remarks>The player's pointer is not moved (nothing in the game calls this).</remarks>
    public void SetPosition(int x, int y)
    {
    }

    /// <inheritdoc/>
    public MouseButtonEvents ButtonPresses(int button) => Events(pointer.TakePresses(button));

    /// <inheritdoc/>
    public MouseButtonEvents ButtonReleases(int button) => Events(pointer.TakeReleases(button));

    /// <inheritdoc/>
    public void SetHorizontalRange(int minimum, int maximum) => (_minX, _maxX) = (Math.Min(minimum, maximum), Math.Max(minimum, maximum));

    /// <inheritdoc/>
    public void SetVerticalRange(int minimum, int maximum) => (_minY, _maxY) = (Math.Min(minimum, maximum), Math.Max(minimum, maximum));

    private MouseButtonEvents Events(PointerPresses presses)
    {
        var (x, y) = Units(presses.X, presses.Y);
        return new MouseButtonEvents(pointer.State.Buttons & 3, presses.Count, x, y);
    }

    /// <summary>A game pixel in the driver's units, inside the game's limits.</summary>
    private (int X, int Y) Units(int pixelX, int pixelY)
    {
        return (Math.Clamp(pixelX * UnitsPerPixel, _minX, _maxX), Math.Clamp(pixelY, _minY, _maxY));
    }
}
