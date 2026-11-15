using CovertAction.Core.Campaign;
using CovertAction.Core.Graphics;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's campaign program, GAME.EXE: where each variable of
/// <see cref="CampaignState"/> lies in it. The start state (the texts and tables) is loaded through it from the
/// player's own GAME.EXE, and the oracle tests load and compare the state through it.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed game.h and symbols/names.txt (<c>D_game_xxxx</c>); the campaign records have the
/// layouts of <see cref="CampaignLayouts"/> (the same as in the hand-off block). The texts the C writes as literals
/// were located from the instructions that use them (by disassembling the program); the tables of texts are tables of
/// near pointers into the program's initialised data, which never change: each pointer is a fixed one, and the text
/// it points at a field. The place of the C library's <c>holdrand</c> was read from the vectors of
/// <c>random_below</c>, the only variable it changes. Only the variables of the ported functions are listed; the
/// others join with the first function that uses them.
/// </remarks>
public static class GameDgroup
{
    // The tables of texts: for each text its address and the size of its buffer (the text and its end mark).
    private static readonly (int Address, int Length)[] MonthTexts =
    [
        (0x2a88, 4), (0x2a8c, 4), (0x2a90, 4), (0x2a94, 4), (0x2a98, 4), (0x2a9c, 4), (0x2aa0, 4), (0x2aa4, 4),
        (0x2aa8, 4), (0x2aac, 4), (0x2ab0, 4), (0x2ab4, 4),
    ];

    private static readonly (int Address, int Length)[] FirstNameTexts =
    [
        (0x171f, 9), (0x1728, 7), (0x172f, 6), (0x1735, 8), (0x173d, 6), (0x1743, 7), (0x174a, 8), (0x1752, 6),
        (0x1758, 8), (0x1760, 9), (0x1769, 5), (0x176e, 9), (0x1777, 9), (0x1780, 9), (0x1789, 5), (0x178e, 6),
        (0x1794, 6), (0x179a, 4), (0x179e, 6), (0x17a4, 7), (0x17ab, 6), (0x17b1, 7), (0x17b8, 6), (0x17be, 5),
        (0x17c3, 8), (0x17cb, 7), (0x17d2, 5), (0x17d7, 6), (0x17dd, 4), (0x17e1, 5), (0x17e6, 8), (0x17ee, 5),
        (0x17f3, 6), (0x17f9, 7), (0x1800, 7), (0x1807, 6), (0x180d, 7), (0x1814, 6), (0x181a, 7), (0x1821, 6),
        (0x1827, 7), (0x182e, 8), (0x1836, 6), (0x183c, 7), (0x1843, 8), (0x184b, 7), (0x1852, 9), (0x185b, 5),
        (0x1860, 9), (0x1869, 7), (0x1870, 8), (0x1878, 7), (0x187f, 6), (0x1885, 6), (0x188b, 8), (0x1893, 7),
        (0x189a, 7), (0x18a1, 6), (0x18a7, 6), (0x18ad, 6), (0x18b3, 7), (0x18ba, 6), (0x18c0, 8), (0x18c8, 8),
        (0x18d0, 7), (0x18d7, 7), (0x18de, 5), (0x18e3, 7), (0x18ea, 7), (0x18f1, 7), (0x18f8, 7), (0x18ff, 5),
        (0x1904, 6), (0x190a, 8), (0x1912, 8), (0x191a, 6), (0x1920, 7), (0x1927, 7), (0x192e, 9), (0x1937, 8),
        (0x193f, 5), (0x1944, 4), (0x1948, 5), (0x194d, 4), (0x1951, 4), (0x1955, 5), (0x195a, 5), (0x195f, 6),
        (0x1965, 8), (0x196d, 9), (0x1976, 5), (0x197b, 7), (0x1982, 6), (0x1988, 7), (0x198f, 7), (0x1996, 7),
        (0x199d, 5), (0x19a2, 4), (0x19a6, 6), (0x19ac, 6), (0x19b2, 5), (0x19b7, 4), (0x19bb, 6), (0x19c1, 6),
        (0x19c7, 7), (0x19ce, 8), (0x19d6, 6), (0x19dc, 7), (0x19e3, 8), (0x19eb, 8), (0x19f3, 9), (0x19fc, 6),
        (0x1a02, 7), (0x1a09, 5), (0x1a0e, 7), (0x1a15, 6), (0x1a1b, 5), (0x1a20, 6), (0x1a26, 5), (0x1a2b, 6),
        (0x1a31, 8), (0x1a39, 8), (0x1a41, 7), (0x1a48, 6), (0x1a4e, 7), (0x1a55, 7), (0x1a5c, 7), (0x1a63, 6),
    ];

    private static readonly (int Address, int Length)[] SurnameTexts =
    [
        (0x1a69, 7), (0x1a70, 6), (0x1a76, 8), (0x1a7e, 9), (0x1a87, 8), (0x1a8f, 7), (0x1a96, 7), (0x1a9d, 8),
        (0x1aa5, 8), (0x1aad, 8), (0x1ab5, 6), (0x1abb, 7), (0x1ac2, 7), (0x1ac9, 8), (0x1ad1, 10), (0x1adb, 9),
        (0x1ae4, 7), (0x1aeb, 5), (0x1af0, 7), (0x1af7, 6), (0x1afd, 6), (0x1b03, 6), (0x1b09, 7), (0x1b10, 7),
        (0x1b17, 5), (0x1b1c, 7), (0x1b23, 7), (0x1b2a, 6), (0x1b30, 8), (0x1b38, 8), (0x1b40, 6), (0x1b46, 7),
        (0x1b4d, 6), (0x1b53, 5), (0x1b58, 6), (0x1b5e, 7), (0x1b65, 6), (0x1b6b, 7), (0x1b72, 7), (0x1b79, 7),
        (0x1b80, 5), (0x1b85, 6), (0x1b8b, 6), (0x1b91, 7), (0x1b98, 8), (0x1ba0, 8), (0x1ba8, 5), (0x1bad, 7),
        (0x1bb4, 6), (0x1bba, 5), (0x1bbf, 6), (0x1bc5, 6), (0x1bcb, 8), (0x1bd3, 8), (0x1bdb, 7), (0x1be2, 6),
        (0x1be8, 7), (0x1bef, 6), (0x1bf5, 8), (0x1bfd, 7), (0x1c04, 9), (0x1c0d, 9), (0x1c16, 8), (0x1c1e, 9),
    ];

    private static readonly (int Address, int Length)[] ClueLabelTexts =
    [
        (0x2db7, 14), (0x2dc5, 13), (0x2dd2, 16), (0x2de2, 10), (0x2dec, 12), (0x2df8, 14), (0x2e06, 9), (0x2e0f, 11),
        (0x2e1a, 14), (0x2e28, 19), (0x2e3b, 15), (0x2e4a, 15), (0x2e59, 18), (0x2e6b, 12), (0x2e77, 10), (0x2e81, 13),
        (0x2e8e, 13), (0x2e9b, 13), (0x2ea8, 10), (0x2eb2, 12), (0x2ebe, 14), (0x2ecc, 13), (0x2ed9, 14), (0x2ee7, 12),
        (0x2ef3, 8), (0x2efb, 9), (0x2f04, 9), (0x2f0d, 12), (0x2f19, 8), (0x2f21, 6), (0x2f27, 11), (0x2f32, 7),
        (0x2f39, 10), (0x2f43, 6), (0x2f49, 8), (0x2f51, 8), (0x2f59, 11), (0x2f64, 10), (0x2f6e, 10), (0x2f78, 10),
        (0x2f82, 2), (0x2f84, 2), (0x2f86, 2), (0x2f88, 2), (0x2f8a, 2), (0x2f8c, 2), (0x2f8e, 2), (0x2f90, 2),
        (0x2f92, 2), (0x2f94, 2), (0x2f96, 2), (0x2f98, 2), (0x2f9a, 2), (0x2f9c, 2), (0x2f9e, 2), (0x2fa0, 2),
        (0x2fa2, 17), (0x2fb3, 13), (0x2fc0, 20), (0x2fd4, 12), (0x2fe0, 19), (0x2ff3, 13), (0x3000, 13), (0x300d, 20),
    ];

    private static readonly (int Address, int Length)[] ActivityPhraseTexts =
    [
        (0x2868, 10),        (0x2872, 16),        (0x2882, 14),        (0x2890, 15),        (0x289f, 16),        (0x28af, 10),        (0x28b9, 16),        (0x28c9, 12),
        (0x28d5, 16),        (0x28e5, 16),        (0x28f5, 10),        (0x28ff, 12),        (0x290b, 16),        (0x291b, 8),        (0x2923, 12),        (0x292f, 15),
        (0x293e, 15),        (0x294d, 12),        (0x2959, 15),        (0x2968, 12),        (0x2974, 14),        (0x2982, 16),        (0x2992, 12),        (0x299e, 10),
        (0x29a8, 12),        (0x29b4, 15),        (0x29c3, 15),        (0x29d2, 10),        (0x29dc, 17),        (0x29ed, 16),        (0x29fd, 15),        (0x2a0c, 15),
        (0x2a1b, 10),        (0x2a25, 17),        (0x2a36, 16),        (0x2a46, 14),        (0x2a54, 14),        (0x2a62, 14),        (0x2a70, 10),        (0x2a7a, 14),
    ];

    private static readonly (int Address, int Length)[] ClueSourceTexts = [(0x30c2, 18), (0x30d4, 19), (0x30e7, 20), (0x30fb, 19), (0x310e, 16), (0x311e, 19), (0x3131, 18)];

    /// <summary>The words of alertness of append_alert_level, unsuspecting to very nervous (literals, no table).</summary>
    private static readonly (int Address, int Length)[] AlertTexts = [(0x276f, 13), (0x277c, 9), (0x2785, 11), (0x2790, 8), (0x2798, 13)];

    /// <summary>The variables of GAME.EXE at their offsets.</summary>
    // orig: GAME DGROUP (src/c/include/game.h; holdrand DS:5234 located from the vectors of random_below)
    public static StateLayout<CampaignState> Layout { get; } = Build();  // after the tables of texts it is built from

