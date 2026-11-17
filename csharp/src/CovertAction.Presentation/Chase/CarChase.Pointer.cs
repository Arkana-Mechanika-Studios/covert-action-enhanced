using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Chase;

// The mouse in the car chase (D-670, a player option on top of the mouse option, off by default): a click stands for one
// of the keys the chase takes, and ends in TakeKey as that key; the mouse gives the player nothing a key does not.
public sealed partial class CarChase
{
    /// <summary>How near (in map pixels) a click must be to a car's or a location's dot to be on it.</summary>
    private const int DotReach = 3;

    // the dashboard's hot spots (the clock's text is five characters of six pixels)
    private const int ClockWidth = 30, ClockHeight = 8;

    /// <summary>
    /// The key a mouse click of the frame stands for, or -1 for none (the click, if any, is used up):
    /// a right click is the arrest key (A: the arrest when a tail car meets the quarry head-on, else the entry, as the key);
    /// on the map or the street view, a left click on the other tail car's dot is Space (switch car), on a location's dot is E
    /// (enter the building), elsewhere it is the arrow of the side of the steered car the click lies on (the turn at the
    /// next crossing); on the windscreen it is = (gear up), on the bonnet - (gear down), on the auto-follow lamp F10, on the
    /// clock the next of the game speeds S, D, F.
    /// </summary>
    /// <remarks>
    /// New code. Every click is read once a frame, also before the tail cars start (it is then dropped, as the keys are).
    /// The positions tested are the ones drawn on the screen; nothing is told that the picture does not show.
    /// </remarks>
    // DIVERGENCE: D-670 the mouse in the car chase is a player option; the original's chase never reads the mouse.
    private int ReadChaseMouseKey()
    {
        if (Input.ReadPointerClick() is not { } click)
        {
            return -1;
        }
        if (click.Right)
        {
            return KeyArrest;
        }
        var state = _state;
        var flee = state.ChaseMode == ChaseMode.Flee;
        var current = state.Cars[state.CurCar];
        var (x, y) = (click.X, click.Y);
        if (x < MapSize)
        {
            if (!flee)
            {
                for (var c = 1; c < ChaseState.CarCount; c++)
                {
                    if (c != state.CurCar && OnMapDot(x, y, state.Cars[c].X, state.Cars[c].Y))
                    {
                        return Keys.Space;
                    }
                }
            }
            for (var location = 1; location < ChaseState.Locations; location++)
            {
                if (LocationShown(location) && OnMapDot(x, y, state.LocX[location], state.LocY[location]))
                {
                    return KeyEnter;
                }
            }
            return ArrowToward(x - MapPixel(current.X) - 1, y - MapPixel(current.Y) - 1);
        }
        if (y >= StreetViewY)
        {
            // the steered car's place in the street view, as DrawViewCars puts it
            return ArrowToward(x - (current.X - state.CamX + StreetViewX), y - (current.Y - state.CamY + StreetViewY));
        }
        if (new HitArea(WindscreenX, WindscreenY, WindscreenWidth, WindscreenHeight).Contains(x, y))
        {
            return KeyGearUp;
        }
        if (new HitArea(BonnetX, BonnetY, WindscreenWidth, BonnetHeight).Contains(x, y))
        {
            return KeyGearDown;
        }
        if (new HitArea(LampX, LampY, LampSize, LampSize).Contains(x, y))
        {
            return Keys.F10;
        }
        if (new HitArea(ClockX, ClockY, ClockWidth, ClockHeight).Contains(x, y))
        {
            // slow, medium, fast, slow, ...
            return state.GameSpeed switch
            {
                0 => KeyMedium,
                1 => KeyFast,
                _ => KeySlow,
            };
        }
        return -1;
    }

    /// <summary>The arrow key of the side a vector from the steered car points to, or -1 when the click is on the car.</summary>
    private static int ArrowToward(int dx, int dy) => PointerHit.Direction4(dx, dy) switch
    {
        Directions.Up => Keys.Up,
        Directions.Right => KeyRight,
        Directions.Down => Keys.Down,
        Directions.Left => KeyLeft,
        _ => -1,
    };

    /// <summary>Whether a map pixel is on the dot a world position has on the city map.</summary>
    private static bool OnMapDot(int x, int y, int worldX, int worldY) =>
        Math.Abs(x - MapPixel(worldX) - 1) <= DotReach && Math.Abs(y - MapPixel(worldY) - 1) <= DotReach;

    /// <summary>Whether the map shows a location: the player knows it (the stand-alone chase shows all), as the entry test counts it.</summary>
    private bool LocationShown(int location) =>
        (!_state.CampaignMode && _behaviour.CutContent.StandaloneChase) || (_state.Ct[_state.InCity].OrgClues & 1 << location) != 0;
}
