using CovertAction.Core.Electronics;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Core.Records;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's electronics program, BUG.EXE: where each variable of
/// <see cref="ElectronicsState"/> lies in it. The start state (the tables of the circuit, the palettes) is loaded through
/// it from the player's own BUG.EXE, and the oracle tests load and compare the state through it.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed bug.h and symbols/names.txt (<c>D_bug_xxxx</c>); the campaign records have the
/// layouts of <see cref="CampaignLayouts"/> (the same as in the hand-off block); the place of the C library's
/// <c>holdrand</c> was read from the vectors of <c>random</c>, the only variable it changes. Only the variables of the
/// ported functions are listed; the others join with the first function that uses them.
/// </remarks>
public static partial class BugDgroup
{
    /// <summary>
    /// Working memory of the original that the rewrite has no use for, as ranges (start inclusive, end exclusive):
    /// buffers a function fills and uses at once, which nothing reads afterwards.
    /// </summary>
    // orig: BUG DS:2ef4 disk_letter, DS:39fe number_buf, DS:3a0e itoa_buf (names.txt); DS:779e bios_regs (found by aligning set_display_page with CODE's, whose
    // bios_regs is CODE DS:1da0)
    public static IReadOnlyList<(int Start, int End)> Scratch { get; } =
    [
        (0x2ef4, 0x2ef6),   // disk_letter: the letter put into the insert-disk prompt
        (0x3a0e, 0x3a1e),   // itoa_buf: a number of a message as text (copied into Txt at once)
        (0x39fe, 0x3a0e),   // number_buf: a number of the puzzle's result as text (itoa's buffer, copied into Txt at once)
        (0x779e, 0x77ac),   // bios_regs: the processor registers for one call to the video BIOS
    ];

    // ---- the places of the program's text tables: each entry's text and its room (end mark included) ----

    private static readonly (int Address, int Length)[] MonthTexts =
    [
        (0x198a, 4), (0x198e, 4), (0x1992, 4), (0x1996, 4), (0x199a, 4), (0x199e, 4), (0x19a2, 4), (0x19a6, 4),
        (0x19aa, 4), (0x19ae, 4), (0x19b2, 4), (0x19b6, 4),
    ];

    private static readonly (int Address, int Length)[] VariableNameTexts =
    [
        (0x21ec, 8), (0x21f4, 8), (0x21fc, 8), (0x2204, 8), (0x220c, 8), (0x2214, 8), (0x221c, 8),
    ];

    private static readonly (int Address, int Length)[] FirstNameTexts =
    [
        (0x245f, 9), (0x2468, 7), (0x246f, 6), (0x2475, 8), (0x247d, 6), (0x2483, 7), (0x248a, 8), (0x2492, 6),
        (0x2498, 8), (0x24a0, 9), (0x24a9, 5), (0x24ae, 9), (0x24b7, 9), (0x24c0, 9), (0x24c9, 5), (0x24ce, 6),
        (0x24d4, 6), (0x24da, 4), (0x24de, 6), (0x24e4, 7), (0x24eb, 6), (0x24f1, 7), (0x24f8, 6), (0x24fe, 5),
        (0x2503, 8), (0x250b, 7), (0x2512, 5), (0x2517, 6), (0x251d, 4), (0x2521, 5), (0x2526, 8), (0x252e, 5),
        (0x2533, 6), (0x2539, 7), (0x2540, 7), (0x2547, 6), (0x254d, 7), (0x2554, 6), (0x255a, 7), (0x2561, 6),
        (0x2567, 7), (0x256e, 8), (0x2576, 6), (0x257c, 7), (0x2583, 8), (0x258b, 7), (0x2592, 9), (0x259b, 5),
        (0x25a0, 9), (0x25a9, 7), (0x25b0, 8), (0x25b8, 7), (0x25bf, 6), (0x25c5, 6), (0x25cb, 8), (0x25d3, 7),
        (0x25da, 7), (0x25e1, 6), (0x25e7, 6), (0x25ed, 6), (0x25f3, 7), (0x25fa, 6), (0x2600, 8), (0x2608, 8),
        (0x2610, 7), (0x2617, 7), (0x261e, 5), (0x2623, 7), (0x262a, 7), (0x2631, 7), (0x2638, 7), (0x263f, 5),
        (0x2644, 6), (0x264a, 8), (0x2652, 8), (0x265a, 6), (0x2660, 7), (0x2667, 7), (0x266e, 9), (0x2677, 8),
        (0x267f, 5), (0x2684, 4), (0x2688, 5), (0x268d, 4), (0x2691, 4), (0x2695, 5), (0x269a, 5), (0x269f, 6),
        (0x26a5, 8), (0x26ad, 9), (0x26b6, 5), (0x26bb, 7), (0x26c2, 6), (0x26c8, 7), (0x26cf, 7), (0x26d6, 7),
        (0x26dd, 5), (0x26e2, 4), (0x26e6, 6), (0x26ec, 6), (0x26f2, 5), (0x26f7, 4), (0x26fb, 6), (0x2701, 6),
        (0x2707, 7), (0x270e, 8), (0x2716, 6), (0x271c, 7), (0x2723, 8), (0x272b, 8), (0x2733, 9), (0x273c, 6),
        (0x2742, 7), (0x2749, 5), (0x274e, 7), (0x2755, 6), (0x275b, 5), (0x2760, 6), (0x2766, 5), (0x276b, 6),
        (0x2771, 8), (0x2779, 8), (0x2781, 7), (0x2788, 6), (0x278e, 7), (0x2795, 7), (0x279c, 7), (0x27a3, 6),
    ];

