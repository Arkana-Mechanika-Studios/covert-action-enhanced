// Adapted from the Darklands Restoration Project, src/Darklands.Host.Sdl/SdlLauncherSurface.cs; same author.
// Adapted: a window of its own with its frame (opened before the game's and closed before it; no frameless window, hit
// test or Windows activation filter); the launcher's picture is drawn at its design size and fitted to the window
// (letterboxed), the pointer mapped back into it; no typed text (folders are chosen, dropped or pasted).
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>The launcher's window: its frames shown through one streaming texture, SDL's events as its input.</summary>
internal sealed class SdlLauncherWindow : ILauncherSurface, IDisposable
{
    /// <summary>The window sizes, largest first (the design's 16:9 shape): the first that fits the screen is taken; the last is the smallest.</summary>
    internal static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1120, 630), (960, 540)];

    private const byte DeskRed = 0x0B, DeskGreen = 0x10, DeskBlue = 0x16;

    private readonly IntPtr _window;
    private readonly IntPtr _renderer;
    private IntPtr _texture;
    private (int Width, int Height) _textureSize;
    private (int Width, int Height) _design = (LauncherScreen.DesignWidth, LauncherScreen.DesignHeight);

    /// <summary>Opens the window.</summary>
    /// <exception cref="SdlHostException">SDL could not open it.</exception>
    public SdlLauncherWindow(string title)
    {
        if (!SdlNative.SDL_Init(SdlNative.InitVideo | SdlNative.InitEvents))
        {
            throw new SdlHostException("SDL_Init failed: " + SdlNative.GetErrorText());
        }
        SdlNative.SDL_SetHint(SdlNative.HintKeycodeOptions, "none");
        var screen = UsableScreen(SdlNative.SDL_GetPrimaryDisplay());
        var (width, height) = Sizes.FirstOrDefault(s => screen is not { } u || (s.Width * 1.05 <= u.Width && s.Height * 1.1 <= u.Height), Sizes[^1]);
        _window = SdlNative.CreateCentredWindow(title, width, height, SdlNative.WindowResizable | SdlNative.WindowHighPixelDensity);
        if (_window == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateWindow failed: " + SdlNative.GetErrorText());
        }
        SdlNative.SDL_SetWindowMinimumSize(_window, Sizes[^1].Width * 2 / 3, Sizes[^1].Height * 2 / 3);
        _renderer = SdlNative.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero)
        {
            _renderer = SdlNative.SDL_CreateRenderer(_window, "software");
        }
        if (_renderer == IntPtr.Zero)
        {
            throw new SdlHostException("SDL_CreateRenderer failed: " + SdlNative.GetErrorText());
        }
        SdlNative.SDL_SetRenderVSync(_renderer, 1);
        // a folder dropped on the window (SDL reports files and folders alike)
        SdlNative.SDL_SetEventEnabled(SdlNative.EventDropFile, true);
    }

    public (int Width, int Height)? DisplaySize => UsableScreen(SdlNative.SDL_GetDisplayForWindow(_window));

    private static (int Width, int Height)? UsableScreen(uint display) =>
        display != 0 && SdlNative.SDL_GetDisplayUsableBounds(display, out var r) && r.Width > 0 ? (r.Width, r.Height) : null;

    public Func<string, string, string?>? PickFolder => (title, start) =>
    {
        var chosen = SdlDialogs.Pick(_window, title, start, folders: true, PumpWhileOpen);
        // what was clicked or typed meanwhile is dropped
        SdlNative.SDL_FlushEvents(SdlNative.EventKeyDown, SdlNative.EventMouseWheel);
        return chosen;
    };

    /// <summary>While a dialog is open: the window's events are handled (and dropped); false once the window is closed.</summary>
    private static bool PumpWhileOpen()
    {
        SdlNative.SDL_PumpEvents();
        while (SdlNative.SDL_PollEvent(out var e))
        {
            if (e.Type == SdlNative.EventQuit)
            {
                // kept for the launcher, which then closes
                SdlNative.SDL_PushEvent(ref e);
                return false;
            }
        }
        return true;
    }

    public string? Clipboard() => SdlNative.TakeString(SdlNative.SDL_GetClipboardText());

    public unsafe void Present(ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (_texture == IntPtr.Zero || _textureSize != (width, height))
        {
            if (_texture != IntPtr.Zero)
            {
                SdlNative.SDL_DestroyTexture(_texture);
            }
            _texture = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStreaming, width, height);
            SdlNative.SDL_SetTextureBlendMode(_texture, SdlNative.BlendModeNone);
            // the text font's dots stay even at any window size (SDL 3.4's pixel art), else smooth
            if (!SdlNative.SDL_SetTextureScaleMode(_texture, SdlNative.ScaleModePixelArt))
            {
                SdlNative.SDL_SetTextureScaleMode(_texture, SdlNative.ScaleModeLinear);
            }
            _textureSize = (width, height);
            _design = (width, height);
        }
        fixed (uint* data = pixels)
        {
            SdlNative.SDL_UpdateTextureFrom(_texture, IntPtr.Zero, (IntPtr)data, width * 4);
        }
        SdlNative.SDL_SetRenderDrawColor(_renderer, DeskRed, DeskGreen, DeskBlue, 255);
        SdlNative.SDL_RenderClear(_renderer);
        SdlNative.RenderCopy(_renderer, _texture, Place());
        SdlNative.SDL_RenderPresent(_renderer);
    }

    /// <summary>Where the launcher's picture goes in the window: the largest rectangle of its shape that fits, in the middle.</summary>
    private SdlNative.SdlRect Place()
    {
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var w, out var h);
        var r = Fit(w, h, _design.Width, _design.Height);
        return new SdlNative.SdlRect { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
    }

    /// <summary>The largest rectangle of the design's shape in a window of <paramref name="width"/> x <paramref name="height"/>, centred.</summary>
    internal static (int X, int Y, int Width, int Height) Fit(int width, int height, int designWidth, int designHeight)
    {
        var w = Math.Min(width, (int)((long)height * designWidth / designHeight));
        var h = (int)((long)w * designHeight / designWidth);
        return ((width - w) / 2, (height - h) / 2, Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>A point of the window (window coordinates) in the launcher's design pixels.</summary>
    private (int X, int Y) ToDesign(float x, float y)
    {
        SdlNative.SDL_GetWindowSize(_window, out var ww, out var wh);
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var pw, out var ph);
        return ToDesign(x, y, ww, wh, pw, ph, _design.Width, _design.Height);
    }

    /// <summary>A point of the window in design pixels: window coordinates to the drawing's pixels (high DPI), then into the fitted picture.</summary>
    internal static (int X, int Y) ToDesign(float x, float y, int windowWidth, int windowHeight, int pixelWidth, int pixelHeight, int designWidth, int designHeight)
    {
        var px = x * pixelWidth / Math.Max(1, windowWidth);
        var py = y * pixelHeight / Math.Max(1, windowHeight);
        var place = Fit(pixelWidth, pixelHeight, designWidth, designHeight);
        return ((int)MathF.Floor((px - place.X) * designWidth / place.Width), (int)MathF.Floor((py - place.Y) * designHeight / place.Height));
    }

    public IReadOnlyList<LauncherInput> Wait(TimeSpan? timeout)
    {
        SdlNative.SdlEvent e;
        if (timeout is { } t)
        {
            if (!SdlNative.SDL_WaitEventTimeout(out e, (int)Math.Max(1, t.TotalMilliseconds)))
            {
                return [new TimePassed()];
            }
        }
        else if (!SdlNative.SDL_WaitEvent(out e))
        {
            return [new CloseRequested()];
        }
        var inputs = new List<LauncherInput>();
        do
        {
            if (ToInput(e) is { } input)
            {
                inputs.Add(input);
            }
        }
        while (SdlNative.SDL_PollEvent(out e));
        // nothing the launcher reads (a key let go, the pointer leaving): draw again anyway, harmlessly
        return inputs.Count > 0 ? inputs : [new SurfaceResized()];
    }

    private LauncherInput? ToInput(SdlNative.SdlEvent e)
    {
        switch (e.Type)
        {
            case SdlNative.EventQuit:
                return new CloseRequested();
            case SdlNative.EventWindowResized or SdlNative.EventWindowPixelSizeChanged:
                return new SurfaceResized();
            case SdlNative.EventDropFile:
                return e.DroppedPath() is { } path ? new PathDropped(path) : null;
            case SdlNative.EventMouseButtonDown when e.Button.Button == SdlNative.MouseButtonLeft:
                var (x, y) = ToDesign(e.Button.X, e.Button.Y);
                return new PointerPressed(x, y);
            case SdlNative.EventMouseButtonUp when e.Button.Button == SdlNative.MouseButtonLeft:
                var (ux, uy) = ToDesign(e.Button.X, e.Button.Y);
                return new PointerReleased(ux, uy);
            case SdlNative.EventMouseMotion:
                var (mx, my) = ToDesign(e.Motion.X, e.Motion.Y);
                return new PointerMoved(mx, my);
            case SdlNative.EventKeyDown:
                var mod = e.Keyboard.Mod;
                var shift = (mod & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
                var control = (mod & (SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl)) != 0;
                LauncherKey? key = e.Keyboard.Key switch
                {
                    SdlNative.KeyUp => LauncherKey.Up,
                    SdlNative.KeyDown => LauncherKey.Down,
                    SdlNative.KeyLeft => LauncherKey.Left,
                    SdlNative.KeyRight => LauncherKey.Right,
                    SdlNative.KeyReturn or SdlNative.KeyKeypadEnter => LauncherKey.Enter,
                    SdlNative.KeySpace => LauncherKey.Space,
                    SdlNative.KeyEscape => LauncherKey.Escape,
                    SdlNative.KeyTab => LauncherKey.Tab,
                    SdlNative.KeyV when control => LauncherKey.Paste,
                    SdlNative.KeyInsert when shift => LauncherKey.Paste,
                    _ => LauncherKey.Other,
                };
                return key is { } k ? new KeyPressed(k, shift, control, e.Keyboard.Scancode) : null;
            default:
                return null;
        }
    }

    public void Dispose()
    {
        if (_texture != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(_texture);
        }
        SdlNative.SDL_DestroyRenderer(_renderer);
        SdlNative.SDL_DestroyWindow(_window);
        SdlNative.SDL_Quit();
    }
}
