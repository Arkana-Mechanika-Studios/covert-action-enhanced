namespace CovertAction.Platform;

/// <summary>
/// The player's mouse over the game's picture, as a host delivers it: the position in game pixels and the button
/// presses and releases since the game last asked (each latched, so a click shorter than a frame is never lost). New
/// code: the original has no host; this is what its mouse driver would have read from the mouse
/// (csharp/docs/MOUSE-SUPPORT.md, section 4; divergence D-630). Only a host whose player turned the mouse on has one.
/// </summary>
public interface IPointer
{
    /// <summary>Where the pointer is and which buttons are held.</summary>
    PointerState State { get; }

    /// <summary>How often a button was pressed since this was last asked for that button, and where the last press was.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    PointerPresses TakePresses(int button);

    /// <summary>The same for releases.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    PointerPresses TakeReleases(int button);
}

/// <summary>The pointer at one moment.</summary>
/// <param name="X">The game pixel under the pointer, 0..319 (the picture's edge while the pointer is outside it).</param>
/// <param name="Y">The game pixel under the pointer, 0..199.</param>
/// <param name="Inside">Whether the pointer is over the picture (not over the black bars or outside the window).</param>
/// <param name="Buttons">The buttons held: bit 0 the left, bit 1 the right.</param>
public readonly record struct PointerState(int X, int Y, bool Inside, int Buttons);

/// <summary>What happened to one button since the game last asked.</summary>
/// <param name="Count">How often it was pressed (or released).</param>
/// <param name="X">The game pixel of the last press (or release), across.</param>
/// <param name="Y">The game pixel of the last press (or release), down.</param>
public readonly record struct PointerPresses(int Count, int X, int Y);

/// <summary>
/// The pointer a host keeps: the host tells it where the mouse went and what its buttons did, the game's mouse driver
/// takes the latched presses. Shared by the hosts (the SDL window and the headless host's scripted pointer). New code
/// (D-630). The pointer starts in the middle of the picture, where the PC's mouse driver puts it at its reset.
/// </summary>
public sealed class PointerLatch : IPointer
{
    private const int Width = 320, Height = 200;

    private readonly PointerPresses[] _presses = [new(0, Width / 2, Height / 2), new(0, Width / 2, Height / 2)];
    private readonly PointerPresses[] _releases = [new(0, Width / 2, Height / 2), new(0, Width / 2, Height / 2)];
    private int _x = Width / 2, _y = Height / 2, _buttons;
    private bool _inside;

    /// <inheritdoc/>
    public PointerState State => new(_x, _y, _inside, _buttons);

    /// <summary>The mouse moved: to a game pixel (clamped to the picture), or off the picture (its position is kept).</summary>
    /// <param name="x">The game pixel across.</param>
    /// <param name="y">The game pixel down.</param>
    /// <param name="inside">Whether the position is over the picture; false: the pointer left it and stays where it was.</param>
    public void MoveTo(int x, int y, bool inside = true)
    {
        _inside = inside;
        if (inside)
        {
            (_x, _y) = (Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1));
        }
    }

    /// <summary>A button went down at the pointer's position.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    public void Press(int button)
    {
        _buttons |= 1 << button;
        _presses[button] = new PointerPresses(_presses[button].Count + 1, _x, _y);
    }

    /// <summary>A button went up at the pointer's position.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    public void Release(int button)
    {
        _buttons &= ~(1 << button);
        _releases[button] = new PointerPresses(_releases[button].Count + 1, _x, _y);
    }

    /// <inheritdoc/>
    public PointerPresses TakePresses(int button) => Take(_presses, button);

    /// <inheritdoc/>
    public PointerPresses TakeReleases(int button) => Take(_releases, button);

    private PointerPresses Take(PointerPresses[] latch, int button)
    {
        if (button is < 0 or > 1)
        {
            return new PointerPresses(0, _x, _y);
        }
        // the PC's mouse driver answers with the last event's place, also when there was none since the last question
        var taken = latch[button];
        latch[button] = taken with { Count = 0 };
        return taken;
    }
}
