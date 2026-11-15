using CovertAction.Host.Headless;
using CovertAction.Platform;

namespace CovertAction.Drivers.Tests;

/// <summary>
/// The mouse of the player option (D-630): the host's latched pointer, the mouse driver's units over it, and the
/// headless host's scripted mouse.
/// </summary>
public sealed class HostMouseDriverTests
{
    [Fact]
    public void ThePointerStartsInTheMiddleAndPressesAreLatchedUntilTaken()
    {
        var latch = new PointerLatch();
        Assert.Equal(new PointerState(160, 100, false, 0), latch.State);
        latch.MoveTo(10, 20);
        latch.Press(0);
        latch.Release(0);
        latch.MoveTo(30, 40);
        latch.Press(0);
        Assert.Equal(new PointerState(30, 40, true, 1), latch.State);
        // two presses since the last question; the place of the last one
        Assert.Equal(new PointerPresses(2, 30, 40), latch.TakePresses(0));
        Assert.Equal(new PointerPresses(0, 30, 40), latch.TakePresses(0));
        Assert.Equal(new PointerPresses(1, 10, 20), latch.TakeReleases(0));
        Assert.Equal(0, latch.TakePresses(1).Count);
    }

    [Fact]
    public void APositionOffThePictureKeepsTheLastOneAndAPositionIsClampedToThePicture()
    {
        var latch = new PointerLatch();
        latch.MoveTo(400, -5);
        Assert.Equal(new PointerState(319, 0, true, 0), latch.State);
        latch.MoveTo(0, 0, inside: false);
        Assert.Equal(new PointerState(319, 0, false, 0), latch.State);
    }

    [Fact]
    public void TheDriverCountsTwoUnitsAPixelAcrossAndKeepsTheGamesLimits()
    {
        var latch = new PointerLatch();
        var mouse = new HostMouseDriver(latch);
        Assert.True(mouse.Installed);
        Assert.False(((IMouseDriver)new NoMouseDriver()).Installed);
        latch.MoveTo(100, 50);
        latch.Press(1);
        Assert.Equal(new MouseStatus(2, 200, 50), mouse.GetStatus());
        Assert.Equal(new MouseButtonEvents(2, 1, 200, 50), mouse.ButtonPresses(1));
        mouse.SetHorizontalRange(300, 100);
        mouse.SetVerticalRange(60, 80);
        Assert.Equal(new MouseStatus(2, 200, 60), mouse.GetStatus());
        latch.MoveTo(319, 199);
        Assert.Equal(new MouseStatus(2, 300, 80), mouse.GetStatus());
        mouse.Reset();
        Assert.Equal(new MouseStatus(2, 638, 199), mouse.GetStatus());
    }

    [Fact]
    public void TheHeadlessHostsScriptMovesAndClicksTheMouseAndTheGameFindsNoKeyThen()
    {
        var host = new HeadlessHost([HeadlessHost.PointerMove(12, 34), .. HeadlessHost.Click(56, 78, button: 1), HostKeys.Enter]);
        IInput input = host;
        Assert.False(input.TryReadKey(out _));
        Assert.Equal((12, 34), (input.Pointer!.State.X, input.Pointer.State.Y));
        Assert.False(input.TryReadKey(out _));
        Assert.False(input.TryReadKey(out _));
        Assert.Equal(1, input.Pointer.State.Buttons >> 1);
        Assert.False(input.TryReadKey(out _));
        Assert.Equal(new PointerPresses(1, 56, 78), input.Pointer.TakePresses(1));
        Assert.True(input.TryReadKey(out var key));
        Assert.Equal(HostKeys.Enter, key);
        Assert.True(host.PointerReads > 0);
    }
}
