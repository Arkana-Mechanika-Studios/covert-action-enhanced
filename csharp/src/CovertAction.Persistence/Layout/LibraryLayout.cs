using CovertAction.Core.Shared;

namespace CovertAction.Persistence.Layout;

/// <summary>
/// Where a program keeps its copies of the variables of the routines every program has (the message box, the
/// animation beside it, the insert-disk prompt, the joystick and mouse): the part of a data segment's layout that
/// goes with <see cref="ILibraryState"/>. One table of places per program; the fields are the same for all.
/// </summary>
/// <remarks>
/// <para>
/// The places were found by pairing each program's copies of the shared routines (<c>quit_prompt</c>, which GAME calls
/// <c>popup_menu</c>, <c>draw_menu_box</c>, <c>start_animation</c>, <c>CheckForDisk</c>, the key and joystick routines;
/// docs/source-map/shared-functions.csv says which copies are the same code) with the cryptography program's,
/// instruction by instruction: where CODE's copy names CODE DS:1d92, GAME's names GAME DS:8ec8. The same pairing
/// gives, for the electronics program, exactly the places its layout was given by hand.
/// </para>
/// <para>
/// Not part of this: the two screen ports and their pointers (each program has more ports, and the pointers may
/// name any of them) and the text buffer <c>Txt</c> (its length is the program's): the program's own layout places
/// them. A variable a program's shared routines never touch has no place here (the building raid has its own box
/// drawing, so its menu line table is with that).
/// </para>
/// </remarks>
public static class LibraryLayout
{
    /// <summary>A text of the program: where it starts and how many bytes it has with its end mark.</summary>
    private readonly record struct TextAt(int Address, int Length)
    {
        public static implicit operator TextAt((int Address, int Length) place) => new(place.Address, place.Length);
    }

    /// <summary>The places of one program's copies; null where its shared routines do not use the variable.</summary>
    private sealed record Addresses(
        string Module,
        int? ColorMapA = null, int? ColorMapB = null, int? DefaultDisk = null, TextAt? StrOneMoment = null, TextAt? StrQuitConfirm = null,
        int? CachedCity = null, int? CurrentDrive = null, int? SaveWidth = null, int? EgaPaletteAnim = null, int? EgaPaletteAnimAlt = null,
        TextAt? StrOk = null, TextAt? BriefingPanName = null, TextAt? BldPrefix = null, int? BldAnimationFlag = null, int? AnimationFile = null,
        int? JoystickDirTableCentre = null, TextAt? StrPleaseInsertDisk = null, TextAt? StrIf35InsertDisk = null, TextAt? StrCloseParen = null,
        TextAt? StrPressReturn = null, int? DiskFileNames = null, TextAt[]? DiskFiles = null, int? DiskLetter = null, int? AnimationRecord = null,
        int? AnimationSpeed = null, int? AnimationX = null, int? AnimationY = null, int? AnimationFrameLength = null, int? MenuLineStart = null, int? MenuTextColor = null,
        int? MenuItemColor = null, int? MenuLineCount = null, int? MenuSelectedColor = null, int? JoystickRaw = null, int? Status = null,
        int? MenuOkButton = null, int? BackdropCode = null, int? MenuFirstItem = null, int? AnimationActive = null, int? NoHi = null,
        int? MenuItemCount = null, int? MenuFirstDraw = null, int? MenuHiliteColor = null, int? MenuTextWidth = null, int? BiosRegs = null,
        int? MenuMarkedMask = null, int? BackdropArgB = null, int? MenuPauseMode = null, int? MouseButtons = null, int? MouseX = null,
        int? MouseY = null, int? MousePressCount = null, int? BackdropArgC = null, int? Quit = null, int? MenuHotkeys = null,
        int? MenuStartItem = null, int? MenuRight = null, int? MenuBottom = null, int? MouseRegs = null);

