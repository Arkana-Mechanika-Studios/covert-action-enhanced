using CovertAction.Content;
using CovertAction.Host.Headless;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The practice sessions a developer starts from the launcher (through the menus part's own start of a practice), each
/// a whole session on the headless host with the player's game files (skipped without them).
/// </summary>
public sealed class PracticeTests
{
    /// <summary>The player's game folder for a test: COVERT_GAME_DIR, else the project's own game folder.</summary>
    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "CODE.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    private static (HeadlessHost Host, PracticeResult Result, Game Game) Play(int level, params int[] keys)
    {
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host);
        return (host, game.PlayCryptoPractice(level), game);
    }

    /// <summary>A practice chase: the player takes the first cars offered and then lets the chase run for some frames without a key.</summary>
    private static (HeadlessHost Host, ChaseResult Result, Game Game) PlayChase(int frames)
    {
        int[] pause = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        int[] keys = [.. pause, HostKeys.Enter, .. pause, HostKeys.Enter, .. pause, HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, frames)];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host);
        return (host, game.PlayChasePractice(0), game);
    }

    [SkippableFact]
    public void APracticeChaseDrawsTheCityAndTheCockpitAndCanBeLeft()
    {
        var (host, _, game) = PlayChase(200);
        Assert.True(host.Frames > 100, $"only {host.Frames} frames were shown");
        Assert.True(host.Pixels.AsSpan().SequenceEqual(game.Graphics.Screen));

        // the screen: the city map on the left (a field of streets), the cockpit and the street
        // view on the right; none of the three is an empty area
        const int mapSize = 200, width = 320;
        Assert.True(Colours(host.Pixels, 1, 1, mapSize - 2, mapSize - 2) >= 2, "the city map is one colour");
        Assert.True(Colours(host.Pixels, mapSize, 0, width - mapSize, 100) >= 4, "the cockpit is not drawn");
        Assert.True(Colours(host.Pixels, mapSize, 100, width - mapSize, 100) >= 4, "the street view is not drawn");

        // the same keys at the same time of day give the same chase, pixel for pixel
        var (again, _, _) = PlayChase(200);
        Assert.True(host.Pixels.AsSpan().SequenceEqual(again.Pixels));
    }

    /// <summary>How many colours an area of the screen shows.</summary>
    private static int Colours(byte[] pixels, int x, int y, int width, int height)
    {
        var seen = new HashSet<byte>();
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                seen.Add(pixels[row * 320 + column]);
            }
        }
        return seen.Count;
    }

    [SkippableFact]
    public void APartThatIsNotInTheRewriteYetIsRefusedByItsCode()
    {
        var game = new Game(GameFiles(), new HeadlessHost([]));
        const sbyte unknownPart = 0x63;
        Assert.Throws<NotSupportedException>(() => game.RunPart(new CovertAction.Core.Shared.SharedState { NextModule = unknownPart }));
    }

    /// <summary>The colours the part has drawn: the screen has more than a few, so a picture is on it.</summary>
    private static int ScreenColours(byte[] pixels) => Colours(pixels, 0, 0, 320, 200);

    [SkippableFact]
    public void APracticeRaidStartsDrawsItsFirstScreenAndCanBeLeft()
    {
        // the player leaves the equipment screen at once, takes the first door, and the window is closed a little later
        int[] pause = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        var host = new HeadlessHost([.. pause, HostKeys.Escape, .. pause, HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 30)], unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host);
        game.PlayRaidPractice(0);
        Assert.True(host.Frames > 0);
        Assert.True(ScreenColours(host.Pixels) >= 4, "the raid's screen is not drawn");
    }

    [SkippableFact]
    public void APracticeWiretapStartsDrawsTheBoardAndCanBeLeft()
    {
        var host = new HeadlessHost([HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 30)], unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host);
        game.PlayElectronicsPractice(0);
        Assert.True(host.Frames > 0);
        Assert.True(ScreenColours(host.Pixels) >= 4, "the board is not drawn");
    }

    [SkippableFact]
    public void APracticeSessionDrawsTheWorkstationAndCanBeLeft()
    {
        // the player presses the help key, moves the cursor, guesses a letter, and then closes the game
        var (host, result, game) = Play(0, HostKeys.F1, HostKeys.Down, HostKeys.ForCharacter('e'));
        Assert.False(result.Solved);
        Assert.True(host.Frames > 0);

        // the screen: a green title bar on a black screen (the cryptography part's colours 10 and 0)
        const int green = 10, black = 0;
        Assert.Equal(green, host.Pixels[2 * 320 + 300]);
        Assert.Equal(black, host.Pixels[190 * 320 + 300]);
        Assert.Equal(0x55ff55u, host.Palette[green]);
        Assert.Equal(0x000000u, host.Palette[black]);
        Assert.True(host.Pixels.AsSpan().SequenceEqual(game.Graphics.Screen));

        // the same keys at the same time of day give the same session, pixel for pixel
        var (again, _, _) = Play(0, HostKeys.F1, HostKeys.Down, HostKeys.ForCharacter('e'));
        Assert.True(host.Pixels.AsSpan().SequenceEqual(again.Pixels));
    }

    [SkippableFact]
    public void TheLevelsAllStart()
    {
        for (var level = 0; level <= 3; level++)
        {
            var (host, result, _) = Play(level, HostKeys.Down, HostKeys.Up);
            Assert.False(result.Solved);
            Assert.Contains(host.Pixels, pixel => pixel != 0);
        }
    }
}
