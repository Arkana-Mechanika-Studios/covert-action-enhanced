# Data model of CODE, COVERT and INTRO

Offsets are DGROUP offsets (load-relative data segment). "BSS" = not in the EXE image, zero at
start. Evidence is the reconstructed function (named by its file stem) and the original bytes.
Header: the reconstructed `code.h`. Names: `symbols/names.txt`.

---------------------------------------------------------------------------------------------
# MODULE CODE (Crypto workstation, CODE.EXE)

## 0. What the program does (for the C# rewrite)

1. Start (`main`, 0b5a): `SHARED->next_module = -1`; register the three driver segments with
   thunk 052d; init thunks 0208/01c9; `difficulty = clamp(SHARED->level - ((SHARED->skill_bits >> 4) & 3) + 1, -3, 5)`;
   `option_flags = SHARED->flags`; `gfx_letter = SHARED->gfx_letter.w`; screen port font = 1, fill = 15;
   `seed_random`; `setup_cipher`; `draw_crypto_screen`; loop `crypto_key_step` until `leave_flag`.
2. Finish: `SHARED->value = solved (byte 09)`, `SHARED->elapsed = elapsed_minutes (low byte, 0f)`,
   `SHARED->next_module = (option_flags & 0x20) ? 0x11 : 0` (0x11 = FINAL, 0 = GAME),
   `signal_quit_game(0)` (no effect), then `prompt_insert_disk(flags & 0x20 ? 1 : 2, 1)` waits until the
   disk holding the next module is in (disk 1 = final.exe, 2 = game.exe). Alt-Q + "Yes" instead stores
   -1 into byte 0 and calls `exit()`: the launcher's loop ends and the whole game closes.
3. Puzzle. `message_text` holds the PLAINTEXT (copied from `SHARED + 0x200`, 512 bytes, upper-cased);
   the screen shows the CIPHER letter of every plaintext letter (`cipher_by_char`), 42 cells per line,
   16 px per line, cell pitch 6 px, first cell at x=0x2c, y=0x10. Under each cell the player's guess for
   that cipher letter is drawn (y+7, `guess_by_char`). The player presses a cipher letter (a-z) then the
   plaintext guess (a-z; any other key clears the guess).
4. Solved test (`check_solution`, 18a4): for every plaintext letter b with cipher letter c =
   cipher_alphabet[b]: if `guess_table[c] != ' '` and `letter_count[c] > 0` then it is correct
   (`== plain_alphabet[b]`) or wrong; letters with `letter_count[c] == 0` are "unused". Solved iff
   `wrong == 0 && unused/2 + correct > 19`. Matches the original's run (checker at 00b4:0d64).
   Only checked after an action (letter assignment, Alt-S, Alt-Q cancel, Alt-P, F1); F10 returns before it.
5. Difficulty `difficulty` (-3..5):
   * `<= 0`: the most frequent cipher letter is revealed, plus `-difficulty` random hints (`reveal_hint_letter`).
   * `1` or `2`: periods are removed from the message; `> 2`: spaces are removed (both in `setup_cipher`).
   * `2` or `> 3`: homophones (draw_crypto_screen): every cipher letter that does not occur takes over the
     odd-indexed occurrences of the currently most frequent letter; `plain_alphabet[h]` is changed to the
     replaced plaintext letter so that the solution table stays right.
6. Timer: `elapsed_seconds` is incremented once per 10 vertical retraces in `crypto_key_step` and once
   per 10 retraces (5 on + 5 off) in `blink_selected_letter`; `draw_timer` carries 60 into
   `elapsed_minutes` and prints `hh:mm:ss`. F1 (hint) adds 0xe10 = 3600 to `elapsed_seconds` (shown as +1 h,
   i.e. +60 minutes). The unit is therefore "display seconds" driven by retraces, not real seconds
   (deterministic in the oracle). No timeout exists.

## 1. Shared block use (state.h `struct shared_state`)

