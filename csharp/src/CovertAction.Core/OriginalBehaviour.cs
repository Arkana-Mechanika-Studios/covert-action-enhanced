namespace CovertAction.Core;

/// <summary>
/// The original game's bugs that the rewrite can either reproduce or fix, in one place. The register of the bugs
/// themselves, with the evidence, is docs/original-bugs.md (entries CA-1, CA-2, ...); each switch here names its entry.
/// A switch that is off reproduces the original exactly; on, it applies the fix described at the switch.
/// </summary>
/// <remarks>
/// The handling follows the register's three classes:
/// <list type="bullet">
/// <item>A bug that damages the player's data (a saved campaign) has a switch; <see cref="Player"/> turns it on.</item>
/// <item>A plain visual glitch has a switch as well, on in <see cref="Player"/>; only the oracle runs with it off.</item>
/// <item>Any other quirk a player could rely on is reproduced always and has no switch; the code marks it with a
/// <c>// BUG:</c> comment that cites the register entry.</item>
/// </list>
/// A rule that depends on a switch takes the <see cref="OriginalBehaviour"/> as a parameter; there is no global instance.
/// </remarks>
public sealed class OriginalBehaviour
{
    /// <summary>The original, bug for bug. The oracle tests compare the C# with the original program under this.</summary>
    public static OriginalBehaviour Faithful => new();

    /// <summary>
    /// What a player gets by default: the original with the data-damaging bugs and the plain visual glitches fixed.
    /// Saved campaigns stay readable by the original game.
    /// </summary>
    public static OriginalBehaviour Player => new()
    {
        FixRaidGuardCountInCityCounter = true,
        FixChaseGameSpeedWriteBack = true,
        FixChaseGearUpSound = true,
        FixTextReaderPastEndOfFile = true,
        FixReportClockAtMidnight = true,
    };

    /// <summary>
    /// What a player gets with the launcher's option "Fix the original's bugs" (settings key fixes.originalBugs) and, if
    /// chosen, "Also fix campaign-logic bugs" (fixes.campaignBugs): the always-on fixes of <see cref="Player"/>, plus the
    /// safe group of the register (docs/original-bugs.md) when <paramref name="fixOriginalBugs"/> is set. With both off
    /// this is <see cref="Player"/>, so the game then plays as release 1.0.
    /// </summary>
    /// <param name="fixOriginalBugs">The safe group: texts, keys, a crash and a sound driver's slip; none changes the story.</param>
    /// <param name="fixCampaignBugs">The campaign-logic group (events, scores, layouts); no fix is in it yet, the flags will join here.</param>
    public static OriginalBehaviour ForPlayer(bool fixOriginalBugs, bool fixCampaignBugs)
    {
        // DIVERGENCE: D-650 a player's option: the original's bugs, fixed when the player asks for it
        // the campaign-logic flags are added to the object below as they come
        _ = fixCampaignBugs;
        return fixOriginalBugs
            ? new()
            {
                FixRaidGuardCountInCityCounter = true,
                FixChaseGameSpeedWriteBack = true,
                FixChaseGearUpSound = true,
                FixTextReaderPastEndOfFile = true,
                FixReportClockAtMidnight = true,
                FixClueTagTrailingBlank = true,
                FixAnimationSkipKeyLeftInBuffer = true,
                FixCaptureCommentVariant = true,
                FixNameFieldDelete = true,
                FixTandySecondPause = true,
                FixPuzzleTimeOutEvaluation = true,
                FixChaseEntryAtUnknownLocation = true,
            }
            : Player;
    }

    /// <summary>
    /// CA-1. During a building raid the original counts the guards still awake in element 0 of the per-city activity
    /// counters and saves the campaign with it, so the first city's counter comes back from every raid holding a
    /// guard count: the chief's advice and the Activity Report then show wrong activity for that city until the case
    /// ends. On: the raid keeps its guard count in a variable of its own and the city's counter is left alone.
    /// </summary>
    public bool FixRaidGuardCountInCityCounter { get; init; }

