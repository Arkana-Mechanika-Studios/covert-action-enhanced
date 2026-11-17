using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Chase;

// One frame of the chase: the player's key, then each car's step, what the quarry notices, and the choice of way at
// a tile centre; then the street view and the cockpit.
public sealed partial class CarChase
{
    /// <summary>The frame at which the tail cars start their engines; keys are taken only after it.</summary>
    private const int StartFrame = 0x20;

    // sound effects of the chase
    private const int SoundStop = 0, SoundFirstGear = 0x16, SoundShiftUp = 0x18, SoundShiftDown = 0x19;
    private const int SoundEngineCar1 = 0x1a, SoundEngineCar2 = 0x1b, SoundPause = 0x25, SoundResume = 0x26;

    // the chase's keys that the shared key codes do not name
    private const int KeyFast = 'f', KeyMedium = 'd', KeySlow = 's', KeyGearUp = '=', KeyGearDown = '-';
    private const int KeyArrest = 'a', KeyEnter = 'e', KeyF2 = 0x3c00, KeyRight = 0x4d00, KeyLeft = 0x4b00;

    /// <summary>Where the Alt-Q question opens.</summary>
    private const int QuitBoxX = 0xca, QuitBoxY = 4;

    /// <summary>Where the street view lies on the screen (and on the off-screen page).</summary>
    private const int StreetViewX = 200, StreetViewY = 100;

    // what the quarry notices
    private const int CloseSight = 0x20;
    private const int RamDistance = 8;
    private const int ArrivalFrames = 0x100, ArrivalFramesLessPerLevel = 0x40;
    private const int WatchFramesToEnd = 0x10;
    private const int TurnTries = 9;

    // map dot colours
    private const int DotErased = 5, DotOwnCar = 10, DotSeen = 0xd, DotLastSeen = 7;

    /// <summary>
    /// One frame of the chase. First, after frame 32, the key the player pressed: arrows queue a turn of the car the
    /// keys steer, = and - change its gear, F10 lights its auto-follow lamp, space switches tail car, F1 makes the
    /// arrest when a tail car meets the quarry head-on (else like F2), F2 goes into the building next to car 0, Alt-Q,
    /// Alt-S and Alt-P quit, switch sound and pause, S, D and F set the game's speed, Esc gives up a tailing chase.
    /// Then every car takes its step (<see cref="MoveCar"/>); then, unless it has arrived, a quarry that is very
    /// suspicious and seen by nobody gets away; last the street view and the cockpit are drawn.
    /// </summary>
    /// <remarks>At frame 32 of a tailing chase the tail cars start in gear 2, with their engines' sounds.</remarks>
    // orig: CHASE 00df:0dc6 chase_tick (src/c/chase/chase_00df_0dc6.c) matched; tests: ChaseOracleTests
    public void ChaseTick()
    {
        var state = _state;
        var cars = state.Cars;
        var tailing = state.ChaseMode != ChaseMode.Flee;
        if (tailing && state.Frame == StartFrame)
        {
            cars[1].Gear = cars[2].Gear = 2;
            DoSound(SoundFirstGear);
            state.SoundParam = cars[state.CurCar].Gear;
            DoSound(SoundEngineCar2);
            DoSound(SoundEngineCar1);
            Input.FlushInput();
            Input.BeginPointer();
        }
        // C: the driver is asked first, the frame tested after
        var keyWaiting = !_world.Input.NoKeyWaiting();
        if (keyWaiting && state.Frame > StartFrame && TakeKey())
        {
            return;
        }
        // DIVERGENCE: D-670 with the car-chase mouse option on (and the mouse option), when no key waits, a click is the key it stands for.
        if (!keyWaiting && Input.ChasePointerOn && ReadChaseMouseKey() is var mouseKey && mouseKey != -1 && state.Frame > StartFrame && TakeKey(mouseKey))
        {
            return;
        }

        state.RamReady = false;
        for (var d = 0; d < ChaseState.CarCount; d++)
        {
            MoveCar(d);
        }
        // C: alert_limit * 2 in 16 bits
        if (tailing && !cars[0].Flag && C16.Int(state.AlertLimit * 2) < cars[0].Alert && state.ArrivalTimer == 0)
        {
            state.DoneFlag = ChaseState.DoneEscaped;
        }
        DrawStreetView(state.CurCar, StreetViewX, StreetViewY);
        DrawDashboard();
    }

