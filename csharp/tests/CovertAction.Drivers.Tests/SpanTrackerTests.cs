using CovertAction.Content;
using CovertAction.Core.Graphics;
using CovertAction.Drivers;

namespace CovertAction.Drivers.Tests;

/// <summary>
/// The tracked draw: what is remembered of an image's place, and how the page is put back from a clean copy.
/// Worked out by hand from the library's instructions (FINAL 0000:0d40 to 0e8f).
/// </summary>
public sealed class SpanTrackerTests
{
    private static McgaGraphicsDriver Driver() => new(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);

    private static RasterPort Port(int page, int x = 0, int y = 0) => new() { PageNr = (short)page, XBgn = (short)x, YBgn = (short)y, Width1 = 319, Length1 = 199 };

    [Fact]
    public void NothingIsRememberedAtFirst()
    {
        var tracker = new SpanTracker();
        Assert.Equal((0xffff, 0), (tracker.FirstRow(0), tracker.LastRow(0)));
        Assert.All(tracker.Left(1).ToArray(), left => Assert.Equal(0xffff, left));
        Assert.All(tracker.Right(1).ToArray(), right => Assert.Equal(0, right));
    }

    [Fact]
    public void AnAreaWidensTheRowsItTouchesAndTheRangeOfRows()
    {
        var tracker = new SpanTracker();
        tracker.Track(Port(0, x: 100, y: 50), new ImageArea(5, 2, 10, 3));     // columns 105..114, rows 52..54
        tracker.Track(Port(2), new ImageArea(110, 54, 20, 2));                 // page 2 is of page 0's kind: columns 110..129, rows 54..55
        Assert.Equal((52, 55), (tracker.FirstRow(0), tracker.LastRow(0)));
        Assert.Equal((105, 114), (tracker.Left(0)[52], tracker.Right(0)[52]));
        Assert.Equal((105, 129), (tracker.Left(0)[54], tracker.Right(0)[54]));
        Assert.Equal((110, 129), (tracker.Left(0)[55], tracker.Right(0)[55]));
        Assert.Equal(0xffff, tracker.Left(0)[56]);
        Assert.Equal(0xffff, tracker.FirstRow(1));                             // the odd pages have their own record

        tracker.Track(Port(1), new ImageArea(0, 0, 0, 0));                     // nothing was drawn: nothing is noted
        Assert.Equal(0xffff, tracker.FirstRow(1));

        tracker.Clear(0);
        Assert.Equal((0xffff, 0), (tracker.FirstRow(0), tracker.LastRow(0)));
        Assert.Equal((0xffff, 0), (tracker.Left(0)[54], tracker.Right(0)[54]));
    }

    [Fact]
    public void ATrackedImageIsTakenOffThePageAgainFromTheCleanCopy()
    {
        var driver = Driver();
        driver.FillPage(1, 4);                              // the clean background
        driver.FillPage(2, 7);
        var image = driver.CutImage(2, 0, 0, 3, 2)!;        // a block of colour 7
        driver.ShowPage(1);
        driver.DrawImageTracked(Port(0), 10, 20, image);
        Assert.Equal(7, driver.GetPixel(0, 11, 21));
        driver.DrawImageUntracked(Port(0), 50, 20, image);  // not tracked: it stays
        driver.RestoreTrackedSpans(1, 0);
        Assert.Equal(4, driver.GetPixel(0, 11, 21));
        Assert.Equal(7, driver.GetPixel(0, 51, 21));
        Assert.Equal(0xffff, driver.Tracked.FirstRow(0));   // and the place is forgotten

        driver.DrawImageTracked(Port(0), 10, 20, image);
        driver.ClearSpanTracking();
        driver.RestoreTrackedSpans(1, 0);                   // nothing is remembered: nothing is put back
        Assert.Equal(7, driver.GetPixel(0, 11, 21));
    }
}
