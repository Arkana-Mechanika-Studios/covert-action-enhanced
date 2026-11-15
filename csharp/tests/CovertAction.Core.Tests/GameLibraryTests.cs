using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Tests;

/// <summary>The helpers every module carries, against the reconstructed C (random_below, clamp) worked out by hand.</summary>
public sealed class GameLibraryTests
{
    /// <summary>A generator that returns a fixed list of values, so a test decides the dice.</summary>
    private sealed class Scripted(params int[] values) : IRandomSource
    {
        private int _next;

        public int Draws => _next;

        public int Rand() => values[_next++];

        public void Srand(int seed) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(26, 0, 0)]
    [InlineData(26, 32767, 25)]      // the largest draw never reaches n
    [InlineData(26, 1260, 0)]        // 26 * 1260 = 32760, just below 32768
    [InlineData(26, 1261, 1)]
    [InlineData(1, 32767, 0)]
    [InlineData(0, 32767, 0)]
    [InlineData(32767, 32767, 32766)]
    [InlineData(200, 16384, 100)]
    [InlineData(-1, 1, -1)]          // a negative n: the long shift rounds down
    [InlineData(-4, 32767, -4)]
    [InlineData(-4, 0, 0)]
    public void RandomBelowScalesOneDraw(int n, int draw, int expected)
    {
        var random = new Scripted(draw);
        Assert.Equal(expected, GameLibrary.RandomBelow(random, n));
        Assert.Equal(1, random.Draws);
    }

    [Fact]
    public void RandomBelowWithTheLibraryGeneratorCoversTheWholeRangeAndNothingElse()
    {
        var random = new MscRandom();
        var seen = new bool[26];
        for (var i = 0; i < 5000; i++)
        {
            seen[GameLibrary.RandomBelow(random, 26)] = true;
        }
        Assert.All(seen, Assert.True);
    }

    [Theory]
    [InlineData(5, 0, 10, 5)]
    [InlineData(-5, 0, 10, 0)]
    [InlineData(15, 0, 10, 10)]
    [InlineData(0, 0, 10, 0)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(4, -3, 5, 4)]
    [InlineData(-9, -3, 5, -3)]
    [InlineData(5, 10, 0, 0)]        // crossed limits: the upper limit is applied last and wins
    [InlineData(20, 10, 0, 0)]
    [InlineData(-32768, -32768, 32767, -32768)]
    public void ClampAppliesTheLowerLimitThenTheUpper(int value, int lo, int hi, int expected) =>
        Assert.Equal(expected, GameLibrary.Clamp(value, lo, hi));

    [Theory]
    [InlineData(5, 1)]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(32767, 1)]
    [InlineData(-32768, -1)]
    public void SignIsOneMinusOneOrZero(int value, int expected) => Assert.Equal(expected, GameLibrary.Sign(value));

    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x12, 0x15, 6)]     // three columns apart in one row
    [InlineData(0x12, 0x42, 6)]     // three rows apart in one column
    [InlineData(0x12, 0x35, 8)]     // three columns and two rows: 2 * 3 + 2
    [InlineData(0xf0, 0x0f, 45)]    // corner to corner: 2 * 15 + 15
    public void TheDistanceOfTwoMapCellsIsTwiceTheLargerDifferencePlusTheSmaller(int a, int b, int expected)
    {
        Assert.Equal(expected, GameLibrary.KeyDistance(a, b));
        Assert.Equal(expected, GameLibrary.KeyDistance(b, a));
    }

    [Fact]
    public void TheDistanceOfTwoCitiesIsMeasuredOnTheMapPicture()
    {
        CovertAction.Core.Records.City[] cities = [new() { GeoX = 10, GeoY = 200 }, new() { GeoX = 250, GeoY = 190 }, new() { GeoX = 10, GeoY = 20 }];
        Assert.Equal(2 * 240 + 10, GameLibrary.CityDistance(cities, 0, 1));
        Assert.Equal(2 * 180 + 0, GameLibrary.CityDistance(cities, 2, 0));
        Assert.Equal(0, GameLibrary.CityDistance(cities, 1, 1));
    }
}
