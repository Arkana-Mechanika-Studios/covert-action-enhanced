using CovertAction.Content;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The campaign part from the launcher's side: a whole walk on the headless host with the player's game files
/// (skipped without them), from a hand-off block as the part finds it on coming back from a mission.
/// </summary>
public sealed class CampaignTests
{
    /// <summary>The hand-off's code of the campaign part, and the exit code of the player's leaving the game.</summary>
    private const sbyte CampaignPart = 0, BreakInRaid = 1, LeaveTheGame = -1;

    /// <summary>The organisation of the player's own agency, whose building in a city is the CIA's headquarters.</summary>
    private const int PlayerAgency = 1;

    /// <summary><see cref="Core.Records.Location.Clues"/>: the player knows which organisation the building belongs to.</summary>
    private const int BuildingIdentified = 4;

    /// <summary>The hotel's menu: the downs from its first choice to Save Game, Load Game and Quit.</summary>
    private const int SaveGame = 3, LoadGame = 4, QuitGame = 8;

    /// <summary>The player's game folder for a test: COVERT_GAME_DIR, else the project's own game folder.</summary>
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

    /// <summary>
    /// A hand-off block of a campaign under way, as the part finds it when it comes back from a mission that was not
    /// played: the agent in the first city of the European world (WORLD0.DTA) at eight in the morning of the first
    /// day, on the easiest level, with a cast of no one and no events yet.
    /// </summary>
    internal static SharedState CampaignUnderWay(GameFolder folder, params int[] buildingsKnown)
    {
        const int counts = 4, eightInTheMorning = 8 * 60;
        var shared = new SharedState
        {
            NextModule = CampaignPart,
            Entered = 1,
            GameMode = 0,
            GfxLetter = (short)'M',
            City = 0,
            Org = 1,
            Clock = eightInTheMorning,
            Person = -1,
            SelPerson = -1,
        };
        // the buildings the player knows in the first city: each is a line of the city screen's menu after the hotel
        for (var i = 0; i < buildingsKnown.Length; i++)
        {
            shared.Campaign.Locations[i].Org = (short)buildingsKnown[i];
            shared.Campaign.Locations[i].Loc = 0;
            shared.Campaign.Locations[i].Clues = BuildingIdentified;
        }
        shared.NLinks = (short)buildingsKnown.Length;
        var file = folder.Read("WORLD0.DTA");
        var (cities, orgs) = (shared.Campaign.Cities, shared.Campaign.Orgs);
        for (var i = 0; i < cities.Length; i++)
        {
            CampaignLayouts.City.Load(file.AsSpan(counts + i * CampaignLayouts.CitySize, CampaignLayouts.CitySize), cities[i]);
        }
        var orgTable = counts + cities.Length * CampaignLayouts.CitySize;
        for (var i = 0; i < orgs.Length; i++)
        {
            CampaignLayouts.Org.Load(file.AsSpan(orgTable + i * CampaignLayouts.OrgSize, CampaignLayouts.OrgSize), orgs[i]);
        }
        return shared;
    }

