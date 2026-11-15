using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Raid;

/// <summary>
/// The generation of the building a raid takes place in: the outer wall, the inner walls, the rooms they enclose, the
/// doors between them, the rooms dropped again, and the furniture; and the small measures of the map the rest of the
/// raid uses.
/// </summary>
/// <remarks>
/// The generator works on the wall bits of the cells (<see cref="RaidState.WallFlags"/>): a wall between two cells is
/// a bit in both, the side's bit in one and the opposite side's bit in the other. Each wall bit change also writes the
/// wall tile of both cells into <see cref="RaidState.CellMap"/>.
/// </remarks>
public static class BuildingRules
{
    private const int North = 0;
    private const int East = 1;
    private const int South = 2;
    private const int West = 3;

    /// <summary>The wall bit of side <paramref name="side"/> 0..3.</summary>
    private static int WallBit(int side) => 1 << side;

    /// <summary>The wall bits of the two sides along a north-south line (north and south).</summary>
    private const int NorthSouthWalls = 1 | 4;

    /// <summary>The wall bits of the two sides along an east-west line (east and west).</summary>
    private const int EastWestWalls = 2 | 8;

    /// <summary>The one-room ambush is a rectangle of this many cells across ...</summary>
    private const int OneRoomWidth = 12;

    /// <summary>... and this many down.</summary>
    private const int OneRoomHeight = 13;

    /// <summary>Inner wall segments the generator tries to draw.</summary>
    private const int WallSegments = 20;

    /// <summary>Tries to find a cell a wall segment may start from, and tries to find a room's next door.</summary>
    private const int Tries = 999;

    /// <summary>A room more than this many cells wide or high makes the generator start again.</summary>
    private const int MaxRoomSide = 10;

    /// <summary>A room of more than this many cells is middle-sized for the choice of its type ...</summary>
    private const int MiddleRoomCells = 40;

    /// <summary>... and of more than this many cells big.</summary>
    private const int BigRoomCells = 72;

    /// <summary>A room gets one piece of furniture for every this many cells.</summary>
    private const int CellsPerPiece = 6;

    /// <summary>Tries to place one piece of furniture.</summary>
    private const int PieceTries = 99;

    /// <summary>How much a door on its outer wall is worth less than a room's size, in the choice of the guards' home room.</summary>
    private const int OuterWallWeight = 0x32;

    /// <summary>The kinds of furniture: tiles 12, 16, ... 56, each in four orientations.</summary>
    private const int FurnitureKinds = 16;

    /// <summary>
    /// The bit of room <paramref name="room"/> in a room's door mask (<see cref="Room.Doors"/>), as the original computes
    /// it: <c>1 &lt;&lt; room</c> in 16 bits, widened to 32 with its sign.
    /// </summary>
    /// <remarks>
    /// New code (the expression the original writes at each use). Because the shift is 16-bit, rooms 16 to 23 have no
    /// bit at all (they never count as joined, so a room may get several doors to one of them), and room 15's bit is
    /// the whole upper half of the mask.
    /// </remarks>
    // C: 1 << n is an int: 0 for n of 16 and above, and 0x8000 widens to 0xffff8000
    // BUG: CA-28 the bit of rooms 16 to 23 and of room 15; reproduced as coded.
    public static int RoomBit(int room) => C16.Shl(1, room);

    /// <summary>
    /// Puts a wall on side <paramref name="side"/> of cell (<paramref name="x"/>, <paramref name="y"/>): the side's bit in
    /// the cell, the opposite bit in the neighbour (when it is on the map), and the wall tiles of both cells from their
    /// bits.
    /// </summary>
    /// <param name="state">The building.</param>
    /// <param name="x">Cell column.</param>
    /// <param name="y">Cell row.</param>
    /// <param name="side">0 north, 1 east, 2 south, 3 west.</param>
    // orig: TAC 00e8:534f set_wall_edge (src/c/tac/tac_00e8_534f.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static void SetWallEdge(RaidState state, int x, int y, int side)
    {
        var walls = state.WallFlags;
        walls[RaidState.Cell(x, y)] |= (byte)WallBit(side);
        if (side == North && y > 0)
        {
            walls[RaidState.Cell(x, y - 1)] |= (byte)WallBit(South);
        }
        if (side == East && x < RaidState.MapW - 1)
        {
            walls[RaidState.Cell(x + 1, y)] |= (byte)WallBit(West);
        }
        if (side == South && y < RaidState.MapH - 1)
        {
            walls[RaidState.Cell(x, y + 1)] |= (byte)WallBit(North);
        }
        if (side == West && x > 0)
        {
            walls[RaidState.Cell(x - 1, y)] |= (byte)WallBit(East);
        }
        PutWallTile(state, RaidState.Cell(x, y));
        // the neighbour's tile is written even where its bit was not (at the map's edge)
        PutWallTile(state, RaidState.Cell(state.Mve4X[side] + x, state.Mve4Y[side] + y));
    }

