using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The player's turn of a frame: his key, his walk, turn and lunge, and the guards he runs into.</summary>
public sealed partial class BuildingRaid
{
    /// <summary><see cref="RaidState.PlayerState"/>: standing, walking, at the arrest prompt, frozen, lunging.</summary>
    private const int Standing = 0, AtArrest = 2, Frozen = 3, Lunging = 4;

    /// <summary><see cref="RaidState.Equip"/>: the camera (bit 2) and the gas mask (bit 5).</summary>
    private const int Camera = 0x04, GasMask = 0x20;

    /// <summary><see cref="RaidState.GameMode"/>: the raid whose guards wait for the player's first step.</summary>
    private const int ArrivalMode = 1;

    /// <summary>A knocked-out time above which the player lies down; above 256 the raid is over.</summary>
    private const int LyingDown = 0x40, KnockedOut = 0x100;

    /// <summary>The bullets of a clip.</summary>
    private const int ClipBullets = 6;

    /// <summary>The key a joystick's first button stands for in a room with a guard (space: the action key), and its second alone (the run key). Enter arrests, as F1.</summary>
    private const int KeySpace = 0x20, KeyRun = 0x35;

    /// <summary>The scan code keys of the raid, in the high byte (F1 ... F10; S, P, Q, J).</summary>
    private const int KeyF1 = 0x3b00, KeyF2 = 0x3c00, KeyF3 = 0x3d00, KeyF4 = 0x3e00, KeyF5 = 0x3f00, KeyF6 = 0x4000, KeyF7 = 0x4100,
        KeyF8 = 0x4200, KeyF9 = 0x4300, KeyF10 = 0x4400, KeySound = 0x1f00, KeyPause = 0x1900, KeyQuit = 0x1000, KeyJoystick = 0x2400;

    /// <summary>The time a laid trap waits for its remote control, and the time F8 sets it to (it goes off).</summary>
    private const int TrapWaiting = -9999, TrapFired = -63;

    /// <summary>Sounds of the pause.</summary>
    private const int SoundPause = 0x25, SoundResume = 0x26;

    /// <summary>How near (in pixels across and down) a guard must be for the player to knock him out by running into him.</summary>
    private const int ContactRange = 10;

    /// <summary>The sound routine's request of a frame: the music by the alert.</summary>
    private const int SoundFrame = 0x2c, SoundSiren = 0x12;

    /// <summary><see cref="RaidState.SoundMode"/>: quiet, guards alarmed, alarmed but disguised, alert, alert with alarmed guards.</summary>
    private const int MusicQuiet = 1, MusicAlarmed = 2, MusicDisguised = 3, MusicAlert = 4, MusicAlertAlarmed = 5;

    /// <summary>The frame counter wraps back by 256 past 7500 frames.</summary>
    private const int ClockWrap = 0x1d4c;

    /// <summary>The palette of the alert's flashing.</summary>
    private const int AlertPalette = 2;

    /// <summary>The status bar's rows on the people's page, and its colours: the disguise's, the gas's (red for blue).</summary>
    private const int StatusBarY = 0xa0, StatusBarWidth = 0x140, StatusBarHeight = 0x28, DisguiseColour = 0xd, GasBlue = 9, GasRed = 0xc;

