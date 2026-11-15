using System.Text;
using CovertAction.Msc;

namespace CovertAction.Msc.Tests;

/// <summary>
/// The C string routines against the behaviour of the library's instructions (CODE 0000:3010..35d6): results,
/// exactly which bytes are written, and what happens at the edge of a buffer.
/// </summary>
public sealed class CStrTests
{
    /// <summary>A buffer of <paramref name="size"/> bytes holding <paramref name="text"/> and its end mark, the rest filled with '#'.</summary>
    private static byte[] Buffer(string text, int size)
    {
        var buffer = Enumerable.Repeat((byte)'#', size).ToArray();
        CStr.Literal(text).CopyTo(buffer, 0);
        return buffer;
    }

    private static string Show(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes).Replace('\0', '0');

    [Fact]
    public void LiteralAddsTheEndMark()
    {
        Assert.Equal(new byte[] { (byte)'O', (byte)'K', 0 }, CStr.Literal("OK"));
        Assert.Equal(new byte[] { 0 }, CStr.Literal(""));
        Assert.Throws<ArgumentException>(() => CStr.Literal("é"));
    }

    [Fact]
    public void StrlenCountsUpToTheEndMark()
    {
        Assert.Equal(0, CStr.Strlen(CStr.Literal("")));
        Assert.Equal(5, CStr.Strlen(Buffer("HELLO", 16)));
        Assert.Throws<MemoryOverrunException>(() => CStr.Strlen("ABC"u8));
    }

    [Fact]
    public void StrcpyWritesTheTextAndItsEndMarkAndNothingElse()
    {
        var target = Buffer("", 8);
        target[0] = (byte)'#';
        CStr.Strcpy(target, CStr.Literal("ABC"));
        Assert.Equal("ABC0####", Show(target));
        CStr.Strcpy(target, CStr.Literal(""));
        Assert.Equal("0BC0####", Show(target));
    }

    [Fact]
    public void StrcpyIntoATooSmallBufferIsAnOverrun()
    {
        Assert.Throws<MemoryOverrunException>(() => CStr.Strcpy(new byte[3], CStr.Literal("ABC")));
        Assert.Throws<MemoryOverrunException>(() => CStr.Strcpy(new byte[8], "ABC"u8));
    }

    /// <summary>The game's way of deleting one character: strcpy(&amp;text[i], &amp;text[i + 1]) (setup_cipher copies through a temporary; others do not).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrcpyOntoAPlaceBeforeItsSourceShiftsTheText(bool targetOdd)
    {
        var text = Buffer("A.BCDEF", 10);
        CStr.Strcpy(text.AsSpan(1), text.AsSpan(2), targetOdd);
        Assert.Equal("ABCDEF0", Show(text.AsSpan(0, 7)));
    }

    /// <summary>
    /// A copy onto a place behind its own source overwrites what it has yet to read; the two-bytes-at-a-time move then
    /// repeats pairs, and where the pairs start depends on whether the target's address is odd.
    /// </summary>
    [Fact]
    public void StrcpyOntoAPlaceBehindItsSourceRepeatsAsTheWordMoveDoes()
    {
        // source "ABCDEF" at 0, target at 1: 7 bytes move (the text and its end mark)
        var even = Buffer("ABCDEF", 12);
        CStr.Strcpy(even.AsSpan(1), even.AsSpan(0), targetAddressOdd: false);
        // pairs: (A,B) -> 1..2; then 2..3, now (B,D) -> 3..4; then 4..5, now (D,F) -> 5..6; the last byte 6, now F -> 7
        Assert.Equal("AABBDDFF", Show(even.AsSpan(0, 8)));

        var odd = Buffer("ABCDEF", 12);
        CStr.Strcpy(odd.AsSpan(1), odd.AsSpan(0), targetAddressOdd: true);
        // one byte first: A -> 1; then pairs: 1..2, now (A,C) -> 2..3; 3..4, now (C,E) -> 4..5; 5..6, now (E, end mark) -> 6..7
        Assert.Equal("AAACCEE0", Show(odd.AsSpan(0, 8)));
    }

    [Fact]
    public void StrcatAppendsAtTheEndMark()
    {
        var target = Buffer("AB", 8);
        CStr.Strcat(target, CStr.Literal("CD"));
        Assert.Equal("ABCD0###", Show(target));
        CStr.Strcat(target, CStr.Literal(""));
        Assert.Equal("ABCD0###", Show(target));
        Assert.Throws<MemoryOverrunException>(() => CStr.Strcat(target, CStr.Literal("EFGH")));
    }

