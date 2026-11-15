using CovertAction.Content;
using CovertAction.Core.Graphics;
using CovertAction.Drivers;

namespace CovertAction.Drivers.Tests;

/// <summary>
/// The game's drawing code on a small font made up for the tests: what each routine draws, pixel by pixel, worked
/// out by hand from the original driver's instructions. The driver oracle (McgaOracleTests) proves the same code
/// against the original with the player's game; these tests run everywhere.
/// </summary>
public sealed class McgaGraphicsDriverTests
{
    /// <summary>
    /// A font file with one font of two characters, two rows high, one padding column and one padding row:
    /// 'A' is two pixels wide (##, #.), 'B' three (#.#, ###).
    /// </summary>
    internal static byte[] FontFile =>
    [
        1, 0,                       // one font
        14, 0,                      // its pictures begin at byte 14
        2, 3,                       // the widths of 'A' and 'B'
        (byte)'A', (byte)'B', 1, 0, 2, 1, 1, 0,     // first, last, bytes per row, fixed width, rows, padding columns, padding rows, unused
        0b1100_0000, 0b1010_0000,   // top row of A, of B
        0b1000_0000, 0b1110_0000,   // second row
        0, 0, 0,                    // the padding row, and the byte read behind the last character
    ];

    private static McgaGraphicsDriver Driver()
    {
        // colour n of the driver's table is (n, n, n)
        var colours = new byte[3 * McgaDriverFile.Colours];
        for (var i = 0; i < colours.Length; i++)
        {
            colours[i] = (byte)(i / 3);
        }
        return new McgaGraphicsDriver(new FontSet(FontFile), colours);
    }

    private static RasterPort Port(int page = 0, int x = 0, int y = 0) =>
        new() { PageNr = (short)page, XBgn = (short)x, YBgn = (short)y, Width1 = 319, Length1 = 199, APen = 7, BPen = 1, FontNr = 1 };

    /// <summary>The pixels of a page that are not 0, as (x, y, colour), in reading order.</summary>
    private static (int X, int Y, int Colour)[] Drawn(byte[] page) =>
        [.. Enumerable.Range(0, page.Length).Where(i => page[i] != 0).Select(i => (i % 320, i / 320, (int)page[i]))];

    // ---- text ----

    [Fact]
    public void ATextWithoutBackgroundSetsOnlyTheCharactersPixels()
    {
        var driver = Driver();
        var end = driver.DrawString(Port(), 1, 1, "AB");
        Assert.Equal([(1, 1, 7), (2, 1, 7), (4, 1, 7), (6, 1, 7), (1, 2, 7), (4, 2, 7), (5, 2, 7), (6, 2, 7)], Drawn(driver.Pages[0]));
        // each character moves the pen by its width and the padding column: 3 and 4
        Assert.Equal(8, end);
    }

    [Fact]
    public void ATextWithBackgroundFillsEachCellWithItsPaddingColumnAndRow()
    {
        var driver = Driver();
        var port = Port();
        port.DrawMode = 1;
        driver.DrawString(port, 0, 0, "A");
        // a cell of 3 columns and 3 rows: the character in pen 7, the rest in pen 1
        Assert.Equal([(0, 0, 7), (1, 0, 7), (2, 0, 1), (0, 1, 7), (1, 1, 1), (2, 1, 1), (0, 2, 1), (1, 2, 1), (2, 2, 1)], Drawn(driver.Pages[0]));
    }

    [Fact]
    public void ACharacterCodeFrom128ChangesTheColourAndThePortKeepsIt()
    {
        var driver = Driver();
        var port = Port();
        var end = driver.DrawString(port, 0, 0, "A\u0085A");
        Assert.Equal([(0, 0, 7), (1, 0, 7), (3, 0, 5), (4, 0, 5), (0, 1, 7), (3, 1, 5)], Drawn(driver.Pages[0]));
        Assert.Equal(5, port.APen);
        Assert.Equal(6, end);
    }

    [Fact]
    public void ATextIsClippedAtTheTopAndBottomOfThePortsWindow()
    {
        var driver = Driver();
        driver.DrawString(Port(), 0, -1, "A");
        Assert.Equal([(0, 0, 7)], Drawn(driver.Pages[0]));      // only the character's second row, in the window's first

        driver = Driver();
        var low = Port();
        low.Length1 = 4;
        driver.DrawString(low, 0, 4, "A");
        Assert.Equal([(0, 4, 7), (1, 4, 7)], Drawn(driver.Pages[0]));   // only its first row, in the window's last

        driver.DrawString(low, 0, 5, "A");                      // below the window: nothing more
        Assert.Equal(2, Drawn(driver.Pages[0]).Length);
    }