    /// <summary>
    /// One frame of the raid. When the player steps into another room: the room left keeps a body mark if a guard lies
    /// dead there, a door is open or a body was there; the player is put on the new room's door cell; the street (room
    /// 0) ends the raid; else the room is drawn, marked visited, the radar redrawn, and the person to arrest raises the
    /// alert at an undisguised player. Then the player's turn, the eight guards', the people drawn, the music by the
    /// alert, the clock, the alert's count-down with its flashing and siren, now and then a room clearing of gas, and
    /// the status bar's colours for the disguise and the gas.
    /// </summary>
    // orig: TAC 00e8:0bcb frame_step (src/c/tac/tac_00e8_0bcb.c) matched; tests: TacOracleTests
    public void FrameStep()
    {
        state.Cx = C16.Div(state.Px, CellPixels);
        state.Cy = C16.Div(state.Py, CellPixels);
        if (state.RoomAtCell[RaidState.Cell(state.Cx, state.Cy)] != state.InRoom && !EnterRoom())
        {
            return;
        }
        UpdatePlayer();
        if (state.LevelDone != 0)
        {
            return;
        }
        for (var guard = 1; guard < RaidState.Actors; guard++)
        {
            UpdateGuard(guard);
        }
        DrawActors();
        PlayFrameMusic();
        state.Rtime++;
        if (state.Rtime > ClockWrap)
        {
            state.Rtime -= 0x100;
        }
        DrawStatusClock();
        if (state.PaletteDirty)
        {
            SetPalette(NormalPalette);
            state.PaletteDirty = false;
        }
        if ((state.ResultBits & RaidResults.Arrested) != 0)
        {
            state.Disguised = false;
        }
        CountDownAlert();
        if ((state.Rtime & 0x1f) == 0)
        {
            world.Input.SetNumLock(true);
            var room = state.Rm[GameLibrary.RandomBelow(state.Random, RaidState.MaxRooms)];
            if ((room.Status & RoomStatus.Gas) != 0)
            {
                room.Status &= ~RoomStatus.Gas;
                DrawMinimap(RadarLeft, RadarTop);
            }
        }
        ShowStatusBarColours();
    }

    /// <summary>The player has stepped into another room (frame_step).</summary>
    /// <returns>Whether the frame goes on (not when he left into the street).</returns>
    private bool EnterRoom()
    {
        if (state.InRoom != -1)
        {
            var left = state.Rm[state.InRoom];
            left.Status &= ~RoomStatus.Bodies;
            for (var a = 1; a < RaidState.Actors - 1; a++)
            {
                var guard = state.Bg[a];
                if (guard.Status != ActorFlags.None && guard.Stun > 999)
                {
                    guard.Status = ActorFlags.None;
                    left.Status |= RoomStatus.Bodies;
                }
            }
            if (state.OpenDoorCount > 0 || state.BodiesHere != 0)
            {
                left.Status |= RoomStatus.Bodies;
            }
        }
        state.OpenDoorCount = 0;
        state.InRoom = state.RoomAtCell[RaidState.Cell(state.Cx, state.Cy)];
        state.DisguiseStare = 0;
        state.Px = C16.Int((state.Cx << 4) - state.MoveX[state.PlayerFacing] + 8);
        // C: MoveX[facing + 9] lies past the table, on MoveY[facing]
        state.Py = C16.Int((state.Cy << 4) - state.MoveY[state.PlayerFacing] + 8);
        if (state.InRoom == 0)
        {
            state.LevelDone = 1;
            RestoreBackground(state.ActorSpriteX[0], state.ActorSpriteY[0], ActorPicture);
            return false;
        }
        DrawRoomView(state.InRoom);
        var room = state.Rm[state.InRoom];
        room.Status |= RoomStatus.Visited;
        DrawMinimap(RadarLeft, RadarTop);
        if (state.InRoom == state.MacGuff && state.Who != -1 && !state.Disguised)
        {
            GuardRules.RaiseAlert(state, state.InRoom, ArrestAlert);
        }
        state.BodiesHere = (short)(room.Status & RoomStatus.Bodies);
        return true;
    }

    /// <summary>The music of the frame by the alert and the alarmed guards; a new variant every 256 frames (frame_step).</summary>
    private void PlayFrameMusic()
    {
        state.SoundMode = MusicQuiet;
        if ((state.Rtime & 0xff) == 0)
        {
            state.SoundVariant = (short)(GameLibrary.RandomBelow(state.Random, 3) + 1);
        }
        if (state.Eseen <= 0)
        {
            if (state.AlarmGuardsPrev != 0)
            {
                state.SoundMode = (short)(state.Disguised ? MusicDisguised : MusicAlarmed);
            }
        }
        else
        {
            state.SoundMode = (short)(state.AlarmGuardsPrev != 0 ? MusicAlertAlarmed : MusicAlert);
        }
        DoSound(SoundFrame);
    }

