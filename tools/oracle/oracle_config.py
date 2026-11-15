"""Oracle configuration in one place: library cuts, pointer arguments of boundary calls,
and the value ranges of the typed model used to bound fuzzing.

Everything here is derived from the matched C (the reconstruction's files and headers)
and the MSC 5.1 library; each entry says where from.
"""

# --------------------------------------------------------------------------------------------
# 1. Library cuts: C library routines recorded as ONE boundary call (the interrupts beneath are
#    not executed). Key = names.txt name. Each entry:
#      nargs   argument words when the caller's clean-up cannot be read (default)
#      ret     "long" (DX:AX) or "int" (AX)
#      ptrs    {argument index: "str" (C string) | ("mem", length or "argN")} captured at call time
#      family  further routines whose own vectors run the library internals for real: their writes to
#              the library's private data are listed as "ignore" ranges in those vectors
#    Semantics of each routine (what it returns, what it writes) are in run_function.Script.library.
LIBRARY_CUTS = {
    "_time": dict(nargs=1, ret="long", ptrs={}, family=("___tzset", "_tzset", "__isindst", "__dtoxtime")),
    "_open": dict(nargs=2, ret="int", ptrs={0: "str"}, family=()),
    "_dos_open": dict(nargs=3, ret="int", ptrs={0: "str"}, family=()),
    "_read": dict(nargs=3, ret="int", ptrs={}, family=()),
    "_write": dict(nargs=3, ret="int", ptrs={1: ("mem", "arg2")}, family=()),
    "_close": dict(nargs=1, ret="int", ptrs={}, family=()),
    "_lseek": dict(nargs=4, ret="long", ptrs={}, family=()),
    "_access": dict(nargs=2, ret="int", ptrs={0: "str"}, family=()),
}
# MicroProse's own assembly graphics/disk library linked into the module (frame 0000 of CODE,
# `int far fill_rect();` etc. in the C): recorded as ONE call at the routine (far, stack arguments)
# instead of the driver slots / INT 21h it uses beneath. Its private working data (CODE DS:1395..1709
# span tables) is then not written. The routine's own vector still runs it for real. The answer is
# the scripted int input (find_file_on_disk: 0 found, -1 missing; the others return nothing used).
MODULE_CUTS = {
    "_fill_rect": dict(nargs=6, ret="int", ptrs={}, family=()),        # (port, x, y, w, h, colour) 0576
    "_draw_line_port": dict(nargs=6, ret="int", ptrs={}, family=()),   # (port, x1, y1, x2, y2, colour) 097a
    "_find_file_on_disk": dict(nargs=2, ret="int", ptrs={0: "str"}, family=()),  # (name, drive) 0002
}
CUTS = {**LIBRARY_CUTS, **MODULE_CUTS}
# Where each MODULE_CUTS routine sits, by module and address (frame:offset), not by names.txt name:
# BUG's own `_fill_rect` (00e8:6f5a) is the corner variant that calls the real fill, which BUG names
# `_fill_rect_port` (0000:0602, same routine as FINAL/TAC 0602). Modules not listed have no module cuts.
_LIB_0576 = {"_fill_rect": (0, 0x0576), "_draw_line_port": (0, 0x097a), "_find_file_on_disk": (0, 0x0002)}
_LIB_0602 = {"_fill_rect": (0, 0x0602), "_draw_line_port": (0, 0x0a06), "_find_file_on_disk": (0, 0x0002)}
MODULE_CUT_AT = {"CODE": _LIB_0576, "CHASE": _LIB_0576, "GAME": _LIB_0576,
                 "FINAL": _LIB_0602, "TAC": _LIB_0602, "BUG": _LIB_0602}


def cut_labels(module, functions):
    """label -> cut name for one module: C library cuts by names.txt name, module cuts by address.
    `functions` = oracle_common.module_info(module)["functions"]."""
    out = {g["label"]: g["name"] for g in functions.values() if g["name"] in LIBRARY_CUTS}
    at = {(g["frame"], g["offset"]): g["label"] for g in functions.values()}
    for name, where in MODULE_CUT_AT.get(module.upper(), {}).items():
        out[at[where]] = name       # KeyError = the table names an address that is not a function
    return out

# Not cut yet (they keep stdio/heap state in DGROUP that the port does not model): _fopen, _fclose,
# _fread, _fwrite, _fscanf, _fseek, _ftell, _malloc, _free. None of them is linked into CODE.

