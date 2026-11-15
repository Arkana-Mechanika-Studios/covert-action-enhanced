using CovertAction.Core.Electronics;
using CovertAction.Persistence.Dgroup;

namespace CovertAction.Persistence.Tests.Electronics;

/// <summary>The layout of BUG's data segment on made-up bytes: offsets as in the reconstructed bug.h, and a byte-identical round trip.</summary>
public sealed class BugDgroupTests
{
    private static byte[] Noise()
    {
        var image = new byte[0x10000];
        new Random(1990).NextBytes(image);
        // every seventh byte 0, so that every text (city names, the clock) has its end mark
        for (var i = 0; i < image.Length; i += 7)
        {
            image[i] = 0;
        }
        return image;
    }

    private static void SetWord(byte[] image, int offset, int value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
    }

    [Fact]
    public void TheDataSegmentRoundTripsByteForByte()
    {
        var image = Noise();
        // every short field gets an end mark in its last byte, so that the short texts (names, joining words) have one
        foreach (var field in BugDgroup.Layout.Fields.Where(f => f.Length <= 7))
        {
            image[field.Offset + field.Length - 1] = 0;
        }
        // the three port pointers hold the addresses of their records
        SetWord(image, 0x155a, 0x1548);
        SetWord(image, 0x1650, 0x163e);
        SetWord(image, 0x2f08, 0x2ef6);
        // the disk names' table, and the animation record that names its file
        SetWord(image, 0x2eec, 0x2e74);
        SetWord(image, 0x2eee, 0x2e7e);
        SetWord(image, 0x2ef0, 0x2e87);
        SetWord(image, 0x2ef2, 0x2e8f);
        SetWord(image, 0x3a68, 0x2e34);
        // the texts shorter than seven bytes get their end marks
        image[0x2e1d + 2] = 0;   // "OK"
        image[0x2e2d + 3] = 0;   // "bld"
        image[0x2ec1 + 1] = 0;   // ")"
        for (var org = 0; org < 16; org++)
        {
            image[0x7fc0 + 36 * org + 5] = 0;   // the organisations' short names, six bytes each
        }
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(image, state);
        var stored = (byte[])image.Clone();
        BugDgroup.Layout.Store(state, stored);
        Assert.Equal(image, stored);
    }

    [Fact]
    public void TheTwoDimensionalTablesAreStoredRowAfterRow()
    {
        var image = new byte[0x10000];
        SetWord(image, 0x594c + 2 * (5 * 3 + 2), 0x8b);            // BUGchip[3][2]
        SetWord(image, 0x747e + 2 * (10 * 5 + 9), 7);              // connect[5][9]
        SetWord(image, 0x750a + 2 * (10 * 13 + 4), 1);             // pin[13][4], the terminal of line 4
        SetWord(image, 0x843c + 2 * (25 * 39 + 24), 0x144);        // board[39][24]
        SetWord(image, 0x00b2 + 2 * (8 * 11 + 7), 1);              // truth_tables[11], lower output for inputs 1, 1
        SetWord(image, 0x7622 + 2 * 9, 1);                         // terminal_flags[9]
        image[0x1654 + 17 * 4 + 16] = 0x2a;                        // the border colour of palette 4
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(image, state);
        Assert.Equal(0x8b, state.BUGchip[3][2]);
        Assert.Equal(7, state.Connect[5][9]);
        Assert.Equal(1, state.Pin[13][4]);
        Assert.Equal((short)(Wire.Powered | Wire.West | Wire.East), state.Board[39][24]);
        Assert.Equal(1, state.TruthTables[11][7]);
        Assert.True(state.TerminalFlags[9]);
        Assert.Equal(0x2a, state.Palettes[4][16]);
    }

