using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The view of the room the player is in: the playfield on the left of the screen.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The playfield: the left 172 by 200 pixels of the screen; the room is centred on (88, 108).</summary>
    private const int ViewWidth = 0xac, ViewHeight = 0xc8, ViewCentreX = 0x58, ViewCentreY = 0x6c;

    /// <summary>The floor tile's place on the tile sheet.</summary>
    private const int FloorTileX = 0, FloorTileY = 0x70;

    /// <summary>The tile of an open door or piece lies one tile row below the closed one on the sheet.</summary>
    private const int OpenTileOffset = 0x10;

    // palette indices
    private const int ViewBlue = 1, ViewStripes = 5, ViewEdge = 0xf, RoomFrame = 0xb, TitleFrame = 7, TitleLetters = 0xf;

    /// <summary><see cref="RaidState.PlayerState"/>: walking (a guard aims better at a player standing still).</summary>
    private const int Walking = 1;

    /// <summary><see cref="RaidState.Equip"/>: the Uzi (bit 1), a better weapon than the pistol.</summary>
    private const int Uzi = 0x02;

    /// <summary>The palette of the flash of a shot.</summary>
    private const int ShotFlashPalette = 3;

    /// <summary>The aim icon among <see cref="RaidState.ItemIcons"/>, and the colour of its picture that shows the chance.</summary>
    private const int AimIcon = 14, AimIconColour = 1;

    /// <summary>Sounds of a shot: the player hit, a guard hit (pistol, Uzi), the player missed, a guard missed.</summary>
    private const int SoundPlayerHit = 3, SoundGuardHit = 4, SoundUziShot = 6, SoundPlayerMissed = 0x21, SoundGuardMissed = 0x22;

    /// <summary>
    /// A shot, or with <paramref name="fire"/> 0 the aim of the player's next one. A guard shoots at the player when he
    /// can see him; the player (with bullets left) at the nearest active guard of the room in sight in front of him,
    /// in his direction or the one beside it. The chance grows with the shooter's time on the target (a guard aims
    /// worse on the easier levels and at a walking player; the Uzi adds one) and falls with every 48 pixels of distance;
    /// a shot at the player counts towards the alarm. A hit marks the target hit; a miss raises the alert at the
    /// player's cell. A shot flashes the screen; an aim shows the chance as the colour of the aim icon.
    /// </summary>
    /// <param name="shooter">The actor: 0 the player, 1..8 a guard.</param>
    /// <param name="fire">Nonzero to shoot, 0 to show the player's aim.</param>
    // orig: TAC 00e8:225d aim_and_fire (src/c/tac/tac_00e8_225d.c) matched; tests: TacOracleTests
    public void AimAndFire(int shooter, int fire)
    {
        int target;
        if (shooter != 0)
        {
            target = GuardRules.LineOfSightToPlayer(state, shooter) != 0 ? 0 : -1;
        }
        else
        {
            if (state.Bullets == 0)
            {
                return;
            }
            if (fire != 0)
            {
                state.Bullets--;
                DrawStatusPanel();
                state.PlayerActed = true;
            }
            target = NearestGuardInSight();
        }
        var chance = 0;
        if (target != -1)
        {
            var aimer = state.Bg[shooter];
            var aimed = state.Bg[target];
            var distance = BuildingRules.ApproxDistance(aimer.Px - aimed.Px, aimer.Py - aimed.Py);
            if (target == 0)
            {
                state.Alarm++;
                GuardRules.RaiseAlert(state, state.InRoom, ArrestAlert);
                chance = C16.Div(aimer.Sighting, C16.Int(5 - state.Diff));
                if (state.PlayerState != Walking)
                {
                    chance++;
                }
                if (state.Diff == 0)
                {
                    chance = 1;
                }
            }
            else
            {
                chance = aimed.Sighted / 2;
                if ((state.Equip & Uzi) != 0)
                {
                    chance++;
                }
                if (fire != 0)
                {
                    aimed.Sighted = 0;
                }
            }
            chance += 1 - distance / 0x30;
            if (fire != 0)
            {
                Shoot(aimer, aimed, target == 0, chance);
            }
        }
        else if (fire != 0)
        {
            DoSound(SoundGuardMissed);
        }
        if (fire != 0)
        {
            SetPalette(ShotFlashPalette);
            Output.WaitVsync(1);
            SetPalette(NormalPalette);
        }
        else if (chance != 0)
        {
            int colour = state.AimChanceColour[GameLibrary.Clamp(chance, 0, 4)];
            world.Graphics.DrawImagePort(state.Rp1, 4, 1, state.ItemIcons[AimIcon]);
            world.Graphics.RecolorRect(state.Rp1, 4, 1, 0x10, 0x10, AimIconColour, colour);
        }
    }

    /// <summary>The nearest active, standing guard of the player's room in sight and in front of him (aim_and_fire).</summary>
    /// <returns>The guard, 1..8, or -1.</returns>
    private int NearestGuardInSight()
    {
        var target = -1;
        var nearest = 9999;
        for (var guard = 1; guard <= RaidState.Actors - 1; guard++)
        {
            var g = state.Bg[guard];
            if ((g.Status & ActorFlags.Active) == 0 || g.Room != state.InRoom || g.Stun > 999)
            {
                continue;
            }
            int dx = g.Px - state.Px, dy = g.Py - state.Py;
            var direction = GuardRules.DirectionFromVectorCoarse(dx, dy);
            if (direction != state.PlayerFacing && ((state.PlayerFacing - 2) & 7) + 1 != direction)
            {
                continue;
            }
            if (GuardRules.LineOfSightToPlayer(state, guard) != 0)
            {
                var distance = BuildingRules.ApproxDistance(dx, dy);
                if (distance < nearest)
                {
                    target = guard;
                    nearest = distance;
                }
            }
        }
        return target;
    }

    /// <summary>The shot itself: a hit below the chance in four, else a miss that raises the alert at the player's cell (aim_and_fire).</summary>
    private void Shoot(BadGuy aimer, BadGuy aimed, bool atPlayer, int chance)
    {
        var uzi = (state.Equip & Uzi) != 0;
        if (atPlayer)
        {
            aimer.Sighting = 0;
        }
        if (GameLibrary.RandomBelow(state.Random, 4) < chance)
        {
            aimed.Status |= ActorFlags.Hit;
            DoSound(atPlayer ? SoundPlayerHit : uzi ? SoundUziShot : SoundGuardHit);
        }
        else
        {
            DoSound(atPlayer ? SoundPlayerMissed : uzi ? SoundUziShot : SoundGuardMissed);
            GuardRules.RaiseAlert(state, state.InRoom, ArrestAlert);
            state.AlertCellX = C16.Div(state.Px, CellPixels);
            state.AlertCellY = C16.Div(state.Py, CellPixels);
        }
    }

    /// <summary>
    /// Draws the view of a room: the playfield cleared to blue with stripes, the room centred in it with its floor and
    /// every piece from the cell map (open ones from the sheet's row below), its frame; the clean picture is copied to
    /// the backdrop the people are drawn over; then the title bar with the name of the player's room ("Street" in the
    /// one-room raid) and the status panel. The room's edges in the building (<see cref="RaidState.RoomLeft"/> ...)
    /// are kept for the player's walk.
    /// </summary>
    /// <param name="room">The room, 1..<see cref="RaidState.Nroom"/>.</param>
    // orig: TAC 00e8:5674 draw_room_view (src/c/tac/tac_00e8_5674.c) matched; tests: TacOracleTests
    public void DrawRoomView(int room)
    {
        var graphics = world.Graphics;
        Output.FillRectCorners(0, 0, ViewWidth, ViewHeight, ViewBlue);
        for (var y = 0; y < ViewHeight; y += 2)
        {
            Output.DrawLine(0, y, ViewWidth, y, ViewStripes);
        }
        Output.DrawLine(ViewWidth - 1, 0, ViewWidth - 1, ViewHeight - 1, ViewEdge);
        Output.DrawLine(ViewWidth, 0, ViewWidth, ViewHeight - 1, ViewStripes);
        var r = state.Rm[room];
        int width = r.Dx << 4, height = r.Dy << 4;
        state.BaseX = C16.Int(-(width / 2 - ViewCentreX));
        state.BaseY = C16.Int(-(height / 2 - ViewCentreY));
        // C: the first bounds, on the screen, are overwritten below by the bounds in the building
        state.RoomRight = C16.Int(state.BaseX + width - 1);
        state.RoomBottom = C16.Int(height + state.BaseY - 1);
        for (var a = 0; r.Dx > a; a++)
        {
            for (var b = 0; r.Dy > b; b++)
            {
                graphics.CopyRect(state.RpSprite, FloorTileX, FloorTileY, CellPixels, CellPixels, state.Rp1, (a << 4) + state.BaseX, (b << 4) + state.BaseY);
            }
        }
        state.RoomLeft = C16.Int(r.X << 4);
        state.RoomTop = C16.Int(r.Y << 4);
        state.RoomRight = C16.Int(width + state.RoomLeft - 1);
        state.RoomBottom = C16.Int(height + state.RoomTop - 1);
        for (var b = 0; r.Dy > b; b++)
        {
            for (var a = 0; r.Dx > a; a++)
            {
                var content = state.CellMap[RaidState.Cell(r.X + a, r.Y + b)];
                if (content == RaidState.CellEmpty)
                {
                    continue;
                }
                int tileY = state.TileType(content & RaidState.CellTile).SpriteY;
                if ((content & RaidState.CellOpen) != 0)
                {
                    tileY += OpenTileOffset;
                }
                var piece = state.TileType(content & RaidState.CellTile);
                graphics.CopyRect(state.RpSprite, piece.SpriteX, tileY, CellPixels, CellPixels, state.Rp1, (a << 4) + state.BaseX, (b << 4) + state.BaseY);
            }
        }
        Output.DrawRectOutline(state.BaseX - 1, state.BaseY - 1, width + 1, height + 1, RoomFrame);
        graphics.CopyRect(state.Rp1, 0, 0, ViewWidth, ViewHeight, state.RpBack, 0, 0);
        Output.DrawRectOutline(0x1a, 0, 0x8f, 0x12, TitleFrame);
        graphics.FillRect(state.Rp1, 0x1b, 1, 0x8d, 0x10, ViewBlue);
        // C: the title is the player's room's, not necessarily the one drawn
        Output.DrawTextFlag0(state.GameMode == RaidState.OneRoomMode ? state.StrStreet : state.Rms[state.Rm[state.InRoom].Type].Name, 0x20, 2, TitleLetters);
        DrawStatusPanel();
    }
}