# --------------------------------------------------------------------------------------------
# 2. Pointer arguments of driver thunks (stack convention), captured at call time and written
#    in place of the word as {"ptr": n, "str": base64} (C string, NUL excluded) or
#    {"ptr": n, "mem": base64} (fixed length). Evidence: the reconstructed callers.
DRIVER_POINTERS = {
    "_draw_string": {3: "str"},                          # (port, x, y, text): code 2958 draw_text
    "_set_palette": {0: ("mem", 17)},                    # 16 EGA registers + overscan (code 19ce)
    "_set_color_maps": {0: ("mem", 16), 1: ("mem", 16)},  # color_map_a/b, 16 bytes (code.h)
}
# Driver slots whose stand-in returns what the real driver returns, computed by run_function._drawn_area:
# the MGRAPHIC image draw gives DX:AX = its record of the area drawn (x, y, w, h after clipping), which
# _draw_image_tracked reads to record the spans for the next restore. (module, slot offset).
DRAWN_AREA_SLOTS = {("GAME", 0x134), ("FINAL", 0x1c0), ("CHASE", 0x134)}
# Far data segments of the module image (frames relative to the load segment, [start, end)): tracked like
# far memory, so a function's reads before writes go to far_in and its writes to far_out (linear keys;
# the vector's segments.far_data gives each segment's absolute paragraph). FINAL 0fb4 / GAME 10e2 = the
# animation engine's segment F_0fb4_0000 / F_10e2_0000 (script stack, sound queue, sprites), up to DGROUP.
FAR_DATA = {"FINAL": [(0x0fb4, 0x10d8)], "GAME": [(0x10e2, 0x1206)]}
# Interrupt services that take DS:DX data: recorded per call as "args_mem": {"3": base64}
# (index 3 = DX in the [ax,bx,cx,dx,si,di] argument list).
INT_POINTERS = {
    (0x21, 0x09): "dollar",     # print string ending in '$'
    (0x21, 0x0f): ("mem", 37),  # FCB open
    (0x21, 0x11): ("mem", 37),  # FCB find first (CODE 0002 _find_file_on_disk)
    (0x21, 0x12): ("mem", 37),
    (0x21, 0x3c): "str", (0x21, 0x3d): "str", (0x21, 0x41): "str", (0x21, 0x43): "str",
    (0x21, 0x4e): "str", (0x21, 0x5b): "str",
    (0x21, 0x40): ("mem", "cx"),  # write CX bytes
}