    /// <summary>
    /// CA-5 (docs/original-bugs.md): after a car chase the original writes the game speed back into the skills word
    /// but clears only one of the speed's two bits first, so "slow" is lost and "fast" comes back as a speed the menu
    /// does not have. Data-damaging (the word is saved with the campaign). On: both bits are cleared before the
    /// speed is written. Off: the original's write.
    /// </summary>
    public bool FixChaseGameSpeedWriteBack { get; init; }

    /// <summary>
    /// CA-26 (docs/original-bugs.md): in the car chase the gear-up sound never plays for one of the speed classes.
    /// A plain glitch of the sound: fixed whenever a player plays, exact only for the oracle. On: the sound plays.
    /// Off: the original's silence.
    /// </summary>
    public bool FixChaseGearUpSound { get; init; }

    /// <summary>
    /// CA-17 (docs/original-bugs.md): the library's text reader copies the lines of an entry until one starts with '*'
    /// and never looks for the file's end, so an entry at the very end of a file (PLOT.TXT's last one) is appended to
    /// the text buffer again and again, past its end: the game hangs or crashes. On: the reader stops at the file's
    /// end. Off: the original's endless reading.
    /// </summary>
    public bool FixTextReaderPastEndOfFile { get; init; }

    /// <summary>
    /// CA-27 (docs/original-bugs.md): the clock of the case log's headlines (the menus part's format_game_time) shows
    /// the hour after midnight as "00" instead of "12": its 12-hour formula takes the remainder of -1. A plain
    /// glitch of the display: fixed whenever a player plays, exact only for the oracle. On: "12:xx AM". Off: the
    /// original's "00:xx AM".
    /// </summary>
    public bool FixReportClockAtMidnight { get; init; }

    /// <summary>
    /// CA-16: CLUES.TXT's entry for the Bagman of crime 9 is filed under "*C0904 " with a blank at the end, and the
    /// reader matches a tag only against a whole line, so the entry is never found: the clue reads "is information
    /// requires security clearance: *C0904". On: a blank at the end of a tag line does not count. Off: the original's miss.
    /// </summary>
    public bool FixClueTagTrailingBlank { get; init; }

    /// <summary>
    /// CA-34: the animation player means to take the key that skips an animation out of the keyboard buffer but tests
    /// the address of the routine instead of calling it, so the key stays and the next menu takes it. On: the key is
    /// read. Off: it stays in the buffer.
    /// </summary>
    public bool FixAnimationSkipKeyLeftInBuffer { get; init; }

    /// <summary>
    /// CA-41: the chief's comment on a capture adds the rank's variant to the program's own text "*nice0" and never
    /// puts it back, so after the first capture every later one reads the first one's variant. On: the tag is built in
    /// a copy. Off: the original's stuck variant.
    /// </summary>
    public bool FixCaptureCommentVariant { get; init; }

    /// <summary>
    /// CA-43: in a text field (the agent's code name) the Delete key takes out the character under the cursor and also
    /// blanks the one that moves into its place. On: Delete takes out one character. Off: two.
    /// </summary>
    public bool FixNameFieldDelete { get; init; }

    /// <summary>
    /// CA-55: the Tandy driver's pause, repeated without a resume between, saves the already silenced voices over the
    /// saved ones and the music is lost. The game always resumes after a pause, so it cannot be seen in play. On: a
    /// second pause in a row is ignored. Off: the original's.
    /// </summary>
    public bool FixTandySecondPause { get; init; }

    /// <summary>
    /// CA-40: when the wiretap puzzle's time runs out, the clock calls the evaluation without its argument, which then
    /// reads a stack word: the last evaluation draws the board and the terminals only when the tick that ended the time
    /// was not the first of its call. On: it always draws. Off: the original's.
    /// </summary>
    public bool FixPuzzleTimeOutEvaluation { get; init; }

    /// <summary>
    /// CA-4: the car chase's "enter the building" test (F2) measures only the locations the player knows and reuses the
    /// last distance for an unknown one; for location 1 that distance was never set, so the original reads stack
    /// garbage (the rewrite stops with an error, because it cannot tell which garbage). On: an unknown location is not
    /// reached. Off: the original's read.
    /// </summary>
    public bool FixChaseEntryAtUnknownLocation { get; init; }
}
