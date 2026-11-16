using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;

namespace CovertAction.Presentation.Final;

/// <summary>The Hall of Fame: its file, FAME.DTA, which holds the six places as they are in memory.</summary>
public sealed partial class Headquarters
{
    /// <summary>The <c>access</c> modes the original tests the file with: may be read, may be written.</summary>
    private const int MayRead = 4, MayWrite = 2;

    /// <summary>The permission a newly made file gets (read and write for its owner).</summary>
    private const int NewFilePermission = 0x80;

    /// <summary>The C library's answer of <c>access</c> when the file is not there or not allowed.</summary>
    private const int NotAccessible = -1;

    /// <summary>Which of <see cref="FinalState.FameFileNames"/> each call uses.</summary>
    private const int TestedForReading = 0, OpenedForReading = 1, TestedForWriting = 2, OpenedForWriting = 3;

    /// <summary>
    /// Reads the Hall of Fame from its file. The places are emptied first, so that without a file the Hall of Fame is
    /// empty.
    /// </summary>
    // orig: FINAL 0100:4cd0 read_fame_file (src/c/final/final_0100_4cd0.c) matched; tests: FinalOracleTests
    public void ReadFameFile()
    {
        CaseReview.ClearFameTable(_state);
        var files = _world.Files;
        if (files.Access(_state.FameFileNames[TestedForReading], MayRead) == NotAccessible)
        {
            return;
        }
        // the original opens the file for reading and writing, and makes it if it is not there
        var file = files.Open(_state.FameFileNames[OpenedForReading], OpenMode.Binary | OpenMode.Create | OpenMode.ReadWrite, NewFilePermission);
        FinalDgroup.ReadRecords(buffer => files.Read(file, buffer), FinalDgroup.FameEntry, FinalDgroup.FameEntrySize, _state.FameTable, _state.FameTable.Length);
        files.Close(file);
    }

    /// <summary>Writes the Hall of Fame to its file, when the file may be written.</summary>
    // orig: FINAL 0100:4d1d write_fame_file (src/c/final/final_0100_4d1d.c) matched; tests: FinalOracleTests
    // BUG: CA-64 a missing FAME.DTA is never made, so no score is saved; reproduced as coded unless OriginalBehaviour.FixMissingFameFile.
    public void WriteFameFile()
    {
        var files = _world.Files;
        // DIVERGENCE: D-650 a player's option: CA-64, a file that is not there at all is made (in the saves folder) and written
        var missing = _behaviour.FixMissingFameFile && files.Access(_state.FameFileNames[TestedForReading], MayRead) == NotAccessible;
        if (files.Access(_state.FameFileNames[TestedForWriting], MayWrite) == NotAccessible && !missing)
        {
            return;
        }
        var file = files.Open(_state.FameFileNames[OpenedForWriting], OpenMode.Binary | OpenMode.Truncate | OpenMode.Create | OpenMode.ReadWrite, NewFilePermission);
        files.Write(file, FinalDgroup.RecordBytes(FinalDgroup.FameEntry, FinalDgroup.FameEntrySize, _state.FameTable));
        files.Close(file);
    }

    /// <summary>The score of an empty place of the Hall of Fame.</summary>
    private const short EmptyPlace = -1;

    /// <summary>The bit of a place's level word, and of the score kept for its replay, that marks Maxine.</summary>
    private const int FemaleLevelBit = 0x80, FemaleScoreBit = 0x8000;

    /// <summary>
    /// Enters the finished case in the Hall of Fame: its score is the efficiency <paramref name="rating"/> times the
    /// level of play plus one. The case takes the first place whose score is lower (the last place at the latest),
    /// the places below move down one, and the Hall of Fame is shown. If the player clears it there, the case is entered
    /// again into the empty Hall of Fame.
    /// </summary>
    /// <param name="rating">The case's efficiency rating.</param>
    /// <returns>The place the case took, 0..5.</returns>
    // orig: FINAL 0100:5164 insert_hall_of_fame (src/c/final/final_0100_5164.c) matched; tests: FinalOracleTests
    // DORMANT: the original's "return -1" after its search can not be reached: the search stops at the last place.
    public int InsertHallOfFame(int rating)
    {
        const int lastPlace = FinalState.FameEntries - 1;
        var table = _state.FameTable;
        // C: a 16-bit product
        var score = C16.Int(rating * (_state.Diff + 1));
        while (true)
        {
            var place = 0;
            while (table[place].Score >= score && place != lastPlace)
            {
                place++;
            }
            for (var below = lastPlace; below > place; below--)
            {
                table[below].CopyFrom(table[below - 1]);
            }
            var entry = table[place];
            entry.Score = (short)score;
            entry.Name = _state.Pl.Handle;
            entry.Level = _state.Diff;
            if (_state.Status.HasFlag(Options.Female))
            {
                entry.Level |= FemaleLevelBit;
            }
            // the career record's plot word and variant: the plot (0..15) and its variant of the case
            entry.Plot = _state.Pl.Type;
            entry.Variant = _state.Pl.Index;
            entry.Ep = (short)rating;
            entry.Month = _state.Pl.Ncase;
            if (!HallOfFameScreen(place))
            {
                return place;
            }
            CaseReview.ClearFameTable(_state);
        }
    }

