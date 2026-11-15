using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The small output routines every part of the game has: drawing a text, a line or a box on the screen, waiting
/// for the display, playing a sound when sound is on. The original's programs each carry a copy; where the copies
/// are the same instructions (docs/source-map/shared-functions.csv) this one implementation stands for all of
/// them, and its comment lists them. A copy that differs is not listed and gets its own method when its part is
/// ported. All drawing goes to the screen port <see cref="ILibraryState.Rp1"/>.
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="world">The drivers and the machine.</param>
public sealed class LibraryOutput(ILibraryState state, Boundaries world)
{
    /// <summary>Grey, the frame colour of boxes whose own partner colour would be black or colour 13.</summary>
    private const int Grey = 7;

    /// <summary>Plays a sound effect, unless the player has switched sound off (Alt-S).</summary>
    /// <param name="effect">The effect's number (see <see cref="Sounds"/>).</param>
    // orig: CODE 0000:0cbd DoSound (src/c/code/code_0000_0cbd.c) matched; also FINAL 0100:041d, GAME 00f7:0337, BUG 00e8:020e; tests: CodeOracleTests
    // Other code under the same name: TAC 00e8:0662, CHASE 00df:0467 (not this method; docs/source-map/shared-functions.csv).
    public void DoSound(int effect)
    {
        if (state.Status.HasFlag(Options.Sound))
        {
            world.Sound.PlaySound(effect);
        }
    }

    /// <summary>Asks the display to show another of its pages. Nothing in this part calls it.</summary>
    /// <param name="page">The page number; only its low byte is used.</param>
    // orig: CODE 0000:0cd4 set_display_page (src/c/code/code_0000_0cd4.c) matched; also FINAL 0100:0519, TAC 00e8:0681, BUG 00e8:0225, CHASE 00df:0482; tests: CodeOracleTests
    // DORMANT: no caller in CODE; left over from the routines all parts share.
    public void SetDisplayPage(int page) => world.Graphics.SelectDisplayPage(page & 0xff);

    /// <summary>Draws a straight line on the screen.</summary>
    /// <param name="x1">Start, x.</param>
    /// <param name="y1">Start, y.</param>
    /// <param name="x2">End, x.</param>
    /// <param name="y2">End, y.</param>
    /// <param name="color">Palette index.</param>
    // orig: CODE 0000:2938 draw_line (src/c/code/code_0000_2938.c) matched; also FINAL 0100:c018, GAME 00f7:d39a, TAC 00e8:d0b2, BUG 00e8:6e7e, CHASE 00df:4186; tests: CodeOracleTests
    public void DrawLine(int x1, int y1, int x2, int y2, int color) =>
        world.Graphics.DrawLinePort(state.Rp1, x1, y1, x2, y2, color);

    /// <summary>Draws a text on the screen in the given colour, which stays the port's foreground pen.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="color">Palette index of the letters.</param>
    // orig: CODE 0000:2958 draw_text (src/c/code/code_0000_2958.c) matched; also FINAL 0100:c038, GAME 00f7:d3ba, TAC 00e8:d0d2, BUG 00e8:6e9e, CHASE 00df:41a6; tests: CodeOracleTests
    public void DrawText(string text, int x, int y, int color)
    {
        state.Rp1.APen = (short)color;
        world.Graphics.DrawString(state.Rp1, x, y, text);
    }

    /// <summary>
    /// Draws a text with the port's draw mode switched to 0 for the call and back to 1 after it: only the letters'
    /// own pixels are drawn, not the cell around them. This is how letters are put on freshly filled areas.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="color">Palette index of the letters.</param>
    // orig: CODE 0000:2979 draw_text_flag0 (src/c/code/code_0000_2979.c) matched; also FINAL 0100:c059, GAME 00f7:d3db, TAC 00e8:d0f3, BUG 00e8:6ebf, CHASE 00df:41c7; tests: CodeOracleTests
    public void DrawTextFlag0(string text, int x, int y, int color)
    {
        state.Rp1.DrawMode = 0;
        DrawText(text, x, y, color);
        state.Rp1.DrawMode = 1;
    }