    /// <summary>The alert counts down (it ends the disguise); at its end the target room's alert goes; it flashes every 16 frames, with the siren when high (frame_step).</summary>
    private void CountDownAlert()
    {
        if (state.Eseen == 0)
        {
            return;
        }
        state.Disguised = false;
        if (--state.Eseen == 0)
        {
            GuardRules.RaiseAlert(state, state.MacGuff, 0);
        }
        if ((state.Rtime & 0xf) == 0 && state.GameMode != RaidState.OneRoomMode)
        {
            SetPalette(AlertPalette);
            state.PaletteDirty = true;
            if (state.Eseen > 200)
            {
                DoSound(SoundSiren);
                state.SirenSounded = true;
            }
        }
    }

    /// <summary>The status bar's colours follow the disguise (not on CGA) and the room's gas (frame_step).</summary>
    private void ShowStatusBarColours()
    {
        var graphics = world.Graphics;
        if (state.DisguiseShown != state.Disguised && !state.CGA)
        {
            state.DisguiseShown = state.Disguised;
            if (state.Disguised)
            {
                graphics.RecolorRect(state.RpGuys, 0, StatusBarY, StatusBarWidth, StatusBarHeight, DisguiseColour, state.BuildingColor);
            }
            else
            {
                graphics.RecolorRect(state.RpGuys, 0, StatusBarY, StatusBarWidth, StatusBarHeight, state.BuildingColor, DisguiseColour);
            }
        }
        var gas = (short)(state.Rm[state.InRoom].Status & RoomStatus.Gas);
        if (gas != state.GasShown)
        {
            state.GasShown = gas;
            if (gas != 0)
            {
                graphics.RecolorRect(state.RpGuys, 0, StatusBarY, StatusBarWidth, StatusBarHeight, GasBlue, GasRed);
            }
            else
            {
                graphics.RecolorRect(state.RpGuys, 0, StatusBarY, StatusBarWidth, StatusBarHeight, GasRed, GasBlue);
            }
        }
    }

