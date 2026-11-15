using CovertAction.Content;
using CovertAction.Host.Headless;
using CovertAction.Core.Shared;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>The menus part (FINAL) started from the launcher on the headless host with the player's game files (skipped without them).</summary>
public sealed class MenusTests
{
    private const sbyte FreshStart = 0x0b, PracticeChase = 0x0e;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "FINAL.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>The menu is drawn, and "Practice a skill" with the second skill (driving) hands over to the car chase.</summary>
    [SkippableFact]
    public void ThePracticeChoiceOfTheMainMenuHandsOverToTheChase()
    {
        // the first key ends the title (the credits are then skipped); then: Practice (third choice), level 0, second skill
        int[] wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        int[] keys = [.. wait, HostKeys.Space, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait, HostKeys.Enter, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };

        // only the menus part runs: its hand-off block names the chase, which would be the launcher's next step
        game.RunPart(shared);
        Assert.Equal(PracticeChase, shared.NextModule);
        Assert.True(host.Frames > 0);
    }

    /// <summary>
    /// The campaign's "decode a message" hands over with code 2 (CODE.EXE in the original launcher's table, COVERT
    /// DS:0124); the launcher knew only the practice session's 0x0f (found in the first played session).
    /// </summary>
    [SkippableFact]
    public void TheCampaignsCodedMessageOpensTheCryptographyWorkstation()
    {
        const sbyte decodeMessage = 2;
        // no key: the workstation starts, draws and ends at its first wait for a key (the headless host then quits)
        var host = new HeadlessHost([], unixSeconds: 600_000_000);
        var shared = new SharedState { NextModule = decodeMessage, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };
        shared.Text.Text = "MEET THE COURIER IN ROME.";

        var exception = Record.Exception(() => new Game(GameFiles(), host).RunPart(shared));

        Assert.Null(exception);
        Assert.True(host.Frames > 0);
    }

    /// <summary>
    /// The game given a saves folder (as the program players start gives it one) writes the Hall of Fame there, with
    /// the player's entries, and leaves the player's copy as it is (found by the session proof: the hosts gave none).
    /// </summary>
    [SkippableFact]
    public void TheHallOfFameIsWrittenToTheSavesFolder()
    {
        var files = GameFiles();
        var saves = Directory.CreateTempSubdirectory("covert-saves-");
        try
        {
            var players = File.ReadAllBytes(files.Find("FAME.DTA")!);
            // the first key ends the title; then the fourth choice of the main menu, the Hall of Fame
            int[] wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
            int[] keys = [.. wait, HostKeys.Space, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Down, .. wait, HostKeys.Enter, .. wait];
            var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
            var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };

            new Game(files, host, saves.FullName).RunPart(shared);

            var written = Path.Combine(saves.FullName, "FAME.DTA");
            Assert.True(File.Exists(written));
            Assert.Equal(players, File.ReadAllBytes(written)[..players.Length]);
            Assert.Equal(players, File.ReadAllBytes(files.Find("FAME.DTA")!));
        }
        finally
        {
            saves.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The main menu waits by asking for a key again and again: the screen must be shown while it does, or the player
    /// sees the end of the title (black) and answers a menu he cannot see (found in the first played session).
    /// </summary>
    [SkippableFact]
    public void TheMainMenuIsShownWhileItWaitsForAKey()
    {
        // the title and the credits play to their end; then the menu waits for a long moment without a key
        var keys = Enumerable.Repeat(HeadlessHost.NoKeyYet, 1500).ToArray();
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };

        new Game(GameFiles(), host).RunPart(shared);

        // the last picture shown is the menu's box, not the black end of the credits
        var palette = host.Palette.ToArray();
        Assert.Contains(host.Pixels.ToArray(), pixel => (palette[pixel] & 0xFFFFFF) != 0);
    }
}