    // orig: GAME DGROUP, the library's variables (GAME's names: src/c/include/game.h; picface, FaceX, FaceY, NoWait, NoHi and Status are MicroProse's)
    private static readonly Addresses Game = new("GAME",
        ColorMapA: 0x149e, ColorMapB: 0x14ae, DefaultDisk: 0x16c0, StrOneMoment: (0x42b6, 21), StrQuitConfirm: (0x42cb, 41), CachedCity: 0x42fa,
        CurrentDrive: 0x42fc, SaveWidth: 0x42fe, EgaPaletteAnim: 0x4302, EgaPaletteAnimAlt: 0x4314, StrOk: (0x4325, 3),
        BriefingPanName: (0x4328, 13), BldPrefix: (0x4335, 4), BldAnimationFlag: 0x433a, AnimationFile: 0x433c, JoystickDirTableCentre: 0x4352,
        StrPleaseInsertDisk: (0x41e2, 20), StrIf35InsertDisk: (0x41f6, 23), StrCloseParen: (0x420d, 2), StrPressReturn: (0x420f, 41),
        DiskFileNames: 0x4236, DiskFiles: [(0x41c0, 10), (0x41ca, 9), (0x41d3, 8), (0x41db, 7)], DiskLetter: 0x4240, AnimationRecord: 0x574a,
        AnimationSpeed: 0x5750, AnimationX: 0x5752, AnimationY: 0x5754, AnimationFrameLength: 0x5756, MenuLineStart: 0x5768, MenuTextColor: 0x57a8,
        MenuItemColor: 0x57aa, MenuLineCount: 0x57ac, MenuSelectedColor: 0x57ae, JoystickRaw: 0x7200, Status: 0x789a, MenuOkButton: 0x7cbe,
        BackdropCode: 0x83c2, MenuFirstItem: 0x8ec8, AnimationActive: 0x90e2, NoHi: 0x93ec, MenuItemCount: 0x967e, MenuFirstDraw: 0x968e,
        MenuHiliteColor: 0x9692, MenuTextWidth: 0x9696, BiosRegs: 0x9790, MenuMarkedMask: 0x97a4, BackdropArgB: 0x9fac, MenuPauseMode: 0x9fae,
        BackdropArgC: 0x9fb2, Quit: 0xa3ee, MenuHotkeys: 0xa3f0, MenuStartItem: 0xa410, MenuRight: 0xa45c, MenuBottom: 0xa620);

    // orig: FINAL DGROUP, the library's variables (FINAL's names: src/c/include/final.h, docs/data-model/final.md section 5)
    private static readonly Addresses Final = new("FINAL",
        ColorMapA: 0x1592, ColorMapB: 0x15a2, DefaultDisk: 0x1664, StrOneMoment: (0x4778, 21), StrQuitConfirm: (0x478d, 41), CachedCity: 0x47bc,
        CurrentDrive: 0x47be, SaveWidth: 0x47c0, EgaPaletteAnim: 0x47c4, EgaPaletteAnimAlt: 0x47d6, StrOk: (0x47e7, 3),
        BriefingPanName: (0x47ea, 13), BldPrefix: (0x47f7, 4), BldAnimationFlag: 0x47fc, AnimationFile: 0x47fe, JoystickDirTableCentre: 0x4814,
        StrPleaseInsertDisk: (0x4a72, 20), StrIf35InsertDisk: (0x4a86, 23), StrCloseParen: (0x4a9d, 2), StrPressReturn: (0x4a9f, 41),
        DiskFileNames: 0x4ac6, DiskFiles: [(0x4a50, 10), (0x4a5a, 9), (0x4a63, 8), (0x4a6b, 7)], DiskLetter: 0x4ad0, AnimationRecord: 0x5d76,
        AnimationSpeed: 0x5d7c, AnimationX: 0x5d7e, AnimationY: 0x5d80, AnimationFrameLength: 0x5d82, MenuLineStart: 0x5d94, MenuTextColor: 0x5dd4,
        MenuItemColor: 0x5dd6, MenuLineCount: 0x5dd8, MenuSelectedColor: 0x5dda, JoystickRaw: 0x79e0, Status: 0x8090, MenuOkButton: 0x84b4,
        BackdropCode: 0x8bb8, MenuFirstItem: 0x96cc, AnimationActive: 0x98e0, NoHi: 0x9bec, MenuItemCount: 0x9e7e, MenuFirstDraw: 0x9e8e,
        MenuHiliteColor: 0x9e92, MenuTextWidth: 0x9e96, BiosRegs: 0x9f90, MenuMarkedMask: 0x9fa4, BackdropArgB: 0xa7ae, MenuPauseMode: 0xa7b0,
        MouseButtons: 0xa7b4, MouseX: 0xa7b6, MouseY: 0xa7b8, MousePressCount: 0xa7ba, BackdropArgC: 0xa7bc, Quit: 0xabf8, MenuHotkeys: 0xabfc,
        MenuStartItem: 0xac1c, MenuRight: 0xac6a, MenuBottom: 0xae2e, MouseRegs: 0x8bba);

