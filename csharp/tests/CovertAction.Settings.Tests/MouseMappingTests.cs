using CovertAction.Host.Sdl;

namespace CovertAction.Settings.Tests;

/// <summary>
/// The SDL host's mouse mapping (PictureLayout, D-630): at every window size, a mouse position gives the game pixel the
/// stretched picture shows there. The picture is drawn at <see cref="PictureLayout.Place"/> (SdlHost draws it there) and
/// each window pixel shows the game pixel under its centre (the graphics card's sampling, nearest and pixel art alike);
/// the black bars are outside the picture; on a screen of high pixel density the events' window coordinates are half
/// (or another fraction) of the drawing area's pixels.
/// </summary>
public sealed class MouseMappingTests
{
    [Theory]
    [InlineData(960, 720, 1)]
    [InlineData(1280, 960, 1)]
    [InlineData(1920, 1080, 1)]
    [InlineData(1366, 768, 1)]
    [InlineData(1013, 571, 1)]
    [InlineData(800, 900, 1)]
    [InlineData(500, 300, 1)]
    [InlineData(2560, 1440, 2)]
    [InlineData(1920, 1200, 1.5)]
    public void EveryWindowPositionGivesTheGamePixelShownThere(int width, int height, double density)
    {
        var place = PictureLayout.Place(width, height);
        Assert.True(place.Width * 3 == place.Height * 4 || Math.Abs(place.Width * 3 - place.Height * 4) < 4, "the place is 4:3");
        var window = ((int)Math.Round(width / density), (int)Math.Round(height / density));
        var failures = new List<string>();
        for (var py = 0; py < height; py += 3)
        {
            for (var px = 0; px < width; px += 5)
            {
                // the event's window coordinates of the drawing area's pixel px (its middle)
                var (wx, wy) = ((float)((px + 0.5) * window.Item1 / width), (float)((py + 0.5) * window.Item2 / height));
                var (gx, gy, inside) = PictureLayout.ToGame(place, (width, height), window, wx, wy);
                var shown = px >= place.X && px < place.X + place.Width && py >= place.Y && py < place.Y + place.Height;
                if (inside != shown)
                {
                    failures.Add($"({px},{py}): inside {inside}, picture {shown}");
                    continue;
                }
                if (!shown)
                {
                    continue;
                }
                // the game pixel under the window pixel's centre, as the stretched picture shows it
                var expectedX = (int)Math.Floor((px - place.X + 0.5) * 320 / place.Width);
                var expectedY = (int)Math.Floor((py - place.Y + 0.5) * 200 / place.Height);
                var tie = PictureLayout.IsTie(px - place.X, place.Width, 320) || PictureLayout.IsTie(py - place.Y, place.Height, 200);
                if ((gx, gy) != (expectedX, expectedY) && !(tie && Math.Abs(gx - expectedX) <= 1 && Math.Abs(gy - expectedY) <= 1))
                {
                    failures.Add($"({px},{py}) shows ({expectedX},{expectedY}), mouse gives ({gx},{gy})");
                }
            }
        }
        Assert.True(failures.Count == 0, $"{width}x{height} at {density}x: " + string.Join("; ", failures.Take(10)));
    }

    [Fact]
    public void TheCornersOfThePictureAreItsCornerPixelsAndTheBarsAreOutside()
    {
        var place = PictureLayout.Place(1920, 1080);
        Assert.Equal((240, 0, 1440, 1080), place);
        Assert.Equal((0, 0, true), PictureLayout.ToGame(place, (1920, 1080), (1920, 1080), 240.5f, 0.5f));
        Assert.Equal((319, 199, true), PictureLayout.ToGame(place, (1920, 1080), (1920, 1080), 1679.5f, 1079.5f));
        Assert.Equal((0, 50, false), PictureLayout.ToGame(place, (1920, 1080), (1920, 1080), 10f, 270.5f));
        Assert.Equal((319, 50, false), PictureLayout.ToGame(place, (1920, 1080), (1920, 1080), 1900f, 270.5f));
    }

    [Fact]
    public void SdlsSoftwareRendererIsFollowedWhereItDraws()
    {
        // 16.16 fixed point, as SDL's software stretching steps: at 1366x768 it lands short of the exact centre in places
        var place = PictureLayout.Place(1366, 768);
        for (var px = 0; px < place.Width; px++)
        {
            var inc = (320L << 16) / place.Width;
            var expected = (int)((px * inc + inc / 2) >> 16);
            Assert.Equal(expected, PictureLayout.ToGame(place, (1366, 768), (1366, 768), place.X + px + 0.5f, 384.5f, software: true).X);
        }
    }

    [Fact]
    public void TheTextPagesCellsLieOverTheSamePlace()
    {
        Assert.Equal((0, 0), PictureLayout.TextCell(0, 0));
        Assert.Equal((79, 24), PictureLayout.TextCell(319, 199));
        // a text cell is 4 game pixels wide and 8 high over the same 4:3 place
        Assert.Equal((10, 3), PictureLayout.TextCell(40, 24));
        Assert.Equal((9, 2), PictureLayout.TextCell(39, 23));
    }
}

/// <summary>The mouse option in the settings (input.mouse, D-630): off by default, the file and the command line turn it on or off.</summary>
public sealed class MouseSettingTests
{
    [Fact]
    public void TheMouseIsOffByDefaultAndTheFileAndTheCommandLineSetIt()
    {
        var template = SettingsFile.Parse(SettingsFile.Template);
        Assert.Empty(template.Problems);
        Assert.False(StartSettings.Resolve(CommandLine.Parse([]), _ => null, template.Settings).Mouse);

        var on = SettingsFile.Parse("""{ "input": { "mouse": true } }""");
        Assert.Empty(on.Problems);
        Assert.True(StartSettings.Resolve(CommandLine.Parse([]), _ => null, on.Settings).Mouse);

        var off = StartSettings.Resolve(CommandLine.Parse(["--no-mouse"]), _ => null, on.Settings);
        Assert.False(off.Mouse);
        Assert.Equal("the command line (--no-mouse)", off.FixedKeys["input.mouse"]);
        var cli = StartSettings.Resolve(CommandLine.Parse(["--mouse"]), _ => null, template.Settings);
        Assert.True(cli.Mouse);
        Assert.Equal("the command line (--mouse)", cli.FixedKeys["input.mouse"]);

        Assert.NotEmpty(SettingsFile.Parse("""{ "input": { "mouse": "yes" } }""").Problems);
    }

    [Fact]
    public void TheLauncherSavesTheMouseOption()
    {
        var path = Path.Combine(Path.GetTempPath(), "covert-mouse-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.True(SettingsFile.TryWriteTemplate(path, out _));
            Assert.True(SettingsFileEdits.TrySet(path, new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["input.mouse"] = true }, out var message), message);
            var load = SettingsFile.Load(path);
            Assert.Empty(load.Problems);
            Assert.True(load.Settings.Mouse);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
