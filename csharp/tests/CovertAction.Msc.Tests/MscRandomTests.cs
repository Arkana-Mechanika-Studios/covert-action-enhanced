using CovertAction.Msc;

namespace CovertAction.Msc.Tests;

/// <summary>
/// The generator against the library routine's own instructions (CODE 0000:3652: multiply the 32-bit state by
/// 0x000343FD with __aNlmul, add 0x00269EC3, return the high word and 0x7FFF) and against the sequence every
/// Microsoft C library gives after start-up.
/// </summary>
public sealed class MscRandomTests
{
    /// <summary>rand() step by step as the instructions do it, in 16-bit registers.</summary>
    private static (uint State, int Result) LibraryRand(uint state)
    {
        ushort lo = (ushort)state, hi = (ushort)(state >> 16);
        const ushort mulLo = 0x43FD, mulHi = 0x0003;
        // __aNlmul: low * low in DX:AX, plus the two cross products in the high word
        var product = (uint)lo * mulLo;
        var cross = (ushort)(hi * mulLo + lo * mulHi);
        var ax = (ushort)product;
        var dx = (ushort)((product >> 16) + cross);
        // add ax, 9EC3h / adc dx, 26h
        var sum = ax + 0x9EC3u;
        ax = (ushort)sum;
        dx = (ushort)(dx + 0x26 + (sum >> 16));
        // mov ax, dx / and ah, 7Fh
        return ((uint)dx << 16 | ax, dx & 0x7FFF);
    }

    [Fact]
    public void AFreshGeneratorGivesTheKnownMicrosoftSequence()
    {
        // the first values of rand() in a Microsoft C program that never calls srand (the state starts at 1)
        int[] expected = [41, 18467, 6334, 26500, 19169, 15724, 11478, 29358, 26962, 24464];
        var random = new MscRandom();
        Assert.Equal(expected, expected.Select(_ => random.Rand()).ToArray());
    }

    [Fact]
    public void SrandOneRestartsTheSameSequence()
    {
        var random = new MscRandom();
        random.Rand();
        random.Srand(1);
        Assert.Equal(41, random.Rand());
        Assert.Equal(18467, random.Rand());
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(0u)]
    [InlineData(0x7FFFu)]
    [InlineData(0xFFFFu)]
    [InlineData(0xDEADBEEFu)]
    [InlineData(0xFFFFFFFFu)]
    public void EveryStepMatchesTheLibrarysInstructions(uint start)
    {
        var random = new MscRandom(start);
        var state = start;
        for (var i = 0; i < 20000; i++)
        {
            (state, var expected) = LibraryRand(state);
            Assert.Equal(expected, random.Rand());
            Assert.Equal(state, random.State);
        }
    }

    [Fact]
    public void ResultsStayWithin15Bits()
    {
        var random = new MscRandom(12345);
        for (var i = 0; i < 100000; i++)
        {
            Assert.InRange(random.Rand(), 0, 32767);
        }
    }

    [Fact]
    public void SrandStoresTheSeedAsAnUnsigned16BitValueAndClearsTheHighWord()
    {
        var random = new MscRandom(0xDEADBEEF);
        random.Srand(-1);
        Assert.Equal(0x0000FFFFu, random.State);
        random.Srand(0x7ABC);
        Assert.Equal(0x00007ABCu, random.State);
    }

    [Fact]
    public void TheStateCanBeSetAndReadForTheStateCodec()
    {
        var a = new MscRandom(777);
        a.Rand();
        var b = new MscRandom { State = a.State };
        Assert.Equal(a.Rand(), b.Rand());
    }
}