    /// <summary>
    /// The player's turn of a frame. The raid ends when he has more wounds than he can take; gas without the mask and
    /// being knocked out keep him down (keys are thrown away). A clip is loaded when the pistol is empty. One key (or
    /// the joystick) is acted on: speed, action, run, arrest (F1, Enter), camera (F2), bug (F3), use (F4), throw (F5..F7),
    /// traps (F8, F9), weapon (F10), sound, pause, quit, joystick, and the keypad's walk (with Shift or the arrows: a
    /// lunge, which needs room to land). He turns one eighth towards the wanted facing, walks unless a wall or a solid
    /// piece is in the way, and his state goes into actor slot 0. A guard he runs into is knocked out (and wounds him
    /// unless disguised; he then wears the guard's clothes and takes his grenades). The rescue and street raids end
    /// when no guard is awake or the time is up.
    /// </summary>
    // orig: TAC 00e8:0f4c update_player (src/c/tac/tac_00e8_0f4c.c) matched; tests: TacOracleTests
    // BUG: CA-1 the end of the rescue and street raids reads the count of awake guards; OriginalBehaviour.FixRaidGuardCountInCityCounter
    public void UpdatePlayer()
    {
        var player = state.Bg[0];
        if (WoundsAllowed() < state.Wounds)
        {
            state.LevelDone = 1;
        }
        if ((state.Rm[state.InRoom].Status & RoomStatus.Gas) != 0 && (state.Equip & GasMask) == 0)
        {
            player.Stun = C16.Int(player.Stun + 2);
        }
        if (player.Stun != 0)
        {
            player.Stun--;
            if (player.Stun > LyingDown)
            {
                player.Sighting = 0;
                player.Status |= ActorFlags.Sneak;
                state.Running = true;
                while (!world.Input.NoKeyWaiting())
                {
                    ReadKey();
                }
                if (player.Stun > KnockedOut)
                {
                    state.LevelDone = 1;
                }
                return;
            }
        }
        state.PlayerActed = false;
        if (state.Bullets == 0 && state.Clips != 0)
        {
            state.Bullets = ClipBullets;
            state.Clips--;
            DrawStatusPanel();
        }
        var joystickKey = ReadJoystickKey();
        if (!(world.Input.NoKeyWaiting() && joystickKey == -1 || state.LungeTimer != 0 || state.ThrowTimer != 0))
        {
            int key;
            if (joystickKey == -1)
            {
                key = ReadKey();
                state.WalkHold = (short)(key != state.LastKey ? 4 : 0);
            }
            else
            {
                key = joystickKey;
            }
            state.LastKey = (short)key;
            ActOnKey(key, joystickKey);
            if (state.GameMode == ArrivalMode && (state.PlayerState == Walking || state.PlayerState == Lunging))
            {
                GuardRules.RaiseAlert(state, state.InRoom, ArrestAlert);
                state.GameMode = 0;
            }
            if (state.PlayerState == Lunging)
            {
                PrepareLunge();
            }
        }
        else
        {
            state.LastKey = -1;
        }
        state.WalkHold--;
        if ((state.Status & Options.Joystick) == 0 || !Input.JoystickButtonDown(0))
        {
            Input.FlushInput();
        }
        TurnTowardsWantedFacing();
        if (state.PlayerState == Walking)
        {
            StopAtWalls();
        }
        Move();
        // the player's state is published in actor slot 0
        player.Px = state.Px;
        player.Py = state.Py;
        player.Dir = state.PlayerFacing;
        player.Fire = state.FireTimer;
        player.Mode = state.PlayerState;
        player.Pose = state.PlayerPose;
        player.Room = state.InRoom;
        // C: the sneak bit is cleared, then the run flag shifted into it is added
        player.Status &= ~ActorFlags.Sneak;
        player.Status += (short)((state.Running ? 1 : 0) << 2);
        if (state.FireTimer != 0)
        {
            state.FireTimer--;
        }
        KnockOutGuardInTheWay();
        if (state.Rtime > 0x40 && (state.GameMode == RaidState.OneRoomMode || state.GameMode == RescueMode)
            && (GuardRules.AwakeGuardCount(state, behaviour) == 0 || state.Rtime > 0xe10))
        {
            state.InRoom = 0;
            state.LevelDone = 1;
        }
    }

    /// <summary>The joystick's key, when the joystick is on and no key waits: its direction, and its buttons (update_player).</summary>
    /// <returns>The key, or -1 to let the keyboard decide.</returns>
    private int ReadJoystickKey()
    {
        var key = -1;
        if ((state.Status & Options.Joystick) == 0 || !world.Input.NoKeyWaiting())
        {
            return key;
        }
        int dx = 0, dy = 0;
        key = Input.ReadJoystickDir(ref dx, ref dy);
        if (Input.JoystickButtonDown(0))
        {
            if (state.GuardInRoom)
            {
                key = KeySpace;
            }
            else
            {
                key = KeyF1;
                Input.FlushInput();
            }
        }
        if (Input.JoystickButtonDown(1))
        {
            // the second button with a direction is that direction's arrow key with Shift: a lunge
            key = key == -1 ? KeyRun : state.JoyScancode[key] + 0x80;
            Input.FlushInput();
        }
        return key;
    }