    private static void PutWallTile(RaidState state, int cell) =>
        state.CellMap[cell] = state.WallTileTable[state.WallFlags[cell]];

    /// <summary>
    /// Starts a fresh plan: no walls, no rooms, bare floor everywhere, then the outer wall, a rectangle one cell inside
    /// the map's edge (12 x 13 cells for the one-room ambush, else the whole map). The wall bits the outer wall leaves on
    /// the map's first and last row and column are cleared again.
    /// </summary>
    /// <param name="state">The building; <see cref="RaidState.GameMode"/> is read.</param>
    // orig: TAC 00e8:45da init_building_grid (src/c/tac/tac_00e8_45da.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static void InitBuildingGrid(RaidState state)
    {
        var (width, height) = state.GameMode == RaidState.OneRoomMode ? (OneRoomWidth, OneRoomHeight) : (RaidState.MapW, RaidState.MapH);
        Array.Clear(state.WallFlags);
        Array.Clear(state.RoomAtCell);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        for (var x = 1; x < width - 1; x++)
        {
            SetWallEdge(state, x, 1, North);
            SetWallEdge(state, x, height - 2, South);
            state.WallFlags[RaidState.Cell(x, 0)] = 0;
            state.WallFlags[RaidState.Cell(x, RaidState.MapH - 1)] = 0;
        }
        for (var y = 1; y < height - 1; y++)
        {
            SetWallEdge(state, 1, y, West);
            SetWallEdge(state, width - 2, y, East);
            state.WallFlags[RaidState.Cell(0, y)] = 0;
            state.WallFlags[RaidState.Cell(RaidState.MapW - 1, y)] = 0;
        }
    }

    /// <summary>
    /// Draws up to twenty inner walls. Each starts at a random cell that has a wall on exactly one side and no wall
    /// across its line within four cells either way, and runs away from that wall, cell by cell, until it has met one
    /// more walled cell (or left the map).
    /// </summary>
    /// <remarks>
    /// The wall drawn runs at right angles to the wall it starts from: a cell with only a north wall starts a wall
    /// going south along its east side ... The pass stops early when 999 random cells give no start.
    /// </remarks>
    /// <param name="state">The building and the random generator.</param>
    // orig: TAC 00e8:46cb add_wall_segments (src/c/tac/tac_00e8_46cb.c) matched; tests: BuildingRulesTests, TacOracleTests
    // BUG: CA-29 the start test reads wall bits off the map (guards_left, AtLoc, the stack); reproduced as coded.
    public static void AddWallSegments(RaidState state)
    {
        for (var segment = 0; segment < WallSegments; segment++)
        {
            int x, y, heading;
            var tries = 0;
            bool clear;
            do
            {
                x = GameLibrary.RandomBelow(state.Random, RaidState.MapW - 2) + 1;
                y = GameLibrary.RandomBelow(state.Random, RaidState.MapH - 2) + 1;
                heading = state.WallFlags[RaidState.Cell(x, y)] switch
                {
                    1 => South,     // a north wall only: the new wall goes south
                    2 => West,
                    4 => North,
                    8 => East,
                    _ => -1,
                };
                // the cells along the line the new wall starts on, four either way, must have no wall across it
                // (with no heading the line is the north-south one, as the original computes it)
                clear = true;
                var crossing = (heading & 1) != 0 ? NorthSouthWalls : EastWestWalls;
                for (var step = -4; step <= 4; step++)
                {
                    if ((WallBitsEvenOffTheMap(state, x + state.Mve4X[heading + 1] * step, y + state.Mve4Y[heading + 1] * step) & crossing) != 0)
                    {
                        clear = false;
                    }
                }
            }
            while ((heading == -1 || !clear) && ++tries < Tries);
            if (tries >= Tries)
            {
                return;
            }

            // walk away from the wall, putting the new wall on the side to the right of the heading
            var walledCellsMet = -1;
            do
            {
                if (state.WallFlags[RaidState.Cell(x, y)] != 0)
                {
                    walledCellsMet++;
                }
                SetWallEdge(state, x, y, (heading + 1) & 3);
                x += state.Mve4X[heading];
                y += state.Mve4Y[heading];
            }
            // C: the original stops at row 40, not at the map's 32 rows; rows 32..39 are cells of the next column
            while (walledCellsMet == 0 && x >= 0 && y >= 0 && x < RaidState.MapW && y < RaidState.MapW);
        }
    }