    [SkippableFact]
    public void TheAgentReachesTheCityScreenFliesToAnotherCityAndLeavesTheGameFromTheHotel()
    {
        var folder = GameFiles();
        // a box throws away the keys waiting when it opens: each choice comes after a moment without a key
        int[] choose(int downs) => [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, .. Enumerable.Repeat(HostKeys.Down, downs), HostKeys.Enter];
        int[] keys =
        [
            .. choose(0),       // the city screen: to the airport
            .. choose(2),       // the flight map: past the city the agent is in, to the second city
            .. choose(1),       // the second city's screen: to the hotel
            .. choose(8),       // the hotel: quit the game
            .. choose(0),       // the farewell picture: a key ends the game
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(folder, host);
        var shared = CampaignUnderWay(folder);

        game.RunPart(shared);

        Assert.True(host.Frames > 0);
        // the campaign was handed on from the second city, and the next part is the end of the game
        Assert.Equal(1, shared.City);
        Assert.Equal(LeaveTheGame, shared.NextModule);
        Assert.True(shared.Clock > 8 * 60, "the flight took no time");
    }

    /// <summary>
    /// Alt-Q confirmed in one of the campaign's boxes leaves the game the campaign's way (GAME 00f7:c6ae popup_menu calls
    /// save_and_hand_off): the campaign goes into the hand-off block with the game mode of leaving, and the farewell
    /// picture waits for a key. Found by the session proof (travel-save), where the rewrite ended at once.
    /// </summary>
    [SkippableFact]
    public void AltQConfirmedOnTheCityScreenSavesTheCampaignAndShowsTheFarewellPicture()
    {
        var folder = GameFiles();
        int[] keys =
        [
            HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.AltQ,     // the city screen: Alt-Q
            .. Choose(1),                                                    // "Are you sure ...": Yes
            .. Choose(0),                                                    // the farewell picture: a key
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var taken = 0;
        host.EntryTaken += entry => taken += entry == HeadlessHost.EndOfKeys ? 0 : 1;
        var shared = CampaignUnderWay(folder);

        new Game(folder, host).RunPart(shared);

        Assert.Equal(LeaveTheGame, shared.NextModule);
        // save_and_hand_off writes the part to run next into the game mode too
        Assert.Equal(LeaveTheGame, shared.GameMode);
        // the farewell picture took the last key
        Assert.Equal(keys.Length, taken);
    }

    /// <summary>The keys of a choice: after a moment without a key (a box throws away the keys waiting when it opens), the line, then Enter.</summary>
    private static int[] Choose(int downs) => [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, .. Enumerable.Repeat(HostKeys.Down, downs), HostKeys.Enter];

    /// <summary>The first building on the city screen's menu: after the airport and the hotel.</summary>
    private const int FirstBuildingLine = 2;

    /// <summary>The lines of a building's menu.</summary>
    private const int BreakIntoBuilding = 1, LeaveBuilding = 4;

    [SkippableFact]
    public void ABuildingChosenOnTheCityScreenOpensItsMenuAndLeavingItGoesBackToTheStreet()
    {
        var folder = GameFiles();
        int[] keys =
        [
            .. Choose(FirstBuildingLine),   // the city screen: to the building
            .. Choose(LeaveBuilding),       // the building's menu: leave
            .. Choose(0),                   // the city screen again: to the airport
            .. Choose(2),                   // the flight map: to the second city
            .. Choose(1),                   // the second city's screen: to the hotel
            .. Choose(8),                   // the hotel: quit the game
            .. Choose(0),                   // the farewell picture
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var shared = CampaignUnderWay(folder, buildingsKnown: [2]);

        new Game(folder, host).RunPart(shared);

        // the walk went on from the building to the second city
        Assert.Equal(1, shared.City);
        Assert.Equal(LeaveTheGame, shared.NextModule);
    }

    [SkippableFact]
    public void ABreakInChosenInABuildingHandsTheAgentOverToTheRaid()
    {
        var folder = GameFiles();
        int[] keys =
        [
            .. Choose(FirstBuildingLine),   // the city screen: to the building
            .. Choose(BreakIntoBuilding),   // the building's menu: break into the building
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var shared = CampaignUnderWay(folder, buildingsKnown: [2]);

        new Game(folder, host).RunPart(shared);

        // the campaign part ended handing over to the raid on the building the agent is at
        Assert.Equal(BreakInRaid, shared.NextModule);
        Assert.Equal(2, shared.Org);
    }

    [SkippableFact]
    public void TheCiaBuildingGivesTheChiefsAdviceAndItsMenuAndLeavingItGoesBackToTheStreet()
    {
        var folder = GameFiles();
        const int leaveTheCiaBuilding = 3;
        int[] keys =
        [
            .. Choose(FirstBuildingLine),   // the city screen: to the CIA building
            .. Choose(0),                   // "You arrive at the CIA building ..."
            .. Choose(0),                   // the chief begins
            .. Choose(0),                   // and has nothing to name yet
            .. Choose(leaveTheCiaBuilding), // the floors: leave
            .. Choose(0),                   // the city screen: to the airport
            .. Choose(2),                   // the flight map: to the second city
            .. Choose(1),                   // the second city's screen: to the hotel
            .. Choose(8),                   // the hotel: quit the game
            .. Choose(0),                   // the farewell picture
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var shared = CampaignUnderWay(folder, buildingsKnown: [PlayerAgency]);

        new Game(folder, host).RunPart(shared);

        Assert.Equal(1, shared.City);
        Assert.Equal(LeaveTheGame, shared.NextModule);
    }

    [SkippableFact]
    public void TheHotelSavesTheGameToASlotAndLoadsItBack()
    {
        var folder = GameFiles();
        var saves = Directory.CreateTempSubdirectory("covert-saves-");
        try
        {
            int[] choose(int downs) => [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, .. Enumerable.Repeat(HostKeys.Down, downs), HostKeys.Enter];
            int[] keys =
            [
                .. choose(0),       // the city screen: to the airport
                .. choose(2),       // the flight map: to the second city
                .. choose(1),       // the second city's screen: to the hotel
                .. choose(SaveGame),    // the hotel: Save Game
                .. choose(0),       // the drive: the one the game runs from
                .. choose(0),       // the slots: the first
                .. choose(0),       // "Game has been saved."
                .. choose(0),       // the hotel: leave
                .. choose(0),       // the city screen: to the airport
                .. choose(3),       // the flight map: back to the first city
                .. choose(1),       // the first city's screen: to the hotel
                .. choose(LoadGame),    // the hotel: Load Game
                .. choose(0),       // the drive
                .. choose(0),       // the slots: the first
                .. choose(8),       // the hotel: quit the game
                .. choose(0),       // the farewell picture
            ];
            var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
            var game = new Game(folder, host, saves.FullName);
            var shared = CampaignUnderWay(folder);

            game.RunPart(shared);

            // the game is in the slot of the temporary folder (never in the player's own), and loading it back took the
            // agent from the first city, where the hotel was quit from, to the second, where the game was saved
            Assert.True(new FileInfo(Path.Combine(saves.FullName, "CV0.SVE")).Length > SaveFileCodec.HeaderSize);
            Assert.False(File.Exists(Path.Combine(folder.Path, "CV0.SVE")));
            Assert.Equal(1, shared.City);
            Assert.Equal(LeaveTheGame, shared.NextModule);
        }
        finally
        {
            saves.Delete(recursive: true);
        }
    }
}
