using CovertAction.Core.Raid;

namespace CovertAction.Core.Tests.Raid;

/// <summary>The building generator's rules on hand-made maps, with the values worked out from the original's C (TAC).</summary>
public sealed class BuildingRulesTests
{
    /// <summary>A state with the program's direction and wall tables (TAC DS:06ae, 06b8, 1ff0) and an empty map.</summary>
    private static RaidState NewState()
    {
        var state = new RaidState();
        short[] dx = [0, 1, 0, -1, 0], dy = [-1, 0, 1, 0, -1];
        dx.CopyTo(state.Mve4X, 0);
        dy.CopyTo(state.Mve4Y, 0);
        byte[] tiles = [0, 0, 1, 4, 2, 0, 5, 0, 3, 7, 0, 0, 6, 0, 0, 0];
        tiles.CopyTo(state.WallTileTable, 0);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        return state;
    }

    [Fact]
    public void AWallEdgeSetsTheBitOnBothSidesAndTheWallTilesOfBothCells()
    {
        var state = NewState();
        BuildingRules.SetWallEdge(state, 5, 7, 1);     // east wall of (5, 7)
        Assert.Equal(2, state.WallFlags[RaidState.Cell(5, 7)]);
        Assert.Equal(8, state.WallFlags[RaidState.Cell(6, 7)]);
        Assert.Equal(1, state.CellMap[RaidState.Cell(5, 7)]);    // wall_tile_table[2]
        Assert.Equal(3, state.CellMap[RaidState.Cell(6, 7)]);    // wall_tile_table[8]
    }

    [Fact]
    public void ANorthWallOnTheTopRowLeavesTheNeighbourBitButWritesItsTile()
    {
        var state = NewState();
        BuildingRules.SetWallEdge(state, 3, 0, 0);
        Assert.Equal(1, state.WallFlags[RaidState.Cell(3, 0)]);
        // the cell "north" of row 0 is row 31 of the column before; its tile becomes wall_tile_table[0] = 0
        Assert.Equal(0, state.WallFlags[RaidState.Cell(2, 31)]);
        Assert.Equal(0, state.CellMap[RaidState.Cell(2, 31)]);
    }

    [Fact]
    public void TheOneRoomAmbushHasItsOuterWallTwelveByThirteen()
    {
        var state = NewState();
        state.GameMode = RaidState.OneRoomMode;
        BuildingRules.InitBuildingGrid(state);
        Assert.Equal(1 | 8, state.WallFlags[RaidState.Cell(1, 1)]);
        Assert.Equal(2 | 4, state.WallFlags[RaidState.Cell(10, 11)]);
        Assert.Equal(8, state.WallFlags[RaidState.Cell(11, 5)]);      // the outer side of the east wall
        Assert.Equal(0, state.WallFlags[RaidState.Cell(12, 5)]);
    }

