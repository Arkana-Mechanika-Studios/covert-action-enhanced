# BUG data model

Module BUG.EXE: the electronics minigame (wiretaps, car tracer). Types are in
the reconstructed `bug.h`. All offsets are DGROUP offsets of BUG.EXE.
Everything below is checked against the 120 byte-matched functions of the module.
BUG links the same campaign library as GAME (reports, clues, message log), so a large part of its
DGROUP is the campaign tables of `campaign.h` at other addresses; section 6.

## 1. The circuit board

### truth_tables  `0x00b2`, 12 x 4 entries of 4 bytes (192 bytes)
`struct chip_out { int top, bottom; }`; `truth_tables[type][2 * lower_input + upper_input]` gives the
two output signals (upper line, lower line) of one chip. Verified against all 12 chip operations
(all 48 entries identical). Read by `bug_status_message` (09cc) only.
Types: 0 OR to top, 1 OR to bottom, 2 a/a, 3 b/b, 4 a/b (straight), 5 b/a (swap), 6..11 add inverters.

### chips  `0x594c`, `int chips[7][5]` (35 words)
Column-major: `chips[column][row]`, 7 columns of 5 chips; screen position x = column*32 + 0x28, y = row*32 + 8.
Word: bits 0-5 type (0..11), `0x40` fixed (locked, drawn with a frame, cannot be swapped), `0x80` type
visible. Writers: `build_circuit_board` (0395: random types `clamp(random(level+6)+level, 0, 11)`, last
column type >= 4; shown with probability from level; `2*level` fixed chips, only chips of type > 3, never in column 5 unless level 4),
`puzzle_input_step` (10bc swap with the carried chip, result always `| 0x80`). Readers: 09cc, 10bc, 0395.
`chips[3][2]` (0x596e) is the cursor start chip (drawn selected at (0x88, 0x48)).

### carried_chip `0x5c30`, int
The chip held by the player (type only, no flags): drawn at (0x98, 0xb1); swapped by Enter/Space.
Random start `clamp(random(level+6)+level, 0, 11)`.

### wire_dest  `0x747e`, `int wire_dest[7][10]` (140 bytes; 6 rows used)
`wire_dest[col][s]` = input index (0..9) in column col+1 that output s of column col feeds. Always a
permutation of 0..9 per column. Written by 0395 only: per chip row i, parallel lines give
`[2i]=2i, [2i+1]=2i+1`, crossed lines (random(3)==2) give `[2i]=2i+1, [2i+1]=2i`; when the previous
row was also parallel, a diagonal swaps `[2i-1]` and `[2i]` (counted in p; layout accepted when
`4*level <= p <= 4*level+4`). Read by 09cc. The 7th row (0x74f6..0x750a) is never touched.

### signals  `0x750a`, `int signals[14][10]` (280 bytes)
`signals[2c]` = the ten inputs of column c, `signals[2c+1]` = its ten outputs; value 0/1, -1 = not computed.
Row 0 is the supply, set in 0395 to `(e & 3) == 0 || (e & 3) == 3` (1,0,0,1,1,0,0,1,1,0). 09cc evaluates
column by column and copies outputs to `signals[2c+2][wire_dest[c][s]]`. `final_outputs` (0x760e) is
`signals[13]`; `terminal_flags` (0x7622) follows. 0x7636 (`D_7636`, set to 1 in bug_init) is the next word; unknown.

### terminal_flags  `0x7622`, `int[10]`
0 = phone, 1 = alarm, per output terminal (right edge, y = terminal*16, x = 0x108 + 0x18 for odd). Written
in 0395 after generation as `!final_outputs[e]`: the terminals powered in the generated solution are the
phones the player has to unpower, the others are alarms. 09cc counts `phones_ok` (unpowered phones)
and `alarm_count` (powered alarms, ends the puzzle with "Alarm!!!").

### wire_grid  `0x843c`, `int wire_grid[40][25]` (2000 bytes, ends at 0x8c0c)
The 320x200 screen in 8x8 cells, `[x][y]` (50 bytes per column). Low byte = bit n for a wire piece leaving
the cell in direction n (0 N, 1 NE, 2 E, 3 SE, 4 S, 5 SW, 6 W, 7 NW; `dir_dx` 0x92 = 0,1,1,1,0,-1,-1,-1 and
`dir_dy` 0xa2 = -1,-1,0,1,1,1,0,-1, 8 words each); bit `0x100` = powered. 0395 first learns the cells
from the background picture (one pixel 2 px from the cell centre per direction, colour 10, 3 on CGA, bit set when it has that colour),
then overwrites the 2x2 blocks of the crossing slots (cells (4e+7 .. 4e+8, 4i+1 .. 4i+2)) and sets bit 0x10
on (c, 18) and (c+1, 18). `power_wire` (0efd) walks the graph and sets/clears 0x100; `draw_wire_cell` (0ff1)
draws the pieces of a cell (sprite n, 0x10+n powered, 0x18+n for the direction back to where it came from).
0x843a/0x8438/0x8436 (before the grid) are separate scalars: `menu_bottom`, `gfx_letter`, unnamed.

