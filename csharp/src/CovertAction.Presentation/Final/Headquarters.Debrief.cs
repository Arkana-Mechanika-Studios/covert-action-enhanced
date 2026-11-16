using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Final;

/// <summary>The debriefing after a finished case: the chief's closing talk, the case's review, the rating, the career and the way on.</summary>
public sealed partial class Headquarters
{
    /// <summary>How a case ended (<see cref="FinalState.CaseFlags"/>): the crime was carried out, the MasterMind was arrested.</summary>
    private const int CrimeCarriedOut = 0x40, MastermindArrested = 0x80;

    /// <summary>The animation's scenes (the animation's program variable 0): the chief talks, the President congratulates, the agent is promoted.</summary>
    private const int SceneChief = 4, SceneCrimeStopped = 3, ScenePromotion = 1;

    /// <summary>The boxes of the debriefing are wrapped at 38 columns for the President's congratulation and at 36 for the promotion (the chief's are <see cref="BriefingColumns"/>).</summary>
    private const int CongratulationColumns = 0x26, PromotionColumns = 0x24;

    /// <summary>The page of the picture of the roster's icons.</summary>
    private const int IconsPage = 2;

    /// <summary>The case's rating becomes a row of the vacation table: the rating times the level of play (1..4) divided by 150, at most row 20.</summary>
    private const int RatingPerVacationRow = 0x96, LastVacationRow = 0x14;

    /// <summary>The career record (<see cref="Core.Records.Plot.History"/>) is searched for its free place up to this byte.</summary>
    private const int CareerSearchLimit = 0x1fc;

    /// <summary>The byte that ends the career record and marks a free place in it.</summary>
    private const byte EndOfCareerRecords = 0xff;

    /// <summary>Points of the career: the rating times the level (1..4), divided by 16, are added for the promotion's formula.</summary>
    private const int PointsDivisor = 16;

    /// <summary>The promotion's rank: the months of the career (times 100, plus 10000) over the points (plus 100), kept between rank 1 and 99.</summary>
    private const int RankScale = 100, RankOffset = 10000, LowestRank = 1, HighestRank = 0x63;

    /// <summary>The career's end: ten years of cases, one a month.</summary>
    private const int TenYearCareerMonths = 0x78;

    /// <summary>The choices of the end-of-case menu after the first, "Continue Game".</summary>
    private const int SaveChoice = 1, EndChoice = 2, ChangeLevelChoice = 3;

    /// <summary>The choice "End Game" of the menu that follows a saved game.</summary>
    private const int EndAfterSaveChoice = 1;

    /// <summary>What the player may know of the MasterMind, Mr. Big (<see cref="Core.Records.Person.Clues"/>).</summary>
    private const int KnowsPhoto = 1, KnowsIdentity = 2, KnowsOrganisation = 4, KnowsCity = 8, NoFactLeft = -1;

    /// <summary>The base the rank's number is written in.</summary>
    private const int DecimalBase = 10;

    /// <summary>The review's screen is cleared to blue before the roster is drawn on it; a byte holds 256 values (the rating is stored in two).</summary>
    private const int ReviewBackground = 1, ByteRange = 256;

