namespace CovertAction.Msc;

/// <summary>
/// The C string routines of the Microsoft C 5.1 library, working on byte buffers as the original does: a text is the
/// bytes up to the first zero byte (the end mark), and nothing knows how large its buffer is. The game's texts stay
/// <see cref="byte"/> buffers in the ports (never .NET strings) so that lengths, truncation and overflow behave as in
/// the original; only the screen and the file readers turn bytes into characters.
/// </summary>
/// <remarks>
/// Each routine follows the library's instructions as found in the game's own programs, including the order in which
/// bytes are read and written; that order decides the result when source and target overlap. A routine that would
/// leave the span it was given throws <see cref="MemoryOverrunException"/>: pass the bytes the original can really
/// reach. The two address-parity parameters only matter for a copy onto a place behind its own source (see
/// <see cref="Strcpy"/>).
/// </remarks>
public static class CStr
{
    /// <summary>The length of the text at the start of <paramref name="text"/>: the number of bytes before the end mark.</summary>
    /// <exception cref="MemoryOverrunException">No end mark inside the span.</exception>
    // orig: CODE 0000:30ae strlen (SLIBCR.LIB strlen.asm) lib; also FINAL 0100:e0ba, GAME 00f7:f43c, TAC 00e8:f154, BUG 00e8:8ea6, CHASE 00df:50b8; tests: CStrTests
    public static short Strlen(ReadOnlySpan<byte> text)
    {
        var end = text.IndexOf((byte)0);
        return end < 0
            ? throw new MemoryOverrunException($"strlen: no end mark within {text.Length} bytes")
            : (short)end;
    }

    /// <summary>
    /// Copies the text at <paramref name="source"/>, end mark included, to the start of <paramref name="target"/>.
    /// The length is measured first; then the bytes move front to back, two at a time (one single byte first when the
    /// target's address is odd, one last when a byte is left over).
    /// </summary>
    /// <param name="target">Where the text goes; must hold the text and its end mark.</param>
    /// <param name="source">The text to copy.</param>
    /// <param name="targetAddressOdd">
    /// Whether the target's address in the data segment is odd. It changes the result only when the target starts
    /// inside the source text behind its first byte (a copy that overwrites what it has yet to read).
    /// </param>
    /// <exception cref="MemoryOverrunException">The text has no end mark, or the target is too small.</exception>
    // orig: CODE 0000:3050 strcpy (SLIBCR.LIB strcpy.asm) lib; also FINAL 0100:e05c, GAME 00f7:f3de, TAC 00e8:f0f6, BUG 00e8:8e48, CHASE 00df:505a; tests: CStrTests
    public static void Strcpy(Span<byte> target, ReadOnlySpan<byte> source, bool targetAddressOdd = false) =>
        MoveForward(target, source, Strlen(source) + 1, targetAddressOdd, "strcpy");

    /// <summary>
    /// Appends the text at <paramref name="source"/>, end mark included, to the text at <paramref name="target"/>.
    /// Both lengths are measured first; the bytes then move as in <see cref="Strcpy"/>, except that the single first
    /// byte depends on the <em>source</em>'s address.
    /// </summary>
    /// <param name="target">The text that grows; must hold both texts and one end mark.</param>
    /// <param name="source">The text to append.</param>
    /// <param name="sourceAddressOdd">Whether the source's address is odd (matters only for overlapping texts).</param>
    /// <exception cref="MemoryOverrunException">A text has no end mark, or the target is too small.</exception>
    // orig: CODE 0000:3010 strcat (SLIBCR.LIB strcat.asm) lib; also FINAL 0100:e01c, GAME 00f7:f39e, TAC 00e8:f0b6, BUG 00e8:8e08, CHASE 00df:501a; tests: CStrTests
    public static void Strcat(Span<byte> target, ReadOnlySpan<byte> source, bool sourceAddressOdd = false)
    {
        var end = Strlen(target);
        MoveForward(target[end..], source, Strlen(source) + 1, sourceAddressOdd, "strcat");
    }

