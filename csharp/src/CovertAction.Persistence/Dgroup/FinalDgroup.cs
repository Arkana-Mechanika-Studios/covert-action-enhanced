using System.Buffers.Binary;
using CovertAction.Core.Final;
using CovertAction.Core.Graphics;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's menus program, FINAL.EXE: where each variable of
/// <see cref="FinalState"/> lies in it. The oracle tests load and compare the state through it; the start state
/// (tables, texts) is loaded through it from the player's own FINAL.EXE.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed final.h, docs/data-model/final.md and symbols/names.txt
/// (<c>D_final_xxxx</c>); the campaign records have the layouts of <see cref="CampaignLayouts"/> (the same as in the
/// hand-off block); the place of the C library's <c>holdrand</c> was read from the vectors of <c>random</c>, the only
/// variable it changes. Only the variables of the ported functions are listed; the others join with the first
/// function that uses them.
/// </remarks>
public static class FinalDgroup
{
    /// <summary>The fields of a <see cref="FameEntry"/> at their offsets; 44 bytes.</summary>
    // orig: struct fame_entry (src/c/include/final.h)
    public static StateLayout<FameEntry> FameEntry { get; } = new StateLayout<FameEntry>("fame_entry")
        .Int16(0x00, "+0x00 score", r => r.Score, (r, v) => r.Score = v)
        .Text(0x02, 32, "+0x02 name", r => r.Name, (r, v) => r.Name = v)
        .Int16(0x22, "+0x22 level", r => r.Level, (r, v) => r.Level = v)
        .Int16(0x24, "+0x24 plot", r => r.Plot, (r, v) => r.Plot = v)
        .Int16(0x26, "+0x26 variant", r => r.Variant, (r, v) => r.Variant = v)
        .Int16(0x28, "+0x28 ep", r => r.Ep, (r, v) => r.Ep = v)
        .Int16(0x2a, "+0x2a month", r => r.Month, (r, v) => r.Month = v);

    /// <summary>The size of a <see cref="FameEntry"/> in bytes.</summary>
    public const int FameEntrySize = 44;

    /// <summary>Where the 64 texts of <c>clue_labels</c> lie (the targets of its pointers, row by row), in the program's initialised data.</summary>
    private static readonly int[] ClueLabelTexts =
    [
        0x3875, 0x3883, 0x3890, 0x38a0, 0x38aa, 0x38b6, 0x38c4, 0x38cd, 0x38d8, 0x38e6, 0x38f9, 0x3908, 0x3917, 0x3929, 0x3935, 0x393f,
        0x394c, 0x3959, 0x3966, 0x3970, 0x397c, 0x398a, 0x3997, 0x39a5, 0x39b1, 0x39b9, 0x39c2, 0x39cb, 0x39d7, 0x39df, 0x39e5, 0x39f0,
        0x39f7, 0x3a01, 0x3a07, 0x3a0f, 0x3a17, 0x3a22, 0x3a2c, 0x3a36, 0x3a40, 0x3a42, 0x3a44, 0x3a46, 0x3a48, 0x3a4a, 0x3a4c, 0x3a4e,
        0x3a50, 0x3a52, 0x3a54, 0x3a56, 0x3a58, 0x3a5a, 0x3a5c, 0x3a5e, 0x3a60, 0x3a71, 0x3a7e, 0x3a92, 0x3a9e, 0x3ab1, 0x3abe, 0x3acb,
    ];

    /// <summary>Where the texts of the seven placeholder names lie, eight bytes apart ("$VICTIM" ...).</summary>
    private const int VariableNameTexts = 0x3da8;

    /// <summary>The buffers of the seven placeholder values (the targets of macro_values' pointers) and their sizes.</summary>
    private static readonly (int Address, int Length)[] VariableValueBuffers =
        [(0x3de0, 0x34), (0x3e14, 0x1a), (0x3e2e, 0x1a), (0x3e48, 0x1a), (0x3e62, 0x1a), (0x3e7c, 0x1a), (0x3e96, 0x34)];

    /// <summary>The four texts "fame.dta": tested and opened for reading, tested and opened for writing.</summary>
    private static readonly int[] FameFileNameTexts = [0x328e, 0x3297, 0x32a0, 0x32a9];

    /// <summary>The texts of the four ratings of a skill, and their bytes up to the next.</summary>
    private static readonly (int Address, int Length)[] RatingNameTexts = [(0x2488, 8), (0x2490, 5), (0x2495, 10), (0x249f, 9)];

    /// <summary>Where the texts of the 26 MasterMinds' organisations lie (the targets of the pointers of <c>mastermind_names</c>), each up to the next; the last is 13 bytes.</summary>
    private static readonly int[] MastermindNameTexts =
    [
        0x2b80, 0x2b84, 0x2b88, 0x2b96, 0x2ba4, 0x2bb2, 0x2bbb, 0x2bc8, 0x2bd4, 0x2bde, 0x2bef, 0x2bf5, 0x2c01,
        0x2c05, 0x2c13, 0x2c1f, 0x2c2c, 0x2c38, 0x2c46, 0x2c4b, 0x2c54, 0x2c62, 0x2c70, 0x2c77, 0x2c7b, 0x2c84, 0x2c91,
    ];

    /// <summary>The texts of the briefing's first pages (<see cref="BriefingText"/>) and their sizes, end mark included.</summary>
    private static readonly (int Address, int Length)[] BriefingTextPlaces =
    [
        (0x301c, 26), (0x3036, 45), (0x3063, 8), (0x306b, 20), (0x307f, 17), (0x3090, 6), (0x3096, 26), (0x30b0, 59),
        (0x30eb, 56), (0x3123, 78), (0x3171, 50), (0x31a3, 67), (0x31e6, 21), (0x31fb, 59), (0x3236, 43),
    ];

