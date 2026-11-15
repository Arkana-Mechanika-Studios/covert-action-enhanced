using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Chase;

// The car chase from start to end: the hand-off, the pictures, the offer, the frames, the result.
public sealed partial class CarChase
{
    // the hand-off's "next part" codes that start a chase, and the ones the chase hands back
    private const sbyte FollowCar = 3, FollowCarToo = 0xe, VariantChase = 5, Followed = 6;
    private const sbyte CampaignPart = 0, MenusPart = 0x11;

    // the skills word: the driving skill in bits 2-3 lowers the difficulty, the game speed is kept in bits 10-11
    private const int DrivingSkillShift = 2, GameSpeedShift = 10;
    private const int GameSpeedBit11 = 0x0800, GameSpeedBits = 0x0c00;

    private const sbyte CgaLetter = (sbyte)'C';
    private const int SheetPage = 3, WindscreenPage = 2;
    private const int MenuFont = 1, ChaseFont = 2, TitleBackground = 0xf;
    private const int ResultBoxX = 0x64, ResultBoxY = 0x50;
    private const int MinutesPerHour = 60, HoursPerDay = 24, NightEnds = 8, NightBegins = 20;
    private const int FramesPerMinute = 60;
    private const int AlertPercentPerRatio = 25;

    /// <summary>
    /// The car chase: the part the campaign starts when the player follows someone who left a building, or is
    /// followed. Takes over the campaign, sets the difficulty (the level less the driving skill), the speed, night
    /// and the display, loads chase.pic (and cuts the 16 gauge needles from it) and street.pic, lets the player pick
    /// two cars when tailing, builds the city and runs a frame at a time until the chase is over; then shows the
    /// result and hands back the outcome, the quarry's suspicion as a percentage and the time taken.
    /// </summary>
    /// <remarks>
    /// The frames are 8, 4 or 0 display frames apart (slow, medium, fast). The quarry's sex (bit 0 of the hand-off's
    /// result byte) picks "he" or "she" in the result. A variant chase (code 5) writes no result text: the box shows
    /// whatever the text buffer held.
    /// </remarks>
    /// <exception cref="ProgramExitException">The player quit the whole game (Alt-Q), or a file could not be read.</exception>
    // orig: CHASE 00df:001c main (src/c/chase/chase_00df_001c.c) matched; tests: ChaseOracleTests
    // Other code under the same name: COVERT 0000:0010 (not this method; docs/source-map/shared-functions.csv).
    public void Main()
    {
        var state = _state;
        var shared = _shared;
        ChaseRules.ImportSharedState(state, shared);
        switch (shared.NextModule)
        {
            case FollowCar:
            case FollowCarToo:
                state.ChaseMode = ChaseMode.Tail;
                break;
            case VariantChase:
                state.ChaseMode = ChaseMode.Variant;
                break;
            case Followed:
                state.ChaseMode = ChaseMode.Flee;
                break;
        }
        int quarrySex = shared.Result;
        shared.NextModule = -1;
        shared.NotHurt = 1;
        state.GameSpeed = (short)(state.Skills >> GameSpeedShift & 3);
        state.Diff = (short)GameLibrary.Clamp(state.Diff - (state.Skills >> DrivingSkillShift & 3) + 1, 0, 3);
        state.AlertLevel = shared.Alert;
        // C: the clock is unsigned here
        var hour = C16.URem(C16.UDiv(C16.Unsigned(state.Gtime), MinutesPerHour), HoursPerDay);
        state.Night = hour < NightEnds || hour >= NightBegins;
        state.GfxLetter = C16.Char(shared.GfxLetter);
        state.CGA = state.GfxLetter == CgaLetter;
        // DIVERGENCE: D-2 the original attaches the graphics, input and sound drivers here (attach_driver_segment three times).

        LoadSheets();
        state.CampaignMode = true;
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, _world.Timer);
        _world.Graphics.SetPalette(state.PaletteChase);
        if (state.ChaseMode != ChaseMode.Flee)
        {
            ChaseDashboard();
        }
        state.Rp1.FontNr = ChaseFont;
        ChaseRules.MapStub();
        GenerateMap();
        ChaseRules.InitCars(state);
        do
        {
            ChaseTick();
            state.Frame++;
            Output.WaitVsync(state.GameSpeed == 1 ? 4 : state.GameSpeed != 0 ? 0 : 8);
        }
        while (state.DoneFlag == ChaseState.Running);
        DoSound(SoundStop);

