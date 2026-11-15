using System.Globalization;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Chase;

// The cockpit at the top right of the screen: the view through the windscreen of the car the keys steer (one of eight
// pictures of street.pic, which turn as the car drives), the closeness gauge, the speed, the clock, the auto-follow
// lamp, the alert bar and the tracking device's display.
public sealed partial class CarChase
{
    // the windscreen: 116 x 44 pixels at (201, 1); street.pic holds the eight pictures in cells of 125 x 47, four to a column
    private const int WindscreenX = 0xc9, WindscreenY = 1, WindscreenWidth = 0x74, WindscreenHeight = 0x2c;
    private const int WindscreenCellWidth = 0x7d, WindscreenCellHeight = 0x2f, WindscreenRows = 4;
    private const int ViewFrames = 8;

    // the bonnet under it, from chase.pic
    private const int BonnetX = 0xca, BonnetY = 0x2d, BonnetSourceY = 0x91, BonnetHeight = 0x18;

    /// <summary>A turn slides the windscreen picture between these offsets.</summary>
    private const int TurnSlideNear = 8, TurnSlideFar = 0x6c, TurnSlideSteps = 0x20;

    // the closeness gauge: a needle picture per distance, placed by the square of the closeness
    private const int GaugeReach = 0x3f;
    private const int NeedleTop = 0x13, NeedleCentre = 0x3d, NeedleScale = 0x14;
    private const int NeedlePictures = 16;

    // the texts and lamps of the dashboard
    private const int SpeedX = 0xe6, SpeedY = 0x4b, ClockX = 0x122, ClockY = 0x4b;
    private const int MphPerGear = 20;
    private const int AlertBarX = 0x120, AlertBarY = 0x54, AlertBarSourceY = 0xba, AlertBarLength = 0x1e, AlertBarHeight = 2;
    private const int AlertBarSteps = 10, AlertBarStep = 3, AlertBarScale = 5;
    private const int LampX = 0x113, LampY = 0x54, LampSourceY = 0xb9, LampSize = 10;
    private const int TrackerX = 0x111, TrackerY = 0x4a, TrackerSourceY = 0xaf, TrackerWidth = 0xe, TrackerHeight = 0xa;
    private const int TrackerRight = 0x118, TrackerLower = 0x4f, TrackerCorner = 4, TrackerCentreX = 0x116, TrackerCentreY = 0x4e;

    // palette indices of the dashboard
    private const int DashboardBackground = 5;
    private const int SpeedColor = 7;
    private const int ClockColor = 9;
    private const int AlertBarColor = 0xe;
    private const int LampColor = 7;
    private const int BlipColor = 0xb;

    private const int TurnSound = 0x17;

    /// <summary>
    /// Slides the windscreen picture sideways for a turn of the car the keys steer: to the right for a left turn, to
    /// the left for a right turn (a U-turn or no turn slides nothing), in steps of 32 / (game speed + 2) pixels, two
    /// display frames each, with the tyres' sound. The dialog colours are set for the time of the slide.
    /// </summary>
    /// <param name="oldDir">The heading before the turn (the original takes it as a byte).</param>
    /// <param name="newDir">The heading after it.</param>
    // orig: CHASE 00df:29d8 animate_turn (src/c/chase/chase_00df_29d8.c) matched; tests: ChaseOracleTests
    public void AnimateTurn(int oldDir, int newDir)
    {
        _world.Graphics.SetPalette(_state.PalettePlain);
        // C: 32 / (game_speed + 2) in 16 bits; a speed of -2 would stop the original with a divide error
        var step = C16.Div(TurnSlideSteps, C16.Int(_state.GameSpeed + 2));
        if (((oldDir - 1) & 3) == newDir)
        {
            DoSound(TurnSound);
            var offset = TurnSlideNear;
            do
            {
                DrawWindscreenSlid(offset);
                Output.WaitVsync(2);
                offset += step;
            }
            while (offset < WindscreenWidth);
        }
        if (((oldDir + 1) & 3) == newDir)
        {
            DoSound(TurnSound);
            var offset = TurnSlideFar;
            do
            {
                DrawWindscreenSlid(offset);
                Output.WaitVsync(2);
                offset -= step;
            }
            while (offset > TurnSlideNear);
        }
        _world.Graphics.SetPalette(_state.PaletteChase);
    }