    [Fact]
    public void ATextIsDrawnAtThePortsOriginOnThePortsPage()
    {
        var driver = Driver();
        driver.DrawString(Port(page: 1, x: 10, y: 20), 0, 0, "A");
        Assert.Empty(Drawn(driver.Pages[0]));
        Assert.Equal([(10, 20, 7), (11, 20, 7), (10, 21, 7)], Drawn(driver.Pages[1]));
    }

    [Fact]
    public void NothingIsDrawnForAnEmptyTextOrAFontThatDoesNotExist()
    {
        var driver = Driver();
        var port = Port(x: 3);
        Assert.Equal(2, driver.DrawString(port, 5, 0, ""));     // the original's result here: the column less the window's left edge
        port.FontNr = 2;
        driver.DrawString(port, 5, 0, "A");
        port.FontNr = 0;
        driver.DrawString(port, 5, 0, "A");
        Assert.Empty(Drawn(driver.Pages[0]));
    }

    [Fact]
    public void ACharactersWidthIncludesThePaddingColumn()
    {
        var driver = Driver();
        Assert.Equal(3, driver.CharWidth(1, 'A'));
        Assert.Equal(4, driver.CharWidth(1, 'B'));
        Assert.Equal(0, driver.CharWidth(1, 'C'));          // not in the font
        Assert.Equal(0, driver.CharWidth(1, '\u0085'));     // a colour code
        Assert.Equal(0, driver.CharWidth(2, 'A'));          // no such font
    }

    // ---- fills ----

    [Fact]
    public void AFillCoversItsRectangleAtThePortsOrigin()
    {
        var driver = Driver();
        driver.FillRect(Port(x: 10, y: 5), 1, 1, 3, 2, 4);
        Assert.Equal([(11, 6, 4), (12, 6, 4), (13, 6, 4), (11, 7, 4), (12, 7, 4), (13, 7, 4)], Drawn(driver.Pages[0]));
    }

    [Fact]
    public void AFillOnePixelWideInColumn0OrInTheLastLineColumnDrawsNothing()
    {
        // docs/original-bugs.md CA-23
        var driver = Driver();
        driver.FillRect(Port(), 0, 0, 1, 3, 4);
        driver.FillRect(Port(), 319, 0, 1, 3, 4);
        Assert.Empty(Drawn(driver.Pages[0]));
        driver.FillRect(Port(), 1, 0, 1, 2, 4);
        Assert.Equal([(1, 0, 4), (1, 1, 4)], Drawn(driver.Pages[0]));
    }

    [Fact]
    public void AFillPastTheRightEdgeGoesOnInTheNextRowAndRow200LandsInRow128()
    {
        // docs/original-bugs.md CA-24: the cryptography screen's own first fill is (0,0)-(320,200)
        var driver = Driver();
        driver.FillRect(Port(), 318, 0, 3, 1, 4);
        Assert.Equal([(318, 0, 4), (319, 0, 4), (0, 1, 4)], Drawn(driver.Pages[0]));

        driver = Driver();
        driver.FillRect(Port(), 5, 199, 2, 2, 4);
        Assert.Equal([(5, 128, 4), (6, 128, 4), (5, 199, 4), (6, 199, 4)], Drawn(driver.Pages[0]));

        // the pixel behind the last of the page is below the picture: not drawn, no error
        driver.FillRect(Port(), 318, 199, 3, 1, 4);
    }

