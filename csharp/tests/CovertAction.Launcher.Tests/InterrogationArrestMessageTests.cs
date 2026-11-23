using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Platform;
using CovertAction.Presentation.Game;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// GitHub issue #2 (docs/original-bugs.md CA-66): the interrogation's own queued message (type 6 or 12, no person) has the
/// minute of the suspect's event messages, so the decode's partner search takes it as the partner. The original then
/// reads and writes ps[-1], the end of the text in front of the persons table; the rewrite did the same with an index
/// of -1 and threw. Needs the player's game files (skipped without them).
/// </summary>
public sealed class InterrogationArrestMessageTests
{
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

    [SkippableFact]
    public void TheArrestsOwnMessageIsTakenAsPartnerAndReadsTheTextInFrontOfThePersonsTable()
    {
        int[] keys = [.. Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.Enter }, 120).SelectMany(x => x)];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host, null, new GameOptions());
        var part = game.OpenCampaign(new SharedState { NotHurt = 1 }, out var state);
        state.Status |= Options.Animation;
        state.Rp1.FontNr = 1;
        // the program's text in front of ps (characters 12 and 13 are the clue word of "person -1")
        state.TextBeforePersons = "One moment please...";
        state.NACTIVITY = 4;
        state.Ac[1].Who = 3;
        state.Ac[2].Who = 4;
        state.Ps[3].Org = 2;
        state.Ps[3].Role = 2;
        state.Ps[3].Rknown = 2;
        state.Ps[4].Org = 3;
        // the suspect has sent a message in this very minute: event 0 of member 1 to member 2
        state.Ev[0].Act = 1;
        state.Ev[0].Status = 0x200 | 2;
        state.Sn[0].Time = state.Gtime;
        state.Sn[0].Type = 0;
        state.Sn[0].Data = 0;
        state.Sn[0].Who = 3;
        state.Sn[0].Who2 = 4;
        state.Sn[0].Clues = 0;
        state.NextSyn = 1;

        part.Plot.InterrogateSuspect(3);

        // the turned suspect's message (type 12, nobody) follows the message, and both are read; ps[-1].clues is characters 12 and 13
        Assert.Equal(2, state.NextSyn);
        Assert.Equal(-1, state.Sn[1].Who);
        Assert.NotEqual(0, state.Sn[0].Clues & unchecked((short)0x8000));
        Assert.NotEqual(0, state.Sn[1].Clues & unchecked((short)0x8000));
        // the decode ORs known parts (bits 1 to 3) into those two bytes and touches nothing else of the text
        const string before = "One moment please...";
        var after = state.TextBeforePersons;
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            var onlyGained = i is 12 or 13 ? (after[i] & before[i]) == before[i] && (after[i] & ~before[i] & ~0xe) == 0 : after[i] == before[i];
            Assert.True(onlyGained, $"character {i}: '{before[i]}' became '{after[i]}'");
        }
    }
}
