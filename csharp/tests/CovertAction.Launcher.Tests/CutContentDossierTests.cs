using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Platform;
using CovertAction.Presentation.Game;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The captured dossier (docs/cut-content.md CC-1, CC-19; DIVERGENCES.md D-660): CAPTRD01.PAN plays when a cast member is
/// captured, with the woman's photograph for a woman, only when the option is on. Needs the player's game files (skipped
/// without them).
/// </summary>
public sealed class CutContentDossierTests
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

    /// <summary>What the screen shows when the box under the dossier is closed after a while, and how many keys were asked for.</summary>
    private sealed record Shown(byte[] Pixels, uint[] Palette, string Scene);

    private static (CampaignPart Part, CampaignState State, HeadlessHost Host, Func<Shown> Taken) Open(GameFolder folder, bool option)
    {
        // a box throws away the keys waiting when it opens, and the scene runs while it waits
        int[] keys = [.. Enumerable.Repeat(HeadlessHost.NoKeyYet, 80), HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 10), HostKeys.Enter];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        byte[]? pixels = null;
        uint[]? palette = null;
        string scene = "";
        CampaignState? campaign = null;
        var taken = 0;
        host.EntryTaken += entry =>
        {
            if (entry == HostKeys.Enter && ++taken == 1)
            {
                (pixels, palette, scene) = ((byte[])host.Pixels.Clone(), (uint[])host.Palette.Clone(), campaign!.Animation.AnimationFile.Text);
            }
        };
        var game = new Game(folder, host, null, new GameOptions(CutCapturedDossier: option));
        var part = game.OpenCampaign(new SharedState(), out var state);
        campaign = state;
        state.Status |= Options.Animation;
        state.Rp1.FontNr = 1;
        return (part, state, host, () => new Shown(pixels ?? throw new InvalidOperationException("the box never closed"), palette!, scene));
    }

    /// <summary>The scene the dossier shows for a person of the given face (bit 0: a man).</summary>
    private static Shown Dossier(GameFolder folder, bool option, short face)
    {
        var (part, state, _, taken) = Open(folder, option);
        state.Ps[3].Face = face;
        part.Plot.ShowCapturedDossier(3);
        return taken();
    }

    private static bool SameScene(Shown a, Shown b)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = SceneX; x < Width; x++)
            {
                if (a.Palette[a.Pixels[y * Width + x]] != b.Palette[b.Pixels[y * Width + x]])
                {
                    return false;
                }
            }
        }
        return true;
    }

    [SkippableFact]
    public void TheDossierIsTheAnimationCaptrd01InTheScenesFrame()
    {
        var shown = Dossier(GameFiles(), true, 1);
        Assert.Equal("captrd01.pan", shown.Scene);
        // something is drawn in the 96 columns at the right, where the scenes play
        var colours = new HashSet<uint>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = SceneX; x < Width; x++)
            {
                colours.Add(shown.Palette[shown.Pixels[y * Width + x]]);
            }
        }
        Assert.True(colours.Count >= 4, "the dossier page should show several colours (the page, the photograph, the text, the stamp)");
    }

    [SkippableFact]
    public void TheWomansPhotographShowsForAWomanAndTheMansForAManWithTheOptionOn()
    {
        var folder = GameFiles();
        // bit 0 of the face is the person's sex: set, a man (script variable -1); clear, a woman (1)
        var man = Dossier(folder, true, 1);
        var woman = Dossier(folder, true, 0);
        Assert.False(SameScene(man, woman), "the woman's photograph differs from the man's");
        // the man is the page the script draws anyway: option off, the script's own switch shows him for a man and a woman alike
        var scriptAlone = Dossier(folder, false, 0);
        Assert.True(SameScene(man, scriptAlone));
    }

    [SkippableFact]
    public void TheOptionIsOffByDefault()
    {
        Assert.False(new GameOptions().CutCapturedDossier);
        Assert.False(OriginalBehaviour.ForPlayer(false, false).CutContent.CapturedDossier);
        Assert.False(OriginalBehaviour.Faithful.CutContent.CapturedDossier);
        Assert.False(OriginalBehaviour.Player.CutContent.CapturedDossier);
        Assert.True(new GameOptions(CutCapturedDossier: true).Behaviour.CutContent.CapturedDossier);
    }

    /// <summary>The animations shown while a cast member's capture is questioned (InterrogateSuspect), in order.</summary>
    private static List<string> Interrogation(GameFolder folder, bool option, short face)
    {
        int[] keys = [.. Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.Enter }, 60).SelectMany(x => x)];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var seen = new List<string>();
        CampaignState? campaign = null;
        host.EntryTaken += _ =>
        {
            var scene = campaign!.Animation.AnimationFile.Text;
            if (scene.Length > 0 && (seen.Count == 0 || seen[^1] != scene))
            {
                seen.Add(scene);
            }
        };
        var shared = new SharedState { NotHurt = 1 };
        var game = new Game(folder, host, null, new GameOptions(CutCapturedDossier: option));
        var part = game.OpenCampaign(shared, out var state);
        campaign = state;
        state.Status |= Options.Animation;
        state.Rp1.FontNr = 1;
        // a cast of four, the second of them person 3: an agent of organisation 2 at the CIA's city, a known role, caught in a fight
        state.NACTIVITY = 4;
        state.Ac[1].Who = 3;
        state.Ps[3].Org = 2;
        state.Ps[3].Face = face;
        state.Ps[3].Role = 2;
        state.Ps[3].Rknown = 2;
        part.Plot.InterrogateSuspect(3);
        return seen;
    }

    [SkippableFact]
    public void ACapturedCastMemberIsShownTheDossierBeforeTheChiefsCommentOnlyWithTheOptionOn()
    {
        var folder = GameFiles();
        var off = Interrogation(folder, false, 1);
        var on = Interrogation(folder, true, 1);
        Assert.DoesNotContain("captrd01.pan", off);
        Assert.Contains("captrd01.pan", on);
        // the scenes of the original are all there, in their order, and the dossier lies between the jail and the chief's briefing
        Assert.Equal(off, on.Where(scene => scene != "captrd01.pan"));
        var at = on.IndexOf("captrd01.pan");
        Assert.True(at > 0 && at < on.Count - 1);
    }
}
