using CovertAction.Core.Records;
using CovertAction.Msc;

namespace CovertAction.Core.Shared;

/// <summary>
/// The small helpers MicroProse compiled into every program of the game: each part carries its own copy of the same
/// few lines. The rewrite has each of them once; the comment on a method lists the copies it stands for.
/// </summary>
public static class GameLibrary
{
    /// <summary>
    /// The game's dice: a random whole number from 0 to <paramref name="n"/> - 1, each about equally likely. Exactly
    /// one draw from the generator, scaled: <c>n * rand() / 32768</c>, rounded down.
    /// </summary>
    /// <param name="random">The generator to draw from.</param>
    /// <param name="n">The number of outcomes, within -32768..32767. 0 gives 0 (the draw still happens); a negative n gives n..0.</param>
    // orig: CODE 0000:2a89 random_below (src/c/code/code_0000_2a89.c) matched; also GAME 00f7:d4eb, TAC 00e8:d203, BUG 00e8:6fcf, CHASE 00df:42d7, FINAL 0100:c169; tests: GameLibraryTests, CodeOracleTests
    // C: (int)((long)n * rand() >> 15). The product of two 16-bit values fits C#'s int, and its shift rounds down as
    // the original's long shift does. (The CHASE copy returns the long itself and is not this method.)
    public static int RandomBelow(IRandomSource random, int n) => n * random.Rand() >> 15;

    /// <summary>
    /// Limits <paramref name="value"/> to the range <paramref name="lo"/>..<paramref name="hi"/>. The lower limit is
    /// applied first, then the upper: with crossed limits (lo above hi) the result is <paramref name="hi"/>.
    /// </summary>
    /// <param name="value">The value to limit.</param>
    /// <param name="lo">The smallest result, unless <paramref name="hi"/> is smaller still.</param>
    /// <param name="hi">The largest result.</param>
    // orig: CODE 0000:2aa2 clamp (src/c/code/code_0000_2aa2.c) matched; also FINAL 0100:c182, GAME 00f7:d504, TAC 00e8:d21c, BUG 00e8:6fe8, CHASE 00df:42f0; tests: GameLibraryTests, CodeOracleTests
    public static int Clamp(int value, int lo, int hi)
    {
        if (value < lo)
        {
            value = lo;
        }
        if (value > hi)
        {
            value = hi;
        }
        return value;
    }

    /// <summary>The sign of a number: 1, -1 or 0.</summary>
    /// <param name="value">The number.</param>
    // orig: GAME 00f7:399e sign (src/c/game/game_00f7_399e.c) matched; also TAC 00e8:06d9, CHASE 00df:2bb3; tests: GameLibraryTests
    public static int Sign(int value) => Math.Sign((short)value);

    /// <summary>
    /// How far two cells of the map grid are apart. A cell is one number, its column in the low four bits and its
    /// row in the next four (a city's and an organisation's <c>Climate</c>). The distance is twice the larger of the
    /// two differences plus the smaller: a diagonal step counts as little more than a straight one.
    /// </summary>
    /// <param name="keyA">One cell.</param>
    /// <param name="keyB">The other cell.</param>
    // orig: FINAL 0100:8327 key_distance (src/c/final/final_0100_8327.c) matched; also BUG 00e8:495d, TAC 00e8:b1e1; tests: GameLibraryTests, LibraryOracleTests, TacOracleTests
    public static int KeyDistance(int keyA, int keyB)
    {
        var columns = Math.Abs((keyA & 0x0f) - (keyB & 0x0f));
        var rows = Math.Abs((keyA >> 4 & 0x0f) - (keyB >> 4 & 0x0f));
        return 2 * Math.Max(columns, rows) + Math.Min(columns, rows);
    }

    /// <summary>
    /// How far two cities are apart on the map picture, by the same rule as <see cref="KeyDistance"/> on their pixel
    /// positions: twice the larger difference plus the smaller.
    /// </summary>
    /// <param name="cities">The world's cities.</param>
    /// <param name="cityA">One city's number.</param>
    /// <param name="cityB">The other city's number.</param>
    // orig: FINAL 0100:8383 city_distance (src/c/final/final_0100_8383.c) matched; also GAME 00f7:070f, TAC 00e8:b23d, BUG 00e8:49b9; tests: GameLibraryTests, LibraryOracleTests
    public static int CityDistance(IReadOnlyList<City> cities, int cityA, int cityB)
    {
        var across = Math.Abs(cities[cityA].GeoX - cities[cityB].GeoX);
        var down = Math.Abs(cities[cityA].GeoY - cities[cityB].GeoY);
        return 2 * Math.Max(across, down) + Math.Min(across, down);
    }
}