    // orig: TAC DGROUP, the library's variables (TAC's names: src/c/include/tac.h)
    private static readonly Addresses Tac = new("TAC",
        ColorMapA: 0x1b08, ColorMapB: 0x1b18, DefaultDisk: 0x1bcc, StrOneMoment: (0x3656, 21), StrQuitConfirm: (0x366b, 41), CachedCity: 0x369a,
        CurrentDrive: 0x369c, SaveWidth: 0x369e, EgaPaletteAnim: 0x36a2, EgaPaletteAnimAlt: 0x36b4, StrOk: (0x36c5, 3),
        BriefingPanName: (0x36c8, 13), BldPrefix: (0x36d5, 4), BldAnimationFlag: 0x36da, AnimationFile: 0x36dc, JoystickDirTableCentre: 0x36f2,
        StrPleaseInsertDisk: (0x373e, 20), StrIf35InsertDisk: (0x3752, 23), StrCloseParen: (0x3769, 2), StrPressReturn: (0x376b, 41),
        DiskFileNames: 0x3792, DiskFiles: [(0x371c, 10), (0x3726, 9), (0x372f, 8), (0x3737, 7)], DiskLetter: 0x379c, AnimationRecord: 0x72fe,
        AnimationSpeed: 0x7304, AnimationX: 0x7306, AnimationY: 0x7308, AnimationFrameLength: 0x730a, MenuLineStart: 0x731c, MenuTextColor: 0x735c,
        MenuItemColor: 0x735e, MenuLineCount: 0x7360, MenuSelectedColor: 0x7362, JoystickRaw: 0x8dd4, Status: 0x9b68, MenuOkButton: 0xa1e0,
        BackdropCode: 0xa8e6, MenuFirstItem: 0xb3f2, AnimationActive: 0xbb52, NoHi: 0xbb5a, MenuItemCount: 0xbb5e, MenuFirstDraw: 0xbb78,
        MenuHiliteColor: 0xbb80, MenuTextWidth: 0xbb86, MenuMarkedMask: 0xbdf4, BackdropArgB: 0xc5fc, MenuPauseMode: 0xc5fe,
        BackdropArgC: 0xc844, Quit: 0xc86c, MenuHotkeys: 0xc96c, MenuStartItem: 0xc99a, MenuRight: 0xc9e8, MenuBottom: 0xcbb0);

    /// <summary>Adds the campaign program's (GAME.EXE) copies of the library's variables to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    public static StateLayout<TState> AddGame<TState>(StateLayout<TState> layout)
        where TState : class, ILibraryState => Add(layout, Game);

    /// <summary>Adds the menus program's (FINAL.EXE) copies of the library's variables to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    public static StateLayout<TState> AddFinal<TState>(StateLayout<TState> layout)
        where TState : class, ILibraryState => Add(layout, Final);

    /// <summary>Adds the building raid program's (TAC.EXE) copies of the library's variables to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    public static StateLayout<TState> AddTac<TState>(StateLayout<TState> layout)
        where TState : class, ILibraryState => Add(layout, Tac);

    /// <summary>GAME's working memory of the library routines, which the rewrite has no use for: ranges an oracle test does not compare.</summary>
    public static IReadOnlyList<(int Start, int End)> GameScratch { get; } = Scratch(Game);

    /// <summary>
    /// FINAL's working memory of the library routines, and of the C library beneath them: its error words (errno,
    /// set when DOS refuses a file or memory), and what its buffered text files leave behind (the stream records,
    /// the readers' line buffer, and the buffers themselves in the near heap), as FINAL's port found them.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> FinalScratch { get; } =
    [
        .. Scratch(Final),
        (0x5680, 0x5690),   // the C library's file tables
        (0x56f3, 0x56f5),   // errno
        (0x56fe, 0x5700),   // _doserrno
        (0x5740, 0x5890),   // the stream records (FILE) and their flags
        (0x79b0, 0x79e2),   // the text readers' line buffer, in front of Txt
        (0xbb00, 0xc000),   // the near heap: the streams' 512-byte buffers
    ];