    /// <summary>Reads and carries out one key (see <see cref="ChaseTick"/>).</summary>
    /// <param name="mouseKey">The key a mouse click stands for (D-670), or -1: the key the driver has.</param>
    /// <returns>True when the frame ends at once (an arrest, Esc).</returns>
    private bool TakeKey(int mouseKey = -1)
    {
        var state = _state;
        var cars = state.Cars;
        var flee = state.ChaseMode == ChaseMode.Flee;
        switch (mouseKey != -1 ? mouseKey : Input.ReadKey())
        {
            case KeyFast:
                state.GameSpeed = 2;
                break;
            case KeyMedium:
                state.GameSpeed = 1;
                break;
            case KeySlow:
                state.GameSpeed = 0;
                break;
            case Keys.F10:
                cars[state.CurCar].Flag = true;
                break;
            case Keys.Up:
                QueueTurn(Directions.Up);
                break;
            case KeyRight:
                QueueTurn(Directions.Right);
                break;
            case Keys.Down:
                QueueTurn(Directions.Down);
                break;
            case KeyLeft:
                QueueTurn(Directions.Left);
                break;
            case KeyGearUp:
                GearUp(cars[state.CurCar], flee);
                break;
            case KeyGearDown:
                GearDown(cars[state.CurCar]);
                break;
            case Keys.Space:
                if (!flee)
                {
                    state.CurCar++;
                    if (state.CurCar >= ChaseState.CarCount)
                    {
                        state.CurCar = 1;
                    }
                    state.ViewFrame = (short)((state.ViewFrame + 4) & 7);
                }
                PlayEngine();
                break;
            case Keys.F1:
            case KeyArrest:
                if (state.RamReady)
                {
                    state.Outcome = ChaseOutcome.Arrest;
                    state.DoneFlag = ChaseState.DoneNormal;
                    return true;
                }
                EnterBuilding();
                break;
            case KeyF2:
            case KeyEnter:
                EnterBuilding();
                break;
            case Keys.AltQ:
                if (Menus.QuitPrompt(state.StrQuitConfirm2, QuitBoxX, QuitBoxY) == 1)
                {
                    ChaseRules.SetLeaveGame(_shared, true);
                    // DIVERGENCE: D-401 exit() is called without an exit code (a leftover word of the stack goes to DOS); here 0.
                    throw new ProgramExitException(0);
                }
                break;
            case Keys.AltS:
                if (state.Status.HasFlag(Options.Sound))
                {
                    DoSound(SoundStop);
                }
                state.Status ^= Options.Sound;
                break;
            case Keys.AltP:
                DoSound(SoundPause);
                Input.ReadKey();
                DoSound(SoundResume);
                break;
            case Keys.Esc:
                if (!flee)
                {
                    state.DoneFlag = ChaseState.DoneNormal;
                    return true;
                }
                break;
        }

        // a U-turn is made at once by a car with excellent handling, and forgotten by any other
        var current = cars[state.CurCar];
        if (((current.Dir + 2) & 3) == current.Turn)
        {
            if ((current.Attr & Car.Handling) != 0)
            {
                DoSound(TurnSound);
                current.Dir = current.Turn;
            }
            current.Turn = Directions.None;
        }
        return false;
    }

    /// <summary>An arrow key: the car the keys steer turns at the next tile centre, and its auto-follow lamp goes out.</summary>
    private void QueueTurn(int dir)
    {
        var car = _state.Cars[_state.CurCar];
        car.Turn = (short)dir;
        car.Flag = false;
    }

    /// <summary>The = key: one gear up, at most the car's top gear (2 when fleeing).</summary>
    private void GearUp(Car car, bool flee)
    {
        if (car.Gear == 0)
        {
            DoSound(SoundFirstGear);
        }
        else if (ShiftUpSounds(car))
        {
            DoSound(SoundShiftUp);
        }
        car.Gear = (short)GameLibrary.Clamp(car.Gear + 1, 0, car.TopGear);
        if (flee && car.Gear > 2)
        {
            car.Gear = 2;
        }
        PlayEngine();
    }

    /// <summary>Whether shifting up from the car's gear makes the shift-up sound.</summary>
    private bool ShiftUpSounds(Car car)
    {
        // BUG: CA-26 the original tests (attributes + 2) & 3 where the top gear is meant: for the fastest cars (speed class 2) the test becomes "0 > gear" and the sound never plays. OriginalBehaviour.FixChaseGearUpSound
        if (_behaviour.FixChaseGearUpSound)
        {
            return car.TopGear > car.Gear;
        }
        else
        {
            return ((car.Attr + 2) & 3) > car.Gear;
        }
    }

