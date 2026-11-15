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
/// The campaign part entered for the first time in a case (hand-off entered 0): the hidden turns that give the agent's
/// side its first persons, the chief's briefing and the city screen. From the launcher on the headless host with the
/// player's game files (skipped without them).
/// </summary>
public sealed class NewCaseTests
{
    /// <summary>The hand-off's code of the campaign part.</summary>
    private const sbyte CampaignPart = 0;

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
    /// The hand-off block the menus part leaves for a new case: a case of the first plot laid out in WORLD0.DTA as the
    /// menus part lays out a new one (without its briefing's boxes), nothing done in it yet, and "entered" 0.
    /// </summary>
    private static SharedState FreshCase(GameFolder folder, short level)
    {
        const short firstPlot = 1, europe = 0, startCity = 1, cia = 1;
        var state = new FinalState();
        FinalDgroup.Layout.Load(Executable.Read(folder.Read("FINAL.EXE")).DataSegment(), state);
        var shared = new SharedState { NextModule = CampaignPart, Entered = 0, GfxLetter = (short)'M' };
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
        state.Diff = level;
        state.InCity = startCity;
        state.AtLoc = cia;
        state.TheCrime = career.Crime;
        menus.LoadCrimeFile(state.TheCrime);
        CaseSetup.LayOutCase(state);
        CaseSetup.LinkOrganisations(state);
        CaseSetup.AddRedHerrings(state);
        menus.HandOff.WriteEnvState(state.HandOff.StrEnvSve);
        shared.NextModule = CampaignPart;
        shared.Entered = 0;
        return shared;
    }

    /// <summary>
    /// A fresh case's first entry: the hidden turns run, the chief briefs the agent, and the agent stands at the city
    /// screen. The keys are a moment without a key and a Return, over and over: each box takes the Return; at the city
    /// screen it chooses the airport and then, on the flight map, to stay, (whatever the building menus do with a Return), until the keys end or the case runs out of time and the part
    /// hands over; no part of it may throw.
    /// </summary>
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(3)]
    public void ANewCaseIsBriefedAndTheAgentReachesTheCityScreen(short level)
    {
        var folder = GameFiles();
        var shared = FreshCase(folder, level);
        var keys = Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HostKeys.Enter }, 600).SelectMany(pair => pair);
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);

        new Game(folder, host).RunPart(shared);

        Assert.True(host.Frames > 0);
        // the briefing is over (the part marked the case entered) and the keys then took the agent through the city screens
        Assert.Equal(1, shared.Entered);
    }
}

