using CovertAction.Core.Raid;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Tests.Raid;

/// <summary>The raid's clock, with the values worked out from the original's C (TAC 00e8:b969).</summary>
public sealed class RaidRulesTests
{
    /// <summary>The library's format_clock_time on the raid's clock.</summary>
    private static string TimeString(RaidState state, int seconds) => LibraryTexts.FormatClockTime(state.ClockText, state.Gtime, seconds);

    private static RaidState NewState(int gtime)
    {
        var state = new RaidState { Gtime = (short)gtime };
        "00:00:00".CopyTo(state.ClockText);
        return state;
    }

    [Theory]
    [InlineData(2340, 1, "15:00:01")]        // minute 2340 of the case is 15:00 of the second day
    [InlineData(1439, 61, "00:00:01")]       // round midnight
    [InlineData(600, 3599, "10:59:59")]
    public void TheClockIsTheCampaignsTimePlusTheRaidsSeconds(int gtime, int seconds, string expected) =>
        Assert.Equal(expected, TimeString(NewState(gtime), seconds));

    [Fact]
    public void ANegativeMinuteWrapsAsAnUnsignedNumber()
    {
        // (unsigned)-1 % 1440 = 65535 % 1440 = 735: 12:15
        Assert.Equal("12:15:00", TimeString(NewState(-1), 0));
    }
}