    /// <summary>Draws the windscreen picture moved <paramref name="offset"/> pixels to the right, its right end coming in again at the left.</summary>
    private void DrawWindscreenSlid(int offset)
    {
        var (sourceX, sourceY) = WindscreenCell();
        _world.Graphics.CopyRect(_state.RpGrid, sourceX, sourceY, WindscreenWidth - offset, WindscreenHeight, _state.Rp1, offset + WindscreenX, WindscreenY);
        _world.Graphics.CopyRect(_state.RpGrid, sourceX - offset + WindscreenWidth, sourceY, offset, WindscreenHeight, _state.Rp1, WindscreenX, WindscreenY);
    }

    /// <summary>Where in street.pic the current windscreen picture lies.</summary>
    // C: view_frame / 4 and % 4 in 16 bits (a negative frame would pick a cell left of or above the sheet)
    private (int X, int Y) WindscreenCell() =>
        (C16.Div(_state.ViewFrame, WindscreenRows) * WindscreenCellWidth, C16.Rem(_state.ViewFrame, WindscreenRows) * WindscreenCellHeight);

    /// <summary>
    /// Redraws the cockpit: the windscreen and bonnet when the picture changed, the closeness gauge's needle while the
    /// quarry is in sight, the speed (20 mph a gear) and the clock every frame, and every fourth frame the alert bar,
    /// the auto-follow lamp and, for a car with a tracking device, the corner of the display the quarry lies towards
    /// (blinking with the car's own heading as a dot).
    /// </summary>
    /// <remarks>
    /// The needle's picture and height come from the distance in sight times 1.5: the closer, the higher the needle;
    /// when the two cars head different ways it also moves to the left. The alert bar is 3 pixels per half of the
    /// suspicion limit the current car has used up, 30 at most, and blinks when full.
    /// </remarks>
    // orig: CHASE 00df:2496 draw_dashboard (src/c/chase/chase_00df_2496.c) matched; tests: ChaseOracleTests
    public void DrawDashboard()
    {
        var state = _state;
        var current = state.Cars[state.CurCar];
        if (state.ViewFrame > ViewFrames - 1)
        {
            state.ViewFrame = 0;
        }
        if (state.ViewFrame != state.ShownViewFrame)
        {
            DrawWindscreen();
        }
        DrawGauge(current);
        state.ShownViewFrame = state.ViewFrame;

        state.MessageText.Text = state.SpeedPrefix + C16.Int(MphPerGear * current.Gear).ToString(CultureInfo.InvariantCulture);
        state.Rp1.BPen = DashboardBackground;
        Output.DrawText(state.MessageText.Text, SpeedX, SpeedY, SpeedColor);

        // C: 5 * alert / limit in 16 bits
        state.DashScratch = (short)(GameLibrary.Clamp(C16.Div(C16.Int(AlertBarScale * current.Alert), state.AlertLimit), 0, AlertBarSteps) * AlertBarStep);
        var everyFourth = (state.Frame & 3) == 0;
        if (everyFourth)
        {
            _world.Graphics.CopyRect(state.RpSprite, AlertBarX, AlertBarSourceY, AlertBarLength, AlertBarHeight, state.Rp1, AlertBarX, AlertBarY + 1);
            if (state.DashScratch != 0 && ((state.Frame & 7) == 0 || state.DashScratch < AlertBarLength))
            {
                _world.Graphics.RecolorRect(state.Rp1, AlertBarX, AlertBarY, state.DashScratch, AlertBarHeight, SpeedColor, AlertBarColor);
            }
        }
        Output.DrawText(ChaseRules.FormatGameTime(state, state.Frame), ClockX, ClockY, ClockColor);
        if (everyFourth)
        {
            _world.Graphics.CopyRect(state.RpSprite, LampX, LampSourceY, LampSize, LampSize, state.Rp1, LampX, LampY);
            if ((state.Frame & 4) == 0 && current.Flag)
            {
                _world.Graphics.RecolorRect(state.Rp1, LampX + 1, LampY + 1, 6, 7, 1, state.CGA ? White : LampColor);
            }
        }
        if (everyFourth)
        {
            _world.Graphics.CopyRect(state.RpSprite, TrackerX, TrackerSourceY, TrackerWidth, TrackerHeight, state.Rp1, TrackerX, TrackerY);
            if ((current.Attr & Car.Tracker) != 0)
            {
                DrawTracker(current);
            }
        }
    }

