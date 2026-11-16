using CovertAction.Core.Campaign;
using CovertAction.Core.Raid;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// The campaign-logic fixes of the option "Also fix campaign-logic bugs" (docs/original-bugs.md CA-28, CA-32, CA-35, CA-36)
/// that need no game file: each bug is shown with the original's behaviour (the option off) and the fix with it on.
/// </summary>
public sealed class CampaignBugFixesCoreTests
{
    /// <summary>What the player has with the campaign option off (release 1.0), and with it on.</summary>
    private static readonly OriginalBehaviour Off = OriginalBehaviour.ForPlayer(false, false), On = OriginalBehaviour.ForPlayer(false, true);

    // ---- CA-32 ----

    private static CampaignState Log(OriginalBehaviour behaviour, params (int Who, int Who2, int Time)[] messages)
    {
        var state = new CampaignState { Behaviour = behaviour, NEVENT = 2, NextSyn = (short)messages.Length };
        state.MessageText.ReceivedFrom = "Got";
        state.MessageText.SentTo = "Gave";
        state.MessageText.BeforeName = " ";
        state.MessageText.Someone = " anon";
        state.MessageText.OfThe = " of ";
        state.MessageText.In = " at ";
        state.MessageText.On = " on ";
        state.MessageText.BeforeDay = "/";
        state.MessageText.End = ".";
        state.Ev[1].Name = "Sent";
        state.Ev[1].Status = 0x0202;                // a message to a cast member
        for (var i = 0; i < messages.Length; i++)
        {
            var message = state.Sn[i];
            message.Data = 1;
            message.Time = (short)messages[i].Time;
            message.Who = (sbyte)messages[i].Who;
            message.Who2 = (sbyte)messages[i].Who2;
            message.Loc2 = 5;
        }
        state.Ct[15].Police = 7;
        return state;
    }

    [Fact]
    public void Ca32AMessageWithoutAPairWritesIntoCityFifteensCounter()
    {
        var state = Log(Off, (10, 20, 3000));
        state.Sn[0].Clues = 0x02;
        state.Ps[20].Clues = 0x02;
        state.Ps[10].Clues = 0x06;
        state.Ct[15].Police = 3000;                 // the minute of "message -1", which the lone message finds equal to its own
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal(0x06, state.Ct[15].Clued);
    }

    [Fact]
    public void Ca32WithTheFixALoneMessageHasNoPairAndCityFifteenIsLeftAlone()
    {
        var state = Log(On, (10, 20, 3000));
        state.Sn[0].Clues = 0x02;
        state.Ps[20].Clues = 0x02;
        state.Ps[10].Clues = 0x06;
        state.Ct[15].Police = 3000;
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal(0, state.Ct[15].Clued);
        Assert.Equal(0x06, state.Ps[10].Clues);
    }

    private static CampaignState TwoPairsInOneMinute(OriginalBehaviour behaviour)
    {
        // messages 0 and 1 are the pair of persons 10 and 20, 2 and 3 the pair of 30 and 40, all sent at minute 3000
        var state = Log(behaviour, (10, 20, 3000), (20, 10, 3000), (30, 40, 3000), (40, 30, 3000));
        state.Sn[2].Clues = 0x02;                   // the player knows the receiver's name
        state.Ps[40].Clues = 0x02;
        state.Ps[30].Clues = 0x06;                  // and what the sender is
        return state;
    }

    [Fact]
    public void Ca32TheFirstMessageOfASecondPairIsPairedWithTheFirstPairsSecond()
    {
        var state = TwoPairsInOneMinute(Off);
        CampaignTexts.AppendMessageText(state, 2);
        Assert.Equal((0x06, 0x00), (state.Sn[1].Clues, state.Sn[3].Clues));   // what the sender is lands on the other pair's message
    }

    [Fact]
    public void Ca32WithTheFixThePairIsTheMessageThatNamesTheSamePeopleTheOtherWayRound()
    {
        var state = TwoPairsInOneMinute(On);
        CampaignTexts.AppendMessageText(state, 2);
        Assert.Equal((0x00, 0x06), (state.Sn[1].Clues, state.Sn[3].Clues));
    }