    /// <summary>The - key: one gear down, at most to gear 3.</summary>
    private void GearDown(Car car)
    {
        if (car.Gear != 0)
        {
            DoSound(SoundShiftDown);
        }
        car.Gear = (short)GameLibrary.Clamp(car.Gear - 1, 0, 3);
        PlayEngine();
    }

    /// <summary>The engine's sound of the car the keys steer, at its gear.</summary>
    private void PlayEngine()
    {
        _state.SoundParam = _state.Cars[_state.CurCar].Gear;
        DoSound(_state.CurCar == 1 ? SoundEngineCar1 : SoundEngineCar2);
    }

    /// <summary>F2: when car 0 stands at a building the player knows, the chase ends there.</summary>
    private void EnterBuilding()
    {
        if (ChaseRules.CheckLocationEntry(_state, _shared, _state.Cars[0].X, _state.Cars[0].Y, _behaviour))
        {
            _state.DoneFlag = ChaseState.DoneNormal;
            _state.Outcome = ChaseOutcome.Lost;
        }
    }

    /// <summary>
    /// One car's step in a frame: its dot on the map is wiped; it drives a unit when its gear's pattern says so; the
    /// car the player does not drive refreshes its mark when seen and counts down the quarry's arrival; the quarry
    /// notices the car (<see cref="Watch"/>); and at a tile centre the car chooses its way.
    /// </summary>
    private void MoveCar(int d)
    {
        var state = _state;
        var car = state.Cars[d];
        // the car the player does not drive is shown only where it was last seen
        var opposing = state.ChaseMode == ChaseMode.Flee ? d != 0 : d == 0;
        if (!opposing)
        {
            PlotMapDot(car.X, car.Y, DotErased);
        }
        else
        {
            PlotMapDot(car.MarkX, car.MarkY, DotErased);
        }

        var moved = false;
        // C: 1 << frame % 6, a 16-bit remainder and shift
        if ((state.SpeedPattern[car.Gear] & C16.Shl(1, C16.Rem(state.Frame, 6))) != 0)
        {
            car.X += state.DirDx[car.Dir];
            car.Y += state.DirDy[car.Dir];
            moved = true;
            if (!opposing && car.X == car.MarkX && car.Y == car.MarkY)
            {
                car.MarkX = -1;
            }
            if (d == state.CurCar)
            {
                state.ViewFrame = (short)((state.ViewFrame + 1) & 7);
            }
        }

        if (!opposing)
        {
            if (d != state.CurCar || (state.Frame & 2) != 0)
            {
                PlotMapDot(car.X, car.Y, DotOwnCar);
            }
        }
        else
        {
            state.AlertRatio = C16.Div(car.Alert, state.AlertLimit);
            if (car.Flag || state.MarkFollowAll)
            {
                car.MarkX = car.X;
                car.MarkY = car.Y;
                car.Turn = car.Dir;
            }
            if (state.ChaseMode != ChaseMode.Flee || car.Flag)
            {
                PlotMapDot(car.MarkX, car.MarkY, car.Flag ? DotSeen : DotLastSeen);
            }
            car.Flag = false;
            // (fleeing, both pursuers count the timer down: twice a frame)
            if (state.ArrivalTimer != 0)
            {
                state.ArrivalTimer--;
                if (state.ArrivalTimer == 0)
                {
                    state.DoneFlag = ChaseState.DoneNormal;
                }
            }
            if (state.ChaseMode != ChaseMode.Flee && car.X == state.DestX && car.Y == state.DestY && state.ArrivalTimer == 0)
            {
                // the quarry has arrived: it stops, and the chase ends after 256 - 64 x difficulty frames
                car.Gear = 0;
                state.ArrivalTimer = C16.Int(ArrivalFrames - (state.Diff << 6));
            }
        }

        Watch(d);

        if (moved && car.X % ChaseState.TileUnits == 0 && car.Y % ChaseState.TileUnits == 0)
        {
            ChaseRules.OnTileCentre();
            int tileX = C16.Div(car.X, ChaseState.TileUnits), tileY = C16.Div(car.Y, ChaseState.TileUnits);
            if (d == 0)
            {
                if (state.ChaseMode == ChaseMode.Flee)
                {
                    SteerFleeingPlayer(car, tileX, tileY);
                }
                else
                {
                    SteerQuarry(car, tileX, tileY);
                }
            }
            else
            {
                var dir = state.ChaseMode == ChaseMode.Flee ? PursuerDirection(car, tileX, tileY) : TailDirection(d, car, tileX, tileY);
                if (ChaseRules.CanMove(state, tileX, tileY, dir) && Reverse(car) != dir)
                {
                    if (d == state.CurCar)
                    {
                        AnimateTurn(car.Dir, dir);
                    }
                }
                else
                {
                    dir = OpenDirectionFrom(car, tileX, tileY, GameLibrary.RandomBelow(state.Random, 4));
                }
                car.Dir = (short)dir;
            }
        }
    }

