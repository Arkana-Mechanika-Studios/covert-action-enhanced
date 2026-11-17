using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Platform;
using CovertAction.Presentation.Game;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The captured dossier with the Amiga's own page (docs/cut-content-options.md "On the Amiga", DIVERGENCES.md D-684): with the
/// cut-content dossier option and the Amiga pictures option both on, the game plays the Amiga's CAPT1FPO.PAN in place of
/// CAPTRD01.PAN, through the game's own animation routines, and its switch shows the woman's photograph for a woman. Needs the
/// player's game files and Amiga files (skipped without them).
/// </summary>
public sealed class AmigaDossierTests
{
    private const int SceneX = 0xe0, Width = 320, Height = 200;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "GAME.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    private static string AmigaFiles()
    {
        var folder = Environment.GetEnvironmentVariable("COVERT_AMIGA_DIR");
        Skip.If(string.IsNullOrEmpty(folder) || !Directory.Exists(folder), "no Amiga files: set COVERT_AMIGA_DIR to the folder with the Amiga disks' folders");
        return folder!;
    }

    /// <summary>The scene's 96 columns as the box under the dossier is closed after a while: the display colour numbers, and the scene's file.</summary>
    private static (byte[] Scene, string File) Dossier(GameFolder folder, GameOptions options, short face)
    {
        int[] keys = [.. Enumerable.Repeat(HeadlessHost.NoKeyYet, 80), HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 10), HostKeys.Enter];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        byte[]? scene = null;
        string file = "";
        CampaignState? campaign = null;
        var taken = 0;
        host.EntryTaken += entry =>
        {
            if (entry == HostKeys.Enter && ++taken == 1)
            {
                var pixels = host.Pixels;
                scene = new byte[(Width - SceneX) * Height];
                for (var y = 0; y < Height; y++)
                {
                    Array.Copy(pixels, y * Width + SceneX, scene, y * (Width - SceneX), Width - SceneX);
                }
                file = campaign!.Animation.AnimationFile.Text;
            }
        };
        var part = new Game(folder, host, null, options).OpenCampaign(new SharedState(), out var state);
        campaign = state;
        state.Status |= Options.Animation;
        state.Rp1.FontNr = 1;
        state.Ps[3].Face = face;
        part.Plot.ShowCapturedDossier(3);
        return (scene ?? throw new InvalidOperationException("the box never closed"), file);
    }

    [SkippableFact]
    public void WithBothOptionsTheAmigasDossierPlaysWithItsOwnColoursAndItsSwitchShowsTheWomansPhotograph()
    {
        var game = GameFiles();
        var both = new GameOptions(CutCapturedDossier: true, AmigaPictures: true, AmigaFolder: AmigaFiles());
        var (man, manFile) = Dossier(game, both, 1);
        var (woman, womanFile) = Dossier(game, both, 0);
        // the game asked for its CAPTRD01.PAN; the Amiga's page is what plays
        Assert.Equal("captrd01.pan", manFile);
        Assert.Equal("captrd01.pan", womanFile);
        // the page is drawn in the Amiga's own colours (display colours 128 and up), nearly all of its 96 x 200 pixels
        Assert.True(man.Count(pixel => pixel >= PictureColours.First) > 5000, "the page is drawn in the Amiga's colours");
        Assert.True(woman.Count(pixel => pixel >= PictureColours.First) > 5000);
        // the Amiga's script sets nothing itself: the variable the game sets (a man for bit 0 of the face) picks the photograph
        Assert.False(man.SequenceEqual(woman), "the woman's photograph differs from the man's");
    }

    [SkippableFact]
    public void WithTheDossierOptionAloneTheGamesOwnPageShowsInTheGamesColoursAndTheAmigaOptionChangesIt()
    {
        var game = GameFiles();
        var (own, ownFile) = Dossier(game, new GameOptions(CutCapturedDossier: true), 1);
        Assert.Equal("captrd01.pan", ownFile);
        Assert.All(own, pixel => Assert.True(pixel < PictureColours.First));
        var (amiga, _) = Dossier(game, new GameOptions(CutCapturedDossier: true, AmigaPictures: true, AmigaFolder: AmigaFiles()), 1);
        Assert.False(own.SequenceEqual(amiga));
    }
}
