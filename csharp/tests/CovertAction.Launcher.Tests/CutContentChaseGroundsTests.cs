using CovertAction.Core;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Chase;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The car chase's twenty ground pictures (docs/cut-content.md CC-25; DIVERGENCES.md D-664), a player option: the map
/// generator draws one of the first sixteen of CHASE.PIC's twenty for each cell; with the option one of all twenty, with
/// the same number of random calls.
/// </summary>
public sealed class CutContentChaseGroundsTests
{
    private static (CarChase Chase, ChaseState State) Chase(bool option, int seed = 1234)
    {
        var state = new ChaseState();
        state.Random.Srand(seed);
        var behaviour = OriginalBehaviour.ForPlayer(false, false, new CutContentOptions(ChaseGrounds: option));
        var world = new Boundaries(null!, null!, null!, null!, null!, null!);
        return (new CarChase(state, new SharedState(), world, behaviour), state);
    }

    private static int Picture(ChaseState state, int index) => (state.MapCells[index] >> 4) + (state.HighGround[index] ? 16 : 0);

    [Fact]
    public void TheOptionIsOffByDefault() => Assert.False(CutContentOptions.None.ChaseGrounds);

    [Fact]
    public void WithoutTheOptionEveryGroundIsOneOfTheFirstSixteenAsTheOriginalDrawsIt()
    {
        var (chase, state) = Chase(false);
        chase.FillGround();
        var expected = new MscRandomReplay(1234);
        for (var x = 0; x < ChaseState.MapTiles; x++)
        {
            for (var y = 0; y < ChaseState.MapTiles; y++)
            {
                var index = ChaseState.CellIndex(x, y);
                // the original's expression, call by call
                Assert.Equal((byte)((GameLibrary.RandomBelow(expected.Random, 16) << 4) + 0xf), state.MapCells[index]);
                Assert.False(state.HighGround[index]);
            }
        }
    }

    [Fact]
    public void WithTheOptionAllTwentyAppearAndTheNumberOfRandomCallsIsTheSame()
    {
        var (off, offState) = Chase(false);
        var (on, onState) = Chase(true);
        off.FillGround();
        on.FillGround();
        // the same calls: the generator is in the same state afterwards
        Assert.Equal(offState.Random.State, onState.Random.State);
        var seen = new SortedSet<int>();
        for (var i = 0; i < ChaseState.MapTiles * ChaseState.MapTiles; i++)
        {
            seen.Add(Picture(onState, i));
            // every cell has its sides open
            Assert.Equal(0xf, onState.MapCells[i] & 0xf);
        }
        Assert.Equal(Enumerable.Range(0, 20), seen);
        Assert.All(offState.HighGround, high => Assert.False(high));
    }

    [Fact]
    public void TheLargestGroundIsBelowSixteenWithoutTheOptionAndAboveItWith()
    {
        var (off, offState) = Chase(false, 99);
        var (on, onState) = Chase(true, 99);
        off.FillGround();
        on.FillGround();
        // the draw of 20 instead of 16 gives other numbers (n * rand >> 15), so only the ranges are compared
        Assert.InRange(Enumerable.Range(0, offState.MapCells.Length).Max(i => Picture(offState, i)), 0, 15);
        Assert.InRange(Enumerable.Range(0, onState.MapCells.Length).Max(i => Picture(onState, i)), 16, 19);
    }

    /// <summary>A second generator with the seed of the first, to repeat its draws.</summary>
    private sealed class MscRandomReplay
    {
        public MscRandomReplay(int seed)
        {
            Random = new CovertAction.Msc.MscRandom();
            Random.Srand(seed);
        }

        public CovertAction.Msc.MscRandom Random { get; }
    }
}
