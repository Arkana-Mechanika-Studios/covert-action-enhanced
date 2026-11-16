using CovertAction.Content;

namespace CovertAction.Drivers.Tests;

/// <summary>The display colours 128 to 255 that the Amiga version's pictures take for their own colours (D-642).</summary>
public sealed class PictureColoursTests
{
    [Fact]
    public void AColourOfAPictureGetsTheNextDisplayColourFrom128AndKeepsItWhenItComesAgain()
    {
        var palette = new byte[3 * 256];
        var colours = new PictureColours(palette);
        Assert.Equal(128, colours.Entry(0xf80));
        Assert.Equal(129, colours.Entry(0x08f));
        Assert.Equal(128, colours.Entry(0xf80));
        Assert.Equal(2, colours.Count);
        // the components widened from 4 bits to the VGA's 6: full stays full, 8 of 15 is 34
        Assert.Equal(new byte[] { 63, 34, 0 }, palette[(3 * 128)..(3 * 128 + 3)]);
        Assert.Equal(new byte[] { 0, 34, 63 }, palette[(3 * 129)..(3 * 129 + 3)]);
        // the colours below 128, the game's, are not touched
        Assert.All(palette[..(3 * 128)], value => Assert.Equal(0, value));
    }

    [Fact]
    public void ADisplayColourIsGivenAgainOnlyAfterAReset()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        Assert.Equal(128, colours.Entry(0x123));
        colours.Reset();
        Assert.Equal(0, colours.Count);
        Assert.Equal(128, colours.Entry(0x456));
    }

    [Fact]
    public void WhenAll128DisplayColoursAreGivenAFurtherColourIsTheNearestGivenOne()
    {
        var colours = new PictureColours(new byte[3 * 256]);
        // 128 different colours: 16 reds times 8 greens, no blue
        var given = Enumerable.Range(0, PictureColours.Capacity).Select(i => i / 8 << 8 | i % 8 << 4).ToArray();
        foreach (var rgb in given)
        {
            colours.Entry(rgb);
        }
        Assert.Equal(PictureColours.Capacity, colours.Count);
        // 0x501 differs from the given 0x500 by one step of blue only
        Assert.Equal(PictureColours.First + Array.IndexOf(given, 0x500), colours.Entry(0x501));
        Assert.Equal(PictureColours.Capacity, colours.Count);
    }

    [Fact]
    public void TheModeSetClearsThePicturesColoursOnlyWithTheScreen()
    {
        var graphics = new McgaGraphicsDriver(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);
        graphics.PictureColours.Entry(0xabc);
        graphics.SubCode0208(0x80);     // the screen is kept, and with it its pictures' colours
        Assert.Equal(1, graphics.PictureColours.Count);
        graphics.SubCode0208(0);
        Assert.Equal(0, graphics.PictureColours.Count);
    }

    [Fact]
    public void TheModeSetLeavesThePicturesColoursInThePalette()
    {
        var graphics = new McgaGraphicsDriver(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);
        var entry = graphics.PictureColours.Entry(0x0f0);
        graphics.SubCode0208(0x80);
        // the BIOS's mode set loads the driver's 128 colours (here all black) and leaves colours 128 and above
        Assert.Equal(new byte[] { 0, 63, 0 }, graphics.Palette[(3 * entry)..(3 * entry + 3)]);
    }

    [Fact]
    public void TheGamesOwnColoursAreNotInTheRangeThePicturesUse()
    {
        // colours 0 to 15 are what the game's palette calls set; the pictures' colours begin at 128 and the game never draws with them
        Assert.True(PictureColours.First > 16);
        var graphics = new McgaGraphicsDriver(new FontSet(McgaGraphicsDriverTests.FontFile), new byte[3 * McgaDriverFile.Colours]);
        graphics.PictureColours.Entry(0xfff);
        graphics.SetPaletteEntry(5, 7);
        graphics.SetPalette(new byte[17]);
        Assert.Equal(new byte[] { 63, 63, 63 }, graphics.Palette[(3 * PictureColours.First)..(3 * PictureColours.First + 3)]);
    }
}
