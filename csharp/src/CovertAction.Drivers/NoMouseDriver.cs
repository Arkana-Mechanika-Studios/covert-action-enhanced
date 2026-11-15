using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// The mouse of a PC without a mouse: every service does nothing and reports no button and position 0. The parts
/// of the game ported so far never ask for the mouse; a real one comes with the first part that does.
/// </summary>
/// <remarks>New code: a stand-in (docs/faithfulness/DIVERGENCES.md D-8).</remarks>
public sealed class NoMouseDriver : IMouseDriver
{
    /// <inheritdoc/>
    // DIVERGENCE: D-8 there is no mouse yet.
    public void Reset()
    {
    }

    /// <inheritdoc/>
    public void ShowCursor()
    {
    }

    /// <inheritdoc/>
    public void HideCursor()
    {
    }

    /// <inheritdoc/>
    public MouseStatus GetStatus() => default;

    /// <inheritdoc/>
    public void SetPosition(int x, int y)
    {
    }

    /// <inheritdoc/>
    public MouseButtonEvents ButtonPresses(int button) => default;

    /// <inheritdoc/>
    public MouseButtonEvents ButtonReleases(int button) => default;

    /// <inheritdoc/>
    public void SetHorizontalRange(int minimum, int maximum)
    {
    }

    /// <inheritdoc/>
    public void SetVerticalRange(int minimum, int maximum)
    {
    }
}
