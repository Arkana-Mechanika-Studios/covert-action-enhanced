using CovertAction.Core;
using CovertAction.Core.Electronics;
using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Electronics;

/// <summary>The part's start and end: what the launcher's hand-off block asks for, and what the part hands back.</summary>
public sealed partial class ElectronicsWorkbench
{
    /// <summary>The "next part" code the hand-off carries when the board is the car tracer (the agent tails a car and plants it).</summary>
    private const sbyte CarTracerPart = 0x0c;

    /// <summary>The "next part" code a part hands back at the end of a practice session: the menus.</summary>
    private const sbyte MenusPart = 0x11;

    /// <summary>"Leave the game": what the hand-off's next-part byte holds while the part runs.</summary>
    private const sbyte LeaveGame = -1;

    /// <summary>The agent's electronics skill: bits 6 and 7 of the skills word, 0..3; each level takes one off the difficulty.</summary>
    private const int ElectronicsSkillShift = 6;

    /// <summary>The difficulties the puzzle knows: 0 (the easiest board) to 3.</summary>
    private const int EasiestLevel = 0, HardestLevel = 3;

    /// <summary>A wiretap has 10 minutes of game time at the lowest alert and 2 at a high one; the car tracer half of that.</summary>
    private const int LongestTime = 10, ShortestTime = 2;

    /// <summary>How much more alert the other side must be (in percent) to cost the player one minute of the time limit.</summary>
    private const int AlertPerMinute = 5;

    /// <summary>The picture sheet the popup frame and the building picture are cut from, and the pictures' sizes.</summary>
    private const int PopupFrameWidth = 0x92, PopupFrameHeight = 0x66, BuildingPictureX = 0xc8, BuildingPictureWidth = 0x48, BuildingPictureHeight = 0x3d;

    /// <summary>The font and the pen colour (white) the part's text starts with.</summary>
    private const int MenuFont = 1, White = 0xf;

    /// <summary>The page the board's picture is drawn on: the screen.</summary>
    private const int ScreenPage = 0;

    /// <summary>The sound effect number that stops the sound.</summary>
    private const int SoundStop = 0;

    /// <summary>The palette the part leaves the screen in.</summary>
    private const int EndPalette = 4;

    /// <summary>
    /// One session at the workbench, from the hand-off block to the hand-off block: takes the campaign over, sets the
    /// difficulty (lowered by the agent's electronics skill) and the time limit (shorter when the other side is
    /// alert, half as long for the car tracer), loads the pictures, builds the board and plays the puzzle until it is
    /// over; then hands back the share of phones tapped and the alarms, saves the campaign for the next part and asks
    /// for the disk the next part is on.
    /// </summary>
    // orig: BUG 00e8:0018 bug_init (src/c/bug/bug_00e8_0018.c) matched; tests: BugOracleTests
    public void BugInit()
    {
        // the original also attaches the three drivers (attach_driver_segment); the rewrite has none to attach
        // DIVERGENCE: D-2 the three attach_driver_segment calls (BUG 0000:05b9) that patch the driver thunks are left out.
        // DORMANT: the original sets one variable to 1 here and never reads it again
        state.StartMark = 1;
        // C: the original passes its own copy of the name; the library compares it by text with the hand-off's
        HandOff.ReadEnvState(state.HandOff.StrEnvSveRead);
        state.BaseLevel = state.Diff;
        var electronicsSkill = shared.SkillBits >> ElectronicsSkillShift & 3;
        state.Diff = (short)GameLibrary.Clamp(state.Diff - electronicsSkill + 1, EasiestLevel, HardestLevel);
        var minutes = GameLibrary.Clamp(LongestTime - shared.Alert / AlertPerMinute, ShortestTime, LongestTime);
        state.TimeLeft = (short)(minutes * SecondsPerMinute);
        if (shared.NextModule == CarTracerPart)
        {
            state.Mode = ElectronicsMode.CarTracer;
            state.TimeLeft /= 2;
        }
        shared.NextModule = LeaveGame;

        state.Rp1.FontNr = MenuFont;
        state.Rp1.BPen = White;
        world.Graphics.SetColorMaps(state.BoardColorMap, state.BoardColorMap);
        LoadPopupPictures();
        LoadBugsPic();
        world.Graphics.SetColorMaps(state.BoardColorMap, state.BoardColorMap);
        Pictures.LoadPictureSwapped(state.BoardSheetName, ScreenPage);
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, world.Timer);
        BuildCircuitBoard();
        Input.FlushInput();
        Input.BeginPointer();
        do
        {
            PuzzleInputStep();
        }
        while (state.DoneFlag == 0);

        var practice = state.Status.HasFlag(Options.Practice);
        // the hand-off's byte 8 holds the tapped share (five per cent per phone), byte 9 the alarms
        shared.Person = (sbyte)(5 * state.ResultPhones);
        shared.Value = (sbyte)state.AlarmCount;
        shared.NextModule = practice ? MenusPart : (sbyte)0;
        HandOff.WriteEnvState(state.HandOff.StrEnvSve);
        Output.DoSound(SoundStop);
        ShutdownSound();
        SelectPalette(EndPalette);
        // the next part is on disk 1 (the menus) or disk 2 (the campaign)
        Menus.CheckForDisk(practice ? 1 : 2, 1);
    }

    /// <summary>Cuts the intelligence popup's frame and the picture of a building out of camera.pic.</summary>
    private void LoadPopupPictures()
    {
        world.Graphics.BeginImageCuts();
        Pictures.LoadPictureSwapped(state.PopupSheetName, SheetPage);
        state.PopupPictures[0] = world.Graphics.CutImage(SheetPage, 0, 0, PopupFrameWidth, PopupFrameHeight);
        state.PopupPictures[1] = world.Graphics.CutImage(SheetPage, BuildingPictureX, 0, BuildingPictureWidth, BuildingPictureHeight);
        world.Graphics.EndImageCuts();
    }
}
