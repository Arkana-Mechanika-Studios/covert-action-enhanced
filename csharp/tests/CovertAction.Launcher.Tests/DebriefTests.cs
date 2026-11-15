using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Final;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The menus part entered after a finished case (the hand-off code 9 the campaign leaves): from the launcher on the
/// headless host with the player's game files (skipped without them).
/// </summary>
public sealed class DebriefTests
{
    /// <summary>The hand-off's code of the menus part entered after a case, and of the campaign it hands over to.</summary>
    private const sbyte AfterCase = 9, CampaignPart = 0;

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

    /// <summary>
    /// The hand-off block the campaign leaves when a case is over: a case of the first plot, on the easiest level,
    /// laid out in WORLD0.DTA as the menus part lays out a new one (without its briefing's boxes), and nothing yet
    /// done in it.
    /// </summary>
    private static SharedState FinishedCase(GameFolder folder)
    {
        const short firstPlot = 1, europe = 0, startCity = 1, cia = 1;
        var state = new FinalState();
        FinalDgroup.Layout.Load(Executable.Read(folder.Read("FINAL.EXE")).DataSegment(), state);
        var shared = new SharedState { NextModule = AfterCase, GfxLetter = (short)'M' };
        // the hand-off reaches nothing but the files
        using var files = new GameFolderFiles(folder);
        var world = new Boundaries(null!, null!, null!, null!, null!, files);
        var menus = new Headquarters(state, shared, world, OriginalBehaviour.Player);
        menus.LoadWorldFile(europe);
        var career = state.Pl;
        career.Type = firstPlot;
        career.World = europe;
        career.Index = 0;
        career.Crime = state.Plots[firstPlot].CrimeFile[0];
        // the MasterMind: the first organisation (from the fourth on) that has one and that the plot allows
        career.BigOrg = (short)Enumerable.Range(4, 12).First(org => state.Org[org].Clues != -1 && (state.Org[org].Clues & state.Plots[firstPlot].OrgMask) != 0);
        career.BigLoc = 0;
        career.BigIndex = (byte)state.Org[career.BigOrg].Clues;
        career.BigFace = CaseSetup.PackMastermindLook(state, career.BigIndex);
        career.Clues = 0;
        career.Done = 0;
        career.History[0] = 0xff;
        career.Rank = 0x63;
        state.InCity = startCity;
        state.AtLoc = cia;
        state.TheCrime = career.Crime;
        menus.LoadCrimeFile(state.TheCrime);
        CaseSetup.LayOutCase(state);
        CaseSetup.LinkOrganisations(state);
        CaseSetup.AddRedHerrings(state);
        menus.HandOff.WriteEnvState(state.HandOff.StrEnvSve);
        shared.NextModule = AfterCase;
        return shared;
    }

    [SkippableFact]
    public void TheDebriefingIsShownAndTheNextCaseBegins()
    {
        var folder = GameFiles();
        var shared = FinishedCase(folder);
        // a box throws away the keys waiting when it opens: each page comes after a moment without a key
        var keys = Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HostKeys.Enter }, 400).SelectMany(pair => pair);
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);

        new Game(folder, host).RunPart(shared);

        Assert.True(host.Frames > 0);
        Assert.False(host.QuitRequested, "the keys ran out before the debriefing and the next case's briefing were over");
        Assert.Equal(CampaignPart, shared.NextModule);
        // the finished case is in the career: one month, and its record (plot 1, variant 0) at the head of the history
        var career = shared.Campaign.Plot;
        Assert.Equal(1, career.Ncase);
        Assert.Equal(1 << 4, career.History[0]);
    }

    /// <summary>
    /// Every card of the efficiency report is drawn on a panel, the cast members' cards too (FINAL 0100:2e06 calls
    /// draw_report_card before each member's name): the first card's panel at (8, 11) has its grey top edge on row 11
    /// (up to the dark grey corner) and its grey left edge in column 8. Found by the session proof (FINAL/debriefing).
    /// </summary>
    [SkippableFact]
    public void TheFirstCardOfTheEfficiencyReportIsDrawnOnItsPanel()
    {
        const int grey = 7, cardX = 8, cardY = 0xb, panelWidth = 0x130;
        var folder = GameFiles();
        var shared = FinishedCase(folder);
        var keys = Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HostKeys.Enter }, 400).SelectMany(pair => pair);
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(folder, host);
        var seen = false;
        host.EntryTaken += _ =>
        {
            var screen = game.Graphics.Screen.ToArray();
            seen |= Enumerable.Range(cardX + 1, panelWidth - 1).All(x => screen[cardY * 320 + x] == grey)
                && Enumerable.Range(cardY + 1, 200 - cardY - 2).All(y => screen[y * 320 + cardX] == grey);
        };

        game.RunPart(shared);

        Assert.True(seen, $"the efficiency report's first card was not drawn on a panel at (8, 11)");
    }
}