    /// <summary>Acts on the key of the frame (update_player).</summary>
    private void ActOnKey(int key, int joystickKey)
    {
        switch (key)
        {
            case 'f':
                state.GameSpeed = 2;
                break;
            case 'd':
                state.GameSpeed = 1;
                break;
            case 's':
                state.GameSpeed = 0;
                break;
            case KeySpace:
                if (state.InRoom == state.MacGuff && state.Who != -1 && (state.ResultBits & RaidResults.Arrested) == 0)
                {
                    state.PlayerActed = true;
                }
                if (state.FireTimer < 4 && (joystickKey == -1 || state.FireTimer == 0))
                {
                    state.FireTimer += 4;
                }
                if (state.PlayerState != Frozen)
                {
                    state.PlayerState = Standing;
                }
                break;
            case KeyRun:
                state.PlayerState = Standing;
                state.Running = !state.Running;
                state.Bg[0].Status &= ~ActorFlags.Sneak;
                state.Bg[0].Status |= (ActorFlags)((state.Running ? 1 : 0) << 2);
                break;
            case KeyEnter:
            case KeyF1:
                if (state.GameMode != RaidState.OneRoomMode)
                {
                    ArrestPrompt();
                    state.PlayerState = AtArrest;
                    state.Running = false;
                }
                break;
            case KeyF2:
                state.PlayerActed = true;
                if ((state.Equip & Camera) != 0)
                {
                    TakePhoto();
                }
                break;
            case KeyF3:
                state.PlayerActed = true;
                PlantBug();
                break;
            case KeyF4:
                state.PlayerActed = true;
                UseObjectInFront();
                break;
            case KeyF5:
                Throw(1);
                break;
            case KeyF6:
                Throw(2);
                break;
            case KeyF7:
                Throw(3);
                break;
            case KeyF8:
                foreach (var grenade in state.Gr)
                {
                    if (grenade.Time == TrapWaiting)
                    {
                        grenade.Time = TrapFired;
                    }
                }
                break;
            case KeyF9:
                state.PlayerActed = true;
                if (state.Grenades[state.Gtype] > 0)
                {
                    FireProjectile(state.Px, state.Py, state.PlayerFacing, -1, state.Gtype);
                }
                break;
            case KeyF10:
                state.Gtype++;
                if (state.Gtype > 2)
                {
                    state.Gtype = 0;
                }
                DrawStatusPanel();
                break;
            case KeySound:
                if ((state.Status & Options.Sound) != 0)
                {
                    DoSound(0);
                }
                state.Status ^= Options.Sound;
                break;
            case KeyPause:
                SaveScreen();
                DoSound(SoundPause);
                state.Menu.MenuPauseMode = true;
                ShowMessageBox(state.PauseText, 0x50, 0x60);
                Screens.WaitForKey();
                DoSound(SoundResume);
                RestoreFullScreen();
                break;
            case KeyQuit:
                SaveScreen();
                if (ShowMessageBox(state.QuitText, 0x50, 0x50) == 1)
                {
                    ExitCleanup(true);
                    // C: exit() is called without its argument; the launcher reads the end of the game from the hand-off
                    throw new ProgramExitException(0);
                }
                RestoreFullScreen();
                break;
            case KeyJoystick:
                state.Status ^= Options.Joystick;
                if ((state.Status & Options.Joystick) != 0)
                {
                    world.Input.CalibrateJoystick();
                }
                break;
            case 0:
                state.PlayerState = Standing;
                state.WantedFacing = state.PlayerFacing;
                break;
            default:
                if (WalkFacing(key) is { } walk)
                {
                    state.WantedFacing = (short)walk;
                    state.PlayerState = Walking;
                }
                else if (LungeFacing(key) is { } lunge)
                {
                    state.WantedFacing = (short)lunge;
                    state.PlayerState = Lunging;
                }
                break;
        }
    }

    /// <summary>A throw (F5..F7): a grenade of the chosen kind, if one is left (the count goes down even when none is) (update_player).</summary>
    private void Throw(int how)
    {
        state.PlayerActed = true;
        // C: the count is tested before it goes down, so it falls below 0 when the kind is used up
        if (state.Grenades[state.Gtype]-- > 0)
        {
            FireProjectile(state.Px, state.Py, state.PlayerFacing, how, state.Gtype);
            state.ThrowTimer = 3;
        }
    }

    /// <summary>The facing a walking key asks for: the keypad's digits, its keys with Num Lock off, or the joystick's directions 1..8 (update_player).</summary>
    private static int? WalkFacing(int key) => key switch
    {
        8 or 0x37 or 0x4737 => 8,
        1 or 0x38 or 0x4838 => 1,
        2 or 0x39 or 0x4939 => 2,
        7 or 0x34 or 0x4b34 => 7,
        3 or 0x36 or 0x4d36 => 3,
        6 or 0x31 or 0x4f31 => 6,
        5 or 0x32 or 0x5032 => 5,
        4 or 0x33 or 0x5133 => 4,
        _ => null,
    };