    /// <summary>
    /// What happens between the quarry and car <paramref name="d"/> in this frame. A tail car in the quarry's sight
    /// raises its own suspicion (more when close, fast or conspicuous), and one the quarry has watched too long raises
    /// the quarry's (which then speeds up, and flees above the limit); after the arrival, a tail car in sight has
    /// seen the quarry go in. A car meeting the quarry head-on within 8 units makes the arrest possible (fleeing: the
    /// pursuer has caught the player). Fleeing, a pursuer in sight speeds up with time; one out of sight drives at
    /// gear 3 and is seen when it comes close. Out of sight, a tail car's suspicion falls by one.
    /// </summary>
    private void Watch(int d)
    {
        var state = _state;
        var cars = state.Cars;
        var car = cars[d];
        var flee = state.ChaseMode == ChaseMode.Flee;
        var distance = ChaseRules.SightDistance(state, d);
        if (d != 0 && distance != 0)
        {
            if (!flee)
            {
                if (car.Gear != 0)
                {
                    car.Alert++;
                    if (distance <= CloseSight)
                    {
                        car.Alert++;
                    }
                    if (car.Gear > 2)
                    {
                        car.Alert++;
                    }
                    // conspicuousness Low -1, Moderate 0, High +1, Extreme +2
                    car.Alert = C16.Int(car.Alert + car.Conspicuousness - 1);
                }
                cars[0].Flag = true;
                if (car.Alert > C16.Int(state.AlertLimit * 2))
                {
                    cars[0].Alert++;
                    if (C16.Div(state.AlertLimit, 2) < cars[0].Alert && state.ArrivalTimer == 0)
                    {
                        cars[0].Gear = 3;
                    }
                    if (cars[0].Alert > state.AlertLimit && state.ArrivalTimer == 0)
                    {
                        cars[0].Gear = 4;
                    }
                }
                if (state.ArrivalTimer != 0 && !flee)
                {
                    state.Outcome = ChaseOutcome.Spotted;
                    state.WatchCount++;
                    if (state.WatchCount > WatchFramesToEnd)
                    {
                        state.DoneFlag = ChaseState.DoneNormal;
                    }
                }
            }
            if (!flee && state.ArrivalTimer == 0 && cars[0].Gear < car.Gear)
            {
                // the quarry keeps up with a faster car in its sight
                cars[0].Gear = car.Gear;
            }
            if (Reverse(cars[0]) == car.Dir && C16.Int(C16.Abs(C16.Int(car.X - cars[0].X)) + C16.Abs(C16.Int(car.Y - cars[0].Y))) <= RamDistance)
            {
                state.RamReady = true;
                if (flee)
                {
                    // C: outcome = done_flag = 1: caught, and the chase ends
                    state.Outcome = ChaseOutcome.Spotted;
                    state.DoneFlag = ChaseState.DoneNormal;
                }
            }
            if (flee)
            {
                car.MarkX = car.X;
                car.MarkY = car.Y;
                car.Flag = true;
                car.Gear = (short)GameLibrary.Clamp((state.Frame >> 3) - d, 0, distance < CloseSight ? 2 : 3);
            }
        }
        else
        {
            if (d != 0 && car.Alert > 0)
            {
                car.Alert--;
            }
            if (d != 0 && flee)
            {
                var alternate = (state.Frame & 0x10 << d) != 0;
                car.Gear = 3;
                if (state.Diff == 0)
                {
                    car.Gear = (short)(alternate ? 2 : 3);
                }
                if (state.Diff == 3)
                {
                    car.Gear = (short)(alternate ? 3 : 4);
                }
                var apart = C16.Int(C16.Abs(C16.Int(cars[0].X - car.X)) + C16.Abs(C16.Int(cars[0].Y - car.Y)));
                // seen from (5 - difficulty) x 16 units
                if (C16.Div(C16.Int((5 - state.Diff) << 5), 2) >= apart)
                {
                    car.MarkX = car.X;
                    car.MarkY = car.Y;
                    car.Flag = true;
                }
            }
        }
    }

