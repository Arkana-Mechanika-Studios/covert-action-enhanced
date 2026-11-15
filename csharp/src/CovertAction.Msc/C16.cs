namespace CovertAction.Msc;

/// <summary>
/// The arithmetic of the original's compiler, Microsoft C 5.1 for the 8086 in the small model, for the places where C#
/// computes differently. In that C an <c>int</c> has 16 bits and a <c>long</c> 32; C# computes every <c>short</c>
/// expression in 32 bits and only narrows when told to. A ported expression gives the original's result when each
/// value is narrowed where the original's was (ARCHITECTURE.md "16-bit C semantics" has the rule and the type table);
/// the functions here are the narrowing points and the operations whose corner cases differ between the two languages.
/// </summary>
/// <remarks>
/// Type mirror: C <c>int</c> = <see cref="short"/>, <c>unsigned</c> = <see cref="ushort"/>, <c>char</c> (signed in MSC)
/// = <see cref="sbyte"/>, <c>unsigned char</c> = <see cref="byte"/>, <c>long</c> = <see cref="int"/>,
/// <c>unsigned long</c> = <see cref="uint"/>.
/// </remarks>
public static class C16
{
    // ---- narrowing: the value an expression has once the original stores it in a variable of that type ----

    /// <summary>The 16-bit <c>int</c> a wider result becomes: the low 16 bits, read as signed (32768 becomes -32768).</summary>
    public static short Int(int value) => unchecked((short)value);

    /// <summary>The 16-bit <c>unsigned</c> a wider result becomes: the low 16 bits (-1 becomes 65535).</summary>
    public static ushort Unsigned(int value) => unchecked((ushort)value);

    /// <summary>The signed <c>char</c> a wider result becomes: the low 8 bits, read as signed (200 becomes -56).</summary>
    public static sbyte Char(int value) => unchecked((sbyte)value);

    /// <summary>The <c>unsigned char</c> a wider result becomes: the low 8 bits.</summary>
    public static byte UChar(int value) => unchecked((byte)value);

    /// <summary>
    /// A <c>char</c> read from a text buffer, promoted to <c>int</c> as MSC does (instruction <c>cbw</c>): bytes 0x80..0xFF
    /// are negative. Text buffers are <see cref="byte"/> arrays in the ports; use this wherever the original computes or
    /// compares with a plain <c>char</c> that may hold such a byte.
    /// </summary>
    public static short CharToInt(byte raw) => unchecked((sbyte)raw);

    /// <summary>The C truth value of a condition: 1 or 0 (what <c>a &lt; b</c> or <c>!x</c> yields as an <c>int</c>).</summary>
    public static short Bool(bool condition) => condition ? (short)1 : (short)0;

    // ---- int: division, shifts, negation ----

    /// <summary>
    /// <c>a / b</c> for two <c>int</c>s (instruction <c>idiv</c>): the quotient is cut towards zero, -7 / 2 = -3.
    /// Division by zero and -32768 / -1 raise the divide error.
    /// </summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    public static short Div(short a, short b)
    {
        CheckDivisor(a, b);
        return (short)(a / b);
    }

    /// <summary>
    /// <c>a % b</c> for two <c>int</c>s (the remainder <c>idiv</c> leaves): it has the sign of <paramref name="a"/>,
    /// -7 % 2 = -1. Division by zero and -32768 % -1 raise the divide error (the processor faults before any remainder exists).
    /// </summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    public static short Rem(short a, short b)
    {
        CheckDivisor(a, b);
        return (short)(a % b);
    }

    /// <summary><c>a / b</c> for two <c>unsigned</c>s (instruction <c>div</c> with DX = 0). Division by zero raises the divide error.</summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    public static ushort UDiv(ushort a, ushort b) =>
        b == 0 ? throw new DivideErrorException($"{a} / 0 (unsigned)") : (ushort)(a / b);

    /// <summary><c>a % b</c> for two <c>unsigned</c>s. Division by zero raises the divide error.</summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    public static ushort URem(ushort a, ushort b) =>
        b == 0 ? throw new DivideErrorException($"{a} % 0 (unsigned)") : (ushort)(a % b);