### puzzle_images `0x773c` (and 0x777c)
`int puzzle_images[64]`: handles cut from bugs.pic by `load_bugs_pic` (02dc). 0..15 and 16..31 are two
rows of 8x8 wire pieces (row 0 off: index = direction; row 1: `0x10+n` powered, `0x18+n`), 32..43 (the old
`D_777c`) the twelve 16x16 chip pictures `(chip & 0x3f) + 0x20`. 44..63 unused.

### scalar puzzle state
| name | addr | meaning |
|---|---|---|
| level | 1652 | difficulty 0..3: shared word 0x3a, in bug_init `clamp(level - ((skill_bits>>6)&3) + 1, 0, 3)` (0395 also reads it through `((int*)&sheet_port)[1]`, a self assignment) |
| base_level | 8f96 | level before that adjustment; clue strength at the end `base_level << 4` |
| mode | 6056 | 0 phone taps, 1 car tracer (hand-off byte 0 == 0xc; time halved) |
| time_left | 7db4 | seconds, start `clamp(10 - alert/5, 2, 10) * 60` (alert = shared word 0x48) |
| tick_count | 675c | animation ticks, 8 per second; palette `tick_count & 3` |
| phones_ok / best_phones | 8c0c / 5c22 | unpowered phones now / best so far (new best gives a clue, `wiretap_reveal_clue(8)`) |
| alarm_count | 747a | powered alarms (int; bug_init stores the low byte) |
| result_phones | 675a | phones handed back: shared byte 8 = 5 * phones (percent), byte 9 = alarms |
| done_flag | 8206 | puzzle over (-1 = quit whole game in 58b2) |
| cursor_col / cursor_row | 5c24 / 5c2e | 0..5 (columns 0..5 only), 0..4; start (3, 2) |
| chip_type_limit | 8274 | `clamp(2*level + 6, 0, 11)`; written only (probably an unused cap) |
| random_seed | 5c20 | `time(0) & 0x7fff`, srand |
| clock_str | 1760 | "00:00:00" digit buffer of draw_timer (153e) |
Target phones: 6 - level/2 (mode 0), 5 (mode 1; the percentage text is `20 * phones_ok`).

## 2. Pictures and ports
- `struct port` (0x14 bytes): mode(0), u_02, u_04, max_x 319 (6), max_y 199 (8), text_background (0xa),
  text_colour (0xc), fill_colour (0xe), font (0x10), self pointer (0x12). `draw_ctx` (2f08) points to the port at 0x2ef6;
  `sheet_port` (1650) points to the static port at 0x163e (the word after it is `level`).
  Fill colour is the box fill/clear colour (09cc sets 1/0xf); mode is 0 normally, 2 around the banner (5371).
- `image_table` (764c, 0x74 words): 0x00-0x27 female face parts (facesf.pic), 0x28-0x4f male parts (faces.pic),
  0x50-0x5f sixteen 8x8 tiles (`small_tiles`, 76ec), 0x60-0x66 seven 32x26 pictures, 0x68-0x6b male captions,
  0x6c-0x6f small pictures (0x6f = envelope), 0x70-0x73 female captions. Cut by 44ad / 40c4 (all was formerly
  `D_769c, D_770c, D_771c, D_7724.. D_772c`).
- `colour_map_a/b` (157c, 158c): two 16-byte translation tables (values 0..3 and 0x11/0x22/0x33) passed to driver call 0255; pairs at 0x159c,
  0x15dc, 0x15fc are the other variants; probably CGA. `palette_animation` (2dfa), `palette_dark_face` (2e0c): 17-byte EGA palette
  register tables (identity except entries 5, 12).

## 3. Menu box / quit prompt / disk prompt (58b2, 5d81, 6d48)
`menu_line_start[]` (3a86, 32 words: char offset of each line), `menu_hotkeys` (8208, 32 bytes), `menu_line_count`
3aca, `menu_text_width` 7646 (pixels of widest line), `menu_first_item` 7262 (first selectable line, -1), `menu_first_draw`
763e, colours 3ac6/3ac8/3acc/7642, `menu_right` 8276 and `menu_bottom` 843a (box edges), `menu_item_count` 747c,
`menu_ok_button` 6052 (draws an "OK" button, no selection), `menu_marked_items` 77b2 (bit n: item n printed with '^'),
`menu_default_item` 8228, `menu_no_selection` 7476. `expected_disk` (163c, initial 2) and `disk_files` (2eea,
words 1..4 -> "final.exe", "game.exe", "tac.exe", "hq.pan", tested by driver call 0002), `disk_letter` (2ef4, "A").
`current_drive` (2df4): -1 until `__dos_getdrive`; <= 2 means a floppy and "One moment please..." is drawn.
`animation_*` (3a68.. 3a74, 7474): written by `start_animation` (61e3): file name pointer, x, arg, active flag; 3a6e/3a72/3a74 unknown.

