using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Tests.Chase;

/// <summary>
/// The rules of the car chase on hand-made states; the expected values are worked out from the reconstructed C of CHASE
/// (can_move, sight_distance, init_cars, check_location_entry, format_game_time, set_leave_game), not from the C#.
/// </summary>
public sealed class ChaseRulesTests
{
    /// <summary>A chase with the original's step tables, every map cell closed on all sides, and the clock template "0:00:00".</summary>
    private static ChaseState Chase()
    {
        var state = new ChaseState();
        short[] dx = [0, 1, 0, -1], dy = [-1, 0, 1, 0];
        dx.CopyTo(state.DirDx, 0);
        dy.CopyTo(state.DirDy, 0);
        "0:00:00".CopyTo(0, state.ClockText, 0, 7);
        return state;
    }

    private static void SetCell(ChaseState state, int x, int y, int value) => state.MapCells[ChaseState.CellIndex(x, y)] = (byte)value;

    private static void Place(Car car, int x, int y, int dir)
    {
        car.X = (short)x;
        car.Y = (short)y;
        car.Dir = (short)dir;
    }

    [Fact]
    public void SetLeaveGameEndsTheGameOnlyWhenAsked()
    {
        var shared = new SharedState { NextModule = 0 };
        ChaseRules.SetLeaveGame(shared, false);
        Assert.Equal(0, shared.NextModule);
        ChaseRules.SetLeaveGame(shared, true);
        Assert.Equal(-1, shared.NextModule);
    }

    [Fact]
    public void ImportSharedStateTakesTheScalarsAndCopiesTheTablesAsLongAsTheBlockSays()
    {
        var state = Chase();
        var shared = new SharedState { City = 3, Org = 7, Level = 2, Clock = 900, SkillBits = 0x0c00, NCast = 2, NEvents = 1 };
        var campaign = shared.Campaign;
        campaign.Cities[3].OrgClues = 0x00a0;
        campaign.Orgs[5].Sname = "KGB";
        for (var i = 0; i < 2; i++)
        {
            campaign.Cast.Add(new());
            campaign.CastProgress.Add(new());
        }
        campaign.Events.Add(new());
        state.Ev[1].Chron = 99;   // beyond the block's events: stays

        ChaseRules.ImportSharedState(state, shared);

        Assert.Equal((3, 7, 2, 900, 0x0c00), ((int)state.InCity, (int)state.AtLoc, (int)state.Diff, (int)state.Gtime, (int)state.Skills));
        Assert.Equal((2, 1), ((int)state.NACTIVITY, (int)state.NEVENT));
        Assert.Equal(0x00a0, state.Ct[3].OrgClues);
        Assert.Equal("KGB", state.Org[5].Sname);
        Assert.Equal(99, state.Ev[1].Chron);
        // copies, not the hand-off's own records
        Assert.NotSame(campaign.Orgs[5], state.Org[5]);
    }

    [Fact]
    public void OnTheOuterRingACarMayOnlyDriveInwards()
    {
        var state = Chase();
        Array.Fill(state.MapCells, (byte)0xff);
        Assert.True(ChaseRules.CanMove(state, 0, 5, Directions.Right));
        Assert.False(ChaseRules.CanMove(state, 0, 5, Directions.Down));
        Assert.True(ChaseRules.CanMove(state, 5, 0, Directions.Down));
        Assert.False(ChaseRules.CanMove(state, 5, 0, Directions.Left));
        Assert.True(ChaseRules.CanMove(state, 23, 5, Directions.Left));
        Assert.False(ChaseRules.CanMove(state, 23, 5, Directions.Up));
        Assert.True(ChaseRules.CanMove(state, 5, 23, Directions.Up));
        Assert.False(ChaseRules.CanMove(state, 5, 23, Directions.Right));
        // a corner allows nothing: two of the tests contradict each other
        Assert.False(ChaseRules.CanMove(state, 0, 0, Directions.Right));
        Assert.False(ChaseRules.CanMove(state, 0, 0, Directions.Down));
    }