    /// <summary>
    /// The player's own car when fleeing, at a tile centre: it takes the queued turn when the way is open (with the
    /// windscreen's slide), and when it cannot drive on it takes a random open way.
    /// </summary>
    private void SteerFleeingPlayer(Car car, int tileX, int tileY)
    {
        var state = _state;
        if (car.Turn != Directions.None && ChaseRules.CanMove(state, tileX, tileY, car.Turn))
        {
            if (state.CurCar == 0 && car.Dir != car.Turn)
            {
                AnimateTurn(car.Dir, car.Turn);
            }
            car.Dir = car.Turn;
            car.Turn = Directions.None;
        }
        if (!ChaseRules.CanMove(state, tileX, tileY, car.Dir))
        {
            var dir = OpenDirectionFrom(car, tileX, tileY, GameLibrary.RandomBelow(state.Random, 4));
            if (state.CurCar == 0)
            {
                AnimateTurn(car.Dir, dir);
            }
            car.Dir = (short)dir;
        }
    }

    /// <summary>
    /// The quarry at a tile centre: back to gear 2 unless it has arrived; it heads for its destination along x or y
    /// (at random, or the other when blocked); the more suspicious, the likelier a random way instead; then the tail
    /// cars that see it learn where it is going (the follow point, two units ahead of it).
    /// </summary>
    private void SteerQuarry(Car quarry, int tileX, int tileY)
    {
        var state = _state;
        if (state.ArrivalTimer == 0)
        {
            quarry.Gear = 2;
        }
        var toX = C16.Int(state.DestX - quarry.X);
        var toY = C16.Int(state.DestY - quarry.Y);
        int dir;
        // C: random_below(2) is drawn first, whatever the distances
        if ((GameLibrary.RandomBelow(state.Random, 2) != 0 || toY == 0) && toX != 0)
        {
            dir = toX > 0 ? Directions.Right : Directions.Left;
            if (!ChaseRules.CanMove(state, tileX, tileY, dir))
            {
                dir = toY > 0 ? Directions.Down : Directions.Up;
            }
        }
        else
        {
            dir = toY > 0 ? Directions.Down : Directions.Up;
            if (!ChaseRules.CanMove(state, tileX, tileY, dir))
            {
                dir = toX > 0 ? Directions.Right : Directions.Left;
            }
        }
        if (quarry.Alert > GameLibrary.RandomBelow(state.Random, C16.Int(state.AlertLimit * 4)))
        {
            dir = GameLibrary.RandomBelow(state.Random, 4);
        }
        quarry.Dir = (short)OpenDirectionFrom(quarry, tileX, tileY, dir);
        for (var tail = 1; tail < ChaseState.CarCount; tail++)
        {
            if (ChaseRules.SightDistance(state, tail) != 0)
            {
                SetFollowPoint();
            }
        }
    }

    /// <summary>
    /// A pursuer at a tile centre: it works out where the player is relative to the player's heading (ahead and to the
    /// side) and tries to cut the player off; exactly in line, it swerves to alternate sides.
    /// </summary>
    /// <exception cref="UninitialisedReadException">The player's heading is not 0..3: the original then uses two values it never set.</exception>
    private int PursuerDirection(Car car, int tileX, int tileY)
    {
        var state = _state;
        var player = state.Cars[0];
        var dx = C16.Int(player.X - car.X);
        var dy = C16.Int(player.Y - car.Y);
        // ahead: how far the player is ahead of the pursuer along the player's heading; side: across it
        var (ahead, side) = player.Dir switch
        {
            Directions.Up => (C16.Neg(dy), (int)dx),
            Directions.Down => (dy, C16.Neg(dx)),
            Directions.Right => (dx, dy),
            Directions.Left => (C16.Neg(dx), C16.Neg(dy)),
            // DORMANT: the player's heading is always 0..3
            _ => throw new UninitialisedReadException("chase_tick's offsets to the player, for a heading other than 0..3"),
        };
        var straight = (int)player.Dir;
        var sideways = (side > 0 ? player.Dir + 1 : player.Dir - 1) & 3;
        int dir;
        if (side == 0)
        {
            dir = ((state.WeaveToggle++ & 1) * 2 + player.Dir - 1) & 3;
            if (Reverse(car) == dir || !ChaseRules.CanMove(state, tileX, tileY, dir))
            {
                dir = straight;
            }
            if (!ChaseRules.CanMove(state, state.DirDx[dir] + tileX, state.DirDy[dir] + tileY, player.Dir))
            {
                dir = straight;
            }
            if (ahead < 0)
            {
                dir = (player.Dir + 2) & 3;
            }
            if (ahead > CloseSight)
            {
                dir = straight;
            }
        }
        else if (C16.Neg(C16.Abs((short)GameLibrary.RandomBelow(state.Random, side))) < ahead)
        {
            dir = straight;
            if (Reverse(car) == dir || !ChaseRules.CanMove(state, tileX, tileY, dir))
            {
                dir = sideways;
            }
        }
        else
        {
            dir = sideways;
            if (Reverse(car) == dir || !ChaseRules.CanMove(state, tileX, tileY, dir))
            {
                dir = straight;
            }
        }
        return dir;
    }