## 4. State hand-over (read_env_state 6985 / write_env_state 659e)
Shared block (state.h) words -> BUG globals: 0x20 cur_city 8c0e, 0x22 cur_org 8fd8, 0x24 campaign_flags 5c26,
0x26 case_no 7640, 0x28 org_2 7644, 0x2a saved_scalar_2a 764a, 0x2c saved_scalar_2c 7270, 0x2e saved_scalar_2e 7272,
0x30 game_minutes 594a, 0x32 person_count 6054, 0x34 link_count 7268, 0x36 plot_step 726a, 0x38 saved_scalar_38 8e54,
0x3a level 1652, 0x3c saved_scalar_3c 763a, 0x3e cast_count 77b0, 0x40 event_count 7638, 0x42 selected_person 1532,
0x44 gfx_letter 8438 (byte), 0x46 message_count 7260, 0x4a city_mask 77ac, 0x4c mask_4c 8c50, 0x4e skill_bits 7fbe,
0x52 heat 779c. (`is_cga` 5c2c = gfx_letter == 'C'.) The 2a/2c/2e/38/3c words are only copied.
Twelve chunks then follow (read_chunk 692c / write_chunk 68d3; `shared_cursor` 3ae2 = 0x400 reads from the shared
block, 0 reads the file `state_handle` 3ae4 / `chunk_dest` 3ace far pointer): scalar block 0x28e, cast 0x30*N, cast
progress 6*N, events 0x2c*M, cities 0x220, orgs 0x240, persons 0x400, links 0xc00, activity 0x3c0, messages 0x700,
plot objects 0x48, counters 0x40. Same as campaign.h.

### scalar_block `0x5992`, 0x28e bytes (struct in bug.h)
+0x04 `mastermind_known` 5996 (knowledge bits given to person 0), +0x06 `mastermind_org` 5998, +0x08
`mastermind_city` 599a, +0x0a `mastermind_look` 599c (look word of person 0) -- used by `find_or_create_person`
(2175) and `org_presence` (4a9d); +0x16 `clock_month` 59a8 (index modulo 12 into `month_names`, 1ac0);
+0x18 `clock_copy` 59aa (assigned game_minutes by write_env_state); +0x1a `city_name` 59ac (name of the current city
copied by write_env_state, 16 bytes); +0x2a 59bc 40 bytes default text of template variable 0; +0x52 59e4 default text of
variable 6 (572 bytes to the end of the block). Mapping to GAME: GAME's block starts at 0x760a (month at 0x7620).
Unknown: +0x00..0x03, +0x0c..0x15 and the use of the long tail (probably more per-case strings).

## 5. Message text templates (3b26, 3c9e, 3fc8, 2fe9, 2d22)
Seven replacement buffers (0x2224..0x230e: 52, 5 x 26, 52 bytes) are addressed through the pointer table
`0x231c` (words). Two token tables name the slots: for **clues.txt** (`msg_tokens` 20ba, 5 entries) `$KEY $NAME $ORG
$CITY $ROLE`; for **text.dta** messages (table at 0x230e, 7 entries) `$VICTIM $SNDORG $RCVORG $SNDLOC $RCVLOC $HLPORG $OBJECT`.
So slot 0 (`msg_subst` 231c) = $KEY (item text) / $VICTIM, slot 1 (`subst_person` 231e) = $NAME / $SNDORG, slot 2 (`subst_org` 2320)
= $ORG / $RCVORG, slot 3 (`subst_city` 2322) = $CITY / $SNDLOC, slot 4 (`subst_rcvloc` 2324) = $ROLE / $RCVLOC,
slot 5 (`subst_hlporg` 2326) = $HLPORG, slot 6 (`subst_object` 2328) = $OBJECT. `msg_tag_buf` (3a2e, "*MSG0000" /
"*ALRT00") is the tag buffer; text.dta and clues.txt file names are literals. `read_cleared_text(key, file, tokens, subst, n)`.