    /// <summary>
    /// The library's block move (<c>movsb</c> if the address is odd, <c>rep movsw</c>, <c>movsb</c> for a last byte): each
    /// step reads its one or two bytes and then writes them, so overlapping spans behave as the original memory does.
    /// </summary>
    private static void MoveForward(Span<byte> target, ReadOnlySpan<byte> source, int count, bool oddStart, string routine)
    {
        if (count > target.Length)
        {
            throw new MemoryOverrunException($"{routine}: {count} bytes into a buffer of {target.Length}");
        }
        var i = 0;
        if (oddStart && count > 0)
        {
            target[0] = source[0];
            i = 1;
        }
        for (; i + 1 < count; i += 2)
        {
            var (first, second) = (source[i], source[i + 1]);
            target[i] = first;
            target[i + 1] = second;
        }
        if (i < count)
        {
            target[i] = source[i];
        }
    }

    /// <summary>
    /// Compares two texts byte by byte, bytes taken as unsigned (0..255): -1 when <paramref name="a"/> sorts first, 1 when
    /// <paramref name="b"/> does, 0 when they are the same. (Not the difference of the bytes: exactly -1, 0 or 1.)
    /// </summary>
    /// <exception cref="MemoryOverrunException">A text has no end mark inside its span.</exception>
    // orig: CODE 0000:3082 strcmp (SLIBCR.LIB strcmp.asm) lib; also FINAL 0100:e08e, GAME 00f7:f410, TAC 00e8:f128, BUG 00e8:8e7a, CHASE 00df:508c; tests: CStrTests
    public static short Strcmp(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        // the library compares strlen(b) + 1 bytes, so the comparison ends at b's end mark at the latest
        var count = Strlen(b) + 1;
        for (var i = 0; i < count; i++)
        {
            if (i >= a.Length)
            {
                throw new MemoryOverrunException($"strcmp: first text has no end mark within {a.Length} bytes");
            }
            if (a[i] != b[i])
            {
                return a[i] < b[i] ? (short)-1 : (short)1;
            }
        }
        return 0;
    }

    /// <summary>
    /// Compares at most <paramref name="count"/> bytes of two texts, bytes as unsigned: -1, 0 or 1 as <see cref="Strcmp"/>.
    /// The comparison ends at the end mark of <paramref name="a"/>.
    /// </summary>
    /// <param name="a">First text.</param>
    /// <param name="b">Second text.</param>
    /// <param name="count">The most bytes to compare; an <c>unsigned</c> in the original (a negative <c>int</c> is a large count).</param>
    /// <exception cref="MemoryOverrunException">The comparison would leave a span.</exception>
    // orig: CODE 0000:30f2 strncmp (SLIBCR.LIB strncmp.asm) lib; also FINAL 0100:e0fe, GAME 00f7:f480, TAC 00e8:f198, BUG 00e8:8eea, CHASE 00df:50fc; tests: CStrTests
    public static short Strncmp(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ushort count)
    {
        if (count == 0)
        {
            return 0;
        }
        // the library first looks for a's end mark within count bytes and compares up to and including it
        var end = a[..Math.Min(count, a.Length)].IndexOf((byte)0);
        if (end < 0 && a.Length < count)
        {
            throw new MemoryOverrunException($"strncmp: first text has no end mark within {a.Length} bytes");
        }
        var length = end < 0 ? count : end + 1;
        for (var i = 0; i < length; i++)
        {
            if (i >= b.Length)
            {
                throw new MemoryOverrunException($"strncmp: second text ends after {b.Length} bytes");
            }
            if (a[i] != b[i])
            {
                return a[i] < b[i] ? (short)-1 : (short)1;
            }
        }
        return 0;
    }

    /// <summary>
    /// Copies at most <paramref name="count"/> bytes of the text at <paramref name="source"/> and fills the rest of the
    /// <paramref name="count"/> bytes with zeros. A text of <paramref name="count"/> bytes or more arrives without an end mark.
    /// </summary>
    /// <param name="target">Where the bytes go; exactly <paramref name="count"/> bytes are written.</param>
    /// <param name="source">The text to copy.</param>
    /// <param name="count">The number of bytes to write; an <c>unsigned</c> in the original.</param>
    /// <exception cref="MemoryOverrunException">The target is smaller than <paramref name="count"/>, or the source ends early without an end mark.</exception>
    // orig: CODE 0000:30ca strncpy (SLIBCR.LIB strncpy.asm) lib; also FINAL 0100:e0d6, GAME 00f7:f458, TAC 00e8:f170, BUG 00e8:8ec2, CHASE 00df:50d4; tests: CStrTests
    public static void Strncpy(Span<byte> target, ReadOnlySpan<byte> source, ushort count)
    {
        if (count > target.Length)
        {
            throw new MemoryOverrunException($"strncpy: {count} bytes into a buffer of {target.Length}");
        }
        var i = 0;
        for (; i < count; i++)
        {
            if (i >= source.Length)
            {
                throw new MemoryOverrunException($"strncpy: source has no end mark within {source.Length} bytes");
            }
            if (source[i] == 0)
            {
                break;
            }
            target[i] = source[i];
        }
        target[i..count].Clear();
    }