    /// <summary>
    /// The texts of the library's screens about persons (what a campaign wiretap shows) lie 0xb02 bytes below TAC's
    /// copies; the names, months, clue labels and message file the part had already are the same texts, not copies.
    /// </summary>
    [Fact]
    public void TheDossierTextsAreLoadedAndShareThePartsOwnNames()
    {
        var image = new byte[0x10000];
        void Put(int offset, string text) => System.Text.Encoding.Latin1.GetBytes(text).CopyTo(image, offset);
        Put(0x1adb, " has been\nidentified");          // TAC DS:25dd PlotSuffix
        Put(0x21b1, "Rcvd msg fm");                    // TAC DS:2cb3 the message log's "Received"
        Put(0x23f8, "text.dta");                       // BUG's message file, now the dossier's
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(image, state);
        Assert.Equal(" has been\nidentified", state.Dossier.PlotSuffix);
        Assert.Equal("Rcvd msg fm", state.Dossier.Words.Received);
        Assert.Equal("text.dta", state.Dossier.MessageFile);
        Assert.Same(state.Dossier.Names, state.Names);
        Assert.Same(state.Dossier.Words.MonthNames, state.MonthNames);
        Assert.Same(state.Dossier.ClueLabels, state.KeyPhrases);
        Assert.Same(state.Dossier.MessageTag, state.MessageTag);
    }

    /// <summary>
    /// coded_flag (DS:675e) is one variable: gather_intelligence sets it and the clue report reads it as the clue's
    /// source (show_clue_report, BUG 00e8:2911: 1 is "Electronic WireTap"). Kept apart, the report read 0 and wrote
    /// "Clandestine Photo" with the camera under a wiretap's clue (session BUG/level1-tap, checkpoint 6).
    /// </summary>
    [Fact]
    public void TheCodedFlagIsTheClueReportsSource()
    {
        var image = new byte[0x10000];
        SetWord(image, 0x675e, 1);
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(image, state);
        Assert.Equal(1, state.Dossier.CodedFlag);
        state.CodedFlag = 0;
        Assert.Equal(0, state.Dossier.CodedFlag);
        state.Dossier.CodedFlag = 1;
        Assert.Equal(1, state.CodedFlag);
    }

    /// <summary>
    /// The dossier's " in " before the city a person was recruited near (show_suspect_dossier, BUG 00e8:31b8, TAC
    /// 00e8:9a3c) lies between " by " and "." in BUG (DS:2137) and in TAC (DS:2c39), as FINAL's and GAME's: unloaded,
    /// the line read "RecruitedAmman." where the original has "Recruited in Amman." (session COVERT/mastermind-debrief).
    /// </summary>
    [Fact]
    public void TheRecruitmentCitysInIsLoadedInBugAndTac()
    {
        var image = new byte[0x10000];
        void Put(int offset, string text) => System.Text.Encoding.Latin1.GetBytes(text + "\0").CopyTo(image, offset);
        Put(0x2128, "Recruited");
        Put(0x2132, " by ");
        Put(0x2137, " in ");
        Put(0x213c, ".");
        var bug = new ElectronicsState();
        BugDgroup.Layout.Load(image, bug);
        Assert.Equal(" in ", bug.Dossier.In);
        Put(0x2c2a, "Recruited");
        Put(0x2c34, " by ");
        Put(0x2c39, " in ");
        Put(0x2c3e, ".");
        var raid = new CovertAction.Core.Raid.RaidState();
        TacDgroup.Layout.Load(image, raid);
        Assert.Equal(" in ", raid.Dossier.In);
        Assert.Equal(" by ", raid.Dossier.By);
        Assert.Equal(".", raid.Dossier.Period);
    }

    /// <summary>
    /// Message -1, the 14 bytes in front of the message table (DS:604a): its minute is city 15's organisation clues
    /// (DS:604c), its knowledge word city 15's map position (DS:6050), its second city the high byte of mode (DS:6057).
    /// </summary>
    [Fact]
    public void MessageMinusOneIsReadFromTheBytesInFrontOfTheMessageTable()
    {
        var image = new byte[0x10000];
        SetWord(image, 0x604c, 0x1234);
        SetWord(image, 0x6050, 0x0a05);
        SetWord(image, 0x6056, 0x0301);
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(image, state);
        var message = ((CovertAction.Core.Shared.ICampaignTables)state).MessageBeforeFirst;
        Assert.Equal(0x1234, message.Time);
        Assert.Equal(0x0a05, message.Clues);
        Assert.Equal(3, message.Loc2);
    }
}