    [Fact]
    public void InsideTheRingOnlyBitZeroOfTheLeftCellAndBitThreeOfTheUpperCellCount()
    {
        // CA-3: bits 1 and 2, which the generator clears for the right and bottom sides, are never tested
        var state = Chase();
        SetCell(state, 5, 5, 0x06);
        Assert.False(ChaseRules.CanMove(state, 5, 5, Directions.Right));
        Assert.False(ChaseRules.CanMove(state, 5, 5, Directions.Down));

        SetCell(state, 5, 5, 0x01);
        Assert.True(ChaseRules.CanMove(state, 5, 5, Directions.Right));
        Assert.False(ChaseRules.CanMove(state, 5, 5, Directions.Down));

        SetCell(state, 5, 5, 0x08);
        Assert.True(ChaseRules.CanMove(state, 5, 5, Directions.Down));

        // up and left read the neighbour above and the neighbour to the left
        SetCell(state, 5, 4, 0x08);
        Assert.True(ChaseRules.CanMove(state, 5, 5, Directions.Up));
        SetCell(state, 4, 5, 0x01);
        Assert.True(ChaseRules.CanMove(state, 5, 5, Directions.Left));
        SetCell(state, 4, 5, 0x08);
        Assert.False(ChaseRules.CanMove(state, 5, 5, Directions.Left));
    }

    [Fact]
    public void TheQuarrySeesACarInItsColumnThroughOpenTiles()
    {
        var state = Chase();
        Array.Fill(state.MapCells, (byte)0x08);   // every tile open downwards
        Place(state.Cars[0], 160, 200, Directions.Down);
        Place(state.Cars[1], 160, 136, Directions.Down);
        // 64 units apart: two whole tiles, so three tiles are walked (4, 5, 6), all open downwards
        Assert.Equal(64, ChaseRules.SightDistance(state, 1));
        Assert.True(state.SightFlag);

        SetCell(state, 5, 6, 0x00);
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
        Assert.True(state.SightFlag);   // set even when the view is blocked
    }

    [Fact]
    public void ACarHeadingUpLooksFromTheFarSideOfItsTile()
    {
        var state = Chase();
        // the quarry is above: the walk goes up from the tile of y + 31
        Place(state.Cars[0], 160, 100, Directions.Up);
        Place(state.Cars[1], 160, 164, Directions.Up);
        // tiles walked: (164 + 31) / 32 = 6, then 5, 4 (64 / 32 = 2: three tiles); up tests bit 3 of the tile above
        SetCell(state, 5, 5, 0x08);
        SetCell(state, 5, 4, 0x08);
        SetCell(state, 5, 3, 0x08);
        Assert.Equal(64, ChaseRules.SightDistance(state, 1));
        SetCell(state, 5, 3, 0x00);
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
    }

    [Fact]
    public void TheQuarrySeesNothingOutOfLineTooFarOrAtNightBeyondTwoTiles()
    {
        var state = Chase();
        Array.Fill(state.MapCells, (byte)0xff);
        Place(state.Cars[0], 160, 300, Directions.Down);
        // not on a tile's centre line
        Place(state.Cars[1], 161, 280, Directions.Down);
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
        // six tiles or more
        Place(state.Cars[1], 160, 300 - 0xc0, Directions.Down);
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
        Place(state.Cars[1], 160, 300 - 0xbf, Directions.Down);
        Assert.Equal(0xbf, ChaseRules.SightDistance(state, 1));
        // at night more than 64 units in either direction
        state.Night = true;
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
        Place(state.Cars[1], 160, 300 - 0x40, Directions.Down);
        Assert.Equal(0x40, ChaseRules.SightDistance(state, 1));
    }

    [Fact]
    public void TwoCarsOnTheSameSpotSeeEachOtherAtDistanceZeroWithTheSightFlagClear()
    {
        var state = Chase();
        Array.Fill(state.MapCells, (byte)0xff);
        state.SightFlag = true;
        Place(state.Cars[0], 160, 160, Directions.Down);
        Place(state.Cars[1], 160, 160, Directions.Down);
        Assert.Equal(0, ChaseRules.SightDistance(state, 1));
        Assert.False(state.SightFlag);
    }

