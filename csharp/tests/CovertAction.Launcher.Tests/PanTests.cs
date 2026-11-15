using CovertAction.Content;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The animation reader and player: on a small animation made up for the test, and on every animation of the
/// player's game against the frames of the project's verified players (build/validation/pan-N, written by
/// the reference player: the frame after 1, 2, 10, 30, 100 and 300 ticks). Skipped without them.
/// </summary>
public sealed class PanTests
{
    /// <summary>An animation of 4 by 2 pixels on a background of colour 3, with one image (a single pixel of colour 9) and the given program.</summary>
    private static byte[] Animation(params byte[] program)
    {
        var padded = new byte[16];
        program.CopyTo(padded, 0);
        byte[] image = [7, 0, 1, 0, 1, 0, 11, 9, 0];    // format 7, 1 by 1, codes up to 11 bits, the byte 09h as a 9-bit code
        return
        [
            .. "PANI"u8, 3, 1,
            0,                                  // no palette of its own
            0, 0, 0, 0,                         // place on the screen
            3, 0, 1, 0,                         // width and height less one
            0, 0,                               // delay
            2, 3, 0,                            // background: colour 3
            1, 0, .. new byte[2 * 249],         // image 0 is there
            .. image, 0,                        // (padded to an even length)
            1, 0,                               // the program: one unit of 16 bytes
            .. padded,
        ];
    }

    [Fact]
    public void AnAnimationStartsWithItsBackgroundAndEndsWhenItsProgramSaysSo()
    {
        const byte end = 21;
        var file = PanFile.Read(Animation(end));
        Assert.Equal((4, 2), (file.Width, file.Height));
        Assert.NotNull(file.Images[0]);
        Assert.Null(file.Images[1]);
        var player = new PanPlayer(file);
        Assert.All(player.Frame, pixel => Assert.Equal(3, pixel));
        Assert.False(player.Tick());
        Assert.True(player.Ended);
    }

    [Fact]
    public void ASpriteShowsItsImageWhereItWasStartedAndTheProgramRests()
    {
        // push the seven values of "start sprite" (program start 14, number 1, no parent, x 2, y 1, speed 255, tracked),
        // start it, rest; the sprite's own program at 14: show image 0, pause
        byte[] Push(int value) => [5, 0, (byte)value, (byte)(value >> 8)];
        byte[] program = [.. Push(30), .. Push(1), .. Push(-1), .. Push(2), .. Push(1), .. Push(255), .. Push(0), 0, 20];
        var file = Animation();
        var withProgram = new List<byte>(file[..^16]);
        withProgram[^2] = 3;        // three units of 16 bytes
        byte[] sprite = [0, 0, 9];  // show image 0; pause
        withProgram.AddRange([.. program, .. sprite, .. new byte[48 - program.Length - sprite.Length]]);
        var player = new PanPlayer(PanFile.Read(withProgram.ToArray()));
        Assert.Equal(30, program.Length);
        Assert.True(player.Tick());
        Assert.Equal([3, 3, 3, 3, 3, 3, 9, 3], player.Frame);
        Assert.True(player.Tick());     // the program rests, the sprite is paused: the same frame
        Assert.Equal([3, 3, 3, 3, 3, 3, 9, 3], player.Frame);
    }

    [Fact]
    public void ADamagedFileIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PanFile.Read([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Throws<InvalidDataException>(() => PanFile.Read(Animation(21)[..40]));
        Assert.Throws<InvalidDataException>(() => new PanPlayer(PanFile.Read(Animation(99))).Tick());   // no such instruction
    }

    [SkippableFact]
    public void EveryAnimationOfTheGamePlaysAsTheVerifiedPlayersPlayIt()
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); root is null && dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "build", "validation", "pan-1")) && Directory.Exists(Path.Combine(dir.FullName, "game", "Covert Action", "Covert")))
            {
                root = dir.FullName;
            }
        }
        Skip.If(root is null, "needs the game's files and build/validation/pan-N (frames of the project's reference player)");
        var game = new GameFolder(Path.Combine(root!, "game", "Covert Action", "Covert"));
        var compared = 0;
        foreach (var ticks in new[] { 1, 2, 10, 30, 100, 300 })
        {
            var reference = Path.Combine(root!, "build", "validation", $"pan-{ticks}");
            foreach (var pixelsFile in Directory.EnumerateFiles(reference, "*.pixels"))
            {
                var name = Path.GetFileNameWithoutExtension(pixelsFile);
                var expectedPixels = File.ReadAllBytes(pixelsFile);
                var expectedPalette = File.ReadAllBytes(Path.ChangeExtension(pixelsFile, ".palette"));
                var file = PanFile.Read(game.Read(name));
                var player = new PanPlayer(file);
                for (var tick = 0; tick < ticks && player.Tick(); tick++)
                {
                }
                Assert.True(expectedPixels.Length == player.Frame.Length, $"{name}: {file.Width} by {file.Height} pixels, the reference has {expectedPixels.Length}");
                // compared as the colours shown: the palette index of each pixel through the animation's palette
                var differing = Enumerable.Range(0, expectedPixels.Length).Count(i => expectedPalette[expectedPixels[i]] != file.Palette[player.Frame[i]]);
                Assert.True(differing == 0, $"{name} after {ticks} ticks: {differing} pixels differ from the verified players'");
                compared++;
            }
        }
        Assert.True(compared >= 6 * 24, $"only {compared} frames compared");
    }
}