## 6. Campaign tables as BUG sees them (types of `campaign.h`)
| table | addr | count x size | notes |
|---|---|---|---|
| persons | 0172 | 64 x 16 | `struct person`; BUG's old placeholders 0174 = .look, 0176 = .city, 0178 = .known (lo byte), 017a = .u_08 |
| links | 0572 | 256 x 12 | add_link 272b, remove_link 27cc; 5e4e = cities[].orgs_seen |
| activity | 1172 | 80 x 12 | 1174 = .cast, 1176 = .what, 1178 = .when |
| cities | 5e32 | 16 x 34 | 5e4a = .grid (5e4a, key distance), 5e4c .size, 5e4e .orgs_seen, 5e52 = .x/.y |
| orgs | 7fc0 | 16 x 36 | `short_name` is printed by the message log (37fd "of the <short>"); 7fc6 = .name; 7fda .grid, 7fdc .strength, 7fde .divisor, 7fe0 .flags, 7fe2 .ethnic |
| messages | 6058 | 128 x 14 | 605a = due, 604c/6068 = previous/next record's due |
| events | 6760 | M x 44 | 6764 = .msg_id, 6766 = .text, 6786 = .target_kind |
| cast | 77b4 | N x 48 | 77d8 = byte at +0x24, 77dc = .flags, 77de = .clue_type |
| cast_progress | 8378 | N x 6 | step, accumulated progress |
| plot_objects | 822c | 4 x 18 | only copied |
| city_counters / org_counters | 8c10 / 8c30 | 16 words each | incremented when a city / org is shown in a clue |
All fields read through the old placeholders are now `table[i].member` (22ad, 22e0, 2175, 2627, 2911, 2c6f, 2d22, 2e8f,
2fc9, 2fe9, 31b8, 37fd, 3c9e, 3fc8, 4a9d, 48e9, 49b9, 4b50, 4b98, 4c22, 1882, 272b, 27cc, 282e, 6985, 659e).

## 7. Text tables
`rank_names` 1f24 (8 pointers: Recruit .. MasterMind; followed by tags CAR WPN ADR TKT MSG $ $ FCE); `month_names` 1ac0;
`clue_methods` 207e (7: Clandestine Photo, Electronic WireTap, Covert Surveillance, File Record Search, Local Informant,
INTERPOL Data Base, Local Authorities); `clue_kind_phrases` 1a40 (joining phrases " tied to ", ...; index from
`clue_kind_index`); `bit_count` 1aa0 (popcount of 5 bits, bytes); item names 1f44 (16 x 8 pointers, see section 8).
String names `str_*` are listed in `db.txt`.

## 8. Findings that contradict existing names (to be fixed in symbols/names.txt, not done by me)
- `D_bug_1f44 _when_phrases` and `sub_bug_00e8_2f44 _append_when_text`: the table holds the **names of case items**
  (item types: 0 car "Ford Escort #", 1 weapon "Walther PPK #", 2 street "Rue Laverne ", 3 airline "PanAm #",
  4 telex company "W.Union #", 5-6 money "$" (16 entries; "00"/"000" appended), 7 identity document "Swiss Passport #");
  index = `[(a >> 8) & 0xf][(a >> 4) & 7]`, number `(a & 0x3f) + 1`. Suggest `_item_names` and `_append_item_text`.
  Consequently `activity.when` (campaign.h) is an **item code**, not a time; it is also the clue's `$KEY` text.
- `msg_subst/subst_person/subst_org/subst_city` are slots 0..3 of seven (section 5); names fit the clue token set
  ($KEY.., $NAME, $ORG, $CITY) but the first one holds the item text / $VICTIM, not a "time text".
- `sub_bug_00e8_6e7a`..: empty stubs.

## 9. Proposals for shared headers
campaign.h:
- `struct activity`: rename `when` to `item` (item code, see above).
- `struct cast`: split `role[34]` into `char role[32]; char u_24 (item serial/variant byte used by lookup_clue_text, 2d22: >>1 or &0x7f); char u_25;`
  and describe `clue_type` low nibble as the item type.
- `struct msg.told`: BUG's 1882/37fd read the low nibble as **learned facts**: 1 date, 2 person, 4 organisation, 8 city. `person`/`other`
  are used as person indices (persons[x]) by 37fd and 3c9e, not as cast indices.
- `struct org.short_name`: printed by the message log.
state.h: names for the five copy-only words (0x2a, 0x2c, 0x2e, 0x38, 0x3c) stay unknown; `gfx_letter` is read as a byte at 0x44.

## 10. Still unknown
- 0x7636 (set to 1 in bug_init), 0x2df2, animation record words 0x3a6e / 0x3a72 / 0x3a74, 0x5300 (picture decoder result),
  0x3453, 0x3ad0 (declared only).
- Meaning of `chip_type_limit` (8274); the direction pieces learned from the picture were not decoded further than "colour 10 (3 on CGA)".
- Whether `terminal_flags` could change after generation (nothing in BUG writes it later).
- Colour map / palette tables are tied to the unseen driver calls 0255 and 024e (guessed from shape only).
- Exact semantics of port.mode (values 0 and 2) and port.u_02/u_04.
