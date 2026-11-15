using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The raid's sound, palettes, pictures, radar and status panel.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The radar's top-left corner on the screen.</summary>
    private const int RadarX = 0xb4;
    private const int RadarY = 0x64;

    /// <summary>The radar draws each cell as 3 x 3 pixels: 120 x 96 for the map.</summary>
    private const int RadarCell = 3;

    /// <summary>The row of the tile sheet where the radar's 3 x 3 tiles begin.</summary>
    private const int RadarTilesY = 0x80;

    private const int Grey = 7;
    private const int Cyan = 3;
    private const int Magenta = 5;
    private const int LightBlue = 9;

    /// <summary>
    /// Plays sound effect <paramref name="effect"/> when the player has sound on, in the raid's alarm state and variant.
    /// </summary>
    /// <param name="effect">The effect's number.</param>
    // orig: TAC 00e8:0662 DoSound (src/c/tac/tac_00e8_0662.c) matched; tests: TacOracleTests
    // Other code under the same name: CODE 0000:0cbd and its copies (LibraryOutput.DoSound), CHASE 00df:0467 (docs/source-map/shared-functions.csv).
    public void DoSound(int effect)
    {
        if ((state.Status & Options.Sound) != 0)
        {
            world.Sound.PlaySound(effect, state.SoundMode, state.SoundVariant);
        }
    }

    /// <summary>Sets palette <paramref name="palette"/> of the five (0 normal, 1 the room's, 2 and 4 the flashes of a blast).</summary>
    /// <param name="palette">0..4.</param>
    // orig: TAC 00e8:06f6 set_palette (src/c/tac/tac_00e8_06f6.c) matched; tests: TacOracleTests
    // Other code under the same name: the set_palette thunks of GAME, BUG, CHASE, CODE (IGraphicsDriver.SetPalette).
    public void SetPalette(int palette) => world.Graphics.SetPalette(state.PaletteSets[palette]);

    /// <summary>Draws picture <paramref name="sprite"/> of the sprite sheet at screen (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="sprite">0..159.</param>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    // orig: TAC 00e8:5b0d draw_sprite (src/c/tac/tac_00e8_5b0d.c) matched; tests: TacOracleTests
    // Other code under the same name: FINAL 0100:b65f (docs/source-map/shared-functions.csv).
    public void DrawSprite(int sprite, int x, int y) => world.Graphics.DrawImagePort(state.Rp1, x, y, state.SpriteHandles[sprite]);

    /// <summary>Draws icon <paramref name="index"/> at screen (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="index">0..119.</param>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    // orig: TAC 00e8:5aed draw_icon (src/c/tac/tac_00e8_5aed.c) matched; tests: TacOracleTests
    public void DrawIcon(int index, int x, int y) => world.Graphics.DrawImagePort(state.Rp1, x, y, state.IconHandles[index]);

    /// <summary>
    /// Draws the radar, the building's floor plan at 3 pixels a cell with its corner at (<paramref name="x"/>,
    /// <paramref name="y"/>): the rooms the player has seen (all of them with the floor plan), each piece as its small
    /// tile, the floor cyan (red where gas is), outside black; then an X on every laid trap. It is drawn on the backdrop
    /// page and copied to the screen. The one-room ambush has no plan, only the traps.
    /// </summary>
    /// <param name="x">Screen x of the corner.</param>
    /// <param name="y">Screen y of the corner.</param>
    // orig: TAC 00e8:540c draw_minimap (src/c/tac/tac_00e8_540c.c) matched; tests: TacOracleTests
    public void DrawMinimap(int x, int y)
    {
        const int Backdrop = 1;
        state.Rp1.PageNr = Backdrop;
        Output.FillRectCorners(x, y, x + 0x77, y + 0x5f, Cyan);
        if (state.GameMode != RaidState.OneRoomMode)
        {
            Output.DrawRectOutline(x - 1, y - 1, 0x79, 0x61, Grey);
            for (var column = 0; column < RaidState.MapW; column++)
            {
                for (var row = 0; row < RaidState.MapH; row++)
                {
                    DrawRadarCell(x, y, column, row);
                }
            }
        }
        foreach (var trap in state.Gr)
        {
            // C: && binds before ||: a free slot whose time is still the remote trap's mark is drawn too
            if ((trap.Type != Grenade.Free && trap.Time == Grenade.BoobyTrap) || trap.Time == Grenade.RemoteTrap)
            {
                var trapX = C16.Int(C16.Div(C16.Int(RadarCell * trap.Px), 16) + x);
                var trapY = C16.Int(C16.Div(C16.Int(RadarCell * trap.Py), 16) + y);
                Output.DrawLine(trapX - 1, trapY - 1, trapX + 1, trapY + 1, White);
                Output.DrawLine(trapX + 1, trapY - 1, trapX - 1, trapY + 1, White);
            }
        }
        state.Rp1.PageNr = 0;
        world.Graphics.CopyRect(state.RpBack, x - 1, y - 1, 0x7a, 0x62, state.Rp1, x - 1, y - 1);
    }

    /// <summary>One cell of the radar (draw_minimap).</summary>
    private void DrawRadarCell(int x, int y, int column, int row)
    {
        var cell = RaidState.Cell(column, row);
        var room = state.Rm[state.RoomAtCell[cell]];
        if ((room.Status & RoomStatus.Visited) == 0 && (state.Equip & Equipment.FloorPlan) == 0)
        {
            return;
        }
        var left = RadarCell * column + x;
        var top = RadarCell * row + y;
        var tile = state.CellMap[cell];
        if (tile != RaidState.CellEmpty)
        {
            var kind = state.TileType(tile & RaidState.CellTile);
            // C: 16-bit divisions of the sheet coordinates
            var sheetX = C16.Div(kind.SpriteX, 16) * RadarCell;
            var sheetY = C16.Div(kind.SpriteY, 16) * RadarCell + RadarTilesY;
            world.Graphics.CopyRect(state.RpSprite, sheetX, sheetY, RadarCell, RadarCell, state.Rp1, left, top);
            return;
        }
        Output.FillRectCorners(left, top, left + 2, top + 2, state.RoomAtCell[cell] != 0 ? Cyan : 0);
        if ((room.Status & RoomStatus.Gas) != 0)
        {
            Output.FillRectCorners(left, top, left + 2, top + 2, Red);
        }
    }

    /// <summary>
    /// Redraws the status panel on the right: the clean panel from the backdrop; the selected grenade kind (moving on to
    /// the next kind with grenades left, at most four steps) framed; the equipment's icons; the film counter with a
    /// camera; the rounds, clips, bugs, wounds and grenades as rows of small pictures; and, once a letter of the password
    /// is known, the password as known so far.
    /// </summary>
    // orig: TAC 00e8:7e72 draw_status_panel (src/c/tac/tac_00e8_7e72.c) matched; tests: TacOracleTests
    public void DrawStatusPanel()
    {
        world.Graphics.CopyRect(state.RpBack, 0xb0, 0, 0x90, 0x64, state.Rp1, 0xb0, 0);
        var graphics = world.Graphics;
        var steps = 0;
        while (state.Grenades[state.Gtype] <= 0)
        {
            if (++steps >= 4)
            {
                break;
            }
            if (++state.Gtype == 3)
            {
                state.Gtype = 0;
            }
        }
        if (steps < 4)
        {
            var bar = state.AmmoBarPos[state.Gtype];
            graphics.FillRect(state.Rp1, bar.X - 1, bar.Y - 1, C16.Int(6 * state.Grenades[state.Gtype] + 8), 0xf, LightBlue);
        }
        for (var item = 7; item >= 0; item--)
        {
            if ((state.Equip & 1 << item) != 0 && item != Equipment.BugsBit)
            {
                graphics.DrawImagePort(state.Rp1, state.EquipIconPos[item].X, state.EquipIconPos[item].Y, state.ItemIcons[item]);
            }
        }
        if ((state.Equip & Equipment.Camera) != 0)
        {
            DrawFilmCounter();
        }
        const int RoundIcon = 11, ClipIcon = 12, WoundIcon = 13, FirstGrenadeIcon = 8;
        var rounds = state.RoundIconPos[0];
        for (var i = 0; i < state.Bullets; i++)
        {
            graphics.DrawImagePort(state.Rp1, 5 * i + rounds.X, rounds.Y, state.ItemIcons[RoundIcon]);
        }
        var clips = state.RoundIconPos[1];
        for (var i = 0; i < state.Clips; i++)
        {
            graphics.DrawImagePort(state.Rp1, 0xd * i + clips.X, clips.Y, state.ItemIcons[ClipIcon]);
        }
        var bugs = state.EquipIconPos[Equipment.BugsBit];
        for (var i = 0; i < state.Bugs; i++)
        {
            // two columns of bug icons
            graphics.DrawImagePort(state.Rp1, (i & 1) * 9 + bugs.X, i / 2 * 7 + bugs.Y, state.ItemIcons[Equipment.BugsBit]);
        }
        for (var i = 0; i < state.Wounds; i++)
        {
            graphics.DrawImagePort(state.Rp1, 6 * i + 0x104, (i & 1) * 0xa + i + 0x3f, state.ItemIcons[WoundIcon]);
        }
        for (var kind = 0; kind < 3; kind++)
        {
            var bar = state.AmmoBarPos[kind];
            for (var i = 0; state.Grenades[kind] > i; i++)
            {
                graphics.DrawImagePort(state.Rp1, 6 * i + bar.X, bar.Y, state.ItemIcons[FirstGrenadeIcon + kind]);
            }
        }
        DrawKnownPassword();
    }

    /// <summary>The password box of the status panel, once a letter of the mask is not a dot (draw_status_panel).</summary>
    private void DrawKnownPassword()
    {
        for (var i = 0; state.PasswordWord.Length > i; i++)
        {
            if (state.PasswordMask[i] != '.')
            {
                world.Graphics.FillRect(state.Rp1, 0xf8, 0x58, 0x40, 9, LightGreen);
                Output.DrawRectOutline(0xf8, 0x58, 0x40, 9, Green);
                Output.DrawTextFlag0(MaskText(), 0xfa, 0x59, Magenta);
                break;
            }
        }
    }

    /// <summary>The password mask up to its end mark.</summary>
    private string MaskText()
    {
        var end = Array.IndexOf(state.PasswordMask, '\0');
        return end >= 0 ? new string(state.PasswordMask, 0, end) : throw new MemoryOverrunException("the password mask has no end mark");
    }

    /// <summary>Redraws the status panel (after a bug was used up).</summary>
    // orig: TAC 00e8:7e6e refresh_status_panel (src/c/tac/tac_00e8_7e6e.c) matched; tests: TacOracleTests
    public void RefreshStatusPanel() => DrawStatusPanel();
}