    [Fact]
    public void LabellingGivesOneRoomPerWalledRectangleAndRejectsRoomsWiderThanTen()
    {
        var state = NewState();
        state.GameMode = RaidState.OneRoomMode;
        BuildingRules.InitBuildingGrid(state);
        // the 10 x 11 inside of the ambush rectangle is one room; the floor east of it belongs to room 0
        Assert.False(BuildingRules.LabelRooms(state));   // 11 rows high
        Assert.Equal(1, state.Nroom);
        var room = state.Rm[1];
        Assert.Equal((1, 1, 10, 11), (room.X, room.Y, room.Dx, room.Dy));
        Assert.Equal(1, state.RoomAtCell[RaidState.Cell(10, 11)]);
        Assert.Equal(0, state.RoomAtCell[RaidState.Cell(11, 1)]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(14, 0x4000)]
    [InlineData(15, unchecked((int)0xffff8000))]
    [InlineData(16, 0)]
    [InlineData(23, 0)]
    public void ARoomsBitIsSixteenBitsWide(int room, int bit) =>
        Assert.Equal(bit, BuildingRules.RoomBit(room));

    [Fact]
    public void RemovingARoomClearsItsCellsButKeepsTheDoorTilesAndLeadsItsDoorsOutside()
    {
        var state = NewState();
        state.Rm[3].X = 4;
        state.Rm[3].Y = 4;
        state.Rm[3].Dx = 2;
        state.Rm[3].Dy = 2;
        state.RoomDoorCount[3] = 1;
        state.CellMap[RaidState.Cell(4, 4)] = 0x0a;      // a door tile, side 2
        state.CellMap[RaidState.Cell(5, 5)] = 20;        // furniture
        state.RoomAtCell[RaidState.Cell(5, 5)] = 3;
        state.Ndoor = 1;
        state.Dr[0].Room1 = 2;
        state.Dr[0].Room2 = 3;
        BuildingRules.RemoveRoom(state, 3);
        Assert.Equal(RoomStatus.Removed, state.Rm[3].Status);
        Assert.Equal(0x0a, state.CellMap[RaidState.Cell(4, 4)]);
        Assert.Equal(RaidState.CellEmpty, state.CellMap[RaidState.Cell(5, 5)]);
        Assert.Equal(0, state.RoomAtCell[RaidState.Cell(5, 5)]);
        Assert.Equal(3, state.InRoom);
        Assert.Equal((2, 0), (state.Dr[0].Room1, state.Dr[0].Room2));
    }

    [Fact]
    public void ADoorRecordedFromTheOtherSideGivesTheCellAcrossIt()
    {
        var state = NewState();
        state.Ndoor = 1;
        state.Dr[0].Room1 = 4;
        state.Dr[0].Room2 = 6;
        state.Dr[0].X = 10;
        state.Dr[0].Y = 12;
        state.Dr[0].Dir = 1;
        int x = -1, y = -1;
        Assert.True(BuildingRules.FindDoorCell(state, 6, 4, ref x, ref y));
        Assert.Equal((11, 12), (x, y));
        Assert.True(BuildingRules.FindDoorCell(state, 4, 6, ref x, ref y));
        Assert.Equal((10, 12), (x, y));
        Assert.False(BuildingRules.FindDoorCell(state, 4, 5, ref x, ref y));
        Assert.Equal((10, 12), (x, y));
    }

    [Theory]
    [InlineData(10, -4, 12)]
    [InlineData(-3, 9, 10)]
    [InlineData(-32768, 0, -16384)]     // abs(-32768) stays negative: the "shorter" side is halved
    public void TheDistanceEstimateIsTheLongerSidePlusHalfTheShorter(int a, int b, int expected) =>
        Assert.Equal(expected, BuildingRules.ApproxDistance(a, b));

    /// <summary>
    /// tac_init (TAC 00e8:0018) only sets MacHere after the building is made, so the plot object the
    /// building's person holds (put in the target room by furnish_room, which sets MacHere) keeps it set, and the guards
    /// of the plot object come: two more a level. Found by the session proof (TAC/level3-arrest: the Paymaster with the
    /// Payoff at level 3): the rewrite cleared it, 20 guards were left instead of 26 and the random numbers parted at
    /// the first frame.
    /// </summary>
    [Fact]
    public void ThePlotObjectTheBuildingsPersonHoldsKeepsItsGuards()
    {
        var state = new RaidState { Who = 8, Diff = 3, InCity = 13, GuardsLeft = 20, MacHere = true };
        BuildingRules.SetUpThePlotObjectsGuards(state);
        Assert.True(state.MacHere);
        Assert.Equal(26, state.GuardsLeft);
        Assert.Equal(3, state.Diff);

        var withoutObject = new RaidState { Who = 8, Diff = 3, InCity = 13, GuardsLeft = 20 };
        BuildingRules.SetUpThePlotObjectsGuards(withoutObject);
        Assert.False(withoutObject.MacHere);
        Assert.Equal(20, withoutObject.GuardsLeft);

        // person 0: the plot object counts as present, and the raid is a level harder
        var mastermind = new RaidState { Who = 0, Diff = 3, GuardsLeft = 20 };
        BuildingRules.SetUpThePlotObjectsGuards(mastermind);
        Assert.True(mastermind.MacHere);
        Assert.Equal((4, 28), (mastermind.Diff, mastermind.GuardsLeft));
    }

    /// <summary>
    /// The use key on the hidden information empties its cell and then, the plot object's tile being one that opens,
    /// flips the open bit of bare floor: 0x7f, tile 63, one past the table of 62. The original reads that record on
    /// the movement tables behind the table (TAC DS:067a: MoveY[1..6] the name, MoveY[7] and MoveY[8] the picture,
    /// Move8X[0] the bits, Move8X[1] where). Found by the session proof (TAC/level3-arrest): the rewrite stopped with an
    /// index out of range when it drew the cell again.
    /// </summary>
    [Fact]
    public void TileSixtyThreeIsReadOnTheMovementTablesBehindTheTable()
    {
        var state = new RaidState();
        short[] moveY = [0, -3, -2, 0, 2, 3, 2, 0, -2], move8X = [0, 0, 1, 1, 1, 0, -1, -1, -1];
        moveY.CopyTo(state.MoveY, 0);
        move8X.CopyTo(state.Move8X, 0);
        state.Ob[5].SpriteX = 0x50;

        var behind = state.TileType(0x7f & RaidState.CellTile);
        Assert.Equal((0, -2, TileFlags.None, (short)0), (behind.SpriteX, behind.SpriteY, behind.Bits, behind.Where));
        // the name: the bytes of MoveY[1] and MoveY[2] (fd ff fe ff), then MoveY[3]'s zero
        Assert.Equal("ýÿþÿ", behind.PrintedName());
        Assert.Same(state.Ob[5], state.TileType(5));
    }
}
