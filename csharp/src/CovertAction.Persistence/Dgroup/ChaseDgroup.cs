using CovertAction.Core.Chase;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's car-chase program, CHASE.EXE: where each variable of
/// <see cref="ChaseState"/> lies in it. The game's start state is loaded through it from the initialised data of the
/// player's own CHASE.EXE, and the oracle tests load the state from the data segment the original had before a
/// function ran and compare the one it left.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed chase.h, docs/data-model/chase.md and symbols/names.txt (<c>D_chase_xxxx</c>);
/// <c>holdrand</c> was located by the bytes the original's <c>rand</c> changes. Not modelled: the picture loader's working data, the C library's data and the
/// stack.
/// </remarks>
public static class ChaseDgroup
{
    /// <summary>The size of one car record.</summary>
    private const int CarSize = 20;

    /// <summary>
    /// Working memory of the original that the rewrite has no use for, as ranges (start inclusive, end exclusive):
    /// buffers a function fills and uses at once, which nothing reads afterwards.
    /// </summary>
    // orig: CHASE DS:19f4 disk_letter, DS:23b2 shared_src, DS:23b6 shared_cursor, DS:23d0 itoa_buf2, DS:50ce mouse_regs, DS:5c54 bios_regs, DS:626c itoa_buf (src/c/include/chase.h)
    public static IReadOnlyList<(int Start, int End)> Scratch { get; } =
    [
        (0x19f4, 0x19f6),   // disk_letter: the letter put into the insert-disk prompt
        (0x1f53, 0x1f55),   // the C library's errno: 2 after a file that was looked for and is not there (an archive the chase tries first)
        (0x1f5e, 0x1f60),   // the C library's _doserrno, set with it
        (0x23b2, 0x23b8),   // shared_src, shared_cursor: where read_shared_or_file copies the next table from (D-400)
        (0x23d0, 0x23e0),   // itoa_buf2: a location number as text
        (0x50ce, 0x50dc),   // mouse_regs: the processor registers for one call to the mouse driver
        (0x5c54, 0x5c62),   // bios_regs: the processor registers for one call to the video BIOS
        (0x626c, 0x6276),   // itoa_buf: the dashboard's speed as text
    ];

    /// <summary>One car record (struct car).</summary>
    // orig: struct car (src/c/include/chase.h)
    private static StateLayout<Car> CarLayout { get; } = new StateLayout<Car>("car")
        .Int16(0x00, "x", c => c.X, (c, v) => c.X = v)
        .Int16(0x02, "y", c => c.Y, (c, v) => c.Y = v)
        .Int16(0x04, "dir", c => c.Dir, (c, v) => c.Dir = v)
        .Flag(0x06, "flag", c => c.Flag, (c, v) => c.Flag = v)
        .Int16(0x08, "mark_x", c => c.MarkX, (c, v) => c.MarkX = v)
        .Int16(0x0a, "mark_y", c => c.MarkY, (c, v) => c.MarkY = v)
        .Int16(0x0c, "turn", c => c.Turn, (c, v) => c.Turn = v)
        .Int16(0x0e, "alert", c => c.Alert, (c, v) => c.Alert = v)
        .Int16(0x10, "gear", c => c.Gear, (c, v) => c.Gear = v)
        .Int16(0x12, "attr", c => c.Attr, (c, v) => c.Attr = v);

    // orig: struct line_ends (src/c/include/chase.h)
    private static StateLayout<LineEnds> LineEndsLayout { get; } = new StateLayout<LineEnds>("line_ends")
        .Int16(0x00, "from", e => e.From, (e, v) => e.From = v)
        .Int16(0x02, "to", e => e.To, (e, v) => e.To = v);

    // orig: struct rect4 (src/c/include/chase.h)
    private static StateLayout<SheetRect> SheetRectLayout { get; } = new StateLayout<SheetRect>("rect4")
        .Int16(0x00, "x1", r => r.X1, (r, v) => r.X1 = v)
        .Int16(0x02, "y1", r => r.Y1, (r, v) => r.Y1 = v)
        .Int16(0x04, "x2", r => r.X2, (r, v) => r.X2 = v)
        .Int16(0x06, "y2", r => r.Y2, (r, v) => r.Y2 = v);