    private static void CheckDivisor(short a, short b)
    {
        if (b == 0)
        {
            throw new DivideErrorException($"{a} / 0");
        }
        if (a == short.MinValue && b == -1)
        {
            // 32768 does not fit the 16-bit quotient register
            throw new DivideErrorException("-32768 / -1");
        }
    }

    /// <summary>
    /// <c>value &lt;&lt; count</c> for an <c>int</c> and a count that is not a constant (instruction <c>shl ax, cl</c>).
    /// The count is the low byte of the count expression, 0..255, and the processor shifts one bit that many times:
    /// the bits shifted out are lost, so 16 and more give 0.
    /// </summary>
    /// <remarks>
    /// This is the 8086 and 8088, the processors the game was written for (decision of 13 November 2026,
    /// docs/faithfulness/DECISIONS.md). Processors from the 80286 on use only the low five bits of the count, so a
    /// count of 32 shifts by 0 there; the oracle's emulator is such a processor, and a case whose count exceeds 31
    /// cannot be checked against it.
    /// </remarks>
    public static short Shl(short value, int count)
    {
        var n = count & 0xFF;
        return n >= 16 ? (short)0 : unchecked((short)(value << n));
    }

    /// <summary>
    /// <c>value &gt;&gt; count</c> for a signed <c>int</c> (instruction <c>sar ax, cl</c>): the sign bit is copied in from
    /// the left, so negative values stay negative (-1 &gt;&gt; n = -1) and counts of 16 and more give 0 or -1.
    /// The count is taken as in <see cref="Shl"/>.
    /// </summary>
    public static short Sar(short value, int count)
    {
        var n = count & 0xFF;
        return (short)(value >> (n >= 16 ? 15 : n));
    }

    /// <summary>
    /// <c>value &gt;&gt; count</c> for an <c>unsigned</c> (instruction <c>shr ax, cl</c>): zeros come in from the left,
    /// counts of 16 and more give 0. The count is taken as in <see cref="Shl"/>.
    /// </summary>
    public static ushort Shr(ushort value, int count)
    {
        var n = count & 0xFF;
        return n >= 16 ? (ushort)0 : (ushort)(value >> n);
    }

    /// <summary>Unary minus on an <c>int</c> (instruction <c>neg</c>): -(-32768) stays -32768.</summary>
    public static short Neg(short value) => unchecked((short)-value);

    /// <summary>The library's <c>abs()</c>: negative values are negated, so abs(-32768) is still -32768.</summary>
    // orig: CODE 0000:362a abs (SLIBCR.LIB abs.c) lib; also FINAL 0100:e8b2, GAME 00f7:fc44, TAC 00e8:fd20, BUG 00e8:95c6, CHASE 00df:5722; tests: C16Tests
    public static short Abs(short value) => value < 0 ? Neg(value) : value;

    // ---- bytes and words ----

    /// <summary>The low byte of a word (what a <c>char</c> view of an <c>int</c> variable reads on this little-endian machine).</summary>
    public static byte LoByte(short word) => unchecked((byte)word);

    /// <summary>The high byte of a word.</summary>
    public static byte HiByte(short word) => unchecked((byte)(word >> 8));

    /// <summary>The word made of two bytes (<paramref name="hi"/> * 256 + <paramref name="lo"/>).</summary>
    public static short MakeWord(byte lo, byte hi) => unchecked((short)(lo | hi << 8));

    // ---- long: the run-time helpers the compiler calls for 32-bit arithmetic ----

    /// <summary>The <c>long</c> held in the register pair DX:AX (how the original returns and passes 32-bit values).</summary>
    public static int Long(ushort ax, short dx) => dx << 16 | ax;

    /// <summary>The low word (AX) of a <c>long</c>; <c>(int)longValue</c> in the original.</summary>
    public static short LoWord(int value) => unchecked((short)value);

