// Adapted from the Darklands Restoration Project, src/Darklands.Host.Sdl/SdlHost.cs; same author.
// A short version of that host: one window that shows the game's picture, and the keyboard. Left out: the picture
// filters, the high-resolution renderer, the mouse, the developer console, Darklands's keyboard timing. Added
// (csharp/docs/PERFORMANCE.md): the PC keyboard's own repeat and the BIOS's 15-key buffer, precise frame waits, a timing trace;
// a controller as the PC's joystick (SdlJoystick).
using CovertAction.Platform;

namespace CovertAction.Host.Sdl;

/// <summary>
/// The SDL 3 host: a resizable window that shows the game's 320x200 picture as a 4:3 screen (as the original's
/// display shows it), the keyboard as game key codes, a joystick or gamepad as the PC's joystick, a sound device and
/// the system's time.
/// </summary>
internal sealed class SdlHost : IHost, IVideo, IInput, IClock, IDisposable
{
    private const int ScreenWidth = 320;
    private const int ScreenHeight = 200;

    private readonly IntPtr _window;
    private readonly IntPtr _renderer;
    private readonly IntPtr _texture;
    private readonly uint[] _argb = new uint[ScreenWidth * ScreenHeight];
    private readonly IntPtr _textTexture;
    private readonly uint[] _textArgb = new uint[TextModeRaster.Width * TextModeRaster.Height];
    private readonly Queue<int> _keys = new();
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private readonly FrameTrace? _trace = FrameTrace.FromEnvironment();
    private readonly ScriptedInput? _script = ScriptedInput.FromEnvironment();

    /// <summary>The game port's joystick: the first controller connected (SdlJoystick); none while a script plays.</summary>
    private readonly SdlJoystick? _joystick;

    /// <summary>
    /// The keys the BIOS's keyboard buffer holds: 16 words, one always free. A key pressed while it is full is lost (the
    /// PC beeps). Unbounded, keys typed or repeated faster than the game takes them piled up and were acted on for
    /// seconds after the key was let go (csharp/docs/PERFORMANCE.md).
    /// </summary>
    private const int BiosBufferKeys = 15;

    /// <summary>
    /// A held key repeats as the PC's keyboard repeats it: after half a second, 10.9 times a second (the AT keyboard's
    /// own setting at power-on, which DOS and the game leave). Windows repeats at its own rate, by default about 30 a
    /// second: in a raid, which takes a key every turn (8.75 a second at medium speed), the keys piled up and the agent went on
    /// walking after the key was let go (csharp/docs/PERFORMANCE.md). The settings' input.keyRepeat "windows" (or COVERT_KEY_REPEAT=system): the system's rate.
    /// </summary>
    private const double TypematicDelay = 500, TypematicInterval = 1000 / 10.9;

    private readonly bool _systemRepeat;
    private int _heldScancode = -1;
    private int _heldKey;
    private double _nextRepeat;

    /// <summary>Opens the window.</summary>
    /// <param name="title">The window's title.</param>
    /// <param name="scale">How many window pixels a game pixel is wide when the window opens.</param>
    public SdlHost(string title, int scale)
        : this(title, new SdlWindowOptions(ScreenWidth * scale, ScreenWidth * scale * 3 / 4))
    {
    }