    // orig: struct color_pair (src/c/include/chase.h)
    private static StateLayout<ColorPair> ColorPairLayout { get; } = new StateLayout<ColorPair>("color_pair")
        .Int16(0x00, "for_light", c => c.ForLight, (c, v) => c.ForLight = v)
        .Int16(0x02, "for_dark", c => c.ForDark, (c, v) => c.ForDark = v);

    // orig: struct span (src/c/include/chase.h)
    private static StateLayout<TileSpan> TileSpanLayout { get; } = new StateLayout<TileSpan>("span")
        .Int16(0x00, "off", s => s.Off, (s, v) => s.Off = v)
        .Int16(0x02, "size", s => s.Size, (s, v) => s.Size = v);

    /// <summary>The variables of CHASE.EXE at their offsets.</summary>
    // orig: CHASE DGROUP (src/c/include/chase.h; holdrand DS:2132 is the long the library's rand changes)
    public static StateLayout<ChaseState> Layout { get; } = Build();

    private static StateLayout<ChaseState> Build()
    {
        var layout = new StateLayout<ChaseState>("CHASE DGROUP")
            // ---- initialised data ----
            .Text(0x0074, 10, "DS:0074 StrChasePic", s => s.StrChasePic, (s, v) => s.StrChasePic = v)
            .Text(0x007e, 11, "DS:007e StrStreetPic", s => s.StrStreetPic, (s, v) => s.StrStreetPic = v)
            .Text(0x0089, 76, "DS:0089 StrLostTrack", s => s.StrLostTrack, (s, v) => s.StrLostTrack = v)
            .Text(0x00d5, 3, "DS:00d5 StrHeTurns", s => s.StrHeTurns, (s, v) => s.StrHeTurns = v)
            .Text(0x00d8, 4, "DS:00d8 StrSheTurns", s => s.StrSheTurns, (s, v) => s.StrSheTurns = v)
            .Text(0x00dc, 57, "DS:00dc StrTurnsCorner", s => s.StrTurnsCorner, (s, v) => s.StrTurnsCorner = v)
            .Text(0x0115, 49, "DS:0115 StrLessObvious", s => s.StrLessObvious, (s, v) => s.StrLessObvious = v)
            .Text(0x0146, 60, "DS:0146 StrDashIntoBuilding", s => s.StrDashIntoBuilding, (s, v) => s.StrDashIntoBuilding = v)
            .Text(0x0182, 3, "DS:0182 StrHePullsOver", s => s.StrHePullsOver, (s, v) => s.StrHePullsOver = v)
            .Text(0x0185, 4, "DS:0185 StrShePullsOver", s => s.StrShePullsOver, (s, v) => s.StrShePullsOver = v)
            .Text(0x0189, 67, "DS:0189 StrPullsOver", s => s.StrPullsOver, (s, v) => s.StrPullsOver = v)
            .Text(0x01cc, 37, "DS:01cc StrRunOffRoad", s => s.StrRunOffRoad, (s, v) => s.StrRunOffRoad = v)
            .Text(0x01f1, 9, "DS:01f1 StrYouRun", s => s.StrYouRun, (s, v) => s.StrYouRun = v)
            .Text(0x01fa, 4, "DS:01fa StrHis", s => s.StrHis, (s, v) => s.StrHis = v)
            .Text(0x01fe, 4, "DS:01fe StrHer", s => s.StrHer, (s, v) => s.StrHer = v)
            .Text(0x0202, 40, "DS:0202 StrCarOffRoad", s => s.StrCarOffRoad, (s, v) => s.StrCarOffRoad = v)
            .Part(0x1600, "DS:1600 RpBackX", RasterPortLayout.Layout, s => s.RpBackX);
        Port(layout, 0x1612, "DS:1612 RpBack", s => s.RpBack, (s, v) => s.RpBack = v);
        layout
            .Bytes(0x1634, 16, "DS:1634 cga_pattern_menu_a", s => s.ColorMapA)
            .Bytes(0x1644, 160, "DS:1644 cga_pattern_menu_b", s => s.ColorMapB)
            .Bytes(0x16e4, 16, "DS:16e4 cga_pattern_cars", s => s.CgaPatternCars)
            .Int16(0x16f4, "DS:16f4 chase_mode", s => (short)s.ChaseMode, (s, v) => s.ChaseMode = (ChaseMode)v)
            .Int16(0x16f6, "DS:16f6 game_speed", s => s.GameSpeed, (s, v) => s.GameSpeed = v)
            .Int16(0x16f8, "DS:16f8 default_disk", s => s.Disk.DefaultDisk, (s, v) => s.Disk.DefaultDisk = v)
            .Bytes(0x16fa, 17, "DS:16fa palette_chase", s => s.PaletteChase)
            .Int16(0x178c, "DS:178c chase_pic_buffer", s => s.ChasePicBuffer, (s, v) => s.ChasePicBuffer = v)
            .Bytes(0x178e, 17, "DS:178e palette_plain", s => s.PalettePlain)
            .Int16Array(0x17a0, 4, "DS:17a0 dir_dx", s => s.DirDx)
            .Int16Array(0x17a8, 4, "DS:17a8 dir_dy", s => s.DirDy)
            .Int16Array(0x17b0, 5, "DS:17b0 speed_pattern", s => s.SpeedPattern);
        for (var i = 0; i < 16; i++)
        {
            var n = i;
            layout.Part(0x170c + 8 * n, $"DS:{0x170c + 8 * n:x4} gauge_rects[{n}]", SheetRectLayout, s => s.GaugeRects[n]);
        }
        for (var i = 0; i < 4; i++)
        {
            var n = i;
            layout.Part(0x1862 + 4 * n, $"DS:{0x1862 + 4 * n:x4} car_level_colors[{n}]", ColorPairLayout, s => s.CarLevelColors[n]);
            layout.Part(0x17ba + 4 * n, $"DS:{0x17ba + 4 * n:x4} road_x_ends[{n}]", LineEndsLayout, s => s.RoadXEnds[n]);
            layout.Part(0x17ca + 4 * n, $"DS:{0x17ca + 4 * n:x4} road_y_ends[{n}]", LineEndsLayout, s => s.RoadYEnds[n]);
        }
        layout
            .Int16(0x17da, "DS:17da cur_car", s => s.CurCar, (s, v) => s.CurCar = v)
            .Part(0x17dc, "DS:17dc RpSpriteX", RasterPortLayout.Layout, s => s.RpSpriteX);
        Port(layout, 0x17ee, "DS:17ee RpSprite", s => s.RpSprite, (s, v) => s.RpSprite = v);
        layout.Part(0x17f0, "DS:17f0 RpGridX", RasterPortLayout.Layout, s => s.RpGridX);
        Port(layout, 0x1802, "DS:1802 RpGrid", s => s.RpGrid, (s, v) => s.RpGrid = v);
        layout.Part(0x1804, "DS:1804 gauge_sheetX", RasterPortLayout.Layout, s => s.GaugeSheetX);
        Port(layout, 0x1816, "DS:1816 gauge_sheet", s => s.GaugeSheet, (s, v) => s.GaugeSheet = v);
        for (var i = 0; i < 4; i++)
        {
            var n = i;
            layout.Part(0x189c + 4 * n, $"DS:{0x189c + 4 * n:x4} tile_edge_x[{n}]", TileSpanLayout, s => s.TileEdgeX[n]);
            layout.Part(0x18ac + 4 * n, $"DS:{0x18ac + 4 * n:x4} tile_edge_y[{n}]", TileSpanLayout, s => s.TileEdgeY[n]);
        }
        layout
            .Text(0x1818, 9, "DS:1818 cars_pic_name", s => s.CarsPicName, (s, v) => s.CarsPicName = v)
            .Text(0x1821, 11, "DS:1821 str_tracking", s => s.StrTracking, (s, v) => s.StrTracking = v)
            .Text(0x182c, 1, "DS:182c str_empty", s => s.StrEmpty, (s, v) => s.StrEmpty = v)
            .Text(0x182d, 5, "DS:182d str_mph", s => s.StrMph, (s, v) => s.StrMph = v)
            .Text(0x1832, 10, "DS:1832 str_excellent", s => s.StrExcellent, (s, v) => s.StrExcellent = v)
            .Text(0x183c, 5, "DS:183c str_fair", s => s.StrFair, (s, v) => s.StrFair = v)
            .Text(0x1841, 4, "DS:1841 str_low", s => s.StrConspicuousness[0], (s, v) => s.StrConspicuousness[0] = v)
            .Text(0x1845, 9, "DS:1845 str_moderate", s => s.StrConspicuousness[1], (s, v) => s.StrConspicuousness[1] = v)
            .Text(0x184e, 5, "DS:184e str_high", s => s.StrConspicuousness[2], (s, v) => s.StrConspicuousness[2] = v)
            .Text(0x1853, 8, "DS:1853 str_extreme", s => s.StrConspicuousness[3], (s, v) => s.StrConspicuousness[3] = v)
            .Text(0x185b, 6, "DS:185b str_car_no", s => s.StrCarNo, (s, v) => s.StrCarNo = v)
            .Text(0x18bc, 2, "DS:18bc speed_prefix", s => s.SpeedPrefix, (s, v) => s.SpeedPrefix = v)
            .Int16(0x18be, "DS:18be shown_view_frame", s => s.ShownViewFrame, (s, v) => s.ShownViewFrame = v)
            .Int16(0x18c0, "DS:18c0 shown_needle_y", s => s.ShownNeedleY, (s, v) => s.ShownNeedleY = v)
            .Chars(0x18c2, 7, "DS:18c2 clock_text", s => s.ClockText)
            .Text(0x18ca, 4, "DS:18ca \"Loc\"", s => s.StrLoc, (s, v) => s.StrLoc = v)
            .Text(0x1872, 41, "DS:1872 str_quit_confirm2", s => s.StrQuitConfirm2, (s, v) => s.StrQuitConfirm2 = v)
            .Text(0x18ce, 21, "DS:18ce str_one_moment", s => s.Menu.StrOneMoment, (s, v) => s.Menu.StrOneMoment = v)
            .Text(0x18e3, 41, "DS:18e3 str_quit_confirm", s => s.Menu.StrQuitConfirm, (s, v) => s.Menu.StrQuitConfirm = v)
            .Int16(0x1912, "DS:1912 animation_restart_mark", s => s.Animation.CachedCity, (s, v) => s.Animation.CachedCity = v)
            .Int16(0x1914, "DS:1914 drive_number", s => s.Animation.CurrentDrive, (s, v) => s.Animation.CurrentDrive = v)
            .Int16(0x1916, "DS:1916 save_width", s => s.Animation.SaveWidth, (s, v) => s.Animation.SaveWidth = v)
            .Bytes(0x191a, 17, "DS:191a palette_menu", s => s.Animation.EgaPaletteAnim)
            .Bytes(0x192c, 17, "DS:192c palette_menu_alt", s => s.Animation.EgaPaletteAnimAlt)
            .Text(0x193d, 3, "DS:193d str_ok", s => s.Menu.StrOk, (s, v) => s.Menu.StrOk = v)
            .Text(0x1940, 13, "DS:1940 briefing_pan_name", s => s.Animation.BriefingPanName, (s, v) => s.Animation.BriefingPanName = v)
            .Text(0x194d, 4, "DS:194d bld_prefix", s => s.Animation.BldPrefix, (s, v) => s.Animation.BldPrefix = v)
            .Flag(0x1952, "DS:1952 save_extra_area", s => s.Animation.BldAnimationFlag, (s, v) => s.Animation.BldAnimationFlag = v)
            .Bytes(0x1954, Core.Menus.AnimationState.AnimationFileBytes, "DS:1954 animation_file", s => s.Animation.AnimationFile.Bytes)
            .Int16Array(0x1962, 9, "DS:1962 joystick_dir_table", s => s.Input.JoystickDirTable)
            .Text(0x1974, 10, "DS:1974 disk 1 file name", s => s.Disk.DiskFileNames[0], (s, v) => s.Disk.DiskFileNames[0] = v)
            .Text(0x197e, 9, "DS:197e disk 2 file name", s => s.Disk.DiskFileNames[1], (s, v) => s.Disk.DiskFileNames[1] = v)
            .Text(0x1987, 8, "DS:1987 disk 3 file name", s => s.Disk.DiskFileNames[2], (s, v) => s.Disk.DiskFileNames[2] = v)
            .Text(0x198f, 7, "DS:198f disk 4 file name", s => s.Disk.DiskFileNames[3], (s, v) => s.Disk.DiskFileNames[3] = v)
            .Text(0x1996, 20, "DS:1996 \"Please insert Disk \"", s => s.Disk.StrPleaseInsertDisk, (s, v) => s.Disk.StrPleaseInsertDisk = v)
            .Text(0x19aa, 23, "DS:19aa 3.5 inch line", s => s.Disk.StrIf35InsertDisk, (s, v) => s.Disk.StrIf35InsertDisk = v)
            .Text(0x19c1, 2, "DS:19c1 \")\"", s => s.Disk.StrCloseParen, (s, v) => s.Disk.StrCloseParen = v)
            .Text(0x19c3, 41, "DS:19c3 \"Press Return\" text", s => s.Disk.StrPressReturn, (s, v) => s.Disk.StrPressReturn = v)
            // disk_file_names: a table of pointers to the names above (entry 0 is never used)
            .FixedPointer(0x19ec, "DS:19ec disk_file_names[1]", 0x1974)
            .FixedPointer(0x19ee, "DS:19ee disk_file_names[2]", 0x197e)
            .FixedPointer(0x19f0, "DS:19f0 disk_file_names[3]", 0x1987)
            .FixedPointer(0x19f2, "DS:19f2 disk_file_names[4]", 0x198f)
            .Part(0x19f6, "DS:19f6 Rp1X", RasterPortLayout.Layout, s => s.Rp1X);
        Port(layout, 0x1a08, "DS:1a08 Rp1", s => s.Rp1, (s, v) => s.Rp1 = v);
        layout
            // ---- the C library's random state ----
            .UInt32(0x2132, "DS:2132 holdrand", s => s.Random.State, (s, v) => s.Random.State = v)
            // ---- uninitialised data ----
            .Int16(0x23ba, "DS:23ba cam_x", s => s.CamX, (s, v) => s.CamX = v)
            .Int16(0x23bc, "DS:23bc cam_y", s => s.CamY, (s, v) => s.CamY = v)
            .Int16(0x23be, "DS:23be cam_tile_x", s => s.CamTileX, (s, v) => s.CamTileX = v)
            .Int16(0x23c0, "DS:23c0 cam_tile_y", s => s.CamTileY, (s, v) => s.CamTileY = v)
            .Int16(0x23c2, "DS:23c2 gauge_depth", s => s.GaugeDepth, (s, v) => s.GaugeDepth = v)
            .Int16(0x23c4, "DS:23c4 gauge_dist", s => s.GaugeDist, (s, v) => s.GaugeDist = v)
            .Int16(0x23c6, "DS:23c6 needle_x", s => s.NeedleX, (s, v) => s.NeedleX = v)
            .Int16(0x23c8, "DS:23c8 needle_y", s => s.NeedleY, (s, v) => s.NeedleY = v)
            .Int16(0x23ca, "DS:23ca quarry_dx", s => s.QuarryDx, (s, v) => s.QuarryDx = v)
            .Int16(0x23cc, "DS:23cc quarry_dy", s => s.QuarryDy, (s, v) => s.QuarryDy = v)
            .Int16(0x23ce, "DS:23ce dash_scratch", s => s.DashScratch, (s, v) => s.DashScratch = v)
            .PointerFlag(0x23e0, "DS:23e0 animation_name", 0x1954, s => s.Animation.AnimationRecordNamed, (s, v) => s.Animation.AnimationRecordNamed = v)
            .Int16(0x23e6, "DS:23e6 animation_aux_1", s => s.Animation.AnimationSpeed, (s, v) => s.Animation.AnimationSpeed = v)
            .Int16(0x23e8, "DS:23e8 animation_x", s => s.Animation.AnimationX, (s, v) => s.Animation.AnimationX = v)
            .Int16(0x23ea, "DS:23ea animation_y", s => s.Animation.AnimationY, (s, v) => s.Animation.AnimationY = v)
            .Int16(0x23ec, "DS:23ec animation_aux_2", s => s.Animation.AnimationFrameLength, (s, v) => s.Animation.AnimationFrameLength = v)
            .Int16Array(0x23fe, 32, "DS:23fe menu_line_start", s => s.Menu.MenuLineStart)
            .Int16(0x243e, "DS:243e menu_text_color", s => s.Menu.MenuTextColor, (s, v) => s.Menu.MenuTextColor = v)
            .Int16(0x2440, "DS:2440 menu_item_color", s => s.Menu.MenuItemColor, (s, v) => s.Menu.MenuItemColor = v)
            .Int16(0x2442, "DS:2442 menu_line_count", s => s.Menu.MenuLineCount, (s, v) => s.Menu.MenuLineCount = v)
            .Int16(0x2444, "DS:2444 menu_selected_color", s => s.Menu.MenuSelectedColor, (s, v) => s.Menu.MenuSelectedColor = v)
            .Int16(0x3e82, "DS:3e82 joystick_raw", s => s.Input.JoystickRaw, (s, v) => s.Input.JoystickRaw = v)
            .Int16(0x3e80, "DS:3e80 alert_level", s => s.AlertLevel, (s, v) => s.AlertLevel = v)
            .Flag(0x3e84, "DS:3e84 sight_flag", s => s.SightFlag, (s, v) => s.SightFlag = v)
            .Bytes(0x3e86, 1024, "DS:3e86 message_buf", s => s.MessageText.Bytes)
            .Flag(0x4286, "DS:4286 Night", s => s.Night, (s, v) => s.Night = v)
            .Int16(0x4288, "DS:4288 diff", s => s.Diff, (s, v) => s.Diff = v)
            .Int16(0x428c, "DS:428c dest_loc", s => s.DestLoc, (s, v) => s.DestLoc = v)
            .Int16(0x428e, "DS:428e Gtime", s => s.Gtime, (s, v) => s.Gtime = v);
        for (var i = 0; i < ChaseState.CarCount; i++)
        {
            var n = i;
            layout.Part(0x4290 + CarSize * n, $"DS:{0x4290 + CarSize * n:x4} cars[{n}]", CarLayout, s => s.Cars[n]);
        }
        for (var i = 0; i < ChaseState.EventRoom; i++)
        {
            var n = i;
            var offset = 0x50e0 + CampaignLayouts.EventSize * n;
            layout.Part(offset, $"DS:{offset:x4} ev[{n}]", CampaignLayouts.Event, s => s.Ev[n]);
        }
        for (var i = 0; i < ChaseState.CastRoom; i++)
        {
            var n = i;
            var offset = 0x5c6a + CampaignLayouts.ActivitySize * n;
            layout.Part(offset, $"DS:{offset:x4} ac[{n}]", CampaignLayouts.Activity, s => s.Ac[n]);
            offset = 0x673e + CampaignLayouts.Activity2Size * n;
            layout.Part(offset, $"DS:{offset:x4} ac2[{n}]", CampaignLayouts.Activity2, s => s.Ac2[n]);
        }
        layout
            .Int16(0x5be0, "DS:5be0 NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v)
            .Int16(0x5be4, "DS:5be4 TheSeed", s => s.TheSeed, (s, v) => s.TheSeed = v)
            .Int16(0x5be6, "DS:5be6 Nloc", s => s.Nloc, (s, v) => s.Nloc = v)
            .Int16(0x5be8, "DS:5be8 ChronTime", s => s.ChronTime, (s, v) => s.ChronTime = v)
            .Int16(0x5bea, "DS:5bea GMode", s => s.GMode, (s, v) => s.GMode = v)
            .Int16(0x5bee, "DS:5bee TheCity1", s => s.TheCity1, (s, v) => s.TheCity1 = v)
            .Int16(0x5bf0, "DS:5bf0 TheCity2", s => s.TheCity2, (s, v) => s.TheCity2 = v)
            .Int16(0x5bfe, "DS:5bfe NEVENT", s => s.NEVENT, (s, v) => s.NEVENT = v)
            .Int16(0x5c06, "DS:5c06 TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v)
            .Int16(0x5c0a, "DS:5c0a TheOrg1", s => s.TheOrg1, (s, v) => s.TheOrg1 = v)
            .Int16(0x5c0e, "DS:5c0e TheOrg2", s => s.TheOrg2, (s, v) => s.TheOrg2 = v)
            .Int16(0x5c64, "DS:5c64 Doubles", s => s.Doubles, (s, v) => s.Doubles = v)
            .Int16(0x5c66, "DS:5c66 NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v)
            .Int16(0x6848, "DS:6848 InterPol", s => s.InterPol, (s, v) => s.InterPol = v)
            .Int16(0x684a, "DS:684a Sprung", s => s.Sprung, (s, v) => s.Sprung = v)
            .Int16(0x684c, "DS:684c MacGuffin", s => s.MacGuffin, (s, v) => s.MacGuffin = v);
        layout
            .Int16(0x42cc, "DS:42cc weave_toggle", s => s.WeaveToggle, (s, v) => s.WeaveToggle = v)
            .Part(0x42ce, "DS:42ce pl", CampaignLayouts.Plot, s => s.Pl)
            .Int16(0x455c, "DS:455c random_seed", s => s.RandomSeed, (s, v) => s.RandomSeed = v)
            .Int16(0x455e, "DS:455e Status", s => (short)s.Status, (s, v) => s.Status = (Options)v)
            .Flag(0x4560, "DS:4560 CGA", s => s.CGA, (s, v) => s.CGA = v);
        for (var i = 0; i < ChaseState.Cities; i++)
        {
            var n = i;
            var offset = 0x4562 + CampaignLayouts.CitySize * n;
            layout.Part(offset, $"DS:{offset:x4} ct[{n}]", CampaignLayouts.City, s => s.Ct[n]);
        }
        layout
            .Flag(0x4782, "DS:4782 menu_close_box", s => s.Menu.MenuOkButton, (s, v) => s.Menu.MenuOkButton = v)
            .Int16(0x4786, "DS:4786 Nperson", s => s.Nperson, (s, v) => s.Nperson = v)
            .Int16(0x4784, "DS:4784 watch_count", s => s.WatchCount, (s, v) => s.WatchCount = v)
            .Bytes(0x4788, ChaseState.MapTiles * ChaseState.MapTiles, "DS:4788 map_cells", s => s.MapCells)
            .Int16(0x50c8, "DS:50c8 view_frame", s => s.ViewFrame, (s, v) => s.ViewFrame = v)
            .Int16(0x50ca, "DS:50ca pan_resume_id", s => s.Animation.BackdropCode, (s, v) => s.Animation.BackdropCode = v)
            .Flag(0x50cc, "DS:50cc ram_ready", s => s.RamReady, (s, v) => s.RamReady = v)
            .Int16(0x50dc, "DS:50dc outcome", s => (short)s.Outcome, (s, v) => s.Outcome = (ChaseOutcome)v)
            .Int16(0x50de, "DS:50de frame", s => s.Frame, (s, v) => s.Frame = v)
            .Int16(0x5be2, "DS:5be2 menu_first_item", s => s.Menu.MenuFirstItem, (s, v) => s.Menu.MenuFirstItem = v)
            .Flag(0x5bf2, "DS:5bf2 animation_active", s => s.Animation.AnimationActive, (s, v) => s.Animation.AnimationActive = v)
            .Flag(0x5bf4, "DS:5bf4 NoHi", s => s.Menu.NoHi, (s, v) => s.Menu.NoHi = v)
            .Int16(0x5bf8, "DS:5bf8 menu_item_count", s => s.Menu.MenuItemCount, (s, v) => s.Menu.MenuItemCount = v)
            .Int16(0x5bfa, "DS:5bfa sound_param", s => s.SoundParam, (s, v) => s.SoundParam = v)
            .Flag(0x5bfc, "DS:5bfc campaign_mode", s => s.CampaignMode, (s, v) => s.CampaignMode = v)
            .Int16(0x5c00, "DS:5c00 map_seed", s => s.MapSeed, (s, v) => s.MapSeed = v)
            .Int16(0x5c02, "DS:5c02 start_loc", s => s.StartLoc, (s, v) => s.StartLoc = v)
            .Flag(0x5c04, "DS:5c04 menu_first_draw", s => s.Menu.MenuFirstDraw, (s, v) => s.Menu.MenuFirstDraw = v)
            .Int16(0x5c08, "DS:5c08 menu_hilite_color", s => s.Menu.MenuHiliteColor, (s, v) => s.Menu.MenuHiliteColor = v)
            .Int16(0x5c0c, "DS:5c0c menu_text_width", s => s.Menu.MenuTextWidth, (s, v) => s.Menu.MenuTextWidth = v)
            .ImageArray(0x5c12, 16, "DS:5c12 gauge_images", s => s.GaugeImages, s => s.Images)
            .Int16(0x5c62, "DS:5c62 arrival_timer", s => s.ArrivalTimer, (s, v) => s.ArrivalTimer = v)
            .Int16(0x5c68, "DS:5c68 menu_marked_items", s => s.Menu.MenuMarkedMask, (s, v) => s.Menu.MenuMarkedMask = v)
            .Flag(0x626a, "DS:626a mark_follow_all", s => s.MarkFollowAll, (s, v) => s.MarkFollowAll = v)
            .Int16(0x647a, "DS:647a pan_resume_a", s => s.Animation.BackdropArgB, (s, v) => s.Animation.BackdropArgB = v)
            .Flag(0x647c, "DS:647c menu_pause_mode", s => s.Menu.MenuPauseMode, (s, v) => s.Menu.MenuPauseMode = v)
            .Int16(0x6488, "DS:6488 pan_resume_b", s => s.Animation.BackdropArgC, (s, v) => s.Animation.BackdropArgC = v)
            .Int16(0x6480, "DS:6480 mouse_buttons", s => s.Input.MouseButtons, (s, v) => s.Input.MouseButtons = v)
            .Int16(0x6482, "DS:6482 mouse_x", s => s.Input.MouseX, (s, v) => s.Input.MouseX = v)
            .Int16(0x6484, "DS:6484 mouse_y", s => s.Input.MouseY, (s, v) => s.Input.MouseY = v)
            .Int16(0x6486, "DS:6486 mouse_count", s => s.Input.MousePressCount, (s, v) => s.Input.MousePressCount = v)
            .Int16(0x647e, "DS:647e skills", s => s.Skills, (s, v) => s.Skills = v);
        for (var i = 0; i < ChaseState.Locations; i++)
        {
            var n = i;
            var offset = 0x648a + CampaignLayouts.OrgSize * n;
            layout.Part(offset, $"DS:{offset:x4} org[{n}]", CampaignLayouts.Org, s => s.Org[n]);
        }
        return layout
            .Int16(0x66ce, "DS:66ce done_flag", s => s.DoneFlag, (s, v) => s.DoneFlag = v)
            .Chars(0x66d0, 32, "DS:66d0 menu_hotkeys", s => s.Menu.MenuHotkeys)
            .Int16(0x66f0, "DS:66f0 menu_start_item", s => s.Menu.MenuStartItem, (s, v) => s.Menu.MenuStartItem = v)
            .Int16(0x673c, "DS:673c menu_right", s => s.Menu.MenuRight, (s, v) => s.Menu.MenuRight = v)
            .Char(0x67fe, "DS:67fe gfx_letter", s => s.GfxLetter, (s, v) => s.GfxLetter = v)
            .Int16(0x6800, "DS:6800 menu_bottom", s => s.Menu.MenuBottom, (s, v) => s.Menu.MenuBottom = v)
            .Int16Array(0x6802, ChaseState.Locations, "DS:6802 loc_x", s => s.LocX)
            .Int16Array(0x6822, ChaseState.Locations, "DS:6822 loc_y", s => s.LocY)
            .Int16(0x6842, "DS:6842 alert_ratio", s => s.AlertRatio, (s, v) => s.AlertRatio = v)
            .Int16(0x6844, "DS:6844 InCity", s => s.InCity, (s, v) => s.InCity = v)
            .Int16(0x6846, "DS:6846 alert_limit", s => s.AlertLimit, (s, v) => s.AlertLimit = v)
            .Int16(0x69ce, "DS:69ce dest_x", s => s.DestX, (s, v) => s.DestX = v)
            .Int16(0x69d0, "DS:69d0 dest_y", s => s.DestY, (s, v) => s.DestY = v)
            .Int16(0x69d2, "DS:69d2 AtLoc", s => s.AtLoc, (s, v) => s.AtLoc = v);
    }

    /// <summary>A port pointer variable: it may point at any of the five port records.</summary>
    private static void Port(StateLayout<ChaseState> layout, int offset, string name, Func<ChaseState, RasterPort> get, Action<ChaseState, RasterPort> set) =>
        layout.Pointer(offset, name, get, set,
            (0x1600, s => s.RpBackX), (0x17dc, s => s.RpSpriteX), (0x17f0, s => s.RpGridX), (0x1804, s => s.GaugeSheetX), (0x19f6, s => s.Rp1X));
}