# --------------------------------------------------------------------------------------------
# 3. Typed-model ranges for fuzzing. A fuzzed case is "realistic": true only when every mutated
#    global, shared byte and argument stays inside these ranges; any other mutation (bytes that
#    are not modelled variables, pointers, driver tables) gives "realistic": false.
#
# DGROUP globals: offset -> (name, element count, element width, allowed, evidence)
#   allowed = (lo, hi) signed range, or a tuple of allowed values ("set", values).
LETTERS = tuple(range(0x41, 0x5b))
TEXT = (0x00, 0x0a) + tuple(range(0x20, 0x7f))     # printable ASCII, newline, NUL
GLOBALS = {
    "CODE": {
        0x105a: ("cipher_alphabet", 26, 1, ("set", LETTERS), "code.h: shuffled 'A'..'Z' (setup_cipher 0cf6)"),
        0x1078: ("plain_alphabet", 26, 1, ("set", LETTERS), "code.h: 'A'..'Z'"),
        0x1096: ("guess_table", 26, 1, ("set", (0x20,) + LETTERS), "code.h: NO_GUESS 0x20 or 'A'..'Z' (155a)"),
        0x1a7a: ("elapsed_seconds", 1, 2, (-32768, 32767), "int, any value (draw_timer 192e)"),
        0x1a7c: ("letter_revealed", 26, 2, (0, 1), "flags set by reveal_hint_letter 132d"),
        0x1abe: ("alphabet_len", 1, 2, (0, 26), "loops b < alphabet_len over 28-entry tables; ALPHABET 26"),
        0x1ac0: ("letter_count", 26, 2, (0, 512), "letters counted in the 512-byte message (setup_cipher)"),
        0x1b72: ("message_text", 511, 1, ("set", TEXT),
                 "MSG_BYTES 512 of message text: printable ASCII and NUL (copied from SHARED+200, then "
                 "strupr; setup_cipher 0cf6; real sessions hold lower case before strupr)"),
        0x1982: ("holdrand", 2, 2, (-32768, 32767), "MSC rand state (ARCHITECTURE.md): any 32-bit value"),
        0x1d72: ("CODEdiff", 1, 2, (-3, 5), "clamp(level - skill + 1, -3, 5) in DoCode 0b5a"),
        0x1d76: ("random_seed", 1, 2, (0, 0x7fff), "time(0) & 0x7fff (seed_random 2a72)"),
        0x1d78: ("CODEstatus", 1, 1, (0, 255), "OPT_* bit flags byte (state.h)"),
        0x1d8e: ("CODESuccess", 1, 2, (0, 1), "0/1 (crypto_key_step 155a)"),
        0x1d90: ("CODERtime", 1, 2, (-32768, 32767), "int minutes, any value (draw_timer 192e)"),
        0x1dbe: ("CODEQuit", 1, 2, (0, 1), "0/1 (crypto_key_step 155a)"),
        0x1d94: ("animation_active", 1, 2, (0, 1), "flag (quit_prompt 19ce)"),
        0x1db2: ("menu_pause_mode", 1, 2, (0, 1), "flag (crypto_key_step Alt-P)"),
        0x11ac: ("bld_animation_flag", 1, 2, (0, 1), "flag (save_screen_area 23c0)"),
        0x1170: ("save_width", 1, 2, (1, 320), "screen width saved around animations, <= SCREEN_W"),
        0x1262: ("Rp1", 1, 2, ("set", (0x1250, 0x1044)), "pointer to Rp1X or RpBackX (code.h)"),
        0x1056: ("RpBack", 1, 2, ("set", (0x1044, 0x1250)), "pointer to RpBackX or Rp1X (code.h)"),
        # RasterPort fields of Rp1X (gfx.h): PageNr 0..3, DrawMode 0/1, pens 0..255, FontNr 0..3
        0x1250: ("Rp1X.PageNr", 1, 2, (0, 3), "gfx.h"),
        0x125a: ("Rp1X.DrawMode", 1, 2, (0, 1), "gfx.h"),
        0x125c: ("Rp1X.APen", 1, 2, (0, 255), "gfx.h"),
        0x125e: ("Rp1X.BPen", 1, 2, (0, 255), "gfx.h"),
        0x1260: ("Rp1X.FontNr", 1, 2, (0, 3), "gfx.h"),
        0x1db4: ("mouse_buttons", 1, 2, (0, 7), "int 33h status bits"),
        0x1db6: ("mouse_x", 1, 2, (0, 639), "int 33h position"),
        0x1db8: ("mouse_y", 1, 2, (0, 199), "int 33h position"),
        0x1b70: ("joystick_raw", 1, 2, (-32768, 32767), "two signed bytes (code.h union)"),
    },
}
# Shared block bytes the modules read: offset -> (name, count, width, allowed, evidence) (state.h)
SHARED = {
    "CODE": {
        0x200: ("SHARED_TEXT", 511, 1, ("set", TEXT),
                "512 bytes of message text written by GAME/FINAL (state.h SHARED_TEXT)"),
        0x024: ("flags", 1, 1, (0, 255), "OPT_* bits (state.h +24)"),
        0x03a: ("level", 1, 2, (0, 3), "difficulty level (state.h +3a; DoCode clamps the result)"),
        0x044: ("gfx_letter", 1, 1, ("set", LETTERS), "driver letter, modules test 'C' (state.h +44)"),
    },
}
# Arguments: function name -> one spec per argument word:
#   "int" any int16; (lo, hi); ("set", values); "port" (RasterPort pointer: Rp1X/RpBackX);
#   "cstr" pointer to a NUL-terminated DGROUP string; "buf" pointer to writable DGROUP bytes;
#   "local" pointer to the caller's frame (the generator points it just above the argument words);
#   "seen" only values the real sessions passed at that position.
# Functions without an entry: every position is "seen" (assembly helpers with register arguments
# are realistic only with captured registers).
PORTS = (0x1250, 0x1044)
ARGS = {
    "CODE": {
        "_clamp": ["int", "int", "int"], "_random_below": ["int"], "_abs": ["int"], "_srand": ["int"],
        "_GoodBye": ["int", "int"], "_DoSound": ["int"], "_set_display_page": [(0, 7)],
        "_draw_backdrop_face": ["int", "int", "int"], "_animation_begin": ["int"], "_draw_timer": ["int"],
        "_save_screen_area": ["int"], "_blink_selected_letter": [(0, 25)],
        "_joystick_button_down": [(0, 1)], "_CheckForDisk": [(1, 4), ("set", (0, 1, -1))],
        "_quit_prompt": ["seen", "int", "int"], "_draw_menu_box": ["seen", "int", "int", "seen"],
        "_draw_framed_box": ["int"] * 5, "_draw_rect_outline": ["int"] * 5, "_draw_line": ["int"] * 5,
        "_fill_rect_corners": [(0, 319), (0, 199), (0, 319), (0, 199), (0, 255)],
        "_draw_text": ["cstr", "int", "int", (0, 255)], "_draw_text_flag0": ["cstr", "int", "int", (0, 255)],
        "_wait_vsync": [(0, 1000)], "_read_joystick_dir": ["local", "local"],
        "_mouse_set_position": ["int", "int"], "_mouse_button_press": [(0, 2)], "_mouse_button_release": [(0, 2)],
        "_mouse_set_range": ["int"] * 4, "_show_page": [(0, 3)],
        "_fill_rect": ["port", (0, 319), (0, 199), (1, 320), (1, 200), (0, 255)],
        "_draw_line_port": ["port", "int", "int", "int", "int", (0, 255)],
        "_strlen": ["cstr"], "_strcpy": ["buf", "cstr"], "_strcat": ["cstr", "cstr"], "_strcmp": ["cstr", "cstr"],
        "_strncpy": ["buf", "cstr", (0, 32)], "_strncmp": ["cstr", "cstr", "int"], "_strupr": ["cstr"],
        "_itoa": ["int", "buf", (2, 36)], "_getenv": ["cstr"], "__catox": ["cstr"], "_inp": ["int"],
        "_time": [("set", (0,))], "__aNlmul": ["int"] * 4, "_exit": ["int"], "__exit": ["int"],
        "__NMSG_TEXT": [(0, 255)], "__NMSG_WRITE": [(0, 255)],
    },
}
# Cross-argument conditions (the typed model's array bounds): name -> predicate(args)
ARG_CHECKS = {
    "CODE": {
        # fill_rect writes rows y .. y+h-1 of the 200-row span tables at DS:1395/154d (asm 0576)
        "_fill_rect": lambda a: a[2] + a[4] <= 200 and a[1] + a[3] <= 320,
        "_fill_rect_corners": lambda a: a[0] <= a[2] and a[1] <= a[3],
    },
}