    /// <summary>
    /// The wall bits <see cref="AddWallSegments"/> reads for cell (x, y), which may lie up to three columns off the map:
    /// the original indexes the table without a check and reads the bytes around it in its data segment.
    /// </summary>
    /// <remarks>
    /// Before the table lie unused bytes (always 0) and, just before it, the low byte of
    /// <see cref="RaidState.GuardsLeft"/>; after it <see cref="RaidState.AtLoc"/>, four unused bytes and the bottom of the
    /// stack. So the number of guards still to come (8 or more, a west wall bit) can stop an inner wall near the west
    /// edge (docs/original-bugs.md CA-29).
    /// </remarks>
    private static int WallBitsEvenOffTheMap(RaidState state, int x, int y)
    {
        var index = RaidState.Cell(x, y);
        const int GuardsLeftLowByte = -2;
        const int StackEnd = RaidState.Cells + 0x3f;   // the farthest a scan reaches: column 41, row 30
        return index switch
        {
            >= 0 and < RaidState.Cells => state.WallFlags[index],
            GuardsLeftLowByte => C16.LoByte(state.GuardsLeft),
            > -64 and < GuardsLeftLowByte => 0,
            RaidState.Cells => C16.LoByte(state.AtLoc),
            RaidState.Cells + 1 => C16.HiByte(state.AtLoc),
            // UNCERTAIN: U-500 the bytes after AtLoc: four unused bytes, then stack the raid never reaches before the building is generated; what the PC's memory held there (0 in every session)
            > RaidState.Cells + 1 and <= StackEnd => 0,
            _ => throw new IndexOutOfRangeException($"add_wall_segments reads the wall bits of cell ({x}, {y}), far off the map"),
        };
    }

    /// <summary>
    /// Turns the walls into rooms: scans the inner cells row by row; a cell without a wall on its west side belongs to
    /// the room of its west neighbour, one with a west wall but none on its north side to the room of its north
    /// neighbour, and one with both walls starts a new room. Each room's first cell, width and height are recorded.
    /// </summary>
    /// <param name="state">The building; <see cref="RaidState.Nroom"/> is the number of rooms found.</param>
    /// <returns>Whether every room is at most 10 cells wide and high (otherwise the generator starts again).</returns>
    // orig: TAC 00e8:4838 label_rooms (src/c/tac/tac_00e8_4838.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static bool LabelRooms(RaidState state)
    {
        state.Nroom = 0;
        for (var y = 1; y < RaidState.MapH - 1; y++)
        {
            var width = 1;
            for (var x = 1; x < RaidState.MapW - 1; x++)
            {
                var cell = RaidState.Cell(x, y);
                var walls = state.WallFlags[cell];
                if ((walls & WallBit(West)) == 0)
                {
                    state.RoomAtCell[cell] = state.RoomAtCell[RaidState.Cell(x - 1, y)];
                    width++;
                    state.Rm[state.RoomAtCell[cell]].Dx = (sbyte)width;
                }
                else if ((walls & WallBit(North)) == 0)
                {
                    width = 1;
                    state.RoomAtCell[cell] = state.RoomAtCell[RaidState.Cell(x, y - 1)];
                    state.Rm[state.RoomAtCell[cell]].Dy++;
                }
                else
                {
                    width = 1;
                    state.Nroom++;
                    // C: the room number goes through the byte of room_at_cell
                    state.RoomAtCell[cell] = (byte)state.Nroom;
                    var room = state.Rm[state.Nroom];
                    room.X = (sbyte)x;
                    room.Y = (sbyte)y;
                    room.Dy = 1;
                }
            }
        }
        var fits = true;
        for (var room = 1; room <= state.Nroom; room++)
        {
            if (state.Rm[room].Dx > MaxRoomSide || state.Rm[room].Dy > MaxRoomSide)
            {
                fits = false;
            }
        }
        return fits;
    }

