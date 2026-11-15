# The Amiga version as a source of original names

The Amiga release of Covert Action (MicroProse, 1992, "version 447.101") was built from
the same C sources as the DOS game, with SAS/C 5.10 instead of MSC 5.1. One file on its
first disk still carries the original symbol table and the compiler's debug types, which
give the original names of the global data of every module and of a few functions. This
page records what the disks hold, how the names were tied to our labels, and what the
68k code says about our data model and the original-bug register. The mapping itself is
[symbols/amiga-names.txt](../symbols/amiga-names.txt).

The disk images and everything extracted from them stay in the ignored `amiga/` folder;
nothing from them is committed apart from short names and constants quoted here.

## The disks

| Image (`amiga/disks/`) | SHA-256 |
|---|---|
| `Covert Action v447.101 (1992)(MicroProse)(Disk 1 of 3)[cr SR][KS2.0, 1Mb Chip, 1Mb Fast].adf` | `0c3414bb5a973157b577ec6d2fdf0d8b1fe79d602832723435322542923e552c` |
| `Covert Action v447.101 (1992)(MicroProse)(Disk 2 of 3).adf` | `ec2c10711aaa0f00df0f4db8d78d2c441f5e55946a59622d0036ae92bb736eed` |
| `Covert Action v447.101 (1992)(MicroProse)(Disk 3 of 3).adf` | `44f31aad79f95aef27c58d53e4ea61b2dd2dc896422b0053134e067c59628a29` |

