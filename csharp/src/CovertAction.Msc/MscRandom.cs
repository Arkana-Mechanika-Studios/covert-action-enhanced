namespace CovertAction.Msc;

/// <summary>
/// The game's source of chance. Every rule that rolls dice takes one of these instead of a global generator, so a test
/// (or a replay) decides the numbers; in play it is <see cref="MscRandom"/>, the generator the original was linked with.
/// </summary>
public interface IRandomSource
{
    /// <summary>The library's <c>rand()</c>: the next number, 0..32767.</summary>
    int Rand();

    /// <summary>
    /// The library's <c>srand(seed)</c>: restarts the sequence. Only the low 16 bits of the seed count. The game seeds
    /// with the clock's seconds, <c>time(0) &amp; 0x7fff</c>.
    /// </summary>
    void Srand(int seed);
}

/// <summary>
/// The random number generator of the Microsoft C 5.1 run-time library, which every module of the game is linked with:
/// a 32-bit state (the library's <c>holdrand</c>) that <c>rand()</c> advances by <c>state * 214013 + 2531011</c> and of
/// which it returns bits 16..30. The order and number of draws is part of the game's behaviour: a port must draw
/// exactly when the original does.
/// </summary>
/// <remarks>
/// Read from the library's instructions in the game itself (the library source rand.c is not among the MSC 5.1 files
/// under dos/msc510): the constants 0x000343FD and 0x00269EC3, the multiplication by <c>__aNlmul</c>, and
/// <c>mov ax, dx / and ah, 7Fh</c> for the result. The state is 1 when a program starts, as in every Microsoft C library.
/// </remarks>
// orig: CODE DS:1982 holdrand (the long the two routines below share; every module has its own copy)
public sealed class MscRandom(uint state = 1) : IRandomSource
{
    private const uint Multiplier = 0x000343FD; // 214013
    private const uint Increment = 0x00269EC3;  // 2531011

    /// <summary>
    /// The library's 32-bit state. Readable and writable because it lives in each module's data segment: the state codec
    /// loads it from there and writes it back, which is how a function-level oracle test checks the number of draws.
    /// </summary>
    public uint State { get; set; } = state;

    /// <inheritdoc/>
    // orig: CODE 0000:3652 rand (SLIBCR.LIB rand.c) lib; also FINAL 0100:e8da, GAME 00f7:fc6c, TAC 00e8:fd48, BUG 00e8:95ee, CHASE 00df:574a; tests: MscRandomTests
    public int Rand()
    {
        State = unchecked(State * Multiplier + Increment);
        return (int)(State >> 16 & 0x7FFF);
    }

    /// <inheritdoc/>
    // orig: CODE 0000:3640 srand (SLIBCR.LIB rand.c) lib; also FINAL 0100:e8c8, GAME 00f7:fc5a, TAC 00e8:fd36, BUG 00e8:95dc, CHASE 00df:5738; tests: MscRandomTests
    // C: the seed is an unsigned 16-bit value; the high word of the state is set to 0, so srand(-1) gives the state 0x0000FFFF.
    public void Srand(int seed) => State = unchecked((ushort)seed);
}