    /// <summary>
    /// Joins the rooms with doors. A walk starts in a random room; each step picks a random side of the current room
    /// and the room behind the middle cell of that side, puts a door there when the two are not joined yet and the
    /// wall there is plain (no wall across it on either side, one cell either way), and moves on to that room. The walk
    /// goes on until every room has been joined to one already reached.
    /// </summary>
    /// <remarks>
    /// A door clears the wall bits of its two cells, puts a door tile in each, counts a door for both rooms, joins them
    /// in their door masks and is recorded in <see cref="RaidState.Dr"/>. The walk gives up after 999 tries without a
    /// new door, or at 96 doors.
    /// </remarks>
    /// <param name="state">The building and the random generator.</param>
    /// <returns>Whether all rooms were joined (otherwise the generator starts again).</returns>
    // orig: TAC 00e8:4931 connect_rooms (src/c/tac/tac_00e8_4931.c) matched; tests: BuildingRulesTests, TacOracleTests
    // BUG: CA-28 rooms 16 to 23 have no door bit and room 15's bit is the upper half of the mask (RoomBit); reproduced as coded.
    public static bool ConnectRooms(RaidState state)
    {
        for (var room = 0; room < RaidState.MaxRooms; room++)
        {
            state.RoomDoorCount[room] = 0;
            state.Rm[room].Doors = 0;
        }
        // DORMANT: the original keeps a flag, cleared here, that would make the walk go on until it reached a room the
        // player had visited; nothing sets it
        var waitForVisited = false;
        var tries = 0;
        state.Ndoor = 0;
        var current = GameLibrary.RandomBelow(state.Random, state.Nroom) + 1;
        var reached = 1;
        do
        {
            int x, y, side, behind;
            bool fits;
            do
            {
                var room = state.Rm[current];
                // C: dx / (char)2 and dy / (char)2: signed char halves
                switch (GameLibrary.RandomBelow(state.Random, 4))
                {
                    case 0:
                        (x, y, side) = (room.X + room.Dx / 2, room.Y, North);
                        break;
                    case 1:
                        (x, y, side) = (room.X + room.Dx / 2, room.Y + room.Dy - 1, South);
                        break;
                    case 2:
                        (x, y, side) = (room.X, room.Y + room.Dy / 2, West);
                        break;
                    default:
                        (x, y, side) = (room.X + room.Dx - 1, room.Y + room.Dy / 2, East);
                        break;
                }
                fits = true;
                behind = state.RoomAtCell[RaidState.Cell(state.Mve4X[side] + x, state.Mve4Y[side] + y)];
                if ((room.Doors & RoomBit(behind)) == 0)
                {
                    var crossing = (side & 1) != 0 ? NorthSouthWalls : EastWestWalls;
                    for (var step = -1; step <= 1; step++)
                    {
                        var alongX = state.Mve4X[side + 1] * step;
                        var alongY = state.Mve4Y[side + 1] * step;
                        if ((state.WallFlags[RaidState.Cell(alongX + x, alongY + y)] & crossing) != 0)
                        {
                            fits = false;
                        }
                        if ((state.WallFlags[RaidState.Cell(alongX + state.Mve4X[side] + x, alongY + state.Mve4Y[side] + y)] & crossing) != 0)
                        {
                            fits = false;
                        }
                    }
                }
                if (behind == 0)
                {
                    fits = false;
                }
            }
            while (++tries < Tries && !fits);

            if ((state.Rm[current].Doors & RoomBit(behind)) == 0 && tries < Tries)
            {
                PutDoor(state, current, behind, x, y, side);
                tries = 0;
                if (state.Rm[behind].Doors == 0)
                {
                    reached++;
                }
                state.Rm[current].Doors |= RoomBit(behind);
                state.Rm[behind].Doors |= RoomBit(current);
                if (waitForVisited && (state.Rm[behind].Status & RoomStatus.Visited) != 0)
                {
                    waitForVisited = false;
                }
            }
            current = behind;
        }
        while ((reached < state.Nroom || waitForVisited) && tries < Tries && state.Ndoor < RaidState.MaxDoors);
        return !waitForVisited && tries < Tries && state.Ndoor < RaidState.MaxDoors;
    }

