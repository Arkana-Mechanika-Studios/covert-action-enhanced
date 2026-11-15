using CovertAction.Core.Graphics;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Raid;

/// <summary>The raid's pictures of the building, the equipment screen and the choice of the door: how the player starts.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The 16 x 16 cells of the sprite sheet: 15 x 15 pixels of picture inside a one-pixel grid line.</summary>
    private const int CellSize = 16, CellPicture = 15, GridLine = 1;

    /// <summary>The sprite sheet: 20 cells wide, 8 rows high for the building's pieces and people.</summary>
    private const int SpriteColumns = 20, SpriteRows = 8;

    /// <summary>
    /// The cells below the grid, which fill the handles after it: a row of 6 (handles 160 to 165) at y 0x81, one cell
    /// (handle 166) at x 0x61, and a row of 8 (handles 168 to 175) at y 0x91.
    /// </summary>
    private const int ExtraRowAFirst = 160, ExtraRowACount = 6, ExtraRowAY = 0x81, ExtraCell = 166, ExtraCellX = 0x61;

    private const int ExtraRowBFirst = 168, ExtraRowBCount = 8, ExtraRowBY = 0x91;

    /// <summary>The walls' colour is one of three, by organisation (the picture's yellow is recoloured to it).</summary>
    private const int PictureWallColour = 0xe, CgaWallColour = 0xe, WallColours = 3;

    private static readonly int[] WallColourByOrganisation = [0xe, 0xb, 0xf];

    /// <summary>The kit of a special raid: the escape's (the pistol, the vest and the detector), the jailbreak's, and the ambush's.</summary>
    private const short EscapeKit = 0xc1, JailbreakKit = 0xc2;

    /// <summary>The grenades the jailbreak and the ambush start with.</summary>
    private const int SpecialRaidGrenades = 4;

    /// <summary>The doors are numbered on the radar: the number stands at the door's cell scaled by 3, from the radar's corner (two pixels above it).</summary>
    private const int DoorMarkY = 0x62, DoorMarkScale = 3;

    /// <summary>The colour of a door's number on the radar: yellow on CGA, white otherwise.</summary>
    private const int CgaDoorColour = 4, DoorColour = 0xf;

    /// <summary>Where the "Which door?" menu stands.</summary>
    private const int DoorMenuX = RadarLeft, DoorMenuY = 0x40;

    /// <summary>
    /// The tile put where the agent starts when a special raid (kinds 1 to 3) starts in the middle of a room: a chair
    /// (TAC's <c>ob[0xe]</c>, "Chair", the third of the four chair tiles: TF_SOLID, drawn and examined as any chair).
    /// </summary>
    private const byte StartTile = 0xe;

    /// <summary>The tile put at the place where the alarm sounds, in the ambush.</summary>
    private const byte AlarmTile = 2;

    /// <summary>The alarm the jailbreak raises in the target room at once, and the ambush in the first room.</summary>
    private const int JailbreakAlarm = 0x3e7, AmbushAlarm = 0xf0;

    /// <summary>
    /// Loads the building's picture sheet (the one for Maxine or for Max) onto the sprite sheet's page and cuts it up: the
    /// building's pieces and people into <see cref="RaidState.SpriteHandles"/>. The sheet's yellow is recoloured to the
    /// colour of the walls, which depends on the organisation the building belongs to.
    /// </summary>
    // orig: TAC 00e8:5b2d load_sprites_pic (src/c/tac/tac_00e8_5b2d.c) matched; tests: TacOracleTests
    public void LoadSpritesPic()
    {
        world.Graphics.BeginImageCuts();
        if (state.FacesOn)
        {
            Pictures.LoadFacesPic(state.IconHandles, state.FacesWomenName, state.FacesMenName);
        }
        Pictures.LoadPictureSwapped(state.Status.HasFlag(Options.Female) ? state.SpritesFemaleName : state.SpritesMaleName, SheetPage);
        state.BuildingColor = (short)(state.CGA ? CgaWallColour : WallColourByOrganisation[state.AtLoc % WallColours]);
        if (!state.CGA)
        {
            world.Graphics.RecolorRect(state.RpSprite, 0, 0, ScreenWidth, ScreenHeight, PictureWallColour, state.BuildingColor);
        }
        for (var row = 0; row < SpriteRows; row++)
        {
            for (var column = 0; column < SpriteColumns; column++)
            {
                state.SpriteHandles[SpriteColumns * row + column] = CutCell(column * CellSize + GridLine, row * CellSize + GridLine);
            }
        }
        for (var i = 0; i < ExtraRowACount; i++)
        {
            state.SpriteHandles[ExtraRowAFirst + i] = CutCell(i * CellSize + GridLine, ExtraRowAY);
        }
        for (var i = 0; i < ExtraRowBCount; i++)
        {
            state.SpriteHandles[ExtraRowBFirst + i] = CutCell(i * CellSize + GridLine, ExtraRowBY);
        }
        state.SpriteHandles[ExtraCell] = CutCell(ExtraCellX, ExtraRowAY);
        world.Graphics.EndImageCuts();
    }

    /// <summary>Cuts one 15 x 15 picture out of the sprite sheet's page.</summary>
    private Image? CutCell(int x, int y) => world.Graphics.CutImage(SheetPage, x, y, CellPicture, CellPicture);

    /// <summary>
    /// Sets the player up for the raid: the kit (chosen on the equipment screen in a plain break-in, fixed in the special
    /// raids), then where the agent stands: in the middle of the target room in a special raid, at one of the building's
    /// doors to the outside in a break-in (with a menu to choose when there are several). Clears the people, the radar
    /// marks and the grenades, places the agent and the guards (one more than the difficulty), and in the jailbreak and
    /// the ambush marks the place of the alarm.
    /// </summary>
    // orig: TAC 00e8:070e choose_door (src/c/tac/tac_00e8_070e.c) matched; tests: TacOracleTests
    public void ChooseDoor()
    {
        var special = state.GameMode is RaidState.EscapeMode or RaidState.KillMode or RaidState.OneRoomMode;
        if (!special)
        {
            EquipmentScreen(chooseKit: true);
        }
        else
        {
            state.Equip = EscapeKit;
            if (state.GameMode is RaidState.KillMode or RaidState.OneRoomMode)
            {
                state.Grenades[0] = SpecialRaidGrenades;
                if (state.GameMode == RaidState.KillMode)
                {
                    state.Equip = JailbreakKit;
                }
                else
                {
                    state.Equip &= unchecked((short)~Equipment.Detector);
                }
            }
            EquipmentScreen(chooseKit: false);
            DrawStatusPanel();
        }
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        state.Rm[0].Status |= RoomStatus.Visited;
        DrawMinimap(RadarLeft, RadarTop);
        state.Rp1.BPen = 1;
        // provisional place, put right below; the guards placed first see it
        state.Px = 0x148;
        state.Py = 0x108;

        int cellX, cellY;
        if (special)
        {
            (cellX, cellY) = StartInTargetRoom();
        }
        else
        {
            (cellX, cellY) = StartAtDoor();
        }
        if (state.GameMode == RaidState.KillMode)
        {
            GuardRules.RaiseAlert(state, state.MacGuff, JailbreakAlarm);
        }
        state.WantedFacing = state.PlayerFacing;
        foreach (var grenade in state.Gr)
        {
            grenade.Type = Grenade.Free;
        }
        for (var actor = 0; actor < RaidState.Actors; actor++)
        {
            state.ActorSpriteX[actor] = state.ActorSpriteY[actor] = state.ActorRadarX[actor] = state.ActorRadarY[actor] = 0;
            state.Bg[actor].Status = ActorFlags.None;
        }
        GuardRules.SpawnGuard(state, 0);
        state.Bg[0].Status |= ActorFlags.Active;
        // one guard more than the raid's difficulty, none in the jailbreak
        for (var guard = 1; guard <= state.Diff + 1; guard++)
        {
            if (state.GameMode != RaidState.KillMode)
            {
                GuardRules.SpawnGuard(state, guard);
            }
        }
        Array.Clear(state.RoomLastVisit);
        state.Px = (short)((cellX << 4) + 8);
        state.Py = (short)((cellY << 4) + 8);
        state.BodiesHere = 0;
        state.InRoom = -1;
        if (state.GameMode is RaidState.KillMode or RaidState.OneRoomMode)
        {
            state.AlertCellX = (short)(state.Px / CellSize);
            state.AlertCellY = (short)(state.Py / CellSize);
            if (state.GameMode == RaidState.OneRoomMode)
            {
                GuardRules.RaiseAlert(state, 1, AmbushAlarm);
                state.CellMap[RaidState.Cell(state.AlertCellX, state.AlertCellY)] = AlarmTile;
            }
        }
    }

    /// <summary>
    /// The start of a special raid: the agent stands in the middle of the target room (the ambush: at its bottom edge, and
    /// the alarm room is the target room). The escape forgets the target room afterwards. The jailbreak shows the rooms
    /// next to the target room on the radar.
    /// </summary>
    /// <returns>The cell the agent starts in.</returns>
    private (int X, int Y) StartInTargetRoom()
    {
        var target = state.Rm[state.MacGuff];
        state.InRoom = state.MacGuff;
        var x = target.X + target.Dx / 2;
        var y = target.Y + target.Dy / 2;
        if (state.GameMode == RaidState.OneRoomMode)
        {
            y = target.Dy + target.Y - 1;
            state.ToRoom = state.MacGuff;
        }
        if (state.GameMode == RaidState.EscapeMode)
        {
            state.MacGuff = -1;
        }
        state.PlayerFacing = 1;
        state.CellMap[RaidState.Cell(x, y - (state.GameMode == RaidState.OneRoomMode ? 1 : 0))] = StartTile;
        if (state.GameMode == RaidState.KillMode)
        {
            for (var i = 0; i < state.Ndoor; i++)
            {
                var door = state.Dr[i];
                if (door.Room1 == state.MacGuff)
                {
                    state.Rm[door.Room2].Status |= RoomStatus.Visited;
                }
                if (door.Room2 == state.MacGuff)
                {
                    state.Rm[door.Room1].Status |= RoomStatus.Visited;
                }
            }
        }
        return (x, y);
    }

    /// <summary>
    /// The start of a break-in: the doors from outside into the building are numbered on the radar, and the player chooses
    /// one when there are several. The agent stands just inside it, facing in; its cell is marked as an opened door. When
    /// the raid is about a person, the alarm is raised in the target room at once.
    /// </summary>
    /// <returns>The cell the agent starts in.</returns>
    private (int X, int Y) StartAtDoor()
    {
        state.InRoom = -1;
        // C: the original keeps at most 8 doors; no building has more doors to the outside
        var entrances = new List<int>();
        for (var i = 0; i < state.Ndoor; i++)
        {
            var door = state.Dr[i];
            if ((door.Room1 == 0 && door.Room2 != 0) || (door.Room2 == 0 && door.Room1 != 0))
            {
                entrances.Add(i);
                Output.DrawTextFlag0(entrances.Count.ToString(), door.X * DoorMarkScale + RadarLeft, door.Y * DoorMarkScale + DoorMarkY, state.CGA ? CgaDoorColour : DoorColour);
            }
        }
        // C: a building always has a door to the outside
        var chosen = entrances[0];
        if (entrances.Count > 1)
        {
            state.MessageText.Text = state.StrWhichDoor + string.Concat(entrances.Select((_, n) => state.StrDoorNumber + (n + 1) + state.StrDoorLineEnd));
            var choice = ShowMessageBox(state.MessageText, DoorMenuX, DoorMenuY);
            chosen = choice != -1 ? entrances[choice] : entrances[0];
        }

        var entrance = state.Dr[chosen];
        int x = 0, y = 0;
        if (entrance.Room1 == 0 && entrance.Room2 != 0)
        {
            // the door's room 2 is inside: the agent stands one step inside, facing the way the door looks
            state.InRoom = entrance.Room2;
            x = entrance.X + state.Mve4X[entrance.Dir];
            y = entrance.Y + state.Mve4Y[entrance.Dir];
            state.PlayerFacing = (short)(entrance.Dir * 2 + 1);
        }
        if (entrance.Room2 == 0 && entrance.Room1 != 0)
        {
            state.InRoom = entrance.Room1;
            x = entrance.X;
            y = entrance.Y;
            state.PlayerFacing = (short)(((entrance.Dir + 2) & 3) * 2 + 1);
        }
        state.CellMap[RaidState.Cell(x, y)] |= RaidState.CellOpen;
        if (state.Who != -1)
        {
            GuardRules.RaiseAlert(state, state.MacGuff, 0);
        }
        return (x, y);
    }
}