    /// <summary>The high word (DX) of a <c>long</c>.</summary>
    public static short HiWord(int value) => (short)(value >> 16);

    /// <summary>
    /// <c>a * b</c> for two <c>long</c>s: the low 32 bits of the product; overflow wraps silently. An <c>int</c> operand
    /// must have been widened as the original widens it (signed: plain C# conversion; <c>unsigned</c>: through <see cref="ushort"/>).
    /// </summary>
    // orig: CODE 0000:368c __aNlmul (LIBH.LIB lmul.asm) lib; also GAME, FINAL, TAC, BUG, CHASE copies, FINAL 0100:eac8, GAME 00f7:fe3a, TAC 00e8:fe96, BUG 00e8:973c, CHASE 00df:57fc; tests: C16Tests
    public static int LMul(int a, int b) => unchecked(a * b);

    /// <summary>
    /// <c>a / b</c> for two <c>long</c>s. The helper divides the magnitudes as unsigned numbers and then sets the sign, so
    /// the quotient is cut towards zero and -2147483648 / -1 quietly gives -2147483648 (C# would throw). Division by zero
    /// raises the divide error.
    /// </summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    // orig: GAME 00f7:fd9e __aNldiv (LIBH.LIB ldiv.asm) lib; also FINAL 0100:ea2c and the TAC, BUG copies, TAC 00e8:fdfa, BUG 00e8:96a0; tests: C16Tests
    public static int LDiv(int a, int b)
    {
        if (b == 0)
        {
            throw new DivideErrorException($"{a}L / 0");
        }
        var quotient = Magnitude(a) / Magnitude(b);
        return unchecked((int)((a < 0) != (b < 0) ? 0 - quotient : quotient));
    }

    /// <summary>
    /// <c>a % b</c> for two <c>long</c>s: the remainder of the magnitudes with the sign of <paramref name="a"/>;
    /// -2147483648 % -1 is 0. Division by zero raises the divide error.
    /// </summary>
    /// <exception cref="DivideErrorException">The original would stop with a divide error.</exception>
    // orig: TAC 00e8:feca __aNlrem (LIBH.LIB lrem.asm) lib; tests: C16Tests
    public static int LRem(int a, int b)
    {
        if (b == 0)
        {
            throw new DivideErrorException($"{a}L % 0");
        }
        var remainder = Magnitude(a) % Magnitude(b);
        return unchecked((int)(a < 0 ? 0 - remainder : remainder));
    }

    /// <summary>|value| as an unsigned number; -2147483648 gives 2147483648, as the helper's <c>neg</c> on DX:AX does.</summary>
    private static uint Magnitude(int value) => unchecked(value < 0 ? 0 - (uint)value : (uint)value);

    /// <summary>
    /// <c>value &lt;&lt; count</c> for a <c>long</c>. The helper shifts one bit at a time, as often as the low byte of the
    /// count says (0..255): 32 and more give 0. C#'s own shift would use only the low five bits.
    /// </summary>
    // orig: GAME 00f7:fe6e __aNlshl (LIBH.LIB lshl.asm) lib; also FINAL 0100:eafc, TAC 00e8:ff6c, BUG 00e8:9770; tests: C16Tests
    public static int LShl(int value, int count)
    {
        var n = count & 0xFF;
        return n >= 32 ? 0 : value << n;
    }

    /// <summary>
    /// <c>value &gt;&gt; count</c> for a signed <c>long</c>. One bit at a time with the sign copied in, as often as the low
    /// byte of the count says (0..255): 32 and more give 0 for positive values and -1 for negative ones. The game's own
    /// random-number idiom, <c>(long)n * rand() &gt;&gt; 15</c>, goes through this helper.
    /// </summary>
    // orig: CODE 0000:36c0 __aNlshr (LIBH.LIB lshr.asm) lib; also GAME, FINAL, TAC, BUG, CHASE copies; tests: C16Tests
    public static int LShr(int value, int count)
    {
        var n = count & 0xFF;
        return value >> (n >= 32 ? 31 : n);
    }
}