    private static StateLayout<CampaignState> Build()
    {
        var layout = new StateLayout<CampaignState>("GAME DGROUP");

        // ---- this part's copy of the campaign ----
        Records(layout, 0x0094, "DS:0094 ps", CampaignLayouts.PersonSize, CampaignState.PersonCount, CampaignLayouts.Person, s => s.Ps);
        Records(layout, 0x0494, "DS:0494 lc", CampaignLayouts.LocationSize, CampaignState.LocationCount, CampaignLayouts.Location, s => s.Lc);
        Records(layout, 0x1094, "DS:1094 cl", CampaignLayouts.ClueSize, CampaignState.ClueCount, CampaignLayouts.Clue, s => s.Cl);
        Records(layout, 0x7a9e, "DS:7a9e ct", CampaignLayouts.CitySize, CampaignState.WorldSize, CampaignLayouts.City, s => s.Ct);
        Records(layout, 0x7cc2, "DS:7cc2 sn", CampaignLayouts.SynopsisSize, CampaignState.SynopsisCount, CampaignLayouts.Synopsis, s => s.Sn);
        Records(layout, 0x83c6, "DS:83c6 ev", CampaignLayouts.EventSize, CampaignState.EventCapacity, CampaignLayouts.Event, s => s.Ev);
        Records(layout, 0x97a6, "DS:97a6 ac", CampaignLayouts.ActivitySize, CampaignState.CastCapacity, CampaignLayouts.Activity, s => s.Ac);
        Records(layout, 0x9fb4, "DS:9fb4 org", CampaignLayouts.OrgSize, CampaignState.WorldSize, CampaignLayouts.Org, s => s.Orgs);
        Records(layout, 0xa414, "DS:a414 mc", CampaignLayouts.MacGuffinsSize, CampaignState.MacGuffinCount, CampaignLayouts.MacGuffins, s => s.Mc);
        Records(layout, 0x1e22, "DS:1e22 incoming_banner_shapes", 12, CampaignState.BannerShapeCount, ShapeLayout, s => s.BannerShapes);
        Records(layout, 0xa55e, "DS:a55e ac2", CampaignLayouts.Activity2Size, CampaignState.CastCapacity, CampaignLayouts.Activity2, s => s.Ac2);
        layout
            .Part(0x760a, "DS:760a pl", CampaignLayouts.Plot, s => s.Pl)
            .Int16Array(0xa628, CampaignState.WorldSize, "DS:a628 active", s => s.Active)
            .Int16Array(0xa648, CampaignState.WorldSize, "DS:a648 org_counters", s => s.OrgCounters)

            // ---- the campaign's scalars ----
            .Int16(0x1454, "DS:1454 Sprung", s => s.Sprung, (s, v) => s.Sprung = v)
            .Int16(0x557e, "DS:557e excluded_person", s => s.ExcludedPerson, (s, v) => s.ExcludedPerson = v)
            .Int16(0x5580, "DS:5580 flagged_cast_person", s => s.FlaggedCastPerson, (s, v) => s.FlaggedCastPerson = v)
            .Int16(0x7604, "DS:7604 diff", s => s.Diff, (s, v) => s.Diff = v)
            .Int16(0x7606, "DS:7606 day", s => s.Day, (s, v) => s.Day = v)
            .Int16(0x7608, "DS:7608 Gtime", s => s.Gtime, (s, v) => s.Gtime = v)
            .Int16(0x7cc0, "DS:7cc0 Nperson", s => s.Nperson, (s, v) => s.Nperson = v)
            .Int16(0x8ec6, "DS:8ec6 NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v)
            .Int16(0x8ece, "DS:8ece Nloc", s => s.Nloc, (s, v) => s.Nloc = v)
            .Int16(0x8ed0, "DS:8ed0 ChronTime", s => s.ChronTime, (s, v) => s.ChronTime = v)
            .Int16(0x93ea, "DS:93ea min", s => s.Min, (s, v) => s.Min = v)
            .Int16(0x9682, "DS:9682 hour", s => s.Hour, (s, v) => s.Hour = v)
            .Int16(0x9688, "DS:9688 NEVENT", s => s.NEVENT, (s, v) => s.NEVENT = v)
            .Int16(0x9690, "DS:9690 TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v)
            .Int16(0x978c, "DS:978c Alerted", s => s.Alerted, (s, v) => s.Alerted = v)
            .Int16(0x97a0, "DS:97a0 Doubles", s => s.Doubles, (s, v) => s.Doubles = v)
            .Int16(0x97a2, "DS:97a2 NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v)
            .Int16(0xa626, "DS:a626 InCity", s => s.InCity, (s, v) => s.InCity = v)
            .Int16(0xa66a, "DS:a66a Sleepy", s => s.Sleepy, (s, v) => s.Sleepy = v)
            .Int16(0xaa58, "DS:aa58 AtLoc", s => s.AtLoc, (s, v) => s.AtLoc = v)

            // ---- the C library's random state ----
            .UInt32(0x5234, "DS:5234 holdrand", s => s.Random.State, (s, v) => s.Random.State = v)

            // ---- texts the campaign writes ----
            .Bytes(0x7202, 1024, "DS:7202 Txt", s => s.Txt.Bytes)
            .Bytes(0x3cf8, 16, "DS:3cf8 clock_text", s => s.ClockText.Bytes)
            .Bytes(0x5514, 16, "DS:5514 number_text", s => s.NumberText.Bytes)
            .Bytes(0xa45e, 256, "DS:a45e tail of replace_substrings", s => s.ReplaceTail.Bytes)

            // ---- texts of the program ----
            .Text(0x1717, 8, "DS:1717 \"Agent A\"", s => s.StrAgentLetter, (s, v) => s.StrAgentLetter = v)
            .Text(0x1c27, 2, "DS:1c27 \" \"", s => s.StrNameSpace, (s, v) => s.StrNameSpace = v)
            .Text(0x31c4, 11, "DS:31c4 \"this face.\"", s => s.StrThisFace, (s, v) => s.StrThisFace = v)
            .Text(0x31cf, 9, "DS:31cf code name of a participant", s => s.StrCodeNameReal, (s, v) => s.StrCodeNameReal = v)
            .Text(0x31d8, 12, "DS:31d8 code name of a decoy", s => s.StrCodeNameDecoy, (s, v) => s.StrCodeNameDecoy = v)
            .Text(0x31e4, 3, "DS:31e4 clue_suffix_5", s => s.ClueSuffixHundreds, (s, v) => s.ClueSuffixHundreds = v)
            .Text(0x31e7, 4, "DS:31e7 clue_suffix_6", s => s.ClueSuffixThousands, (s, v) => s.ClueSuffixThousands = v)
            .Text(0x3b0c, 11, "DS:3b0c \"facesf.pic\"", s => s.FaceSheetName, (s, v) => s.FaceSheetName = v)
            .Text(0x3b17, 10, "DS:3b17 \"faces.pic\"", s => s.FaceSheet2Name, (s, v) => s.FaceSheet2Name = v)
            .Bytes(0x16ae, 17, "DS:16ae ega_palette", s => s.EgaPalette)
            .Bytes(0x16c2, 14, "DS:16c2 scene_name", s => s.SceneName.Bytes)
            .Bytes(0x3a76, CampaignState.VariantTagSize, "DS:3a76 \"*SLOC00\"", s => s.SenderCityTag.Bytes)
            .Bytes(0x3a87, CampaignState.VariantTagSize, "DS:3a87 \"*RLOC00\"", s => s.ReceiverCityTag.Bytes)
            .Bytes(0x3a98, CampaignState.VariantTagSize, "DS:3a98 \"*SORG00\"", s => s.SenderOrgTag.Bytes)
            .Bytes(0x3aa9, CampaignState.VariantTagSize, "DS:3aa9 \"*RORG00\"", s => s.ReceiverOrgTag.Bytes)
            .Bytes(0x3ac7, CampaignState.VariantTagSize, "DS:3ac7 \"*FLUF00\"", s => s.FillerTag.Bytes)
            .Bytes(0x3ae9, CampaignState.VariantTagSize, "DS:3ae9 \"*AIDD00\"", s => s.HelperTag.Bytes)
            .Text(0x3ad8, 8, "DS:3ad8 \"*ALRT00\"", s => s.AlertTagTemplate, (s, v) => s.AlertTagTemplate = v)
            .Text(0x3aba, 2, "DS:3aba character 0x80", s => s.StrCodedStart, (s, v) => s.StrCodedStart = v)
            .Text(0x3ac5, 2, "DS:3ac5 character 0x8f", s => s.StrCodedEnd, (s, v) => s.StrCodedEnd = v)
            .Text(0x1daa, 12, "DS:1daa \"Master Plan\"", s => s.StrMasterPlan, (s, v) => s.StrMasterPlan = v)
            .Text(0x1db6, 11, "DS:1db6 \"--SECRET--\"", s => s.StrSecret, (s, v) => s.StrSecret = v)
            .Text(0x1dc1, 9, "DS:1dc1 \"ARRESTED\"", s => s.StrArrested, (s, v) => s.StrArrested = v)
            .Text(0x1dca, 10, "DS:1dca \"IN HIDING\"", s => s.StrInHiding, (s, v) => s.StrInHiding = v)
            .Text(0x1dd4, 8, "DS:1dd4 \" TURNED\"", s => s.StrTurned, (s, v) => s.StrTurned = v)
            .Text(0x1ddc, 15, "DS:1ddc \"Personnel File\"", s => s.StrPersonnelFile, (s, v) => s.StrPersonnelFile = v)
            .Text(0x1deb, 13, "DS:1deb \" Action Team\"", s => s.StrActionTeam, (s, v) => s.StrActionTeam = v)
            .Text(0x1df8, 3, "DS:1df8 \", \"", s => s.StrNameCitySeparator, (s, v) => s.StrNameCitySeparator = v)
            .Text(0x1dfb, 14, "DS:1dfb \"(No activity)\"", s => s.StrNoActivity, (s, v) => s.StrNoActivity = v)
            .Text(0x16e0, 2, "DS:16e0 line end of wrapped texts", s => s.LineEnd, (s, v) => s.LineEnd = v)
            .Text(0x260c, 10, "DS:260c \"prose.dta\"", s => s.ProseFileName, (s, v) => s.ProseFileName = v)
            .Text(0x2616, 6, "DS:2616 \"$RPLC\"", s => s.ProseReplacementToken, (s, v) => s.ProseReplacementToken = v)
            .Text(0x261c, 14, "DS:261c \"Max Remington\"", s => s.HeroName, (s, v) => s.HeroName = v)
            .Text(0x262a, 6, "DS:262a \"$NAME\"", s => s.ProseNameToken, (s, v) => s.ProseNameToken = v)
            .Text(0x2630, 6, "DS:2630 \"$CITY\"", s => s.ProseCityToken, (s, v) => s.ProseCityToken = v)
            .Text(0x2636, 5, "DS:2636 \"$ORG\"", s => s.ProseOrgToken, (s, v) => s.ProseOrgToken = v)
            .Text(0x263b, 4, "DS:263b \"$US\"", s => s.ProseAgencyToken, (s, v) => s.ProseAgencyToken = v)
            .Text(0x31a6, 7, "DS:31a6 \"*C0000\" (cast_clue_code)", s => s.ClueCodeTagTemplate, (s, v) => s.ClueCodeTagTemplate = v)
            .Text(0x31ad, 10, "DS:31ad \"clues.txt\" (cast_clue_code)", s => s.ClueCodeFileName, (s, v) => s.ClueCodeFileName = v)
            .Text(0x31eb, 5, "DS:31eb \"*C00\"", s => s.ClueTagTemplate, (s, v) => s.ClueTagTemplate = v)
            .Text(0x31f0, 7, "DS:31f0 \"*C0000\" (a participant's role)", s => s.RoleClueTagTemplate, (s, v) => s.RoleClueTagTemplate = v)
            .Text(0x31f7, 7, "DS:31f7 \"*C0000\" (a decoy's role)", s => s.DecoyClueTagTemplate, (s, v) => s.DecoyClueTagTemplate = v)
            .Text(0x31fe, 10, "DS:31fe \"clues.txt\" (append_clue_text)", s => s.ClueFileName, (s, v) => s.ClueFileName = v)
            .Int16(0x968c, "DS:968c clue_phrase_index", s => s.CluePhraseIndex, (s, v) => s.CluePhraseIndex = v)
            .Text(0x3cf4, 4, "DS:3cf4 day digits of the clock text", s => s.ClockDayText, (s, v) => s.ClockDayText = v)
            .Text(0x3a46, 47, "DS:3a46 clearance notice", s => s.ClearanceNotice, (s, v) => s.ClearanceNotice = v)
            .Text(0x3a3d, 2, "DS:3a3d line separator", s => s.LineSeparator, (s, v) => s.LineSeparator = v)
            .Char(0xa61e, "DS:a61e GMode", s => s.GMode, (s, v) => s.GMode = v)
            .Bytes(0x151e, 16, "DS:151e photo colour map", s => s.PhotoColorMap)
            .Bytes(0x152e, 16, "DS:152e colour map of the place and clock plate", s => s.PlateColorMap)
            .Bytes(0x14fe, 16, "DS:14fe colour map of a bulletin", s => s.BulletinColorMap)
            .Text(0x1e14, 13, "DS:1e14 bulletin caption", s => s.StrBulletinCaption, (s, v) => s.StrBulletinCaption = v)
            .Bytes(0x5554, 9, "DS:5554 text_key", s => s.MessageTag.Bytes)
            .Text(0x3b03, 9, "DS:3b03 msg_tag_template", s => s.MessageTemplate, (s, v) => s.MessageTemplate = v)
            .Text(0x3afa, 9, "DS:3afa text_dta_name", s => s.TextFileName, (s, v) => s.TextFileName = v)
            .Text(0x3d08, 3, "DS:3d08 \", \"", s => s.StrPlaceSeparator, (s, v) => s.StrPlaceSeparator = v)
            .Int16(0x3d0c, "DS:3d0c status_bar_y", s => s.StatusBarY, (s, v) => s.StatusBarY = v)
            .Bytes(0x2750, 8, "DS:2750 plane_step_dx", s => s.PlaneStepX)
            .Bytes(0x2758, 8, "DS:2758 plane_step_dy", s => s.PlaneStepY)
            .Bytes(0x2760, 9, "DS:2760 plane_heading_by_sign (from its entry [-1][-1])", s => s.PlaneHeadings)
            .Flag(0x789c, "DS:789c CGA", s => s.CGA, (s, v) => s.CGA = v)
            .Text(0x369b, 24, "DS:369b activity report title", s => s.StrActivityReportTitle, (s, v) => s.StrActivityReportTitle = v)
            .Text(0x007e, 22, "DS:007e text in front of ps", s => s.TextBeforePersons, (s, v) => s.TextBeforePersons = v)
            .Text(0x372c, 2, "DS:372c org_list_prefix", s => s.OrgListPrefix, (s, v) => s.OrgListPrefix = v)
            .Text(0x372e, 2, "DS:372e org_list_suffix", s => s.OrgListSuffix, (s, v) => s.OrgListSuffix = v)
            .Text(0x379f, 2, "DS:379f list_item_prefix", s => s.ListItemPrefix, (s, v) => s.ListItemPrefix = v)
            .Text(0x37a1, 2, "DS:37a1 list_item_suffix", s => s.ListItemSuffix, (s, v) => s.ListItemSuffix = v)
            .Text(0x3209, 7, "DS:3209 Dossier.Name", s => s.Dossier.Name, (s, v) => s.Dossier.Name = v)
            .Text(0x3210, 7, "DS:3210 Dossier.Rank", s => s.Dossier.Rank, (s, v) => s.Dossier.Rank = v)
            .Text(0x3217, 7, "DS:3217 Dossier.Org", s => s.Dossier.Org, (s, v) => s.Dossier.Org = v)
            .Text(0x321e, 8, "DS:321e Dossier.City", s => s.Dossier.City, (s, v) => s.Dossier.City = v)
            .Text(0x3226, 10, "DS:3226 Dossier.Recruited", s => s.Dossier.Recruited, (s, v) => s.Dossier.Recruited = v)
            .Text(0x3230, 5, "DS:3230 Dossier.By", s => s.Dossier.By, (s, v) => s.Dossier.By = v)
            .Text(0x3235, 5, "DS:3235 Dossier.In", s => s.Dossier.In, (s, v) => s.Dossier.In = v)
            .Text(0x323a, 2, "DS:323a Dossier.FullStop", s => s.Dossier.FullStop, (s, v) => s.Dossier.FullStop = v)
            .Text(0x323c, 8, "DS:323c Dossier.Role", s => s.Dossier.Role, (s, v) => s.Dossier.Role = v)
            .Text(0x3244, 20, "DS:3244 Dossier.RoleNotInvolved", s => s.Dossier.RoleNotInvolved, (s, v) => s.Dossier.RoleNotInvolved = v)
            .Text(0x3258, 15, "DS:3258 Dossier.RoleUnknown", s => s.Dossier.RoleUnknown, (s, v) => s.Dossier.RoleUnknown = v)
            .Text(0x3267, 15, "DS:3267 Dossier.UnderArrest", s => s.Dossier.UnderArrest, (s, v) => s.Dossier.UnderArrest = v)
            .Text(0x3276, 12, "DS:3276 Dossier.InHiding", s => s.Dossier.InHiding, (s, v) => s.Dossier.InHiding = v)
            .Text(0x3282, 9, "DS:3282 Dossier.Turned", s => s.Dossier.Turned, (s, v) => s.Dossier.Turned = v)
            .Text(0x3290, 22, "DS:3290 Dossier.MessageNotDecoded", s => s.Dossier.MessageNotDecoded, (s, v) => s.Dossier.MessageNotDecoded = v)
            .Text(0x32a6, 8, "DS:32a6 Dossier.More", s => s.Dossier.More, (s, v) => s.Dossier.More = v)
            .Text(0x328c, 2, "DS:328c Dossier.OpenQuote", s => s.Dossier.OpenQuote, (s, v) => s.Dossier.OpenQuote = v)
            .Text(0x328e, 2, "DS:328e Dossier.CloseQuote", s => s.Dossier.CloseQuote, (s, v) => s.Dossier.CloseQuote = v)
            .Text(0x2bd9, 52, "DS:2bd9 Knowledge.Participant", s => s.Knowledge.Participant, (s, v) => s.Knowledge.Participant = v)
            .Text(0x2c0e, 36, "DS:2c0e Knowledge.Identified", s => s.Knowledge.Identified, (s, v) => s.Knowledge.Identified = v)
            .Text(0x2c32, 2, "DS:2c32 Knowledge.LineBreak", s => s.Knowledge.LineBreak, (s, v) => s.Knowledge.LineBreak = v)
            .Text(0x2c34, 34, "DS:2c34 Knowledge.Photograph", s => s.Knowledge.Photograph, (s, v) => s.Knowledge.Photograph = v)
            .Text(0x2c56, 3, "DS:2c56 Knowledge.PhotographEnd", s => s.Knowledge.PhotographEnd, (s, v) => s.Knowledge.PhotographEnd = v)
            .Text(0x2c5a, 22, "DS:2c5a Knowledge.Spotted", s => s.Knowledge.Spotted, (s, v) => s.Knowledge.Spotted = v)
            .Text(0x2c73, 41, "DS:2c73 Knowledge.Member", s => s.Knowledge.Member, (s, v) => s.Knowledge.Member = v)
            .Text(0x2c9e, 52, "DS:2c9e Knowledge.AdditionalInformation", s => s.Knowledge.AdditionalInformation, (s, v) => s.Knowledge.AdditionalInformation = v)
            .Text(0x2cd4, 32, "DS:2cd4 Knowledge.Rank", s => s.Knowledge.Rank, (s, v) => s.Knowledge.Rank = v)
            .Text(0x2cf6, 39, "DS:2cf6 Knowledge.RecruitedWhere", s => s.Knowledge.RecruitedWhere, (s, v) => s.Knowledge.RecruitedWhere = v)
            .Text(0x2d1f, 39, "DS:2d1f Knowledge.RecruitedByWhom", s => s.Knowledge.RecruitedByWhom, (s, v) => s.Knowledge.RecruitedByWhom = v)
            .Text(0x2769, 6, "DS:2769 Knowledge.Quiet", s => s.Knowledge.Quiet, (s, v) => s.Knowledge.Quiet = v)
            .Text(0x3886, 15, "DS:3886 Bulletins.LogFull", s => s.Bulletins.LogFull, (s, v) => s.Bulletins.LogFull = v)
            .Text(0x3b22, 11, "DS:3b22 Bulletins.GoneIntoHidingStart", s => s.Bulletins.GoneIntoHidingStart, (s, v) => s.Bulletins.GoneIntoHidingStart = v)
            .Text(0x3b2d, 24, "DS:3b2d Bulletins.GoneIntoHiding", s => s.Bulletins.GoneIntoHiding, (s, v) => s.Bulletins.GoneIntoHiding = v)
            .Text(0x3b45, 18, "DS:3b45 Bulletins.SurveillanceTeam", s => s.Bulletins.SurveillanceTeam, (s, v) => s.Bulletins.SurveillanceTeam = v)
            .Text(0x3c86, 11, "DS:3c86 Bulletins.OutOfHidingStart", s => s.Bulletins.OutOfHidingStart, (s, v) => s.Bulletins.OutOfHidingStart = v)
            .Text(0x3c91, 26, "DS:3c91 Bulletins.OutOfHiding", s => s.Bulletins.OutOfHiding, (s, v) => s.Bulletins.OutOfHiding = v)
            .Text(0x3b57, 8, "DS:3b57 Bulletins.EndOfCaseSave", s => s.Bulletins.EndOfCaseSave, (s, v) => s.Bulletins.EndOfCaseSave = v)
            .Text(0x3b5f, 20, "DS:3b5f Bulletins.InterpolStart", s => s.Bulletins.InterpolStart, (s, v) => s.Bulletins.InterpolStart = v)
            .Text(0x3b73, 13, "DS:3b73 Bulletins.SpottedIn", s => s.Bulletins.SpottedIn, (s, v) => s.Bulletins.SpottedIn = v)
            .Text(0x3b80, 3, "DS:3b80 Bulletins.Exclamation", s => s.Bulletins.Exclamation, (s, v) => s.Bulletins.Exclamation = v)
            .Text(0x3b83, 9, "DS:3b83 Bulletins.Interpol", s => s.Bulletins.Interpol, (s, v) => s.Bulletins.Interpol = v)
            .Text(0x3b8d, 31, "DS:3b8d Bulletins.MessageTraffic", s => s.Bulletins.MessageTraffic, (s, v) => s.Bulletins.MessageTraffic = v)
            .Text(0x3bac, 7, "DS:3bac Bulletins.From", s => s.Bulletins.From, (s, v) => s.Bulletins.From = v)
            .Text(0x3bb3, 5, "DS:3bb3 Bulletins.To", s => s.Bulletins.To, (s, v) => s.Bulletins.To = v)
            .Text(0x3bb8, 6, "DS:3bb8 Bulletins.TrafficEnd", s => s.Bulletins.TrafficEnd, (s, v) => s.Bulletins.TrafficEnd = v)
            .Text(0x3bbe, 20, "DS:3bbe Bulletins.SatelliteIntercept", s => s.Bulletins.SatelliteIntercept, (s, v) => s.Bulletins.SatelliteIntercept = v)
            .Text(0x3bd2, 15, "DS:3bd2 Bulletins.DoubleAgent", s => s.Bulletins.DoubleAgent, (s, v) => s.Bulletins.DoubleAgent = v)
            .Text(0x3be1, 12, "DS:3be1 Bulletins.WiretapAt", s => s.Bulletins.WiretapAt, (s, v) => s.Bulletins.WiretapAt = v)
            .Text(0x3bed, 2, "DS:3bed Bulletins.Slash", s => s.Bulletins.Slash, (s, v) => s.Bulletins.Slash = v)
            .Text(0x3bef, 10, "DS:3bef Bulletins.Reveals", s => s.Bulletins.Reveals, (s, v) => s.Bulletins.Reveals = v)
            .Text(0x3bf9, 4, "DS:3bf9 Bulletins.He", s => s.Bulletins.He, (s, v) => s.Bulletins.He = v)
            .Text(0x3bfd, 5, "DS:3bfd Bulletins.She", s => s.Bulletins.She, (s, v) => s.Bulletins.She = v)
            .Text(0x3c02, 2, "DS:3c02 Bulletins.Space", s => s.Bulletins.Space, (s, v) => s.Bulletins.Space = v)
            .Text(0x3c04, 2, "DS:3c04 Bulletins.LineBreak", s => s.Bulletins.LineBreak, (s, v) => s.Bulletins.LineBreak = v)
            .Text(0x3c07, 7, "DS:3c07 Bulletins.AirportFrom", s => s.Bulletins.AirportFrom, (s, v) => s.Bulletins.AirportFrom = v)
            .Text(0x3c12, 35, "DS:3c12 Bulletins.ReturnedFrom", s => s.Bulletins.ReturnedFrom, (s, v) => s.Bulletins.ReturnedFrom = v)
            .Text(0x3c35, 3, "DS:3c35 Bulletins.FullStop", s => s.Bulletins.FullStop, (s, v) => s.Bulletins.FullStop = v)
            .Text(0x3c38, 21, "DS:3c38 Bulletins.AirportSurveillance", s => s.Bulletins.AirportSurveillance, (s, v) => s.Bulletins.AirportSurveillance = v)
            .Text(0x3c4e, 30, "DS:3c4e Bulletins.DepartedFrom", s => s.Bulletins.DepartedFrom, (s, v) => s.Bulletins.DepartedFrom = v)
            .Text(0x16d0, 8, "DS:16d0 StrHandOffSave", s => s.StrHandOffSave, (s, v) => s.StrHandOffSave = v)
            .Text(0x16d8, 7, "DS:16d8 StrGoodbyePicture", s => s.StrGoodbyePicture, (s, v) => s.StrGoodbyePicture = v)
            .Text(0x3864, 15, "DS:3864 \"Message Decode\"", s => s.StrMessageDecode, (s, v) => s.StrMessageDecode = v)
            .Text(0x3873, 6, "DS:3873 \"FROM:\"", s => s.StrDecodeFrom, (s, v) => s.StrDecodeFrom = v)
            .Text(0x3879, 4, "DS:3879 \"TO:\"", s => s.StrDecodeTo, (s, v) => s.StrDecodeTo = v)
            .Text(0x387d, 9, "DS:387d \"MESSAGE:\"", s => s.StrDecodeMessage, (s, v) => s.StrDecodeMessage = v)
            .Text(0x2696, 42, "DS:2696 StrExchangeChoices", s => s.StrExchangeChoices, (s, v) => s.StrExchangeChoices = v)
            .Text(0x26c0, 23, "DS:26c0 StrExchangedFor", s => s.StrExchangedFor, (s, v) => s.StrExchangedFor = v)
            .Text(0x26d7, 3, "DS:26d7 StrExchangedEnd", s => s.StrExchangedEnd, (s, v) => s.StrExchangedEnd = v)
            .Text(0x26da, 20, "DS:26da StrTwoDoubleAgents", s => s.StrTwoDoubleAgents, (s, v) => s.StrTwoDoubleAgents = v)
            .Text(0x36cf, 24, "DS:36cf StrWhichOrganization", s => s.StrWhichOrganization, (s, v) => s.StrWhichOrganization = v)
            .Text(0x36e7, 11, "DS:36e7 StrAllies", s => s.StrAllies, (s, v) => s.StrAllies = v)
            .Text(0x36f2, 3, "DS:36f2 StrOrgListLine", s => s.StrOrgListLine, (s, v) => s.StrOrgListLine = v)
            .Text(0x36f5, 5, "DS:36f5 StrNone", s => s.StrNone, (s, v) => s.StrNone = v)
            .Text(0x36fa, 19, "DS:36fa StrKnownLocations", s => s.StrKnownLocations, (s, v) => s.StrKnownLocations = v)
            .Text(0x370d, 5, "DS:370d StrBuildingIn", s => s.StrBuildingIn, (s, v) => s.StrBuildingIn = v)
            .Text(0x3712, 3, "DS:3712 StrBuildingLine", s => s.StrBuildingLine, (s, v) => s.StrBuildingLine = v)
            .Text(0x3715, 15, "DS:3715 StrAssociates", s => s.StrAssociates, (s, v) => s.StrAssociates = v)
            .Text(0x37a3, 10, "DS:37a3 StrDocuments", s => s.StrDocuments, (s, v) => s.StrDocuments = v)
            .Text(0x37ad, 19, "DS:37ad StrSelectDocument", s => s.StrSelectDocument, (s, v) => s.StrSelectDocument = v)
            .Text(0x37c0, 14, "DS:37c0 StrMasterPlanLine", s => s.StrMasterPlanLine, (s, v) => s.StrMasterPlanLine = v)
            .Text(0x37ce, 17, "DS:37ce StrPersonnelFileLine", s => s.StrPersonnelFileLine, (s, v) => s.StrPersonnelFileLine = v)
            .Text(0x37df, 7, "DS:37df StrNoDocuments", s => s.StrNoDocuments, (s, v) => s.StrNoDocuments = v)
            .Text(0x16e8, 9, "DS:16e8 building kind 0", s => s.BuildingKinds[0], (s, v) => s.BuildingKinds[0] = v)
            .Text(0x16f1, 7, "DS:16f1 building kind 1", s => s.BuildingKinds[1], (s, v) => s.BuildingKinds[1] = v)
            .Text(0x16f8, 11, "DS:16f8 building kind 2", s => s.BuildingKinds[2], (s, v) => s.BuildingKinds[2] = v)
            .Text(0x1703, 12, "DS:1703 building kind 3", s => s.BuildingKinds[3], (s, v) => s.BuildingKinds[3] = v)
            .Text(0x170f, 8, "DS:170f building kind 4", s => s.BuildingKinds[4], (s, v) => s.BuildingKinds[4] = v)
            .Text(0x37e6, 14, "DS:37e6 StrSuspectFiles", s => s.StrSuspectFiles, (s, v) => s.StrSuspectFiles = v)
            .Text(0x37f4, 2, "DS:37f4 StrSuspectListStart", s => s.StrSuspectListStart, (s, v) => s.StrSuspectListStart = v)
            .Text(0x37f6, 9, "DS:37f6 StrSuspectOfThe", s => s.StrSuspectOfThe, (s, v) => s.StrSuspectOfThe = v)
            .Text(0x37ff, 5, "DS:37ff StrSuspectIn", s => s.StrSuspectIn, (s, v) => s.StrSuspectIn = v)
            .Text(0x3804, 4, "DS:3804 StrSuspectLineEnd", s => s.StrSuspectLineEnd, (s, v) => s.StrSuspectLineEnd = v)
            .Text(0x3808, 3, "DS:3808 StrSuspectRoleStart", s => s.StrSuspectRoleStart, (s, v) => s.StrSuspectRoleStart = v)
            .Text(0x380b, 2, "DS:380b StrSuspectRoleEnd", s => s.StrSuspectRoleEnd, (s, v) => s.StrSuspectRoleEnd = v)
            .Text(0x380d, 11, "DS:380d StrSuspectArrested", s => s.StrSuspectArrested, (s, v) => s.StrSuspectArrested = v)
            .Text(0x3818, 9, "DS:3818 StrSuspectHiding", s => s.StrSuspectHiding, (s, v) => s.StrSuspectHiding = v)
            .Text(0x3821, 9, "DS:3821 StrSuspectTurned", s => s.StrSuspectTurned, (s, v) => s.StrSuspectTurned = v)
            .Text(0x382a, 13, "DS:382a StrSuspectInCustody", s => s.StrSuspectInCustody, (s, v) => s.StrSuspectInCustody = v)
            .Text(0x3837, 3, "DS:3837 StrSuspectNotesEnd", s => s.StrSuspectNotesEnd, (s, v) => s.StrSuspectNotesEnd = v)
            .Text(0x383a, 7, "DS:383a StrNoSuspects", s => s.StrNoSuspects, (s, v) => s.StrNoSuspects = v)
            .Text(0x3841, 15, "DS:3841 StrCodedMessages", s => s.StrCodedMessages, (s, v) => s.StrCodedMessages = v)
            .Text(0x3850, 2, "DS:3850 StrCodedListStart", s => s.StrCodedListStart, (s, v) => s.StrCodedListStart = v)
            .Text(0x3852, 2, "DS:3852 StrCodedAfterName", s => s.StrCodedAfterName, (s, v) => s.StrCodedAfterName = v)
            .Text(0x3854, 6, "DS:3854 StrCodedEllipsis", s => s.StrCodedEllipsis, (s, v) => s.StrCodedEllipsis = v)
            .Text(0x385a, 3, "DS:385a StrCodedLineEnd", s => s.StrCodedLineEnd, (s, v) => s.StrCodedLineEnd = v)
            .Text(0x385d, 7, "DS:385d StrNoCodedMessages", s => s.StrNoCodedMessages, (s, v) => s.StrNoCodedMessages = v)
            .Int16(0xa624, "DS:a624 entry_mode", s => s.EntryMode, (s, v) => s.EntryMode = v)
            .Text(0x36b3, 9, "DS:36b3 StrWireTaps", s => s.StrWireTaps, (s, v) => s.StrWireTaps = v)
            .Text(0x36bd, 2, "DS:36bd StrTapSlash", s => s.StrTapSlash, (s, v) => s.StrTapSlash = v)
            .Text(0x36bf, 6, "DS:36bf StrTapDots", s => s.StrTapDots, (s, v) => s.StrTapDots = v)
            .Text(0x36c5, 3, "DS:36c5 StrTapPercent", s => s.StrTapPercent, (s, v) => s.StrTapPercent = v)
            .Text(0x36c8, 7, "DS:36c8 StrNoWireTaps", s => s.StrNoWireTaps, (s, v) => s.StrNoWireTaps = v)
            .Bytes(0x5524, 16, "DS:5524 percent_text", s => s.PercentText.Bytes)
            .Text(0x263f, 13, "DS:263f StrSurrenderScene", s => s.StrSurrenderScene, (s, v) => s.StrSurrenderScene = v)
            .Text(0x2655, 12, "DS:2655 StrWarningScene", s => s.StrWarningScene, (s, v) => s.StrWarningScene = v)
            .Text(0x2661, 14, "DS:2661 StrJailBreakIn", s => s.StrJailBreakIn, (s, v) => s.StrJailBreakIn = v)
            .Text(0x266f, 3, "DS:266f StrJailBreakCityEnd", s => s.StrJailBreakCityEnd, (s, v) => s.StrJailBreakCityEnd = v)
            .Text(0x2672, 14, "DS:2672 StrJailBreakAgentsFree", s => s.StrJailBreakAgentsFree, (s, v) => s.StrJailBreakAgentsFree = v)
            .Text(0x2680, 3, "DS:2680 StrJailBreakEnd", s => s.StrJailBreakEnd, (s, v) => s.StrJailBreakEnd = v)
            .Text(0x2683, 11, "DS:2683 StrEscapeScene", s => s.StrEscapeScene, (s, v) => s.StrEscapeScene = v)
            .Bytes(0x264c, 9, "DS:264c \"*grilled\"", s => s.GrilledProseKey.Bytes)
            .Bytes(0x268e, 8, "DS:268e \"*escape\"", s => s.EscapeProseKey.Bytes)
            .Bytes(0x15ae, 16, "DS:15ae colour map of the capture scene", s => s.SurrenderColorMap)
            .Bytes(0x167e, 16, "DS:167e colour map of the jail-break scene", s => s.WarningColorMap)
            .Bytes(0x159e, 16, "DS:159e colour map of the escape scene", s => s.EscapeColorMap)
            .Int16(0x54ec, "DS:54ec leaving_building", s => s.LeavingBuilding, (s, v) => s.LeavingBuilding = v)
            .Text(0x245a, 13, "DS:245a StrInterrogationScene", s => s.StrInterrogationScene, (s, v) => s.StrInterrogationScene = v)
            .Text(0x2467, 12, "DS:2467 StrYouAccuse", s => s.StrYouAccuse, (s, v) => s.StrYouAccuse = v)
            .Text(0x2473, 59, "DS:2473 StrAccuseRoles", s => s.StrAccuseRoles, (s, v) => s.StrAccuseRoles = v)
            .Text(0x24ae, 4, "DS:24ae StrAccuseRoleEnd", s => s.StrAccuseRoleEnd, (s, v) => s.StrAccuseRoleEnd = v)
            .Text(0x24b2, 30, "DS:24b2 StrNoEvidence", s => s.StrNoEvidence, (s, v) => s.StrNoEvidence = v)
            .Text(0x24d0, 5, "DS:24d0 StrNoEvidenceHe", s => s.StrNoEvidenceHe, (s, v) => s.StrNoEvidenceHe = v)
            .Text(0x24d5, 6, "DS:24d5 StrNoEvidenceShe", s => s.StrNoEvidenceShe, (s, v) => s.StrNoEvidenceShe = v)
            .Text(0x24db, 28, "DS:24db StrReleased", s => s.StrReleased, (s, v) => s.StrReleased = v)
            .Text(0x2510, 13, "DS:2510 StrYouAsk", s => s.StrYouAsk, (s, v) => s.StrYouAsk = v)
            .Text(0x251d, 50, "DS:251d StrConfesses", s => s.StrConfesses, (s, v) => s.StrConfesses = v)
            .Text(0x254f, 61, "DS:254f StrDoesNotRemember", s => s.StrDoesNotRemember, (s, v) => s.StrDoesNotRemember = v)
            .Text(0x258c, 67, "DS:258c StrMastermindBreaks", s => s.StrMastermindBreaks, (s, v) => s.StrMastermindBreaks = v)
            .Text(0x25cf, 11, "DS:25cf StrJailScene", s => s.StrJailScene, (s, v) => s.StrJailScene = v)
            .Text(0x25da, 30, "DS:25da StrTakenToJail", s => s.StrTakenToJail, (s, v) => s.StrTakenToJail = v)
            .Text(0x25f8, 13, "DS:25f8 StrBriefingScene", s => s.StrBriefingScene, (s, v) => s.StrBriefingScene = v)
            .Bytes(0x24f7, 8, "DS:24f7 \"*inter1\"", s => s.ArrestProseKey.Bytes)
            .Bytes(0x24ff, 9, "DS:24ff \"*doublea\"", s => s.DoubleAgentProseKey.Bytes)
            .Bytes(0x2508, 8, "DS:2508 \"*inter2\"", s => s.QuestionProseKey.Bytes)
            .Bytes(0x2605, 7, "DS:2605 \"*nice0\"", s => s.CaptureProseKey.Bytes)
            .Bytes(0x161e, 16, "DS:161e colour map of the interrogation", s => s.InterrogationColorMap)
            .Bytes(0x166e, 16, "DS:166e colour map of the jail", s => s.JailColorMap)
            .Bytes(0x168e, 16, "DS:168e colour map of the chief's scene", s => s.BriefingColorMap)
            .Bytes(0x2b9e, 32, "DS:2b9e bit_count_table", s => s.BitCount)
            .Text(0x16e2, 6, "DS:16e2 StrEllipsis", s => s.StrEllipsis, (s, v) => s.StrEllipsis = v)
            .Text(0x3143, 5, "DS:3143 StrClueTitle", s => s.StrClueTitle, (s, v) => s.StrClueTitle = v)
            .Text(0x3148, 10, "DS:3148 StrClueSource", s => s.StrClueSource, (s, v) => s.StrClueSource = v)
            .Text(0x3152, 2, "DS:3152 StrClueSlash", s => s.StrClueSlash, (s, v) => s.StrClueSlash = v)
            .Text(0x3154, 2, "DS:3154 StrClueSourceEnd", s => s.StrClueSourceEnd, (s, v) => s.StrClueSourceEnd = v)
            .Text(0x3156, 11, "DS:3156 StrClueMethod", s => s.StrClueMethod, (s, v) => s.StrClueMethod = v)
            .Text(0x3161, 16, "DS:3161 StrRelatedClues", s => s.StrRelatedClues, (s, v) => s.StrRelatedClues = v)
            .Text(0x3171, 2, "DS:3171 StrRelatedClueEnd", s => s.StrRelatedClueEnd, (s, v) => s.StrRelatedClueEnd = v)
            .Text(0x3173, 8, "DS:3173 StrNoRelatedClues", s => s.StrNoRelatedClues, (s, v) => s.StrNoRelatedClues = v)
            .Text(0x31c2, 2, "DS:31c2 StrCluePeriod", s => s.StrCluePeriod, (s, v) => s.StrCluePeriod = v)
            .Int16(0x83c4, "DS:83c4 last_hint", s => s.LastHint, (s, v) => s.LastHint = v)
            .Text(0x3730, 16, "DS:3730 StrWhichCity", s => s.StrWhichCity, (s, v) => s.StrWhichCity = v)
            .Text(0x3740, 19, "DS:3740 StrCityKnownLocations", s => s.StrCityKnownLocations, (s, v) => s.StrCityKnownLocations = v)
            .Text(0x3753, 18, "DS:3753 StrUnknownBuilding", s => s.StrUnknownBuilding, (s, v) => s.StrUnknownBuilding = v)
            .Text(0x3765, 3, "DS:3765 StrCityListLine", s => s.StrCityListLine, (s, v) => s.StrCityListLine = v)
            .Text(0x3768, 6, "DS:3768 StrCityNone", s => s.StrCityNone, (s, v) => s.StrCityNone = v)
            .Text(0x376e, 12, "DS:376e StrCitySuspects", s => s.StrCitySuspects, (s, v) => s.StrCitySuspects = v)
            .Text(0x377a, 2, "DS:377a StrCitySuspectSlash", s => s.StrCitySuspectSlash, (s, v) => s.StrCitySuspectSlash = v)
            .Text(0x377c, 7, "DS:377c StrCitySuspectRole", s => s.StrCitySuspectRole, (s, v) => s.StrCitySuspectRole = v)
            .Text(0x3783, 4, "DS:3783 StrCitySuspectLineEnd", s => s.StrCitySuspectLineEnd, (s, v) => s.StrCitySuspectLineEnd = v)
            .Text(0x378d, 8, "DS:378d StrCityClues", s => s.StrCityClues, (s, v) => s.StrCityClues = v)
            .Text(0x3795, 2, "DS:3795 StrCityClueStart", s => s.StrCityClueStart, (s, v) => s.StrCityClueStart = v)
            .Text(0x3797, 2, "DS:3797 StrCityClueEnd", s => s.StrCityClueEnd, (s, v) => s.StrCityClueEnd = v)
            .Text(0x3d0e, 9, "DS:3d0e StrCluesArrested", s => s.StrCluesArrested, (s, v) => s.StrCluesArrested = v)
            .Text(0x3d17, 7, "DS:3d17 StrCluesTurned", s => s.StrCluesTurned, (s, v) => s.StrCluesTurned = v)
            .Text(0x3d1e, 6, "DS:3d1e StrCluesHeading", s => s.StrCluesHeading, (s, v) => s.StrCluesHeading = v)
            .Text(0x3d2e, 5, "DS:3d2e StrClueDetailTitle", s => s.StrClueDetailTitle, (s, v) => s.StrClueDetailTitle = v)
            .Text(0x3d33, 10, "DS:3d33 StrClueDetailSource", s => s.StrClueDetailSource, (s, v) => s.StrClueDetailSource = v)
            .Text(0x3d3d, 2, "DS:3d3d StrClueDetailSlash", s => s.StrClueDetailSlash, (s, v) => s.StrClueDetailSlash = v)
            .Text(0x3d3f, 2, "DS:3d3f StrClueDetailSourceEnd", s => s.StrClueDetailSourceEnd, (s, v) => s.StrClueDetailSourceEnd = v)
            .Text(0x3d41, 2, "DS:3d41 StrClueDetailEnd", s => s.StrClueDetailEnd, (s, v) => s.StrClueDetailEnd = v)
            .Text(0x3d43, 9, "DS:3d43 StrClueDetailArrested", s => s.StrClueDetailArrested, (s, v) => s.StrClueDetailArrested = v)
            .Text(0x3d4c, 10, "DS:3d4c StrClueDetailInHiding", s => s.StrClueDetailInHiding, (s, v) => s.StrClueDetailInHiding = v)
            .Text(0x3d56, 8, "DS:3d56 StrClueDetailTurned", s => s.StrClueDetailTurned, (s, v) => s.StrClueDetailTurned = v)
            .Int16Array(0x3d24, 4, "DS:3d24 row_colours", s => s.RowColours)
            .Int16(0x3d2c, "DS:3d2c clue_list_selection", s => s.ClueListSelection, (s, v) => s.ClueListSelection = v)
            .Text(0x23fe, 13, "DS:23fe StrInfiltrationBriefingScene", s => s.StrInfiltrationBriefingScene, (s, v) => s.StrInfiltrationBriefingScene = v)
            .Text(0x240b, 29, "DS:240b StrFoundObject", s => s.StrFoundObject, (s, v) => s.StrFoundObject = v)
            .Text(0x2428, 50, "DS:2428 StrFoundObjectEnd", s => s.StrFoundObjectEnd, (s, v) => s.StrFoundObjectEnd = v)
            .Int16(0x93ee, "DS:93ee pic", s => s.Pic, (s, v) => s.Pic = v)
            .Text(0x32af, 12, "DS:32af received message", s => s.MessageText.ReceivedFrom, (s, v) => s.MessageText.ReceivedFrom = v)
            .Text(0x32bb, 12, "DS:32bb sent message", s => s.MessageText.SentTo, (s, v) => s.MessageText.SentTo = v)
            .Text(0x32c7, 2, "DS:32c7 \" \"", s => s.MessageText.BeforeName, (s, v) => s.MessageText.BeforeName = v)
            .Text(0x32c9, 9, "DS:32c9 \" someone\"", s => s.MessageText.Someone, (s, v) => s.MessageText.Someone = v)
            .Text(0x32d2, 9, "DS:32d2 \" of the \"", s => s.MessageText.OfThe, (s, v) => s.MessageText.OfThe = v)
            .Text(0x32db, 5, "DS:32db \" in \"", s => s.MessageText.In, (s, v) => s.MessageText.In = v)
            .Text(0x32e0, 5, "DS:32e0 \" on \"", s => s.MessageText.On, (s, v) => s.MessageText.On = v)
            .Text(0x32e5, 2, "DS:32e5 \" \"", s => s.MessageText.BeforeDay, (s, v) => s.MessageText.BeforeDay = v)
            .Text(0x32e7, 2, "DS:32e7 \".\"", s => s.MessageText.End, (s, v) => s.MessageText.End = v)

            // ---- the screen ----
            .Part(0x1456, "DS:1456 RpPicX", RasterPortLayout.Layout, s => s.RpPicX)
            .Pointer<RasterPort>(0x1468, "DS:1468 RpPic", s => s.RpPic, (s, v) => s.RpPic = v, (0x1456, s => s.RpPicX), (0x146a, s => s.RpBackX), (0x435c, s => s.Rp1X))
            .Part(0x146a, "DS:146a RpBackX", RasterPortLayout.Layout, s => s.RpBackX)
            .Pointer<RasterPort>(0x147c, "DS:147c RpBack", s => s.RpBack, (s, v) => s.RpBack = v, (0x146a, s => s.RpBackX), (0x1456, s => s.RpPicX), (0x435c, s => s.Rp1X))
            .Part(0x435c, "DS:435c Rp1X", RasterPortLayout.Layout, s => s.Rp1X)
            .Pointer<RasterPort>(0x436e, "DS:436e Rp1", s => s.Rp1, (s, v) => s.Rp1 = v, (0x435c, s => s.Rp1X), (0x146a, s => s.RpBackX), (0x1456, s => s.RpPicX))
            .ImageArray(0x969c, CampaignState.SpriteCount, "DS:969c sprite", s => s.Sprite, s => s.Images)

            // ---- menus ----
            .Int16(0x969a, "DS:969a Mindex", s => s.Mindex, (s, v) => s.Mindex = v)
            .Int16Array(0xa9b0, CampaignState.MenuRows, "DS:a9b0 Mitem", s => s.Mitem);
        TextTable(layout, 0x2bbe, "DS:2bbe month_names", MonthTexts, s => s.MonthNames);
        for (var row = 0; row < CampaignState.FirstNameRows; row++)
        {
            var sex = row;
            TextTable(layout, 0x1c2a + 2 * CampaignState.NamesPerRow * sex, $"DS:1c2a first_name_table[{sex}]",
                FirstNameTexts[(CampaignState.NamesPerRow * sex)..(CampaignState.NamesPerRow * (sex + 1))], s => s.FirstNames[sex]);
        }
        TextTable(layout, 0x1d2a, "DS:1d2a surname_table", SurnameTexts, s => s.Surnames);
        TextTable(layout, 0x3a10, "DS:3a10 text_var_names",
            [(0x38ee, 8), (0x38f6, 8), (0x38fe, 8), (0x3906, 8), (0x390e, 8), (0x3916, 8), (0x391e, 8)], s => s.VariableNames);
        BufferTable(layout, 0x3a1e, "DS:3a1e text_var_values", [0x3926, 0x395a, 0x3974, 0x398e, 0x39a8, 0x39c2, 0x39dc], CampaignState.VariableValueSizes, s => s.VariableValues);
        TextTable(layout, 0x2b3e, "DS:2b3e activity_phrase_table", ActivityPhraseTexts, s => s.ActivityPhrases);
        TextTable(layout, 0x317c, "DS:317c clue_phrase_table", ClueSourceTexts, s => s.ClueSources);
        TextTable(layout, 0x31b8, "DS:31b8 clue_var_names", [(0x318a, 5), (0x318f, 6), (0x3195, 5), (0x319a, 6), (0x31a0, 6)], s => s.ClueVariableNames);
        TextTable(layout, 0x3022, "DS:3022 rank_name_table",
            [(0x2d48, 8), (0x2d50, 10), (0x2d5a, 11), (0x2d65, 6), (0x2d6b, 10), (0x2d75, 14), (0x2d83, 13), (0x2d90, 11)], s => s.Dossier.RankNames);
        for (var level = 0; level < KnowledgeWords.AlertLevels; level++)
        {
            var index = level;
            var (address, length) = AlertTexts[index];
            layout.Text(address, length, $"DS:{address:x4} alertness {index}", s => s.Knowledge.Alertness[index], (s, v) => s.Knowledge.Alertness[index] = v);
        }
        for (var row = 0; row < CampaignState.ClueLabelRows; row++)
        {
            var kind = row;
            TextTable(layout, 0x3042 + 2 * CampaignState.ClueLabelsPerRow * kind, $"DS:3042 clue_label_table[{kind}]",
                ClueLabelTexts[(CampaignState.ClueLabelsPerRow * kind)..(CampaignState.ClueLabelsPerRow * (kind + 1))], s => s.ClueLabels[kind]);
        }
        // the variables of the routines every part has: message box, animation, disk prompt, joystick (picface is
        // the box's backdrop code, there)
        LibraryLayout.AddGame(layout);
        // the texts of the library's screens about a case (the chronology)
        DossierLayout.AddGame(layout, s => s.DossierTexts);
        // the rest of what a save carries, and the save routine's own variables
        layout
            .Text(0x41a0, 8, "DS:41a0 \"env.sve\"", s => s.StrEnvSve, (s, v) => s.StrEnvSve = v)
            .Int16(0x55c2, "DS:55c2 save_mem_offset", s => s.SaveMemOffset, (s, v) => s.SaveMemOffset = v)
            .Int16(0x55c4, "DS:55c4 save_handle", s => s.SaveHandle, (s, v) => s.SaveHandle = v)
            .Int16(0x8edc, "DS:8edc TheCity1", s => s.TheCity1, (s, v) => s.TheCity1 = v)
            .Int16(0x8ede, "DS:8ede TheCity2", s => s.TheCity2, (s, v) => s.TheCity2 = v)
            .Int16(0x968a, "DS:968a TheSeed", s => s.TheSeed, (s, v) => s.TheSeed = v)
            .Int16(0x9694, "DS:9694 TheOrg1", s => s.TheOrg1, (s, v) => s.TheOrg1 = v)
            .Int16(0x9698, "DS:9698 TheOrg2", s => s.TheOrg2, (s, v) => s.TheOrg2 = v)
            .Int16(0x9fb0, "DS:9fb0 skills", s => s.Skills, (s, v) => s.Skills = v)
            .Int16(0xa668, "DS:a668 InterPol", s => s.InterPol, (s, v) => s.InterPol = v)
            .Int16(0xa86e, "DS:a86e MacGuffin", s => s.MacGuffin, (s, v) => s.MacGuffin = v);
        // the animation player's variables (the library's)
        PanEngineLayout.AddGame(layout, s => s.Pan);
        AddPlaces(layout);
        AddBuildings(layout);
        AddSaveScreens(layout);
        return layout;
    }

    /// <summary>The variables of the campaign's walk: the city screen, its picture, the flight map, the part's start and the loader.</summary>
    private static void AddPlaces(StateLayout<CampaignState> layout) => layout
            .Int16(0xa3e8, "DS:a3e8 Begun", s => s.Begun, (s, v) => s.Begun = v)
            .Int16(0x9686, "DS:9686 missions played", s => s.MissionsPlayed, (s, v) => s.MissionsPlayed = v)
            .Int16(0x8ed6, "DS:8ed6 picture_extent_x", s => s.PictureExtentX, (s, v) => s.PictureExtentX = v)
            .Int16(0x8eda, "DS:8eda picture_extent_y", s => s.PictureExtentY, (s, v) => s.PictureExtentY = v)
            .Int16(0x5510, "DS:5510 cached_picture_width", s => s.CachedPictureWidth, (s, v) => s.CachedPictureWidth = v)
            .Int16(0x5512, "DS:5512 cached_picture_height", s => s.CachedPictureHeight, (s, v) => s.CachedPictureHeight = v)
            .Bytes(0x169e, 16, "DS:169e colour map of the break-out scene", s => s.BustOutColorMap)
            .Bytes(0x163e, 16, "DS:163e colour map of the wounded agent", s => s.WoundedColorMap)
            .Text(0x0076, 8, "DS:0076 CityWords.HandOffLoad", s => s.CityWords.HandOffLoad, (s, v) => s.CityWords.HandOffLoad = v)
            .Text(0x206e, 12, "DS:206e CityWords.WarningScene", s => s.CityWords.WarningScene, (s, v) => s.CityWords.WarningScene = v)
            .Text(0x207a, 14, "DS:207a CityWords.JailBreakIn", s => s.CityWords.JailBreakIn, (s, v) => s.CityWords.JailBreakIn = v)
            .Text(0x2088, 3, "DS:2088 CityWords.JailBreakCityEnd", s => s.CityWords.JailBreakCityEnd, (s, v) => s.CityWords.JailBreakCityEnd = v)
            .Text(0x208b, 28, "DS:208b CityWords.JailBreakAgentsFree", s => s.CityWords.JailBreakAgentsFree, (s, v) => s.CityWords.JailBreakAgentsFree = v)
            .Text(0x20a7, 3, "DS:20a7 CityWords.JailBreakEnd", s => s.CityWords.JailBreakEnd, (s, v) => s.CityWords.JailBreakEnd = v)
            .Text(0x20aa, 12, "DS:20aa CityWords.BustOutScene", s => s.CityWords.BustOutScene, (s, v) => s.CityWords.BustOutScene = v)
            .Text(0x20b6, 11, "DS:20b6 CityWords.Escapes", s => s.CityWords.Escapes, (s, v) => s.CityWords.Escapes = v)
            .Text(0x20c1, 24, "DS:20c1 CityWords.EscapeFoiled", s => s.CityWords.EscapeFoiled, (s, v) => s.CityWords.EscapeFoiled = v)
            .Text(0x20d9, 46, "DS:20d9 CityWords.CarTracerBuilding", s => s.CityWords.CarTracerBuilding, (s, v) => s.CityWords.CarTracerBuilding = v)
            .Text(0x210f, 12, "DS:210f CityWords.WoundedScene", s => s.CityWords.WoundedScene, (s, v) => s.CityWords.WoundedScene = v)
            .Text(0x211b, 43, "DS:211b CityWords.RecoverFromWounds", s => s.CityWords.RecoverFromWounds, (s, v) => s.CityWords.RecoverFromWounds = v)
            .Text(0x215c, 12, "DS:215c CityWords.YouAreIn", s => s.CityWords.YouAreIn, (s, v) => s.CityWords.YouAreIn = v)
            .Text(0x2168, 19, "DS:2168 CityWords.DoYouGoTo", s => s.CityWords.DoYouGoTo, (s, v) => s.CityWords.DoYouGoTo = v)
            .Text(0x217b, 15, "DS:217b CityWords.TheAirport", s => s.CityWords.TheAirport, (s, v) => s.CityWords.TheAirport = v)
            .Text(0x218a, 15, "DS:218a CityWords.YourHotel", s => s.CityWords.YourHotel, (s, v) => s.CityWords.YourHotel = v)
            .Text(0x2199, 17, "DS:2199 CityWords.UnknownBuilding", s => s.CityWords.UnknownBuilding, (s, v) => s.CityWords.UnknownBuilding = v)
            .Text(0x21aa, 3, "DS:21aa CityWords.BuildingLineEnd", s => s.CityWords.BuildingLineEnd, (s, v) => s.CityWords.BuildingLineEnd = v)
            .Text(0x21ad, 3, "DS:21ad CityWords.ContactLineEnd", s => s.CityWords.ContactLineEnd, (s, v) => s.CityWords.ContactLineEnd = v)
            .Text(0x21b0, 12, "DS:21b0 CityWords.CheckData", s => s.CityWords.CheckData, (s, v) => s.CityWords.CheckData = v)
            .Text(0x26ee, 11, "DS:26ee CityWords.EuropeMap", s => s.CityWords.EuropeMap, (s, v) => s.CityWords.EuropeMap = v)
            .Text(0x26f9, 11, "DS:26f9 CityWords.AfricaMap", s => s.CityWords.AfricaMap, (s, v) => s.CityWords.AfricaMap = v)
            .Text(0x2704, 12, "DS:2704 CityWords.CentralAmericaMap", s => s.CityWords.CentralAmericaMap, (s, v) => s.CityWords.CentralAmericaMap = v)
            .Text(0x2710, 11, "DS:2710 CityWords.YoureIn", s => s.CityWords.YoureIn, (s, v) => s.CityWords.YoureIn = v)
            .Text(0x271b, 23, "DS:271b CityWords.DoYouTravelTo", s => s.CityWords.DoYouTravelTo, (s, v) => s.CityWords.DoYouTravelTo = v)
            .Text(0x2732, 12, "DS:2732 CityWords.StayHere", s => s.CityWords.StayHere, (s, v) => s.CityWords.StayHere = v)
            .Text(0x273e, 2, "DS:273e CityWords.CityIndent", s => s.CityWords.CityIndent, (s, v) => s.CityWords.CityIndent = v)
            .Text(0x2740, 2, "DS:2740 CityWords.CityLineEnd", s => s.CityWords.CityLineEnd, (s, v) => s.CityWords.CityLineEnd = v)
            .Text(0x2742, 13, "DS:2742 CityWords.TravelCheckData", s => s.CityWords.TravelCheckData, (s, v) => s.CityWords.TravelCheckData = v)
            .Text(0x2854, 5, "DS:2854 CityWords.PictureExtension", s => s.CityWords.PictureExtension, (s, v) => s.CityWords.PictureExtension = v)
            .Text(0x2859, 3, "DS:2859 CityWords.CountrySeparator", s => s.CityWords.CountrySeparator, (s, v) => s.CityWords.CountrySeparator = v)
            .Text(0x285c, 11, "DS:285c CityWords.CityArchive", s => s.CityWords.CityArchive, (s, v) => s.CityWords.CityArchive = v)
            .Text(0x32ea, 10, "DS:32ea CityWords.HotelPicture", s => s.CityWords.HotelPicture, (s, v) => s.CityWords.HotelPicture = v)
            .Text(0x32f4, 115, "DS:32f4 CityWords.HotelMenu", s => s.CityWords.HotelMenu, (s, v) => s.CityWords.HotelMenu = v)
            .Text(0x336f, 68, "DS:336f CityWords.DataFilesMenuStart", s => s.CityWords.DataFilesMenuStart, (s, v) => s.CityWords.DataFilesMenuStart = v)
            .Text(0x33b3, 59, "DS:33b3 CityWords.DataFilesMenuEnd", s => s.CityWords.DataFilesMenuEnd, (s, v) => s.CityWords.DataFilesMenuEnd = v)
            .Text(0x41a8, 8, "DS:41a8 CityWords.HandOffName", s => s.CityWords.HandOffName, (s, v) => s.CityWords.HandOffName = v)
            .Text(0x41b0, 13, "DS:41b0 CityWords.FileError", s => s.CityWords.FileError, (s, v) => s.CityWords.FileError = v)
            .Text(0x41bd, 2, "DS:41bd CityWords.FileErrorEnd", s => s.CityWords.FileErrorEnd, (s, v) => s.CityWords.FileErrorEnd = v)
            .Bytes(0x2107, 8, "DS:2107 CityWords.CarCaptureProseKey", s => s.CityWords.CarCaptureProseKey.Bytes)
            .Bytes(0x2146, 11, "DS:2146 CityWords.AmbushLostProseKey", s => s.CityWords.AmbushLostProseKey.Bytes)
            .Bytes(0x2151, 11, "DS:2151 CityWords.AmbushWonProseKey", s => s.CityWords.AmbushWonProseKey.Bytes)
            .Bytes(0x21bc, 10, "DS:21bc CityWords.AmbushProseKey", s => s.CityWords.AmbushProseKey.Bytes)
            .Bytes(0x3367, 8, "DS:3367 CityWords.LoungeProseKey", s => s.CityWords.LoungeProseKey.Bytes)
            .Text(0x27a6, 13, "DS:27a6 Briefing.BriefingScene", s => s.Briefing.BriefingScene, (s, v) => s.Briefing.BriefingScene = v)
            .Text(0x27b3, 24, "DS:27b3 Briefing.RestIsUpToYou", s => s.Briefing.RestIsUpToYou, (s, v) => s.Briefing.RestIsUpToYou = v)
            .Text(0x27cb, 81, "DS:27cb Briefing.ResearchAssistant", s => s.Briefing.ResearchAssistant, (s, v) => s.Briefing.ResearchAssistant = v)
            .Text(0x281c, 56, "DS:281c Briefing.PresidentMessage", s => s.Briefing.PresidentMessage, (s, v) => s.Briefing.PresidentMessage = v);

    /// <summary>The variables of the building menus: their texts, the CIA building's, the colour maps of their scenes.</summary>
    private static void AddBuildings(StateLayout<CampaignState> layout)
    {
        layout
            .Bytes(0x54dc, 16, "DS:54dc building_pan_name", s => s.BuildingPanName.Bytes)
            .Int16(0x54ee, "DS:54ee closeness_percent", s => s.ClosenessPercent, (s, v) => s.ClosenessPercent = v)
            .Bytes(0x155e, 16, "DS:155e colour map of the CIA building", s => s.HeadquartersColorMap)
            .Bytes(0x156e, 16, "DS:156e colour map of the intelligence section", s => s.IntelligenceColorMap)
            .Bytes(0x157e, 16, "DS:157e colour map of the crypto branch", s => s.CryptoColorMap)
            .Bytes(0x158e, 16, "DS:158e colour map of the data section", s => s.DataSectionColorMap)
            .Bytes(0x15be, 16, "DS:15be colour map of the stakeout", s => s.BinocularsColorMap)
            .Bytes(0x162e, 16, "DS:162e colour map of the chief's office", s => s.ChiefColorMap)
            .Bytes(0x164e, 16, "DS:164e colour map of being followed", s => s.FollowedColorMap)
            .Bytes(0x165e, 16, "DS:165e colour map of the car tracer", s => s.TraceColorMap)
            .Text(0x21c6, 16, "DS:21c6 BuildingWords.YouAreAt", s => s.BuildingWords.YouAreAt, (s, v) => s.BuildingWords.YouAreAt = v)
            .Text(0x21d6, 17, "DS:21d6 BuildingWords.UnknownBuilding", s => s.BuildingWords.UnknownBuilding, (s, v) => s.BuildingWords.UnknownBuilding = v)
            .Text(0x21e7, 47, "DS:21e7 BuildingWords.FewGuards", s => s.BuildingWords.FewGuards, (s, v) => s.BuildingWords.FewGuards = v)
            .Text(0x2216, 43, "DS:2216 BuildingWords.ExtraGuards", s => s.BuildingWords.ExtraGuards, (s, v) => s.BuildingWords.ExtraGuards = v)
            .Text(0x2241, 39, "DS:2241 BuildingWords.SwarmingGuards", s => s.BuildingWords.SwarmingGuards, (s, v) => s.BuildingWords.SwarmingGuards = v)
            .Text(0x2268, 28, "DS:2268 BuildingWords.DoYouPlaceWiretap", s => s.BuildingWords.DoYouPlaceWiretap, (s, v) => s.BuildingWords.DoYouPlaceWiretap = v)
            .Text(0x2284, 22, "DS:2284 BuildingWords.BreakIntoBuilding", s => s.BuildingWords.BreakIntoBuilding, (s, v) => s.BuildingWords.BreakIntoBuilding = v)
            .Text(0x229a, 40, "DS:229a BuildingWords.WatchCheckLeave", s => s.BuildingWords.WatchCheckLeave, (s, v) => s.BuildingWords.WatchCheckLeave = v)
            .Text(0x22c2, 13, "DS:22c2 BuildingWords.BuildingScene", s => s.BuildingWords.BuildingScene, (s, v) => s.BuildingWords.BuildingScene = v)
            .Text(0x22cf, 12, "DS:22cf BuildingWords.BreakInPicture", s => s.BuildingWords.BreakInPicture, (s, v) => s.BuildingWords.BreakInPicture = v)
            .Text(0x22db, 12, "DS:22db BuildingWords.WiretapPicture", s => s.BuildingWords.WiretapPicture, (s, v) => s.BuildingWords.WiretapPicture = v)
            .Text(0x22e7, 16, "DS:22e7 BuildingWords.GuardsAre", s => s.BuildingWords.GuardsAre, (s, v) => s.BuildingWords.GuardsAre = v)
            .Text(0x22f7, 17, "DS:22f7 BuildingWords.YouWillHave", s => s.BuildingWords.YouWillHave, (s, v) => s.BuildingWords.YouWillHave = v)
            .Text(0x2308, 11, "DS:2308 BuildingWords.MinutesEnd", s => s.BuildingWords.MinutesEnd, (s, v) => s.BuildingWords.MinutesEnd = v)
            .Text(0x2313, 13, "DS:2313 BuildingWords.FollowedScene", s => s.BuildingWords.FollowedScene, (s, v) => s.BuildingWords.FollowedScene = v)
            .Bytes(0x2320, 10, "DS:2320 BuildingWords.FollowedProseKey", s => s.BuildingWords.FollowedProseKey.Bytes)
            .Text(0x232a, 7, "DS:232a BuildingWords.HeadquartersScene", s => s.BuildingWords.HeadquartersScene, (s, v) => s.BuildingWords.HeadquartersScene = v)
            .Text(0x2331, 61, "DS:2331 BuildingWords.ArriveAtCia", s => s.BuildingWords.ArriveAtCia, (s, v) => s.BuildingWords.ArriveAtCia = v)
            .Text(0x236e, 11, "DS:236e BuildingWords.BinocularsScene", s => s.BuildingWords.BinocularsScene, (s, v) => s.BuildingWords.BinocularsScene = v)
            .Text(0x2379, 19, "DS:2379 BuildingWords.YouNotice", s => s.BuildingWords.YouNotice, (s, v) => s.BuildingWords.YouNotice = v)
            .Text(0x238c, 24, "DS:238c BuildingWords.LeavingTheBuilding", s => s.BuildingWords.LeavingTheBuilding, (s, v) => s.BuildingWords.LeavingTheBuilding = v)
            .Text(0x23a4, 55, "DS:23a4 BuildingWords.WatchMenu", s => s.BuildingWords.WatchMenu, (s, v) => s.BuildingWords.WatchMenu = v)
            .Text(0x23db, 10, "DS:23db BuildingWords.HeLooks", s => s.BuildingWords.HeLooks, (s, v) => s.BuildingWords.HeLooks = v)
            .Text(0x23e5, 11, "DS:23e5 BuildingWords.SheLooks", s => s.BuildingWords.SheLooks, (s, v) => s.BuildingWords.SheLooks = v)
            .Text(0x23f0, 3, "DS:23f0 BuildingWords.PeriodLine", s => s.BuildingWords.PeriodLine, (s, v) => s.BuildingWords.PeriodLine = v)
            .Text(0x23f3, 11, "DS:23f3 BuildingWords.TraceScene", s => s.BuildingWords.TraceScene, (s, v) => s.BuildingWords.TraceScene = v)
            .Text(0x33ee, 120, "DS:33ee HeadquartersWords.FloorMenu", s => s.HeadquartersWords.FloorMenu, (s, v) => s.HeadquartersWords.FloorMenu = v)
            .Text(0x3466, 9, "DS:3466 HeadquartersWords.IntelligenceScene", s => s.HeadquartersWords.IntelligenceScene, (s, v) => s.HeadquartersWords.IntelligenceScene = v)
            .Text(0x346f, 112, "DS:346f HeadquartersWords.IntelligenceMenu", s => s.HeadquartersWords.IntelligenceMenu, (s, v) => s.HeadquartersWords.IntelligenceMenu = v)
            .Text(0x34df, 20, "DS:34df HeadquartersWords.NoNewInformationLocal", s => s.HeadquartersWords.NoNewInformationLocal, (s, v) => s.HeadquartersWords.NoNewInformationLocal = v)
            .Text(0x34f3, 20, "DS:34f3 HeadquartersWords.NoNewInformationWorld", s => s.HeadquartersWords.NoNewInformationWorld, (s, v) => s.HeadquartersWords.NoNewInformationWorld = v)
            .Text(0x3507, 44, "DS:3507 HeadquartersWords.AgentConfesses", s => s.HeadquartersWords.AgentConfesses, (s, v) => s.HeadquartersWords.AgentConfesses = v)
            .Text(0x3533, 52, "DS:3533 HeadquartersWords.SearchHisFiles", s => s.HeadquartersWords.SearchHisFiles, (s, v) => s.HeadquartersWords.SearchHisFiles = v)
            .Text(0x3567, 93, "DS:3567 HeadquartersWords.AgentInnocent", s => s.HeadquartersWords.AgentInnocent, (s, v) => s.HeadquartersWords.AgentInnocent = v)
            .Text(0x35c4, 13, "DS:35c4 HeadquartersWords.DataScene", s => s.HeadquartersWords.DataScene, (s, v) => s.HeadquartersWords.DataScene = v)
            .Text(0x35d1, 84, "DS:35d1 HeadquartersWords.DataMenu", s => s.HeadquartersWords.DataMenu, (s, v) => s.HeadquartersWords.DataMenu = v)
            .Text(0x3625, 55, "DS:3625 HeadquartersWords.DataMenuEnd", s => s.HeadquartersWords.DataMenuEnd, (s, v) => s.HeadquartersWords.DataMenuEnd = v)
            .Text(0x365c, 11, "DS:365c HeadquartersWords.CryptoScene", s => s.HeadquartersWords.CryptoScene, (s, v) => s.HeadquartersWords.CryptoScene = v)
            .Text(0x3667, 52, "DS:3667 HeadquartersWords.CryptoMenu", s => s.HeadquartersWords.CryptoMenu, (s, v) => s.HeadquartersWords.CryptoMenu = v)
            .Text(0x3d5e, 12, "DS:3d5e HeadquartersWords.OfficeSceneOfFemaleAgent", s => s.HeadquartersWords.OfficeSceneOfFemaleAgent, (s, v) => s.HeadquartersWords.OfficeSceneOfFemaleAgent = v)
            .Text(0x3d6a, 12, "DS:3d6a HeadquartersWords.OfficeSceneOfMaleAgent", s => s.HeadquartersWords.OfficeSceneOfMaleAgent, (s, v) => s.HeadquartersWords.OfficeSceneOfMaleAgent = v)
            .Text(0x3d76, 57, "DS:3d76 HeadquartersWords.GoingOver", s => s.HeadquartersWords.GoingOver, (s, v) => s.HeadquartersWords.GoingOver = v)
            .Text(0x3daf, 36, "DS:3daf HeadquartersWords.LookIntoOrganizations", s => s.HeadquartersWords.LookIntoOrganizations, (s, v) => s.HeadquartersWords.LookIntoOrganizations = v)
            .Text(0x3dd3, 2, "DS:3dd3 HeadquartersWords.OrganizationIndent", s => s.HeadquartersWords.OrganizationIndent, (s, v) => s.HeadquartersWords.OrganizationIndent = v)
            .Text(0x3dd5, 2, "DS:3dd5 HeadquartersWords.OrganizationEnd", s => s.HeadquartersWords.OrganizationEnd, (s, v) => s.HeadquartersWords.OrganizationEnd = v)
            .Text(0x3dd7, 49, "DS:3dd7 HeadquartersWords.ActiveCities", s => s.HeadquartersWords.ActiveCities, (s, v) => s.HeadquartersWords.ActiveCities = v)
            .Text(0x3e08, 3, "DS:3e08 HeadquartersWords.CityEnd", s => s.HeadquartersWords.CityEnd, (s, v) => s.HeadquartersWords.CityEnd = v)
            .Text(0x3e0b, 39, "DS:3e0b HeadquartersWords.NotMuchToGo", s => s.HeadquartersWords.NotMuchToGo, (s, v) => s.HeadquartersWords.NotMuchToGo = v)
            .Text(0x3e32, 5, "DS:3e32 HeadquartersWords.TheOrganization", s => s.HeadquartersWords.TheOrganization, (s, v) => s.HeadquartersWords.TheOrganization = v)
            .Text(0x3e37, 7, "DS:3e37 HeadquartersWords.HasA", s => s.HeadquartersWords.HasA, (s, v) => s.HeadquartersWords.HasA = v)
            .Text(0x3e3e, 16, "DS:3e3e HeadquartersWords.RightHereIn", s => s.HeadquartersWords.RightHereIn, (s, v) => s.HeadquartersWords.RightHereIn = v)
            .Text(0x3e4e, 60, "DS:3e4e HeadquartersWords.SuggestGoThere", s => s.HeadquartersWords.SuggestGoThere, (s, v) => s.HeadquartersWords.SuggestGoThere = v)
            .Text(0x3e8a, 46, "DS:3e8a HeadquartersWords.VitalInfo", s => s.HeadquartersWords.VitalInfo, (s, v) => s.HeadquartersWords.VitalInfo = v)
            .Text(0x3eb8, 2, "DS:3eb8 HeadquartersWords.NameSpace", s => s.HeadquartersWords.NameSpace, (s, v) => s.HeadquartersWords.NameSpace = v)
            .Text(0x3eba, 32, "DS:3eba HeadquartersWords.CheckWithCrypto", s => s.HeadquartersWords.CheckWithCrypto, (s, v) => s.HeadquartersWords.CheckWithCrypto = v)
            .Text(0x3eda, 54, "DS:3eda HeadquartersWords.EnoughEvidence", s => s.HeadquartersWords.EnoughEvidence, (s, v) => s.HeadquartersWords.EnoughEvidence = v)
            .Text(0x3f10, 43, "DS:3f10 HeadquartersWords.CheckOutPerson", s => s.HeadquartersWords.CheckOutPerson, (s, v) => s.HeadquartersWords.CheckOutPerson = v)
            .Text(0x3f3b, 3, "DS:3f3b HeadquartersWords.He", s => s.HeadquartersWords.He, (s, v) => s.HeadquartersWords.He = v)
            .Text(0x3f3e, 4, "DS:3f3e HeadquartersWords.She", s => s.HeadquartersWords.She, (s, v) => s.HeadquartersWords.She = v)
            .Text(0x3f42, 18, "DS:3f42 HeadquartersWords.RightHereInCity", s => s.HeadquartersWords.RightHereInCity, (s, v) => s.HeadquartersWords.RightHereInCity = v)
            .Text(0x3f54, 3, "DS:3f54 HeadquartersWords.FullStop", s => s.HeadquartersWords.FullStop, (s, v) => s.HeadquartersWords.FullStop = v)
            .Bytes(0x3f57, 9, "DS:3f57 HeadquartersWords.AdviceKnownKey", s => s.HeadquartersWords.AdviceKnownKey.Bytes)
            .Bytes(0x3f60, 9, "DS:3f60 HeadquartersWords.AdviceBuildingKey", s => s.HeadquartersWords.AdviceBuildingKey.Bytes)
            .Bytes(0x3f69, 9, "DS:3f69 HeadquartersWords.AdviceNoBuildingKey", s => s.HeadquartersWords.AdviceNoBuildingKey.Bytes)
            .Bytes(0x3f72, 9, "DS:3f72 HeadquartersWords.AdviceSeenKey", s => s.HeadquartersWords.AdviceSeenKey.Bytes)
            .Bytes(0x3f7b, 9, "DS:3f7b HeadquartersWords.AdviceUnseenKey", s => s.HeadquartersWords.AdviceUnseenKey.Bytes)
            .Bytes(0x3f84, 9, "DS:3f84 HeadquartersWords.AdviceElsewhereKey", s => s.HeadquartersWords.AdviceElsewhereKey.Bytes)
            .Text(0x2ab8, 23, "DS:2ab8 InvestigationWords.CodedMessageHeading", s => s.InvestigationWords.CodedMessageHeading, (s, v) => s.InvestigationWords.CodedMessageHeading = v)
            .Text(0x2acf, 23, "DS:2acf InvestigationWords.MeetingNotesHeading", s => s.InvestigationWords.MeetingNotesHeading, (s, v) => s.InvestigationWords.MeetingNotesHeading = v)
            .Text(0x2ae6, 24, "DS:2ae6 InvestigationWords.NewInformation", s => s.InvestigationWords.NewInformation, (s, v) => s.InvestigationWords.NewInformation = v)
            .Text(0x2afe, 28, "DS:2afe InvestigationWords.AdditionalInformation", s => s.InvestigationWords.AdditionalInformation, (s, v) => s.InvestigationWords.AdditionalInformation = v)
            .Text(0x2b1a, 2, "DS:2b1a InvestigationWords.NameSpace", s => s.InvestigationWords.NameSpace, (s, v) => s.InvestigationWords.NameSpace = v)
            .Text(0x2b1c, 6, "DS:2b1c InvestigationWords.DotsLine", s => s.InvestigationWords.DotsLine, (s, v) => s.InvestigationWords.DotsLine = v)
            .Text(0x2b22, 4, "DS:2b22 InvestigationWords.On", s => s.InvestigationWords.On, (s, v) => s.InvestigationWords.On = v)
            .Text(0x2b26, 2, "DS:2b26 InvestigationWords.DateSpace", s => s.InvestigationWords.DateSpace, (s, v) => s.InvestigationWords.DateSpace = v)
            .Text(0x2b28, 8, "DS:2b28 InvestigationWords.OfThe", s => s.InvestigationWords.OfThe, (s, v) => s.InvestigationWords.OfThe = v)
            .Text(0x2b30, 4, "DS:2b30 InvestigationWords.In", s => s.InvestigationWords.In, (s, v) => s.InvestigationWords.In = v)
            .Text(0x2b34, 2, "DS:2b34 InvestigationWords.LineEnd", s => s.InvestigationWords.LineEnd, (s, v) => s.InvestigationWords.LineEnd = v)
            .Text(0x2b37, 2, "DS:2b37 InvestigationWords.LooksHint", s => s.InvestigationWords.LooksHint, (s, v) => s.InvestigationWords.LooksHint = v)
            .Text(0x2b39, 5, "DS:2b39 InvestigationWords.BuildingIn", s => s.InvestigationWords.BuildingIn, (s, v) => s.InvestigationWords.BuildingIn = v);
        for (var scene = 0; scene < CampaignState.BuildingScenes; scene++)
        {
            var index = scene;
            layout.Bytes(0x15ce + 16 * index, 16, $"DS:{0x15ce + 16 * index:x4} colour map of building scene {index}", s => s.BuildingColorMaps[index]);
        }
    }

    /// <summary>
    /// The save screens' variables and texts (the library's <see cref="SaveScreenState"/>, here at GAME's places; the
    /// texts in the order of <see cref="SaveText"/>).
    /// </summary>
    private static void AddSaveScreens(StateLayout<CampaignState> layout)
    {
        layout
            .Text(0x3fa4, 10, "DS:3fa4 save_file_name", s => s.Saves.SavePath, (s, v) => s.Saves.SavePath = v)
            .Flag(0x55a6, "DS:55a6 loading_flag", s => s.Saves.Loading, (s, v) => s.Saves.Loading = v)
            .Int16(0x55a8, "DS:55a8 save_error_code", s => s.Saves.SaveError, (s, v) => s.Saves.SaveError = v)
            .Int16(0x978e, "DS:978e save_slot_exists_mask", s => s.Saves.SlotExistsMask, (s, v) => s.Saves.SlotExistsMask = v)
            .Int16(0xa86c, "DS:a86c save_menu_choice", s => s.Saves.SlotChoice, (s, v) => s.Saves.SlotChoice = v)
            .Int16(0x55aa, "DS:55aa slot_file_handle", s => s.Saves.SlotFileHandle, (s, v) => s.Saves.SlotFileHandle = v)
            .Int16(0x55ac, "DS:55ac slot_scope_word", s => s.Saves.SlotLevel, (s, v) => s.Saves.SlotLevel = v)
            .Bytes(0x93f0, SaveScreenState.SlotBlockLength, "DS:93f0 slot_block_copy", s => s.Saves.SlotBlock)
            .Bytes(0x5596, 16, "DS:5596 slot_number_text", s => s.Saves.Digits.Bytes);
        (int Address, int Length)[] saveTexts =
        [
            (0x3f8e, 21), (0x401b, 21), (0x3fae, 4), (0x3fb2, 7), (0x3fb9, 16), (0x3fc9, 12), (0x3fd5, 3), (0x3fd8, 2), (0x3fda, 3),
            (0x3fdd, 9), (0x3fe6, 12), (0x3ff2, 12), (0x3ffe, 10), (0x4008, 5), (0x400d, 14), (0x4030, 22), (0x4046, 17), (0x4057, 22),
            (0x406d, 12), (0x4079, 24), (0x4091, 4), (0x4095, 13), (0x40a2, 2), (0x40a4, 25), (0x40bd, 59), (0x40f8, 57), (0x4131, 61),
            (0x416e, 29), (0x418b, 21),
        ];
        for (var i = 0; i < saveTexts.Length; i++)
        {
            var index = i;
            var (text, length) = saveTexts[index];
            layout.Text(text, length, $"DS:{text:x4} save text {(SaveText)index}", s => s.Saves.Texts[index], (s, v) => s.Saves.Texts[index] = v);
        }
    }

    /// <summary>A record of a figure table (<see cref="Shape"/>): six words.</summary>
    // orig: struct R, 12 bytes (src/c/game/game_00f7_147d.c)
    private static StateLayout<Shape> ShapeLayout => new StateLayout<Shape>("Shape")
        .Int16(0x00, "+0x00 kind", r => r.Kind, (r, v) => r.Kind = v)
        .Int16(0x02, "+0x02 x1", r => r.X1, (r, v) => r.X1 = v)
        .Int16(0x04, "+0x04 y1", r => r.Y1, (r, v) => r.Y1 = v)
        .Int16(0x06, "+0x06 x2", r => r.X2, (r, v) => r.X2 = v)
        .Int16(0x08, "+0x08 y2", r => r.Y2, (r, v) => r.Y2 = v)
        .Int16(0x0a, "+0x0a colour", r => r.Color, (r, v) => r.Color = v);

    /// <summary>A table of campaign records (<c>struct Person ps[64]</c>): one part per record, <paramref name="size"/> bytes apart.</summary>
    private static void Records<TRecord>(StateLayout<CampaignState> layout, int offset, string name, int size, int count, StateLayout<TRecord> record, Func<CampaignState, TRecord[]> table)
        where TRecord : class
    {
        for (var i = 0; i < count; i++)
        {
            var index = i;
            layout.Part(offset + size * index, $"{name}[{index}]", record, s => table(s)[index]);
        }
    }

    /// <summary>
    /// A table of pointers to text buffers that the original copies texts into, each buffer stored whole (the bytes
    /// behind a text's end mark too): the pointers lead to fixed places, the buffers are those of the state's table.
    /// </summary>
    private static void BufferTable(StateLayout<CampaignState> layout, int offset, string name, int[] addresses, int[] sizes, Func<CampaignState, TextBufferTable> table)
    {
        for (var i = 0; i < addresses.Length; i++)
        {
            var index = i;
            layout
                .FixedPointer(offset + 2 * index, $"{name}[{index}]", addresses[index])
                .Bytes(addresses[index], sizes[index], $"{name}[{index}] text", s => table(s).Buffers[index].Bytes);
        }
    }

    /// <summary>
    /// A table of texts of the program (<c>char *month_names[12]</c>): a fixed pointer per entry, and the text it points
    /// at as an entry of the state's array.
    /// </summary>
    private static void TextTable(StateLayout<CampaignState> layout, int offset, string name, (int Address, int Length)[] texts, Func<CampaignState, string[]> table)
    {
        for (var i = 0; i < texts.Length; i++)
        {
            var index = i;
            var (address, length) = texts[index];
            layout
                .FixedPointer(offset + 2 * index, $"{name}[{index}]", address)
                .Text(address, length, $"{name}[{index}] text", s => table(s)[index], (s, v) => table(s)[index] = v);
        }
    }
}