    /// <summary>Opens the window as the player's settings say (csharp/docs/SETTINGS.md).</summary>
    /// <param name="title">The window's title.</param>
    /// <param name="options">The window's size, fullscreen, the picture's scaling, the keyboard's repeat.</param>
    public SdlHost(string title, SdlWindowOptions options)
    {
        _systemRepeat = options.SystemKeyRepeat;
        if (!SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents))
        {
            throw new SdlHostException("SDL_Init failed: " + SdlNative.GetErrorText());
        }
        // the key events' symbols as SDL2 gave them: the layout's own (no French digits or Latin letters put in by SDL3)
        SdlNative.SDL_SetHint(SdlNative.HintKeycodeOptions, "none");
        // the original's display shows its 320x200 pixels on a 4:3 screen: a pixel is taller than it is wide
        _window = SdlNative.CreateCentredWindow(title, options.Width, options.Height,
            SdlNative.WindowResizable | SdlNative.WindowHighPixelDensity | (options.Fullscreen ? SdlNative.WindowFullscreen : 0));
        if (_window == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateWindow failed: " + SdlNative.GetErrorText());
        }
        _renderer = SdlNative.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero)
        {
            _renderer = SdlNative.SDL_CreateRenderer(_window, "software");
        }
        if (_renderer == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateRenderer failed: " + SdlNative.GetErrorText());
        }
        _texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStreaming, ScreenWidth, ScreenHeight);
        Scale(_texture, options.Scaling, SdlNative.ScaleModeNearest);
        SdlNative.SDL_SetTextureBlendMode(_texture, SdlNative.BlendModeNone);
        // the text page at its own resolution (720x400), shown stretched to the same 4:3 place as the picture
        _textTexture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStreaming, TextModeRaster.Width, TextModeRaster.Height);
        Scale(_textTexture, options.Scaling, SdlNative.ScaleModeLinear);
        SdlNative.SDL_SetTextureBlendMode(_textTexture, SdlNative.BlendModeNone);
        var audio = SdlAudioOutput.TryOpen(out _);
        Audio = (IPcmOutput?)audio ?? new SilentPcmOutput();
        // a timing run replays a recorded session, which had no joystick: no controller is asked then
        _joystick = _script is null ? new SdlJoystick() : null;
        if (_trace is not null)
        {
            if (audio is not null)
            {
                audio.Trace = _trace;
            }
            _trace.Mark($"renderer {SdlNative.Text(SdlNative.SDL_GetRendererName(_renderer))}; sound {audio?.Description ?? "none"}; joystick {_joystick?.Description ?? "none"}; script {(_script is null ? "none" : "yes")}");
        }
    }

    /// <summary>A part of the game starts (the launcher's hand-off code): for the trace and the scripted keys.</summary>
    public void PartStarting(int code)
    {
        _script?.PartStarting();
        _trace?.Mark($"part {code}");
    }

    public IVideo Video => this;

    public IInput Input => this;

    public IPcmOutput Audio { get; }

    public IClock Clock => this;

    public bool QuitRequested { get; private set; }

    public long Milliseconds => _watch.ElapsedMilliseconds;

    public long UnixSeconds => _script?.UnixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>The stopwatch's time with its fraction: frames are due every 14.27 ms (HostLink.WaitForFrame).</summary>
    public double PreciseMilliseconds => _watch.Elapsed.TotalMilliseconds;

    /// <summary>
    /// SDL's wait (on Windows a high-resolution waitable timer): a millisecond is a millisecond whatever the system's
    /// timer resolution (Thread.Sleep waits for the system's tick, 15.6 ms where no program has asked for a finer one).
    /// </summary>
    public void Sleep(int milliseconds)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (milliseconds > 0)
        {
            SdlNative.SDL_DelayNS((ulong)milliseconds * 1_000_000);
        }
        _trace?.Slept(milliseconds, start);
    }

    /// <summary>
    /// The wait for the display's next frame, ended on time: SDL sleeps short of the deadline and spins the last part
    /// (SDL_DelayPrecise). Slept in whole milliseconds, frames came 11 to 18 ms apart instead of 14.27 (PERFORMANCE.md).
    /// </summary>
    public void WaitUntil(double deadline)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var left = deadline - PreciseMilliseconds;
        if (left > 0)
        {
            SdlNative.SDL_DelayPrecise((ulong)(left * 1_000_000));
        }
        _trace?.Slept(Math.Max(0, left), start, frame: true);
    }

    public void Present(ReadOnlySpan<byte> pixels, ReadOnlySpan<uint> palette)
    {
        const uint opaque = 0xff000000;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < _argb.Length; i++)
        {
            _argb[i] = opaque | palette[pixels[i]];
        }
        var converted = System.Diagnostics.Stopwatch.GetTimestamp();
        SdlNative.SDL_UpdateTexture(_texture, IntPtr.Zero, _argb, ScreenWidth * sizeof(uint));
        var uploaded = System.Diagnostics.Stopwatch.GetTimestamp();
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        SdlNative.RenderCopy(_renderer, _texture, PicturePlace());
        SdlNative.SDL_RenderPresent(_renderer);
        _trace?.Frame(false, start, converted, uploaded, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    public void PresentText(TextScreen screen)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        TextModeRaster.Render(screen, TextFont.Builtin, Milliseconds, _textArgb);
        var converted = System.Diagnostics.Stopwatch.GetTimestamp();
        SdlNative.SDL_UpdateTexture(_textTexture, IntPtr.Zero, _textArgb, TextModeRaster.Width * sizeof(uint));
        var uploaded = System.Diagnostics.Stopwatch.GetTimestamp();
        SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SdlNative.SDL_RenderClear(_renderer);
        SdlNative.RenderCopy(_renderer, _textTexture, PicturePlace());
        SdlNative.SDL_RenderPresent(_renderer);
        _trace?.Frame(true, start, converted, uploaded, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    public void Pump()
    {
        while (SdlNative.SDL_PollEvent(out var e))
        {
            switch (e.Type)
            {
                case SdlNative.EventQuit:
                    QuitRequested = true;
                    break;
                case SdlNative.EventKeyDown:
                    if (e.Keyboard.Repeat != 0 && !_systemRepeat)
                    {
                        break;      // the system's repeat: the keyboard's own is made below (TypematicInterval)
                    }
                    var key = SdlKeyMap.ToGameKey(e.Keyboard.Scancode, e.Keyboard.Key, e.Keyboard.Mod);
                    // the last key pressed is the one that repeats, as on a PC (a Shift pressed after a letter stops it)
                    _heldScancode = key == HostKeys.None ? -1 : e.Keyboard.Scancode;
                    _heldKey = key;
                    _nextRepeat = PreciseMilliseconds + TypematicDelay;
                    if (key != HostKeys.None)
                    {
                        Type(key, e.Keyboard.Timestamp);
                    }
                    break;
                case SdlNative.EventKeyUp:
                    if (e.Keyboard.Scancode == _heldScancode)
                    {
                        _heldScancode = -1;
                    }
                    break;
                case var type when SdlJoystick.IsDeviceEvent(type):
                    _joystick?.Refresh();
                    _trace?.Mark($"joystick {_joystick?.Description ?? "none"}");
                    break;
            }
        }
        if (_heldScancode >= 0 && !_systemRepeat && PreciseMilliseconds >= _nextRepeat)
        {
            // one repeat for a pause of the game longer than the interval: the keyboard's repeats are not caught up
            var now = PreciseMilliseconds;
            _nextRepeat += TypematicInterval;
            if (_nextRepeat <= now)
            {
                _nextRepeat = now + TypematicInterval;
            }
            Type(_heldKey, 0);
        }
    }

    /// <summary>A key into the BIOS's buffer, unless it is full (<see cref="BiosBufferKeys"/>); none while a script plays.</summary>
    private void Type(int key, ulong timestamp)
    {
        if (_script is null && _keys.Count < BiosBufferKeys)
        {
            _keys.Enqueue(key);
            _trace?.KeyArrived(timestamp);
        }
    }

    public bool TryReadKey(out int key)
    {
        _trace?.Polled();
        if (_script is not null)
        {
            // a timing run: the recorded session's next entry (ScriptedInput); used up, the window closes
            var next = _script.Next(out key);
            if (next is null)
            {
                QuitRequested = true;
                return false;
            }
            if (next.Value)
            {
                _trace?.KeyArrived(0);
                _trace?.KeyTaken();
            }
            return next.Value;
        }
        if (_keys.TryDequeue(out key))
        {
            _trace?.KeyTaken();
            return true;
        }
        return false;
    }

    /// <summary>The game port: the controller's present state (SdlJoystick), or no joystick.</summary>
    public JoystickReading ReadJoystick() => _joystick?.Read() ?? JoystickReading.None;

    public void Dispose()
    {
        _joystick?.Dispose();
        Audio.Dispose();
        _trace?.Dispose();
        SdlNative.SDL_DestroyTexture(_textTexture);
        SdlNative.SDL_DestroyTexture(_texture);
        SdlNative.SDL_DestroyRenderer(_renderer);
        SdlNative.SDL_DestroyWindow(_window);
        SdlNative.SDL_Quit();
    }

    /// <summary>
    /// Has a texture scaled as pixel art (SDL 3.4). The picture (320x200) and the text page (720x400) are stretched to a
    /// 4:3 place by factors that are rarely whole numbers: nearest sampling would draw some of their pixels a window
    /// pixel wider or taller than others (uneven strokes in the fonts); pixel art keeps every pixel one size, blending
    /// only the edge row or column that falls between two window pixels. Chosen by the user, 14 November 2026.
    /// </summary>
    /// <param name="texture">The picture's or the text page's texture.</param>
    /// <param name="fallback">The scale mode where the SDL library is older than 3.4.</param>
    /// <param name="scaling">The player's choice (display.scaling): pixel art (the default), nearest or smooth (linear).</param>
    private static void Scale(IntPtr texture, PictureScaling scaling, int fallback)
    {
        if (scaling == PictureScaling.Nearest)
        {
            SdlNative.SDL_SetTextureScaleMode(texture, SdlNative.ScaleModeNearest);
        }
        else if (scaling == PictureScaling.Smooth)
        {
            SdlNative.SDL_SetTextureScaleMode(texture, SdlNative.ScaleModeLinear);
        }
        else if (!SdlNative.SDL_SetTextureScaleMode(texture, SdlNative.ScaleModePixelArt))
        {
            SdlNative.SDL_SetTextureScaleMode(texture, fallback);
        }
    }

    /// <summary>Where the picture goes in the window: the largest 4:3 rectangle that fits, in the middle.</summary>
    private SdlNative.SdlRect PicturePlace()
    {
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var width, out var height);
        var pictureWidth = Math.Min(width, height * 4 / 3);
        var pictureHeight = pictureWidth * 3 / 4;
        return new SdlNative.SdlRect { X = (width - pictureWidth) / 2, Y = (height - pictureHeight) / 2, Width = pictureWidth, Height = pictureHeight };
    }
}

/// <summary>How the 320x200 picture (and the text page) is enlarged to the window (display.scaling).</summary>
internal enum PictureScaling
{
    /// <summary>Every game pixel the same size, only the edges between them blended (SDL 3.4's pixel art; the default).</summary>
    PixelArt,

    /// <summary>Hard pixels: each window pixel takes the nearest game pixel.</summary>
    Nearest,

    /// <summary>Linear: blurred.</summary>
    Smooth,
}

/// <summary>The game window as the player's settings set it up.</summary>
/// <param name="Width">The window's width when it opens.</param>
/// <param name="Height">The window's height when it opens.</param>
/// <param name="Fullscreen">The window fills the screen.</param>
/// <param name="Scaling">How the picture is enlarged.</param>
/// <param name="SystemKeyRepeat">A held key repeats at the system's rate (else the PC keyboard's).</param>
internal sealed record SdlWindowOptions(int Width, int Height, bool Fullscreen = false, PictureScaling Scaling = PictureScaling.PixelArt, bool SystemKeyRepeat = false);