    // ---- CA-35 ----

    private static CampaignState Traveller(OriginalBehaviour behaviour, bool destinationsOccupied)
    {
        var state = new CampaignState { Behaviour = behaviour, NACTIVITY = 1, InCity = 2, TextBeforePersons = "One moment please...\n", Nperson = 20 };
        state.Pl.BigOrg = -1;
        state.Ac[0].Status = 0x08;                  // may be seen leaving
        state.Ac[0].Who = 5;
        state.Ps[5].Clues = 0x08;                   // the player has spotted this person in a city
        state.Ps[5].Alert = 99;
        state.Ps[5].Org = 3;
        state.Ps[5].Loc = 0;
        state.Ps[5].Face = 1;
        state.Orgs[3].Size = 50;
        state.Orgs[3].Covert = 1;                   // present everywhere
        for (var city = 0; city < 12 && destinationsOccupied; city++)
        {
            // a cast member of the plot already sits in the organisation's place of each of the first twelve cities
            var person = state.Ps[6 + city];
            person.Org = 3;
            person.Loc = (short)city;
            person.Role = 1;
            person.Face = 1;
        }
        state.Random.Srand(3);
        return state;
    }

    [Fact]
    public void Ca35NoCastMemberIsSeenLeaving()
    {
        var state = Traveller(Off, destinationsOccupied: false);
        Assert.Equal(-1, PlotRules.TryDepart(state, out var from));
        Assert.Equal((-1, 0), (from, state.Ps[5].Loc));
    }

    [Fact]
    public void Ca35WithTheFixAMemberIsSeenLeavingForACityWhereHisOrganisationHasNoOneTheAgentKnows()
    {
        var state = Traveller(On, destinationsOccupied: false);
        Assert.Equal(5, PlotRules.TryDepart(state, out var from));
        Assert.Equal(0, from);
        Assert.NotEqual(0, state.Ps[5].Loc);
        Assert.Equal(99 / 2, state.Ps[5].Alert);
        Assert.Equal(0, state.Ps[5].Clues & 0x08);  // the player has lost the member's city
    }

    [Fact]
    public void Ca35WithTheFixACityWhereAnotherCastMemberSitsIsNotAGoal()
    {
        // the persons 6 .. 17 are cast members at the first twelve cities of organisation 3; the four tries of one roll
        // may still find a free city, so every seed is tried: the member never lands where a cast member is
        for (var seed = 1; seed < 60; seed++)
        {
            var state = Traveller(On, destinationsOccupied: true);
            state.Random.Srand(seed);
            if (PlotRules.TryDepart(state, out _) == 5)
            {
                Assert.True(state.Ps[5].Loc >= 12, $"seed {seed}: the member went to city {state.Ps[5].Loc}");
            }
        }
    }

    // ---- CA-36 ----

    private static (int DuplicateFaces, int Stale) Faces(OriginalBehaviour behaviour, int seed)
    {
        var state = new CampaignState { Behaviour = behaviour, Nperson = CampaignState.PersonCount };
        state.Pl.BigOrg = -1;
        foreach (var slot in state.Ps)
        {
            slot.Alert = 50;
            slot.Intel = 9;
        }
        state.Random.Srand(seed);
        var persons = new List<int>();
        for (var org = 1; org <= 4; org++)
        {
            for (var city = 0; city < 10; city++)
            {
                persons.Add(PlotRules.FindOrCreatePerson(state, org, city, 1, out _));
            }
        }
        var low = persons.Select(p => state.Ps[p].Face & 0x3f).ToList();
        var stale = persons.Count(p => state.Ps[p].Alert != 0 || state.Ps[p].Intel != 0);
        return (low.Count - low.Distinct().Count(), stale);
    }

    [Fact]
    public void Ca36PersonsGetTheSameFaceAndKeepTheAlertOfTheRecordsLastOccupant()
    {
        // forty persons of 64 faces: without a working test some share a face (the chance of none is far below 1 in a million)
        var (duplicates, stale) = Faces(Off, 1);
        Assert.True(duplicates > 0, "no two persons share a face");
        Assert.Equal(40, stale);
    }

