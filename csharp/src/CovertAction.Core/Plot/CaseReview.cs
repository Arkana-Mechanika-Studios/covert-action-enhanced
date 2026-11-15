using CovertAction.Msc;

namespace CovertAction.Core.Final;

/// <summary>
/// The rules of the end of a case that need no screen: everything is revealed for the review, the Hall of Fame is
/// emptied, and the dates and clue sentences of the reports are put together.
/// </summary>
public static class CaseReview
{
    /// <summary>A cast member's and an event's mark "revealed at the end of the case": the top bit of its status word.</summary>
    private const short Revealed = unchecked((short)0x8000);

    /// <summary>A message's four known-detail bits: the date, and the name, organisation and city of the other party.</summary>
    private const short AllMessageDetails = 0xf;

    /// <summary>A person's knowledge bits, all set.</summary>
    private const short EverythingKnown = -1;

    /// <summary>A Hall of Fame entry's score when the place is empty.</summary>
    private const short EmptyPlace = -1;

    /// <summary>
    /// Reveals the whole case for the review at its end: the player is taken to know everything about every person in
    /// use, every member of the cast is marked revealed and recorded as seen in its person, every message has all its
    /// details known, and every event is marked revealed.
    /// </summary>
    /// <param name="state">The case.</param>
    // orig: FINAL 0100:2d63 reveal_case_state (src/c/final/final_0100_2d63.c) matched; tests: CaseReviewTests, FinalOracleTests
    public static void RevealCaseState(FinalState state)
    {
        for (var i = 0; i < state.Nperson; i++)
        {
            if (state.Ps[i].Face != 0)
            {
                state.Ps[i].Clues = EverythingKnown;
            }
        }
        for (var i = 0; i < state.NACTIVITY; i++)
        {
            var member = state.Ac[i];
            member.Status |= Revealed;
            // C: a shift by a variable count (a member is 0..31: from member 16 on the bit is 0, as on the 8086)
            state.Ps[member.Who].Rknown |= C16.Shl(1, i);
        }
        for (var i = 0; i < state.NextSyn; i++)
        {
            state.Sn[i].Clues |= AllMessageDetails;
        }
        for (var i = 0; i < state.NEVENT; i++)
        {
            state.Ev[i].Status |= Revealed;
        }
    }

    /// <summary>Empties the Hall of Fame: every place gets the score of an empty place.</summary>
    /// <param name="state">The Hall of Fame.</param>
    // orig: FINAL 0100:4caa clear_fame_table (src/c/final/final_0100_4caa.c) matched; tests: CaseReviewTests, FinalOracleTests
    public static void ClearFameTable(FinalState state)
    {
        foreach (var entry in state.FameTable)
        {
            entry.Score = EmptyPlace;
        }
    }

    /// <summary>
    /// Writes a moment of the case as a date and time, "hh:mm AM Mon dd", into <see cref="FinalState.ClockText"/>:
    /// the hour on a 12-hour clock, the month of the agent's career, and the day counted from 1. Also keeps the minute,
    /// hour and day in their variables.
    /// </summary>
    /// <remarks>
    /// The clock text is a template: the colon and the blanks are its own; the digits, the A or P, the month's name
    /// and what follows it (<see cref="FinalState.ClockDayText"/>, whose digits are then overwritten) are written into it.
    /// </remarks>
    /// <param name="state">The clock text, the month names and the career's month.</param>
    /// <param name="minutes">Minutes since the case began.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce (the hour after midnight).</param>
    /// <returns>The text, up to its end mark.</returns>
    // orig: FINAL 0100:2c94 format_game_time (src/c/final/final_0100_2c94.c) matched; tests: CaseReviewTests, FinalOracleTests
    // FINAL's own variant (docs/source-map/shared-functions.csv: the other parts' are other code).
    public static string FormatGameTime(FinalState state, int minutes, OriginalBehaviour behaviour)
    {
        const int hourTens = 0, hourUnits = 1, minuteTens = 3, minuteUnits = 4, amPm = 6, monthAt = 9, dayTens = 13, dayUnits = 14;
        var text = state.ClockText;
        // C: 16-bit divisions and remainders (they round towards 0, as C#'s do, also for a negative time)
        var time = C16.Int(minutes);
        state.GameMinute = C16.Rem(time, 60);
        text[minuteTens] = Digit(state.GameMinute / 10);
        text[minuteUnits] = Digit(state.GameMinute % 10);
        var hours = time / 60;
        state.GameHour = (short)(hours % 24);
        // BUG: CA-27 C's remainder keeps the sign: hour 0 gives 0, so the hour after midnight shows as 00 (not 12) AM
        // (OriginalBehaviour.FixReportClockAtMidnight)
        const int midnight = 0, twelve = 12;
        var hourOfTwelve = behaviour.FixReportClockAtMidnight && state.GameHour == midnight ? twelve : (state.GameHour - 1) % 12 + 1;
        text[hourTens] = Digit(hourOfTwelve / 10);
        text[hourUnits] = Digit(hourOfTwelve % 10);
        text[amPm] = state.GameHour > 11 ? 'P' : 'A';
        var month = state.MonthNames[state.Pl.Ncase % FinalState.MonthCount];
        CharTable.Put(text, monthAt, month + state.ClockDayText);
        state.GameDay = (short)(hours / 24 + 1);
        text[dayTens] = Digit(state.GameDay / 10);
        text[dayUnits] = Digit(state.GameDay % 10);
        return CharTable.TextOf(text);
    }

    /// <summary>A digit as a character; the original adds '0' and keeps the low byte, so a negative number gives a sign-like character.</summary>
    private static char Digit(int value) => (char)C16.UChar(value + '0');

}