    /// <summary>The door itself: the wall bits cleared, the doors counted, the door tiles and the door record (connect_rooms).</summary>
    private static void PutDoor(RaidState state, int room, int behind, int x, int y, int side)
    {
        var opposite = (side + 2) & 3;
        var here = RaidState.Cell(x, y);
        var there = RaidState.Cell(state.Mve4X[side] + x, state.Mve4Y[side] + y);
        state.WallFlags[here] &= (byte)(15 - WallBit(side));
        state.WallFlags[there] &= (byte)(15 - WallBit(opposite));
        state.RoomDoorCount[room]++;
        state.RoomDoorCount[behind]++;
        state.CellMap[here] = (byte)(side + RaidState.TileDoor);
        state.CellMap[there] = (byte)(opposite + RaidState.TileDoor);
        var door = state.Dr[state.Ndoor];
        door.Room1 = (sbyte)room;
        door.Room2 = (sbyte)behind;
        door.X = (sbyte)x;
        door.Y = (sbyte)y;
        door.Dir = (sbyte)side;
        state.Ndoor++;
    }

    /// <summary>
    /// Drops the rooms the building cannot use and picks the two special rooms. A room without a door goes, and so does
    /// a room on the outer wall with fewer than two doors. Of the other rooms on the outer wall the one with the most
    /// doors beyond its outer sides, then the biggest, becomes the guards' home room <see cref="RaidState.Guard"/>; of
    /// the inner rooms the biggest becomes the target room <see cref="RaidState.MacGuff"/>.
    /// </summary>
    /// <remarks>
    /// When no room on the outer wall was dropped, rooms are dropped from room 1 on, leaving out the two special rooms,
    /// until a room with doors that is neither special room has been dropped (a room dropped earlier may be dropped and
    /// counted again).
    /// </remarks>
    /// <param name="state">The building.</param>
    /// <returns>The number of rooms dropped.</returns>
    // orig: TAC 00e8:4d3a prune_rooms (src/c/tac/tac_00e8_4d3a.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static int PruneRooms(RaidState state)
    {
        var outerRoomDropped = false;
        var removed = 0;
        var biggestInner = 0;
        var bestOuter = -999;
        for (var room = 1; room <= state.Nroom; room++)
        {
            if (state.RoomDoorCount[room] == 0)
            {
                RemoveRoom(state, room);
                removed++;
                continue;
            }
            var record = state.Rm[room];
            var outerSides = 0;
            if (record.X == 1)
            {
                outerSides++;
            }
            if (record.Y == 1)
            {
                outerSides++;
            }
            if (record.X + record.Dx == RaidState.MapW - 1)
            {
                outerSides++;
            }
            if (record.Y + record.Dy == RaidState.MapH - 1)
            {
                outerSides++;
            }
            var area = record.Dx * record.Dy;
            if (outerSides != 0)
            {
                if (state.RoomDoorCount[room] < 2)
                {
                    RemoveRoom(state, room);
                    removed++;
                    outerRoomDropped = true;
                }
                else
                {
                    var worth = C16.Int((outerSides - state.RoomDoorCount[room]) * OuterWallWeight + area);
                    if (worth > bestOuter)
                    {
                        bestOuter = worth;
                        state.Guard = (short)room;
                    }
                }
            }
            else if (area > biggestInner)
            {
                biggestInner = area;
                state.MacGuff = (short)room;
            }
        }
        if (!outerRoomDropped)
        {
            var room = 0;
            do
            {
                room++;
                if (room != state.Guard && room != state.MacGuff)
                {
                    RemoveRoom(state, room);
                    removed++;
                }
            }
            while (state.RoomDoorCount[room] == 0 || room == state.Guard || room == state.MacGuff);
        }
        return removed;
    }