    [Fact]
    public void InitCarsPutsTheQuarryOnItsStartAndTheTailCarsBehindIt()
    {
        var state = Chase();
        state.Diff = 0;
        state.AlertLevel = 0;
        state.StartLoc = 3;
        state.DestLoc = 7;
        state.LocX[3] = 192;
        state.LocY[3] = 336;
        state.LocX[7] = 480;
        state.LocY[7] = 208;
        state.CurCar = 1;
        state.Cars[0].Flag = true;

        ChaseRules.InitCars(state);

        // 1000 / 3 = 333; 3330 / 100 = 33; * 10
        Assert.Equal(330, state.AlertLimit);
        var quarry = state.Cars[0];
        Assert.Equal((192, 336, Directions.Up), (quarry.X, quarry.Y, (int)quarry.Dir));
        Assert.Equal((192, 336, -1, 2), (quarry.MarkX, quarry.MarkY, (int)quarry.Turn, (int)quarry.Gear));
        Assert.True(quarry.Flag);   // left as it was
        // heading up, so "behind" is further down
        Assert.Equal((192, 344), (state.Cars[1].X, state.Cars[1].Y));
        Assert.Equal((192, 352), (state.Cars[2].X, state.Cars[2].Y));
        Assert.All(state.Cars[1..], car => Assert.Equal((true, -1, -1, 0, 0), (car.Flag, (int)car.MarkX, (int)car.Turn, (int)car.Alert, (int)car.Gear)));
        Assert.Equal((480, 208), (state.DestX, state.DestY));
        Assert.Equal(1, state.CurCar);

        state.ChaseMode = ChaseMode.Flee;
        state.Diff = 3;
        state.AlertLevel = 50;
        ChaseRules.InitCars(state);
        // 1000 / 6 = 166; 1660 / 150 = 11; * 10
        Assert.Equal(110, state.AlertLimit);
        Assert.Equal(0, state.CurCar);
    }

