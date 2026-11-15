using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The guards' turn, the grenades thrown and the traps laid, and the raid's message box.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>Where the trap menu is shown.</summary>
    private const int TrapMenuX = 0x1e;
    private const int TrapMenuY = 0x32;

    private const int SoundTrapLaid = 0xb;

    /// <summary>A guard counts as awake below this knocked-out time, out of the raid for good from the next.</summary>
    private const int AwakeStun = 0x18;
    private const int OutStun = 0x3e7;

    /// <summary>Above this knocked-out time a guard lies on the floor and does nothing.</summary>
    private const int LyingStun = 0x20;

    /// <summary>The tile bits a patrolling guard goes to look at: terminals, doors, photographs and buggable pieces.</summary>
    private const TileFlags WorthALook = TileFlags.Terminal | TileFlags.Door | TileFlags.Photo | TileFlags.Buggable;

    /// <summary>Shows a message box with <paramref name="text"/> at (<paramref name="x"/>, <paramref name="y"/>) and waits for the player's choice.</summary>
    /// <param name="text">The box's text: lines ended by line feeds, the choices after the first.</param>
    /// <param name="x">Screen x of the box.</param>
    /// <param name="y">Screen y of the box.</param>
    /// <returns>The choice (the library's <see cref="Library.LibraryMenus.QuitPrompt(string, int, int)"/>).</returns>
    // orig: TAC 00e8:34c0 show_message_box (src/c/tac/tac_00e8_34c0.c) matched; tests: TacOracleTests
    public int ShowMessageBox(TextBuffer text, int x, int y)
    {
        var choice = Menus.QuitPrompt(text, x, y);
        // the screen's background pen is set back to blue
        state.Rp1.BPen = Blue;
        return choice;
    }

    /// <summary>
    /// Throws a grenade or lays a trap. A grenade takes the first free slot at (<paramref name="x"/>, <paramref name="y"/>)
    /// (the player's starts a step to his right), flies 10 frames per unit of <paramref name="power"/> and sounds. With
    /// power -1 the player lays a trap: a menu offers a remote-controlled trap or a booby trap (or cancel), either of which
    /// uses a grenade and shows on the radar. The status panel is drawn again after the player's throw.
    /// </summary>
    /// <remarks>Leaving the trap menu with Escape keeps the slot with a time the slot had before: a quirk kept as it is.</remarks>
    /// <param name="x">Pixel x of the thrower.</param>
    /// <param name="y">Pixel y of the thrower.</param>
    /// <param name="dir">The facing it flies in.</param>
    /// <param name="power">1..3, or -1 for a trap.</param>
    /// <param name="weapon">The grenade kind, plus <see cref="Grenade.ThrownByGuard"/> for a guard's.</param>
    // orig: TAC 00e8:2553 fire_projectile (src/c/tac/tac_00e8_2553.c) matched; tests: TacOracleTests
    public void FireProjectile(int x, int y, int dir, int power, int weapon)
    {
        foreach (var grenade in state.Gr)
        {
            if (grenade.Type != Grenade.Free)
            {
                continue;
            }
            if ((weapon & Grenade.ThrownByGuard) == 0 && power != -1)
            {
                var right = ((dir + 2) & 7) + 1;
                x += state.MoveX[right];
                y += state.MoveY[right];
            }
            grenade.Px = C16.Int(x);
            grenade.Py = C16.Int(y);
            grenade.Dir = C16.Int(dir);
            grenade.Type = C16.Int(weapon);
            if (power != -1)
            {
                grenade.Time = C16.Int(10 * power);
                DoSound(SoundThrow);
            }
            else
            {
                LayTrap(grenade, weapon);
            }
            break;
        }
        if ((weapon & Grenade.ThrownByGuard) == 0)
        {
            DrawStatusPanel();
        }
    }

    /// <summary>The trap menu of fire_projectile; Num Lock is off while it shows.</summary>
    private void LayTrap(Grenade grenade, int weapon)
    {
        const int Cancel = 0, Remote = 1, Booby = 2;
        SaveScreen();
        world.Input.SetNumLock(false);
        state.MessageText.Text = state.StrSetTrap;
        var choice = ShowMessageBox(state.MessageText, TrapMenuX, TrapMenuY);
        world.Input.SetNumLock(true);
        if (choice > 0)
        {
            DoSound(SoundTrapLaid);
            state.Grenades[weapon]--;
        }
        switch (choice)
        {
            case Cancel:
                grenade.Type = Grenade.Free;
                break;
            case Remote:
                grenade.Time = Grenade.RemoteTrap;
                break;
            case Booby:
                grenade.Time = Grenade.BoobyTrap;
                break;
        }
        RestoreFullScreen();
        if (choice is Remote or Booby)
        {
            DrawMinimap(RadarX, RadarY);
        }
    }

    /// <summary>
    /// One guard's turn (slot <paramref name="guard"/>, 1..8, once a frame each). The first guard of a pass starts the
    /// pass's counts (and at frame 0 works out the distances to the home and target rooms). An empty slot may get a new
    /// guard every 16 frames. A guard then counts himself (awake, in the alarm room ...), looks for the player (he stares
    /// at a disguised player; an undisguised one he watches, and past a limit raises the alarm and shoots), lies down while
    /// knocked out, finds a body, picks a cell to go to when he has none (near where the player was seen when he hunts in
    /// the alarm room; a piece worth a look, or the door to the next room, when he patrols), picks up a booby trap he
    /// stands on, walks one step along his way (turning first), and at the cell's centre waits, opens what he came to
    /// open, may throw a grenade where the player was seen, and when his wait is over shuts it again or steps through the
    /// door into the next room. A guard who walks off the building leaves the raid.
    /// </summary>
    /// <param name="guard">The guard's slot, 1..8.</param>
    // orig: TAC 00e8:5ca6 update_guard (src/c/tac/tac_00e8_5ca6.c) matched; tests: TacOracleTests
    // BUG: CA-1 the count of awake guards is kept in city 0's activity counter; OriginalBehaviour.FixRaidGuardCountInCityCounter
    public void UpdateGuard(int guard)
    {
        if (guard == 1)
        {
            StartGuardPass();
        }
        var me = state.Bg[guard];
        if ((me.Status & ActorFlags.Active) == 0 && (state.Rtime & 0xf) == 0)
        {
            MaybeSpawn(guard);
        }
        if ((me.Status & ActorFlags.Active) == 0)
        {
            return;
        }
        CountGuard(me);
        var cellX = C16.Div(me.Px, 16);
        var cellY = C16.Div(me.Py, 16);
        WatchPlayer(guard, me);
        if (!RecoverAndAlert(guard, me))
        {
            return;
        }
        if (me.Sighting != 0)
        {
            FacePlayer(me);
        }
        var room = (int)state.RoomAtCell[RaidState.Cell(cellX, cellY)];
        if (room == 0)
        {
            LeaveRaid(me);
            return;
        }
        if ((state.Rm[room].Status & RoomStatus.Bodies) != 0)
        {
            state.Rm[room].Status &= ~RoomStatus.Bodies;
            if (state.Eseen == 0)
            {
                GuardRules.RaiseAlert(state, room, CrimeAlarm);
            }
            else
            {
                state.Eseen = CrimeAlarm;
            }
        }
        var hunting = me.Room == state.ToRoom && state.Eseen != 0;
        if (me.Destx == -1 && me.Pause == 0 && me.Fire == 0 && !ChooseDestination(guard, me, room, cellX, cellY, hunting))
        {
            return;
        }
        if (me.Stun == 0)
        {
            PickUpBoobyTrap(me);
        }
        if (me.Pause == 0 && me.Fire == 0)
        {
            Walk(guard, me, cellX, cellY, hunting);
        }
        if (me.Pause != 0 && --me.Pause == 0)
        {
            EndWait(me, cellX, cellY);
        }
    }

    /// <summary>The first guard of a pass: the last pass's alarm count kept, the counts cleared (update_guard).</summary>
    private void StartGuardPass()
    {
        state.AlarmGuardsPrev = state.AlarmGuards;
        state.AlarmGuards = 0;
        state.MaxSeen = 0;
        state.GuardsNotGone = 0;
        GuardRules.SetAwakeGuardCount(state, behaviour, 0);
        state.GuardInRoom = false;
        if (state.Rtime == 0)
        {
            // the doors from the guards' home room, kept for the hunters; then the alarm distances from the target room
            GuardRules.RaiseAlert(state, state.Guard, 0);
            for (var room = 0; room < RaidState.MaxRooms; room++)
            {
                state.ExitDist[room] = state.Rm[room].ToDist;
            }
            GuardRules.RaiseAlert(state, state.MacGuff, 0);
        }
        if (state.PlayerActed && state.Who != -1 && state.InRoom == state.MacGuff)
        {
            GuardRules.RaiseAlert(state, state.MacGuff, CrimeAlarm);
        }
    }

    /// <summary>
    /// An empty slot may get a guard on every 16th frame: outside kill raids with a chance that grows with the alarm (and
    /// always at frame 0); in kill raids, or with the plot object here, the slot whose number matches the frame (update_guard).
    /// </summary>
    private void MaybeSpawn(int guard)
    {
        // C: the random draw comes first, then the frame test
        var spawn = state.GameMode != RaidState.KillMode
            && (GameLibrary.RandomBelow(state.Random, C16.Div(1000, C16.Int(state.Diff + 1))) < state.Eseen || state.Rtime == 0);
        if (!spawn && (state.GameMode == RaidState.KillMode || state.MacHere))
        {
            spawn = (guard & 7) == (C16.Sar(state.Rtime, 5) & 7);
        }
        if (spawn)
        {
            GuardRules.SpawnGuard(state, guard);
        }
    }

    /// <summary>The pass's counts of a guard: not gone, awake, in the alarm room; a kill raid fails once an awake guard reaches the target room the player left (update_guard).</summary>
    private void CountGuard(BadGuy me)
    {
        if (me.Stun < OutStun)
        {
            state.GuardsNotGone++;
        }
        if (me.Stun < AwakeStun)
        {
            GuardRules.SetAwakeGuardCount(state, behaviour, C16.Int(GuardRules.AwakeGuardCount(state, behaviour) + 1));
        }
        if (me.Room == state.ToRoom && (me.Stun < OutStun || state.Diff < 2))
        {
            state.AlarmGuards++;
            if (state.AlarmGuards >= 2 && state.ToRoom != state.InRoom)
            {
                // BUG: CA-30 outside a kill raid this makes the alarm room -1
                GuardRules.RaiseAlert(state, -1, 0);
            }
        }
        if (state.GameMode == RaidState.KillMode && me.Room == state.MacGuff && me.Stun < AwakeStun && state.InRoom != state.MacGuff)
        {
            state.LevelDone = 1;
        }
    }

    /// <summary>
    /// Whether the guard sees the player, and what he makes of it: a disguised player is stared at (and found out when
    /// stared at long, or when he faces the guard); an undisguised one is watched, and his place noted (update_guard).
    /// </summary>
    private void WatchPlayer(int guard, BadGuy me)
    {
        // C: && stops early, but the sight test (which sets guard_sees) is its first term and always made
        if (GuardRules.GuardSeesPlayer(state, guard) != 0 && me.Stun < AwakeStun && state.GameMode != 1)
        {
            if (state.Disguised)
            {
                var player = state.Bg[0];
                var facing = GuardRules.DirectionFromVector(C16.Int(me.Px - player.Px), C16.Int(me.Py - player.Py));
                var turn = C16.Abs(C16.Int(facing - player.Dir));
                if (C16.Div(256, C16.Int(state.Diff + 1)) < ++state.DisguiseStare || turn < 2 || turn > 6)
                {
                    me.Sighting = C16.Int(me.Sighting + 2);
                }
                else
                {
                    me.Sighting = 0;
                }
            }
            else
            {
                me.Sighting++;
                me.Status &= ~ActorFlags.Leaving;
                state.AlertCellX = C16.Div(state.Px, 16);
                state.AlertCellY = C16.Div(state.Py, 16);
                me.Pause = 1;
            }
        }
        else
        {
            me.Sighting = 0;
        }

        if (state.GuardSees != 0)
        {
            if (state.PlayerActed && me.Stun < AwakeStun)
            {
                state.Disguised = false;
                GuardRules.RaiseAlert(state, state.InRoom, CrimeAlarm);
            }
            me.Sighted++;
            if (me.Sighted > state.MaxSeen && me.Stun < OutStun)
            {
                state.MaxSeen = me.Sighted;
            }
        }
        else
        {
            me.Sighted = 0;
        }

        if (me.Room == state.InRoom)
        {
            if (me.Stun < OutStun)
            {
                state.GuardInRoom = true;
            }
            if (state.GuardSees != 0 || me.Fire != 0 || state.Eseen == 0 || state.ToRoom != state.InRoom)
            {
                me.Status &= ~ActorFlags.Alert;
            }
            else if ((state.Rtime & 3) == 0)
            {
                me.Status |= ActorFlags.Alert;
            }
        }
    }

    /// <summary>
    /// Gas keeps three of four guards down longer; the knocked-out time runs down; a guard in the alarm room with the
    /// player turns alert every eighth frame unless where the player was seen is well out of his sight (update_guard).
    /// </summary>
    /// <returns>False while the guard lies on the floor: his turn ends.</returns>
    private bool RecoverAndAlert(int guard, BadGuy me)
    {
        if ((state.Rm[me.Room].Status & RoomStatus.Gas) != 0 && (guard & 3) != 0)
        {
            me.Stun = C16.Int(me.Stun + 2);
        }
        if (me.Stun != 0)
        {
            me.Stun--;
            if (me.Stun > LyingStun)
            {
                me.Sighting = 0;
                me.Status |= ActorFlags.Sneak;
                return false;
            }
        }
        if (me.Room == state.ToRoom && state.ToRoom == state.InRoom && state.Eseen != 0 && (state.Rtime & 7) == 0)
        {
            me.Status |= ActorFlags.Sneak;
            if (state.AlertCellX != 0 && SightToAlertCell(me.Px, me.Py) >= GuardRules.SightClear)
            {
                me.Status &= ~ActorFlags.Sneak;
            }
        }
        return true;
    }

    /// <summary>
    /// The sight line from a pixel to the centre of the cell where the player was last seen, plus 1 when its first half
    /// is free of cover (update_guard).
    /// </summary>
    private int SightToAlertCell(int x, int y)
    {
        // C: the call comes first, then the cover bits it set are read (checked against asm: 5ca6, 60aa and 62dc)
        var sight = GuardRules.LineBlockedCount(state, x, y, (state.AlertCellX << 4) + 8, (state.AlertCellY << 4) + 8);
        return sight + (state.CoverBits & 1);
    }

    /// <summary>A guard who watches the player turns to him; past the difficulty's limit he raises the alarm and shoots (update_guard).</summary>
    private void FacePlayer(BadGuy me)
    {
        var facing = GuardRules.DirectionFromVector(C16.Int(state.Px - me.Px), C16.Int(state.Py - me.Py));
        if (facing != 0)
        {
            me.Dir = (short)facing;
        }
        if (me.Room == state.InRoom && (5 - state.Diff) * 7 - 6 < me.Sighting)
        {
            GuardRules.RaiseAlert(state, state.InRoom, CrimeAlarm);
            state.RoomLastVisit[state.InRoom] = 0;
            if (me.Fire == 0)
            {
                me.Fire += 4;
            }
            me.Pause = 9;
            me.Status &= ~ActorFlags.Sneak;
        }
    }

    /// <summary>The guard leaves the raid; a guard more may come (update_guard).</summary>
    private void LeaveRaid(BadGuy me)
    {
        me.Status &= ~ActorFlags.Active;
        state.GuardsLeft++;
    }

    /// <summary>
    /// The cell the guard goes to next, and his way there (update_guard). A hunter in the alarm room tries 16 (up to 32)
    /// random free cells and takes the nearest to both him and where the player was seen that has that place in sight. A
    /// patroller looks for a piece worth a look (more tries in a valuable room, fewer with an alert organisation and a
    /// violent raid); with none, with an alarm elsewhere, or in gas, he heads for the door to the next room; a hunter
    /// with nothing to look at in the guards' home room without an alarm wanders in it instead.
    /// </summary>
    /// <returns>False when the guard cannot get there from outside the player's room: he has left the raid.</returns>
    private bool ChooseDestination(int guard, BadGuy me, int room, int cellX, int cellY, bool hunting)
    {
        var record = state.Rm[room];
        int targetX = -1, targetY = 0;
        if (hunting)
        {
            var best = 0x3e7;  // farther than any cell
            var tries = 0;
            do
            {
                var x = record.X + GameLibrary.RandomBelow(state.Random, record.Dx);
                var y = record.Y + GameLibrary.RandomBelow(state.Random, record.Dy);
                var free = state.CellMap[RaidState.Cell(x, y)] == RaidState.CellEmpty;
                // C: 16-bit sums of distances
                var cost = C16.Int(Math.Abs(x - state.AlertCellX) + Math.Abs(y - state.AlertCellY) + Math.Abs(x - cellX) + Math.Abs(y - cellY));
                if (free && cost < best && SightFromCellToAlertCell(x, y) < GuardRules.SightClear)
                {
                    (targetX, targetY, best) = (x, y, cost);
                }
                tries++;
            }
            while (tries < 16 || (tries < 32 && targetX == -1));
        }
        else
        {
            var found = false;
            var tries = 0;
            do
            {
                targetX = record.X + GameLibrary.RandomBelow(state.Random, record.Dx);
                targetY = record.Y + GameLibrary.RandomBelow(state.Random, record.Dy);
                var tile = state.CellMap[RaidState.Cell(targetX, targetY)];
                if (tile != RaidState.CellEmpty && (state.TileType(tile & RaidState.CellTile).Bits & WorthALook) != 0)
                {
                    found = true;
                }
            }
            // C: 16-bit sum; the room's worth less the organisation's alertness / 16 less twice the violence
            while (!found && C16.Int(state.Rms[record.Type].Intel - C16.Div(state.SharedAlert, 16) - state.Alarm * 2) > ++tries);
            if (!found || (state.Eseen != 0 && room != state.ToRoom) || (record.Status & RoomStatus.Gas) != 0)
            {
                var next = GuardRules.ChooseNextRoom(state, room, guard);
                BuildingRules.FindDoorCell(state, room, next, ref targetX, ref targetY);
                me.Status |= ActorFlags.Leaving;
            }
            if (!found && GuardRules.GuardBehaviour(state, guard) == GuardRules.Hunt && me.Room == state.Guard && state.Eseen == 0)
            {
                targetX = record.X + GameLibrary.RandomBelow(state.Random, record.Dx);
                targetY = record.Y + GameLibrary.RandomBelow(state.Random, record.Dy);
                me.Status &= ~ActorFlags.Leaving;
            }
        }
        if (targetX == -1)
        {
            (targetX, targetY) = (cellX, cellY);
        }
        GuardRules.BuildPathMap(state, guard, room, cellX, cellY, targetX, targetY);
        if (state.PathDist[guard][RaidState.Cell(cellX, cellY)] == 0 && me.Room != state.InRoom)
        {
            LeaveRaid(me);
            return false;
        }
        me.Destx = (short)targetX;
        me.Desty = (short)targetY;
        me.Room = (short)room;
        return true;
    }

    /// <summary>The sight line from a cell's centre to where the player was seen, plus the near-half cover bit (update_guard).</summary>
    private int SightFromCellToAlertCell(int x, int y) => SightToAlertCell((x << 4) + 8, (y << 4) + 8);

    /// <summary>A guard on his feet near a booby trap in his room sets it off (update_guard).</summary>
    private void PickUpBoobyTrap(BadGuy me)
    {
        foreach (var trap in state.Gr)
        {
            if (trap.Type != Grenade.Free && trap.Time == Grenade.BoobyTrap
                && C16.Int(C16.Abs(C16.Int(trap.Px - me.Px)) + C16.Abs(C16.Int(trap.Py - me.Py))) < 0x18
                && state.RoomAtCell[RaidState.Cell(trap.Px / 16, trap.Py / 16)] == me.Room)
            {
                trap.Time = Grenade.Burst;
                break;
            }
        }
    }

    /// <summary>
    /// One step of the guard along his way: towards the neighbour one step nearer his target (straight steps preferred),
    /// first turning an eighth at a time; at the cell's centre he faces his target and waits (update_guard).
    /// </summary>
    private void Walk(int guard, BadGuy me, int cellX, int cellY, bool hunting)
    {
        var distance = state.PathDist[guard];
        // C: path_dist is a signed char
        var here = (sbyte)distance[RaidState.Cell(cellX, cellY)];
        var lowest = OutStun;
        var step = 0;
        for (var facing = 0; facing <= 8; facing++)
        {
            var there = (sbyte)distance[RaidState.Cell(state.Move8X[facing] + cellX, state.Move8Y[facing] + cellY)];
            if (there != 0 && (here - 1 == there || (here == 0 && there < lowest)) && (step == 0 || (facing & 1) != 0))
            {
                step = facing;
                lowest = there;
            }
        }
        var aimX = ((state.Move8X[step] + cellX) << 4) + 8;
        var aimY = ((state.Move8Y[step] + cellY) << 4) + 8;
        // C: 16-bit differences halved
        var towardsX = C16.Div(C16.Int(aimX - me.Px), 2);
        var towardsY = C16.Div(C16.Int(aimY - me.Py), 2);
        var heading = FacingOfSigns(towardsX, towardsY);
        if (heading != 0)
        {
            if (me.Dir == heading)
            {
                var sneaking = (me.Status & ActorFlags.Sneak) != 0;
                me.Px = C16.Int(me.Px + (sneaking ? state.Move8X[heading] : state.MoveX[heading]));
                me.Py = C16.Int(me.Py + (sneaking ? state.Move8Y[heading] : state.MoveY[heading]));
            }
            else
            {
                // turn an eighth the shorter way round
                me.Dir = (short)(((heading - me.Dir) & 7) < 4 ? me.Dir + 1 : me.Dir - 1);
                me.Dir = (short)(((me.Dir - 1) & 7) + 1);
                if (me.Dir != heading)
                {
                    me.Status &= ~ActorFlags.Sneak;
                }
            }
        }
        me.Pose = (short)((me.Pose + 1) & 3);
        if (C16.Abs(towardsX) < 2 && C16.Abs(towardsY) < 2)
        {
            ArriveAtCellCentre(me, cellX, cellY, hunting);
        }
    }

    /// <summary>The facing of a step by the signs of its parts (the table addressed from its centre).</summary>
    private int FacingOfSigns(int dx, int dy) =>
        state.DirFromSigns[(GameLibrary.Sign(dx) + 1) * 3 + GameLibrary.Sign(dy) + 1];

    /// <summary>
    /// At a cell's centre: the guard faces his target (or, on it, away from the wall of its tile). A hunter forgets his
    /// target, may throw a grenade where the player was seen when it is in line and 32 to 112 pixels away (not more often
    /// than the difficulty allows), and waits. A patroller waits longer, and opens what he came to open; one leaving the
    /// room may throw a stun grenade into an alarm room ahead of him, and turns round to face the door (update_guard).
    /// </summary>
    private void ArriveAtCellCentre(BadGuy me, int cellX, int cellY, bool hunting)
    {
        var heading = FacingOfSigns(me.Destx - cellX, me.Desty - cellY);
        me.Dir = heading != 0 ? (short)heading : (short)(((state.CellMap[RaidState.Cell(cellX, cellY)] & 3) << 1) + 1);
        if (hunting)
        {
            me.Destx = -1;
            var dx = C16.Int((state.AlertCellX << 4) - me.Px);
            var dy = C16.Int((state.AlertCellY << 4) - me.Py);
            if (C16.Abs(dx) < 12 || C16.Abs(dy) < 12 || C16.Abs(C16.Int(C16.Abs(dx) - C16.Abs(dy))) < 12)
            {
                var facing = GuardRules.DirectionFromVector(dx, dy);
                var distance = BuildingRules.ApproxDistance(dx, dy);
                if (distance > 0x20 && distance < 0x70 && C16.Int(state.Rtime - state.LastSearchFrame) > (5 - state.Diff) << 3)
                {
                    FireProjectile(me.Px, me.Py, facing, C16.Div(C16.Int(distance + 16), 32), Grenade.ThrownByGuard);
                    state.LastSearchFrame = state.Rtime;
                }
            }
            me.Pause = C16.Int(GameLibrary.RandomBelow(state.Random, 8) + (4 - state.Diff) * 4);
        }
        else
        {
            me.Pause = C16.Int((4 - state.Diff) * 6 + GameLibrary.RandomBelow(state.Random, 8));
            me.Pose = 0;
            // C: the original reads the cell before it tests the target; with no target the value is not used
            if (me.Destx != -1 && OpenableAt(me.Destx, me.Desty) is { } kind)
            {
                if (me.Room == state.InRoom && (kind.Bits & TileFlags.Door) == 0)
                {
                    state.OpenDoorCount++;
                }
                state.CellMap[RaidState.Cell(me.Destx, me.Desty)] |= RaidState.CellOpen;
                RedrawCell(me.Destx, me.Desty);
                if ((me.Status & ActorFlags.Leaving) == 0)
                {
                    me.Pause += 0x12;
                }
            }
        }
        if ((me.Status & ActorFlags.Leaving) != 0)
        {
            // C: && stops early: the room ahead is read last
            if (state.CampaignDiff > 1 && state.Eseen != 0 && state.AlarmGuardsPrev == 0
                && state.RoomAtCell[RaidState.Cell(state.Move8X[me.Dir] + cellX, state.Move8Y[me.Dir] + cellY)] == state.ToRoom)
            {
                me.Pause = (short)(GameLibrary.RandomBelow(state.Random, 3) * 5 + 9);
                FireProjectile(me.Px, me.Py, me.Dir, me.Pause / 5, Grenade.ThrownByGuard + Grenade.Stun);
            }
            me.Dir = (short)(((me.Dir + 3) & 7) + 1);
        }
    }

    /// <summary>The kind of the openable piece on a cell, or null (bare floor or a piece that does not open).</summary>
    private ObjectType? OpenableAt(int x, int y)
    {
        var tile = state.CellMap[RaidState.Cell(x, y)];
        if (tile == RaidState.CellEmpty)
        {
            return null;
        }
        var kind = state.TileType(tile & RaidState.CellTile);
        return (kind.Bits & TileFlags.Openable) != 0 ? kind : null;
    }

    /// <summary>
    /// The guard's wait is over: what he opened he shuts, he has no target any more, and a guard leaving his room steps
    /// through the door into the next room, where he looks about him (update_guard).
    /// </summary>
    private void EndWait(BadGuy me, int cellX, int cellY)
    {
        if (me.Destx != -1 && OpenableAt(me.Destx, me.Desty) is { } kind)
        {
            if (me.Room == state.InRoom && (kind.Bits & TileFlags.Door) == 0)
            {
                state.OpenDoorCount--;
            }
            state.CellMap[RaidState.Cell(me.Destx, me.Desty)] &= unchecked((byte)~RaidState.CellOpen);
            RedrawCell(me.Destx, me.Desty);
        }
        me.Destx = -1;
        if ((me.Status & ActorFlags.Leaving) == 0)
        {
            return;
        }
        for (var side = 0; side < 4; side++)
        {
            var x = state.Mve4X[side] + cellX;
            var y = state.Mve4Y[side] + cellY;
            var beyond = state.RoomAtCell[RaidState.Cell(x, y)];
            if (me.Room != beyond)
            {
                me.Px = (short)((x << 4) + 8);
                me.Py = (short)((y << 4) + 8);
                me.Room = beyond;
                me.Dir = (short)(2 * side + 1);
                me.Sighting = 10;
                break;
            }
        }
        me.Status &= ~ActorFlags.Leaving;
    }
}