    /// <summary>The facing a lunging key asks for: the arrow keys, or the joystick's second button with a direction (update_player).</summary>
    private static int? LungeFacing(int key) => key switch
    {
        0xc7 or 0x4700 => 8,
        0xc8 or 0x4800 => 1,
        0xc9 or 0x4900 => 2,
        0xcb or 0x4b00 => 7,
        0xcd or 0x4d00 => 3,
        0xcf or 0x4f00 => 6,
        0xd0 or 0x5000 => 5,
        0xd1 or 0x5100 => 4,
        _ => null,
    };

    /// <summary>
    /// A lunge looks 3 to 9 steps ahead for a place to land: it stops at the room's edge or at a tall piece; it may go on
    /// over bare floor or low pieces, each landing making it longer. Without a landing the player stands (update_player).
    /// </summary>
    private void PrepareLunge()
    {
        state.PlayerActed = true;
        state.LungeTimer = 0;
        for (var e = 3; e <= 9; e++)
        {
            // C: 16-bit products, divided as signed
            var x = C16.Div(C16.Int(state.MoveX[state.WantedFacing] * e + state.Px), 16);
            var y = C16.Div(C16.Int(state.MoveY[state.WantedFacing] * e + state.Py), 16);
            var cell = RaidState.Cell(x, y);
            if (state.RoomAtCell[cell] != state.InRoom)
            {
                state.LungeTimer = (short)(e / 2 + 4);
                break;
            }
            var content = state.CellMap[cell];
            if (content == RaidState.CellEmpty)
            {
                state.LungeTimer = (short)(e / 2 + 6);
                state.PlayerFacing = state.WantedFacing;
                continue;
            }
            var bits = TileBits(content & RaidState.CellTile);
            if ((bits & TileFlags.Tall) != 0)
            {
                break;
            }
            if ((bits & TileFlags.Solid) == 0)
            {
                state.LungeTimer = (short)(e / 2 + 6);
                state.PlayerFacing = state.WantedFacing;
            }
        }
        if (state.LungeTimer == 0)
        {
            state.PlayerState = Standing;
        }
        else
        {
            state.Running = true;
        }
    }

    /// <summary>The player turns one eighth of a circle towards the wanted facing, the shorter way (update_player).</summary>
    private void TurnTowardsWantedFacing()
    {
        if (state.WantedFacing == state.PlayerFacing)
        {
            return;
        }
        if ((C16.UChar(state.WantedFacing - state.PlayerFacing) & 7) < 4)
        {
            state.PlayerFacing++;
        }
        else
        {
            state.PlayerFacing--;
        }
        state.PlayerFacing = (short)(((state.PlayerFacing - 1) & 7) + 1);
    }

    /// <summary>
    /// A walking player stops at the room's walls (8 pixels from the edge; not while standing in an open door) and before
    /// a solid piece two steps ahead, across, down or both (update_player).
    /// </summary>
    private void StopAtWalls()
    {
        int dx = state.MoveX[state.PlayerFacing], dy = state.MoveY[state.PlayerFacing];
        var aheadX = C16.Div(C16.Int(dx * 2 + state.Px), 16);
        var aheadY = C16.Div(C16.Int(dy * 2 + state.Py), 16);
        var here = state.CellMap[RaidState.Cell(state.Cx, state.Cy)];
        var inOpenDoor = (here & RaidState.CellOpen) != 0 && (TileBits(here & RaidState.CellTile) & TileFlags.Door) != 0;
        if (!inOpenDoor && (state.RoomLeft + 8 > state.Px && dx < 0 || state.RoomRight - 8 < state.Px && dx > 0))
        {
            state.PlayerState = Standing;
        }
        if (aheadX != state.Cx && IsSolid(aheadX, state.Cy))
        {
            state.PlayerState = Standing;
        }
        if (!inOpenDoor && (state.RoomTop + 8 > state.Py && dy < 0 || state.RoomBottom - 8 < state.Py && dy > 0))
        {
            state.PlayerState = Standing;
        }
        if (aheadY != state.Cy && IsSolid(state.Cx, aheadY))
        {
            state.PlayerState = Standing;
        }
        if (aheadX != state.Cx && aheadY != state.Cy && IsSolid(aheadX, aheadY))
        {
            state.PlayerState = Standing;
        }
    }