# --------------------------------------------------------------------------------------------
# 4. Scripted cases, added to every function's vectors from its first realistic states.
#    keys: key script ("WAIT" = the following keys are typed only after the program has seen an
#    empty buffer, so a flush_input before a menu does not drain them); patch: a state change
#    (make_vectors.PATCHES). Menu lines start with a space; Return picks the highlighted one,
#    the next character is the line's hot key (draw_menu_box 1e9d).
_MENU = [
    dict(keys=["WAIT", "DOWN", "WAIT", "ENTER"], why="second menu line (Yes) with Return"),
    dict(keys=["WAIT", "ENTER"], why="first menu line (No) with Return"),
    dict(keys=["WAIT", "UP", "WAIT", "ENTER"], why="cursor up from the first line, Return"),
    dict(keys=["WAIT", "DOWN", "WAIT", "DOWN", "WAIT", "ENTER"], why="past the last line, Return"),
    dict(keys=["WAIT", "y"], why="hot key Y"),
    dict(keys=["WAIT", "n"], why="hot key N"),
    dict(keys=["WAIT", "ESC"], why="ESC in the menu"),
]
SCRIPTED = {
    "CODE": {
        "_crypto_key_step": [
            dict(keys=["ALT_Q", "WAIT", "DOWN", "WAIT", "ENTER"], why="Alt-Q, Yes with Return: GoodBye + exit"),
            dict(keys=["ALT_Q", "WAIT", "ENTER"], why="Alt-Q, No with Return"),
            dict(keys=["ALT_P", "WAIT", "ENTER"], why="Alt-P pause, Return"),
            dict(keys=["F1"], patch="all_guessed", why="F1 hint when every used letter is guessed (999 tries)"),
            dict(keys=["{"], why="'{' = table entry 26 (spare '[' slot): ignored"),
            dict(keys=["|"], why="'|' = table entry 27 (spare '\\' slot): ignored"),
            dict(keys=["`"], why="'`' = entry -1: ignored"),
            dict(keys=["a", "{"], why="guess '{' (entry 26): clears the guess"),
            dict(keys=["a", "|"], why="guess '|' (entry 27): clears the guess"),
            dict(keys=["a", "["], why="guess '[': clears the guess"),
        ],
        "_quit_prompt": _MENU,
        "_draw_menu_box": _MENU,
        "_DoCode": [dict(keys=["ALT_Q", "WAIT", "DOWN", "WAIT", "ENTER"], why="Alt-Q, Yes: GoodBye + exit"),
                    dict(keys=["ALT_Q", "WAIT", "y"], why="Alt-Q, hot key Y")],
        "_reveal_hint_letter": [dict(patch="all_guessed", why="every used letter guessed correctly: 999 tries"),
                                dict(patch="all_guessed_wrong", why="every used letter guessed wrongly: 999 tries")],
        "_CheckForDisk": [
            dict(overrides={"_find_file_on_disk": [0xffff, 0]}, keys=["WAIT", "ENTER"],
                 why="disk missing once: insert-disk prompt, Return, then found"),
            dict(overrides={"_find_file_on_disk": [0xffff, 0xffff, 0]}, keys=["WAIT", "ENTER", "WAIT", "ENTER"],
                 why="disk missing twice"),
        ],
        "_read_key": [dict(keys=["{"], why="'{'"), dict(keys=["|"], why="'|'"), dict(keys=["UP"], why="cursor up"),
                      dict(keys=["DOWN"], why="cursor down")],
    },
}


# --------------------------------------------------------------------------------------------
# 5. Driver entry slots (driver_vectors.py): bounded variations of the stack arguments CODE passes.
#    Specs as in ARGS ("seen" = values of the real sessions); register-convention slots are
#    recorded from the real sessions only.
DRIVER_ARGS = {
    "_draw_string": ["port", (0, 319), (0, 199), "cstr"],
    "_copy_rect": ["port", (0, 319), (0, 199), (1, 320), (1, 200), "port", (0, 319), (0, 199)],
    "_recolor_rect": ["port", (0, 319), (0, 199), (1, 320), (1, 200), (0, 255), (0, 255)],
    "_char_width": [(0, 3), (0x20, 0x7e)],
    "_show_page": [(0, 3)],
    "_play_sound": [(0, 0x3f)],
}
DRIVER_ARG_CHECKS = {
    "_copy_rect": lambda a: a[1] + a[3] <= 320 and a[2] + a[4] <= 200 and a[6] + a[3] <= 320 and a[7] + a[4] <= 200,
    "_recolor_rect": lambda a: a[1] + a[3] <= 320 and a[2] + a[4] <= 200,
}
# Slots the routes never reach, built on another entry's real driver state with arguments from the C:
# show_page(0) and show_page(Rp1->PageNr) in CheckForDisk 26ba / quit_prompt 19ce; set_palette with
# ega_palette_anim(_alt) (code.h 1174/1186) in quit_prompt 19ce.
DRIVER_SYNTHETIC = {
    "_show_page": [[0], [1], [2]],
    "_set_palette": [[0x1174], [0x1186]],
}