    /// <summary>
    /// The debriefing after a finished case. The CIA chief says the case can be reviewed; the whole case is revealed and
    /// told again by the action log, and the efficiency report rates it. The plot's closing text follows, page by page
    /// (and, when the MasterMind got away, the news that the interrogations gave another fact about Mr. Big, or, when he
    /// was arrested, the President's congratulation). Then the vacation, the Hall of Fame, the career record and
    /// summary, a promotion when the rank improves, the retirement after all 26 MasterMinds or ten years, and the menu: continue,
    /// save, end the game or (after an arrest at levels 0 to 2) change the level of play.
    /// </summary>
    /// <exception cref="ProgramExitException">The player ended the game, or retired; the hand-off's next part is then "leave the game".</exception>
    // orig: FINAL 0100:175e debrief_wrapup (src/c/final/final_0100_175e.c) matched; tests: FinalOracleTests, DebriefTests
    // Uses the library's find_or_create_person (FINAL 0100:5b3f add_person_knowledge: CampaignLibrary.FindOrCreatePerson) and announce_new_facts (FINAL 0100:5caa: LibraryDossiers.RevealPersonFacts).
    // C: the career record's free place is the first byte of 0xff, found byte by byte: a case rated 255, 511, ... (low byte 0xff) has its record cut off there and overwritten by the next case (a suspected original bug, reported)
    public void DebriefWrapup()
    {
        var state = _state;
        var texts = state.DebriefTexts;
        Output.DoSound(ThemeTune);
        StartChiefAnimation(SceneChief);
        state.Txt.Text = texts[(int)DebriefText.ChiefClosingA] + texts[(int)DebriefText.ChiefClosingB];
        ShowBriefingBox();
        ClearTextBox();

        ReviewTheCase();
        var rating = ShowEfficiencyReport();
        Output.DoSound(SceneEndTune);

        TellThePlotsEnd();
        TellWhatTheCaseLeft();
        PostCaseVacation(GameLibrary.Clamp(C16.Int(rating * (state.Diff + 1)) / RatingPerVacationRow, state.Pl.Ncase != 0 ? 1 : 0, LastVacationRow), 1);
        Input.FlushInput();
        Screens.WaitForKey();
        Output.DoSound(SceneEndTune);
        Menus.CheckForDisk(1, 1);

        ReadFameFile();
        InsertHallOfFame(rating);
        WriteFameFile();
        EnterIntoCareer(rating);
        ShowCareerSummary();
        PromoteIfDue(rating);
        RetireIfDue();
        EndOfCaseMenu();
        StartChiefAnimation(SceneChief);
    }

    /// <summary>Starts the chief's animation in the scene given (the animation's program variable 0), with the briefing's colours.</summary>
    private void StartChiefAnimation(int scene)
    {
        Animation.SetScriptVar(BriefingSceneVariable, scene);
        _world.Graphics.SetColorMaps(_state.BriefingColorMap, _state.BriefingColorMap);
        Menus.StartAnimation(_state.DebriefTexts[(int)DebriefText.AnimationFile], 0, 0);
    }