    [Fact]
    public void AFillThatOverrunsTheOriginalsEdgeTablesIsAnError()
    {
        // docs/original-bugs.md CA-19
        Assert.Throws<ArgumentOutOfRangeException>(() => Driver().FillRect(Port(), 0, 0, 10, 221, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Driver().FillRect(Port(), 0, -1, 10, 5, 4));
    }

    [Fact]
    public void AFillOfNoRowsDrawsNothing()
    {
        var driver = Driver();
        driver.FillRect(Port(), 5, 5, 10, 0, 4);
        Assert.Empty(Drawn(driver.Pages[0]));
    }

    // ---- rectangles and pages ----

    [Fact]
    public void RecolouringReplacesOneColourInsideTheRectangle()
    {
        var driver = Driver();
        driver.FillRect(Port(), 1, 0, 4, 1, 4);
        driver.FillRect(Port(), 3, 0, 2, 1, 6);
        driver.RecolorRect(Port(), 0, 0, 4, 1, 4, 9);
        Assert.Equal([(1, 0, 9), (2, 0, 9), (3, 0, 6), (4, 0, 6)], Drawn(driver.Pages[0]));
    }

    [Fact]
    public void ARectangleIsCopiedFromOnePageToAnotherAndAPageIsShown()
    {
        var driver = Driver();
        driver.FillRect(Port(), 2, 2, 2, 2, 4);
        driver.CopyRect(Port(), 2, 2, 2, 1, Port(page: 1), 10, 10);
        Assert.Equal([(10, 10, 4), (11, 10, 4)], Drawn(driver.Pages[1]));

        driver.CopyRect(Port(), 2, 2, 0, 5, Port(page: 2), 0, 0);   // no width: nothing
        Assert.Empty(Drawn(driver.Pages[2]));

        driver.ShowPage(1);
        Assert.Equal([(10, 10, 4), (11, 10, 4)], Drawn(driver.Pages[0]));
        driver.ShowPage(0);                                           // the screen itself: nothing happens
        Assert.Equal(2, Drawn(driver.Pages[0]).Length);
    }

    /// <summary>
    /// copy_rect looks the source's first row up in the driver's table of rows without a check; the two words in front of
    /// the table (MGRAPHIC 165Ch, 165Eh) are 0, so a copy from row -2 or -1 starts at the top of the page and goes on a
    /// row at a time. Met by the raid's tile 63, whose picture lies at row -2 (TAC/level3-arrest).
    /// </summary>
    [Fact]
    public void ACopyFromAboveThePageStartsAtItsTop()
    {
        var driver = Driver();
        driver.FillRect(Port(), 1, 0, 2, 1, 4);
        driver.FillRect(Port(), 1, 1, 2, 1, 5);
        driver.CopyRect(Port(), 1, -2, 1, 2, Port(page: 1), 10, 10);
        Assert.Equal([(10, 10, 4), (10, 11, 5)], Drawn(driver.Pages[1]));
    }

    [Fact]
    public void APageNobodySetUpIsTheScreen()
    {
        var driver = Driver();
        driver.FillRect(Port(page: 3), 1, 1, 2, 1, 4);
        Assert.Equal([(1, 1, 4), (2, 1, 4)], Drawn(driver.Pages[0]));
    }

    [Fact]
    public void APictureRowIsPutOnAPageTwoPixelsAtATimeAndAPixelCanBeReadBack()
    {
        var driver = Driver();
        driver.PutPicLine([1, 2, 3, 4], 1, 5, 10, 3);     // three pixels asked for: four are copied
        Assert.Equal([(10, 5, 1), (11, 5, 2), (12, 5, 3), (13, 5, 4)], Drawn(driver.Pages[1]));
        Assert.Equal(3, driver.GetPixel(1, 12, 5));
        Assert.Equal(0, driver.GetPixel(0, 12, 5));
    }

    [Fact]
    public void APageIsFilledAndSpansAreCopiedBetweenPages()
    {
        var driver = Driver();
        driver.FillPage(1, 0x0107);                         // the colour's low byte counts
        Assert.All(driver.Pages[1], pixel => Assert.Equal(7, pixel));

        var left = new ushort[200];
        var right = new ushort[200];
        (left[3], right[3]) = (5, 6);
        (left[4], right[4]) = (9, 8);                       // right of its right edge: skipped
        (left[5], right[5]) = (0, 0);                       // a single pixel in column 0 is copied (unlike a fill)
        driver.CopySpans(1, 0, 3, 5, left, right);
        Assert.Equal([(5, 3, 7), (6, 3, 7), (0, 5, 7)], Drawn(driver.Pages[0]));
        driver.CopySpans(1, 0, -1, 5, left, right);         // a negative first row: nothing
        Assert.Equal(3, Drawn(driver.Pages[0]).Length);
    }

    [Fact]
    public void APageCanBeGivenABufferOfItsOwn()
    {
        var driver = Driver();
        Assert.Same(driver.Pages[0], driver.Pages[3]);      // not set up: the screen
        driver.SetPageBuffer(3, driver.AllocPageBuffer(1));
        Assert.NotSame(driver.Pages[0], driver.Pages[3]);
        driver.FillPage(3, 4);
        Assert.Empty(Drawn(driver.Pages[0]));
        driver.FreePageBuffer(3);                           // the page keeps its picture until it gets another buffer
        Assert.Equal(4, driver.GetPixel(3, 0, 0));
        driver.SetPageBuffer(3, driver.AllocPageBuffer(0)); // 0 asks for the screen's own buffer
        Assert.Same(driver.Pages[0], driver.Pages[3]);
        Assert.Equal(0, driver.PageFlipActive(0));
    }

    // ---- images ----

    [Fact]
    public void AnImageKeepsEachRowFromItsFirstToItsLastVisiblePixel()
    {
        var driver = Driver();
        Assert.True(driver.BeginImageCuts());
        driver.BeginImageBuild(5, 3);
        driver.AddImageRow([0, 7, 0, 7, 0]);
        driver.AddImageRow([0, 0, 0, 0, 0]);
        driver.AddImageRow([3, 0, 0, 0, 0]);
        var image = driver.FinishImageBuild()!;
        Assert.Equal((5, 3), (image.Width, image.Height));
        Assert.Equal([(1, "7,0,7"), (5, ""), (0, "3")], image.Rows.Select(row => (row.Skip, string.Join(",", row.Pixels))));
        Assert.Null(driver.FinishImageBuild());             // nothing is being built any more
    }

    [Fact]
    public void AnImageCutFromAPageIsDrawnWithItsSeeThroughPixels()
    {
        var driver = Driver();
        driver.PutPicLine([0, 5, 0, 5], 1, 0, 0, 4);
        driver.PutPicLine([6, 6, 0, 0], 1, 1, 0, 4);
        var image = driver.CutImage(1, 0, 0, 4, 2)!;
        driver.FillPage(0, 9);
        driver.DrawImagePort(Port(x: 10, y: 20), 1, 1, image);
        // only the image's own pixels changed: the 0s inside and around it let the page show
        Assert.Equal([(12, 21, 5), (14, 21, 5), (11, 22, 6), (12, 22, 6)], Drawn(driver.Pages[0]).Where(p => p.Colour != 9));
        driver.DrawImagePort(Port(), 0, 0, null);            // no image: nothing
    }

    [Fact]
    public void AClippedDrawShowsThePartInsideTheWindowAndSaysWhere()
    {
        var driver = Driver();
        driver.FillPage(1, 4);
        var image = driver.CutImage(1, 0, 0, 4, 3)!;
        var port = Port(x: 100, y: 50);
        port.Width1 = 5;
        port.Length1 = 5;
        // two columns left of the window, one row above it
        Assert.Equal(new ImageArea(0, 0, 2, 2), driver.DrawImageUntracked(port, -2, -1, image));
        Assert.Equal([(100, 50, 4), (101, 50, 4), (100, 51, 4), (101, 51, 4)], Drawn(driver.Pages[0]));
        // past the right and bottom edges
        Assert.Equal(new ImageArea(4, 4, 2, 2), driver.DrawImageUntracked(port, 4, 4, image));
        // wholly outside: nothing drawn, an empty area
        Assert.Equal(default, driver.DrawImageUntracked(port, 6, 0, image));
        Assert.Equal(default, driver.DrawImageUntracked(port, -4, 0, image));
        Assert.Equal(8, Drawn(driver.Pages[0]).Length);
    }

    [Fact]
    public void AnImageHoldsNoMoreThanTheOriginalsMemoryForOne()
    {
        // 320 pixels and 4 bytes a row: 65520 bytes hold 202 rows; the 203rd is dropped and the image is a row lower
        var driver = Driver();
        driver.BeginImageBuild(320, 203);
        var row = Enumerable.Repeat((byte)1, 320).ToArray();
        for (var i = 0; i < 203; i++)
        {
            driver.AddImageRow(row);
        }
        var image = driver.FinishImageBuild()!;
        Assert.Equal((202, 202), (image.Height, image.Rows.Count));
        driver.BeginImageBuild(1, 1);                        // and no image is started until images are begun anew
        Assert.Null(driver.FinishImageBuild());
        driver.BeginImageCuts();
        driver.BeginImageBuild(1, 1);
        Assert.NotNull(driver.FinishImageBuild());
    }

    [Fact]
    public void TheDissolveEndsWithThePageOnTheScreenAndShowsItsProgress()
    {
        var driver = Driver();
        driver.FillPage(1, 6);
        var frames = 0;
        driver.FrameShown = () => frames++;
        driver.SubGame014b(1);
        Assert.All(driver.Pages[0], pixel => Assert.Equal(6, pixel));
        Assert.Equal(64000 / 0x215, frames);
        driver.SubGame014b(0);
    }

    // ---- colours ----

    [Fact]
    public void SettingTheModeLoadsTheDriversColoursAndClearsTheScreenUnlessAskedNotTo()
    {
        var driver = Driver();
        driver.FillRect(Port(), 1, 1, 2, 1, 4);
        driver.SubCode0208(0x80);
        Assert.Equal(2, Drawn(driver.Pages[0]).Length);
        Assert.Equal([5, 5, 5], driver.Palette[15..18]);
        driver.SubCode0208(0);
        Assert.Empty(Drawn(driver.Pages[0]));
    }

    [Fact]
    public void APaletteNamesSixteenOfTheDriversColoursAndTheBorder()
    {
        var driver = Driver();
        var palette = new byte[17];
        palette[1] = 63;
        palette[2] = 20;
        palette[9] = 20;
        palette[16] = 20;           // the border: colour 20, which entries 2 and 9 show; the last of them wins
        driver.SetPalette(palette);
        Assert.Equal([63, 63, 63, 20, 20, 20], driver.Palette[3..9]);
        Assert.Equal(9, driver.Border);

        var colours = new uint[256];
        driver.CopyColours(colours);
        Assert.Equal(0xffffffu, colours[1]);    // 63 of 63 is full brightness
        Assert.Equal(0x515151u, colours[2]);    // 20 of 63

        palette[16] = 33;           // no entry shows it: the border stays
        driver.SetPalette(palette);
        Assert.Equal(9, driver.Border);
    }

    [Fact]
    public void OnePaletteIndexCanBeGivenOneOfTheDriversColours()
    {
        var driver = Driver();
        driver.SetPaletteEntry(5, 20);
        Assert.Equal([20, 20, 20], driver.Palette[15..18]);
        Assert.Equal([0, 0, 0], driver.Palette[12..15]);
    }

    // ---- lines ----

    [Theory]
    [InlineData(0, 0, 3, 0, new[] { 0, 0, 1, 0, 2, 0, 3, 0 })]
    [InlineData(2, 1, 2, 3, new[] { 2, 1, 2, 2, 2, 3 })]
    [InlineData(0, 0, 2, 2, new[] { 0, 0, 1, 1, 2, 2 })]
    [InlineData(2, 0, 0, 2, new[] { 2, 0, 1, 1, 0, 2 })]
    [InlineData(0, 0, 4, 2, new[] { 0, 0, 1, 1, 2, 1, 3, 2, 4, 2 })]
    [InlineData(4, 2, 0, 0, new[] { 0, 0, 1, 1, 2, 1, 3, 2, 4, 2 })]   // drawn from its left end either way
    [InlineData(0, 0, 1, 4, new[] { 0, 0, 0, 1, 1, 2, 1, 3, 1, 4 })]
    [InlineData(5, 5, 5, 5, new[] { 5, 5 })]
    public void ALineHasTheOriginalsPixels(int x1, int y1, int x2, int y2, int[] pixels)
    {
        var driver = Driver();
        driver.DrawLinePort(Port(), x1, y1, x2, y2, 4);
        var expected = pixels.Chunk(2).Select(p => (p[0], p[1], 4)).OrderBy(p => p.Item2).ThenBy(p => p.Item1);
        Assert.Equal(expected, Drawn(driver.Pages[0]));
    }

    [Fact]
    public void ALineIsClippedToThePortsWindowAndDrawnFromItsOrigin()
    {
        var driver = Driver();
        var port = Port(x: 100, y: 50);
        port.Width1 = 2;
        port.Length1 = 2;
        driver.DrawLinePort(port, -5, 1, 9, 1, 4);
        Assert.Equal([(100, 51, 4), (101, 51, 4), (102, 51, 4)], Drawn(driver.Pages[0]));
        Assert.Equal((100, 50, 2), (driver.LineOriginX, driver.LineOriginY, driver.LineRight));
    }

    [Fact]
    public void TheHighByteOfTheColourChoosesHowALineCombinesWithThePicture()
    {
        var driver = Driver();
        driver.FillRect(Port(), 1, 0, 2, 1, 6);
        driver.DrawLinePort(Port(), 0, 0, 3, 0, 0x0300 | 5);     // exclusive or
        Assert.Equal([(0, 0, 5), (1, 0, 3), (2, 0, 3), (3, 0, 5)], Drawn(driver.Pages[0]));
        Assert.Equal(PixelOperation.Xor, driver.LineOperation);

        driver.DrawLinePort(Port(), 0, 0, 3, 0, 0x0100 | 1);     // and
        Assert.Equal([(0, 0, 1), (1, 0, 1), (2, 0, 1), (3, 0, 1)], Drawn(driver.Pages[0]));
        driver.DrawLinePort(Port(), 0, 0, 0, 0, 0x0200 | 4);     // or
        Assert.Equal((0, 0, 5), Drawn(driver.Pages[0])[0]);
        driver.DrawLinePort(Port(), 0, 0, 0, 0, 2);              // replace
        Assert.Equal((0, 0, 2), Drawn(driver.Pages[0])[0]);
    }
}