Disk 1 is a cracked dump (its `startup-sequence` carries the crack group's text); disks 2
and 3 are plain. Contents (`amiga/files/diskN`):

- **disk 1 (boot, volume COVA, 56 entries)**: the programs `game`, `covert`, `ca`, `chkmem`;
  a minimal AmigaDOS (`c/`, `l/`, `libs/diskfont.library`, `devs/`, `s/startup-sequence`:
  copies `cova:disk.pix` to RAM, runs `chkmem`, then `game`); `fonts/` (cov1..cov3);
  the campaign data shared with the PC (`crime0..12.dta`, `world0..2.dta`, `text.dta`,
  `prose.dta`, `fame.dta`, `clues.txt`, `plot.txt`); title and intro pictures (`.pix`,
  `.pan`, `screen00.lbm`) and three music files (`thememusic.dat`, `spy6music.dat`,
  `finalmusic.dat`).
- **disk 2 (103 files)**: the city pictures (`amman.pix` .. `washingt.pix`, maps of Europe,
  Africa, South and Central America), building and scene animations (`blding01..04.pan`,
  `hq.pan`, `briefing.pan`, `crypto.pan`, `bustout.pan`, ...), the raid and puzzle sheets
  (`sprites.pix`, `spritesf.pix`, `guysnew2/3.pix`, `equip*.pix`, `board.pix`, `bugs.pix`),
  the per-module sound banks (`gamesnd.dat`, `tacsnd.dat`, `bugsnd.dat`, `codesnd.dat`),
  copies of the text and data files, and one save, `cv3.sve` (10,828 bytes).
- **disk 3 (19 files)**: the car chase (`cars.pix`, `chase2.pix`, `street.pix`,
  `chasesnd.dat`), faces, fonts, the hard-disk installer (`install`, `remove_covert`) and
  `read.me` (how to back up the disks).

`cv3.sve` has exactly the PC save layout (the saved game's layout): the
0x3f0-byte header with the scalars at the PC shared-block offsets, then the 0x28e-byte
scalar block and the tables, all in big-endian words.

## The three programs

All three are AmigaOS hunk executables.

- **`game`** (277,744 bytes, 137 hunks: 49 CODE, 42 DATA, 46 BSS, no symbols) is the game:
  one program for everything the PC splits into eight executables. Every C source file is
  a CODE/DATA/BSS triplet; hunk 0 is the start-up code with the SAS/C library linked in.
  Calls between files go through absolute relocations, so a RELOC32 entry of a code hunk
  pointing into hunk 2 or 3 is a use of a covert.c global at that offset.
- **`covert`** (52,988 bytes) is an older debug link of the same program, left on the disk
  truncated: its header declares 138 hunks (293,384 bytes) but the file ends inside hunk 7.
  What it holds is enough: a SYMBOL table of 232 names (82 library names in hunk 0, 9
  functions of covert.c in hunk 1, 28 globals in hunk 2, 106 in hunk 3, `_DoCode` and six
  globals of code/main.c in hunks 4-6) and SAS/C 5.10 HUNK_DEBUG blocks with line tables and
  complete type information (every struct with its member names, types and offsets, every
  global with its type and address) for `SPIES:game/covert.c` and `SPIES:code/main.c`.
  Its data hunks 2, 3, 5 and 6 have the same sizes as those of `game` and hunk 2 has the
  same bytes, so the addresses apply to `game` unchanged; code hunks 0, 1, 4 and 7 differ
  slightly (an earlier build).
- **`ca`** (3 code hunks, no symbols) is the hard-disk loader ("Covert Action Hard Disk
  loader program"): assigns `COVA:` and runs `cova:chkmem` and `cova:game`.

### Original source layout

The debug blocks name the files `SPIES:game/covert.c` and `SPIES:code/main.c`: the project
was called SPIES, with one directory per module. `covert.c` holds `main` (the module
dispatcher), a few Amiga helpers and the **definitions of all shared globals**: the
campaign tables, the scalars, the raid (TAC) and puzzle (BUG) state, the drawing ports and
the movement tables. `code/main.c` is CODE's entry `DoCode` with its six statics. The
other files have no names left, but their strings and the matched code put the hunk
triplets in module order:

| hunks | module | identified by |
|---|---|---|
| 0 | start-up + SAS/C lc.lib | library symbols |
| 1-3 | game/covert.c | symbols, debug info |
| 4-6, 7-9 | CODE (code/main.c, the workstation) | `_DoCode`; "CRYPTO WORKSTATION" |
| 10-15 | BUG (main, puzzle) | board.pic, bugs.pic; BUG main = bug 0018 |
| 16-24 | CHASE (3 files) | cars.pic, "mph"; can_move, check_location_entry |
| 25-45 | TAC (7 files) | equip/guys pictures; TAC main = tac 0018, raise_alert, update_guard ... |
| 46-69 | FINAL (8 files) | briefing.pan, music, "Red Herring"; setup_new_case |
| 70-114 | GAME (15 files) | GAME main = game 005a, popup c6ae, clues screen 98c8 ... |
| 115-116 | state exchange | copies the scalars to/from `WD` (the PC per-module env.sve readers/writers) |
| 117-136 | system, graphics, sound, file support | |

C `int` is 16 bits (SAS/C short-integer option): every int member is 2 bytes in the
debug types and the scalars are 2 bytes apart.

## Reproducing

1. Extract each disk image (`amiga/disks/<disk>.adf`, OFS/FFS) to `amiga/files/diskN` with a read-only extractor.
2. List the hunks and the 232 symbols of `amiga/files/disk1/covert` with a hunk reader; the same on `game` shows the 137 hunks.
3. The analysis scripts are scratch work in `build/amiga/` (not committed): a full hunk
   parser with relocations, a decoder of the SAS/C 5.10 debug records (tags 03 variable,
   04 struct, 05 member, 07 array/pointer, 01 function), a symbol resolver that turns a
   hunk 2/3 address into `table[i].field`, a Capstone 68000 disassembler that annotates
   every relocated operand with that name or the string it points at, a cross-reference by
   symbol and function, and the generator of `symbols/amiga-names.txt`.

## Method

1. **Data symbols**: names, addresses, types and sizes come from the debug block, so every
   covert.c global is known exactly (e.g. `_ac` = `struct Activity[32]` at BSS 0x10).
2. **Shared scalars**: the state exchange in hunk 115 copies 24 globals to `WD` (23 back) at
   the byte offsets of the PC shared block (`_InCity` at 0x20 ... `_Alerted` at 0x52). Each
   PC module's own loader (game aebb, tac cbb9, bug 6985, chase 04a4, final a6dd) reads the
   same offsets into its labels, which gives the label of every scalar in every module.
   The same loaders read the twelve campaign tables in the save-file order, which gives the
   table labels.
3. **Struct fields**: the debug types against campaign.h, tac.h and gfx.h, by offset; the
   record sizes agree in every case.
4. **Everything else**: the 68k functions that use a symbol (found through the relocations)
   were read against our matched C until the statements lined up: same constants, strings,
   call order. For example the Amiga `raise_alert` (hunk 40 +0x1748) clears `_Disguised`,
   scales the amount by `_Diff + 4`, stores the room in `_ToRoom`, uses `_MacGuff` in kind
   2, clamps `_Eseen` to 9999 and relaxes `rm[].toDist` over `dr[]`, statement for
   statement tac 6cb1. The functions recognised this way:

| Amiga (game hunk +offset) | ours |
|---|---|
| 115 +0 / +0x172 | the state writers / readers (game aad4/aebb, tac c7d2/cbb9, ...) |
| 25 +0 | TAC main (tac 0018) |
| 28 +0, +0x63c, +0xb0a | choose_door 070e, frame_step 0bcb, update_player 0f4c |
| 31 +0x4f4, +0x7ec, +0xb10 | take_photo 3081, plant_bug 32e6, 34e2 |
| 34 +0, 37 +0 | building generator 44e4, draw_room 5674 |
| 40 +0, +0x1748 | update_guard 5ca6, raise_alert 6cb1 |
| 10 +0 | BUG main (bug 0018) |
| 16 +0, 19 +0x63e, 22 +0x1f90 | CHASE main 001c, check_location_entry 3190, can_move 1ede |
| 64 +0, +0x952 | setup_new_case final 3f4c, place_cast_member 46df |
| 49 +0x314 (end) | the counter reset of final 0906 87 |
| 70 +0, 82 +0, 82 +0x130 | GAME main 005a, case briefing 3b76, popup_menu_on_clean_screen 3c87 |
| 88 +0, +0x29c | hotel wait 624a, cia_building_menu 6458 |
| 91 +0, 94 +0x150c, 97 +0x80, 103 +0x7e | clues screen 98c8, format_game_time 95ab, popup c6ae, sprite sheets 851f |
| covert hunk 1 `_SetChron` | reset_event_steps final 46b5 |
| `_DoCode` | CODE main (code 0b5a) |

## Results

`symbols/amiga-names.txt` has three sections:

- **labels and functions** (309 lines, the rename candidates): the original OMF name with
  its underscore. 131 distinct original names: 118 certain, 9 likely (best line), 4 guess.
  By line: 260 certain, 46 likely, 5 guess; only certain and likely are meant to be applied.
  One original name usually stands for several of our labels, one per module, because the
  PC keeps a private copy of each table and scalar in every executable.
- **fields** (187 lines, for later): labels that lie inside a table (`D_game_7610` =
  `pl.BigOrg`, `D_game_a648` = `active[1]`) and the members of the 17 structs.
- **C library** (108 lines): 24 of the 82 SAS/C library names exist under the same name in
  our MSC library labels; nothing to apply.

Of the 150 non-library symbols, 118 of the 134 covert.c globals, all 6 main.c globals and
7 of the 10 functions (`_main`, `_DoSound`, `_GoodBye`, `_FukSnd`, `_SetChron`,
`_CheckForDisk`, `_DoCode`; `_FukSnd` only as a guess) are mapped.

### What the original names change in our reading

- The cast table is the original **`Activity`** (`_ac`, count `_NACTIVITY`), its progress
  table `Activity2` (`_ac2`), and our `activity` table is the original **`Clue`** (`_cl`,
  80 entries; FINAL's name `clues` was right). Messages are **`Synopsis`** (`_sn`, count
  `_NextSyn`, hence "NSYN Overflow"); plot objects are **`MacGuffins`** (`_mc`); links are
  **`Location`** (`_lc`, count `_Nloc`); the counter block is `int active[2][16]`; the
  scalar block is **`struct Plot pl`**.
- Plot organisations and cities: the original numbers them **F, 1, 2**: our `plot_org_1/2/3`
  = `_TheOrgF`, `_TheOrg1`, `_TheOrg2`; `plot_city_1/2/3` = `_TheCityF`, `_TheCity1`,
  `_TheCity2`. The case number is `_TheCrime`, the plot step `_ChronTime`, the clock `_Gtime`.
- Shared words with only a placeholder name so far: 0x38 (`saved_38`) is **`_MacGuffin`**,
  0x42 (`sel_person`) **`_Sprung`**, 0x4a (`city_mask`) **`_Doubles`**, 0x4c (`mask_4c`,
  TAC `org_seen_mask`) **`_InterPol`** (a per-organisation bit mask; bit 0 is set by the
  "Master Plan" document), 0x52 (`heat`) **`_Alerted`**.
- Field names of the WORLDn.DTA records: our `city.grid` and `org.grid` are both
  **`climate`** in the original, `city.size` is `police`, `org.strength` `size`,
  `org.divisor` `covert`, `org.flags` `clues`, `org.ethnic` `race`. Our names describe the
  use found in the code; the original names suggest the fields were planned for something
  else. Worth a look before renaming the fields.
- Person: `look` = `face`, `known` = `clues`, `cast_mask` = `role`, `seen_by` = `rknown`,
  `score` = `intel`, `status` = `alert`. Cast: `exposure` = `cover`, `key_seed` = `key`,
  `role_flags` = `status`, `flags` = `clued`, `clue_type` = `ctype`; cast progress `u_02`,
  `u_04` = `intel`, `firstwho`. Event: `msg_id` = `synch`, `target_kind` = `status`,
  `items` = `macguff`, `score` = **`VP`** (victory points). Link: `flags` = `clues`,
  `percent` = `tapped`, `rooms_seen` = `seen`.
- Scalar block: GAME `clock_month` (7620) is **`ncase`** (the number of cases played; FINAL
  already calls it `career_months`); TAC `home_org`, `home_city`, `home_look` (98de-98e2)
  are the MasterMind's `BigOrg`, `BigLoc`, `BigFace` (GAME's `mastermind_*`), TAC
  `person_gen_mask` (98dc) is `clues` (GAME `mastermind_known`).
- TAC: `alert_level` = `_Eseen`, `violence_count` = `_Alarm`, `film_used` = `_PhotosTaken`,
  `frame_tick` = `_Rtime`, `target_room` = `_MacGuff`, `alarm_room` = `_ToRoom`,
  `guard_home_room` = `_Guard`, `mission_kind` = `_GameMode`, `room_types` = `_rms`
  (`struct Rooms`), `tile_types` = `_ob` (`struct Object`), `actors` = `_bg`
  (`struct BadGuy`), `portrait_port` = `_RpGuys` (the port of the guards' sprite sheet:
  "portrait" is probably a misreading).
- GAME's six face/icon sprite arrays (969c, 96ec, 973c, 975c, 976c, 977c and 9774-977a) are
  slices of one original array `unsigned sprite[300]`, at the same word indices; BUG's
  `image_table` has the same layout.
- `D_tac_a1e8 _messages` and `D_bug_605a _messages_due` are two bytes into the message
  table (`Synopsis.time`); the tables start at `D_tac_a1e6` / `D_bug_6058`.
- `D_chase_4770 _map_prev_row` (map cells minus one row) happens to fall inside CHASE's city
  table; it was left out.

### Layout differences against docs/data-model

None of the record sizes differs. The debug types refine these fields:

- `struct event` 01 (`u_01`) and `struct org` 23 (`u_23`) are padding after a char member
  (`act`, `race`); the original `Org` is 35 bytes, padded to 36 in the array.
- `struct activity` `u_08[4]` is two ints, `info2` and `key2` (a second subject/key pair).
- `struct object` `name[17]` is `char name[16]` plus `char type` at 0x10; 0x11 is `who`.
- `struct room` `flags` + `u_09` is one int `status`, and `struct actor` `fl` + `fl_hi` one
  int `status`: our notes that the code treats them as words were right. On the 68k the
  flag bits are in the low byte, at +9 / +1.
- The Amiga tables have fixed maxima: 32 cast members (`Activity[32]`), 64 events.
- Port record (`gfx_port`): `PageNr, XBgn, YBgn, Width1, Length1, DrawMode, APen, BPen,
  FontNr` = our `page, x0, y0, x1, y1, text_flag, text_color, fill_color, font`.

### Amiga and PC builds compared

- One program instead of eight: covert.c `main` loops on `CD[0]` (the next-module byte)
  through an 18-entry jump table (codes 0..0x11) and calls the module entry functions; there
  is no 0x63 / mcv path (CA-2).
- The shared block is two 1 KB buffers: `CDX` (bytes, through `CD`) for the hand-off bytes
  0x00-0x0f and `WDX` (words, through `WD`) for the scalars at 0x20-0x52. The campaign tables
  are not copied at all: the table copiers of the state exchange are empty functions, every
  module works on the same `_ac`, `_ev`, ... (CA-6 does not arise).
- TAC keeps the raid level in its own `_Diff` and leaves the campaign level `_diff` alone;
  the PC TAC overwrites `level` and writes it back to shared 0x3a. BUG lowers `_diff` itself
  and copies it into `_Diff` for the puzzle, restoring the base level around the
  intelligence report. Both builds take the level from 0x50 (`level_copy`) when GAME is
  re-entered, so the overwrite does not reach the campaign.
- The CGA flag (`_CGA`) and its tests are kept but nothing sets it.
- TAC's awake-guard counter is a separate word, not the campaign counter block (CA-1).
- Text, popup state and sprite handles are single globals shared by every module (`_Txt`,
  `_NoHi`, `_NoWait`, `_picface`, `_sprite`) where the PC has one copy per executable.
- Amiga only: sound state `_efx`, `_efxreq`, `_InitSound`, `_FukSnd`; `_ntsc` (set when ExecBase
  VBlankFrequency is 60), `_currentdisk`, `_CheckForDisk`, the title helpers
  `_plot`, `_ZoomText` and the camera sprites `_csprite`.

### Not mapped

- Globals without a PC counterpart or without any reference: `_CD`, `_WD` (pointers; the PC
  reaches the block through 0000:04F0), `_Dummy`, `_MICKEY`, `_Quick` (no code uses them),
  `_bollox_start`, `_bollox_end` (two zero words in the data hunk, unreferenced), `_csprite`,
  `_currentdisk`, `_ntsc`, `_efx`, `_efxreq`.
- The port records `_RpPicX`, `_RpSpriteX`, `_RpGuysX`, `_RpGridX` (only the pointers are
  mapped; which of our labels is the record and which the pointer is clear only in CODE).
- Functions `_plot`, `_ZoomText`, `_InitSound` (Amiga only).
- 58 SAS/C library and runtime names (`__CXM33`, `_stcl_d`, `__pfmt`, ...).
- `_CODEMyDisk` and `_CDX` / `_WDX` are only guesses (not to be applied).

## Applying the names

The rename section is meant for certain and likely lines only. Names
that need care because they collide with something in the same PC module:

| Module | Label -> original | Collision |
|---|---|---|
| CHASE | `D_chase_648a` (org table) -> `_org` | `_org` is today the name of `D_chase_69d2` (current organisation), which itself becomes `_AtLoc`: rename `69d2` first |
| FINAL | `D_final_a7be` (org table) -> `_org` | same with `D_final_b374` (current organisation -> `_AtLoc`) |
| GAME, TAC, BUG | org tables `D_game_9fb4`, `D_tac_c604`, `D_bug_7fc0` -> `_org` | `org` is a struct member of person/link and a local or parameter name in many files (39 GAME, 10 TAC, 8 BUG files) |
| TAC | `D_tac_91e0` -> `_Diff`, `D_tac_cf42` -> `_diff` | the two names differ only in case; MSC LINK folds case unless /NOI |
| GAME, TAC, BUG, FINAL | activity tables `D_game_1094`, `D_tac_16fe`, `D_bug_1172`, `D_final_1188` -> `_cl` | `cl` is a local in 3-5 files per module (and `.cl` once in TAC/BUG) |
| BUG | `D_bug_6760` -> `_ev` | local `ev` in one file |
| GAME | `D_game_0494` -> `_lc` | local `lc` in one file |
| GAME | `D_game_a628` -> `_active` | `active` used as a local/field in one file; the label is the start of `active[2][16]`, `D_game_a648` (org counters) is `active[1]` and stays a field |
| all | `_ac` (cast) / `_cl` (our activity) | the original `Activity` is our cast; our `struct activity` is the original `Clue`. Renaming the tables without the struct types leaves `struct activity` describing `_cl` |
| all | `_hour`, `_day`, `_min`, `_pic`, `_Status`, `_diff` | no clash found in the matched C today, but these are common words for locals and parameters in new code |

The check (bare name against the identifiers of the reconstructed C of the module outside comments and
strings, the struct and field names of its headers, and the names already in
symbols/names.txt for that module) found nothing else among the 306 certain/likely lines.

## Original-bug register

docs/original-bugs.md now has an "Amiga:" note in each row: CA-3, CA-4, CA-5 and CA-7 are
present unchanged in the 68k code; CA-1 is absent (the raid counts guards in a separate
word), CA-2 has no counterpart (no 0x63 path) and CA-6 cannot happen (no copied block).
