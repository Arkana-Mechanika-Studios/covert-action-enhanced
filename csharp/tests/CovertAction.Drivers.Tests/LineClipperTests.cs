using CovertAction.Drivers;

namespace CovertAction.Drivers.Tests;

/// <summary>
/// The line clipper against results worked out by hand from the original's instructions (CODE 0000:07ac): which end
/// is moved, onto which edge, and how the new end is rounded. The driver oracle proves the lines the game draws
/// after clipping; these tests hold the clipping itself.
/// </summary>
public sealed class LineClipperTests
{
    private static (int, int, int, int)? Clip(int x1, int y1, int x2, int y2, int right = 10, int bottom = 10) =>
        LineClipper.Clip((short)x1, (short)y1, (short)x2, (short)y2, (short)right, (short)bottom) is { } line
            ? (line.X1, line.Y1, line.X2, line.Y2)
            : null;

    [Fact]
    public void ALineInsideTheWindowStaysAsItIs() => Assert.Equal((3, 4, 10, 0), Clip(3, 4, 10, 0));

    [Theory]
    [InlineData(-5, 2, -1, 8)]      // both ends left of the window
    [InlineData(11, 0, 30, 10)]     // right of it
    [InlineData(0, -3, 10, -1)]     // above
    [InlineData(2, 11, 8, 50)]      // below
    public void ALineBeyondOneEdgeIsNotDrawn(int x1, int y1, int x2, int y2) => Assert.Null(Clip(x1, y1, x2, y2));

    [Fact]
    public void AnEndBeyondTheRightEdgeMovesOntoIt() => Assert.Equal((0, 0, 10, 5), Clip(0, 0, 20, 10));

    [Fact]
    public void AnEndBelowTheWindowMovesOntoTheBottomEdge() => Assert.Equal((0, 0, 2, 10), Clip(0, 0, 4, 20));

    [Fact]
    public void ALineThatLeavesThroughTheBottomBeforeTheRightEdgeEndsOnTheBottomEdge() =>
        // at the right edge the line would be in row 20; so the bottom edge decides: column 5
        Assert.Equal((0, 0, 5, 10), Clip(0, 0, 20, 40));

    [Theory]
    [InlineData(10, 3)]     // 10 * 10 / 30 = 3.33
    [InlineData(20, 7)]     // 10 * 20 / 30 = 6.67
    [InlineData(15, 5)]     // exactly 5
    [InlineData(3, 1)]      // 10 * 3 / 30 = 1
    public void TheNewEndIsRoundedToTheNearestRow(int y2, int row) => Assert.Equal((0, 0, 10, row), Clip(0, 0, 30, y2, bottom: 100));

    [Fact]
    public void TheOriginalsRoundingTakesAThirdForAHalfWhenTheDivisorIsOdd() =>
        // 4 / 3 = 1.33, but the remainder 1 is not below half the divisor as the original halves it (3 / 2 = 1): rounded up
        Assert.Equal((0, 0, 1, 2), Clip(0, 0, 3, 4, right: 1));

    [Fact]
    public void WhenOnlyTheFirstEndIsOutsideTheInsideEndComesFirst() => Assert.Equal((5, 5, 0, 5), Clip(-10, 5, 5, 5));

    [Fact]
    public void BothEndsOutsideOnOppositeSidesAreBothMoved() => Assert.Equal((10, 5, 0, 5), Clip(-10, 5, 20, 5));

    [Fact]
    public void ALineThatPassesTheWindowsCornerOutsideIsNotDrawn() =>
        // from the left of the window to above it, cutting the corner outside
        Assert.Null(Clip(-10, 5, 5, -10));

    [Fact]
    public void ADistanceBeyondSixteenBitsIsHalvedWithTheOther()
    {
        // 40000 rows down do not fit a 16-bit number: both distances are halved (20 across, 20000 down), the direction
        // stays. Both ends are outside: the second moves onto the right edge, the first onto the top edge.
        Assert.Equal((30, 10000, 20, 0), Clip(0, -20000, 40, 20000, right: 30, bottom: 30000));
    }

    [Fact]
    public void ADivisionTheProcessorRefusesGivesTheHandlersLargeQuotient()
    {
        // one column across, halved to none: the division by 0 is answered as the original's handler answers it,
        // which puts the crossing far outside the window; the line is not drawn, and nothing throws
        Assert.Null(Clip(0, -20000, 1, 20000, right: 0, bottom: 100));
    }
}