    private static readonly (int Address, int Length)[] SurnameTexts =
    [
        (0x27a9, 7), (0x27b0, 6), (0x27b6, 8), (0x27be, 9), (0x27c7, 8), (0x27cf, 7), (0x27d6, 7), (0x27dd, 8),
        (0x27e5, 8), (0x27ed, 8), (0x27f5, 6), (0x27fb, 7), (0x2802, 7), (0x2809, 8), (0x2811, 10), (0x281b, 9),
        (0x2824, 7), (0x282b, 5), (0x2830, 7), (0x2837, 6), (0x283d, 6), (0x2843, 6), (0x2849, 7), (0x2850, 7),
        (0x2857, 5), (0x285c, 7), (0x2863, 7), (0x286a, 6), (0x2870, 8), (0x2878, 8), (0x2880, 6), (0x2886, 7),
        (0x288d, 6), (0x2893, 5), (0x2898, 6), (0x289e, 7), (0x28a5, 6), (0x28ab, 7), (0x28b2, 7), (0x28b9, 7),
        (0x28c0, 5), (0x28c5, 6), (0x28cb, 6), (0x28d1, 7), (0x28d8, 8), (0x28e0, 8), (0x28e8, 5), (0x28ed, 7),
        (0x28f4, 6), (0x28fa, 5), (0x28ff, 6), (0x2905, 6), (0x290b, 8), (0x2913, 8), (0x291b, 7), (0x2922, 6),
        (0x2928, 7), (0x292f, 6), (0x2935, 8), (0x293d, 7), (0x2944, 9), (0x294d, 9), (0x2956, 8), (0x295e, 9),
    ];

    /// <summary>The names of the case's items, eight kinds of eight (the rows of the table that hold texts).</summary>
    private static readonly (int Address, int Length)[] KeyPhraseTexts =
    [
        (0x1cb9, 14), (0x1cc7, 13), (0x1cd4, 16), (0x1ce4, 10), (0x1cee, 12), (0x1cfa, 14), (0x1d08, 9), (0x1d11, 11),
        (0x1d1c, 14), (0x1d2a, 19), (0x1d3d, 15), (0x1d4c, 15), (0x1d5b, 18), (0x1d6d, 12), (0x1d79, 10), (0x1d83, 13),
        (0x1d90, 13), (0x1d9d, 13), (0x1daa, 10), (0x1db4, 12), (0x1dc0, 14), (0x1dce, 13), (0x1ddb, 14), (0x1de9, 12),
        (0x1df5, 8), (0x1dfd, 9), (0x1e06, 9), (0x1e0f, 12), (0x1e1b, 8), (0x1e23, 6), (0x1e29, 11), (0x1e34, 7),
        (0x1e3b, 10), (0x1e45, 6), (0x1e4b, 8), (0x1e53, 8), (0x1e5b, 11), (0x1e66, 10), (0x1e70, 10), (0x1e7a, 10),
        (0x1e84, 2), (0x1e86, 2), (0x1e88, 2), (0x1e8a, 2), (0x1e8c, 2), (0x1e8e, 2), (0x1e90, 2), (0x1e92, 2),
        (0x1e94, 2), (0x1e96, 2), (0x1e98, 2), (0x1e9a, 2), (0x1e9c, 2), (0x1e9e, 2), (0x1ea0, 2), (0x1ea2, 2),
        (0x1ea4, 17), (0x1eb5, 13), (0x1ec2, 20), (0x1ed6, 12), (0x1ee2, 19), (0x1ef5, 13), (0x1f02, 13), (0x1f0f, 20),
    ];

    private static readonly (int Address, int Length)[] VariableValueTexts =
    [
        (0x2224, 52), (0x2258, 26), (0x2272, 26), (0x228c, 26), (0x22a6, 26), (0x22c0, 26), (0x22da, 52),
    ];

    /// <summary>The variables of BUG.EXE at their offsets.</summary>
    // orig: BUG DGROUP (src/c/include/bug.h; holdrand DS:377c located from the vectors of random)
    public static StateLayout<ElectronicsState> Layout { get; } = Build();

