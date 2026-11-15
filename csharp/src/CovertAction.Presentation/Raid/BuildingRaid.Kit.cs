using CovertAction.Core.Raid;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Raid;

/// <summary>The equipment screen: the player picks the kit for a break-in.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The items on the screen are numbered 1 to 11; the player may take five of them.</summary>
    private const int PicksAllowed = 5, FirstItem = 1;

    /// <summary>The screen's background colour when it only shows the fixed kit of a special raid, and the cursor's colours.</summary>
    private const int KitScreenColour = 5, CursorFrom = 1, CursorTo = 0xb;

    /// <summary>The equipment screen's status panel is moved this far to the right.</summary>
    private const int PanelShift = 6;

    /// <summary>The sounds: the screen opens (0x27) and closes (1).</summary>
    private const int SoundKitOpens = 0x27, SoundKitCloses = 1;

    /// <summary>How long the cursor's highlight is shown before a key is looked for, in vertical retraces.</summary>
    private const int CursorWait = 0xf;

    /// <summary>The bugs a kit with the bug bag holds, and the grenades of a full load and of a spare load.</summary>
    private const int BugBag = 10, FullGrenades = 8, SpareGrenadeCount = 2, Rounds = 6, SpareClips = 3;

    /// <summary>The joystick's buttons stand for Space (take the item) and Esc (leave).</summary>
    private const int ButtonA = 0, ButtonB = 1;

    /// <summary>Which neighbour of an item the cursor moves to.</summary>
    private enum Step
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// The cursor's steps for a key: the eight directions of the raid's keys (1 north, clockwise) and the arrow, page and
    /// home/end keys. A diagonal is two steps, taken in the order the original takes them.
    /// </summary>
    private static Step[] StepsOf(int key) => key switch
    {
        1 or Keys.Up => [Step.Up],
        2 or 0x4900 => [Step.Right, Step.Up],
        3 or 0x4d00 => [Step.Right],
        4 or 0x5100 => [Step.Down, Step.Right],
        5 or Keys.Down => [Step.Down],
        6 or 0x4f00 => [Step.Left, Step.Down],
        7 or 0x4b00 => [Step.Left],
        8 or 0x4700 => [Step.Up, Step.Left],
        _ => [],
    };

    private int Neighbour(int item, Step step) => step switch
    {
        Step.Up => state.EquipNeighbours[item].Up,
        Step.Down => state.EquipNeighbours[item].Down,
        Step.Left => state.EquipNeighbours[item].Left,
        _ => state.EquipNeighbours[item].Right,
    };

    /// <summary>
    /// The equipment screen. Loads its picture and cuts the items' pictures out of it, readies the counters, and draws the
    /// radar. When the kit is fixed (a special raid) it ends there with the screen painted in one colour. Otherwise the
    /// player moves a cursor over the items (keypad, arrows or joystick), takes or gives back an item with Enter or Space
    /// (five may be taken) and leaves with Esc. The kit is the bits of <see cref="RaidState.Equip"/>; the bug bag, the
    /// grenades and the status panel follow each pick.
    /// </summary>
    /// <param name="chooseKit">Whether the player chooses; false for the fixed kits of the special raids.</param>
    // orig: TAC 00e8:79ea equipment_screen (src/c/tac/tac_00e8_79ea.c) matched; tests: TacOracleTests
    public void EquipmentScreen(bool chooseKit)
    {
        world.Graphics.BeginImageCuts();
        Pictures.LoadPictureSwapped(state.EquipmentSheetName, DriverWorkPage);
        for (var item = 0; item < RaidState.ItemIconCount; item++)
        {
            var area = state.EquipSourceAreas[item];
            state.ItemIcons[item] = world.Graphics.CutImage(DriverWorkPage, area.X1, area.Y1, area.Width, area.Height);
        }
        world.Graphics.EndImageCuts();
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        ShiftPanelPlaces();
        state.Bullets = Rounds;
        state.Clips = SpareClips;
        state.Rm[0].Status |= RoomStatus.Visited;
        DrawMinimap(RadarLeft, RadarTop);
        if (!chooseKit)
        {
            world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, KitScreenColour);
            return;
        }

        DoSound(SoundKitOpens);
        var picksLeft = PicksAllowed;
        state.Equip = 1;
        var cursor = FirstItem;
        DrawStatusPanel();
        do
        {
            var area = state.EquipAreas[cursor];
            world.Graphics.RecolorRect(state.Rp1, area.X1, area.Y1, area.Width, area.Height, CursorFrom, CursorTo);
            Output.WaitVsync(CursorWait);
            var key = WaitForKitKey();
            var previous = cursor;
            foreach (var step in StepsOf(key))
            {
                cursor = Neighbour(cursor, step);
            }
            if (key is Keys.Return or Keys.Space)
            {
                ToggleItem(cursor, ref picksLeft);
            }
            else if (key == Keys.Esc)
            {
                picksLeft = -1;
            }
            var old = state.EquipAreas[previous];
            var picture = (state.Equip & 1 << previous) != 0 ? state.SavedScreen : state.RpBack;
            world.Graphics.CopyRect(picture, old.X1, old.Y1, old.Width, old.Height, state.Rp1, old.X1, old.Y1);
        }
        while (picksLeft != -1);
        DoSound(SoundKitCloses);
    }

    /// <summary>
    /// The equipment screen moves the 13 places of the status panel's icons (the equipment's eight, the grenades' three and
    /// the rounds' and clips' two, which follow each other in the program's data) 6 pixels to the right.
    /// </summary>
    private void ShiftPanelPlaces()
    {
        foreach (var place in state.EquipIconPos.Concat(state.AmmoBarPos).Concat(state.RoundIconPos))
        {
            place.X += PanelShift;
        }
    }

    /// <summary>Waits for a key of the keyboard, or of the joystick when it is on and no key is waiting (button A is Space, button B Esc).</summary>
    private int WaitForKitKey()
    {
        int key;
        do
        {
            key = -1;
            if (state.Status.HasFlag(Options.Joystick) && !world.Input.Kbhit())
            {
                int dx = 0, dy = 0;
                key = Input.ReadJoystickDir(ref dx, ref dy);
                if (Input.JoystickButtonDown(ButtonA))
                {
                    key = Keys.Space;
                }
                if (Input.JoystickButtonDown(ButtonB))
                {
                    key = Keys.Esc;
                }
            }
            else
            {
                key = ReadKey();
            }
        }
        while (key == -1);
        return key;
    }

    /// <summary>
    /// Takes the item under the cursor, or gives it back; then works the kit out again: the lowest bit stands for a plain
    /// pistol (no Uzi), the bug bag holds 10 bugs, the grenades are two of each kind or a full eight of a kind, and the
    /// status panel is drawn again.
    /// </summary>
    /// <param name="item">The item, 1..11.</param>
    /// <param name="picksLeft">How many items the player may still take.</param>
    private void ToggleItem(int item, ref int picksLeft)
    {
        var area = state.EquipAreas[item];
        if ((state.Equip & 1 << item) != 0)
        {
            state.Equip &= (short)~(1 << item);
            world.Graphics.CopyRect(state.RpBack, area.X1, area.Y1, area.Width, area.Height, state.Rp1, area.X1, area.Y1);
            picksLeft++;
        }
        else if (picksLeft != 0)
        {
            state.Equip |= (short)(1 << item);
            world.Graphics.CopyRect(state.SavedScreen, area.X1, area.Y1, area.Width, area.Height, state.Rp1, area.X1, area.Y1);
            picksLeft--;
        }
        // the lowest bit is the opposite of the Uzi's; bit 15 (the floor plan) is never taken
        state.Equip = (short)(((state.Equip & Equipment.Uzi) == 0 ? 1 : 0) + (state.Equip & Equipment.ChosenMask));
        state.Bugs = (short)((state.Equip & 1 << Equipment.BugsBit) != 0 ? BugBag : 0);
        for (var kind = 0; kind < state.Grenades.Length; kind++)
        {
            state.Grenades[kind] = (short)((state.Equip & 1 << Equipment.SpareGrenades) != 0 ? SpareGrenadeCount : 0);
        }
        for (var kind = 0; kind < state.Grenades.Length; kind++)
        {
            if ((state.Equip & 1 << (Equipment.GrenadeKinds + kind)) != 0)
            {
                state.Grenades[kind] = FullGrenades;
            }
        }
        DrawStatusPanel();
    }
}
