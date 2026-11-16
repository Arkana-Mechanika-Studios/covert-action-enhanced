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
    /// <param name="fixCampaignBugs">The campaign-logic group (events, scores, layouts, the career record); independent of the first option.</param>
    public static OriginalBehaviour ForPlayer(bool fixOriginalBugs, bool fixCampaignBugs)
    {
        // DIVERGENCE: D-650 a player's option: the original's bugs, fixed when the player asks for it
        var safe = fixOriginalBugs;
        var campaign = fixCampaignBugs;
        return new()
        {
            FixRaidGuardCountInCityCounter = true,
            FixChaseGameSpeedWriteBack = true,
            FixChaseGearUpSound = true,
            FixTextReaderPastEndOfFile = true,
            FixReportClockAtMidnight = true,
            FixClueTagTrailingBlank = safe,
            FixAnimationSkipKeyLeftInBuffer = safe,
            FixCaptureCommentVariant = safe,
            FixNameFieldDelete = safe,
            FixTandySecondPause = safe,
            FixPuzzleTimeOutEvaluation = safe,
            FixChaseEntryAtUnknownLocation = safe,
            FixWideFurnitureOverWall = safe,
            FixFileRoomShortKey = safe,
            FixUnsolvableCircuitBoard = safe,
            FixRaidKeyRepeat = safe,
            FixMissingFameFile = safe,
            FixAnnouncedEventCancelledBySharedMessageId = campaign,
            FixReceiveEventNamingItsOwnActor = campaign,
            FixCrimeEndingBelowFivePoints = campaign,
            FixBigBuildingDoorBits = campaign,
            FixLoneMessagePairing = campaign,
            FixDepartureLookup = campaign,
            FixNewPersonFaceAndState = campaign,
            FixTrainingRatingLowered = campaign,
            FixApartmentSceneStalePlot = campaign,
            FixCareerRecordEndSearch = campaign,
            FixDecodeVisitLeftoverCharge = campaign,
            FixCaseLayOutDrift = campaign,
            FixCrime10MeetingPartner = campaign,
            FixSkillsKeptFromPractice = campaign,
        };
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

    /// <summary>
    /// CA-12: when a pair of events (a message sent and received, a meeting) is scheduled, the original cancels every
    /// other event with the pair's message id, including an event of a single member that has no partner and only
    /// shares the number: CRIME1's "Stole Blueprints" (id 2, the MasterMind's message to the Organizer's) never
    /// happens, and its bulletin is never shown. On: only events that themselves have a partner are cancelled, and
    /// the bulletin of such an event reads its own text of the file where the number is used twice. Off: the original's.
    /// </summary>
    public bool FixAnnouncedEventCancelledBySharedMessageId { get; init; }

    /// <summary>
    /// CA-13: two receiving events of the data (CRIME2 "Financed operation", CRIME8 "Driver") name their own actor as
    /// the sender, so when one is picked before the real sender it is paired with itself and both events are cancelled.
    /// On: such an event answers the event of the same message id that sends to its actor. Off: the original's.
    /// </summary>
    public bool FixReceiveEventNamingItsOwnActor { get; init; }

    /// <summary>
    /// CA-15: the case counts as "crime carried out" only when an event worth 5 or more points happened, and none of
    /// CRIME12's is (its best is "Made Product", 4), so such a case always ends as "crime prevented" and its variant is
    /// never marked played. On: a crime whose best event is worth less than 5 is carried out when that event happened.
    /// Off: the original's.
    /// </summary>
    public bool FixCrimeEndingBelowFivePoints { get; init; }

    /// <summary>
    /// CA-28: a room's bit in the doors mask of a building is built in 16 bits, so rooms 16 to 23 have none (they never
    /// count as joined and get extra doors) and room 15's is the whole upper half. On: the bit is 1 shifted by the
    /// room's number in 32 bits. Off: the original's.
    /// </summary>
    public bool FixBigBuildingDoorBits { get; init; }

    /// <summary>
    /// CA-32: a message of the log whose pair is not in the log at the same minute is paired with "message -1", which is
    /// the end of city 15's record, and the two messages of a pair are mixed up with a second pair of the same minute.
    /// On: the pair is the neighbouring message of the same minute that names the same two people the other way round,
    /// and a message with none has no pair. Off: the original's.
    /// </summary>
    public bool FixLoneMessagePairing { get; init; }

    /// <summary>
    /// CA-35: the plot step's "seen leaving a city" looks the destination's person up with no knowledge to add, which
    /// finds no one, and tests "person -1", so no cast member is ever seen leaving. On: the destination's person is
    /// looked up without the knowledge filter and a free place or a person unknown to the player qualifies. Off: the
    /// original's.
    /// </summary>
    public bool FixDepartureLookup { get; init; }

    /// <summary>
    /// CA-36: the face-uniqueness test of a new person (GAME's find_or_create_person) is garbled and almost never holds,
    /// and the new person keeps the alert and investigation score of the free record's previous occupant. On: the new
    /// face differs in its low six bits from every other person's, and alert and investigation start at 0. Off: the
    /// original's.
    /// </summary>
    public bool FixNewPersonFaceAndState { get; init; }

    /// <summary>
    /// CA-42: after the agent's training the original lowers the rating of one skill (the one behind the last raised) from
    /// 2 to 1 after the ratings were shown, so the skill played is not the one shown. On: the shown ratings are kept.
    /// Off: the original's.
    /// </summary>
    public bool FixTrainingRatingLowered { get; init; }

    /// <summary>
    /// CA-44: when no variant of the case's plot fits, the apartment scene chooses a new plot but goes on looking for
    /// variants in the old one. On: it looks in the new plot. Off: the original's.
    /// </summary>
    public bool FixApartmentSceneStalePlot { get; init; }

    /// <summary>
    /// CA-46: the career record's free place is found as the first byte of 0xff, though a case's efficiency rating is
    /// stored in two bytes, so a rating with a low byte of 255 is taken for the end of the record and the next case is
    /// written over it. On: the free place is found record by record. The file's format does not change. Off: the
    /// original's.
    /// </summary>
    public bool FixCareerRecordEndSearch { get; init; }

    /// <summary>
    /// CA-7: coming back from the cryptography workstation, the CIA building charges half an hour per unit of the
    /// hand-off's second result, which the workstation never writes: the time comes from whatever the last building
    /// raid or car chase left there (up to 50 hours after a chase). The workstation's own minutes are already charged
    /// on entry, and the manual speaks only of the time spent at the workstation (p. 72). On: no further charge. Off:
    /// the original's.
    /// </summary>
    public bool FixDecodeVisitLeftoverCharge { get; init; }

    /// <summary>
    /// CA-59: the other half of a wide piece of furniture (a desk, a couch) is written beside the piece without looking
    /// at what is there, so it can replace a wall cell of the room. On: such a piece is left out when its other half
    /// would not land on a free cell. Off: the original's.
    /// </summary>
    public bool FixWideFurnitureOverWall { get; init; }

    /// <summary>
    /// CA-61: the file room's search tests only the organisation's display name, and "Stasi" is spelled "Stassi" there,
    /// so the correct spelling finds nothing. On: the organisation's short key ("Stasi") is searched as well. Off: the
    /// original's.
    /// </summary>
    public bool FixFileRoomShortKey { get; init; }

    /// <summary>
    /// CA-62: the electronics board generator never checks that a board can be won; a rare one has no winning
    /// arrangement. On: the chips are drawn again until a bounded search finds a win (<c>CircuitSolver</c>). Off: the
    /// original's draw.
    /// </summary>
    public bool FixUnsolvableCircuitBoard { get; init; }

    /// <summary>
    /// CA-63: the building raid sets the keyboard's repeat (250 ms, 15 a second) through the wrong interrupt, so the PC's
    /// 500 ms delay stays and a held key at the faster game speeds walks, stops and walks on. On: the host applies the
    /// intended repeat while the raid runs. Off: the original's.
    /// </summary>
    public bool FixRaidKeyRepeat { get; init; }

    /// <summary>
    /// CA-64: the Hall of Fame's file is written only if it exists already, so without a FAME.DTA no score is ever saved.
    /// On: a file that is not there at all is made, in the saves folder (never in the player's game folder), and written.
    /// Off: the original's.
    /// </summary>
    public bool FixMissingFameFile { get; init; }

    /// <summary>
    /// CA-58: the lay-out of a new case moves the eastern organisations on the map at every attempt and never moves them
    /// back, so after a few failed attempts they can have drifted where the plot's conditions cannot be met and the game
    /// hangs. On: the organisations' positions are restored before each attempt. Off: the original's.
    /// </summary>
    public bool FixCaseLayOutDrift { get; init; }

    /// <summary>
    /// CA-60: CRIME10's event 20 (the Mastermind's meeting) names member 4, the Extractor, as its partner, but is paired
    /// with the Travel Agent's event 19 (member 5). On: its partner is read as member 5. Off: the original's.
    /// </summary>
    public bool FixCrime10MeetingPartner { get; init; }

    /// <summary>
    /// CA-65: a new agent's training adds the ratings to the skills word without clearing it, so after a practice mission
    /// in the same session (which leaves every skill Good) the ratings come out raised, with a carry into the next skill.
    /// On: the four skill ratings are cleared before the training. Off: the original's.
    /// </summary>
    public bool FixSkillsKeptFromPractice { get; init; }
}