    private static StateLayout<ElectronicsState> Build()
    {
        var layout = new StateLayout<ElectronicsState>("BUG DGROUP")
            // ---- initialised data ----
            .Int16Array(0x0092, ElectronicsState.Directions, "DS:0092 BUGMoveX", s => s.BUGMoveX)
            .Int16Array(0x00a2, ElectronicsState.Directions, "DS:00a2 BUGMoveY", s => s.BUGMoveY);
        Rows(layout, 0x00b2, "DS:00b2 truth_tables", ElectronicsState.ChipTypes, ElectronicsState.TruthTableEntries, s => s.TruthTables);
        layout
            .Part(0x1548, "DS:1548 RpBackX", RasterPortLayout.Layout, s => s.RpBackX)
            .Pointer<RasterPort>(0x155a, "DS:155a RpBack", s => s.RpBack, (s, v) => s.RpBack = v, (0x1548, s => s.RpBackX), (0x2ef6, s => s.Rp1X))
            .Bytes(0x157c, 16, "DS:157c colour_map_a", s => s.ColorMapA)
            .Bytes(0x158c, 16, "DS:158c colour_map_b", s => s.ColorMapB)
            .Int16(0x1652, "DS:1652 diff", s => s.Diff, (s, v) => s.Diff = v)
            .Part(0x163e, "DS:163e RpSpriteX", RasterPortLayout.Layout, s => s.RpSpriteX)
            .Pointer<RasterPort>(0x1650, "DS:1650 RpSprite", s => s.RpSprite, (s, v) => s.RpSprite = v, (0x163e, s => s.RpSpriteX))
            .ImageArray(0x764c, ElectronicsState.SpriteCount, "DS:764c sprite", s => s.Sprite, s => s.Images)
            .ImageArray(0x773c, ElectronicsState.PuzzleImageCount, "DS:773c puzzle_images", s => s.PuzzleImages, s => s.Images)
            .Text(0x1760, 9, "DS:1760 clock_str", s => s.ClockStr, (s, v) => s.ClockStr = v)
            .Part(0x2ef6, "DS:2ef6 Rp1X", RasterPortLayout.Layout, s => s.Rp1X)
            .Pointer<RasterPort>(0x2f08, "DS:2f08 Rp1", s => s.Rp1, (s, v) => s.Rp1 = v, (0x2ef6, s => s.Rp1X), (0x1548, s => s.RpBackX))
            .Int16(0x5c26, "DS:5c26 Status", s => (short)s.Status, (s, v) => s.Status = (Options)v);
        for (var palette = 0; palette < ElectronicsState.PaletteCount; palette++)
        {
            var index = palette;
            layout.Bytes(0x1654 + ElectronicsState.PaletteSize * index, ElectronicsState.PaletteSize, $"DS:1654 palette {index}", s => s.Palettes[index]);
        }
        layout
            // ---- the C library's random state ----
            .UInt32(0x377c, "DS:377c holdrand", s => s.Random.State, (s, v) => s.Random.State = v);
        // ---- uninitialised data ----
        Rows(layout, 0x594c, "DS:594c BUGchip", ElectronicsState.Columns, ElectronicsState.Rows, s => s.BUGchip);
        layout
            .Int16(0x5c20, "DS:5c20 random_seed", s => s.RandomSeed, (s, v) => s.RandomSeed = v)
            .Int16(0x5c22, "DS:5c22 best_phones", s => s.BestPhones, (s, v) => s.BestPhones = v)
            .Int16(0x5c24, "DS:5c24 cursor_col", s => s.CursorCol, (s, v) => s.CursorCol = v)
            .Flag(0x5c2c, "DS:5c2c CGA", s => s.CGA, (s, v) => s.CGA = v)
            .Int16(0x5c2e, "DS:5c2e cursor_row", s => s.CursorRow, (s, v) => s.CursorRow = v)
            .Int16(0x5c30, "DS:5c30 carried_chip", s => s.CarriedChip, (s, v) => s.CarriedChip = v)
            .Int16(0x6056, "DS:6056 mode", s => (short)s.Mode, (s, v) => s.Mode = (ElectronicsMode)v)
            .Int16(0x675a, "DS:675a result_phones", s => s.ResultPhones, (s, v) => s.ResultPhones = v)
            .Int16(0x675c, "DS:675c tick_count", s => s.TickCount, (s, v) => s.TickCount = v)
            .Int16(0x747a, "DS:747a alarm_count", s => s.AlarmCount, (s, v) => s.AlarmCount = v);
        Rows(layout, 0x747e, "DS:747e connect", ElectronicsState.Columns, ElectronicsState.Lines, s => s.Connect);
        Rows(layout, 0x750a, "DS:750a pin", ElectronicsState.PinRows, ElectronicsState.Lines, s => s.Pin);
        layout
            .FlagArray(0x7622, ElectronicsState.Lines, "DS:7622 terminal_flags", s => s.TerminalFlags)
            .Int16(0x7db4, "DS:7db4 time_left", s => s.TimeLeft, (s, v) => s.TimeLeft = v)
            .Int16(0x8274, "DS:8274 chip_type_limit", s => s.ChipTypeLimit, (s, v) => s.ChipTypeLimit = v);
        Rows(layout, 0x843c, "DS:843c board", ElectronicsState.BoardWidth, ElectronicsState.BoardHeight, s => s.Board);
        layout
            .Int16(0x8c0c, "DS:8c0c phones_ok", s => s.PhonesOk, (s, v) => s.PhonesOk = v)
            .Int16(0x8f96, "DS:8f96 base_level", s => s.BaseLevel, (s, v) => s.BaseLevel = v)
            .Int16(0x7636, "DS:7636 start mark", s => s.StartMark, (s, v) => s.StartMark = v)
            // ---- the names of the picture files ----
            .Text(0x0074, 11, "DS:0074 popup sheet name", s => s.PopupSheetName, (s, v) => s.PopupSheetName = v)
            .Text(0x007f, 10, "DS:007f board sheet name", s => s.BoardSheetName, (s, v) => s.BoardSheetName = v)
            .Text(0x240a, 11, "DS:240a women's faces sheet name", s => s.FacesWomenName, (s, v) => s.FacesWomenName = v)
            .Text(0x2415, 10, "DS:2415 men's faces sheet name", s => s.FacesMenName, (s, v) => s.FacesMenName = v)
            .Text(0x16a9, 9, "DS:16a9 bugs sheet name", s => s.BugsSheetName, (s, v) => s.BugsSheetName = v)
            // ---- the texts of the puzzle's result ----
            .Text(0x16b2, 12, "DS:16b2 str_car_tracer", s => s.StrCarTracer, (s, v) => s.StrCarTracer = v)
            .Text(0x16be, 12, "DS:16be str_percent_complete", s => s.StrPercentComplete, (s, v) => s.StrPercentComplete = v)
            .Text(0x16ca, 10, "DS:16ca str_alarm", s => s.StrAlarm, (s, v) => s.StrAlarm = v)
            .Text(0x16d4, 23, "DS:16d4 str_taps_completed", s => s.StrTapsCompleted, (s, v) => s.StrTapsCompleted = v)
            .Text(0x16eb, 23, "DS:16eb str_tracer_installed", s => s.StrTracerInstalled, (s, v) => s.StrTracerInstalled = v)
            .Text(0x1702, 15, "DS:1702 str_time_expired", s => s.StrTimeExpired, (s, v) => s.StrTimeExpired = v)
            .Text(0x1711, 17, "DS:1711 str_phones_tapped", s => s.StrPhonesTapped, (s, v) => s.StrPhonesTapped = v)
            .Text(0x1722, 3, "DS:1722 str_no", s => s.StrNo, (s, v) => s.StrNo = v)
            .Text(0x1725, 9, "DS:1725 str_alarms", s => s.StrAlarms, (s, v) => s.StrAlarms = v)
            .Text(0x172e, 41, "DS:172e str_quit_confirm", s => s.StrQuitConfirm, (s, v) => s.StrQuitConfirm = v)
            .Text(0x1757, 9, "DS:1757 str_paused", s => s.StrPaused, (s, v) => s.StrPaused = v);

        // ---- the routines shared with the other parts: message box, animation, disk prompt, joystick ----
        // (places found by aligning BUG's copies of quit_prompt, draw_menu_box, start_animation, CheckForDisk and the
        // input routines with CODE's, instruction by instruction)
        layout
            .Int16(0x163c, "DS:163c expected_disk", s => s.Disk.DefaultDisk, (s, v) => s.Disk.DefaultDisk = v)
            .Text(0x2dae, 21, "DS:2dae str_one_moment", s => s.Menu.StrOneMoment, (s, v) => s.Menu.StrOneMoment = v)
            .Text(0x2dc3, 41, "DS:2dc3 str_quit_confirm2", s => s.Menu.StrQuitConfirm, (s, v) => s.Menu.StrQuitConfirm = v)
            .Int16(0x2df2, "DS:2df2 (CODE D_116c)", s => s.Animation.CachedCity, (s, v) => s.Animation.CachedCity = v)
            .Int16(0x2df4, "DS:2df4 current_drive", s => s.Animation.CurrentDrive, (s, v) => s.Animation.CurrentDrive = v)
            .Int16(0x2df6, "DS:2df6 save_width", s => s.Animation.SaveWidth, (s, v) => s.Animation.SaveWidth = v)
            .Bytes(0x2dfa, 17, "DS:2dfa palette of the animation", s => s.Animation.EgaPaletteAnim)
            .Bytes(0x2e0c, 17, "DS:2e0c palette of the animation, second", s => s.Animation.EgaPaletteAnimAlt)
            .Text(0x2e1d, 3, "DS:2e1d \"OK\"", s => s.Menu.StrOk, (s, v) => s.Menu.StrOk = v)
            .Text(0x2e20, 13, "DS:2e20 briefing_pan_name", s => s.Animation.BriefingPanName, (s, v) => s.Animation.BriefingPanName = v)
            .Text(0x2e2d, 4, "DS:2e2d bld_prefix", s => s.Animation.BldPrefix, (s, v) => s.Animation.BldPrefix = v)
            .Flag(0x2e32, "DS:2e32 bld animation flag", s => s.Animation.BldAnimationFlag, (s, v) => s.Animation.BldAnimationFlag = v)
            .Bytes(0x2e34, Core.Menus.AnimationState.AnimationFileBytes, "DS:2e34 animation file name", s => s.Animation.AnimationFile.Bytes)
            .Int16Array(0x2e42, 9, "DS:2e42 joystick direction table", s => s.Input.JoystickDirTable)
            .Text(0x2e74, 10, "DS:2e74 disk 1 file name", s => s.Disk.DiskFileNames[0], (s, v) => s.Disk.DiskFileNames[0] = v)
            .Text(0x2e7e, 9, "DS:2e7e disk 2 file name", s => s.Disk.DiskFileNames[1], (s, v) => s.Disk.DiskFileNames[1] = v)
            .Text(0x2e87, 8, "DS:2e87 disk 3 file name", s => s.Disk.DiskFileNames[2], (s, v) => s.Disk.DiskFileNames[2] = v)
            .Text(0x2e8f, 7, "DS:2e8f disk 4 file name", s => s.Disk.DiskFileNames[3], (s, v) => s.Disk.DiskFileNames[3] = v)
            .Text(0x2e96, 20, "DS:2e96 \"Please insert Disk \"", s => s.Disk.StrPleaseInsertDisk, (s, v) => s.Disk.StrPleaseInsertDisk = v)
            .Text(0x2eaa, 23, "DS:2eaa 3.5 inch line", s => s.Disk.StrIf35InsertDisk, (s, v) => s.Disk.StrIf35InsertDisk = v)
            .Text(0x2ec1, 2, "DS:2ec1 \")\"", s => s.Disk.StrCloseParen, (s, v) => s.Disk.StrCloseParen = v)
            .Text(0x2ec3, 41, "DS:2ec3 \"Press Return\" text", s => s.Disk.StrPressReturn, (s, v) => s.Disk.StrPressReturn = v)
            // disk_files: a table of pointers to the names above, indexed by the disk number 1..4
            .FixedPointer(0x2eec, "DS:2eec disk_files[1]", 0x2e74)
            .FixedPointer(0x2eee, "DS:2eee disk_files[2]", 0x2e7e)
            .FixedPointer(0x2ef0, "DS:2ef0 disk_files[3]", 0x2e87)
            .FixedPointer(0x2ef2, "DS:2ef2 disk_files[4]", 0x2e8f)
            .PointerFlag(0x3a68, "DS:3a68 animation_file", 0x2e34, s => s.Animation.AnimationRecordNamed, (s, v) => s.Animation.AnimationRecordNamed = v)
            .Int16(0x3a6e, "DS:3a6e (CODE D_1afe)", s => s.Animation.AnimationSpeed, (s, v) => s.Animation.AnimationSpeed = v)
            .Int16(0x3a70, "DS:3a70 animation_x", s => s.Animation.AnimationX, (s, v) => s.Animation.AnimationX = v)
            .Int16(0x3a72, "DS:3a72 animation_y", s => s.Animation.AnimationY, (s, v) => s.Animation.AnimationY = v)
            .Int16(0x3a74, "DS:3a74 (CODE D_1b04)", s => s.Animation.AnimationFrameLength, (s, v) => s.Animation.AnimationFrameLength = v)
            .Int16Array(0x3a86, 32, "DS:3a86 menu_line_start", s => s.Menu.MenuLineStart)
            .Int16(0x3ac6, "DS:3ac6 menu_text_color", s => s.Menu.MenuTextColor, (s, v) => s.Menu.MenuTextColor = v)
            .Int16(0x3ac8, "DS:3ac8 menu_item_color", s => s.Menu.MenuItemColor, (s, v) => s.Menu.MenuItemColor = v)
            .Int16(0x3aca, "DS:3aca menu_line_count", s => s.Menu.MenuLineCount, (s, v) => s.Menu.MenuLineCount = v)
            .Int16(0x3acc, "DS:3acc menu_selected_color", s => s.Menu.MenuSelectedColor, (s, v) => s.Menu.MenuSelectedColor = v)
            .Int16(0x5540, "DS:5540 joystick_raw", s => s.Input.JoystickRaw, (s, v) => s.Input.JoystickRaw = v)
            .Bytes(0x5542, 512, "DS:5542 Txt", s => s.MessageText.Bytes)
            .Flag(0x6052, "DS:6052 menu_ok_button", s => s.Menu.MenuOkButton, (s, v) => s.Menu.MenuOkButton = v)
            .Int16(0x6758, "DS:6758 popup_face (backdrop_code)", s => s.Animation.BackdropCode, (s, v) => s.Animation.BackdropCode = v)
            .Int16(0x7262, "DS:7262 menu_first_item", s => s.Menu.MenuFirstItem, (s, v) => s.Menu.MenuFirstItem = v)
            .Flag(0x7474, "DS:7474 animation_active", s => s.Animation.AnimationActive, (s, v) => s.Animation.AnimationActive = v)
            .Flag(0x7476, "DS:7476 NoHi", s => s.Menu.NoHi, (s, v) => s.Menu.NoHi = v)
            .Int16(0x747c, "DS:747c menu_item_count", s => s.Menu.MenuItemCount, (s, v) => s.Menu.MenuItemCount = v)
            .Flag(0x763e, "DS:763e menu_first_draw", s => s.Menu.MenuFirstDraw, (s, v) => s.Menu.MenuFirstDraw = v)
            .Int16(0x7642, "DS:7642 menu_hilite_color", s => s.Menu.MenuHiliteColor, (s, v) => s.Menu.MenuHiliteColor = v)
            .Int16(0x7646, "DS:7646 menu_text_width", s => s.Menu.MenuTextWidth, (s, v) => s.Menu.MenuTextWidth = v)
            .Int16(0x77b2, "DS:77b2 menu_marked_mask", s => s.Menu.MenuMarkedMask, (s, v) => s.Menu.MenuMarkedMask = v)
            .Chars(0x8208, 32, "DS:8208 menu_hotkeys", s => s.Menu.MenuHotkeys)
            .Int16(0x7fba, "DS:7fba backdrop_arg_b", s => s.Animation.BackdropArgB, (s, v) => s.Animation.BackdropArgB = v)
            .Flag(0x7fbc, "DS:7fbc paused (menu_pause_mode)", s => s.Menu.MenuPauseMode, (s, v) => s.Menu.MenuPauseMode = v)
            .Int16(0x8200, "DS:8200 backdrop_arg_c", s => s.Animation.BackdropArgC, (s, v) => s.Animation.BackdropArgC = v)
            .Int16(0x8206, "DS:8206 done_flag", s => s.DoneFlag, (s, v) => s.DoneFlag = v)
            .Int16(0x8228, "DS:8228 menu_start_item", s => s.Menu.MenuStartItem, (s, v) => s.Menu.MenuStartItem = v)
            .Int16(0x8276, "DS:8276 menu_right", s => s.Menu.MenuRight, (s, v) => s.Menu.MenuRight = v)
            .Int16(0x843a, "DS:843a menu_bottom", s => s.Menu.MenuBottom, (s, v) => s.Menu.MenuBottom = v);

        // ---- this part's copy of the campaign ----
        Records(layout, 0x0172, "DS:0172 ps", 16, CampaignTables.PersonCount, CampaignLayouts.Person, s => s.Ps);
        Records(layout, 0x0572, "DS:0572 lc", 12, CampaignTables.LocationCount, CampaignLayouts.Location, s => s.Lc);
        Records(layout, 0x5e32, "DS:5e32 ct", 34, CampaignTables.WorldSize, CampaignLayouts.City, s => s.Ct);
        Records(layout, 0x7fc0, "DS:7fc0 org", 36, CampaignTables.WorldSize, CampaignLayouts.Org, s => s.Org);
        Records(layout, 0x77b4, "DS:77b4 ac", 48, ElectronicsState.CastCapacity, CampaignLayouts.Activity, s => s.Ac);
        Records(layout, 0x1172, "DS:1172 cl", CampaignLayouts.ClueSize, CampaignTables.ClueCount, CampaignLayouts.Clue, s => s.Cl);
        Records(layout, 0x6058, "DS:6058 sn", CampaignLayouts.SynopsisSize, CampaignTables.SynopsisCount, CampaignLayouts.Synopsis, s => s.Sn);
        Records(layout, 0x6760, "DS:6760 ev", CampaignLayouts.EventSize, ElectronicsState.EventCapacity, CampaignLayouts.Event, s => s.Ev);
        layout
            .Part(0x5992, "DS:5992 pl", CampaignLayouts.Plot, s => s.Pl)
            .Text(0x2b4a, 9, "DS:2b4a clock_text", s => s.ClockText, (s, v) => s.ClockText = v)
            .Int16(0x594a, "DS:594a Gtime", s => s.Gtime, (s, v) => s.Gtime = v)
            .Int16(0x6054, "DS:6054 Nperson", s => s.Nperson, (s, v) => s.Nperson = v)
            .Int16(0x7268, "DS:7268 Nloc", s => s.Nloc, (s, v) => s.Nloc = v)
            .Int16(0x7260, "DS:7260 NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v)
            .Int16(0x726a, "DS:726a ChronTime", s => s.ChronTime, (s, v) => s.ChronTime = v)
            .Int16(0x77b0, "DS:77b0 NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v)
            .Int16(0x8c0e, "DS:8c0e InCity", s => s.InCity, (s, v) => s.InCity = v)
            .Int16(0x8fd8, "DS:8fd8 AtLoc", s => s.AtLoc, (s, v) => s.AtLoc = v)
            .Int16Array(0x8c10, CampaignTables.WorldSize, "DS:8c10 active", s => s.Active)
            .Int16Array(0x8c30, CampaignTables.WorldSize, "DS:8c30 org_counters", s => s.OrgCounters);
        Records(layout, 0x8378, "DS:8378 ac2", CampaignLayouts.Activity2Size, ElectronicsState.CastCapacity, CampaignLayouts.Activity2, s => s.Ac2);

        // ---- the intelligence screens ----
        layout
            .Bytes(0x1aa0, 32, "DS:1aa0 bit_count", s => s.BitCount)
            .Int16(0x5944, "DS:5944 popup_anchor_x", s => s.PopupAnchorX, (s, v) => s.PopupAnchorX = v)
            .Int16(0x5948, "DS:5948 popup_anchor_y", s => s.PopupAnchorY, (s, v) => s.PopupAnchorY = v)
            .Int16(0x675e, "DS:675e coded_flag", s => s.CodedFlag, (s, v) => s.CodedFlag = v)
            .ImageArray(0x5c28, 2, "DS:5c28 popup_frame_image, building_image", s => s.PopupPictures, s => s.Images)
            .Bytes(0x159c, 16, "DS:159c board colour map", s => s.BoardColorMap)
            .Text(0x19ba, 23, "DS:19ba coded message heading", s => s.IntelTexts.CodedMessage, (s, v) => s.IntelTexts.CodedMessage = v)
            .Text(0x19d1, 23, "DS:19d1 meeting notes heading", s => s.IntelTexts.MeetingNotes, (s, v) => s.IntelTexts.MeetingNotes = v)
            .Text(0x19e8, 24, "DS:19e8 new information", s => s.IntelTexts.NewInformation, (s, v) => s.IntelTexts.NewInformation = v)
            .Text(0x1a00, 28, "DS:1a00 additional information", s => s.IntelTexts.AdditionalInformation, (s, v) => s.IntelTexts.AdditionalInformation = v)
            .Text(0x1a1c, 2, "DS:1a1c after the name", s => s.IntelTexts.AfterName, (s, v) => s.IntelTexts.AfterName = v)
            .Text(0x1a1e, 6, "DS:1a1e before what was learned", s => s.IntelTexts.Learned, (s, v) => s.IntelTexts.Learned = v)
            .Text(0x1a24, 4, "DS:1a24 on", s => s.IntelTexts.On, (s, v) => s.IntelTexts.On = v)
            .Text(0x1a28, 2, "DS:1a28 before the day", s => s.IntelTexts.BeforeDay, (s, v) => s.IntelTexts.BeforeDay = v)
            .Text(0x1a2a, 8, "DS:1a2a of the", s => s.IntelTexts.OfThe, (s, v) => s.IntelTexts.OfThe = v)
            .Text(0x1a32, 4, "DS:1a32 in", s => s.IntelTexts.In, (s, v) => s.IntelTexts.In = v)
            .Text(0x1a36, 2, "DS:1a36 line end", s => s.IntelTexts.LineEnd, (s, v) => s.IntelTexts.LineEnd = v)
            .Text(0x1a38, 1, "DS:1a38 empty", s => s.IntelTexts.Empty, (s, v) => s.IntelTexts.Empty = v)
            .Text(0x1a39, 2, "DS:1a39 face only", s => s.IntelTexts.FaceOnly, (s, v) => s.IntelTexts.FaceOnly = v)
            .Text(0x1a3b, 5, "DS:1a3b building in", s => s.IntelTexts.BuildingIn, (s, v) => s.IntelTexts.BuildingIn = v);
        TextTable(layout, 0x1ac0, "DS:1ac0 month_names", MonthTexts, s => s.MonthNames);
        for (var row = 0; row < ElectronicsState.ItemKinds; row++)
        {
            var kind = row;
            TextTable(layout, 0x1f44 + 2 * ElectronicsState.ItemNames * kind, $"DS:1f44 key_phrases[{kind}]",
                KeyPhraseTexts[(ElectronicsState.ItemNames * kind)..(ElectronicsState.ItemNames * (kind + 1))], s => s.KeyPhrases[kind]);
        }
        layout
            .Text(0x20e6, 3, "DS:20e6 str_amount_00", s => s.StrAmount00, (s, v) => s.StrAmount00 = v)
            .Text(0x20e9, 4, "DS:20e9 str_amount_000", s => s.StrAmount000, (s, v) => s.StrAmount000 = v)
            .Text(0x2422, 6, "DS:2422 \" ... \"", s => s.StrOmitted, (s, v) => s.StrOmitted = v);
        (int Address, int Length)[] buildingKinds = [(0x2428, 9), (0x2431, 7), (0x2438, 11), (0x2443, 12), (0x244f, 8)];
        for (var i = 0; i < buildingKinds.Length; i++)
        {
            var level = i;
            var (address, length) = buildingKinds[level];
            layout.Text(address, length, $"DS:{address:x4} building kind {level + 1}", s => s.BuildingKinds[level], (s, v) => s.BuildingKinds[level] = v);
        }

        // ---- the library's text and screen routines ----
        layout
            .Bytes(0x8278, ILibraryTextState.LineCapacity, "DS:8278 token_tail_buf", s => s.TokenTail.Bytes)
            .Text(0x2344, 48, "DS:2344 clearance notice", s => s.ClearanceNotice, (s, v) => s.ClearanceNotice = v)
            .Int16(0x7640, "DS:7640 TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v)
            .Text(0x23f8, 9, "DS:23f8 message file name", s => s.MessageFileName, (s, v) => s.MessageFileName = v)
            .Text(0x2401, 9, "DS:2401 message tag template", s => s.MessageTemplate, (s, v) => s.MessageTemplate = v)
            .Bytes(0x3a2e, 9, "DS:3a2e msg_tag_buf", s => s.MessageTag.Bytes)
            .Text(0x233b, 2, "DS:233b line separator", s => s.LineSeparator, (s, v) => s.LineSeparator = v)
            .Char(0x8438, "DS:8438 GMode", s => s.GMode, (s, v) => s.GMode = v)
            .Bytes(0x15fc, 16, "DS:15fc photo colour map", s => s.PhotoColorMap)
            .Text(0x2457, 8, "DS:2457 agent placeholder", s => s.Names.AgentPlaceholder, (s, v) => s.Names.AgentPlaceholder = v)
            .Text(0x2967, 2, "DS:2967 name space", s => s.Names.Space, (s, v) => s.Names.Space = v);
        TextTable(layout, 0x230e, "DS:230e message variable names", VariableNameTexts, s => s.VariableNameTexts);
        TextTable(layout, 0x231c, "DS:231c message variable values", VariableValueTexts, s => s.VariableValueTexts);
        for (var row = 0; row < 2; row++)
        {
            var sex = row;
            TextTable(layout, 0x296a + 2 * PersonNameTexts.NamesPerTable * sex, $"DS:296a first names[{sex}]",
                FirstNameTexts[(PersonNameTexts.NamesPerTable * sex)..(PersonNameTexts.NamesPerTable * (sex + 1))], s => s.Names.FirstNames[sex]);
        }
        TextTable(layout, 0x2a6a, "DS:2a6a surnames", SurnameTexts, s => s.Names.Surnames);
        AddDossierTexts(layout);
        // the rest of this part's copy of the campaign, and the hand-off routines' variables
        return CampaignCopyLayout.AddBug(layout);
    }

    /// <summary>A table of campaign records (<c>struct Person ps[64]</c>): one part per record, <paramref name="size"/> bytes apart.</summary>
    private static void Records<TRecord>(StateLayout<ElectronicsState> layout, int offset, string name, int size, int count, StateLayout<TRecord> record, Func<ElectronicsState, TRecord[]> table)
        where TRecord : class
    {
        for (var i = 0; i < count; i++)
        {
            var index = i;
            layout.Part(offset + size * index, $"{name}[{index}]", record, s => table(s)[index]);
        }
    }

    /// <summary>
    /// A table of texts of the program (<c>char *month_names[12]</c>): a fixed pointer per entry, and the text it points
    /// at as an entry of the state's array.
    /// </summary>
    private static void TextTable(StateLayout<ElectronicsState> layout, int offset, string name, (int Address, int Length)[] texts, Func<ElectronicsState, string[]> table)
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
    private static void Rows(StateLayout<ElectronicsState> layout, int offset, string name, int outer, int inner, Func<ElectronicsState, short[][]> table)
    {
        for (var i = 0; i < outer; i++)
        {
            var index = i;
            layout.Int16Array(offset + 2 * inner * index, inner, $"{name}[{index}]", s => table(s)[index]);
        }
    }
}