    /// <summary>
    /// GAME's working memory of the picture readers (the file buffer and the picture decoder, which the rewrite
    /// replaces by reading a file at once and decoding it with Content.PictureFile: D-12, D-13), as GAME's port found
    /// it: ranges an oracle test of a routine that loads a picture or an animation does not compare.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> GamePictureScratch { get; } =
    [
        (0x57b0, 0x6fd0),   // file_buffer_handle and the decoder's tables, up to the picture's width and height
        (0x7191, 0x71ce),   // the decoder's state
        (0x8ed8, 0x8eda),   // file_read_ptr
        (0x9da8, 0x9fac),   // file_refill_function and file_block_buffer (512 bytes)
        (0xa870, 0xa9b0),   // the row buffer (320 bytes)
    ];

    /// <summary>TAC's working memory of the library routines.</summary>
    public static IReadOnlyList<(int Start, int End)> TacScratch { get; } = Scratch(Tac);

    private const int RegisterBlockBytes = 14;
    private const int PaletteBytes = 17;
    private const int ColorMapBytes = 16;
    private const int JoystickDirections = 9;

    /// <summary>The letter put into the insert-disk prompt, and the register blocks of one call to the video BIOS and to the mouse driver.</summary>
    private static List<(int Start, int End)> Scratch(Addresses at)
    {
        var ranges = new List<(int Start, int End)>();
        if (at.DiskLetter is { } letter)
        {
            ranges.Add((letter, letter + 2));
        }
        if (at.BiosRegs is { } bios)
        {
            ranges.Add((bios, bios + RegisterBlockBytes));
        }
        if (at.MouseRegs is { } mouse)
        {
            ranges.Add((mouse, mouse + RegisterBlockBytes));
        }
        return ranges;
    }