    /// <summary>Whether the cell holds a solid piece (bare floor is not) (update_player).</summary>
    private bool IsSolid(int x, int y)
    {
        var tile = state.CellMap[RaidState.Cell(x, y)] & RaidState.CellTile;
        return tile != RaidState.CellTile && (state.Ob[tile].Bits & TileFlags.Solid) != 0;
    }

    /// <summary>The movement of the frame by the player's state, and his picture's frame (update_player).</summary>
    private void Move()
    {
        state.PlayerMoving = false;
        switch (state.PlayerState)
        {
            case Lunging:
                if (state.LungeTimer > 6)
                {
                    state.Px = C16.Int(state.Px + state.MoveX[state.PlayerFacing] * 2);
                    state.Py = C16.Int(state.Py + state.MoveY[state.PlayerFacing] * 2);
                }
                if (--state.LungeTimer == 0)
                {
                    state.PlayerState = Standing;
                }
                break;
            case Walking:
                state.PlayerMoving = true;
                if (state.WantedFacing == state.PlayerFacing)
                {
                    if (!state.Running || state.LungeTimer != 0)
                    {
                        state.Px = C16.Int(state.Px + state.MoveX[state.PlayerFacing]);
                        state.Py = C16.Int(state.Py + state.MoveY[state.PlayerFacing]);
                    }
                    else
                    {
                        state.Px = C16.Int(state.Px + state.Move8X[state.PlayerFacing]);
                        state.Py = C16.Int(state.Py + state.Move8Y[state.PlayerFacing]);
                        state.SneakSteps++;
                    }
                    if (state.WalkHold <= 0 && state.LungeTimer == 0)
                    {
                        state.PlayerState = Standing;
                    }
                }
                state.PlayerPose = (short)(state.PlayerPose + 1 & 3);
                break;
            case AtArrest:
                state.PlayerPose = 6;
                break;
            case Frozen:
                state.PlayerPose = FrameSneaking;
                break;
            default:
                state.PlayerPose = 0;
                if (state.MaxSeen != 0 && state.GameMode != ArrivalMode && !state.Disguised)
                {
                    state.PlayerPose = FrameAiming;
                }
                break;
        }
        if (state.Running)
        {
            state.PlayerPose = FrameSneaking;
        }
    }

    /// <summary>
    /// One guard a frame (in turn by the clock) that the player runs into is knocked out: it wounds the player if he
    /// was not disguised and the guard was awake; a dead one leaves the room's count of bodies; the player is disguised
    /// in his clothes and takes two of a random kind of his grenades (update_player).
    /// </summary>
    private void KnockOutGuardInTheWay()
    {
        var e = state.Rtime % 8 + 1;
        var guard = state.Bg[e];
        if ((guard.Status & ActorFlags.Active) == 0 || Math.Abs(state.Px - guard.Px) + Math.Abs(state.Py - guard.Py) >= ContactRange
            || guard.Stun <= 0x18 && state.Disguised)
        {
            return;
        }
        if (!state.Disguised && guard.Stun == 0)
        {
            state.Wounds++;
        }
        if (guard.Stun > 999 && --state.BodiesHere == 0)
        {
            state.Rm[state.InRoom].Status &= ~RoomStatus.Bodies;
        }
        guard.Status = ActorFlags.None;
        guard.Stun = 0;
        state.Disguised = true;
        var kind = GameLibrary.RandomBelow(state.Random, 3);
        state.Grenades[kind] = (short)GameLibrary.Clamp(state.Grenades[kind] + 2, 2, 8);
        DrawStatusPanel();
    }
}
