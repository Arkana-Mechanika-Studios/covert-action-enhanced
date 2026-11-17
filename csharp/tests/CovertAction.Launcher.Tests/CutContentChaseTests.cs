using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The car chase on its own (docs/cut-content.md CC-2; DIVERGENCES.md D-661): the practice menu has a fifth choice when the
/// option is on, and the chase it starts runs without a campaign. Off, the practice menu has its four skills as the original
/// has. Needs the player's game files (skipped without them).
/// </summary>
public sealed class CutContentChaseTests
{
    private const sbyte FreshStart = 0x0b, PracticeRaid = 0x0d, PracticeChase = 0x0e, PracticeWiretap = 0x10;

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

    private static readonly int[] Wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];

    /// <summary>The title, then "Practice", the first level and <paramref name="downs"/> downs in the practice menu, then the chase's first cars and some frames.</summary>
    private static int[] Keys(int downs) =>
    [
        .. Wait, HostKeys.Space, .. Wait, HostKeys.Down, .. Wait, HostKeys.Down, .. Wait, HostKeys.Enter, .. Wait, HostKeys.Enter,
        .. Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.Down }, downs).SelectMany(x => x),
        .. Wait, HostKeys.Enter,
        .. Wait, HostKeys.Enter, .. Wait, HostKeys.Enter, .. Wait, HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 150),
    ];

    /// <summary>The menus part up to the practice's hand-off, and, with <paramref name="chase"/>, the part that comes next.</summary>
    private static (Game Game, SharedState Shared) Practice(bool option, int downs, bool chase, long unixSeconds = 600_000_000)
    {
        var host = new HeadlessHost(Keys(downs), unixSeconds);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };
        var game = new Game(GameFiles(), host, null, new GameOptions(CutStandaloneChase: option));
        game.RunPart(shared);
        if (chase)
        {
            game.RunPart(shared);
        }
        return (game, shared);
    }

    [SkippableFact]
    public void TheOptionIsOffByDefaultAndTheFourSkillsStandAsTheOriginalHasThem()
    {
        Assert.False(new GameOptions().CutStandaloneChase);
        Assert.False(OriginalBehaviour.ForPlayer(false, false).CutContent.StandaloneChase);
        Assert.False(OriginalBehaviour.Faithful.CutContent.StandaloneChase);
        // off, the fourth press of Down reaches the last of the four skills (electronics), and no choice follows it
        Assert.Equal(PracticeWiretap, Practice(false, 4, chase: false).Shared.NextModule);
        // and the driving practice (the second choice) is the chase as ever, a campaign one
        var (driving, shared) = Practice(false, 1, chase: false);
        Assert.Equal(PracticeChase, shared.NextModule);
        driving.RunPart(shared);
        Assert.NotNull(driving.LastChase);
        Assert.False(driving.LastChaseStandalone);
    }

    [SkippableFact]
    public void WithTheOptionTheFifthChoiceIsTheChaseOnItsOwnAndTheOtherChoicesAreAsBefore()
    {
        var (game, shared) = Practice(true, 4, chase: true);
        // the chase part of the driving practice ran without a campaign
        Assert.True(game.LastChaseStandalone);
        Assert.True(shared.Flags.HasFlag(Options.Practice));

        // the driving practice is a campaign chase, the first choice the raid, with the option on as off
        var (driving, _) = Practice(true, 1, chase: true);
        Assert.NotNull(driving.LastChase);
        Assert.False(driving.LastChaseStandalone);
        Assert.Equal(PracticeRaid, Practice(true, 0, chase: false).Shared.NextModule);
    }

    [SkippableFact]
    public void TheStandaloneChaseDrawsAnotherMapAtEachStartAndLabelsEveryPlace()
    {
        var first = Practice(true, 4, chase: true, unixSeconds: 600_000_000).Game.LastChase!;
        var second = Practice(true, 4, chase: true, unixSeconds: 612_345_678).Game.LastChase!;
        Assert.False(first.CampaignMode);
        Assert.NotEqual(first.LocX.Concat(first.LocY), second.LocX.Concat(second.LocY));
        // every place of the map has a spot of its own, and the start and the goal are two of them, neither the campaign's organisation
        Assert.All(new[] { first, second }, state =>
        {
            Assert.InRange(state.StartLoc, 1, ChaseState.Locations - 1);
            Assert.InRange(state.DestLoc, 1, ChaseState.Locations - 1);
            Assert.NotEqual(state.StartLoc, state.DestLoc);
        });
    }
}