    private static StateLayout<TState> Add<TState>(StateLayout<TState> layout, Addresses at)
        where TState : class, ILibraryState
    {
        string Name(int address, string name) => $"DS:{address:x4} {name}";

        void Word(int? address, string name, Func<TState, short> get, Action<TState, short> set)
        {
            if (address is { } a)
            {
                layout.Int16(a, Name(a, name), get, set);
            }
        }

        void Flag(int? address, string name, Func<TState, bool> get, Action<TState, bool> set)
        {
            if (address is { } a)
            {
                layout.Flag(a, Name(a, name), get, set);
            }
        }

        void Text(TextAt? place, string name, Func<TState, string> get, Action<TState, string> set)
        {
            if (place is { } p)
            {
                layout.Text(p.Address, p.Length, Name(p.Address, name), get, set);
            }
        }

        void Bytes(int? address, int length, string name, Func<TState, byte[]> buffer)
        {
            if (address is { } a)
            {
                layout.Bytes(a, length, Name(a, name), buffer);
            }
        }

        // ---- the part's options and its "leave" word ----
        Word(at.Status, "Status", s => (short)s.Status, (s, v) => s.Status = (Options)v);
        Word(at.Quit, "quit word", s => s.Quit, (s, v) => s.Quit = v);
        Bytes(at.ColorMapA, ColorMapBytes, "color_map_a", s => s.ColorMapA);
        Bytes(at.ColorMapB, ColorMapBytes, "color_map_b", s => s.ColorMapB);

        // ---- the message box ----
        Text(at.StrOneMoment, "str_one_moment", s => s.Menu.StrOneMoment, (s, v) => s.Menu.StrOneMoment = v);
        Text(at.StrQuitConfirm, "str_quit_confirm", s => s.Menu.StrQuitConfirm, (s, v) => s.Menu.StrQuitConfirm = v);
        Text(at.StrOk, "str_ok", s => s.Menu.StrOk, (s, v) => s.Menu.StrOk = v);
        if (at.MenuLineStart is { } lines)
        {
            layout.Int16Array(lines, Core.Menus.MenuState.MaxLines, Name(lines, "menu_line_start"), s => s.Menu.MenuLineStart);
        }
        Word(at.MenuTextColor, "menu_text_color", s => s.Menu.MenuTextColor, (s, v) => s.Menu.MenuTextColor = v);
        Word(at.MenuItemColor, "menu_item_color", s => s.Menu.MenuItemColor, (s, v) => s.Menu.MenuItemColor = v);
        Word(at.MenuLineCount, "menu_line_count", s => s.Menu.MenuLineCount, (s, v) => s.Menu.MenuLineCount = v);
        Word(at.MenuSelectedColor, "menu_selected_color", s => s.Menu.MenuSelectedColor, (s, v) => s.Menu.MenuSelectedColor = v);
        Flag(at.MenuOkButton, "menu_ok_button", s => s.Menu.MenuOkButton, (s, v) => s.Menu.MenuOkButton = v);
        Word(at.MenuFirstItem, "menu_first_item", s => s.Menu.MenuFirstItem, (s, v) => s.Menu.MenuFirstItem = v);
        Flag(at.NoHi, "NoHi", s => s.Menu.NoHi, (s, v) => s.Menu.NoHi = v);
        Word(at.MenuItemCount, "menu_item_count", s => s.Menu.MenuItemCount, (s, v) => s.Menu.MenuItemCount = v);
        Flag(at.MenuFirstDraw, "menu_first_draw", s => s.Menu.MenuFirstDraw, (s, v) => s.Menu.MenuFirstDraw = v);
        Word(at.MenuHiliteColor, "menu_hilite_color", s => s.Menu.MenuHiliteColor, (s, v) => s.Menu.MenuHiliteColor = v);
        Word(at.MenuTextWidth, "menu_text_width", s => s.Menu.MenuTextWidth, (s, v) => s.Menu.MenuTextWidth = v);
        Word(at.MenuMarkedMask, "menu_marked_mask", s => s.Menu.MenuMarkedMask, (s, v) => s.Menu.MenuMarkedMask = v);
        Flag(at.MenuPauseMode, "menu_pause_mode (NoWait)", s => s.Menu.MenuPauseMode, (s, v) => s.Menu.MenuPauseMode = v);
        if (at.MenuHotkeys is { } hotkeys)
        {
            layout.Chars(hotkeys, Core.Menus.MenuState.MaxLines, Name(hotkeys, "menu_hotkeys"), s => s.Menu.MenuHotkeys);
        }
        Word(at.MenuStartItem, "menu_start_item", s => s.Menu.MenuStartItem, (s, v) => s.Menu.MenuStartItem = v);
        Word(at.MenuRight, "menu_right", s => s.Menu.MenuRight, (s, v) => s.Menu.MenuRight = v);
        Word(at.MenuBottom, "menu_bottom", s => s.Menu.MenuBottom, (s, v) => s.Menu.MenuBottom = v);

        // ---- the animation beside the box, and the face behind it ----
        Word(at.CachedCity, "(CODE D_116c)", s => s.Animation.CachedCity, (s, v) => s.Animation.CachedCity = v);
        Word(at.CurrentDrive, "current_drive", s => s.Animation.CurrentDrive, (s, v) => s.Animation.CurrentDrive = v);
        Word(at.SaveWidth, "save_width", s => s.Animation.SaveWidth, (s, v) => s.Animation.SaveWidth = v);
        Bytes(at.EgaPaletteAnim, PaletteBytes, "ega_palette_anim", s => s.Animation.EgaPaletteAnim);
        Bytes(at.EgaPaletteAnimAlt, PaletteBytes, "ega_palette_anim_alt", s => s.Animation.EgaPaletteAnimAlt);
        Text(at.BriefingPanName, "briefing_pan_name", s => s.Animation.BriefingPanName, (s, v) => s.Animation.BriefingPanName = v);
        Text(at.BldPrefix, "bld_prefix", s => s.Animation.BldPrefix, (s, v) => s.Animation.BldPrefix = v);
        Flag(at.BldAnimationFlag, "bld_animation_flag", s => s.Animation.BldAnimationFlag, (s, v) => s.Animation.BldAnimationFlag = v);
        if (at.AnimationFile is { } file)
        {
            // 14 bytes in every program: the joystick's direction table follows (docs/original-bugs.md CA-21)
            const int nameBytes = Core.Menus.AnimationState.AnimationFileBytes;
            layout.Bytes(file, nameBytes, Name(file, "animation_file"), s => s.Animation.AnimationFile.Bytes);
            if (at.AnimationRecord is { } record)
            {
                layout.PointerFlag(record, Name(record, "animation record: name"), file, s => s.Animation.AnimationRecordNamed, (s, v) => s.Animation.AnimationRecordNamed = v);
            }
        }
        Word(at.AnimationSpeed, "animation record +6 (CODE D_1afe)", s => s.Animation.AnimationSpeed, (s, v) => s.Animation.AnimationSpeed = v);
        Word(at.AnimationX, "animation_x", s => s.Animation.AnimationX, (s, v) => s.Animation.AnimationX = v);
        Word(at.AnimationY, "animation_y", s => s.Animation.AnimationY, (s, v) => s.Animation.AnimationY = v);
        Word(at.AnimationFrameLength, "animation record +12 (CODE D_1b04)", s => s.Animation.AnimationFrameLength, (s, v) => s.Animation.AnimationFrameLength = v);
        Flag(at.AnimationActive, "animation_active", s => s.Animation.AnimationActive, (s, v) => s.Animation.AnimationActive = v);
        Word(at.BackdropCode, "backdrop_code (picface)", s => s.Animation.BackdropCode, (s, v) => s.Animation.BackdropCode = v);
        Word(at.BackdropArgB, "backdrop_arg_b (FaceX)", s => s.Animation.BackdropArgB, (s, v) => s.Animation.BackdropArgB = v);
        Word(at.BackdropArgC, "backdrop_arg_c (FaceY)", s => s.Animation.BackdropArgC, (s, v) => s.Animation.BackdropArgC = v);

        // ---- the insert-disk prompt ----
        Word(at.DefaultDisk, "default_disk", s => s.Disk.DefaultDisk, (s, v) => s.Disk.DefaultDisk = v);
        Text(at.StrPleaseInsertDisk, "\"Please insert Disk \"", s => s.Disk.StrPleaseInsertDisk, (s, v) => s.Disk.StrPleaseInsertDisk = v);
        Text(at.StrIf35InsertDisk, "3.5 inch line", s => s.Disk.StrIf35InsertDisk, (s, v) => s.Disk.StrIf35InsertDisk = v);
        Text(at.StrCloseParen, "\")\"", s => s.Disk.StrCloseParen, (s, v) => s.Disk.StrCloseParen = v);
        Text(at.StrPressReturn, "\"Press Return\" text", s => s.Disk.StrPressReturn, (s, v) => s.Disk.StrPressReturn = v);
        if (at is { DiskFileNames: { } table, DiskFiles: { } files })
        {
            // disk_file_names: a table of pointers to the four names, indexed by the disk number 1..4
            for (var disk = 0; disk < files.Length; disk++)
            {
                var index = disk;
                var pointer = table + 2 * (index + 1);
                layout.Text(files[index].Address, files[index].Length, Name(files[index].Address, $"disk {index + 1} file name"),
                    s => s.Disk.DiskFileNames[index], (s, v) => s.Disk.DiskFileNames[index] = v);
                layout.FixedPointer(pointer, Name(pointer, $"disk_file_names[{index + 1}]"), files[index].Address);
            }
        }

        // ---- joystick and mouse ----
        if (at.JoystickDirTableCentre is { } centre)
        {
            // the routine reads the 3x3 table from its middle element
            var start = centre - 2 * (JoystickDirections / 2);
            layout.Int16Array(start, JoystickDirections, Name(start, "joystick_dir_table"), s => s.Input.JoystickDirTable);
        }
        Word(at.JoystickRaw, "joystick_raw", s => s.Input.JoystickRaw, (s, v) => s.Input.JoystickRaw = v);
        Word(at.MouseButtons, "mouse_buttons", s => s.Input.MouseButtons, (s, v) => s.Input.MouseButtons = v);
        Word(at.MouseX, "mouse_x", s => s.Input.MouseX, (s, v) => s.Input.MouseX = v);
        Word(at.MouseY, "mouse_y", s => s.Input.MouseY, (s, v) => s.Input.MouseY = v);
        Word(at.MousePressCount, "mouse_press_count", s => s.Input.MousePressCount, (s, v) => s.Input.MousePressCount = v);
        return layout;
    }
}