    /// <summary>
    /// Drops room <paramref name="room"/> from the plan: marks it removed, clears its cells (no walls, no room, bare floor
    /// except for door tiles), and makes its doors lead outside. A dropped room that had doors becomes the room the
    /// player starts in.
    /// </summary>
    /// <param name="state">The building.</param>
    /// <param name="room">The room, 1..23.</param>
    // orig: TAC 00e8:4ead remove_room (src/c/tac/tac_00e8_4ead.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static void RemoveRoom(RaidState state, int room)
    {
        var record = state.Rm[room];
        record.Status |= RoomStatus.Removed;
        for (var x = (int)record.X; x < record.X + record.Dx; x++)
        {
            for (var y = (int)record.Y; y < record.Y + record.Dy; y++)
            {
                var cell = RaidState.Cell(x, y);
                state.WallFlags[cell] = 0;
                state.RoomAtCell[cell] = 0;
                if (!IsDoorTile(state.CellMap[cell]))
                {
                    state.CellMap[cell] = RaidState.CellEmpty;
                }
            }
        }
        if (state.RoomDoorCount[room] != 0)
        {
            state.InRoom = (short)room;
        }
        for (var i = 0; i < state.Ndoor; i++)
        {
            var door = state.Dr[i];
            if (door.Room1 == room)
            {
                door.Room1 = 0;
            }
            if (door.Room2 == room)
            {
                door.Room2 = 0;
            }
        }
    }

    /// <summary>
    /// After the building is made (the end of tac_init): the plot object counts as present (<see cref="RaidState.MacHere"/>)
    /// in a practice session, with person 0 or in a city with a double agent, besides when <see cref="FurnishRoom"/> put the
    /// person's own plot object in the target room (it is set, never cleared, here); person 0 makes the raid one level
    /// harder (up to 4); with the plot object present two more guards a level may come.
    /// </summary>
    /// <param name="state">The raid, its building made.</param>
    public static void SetUpThePlotObjectsGuards(RaidState state)
    {
        const int hardestRaid = 4;
        // C: the bit of the city in the word of doubled cities, a 16-bit shift
        if (state.Status.HasFlag(Options.Practice) || state.Who == 0 || (state.Doubles & C16.Shl(1, state.InCity)) != 0)
        {
            state.MacHere = true;
        }
        if (state.Who == 0 && state.Diff < hardestRaid)
        {
            state.Diff++;
        }
        if (state.MacHere)
        {
            state.GuardsLeft += (short)(state.Diff * 2);
        }
    }

    /// <summary>Whether a cell byte is one of the four door tiles (open or shut, examined or not: the two low bits are the side).</summary>
    private static bool IsDoorTile(byte cell) => (cell & 0xfc) == RaidState.TileDoor;

