namespace CovertAction.Drivers;

/// <summary>
/// The display colours that pictures bring with them (D-642, new code, outside the game): colours 128 to 255 of the
/// 256-colour display, which the game never draws with. The game's own drawing (text, lines, the cursor, the pictures of
/// the PC) uses colours 0 to 15 through the palette the game sets, and nothing it does changes colours 128 and above (the
/// mode set leaves them alone, <see cref="McgaGraphicsDriver.SubCode0208"/>). A picture of the Amiga version has colours of
/// its own; each is given a display colour here, and the picture's pixels are written with those numbers, so the game's
/// colours stay as they are while the picture shows in its own.
/// </summary>
/// <remarks>
/// One display colour per distinct colour, shared between pictures: the black and the greys many pictures have are one
/// entry, so the room lasts for many pictures. The numbers are given out in order and kept until the mode is set anew
/// (a new part of the game begins with pages that hold no picture of the one before); when all 128 are taken, a further
/// colour is shown as the nearest one given out.
/// </remarks>
/// <param name="palette">The display's palette (three bytes per colour, 0..63 each), whose entries are written.</param>
public sealed class PictureColours(byte[] palette)
{
    /// <summary>The first display colour given out.</summary>
    public const int First = 128;

    /// <summary>How many display colours there are to give out.</summary>
    public const int Capacity = 256 - First;

    private readonly Dictionary<int, byte> _given = [];

    /// <summary>The colours given out, as 4-bit red, green and blue packed <c>0xRGB</c>, in the order of their entries.</summary>
    private readonly List<int> _colours = [];

    /// <summary>How many colours have been given out.</summary>
    public int Count => _colours.Count;

    /// <summary>
    /// The display colour for a colour of an Amiga picture: the one already given for it, else the next free one, written
    /// into the palette; else, with none left, the nearest one given out.
    /// </summary>
    /// <param name="rgb">The colour as the Amiga stores it: <c>0x0RGB</c>, each component 0 to 15.</param>
    /// <returns>The display colour, 128 to 255.</returns>
    public byte Entry(int rgb)
    {
        rgb &= 0xfff;
        if (_given.TryGetValue(rgb, out var known))
        {
            return known;
        }
        if (_colours.Count >= Capacity)
        {
            return NearestGiven(rgb);
        }
        var entry = (byte)(First + _colours.Count);
        _colours.Add(rgb);
        _given[rgb] = entry;
        // each 4-bit component widened to the VGA's 6 bits (0 to 63) by repeating its top bits, so 15 is full
        palette[3 * entry] = Widen(rgb >> 8 & 0xf);
        palette[3 * entry + 1] = Widen(rgb >> 4 & 0xf);
        palette[3 * entry + 2] = Widen(rgb & 0xf);
        return entry;
    }

    /// <summary>Forgets the colours given out, so the next picture's take the entries again from the first. The palette is left as it is.</summary>
    public void Reset()
    {
        _given.Clear();
        _colours.Clear();
    }

    private static byte Widen(int fourBits) => (byte)(fourBits << 2 | fourBits >> 2);

    private byte NearestGiven(int rgb)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < _colours.Count; i++)
        {
            var other = _colours[i];
            int dr = (rgb >> 8 & 0xf) - (other >> 8 & 0xf), dg = (rgb >> 4 & 0xf) - (other >> 4 & 0xf), db = (rgb & 0xf) - (other & 0xf);
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                (best, bestDistance) = (i, distance);
            }
        }
        var entry = (byte)(First + best);
        _given[rgb] = entry;
        return entry;
    }
}