    [Fact]
    public void Ca36WithTheFixEveryFaceIsDifferentAndANewPersonIsNotAlertedOrInvestigated()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            Assert.Equal((0, 0), Faces(On, seed));
        }
    }

    /// <summary>The library's copy (workbench, debriefing, raid): forty persons made on records that hold a last occupant's alert.</summary>
    private static (int DuplicateFaces, int Stale) LibraryFaces(OriginalBehaviour behaviour, int seed)
    {
        var state = new CampaignState();
        state.Pl.BigOrg = -1;
        foreach (var slot in state.Ps)
        {
            slot.Alert = 50;
            slot.Intel = 9;
        }
        state.Random.Srand(seed);
        var persons = new List<int>();
        for (var org = 1; org <= 4; org++)
        {
            for (var city = 0; city < 10; city++)
            {
                persons.Add(Shared.CampaignLibrary.FindOrCreatePerson(state.Ps, CampaignState.PersonCount, state.Pl, state.Random, org, city, 1, false, (_, _) => { }, behaviour));
            }
        }
        var low = persons.Select(p => state.Ps[p].Face & 0x3f).ToList();
        return (low.Count - low.Distinct().Count(), persons.Count(p => state.Ps[p].Alert != 0 || state.Ps[p].Intel != 0));
    }

    [Fact]
    public void Ca36TheLibrarysCopyAlsoSharesFacesAndKeepsTheLastOccupantsAlert()
    {
        var (duplicates, stale) = LibraryFaces(Off, 1);
        Assert.True(duplicates > 0);
        Assert.Equal(40, stale);
    }

    [Fact]
    public void Ca36WithTheFixTheLibrarysCopyMakesDistinctUnalertedPersons()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            Assert.Equal((0, 0), LibraryFaces(On, seed));
        }
    }

    // ---- CA-28 ----

    /// <summary>A building as the raid generates it (without the pruning): plans are drawn until one has its rooms joined.</summary>
    private static RaidState Building(OriginalBehaviour behaviour, int seed)
    {
        var state = new RaidState();
        short[] dx = [0, 1, 0, -1, 0], dy = [-1, 0, 1, 0, -1];
        dx.CopyTo(state.Mve4X, 0);
        dy.CopyTo(state.Mve4Y, 0);
        byte[] tiles = [0, 0, 1, 4, 2, 0, 5, 0, 3, 7, 0, 0, 6, 0, 0, 0];
        tiles.CopyTo(state.WallTileTable, 0);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        state.Random.Srand(seed);
        do
        {
            state.Nroom = 0;
            BuildingRules.InitBuildingGrid(state);
            BuildingRules.AddWallSegments(state);
        }
        while (!BuildingRules.LabelRooms(state) || !BuildingRules.ConnectRooms(state, behaviour));
        return state;
    }

    private static int DoubledDoors(RaidState state) =>
        Enumerable.Range(0, state.Ndoor).Select(d => (Math.Min(state.Dr[d].Room1, state.Dr[d].Room2), Math.Max(state.Dr[d].Room1, state.Dr[d].Room2)))
            .GroupBy(pair => pair).Count(group => group.Count() > 1);

    [Fact]
    public void Ca28RoomsAboveFifteenGetSeveralDoorsToTheSameRoom()
    {
        var doubled = 0;
        var bigBuildings = 0;
        for (var seed = 1; seed <= 300; seed++)
        {
            var state = Building(Off, seed);
            if (state.Nroom >= 17)
            {
                bigBuildings++;
                doubled += DoubledDoors(state);
            }
        }
        Assert.True(bigBuildings > 0, "no building with 17 rooms or more in 300 seeds");
        Assert.True(doubled > 0, $"{bigBuildings} big buildings and no room pair with two doors");
    }

    [Fact]
    public void Ca28WithTheFixNoTwoRoomsHaveTwoDoorsBetweenThem()
    {
        var bigBuildings = 0;
        for (var seed = 1; seed <= 300; seed++)
        {
            var state = Building(On, seed);
            bigBuildings += state.Nroom >= 17 ? 1 : 0;
            Assert.Equal(0, DoubledDoors(state));
        }
        Assert.True(bigBuildings > 0);
    }
}
