using CovertAction.Core.Crypto;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's cryptography program, CODE.EXE: where each variable of
/// <see cref="CryptoState"/> lies in it. It serves two purposes. The game's start state is loaded through it from
/// the initialised data of the player's own CODE.EXE (the texts, colour tables and start values are not in the
/// rewrite). And the function-level oracle tests load the state from the data segment the original had before a
/// function ran, run the C# function, store the state back, and compare with the data segment the original left.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed code.h and symbols/names.txt (<c>D_code_xxxx</c>); the texts were located in
/// the program's initialised data. Not modelled: the documentation strings and tables of MicroProse's graphics
/// library, the C library's own data, the stack (which also lies in this segment), and the working buffers listed
/// in <see cref="Scratch"/>.
/// </remarks>
public static class CodeDgroup
{
    /// <summary>
    /// Working memory of the original that the rewrite has no use for, as ranges (start inclusive, end exclusive):
    /// buffers a function fills and uses at once, which nothing reads afterwards.
    /// </summary>
    // orig: CODE DS:10da char_buf, DS:124e disk_letter, DS:1ab4 itoa_buf, DS:1d80 mouse_regs, DS:1da0 bios_regs (src/c/include/code.h); DS:138d..DS:171e the
    // line end points and edge tables of the assembly routines fill_rect and draw_line_port (read from their instructions)
    public static IReadOnlyList<(int Start, int End)> Scratch { get; } =
    [
        (0x10da, 0x10dc),   // char_buf: the one-character text a single letter is drawn from
        (0x124e, 0x1250),   // disk_letter: the letter put into the insert-disk prompt
        (0x138d, 0x171f),   // the graphics library's working data
        (0x1ab4, 0x1abe),   // itoa_buf: a letter count as text
        (0x1d80, 0x1d8e),   // mouse_regs: the processor registers for one call to the mouse driver
        (0x1da0, 0x1dae),   // bios_regs: the processor registers for one call to the video BIOS
    ];