    /// <summary>Draws the windscreen picture and the bonnet under it.</summary>
    private void DrawWindscreen()
    {
        var (sourceX, sourceY) = WindscreenCell();
        _world.Graphics.CopyRect(_state.RpGrid, sourceX, sourceY, WindscreenWidth, WindscreenHeight, _state.Rp1, WindscreenX, WindscreenY);
        _world.Graphics.CopyRect(_state.RpSprite, BonnetX, BonnetSourceY, WindscreenWidth, BonnetHeight, _state.Rp1, BonnetX, BonnetY);
    }

    /// <summary>The closeness gauge (see <see cref="DrawDashboard"/>); it wipes a needle by drawing the windscreen again.</summary>
    private void DrawGauge(Car current)
    {
        var state = _state;
        state.GaugeDist = (short)ChaseRules.SightDistance(state, state.CurCar);
        if (state.GaugeDist != 0 && state.SightFlag)
        {
            // C: 16-bit sums and divisions; the clamps keep the squares inside 16 bits
            state.GaugeDist = C16.Int(state.GaugeDist + C16.Div(state.GaugeDist, 2));
            state.DashScratch = (short)GameLibrary.Clamp(C16.Div(state.GaugeDist, 4) - 2, 0, NeedlePictures - 1);
            state.GaugeDepth = (short)(GaugeReach - GameLibrary.Clamp(state.GaugeDist, 0, GaugeReach));
            var closeness = (state.GaugeDepth * state.GaugeDepth >> 2) * NeedleScale;
            state.NeedleY = (short)((closeness >> 10) + NeedleTop);
            state.NeedleX = NeedleCentre;
            if (current.Dir != state.Cars[0].Dir)
            {
                state.NeedleX -= (short)(closeness >> 7);
            }
            if (state.NeedleY != state.ShownNeedleY || state.ViewFrame != state.ShownViewFrame)
            {
                if (state.ViewFrame == state.ShownViewFrame)
                {
                    DrawWindscreen();
                }
                var picture = state.GaugeRects[state.DashScratch];
                // C: the picture's width less one, halved in 16 bits
                var halfWidth = C16.Div(C16.Int(picture.X2 - picture.X1), 2);
                _world.Graphics.DrawImageUntracked(state.GaugeSheet, state.NeedleX - halfWidth, state.NeedleY, state.GaugeImages[state.DashScratch]);
            }
            state.ShownNeedleY = state.NeedleY;
        }
        else
        {
            if (state.ShownNeedleY != -1 && state.ViewFrame == state.ShownViewFrame)
            {
                DrawWindscreen();
            }
            state.ShownNeedleY = -1;
        }
    }

    /// <summary>
    /// The tracking device's display: on frames with bit 2 set, the corners towards the quarry light up (on a line with
    /// it, two corners); on the others, a dot for the car's own heading.
    /// </summary>
    private void DrawTracker(Car current)
    {
        var state = _state;
        if ((state.Frame & 4) != 0)
        {
            var quarry = state.Cars[0];
            state.QuarryDx = C16.Int(quarry.X - current.X);
            state.QuarryDy = C16.Int(quarry.Y - current.Y);
            if (state.QuarryDx >= 0 && state.QuarryDy <= 0)
            {
                LightCorner(TrackerRight, TrackerY);
            }
            if (state.QuarryDx >= 0 && state.QuarryDy >= 0)
            {
                LightCorner(TrackerRight, TrackerLower);
            }
            if (state.QuarryDx <= 0 && state.QuarryDy <= 0)
            {
                LightCorner(TrackerX, TrackerY);
            }
            if (state.QuarryDx <= 0 && state.QuarryDy >= 0)
            {
                LightCorner(TrackerX, TrackerLower);
            }
        }
        else
        {
            PlotPixel(state.DirDx[current.Dir] + TrackerCentreX, state.DirDy[current.Dir] + TrackerCentreY, ClockColor);
        }
    }

    private void LightCorner(int x, int y) =>
        _world.Graphics.RecolorRect(_state.Rp1, x, y, TrackerCorner, TrackerCorner, DashboardBackground, BlipColor);
}
