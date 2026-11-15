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
}