    /// <summary>Turns the letters a..z of the text into A..Z, in place. Other bytes (accented letters included) stay.</summary>
    /// <exception cref="MemoryOverrunException">No end mark inside the span.</exception>
    // orig: CODE 0000:35a8 strupr (SLIBCR.LIB strupr.asm) lib; also TAC 00e8:fbe8; tests: CStrTests
    public static void Strupr(Span<byte> text)
    {
        var length = Strlen(text);
        for (var i = 0; i < length; i++)
        {
            if (text[i] is >= (byte)'a' and <= (byte)'z')
            {
                text[i] -= 0x20;
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> as text in the given number base, with its end mark. Base 10 writes negative
    /// numbers with a minus sign; every other base writes the 16 bits as an unsigned number (-1 in base 16 is "ffff").
    /// Digits above 9 are the lower-case letters.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="target">Where the text goes: up to 17 digits (base 2), or 6 characters in base 10, plus the end mark.</param>
    /// <param name="radix">The number base, 2..36.</param>
    /// <returns>The number of characters written, not counting the end mark.</returns>
    /// <exception cref="MemoryOverrunException">The target is too small.</exception>
    /// <exception cref="DivideErrorException">Base 0 (the original divides by it).</exception>
    // orig: CODE 0000:3184 itoa (SLIBCR.LIB itoa.asm, its digit loop __cxtoa CODE 0000:35d6 in xtoa.asm) lib; also FINAL 0100:e190, GAME 00f7:f512, TAC 00e8:f22a, BUG 00e8:8f7c, CHASE 00df:518e, FINAL 0100:e85e, GAME 00f7:fbf0, TAC 00e8:fccc, BUG 00e8:9572, CHASE 00df:56ce; tests: CStrTests
    public static short Itoa(short value, Span<byte> target, short radix)
    {
        if (radix == 0)
        {
            throw new DivideErrorException("itoa: base 0");
        }
        if (radix is < 2 or > 36)
        {
            // DIVERGENCE: D-1 the original never ends in base 1 and writes arbitrary bytes for other bases; no caller passes one.
            throw new ArgumentOutOfRangeException(nameof(radix), radix, "itoa: the game only uses bases 2..36");
        }
        Span<byte> digits = stackalloc byte[17];
        var negative = radix == 10 && value < 0;
        // C: base 10 takes the value as a signed int (and -32768 has the magnitude 32768); other bases as unsigned
        var rest = negative ? (uint)-value : (ushort)value;
        var count = 0;
        do
        {
            var digit = rest % (uint)radix;
            digits[count++] = (byte)(digit <= 9 ? '0' + digit : 'a' + digit - 10);
            rest /= (uint)radix;
        }
        while (rest != 0);

        var length = count + (negative ? 1 : 0);
        if (length + 1 > target.Length)
        {
            throw new MemoryOverrunException($"itoa: {length + 1} bytes into a buffer of {target.Length}");
        }
        var at = 0;
        if (negative)
        {
            target[at++] = (byte)'-';
        }
        while (count > 0)
        {
            target[at++] = digits[--count];
        }
        target[at] = 0;
        return (short)length;
    }

    /// <summary>
    /// The bytes of a text written in the source (ASCII), with their end mark: how a port spells one of the original's
    /// string constants when it must hand it to these routines.
    /// </summary>
    public static byte[] Literal(string text)
    {
        var bytes = new byte[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = text[i] <= 0x7F
                ? (byte)text[i]
                : throw new ArgumentException("only ASCII; other bytes are written as numbers", nameof(text));
        }
        return bytes;
    }
}
