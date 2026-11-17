using CovertAction.Platform;
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>
/// The PC's analog joystick played with the first controller SDL finds: a gamepad's left stick (or its directional
/// pad) is the stick, its bottom, right, left and top face buttons are the joystick's four buttons; a plain joystick
/// gives its first two axes and first four buttons. The controller's position becomes the counts a real PC joystick
/// gives the game port (<see cref="JoystickReading.FromStick"/>), so the original's own calibration and scaling run on
/// it in the input driver. Without a controller the port reads as the PC's without a joystick.
/// </summary>
/// <remarks>
/// A controller plugged in or pulled out while the game runs is taken or let go at the next events (hot-plug). The
/// game asks for the joystick only when the player chose it at the set-up screen ("1. Joystick and Keyboard") or
/// switched it on with Alt-J, which calibrates it with the stick where it is: the stick should be at rest then.
/// </remarks>
internal sealed class SdlJoystick : IDisposable
{
    /// <summary>The part of a stick's travel around the middle that counts as the middle (a stick at rest is seldom exactly there).</summary>
    private const double DeadZone = 0.15;

    /// <summary>SDL's full deflection of an axis.</summary>
    private const double AxisRange = 32768;

    /// <summary>The joystick's buttons, at most four: the game port has four button bits.</summary>
    private const int PortButtons = 4;

    private readonly bool _available;
    private IntPtr _gamepad;
    private IntPtr _joystick;

    /// <summary>Starts SDL's joystick and gamepad support and takes the first controller connected, if any.</summary>
    public SdlJoystick()
    {
        // a failure leaves the game without a joystick, as on a PC without one
        _available = SdlNative.SDL_InitSubSystem(SdlNative.InitJoystick | SdlNative.InitGamepad);
        Refresh();
    }

    /// <summary>The controller in use, for the trace: its name, or "none".</summary>
    public string Description =>
        SdlNative.Text(_gamepad != IntPtr.Zero ? SdlNative.SDL_GetGamepadName(_gamepad)
            : _joystick != IntPtr.Zero ? SdlNative.SDL_GetJoystickName(_joystick) : IntPtr.Zero) ?? "none";

    /// <summary>Whether an event is a controller plugged in or pulled out.</summary>
    public static bool IsDeviceEvent(uint type) => type is SdlNative.EventJoystickAdded or SdlNative.EventJoystickRemoved;

    /// <summary>After a controller came or went: lets go of one that is gone, and takes the first one when none is held.</summary>
    public void Refresh()
    {
        if (!_available)
        {
            return;
        }
        if (_gamepad != IntPtr.Zero && !SdlNative.SDL_GamepadConnected(_gamepad))
        {
            SdlNative.SDL_CloseGamepad(_gamepad);
            _gamepad = IntPtr.Zero;
        }
        if (_joystick != IntPtr.Zero && !SdlNative.SDL_JoystickConnected(_joystick))
        {
            SdlNative.SDL_CloseJoystick(_joystick);
            _joystick = IntPtr.Zero;
        }
        if (_gamepad != IntPtr.Zero || _joystick != IntPtr.Zero)
        {
            return;
        }
        foreach (var id in SdlNative.Joysticks())
        {
            if (SdlNative.SDL_IsGamepad(id) && (_gamepad = SdlNative.SDL_OpenGamepad(id)) != IntPtr.Zero)
            {
                return;
            }
            if ((_joystick = SdlNative.SDL_OpenJoystick(id)) != IntPtr.Zero)
            {
                return;
            }
        }
    }

    /// <summary>The game port now: the controller's present position and buttons, or no joystick.</summary>
    public JoystickReading Read()
    {
        if (_gamepad == IntPtr.Zero && _joystick == IntPtr.Zero)
        {
            return JoystickReading.None;
        }
        // the game reads the port in tight loops (waiting for a button to be let go) without pumping events
        SdlNative.SDL_UpdateJoysticks();
        return _gamepad != IntPtr.Zero ? ReadGamepad() : ReadPlainJoystick();
    }

    /// <summary>
    /// Whether a gamepad button is held (D-672): SDL's button number, or <see cref="KeyBindings.TriggerBase"/> + an axis for a trigger.
    /// False without a gamepad (a plain joystick has no such buttons).
    /// </summary>
    public bool IsDown(int id)
    {
        if (_gamepad == IntPtr.Zero)
        {
            return false;
        }
        SdlNative.SDL_UpdateJoysticks();
        return id >= KeyBindings.TriggerBase
            ? SdlNative.SDL_GetGamepadAxis(_gamepad, id - KeyBindings.TriggerBase) > KeyBindings.TriggerPressed
            : SdlNative.SDL_GetGamepadButton(_gamepad, id);
    }

    private JoystickReading ReadGamepad()
    {
        var x = Position(SdlNative.SDL_GetGamepadAxis(_gamepad, SdlNative.GamepadAxisLeftX));
        var y = Position(SdlNative.SDL_GetGamepadAxis(_gamepad, SdlNative.GamepadAxisLeftY));
        // the directional pad pushes the stick all the way
        x = Pad(x, SdlNative.GamepadButtonDpadLeft, SdlNative.GamepadButtonDpadRight);
        y = Pad(y, SdlNative.GamepadButtonDpadUp, SdlNative.GamepadButtonDpadDown);
        int[] buttons = [SdlNative.GamepadButtonSouth, SdlNative.GamepadButtonEast, SdlNative.GamepadButtonWest, SdlNative.GamepadButtonNorth];
        var held = 0;
        for (var i = 0; i < buttons.Length; i++)
        {
            if (SdlNative.SDL_GetGamepadButton(_gamepad, buttons[i]))
            {
                held |= 1 << i;
            }
        }
        return JoystickReading.FromStick(x, y, held);
    }

    private double Pad(double position, int low, int high) =>
        SdlNative.SDL_GetGamepadButton(_gamepad, low) ? -1 : SdlNative.SDL_GetGamepadButton(_gamepad, high) ? 1 : position;

    private JoystickReading ReadPlainJoystick()
    {
        var axes = SdlNative.SDL_GetNumJoystickAxes(_joystick);
        var x = axes > 0 ? Position(SdlNative.SDL_GetJoystickAxis(_joystick, 0)) : 0;
        var y = axes > 1 ? Position(SdlNative.SDL_GetJoystickAxis(_joystick, 1)) : 0;
        var count = Math.Min(PortButtons, SdlNative.SDL_GetNumJoystickButtons(_joystick));
        var held = 0;
        for (var i = 0; i < count; i++)
        {
            if (SdlNative.SDL_GetJoystickButton(_joystick, i))
            {
                held |= 1 << i;
            }
        }
        return JoystickReading.FromStick(x, y, held);
    }

    /// <summary>An SDL axis value as a position from -1 to 1, the dead zone around the middle taken as the middle.</summary>
    private static double Position(short value)
    {
        var position = Math.Clamp(value / AxisRange, -1, 1);
        var distance = Math.Abs(position);
        return distance <= DeadZone ? 0 : Math.Sign(position) * (distance - DeadZone) / (1 - DeadZone);
    }

    public void Dispose()
    {
        if (_gamepad != IntPtr.Zero)
        {
            SdlNative.SDL_CloseGamepad(_gamepad);
            _gamepad = IntPtr.Zero;
        }
        if (_joystick != IntPtr.Zero)
        {
            SdlNative.SDL_CloseJoystick(_joystick);
            _joystick = IntPtr.Zero;
        }
        if (_available)
        {
            SdlNative.SDL_QuitSubSystem(SdlNative.InitJoystick | SdlNative.InitGamepad);
        }
    }
}