    /// <summary>
    /// A tail car at a tile centre: it takes a queued turn when the way is open; with its auto-follow lamp lit it
    /// steers to the follow point (with a tracking device, at the quarry itself when the follow point is stale); and
    /// one that sees the quarry updates the follow point.
    /// </summary>
    private int TailDirection(int d, Car car, int tileX, int tileY)
    {
        var state = _state;
        var cars = state.Cars;
        int dir = car.Dir;
        if (car.Turn != Directions.None)
        {
            if (ChaseRules.CanMove(state, tileX, tileY, car.Turn))
            {
                dir = car.Turn;
                car.Turn = Directions.None;
            }
        }
        else if (car.Flag)
        {
            // the follow point is kept in cars[1]'s mark
            int toX = C16.Int(cars[1].MarkX - car.X), toY = C16.Int(cars[1].MarkY - car.Y);
            if ((car.Attr & Car.Tracker) != 0)
            {
                int quarryX = GameLibrary.Sign(C16.Int(cars[0].X - car.X)), quarryY = GameLibrary.Sign(C16.Int(cars[0].Y - car.Y));
                if (GameLibrary.Sign(toX) != quarryX || GameLibrary.Sign(toY) != quarryY)
                {
                    // aim at the quarry, a little to one side at random
                    toX = quarryX * 4;
                    toY = quarryY * 4 - GameLibrary.RandomBelow(state.Random, 2) * 2 + 1;
                }
            }
            if (toX != 0 || toY != 0)
            {
                int alongX = toX > 0 ? Directions.Right : Directions.Left, alongY = toY > 0 ? Directions.Down : Directions.Up;
                // C: abs of 16-bit values
                var mostlyX = C16.Abs((short)toX) > C16.Abs((short)toY);
                dir = mostlyX ? alongX : alongY;
                if (Reverse(car) == dir || !ChaseRules.CanMove(state, tileX, tileY, dir))
                {
                    dir = mostlyX ? alongY : alongX;
                }
                if (Reverse(car) == dir)
                {
                    dir = car.Dir;
                }
            }
        }
        if (ChaseRules.SightDistance(state, d) != 0)
        {
            SetFollowPoint();
        }
        return dir;
    }

    /// <summary>The follow point: two units ahead of the quarry, kept in <c>cars[1]</c>'s mark.</summary>
    private void SetFollowPoint()
    {
        var cars = _state.Cars;
        cars[1].MarkX = C16.Int((_state.DirDx[cars[0].Dir] << 1) + cars[0].X);
        cars[1].MarkY = C16.Int((_state.DirDy[cars[0].Dir] << 1) + cars[0].Y);
    }

    /// <summary>
    /// The first open way from <paramref name="dir"/> on: while the way is closed, or (up to nine times) it is a
    /// U-turn, another way is drawn at random.
    /// </summary>
    private int OpenDirectionFrom(Car car, int tileX, int tileY, int dir)
    {
        var uTurns = 0;
        // C: the U-turn counter only counts when the way is open (the || stops at a closed way)
        while (!ChaseRules.CanMove(_state, tileX, tileY, dir) || (Reverse(car) == dir && ++uTurns < TurnTries))
        {
            dir = GameLibrary.RandomBelow(_state.Random, 4);
        }
        return dir;
    }

    /// <summary>The heading opposite a car's.</summary>
    private static int Reverse(Car car) => (car.Dir + 2) & 3;
}
