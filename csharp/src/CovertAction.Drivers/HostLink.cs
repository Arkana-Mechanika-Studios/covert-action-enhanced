using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// The game's side of the host: shows the screen the graphics code has drawn, keeps the host's events moving, and
/// keeps the display's pace. The keyboard driver and the timer share it, because the original shows a new frame and
/// reads the keyboard all the time, whatever the game is doing, and the rewrite does so only where the game waits.
/// </summary>
/// <remarks>New code: on a PC the display reads the screen memory by itself, 70 times a second.</remarks>
/// <param name="graphics">The graphics code whose screen is shown.</param>
/// <param name="host">The host program.</param>
public sealed class HostLink(McgaGraphicsDriver graphics, IHost host)
{
    /// <summary>The frames per second of the original's display mode (VGA mode 13h).</summary>
    public const double FramesPerSecond = 70.086;

    private readonly uint[] _colours = new uint[256];
    private double _nextFrame;

    /// <summary>The picture and colours, or the text page, last handed to the host, for <see cref="PresentIfChangedOrDue"/>.</summary>
    private readonly byte[] _shownPixels = new byte[320 * 200];
    private readonly uint[] _shownColours = new uint[256];
    private readonly TextScreen _shownText = new();
    private bool _shownPicture, _shownTextPage;

    /// <summary>The host program.</summary>
    public IHost Host { get; } = host;

    /// <summary>
    /// The text page shown in place of the game's picture, or null while the display is in the graphics mode. The
    /// set-up screen sets it (the PC's text mode); the launcher clears it when the next part starts.
    /// </summary>
    public TextScreen? TextPage { get; set; }

    /// <summary>Shows the screen as it is at this moment: the text page in the text mode, else the game's picture.</summary>
    public void Present()
    {
        if (TextPage is { } page)
        {
            Host.Video.PresentText(page);
            page.CopyTo(_shownText);
            (_shownTextPage, _shownPicture) = (true, false);
            return;
        }
        graphics.CopyColours(_colours);
        Host.Video.Present(graphics.Screen, _colours);
        graphics.Screen.CopyTo(_shownPixels);
        _colours.CopyTo(_shownColours, 0);
        (_shownTextPage, _shownPicture) = (false, true);
    }

    /// <summary>When <see cref="PresentIfDue"/> shows the screen next.</summary>
    private double _nextPresent;

    /// <summary>
    /// Shows the screen if a frame of the 70 Hz display has passed since it was last shown this way, without waiting.
    /// A game that polls for a key in a loop (a menu box) is shown as the PC's display shows its memory all the time;
    /// a part that waits for its frames (<see cref="WaitForFrame"/>) is not slowed down.
    /// </summary>
    public void PresentIfDue()
    {
        var now = Host.Clock.Milliseconds;
        if (now >= _nextPresent)
        {
            Present();
            _nextPresent = now + 1000 / FramesPerSecond;
        }
    }

    /// <summary>
    /// Shows the screen when the game has changed it since it was last shown, else when a frame of the 70 Hz display
    /// has passed (<see cref="PresentIfDue"/>): for the loops that wait for a key or for the PC's clock.
    /// </summary>
    /// <remarks>
    /// New code (csharp/docs/PERFORMANCE.md). Those loops showed the screen on every turn, a millisecond apart: over
    /// 400 frames a second handed to the window, six times the display's, each converted, uploaded and presented. A
    /// change is still shown at once (no later than the PC's display would show it); an unchanged screen at the
    /// display's pace, which keeps the window drawn and the text page's cursor blinking.
    /// </remarks>
    public void PresentIfChangedOrDue()
    {
        if (Changed())
        {
            Present();
            _nextPresent = Host.Clock.Milliseconds + 1000 / FramesPerSecond;
            return;
        }
        PresentIfDue();
    }

    /// <summary>Whether the picture or its colours, or the text page, differ from what was last shown.</summary>
    private bool Changed()
    {
        if (TextPage is { } page)
        {
            return !_shownTextPage || page.CursorRow != _shownText.CursorRow || page.CursorColumn != _shownText.CursorColumn
                || page.CursorVisible != _shownText.CursorVisible || !page.Characters.AsSpan().SequenceEqual(_shownText.Characters)
                || !page.Attributes.AsSpan().SequenceEqual(_shownText.Attributes);
        }
        if (!_shownPicture || !graphics.Screen.SequenceEqual(_shownPixels))
        {
            return true;
        }
        graphics.CopyColours(_colours);
        return !_colours.AsSpan().SequenceEqual(_shownColours);
    }

    /// <summary>
    /// Lets the host handle its events (key presses, its window). When the player has closed the window, the game
    /// ends here, the way the original ends through the C library's exit.
    /// </summary>
    /// <exception cref="ProgramExitException">The player closed the window.</exception>
    public void Pump()
    {
        Host.Input.Pump();
        if (Host.Input.QuitRequested)
        {
            throw new ProgramExitException(0);
        }
    }

    /// <summary>Shows the screen and waits until the next frame of a 70 Hz display is due.</summary>
    /// <remarks>
    /// The frames are due every 1000/70.086 ms on the host's precise clock, and the wait ends on time where the host
    /// can (<see cref="IClock.WaitUntil"/>): counted in whole milliseconds and slept with the system's timer, frames came
    /// 11 to 18 ms apart instead of 14.27 (csharp/docs/PERFORMANCE.md). A host without a precise clock (the headless
    /// one) waits the whole milliseconds as before.
    /// </remarks>
    public void WaitForFrame()
    {
        Present();
        Pump();
        const double frame = 1000 / FramesPerSecond;
        var now = Host.Clock.PreciseMilliseconds;
        // a game that was busy for longer than a frame does not catch up: the next frame is due one frame from now
        _nextFrame = Math.Max(_nextFrame + frame, now);
        Host.Clock.WaitUntil(_nextFrame);
    }
}