    [Fact]
    public void ALocationIsReachedWithinSixUnits()
    {
        var state = Chase();
        var shared = new SharedState();
        state.CampaignMode = true;
        state.InCity = 2;
        state.Ct[2].OrgClues = 1 << 1 | 1 << 4;
        state.LocX[1] = 100;
        state.LocY[1] = 100;
        state.LocX[4] = 300;
        state.LocY[4] = 400;

        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 300, 393));
        Assert.True(ChaseRules.CheckLocationEntry(state, shared, 303, 397));
        Assert.Equal((4, 4), ((int)state.AtLoc, (int)shared.OrgOut));
    }

    [Fact]
    public void AnUnknownLocationIsTestedWithThePreviousDistance()
    {
        // CA-4: location 2 is unknown, but location 1's distance (3) is tested again in its place: 2 is "reached"
        var state = Chase();
        var shared = new SharedState();
        state.InCity = 0;
        state.Ct[0].OrgClues = 1 << 1;
        state.LocX[1] = 100;
        state.LocY[1] = 100;
        state.CampaignMode = true;
        // location 1 is at distance 7: not reached; location 2 reuses 7: not reached either
        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 104, 103));
        // at distance 3 location 1 itself is reached first
        Assert.True(ChaseRules.CheckLocationEntry(state, shared, 101, 102));
        Assert.Equal(1, state.AtLoc);
    }

    [Fact]
    public void WithoutTheCampaignFlagNothingIsWritten()
    {
        var state = Chase();
        var shared = new SharedState { OrgOut = 9 };
        state.Ct[0].OrgClues = 1 << 1;
        state.AtLoc = 5;
        Assert.True(ChaseRules.CheckLocationEntry(state, shared, 0, 0));
        Assert.Equal((5, 9), ((int)state.AtLoc, (int)shared.OrgOut));
    }

    [Fact]
    public void WithLocationOneUnknownTheOriginalReadsAnUninitialisedDistance()
    {
        var state = Chase();
        state.Ct[0].OrgClues = 1 << 2;
        Assert.Throws<UninitialisedReadException>(() => ChaseRules.CheckLocationEntry(state, new SharedState(), 0, 0));
    }

    [Fact]
    public void Ca4WithTheFixAnUnknownLocationIsNotReached()
    {
        // location 1 is unknown (the original reads a distance it never set; the rewrite stops with an error): fixed, it is skipped
        var state = Chase();
        var shared = new SharedState();
        state.InCity = 0;
        state.Ct[0].OrgClues = 1 << 2;
        state.LocX[2] = 100;
        state.LocY[2] = 100;
        state.CampaignMode = true;
        var fixedBugs = OriginalBehaviour.ForPlayer(true, false);
        Assert.Throws<UninitialisedReadException>(() => ChaseRules.CheckLocationEntry(state, shared, 100, 100, OriginalBehaviour.ForPlayer(false, false)));
        // the known location 2 is still reached from near, and not from far
        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 120, 100, fixedBugs));
        Assert.True(ChaseRules.CheckLocationEntry(state, shared, 103, 102, fixedBugs));
        Assert.Equal(2, state.AtLoc);
        // no known location at all: nothing to enter
        state.Ct[0].OrgClues = 0;
        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 100, 100, fixedBugs));
    }

    [Fact]
    public void Ca4WithTheFixAKnownLocationIsReachedAsBefore()
    {
        var state = Chase();
        var shared = new SharedState();
        state.Ct[0].OrgClues = 1 << 1 | 1 << 4;
        state.LocX[1] = 100;
        state.LocY[1] = 100;
        state.LocX[4] = 300;
        state.LocY[4] = 400;
        state.CampaignMode = true;
        foreach (var behaviour in new[] { OriginalBehaviour.ForPlayer(false, false), OriginalBehaviour.ForPlayer(true, false) })
        {
            Assert.False(ChaseRules.CheckLocationEntry(state, shared, 300, 393, behaviour));
            Assert.True(ChaseRules.CheckLocationEntry(state, shared, 303, 397, behaviour));
            Assert.Equal(4, state.AtLoc);
        }
    }

    [Fact]
    public void TheClockShowsTheLastDigitOfTheHourMinutesAndFramesAsSeconds()
    {
        var state = Chase();
        state.Gtime = 754;   // 12:34
        // 125 frames: 2 minutes and 5 "seconds"
        Assert.Equal("2:36:05", ChaseRules.FormatGameTime(state, 125));
        // round the day: 1439 + 1 minute is 0:00
        state.Gtime = 1439;
        Assert.Equal("0:00:59", ChaseRules.FormatGameTime(state, 119));
    }

    [Fact]
    public void D661InTheStandaloneChaseEveryPlaceCanBeEnteredAndNothingIsHandedBack()
    {
        // CC-2: no place is "known" (the city's record is empty), and the original, were it run so, reads an unset distance;
        // the player's option makes every place known, as the map shows them all
        var state = Chase();
        var shared = new SharedState { OrgOut = 9 };
        state.CampaignMode = false;
        state.AtLoc = 5;
        state.LocX[3] = 200;
        state.LocY[3] = 200;
        var option = OriginalBehaviour.ForPlayer(false, false, new CutContentOptions(StandaloneChase: true));
        Assert.Throws<UninitialisedReadException>(() => ChaseRules.CheckLocationEntry(state, shared, 203, 202, OriginalBehaviour.ForPlayer(false, false)));
        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 220, 200, option));
        Assert.True(ChaseRules.CheckLocationEntry(state, shared, 203, 202, option));
        Assert.Equal((5, 9), ((int)state.AtLoc, (int)shared.OrgOut));

        // a campaign chase is unchanged by the option: only the known places
        state.CampaignMode = true;
        state.Ct[0].OrgClues = 1 << 1;
        state.LocX[1] = 0;
        state.LocY[1] = 0;
        Assert.False(ChaseRules.CheckLocationEntry(state, shared, 203, 202, option));
    }
}

