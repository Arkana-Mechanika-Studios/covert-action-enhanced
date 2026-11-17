using System.Reflection;
using CovertAction.Core;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Chase;
using CovertAction.Presentation.Library;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The mouse in the car chase, a player option on top of the mouse option (docs/faithfulness/DIVERGENCES.md D-670;
/// csharp/docs/MOUSE-SUPPORT.md section 7): off, the chase asks the mouse driver nothing; on, each click is the key it stands
/// for (the chase's own key codes), so the game input is the key's.
/// </summary>
public sealed class ChaseMouseTests
{
    // the chase's key codes (CarChase.Tick.cs)
    private const int KeyRight = 0x4d00, KeyLeft = 0x4b00;

    // the steered car 1 stands at world (400, 400): map pixel (100, 100); the camera's corner is (350, 350)
    private const int CarX = 400, CarY = 400, CamCorner = 350;

    private static (CarChase Chase, ChaseState State, ClickMouse Mouse) Chase(bool mouseChase = true, bool installed = true, ChaseMode mode = ChaseMode.Tail)
    {
        var state = new ChaseState { ChaseMode = mode, InCity = 0, CurCar = 1, CamX = CamCorner, CamY = CamCorner };
        state.Cars[1].X = CarX;
        state.Cars[1].Y = CarY;
        state.Cars[2].X = 200;
        state.Cars[2].Y = 200;
        state.LocX[3] = 600;
        state.LocY[3] = 600;
        state.Ct[0].OrgClues = 1 << 3;
        var mouse = new ClickMouse(installed);
        var world = new Boundaries(null!, null!, null!, mouse, null!, null!, mouseChase: mouseChase);
        return (new CarChase(state, new SharedState(), world, OriginalBehaviour.Faithful), state, mouse);
    }

    private static int Click(CarChase chase, ClickMouse mouse, int x, int y, bool right = false)
    {
        mouse.Next = (right, x, y);
        return (int)typeof(CarChase).GetMethod("ReadChaseMouseKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(chase, null)!;
    }

    [Fact]
    public void WithoutTheOptionTheChaseNeverAsksTheMouse()
    {
        // the chase option off, the mouse on: the chase's gate is shut
        var (chase, _, mouse) = Chase(mouseChase: false);
        Assert.False(chase.Input.ChasePointerOn);
        // the mouse option off (no mouse driver with a mouse), the chase option on: the same
        var (noMouse, _, silent) = Chase(installed: false);
        Assert.False(noMouse.Input.ChasePointerOn);
        Assert.Null(noMouse.Input.ReadPointerClick());
        Assert.Equal(0, silent.Calls);
        Assert.Equal(0, mouse.Calls);
        Assert.True(Chase().Chase.Input.ChasePointerOn);
    }

    [Fact]
    public void ClicksOnTheMapAndTheStreetViewAreTheArrowKeysOfTheSideOfTheCar()
    {
        var (chase, _, mouse) = Chase();
        Assert.Equal(KeyRight, Click(chase, mouse, 150, 101));
        Assert.Equal(KeyLeft, Click(chase, mouse, 40, 99));
        Assert.Equal(Keys.Up, Click(chase, mouse, 101, 20));
        Assert.Equal(Keys.Down, Click(chase, mouse, 101, 190));
        // on the car itself: no key
        Assert.Equal(-1, Click(chase, mouse, 101, 101));
        // the street view: the car is drawn at (400 - 350 + 200, 400 - 350 + 100)
        Assert.Equal(KeyRight, Click(chase, mouse, 290, 150));
        Assert.Equal(KeyLeft, Click(chase, mouse, 210, 150));
        Assert.Equal(Keys.Up, Click(chase, mouse, 250, 105));
        Assert.Equal(Keys.Down, Click(chase, mouse, 250, 195));
    }

    [Fact]
    public void TheOtherTailCarsDotIsSpaceAndAKnownLocationsDotIsTheEnterKey()
    {
        var (chase, state, mouse) = Chase();
        Assert.Equal(Keys.Space, Click(chase, mouse, 51, 51));
        // the car the keys steer is no switch
        Assert.Equal(Keys.Up, Click(chase, mouse, 101, 40));
        Assert.Equal('e', Click(chase, mouse, 151, 151));
        // a location the player does not know is not on the map
        state.Ct[0].OrgClues = 0;
        Assert.Equal(Keys.Down, Click(chase, mouse, 151, 151));
        // fleeing has no other car to switch to
        var (fleeing, _, fleeMouse) = Chase(mode: ChaseMode.Flee);
        Assert.NotEqual(Keys.Space, Click(fleeing, fleeMouse, 51, 51));
    }

    [Fact]
    public void TheCockpitsPartsAreTheGearsTheLampAndTheSpeedKeys()
    {
        var (chase, state, mouse) = Chase();
        Assert.Equal('=', Click(chase, mouse, 250, 20));
        Assert.Equal('-', Click(chase, mouse, 250, 0x2d + 6));
        Assert.Equal(Keys.F10, Click(chase, mouse, 0x113 + 3, 0x54 + 3));
        state.GameSpeed = 0;
        Assert.Equal('d', Click(chase, mouse, 0x122 + 3, 0x4b + 2));
        state.GameSpeed = 1;
        Assert.Equal('f', Click(chase, mouse, 0x122 + 3, 0x4b + 2));
        state.GameSpeed = 2;
        Assert.Equal('s', Click(chase, mouse, 0x122 + 3, 0x4b + 2));
        // the dashboard elsewhere does nothing
        Assert.Equal(-1, Click(chase, mouse, 318, 10));
    }

    [Fact]
    public void ARightClickIsTheArrestKeyWhereverItIs()
    {
        var (chase, _, mouse) = Chase();
        Assert.Equal('a', Click(chase, mouse, 100, 150, right: true));
        Assert.Equal('a', Click(chase, mouse, 250, 20, right: true));
        // no click, no key
        Assert.Equal(-1, (int)typeof(CarChase).GetMethod("ReadChaseMouseKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(chase, null)!);
    }

    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(1, -1, -1)]
    [InlineData(5, 0, 1)]
    [InlineData(-5, 1, 3)]
    [InlineData(0, -5, 0)]
    [InlineData(1, 5, 2)]
    [InlineData(4, 4, 2)]
    [InlineData(-4, -4, 0)]
    public void TheSideIsTheLongerPartOfTheVectorAndATieIsVertical(int dx, int dy, int expected)
        => Assert.Equal(expected, PointerHit.Direction4(dx, dy));

    /// <summary>A mouse driver whose right or left press the test sets for the next read.</summary>
    private sealed class ClickMouse(bool installed) : IMouseDriver
    {
        public int Calls { get; private set; }

        public (bool Right, int X, int Y)? Next { get; set; }

        public bool Installed => installed;

        public void Reset() => Calls++;

        public void ShowCursor() => Calls++;

        public void HideCursor() => Calls++;

        public MouseStatus GetStatus()
        {
            Calls++;
            return default;
        }

        public void SetPosition(int x, int y) => Calls++;

        public MouseButtonEvents ButtonPresses(int button)
        {
            Calls++;
            if (Next is { } click && click.Right == (button == 1))
            {
                Next = null;
                return new MouseButtonEvents(1 << button, 1, click.X * 2, click.Y);
            }
            return default;
        }

        public MouseButtonEvents ButtonReleases(int button)
        {
            Calls++;
            return default;
        }

        public void SetHorizontalRange(int minimum, int maximum) => Calls++;

        public void SetVerticalRange(int minimum, int maximum) => Calls++;
    }
}