| offset | field | CODE reads / writes |
|---|---|---|
| 00 | next_module | W: -1 at start; W at end 0x11 or 0; W -1 on quit (quit_prompt, signal_quit_game) |
| 09 | value | W: `solved` (0/1) |
| 0f | elapsed | W: `elapsed_minutes` low byte |
| 24 | flags | R: -> option_flags (bits 4 sound, 8 animation, 0x10 joystick, 0x20 go to FINAL) |
| 3a | level | R: difficulty |
| 44 | gfx_letter (word) | R: -> gfx_letter |
| 4e | skill_bits | R: bits 4..5 (crypto skill) subtracted from level |
| 60,62,64 | seg_gfx, seg_misc, seg_snd | R: handed to thunk 052d |
| 200..3ff | message | R: 512 bytes, plaintext (setup_cipher copies it, byte by byte, through a far pointer) |

CODE never writes `option_flags` back to the block: the toggles (Alt-S/A/J) made inside CODE are lost
when CODE exits (unless another module re-reads them; unverified).

## 2. Graphics port records (shared structure, all modules)

`struct gfx_port` (18 bytes, code.h). Evidence: initial bytes at 1250 and 1044, field writes in
2958 (+0c), 2979 (+0a), 224e/0b5a (+0e), 0b5a/0e2d/14d7 (+10), 19ce (+00).

| off | field | initial | meaning / writers |
|---|---|---|---|
| 00 | page | 0 (screen), 1 (backup) | which video page/buffer; quit_prompt sets 2 around the stub 0cf5 (backdrop) and back to 0 |
| 02,04 | x0,y0 | 0,0 | probably clip rectangle origin; never written in CODE |
| 06,08 | x1,y1 | 319,199 | probably clip limit; never written |
| 0a | text_flag | 1 | draw_text_flag0 sets 0 around a draw, then 1 (probably "keep background"); initial value 1 so draw_text (flag 1) is the normal path |
| 0c | text_color | 15 | written by `draw_text(text,x,y,color)` |
| 0e | fill_color | 4 | main sets 15; draw_timer 10; blink 14; draw_framed_box = its colour; draw_menu_box 5; redraw_guesses 0; clear_animation_area 5; start_animation (no animation) 5 |
| 10 | font | 0 | main 1; draw_crypto_screen/redraw_guesses 2 (counts, guesses), 3 (cipher letters); draw_timer 2; blink 3 then 2 |

* `screen_port` 1262 (word): pointer, initial 0x1250 (`screen_port_record`, 1250..1261). 81 uses; first argument of every far draw call.
* `screen_backup` 1056 (word): pointer, initial 0x1044 (`backup_port`, 1044..1055). Destination/source of `copy_rect` when a menu or the Alt-Q/Alt-P screens cover the picture (320x200 region saved and restored).
* `save_width` 1170 (word, initial 0x140): width of the strip `save_screen_area` copies; draw_menu_box sets it to `clamp(animation_x, menu_right+5, 999)` when an animation is active.
* Writers/readers: all draw code. Unknown: meaning of 02..08 beyond the initial values.

## 3. The puzzle tables

Letters are ASCII uppercase; tables have 28 entries (A..Z, '[' = 0x5b, '\\' = 0x5c). Loops use
`alphabet_len` = 26, so '[' and '\\' are never shuffled or counted (their entries stay identity); cells
whose character is in 0x41..0x5c are drawn as cipher letters. Why the two extra entries exist is unknown
(probably the original message alphabet allowed them).