        WriteResult(quarrySex);
        _world.Graphics.SetPalette(state.PalettePlain);
        state.Rp1.FontNr = MenuFont;
        Menus.QuitPrompt(state.MessageText, ResultBoxX, ResultBoxY);
        HandBack();
        // DIVERGENCE: D-402 the original gives the memory of the cut images back to DOS (_dos_freemem of gauge_images[0]); images are objects here.
        ChaseRules.SetLeaveGame(shared, false);
        _world.Graphics.FreePageBuffer(SheetPage);
        Menus.CheckForDisk(state.Status.HasFlag(Options.Practice) ? 1 : 2, 1);
    }

    /// <summary>Loads chase.pic onto page 3 (into memory of its own) with the 16 gauge needles cut from it, and street.pic onto page 2.</summary>
    private void LoadSheets()
    {
        var state = _state;
        state.ChasePicBuffer = (short)_world.Graphics.AllocPageBuffer(SheetPage);
        _world.Graphics.SetPageBuffer(SheetPage, state.ChasePicBuffer);
        state.Rp1.FontNr = MenuFont;
        state.Rp1.BPen = TitleBackground;
        _world.Graphics.BeginImageCuts();
        Pictures.LoadPicture(SheetPage, state.StrChasePic);
        for (var i = 0; i < state.GaugeImages.Length; i++)
        {
            var rect = state.GaugeRects[i];
            state.GaugeImages[i] = _world.Graphics.CutImage(SheetPage, rect.X1, rect.Y1, rect.X2 - rect.X1 + 1, rect.Y2 - rect.Y1 + 1);
        }
        _world.Graphics.EndImageCuts();
        Pictures.LoadPicture(WindscreenPage, state.StrStreetPic);
    }

    /// <summary>Puts the result's text into the text buffer: lost, got away, arrived, caught, or arrested.</summary>
    private void WriteResult(int quarrySex)
    {
        var state = _state;
        var male = (quarrySex & 1) != 0;
        var text = state.MessageText;
        if (state.Outcome == ChaseOutcome.Lost)
        {
            if (state.ChaseMode == ChaseMode.Tail)
            {
                text.Text = state.DoneFlag != ChaseState.DoneEscaped
                    ? state.StrLostTrack
                    : (male ? state.StrHeTurns : state.StrSheTurns) + state.StrTurnsCorner + state.StrLessObvious;
            }
            if (state.ChaseMode == ChaseMode.Flee)
            {
                text.Text = state.StrDashIntoBuilding;
            }
        }
        if (state.Outcome == ChaseOutcome.Spotted)
        {
            if (state.ChaseMode == ChaseMode.Tail)
            {
                text.Text = (male ? state.StrHePullsOver : state.StrShePullsOver) + state.StrPullsOver;
            }
            if (state.ChaseMode == ChaseMode.Flee)
            {
                text.Text = state.StrRunOffRoad;
            }
        }
        if (state.Outcome == ChaseOutcome.Arrest)
        {
            text.Text = state.StrYouRun + (male ? state.StrHis : state.StrHer) + state.StrCarOffRoad;
        }
    }

    /// <summary>Writes the chase's answer into the hand-off: outcome, suspicion in percent, the next part, the time taken, the game speed.</summary>
    private void HandBack()
    {
        var state = _state;
        var shared = _shared;
        shared.Value = C16.Char((int)state.Outcome);
        shared.Result = C16.Char(GameLibrary.Clamp(C16.Int(AlertPercentPerRatio * state.AlertRatio), 0, 100));
        shared.NextModule = state.Status.HasFlag(Options.Practice) ? MenusPart : CampaignPart;
        shared.NotHurt = 1;
        shared.Elapsed = C16.Char(C16.Div(state.Frame, FramesPerMinute));
        // BUG: CA-5 the original clears only bit 11 of the two speed bits before writing the speed back. OriginalBehaviour.FixChaseGameSpeedWriteBack
        if (_behaviour.FixChaseGameSpeedWriteBack)
        {
            state.Skills = (short)(state.Skills & ~GameSpeedBits | state.GameSpeed << GameSpeedShift);
        }
        else
        {
            state.Skills = (short)(state.Skills & ~GameSpeedBit11 | state.GameSpeed << GameSpeedShift);
        }
        shared.SkillBits = state.Skills;
    }
}
