using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Raid;

/// <summary>
/// The rules of the guards that need no screen: where a new guard comes in, how a guard behaves (patrols, hunts,
/// leaves), which room he goes to next, the way through a room, the alarm and how far each room is from it, and
/// whether a guard can see the player.
/// </summary>
public static class GuardRules
{
    /// <summary>Tries to place a new guard, and to find a cell or a door for him.</summary>
    private const int Tries = 999;

    /// <summary>A guard counts as awake below this knocked-out time ...</summary>
    private const int AwakeStun = 0x18;

    /// <summary>... and as out of the raid for good from this one.</summary>
    private const int OutStun = 0x3e7;

    /// <summary>The cap of the alarm (<see cref="RaidState.Eseen"/>).</summary>
    private const int MaxAlarm = 0x270f;

    /// <summary>The door distance of the outside (room 0) once the alarm distances are worked out.</summary>
    private const short OutsideDistance = 999;

    /// <summary>A sight line: blocked.</summary>
    public const int SightBlocked = 0;

    /// <summary>A sight line: it passes a solid piece that gives partial cover.</summary>
    public const int SightCovered = 1;

    /// <summary>A sight line: clear.</summary>
    public const int SightClear = 2;

    /// <summary>The right edge of the map in pixels (640 - 1), about which a sight line is mirrored.</summary>
    private const int MapRightPixel = 0x27f;

    /// <summary>The bottom edge of the map in pixels (512 - 1).</summary>
    private const int MapBottomPixel = 0x1ff;

    /// <summary>A sight line longer than this in x or y is walked with its slope halved until it is not (only the slope; the end points stay).</summary>
    private const int SlopeLimit = 100;

    /// <summary>"No crossing" in the sight line walk: a coordinate beyond any on the map.</summary>
    private const int Far = 0x3e7;

    /// <summary>The guard behaviours of <see cref="GuardBehaviour"/>: patrol, go to the alarm, hunt, leave for good.</summary>
    public const int Patrol = 0;

    /// <summary>A behaviour: go towards the alarm (or, without one, patrol where nobody went for longest).</summary>
    public const int Respond = 1;

    /// <summary>A behaviour: hunt (at night): head for the guards' home room, or the exit when there is an alarm.</summary>
    public const int Hunt = 2;

    /// <summary>A behaviour: a guard beyond the level's number, on his way out of the building.</summary>
    public const int Withdraw = -1;