    /// <summary>
    /// Waits for a number of display frames, the game's unit of delay (70 a second on the MCGA screen): for each,
    /// until the vertical retrace begins and then until it ends.
    /// </summary>
    /// <param name="frames">The number of frames; 0 returns at once.</param>
    // orig: CODE 0000:29a2 wait_vsync (src/c/code/code_0000_29a2.c) matched; also FINAL 0100:c082, TAC 00e8:d11c, BUG 00e8:6ee8, CHASE 00df:41f0, GAME 00f7:d404; tests: CodeOracleTests
    public void WaitVsync(int frames)
    {
        // C: the count is a 16-bit word counted down to 0, so a negative count waits 65536 + count frames
        for (var left = (ushort)frames; left != 0; left--)
        {
            world.Timer.WaitForFrame();
        }
    }

    /// <summary>Draws one of the part's images (a face part, a title bar's end, a caption) on the screen.</summary>
    /// <param name="sprite">The part's table of images, filled by its picture loaders.</param>
    /// <param name="id">The image's number in the table.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    // orig: FINAL 0100:2c60 draw_image (src/c/final/final_0100_2c60.c) matched; also BUG 00e8:029c, GAME 00f7:3cdd; tests: HarnessTests
    public void DrawImage(IReadOnlyList<Image?> sprite, int id, int x, int y) =>
        world.Graphics.DrawImagePort(state.Rp1, x, y, sprite[id]);

    /// <summary>Fills the rectangle between two corners, both inclusive, on the screen.</summary>
    /// <param name="x1">Left edge.</param>
    /// <param name="y1">Top edge.</param>
    /// <param name="x2">Right edge, inclusive.</param>
    /// <param name="y2">Bottom edge, inclusive.</param>
    /// <param name="color">Palette index.</param>
    // orig: CODE 0000:2a14 fill_rect_corners (src/c/code/code_0000_2a14.c) matched; also GAME 00f7:d476, TAC 00e8:d18e, BUG 00e8:6f5a, CHASE 00df:4262; tests: CodeOracleTests
    // Other code under the same name: FINAL 0100:c0f4 (not this method; docs/source-map/shared-functions.csv).
    public void FillRectCorners(int x1, int y1, int x2, int y2, int color) =>
        world.Graphics.FillRect(state.Rp1, x1, y1, x2 - x1 + 1, y2 - y1 + 1, color);

    /// <summary>
    /// Fills a rectangle and frames it in the fill colour's partner: the same colour with its brightness bit flipped,
    /// or grey where that would be black or colour 13. The fill colour also becomes the port's background pen.
    /// </summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">The right edge is x + width.</param>
    /// <param name="height">The bottom edge is y + height.</param>
    /// <param name="color">Palette index of the fill.</param>
    // orig: CODE 0000:224e draw_framed_box (src/c/code/code_0000_224e.c) matched; also FINAL 0100:97f4, GAME 00f7:cf2e, TAC 00e8:c366, BUG 00e8:6132, CHASE 00df:3a9c; tests: CodeOracleTests
    public void DrawFramedBox(int x, int y, int width, int height, int color)
    {
        FillRectCorners(x, y, x + width, y + height, color);
        state.Rp1.BPen = (short)color;
        var frame = color ^ 8;
        if (frame is 0 or 0xd)
        {
            frame = Grey;
        }
        DrawRectOutline(x, y, width, height, frame);
    }

    /// <summary>Draws the four edges of a rectangle: top, bottom, right, left.</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">The right edge is x + width.</param>
    /// <param name="height">The bottom edge is y + height.</param>
    /// <param name="color">Palette index.</param>
    // orig: CODE 0000:22a6 draw_rect_outline (src/c/code/code_0000_22a6.c) matched; also FINAL 0100:984c, GAME 00f7:cf86, TAC 00e8:c3be, BUG 00e8:618a, CHASE 00df:3af4; tests: CodeOracleTests
    public void DrawRectOutline(int x, int y, int width, int height, int color)
    {
        int right = x + width, bottom = y + height;
        DrawLine(x, y, right, y, color);
        DrawLine(x, bottom, right, bottom, color);
        DrawLine(right, y, right, bottom, color);
        DrawLine(x, y, x, bottom, color);
    }
}