# Per-function instruction budget of one run (default 2 000 000; program entry 20 000 000).
BUDGETS = {
    "TAC": {"_generate_building": 40_000_000,      # a whole floor plan: about 15M instructions
            "_tac_init": 150_000_000},             # loads the campaign files and builds the level
}
# The C library's table of open files, _osfile[20] (DGROUP; found by its use in each module's _read,
# _close and _write): run_function writes it as the cut _open/_read/_close/_lseek would.
OSFILE = {"FINAL": 0x5702, "GAME": 0x4f18, "TAC": 0x3d0a, "BUG": 0x3462, "CHASE": 0x1f62}
# Idle keys (run_function.Script) taken in turn when a function waits with an empty key queue: the
# default ESC; ESC then ENTER where a wait ends only on Return (edit_text_field: the code name, the
# title's name entry, the gender menu; CHASE's start).
IDLE_KEYS = {
    "FINAL": {n: ("ESC", "ENTER") for n in ("_edit_text_field", "_ask_code_name", "_title_startup", "_choose_gender")},
    "CHASE": {n: ("ESC", "ENTER") for n in ("_main", "_chase_dashboard")},
}
# Per-function boundary-call limit of one run (default 20 000; program entry / main 50 000).
MAX_CALLS = {
    "TAC": {"_tac_init": 400_000},
}