    /// <summary>
    /// Furnishes room <paramref name="room"/> (a dropped room is left alone). Chooses the room's type at random among the
    /// types that fit its size and the kind of building; puts a piece of furniture for every six cells, each on a
    /// random cell that suits it (against a wall, facing into the room, or out in the floor away from the walls) and
    /// allowed in that type of room; and, in the target room of a raid with a person to arrest, puts the plot object in
    /// the middle and the person just north of it.
    /// </summary>
    /// <param name="state">The building, the campaign's persons and plot objects, and the random generator.</param>
    /// <param name="room">The room, 1..<see cref="RaidState.Nroom"/>.</param>
    // orig: TAC 00e8:4f8d furnish_room (src/c/tac/tac_00e8_4f8d.c) matched; tests: BuildingRulesTests, TacOracleTests
    // BUG: CA-8 no room type allowed in no kind of building (bed room, kitchen) is ever chosen; reproduced as coded.
    public static void FurnishRoom(RaidState state, int room)
    {
        var record = state.Rm[room];
        if ((record.Status & RoomStatus.Removed) != 0)
        {
            return;
        }
        var area = record.Dx * record.Dy;
        var sizeClass = area > BigRoomCells ? 4 : area > MiddleRoomCells ? 2 : 1;
        var type = ChooseRoomType(state, sizeClass);
        if (state.GameMode == RaidState.OneRoomMode)
        {
            type = 0;
        }
        record.Type = (short)type;

        for (var piece = 0; piece < area / CellsPerPiece; piece++)
        {
            var tries = 0;
            int x, y, tile;
            do
            {
                x = record.X + GameLibrary.RandomBelow(state.Random, record.Dx);
                y = record.Y + GameLibrary.RandomBelow(state.Random, record.Dy);
                tile = ChooseFurniture(state, record, x, y);
            }
            while ((tile == -1 || (state.Ob[tile].Where & 1 << type) == 0) && ++tries < PieceTries);
            if (tries < PieceTries)
            {
                state.CellMap[RaidState.Cell(x, y)] = (byte)tile;
                if ((state.Ob[tile].Bits & TileFlags.Wide) != 0)
                {
                    // a wide piece's other half: the odd tile of a pair is the right-hand half
                    if ((tile & 1) != 0)
                    {
                        state.CellMap[RaidState.Cell(x - 1, y)] = (byte)(tile - 1);
                    }
                    else
                    {
                        state.CellMap[RaidState.Cell(x + 1, y)] = (byte)(tile + 1);
                    }
                }
            }
        }

        if (room != state.MacGuff)
        {
            return;
        }
        if ((state.Who == -1 || state.GameMode == 1) && state.GameMode != 2)
        {
            return;
        }
        var middleX = record.X + record.Dx / 2;
        var middleY = record.Y + record.Dy / 2;
        foreach (var plotObject in state.Mc)
        {
            if (plotObject.Who == state.Who && state.Who != -1)
            {
                state.CellMap[RaidState.Cell(middleX, middleY)] = RaidState.TileMacGuffin;
                var macGuffin = state.Ob[RaidState.TileMacGuffin];
                macGuffin.SpriteX = C16.Int(macGuffin.SpriteX + (plotObject.Type << 4));
                // the tile's name becomes the object's, cut to eleven characters and ended by a zero; the rest of the
                // buffer keeps what it held
                var name = plotObject.Name.Length > ObjectType.NameLength - 1 ? plotObject.Name[..(ObjectType.NameLength - 1)] : plotObject.Name;
                name.CopyTo(macGuffin.Name);
                macGuffin.Name[name.Length] = '\0';
                state.MacHere = true;
            }
        }
        state.CellMap[RaidState.Cell(middleX, middleY - 1)] = RaidState.TileAgent;
        // a woman to arrest has the picture beside the man's; with no one to arrest (an ambush) the original reads
        // the record before the first person, which the typed model does not have
        if ((state.Ps[state.Who].Face & 1) == 0)
        {
            var agent = state.Ob[RaidState.TileAgent];
            agent.SpriteX = C16.Int(agent.SpriteX + 0x10);
        }
    }

    /// <summary>A random room type that fits the size class and the kind of building: each type in turn wins with a chance of one half.</summary>
    private static int ChooseRoomType(RaidState state, int sizeClass)
    {
        var type = -1;
        do
        {
            for (var candidate = 0; candidate < RaidState.RoomTypes; candidate++)
            {
                var kind = state.Rms[candidate];
                // C: the random draw is made only when both masks fit (&& short-circuits)
                if ((kind.Size & sizeClass) != 0 && (kind.Where & state.BuildingKindMask) != 0 && GameLibrary.RandomBelow(state.Random, 2) != 0)
                {
                    type = candidate;
                    break;
                }
            }
        }
        while (type == -1);
        return type;
    }