    [Theory]
    [InlineData("ABC", "ABC", 0)]
    [InlineData("ABC", "ABD", -1)]
    [InlineData("ABD", "ABC", 1)]
    [InlineData("AB", "ABC", -1)]
    [InlineData("ABC", "AB", 1)]
    [InlineData("", "", 0)]
    [InlineData("", "A", -1)]
    [InlineData("a", "B", 1)]
    public void StrcmpGivesMinusOneZeroOrOne(string a, string b, short expected) =>
        Assert.Equal(expected, CStr.Strcmp(CStr.Literal(a), CStr.Literal(b)));

    [Fact]
    public void StrcmpTakesBytesAsUnsigned()
    {
        // 0x80 is above 'A' although it is negative as a signed char
        Assert.Equal(1, CStr.Strcmp([0x80, 0], CStr.Literal("A")));
        Assert.Equal(-1, CStr.Strcmp(CStr.Literal("A"), [0x80, 0]));
    }

    [Theory]
    [InlineData("ABCDEF", "ABCxyz", 3, 0)]
    [InlineData("ABCDEF", "ABCxyz", 4, -1)]
    [InlineData("ABD", "ABC", 3, 1)]
    [InlineData("ABD", "ABC", 0, 0)]
    [InlineData("AB", "ABC", 5, -1)]
    [InlineData("ABC", "AB", 5, 1)]
    [InlineData("AB", "AB", 5, 0)]
    public void StrncmpComparesAtMostCountBytes(string a, string b, int count, short expected) =>
        Assert.Equal(expected, CStr.Strncmp(CStr.Literal(a), CStr.Literal(b), (ushort)count));

    [Fact]
    public void StrncpyPadsWithZerosAndDoesNotAddAnEndMarkToALongText()
    {
        var target = Buffer("", 8);
        target[0] = (byte)'#';
        CStr.Strncpy(target, CStr.Literal("AB"), 5);
        Assert.Equal("AB000###", Show(target));

        target = Buffer("", 8);
        target[0] = (byte)'#';
        CStr.Strncpy(target, CStr.Literal("ABCDEFG"), 3);
        Assert.Equal("ABC#####", Show(target));

        CStr.Strncpy(target, CStr.Literal("XYZ"), 0);
        Assert.Equal("ABC#####", Show(target));
        Assert.Throws<MemoryOverrunException>(() => CStr.Strncpy(new byte[2], CStr.Literal("ABC"), 3));
    }

    [Fact]
    public void StruprChangesOnlyTheLettersAToZ()
    {
        var text = new byte[] { (byte)'a', (byte)'z', (byte)'A', (byte)'1', (byte)'`', (byte)'{', 0x81, 0xE9, 0, (byte)'q' };
        CStr.Strupr(text);
        Assert.Equal(new byte[] { (byte)'A', (byte)'Z', (byte)'A', (byte)'1', (byte)'`', (byte)'{', 0x81, 0xE9, 0, (byte)'q' }, text);
    }

    [Theory]
    [InlineData(0, 10, "0")]
    [InlineData(7, 10, "7")]
    [InlineData(173, 10, "173")]
    [InlineData(32767, 10, "32767")]
    [InlineData(-1, 10, "-1")]
    [InlineData(-32768, 10, "-32768")]
    [InlineData(255, 16, "ff")]
    [InlineData(-1, 16, "ffff")]       // other bases take the 16 bits as unsigned
    [InlineData(-32768, 16, "8000")]
    [InlineData(5, 2, "101")]
    [InlineData(-1, 2, "1111111111111111")]
    [InlineData(35, 36, "z")]
    public void ItoaWritesTheNumberInTheBase(short value, short radix, string expected)
    {
        var target = Buffer("", 20);
        var length = CStr.Itoa(value, target, radix);
        Assert.Equal(expected.Length, length);
        Assert.Equal(expected + "0", Show(target.AsSpan(0, length + 1)));
        Assert.Equal((byte)'#', target[length + 1]);
    }

    [Fact]
    public void ItoaAtTheEdges()
    {
        Assert.Throws<MemoryOverrunException>(() => CStr.Itoa(-32768, new byte[6], 10)); // needs 7 bytes
        CStr.Itoa(-32768, new byte[7], 10);
        Assert.Throws<DivideErrorException>(() => CStr.Itoa(1, new byte[8], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CStr.Itoa(1, new byte[8], 1));
    }
}