# TAC: whole building generations and guard/alarm states. Made-up states (globals set on top of a
# captured raid state, "realistic": false in the vectors). Globals (names.txt): MSC holdrand 4026 (dword),
# GameMode a1e4 (0 break-in; 1/2/3 from selectors 7/8/0xa; 2 and 3 end when every guard is eliminated,
# 3 = one room), Diff 91e0 (0..4), Eseen 91ea (alert, cap 9999), ToRoom c86e, Disguised 91dc,
# player_acted cf44, Rtime a8ec, MacHere bb6a. wall_flags cf8a is 40 x 32; add_wall_segments reads up to
# 4 columns west of its start column (1..38), i.e. into the 128 bytes before it (cf0a..cf89).
def _tac_scripted():
    gm, diff, eseen, toroom = 0xa1e4, 0x91e0, 0x91ea, 0xc86e
    out = {}
    out["_generate_building"] = [
        dict(set={0x4026: ("dword", seed), gm: g, diff: d},
             why=f"whole generation, rand seed {seed:#x}, GameMode {g}, Diff {d}")
        for g in range(4) for seed, d in ((0x0001, 0), (0x2bad, 2), (0x7fff, 4))]
    out["_add_wall_segments"] = [
        dict(set={0x4026: ("dword", seed), 0xcf0a: bytes([fill]) * 0x80},
             why=f"rand seed {seed:#x}, the 128 bytes west of wall_flags = {fill:#04x}")
        for seed in (0x0001, 0x1d3a, 0x5555, 0x7fff) for fill in (0x00, 0x0f, 0x05)]
    out["_raise_alert"] = (
        [dict(args=[-1, 0], set={gm: g}, why=f"raise_alert(-1, 0), GameMode {g}") for g in range(4)]
        + [dict(args=[-1, 0xf0], set={gm: g}, why=f"raise_alert(-1, 0xf0), GameMode {g}") for g in (0, 2)]
        + [dict(args=[1, 0xf0], set={eseen: 9990, gm: 0}, why="alert at the 9999 cap"),
           dict(args=[1, 0x3e7], set={diff: 4, gm: 0}, why="amount 0x3e7 at Diff 4"),
           dict(args=[2, 0x3e7], set={diff: 0, gm: 1}, why="amount 0x3e7 at Diff 0, GameMode 1"),
           dict(args=[0, 0], set={gm: 0}, why="room 0 (outside)")])
    alarm = [dict(set={eseen: e, toroom: 1, gm: g}, why=f"alarm raised (Eseen {e}, ToRoom 1), GameMode {g}")
             for g in range(4) for e in (0xf0, 9999)]
    near = [dict(patch=("tac_player_near_guard", dx, dy, run, fire, sneak), args=[1],
                 why=f"player {dx},{dy} px from guard 1 (running {run}, fire_timer {fire}, guard sneak {sneak})")
            for dx, dy, run, fire, sneak in ((16, 0, 0, 0, 0), (48, 0, 1, 0, 0), (0, 48, 1, 0, 1),
                                             (-40, 24, 0, 4, 1), (80, -16, 1, 4, 0), (-24, -56, 0, 0, 1))]
    out["_update_guard"] = alarm + near + [
        dict(set={0xcf44: 1, 0x91dc: 1}, why="player acted while disguised"),
        dict(set={0xcf44: 1, 0x91dc: 0, eseen: 0}, why="player acted, no alarm yet"),
        dict(args=[1], set={0xa8ec: 0}, why="first frame (Rtime 0): distance maps from Guard and MacGuff"),
        dict(set={0xa8ec: 0x10, eseen: 500}, why="spawn tick with a high alert"),
        dict(set={0xa8ec: 0x20, 0xbb6a: 1, gm: 0}, why="spawn tick, target present"),
    ]
    out["_guard_sees_player"] = [dict(e, args=[1]) for e in near] + [
        dict(patch=("tac_player_near_guard", 32, 0, 0, 0, 0), set={0x91dc: 1}, args=[1],
             why="player 32 px from guard 1, disguised")]
    # the player fires at guard 1 (facing it: guard east 3, west 7, south 5, north 1) or guard 1 fires
    out["_aim_and_fire"] = [
        dict(patch=("tac_player_near_guard", dx, dy, 0, 0, 0, face, 6), args=[0, fire],
             why=f"player {dx},{dy} px from guard 1, facing {face}, 6 rounds, fire {fire}")
        for (dx, dy, face) in ((-40, 0, 3), (40, 0, 7), (0, -40, 5), (0, 40, 1), (-100, -30, 3))
        for fire in (0, 1)] + [
        dict(patch=("tac_player_near_guard", dx, dy, run, f_, sneak), args=[1, 1],
             why=f"guard 1 fires at the player {dx},{dy} px away (running {run}, fire_timer {f_}, sneak {sneak})")
        for dx, dy, run, f_, sneak in ((16, 0, 0, 0, 0), (48, 0, 1, 0, 0), (-40, 24, 0, 4, 1), (80, -16, 1, 4, 0))]
    for name in ("_guard_behaviour", "_room_cost", "_choose_next_room", "_spawn_guard"):
        out[name] = [dict(set={eseen: e, gm: g}, why=f"GameMode {g}, Eseen {e}") for g in range(4) for e in (0, 0xf0)] \
            + [dict(set={0xbb6a: 1, eseen: 0}, why="target present (MacHere)")]
    # devices (gr 9d78): flight, burst moment, burst running, gas ending, traps; near guard 1 and the player
    dev = []
    for kind in (0, 1, 2, 0x80, 0x82):
        dev += [dict(patch=("tac_grenade", 40, 0, 8, 0, -0x3f, kind), args=[1], set={diff: d},
                     why=f"kind {kind:#x} bursts 8 px from guard 1 (player 40 px away), Diff {d}") for d in (0, 4)]
    dev += [dict(patch=("tac_grenade", 40, 0, 0, 30, 5, k, 5), args=[0], why=f"kind {k} in flight (5 frames left)")
            for k in (0, 1, 2)]
    dev += [dict(patch=("tac_grenade", 40, 0, 0, 0, 1, 0, 3), args=[0], why="frag in its last flight frame"),
            dict(patch=("tac_grenade", 40, 0, 0, 0, -1, 1), args=[1], why="gas burst ending: room gets gas"),
            dict(patch=("tac_grenade", 40, 0, 0, 0, -20, 2), args=[1], why="stun burst running (-20)"),
            dict(patch=("tac_grenade", 40, 0, 60, 0, -0x3f, 0), args=[1], why="frag bursts 60 px from guard 1"),
            dict(patch=("tac_grenade", 12, 0, -12, 0, -0x3f, 0), args=[1], why="frag bursts next to the player"),
            dict(patch=("tac_grenade", 40, 0, 0, 0, -9999, 0), args=[1], why="remote trap at guard 1"),
            dict(patch=("tac_grenade", 40, 0, 0, 0, -9998, 0), args=[1], why="booby trap at guard 1")]
    out["_update_projectiles"] = dev
    # F7 (0x4100) throws, F8 (0x4200) fires the remote traps, F9 (0x4300) sets a trap (_update_player 0f4c)
    out["_update_player"] = [
        dict(patch=("tac_grenade", 40, 0, 0, 0, -9999, 0), keys=["F8"], why="F8 with a remote trap set"),
        dict(patch=("tac_grenade", 40, 0, 0, 0, -9998, 0), keys=["F8"], why="F8 with only a booby trap"),
        dict(set={0xb94c: 3, 0xb94e: 3, 0xb950: 3, 0x9d76: 0}, keys=["F7"], why="F7 throws a frag grenade"),
        dict(set={0xb94c: 3, 0xb94e: 3, 0xb950: 3, 0x9d76: 1}, keys=["F7"], why="F7 throws a gas grenade"),
        dict(set={0xb94c: 3, 0xb94e: 3, 0xb950: 3, 0x9d76: 2}, keys=["F9"], why="F9 sets a trap (menu)")]
    out["_update_guard"] += [
        dict(patch=("tac_grenade", 64, 0, 0, 0, -9998, 0), args=[1], why="guard 1 stands on a booby trap"),
        dict(patch=("tac_grenade", 64, 0, 16, 0, -9999, 0), args=[1], why="remote trap next to guard 1"),
        dict(patch=("tac_grenade", 64, 0, 0, 0, -0x3f, 1), args=[1], why="gas bursting at guard 1")]
    return out


