using CovertAction.Settings.Drawing;

namespace CovertAction.Settings;

/// <summary>The launcher drawn by this project (<see cref="LauncherScreen"/>): drawn, shown, and its input handled until the player chooses. New code.</summary>
internal sealed class Launcher : ILauncher
{
    /// <summary>How often the screen is drawn again while a sound test plays (its status line).</summary>
    private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(250);

    public LauncherResult Run(LauncherContext context, ILauncherSurface surface)
    {
        var model = new LauncherModel(context, surface.DisplaySize);
        var screen = new LauncherScreen(model, context, surface);
        var canvas = new Canvas(LauncherScreen.DesignWidth, LauncherScreen.DesignHeight);
        try
        {
            while (true)
            {
                screen.Render(canvas);
                surface.Present(canvas.Pixels, canvas.Width, canvas.Height);
                foreach (var input in surface.Wait(screen.Animating ? Refresh : null))
                {
                    if (screen.Handle(input) is { } choice)
                    {
                        return new LauncherResult(choice, model.Settings(), model.Changes(playing: choice == LauncherChoice.Play));
                    }
                }
            }
        }
        finally
        {
            context.SoundTest?.Stop();
        }
    }

    /// <summary>The launcher's first picture as it would open, drawn without a window (--launcher-screenshot, the tests); <paramref name="tab"/>: 0 General, 1 Advanced, 2 Cut content, 3 Controls.</summary>
    public static Canvas Picture(LauncherContext context, (int Width, int Height)? display = null, bool advanced = false, int tab = 0)
    {
        var surface = new NoSurface(display);
        var screen = new LauncherScreen(new LauncherModel(context, display), context, surface);
        // Ctrl+Tab goes General, Advanced, Cut content, Controls: the tab asked for is that many presses on
        for (var press = advanced && tab == 0 ? 1 : tab; press > 0; press--)
        {
            screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        }
        var canvas = new Canvas(LauncherScreen.DesignWidth, LauncherScreen.DesignHeight);
        screen.Render(canvas);
        return canvas;
    }

    /// <summary>A surface that shows nothing and has no input (for a picture of the launcher).</summary>
    private sealed class NoSurface((int Width, int Height)? display) : ILauncherSurface
    {
        public (int Width, int Height)? DisplaySize => display;

        public void Present(ReadOnlySpan<uint> pixels, int width, int height)
        {
        }

        public IReadOnlyList<LauncherInput> Wait(TimeSpan? timeout) => [new CloseRequested()];

        public string? Clipboard() => null;

        public Func<string, string, string?>? PickFolder => null;
    }
}