    /// <summary>
    /// What it costs a guard to go into room <paramref name="room"/> next, by his behaviour: the lower, the likelier he
    /// goes there (<see cref="ChooseNextRoom"/>). A patrolling guard goes where no guard went for longest; with an alarm
    /// a guard goes towards it; a hunting or withdrawing guard goes towards the alarm when the player has been seen,
    /// else towards the guards' home room. The distances count four per door, plus a random 0..6.
    /// </summary>
    /// <param name="state">The rooms, the alarm and the random generator.</param>
    /// <param name="room">The room behind a door.</param>
    /// <param name="mode">The guard's behaviour (<see cref="Patrol"/>, <see cref="Respond"/>, <see cref="Hunt"/>, <see cref="Withdraw"/>).</param>
    /// <returns>The cost.</returns>
    // orig: TAC 00e8:6c44 room_cost (src/c/tac/tac_00e8_6c44.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int RoomCost(RaidState state, int room, int mode)
    {
        switch (mode)
        {
            case Patrol:
                return state.RoomLastVisit[room];
            case Respond:
                return state.ToRoom <= 0 ? state.RoomLastVisit[room] : TowardsAlarm(state, room);
            case Withdraw:
            case Hunt:
                return state.Eseen != 0 ? TowardsAlarm(state, room) : C16.Int((state.ExitDist[room] << 2) + GameLibrary.RandomBelow(state.Random, 7));
            default:
                // DORMANT: GuardBehaviour gives no other mode; the original's switch would fall out without a result
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "room_cost of an unknown behaviour returns no value");
        }
    }

    private static int TowardsAlarm(RaidState state, int room) =>
        C16.Int((state.Rm[room].ToDist << 2) + GameLibrary.RandomBelow(state.Random, 7));

    /// <summary>
    /// The room a guard in room <paramref name="room"/> goes to next: of the rooms behind the room's doors (the outside
    /// left out), the one of lowest <see cref="RoomCost"/>; it is marked as visited now.
    /// </summary>
    /// <remarks>The cost of a room is drawn once to compare and once more to keep, so a candidate costs two random draws.</remarks>
    /// <param name="state">The doors, the rooms and the random generator.</param>
    /// <param name="room">The guard's room.</param>
    /// <param name="guard">The guard's slot, 1..8.</param>
    /// <returns>The next room.</returns>
    /// <exception cref="UninitialisedReadException">No door leads from the room to another room: the original then takes a room number from the stack.</exception>
    // orig: TAC 00e8:6b78 choose_next_room (src/c/tac/tac_00e8_6b78.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int ChooseNextRoom(RaidState state, int room, int guard)
    {
        var lowest = 0x270f;
        var mode = GuardBehaviour(state, guard);
        int? next = null;
        for (var i = 0; i < state.Ndoor; i++)
        {
            var door = state.Dr[i];
            // C: && evaluates left to right and stops early: the cost (a random draw) is only drawn for a door of this room
            if (door.Room1 == room && RoomCost(state, door.Room2, mode) < lowest && door.Room2 != 0)
            {
                next = door.Room2;
                lowest = RoomCost(state, door.Room2, mode);
            }
            if (door.Room2 == room && RoomCost(state, door.Room1, mode) < lowest && door.Room1 != 0)
            {
                next = door.Room1;
                lowest = RoomCost(state, door.Room1, mode);
            }
        }
        var chosen = next ?? throw new UninitialisedReadException("choose_next_room's room, no door leading on");
        state.RoomLastVisit[chosen] = C16.Int(state.Rtime + 1);
        return chosen;
    }

    /// <summary>
    /// Raises the alarm in room <paramref name="room"/>, or just works the door distances out again (amount 0). The
    /// amount, scaled by the difficulty as amount / 4 * (difficulty + 4), raises <see cref="RaidState.Eseen"/> to at least
    /// that much (at most 9999); any amount also ends the player's disguise. In a kill raid the alarm is always in the
    /// target room. Then every room's number of doors to the alarm room is worked out (-1 unreachable, the outside 999).
    /// </summary>
    /// <param name="state">The alarm and the rooms.</param>
    /// <param name="room">The room; -1 for the target room of a kill raid.</param>
    /// <param name="amount">0, or 0xf0 when a guard saw a crime.</param>
    // orig: TAC 00e8:6cb1 raise_alert (src/c/tac/tac_00e8_6cb1.c) matched; tests: GuardRulesTests, TacOracleTests
    // BUG: CA-30 the alarm of room -1 outside a kill raid makes the alarm room -1 and keeps the old distances; reproduced as coded.
    public static void RaiseAlert(RaidState state, int room, int amount)
    {
        if (amount != 0)
        {
            state.Disguised = false;
        }
        // C: 16-bit division and product
        var raise = C16.Int(C16.Div(C16.Int(amount), 4) * (state.Diff + 4));
        state.ToRoom = C16.Int(room);
        if (state.GameMode == RaidState.KillMode)
        {
            if (room == -1)
            {
                state.ToRoom = state.MacGuff;
            }
            room = state.MacGuff;
        }
        state.Eseen = (short)GameLibrary.Clamp(state.Eseen, raise, MaxAlarm);
        if (room == state.AlertCachedRoom)
        {
            // the cached room is -1 and never written: only the alarm of room -1 outside a kill raid (two guards in
            // an alarm room the player has left) comes here, and keeps the distances of the last alarm room
            return;
        }
        for (var i = 0; i <= state.Nroom; i++)
        {
            state.Rm[i].ToDist = -1;
        }
        state.Rm[room].ToDist = 0;
        bool settled;
        do
        {
            settled = true;
            for (var i = 0; i < state.Ndoor; i++)
            {
                var door = state.Dr[i];
                if (door.Room1 == 0 && door.Room2 == 0)
                {
                    continue;
                }
                var one = state.Rm[door.Room1];
                var other = state.Rm[door.Room2];
                if (other.ToDist != -1 && (one.ToDist == -1 || other.ToDist + 1 < one.ToDist))
                {
                    one.ToDist = C16.Int(other.ToDist + 1);
                    settled = false;
                }
                if (one.ToDist != -1 && (other.ToDist == -1 || one.ToDist + 1 < other.ToDist))
                {
                    other.ToDist = C16.Int(one.ToDist + 1);
                    settled = false;
                }
            }
        }
        while (!settled);
        state.Rm[0].ToDist = OutsideDistance;
    }

    /// <summary>
    /// Works out the way of guard <paramref name="guard"/> through room <paramref name="room"/> to a target cell: a
    /// breadth-first fill from the target over the room's free cells (diagonal steps only between two free cells), each
    /// cell holding its steps from the target plus one. The fill stops once it reaches the guard's cell; the guard then
    /// walks down the numbers. A guard on a cell the fill did not reach (a piece of furniture) takes the number of a
    /// reached cell next to him.
    /// </summary>
    /// <remarks>
    /// A solid target cell gets 0, and from a cell holding 0 the fill goes on straight steps only. The original keeps
    /// its queue in two 256-byte rings; a room has at most 100 cells and each is queued once, so the rings never wrap.
    /// </remarks>
    /// <param name="state">The map and the guard's distance map (<see cref="RaidState.PathDist"/>).</param>
    /// <param name="guard">The guard's slot.</param>
    /// <param name="room">The room the way lies in.</param>
    /// <param name="guardX">The guard's cell column.</param>
    /// <param name="guardY">The guard's cell row.</param>
    /// <param name="targetX">The target cell's column.</param>
    /// <param name="targetY">The target cell's row.</param>
    /// <returns>Whether the fill reached the guard.</returns>
    // orig: TAC 00e8:6ec9 build_path_map (src/c/tac/tac_00e8_6ec9.c) matched; tests: GuardRulesTests, TacOracleTests
    public static bool BuildPathMap(RaidState state, int guard, int room, int guardX, int guardY, int targetX, int targetY)
    {
        var distance = state.PathDist[guard];
        Array.Clear(distance);
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((targetX, targetY));
        distance[RaidState.Cell(targetX, targetY)] = (byte)(IsSolid(state, targetX, targetY) ? 0 : 1);
        var reached = false;
        var free = new bool[RaidState.Facings + 1];
        do
        {
            var (x, y) = queue.Dequeue();
            // C: path_dist is a signed char
            var steps = (sbyte)distance[RaidState.Cell(x, y)];
            for (var facing = 1; facing <= 8; facing++)
            {
                var nx = state.Move8X[facing] + x;
                var ny = state.Move8Y[facing] + y;
                free[facing] = state.RoomAtCell[RaidState.Cell(nx, ny)] == room && !IsSolid(state, nx, ny)
                    // the even facings are the diagonals
                    && !(steps == 0 && (facing & 1) == 0);
            }
            free[0] = free[8];
            free[9] = free[1];
            for (var facing = 1; facing <= 8; facing++)
            {
                var diagonal = (facing & 1) == 0;
                if (!free[facing] || (diagonal && (!free[facing - 1] || !free[facing + 1])))
                {
                    continue;
                }
                var nx = state.Move8X[facing] + x;
                var ny = state.Move8Y[facing] + y;
                var cell = RaidState.Cell(nx, ny);
                if (distance[cell] == 0)
                {
                    distance[cell] = (byte)(steps + 1);
                    queue.Enqueue((nx, ny));
                    if (nx == guardX && ny == guardY)
                    {
                        reached = true;
                    }
                }
            }
        }
        while (!reached && queue.Count > 0);

        var guardCell = RaidState.Cell(guardX, guardY);
        if (distance[guardCell] == 0)
        {
            for (var side = 0; side < 4; side++)
            {
                var next = distance[RaidState.Cell(state.Mve4X[side] + guardX, state.Mve4Y[side] + guardY)];
                if (next != 0)
                {
                    distance[guardCell] = next;
                }
            }
        }
        return reached;
    }

    /// <summary>Whether the cell holds a piece that blocks walking (bare floor does not).</summary>
    private static bool IsSolid(RaidState state, int x, int y)
    {
        var tile = state.CellMap[RaidState.Cell(x, y)] & RaidState.CellTile;
        return tile != RaidState.CellTile && (state.Ob[tile].Bits & TileFlags.Solid) != 0;
    }

    /// <summary>
    /// Tries to bring guard <paramref name="guard"/> into the building, while guards are left and the slot is within
    /// the level's number (3, or 5 with the plot object present, plus 2 per difficulty level). In a kill raid he comes in
    /// through a random outer door that does not lead into the target room. Otherwise he appears on a random free floor
    /// cell of a room the player is not in, far enough from the player (the limit starts at (4 - difficulty) * 75 pixels
    /// and shrinks with each try; after the first frame only in the target or home room when there is a target room);
    /// with the plot object present in an ordinary raid, only in the target room, and not at all once the player is in
    /// it after frame 16.
    /// </summary>
    /// <remarks>
    /// After 999 failed tries he does not come; in a kill raid his position is left as the failed search made it (the
    /// pixel of cell -1).
    /// </remarks>
    /// <param name="state">The people, the map and the random generator.</param>
    /// <param name="guard">The slot, 1..8.</param>
    // orig: TAC 00e8:7155 spawn_guard (src/c/tac/tac_00e8_7155.c) matched; tests: GuardRulesTests, TacOracleTests
    public static void SpawnGuard(RaidState state, int guard)
    {
        if (GuardLimit(state) < guard || state.GuardsLeft == 0)
        {
            return;
        }
        var newcomer = state.Bg[guard];
        var tries = 0;
        if (state.GameMode == RaidState.KillMode)
        {
            tries = EnterThroughAnOuterDoor(state, newcomer);
        }
        else if (!state.MacHere || state.GameMode != 0)
        {
            var minimumDistance = (4 - state.Diff) * 75;
            bool rejected;
            do
            {
                PlaceAtRandom(state, newcomer);
                var distance = C16.Int(C16.Abs(C16.Int(newcomer.Px - state.Px)) + C16.Abs(C16.Int(newcomer.Py - state.Py)));
                // C: || stops at the first true term: the distance limit only shrinks when it is reached
                rejected = newcomer.Room == 0
                    || (state.InRoom == newcomer.Room && state.GameMode != RaidState.OneRoomMode)
                    || state.CellMap[RaidState.Cell(newcomer.Px / 16, newcomer.Py / 16)] != RaidState.CellEmpty
                    || (newcomer.Room != state.MacGuff && newcomer.Room != state.Guard && state.Rtime != 0 && state.MacGuff > 0)
                    || distance < minimumDistance--;
            }
            while (rejected && ++tries < Tries);
        }
        else
        {
            if (state.Rtime > 16 && state.InRoom == state.MacGuff)
            {
                return;
            }
            do
            {
                PlaceAtRandom(state, newcomer);
            }
            while ((newcomer.Room != state.MacGuff || state.CellMap[RaidState.Cell(newcomer.Px / 16, newcomer.Py / 16)] != RaidState.CellEmpty)
                && ++tries < Tries);
        }
        if (tries >= Tries)
        {
            return;
        }
        state.GuardsLeft--;
        newcomer.Status = ActorFlags.Active;
        newcomer.Destx = -1;
        newcomer.Pose = 0;
        newcomer.Dir = 1;
        newcomer.Fire = 0;
        newcomer.Pause = 0;
        newcomer.Mode = 0;
        newcomer.Sighting = 0;
        newcomer.Sighted = 0;
        newcomer.Stun = 0;
    }

    /// <summary>The highest guard slot of the level: 3, or 5 with the plot object present, plus 2 per difficulty level.</summary>
    private static int GuardLimit(RaidState state) => (state.MacHere ? 5 : 3) + (state.Diff << 1);

    /// <summary>A guard on the centre of a random cell, in the room of that cell (spawn_guard).</summary>
    private static void PlaceAtRandom(RaidState state, BadGuy guard)
    {
        guard.Px = (short)((GameLibrary.RandomBelow(state.Random, RaidState.MapW) << 4) + 8);
        guard.Py = (short)((GameLibrary.RandomBelow(state.Random, RaidState.MapH) << 4) + 8);
        guard.Room = state.RoomAtCell[RaidState.Cell(guard.Px / 16, guard.Py / 16)];
    }

    /// <summary>A kill raid's guard at a random door from the outside into a room other than the target room, facing in (spawn_guard); the tries used.</summary>
    private static int EnterThroughAnOuterDoor(RaidState state, BadGuy guard)
    {
        var tries = 0;
        // the position holds the door's cell until the search ends
        guard.Px = -1;
        do
        {
            var door = state.Dr[GameLibrary.RandomBelow(state.Random, state.Ndoor)];
            if (door.Room1 == 0 && door.Room2 != 0 && state.MacGuff != door.Room2)
            {
                guard.Room = door.Room2;
                guard.Px = (short)(door.X + state.Mve4X[door.Dir]);
                guard.Py = (short)(door.Y + state.Mve4Y[door.Dir]);
                guard.Dir = (short)(door.Dir * 2 + 1);
            }
            if (door.Room2 == 0 && door.Room1 != 0 && state.MacGuff != door.Room1)
            {
                guard.Room = door.Room1;
                guard.Px = door.X;
                guard.Py = door.Y;
                guard.Dir = (short)(((door.Dir + 2) & 3) * 2 + 1);
            }
        }
        while (guard.Px == -1 && ++tries < Tries);
        guard.Px = C16.Int((guard.Px << 4) + 8);
        guard.Py = C16.Int((guard.Py << 4) + 8);
        return tries;
    }

    /// <summary>
    /// Whether guard <paramref name="guard"/> notices the player: he must be in the player's room and have him in his
    /// line of sight (<see cref="LineOfSightToPlayer"/>, kept in <see cref="RaidState.GuardSees"/> either way), and,
    /// unless the alarm is in this very room, the player must not be behind him (three to five eighths of a turn away
    /// from where he faces).
    /// </summary>
    /// <param name="state">The people and the map.</param>
    /// <param name="guard">The guard's slot.</param>
    /// <returns>The line-of-sight answer when he notices the player, else 0.</returns>
    // orig: TAC 00e8:74ee guard_sees_player (src/c/tac/tac_00e8_74ee.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int GuardSeesPlayer(RaidState state, int guard)
    {
        var watcher = state.Bg[guard];
        state.GuardSees = 0;
        if (watcher.Room != state.InRoom)
        {
            return 0;
        }
        state.GuardSees = (short)LineOfSightToPlayer(state, guard);
        var turn = C16.Int(watcher.Dir - DirectionFromVector(C16.Int(state.Px - watcher.Px), C16.Int(state.Py - watcher.Py)));
        var behind = C16.Abs(turn) > 2 && C16.Abs(turn) < 6;
        if ((state.Eseen == 0 || watcher.Room != state.ToRoom) && behind)
        {
            return 0;
        }
        return state.GuardSees;
    }

    /// <summary>
    /// Whether guard <paramref name="guard"/> could see the player, looking past the furniture: a clear line sees him, a
    /// blocked one does not. Behind partial cover it depends: an unalert guard sees a player who is not sneaking (or has
    /// just fired); an alert guard never sees a quiet sneaking player; otherwise the cover counts, the far half of the
    /// line for a sneaking player and the near half else.
    /// </summary>
    /// <param name="state">The people and the map.</param>
    /// <param name="guard">The guard's slot.</param>
    /// <returns>Non-zero when he sees the player (1, or a cover bit: 1 or 2).</returns>
    // orig: TAC 00e8:757f line_of_sight_to_player (src/c/tac/tac_00e8_757f.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int LineOfSightToPlayer(RaidState state, int guard)
    {
        var watcher = state.Bg[guard];
        var sight = LineBlockedCount(state, watcher.Px, watcher.Py, state.Px, state.Py);
        if (sight == SightBlocked)
        {
            return 0;
        }
        if (sight == SightClear)
        {
            return 1;
        }
        var alert = (watcher.Status & ActorFlags.Sneak) != 0;
        var sneakingQuietly = state.Running && state.FireTimer == 0;
        if (!alert && !sneakingQuietly)
        {
            return 1;
        }
        if (alert && sneakingQuietly)
        {
            return 0;
        }
        return sneakingQuietly ? state.CoverBits & 2 : state.CoverBits & 1;
    }

    /// <summary>
    /// Follows the straight line from pixel (<paramref name="x1"/>, <paramref name="y1"/>) to (<paramref name="x2"/>,
    /// <paramref name="y2"/>) from cell edge to cell edge and tells what it passes: a tall piece blocks sight, a solid
    /// one gives partial cover. Also sets <see cref="RaidState.CoverBits"/>: bit 0 is cleared when a solid piece lies on
    /// the first half of the line (give or take 4 pixels), bit 1 when one lies on the second half.
    /// </summary>
    /// <remarks>
    /// The line is mirrored so that it always runs right and down. The slope is kept in small numbers: while a
    /// difference exceeds 100 both are halved.
    /// </remarks>
    /// <param name="state">The map; <see cref="RaidState.CoverBits"/> is set.</param>
    /// <param name="x1">From x, pixels.</param>
    /// <param name="y1">From y, pixels.</param>
    /// <param name="x2">To x, pixels.</param>
    /// <param name="y2">To y, pixels.</param>
    /// <returns><see cref="SightBlocked"/>, <see cref="SightCovered"/> or <see cref="SightClear"/>.</returns>
    // orig: TAC 00e8:7615 line_blocked_count (src/c/tac/tac_00e8_7615.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int LineBlockedCount(RaidState state, int x1, int y1, int x2, int y2)
    {
        // C: every value is a 16-bit int; the slope products below may wrap
        short fromX = C16.Int(x1), fromY = C16.Int(y1), toX = C16.Int(x2), toY = C16.Int(y2);
        var dx = C16.Int(toX - fromX);
        var dy = C16.Int(toY - fromY);
        var mirroredX = false;
        var mirroredY = false;
        var verdict = SightClear;
        state.CoverBits = 3;
        if (dx < 0)
        {
            dx = C16.Neg(dx);
            fromX = C16.Int(MapRightPixel - fromX);
            toX = C16.Int(MapRightPixel - toX);
            mirroredX = true;
        }
        if (dy < 0)
        {
            dy = C16.Neg(dy);
            fromY = C16.Int(MapBottomPixel - fromY);
            toY = C16.Int(MapBottomPixel - toY);
            mirroredY = true;
        }
        var x = fromX;
        var y = fromY;
        var length = C16.Int(toX - fromX + toY - fromY);
        while (dx > SlopeLimit || dy > SlopeLimit)
        {
            dx = C16.Div(dx, 2);
            dy = C16.Div(dy, 2);
        }
        var half = C16.Div(length, 2);
        while (true)
        {
            // the next vertical cell edge the line crosses, and the next horizontal one
            short edgeX, atEdgeX, edgeY, atEdgeY;
            if (dx != 0)
            {
                edgeX = C16.Int((x & ~15) + 0x10);
                atEdgeX = C16.Int(C16.Div(C16.Int((edgeX - fromX) * dy), dx) + fromY);
            }
            else
            {
                edgeX = Far;
                atEdgeX = Far;
            }
            if (dy != 0)
            {
                edgeY = C16.Int((y & ~15) + 0x10);
                atEdgeY = C16.Int(C16.Div(C16.Int((edgeY - fromY) * dx), dy) + fromX);
            }
            else
            {
                atEdgeY = Far;
                edgeY = Far;
            }
            // step to the nearer crossing
            if (C16.Int(edgeX + atEdgeX) < C16.Int(edgeY + atEdgeY))
            {
                (x, y) = (edgeX, atEdgeX);
            }
            else
            {
                (x, y) = (atEdgeY, edgeY);
            }
            if (x > toX || y > toY)
            {
                return verdict;
            }
            var column = C16.Sar(x, 4);
            var row = C16.Sar(y, 4);
            if (mirroredX)
            {
                column = C16.Int(RaidState.MapW - 1 - column);
            }
            if (mirroredY)
            {
                row = C16.Int(RaidState.MapH - 1 - row);
            }
            var tile = state.CellMap[RaidState.Cell(column, row)] & RaidState.CellTile;
            if (tile == RaidState.CellTile)
            {
                continue;
            }
            var bits = state.Ob[tile].Bits;
            if ((bits & TileFlags.Tall) != 0)
            {
                return SightBlocked;
            }
            if ((bits & TileFlags.Solid) == 0)
            {
                continue;
            }
            verdict = SightCovered;
            var along = C16.Int(x - fromX + y - fromY);
            if (along < half + 4)
            {
                state.CoverBits &= ~1;
            }
            if (along > half - 4)
            {
                state.CoverBits &= ~2;
            }
        }
    }

    /// <summary>
    /// How guard <paramref name="guard"/> behaves, for the choice of his next room. In a kill raid every guard goes to
    /// the alarm. A guard beyond the level's number who is not knocked out leaves the building (and is counted as a guard
    /// left to come). With an alarm, guards 3 and up go to it and the first two patrol. Otherwise the first
    /// <c>alert / 25 + 2</c> guards patrol, the next two go where nobody went for longest, and the rest hunt at night when
    /// the plot object is not here (else patrol).
    /// </summary>
    /// <param name="state">The people and the alarm; <see cref="RaidState.GuardsLeft"/> may grow.</param>
    /// <param name="guard">The guard's slot, 1..8.</param>
    /// <returns><see cref="Patrol"/>, <see cref="Respond"/>, <see cref="Hunt"/> or <see cref="Withdraw"/>.</returns>
    // orig: TAC 00e8:7821 guard_behaviour (src/c/tac/tac_00e8_7821.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int GuardBehaviour(RaidState state, int guard)
    {
        if (state.GameMode == RaidState.KillMode)
        {
            return Respond;
        }
        // DORMANT: the original sets the guard's x to 1 after this return; the statement is never reached
        if (GuardLimit(state) < guard && state.Bg[guard].Stun == 0)
        {
            state.GuardsLeft++;
            return Withdraw;
        }
        if (state.Eseen != 0 && state.ToRoom > 0)
        {
            return guard >= 3 ? Respond : Patrol;
        }
        // C: 16-bit division
        var alertGuards = C16.Div(state.SharedAlert, 0x19);
        if (alertGuards + 3 > guard)
        {
            return Patrol;
        }
        if (alertGuards + 5 > guard)
        {
            return Respond;
        }
        return state.Night && !state.MacHere ? Hunt : Patrol;
    }

    /// <summary>
    /// The facing 1..8 of the vector (<paramref name="dx"/>, <paramref name="dy"/>) by its quadrant and the nearer axis
    /// (no band for "straight along an axis").
    /// </summary>
    /// <param name="dx">The x difference.</param>
    /// <param name="dy">The y difference (down is positive).</param>
    /// <returns>The facing, 1 north .. 8 north-west, clockwise.</returns>
    // orig: TAC 00e8:78b2 direction_from_vector_coarse (src/c/tac/tac_00e8_78b2.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int DirectionFromVectorCoarse(int dx, int dy)
    {
        // C: 16-bit abs (abs(-32768) stays negative)
        var mostlyAcross = C16.Abs(C16.Int(dx)) > C16.Abs(C16.Int(dy));
        if (dx > 0)
        {
            if (dy > 0)
            {
                return mostlyAcross ? 3 : 4;
            }
            return mostlyAcross ? 2 : 1;
        }
        if (dy > 0)
        {
            return mostlyAcross ? 6 : 5;
        }
        return mostlyAcross ? 7 : 8;
    }

    /// <summary>
    /// The facing 1..8 of the vector (<paramref name="dx"/>, <paramref name="dy"/>): straight along an axis when the
    /// other part is less than half of it, else diagonal. A zero vector gives 8.
    /// </summary>
    /// <param name="dx">The x difference.</param>
    /// <param name="dy">The y difference (down is positive).</param>
    /// <returns>The facing, 1 north .. 8 north-west, clockwise.</returns>
    // orig: TAC 00e8:792e direction_from_vector (src/c/tac/tac_00e8_792e.c) matched; tests: GuardRulesTests, TacOracleTests
    public static int DirectionFromVector(int dx, int dy)
    {
        // C: 16-bit abs and doubling
        var absX = C16.Abs(C16.Int(dx));
        var absY = C16.Abs(C16.Int(dy));
        var horizontal = C16.Int(absY << 1) < absX;
        var vertical = C16.Int(absX << 1) < absY;
        if (dx > 0)
        {
            if (dy > 0)
            {
                return horizontal ? 3 : vertical ? 5 : 4;
            }
            return horizontal ? 3 : vertical ? 1 : 2;
        }
        if (dy > 0)
        {
            return horizontal ? 7 : vertical ? 5 : 6;
        }
        return horizontal ? 7 : vertical ? 1 : 8;
    }

    /// <summary>
    /// Finds the cell the player acts on (photographs, opens, plants a bug on): the cell 3 pixel steps ahead of him when
    /// it holds a piece (a tile above the walls) inside his room's rectangle, else his own cell.
    /// </summary>
    /// <param name="state">The player and the map; <see cref="RaidState.FrontCellX"/> and <see cref="RaidState.FrontCellY"/> are set.</param>
    // orig: TAC 00e8:2fc9 find_front_cell (src/c/tac/tac_00e8_2fc9.c) matched; tests: GuardRulesTests, TacOracleTests
    public static void FindFrontCell(RaidState state)
    {
        // C: 16-bit divisions
        var x = C16.Div(C16.Int(3 * state.MoveX[state.PlayerFacing] + state.Px), 16);
        var y = C16.Div(C16.Int(3 * state.MoveY[state.PlayerFacing] + state.Py), 16);
        var cell = state.CellMap[RaidState.Cell(x, y)];
        var room = state.Rm[state.InRoom];
        const int LastWallTile = 7;
        if (cell != RaidState.CellEmpty && (cell & RaidState.CellTile) > LastWallTile
            && x >= room.X && room.Dx + room.X > x && y >= room.Y && room.Dy + room.Y > y)
        {
            state.FrontCellX = x;
            state.FrontCellY = y;
        }
        else
        {
            state.FrontCellX = state.Cx;
            state.FrontCellY = state.Cy;
        }
    }

    /// <summary>
    /// The number of guards awake in the current pass over the guards (all of them "eliminated" when 0). The original
    /// keeps it in element 0 of the campaign's per-city activity counters, which go back to the campaign at the end of the
    /// raid; with the fix the raid keeps it in a variable of its own.
    /// </summary>
    /// <param name="state">The raid.</param>
    /// <param name="behaviour">Whether CA-1 is fixed.</param>
    /// <returns>The count.</returns>
    // BUG: CA-1 the original counts the awake guards in city 0's activity counter; OriginalBehaviour.FixRaidGuardCountInCityCounter
    public static short AwakeGuardCount(RaidState state, OriginalBehaviour behaviour) =>
        behaviour.FixRaidGuardCountInCityCounter ? state.AwakeGuards : state.Active[0];

    /// <summary>Sets the count of <see cref="AwakeGuardCount"/>.</summary>
    /// <param name="state">The raid.</param>
    /// <param name="behaviour">Whether CA-1 is fixed.</param>
    /// <param name="count">The count.</param>
    // BUG: CA-1 the original counts the awake guards in city 0's activity counter; OriginalBehaviour.FixRaidGuardCountInCityCounter
    public static void SetAwakeGuardCount(RaidState state, OriginalBehaviour behaviour, short count)
    {
        if (behaviour.FixRaidGuardCountInCityCounter)
        {
            state.AwakeGuards = count;
        }
        else
        {
            state.Active[0] = count;
        }
    }
}