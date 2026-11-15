// Adapted from the Darklands Restoration Project, src/Darklands.Platform/DisplayLayout.cs; same author.
// Adapted: Covert Action's picture (320x200, and the 720x400 text page, both shown at the largest 4:3 place of the window
// that SdlHost has always used); the window positions of SDL's events are converted to render-output pixels (high pixel
// density); a position over the black bars is "outside the picture"; the text page's cells added. Left out: the window
// size options (csharp/src/CovertAction.Settings has them) and Darklands' 640x400 graphics place.
namespace CovertAction.Host.Sdl;

/// <summary>
/// Where the SDL host puts the game's picture in its window, and which game pixel a mouse position is over. New code
/// (host presentation, not a reconstruction; csharp/docs/MOUSE-SUPPORT.md section 4).
/// </summary>
internal static class PictureLayout
{
    /// <summary>The game's picture, in pixels.</summary>
    public const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>The text page's cells (80x25) over the same place.</summary>
    public const int TextColumns = 80, TextRows = 25;

    /// <summary>
    /// The picture's place in the window's drawing area: the largest 4:3 rectangle that fits, in the middle (the
    /// original's display shows its 320x200 pixels on a 4:3 screen). The text page is shown at the same place.
    /// </summary>
    /// <param name="width">The drawing area's width in pixels (SDL_GetRenderOutputSize).</param>
    /// <param name="height">The drawing area's height in pixels.</param>
    public static (int X, int Y, int Width, int Height) Place(int width, int height)
    {
        var pictureWidth = Math.Min(width, height * 4 / 3);
        var pictureHeight = pictureWidth * 3 / 4;
        return ((width - pictureWidth) / 2, (height - pictureHeight) / 2, pictureWidth, pictureHeight);
    }

    /// <summary>
    /// The game pixel under a mouse position of SDL's events (window coordinates, which are not drawing-area pixels on a
    /// screen of high pixel density): the position is taken to the drawing area, then the pixel whose stretched picture
    /// covers that window pixel's centre is the one under it (a stretched picture shows at each window pixel the game
    /// pixel under its centre; at a scale that is not whole, the left edge would name the pixel before at every boundary).
    /// Over the black bars the position is outside the picture (its pixel is the nearest edge's).
    /// </summary>
    /// <param name="place">The picture's place (<see cref="Place"/>).</param>
    /// <param name="output">The drawing area's size in pixels.</param>
    /// <param name="window">The window's size in SDL's window coordinates.</param>
    /// <param name="x">The event's position across, in window coordinates.</param>
    /// <param name="y">The event's position down.</param>
    /// <param name="software">SDL's software renderer draws: it steps through the picture in truncated 16.16 fixed point.</param>
    public static (int X, int Y, bool Inside) ToGame((int X, int Y, int Width, int Height) place, (int Width, int Height) output,
        (int Width, int Height) window, float x, float y, bool software = false)
    {
        var px = Whole(x, output.Width, window.Width);
        var py = Whole(y, output.Height, window.Height);
        var inside = px >= place.X && px < place.X + place.Width && py >= place.Y && py < place.Y + place.Height;
        int gx, gy;
        if (software)
        {
            gx = UnderSoftware(px - place.X, place.Width, ScreenWidth);
            gy = UnderSoftware(py - place.Y, place.Height, ScreenHeight);
        }
        else
        {
            gx = Under(px - place.X, place.Width, ScreenWidth);
            gy = Under(py - place.Y, place.Height, ScreenHeight);
        }
        return (Math.Clamp(gx, 0, ScreenWidth - 1), Math.Clamp(gy, 0, ScreenHeight - 1), inside);
    }

    /// <summary>The text page's cell (column 0..79, row 0..24) a game pixel of the same place lies in.</summary>
    /// <param name="x">The game pixel across, 0..319.</param>
    /// <param name="y">The game pixel down, 0..199.</param>
    public static (int Column, int Row) TextCell(int x, int y) => (x * TextColumns / ScreenWidth, y * TextRows / ScreenHeight);

    /// <summary>A window coordinate as a whole drawing-area pixel.</summary>
    private static int Whole(float position, int pixels, int windowSize) =>
        (int)Math.Floor(position * (double)pixels / Math.Max(1, windowSize));

    // SDL's software renderer stretches with 16.16 fixed point (SDL_stretch.c, and the scaling blitters of SDL_blit_auto.c
    // and SDL_blit_slow.c): the step is truncated, inc = (pixels << 16) / shown, and window pixel n takes source pixel
    // (n * inc + inc / 2) >> 16, which lands one game pixel short just past some boundaries.
    internal static int UnderSoftware(int offset, int shown, int pixels)
    {
        if (offset < 0)
        {
            return -1;
        }
        var inc = ((long)pixels << 16) / Math.Max(1, shown);
        return (int)((offset * inc + inc / 2) >> 16);
    }

    // Exact arithmetic. A centre exactly on the line between two game pixels (2.5 window pixels per game pixel, and the
    // like) is a tie the graphics card breaks by its own rounding, either way: it is taken as the pixel before.
    internal static int Under(int offset, int shown, int pixels)
    {
        var position = (2L * offset + 1) * pixels;
        var span = 2L * Math.Max(1, shown);
        var under = Math.DivRem(position, span, out var rest);
        return (int)(rest == 0 || rest < 0 ? under - 1 : under);
    }

    /// <summary>True when a window pixel's centre is exactly on the line between two game pixels (see <see cref="ToGame"/>).</summary>
    internal static bool IsTie(int offset, int shown, int pixels) => (2L * offset + 1) * pixels % (2L * Math.Max(1, shown)) == 0;
}