    /// <summary>
    /// Where the texts of the persons' names lie (the targets of the pointers of <c>first_names</c>, women's then men's,
    /// and of <c>surnames</c>), each up to the next.
    /// </summary>
    private static readonly (int Address, int Length)[] NameTexts =
    [
        (0x401b, 9), (0x4024, 7), (0x402b, 6), (0x4031, 8), (0x4039, 6), (0x403f, 7), (0x4046, 8), (0x404e, 6),
        (0x4054, 8), (0x405c, 9), (0x4065, 5), (0x406a, 9), (0x4073, 9), (0x407c, 9), (0x4085, 5), (0x408a, 6),
        (0x4090, 6), (0x4096, 4), (0x409a, 6), (0x40a0, 7), (0x40a7, 6), (0x40ad, 7), (0x40b4, 6), (0x40ba, 5),
        (0x40bf, 8), (0x40c7, 7), (0x40ce, 5), (0x40d3, 6), (0x40d9, 4), (0x40dd, 5), (0x40e2, 8), (0x40ea, 5),
        (0x40ef, 6), (0x40f5, 7), (0x40fc, 7), (0x4103, 6), (0x4109, 7), (0x4110, 6), (0x4116, 7), (0x411d, 6),
        (0x4123, 7), (0x412a, 8), (0x4132, 6), (0x4138, 7), (0x413f, 8), (0x4147, 7), (0x414e, 9), (0x4157, 5),
        (0x415c, 9), (0x4165, 7), (0x416c, 8), (0x4174, 7), (0x417b, 6), (0x4181, 6), (0x4187, 8), (0x418f, 7),
        (0x4196, 7), (0x419d, 6), (0x41a3, 6), (0x41a9, 6), (0x41af, 7), (0x41b6, 6), (0x41bc, 8), (0x41c4, 8),
        (0x41cc, 7), (0x41d3, 7), (0x41da, 5), (0x41df, 7), (0x41e6, 7), (0x41ed, 7), (0x41f4, 7), (0x41fb, 5),
        (0x4200, 6), (0x4206, 8), (0x420e, 8), (0x4216, 6), (0x421c, 7), (0x4223, 7), (0x422a, 9), (0x4233, 8),
        (0x423b, 5), (0x4240, 4), (0x4244, 5), (0x4249, 4), (0x424d, 4), (0x4251, 5), (0x4256, 5), (0x425b, 6),
        (0x4261, 8), (0x4269, 9), (0x4272, 5), (0x4277, 7), (0x427e, 6), (0x4284, 7), (0x428b, 7), (0x4292, 7),
        (0x4299, 5), (0x429e, 4), (0x42a2, 6), (0x42a8, 6), (0x42ae, 5), (0x42b3, 4), (0x42b7, 6), (0x42bd, 6),
        (0x42c3, 7), (0x42ca, 8), (0x42d2, 6), (0x42d8, 7), (0x42df, 8), (0x42e7, 8), (0x42ef, 9), (0x42f8, 6),
        (0x42fe, 7), (0x4305, 5), (0x430a, 7), (0x4311, 6), (0x4317, 5), (0x431c, 6), (0x4322, 5), (0x4327, 6),
        (0x432d, 8), (0x4335, 8), (0x433d, 7), (0x4344, 6), (0x434a, 7), (0x4351, 7), (0x4358, 7), (0x435f, 6),
        (0x4365, 7), (0x436c, 6), (0x4372, 8), (0x437a, 9), (0x4383, 8), (0x438b, 7), (0x4392, 7), (0x4399, 8),
        (0x43a1, 8), (0x43a9, 8), (0x43b1, 6), (0x43b7, 7), (0x43be, 7), (0x43c5, 8), (0x43cd, 10), (0x43d7, 9),
        (0x43e0, 7), (0x43e7, 5), (0x43ec, 7), (0x43f3, 6), (0x43f9, 6), (0x43ff, 6), (0x4405, 7), (0x440c, 7),
        (0x4413, 5), (0x4418, 7), (0x441f, 7), (0x4426, 6), (0x442c, 8), (0x4434, 8), (0x443c, 6), (0x4442, 7),
        (0x4449, 6), (0x444f, 5), (0x4454, 6), (0x445a, 7), (0x4461, 6), (0x4467, 7), (0x446e, 7), (0x4475, 7),
        (0x447c, 5), (0x4481, 6), (0x4487, 6), (0x448d, 7), (0x4494, 8), (0x449c, 8), (0x44a4, 5), (0x44a9, 7),
        (0x44b0, 6), (0x44b6, 5), (0x44bb, 6), (0x44c1, 6), (0x44c7, 8), (0x44cf, 8), (0x44d7, 7), (0x44de, 6),
        (0x44e4, 7), (0x44eb, 6), (0x44f1, 8), (0x44f9, 7), (0x4500, 9), (0x4509, 9), (0x4512, 8), (0x451a, 9),
    ];

    /// <summary>Where the texts of the eight ranks lie (the targets of the pointers of <c>rank_names</c>).</summary>
    private static readonly (int Address, int Length)[] RankNameTexts =
        [(0x3806, 8), (0x380e, 10), (0x3818, 11), (0x3823, 6), (0x3829, 10), (0x3833, 14), (0x3841, 13), (0x384e, 11)];

    /// <summary>The texts of the efficiency report (<see cref="ReportText"/>) and their sizes, end mark included.</summary>
    private static readonly (int Address, int Length)[] ReportTextPlaces =
    [
        (0x2a1a, 18), (0x2a2c, 3), (0x2a2f, 2), (0x2a31, 9), (0x2a3a, 9), (0x2a43, 7), (0x2a4a, 4), (0x2a4e, 3), (0x2a51, 5),
        (0x2a56, 12), (0x2a62, 6), (0x2a68, 10), (0x2a72, 5), (0x2a77, 2), (0x2a79, 7), (0x2a80, 16), (0x2a90, 12), (0x2a9c, 1),
        (0x2a9d, 17), (0x2aae, 20), (0x2ac2, 16), (0x2ad2, 6), (0x2ad8, 18), (0x2aea, 5), (0x2aef, 5), (0x2af4, 4), (0x2af8, 3),
    ];

    /// <summary>FINAL DS:3ae0 rank_names: the variable behind the last clue label's text.</summary>
    private const int RankNames = 0x3ae0;

    /// <summary>Where the texts of the twelve month names lie, four bytes apart (the targets of the pointers of <c>month_names</c>).</summary>
    private const int MonthNameTexts = 0x3546;

    private const int PlotsAt = 0x1e9e, PlotSize = 74;

    // orig: struct plot (src/c/include/final.h)
    private static StateLayout<PlotDefinition> PlotLayout { get; } = BuildPlot();

    private static StateLayout<PlotDefinition> BuildPlot()
    {
        const int titleBytes = 24, crimeFilesAt = 0x1a, itemsAt = 0x2a, itemBytes = 4;
        var plot = new StateLayout<PlotDefinition>("plot")
            .Text(0x00, titleBytes, "+0x00 title", p => p.Title, (p, v) => p.Title = v)
            .Int16(0x18, "+0x18 org_mask", p => p.OrgMask, (p, v) => p.OrgMask = v)
            .Int16Array(crimeFilesAt, PlotDefinition.Variants, "+0x1a crime_file", p => p.CrimeFile);
        for (var variant = 0; variant < PlotDefinition.Variants; variant++)
        {
            var v = variant;
            plot.TextPointer(itemsAt + itemBytes * v, $"+0x2a item[{v}].a", (p, text) => p.ItemA[v] = text)
                .TextPointer(itemsAt + itemBytes * v + 2, $"+0x2a item[{v}].b", (p, text) => p.ItemB[v] = text);
        }
        return plot;
    }

    /// <summary>The variables of FINAL.EXE at their offsets (built after the tables above, which it uses).</summary>
    // orig: FINAL DGROUP (src/c/include/final.h; holdrand DS:5a1e located from the vectors of random)
    public static StateLayout<FinalState> Layout { get; } = Build();

