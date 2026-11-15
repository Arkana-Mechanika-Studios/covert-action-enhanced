using CovertAction.Core.Final;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Tests.Final;

/// <summary>
/// The rules of the end of a case, on hand-made states; the expected values are worked out from the C of
/// FINAL (reveal_case_state, clear_fame_table, format_game_time, clue_phrase_index), not from the C#.
/// </summary>
public sealed class CaseReviewTests
{
    [Fact]
    public void EverythingInUseIsRevealed()
    {
        var state = new FinalState { Nperson = 2, NACTIVITY = 2, NextSyn = 1, NEVENT = 1 };
        state.Ps[0].Face = 5;
        state.Ac[0].Who = 1;
        state.Ac[1].Who = 1;
        state.Sn[0].Clues = 0x30;
        CaseReview.RevealCaseState(state);
        Assert.Equal(-1, state.Ps[0].Clues);
        // person 1 has no face: its knowledge bits stay, but it records both members
        Assert.Equal(0, state.Ps[1].Clues);
        Assert.Equal(3, state.Ps[1].Rknown);
        Assert.Equal(unchecked((short)0x8000), state.Ac[1].Status);
        Assert.Equal(0x3f, state.Sn[0].Clues);
        Assert.Equal(unchecked((short)0x8000), state.Ev[0].Status);
        Assert.Equal(0, state.Ev[1].Status);
    }

    [Fact]
    public void TheHallOfFameIsEmptied()
    {
        var state = new FinalState();
        state.FameTable[2].Score = 300;
        CaseReview.ClearFameTable(state);
        Assert.All(state.FameTable, e => Assert.Equal(-1, e.Score));
    }

    private static FinalState Clock(short careerMonths)
    {
        var state = new FinalState { ClockDayText = " 10" };
        "00:00 AM Jun 00\0".CopyTo(0, state.ClockText, 0, 16);
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        months.CopyTo(state.MonthNames, 0);
        state.Pl.Ncase = careerMonths;
        return state;
    }

    [Fact]
    public void ATimeIsWrittenOnATwelveHourClock()
    {
        // 2 days, 13 hours and 5 minutes into the case, in the 14th month of the career
        var state = Clock(13);
        Assert.Equal("01:05 PM Feb 03", CaseReview.FormatGameTime(state, (2 * 24 + 13) * 60 + 5, OriginalBehaviour.Faithful));
        Assert.Equal((5, 13, 3), (state.GameMinute, state.GameHour, state.GameDay));
    }

    [Fact]
    public void MidnightIsHourZeroNotTwelve()
    {
        var state = Clock(0);
        // (0 - 1) % 12 + 1 is 0 in C, whose remainder keeps the sign: the original shows midnight as 00, noon as 12
        Assert.Equal("00:00 AM Jan 01", CaseReview.FormatGameTime(state, 0, OriginalBehaviour.Faithful));
        Assert.Equal("12:00 PM Jan 01", CaseReview.FormatGameTime(state, 12 * 60, OriginalBehaviour.Faithful));
    }

    [Fact]
    public void WithTheClockFixedTheHourAfterMidnightIsTwelve()
    {
        var state = Clock(0);
        // CA-27 fixed, as a player gets it: only the hour after midnight changes
        Assert.Equal("12:00 AM Jan 01", CaseReview.FormatGameTime(state, 0, OriginalBehaviour.Player));
        Assert.Equal("12:59 AM Jan 02", CaseReview.FormatGameTime(state, 24 * 60 + 59, OriginalBehaviour.Player));
        Assert.Equal("01:00 AM Jan 01", CaseReview.FormatGameTime(state, 60, OriginalBehaviour.Player));
        Assert.Equal("12:00 PM Jan 01", CaseReview.FormatGameTime(state, 12 * 60, OriginalBehaviour.Player));
        Assert.Equal("01:05 PM Jan 03", CaseReview.FormatGameTime(state, (2 * 24 + 13) * 60 + 5, OriginalBehaviour.Player));
    }

    [Fact]
    public void ANegativeTimeGivesTheOriginalsOddCharacters()
    {
        // -4096 minutes: minute -16, hour -20, twelve-hour -8, day -1; digits '0' + (-1), '0' + (-6) ...
        var state = Clock(0);
        Assert.Equal("0(:/* AM Jan 0/", CaseReview.FormatGameTime(state, -4096, OriginalBehaviour.Faithful));
    }

    [Theory]
    [InlineData(0x0100, 0x0000, 0)]
    [InlineData(0x0200, 0x0000, 1)]
    [InlineData(0x0800, 0x0000, 3)]
    [InlineData(0x1000, 0x0300, 19)]
    [InlineData(-1, 0x0100, 5)]
    public void ThePhraseDependsOnTheKindsOfBothParts(int targetCode, int textId, int expected)
    {
        Assert.Equal(expected, CampaignLibrary.ClueKindIndex(targetCode, textId));
    }
}