    /// <summary>
    /// The piece of furniture tried on cell (x, y), or -1 for none. On bare floor with no wall or other piece next to it
    /// (no neighbour tile above 4, signed: an empty cell is -1): a free-standing piece facing the nearest wall of the
    /// room. On a straight wall: a piece that stands against walls, facing away from it.
    /// </summary>
    private static int ChooseFurniture(RaidState state, Room room, int x, int y)
    {
        var cell = state.CellMap[RaidState.Cell(x, y)];
        var tile = -1;
        if (cell == RaidState.CellEmpty)
        {
            var free = true;
            for (var side = 0; side < 4; side++)
            {
                // C: cell_map is signed char: an empty neighbour (-1) and an open door (0x80 up) count as below 4
                if ((sbyte)state.CellMap[RaidState.Cell(state.Mve4X[side] + x, state.Mve4Y[side] + y)] > 4)
                {
                    free = false;
                }
            }
            if (free)
            {
                var intoX = x - room.X;
                var intoY = y - room.Y;
                int facing;
                if (intoX > intoY)
                {
                    facing = room.Dx / 2 < intoX ? East : West;
                }
                else
                {
                    facing = room.Dy / 2 < intoY ? South : North;
                }
                tile = RandomFurniture(state, facing);
                if ((state.Ob[tile].Bits & TileFlags.AtWall) != 0)
                {
                    tile = -1;
                }
            }
        }
        // C: a signed comparison: the straight walls 0..3 (and any cell byte of 0x80 up)
        else if ((sbyte)cell < 4)
        {
            tile = RandomFurniture(state, cell & 3);
            if ((state.Ob[tile].Bits & TileFlags.AtWall) == 0)
            {
                tile = -1;
            }
        }
        return tile;
    }

    /// <summary>A random furniture tile of orientation <paramref name="side"/>: one of the 16 kinds, drawn again until it is below the tile of the person to arrest.</summary>
    private static int RandomFurniture(RaidState state, int side)
    {
        int tile;
        do
        {
            tile = (GameLibrary.RandomBelow(state.Random, FurnitureKinds) << 2) + side + RaidState.TileFurniture;
        }
        while (tile >= RaidState.TileAgent);
        return tile;
    }

    /// <summary>
    /// Finds the door between rooms <paramref name="roomA"/> and <paramref name="roomB"/> and gives its cell on
    /// <paramref name="roomA"/>'s side.
    /// </summary>
    /// <param name="state">The doors.</param>
    /// <param name="roomA">The room whose side of the door is wanted.</param>
    /// <param name="roomB">The room on the other side.</param>
    /// <param name="x">The door cell's column, when found; otherwise unchanged.</param>
    /// <param name="y">The door cell's row, when found; otherwise unchanged.</param>
    /// <returns>Whether there is such a door.</returns>
    // orig: TAC 00e8:6e34 find_door_cell (src/c/tac/tac_00e8_6e34.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static bool FindDoorCell(RaidState state, int roomA, int roomB, ref int x, ref int y)
    {
        for (var i = 0; i < state.Ndoor; i++)
        {
            var door = state.Dr[i];
            if (door.Room1 == roomA && door.Room2 == roomB)
            {
                x = door.X;
                y = door.Y;
                return true;
            }
            if (door.Room1 == roomB && door.Room2 == roomA)
            {
                x = door.X + state.Mve4X[door.Dir];
                y = door.Y + state.Mve4Y[door.Dir];
                return true;
            }
        }
        return false;
    }

    /// <summary>A quick estimate of the length of the vector (<paramref name="a"/>, <paramref name="b"/>): the longer side plus half the shorter.</summary>
    /// <param name="a">The x difference, pixels.</param>
    /// <param name="b">The y difference, pixels.</param>
    /// <returns>The estimate.</returns>
    // orig: TAC 00e8:5ab6 approx_distance (src/c/tac/tac_00e8_5ab6.c) matched; tests: BuildingRulesTests, TacOracleTests
    public static int ApproxDistance(int a, int b)
    {
        // C: 16-bit abs and sums (abs(-32768) stays negative)
        var x = C16.Abs(C16.Int(a));
        var y = C16.Abs(C16.Int(b));
        return x > y ? C16.Int(x + C16.Div(y, 2)) : C16.Int(y + C16.Div(x, 2));
    }
}