    private static StateLayout<FinalState> Build()
    {
        var layout = new StateLayout<FinalState>("FINAL DGROUP");

        // ---- this part's copy of the campaign ----
        Records(layout, 0x0188, "DS:0188 ps", CampaignTables.PersonCount, CampaignLayouts.PersonSize, CampaignLayouts.Person, s => s.Ps);
        Records(layout, 0x0588, "DS:0588 lc", CampaignTables.LocationCount, CampaignLayouts.LocationSize, CampaignLayouts.Location, s => s.Lc);
        Records(layout, 0x1188, "DS:1188 cl", CampaignTables.ClueCount, CampaignLayouts.ClueSize, CampaignLayouts.Clue, s => s.Cl);
        Records(layout, 0x8294, "DS:8294 ct", CampaignTables.WorldSize, CampaignLayouts.CitySize, CampaignLayouts.City, s => s.Ct);
        Records(layout, 0xa7be, "DS:a7be org", CampaignTables.WorldSize, CampaignLayouts.OrgSize, CampaignLayouts.Org, s => s.Org);
        Records(layout, 0x9fa6, "DS:9fa6 ac", FinalState.CastCapacity, CampaignLayouts.ActivitySize, CampaignLayouts.Activity, s => s.Ac);
        Records(layout, 0xad6c, "DS:ad6c ac2", FinalState.CastCapacity, CampaignLayouts.Activity2Size, CampaignLayouts.Activity2, s => s.Ac2);
        Records(layout, 0x8bca, "DS:8bca ev", FinalState.EventCapacity, CampaignLayouts.EventSize, CampaignLayouts.Event, s => s.Ev);
        Records(layout, 0x84b8, "DS:84b8 sn", CampaignTables.SynopsisCount, CampaignLayouts.SynopsisSize, CampaignLayouts.Synopsis, s => s.Sn);
        Records(layout, 0xac20, "DS:ac20 mc", CampaignTables.MacGuffinCount, CampaignLayouts.MacGuffinsSize, CampaignLayouts.MacGuffins, s => s.Mc);
        layout
            .Part(0x7df2, "DS:7df2 pl", CampaignLayouts.Plot, s => s.Pl)
            .Int16(0x84b6, "DS:84b6 Nperson", s => s.Nperson, (s, v) => s.Nperson = v)
            .Int16(0x96d0, "DS:96d0 Nloc", s => s.Nloc, (s, v) => s.Nloc = v)
            .Int16(0x96ca, "DS:96ca NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v)
            .Int16(0x9fa2, "DS:9fa2 NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v)
            .Int16(0x9e88, "DS:9e88 NEVENT", s => s.NEVENT, (s, v) => s.NEVENT = v)
            .Int16(0x7de6, "DS:7de6 diff", s => s.Diff, (s, v) => s.Diff = v)
            .Int16(0x7dec, "DS:7dec career_write_pos", s => s.CareerWritePos, (s, v) => s.CareerWritePos = v)
            .Bytes(0x5cbc, 16, "DS:5cbc digits_wrapup", s => s.DigitsWrapup.Bytes)
            // ---- the case being set up ----
            .Int16(0x9e94, "DS:9e94 TheOrg1", s => s.TheOrg1, (s, v) => s.TheOrg1 = v)
            .Int16(0x9e98, "DS:9e98 TheOrg2", s => s.TheOrg2, (s, v) => s.TheOrg2 = v)
            .Int16(0xabf6, "DS:abf6 TheOrgF", s => s.TheOrgF, (s, v) => s.TheOrgF = v)
            .Int16(0x96da, "DS:96da TheCity1", s => s.TheCity1, (s, v) => s.TheCity1 = v)
            .Int16(0x96dc, "DS:96dc TheCity2", s => s.TheCity2, (s, v) => s.TheCity2 = v)
            .Int16(0xac1e, "DS:ac1e TheCityF", s => s.TheCityF, (s, v) => s.TheCityF = v)
            .Text(0x3010, 12, "DS:3010 red_herring_role", s => s.RedHerringRole, (s, v) => s.RedHerringRole = v)
            // ---- the player's options and the text buffer ----
            .Bytes(0x79e2, FinalState.TxtLength, "DS:79e2 Txt", s => s.Txt.Bytes)
            // ---- the clock of the reports ----
            .Text(0x2a05, 5, "DS:2a05 clock_day_text", s => s.ClockDayText, (s, v) => s.ClockDayText = v)
            .Chars(0x2a0a, FinalState.ClockTextLength, "DS:2a0a clock_text", s => s.ClockText)
            .Int16(0x9bea, "DS:9bea game_minute", s => s.GameMinute, (s, v) => s.GameMinute = v)
            .Int16(0x9e82, "DS:9e82 game_hour", s => s.GameHour, (s, v) => s.GameHour = v)
            .Int16(0x7dea, "DS:7dea game_day", s => s.GameDay, (s, v) => s.GameDay = v)
            // ---- the screen ----
            .Part(0x4b46, "DS:4b46 Rp1X", RasterPortLayout.Layout, s => s.Rp1X)
            .Pointer<RasterPort>(0x4b58, "DS:4b58 Rp1", s => s.Rp1, (s, v) => s.Rp1 = v, (0x4b46, s => s.Rp1X), (0x155e, s => s.RpBackX))
            .Part(0x155e, "DS:155e RpBackX", RasterPortLayout.Layout, s => s.RpBackX)
            .Pointer<RasterPort>(0x1570, "DS:1570 RpBack", s => s.RpBack, (s, v) => s.RpBack = v, (0x155e, s => s.RpBackX), (0x4b46, s => s.Rp1X))
            .Flag(0x8092, "DS:8092 CGA", s => s.CGA, (s, v) => s.CGA = v)
            // ---- the roster ----
            .Int16(0xb30c, "DS:b30c n_roster", s => s.NRoster, (s, v) => s.NRoster = v)
            .Text(0x2a03, 2, "DS:2a03 roster label separator", s => s.RosterLabelSeparator, (s, v) => s.RosterLabelSeparator = v)
            // ---- texts and file names ----
            .Text(0x3fdc, 2, "DS:3fdc line break", s => s.StrNewline, (s, v) => s.StrNewline = v)
            .Text(0x326e, 8, "DS:326e str_ten_dta", s => s.StrTenDta, (s, v) => s.StrTenDta = v)
            .Chars(0x3276, FinalState.FileNameLength, "DS:3276 crime_file_name", s => s.CrimeFileName)
            .Chars(0x3282, FinalState.FileNameLength, "DS:3282 world_file_name", s => s.WorldFileName)
            // ---- the text field being edited ----
            .Int16(0x5cb6, "DS:5cb6 willy", s => s.Willy, (s, v) => s.Willy = v)
            .Int16(0x5cb8, "DS:5cb8 willyW", s => s.WillyW, (s, v) => s.WillyW = v)
            .Int16(0x5cba, "DS:5cba willyX", s => s.WillyX, (s, v) => s.WillyX = v)
            // ---- the animation ----
            // ---- the C library's random state ----
            .UInt32(0x5a1e, "DS:5a1e holdrand", s => s.Random.State, (s, v) => s.Random.State = v);
        for (var month = 0; month < FinalState.MonthCount; month++)
        {
            var index = month;
            var text = MonthNameTexts + 4 * index;
            layout
                .FixedPointer(0x367c + 2 * index, $"DS:367c month_names[{index}]", text)
                .Text(text, 4, $"DS:{text:x4} month name {index}", s => s.MonthNames[index], (s, v) => s.MonthNames[index] = v);
        }

        // ---- the clue texts: the labels lie one after the other, each up to the next; the last up to rank_names ----
        for (var i = 0; i < ClueLabelTexts.Length; i++)
        {
            var (kind, variant) = (i / FinalState.ClueLabelsPerKind, i % FinalState.ClueLabelsPerKind);
            var text = ClueLabelTexts[i];
            var end = i + 1 < ClueLabelTexts.Length ? ClueLabelTexts[i + 1] : RankNames;
            layout
                .FixedPointer(0x3b00 + 2 * i, $"DS:3b00 clue_labels[{kind}][{variant}]", text)
                .Text(text, end - text, $"DS:{text:x4} clue label {kind}/{variant}", s => s.ClueLabels[kind][variant], (s, v) => s.ClueLabels[kind][variant] = v);
        }
        layout
            .Bytes(0x5d1c, FinalState.DigitsMsgLength, "DS:5d1c digits_msg", s => s.DigitsMsgBuffer.Bytes)
            .Text(0x3ca2, 3, "DS:3ca2 str_suffix_2", s => s.StrSuffix2, (s, v) => s.StrSuffix2 = v)
            .Text(0x3ca5, 4, "DS:3ca5 str_suffix_3", s => s.StrSuffix3, (s, v) => s.StrSuffix3 = v);

        AddDossierTexts(layout);

        // ---- the tables of the program ----
        Rows(layout, 0x1cfe, "DS:1cfe mastermind_looks", FinalState.MastermindCount, FinalState.MastermindLookWords, s => s.MastermindLooks);
        Records(layout, 0xb1c0, "DS:b1c0 fame_table", FinalState.FameEntries, FameEntrySize, FameEntry, s => s.FameTable);
        // ---- the library's screens (faces, photos, report frames) ----
        layout
            .ImageArray(0x9e9c, FinalState.SpriteCount, "DS:9e9c image_table", s => s.SpriteTable, s => s.Images)
            .Char(0xae2c, "DS:ae2c GMode", s => s.GMode, (s, v) => s.GMode = v)
            .Bytes(0x1612, 16, "DS:1612 cga_map_faces", s => s.PhotoColorMap)
            // ---- the library's text readers ----
            .Bytes(0xac6c, ILibraryTextState.LineCapacity, "DS:ac6c macro_scratch", s => s.TokenTail.Bytes)
            .Text(0x3f00, 47, "DS:3f00 str_clearance", s => s.ClearanceNotice, (s, v) => s.ClearanceNotice = v)
            .Text(0x3ef7, 2, "DS:3ef7 line separator", s => s.LineSeparator, (s, v) => s.LineSeparator = v)
            // ---- the messages of the plot's events ----
            .Int16(0x9e90, "DS:9e90 TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v)
            .Text(0x3fb4, 9, "DS:3fb4 text file name", s => s.TextFileName, (s, v) => s.TextFileName = v)
            .Text(0x3fbd, 9, "DS:3fbd message tag template", s => s.MessageTemplate, (s, v) => s.MessageTemplate = v)
            .Bytes(0x5d3c, 9, "DS:5d3c text_tag", s => s.MessageTag.Bytes)
            // ---- the case log and the roster ----
            .Int16(0x7df0, "DS:7df0 Gtime", s => s.Gtime, (s, v) => s.Gtime = v)
            .Text(0x4710, 15, "DS:4710 str_log_overflow", s => s.StrLogOverflow, (s, v) => s.StrLogOverflow = v)
            .Part(0x28ac, "DS:28ac work_portX", RasterPortLayout.Layout, s => s.WorkPortX)
            .FixedPointer(0x28be, "DS:28be work_port", 0x28ac)
            // ---- the training screen ----
            .Int16Array(0x24a8, FinalState.SkillCount, "DS:24a8 skill_colors", s => s.SkillColors);
        for (var i = 0; i < ILibraryTextState.MessageVariables; i++)
        {
            var index = i;
            const int nameLength = 8;
            var name = VariableNameTexts + nameLength * index;
            var (value, valueLength) = VariableValueBuffers[index];
            layout
                .FixedPointer(0x3eca + 2 * index, $"DS:3eca variable name pointer {index}", name)
                .Text(name, nameLength, $"DS:{name:x4} variable name {index}", s => s.VariableNameTexts[index], (s, v) => s.VariableNameTexts[index] = v)
                .FixedPointer(0x3ed8 + 2 * index, $"DS:3ed8 macro_values[{index}]", value)
                .Text(value, valueLength, $"DS:{value:x4} variable value {index}", s => s.VariableValueTexts[index], (s, v) => s.VariableValueTexts[index] = v);
        }
        // ---- the creation of the agent and the training screen ----
        layout
            .Text(0x23c8, 95, "DS:23c8 which difficulty level", s => s.StrWhichLevel, (s, v) => s.StrWhichLevel = v)
            .Text(0x2457, 13, "DS:2457 \"training.pic\"", s => s.TrainingPicture, (s, v) => s.TrainingPicture = v)
            .Text(0x2464, 2, "DS:2464 skills menu start", s => s.StrSkillMenu[0], (s, v) => s.StrSkillMenu[0] = v)
            .Text(0x2466, 12, "DS:2466 \" practice\"", s => s.StrSkillMenu[1], (s, v) => s.StrSkillMenu[1] = v)
            .Text(0x2472, 12, "DS:2472 \" training\"", s => s.StrSkillMenu[2], (s, v) => s.StrSkillMenu[2] = v);
        TextTable(layout, 0x247e, "DS:247e skill_names", [(0x2427, 7), (0x242e, 8), (0x2436, 13), (0x2443, 12)], s => s.SkillNames);

        // ---- the choice of the next case ----
        layout
            .Bytes(0x28c0, 8, "DS:28c0 plot_tag", s => s.PlotTag.Bytes)
            .Text(0x233e, 8, "DS:233e \"*PL0090\"", s => s.StrPlotBriefingTag, (s, v) => s.StrPlotBriefingTag = v)
            .Text(0x2346, 13, "DS:2346 \"briefing.pan\"", s => s.CaseBriefingPanFile, (s, v) => s.CaseBriefingPanFile = v)
            .Text(0x2353, 9, "DS:2353 plot_txt_name_b", s => s.PlotTextFile, (s, v) => s.PlotTextFile = v)
            .Flag(0xae32, "DS:ae32 briefing_shown", s => s.BriefingShown, (s, v) => s.BriefingShown = v)
            .Text(0x235c, 26, "DS:235c apartment_item_1", s => s.ApartmentItems[0], (s, v) => s.ApartmentItems[0] = v)
            .Text(0x2376, 24, "DS:2376 apartment_item_2", s => s.ApartmentItems[1], (s, v) => s.ApartmentItems[1] = v)
            .Text(0x238e, 8, "DS:238e plot_tag_first", s => s.PlotVariantTags[0], (s, v) => s.PlotVariantTags[0] = v)
            .Text(0x2396, 8, "DS:2396 plot_tag_later", s => s.PlotVariantTags[1], (s, v) => s.PlotVariantTags[1] = v)
            .Text(0x239e, 13, "DS:239e apartment_pan_name", s => s.ApartmentPanFile, (s, v) => s.ApartmentPanFile = v)
            .Text(0x23ab, 9, "DS:23ab plot_txt_name", s => s.ApartmentTextFile, (s, v) => s.ApartmentTextFile = v)
            .Int16(0xae7a, "DS:ae7a Sleepy", s => s.Sleepy, (s, v) => s.Sleepy = v)
            .Int16(0x96d6, "DS:96d6 VP", s => s.VP, (s, v) => s.VP = v);

        layout.Text(0x1674, 8, "DS:1674 \"env.sve\" (practice)", s => s.PracticeHandOffFile, (s, v) => s.PracticeHandOffFile = v);

        // ---- the cipher practice: tag and file name of each part, one after the other ----
        for (var i = 0; i < FinalState.PracticeMessageParts; i++)
        {
            var index = i;
            const int first = 0x167d, pair = 0x11, tagLength = 8, fileLength = 9;
            var tag = first + pair * index;
            layout
                .Bytes(tag, tagLength, $"DS:{tag:x4} practice tag {index}", s => s.PracticeTags[index].Bytes)
                .Text(tag + tagLength, fileLength, $"DS:{tag + tagLength:x4} practice file {index}", s => s.PracticeTagFiles[index], (s, v) => s.PracticeTagFiles[index] = v);
        }

        // ---- the code name ----
        layout
            .Text(0x23b4, 20, "DS:23b4 code name prompt", s => s.StrCodeNamePrompt, (s, v) => s.StrCodeNamePrompt = v)
            .Bytes(0x1622, 16, "DS:1622 cga_map_code_name", s => s.CodeNameColorMap);

        // ---- the case's action log: the texts one after the other, each up to the next ----
        int[] logTexts =
        [
            0x28c8, 0x28c9, 0x28cc, 0x28cf, 0x28d1, 0x28d3, 0x28e5, 0x28e7, 0x28e9, 0x28ea, 0x28fa, 0x290a, 0x290b, 0x290e, 0x2911, 0x2913,
            0x2915, 0x2917, 0x291a, 0x291d, 0x292a, 0x292b, 0x2932, 0x2943, 0x2954, 0x2956, 0x2964, 0x2975, 0x2988, 0x299b, 0x29a5, 0x29b7,
            0x29cc, 0x29e0, 0x29f7, 0x2a03,
        ];
        for (var i = 0; i < (int)LogText.Count; i++)
        {
            var index = i;
            var text = logTexts[index];
            layout.Text(text, logTexts[index + 1] - text, $"DS:{text:x4} log text {(LogText)index}", s => s.LogTexts[index], (s, v) => s.LogTexts[index] = v);
        }

        // ---- the debriefing's texts ----
        (int Address, int Length)[] debriefTexts =
        [
            (0x2504, 13), (0x2511, 44), (0x253d, 84), (0x2591, 10), (0x259b, 8), (0x25b0, 9), (0x25b9, 85), (0x260e, 50), (0x2640, 53), (0x2675, 46),
            (0x26a3, 58), (0x26dd, 12), (0x26e9, 49), (0x2727, 51), (0x275a, 52), (0x278e, 52), (0x27c2, 55), (0x27f9, 26), (0x2813, 95), (0x2872, 44),
        ];
        for (var i = 0; i < debriefTexts.Length; i++)
        {
            var index = i;
            var (text, length) = debriefTexts[index];
            layout.Text(text, length, $"DS:{text:x4} debrief text {(DebriefText)index}", s => s.DebriefTexts[index], (s, v) => s.DebriefTexts[index] = v);
        }

        // ---- the career summary ----
        (int Address, int Length)[] careerTexts =
        [
            (0x2c91, 7), (0x2c98, 14), (0x2ca6, 6), (0x2cac, 12), (0x2cb8, 7), (0x2cbf, 3), (0x2cc2, 2), (0x2cc4, 13), (0x2cd1, 6), (0x2cd7, 19),
            (0x2cea, 15), (0x2cf9, 2), (0x2cfb, 14), (0x2d09, 13), (0x2d16, 7), (0x2d1d, 24), (0x2d35, 11), (0x2d40, 7), (0x2d47, 21),
        ];
        for (var i = 0; i < careerTexts.Length; i++)
        {
            var index = i;
            var (text, length) = careerTexts[index];
            layout.Text(text, length, $"DS:{text:x4} career text {(CareerText)index}", s => s.CareerTexts[index], (s, v) => s.CareerTexts[index] = v);
        }
        layout
            .Bytes(0x5cdc, 16, "DS:5cdc digits_career", s => s.DigitsCareer.Bytes)
            .Int16(0xac68, "DS:ac68 masterminds_caught", s => s.MastermindsCaught, (s, v) => s.MastermindsCaught = v);

        // ---- the Hall of Fame screen ----
        TextTable(layout, 0x2d5c, "DS:2d5c crime_names",
        [
            (0x2afc, 11), (0x2b07, 6), (0x2b0d, 14), (0x2b1b, 13), (0x2b28, 6), (0x2b2e, 6), (0x2b34, 8), (0x2b3c, 10),
            (0x2b46, 17), (0x2b57, 8), (0x2b5f, 11), (0x2b6a, 10), (0x2b74, 12),
        ], s => s.CrimeNames);
        (int Address, int Length)[] fameTexts =
        [
            (0x32b2, 13), (0x32bf, 14), (0x32cd, 13), (0x32da, 5), (0x32df, 3), (0x32e2, 6), (0x32e8, 21),
            (0x32fd, 2), (0x32ff, 2), (0x3301, 2), (0x3303, 5), (0x3308, 17), (0x3319, 5), (0x331e, 7),
        ];
        for (var i = 0; i < fameTexts.Length; i++)
        {
            var index = i;
            var (text, length) = fameTexts[index];
            layout.Text(text, length, $"DS:{text:x4} fame text {(FameText)index}", s => s.FameTexts[index], (s, v) => s.FameTexts[index] = v);
        }
        layout.Bytes(0x5d0c, 16, "DS:5d0c digits_fame", s => s.DigitsFame.Bytes);

        // ---- the vacation after a case ----
        Rows(layout, 0x2f3e, "DS:2f3e vacation_table", FinalState.VacationRows, FinalState.VacationRowWords, s => s.VacationTable);
        TextTable(layout, 0x2f36, "DS:2f36 vacation_places", [(0x2dd6, 4), (0x2dda, 4), (0x2dde, 4), (0x2de2, 4)], s => s.VacationPlaceNames);
        (int Address, int Length)[] vacationTexts =
        [
            (0x2de6, 65), (0x2e27, 69), (0x2e6c, 61), (0x2ea9, 64), (0x2ee9, 11), (0x2ef4, 11), (0x2eff, 9),
            (0x2f08, 5), (0x2f0d, 6), (0x2f13, 6), (0x2f19, 13), (0x2f26, 5), (0x2f2b, 5), (0x2f30, 5),
        ];
        for (var i = 0; i < vacationTexts.Length; i++)
        {
            var index = i;
            var (text, length) = vacationTexts[index];
            layout.Text(text, length, $"DS:{text:x4} vacation text {(VacationText)index}", s => s.VacationTexts[index], (s, v) => s.VacationTexts[index] = v);
        }
        layout
            .Bytes(0x5cec, 16, "DS:5cec digits_vacation", s => s.DigitsVacation.Bytes)
            .ImageArray(0x8082, 7, "DS:8082 vacation_background, vacation_layers, vacation_extra", s => s.VacationImages, s => s.Images);

        // ---- the retirement ----
        layout
            .Text(0x2daa, 11, "DS:2daa \"gender.pic\"", s => s.RetirementPicture, (s, v) => s.RetirementPicture = v)
            .Text(0x2db5, 4, "DS:2db5 \"Max\"", s => s.RetirementName[0], (s, v) => s.RetirementName[0] = v)
            .Text(0x2db9, 10, "DS:2db9 \"Remington\"", s => s.RetirementName[1], (s, v) => s.RetirementName[1] = v)
            .Text(0x2dc3, 9, "DS:2dc3 \"ARRESTED\"", s => s.ArrestedStamp[0], (s, v) => s.ArrestedStamp[0] = v)
            .Text(0x2dcc, 9, "DS:2dcc \"ARRESTED\"", s => s.ArrestedStamp[1], (s, v) => s.ArrestedStamp[1] = v);

        // ---- the efficiency report ----
        layout
            .Bytes(0x1632, 16, "DS:1632 cga_map_report", s => s.ReportColorMap)
            .Bytes(0x5ccc, 16, "DS:5ccc digits_report", s => s.DigitsReport.Bytes)
            .Int16(0x7de2, "DS:7de2 objects_seen_mask", s => s.ObjectsSeenMask, (s, v) => s.ObjectsSeenMask = v)
            .Int16(0x7dee, "DS:7dee objects_taken_mask", s => s.ObjectsTakenMask, (s, v) => s.ObjectsTakenMask = v)
            .Int16(0xb308, "DS:b308 ep_earned", s => s.EpEarned, (s, v) => s.EpEarned = v)
            .Int16(0x9be8, "DS:9be8 ep_possible", s => s.EpPossible, (s, v) => s.EpPossible = v)
            .Int16(0xabfa, "DS:abfa case_flags", s => s.CaseFlags, (s, v) => s.CaseFlags = v)
            .Text(0x4013, 8, "DS:4013 \"Agent A\"", s => s.Names.AgentPlaceholder, (s, v) => s.Names.AgentPlaceholder = v)
            .Text(0x4523, 2, "DS:4523 name blank", s => s.Names.Space, (s, v) => s.Names.Space = v);
        TextTable(layout, 0x3ae0, "DS:3ae0 rank_names", RankNameTexts, s => s.RankNames);
        TextTable(layout, 0x4526, "DS:4526 first_names[0]", NameTexts[..PersonNameTexts.NamesPerTable], s => s.Names.FirstNames[0]);
        TextTable(layout, 0x4526 + 2 * PersonNameTexts.NamesPerTable, "DS:4526 first_names[1]",
            NameTexts[PersonNameTexts.NamesPerTable..(2 * PersonNameTexts.NamesPerTable)], s => s.Names.FirstNames[1]);
        TextTable(layout, 0x4626, "DS:4626 surnames", NameTexts[(2 * PersonNameTexts.NamesPerTable)..], s => s.Names.Surnames);
        for (var i = 0; i < ReportTextPlaces.Length; i++)
        {
            var index = i;
            var (text, length) = ReportTextPlaces[index];
            layout.Text(text, length, $"DS:{text:x4} report text {(ReportText)index}", s => s.ReportTexts[index], (s, v) => s.ReportTexts[index] = v);
        }
        // ---- the briefing ----
        layout
            .Bytes(0x1652, FinalState.PaletteLength, "DS:1652 ega_palette", s => s.EgaPalette)
            .Bytes(0x24b0, FinalState.PaletteLength, "DS:24b0 ega_palette_faces", s => s.EgaPaletteFaces)
            .Bytes(0x15d2, 16, "DS:15d2 cga_map_briefing", s => s.BriefingColorMap)
            .Text(0x24c1, 45, "DS:24c1 faces question", s => s.StrFacesQuestion[0], (s, v) => s.StrFacesQuestion[0] = v)
            .Text(0x24ee, 21, "DS:24ee faces question end", s => s.StrFacesQuestion[1], (s, v) => s.StrFacesQuestion[1] = v)
            .Text(0x3261, 13, "DS:3261 briefing animation file", s => s.BriefingPanFile, (s, v) => s.BriefingPanFile = v);
        for (var i = 0; i < FinalState.MastermindCount; i++)
        {
            var index = i;
            var text = MastermindNameTexts[index];
            layout
                .FixedPointer(0x2d76 + 2 * index, $"DS:2d76 mastermind_names[{index}]", text)
                .Text(text, MastermindNameTexts[index + 1] - text, $"DS:{text:x4} mastermind name {index}", s => s.MastermindNames[index], (s, v) => s.MastermindNames[index] = v);
        }
        for (var i = 0; i < BriefingTextPlaces.Length; i++)
        {
            var index = i;
            var (text, length) = BriefingTextPlaces[index];
            layout.Text(text, length, $"DS:{text:x4} briefing text {(BriefingText)index}", s => s.BriefingTexts[index], (s, v) => s.BriefingTexts[index] = v);
        }
        for (var i = 0; i < FameFileNameTexts.Length; i++)
        {
            var index = i;
            layout.Text(FameFileNameTexts[index], 9, $"DS:{FameFileNameTexts[index]:x4} fame file name", s => s.FameFileNames[index], (s, v) => s.FameFileNames[index] = v);
        }
        for (var i = 0; i < RatingNameTexts.Length; i++)
        {
            var index = i;
            var (text, length) = RatingNameTexts[index];
            layout.Text(text, length, $"DS:{text:x4} rating name {index}", s => s.RatingNames[index], (s, v) => s.RatingNames[index] = v);
        }

        // the variables of the routines every part has: message box, animation, disk prompt, joystick and mouse
        LibraryLayout.AddFinal(layout);
        // the animation player's variables (the library's)
        PanEngineLayout.AddFinal(layout, s => s.Pan);
        // the table of plots: each record's texts for its variants are pointers to texts elsewhere in the data
        for (var i = 0; i < FinalState.PlotCount; i++)
        {
            var index = i;
            layout.Part(PlotsAt + PlotSize * index, $"DS:1e9e plots[{index}]", PlotLayout, s => s.Plots[index]);
        }
        AddMenus(layout);
        // the rest of this part's copy of the campaign, and the hand-off routines' variables
        return CampaignCopyLayout.AddFinal(layout);
    }

    /// <summary>The title screen's and the main menu's texts and the save screens' variables, with where the texts lie.</summary>
    private static void AddMenus(StateLayout<FinalState> layout)
    {
        (int Address, int Length)[] titleTexts =
        [
            (0x007e, 54), (0x00b4, 55), (0x00eb, 9), (0x00fc, 11), (0x0107, 12), (0x0113, 101), (0x1c5b, 11), (0x1c66, 56), (0x1c9e, 95),
        ];
        for (var i = 0; i < titleTexts.Length; i++)
        {
            var index = i;
            var (text, length) = titleTexts[index];
            layout.Text(text, length, $"DS:{text:x4} title text {(TitleText)index}", s => s.TitleTexts[index], (s, v) => s.TitleTexts[index] = v);
        }
        int[] handOffFiles = [0x0076, 0x00f4, 0x0178, 0x0180];
        for (var i = 0; i < handOffFiles.Length; i++)
        {
            var index = i;
            layout.Text(handOffFiles[index], 8, $"DS:{handOffFiles[index]:x4} \"env.sve\" {index}", s => s.TitleHandOffFiles[index], (s, v) => s.TitleHandOffFiles[index] = v);
        }
        layout
            .Int16(0xa5a6, "DS:a5a6 entry_mode", s => s.EntryMode, (s, v) => s.EntryMode = v)
            .Int16(0x9bee, "DS:9bee", s => s.Pic, (s, v) => s.Pic = v)
            .Int16(0x9e86, "DS:9e86", s => s.MissionsPlayed, (s, v) => s.MissionsPlayed = v)
            .Bytes(0x1572, 16, "DS:1572 cga_map_title_a", s => s.TitleColorMapA)
            .Bytes(0x1582, 16, "DS:1582 cga_map_title_b", s => s.TitleColorMapB)
            .Bytes(0x15e2, 16, "DS:15e2 cga_map_title_pan", s => s.TitlePanColorMap)
            .Bytes(0x1666, 14, "DS:1666 pan_picture_name", s => s.PanPictureName.Bytes)
            .Text(0x3fc6, 11, "DS:3fc6 \"facesf.pic\"", s => s.FacesFileNames[0], (s, v) => s.FacesFileNames[0] = v)
            .Text(0x3fd1, 10, "DS:3fd1 \"faces.pic\"", s => s.FacesFileNames[1], (s, v) => s.FacesFileNames[1] = v);

        // ---- the save screens ----
        layout
            .Text(0x4834, 10, "DS:4834 save_path", s => s.Saves.SavePath, (s, v) => s.Saves.SavePath = v)
            .Flag(0x5df0, "DS:5df0 loading_flag", s => s.Saves.Loading, (s, v) => s.Saves.Loading = v)
            .Int16(0x5df2, "DS:5df2 save_error", s => s.Saves.SaveError, (s, v) => s.Saves.SaveError = v)
            .Int16(0x9f8e, "DS:9f8e slot_exists_mask", s => s.Saves.SlotExistsMask, (s, v) => s.Saves.SlotExistsMask = v)
            .Int16(0xb07c, "DS:b07c save_slot_choice", s => s.Saves.SlotChoice, (s, v) => s.Saves.SlotChoice = v)
            .Int16(0x5df4, "DS:5df4 slot_file_handle", s => s.Saves.SlotFileHandle, (s, v) => s.Saves.SlotFileHandle = v)
            .Int16(0x5df6, "DS:5df6 slot_first_word", s => s.Saves.SlotLevel, (s, v) => s.Saves.SlotLevel = v)
            .Bytes(0x9bf0, SaveScreenState.SlotBlockLength, "DS:9bf0 slot_block", s => s.Saves.SlotBlock)
            .Bytes(0x5de0, 16, "DS:5de0 digits_slot", s => s.Saves.Digits.Bytes);
        (int Address, int Length)[] saveTexts =
        [
            (0x481e, 21), (0x48ab, 21), (0x483e, 4), (0x4842, 7), (0x4849, 16), (0x4859, 12), (0x4865, 3), (0x4868, 2), (0x486a, 3),
            (0x486d, 9), (0x4876, 12), (0x4882, 12), (0x488e, 10), (0x4898, 5), (0x489d, 14), (0x48c0, 22), (0x48d6, 17), (0x48e7, 22),
            (0x48fd, 12), (0x4909, 24), (0x4921, 4), (0x4925, 13), (0x4932, 2), (0x4934, 25), (0x494d, 59), (0x4988, 57), (0x49c1, 61),
            (0x49fe, 29), (0x4a1b, 21),
        ];
        for (var i = 0; i < saveTexts.Length; i++)
        {
            var index = i;
            var (text, length) = saveTexts[index];
            layout.Text(text, length, $"DS:{text:x4} save text {(SaveText)index}", s => s.Saves.Texts[index], (s, v) => s.Saves.Texts[index] = v);
        }
    }

    /// <summary>
    /// Places the texts of the library's screens about persons (<see cref="FinalState.Dossier"/>): the sentences that
    /// announce a new fact (DS:3696 to DS:3806, each between its two neighbours), the dossier's labels (DS:3cc7 to
    /// DS:3da7) and the placeholder of a message's unknown words. The names, ranks, months and the message file and tag
    /// are the part's own entries elsewhere in this layout: they are the same texts.
    /// </summary>
    // orig: FINAL DS:3696 to DS:3da7, DS:3fde; found by pairing FINAL's announce_new_facts (5caa), show_suspect_dossier (6b82) and append_msg_log_entry (71c7) with TAC's copies (TacDgroup.Dossier.cs)
    private static void AddDossierTexts(StateLayout<FinalState> layout)
    {
        layout
            .Text(0x3696, 1, "DS:3696 PlotPrefix", s => s.Dossier.PlotPrefix, (s, v) => s.Dossier.PlotPrefix = v)
            .Text(0x3697, 52, "DS:3697 PlotSuffix", s => s.Dossier.PlotSuffix, (s, v) => s.Dossier.PlotSuffix = v)
            .Text(0x36cb, 1, "DS:36cb IdentifiedPrefix", s => s.Dossier.IdentifiedPrefix, (s, v) => s.Dossier.IdentifiedPrefix = v)
            .Text(0x36cc, 36, "DS:36cc IdentifiedMiddle", s => s.Dossier.IdentifiedMiddle, (s, v) => s.Dossier.IdentifiedMiddle = v)
            .Text(0x36f0, 2, "DS:36f0 IdentifiedSuffix", s => s.Dossier.IdentifiedSuffix, (s, v) => s.Dossier.IdentifiedSuffix = v)
            .Text(0x36f2, 34, "DS:36f2 PhotoPrefix", s => s.Dossier.PhotoPrefix, (s, v) => s.Dossier.PhotoPrefix = v)
            .Text(0x3714, 3, "DS:3714 PhotoSuffix", s => s.Dossier.PhotoSuffix, (s, v) => s.Dossier.PhotoSuffix = v)
            .Text(0x3717, 1, "DS:3717 SpottedPrefix", s => s.Dossier.SpottedPrefix, (s, v) => s.Dossier.SpottedPrefix = v)
            .Text(0x3718, 22, "DS:3718 SpottedMiddle", s => s.Dossier.SpottedMiddle, (s, v) => s.Dossier.SpottedMiddle = v)
            .Text(0x372e, 2, "DS:372e SpottedSuffix", s => s.Dossier.SpottedSuffix, (s, v) => s.Dossier.SpottedSuffix = v)
            .Text(0x3730, 1, "DS:3730 MemberPrefix", s => s.Dossier.MemberPrefix, (s, v) => s.Dossier.MemberPrefix = v)
            .Text(0x3731, 41, "DS:3731 MemberMiddle", s => s.Dossier.MemberMiddle, (s, v) => s.Dossier.MemberMiddle = v)
            .Text(0x375a, 2, "DS:375a MemberSuffix", s => s.Dossier.MemberSuffix, (s, v) => s.Dossier.MemberSuffix = v)
            .Text(0x375c, 52, "DS:375c InformationPrefix", s => s.Dossier.InformationPrefix, (s, v) => s.Dossier.InformationPrefix = v)
            .Text(0x3790, 2, "DS:3790 InformationSuffix", s => s.Dossier.InformationSuffix, (s, v) => s.Dossier.InformationSuffix = v)
            .Text(0x3792, 32, "DS:3792 RankPrefix", s => s.Dossier.RankPrefix, (s, v) => s.Dossier.RankPrefix = v)
            .Text(0x37b2, 2, "DS:37b2 RankSuffix", s => s.Dossier.RankSuffix, (s, v) => s.Dossier.RankSuffix = v)
            .Text(0x37b4, 39, "DS:37b4 RecruitingPrefix", s => s.Dossier.RecruitingPrefix, (s, v) => s.Dossier.RecruitingPrefix = v)
            .Text(0x37db, 2, "DS:37db RecruitingSuffix", s => s.Dossier.RecruitingSuffix, (s, v) => s.Dossier.RecruitingSuffix = v)
            .Text(0x37dd, 39, "DS:37dd RecruiterPrefix", s => s.Dossier.RecruiterPrefix, (s, v) => s.Dossier.RecruiterPrefix = v)
            .Text(0x3804, 2, "DS:3804 RecruiterSuffix", s => s.Dossier.RecruiterSuffix, (s, v) => s.Dossier.RecruiterSuffix = v)
            .Text(0x3cc7, 7, "DS:3cc7 Name", s => s.Dossier.Name, (s, v) => s.Dossier.Name = v)
            .Text(0x3cce, 7, "DS:3cce Rank", s => s.Dossier.Rank, (s, v) => s.Dossier.Rank = v)
            .Text(0x3cd5, 7, "DS:3cd5 Org", s => s.Dossier.Org, (s, v) => s.Dossier.Org = v)
            .Text(0x3cdc, 8, "DS:3cdc City", s => s.Dossier.City, (s, v) => s.Dossier.City = v)
            .Text(0x3ce4, 10, "DS:3ce4 Recruited", s => s.Dossier.Recruited, (s, v) => s.Dossier.Recruited = v)
            .Text(0x3cee, 5, "DS:3cee By", s => s.Dossier.By, (s, v) => s.Dossier.By = v)
            .Text(0x3cf3, 5, "DS:3cf3 In", s => s.Dossier.In, (s, v) => s.Dossier.In = v)
            .Text(0x3cf8, 2, "DS:3cf8 Period", s => s.Dossier.Period, (s, v) => s.Dossier.Period = v)
            .Text(0x3cfa, 8, "DS:3cfa Role", s => s.Dossier.Role, (s, v) => s.Dossier.Role = v)
            .Text(0x3d02, 20, "DS:3d02 RoleNotInvolved", s => s.Dossier.RoleNotInvolved, (s, v) => s.Dossier.RoleNotInvolved = v)
            .Text(0x3d16, 15, "DS:3d16 RoleUnknown", s => s.Dossier.RoleUnknown, (s, v) => s.Dossier.RoleUnknown = v)
            .Text(0x3d25, 15, "DS:3d25 UnderArrest", s => s.Dossier.UnderArrest, (s, v) => s.Dossier.UnderArrest = v)
            .Text(0x3d34, 12, "DS:3d34 Hiding", s => s.Dossier.Hiding, (s, v) => s.Dossier.Hiding = v)
            .Text(0x3d40, 9, "DS:3d40 TurnedNote", s => s.Dossier.TurnedNote, (s, v) => s.Dossier.TurnedNote = v)
            .Text(0x3d4a, 2, "DS:3d4a Quote", s => s.Dossier.Quote, (s, v) => s.Dossier.Quote = v)
            .Text(0x3d4e, 22, "DS:3d4e NotDecoded", s => s.Dossier.NotDecoded, (s, v) => s.Dossier.NotDecoded = v)
            .Text(0x3d64, 8, "DS:3d64 More", s => s.Dossier.More, (s, v) => s.Dossier.More = v)
            .Text(0x3d6d, 12, "DS:3d6d Words.Received", s => s.Dossier.Words.Received, (s, v) => s.Dossier.Words.Received = v)
            .Text(0x3d79, 12, "DS:3d79 Words.Sent", s => s.Dossier.Words.Sent, (s, v) => s.Dossier.Words.Sent = v)
            .Text(0x3d85, 2, "DS:3d85 Words.Space", s => s.Dossier.Words.Space, (s, v) => s.Dossier.Words.Space = v)
            .Text(0x3d87, 9, "DS:3d87 Words.Someone", s => s.Dossier.Words.Someone, (s, v) => s.Dossier.Words.Someone = v)
            .Text(0x3d90, 9, "DS:3d90 Words.OfThe", s => s.Dossier.Words.OfThe, (s, v) => s.Dossier.Words.OfThe = v)
            .Text(0x3d99, 5, "DS:3d99 Words.In", s => s.Dossier.Words.In, (s, v) => s.Dossier.Words.In = v)
            .Text(0x3d9e, 5, "DS:3d9e Words.On", s => s.Dossier.Words.On, (s, v) => s.Dossier.Words.On = v)
            .Text(0x3da5, 2, "DS:3da5 Words.End", s => s.Dossier.Words.End, (s, v) => s.Dossier.Words.End = v)
            .Text(0x3fde, 6, "DS:3fde Ellipsis", s => s.Dossier.Ellipsis, (s, v) => s.Dossier.Ellipsis = v);
    }

    /// <summary>
    /// Reads a 16-bit word from a file into a variable, as the original's <c>read(file, &amp;variable, 2)</c>: what the
    /// file does not supply keeps the variable's bytes.
    /// </summary>
    /// <param name="read">The file read: fills the buffer it is given from the file.</param>
    /// <param name="variable">The variable's value before the read.</param>
    /// <returns>The variable's value after the read.</returns>
    public static short ReadWord(Func<byte[], int> read, short variable)
    {
        var bytes = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, variable);
        read(bytes);
        return BinaryPrimitives.ReadInt16LittleEndian(bytes);
    }

    /// <summary>
    /// Reads the first <paramref name="count"/> records of a table from a file, as the original's
    /// <c>read(file, table, count * size)</c> into the table's bytes: what the file does not supply keeps the records' bytes.
    /// </summary>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="read">The file read: fills the buffer it is given from the file.</param>
    /// <param name="layout">The record's layout.</param>
    /// <param name="size">The record's size in bytes.</param>
    /// <param name="table">The table.</param>
    /// <param name="count">The number of records to read.</param>
    /// <exception cref="ArgumentOutOfRangeException">More records than the table holds: the original would read over the variables behind it.</exception>
    public static void ReadRecords<TRecord>(Func<byte[], int> read, StateLayout<TRecord> layout, int size, TRecord[] table, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, table.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var bytes = new byte[count * size];
        for (var i = 0; i < count; i++)
        {
            layout.Store(table[i], bytes.AsSpan(i * size, size));
        }
        read(bytes);
        for (var i = 0; i < count; i++)
        {
            layout.Load(bytes.AsSpan(i * size, size), table[i]);
        }
    }

    /// <summary>The bytes of a table of records as the original writes them to a file with <c>write(file, table, count * size)</c>.</summary>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="layout">The record's layout.</param>
    /// <param name="size">The record's size in bytes.</param>
    /// <param name="table">The records.</param>
    public static byte[] RecordBytes<TRecord>(StateLayout<TRecord> layout, int size, IReadOnlyList<TRecord> table)
    {
        var bytes = new byte[table.Count * size];
        for (var i = 0; i < table.Count; i++)
        {
            layout.Store(table[i], bytes.AsSpan(i * size, size));
        }
        return bytes;
    }

    /// <summary>A table of records (<c>struct Person ps[64]</c>): one part per record, <paramref name="size"/> bytes apart.</summary>
    private static void Records<TRecord>(StateLayout<FinalState> layout, int offset, string name, int count, int size, StateLayout<TRecord> record, Func<FinalState, TRecord[]> table)
        where TRecord : class
    {
        for (var i = 0; i < count; i++)
        {
            var index = i;
            layout.Part(offset + size * index, $"{name}[{index}]", record, s => table(s)[index]);
        }
    }

    /// <summary>A table of pointers to texts (<c>char *names[]</c>): each pointer is fixed, each text a field up to the next text.</summary>
    private static void TextTable(StateLayout<FinalState> layout, int offset, string name, (int Address, int Length)[] texts, Func<FinalState, string[]> table)
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

    /// <summary>A two-dimensional word array of the C (<c>int a[outer][inner]</c>): one field per outer index, rows one after the other.</summary>
    private static void Rows(StateLayout<FinalState> layout, int offset, string name, int outer, int inner, Func<FinalState, short[][]> table)
    {
        for (var i = 0; i < outer; i++)
        {
            var index = i;
            layout.Int16Array(offset + 2 * inner * index, inner, $"{name}[{index}]", s => table(s)[index]);
        }
    }
}