SCRIPTED["TAC"] = _tac_scripted()
# GAME: the CIA building's floors (popup lines: Data, Intelligence, Crypto, Leave; Intelligence: Local Scan,
# International Scan, Active Wire Taps, Accuse Double Agent, Check with Sam), on the captured states. The
# Intelligence box opens beside its animation with no line taken yet: in the 20261114 vectors ENTER alone left
# it, one DOWN and ENTER ran the Local Scan and three DOWNs the Wire Taps, so every line takes one DOWN more.
_INTELLIGENCE = ["WAIT", "DOWN", "WAIT", "ENTER", "WAIT"]
SCRIPTED["GAME"] = {
    "_cia_building_menu": [
        dict(keys=_INTELLIGENCE + ["DOWN", "WAIT", "ENTER"], why="Intelligence Section, Local Scan (240 minutes)"),
        dict(keys=_INTELLIGENCE + ["DOWN", "WAIT", "ENTER"], why="Intelligence Section, Local Scan (240 minutes)"),
        dict(keys=_INTELLIGENCE + ["DOWN", "WAIT"] * 2 + ["ENTER"], why="Intelligence Section, International Scan (4 x 60 minutes)"),
        dict(keys=_INTELLIGENCE + ["DOWN", "WAIT"] * 2 + ["ENTER"], why="Intelligence Section, International Scan (4 x 60 minutes)"),
        dict(keys=_INTELLIGENCE + ["DOWN", "WAIT"] * 4 + ["ENTER"], why="Intelligence Section, Accuse Double Agent"),
    ],
}


# State donors: functions no recorded route reaches, run on the DGROUP/shared block captured at another
# function's entry on the real campaign routes (the caller's, when it has one), with arguments read from
# that state. name -> (donor labels, args(dgroup) -> [(args, realistic, why)]). realistic = the call the
# real caller makes (same argument kind); everything else is flagged. Every such case is kept.
def _w(dg, off):
    return int.from_bytes(dg[off:off + 2], "little", signed=True)


def _game_persons(dg):
    """GAME person table ps (0094, 16 bytes: org +0, face +2, loc +4, clues +6, role +8), Nperson 7cc0."""
    return [(p, _w(dg, 0x94 + 16 * p), _w(dg, 0x98 + 16 * p), _w(dg, 0x9a + 16 * p) & 0xffff,
             _w(dg, 0x9c + 16 * p) & 0xffff) for p in range(min(max(_w(dg, 0x7cc0), 0), 64))]


def _game_cast_people(dg):
    """ac (97a6, 48 bytes, who +0) for i < NACTIVITY (97a2)."""
    return sorted({_w(dg, 0x97a6 + 48 * i) for i in range(min(max(_w(dg, 0x97a2), 0), 64))} - {-1})


def _args_clear_person_knowledge(dg):
    cast = _game_cast_people(dg)
    out = [([p, 8], True, f"cast member {p} leaves his city: kind 8 (8755 318)") for p in cast[:3]]
    out += [([p, k], False, f"person {p}, kind {k:#x}") for p, k in zip(cast[3:5] + cast[:2], (1, 2, 4, 0x10))]
    return out


def _args_release_person(dg):
    ps = _game_persons(dg)
    loose = [(p, role) for p, org, loc, clues, role in ps if org != -1 and not role & 0x7fff]
    empty = [p for p, org, loc, clues, role in ps if org == -1]
    cast = [p for p, org, loc, clues, role in ps if role & 0x7fff]
    return ([([p], True, f"person {p} with role {role:#x}: released (8755 316 / 23c9 106)")
             for p, role in loose[:3]]
            + [([p], False, f"empty slot {p}") for p in empty[:1]]
            + [([p], False, f"cast person {p} (role set: kept)") for p in cast[:2]])


def _args_find_or_add_person_quiet(dg):
    ps = _game_persons(dg)
    pairs = {(org, loc) for _, org, loc, _, _ in ps if org != -1}
    new = next(((o, c) for o in range(16) for c in range(16) if (o, c) not in pairs), (15, 15))
    out = [([org, loc, b], False, f"existing pair ({org}, {loc}), bits {b:#x}")
           for (org, loc), b in zip(sorted(pairs)[:2], (0, 0x10))]
    out += [([new[0], new[1], 4], False, f"new pair {new}, bits 4"),
            ([new[0], new[1], 0], False, f"new pair {new}, bits 0: -1"),
            ([_w(dg, 0x7610), _w(dg, 0x7612), 2], False, "the mastermind's pair: slot 0")]
    return out


def _args_search_yield(dg):
    """The CIA building's searches (cia_building_menu 6458): the local scan search_yield(1, InCity, 0x64,
    0, 0) and two of the international scan's search_yield(1, random city, 0x32, 0, 0)."""
    city = _w(dg, 0xa626)
    return ([([1, city, 0x64, 0, 0], True, f"local scan of InCity {city}")]
            + [([1, c, 0x32, 0, 0], True, f"international scan, city {c}") for c in ((city + 5) % 16, (city + 11) % 16)])


def _literals(dg, text):
    """DGROUP offsets of every copy of the string literal `text` (with its end mark)."""
    out, at = [], dg.find(text)
    while at >= 0:
        out.append(at)
        at = dg.find(text, at + 1)
    return out