    /// <summary>
    /// Reveals the case for the review: the roster of the cast is drawn (fewer cells at the higher levels, where the
    /// decoys are not shown), what the player knew of each person is kept (in <c>Intel</c>) before the review shows
    /// everything, the MasterMind keeps what he was known by, the case's counters are cleared, and the action log
    /// tells the case.
    /// </summary>
    private void ReviewTheCase()
    {
        var state = _state;
        Pictures.LoadPictureSwapped(state.DebriefTexts[(int)DebriefText.IconsPicture], IconsPage);
        _world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ReviewBackground);
        // the roster shows the cast without the decoys (four to a level)
        state.NRoster = (short)(state.NACTIVITY - state.Diff * 4);
        for (var person = 0; person < state.Nperson; person++)
        {
            state.Ps[person].Intel = state.Ps[person].Clues;
        }
        var mastermind = state.Ps[0];
        state.Pl.Clues = mastermind.Clues;
        CaseReview.RevealCaseState(state);
        mastermind.Clues = state.Pl.Clues;
        // the case's outcome and its points are counted anew by the log and the report
        state.CaseFlags = state.EpEarned = state.ObjectsTakenMask = state.EpPossible = state.ObjectsSeenMask = 0;
        CaseActionLog();
    }

    /// <summary>
    /// The plot's closing text: read page by page from the plot file under the tag of the plot, the variant played and
    /// the page (a variant whose crime was carried out has the text of the "5" variant, and the chief's other
    /// scene), then, for a case that ended without the crime, the variant is marked played; otherwise the news of one
    /// more fact about Mr. Big, when the interrogations gave one.
    /// </summary>
    private void TellThePlotsEnd()
    {
        const int plotDigits = 3, variantDigit = 5, pageDigit = 6;
        const char carriedOutPage = '5';
        var state = _state;
        var crimeCarriedOut = (state.CaseFlags & CrimeCarriedOut) != 0;
        var tag = state.PlotTag;
        tag.Text = state.DebriefTexts[(int)DebriefText.PlotTagTemplate];
        tag[plotDigits] = Digit(state.Pl.Type / 10);
        tag[plotDigits + 1] = Digit(state.Pl.Type % 10);
        tag[variantDigit] = Digit(state.Pl.Index);
        if (crimeCarriedOut)
        {
            tag[pageDigit] = carriedOutPage;
        }
        StartChiefAnimation(crimeCarriedOut ? SceneChief : SceneCrimeStopped);
        bool found;
        do
        {
            state.Txt.Text = "";
            found = Text.ReadTextWithVars(state.DebriefTexts[(int)DebriefText.PlotFile], tag.Text);
            if (found)
            {
                ShowBriefingBox();
                ClearTextBox();
            }
            tag[pageDigit]++;
        }
        while (found);
        if (crimeCarriedOut)
        {
            // C: a shift by a variable count, as the 8086 takes it
            state.Pl.Done |= C16.Shl(1, state.Pl.Index);
        }
        else
        {
            TellAnotherFactOfMrBig();
        }
    }

    /// <summary>
    /// When the interrogations of the suspects reveal one more fact about Mr. Big (the MasterMind), whom the agent has not
    /// caught: a box says so, and the fact is added to what the player knows (with the dossier shown). Not for the first plot.
    /// </summary>
    private void TellAnotherFactOfMrBig()
    {
        var state = _state;
        var fact = NextFactOfMrBig(state.Pl.Clues);
        if (fact == NoFactLeft || state.Pl.Type == 0)
        {
            return;
        }
        state.Txt.Text = state.DebriefTexts[(int)DebriefText.InterrogationRevealed];
        ShowBriefingBox();
        CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, state.Pl.BigOrg, state.Pl.BigLoc, fact, true, AnnounceNewFacts, _behaviour);
        state.Pl.Clues |= (short)fact;
    }

    /// <summary>
    /// The fact of Mr. Big the interrogations reveal: the first the player does not know yet, in the order identity,
    /// photo, organisation, city.
    /// </summary>
    /// <param name="known">What the player knows of Mr. Big (<see cref="Core.Records.Person.Clues"/>).</param>
    /// <returns>The bit of the fact, or <see cref="NoFactLeft"/> when all four are known.</returns>
    private static int NextFactOfMrBig(int known)
    {
        // C: the original tests the four bits in the order city, organisation, photo, identity and a later one overwrites the earlier choice
        foreach (var fact in new[] { KnowsIdentity, KnowsPhoto, KnowsOrganisation, KnowsCity })
        {
            if ((known & fact) == 0)
            {
                return fact;
            }
        }
        return NoFactLeft;
    }

    /// <summary>
    /// Tells the player the new facts about a person, one box each with the dossier redrawn between them, and waits for
    /// a key. The library's routine, with this part's message box.
    /// </summary>
    /// <param name="person">The person, 0..63; the record holds what is known now.</param>
    /// <param name="knownBefore">What was known before (<see cref="Core.Records.Person.Clues"/>).</param>
    // New code: the library's announce_new_facts (FINAL 0100:5caa, LibraryDossiers.RevealPersonFacts) with this part's message box; tests: FinalOracleTests
    public void AnnounceNewFacts(int person, int knownBefore) =>
        Dossiers.RevealPersonFacts(person, knownBefore, (text, x, y) =>
        {
            // the dossier is drawn complete; the next box waits for a key again
            _state.MenuDrawOnly = false;
            _state.Txt.Text = text;
            MenuChoice(_state.Txt, x, y);
        });

    /// <summary>When the MasterMind is arrested (person 0's alert is -1) the President congratulates the agent.</summary>
    private void TellWhatTheCaseLeft()
    {
        const short arrested = -1;
        var state = _state;
        if (state.Ps[0].Alert != arrested)
        {
            return;
        }
        var texts = state.DebriefTexts;
        state.Txt.Text = texts[(int)DebriefText.CapturedBefore] + state.Org[state.Pl.BigOrg].Name + texts[(int)DebriefText.CapturedMiddle]
            + texts[(int)DebriefText.CapturedAfter];
        ClearTextBox();
        ShowBriefingBox(CongratulationColumns);
    }

    /// <summary>
    /// Counts the case into the career: one more month; its record (plot and variant, how it ended, the rating in two
    /// bytes, and the MasterMind when one was arrested) is put at the end of the career record, and an arrested MasterMind
    /// is marked in the mask of those caught.
    /// </summary>
    /// <param name="rating">The case's efficiency rating.</param>
    private void EnterIntoCareer(int rating)
    {
        const int variantBits = 4;
        var state = _state;
        var career = state.Pl;
        career.Ncase++;
        var records = career.History;
        // the first byte of 0xff is taken for the end of the record (C:, see DebriefWrapup)
        state.CareerWritePos = -1;
        // BUG: CA-46 a rating whose low byte is 0xff is taken for the end mark; OriginalBehaviour.FixCareerRecordEndSearch walks the records
        var step = 1;
        for (var at = 0; at < CareerSearchLimit; at += step)
        {
            if (records[at] == EndOfCareerRecords)
            {
                state.CareerWritePos = (short)at;
                break;
            }
            if (_behaviour.FixCareerRecordEndSearch)
            {
                // a record is four bytes, five when the MasterMind was arrested (the second byte tells)
                step = (records[at + 1] & MastermindArrested) != 0 ? 5 : 4;
            }
        }
        if (state.CareerWritePos != -1)
        {
            var at = (int)state.CareerWritePos;
            records[at++] = (byte)((career.Type << variantBits) + career.Index);
            records[at++] = (byte)state.CaseFlags;
            records[at++] = (byte)(rating / ByteRange);
            records[at++] = (byte)rating;
            if ((state.CaseFlags & MastermindArrested) != 0)
            {
                records[at++] = (byte)career.BigIndex;
            }
            // DORMANT: a full record would have the end mark written behind its last byte; a career of at most 120 cases never fills it
            records[at] = EndOfCareerRecords;
            state.CareerWritePos = (short)at;
        }
        if ((state.CaseFlags & MastermindArrested) != 0)
        {
            career.Bigs |= (uint)C16.LShl(1, career.BigIndex);
        }
    }

    /// <summary>
    /// Adds the case's points to the career's and works out the agent's rank anew: the months of the career over the
    /// points. A better rank (a smaller number) than the agent has is a promotion, which the chief and the President tell.
    /// </summary>
    /// <param name="rating">The case's efficiency rating.</param>
    private void PromoteIfDue(int rating)
    {
        var state = _state;
        var career = state.Pl;
        var texts = state.DebriefTexts;
        // C: all of it is 16-bit arithmetic
        career.VPs += C16.Int(C16.Int(rating * (state.Diff + 1)) / PointsDivisor);
        var newRank = GameLibrary.Clamp(C16.Div(C16.Int(RankScale * career.Ncase + RankOffset), C16.Int(career.VPs + RankScale)), LowestRank, HighestRank);
        if (newRank >= career.Rank)
        {
            return;
        }
        state.Txt.Text = texts[(int)DebriefText.PromotionBefore] + RankDigits(career.Rank) + texts[(int)DebriefText.PromotionMiddle] + RankDigits(newRank)
            + texts[(int)DebriefText.PromotionAfter];
        career.Rank = (short)newRank;
        StartChiefAnimation(ScenePromotion);
        ShowBriefingBox(PromotionColumns);
        ClearTextBox();
    }

    /// <summary>A rank number as the promotion's text writes it, through the debriefing's digits buffer (the C library's itoa).</summary>
    private string RankDigits(int rank)
    {
        CStr.Itoa((short)rank, _state.DigitsWrapup.Bytes, DecimalBase);
        return _state.DigitsWrapup.Text;
    }

    /// <summary>After all 26 MasterMinds or ten years of cases the agent retires: the retirement screen, and the game ends.</summary>
    private void RetireIfDue()
    {
        var state = _state;
        var texts = state.DebriefTexts;
        state.Animation.AnimationActive = false;
        var arrestedAll = state.MastermindsCaught == FinalState.MastermindCount;
        var servedTenYears = state.Pl.Ncase == TenYearCareerMonths;
        if (arrestedAll)
        {
            state.Txt.Text = texts[(int)DebriefText.AllMasterMinds];
        }
        if (servedTenYears)
        {
            // (both: the ten years' text replaces the other)
            state.Txt.Text = texts[(int)DebriefText.TenYears];
        }
        if (!arrestedAll && !servedTenYears)
        {
            return;
        }
        state.Txt.Text += texts[(int)DebriefText.Retire];
        RetirementScreen();
        LeaveTheGame();
    }

    /// <summary>
    /// The menu at the end of the debriefing: go on to the next case, save the game, end it, or (after an arrest at the
    /// levels below the hardest) choose another level of play first. A saved game is marked as saved in this part, and
    /// the player may then go on or end the game.
    /// </summary>
    private void EndOfCaseMenu()
    {
        var state = _state;
        var texts = state.DebriefTexts;
        while (true)
        {
            state.Txt.Text = texts[(int)DebriefText.EndMenu];
            if (state.Diff < HardestLevel && (state.CaseFlags & MastermindArrested) != 0)
            {
                state.Txt.Text += texts[(int)DebriefText.ChangeLevelItem];
            }
            var choice = MenuChoice(state.Txt, TheMenuX, TheMenuY);
            switch (choice)
            {
                case ChangeLevelChoice:
                    // C: the level is raised first and then replaced by the player's choice
                    state.Diff++;
                    state.Txt.Text = texts[(int)DebriefText.LevelMenu];
                    state.Diff = (short)GameLibrary.Clamp(MenuChoice(state.Txt, TheMenuX, TheMenuY), 0, HardestLevel);
                    continue;
                case EndChoice:
                    LeaveTheGame();
                    break;
                case SaveChoice:
                    SaveAtTheEndOfTheCase();
                    break;
            }
            // continue (also what any other answer, as Esc, leads to)
            return;
        }
    }

    /// <summary>Saves the game as it stands at the case's end (marked as saved here: the next start of the part debriefs the next step again), then offers to go on or to end it.</summary>
    private void SaveAtTheEndOfTheCase()
    {
        var state = _state;
        state.Status |= Options.SavedInFinal;
        // the saved campaign starts at the clock's beginning in the agent's home city
        const short homeCity = 1;
        state.Gtime = 0;
        state.InCity = homeCity;
        Saves.SelectSaveFile();
        state.Txt.Text = state.DebriefTexts[(int)DebriefText.EndMenuAfterSave];
        if (MenuChoice(state.Txt, TheMenuX, TheMenuY) == EndAfterSaveChoice)
        {
            LeaveTheGame();
        }
    }

    /// <summary>Ends the program with "leave the game" as the next part in the hand-off block.</summary>
    /// <exception cref="ProgramExitException">Always.</exception>
    private void LeaveTheGame()
    {
        ShutdownHook();
        _shared.NextModule = LeaveGame;
        // DIVERGENCE: D-104 exit() is called without an exit code: DOS gets the word the debriefing saved (SI, 82h), 130; here 0.
        throw new ProgramExitException(0);
    }
}
