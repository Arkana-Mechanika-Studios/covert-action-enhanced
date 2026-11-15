using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The raid's doors, grenades and blasts, and leaving the raid.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>A cell of the room view: 16 x 16 pixels.</summary>
    private const int CellPixels = 16;

    /// <summary>The first sprite of a burst (eight frames), and of the fragmentation's flash (four frames) drawn over it.</summary>
    private const int BurstSprite = 0xa8;
    private const int FlashSprite = 0xa0;

    /// <summary>Palettes: the room's, and the two flashes of a blast (near, and in another room).</summary>
    private const int RoomPalette = 1;
    private const int NearFlashPalette = 2;
    private const int FarFlashPalette = 4;

    /// <summary>Sound effects of the raid.</summary>
    private const int SoundBlastHere = 7;
    private const int SoundBlastElsewhere = 8;
    private const int SoundThrow = 0xc;
    private const int SoundDoorOpens = 0xd;
    private const int SoundDoorCloses = 0xe;

    /// <summary>The alarm a blast raises.</summary>
    private const int CrimeAlarm = 0xf0;

    /// <summary>The colour of a grenade in flight.</summary>
    private const int GrenadeColour = 0xa;

    /// <summary>
    /// Draws cell (<paramref name="x"/>, <paramref name="y"/>) of the player's room again after it changed (a door, drawer
    /// or safe opened or shut): its tile, open or shut, on the screen and on the backdrop. A door also opens or shuts its
    /// other half in the next room, with its sound and picture when that half is in the player's room.
    /// </summary>
    /// <param name="x">Cell column.</param>
    /// <param name="y">Cell row.</param>
    // orig: TAC 00e8:5962 redraw_cell (src/c/tac/tac_00e8_5962.c) matched; tests: TacOracleTests
    public void RedrawCell(int x, int y)
    {
        var cell = RaidState.Cell(x, y);
        var tile = state.CellMap[cell];
        if (tile != RaidState.CellEmpty && state.RoomAtCell[cell] == state.InRoom)
        {
            // tile 63 (the taken hidden information's cell, "opened" by the use key) reads its record behind the table
            var kind = state.TileType(tile & RaidState.CellTile);
            // the open variant of a tile is the picture below it
            var sheetY = C16.Int(kind.SpriteY + ((tile & RaidState.CellOpen) != 0 ? CellPixels : 0));
            var room = state.Rm[state.InRoom];
            var screenX = C16.Int(((x - room.X) << 4) + state.BaseX);
            var screenY = C16.Int(((y - room.Y) << 4) + state.BaseY);
            world.Graphics.CopyRect(state.RpSprite, kind.SpriteX, sheetY, CellPixels, CellPixels, state.Rp1, screenX, screenY);
            world.Graphics.CopyRect(state.Rp1, screenX, screenY, CellPixels, CellPixels, state.RpBack, screenX, screenY);
        }
        if ((TileBits(tile & RaidState.CellTile) & TileFlags.Door) == 0)
        {
            return;
        }
        var side = tile & 3;
        var otherHalf = RaidState.Cell(state.Mve4X[side] + x, state.Mve4Y[side] + y);
        state.CellMap[otherHalf] = (byte)((state.CellMap[otherHalf] & ~RaidState.CellOpen) + (state.CellMap[cell] & RaidState.CellOpen));
        if (state.RoomAtCell[otherHalf] == state.InRoom)
        {
            DoSound((state.CellMap[otherHalf] & RaidState.CellOpen) != 0 ? SoundDoorOpens : SoundDoorCloses);
            RedrawCell(state.Mve4X[side] + x, state.Mve4Y[side] + y);
        }
    }

    /// <summary>
    /// The bits of tile type <paramref name="tile"/>; for bare floor (tile 63, one past the table) the original reads the
    /// word after the table, the first entry of <see cref="RaidState.Move8X"/> (0: no bits).
    /// </summary>
    private TileFlags TileBits(int tile) =>
        // C: ob[63].bits lies at DS:068a, Move8X[0]
        state.TileType(tile).Bits;

    /// <summary>
    /// Moves the grenades and works their blasts, once per half frame. With <paramref name="bursts"/> 0 only the grenades
    /// in flight move: a step along their facing (bouncing back off a wall or a shut door, which ends the flight), drawn
    /// as a dot in the room view and on the radar; at the end of the flight the grenade goes off. With 1 the bursts are
    /// drawn and worked: at the moment of the blast the radar is drawn again, the blast sounds, the player's own blast
    /// raises the alarm in its room and counts as violence, and each person in the room is hit by distance (closer the
    /// harder; guards are hit harder at higher difficulty, the player less): a fragmentation grenade kills a guard or
    /// wounds the player, a stun grenade knocks out for longer, a gas grenade fills the room when its burst ends.
    /// </summary>
    /// <remarks>
    /// The distance counts against a protection of 1 (2 while sneaking), plus 1 for partial cover and 2 for a wall
    /// between. A stun grenade of the player plays its whole burst in one call.
    /// </remarks>
    /// <param name="bursts">0: the flights; 1: the bursts.</param>
    // orig: TAC 00e8:26b0 update_projectiles (src/c/tac/tac_00e8_26b0.c) matched; tests: TacOracleTests
    public void UpdateProjectiles(int bursts)
    {
        foreach (var grenade in state.Gr)
        {
            if (grenade.Type == Grenade.Free)
            {
                continue;
            }
            var room = state.Rm[state.InRoom];
            var screenX = C16.Int(grenade.Px - (room.X << 4) + state.BaseX);
            var screenY = C16.Int(grenade.Py - (room.Y << 4) + state.BaseY);
            var kind = grenade.Type & 0x7f;
            var blastRoom = (int)state.RoomAtCell[RaidState.Cell(grenade.Px / 16, grenade.Py / 16)];
            var visible = blastRoom == state.InRoom && grenade.Time >= Grenade.Burst;
            // while the player is in his throwing pose his own grenades are not drawn
            if (state.ThrowTimer != 0 && (grenade.Type & Grenade.ThrownByGuard) == 0)
            {
                visible = false;
            }
            if (grenade.Time < 0 && grenade.Time > Grenade.BoobyTrap && bursts != 0)
            {
                do
                {
                    BurstFrame(grenade, kind, blastRoom, visible, screenX, screenY);
                }
                while (grenade.Type == Grenade.Stun && grenade.Time > Grenade.Burst);
            }
            if (grenade.Time > 0)
            {
                Fly(grenade, blastRoom, visible, screenX, screenY);
            }
        }
    }

    /// <summary>One frame of a burst: its picture, the blast itself at its first frame, and the end (update_projectiles).</summary>
    private void BurstFrame(Grenade grenade, int kind, int blastRoom, bool visible, int screenX, int screenY)
    {
        if (visible)
        {
            DrawSprite((grenade.Time & 7) + BurstSprite, screenX - 8, screenY - 8);
            if (kind == Grenade.Fragmentation)
            {
                DrawSprite((grenade.Time & 3) + FlashSprite, screenX - 8, screenY - 8);
            }
            if (kind == Grenade.Stun || grenade.Time == Grenade.Burst)
            {
                FlashPalette(NearFlashPalette);
            }
        }
        if (grenade.Time == Grenade.Burst)
        {
            Blast(grenade, kind, blastRoom);
        }
        if (++grenade.Time == 0)
        {
            if (visible)
            {
                world.Graphics.CopyRect(state.RpBack, screenX - 8, screenY - 8, 0xf, 0xf, state.Rp1, screenX - 8, screenY - 8);
            }
            if (kind == Grenade.Gas)
            {
                state.Rm[blastRoom].Status |= RoomStatus.Gas;
                DrawMinimap(RadarX, RadarY);
            }
            grenade.Type = Grenade.Free;
        }
    }

    /// <summary>A flash palette, then the room's; the next frame puts the normal one back (update_projectiles).</summary>
    private void FlashPalette(int flash)
    {
        SetPalette(flash);
        SetPalette(RoomPalette);
        state.PaletteDirty = true;
    }

    /// <summary>The moment of the blast: its sound, alarm and violence, and its effect on every person in the room (update_projectiles).</summary>
    private void Blast(Grenade grenade, int kind, int blastRoom)
    {
        // the burst's length: 16 frames for the player's gas, 8 for the rest (a guard's gas included)
        grenade.Time = (short)(grenade.Type == Grenade.Gas ? -0x10 : -8);
        DrawMinimap(RadarX, RadarY);
        if (kind != Grenade.Gas)
        {
            DoSound(blastRoom == state.InRoom ? SoundBlastHere : SoundBlastElsewhere);
        }
        if (kind != Grenade.Gas && (grenade.Type & Grenade.ThrownByGuard) == 0)
        {
            GuardRules.RaiseAlert(state, blastRoom, CrimeAlarm);
            state.Alarm++;
        }
        if (blastRoom != state.InRoom)
        {
            FlashPalette(FarFlashPalette);
        }
        for (var slot = 0; slot < RaidState.Actors; slot++)
        {
            var person = state.Bg[slot];
            if (person.Room != blastRoom || (person.Status & ActorFlags.Active) == 0)
            {
                continue;
            }
            // C: 16-bit products and divisions
            var distance = BuildingRules.ApproxDistance(person.Px - grenade.Px, person.Py - grenade.Py);
            distance = slot != 0
                ? C16.Div(C16.Int(distance * (state.Diff + 2)), 4)
                : C16.Div(C16.Int(distance * 4), C16.Int(state.Diff + 2));
            var protection = (person.Status & ActorFlags.Sneak) != 0 ? 2 : 1;
            protection += 2 - GuardRules.LineBlockedCount(state, grenade.Px, grenade.Py, person.Px, person.Py);
            if (kind == Grenade.Fragmentation && 0x28 / protection > distance)
            {
                if (slot != 0)
                {
                    MarkGuardDead(slot);
                }
                else
                {
                    state.Wounds++;
                    DrawStatusPanel();
                }
            }
            if (kind == Grenade.Stun)
            {
                person.Stun = (short)GameLibrary.Clamp(person.Stun, C16.Div(C16.Int(0xa0 - distance), (short)protection), 0x3e7);
            }
        }
    }

    /// <summary>One step of a grenade's flight: the dot moves in the room view and on the radar (update_projectiles).</summary>
    private void Fly(Grenade grenade, int flightRoom, bool visible, int screenX, int screenY)
    {
        if (visible)
        {
            world.Graphics.CopyRect(state.RpBack, screenX, screenY, 1, 1, state.Rp1, screenX, screenY);
        }
        var (radarX, radarY) = RadarDot(grenade);
        world.Graphics.CopyRect(state.RpBack, radarX, radarY, 1, 1, state.Rp1, radarX, radarY);

        var nextX = C16.Int(state.MoveX[grenade.Dir] + grenade.Px);
        var nextY = C16.Int(state.MoveY[grenade.Dir] + grenade.Py);
        var next = RaidState.Cell(nextX / 16, nextY / 16);
        var tile = state.CellMap[next];
        // C: ob[cell & 0x3f].bits (tac_00e8_26b0.c line 147): bare floor (0xff) has the open bit and reads ob[63], one past
        // the table (TileBits)
        var openDoor = (tile & RaidState.CellOpen) != 0 && (TileBits(tile & RaidState.CellTile) & TileFlags.Door) != 0;
        if (state.RoomAtCell[next] != flightRoom && !openDoor)
        {
            // it hits a wall: it bounces back two steps and goes off at once
            grenade.Time = 1;
            grenade.Px = C16.Int(grenade.Px - (state.MoveX[grenade.Dir] << 1));
            grenade.Py = C16.Int(grenade.Py - (state.MoveY[grenade.Dir] << 1));
        }
        else
        {
            grenade.Px = nextX;
            grenade.Py = nextY;
        }
        if (visible)
        {
            var room = state.Rm[state.InRoom];
            PlotPoint(C16.Int(grenade.Px - (room.X << 4) + state.BaseX), C16.Int(grenade.Py - (room.Y << 4) + state.BaseY), GrenadeColour);
        }
        (radarX, radarY) = RadarDot(grenade);
        PlotPoint(radarX, radarY, GrenadeColour);
        if (--grenade.Time == 0)
        {
            grenade.Time = Grenade.Burst;
        }
    }

    /// <summary>A grenade's dot on the radar (update_projectiles).</summary>
    private static (int X, int Y) RadarDot(Grenade grenade) =>
        // C: 16-bit products and divisions
        (C16.Int(C16.Div(C16.Int(RadarCell * grenade.Px), 16) + RadarX), C16.Int(C16.Div(C16.Int(RadarCell * grenade.Py), 16) + RadarY));

    /// <summary>
    /// Tidies up before the raid hands over: switches Num Lock off (the raid's keys use the keypad), and when the player
    /// quits the whole game tells the launcher so (the hand-off's "next part" becomes -1).
    /// </summary>
    /// <param name="toDos">Whether the game ends.</param>
    // orig: TAC 00e8:063d exit_cleanup (src/c/tac/tac_00e8_063d.c) matched; tests: TacOracleTests
    public void ExitCleanup(bool toDos)
    {
        world.Input.SetNumLock(false);
        if (toDos)
        {
            shared.NextModule = -1;
        }
    }
}
