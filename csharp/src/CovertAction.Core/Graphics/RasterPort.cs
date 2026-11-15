namespace CovertAction.Core.Graphics;

/// <summary>
/// A drawing port: the record the game hands to every drawing call of the graphics driver to say where and how to
/// draw. It names the surface (the screen or an off-screen page), the window drawing is clipped to, and the current
/// colours and font. Each module keeps a few of them as plain data: one for the screen (<c>Rp1</c>), one for the copy
/// of the screen kept under menus (<c>RpBack</c>), and some for picture sheets. The game changes the fields freely
/// between calls (a text colour before a string, the page around a menu), which is why it is state and not an
/// argument list.
/// </summary>
/// <remarks>
/// The type and its field names are MicroProse's own (<c>struct RasterPort</c> in the Amiga version's symbols,
/// docs/amiga.md); the reconstructed gfx.h has the evidence for each field. All fields are 16-bit <c>int</c>s.
/// </remarks>
// orig: struct RasterPort, 18 bytes (src/c/include/gfx.h); for example CODE DS:1250 Rp1X and CODE DS:1044 RpBackX
public sealed class RasterPort
{
    /// <summary>The surface the calls draw on: 0 the screen, 1 the backup copy of the screen, 2 and 3 picture sheets.</summary>
    public short PageNr { get; set; }

    /// <summary>Left edge of the clip window, in pixels, inclusive (0 for a whole surface).</summary>
    public short XBgn { get; set; }

    /// <summary>Top edge of the clip window, in pixels, inclusive (0 for a whole surface).</summary>
    public short YBgn { get; set; }

    /// <summary>Right edge of the clip window, inclusive: the width minus one (319 for a whole surface).</summary>
    public short Width1 { get; set; }

    /// <summary>Bottom edge of the clip window, inclusive: the height minus one (199 for a whole surface).</summary>
    public short Length1 { get; set; }

    /// <summary>
    /// How text is drawn. 1, the normal way: every character fills its whole cell, its own pixels in the
    /// foreground pen and the rest (with the padding column and row) in the background pen, so a text replaces
    /// what was there. Any other value: only the characters' own pixels are drawn and the picture behind them
    /// stays; the game sets 0 to put a letter on a cell it has just filled.
    /// </summary>
    /// <remarks>Settled by the graphics driver's text routine (MGRAPHIC 0000:0e86; proven by the driver oracle).</remarks>
    public short DrawMode { get; set; }

    /// <summary>The foreground ("A") pen: the palette index text is drawn in.</summary>
    public short APen { get; set; }

    /// <summary>The background ("B") pen: the palette index of fills and cleared areas.</summary>
    public short BPen { get; set; }

    /// <summary>The font texts use and character widths are measured in: 1..3.</summary>
    public short FontNr { get; set; }
}