# The coded messages' paragraphs (GAME compose_message_text 7cf4, TAC a522, BUG 3c9e, FINAL 7668 and its practice
# message 053a): read_random_variant("text.dta", "*SLOC00", 8) and so on. Each call's "text.dta" is its own literal,
# right behind its tag (the call pushes the tag after the file name, MSC lays them out in that order).
_VARIANTS = (("*SLOC00", 8), ("*RORG00", 8), ("*FLUF00", 12), ("*AIDD00", 2))


def _variant_calls(dg):
    """(file, tag, tag text, n) for each paragraph call whose literals are in this DGROUP."""
    out = []
    for tag, n in _VARIANTS:
        for at in _literals(dg, tag.encode() + b"\0"):
            if dg[at + 8:at + 17] == b"text.dta\0":
                out.append((at + 8, at, tag, n))
    return out


def _args_random_variant(caller):
    return lambda dg: [([file, at, n], True, f'read_random_variant("text.dta", "{tag}", {n}), as {caller} calls it')
                       for file, at, tag, n in _variant_calls(dg)]


def _args_text_with_vars(dg):
    """BUG read_text_with_vars(file, tag), as read_random_variant calls it after writing the variant's digits."""
    return [([file, at], True, f'read_text_with_vars("text.dta", "{tag}"), as read_random_variant calls it')
            for file, at, tag, _ in _variant_calls(dg)]


def _args_cleared_text(dg):
    """BUG read_cleared_text(file, tag, 0x230e, msg_subst 231c, 7), as read_text_with_vars (3b26) calls it."""
    return [([file, at, 0x230e, 0x231c, 7], True, f'read_cleared_text("text.dta", "{tag}", 230e, msg_subst, 7), '
             "as read_text_with_vars calls it") for file, at, tag, _ in _variant_calls(dg)]


DONORS = {
    "GAME": {
        # the searches a player starts in the CIA building, on its states (the captured calls are the briefing's)
        "_search_yield": (("sub_game_00f7_6458",), _args_search_yield),
        # the coded messages' paragraphs (compose_message_text 7cf4 is on no recorded route): on the states of a
        # text being read (read_text_entry's entry)
        "_read_random_variant": (("sub_game_00f7_7b99",), _args_random_variant("compose_message_text")),
        # no caller in GAME (FINAL's copy fits the career and log lines in 300 pixels): the state is real, the
        # call is FINAL's
        "_trim_text_to_width": (("sub_game_00f7_7b99",),
                                lambda dg: [([0x12c], False, "trim_text_to_width(300) as FINAL calls it; GAME has no caller")]),
        "_clear_person_knowledge": (("sub_game_00f7_8755",), _args_clear_person_knowledge),
        "_release_person": (("sub_game_00f7_8755",), _args_release_person),
        # no caller in GAME (found by the prologue scan only): the state is real, the call is not
        "_find_or_add_person_quiet": (("sub_game_00f7_8755",), _args_find_or_add_person_quiet),
        "_clear_event_steps": (("sub_game_00f7_8755",), lambda dg: [([], False, "every event (dead code)")]),
    },
    "FINAL": {
        # select_save_file's call write_save_game(save_path) on the save screen's state (ask_save_drive's entry)
        "_write_save_game": (("sub_final_0100_a12e",),
                             lambda dg: [([0x4834], True, "write_save_game(save_path), as select_save_file calls it")]),
        # the practice message (make_practice_message 053a) and the campaign's (7668): on no recorded route
        "_read_random_text_tag": (("sub_final_0100_750d",), _args_random_variant("make_practice_message")),
    },
    "TAC": {
        # the coded messages' paragraphs (a522 is on no recorded route), on the states of a text being read
        "_read_random_variant": (("sub_tac_00e8_a3c7",), _args_random_variant("TAC's compose_message_text")),
    },
    "BUG": {
        # the recorded BUG run is a practice: its messages (3c9e) are never composed. On the states of its menu box
        "_read_random_variant": (("sub_bug_00e8_5d81",), _args_random_variant("BUG's compose_message_text")),
        "_read_text_with_vars": (("sub_bug_00e8_5d81",), _args_text_with_vars),
        "_read_cleared_text": (("sub_bug_00e8_5d81",), _args_cleared_text),
    },
}
# Functions that get donor cases on top of their own captures (DONORS is otherwise only for the unreached).
DONORS_ALWAYS = {"GAME": ("_search_yield",)}
DONOR_STATES = 4        # donor captures used per function (spread over routes and hits)
# Functions whose every distinct captured state is kept as a case (up to --max-cases), not only the first three and
# those that reach new blocks: GAME's draw_actor reads its parent's place uninitialised on the first sprite of a
# pass (hits 1-3), so the later sprites of a pass are the cases that compare.
KEEP_CAPTURES = {"GAME": ("_draw_actor",)}
# Entry points that neither the function database nor symbols/names.txt has (merged into the function before
# them), recorded under their label: label -> why.
MERGED_ENTRIES = {
    "FINAL": {"sub_final_0100_af04": "an empty function (a bare ret) behind apply_pan_palette; no caller"},
    "GAME": {"sub_game_00f7_b6e2": "an empty function (a bare ret) behind apply_pan_palette; no caller"},
}
