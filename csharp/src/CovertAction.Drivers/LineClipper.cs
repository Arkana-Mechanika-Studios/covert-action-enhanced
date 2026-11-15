namespace CovertAction.Drivers;

/// <summary>
/// Cuts a line down to the part inside a window, before the graphics driver draws it: the clipper of MicroProse's
/// graphics library, which every part of the game carries in front of the driver.
/// </summary>
/// <remarks>
/// <para>
/// The window reaches from (0, 0) to (right, bottom), both inclusive. An end of the line that lies outside is moved
/// along the line onto the window's edge: first onto the left or right edge; when the line passes that edge above or
/// below the window, onto the top or bottom edge instead. The new end is computed with 16-bit whole numbers and
/// rounded to the nearest pixel, so a clipped line can lie a pixel beside the unclipped one. That is what the
/// original draws.
/// </para>
/// <para>
/// When both ends are inside, the line is returned as given; when only one is, that end comes first in the result.
/// The driver draws a line from its left end either way.
/// </para>
/// </remarks>
// orig: CODE 0000:07ac clip_line thunk; also CODE 0000:0959 (its divide-error handler); tests: LineClipperTests
public static class LineClipper
{
    /// <summary>On which sides of the window a point lies outside it.</summary>
    [Flags]
    private enum Outside
    {
        Nowhere = 0,
        Right = 1,
        Below = 2,
        Above = 4,
        Left = 8,
    }

    /// <summary>A line's direction: how far it runs across and down, and half of each for rounding.</summary>
    private readonly record struct Slope(short Across, short Down)
    {
        public short HalfAcross => (short)(Across >> 1);

        public short HalfDown => (short)(Down >> 1);
    }

    /// <summary>The part of a line that lies inside a window, or null when none does.</summary>
    /// <param name="x1">One end, column.</param>
    /// <param name="y1">One end, row.</param>
    /// <param name="x2">The other end, column.</param>
    /// <param name="y2">The other end, row.</param>
    /// <param name="right">The window's last column.</param>
    /// <param name="bottom">The window's last row.</param>
    public static (short X1, short Y1, short X2, short Y2)? Clip(short x1, short y1, short x2, short y2, short right, short bottom)
    {
        (short X, short Y) from = (x1, y1), to = (x2, y2);
        var fromOutside = Where(from.X, from.Y, right, bottom);
        var toOutside = Where(to.X, to.Y, right, bottom);
        if (toOutside == Outside.Nowhere)
        {
            if (fromOutside == Outside.Nowhere)
            {
                return (x1, y1, x2, y2);
            }
            // the end to move is always the second: the one inside goes first
            (from, to) = (to, from);
            (fromOutside, toOutside) = (toOutside, fromOutside);
        }
        if ((fromOutside & toOutside) != 0)
        {
            // both ends beyond the same edge: the line misses the window
            return null;
        }

        // the direction is measured once, between the ends as they are now, and serves for both
        var slope = SlopeBetween(from, to);
        if (MoveOntoEdge(from, to, toOutside, slope, right, bottom) is not { } clippedTo)
        {
            return null;
        }
        if (fromOutside == Outside.Nowhere)
        {
            return (from.X, from.Y, clippedTo.X, clippedTo.Y);
        }
        // the first end is outside too: it is moved the same way, measured from the second end as it was given
        if (MoveOntoEdge(to, from, fromOutside, slope, right, bottom) is not { } clippedFrom)
        {
            return null;
        }
        return (clippedTo.X, clippedTo.Y, clippedFrom.X, clippedFrom.Y);
    }

    // orig: CODE 0000:0938 sub_code_0000_0938 thunk; also FINAL 0000:09c4, GAME 0000:0938, TAC 0000:09c4, BUG 0000:09c4, CHASE 0000:0938; tests: LineClipperTests
    private static Outside Where(short x, short y, short right, short bottom) =>
        (x < 0 ? Outside.Left : Outside.Nowhere)
        | (x > right ? Outside.Right : Outside.Nowhere)
        | (y < 0 ? Outside.Above : Outside.Nowhere)
        | (y > bottom ? Outside.Below : Outside.Nowhere);

    /// <summary>
    /// How far the line runs across and down. A distance too large for a 16-bit number is halved, and the other
    /// with it, which keeps the direction.
    /// </summary>
    private static Slope SlopeBetween((short X, short Y) from, (short X, short Y) to)
    {
        int across = to.X - from.X, down = to.Y - from.Y;
        if (across != (short)across || down != (short)down)
        {
            across >>= 1;
            down >>= 1;
        }
        return new Slope((short)across, (short)down);
    }

    /// <summary>The point where the line from <paramref name="anchor"/> towards <paramref name="end"/> meets the window's edge, or null when it misses the window.</summary>
    private static (short X, short Y)? MoveOntoEdge((short X, short Y) anchor, (short X, short Y) end, Outside endOutside, Slope slope, short right, short bottom)
    {
        short edgeY;
        if ((endOutside & (Outside.Left | Outside.Right)) != 0)
        {
            // the row in which the line crosses the left or right edge
            var edgeX = end.X < 0 ? (short)0 : right;
            var y = (short)(RoundedQuotient((short)(edgeX - anchor.X) * slope.Down, slope.Across, slope.HalfAcross) + anchor.Y);
            if (y >= 0 && y <= bottom)
            {
                return (edgeX, y);
            }
            // it crosses above or below the window: the top or bottom edge decides
            edgeY = y < 0 ? (short)0 : BottomOrTop(end.Y, bottom);
        }
        else
        {
            edgeY = BottomOrTop(end.Y, bottom);
        }
        // the column in which the line crosses the top or bottom edge
        var x = (short)(RoundedQuotient((short)(edgeY - anchor.Y) * slope.Across, slope.Down, slope.HalfDown) + anchor.X);
        return x < 0 || x > right ? null : (x, edgeY);
    }

    private static short BottomOrTop(short y, short bottom) => y > bottom ? bottom : (short)0;

    /// <summary>
    /// A 32-bit product divided by a 16-bit number and rounded to the nearest whole number, as the original's
    /// instructions do it: a division that cuts towards zero, turned into one that cuts downwards, plus one when the
    /// remainder is at least half the divisor.
    /// </summary>
    // C: the remainder and its comparison with the half are 16-bit, and the rounding is the original's own (a third
    // rounds up when the divisor is 3); kept literally so that every clipped line ends on the original's pixel.
    private static short RoundedQuotient(int product, short divisor, short halfDivisor)
    {
        var productNegative = product < 0;
        int quotient;
        short remainder;
        if (divisor != 0 && product / divisor is var cut && Math.Abs(cut) <= short.MaxValue)
        {
            quotient = cut;
            remainder = (short)(product % divisor);
        }
        else
        {
            // The processor refuses the division (by 0, or a quotient beyond 16 bits; on the 8086 a quotient of
            // -32768 too). The original catches that and carries on with the largest quotient its author allowed.
            // BUG: CA-22 the original's handler only works on an 80286 or later; on an 8086 it returns to the wrong place.
            // DIVERGENCE: D-5 the result the handler intends is used, whatever the processor.
            const short largest = 0x7f00;
            quotient = productNegative != divisor < 0 ? -largest : largest;
            remainder = 0;
        }
        if (productNegative != divisor < 0)
        {
            remainder = (short)-remainder;
            quotient--;
        }
        remainder -= halfDivisor;
        if (remainder < 0 == productNegative)
        {
            quotient++;
        }
        return (short)quotient;
    }
}