    /// <summary>
    /// The Hall of Fame: the first five places (the sixth instead of the fifth when the case just entered took it), each
    /// on a card of its own with the agent's code name, the case's number and date, the crime and its target, the
    /// efficiency points and the score. The place just earned (<paramref name="earnedEntry"/>) is framed and a "Clear"
    /// button offered. Seen from the main menu (<paramref name="earnedEntry"/> -1), the keys 1 to 5 replay the vacation
    /// of that place's case.
    /// </summary>
    /// <param name="earnedEntry">The place the case just took, 0..5, or -1 from the main menu.</param>
    /// <returns>Whether the player chose to clear the Hall of Fame (C).</returns>
    // orig: FINAL 0100:4d67 hall_of_fame_screen (src/c/final/final_0100_4d67.c) matched; tests: FinalOracleTests
    public bool HallOfFameScreen(int earnedEntry)
    {
        const int fromMenu = -1, vacationRows = 0x14, ratingPerRow = 0x96;
        while (true)
        {
            var replayScores = ShowHallOfFame(earnedEntry);
            if (earnedEntry != fromMenu)
            {
                const int clearX = 0xfc, clearY = 0xbd, buttonColour = 0xb;
                Output.DrawText(_state.FameTexts[(int)FameText.Clear], clearX, clearY, 0);
                Output.DrawRectOutline(0xec, 0xbb, 0x3c, 0xa, buttonColour);
            }
            var key = Screens.WaitForKey();
            if (earnedEntry != fromMenu && key is 'c' or 'C')
            {
                return true;
            }
            if (earnedEntry != fromMenu || key is < '1' or > '5')
            {
                return false;
            }
            // the place's case again: its vacation, with the agent of then
            var kept = replayScores[key - '1'];
            _state.Status &= ~Options.Female;
            if ((kept & FemaleScoreBit) != 0)
            {
                _state.Status |= Options.Female;
            }
            PostCaseVacation(GameLibrary.Clamp((kept & 0x7fff) / ratingPerRow, 0, vacationRows), 0);
            Screens.WaitForKey();
            Output.DoSound(SceneEndTune);
            Menus.CheckForDisk(1, 1);
        }
    }

    /// <summary>
    /// Draws the Hall of Fame's screen and cards; returns the places' scores as kept for a replay (the female bit added).
    /// A place not drawn keeps 0 there (the original's word of the stack, never read: only places drawn can be chosen).
    /// </summary>
    private int[] ShowHallOfFame(int earnedEntry)
    {
        const int titleField = 1, firstYear = 0x7c6, lastPlace = FinalState.FameEntries - 1;
        var texts = _state.FameTexts;
        var txt = _state.Txt;
        var replayScores = new int[FinalState.FameEntries];
        Screens.DrawScreenFrame(texts[(int)FameText.Title], titleField);
        _state.Rp1.BPen = 0xf;
        Text.DrawTextCentered(texts[(int)FameText.Heading], 0xa0, 0x10, 0);
        Text.DrawTextCentered(texts[(int)FameText.Subheading], 0xa0, 0x18, 0);
        Output.DrawLine(0x50, 0x20, 0xf0, 0x20, 0xe);
        var y = 0x28;
        for (var place = 0; place < FinalState.FameEntries; place++)
        {
            // the sixth place is shown only when just earned, and then instead of the fifth
            if (place != earnedEntry && place == lastPlace)
            {
                break;
            }
            if (earnedEntry == lastPlace && place == lastPlace - 1)
            {
                continue;
            }
            var x = CaseSetup.Random(_state, 8) + 0xc;
            Screens.DrawPanel(x - 8, y - 4);
            var entry = _state.FameTable[place];
            replayScores[place] = (ushort)entry.Score;
            if ((entry.Level & FemaleLevelBit) != 0)
            {
                replayScores[place] |= FemaleScoreBit;
            }
            if (entry.Score == EmptyPlace)
            {
                txt.Text = texts[(int)FameText.EmptyPlace];
                Output.DrawText(txt.Text, x, y, 0);
                y += 0x1a;
                continue;
            }
            int month = entry.Month;
            txt.Text = FameNumber(place + 1) + texts[(int)FameText.AfterPlace] + texts[(int)FameText.BeforeCodeName] + entry.Name
                + texts[(int)FameText.AfterCodeName] + FameNumber(month + 1) + texts[(int)FameText.BeforeMonth]
                // C: a signed remainder and quotient: a negative month reads in front of the names
                + _state.MonthNames[month % FinalState.MonthCount] + texts[(int)FameText.BeforeYear] + FameNumber(month / 12 + firstYear);
            Output.DrawText(txt.Text, x, y, 0);
            y += 8;
            var plot = _state.Plots[entry.Plot];
            txt.Text = _state.CrimeNames[plot.CrimeFile[entry.Variant]] + texts[(int)FameText.Slash] + plot.ItemA[entry.Variant];
            Output.DrawText(txt.Text, x, y, 9);
            y += 8;
            txt.Text = texts[(int)FameText.Points] + FameNumber(entry.Ep) + texts[(int)FameText.Score] + FameNumber(entry.Score) + texts[(int)FameText.AfterScore];
            Output.DrawText(txt.Text, x, y, 7);
            y += 0xe;
            if (place == earnedEntry)
            {
                _world.Graphics.RecolorRect(_state.Rp1, x - 4, y - 0x20, 0x128, 0x1a, 0xf, 0xe);
            }
        }
        return replayScores;
    }

    /// <summary>A number as the Hall of Fame writes it out, through its digits buffer (the C library's itoa in base 10).</summary>
    private string FameNumber(int value)
    {
        _state.DigitsFame.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _state.DigitsFame.Text;
    }
}
