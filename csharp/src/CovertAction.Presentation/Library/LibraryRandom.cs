using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Presentation.Library;

/// <summary>The library's routine that starts a part's random numbers; every part with chance in it has a copy.</summary>
public static class LibraryRandom
{
    /// <summary>
    /// Starts the random numbers from the clock, so that every session runs differently: the low 15 bits of the
    /// current time in seconds become the seed.
    /// </summary>
    /// <param name="random">The part's random numbers.</param>
    /// <param name="timer">The clock.</param>
    /// <returns>The seed, which the part keeps among its variables as the original does.</returns>
    // orig: CODE 0000:2a72 seed_random (src/c/code/code_0000_2a72.c) matched; also GAME 00f7:d4d4, TAC 00e8:d1ec, BUG 00e8:6fb8, CHASE 00df:42c0; tests: CodeOracleTests, HarnessTests
    public static short SeedRandom(IRandomSource random, IGameTimer timer)
    {
        var seed = (short)(timer.Time() & 0x7fff);
        random.Srand(seed);
        return seed;
    }
}