    /// <summary>The variables of CODE.EXE at their offsets.</summary>
    // orig: CODE DGROUP (src/c/include/code.h; holdrand DS:1982 is read from the instructions of the library's rand)
    public static StateLayout<CryptoState> Layout { get; } = new StateLayout<CryptoState>("CODE DGROUP")
        // ---- initialised data ----
        .Bytes(0x0f12, 16, "DS:0f12 color_map_a", s => s.ColorMapA)
        .Bytes(0x0f22, 16, "DS:0f22 color_map_b", s => s.ColorMapB)
        .Bytes(0x0f82, 16, "DS:0f82 colour map of DoCode", s => s.ColorMapStart)
        .Int16(0x1042, "DS:1042 default_disk", s => s.Disk.DefaultDisk, (s, v) => s.Disk.DefaultDisk = v)
        .Part(0x1044, "DS:1044 RpBackX", RasterPortLayout.Layout, s => s.RpBackX)
        .Pointer<RasterPort>(0x1056, "DS:1056 RpBack", s => s.RpBack, (s, v) => s.RpBack = v, (0x1044, s => s.RpBackX), (0x1250, s => s.Rp1X))
        .Chars(0x105a, CryptoState.TableLetters, "DS:105a cipher_alphabet", s => s.CipherAlphabet)
        .Chars(0x1078, CryptoState.TableLetters, "DS:1078 plain_alphabet", s => s.PlainAlphabet)
        .Chars(0x1096, CryptoState.TableLetters, "DS:1096 guess_table", s => s.GuessTable)
        .Text(0x10b6, 30, "DS:10b6 title text", s => s.StrCryptoTitle, (s, v) => s.StrCryptoTitle = v)
        .Text(0x10d4, 2, "DS:10d4 \"0\"", s => s.StrZero, (s, v) => s.StrZero = v)
        .Text(0x10d7, 2, "DS:10d7 \"-\"", s => s.StrDash, (s, v) => s.StrDash = v)
        .Text(0x10dc, 41, "DS:10dc str_quit_confirm2", s => s.StrQuitConfirm2, (s, v) => s.StrQuitConfirm2 = v)
        .Text(0x1105, 8, "DS:1105 \"PAUSED\"", s => s.StrPaused, (s, v) => s.StrPaused = v)
        .Text(0x110d, 17, "DS:110d \"MESSAGE DECODED\"", s => s.StrMessageDecoded, (s, v) => s.StrMessageDecoded = v)
        .Text(0x111e, 9, "DS:111e timer_text", s => s.TimerText, (s, v) => s.TimerText = v)
        .Text(0x1128, 21, "DS:1128 str_one_moment", s => s.Menu.StrOneMoment, (s, v) => s.Menu.StrOneMoment = v)
        .Text(0x113d, 41, "DS:113d str_quit_confirm", s => s.Menu.StrQuitConfirm, (s, v) => s.Menu.StrQuitConfirm = v)
        .Int16(0x116c, "DS:116c D_116c", s => s.Animation.CachedCity, (s, v) => s.Animation.CachedCity = v)
        .Int16(0x116e, "DS:116e current_drive", s => s.Animation.CurrentDrive, (s, v) => s.Animation.CurrentDrive = v)
        .Int16(0x1170, "DS:1170 save_width", s => s.Animation.SaveWidth, (s, v) => s.Animation.SaveWidth = v)
        .Bytes(0x1174, 17, "DS:1174 ega_palette_anim", s => s.Animation.EgaPaletteAnim)
        .Bytes(0x1186, 17, "DS:1186 ega_palette_anim_alt", s => s.Animation.EgaPaletteAnimAlt)
        .Text(0x1197, 3, "DS:1197 str_ok", s => s.Menu.StrOk, (s, v) => s.Menu.StrOk = v)
        .Text(0x119a, 13, "DS:119a briefing_pan_name", s => s.Animation.BriefingPanName, (s, v) => s.Animation.BriefingPanName = v)
        .Text(0x11a7, 4, "DS:11a7 bld_prefix", s => s.Animation.BldPrefix, (s, v) => s.Animation.BldPrefix = v)
        .Flag(0x11ac, "DS:11ac bld_animation_flag", s => s.Animation.BldAnimationFlag, (s, v) => s.Animation.BldAnimationFlag = v)
        .Bytes(0x11ae, Core.Menus.AnimationState.AnimationFileBytes, "DS:11ae animation_file", s => s.Animation.AnimationFile.Bytes)
        .Int16Array(0x11bc, 9, "DS:11bc joystick_dir_table", s => s.Input.JoystickDirTable)
        .Text(0x11ce, 10, "DS:11ce disk 1 file name", s => s.Disk.DiskFileNames[0], (s, v) => s.Disk.DiskFileNames[0] = v)
        .Text(0x11d8, 9, "DS:11d8 disk 2 file name", s => s.Disk.DiskFileNames[1], (s, v) => s.Disk.DiskFileNames[1] = v)
        .Text(0x11e1, 8, "DS:11e1 disk 3 file name", s => s.Disk.DiskFileNames[2], (s, v) => s.Disk.DiskFileNames[2] = v)
        .Text(0x11e9, 7, "DS:11e9 disk 4 file name", s => s.Disk.DiskFileNames[3], (s, v) => s.Disk.DiskFileNames[3] = v)
        .Text(0x11f0, 20, "DS:11f0 \"Please insert Disk \"", s => s.Disk.StrPleaseInsertDisk, (s, v) => s.Disk.StrPleaseInsertDisk = v)
        .Text(0x1204, 23, "DS:1204 3.5 inch line", s => s.Disk.StrIf35InsertDisk, (s, v) => s.Disk.StrIf35InsertDisk = v)
        .Text(0x121b, 2, "DS:121b \")\"", s => s.Disk.StrCloseParen, (s, v) => s.Disk.StrCloseParen = v)
        .Text(0x121d, 41, "DS:121d \"Press Return\" text", s => s.Disk.StrPressReturn, (s, v) => s.Disk.StrPressReturn = v)
        // disk_file_names: a table of four pointers to the names above
        .FixedPointer(0x1246, "DS:1246 disk_file_names[1]", 0x11ce)
        .FixedPointer(0x1248, "DS:1248 disk_file_names[2]", 0x11d8)
        .FixedPointer(0x124a, "DS:124a disk_file_names[3]", 0x11e1)
        .FixedPointer(0x124c, "DS:124c disk_file_names[4]", 0x11e9)
        .Part(0x1250, "DS:1250 Rp1X", RasterPortLayout.Layout, s => s.Rp1X)
        .Pointer<RasterPort>(0x1262, "DS:1262 Rp1", s => s.Rp1, (s, v) => s.Rp1 = v, (0x1250, s => s.Rp1X), (0x1044, s => s.RpBackX))
        // ---- the C library's random state ----
        .UInt32(0x1982, "DS:1982 holdrand", s => s.Random.State, (s, v) => s.Random.State = v)
        // ---- uninitialised data ----
        .Int16(0x1a7a, "DS:1a7a elapsed_seconds", s => s.ElapsedSeconds, (s, v) => s.ElapsedSeconds = v)
        .FlagArray(0x1a7c, CryptoState.TableLetters, "DS:1a7c letter_revealed", s => s.LetterRevealed)
        .Int16(0x1abe, "DS:1abe alphabet_len", s => s.AlphabetLen, (s, v) => s.AlphabetLen = v)
        .Int16Array(0x1ac0, CryptoState.TableLetters, "DS:1ac0 letter_count", s => s.LetterCount)
        .PointerFlag(0x1af8, "DS:1af8 animation record: name", 0x11ae, s => s.Animation.AnimationRecordNamed, (s, v) => s.Animation.AnimationRecordNamed = v)
        .Int16(0x1afe, "DS:1afe D_1afe", s => s.Animation.AnimationSpeed, (s, v) => s.Animation.AnimationSpeed = v)
        .Int16(0x1b00, "DS:1b00 animation_x", s => s.Animation.AnimationX, (s, v) => s.Animation.AnimationX = v)
        .Int16(0x1b02, "DS:1b02 animation_y", s => s.Animation.AnimationY, (s, v) => s.Animation.AnimationY = v)
        .Int16(0x1b04, "DS:1b04 D_1b04", s => s.Animation.AnimationFrameLength, (s, v) => s.Animation.AnimationFrameLength = v)
        .Int16Array(0x1b16, 32, "DS:1b16 menu_line_start", s => s.Menu.MenuLineStart)
        .Int16(0x1b56, "DS:1b56 menu_text_color", s => s.Menu.MenuTextColor, (s, v) => s.Menu.MenuTextColor = v)
        .Int16(0x1b58, "DS:1b58 menu_item_color", s => s.Menu.MenuItemColor, (s, v) => s.Menu.MenuItemColor = v)
        .Int16(0x1b5a, "DS:1b5a menu_line_count", s => s.Menu.MenuLineCount, (s, v) => s.Menu.MenuLineCount = v)
        .Int16(0x1b5c, "DS:1b5c menu_selected_color", s => s.Menu.MenuSelectedColor, (s, v) => s.Menu.MenuSelectedColor = v)
        .Int16(0x1b70, "DS:1b70 joystick_raw", s => s.Input.JoystickRaw, (s, v) => s.Input.JoystickRaw = v)
        .Bytes(0x1b72, 512, "DS:1b72 message_text", s => s.MessageText.Bytes)
        .Int16(0x1d72, "DS:1d72 CODEdiff", s => s.CODEdiff, (s, v) => s.CODEdiff = v)
        .Int16(0x1d76, "DS:1d76 random_seed", s => s.RandomSeed, (s, v) => s.RandomSeed = v)
        .Int16(0x1d78, "DS:1d78 CODEstatus", s => (short)s.CODEstatus, (s, v) => s.CODEstatus = (Options)v)
        .Int16(0x1d7a, "DS:1d7a gfx_letter", s => s.GfxLetter, (s, v) => s.GfxLetter = v)
        .Flag(0x1d7c, "DS:1d7c menu_ok_button", s => s.Menu.MenuOkButton, (s, v) => s.Menu.MenuOkButton = v)
        .Int16(0x1d7e, "DS:1d7e backdrop_code", s => s.Animation.BackdropCode, (s, v) => s.Animation.BackdropCode = v)
        .Flag(0x1d8e, "DS:1d8e CODESuccess", s => s.CODESuccess, (s, v) => s.CODESuccess = v)
        .Int16(0x1d90, "DS:1d90 CODERtime", s => s.CODERtime, (s, v) => s.CODERtime = v)
        .Int16(0x1d92, "DS:1d92 menu_first_item", s => s.Menu.MenuFirstItem, (s, v) => s.Menu.MenuFirstItem = v)
        .Flag(0x1d94, "DS:1d94 animation_active", s => s.Animation.AnimationActive, (s, v) => s.Animation.AnimationActive = v)
        .Flag(0x1d96, "DS:1d96 NoHi", s => s.Menu.NoHi, (s, v) => s.Menu.NoHi = v)
        .Int16(0x1d98, "DS:1d98 menu_item_count", s => s.Menu.MenuItemCount, (s, v) => s.Menu.MenuItemCount = v)
        .Flag(0x1d9a, "DS:1d9a menu_first_draw", s => s.Menu.MenuFirstDraw, (s, v) => s.Menu.MenuFirstDraw = v)
        .Int16(0x1d9c, "DS:1d9c menu_hilite_color", s => s.Menu.MenuHiliteColor, (s, v) => s.Menu.MenuHiliteColor = v)
        .Int16(0x1d9e, "DS:1d9e menu_text_width", s => s.Menu.MenuTextWidth, (s, v) => s.Menu.MenuTextWidth = v)
        .Int16(0x1dae, "DS:1dae menu_marked_mask", s => s.Menu.MenuMarkedMask, (s, v) => s.Menu.MenuMarkedMask = v)
        .Int16(0x1db0, "DS:1db0 backdrop_arg_b", s => s.Animation.BackdropArgB, (s, v) => s.Animation.BackdropArgB = v)
        .Flag(0x1db2, "DS:1db2 menu_pause_mode", s => s.Menu.MenuPauseMode, (s, v) => s.Menu.MenuPauseMode = v)
        .Int16(0x1db4, "DS:1db4 mouse_buttons", s => s.Input.MouseButtons, (s, v) => s.Input.MouseButtons = v)
        .Int16(0x1db6, "DS:1db6 mouse_x", s => s.Input.MouseX, (s, v) => s.Input.MouseX = v)
        .Int16(0x1db8, "DS:1db8 mouse_y", s => s.Input.MouseY, (s, v) => s.Input.MouseY = v)
        .Int16(0x1dba, "DS:1dba mouse_press_count", s => s.Input.MousePressCount, (s, v) => s.Input.MousePressCount = v)
        .Int16(0x1dbc, "DS:1dbc backdrop_arg_c", s => s.Animation.BackdropArgC, (s, v) => s.Animation.BackdropArgC = v)
        .Int16(0x1dbe, "DS:1dbe CODEQuit", s => s.CODEQuit, (s, v) => s.CODEQuit = v)
        .Chars(0x1dc0, 32, "DS:1dc0 menu_hotkeys", s => s.Menu.MenuHotkeys)
        .Int16(0x1de0, "DS:1de0 menu_start_item", s => s.Menu.MenuStartItem, (s, v) => s.Menu.MenuStartItem = v)
        .Int16(0x1de2, "DS:1de2 menu_right", s => s.Menu.MenuRight, (s, v) => s.Menu.MenuRight = v)
        .Int16(0x1de4, "DS:1de4 menu_bottom", s => s.Menu.MenuBottom, (s, v) => s.Menu.MenuBottom = v);
}