| addr | name | size / type | meaning | writers | readers |
|---|---|---|---|---|---|
| 105a | cipher_alphabet | 28 char (+2 NUL), initial `ABC..Z[\` | `cipher_alphabet[i]` = cipher letter for plaintext letter i | setup_cipher (shuffle: for i<26 swap with random j) | all |
| 1019 | cipher_by_char | virtual base = 105a - 0x41 | same table indexed by ASCII of the plaintext letter | (alias) | 0e2d, 1409, 175b |
| 1078 | plain_alphabet | 28 char (+2 NUL), initial `ABC..Z[\` | `plain_alphabet[i]` = real plaintext letter that the cipher letter `cipher_alphabet[i]` stands for (identity unless homophones changed it) | draw_crypto_screen (homophones) | 132d, 13a3, 155a, 18a4, 0e2d |
| 1096 | guess_table | 29 spaces + NUL (28 used) | player's guess (plaintext letter) per cipher letter index 0..25; 0x20 = none | 155a (assign / fill when solved), 132d (hint), 13a3 (F10), 0e2d (levels <= 0) | 175b, 18a4, 13a3 |
| 1055 | guess_by_char | virtual base = 1096 - 0x41 | same, indexed by cipher letter ASCII | (alias) | 175b |
| 1a7c | letter_revealed | 28 int (BSS) | 1 = this cipher letter was revealed by a hint (F10 keeps it) | setup_cipher (clear first 26), 132d, 0e2d | 13a3 |
| 1ac0 | letter_count | 28 int (BSS) | occurrences of each cipher letter in the final (possibly homophone-modified, truncated) message | 0e2d (count, homophone shift, recount while drawing) | 132d (picks a letter with count != 0), 18a4 (unused / used) |
| 1a3e | letter_count_by_char | virtual base = 1ac0 - 2*0x41 | same, indexed by cipher letter ASCII | (alias) | 0e2d |
| 1abe | alphabet_len | int (BSS) | 26, set by setup_cipher | setup_cipher | all loops |
| 1b72 | message_text | 512 char (BSS, 1b72..1d71) | plaintext message, spaces/periods possibly removed, last char dropped, upper-cased; the first ~10 bytes are temporarily overwritten by count text in draw_crypto_screen (saved in a local); also the scratch for the insert-disk text (26ba) | setup_cipher, 0e2d (homophones, truncation at index 0x1ce = 462) | 0e2d, 1409, 175b |
| 10da | char_buf | 2 char ("x\0") | one-letter string for drawing | 0e2d, 1409, 14d7, 175b | draw_text |
| 1ab4 | itoa_buf | 10 char (BSS) | digits for the occurrence counts | 0e2d | 0e2d |
| 1d72 | difficulty | int (BSS) | see section 0 (-3..5) | main | setup_cipher, 0e2d |
| 1dbe | leave_flag | int (BSS) | nonzero stops the workstation loop; 1 normal, -1 set by quit_prompt Alt-Q | 155a, 19ce | main, 155a |
| 1d8e | solved | int (BSS) (read as char by main) | 1 when check_solution passed | 155a | main (-> SHARED->value) |
| 1d78 | option_flags | int (BSS) (low byte used) | see flags below | main, 155a, 19ce | many |
| 1d7a | gfx_letter | int (BSS) | copy of SHARED word 0x44 | main | not read by the C (the graphics driver reads DS:1d7a probably) |
| 1d76 | random_seed | int (BSS) | `time(0) & 0x7fff` | seed_random | none |
| 1a7a | elapsed_seconds | int (BSS) | timer ticks, carried into minutes by draw_timer | 155a, 14d7, 192e | 192e |
| 1d90 | elapsed_minutes | int (BSS) (read as char by main) | minutes (hours = /60) | 192e | main (low byte -> SHARED->elapsed) |
| 111e | timer_text | "00:00:00" (9 bytes) | digit characters [0],[1] hours, [3],[4] minutes, [6],[7] seconds; the colons are fixed | 192e | drawn at (0x100,1) font 2 |

`option_flags` bits: 0x04 sound (Alt-S), 0x08 animation (Alt-A), 0x10 joystick (Alt-J), 0x20 next module is FINAL.
Strings used as arguments by number only (not variables): 10b6 "CRYPTO WORKSTATION, Msg:#M173"
(the three digits at positions 26..28 become `(message_text[13]&7)+'0'`, `(message_text[21]&7)+'1'`,
`(message_text[7]&7)+'2'`), 10d4 "0" (initial count), 10d6 "" (empty), 10d7 "-" (hyphen drawn when a
line break falls between two letters), 1058 "" (empty), 1105 "PAUSED\n", 110d "MESSAGE DECODED\n".

Screen layout of draw_crypto_screen: black clear, green (colour 10) title bar `(0x22,0)..(0x140,8)`, lines in colour 2
around the left column, title text at (0x40,1) font 2; left column: for letter g at y = 7*g+9: the letter at x=0xc (font 3), its
count at x=0x14 (font 2), guess at x=4. Each cipher cell drawn with `wait_vsync(2)` (so the text appears gradually).
`hint_scan_animation` (F1 effect): 200 random cells, each blanked, 2 retraces, redrawn.

## 4. Menu / message box (quit_prompt 19ce, draw_menu_box 1e9d, shared design with GAME/FINAL/TAC/BUG/CHASE)

The text is `\n` separated; a line starting with a space is an item (hotkey = next char); returns the
chosen item or -1. y odd = plain box over the existing picture, y even = framed box. Texts used: quit
confirm (items No / Yes), "PAUSED", "MESSAGE DECODED", "Please insert Disk X ...".

| addr | name | size | meaning | writers | readers |
|---|---|---|---|---|---|
| 1b16 | menu_line_start | 32 int | text offset of each line | 1e9d | 1e9d |
| 1b56 | menu_text_color | int | colour of non-item lines (15, or 0 on a white fill) | 1e9d | 1e9d |
| 1b58 | menu_item_color | int | colour of items (7) | 1e9d | 1e9d |
| 1b5a | menu_line_count | int | lines of the text (clamped to (0xc0-y)/8) | 1e9d | 1e9d |
| 1b5c | menu_selected_color | int | colour of the selected item (7, or 8) | 1e9d | 1e9d |
| 1d92 | menu_first_item | int | line number of the first item, -1 until found | 19ce (-1), 1e9d | 19ce, 1e9d |
| 1d96 | menu_message_only | int | 1: no highlight, any of Return/space/ESC continues (insert-disk prompt) | 26ba, 19ce | 19ce |
| 1d98 | menu_item_count | int | number of items | 1e9d | 19ce |
| 1d9a | menu_first_draw | int | 1 while the whole box must be drawn, 0 afterwards | 19ce | 1e9d |
| 1d9c | menu_hilite_color | int | highlight bar colour (5, or 15 on a white fill) | 1e9d | 19ce |
| 1d9e | menu_text_width | int | widest line in pixels (`char_width` sum) | 1e9d | 19ce, 1e9d |
| 1db2 | menu_pause_mode | int | draw only and return -1 at once (PAUSED) | 155a | 19ce |
| 1dae | menu_marked_mask | int | bit n: item n is drawn with '^' in place of its space | external (never set in CODE) | 1e9d |
| 1d7c | menu_ok_button | int | draws an "OK" button (str_ok at 1197), items disabled | external (never set in CODE) | 19ce, 1e9d |
| 1dc0 | menu_hotkeys | 32 char | hotkey of each item | 1e9d | none in CODE |
| 1de0 | menu_start_item | int | item highlighted when the next menu opens; reset to 0 | external | 19ce |
| 1de2 | menu_right | int | right edge: `x + menu_text_width + 8` | 1e9d | 1e9d |
| 1de4 | menu_bottom | int | bottom edge: `y + 8*lines + 6` | 1e9d | 1e9d |

Strings: 10dc and 113d both "Are you sure\nyou want to Quit?\n No\n Yes\n" (10dc used by the workstation key
handler, 113d by quit_prompt's own Alt-Q), 1128 "One moment please...", 1197 "OK".
Unreferenced words 1b5e..1b6f (18 bytes) and 1d74 sit between these variables: unknown.

Backdrop/animation arguments (never set in CODE, effectively dead here): `backdrop_code` 1d7e (BSS 0, set to -1
at the end of every quit_prompt), `backdrop_arg_b` 1db0, `backdrop_arg_c` 1dbc: passed to the empty stub 0cf5.
`(backdrop_code >> 2) & 3 == 3` selects `ega_palette_anim_alt`.

## 5. Picture animation (.pan player): start_animation 22ff, save_screen_area 23c0

Animation support is mostly stubbed in CODE (the stub functions 0cf4, 0cf5, 27ec, 27ed, 27ee are empty), but the data
is real. Nobody in CODE calls `start_animation`, so `animation_active` stays 0 (BSS).

* Record at 1af8 (BSS, addressed by the stub call `27ec(animation_name)` = its address): +00 name pointer
  (`animation_name`, set to 0x11ae), +02/+04 unknown, +06 (1afe) -1, +08 `animation_x` (1b00), +0a `animation_y` (1b02),
  +0c (1b04) -1, remaining bytes up to 1b15 unknown.
* `animation_active` 1d94, `bld_animation_flag` 11ac (1 if the file name starts with "bld"; save_screen_area then also
  saves a 160x30 corner and clears the flag), `current_drive` 116e (-1 -> `_dos_getdrive`; drive <= 2 shows str_one_moment),
  word 116c (-1, set before an animation starts; never read in CODE; unknown), `animation_file` 11ae (22 bytes, initial
  "animation.pan", strcpy target), `briefing_pan_name` 119a "briefing.pan", `bld_prefix` 11a7 "bld".
* `ega_palette_anim` 1174 and `ega_palette_anim_alt` 1186: 17 bytes each = 16 palette entries (identity, entry 5 -> 0;
  the alt table also entry 12 -> 6) + overscan; passed to thunk 01c2 (BUG names the same call "select_palette").
* `color_map_a` 0f12, `color_map_b` 0f22 (16 bytes each; inside `color_maps` 0ef2..0fd1, 14 tables of 16 bytes; 0ef0..0ef1 padding, values 0..3 or
  two such values in one byte, e.g. 0x12): given to thunk 01c9; `main` passes 0f82 twice. Probably CGA/EGA colour
  conversion tables; unconfirmed. The word table 0fd2..1041 (`graphics_library_doc_ptrs`, 56 ascending words 0x6c..0xe9f) points to the 56
  English documentation strings of the MicroProse Graphics Library at 006c..0eef (`graphics_library_docs`); no code reads either.
* `default_disk` 1042 (initial 2) the disk the prompt asks for when a probe fails (start_animation, 2 = game disk).

## 6. Insert-disk prompt (26ba)

`disk_file_names` 1244 is a 1-based pointer table: real words at 1246.. = {"final.exe" 11ce, "game.exe" 11d8,
"tac.exe" 11e1, "hq.pan" 11e9} for disks 1..4. `disk_letter` 124e = "A\0": `disk + 0x40` ('A'..'D'); for disks 3 and 4 the text
adds "(if 3.5\" insert Disk X)" with X = letter - 2 (A, B). Text pieces at 11f0..1245 (literals in the C).

## 7. Joystick (261c, 259f, 256c, 2698) and mouse (27ee..28f0)

* `joystick_dir_table` 11c4 (centre label); real table is 9 words at 11bc..11cd = {8,7,6,1,-1,5,2,3,4}; the C index is
  `3*dx+dy` with dx,dy in {-1,0,1}, label 11c4 = centre: direction codes 1 up, 2 up-right, 3 right, 4 down-right, 5 down,
  6 down-left, 7 left, 8 up-left, -1 centred (dx = X axis, dy = Y axis, negative = up).
* `joystick_raw` 1b70: packed axes from thunk 03b3, low byte X, high byte Y (signed, threshold +-0x60).
* Buttons: `inp(0x201)` bits 4 and 5 (active low). `poll_joystick_key` maps stick up -> 0x4800, down -> 0x5000,
  button 0 -> Return, button 1 -> ESC. Enabled by option bit 0x10; Alt-J toggles it and calls `calibrate_joystick`.
* Mouse (never called in CODE, library leftovers): `mouse_regs` 1d80 (REGS: ax bx cx dx si di flags, 14 bytes: 1d80..1d8d),
  `mouse_buttons` 1db4, `mouse_x` 1db6, `mouse_y` 1db8, `mouse_press_count` 1dba (BX of functions 5/6).
* `bios_regs` 1da0 (REGS, 14 bytes: 1da0..1dad) for the unused `set_display_page` (AH=5).

## 8. DGROUP map

0ef2..0fd1 colour tables; 0fd2..1041 doc string pointers; 1044 backup_port; 1056 screen_backup; 1058 ""; 105a cipher_alphabet;
1078 plain_alphabet; 1096 guess_table; 10b6 title; 10d4..10da small strings, char_buf; 10dc quit text; 1105 "PAUSED";
110d "MESSAGE DECODED"; 111e timer_text; 1128 "One moment please..."; 113d quit text; 1166 "PANI" (4-char tag, unreferenced;
likely used by the library animation code); 116c,116e words; 1170 save_width; 1172 word 1 (unreferenced); 1174/1186
palettes; 1197 "OK"; 119a "briefing.pan"; 11a7 "bld"; 11ae animation_file; 11bc table; 11ce file names; 11f0
insert-disk text; 1246 disk pointers; 124e disk letter; 1250 screen_port_record; 1262 screen_port; 1264.. C library
(messages, "R6001 - null pointer assignment", ...). 1a7a..1de5 variables listed above.

## 9. Unknown / open

* Meaning of port fields +02..+08 (clip rectangle assumed), the two extra letter-table entries '[' and '\\'.
* Thunk 01c9/01c2 exact action (colour tables / palette), the word at 116c, record bytes 1afa..1afc and 1b06..1b15.
* `module_names[0x63]` (COVERT, below) reads outside its table.
* What reads `gfx_letter` (1d7a) and whether CODE's option toggles survive in the block.
* Whether `elapsed_minutes` above 255 wraps in the result byte (it does: only the low byte is stored).

---------------------------------------------------------------------------------------------
# MODULE COVERT (launcher, COVERT.EXE)

| addr | name | size | content | use |
|---|---|---|---|---|
| 00e3 | empty_arg | "" | argv[0] of the spawnl of intro.exe | covert_0010 |
| 00e4 | intro_name | "intro.exe" | | spawnl |
| 00ee | fonts_name | "fonts.cv" | second argument of the graphics driver load | load_overlay |
| 00f7 | misc_name | "misc.exe" | MISC driver | load_overlay |
| 0100 | mcv_switch | "/2" | argv[1] of mcv.exe (module code 0x63) | spawnlp |
| 0103 | mcv_argv0 | "cv" | argv[0] of mcv.exe | spawnlp |
| 0106 | mcv_name | "mcv.exe" | | spawnlp (search path) |
| 010e | empty_arg2 | "" | argv[0] of every module spawn | spawnl |
| 0110 | launcher_port_record | 18-byte gfx_port (0,0,0,319,199,0,15,4,0) | pointed to by the word at 0122 (unreferenced by the C) | library |
| 0124 | module_names | 18 near pointers | next-module code -> file: 0 game, 1 tac, 2 code, 3 chase, 4 bug, 5 chase, 6 chase, 7 tac, 8 tac, 9 final, 10 tac, 11 final, 12 bug, 13 tac, 14 chase, 15 code, 16 bug, 17 final | main loop |
| 0148 | graphics_driver_name | "?graphic.exe" | '?' replaced by SHARED->gfx_driver | load_overlay |
| 0156 | sound_driver_name | "?sound.ca" | '?' replaced by SHARED->snd_driver | load_overlay |
| 0160 | driver_init_arg | word (BSS) | result of thunk 0044(a) for a = 0..2, passed on to 00ec(a, value) | main |
| 0174.. | | library messages ("Allocated 1Mb of space????$", "MS-DOS lied to us...", "Filename not found$", "Not enough memory to load file$", "Overlay load failed for some reason$") | | load_overlay |
| 04c0 | shared_block | 12000 bytes (BSS, to 33a0) | struct shared_state at +0 (0x66 bytes), message/strings from +0x200, tables from +0x400; its far pointer is stored at 0000:04F0 | all modules |
| 33a0 | bios_regs | 14-byte REGS (BSS) | AX=0003 final text-mode switch; byte 33a1 = AH | main |

Module codes come from SHARED->next_module (byte 0); the loop runs `module_names[code]` through `spawnl`, code 0x63 runs
mcv.exe through `spawnlp` with arguments "cv", "/2", `module_names[0x63]` — the last reads 0x124+0xc6 = 0x1ea, far past the
18-entry table (the word there belongs to the "Overlay load failed" text), so that path is probably dead or broken.
Start-up order: intro.exe, driver loads (segments into SHARED +60/62/64), loop from code 0x0b (FINAL).
Unknown: word 0122, thunk meanings (011a:0044/00ec/0140/0300/030e/0465).

---------------------------------------------------------------------------------------------
# MODULE INTRO (hardware menu, INTRO.EXE)

Writes SHARED +02 (graphics letter), +03 (sound letter), +04 (joystick 1/0). Data:

| addr | name | type / size | content | use |
|---|---|---|---|---|
| 01fe | | "misc.exe" | MISC driver loaded first (`load_overlay(0x1fe,0)`) | main |
| 0207 | | 2 bytes 0xff 'O' | probably a colour-attribute marker (0xff, 0x4f) | print_at row 0 |
| 020a..039f | | five 80-char frame rows and four text rows (CP437 box drawing, "Covert Action (TM) - Version 447.01", "From those sneaky guys at MPS Labs.", "Program & Audio-visual Copyright (C) 1990 by MicroProse Software,Inc.,", "All Rights Reserved.") | rows 1..10 | main |
| 03f0 / 0441 | | prompts (0xff 0x0e + "Select graphics mode:" / "Select sound mode:") | | main |
| 0492 / 04ab | | "1. Joystick and Keyboard" / "2. Keyboard only" | | main |
| 04bc / 04bf | | 0xff 0x0e, "One Moment Please..." | | main |
| 04d4 | gfx_menu_text | 5 near pointers | ") VGA (256 color)", ") MCGA (256 color)", ") EGA (16 color)", ") Tandy 1000 (16 color)", ") CGA (4 color)" | main |
| 04de | snd_menu_text | 6 near pointers | ") Shhh! No sounds please", ") IBM sounds", ") Tandy sounds", ") AdLib sound board", ") Roland MT-32 MIDI board", ") Custom sound driver" | main |
| 04ea | snd_driver_letters | "NITARX" (no NUL) | sound driver letters in menu order | main |
| 04f6 | gfx_driver_letters | "EMETC" | graphics letters in menu order (VGA->E, MCGA->M, EGA->E, Tandy->T, CGA->C) | main |
| 04fc | | 5 pointers to "Egraphic.exe", "Mgraphic.exe", "Egraphic.exe", "Tgraphic.exe", "Cgraphic.exe" (0119..014d) | unreferenced by the C | |
| 0506 | menu_blank_row | word -> 015a | 80 blanks + CR LF '$' | print_string |
| 0508 | menu_border_row | word -> 01ad | 0xba + 78 blanks + 0xba | print_at |
| 07c6 | snd_letter_by_key | virtual base = 07f6 - '0' | key code -> letter | main |
| 07de | bios_regs | REGS (14 bytes, BSS) | int 10h: AH=+1, BH=+3, DL=+6, DH=+7 | set_cursor, main |
| 07f4 | gfx_letter_by_key | virtual base = 0824 - '0' | | main |
| 07f6 | snd_letter_by_choice | BSS, entries 1..6 | filled from snd_driver_letters | main |
| 081b | snd_extra_letters | 9 bytes BSS | only read for menu entry e >= 7 (never; 6 entries) | main (dead) |
| 0824 | gfx_letter_by_choice | BSS, entries 1..5 | filled from gfx_driver_letters | main |
Choice rule: key '1'..'5' -> letter; 'H' is remapped to 'C' and 'V' to 'M' after the lookup (so the letters
H/V never occur with the current tables). `D_000f` is declared but unused.

---------------------------------------------------------------------------------------------
# Proposals for shared headers

* **state.h**: (a) name the `flags` bits: `0x04` sound, `0x08` animation, `0x10` joystick, `0x20` go to FINAL (0x11) after
  the minigame; (b) `skill_bits` bits 4..5 = crypto skill (CODE), bits 6..7 = BUG; (c) offset 0x09 `value` is the crypto
  success flag, 0x0f `elapsed` the crypto minutes (low byte); (d) `+0x200..0x3ff` is the 512-byte message block read by CODE
  (and written by FINAL); (e) add `char raw[12000]`-style comment: the block is 12000 bytes (04c0..33a0 in COVERT);
  (f) module code table: 0 game, 1 tac, 2 code, 3 chase, 4 bug, 5 chase, 6 chase, 7 tac, 8 tac, 9 final, 10 tac, 11 final,
  12 bug, 13 tac, 14 chase, 15 code, 16 bug, 17 final, 0x63 mcv, -1 quit (from COVERT module_names).
* **new shared header (gfx.h)**: `struct gfx_port` as in code.h (18 bytes: page, x0,y0,x1,y1, text_flag, text_color,
  fill_color, font), used by every module's `screen_port`/`screen_backup`, plus the key code constants (KEY_*) and the
  screen size SCREEN_W/H; the same `quit_prompt`/`draw_menu_box` machinery exists in FINAL, GAME, TAC, BUG, CHASE.
* **campaign.h**: nothing needed for CODE.
