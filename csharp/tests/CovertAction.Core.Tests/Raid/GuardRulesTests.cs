using CovertAction.Core.Raid;

namespace CovertAction.Core.Tests.Raid;

/// <summary>The guards' rules on hand-made buildings, with the values worked out from the original's C (TAC).</summary>
public sealed class GuardRulesTests
{
    private const byte Desk = 34;
    private const byte Files = 24;

    /// <summary>A state with the program's direction tables (TAC DS:068a, 069c, 06ae, 06b8), an empty map and two tile kinds.</summary>
    private static RaidState NewState()
    {
        var state = new RaidState();
        short[] dx = [0, 1, 0, -1, 0], dy = [-1, 0, 1, 0, -1];
        dx.CopyTo(state.Mve4X, 0);
        dy.CopyTo(state.Mve4Y, 0);
        short[] x8 = [0, 0, 1, 1, 1, 0, -1, -1, -1], y8 = [0, -1, -1, 0, 1, 1, 1, 0, -1];
        x8.CopyTo(state.Move8X, 0);
        y8.CopyTo(state.Move8Y, 0);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        state.Ob[Desk].Bits = TileFlags.Solid;
        state.Ob[Files].Bits = TileFlags.Solid | TileFlags.Tall;
        return state;
    }

    [Theory]
    [InlineData(10, 0, 3)]
    [InlineData(10, 4, 3)]
    [InlineData(10, 5, 4)]
    [InlineData(-3, -10, 1)]
    [InlineData(0, 0, 8)]
    [InlineData(-10, 6, 6)]
    public void AVectorIsStraightWhenItsOtherPartIsLessThanHalf(int dx, int dy, int facing) =>
        Assert.Equal(facing, GuardRules.DirectionFromVector(dx, dy));

    [Theory]
    [InlineData(10, 9, 3)]
    [InlineData(9, 10, 4)]
    [InlineData(5, -5, 1)]
    [InlineData(0, 0, 8)]
    public void TheCoarseFacingTakesTheQuadrantAndTheNearerAxis(int dx, int dy, int facing) =>
        Assert.Equal(facing, GuardRules.DirectionFromVectorCoarse(dx, dy));

    [Fact]
    public void ASightLineIsClearCoveredOrBlockedByWhatItCrosses()
    {
        var state = NewState();
        // from the centre of cell (2, 5) to the centre of cell (8, 5), along row 5
        Assert.Equal(GuardRules.SightClear, GuardRules.LineBlockedCount(state, 40, 88, 136, 88));
        Assert.Equal(3, state.CoverBits);

        state.CellMap[RaidState.Cell(3, 5)] = Desk;         // near the start: the near half is covered
        Assert.Equal(GuardRules.SightCovered, GuardRules.LineBlockedCount(state, 40, 88, 136, 88));
        Assert.Equal(2, state.CoverBits);

        state.CellMap[RaidState.Cell(6, 5)] = Files;        // a tall piece blocks
        Assert.Equal(GuardRules.SightBlocked, GuardRules.LineBlockedCount(state, 40, 88, 136, 88));
        // and the same the other way round, through the mirrored walk
        Assert.Equal(GuardRules.SightBlocked, GuardRules.LineBlockedCount(state, 136, 88, 40, 88));
    }

    [Fact]
    public void TheAlarmDistancesCountDoorsFromTheAlarmRoom()
    {
        var state = NewState();
        state.Nroom = 4;
        state.AlertCachedRoom = -1;
        state.Diff = 2;
        (sbyte, sbyte)[] doors = [(1, 2), (2, 3), (0, 1)];
        for (var i = 0; i < doors.Length; i++)
        {
            (state.Dr[i].Room1, state.Dr[i].Room2) = doors[i];
        }
        state.Ndoor = (short)doors.Length;
        state.Disguised = true;
        GuardRules.RaiseAlert(state, 3, 0xf0);
        Assert.False(state.Disguised);
        Assert.Equal(0xf0 / 4 * 6, state.Eseen);
        Assert.Equal(3, state.ToRoom);
        Assert.Equal([999, 2, 1, 0, -1], state.Rm.Take(5).Select(r => (int)r.ToDist));
    }

    [Fact]
    public void TheAlarmOfRoomMinusOneOutsideAKillRaidOnlyMovesTheAlarmRoom()
    {
        var state = NewState();
        state.AlertCachedRoom = -1;
        state.Nroom = 2;
        state.Rm[1].ToDist = 5;
        state.Eseen = 100;
        GuardRules.RaiseAlert(state, -1, 0);
        Assert.Equal(-1, state.ToRoom);
        Assert.Equal(100, state.Eseen);
        Assert.Equal(5, state.Rm[1].ToDist);
    }

    [Fact]
    public void ThePathFillCountsStepsFromTheTargetAndGoesRoundFurniture()
    {
        var state = NewState();
        for (var x = 2; x <= 5; x++)
        {
            for (var y = 2; y <= 4; y++)
            {
                state.RoomAtCell[RaidState.Cell(x, y)] = 7;
            }
        }
        state.CellMap[RaidState.Cell(3, 3)] = Desk;
        Assert.True(GuardRules.BuildPathMap(state, 1, 7, 5, 3, 2, 3));
        var distance = state.PathDist[1];
        Assert.Equal(1, distance[RaidState.Cell(2, 3)]);
        Assert.Equal(0, distance[RaidState.Cell(3, 3)]);
        Assert.Equal(3, distance[RaidState.Cell(3, 2)]);    // no diagonal step past the desk's corner
        Assert.Equal(5, distance[RaidState.Cell(4, 3)]);    // round the desk
        Assert.Equal(5, distance[RaidState.Cell(5, 3)]);    // diagonally from (4, 2), between two free cells
    }

    [Theory]
    [InlineData(2, 0, false, 0)]       // alert 0: guards 1 and 2 patrol
    [InlineData(3, 0, false, 1)]       // the next two go to the rooms nobody went to
    [InlineData(5, 0, true, 2)]        // the rest hunt at night
    [InlineData(5, 0, false, 0)]
    [InlineData(4, 50, false, 0)]      // alert 50: two more patrol
    public void TheGuardsBehaveByTheirNumberAndTheAlertness(int guard, int alert, bool night, int behaviour)
    {
        var state = NewState();
        state.Diff = 4;
        state.SharedAlert = (short)alert;
        state.Night = night;
        Assert.Equal(behaviour, GuardRules.GuardBehaviour(state, guard));
    }

    [Fact]
    public void AGuardBeyondTheLevelsNumberWithdrawsAndCountsAsAGuardLeft()
    {
        var state = NewState();
        state.Diff = 0;
        state.GuardsLeft = 2;
        Assert.Equal(GuardRules.Withdraw, GuardRules.GuardBehaviour(state, 4));
        Assert.Equal(3, state.GuardsLeft);
    }
}
