using CovertAction.Msc;

namespace CovertAction.Msc.Tests;

/// <summary>
/// The 16-bit arithmetic helpers against values worked out from the 8086 instructions MSC 5.1 emits (idiv, shl/sar/shr
/// by CL, cbw) and from the long helpers' own instructions (lmul, ldiv, lrem, lshl, lshr of LIBH.LIB as linked into the game).
/// </summary>
public sealed class C16Tests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(32767, 32767)]
    [InlineData(32768, -32768)]
    [InlineData(65535, -1)]
    [InlineData(65536, 0)]
    [InlineData(-32769, 32767)]
    [InlineData(0x12345678, 0x5678)]
    public void IntKeepsTheLow16BitsSigned(int wide, short expected) => Assert.Equal(expected, C16.Int(wide));

    [Theory]
    [InlineData(-1, 65535)]
    [InlineData(65536, 0)]
    [InlineData(0x1FFFE, 0xFFFE)]
    public void UnsignedKeepsTheLow16Bits(int wide, int expected) => Assert.Equal((ushort)expected, C16.Unsigned(wide));

    [Theory]
    [InlineData(127, 127)]
    [InlineData(128, -128)]
    [InlineData(200, -56)]
    [InlineData(255, -1)]
    [InlineData(256, 0)]
    public void CharKeepsTheLow8BitsSigned(int wide, sbyte expected)
    {
        Assert.Equal(expected, C16.Char(wide));
        Assert.Equal(unchecked((byte)wide), C16.UChar(wide));
    }

    [Theory]
    [InlineData(0x41, 0x41)]
    [InlineData(0x7F, 127)]
    [InlineData(0x80, -128)]
    [InlineData(0xFF, -1)]
    public void ACharFromATextBufferIsSignExtended(byte raw, short expected) => Assert.Equal(expected, C16.CharToInt(raw));

    [Fact]
    public void TruthValuesAreOneAndZero()
    {
        Assert.Equal(1, C16.Bool(true));
        Assert.Equal(0, C16.Bool(false));
    }

    [Theory]
    [InlineData(7, 2, 3, 1)]
    [InlineData(-7, 2, -3, -1)]
    [InlineData(7, -2, -3, 1)]
    [InlineData(-7, -2, 3, -1)]
    [InlineData(-32768, 1, -32768, 0)]
    [InlineData(-32768, 2, -16384, 0)]
    [InlineData(32767, -1, -32767, 0)]
    [InlineData(1, 32767, 0, 1)]
    public void DivisionCutsTowardsZeroAndTheRemainderHasTheDividendsSign(short a, short b, short quotient, short remainder)
    {
        Assert.Equal(quotient, C16.Div(a, b));
        Assert.Equal(remainder, C16.Rem(a, b));
        // the identity idiv guarantees
        Assert.Equal(a, (short)(C16.Div(a, b) * b + C16.Rem(a, b)));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 0)]
    [InlineData(-32768, -1)]
    public void ADivisionTheProcessorCannotDoIsADivideError(short a, short b)
    {
        Assert.Throws<DivideErrorException>(() => C16.Div(a, b));
        Assert.Throws<DivideErrorException>(() => C16.Rem(a, b));
    }

    [Fact]
    public void UnsignedDivisionTakesTheBitsAsUnsigned()
    {
        // 0xFFFE is -2 as an int and 65534 as an unsigned
        Assert.Equal(32767, C16.UDiv(0xFFFE, 2));
        Assert.Equal(-1, C16.Div(-2, 2));
        Assert.Equal(4, C16.URem(0xFFFE, 10));
        Assert.Throws<DivideErrorException>(() => C16.UDiv(1, 0));
        Assert.Throws<DivideErrorException>(() => C16.URem(1, 0));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 15, -32768)]
    [InlineData(1, 16, 0)]
    [InlineData(-1, 31, 0)]
    [InlineData(3, 14, -16384)]
    [InlineData(0x4001, 1, -32766)]
    [InlineData(1, 32, 0)]      // the 8086 shifts as often as the count says (a 80286 would take it modulo 32)
    [InlineData(1, 255, 0)]
    [InlineData(1, 0x104, 16)]  // only the low byte of the count reaches CL
    public void ShiftLeftLosesTheBitsShiftedOut(short value, int count, short expected) => Assert.Equal(expected, C16.Shl(value, count));

    [Theory]
    [InlineData(-32768, 15, -1)]
    [InlineData(-32768, 16, -1)]
    [InlineData(-2, 1, -1)]
    [InlineData(-1, 31, -1)]
    [InlineData(32767, 14, 1)]
    [InlineData(32767, 16, 0)]
    [InlineData(-7, 1, -4)]     // rounds towards minus infinity, unlike -7 / 2
    [InlineData(-7, 32, -1)]    // 8086: 32 one-bit shifts
    [InlineData(1000, 40, 0)]
    [InlineData(-7, 0x101, -4)] // only the low byte of the count is used
    public void SignedShiftRightCopiesTheSign(short value, int count, short expected) => Assert.Equal(expected, C16.Sar(value, count));

    [Theory]
    [InlineData(0x8000, 15, 1)]
    [InlineData(0xFFFF, 16, 0)]
    [InlineData(0xFFFE, 1, 0x7FFF)]
    [InlineData(0xFFFF, 31, 0)]
    [InlineData(0xFFFF, 32, 0)]
    [InlineData(0xFFFF, 0x100, 0xFFFF)]
    public void UnsignedShiftRightBringsInZeros(int value, int count, int expected) =>
        Assert.Equal((ushort)expected, C16.Shr((ushort)value, count));

    [Fact]
    public void TheShiftsMatchOneBitShiftsRepeatedAsOftenAsTheCountSays()
    {
        short[] values = [0, 1, -1, 0x1234, short.MinValue, short.MaxValue, -7];
        foreach (var value in values)
        {
            for (var count = 0; count < 256; count++)
            {
                short left = value, arithmetic = value;
                var logical = (ushort)value;
                for (var i = 0; i < count; i++)
                {
                    left = (short)(left << 1);
                    arithmetic = (short)(arithmetic >> 1);
                    logical = (ushort)(logical >> 1);
                }
                Assert.Equal(left, C16.Shl(value, count));
                Assert.Equal(arithmetic, C16.Sar(value, count));
                Assert.Equal(logical, C16.Shr((ushort)value, count));
            }
        }
    }

    [Fact]
    public void NegatingTheSmallestIntLeavesIt()
    {
        Assert.Equal(-5, C16.Neg(5));
        Assert.Equal(short.MinValue, C16.Neg(short.MinValue));
        Assert.Equal(5, C16.Abs(-5));
        Assert.Equal(5, C16.Abs(5));
        Assert.Equal(short.MinValue, C16.Abs(short.MinValue));
    }

    [Fact]
    public void BytesAndWords()
    {
        Assert.Equal(0x34, C16.LoByte(0x1234));
        Assert.Equal(0x12, C16.HiByte(0x1234));
        Assert.Equal(0xFF, C16.HiByte(-2));
        Assert.Equal(0x1234, C16.MakeWord(0x34, 0x12));
        Assert.Equal(-2, C16.MakeWord(0xFE, 0xFF));
        Assert.Equal(0x12345678, C16.Long(0x5678, 0x1234));
        Assert.Equal(-1, C16.Long(0xFFFF, -1));
        Assert.Equal(0x5678, C16.LoWord(0x12345678));
        Assert.Equal(0x1234, C16.HiWord(0x12345678));
        Assert.Equal(-1, C16.HiWord(-2));
    }

    /// <summary>__aNlmul as the library computes it: 16-bit partial products, the high cross terms dropped.</summary>
    private static int LibraryLMul(int a, int b)
    {
        ushort aLo = (ushort)a, aHi = (ushort)(a >>> 16), bLo = (ushort)b, bHi = (ushort)(b >>> 16);
        var low = (uint)aLo * bLo;
        var cross = (ushort)(aHi * bLo + aLo * bHi);
        return (int)(low + ((uint)cross << 16));
    }

    [Theory]
    [InlineData(0, 12345)]
    [InlineData(3, 32767)]
    [InlineData(26, 32767)]
    [InlineData(-3, 32767)]
    [InlineData(65536, 65536)]
    [InlineData(0x12345678, 0x0BADF00D)]
    [InlineData(int.MinValue, -1)]
    [InlineData(-1, -1)]
    public void LongMultiplyKeepsTheLow32Bits(int a, int b)
    {
        Assert.Equal(LibraryLMul(a, b), C16.LMul(a, b));
        Assert.Equal((int)((long)a * b), C16.LMul(a, b));
    }

    [Theory]
    [InlineData(7, 2, 3, 1)]
    [InlineData(-7, 2, -3, -1)]
    [InlineData(7, -2, -3, 1)]
    [InlineData(-7, -2, 3, -1)]
    [InlineData(1000000, 7, 142857, 1)]
    [InlineData(-1000000, 70000, -14, -20000)]
    [InlineData(int.MinValue, -1, int.MinValue, 0)]   // the helper wraps where the processor's own idiv would fault
    [InlineData(int.MinValue, 1, int.MinValue, 0)]
    [InlineData(int.MinValue, 2, -1073741824, 0)]
    [InlineData(int.MaxValue, int.MinValue, 0, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue, 1, 0)]
    public void LongDivisionWorksOnMagnitudes(int a, int b, int quotient, int remainder)
    {
        Assert.Equal(quotient, C16.LDiv(a, b));
        Assert.Equal(remainder, C16.LRem(a, b));
    }

    [Fact]
    public void LongDivisionByZeroIsADivideError()
    {
        Assert.Throws<DivideErrorException>(() => C16.LDiv(1, 0));
        Assert.Throws<DivideErrorException>(() => C16.LRem(1, 0));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 31, int.MinValue)]
    [InlineData(1, 32, 0)]        // C# alone would give 1
    [InlineData(-1, 40, 0)]
    [InlineData(1, 255, 0)]
    [InlineData(1, 256, 1)]       // only the low byte of the count is used
    [InlineData(0x00012345, 12, 0x12345000)]
    public void LongShiftLeftShiftsAsOftenAsTheCountSays(int value, int count, int expected) => Assert.Equal(expected, C16.LShl(value, count));

    [Theory]
    [InlineData(int.MinValue, 31, -1)]
    [InlineData(int.MinValue, 32, -1)]   // C# alone would give int.MinValue
    [InlineData(int.MaxValue, 32, 0)]
    [InlineData(-1, 200, -1)]
    [InlineData(1000, 256, 1000)]
    [InlineData(26 * 32767, 15, 25)]
    [InlineData(-3 * 32767, 15, -3)]     // rounds towards minus infinity
    [InlineData(-32768, 15, -1)]
    [InlineData(-32769, 15, -2)]
    public void LongShiftRightCopiesTheSign(int value, int count, int expected) => Assert.Equal(expected, C16.LShr(value, count));

    /// <summary>The shifts against a bit-at-a-time model of the helpers' loops, for every count.</summary>
    [Fact]
    public void LongShiftsMatchTheOneBitLoopForEveryCount()
    {
        int[] values = [0, 1, -1, 0x12345678, int.MinValue, int.MaxValue, -98301];
        foreach (var value in values)
        {
            for (var count = 0; count < 256; count++)
            {
                int left = value, right = value;
                for (var i = 0; i < count; i++)
                {
                    left <<= 1;   // shl ax,1 / rcl dx,1
                    right >>= 1;  // sar dx,1 / rcr ax,1
                }
                Assert.Equal(left, C16.LShl(value, count));
                Assert.Equal(right, C16.LShr(value, count));
            }
        }
    }
}
