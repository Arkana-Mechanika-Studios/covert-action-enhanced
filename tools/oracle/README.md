# Function-level oracle

Runs ONE original function of a Covert Action DOS module in Unicorn and records
everything a port (the C# rewrite) must reproduce: the result registers, the
complete DGROUP and shared block after the call, and every crossing of the
module boundary (driver calls, interrupts, port I/O) in order.

Use the Python with Unicorn 2.1.4 and capstone: `python -B`
(the tools also put `build/python-deps` first on `sys.path`).

| file | role |
|---|---|
| `capture_states.py` | scripted original runs that capture realistic states |
| `run_function.py` | the runner (library + CLI for one call) |
| `make_vectors.py` | vectors for many functions: realistic seeds + coverage-guided fuzzing |
| `selftest.py` | checks the oracle against the matched C and the MSC library |
| `oracle_common.py` | module facts: frames, signatures, thunk slots, `main` |
| `oracle_config.py` | the one place for cuts, pointer arguments, typed-model ranges, scripted cases, driver argument bounds |
| `capture_all.py` | states of every other module (and their driver entries) on the recorded routes |
| `driver_vectors.py` | driver oracle: captures driver entries on the CODE routes, vectors per entry slot |

```
python -B tools/oracle/capture_states.py                 # once: CODE decode, explore, quit routes
python -B tools/oracle/capture_all.py                    # once: campaign, tacphoto, stasi, bug, chase, chaseabort
python -B tools/oracle/make_vectors.py FINAL             # likewise GAME, BUG, CHASE, TAC, COVERT, INTRO
python -B tools/oracle/make_vectors.py --summary CODE FINAL GAME BUG CHASE TAC COVERT INTRO
python -B tools/oracle/make_vectors.py CODE              # all functions -> build/oracle/CODE/
python -B tools/oracle/make_vectors.py CODE _clamp sub_code_0000_18a4
python -B tools/oracle/run_function.py CODE _clamp --args 5 0 3 --state build/oracle/states/CODE/decode-v1
python -B tools/oracle/selftest.py
python -B tools/oracle/driver_vectors.py capture         # once: driver entries on the same routes
python -B tools/oracle/driver_vectors.py vectors         # -> build/oracle/MGRAPHIC/, MISC/, SOUND/
python -B tools/oracle/joystick_vectors.py               # -> MISC/_joystick.json: MISC.EXE's joystick routines on a fed game port
```

The joystick vectors need no capture: MISC.EXE is run on its own, its calibration and reading called in sequences
with the counts of the port's timers given (made apart, in `build/oracle-joystick-<date>`; MiscJoystickOracleTests
finds that folder beside the oracle's own).

Everything under `build/oracle/` is game-derived: never commit it.

Where the checkout has no `build/` or `game/`: `COVERT_DATA_ROOT` names the folder that holds them
(states, code analysis, python-deps and the game folder are read there), and `COVERT_ORACLE_OUT` the folder new
vectors go to. A regeneration goes to a fresh folder (a copy of `build/oracle`, e.g. `build/oracle-next-<date>`),
never over the vectors the tests use; `COVERT_ORACLE_DIR` then points the C# tests at it.

## How states are obtained

1. **Scripted runs** (`capture_states.py`). The existing `trace_code.CodeProbe`
   (its DOS/BIOS fixtures, compact memory layout, key scheduler) plays
   COVERT -> INTRO -> FINAL -> Practice -> Crypto -> CODE with three key routes:
   `decode` (the documented 17-pair solution, ENTER), `explore` (F1, F10, wrong
   guess, Alt-S, Alt-P, Alt-Q No, ESC) and `quit` (Alt-Q Yes). While CODE runs,
   the entry of every function of `functions.json` (plus named entry points the
   database merged) is captured at hits 1, 2, 3, 4, 6, 9, 16, 33, 100, ...:
   registers, 64 stack bytes, the whole DGROUP window and the 12000-byte shared
   block. Two 1 MiB base images are kept per route: `base_start` (CODE process
   start, before crt0) and `base_wait` (first workstation wait: drivers loaded,
   thunk table patched). Output: `build/oracle/states/CODE/<route>-<tag>/`; an
   existing folder is reused, never overwritten (new `--tag` to recapture).
1b. **All other modules** (`capture_all.py`): one `trace_game.GameProbe` run per
   recorded route captures every tracked module it passes through (COVERT, INTRO,
   FINAL, GAME, TAC, BUG, CHASE) the same way (DGROUP/shared zlib-compressed;
   hits 1, 2, 3, 5, 9, 17, 33, 100, 333, 1000), with the layout, idle polls,
   seeded save and actions the protocols document:

   | route | layout / idle | seed | actions | modules reached |
   |---|---|---|---|---|
   | `campaign` | legacy / 256 | new game | `build/game-oracle/roundtrip-actions.json` (briefing, first investigation, save) | COVERT INTRO FINAL GAME |
   | `tacphoto` | legacy / 255 | `save-pending` CV0.SVE | the 37-action photo raid (game-protocol.md) | GAME TAC |
   | `stasi` | compact / 127 | `investigation-code-return` CV0.SVE | `stasi-arrest-save-actions.json` (arrest, interrogation, TAC, save) | GAME TAC |
   | `bug` | legacy / 256 | - | `bug-practice-success-return-actions.json` | FINAL BUG |
   | `chase` | compact / 15 | - | `build/chase-native-9283f7d5a1/actions.json` (successful tail) | FINAL CHASE |
   | `chaseabort` | legacy / 256 | - | `chase-practice-result-actions.json` | FINAL CHASE |

   A module's `base_wait` is its first keyboard wait, or for modules that never
   wait (COVERT, INTRO) the moment it starts a child or exits. Only the first
   instance of a module per route is captured (a later instance at another load
   segment would not match the base images). Driver entries are detected as the
   first instruction in a driver block (segments from shared+60/62/64) after an
   instruction of the module; the slot is the last thunk slot the module ran.
2. **Module startup** (`run_function.Base.startup(module)`): loads the EXE with
   `FinalProbe.start_process` (PSP, environment, relocations), puts a 12000-byte
   shared block in its own DOS block with its far pointer at 0000:04F0, and runs
   the module's own crt0 until `main` (found as the `call` before
   `push ax; call _exit` in `__astart`). Used for GAME in the self-test and as the
   default of the CLI without `--state`.
3. **Fuzzing** (`make_vectors.py`), bounded by the typed model in
   `oracle_config.py`. Each seed is run once with read tracking. *Model* mutants
   only change globals and shared fields that the seed reads AND that
   `GLOBALS` / `SHARED` list, within the range derived from the C (array bounds,
   loop limits: `alphabet_len` 0..26 for the 28-entry tables, letters 'A'..'Z' in
   the alphabets, `NO_GUESS` or a letter in `guess_table`, `CODEdiff` -3..5,
   `Rp1` = Rp1X/RpBackX, RasterPort fields, ...), and arguments within `ARGS`
   (`int`, ranges, sets, `port`, `cstr`, `buf`, `local` = the caller's frame,
   `seen` = values the real sessions passed; `ARG_CHECKS` for cross-argument
   bounds such as `fill_rect` y+h <= 200). Keys, driver results and the clock
   are always in the model. *Free* mutants may write any byte the seed reads, any
   argument word or register. Model mutants run first; free ones only for blocks
   still unreached. A mutant is kept when it reaches new basic blocks (up to
   `--max-cases`, 8); functions with fewer than `--min-cases` (3) cases get
   mutants with a different observable outcome. Unreached functions start from
   the `base_wait` DGROUP with arguments built from `ARGS`; DGROUP F000/F100/F200
   then hold scratch strings that pointer arguments may point at.
   Every case carries `"realistic": true|false`: true for captured states and
   for mutants that stayed inside the model, false otherwise (tests may skip
   those). The id suffix still says where the case came from (`realistic` =
   captured, `synthetic`, `fuzz`, `variant`, `scripted`).
4. **Scripted cases** (`oracle_config.SCRIPTED`), added on top of each
   function's first realistic states: menu lines picked with Return or hot keys
   (`_quit_prompt`, `_draw_menu_box`: second line "Yes", first line, up/down past
   the ends, Y/N, ESC), Alt-Q Yes = GoodBye + exit in `_crypto_key_step` and
   `_DoCode`, Alt-P, the 999-try hint (`patch: all_guessed`: every used letter
   already guessed), the spare table entries 26/27 (`{`, `|`) and -1 (`` ` ``) as
   letter and as guess, and the missing disk in `_CheckForDisk`
   (`overrides: {"_find_file_on_disk": [-1, 0]}`). In a key script `WAIT` holds
   the following keys back until the program has seen an empty buffer, so the
   `flush_input` before a menu does not drain them.
   An entry may also give `set` (DGROUP offset -> word, `("dword", v)` or bytes),
   `args` (the argument words) or `patch: (name, params...)`; such made-up states
   are `"realistic": false` unless the entry says `realistic=True`. Each entry
   starts from a captured (or synthetic) seed, never from another scripted one.
   TAC: whole `_generate_building` runs (MSC rand state 4026 x GameMode 0..3 x
   Diff), `_add_wall_segments` with several rand states and the 128 bytes it
   reads west of `wall_flags` (cf0a..cf89) as 00/0f/05, `_raise_alert(-1, ...)`
   in every GameMode, the 9999 cap, and alarm states (Eseen, ToRoom, GameMode),
   `player acted`, Rtime 0 / spawn ticks and the player placed next to guard 1
   (`tac_player_near_guard`: walls or partial cover on the line of sight, running,
   fire timer, guard sneak bit, player facing, rounds) for `_update_guard`,
   `_guard_sees_player`, `_aim_and_fire` (the player firing at guard 1 from four
   sides, guard 1 firing at the player), `_guard_behaviour`, `_room_cost`,
   `_choose_next_room`, `_spawn_guard`. Scripted and donor cases do not count
   against `--max-cases` / `--min-cases`. `BUDGETS` raises the per-run instruction budget
   (`_generate_building` 40M).
5. **State donors** (`oracle_config.DONORS`), for functions no recorded route
   reaches: the DGROUP/shared block captured at a donor function's entry on the
   real routes (GAME: `sub_game_00f7_8755`, the caller of `_release_person` and
   `_clear_person_knowledge`; up to `DONOR_STATES` captures, spread over routes)
   with arguments read from that state. `realistic` only when the call matches
   what the real caller passes (`clear_person_knowledge(cast member, 8)`);
   `_find_or_add_person_quiet` and `_clear_event_steps` have no caller in GAME,
   so their cases are flagged. Donor cases replace the synthetic ones and are
   always kept. `DONORS_ALWAYS` adds donor cases on top of a function's own
   captures (GAME `_search_yield`: the CIA building's local and international
   scans on cia_building_menu's states; the recorded calls are the briefing's).
   FINAL `_write_save_game` takes the save screen's state (ask_save_drive's
   entry) with `save_path`, as `select_save_file` calls it.
   The coded messages' paragraphs, which no recorded route composes:
   `read_random_variant` (FINAL `read_random_text_tag`, GAME, TAC, BUG) on the
   states of a text being read (`read_cleared_text`'s entry; BUG: its menu box),
   called as the message composers call it (`"text.dta"`, `"*SLOC00"` 8,
   `"*RORG00"` 8, `"*FLUF00"` 12, `"*AIDD00"` 2, the literals of the program);
   BUG's `read_text_with_vars` and `read_cleared_text` likewise, and GAME's
   `trim_text_to_width(300)` (GAME has no caller: flagged).
   `KEEP_CAPTURES` keeps every distinct captured state of a function, not only
   the first three and those reaching new blocks (GAME `_draw_actor`), and
   `MERGED_ENTRIES` adds entry points that neither functions.json nor names.txt
   has, under their label (the empty `sub_final_0100_af04`, `sub_game_00f7_b6e2`).
   These were recorded into `build/oracle-next-20261116` (a copy of
   `oracle-next-20261115` plus `make_vectors-<MODULE>-copies.log`).

   Copies that remain beyond the recordings (a program that never runs its copy,
   or runs it only where the C# cannot replay it) are proven the same routine as
   a recorded copy on the player's programs: `Harness/TwinCopies.cs` compares the
   two bodies byte for byte and accounts for each difference as a call of the
   same routine, a pointer to the same text, or a variable it lists for both
   programs (`TwinCopiesTests`).

Before a case is kept it is run again with only its recorded `inputs` (replay
mode) and must give identical outputs, so the inputs list is complete.

## Running one call

* The memory image of the base is loaded, then the case's DGROUP and shared
  block. The module (CS = load + frame) is never modified by the harness.
* **Frames.** Labels normalise CODE's `_TEXT` to frame 0000, but C and library
  code really runs at CS = load+00B4 (the EXE entry frame); its near function
  pointers and switch tables depend on it. The runner uses the real frame
  (`call.entry` in the vector, `00b4:xxxx`); frame-0000 assembly runs at CS = load.
* **Call.** cdecl: int16 args pushed right to left, then a near (`ret`) or far
  (`retf`, the frame-0000 assembly) return address to a sentinel (CS:0000 of the
  function's frame, a non-code address). The run ends when execution reaches it.
  `__aNlmul` pops its own 8 bytes. The program entry `__astart` runs with DOS
  entry registers (DS = ES = PSP, SS:SP from the header) and ends at INT 21h/4Ch.
* Every C function starts with AX..DI, BP = 0, DS = SS = ES = DGROUP, FLAGS
  0202; assembly/library cases set the registers in `regs_in`.

## Boundaries and conventions

Execution never leaves the module: module code (C, assembly, C library) runs
for real; these are stopped, recorded as `{"target","args","ret"}` and answered
from the scripted inputs.

| boundary | detected as | target | args | ret / input |
|---|---|---|---|---|
| driver call | execution outside the module's DOS block (the frame-0000 thunk slots jump far into MGRAPHIC/MISC/sound drivers; 0ad9 calls slot pointers indirectly) | names.txt name of the thunk slot, else its label (`sub_code_0000_0224`); `_boundaries.json` lists all slots | stack convention: the N words above the far return address (N = largest caller clean-up of that slot); register convention (slots only called from assembly): `[ax,bx,cx,dx,si,di]` | input = int; the stub returns with `retf`, AX = ret & FFFF, DX = ret >> 16, other registers unchanged |
| software interrupt | `INT n` | `intNN.AH` e.g. `int21.0b`, `int10.05`, `int33.03` | `[ax,bx,cx,dx,si,di]` before the INT | input = dict of registers set after the INT: `ax bx cx dx si di es`, flags `cf` (default 0) and `zf` (unchanged unless given), `data` (base64 bytes written at DS:DX); ret = AX after |
| program exit | INT 21h/4Ch, 00h, 31h, INT 20h | `int21.4c` | as above | no input; case gets `"exit": AL` and stops |
| port read | `IN` | `inb` / `inw` | `[port]` | input = int (value read) |
| port write | `OUT` | `outb` / `outw` | `[port, value]` | no input, ret 0 |
| MicroProse assembly library routine in `MODULE_CUTS`: `_fill_rect` (0576; FINAL/TAC/BUG 0602), `_draw_line_port` (097a; FINAL/TAC/BUG 0a06), `_find_file_on_disk` (0002), found by module and address (`MODULE_CUT_AT`), not by names.txt name (BUG's own `_fill_rect` 6f5a is the corner variant; its 0602 is named `_fill_rect_port`) | execution reaches the routine (far, stack arguments) | the cut name (`_fill_rect` also in BUG) | its stack words; `_find_file_on_disk`'s name as `{"ptr","str"}` | input = int (find: 0 found, -1 = FFFF missing; default 0); the routine is not executed, so its span tables (DS:1395..170b) and the INT 21h it uses are not in the caller's vector |
| C library routine in `LIBRARY_CUTS` (CODE: `_time`; also `_open _dos_open _read _write _close _lseek _access` where linked) | execution reaches the routine's entry (not when it is the function under test) | its names.txt name, e.g. `_time` | its stack words (count from the caller's clean-up) | input = int (ret), or `{"ret", "out"}` when it delivers bytes; the routine is not executed (no INT 21h beneath), AX (DX:AX for `long`) = ret, bytes it writes through a pointer (time_t of `time(&t)`, `_read` buffer, `_dos_open` handle) are written and recorded as the call's `"out"` (base64) |

Emulator workaround: unicorn 2.1.4 loads a wrong IP for `RETF`/`RETF n` when
the return address lies under a memory-read hook (far memory always; DGROUP
outside the normal stack in tracked runs, e.g. the CHASE picture decoder's own
stack). `run_function.retf_guard` notes the far return on the read of the
return IP and redoes it after the instruction.

CPU exceptions are not boundaries: when the module installed the vector (CODE
07ac points INT 0 at its divide-overflow handler 0959 around its IDIVs) the
exception is delivered through the IVT with an IRET frame whose IP is the
faulting instruction (286+). Otherwise the case faults and is discarded.

The inputs are consumed in call order by driver calls, library cuts, INTs
(except exit) and `IN`; `OUT` consumes none. A port replays a case by answering boundary call k
with the k-th consumed input.

### Default scripted inputs (`run_function.Script`)

* Keyboard: a key queue (`--keys`, names as in `run_function.KEY`: letters,
  `ESC ENTER SPACE UP DOWN LEFT RIGHT F1..F10 ALT_Q ALT_P ALT_S ALT_J`).
  `_no_key_waiting` returns 1 when the queue is empty, `_read_key_raw` pops a
  BIOS key word, INT 21h/0B returns AL=FF/0, 21h/01,07,08 the ASCII byte (0 then
  the scan code for extended keys), INT 16h/00 the key word, 16h/01 ZF. After 8
  empty polls in a row an idle key (ESC) arrives, so waiting loops end.
  `oracle_config.IDLE_KEYS` gives a function a list of idle keys taken in turn
  (ESC, ENTER) where a wait ends only on Return (FINAL's `edit_text_field` and
  its callers, CHASE's start).
* Clock: INT 21h/2A = 5 Nov 1990, 21h/2C = 12:00:00 advancing one second per
  call, INT 1Ah/00 ticks from 1234h advancing one per call.
* DOS: 30h -> 5.0, 19h -> drive C, 0Eh -> 3 drives, 11h FCB find -> found,
  29h -> AL 0, 35h -> 0000:0000, 44h -> device 80D3 for handles < 5,
  48h -> a block from one free region above the program's block
  (`block_end` + 1 .. A000h, handed out from the bottom, an MCB paragraph
  before each), or CF + 8 and BX = the largest block; 49h frees (a block from
  before the run too); other services: no register change, CF clear.
* DOS files (`Script._dos_file`), served from the player's game folder
  (`game/Covert Action/Covert`, never written; path and drive are dropped, the
  name is upper-cased): 3Dh open -> lowest free handle from 5, or CF + 2 when
  the file is missing; 3Fh read -> the next CX bytes (fewer at the end), written
  at DS:DX; 42h seek (AL 0/1/2, CX:DX) -> DX:AX new position; 3Eh close;
  3Ch create and 40h write work on an in-memory copy for the rest of the run
  (open for writing too; 40h on a read-only handle -> CF + 5). Unknown handles
  >= 5 -> CF + 6; handles 0-4 are devices (read 0 bytes, write all). The call
  record gets `"file"` (the name) on 3Ch/3Dh/3Eh/3Fh/40h/42h and `"out"` (the
  bytes read, base64) on 3Fh; the input dict carries the same `file`/`data`.
* Mouse INT 33h: absent (AX=0), status/press/release all zero.
  BIOS 10h/0F: mode 13h. Ports: 3DAh alternates 8/0 (retrace on/off), 201h FF.
* Library cuts: `_time` = 12:00:00 local on 5 Nov 1990 in MSC's default TZ
  (PST8PDT, so 20:00 UTC = 657835200) plus one second per call (the same
  counter as INT 21h/2C; the fuzzer varies the clock). Files: the same game
  folder and handles as the INT 21h services. `_open` as MSC 5.1 (GAME ed64):
  missing file -> -1 unless O_CREAT (0x100); O_CREAT|O_EXCL on an existing file
  -> -1; O_TRUNC empties the in-memory copy; text mode unless O_BINARY (0x8000)
  (`_fmode` taken as its default, text). `_read` as MSC 5.1 (GAME ef08): binary =
  the DOS read; text = the DOS read of n bytes compacted in place (CR LF -> LF, a
  lone CR stays, Ctrl-Z ends the copy and sets the handle's end-of-file flag so
  later reads return 0 until an `_lseek`; a CR as the last byte peeks the next:
  LF -> the CR is dropped and the LF read next time, or returned when n = 1);
  `out` is the whole buffer as the real routine leaves it (the bytes past the
  count keep the raw data). `_lseek` (GAME ecea): negative result or whence > 2
  -> -1; success clears end of file. `_write` on a file handle extends the
  in-memory copy (the cut does not see the bytes); handles 1-4 take everything.
  Library file calls carry `"file"` in the call record.
  `Script(files={NAME: bytes})` replaces the folder.
* Image draw (`DRAWN_AREA_SLOTS`: GAME/CHASE 0134, FINAL 01c0, the MGRAPHIC
  draw at entry 0b15): computed as the real driver (`run_function._drawn_area`,
  checked against the driver code on 3500 random cases): args = the 4 stack
  words (port, x, y, image segment); ret = DX:AX = the driver's record (07a6 in
  the driver segment) whose 8 bytes x, y, width, height after clipping to the
  port (+6/+8 = last column/row; all 0 when there is no image or it is wholly
  outside) are written there, recorded as the call's `"out"` and listed in
  `far_out`; `_draw_image_tracked` reads them to record its spans. Nothing is
  drawn.
* The graphics driver's memory and images (MGRAPHIC e50a, ea02, 092e, 0a5b,
  0951, 099c, as their driver vectors show): `image_memory_free` (FINAL 019f,
  GAME 0113) = the largest free block in paragraphs (the driver's 48h with BX
  FFFF); `begin_image_cuts` takes that block and answers 1 (0 when there is
  none); `cut_image` (w x h) and `begin_image_build` / `finish_image_build`
  answer the segment of the next image in it, a different handle each call (a
  16-byte head, then the pixels); `add_image_row` answers 1 while an image is
  being built; `end_image_cuts` gives the rest back. So the PAN loader reads
  its pictures and script, and a table filled with image handles shows their
  order.
* The C library's table of open files (`oracle_config.OSFILE`: `_osfile[20]`
  in DGROUP, found by its use in each module's `_read`): as the cut `_open`,
  `_read`, `_close` and `_lseek` do not run, the runner writes it as they would
  (GAME ed64, ef08): open = 0x01 | 0x80 for text | 0x20 for O_APPEND, close =
  0, a successful lseek clears 0x02; a text read clears 0x04 and sets it when
  the bytes read began with a line feed, and sets 0x02 at a Ctrl-Z. The read's
  call record and input carry the resulting byte as `"osfile"`, so a port (the
  library's text `fseek`) can answer from the record.
* Other drivers return 0 (`_char_width` 6). `Script(overrides={"_name": [..]})`
  scripts a driver's results; `{"*": [..]}` rotates values for every non-key
  driver call (used by the fuzzer).

To add scripted inputs, extend `Script.driver` / `Script.interrupt` /
`Script.port_in` (or pass `overrides`), or give `--keys` on the CLI. File reads
(21h/3F) can return data via a `data` entry in the dict.

## Vector format

`build/oracle/<MODULE>/<label>.json`

```
{"module","label","name",
 "call": {"far","iret","nargs","convention","entry":"FRAME:IP","program_entry"},
 "segments": {"psp","load","dgroup","dgroup_size","shared":"SSSS:OOOO"},
 "cases": [{"id","args":[int16...],
            "dgroup_in":"<base64 DGROUP>","shared_in":"<base64 12000 bytes or null>",
            "inputs":[...],"result":{"ax","dx"},
            "dgroup_out":"<base64>","shared_out":"<base64 or null>",
            "calls":[{"target","args":[ints or pointer objects],"ret":int,
                      "out":"<base64>", "args_mem":{"3":"<base64>"}}],   (optional fields)
            "far_in":{...},"far_out":{...},          (only when non-empty)
            "stack_mask":[lo,hi],
            "regs_in":{...},"regs_out":{...},        (non-C functions)
            "exit":code,                             (only when the case exits)
            "origin":"capture decode-v1 hit 3 keys ESC | synthetic ... | ... fuzz alphabet_len,arg0",
            "realistic":true}],
 "ignore":[[lo,hi],...], "ignore_reason":"..."}   (library routines' own vectors only)
```

* `args` are signed int16; call args, ret, result, registers are unsigned words.
* DGROUP is the whole 64 KiB data segment window (`dgroup_size`; CODE: 65536 at
  segment 38AB). `dgroup_in` is what the function saw, its argument frame included.
* `stack_mask` = [lo, hi) DGROUP offsets: the dead stack below the entry SP
  (from the stack segment base, CODE 1DF0) up to the end of the argument words.
  In `dgroup_out` these bytes are reset to the `dgroup_in` values (locals, saved
  registers, return address are not port-visible). Caller frames above stay.
* `far_in` / `far_out`: memory outside the module code, DGROUP and the shared
  block, keyed by 5-hex-digit linear address, value base64 of a contiguous run:
  bytes read before being written (`far_in`) and the final values of written
  bytes (`far_out`). CODE needs them for the IVT (07ac saves/patches INT 0,
  _find_file_on_disk INT 24h), 0000:04F0 (shared pointer), the PSP and
  environment (crt0), and driver headers (_attach_driver_segment reads
  `seg:0028..`). Linear addresses only make sense with `segments`.
  The module's own far data segments (`FAR_DATA`: FINAL 0fb4 and GAME 10e2,
  the animation engine's `F_0fb4_0000` / `F_10e2_0000`, up to DGROUP) count as
  far memory too; `segments.far_data` gives their absolute paragraph and size.
  Their input content comes from the base image (`start`/`wait`) of the route:
  the captures store DGROUP and the shared block, not these segments.
* Pointer arguments of boundary calls are captured at call time
  (`oracle_config.DRIVER_POINTERS`, `LIBRARY_CUTS[..]["ptrs"]`): the argument word
  is replaced by `{"ptr": n, "str": base64}` (C string, NUL excluded; e.g. the
  text of every `_draw_string`, a file name) or `{"ptr": n, "mem": base64}` (fixed
  length: palettes, colour maps, write buffers). Interrupt services that take
  DS:DX data (`INT_POINTERS`: 21h/09, FCB 0F/11/12, file names 3C/3D/41/43/4E/5B,
  write 40) get `"args_mem": {"3": base64}` (index 3 = DX), the numeric args stay.
* `ignore` (top level): in the vectors of a cut library routine and of the
  internals it runs (`_time`: `___tzset _tzset __isindst __dtoxtime`), the DGROUP
  bytes their own runs changed outside the stack mask and outside the pointer
  target: the library's private data (CODE: 1B6E, a tz byte). Everywhere else the
  routine is a boundary and writes none of it.
* `_index.json`: per function signature, case count, covered blocks, origins,
  failures and skip reasons. `_boundaries.json`: thunk slot table.
  `_library.json`: the cut list and the private ranges found.

## Driver oracle (`driver_vectors.py`)

Vectors for the entry slots of the drivers, as CODE calls them, so the port's
`IGraphicsDriver` (and input/sound drivers) can be proven pixel by pixel.

* **Capture.** The same three CODE routes run with the same probe; each time
  execution enters a driver through one of CODE's thunk slots (entry = the far
  pointer in the patched slot) the whole machine is stored (1 MiB, zlib),
  with registers, the DAC palette (the probe's INT 10h/1012 model) and overscan,
  at hits 1..8, 12, 16, 24, 32, 48, ... per entry
  (`build/oracle/states/DRIVERS/<route>-v1/`).
* **Run.** Only the driver's own instructions run (from the entry until the far
  return to the caller). INT 10h/10xx (palette block/single set and read,
  overscan) and the DAC ports 3C7/3C8/3C9 are modelled on the palette; other
  interrupts and port reads (3DAh retrace) are boundaries answered by the same
  `Script` as module vectors and recorded with their inputs. Each case is
  replayed from its inputs before it is kept.
* **Cases.** Every captured entry whose outcome or covered driver blocks are new
  (`NN-realistic`), plus bounded variations of the stack arguments
  (`NN-variation`, `oracle_config.DRIVER_ARGS` / `DRIVER_ARG_CHECKS`: ports
  Rp1X/RpBackX, x 0..319, y 0..199, rectangles inside the screen, colours,
  strings of CODE's DGROUP). Slots the routes never reach get `NN-synthetic`
  cases built on another entry's real driver state with arguments from the C
  (`DRIVER_SYNTHETIC`: `show_page` 0/1/2, `set_palette` with the two EGA tables).
  Register-convention slots (0216, 021d, 0224, 022b, 0255, 025c, 0208) have real
  captures only. All driver cases are `"realistic": true`.

`build/oracle/<DRIVER>/<slot name>.json` (`MGRAPHIC`, `MISC`, `SOUND`): one file per
driver entry, whichever modules call it; named after the first names.txt name of a
calling slot (`_draw_string`), else a slot label (`sub_code_0000_0224`), else
`entry_<offset>`; `callers` lists every (module, slot, name) seen. Captures from
all modules and routes (`states/DRIVERS/*`) are merged per entry:

```
{"driver","slot","slot_offset":"0198","module":"CODE","callers":[{"module","slot","name"}],
 "entry":"137a:107a",
 "convention":"stack"|"regs","nargs",
 "segments":{"driver":"137a","driver_size":9152,"video":"a000","dgroup":"38ab",
             "pages":["a000","15ee","258e",...]},          (MGRAPHIC page table, CS:17f0)
 "cases":[{"id","origin","realistic",
           "args":[words or {"ptr","str"|"mem"}],        stack arguments as CODE pushed them
           "regs_in":{ax..di,bp,sp,ds,es,ss},             register-convention slots read these
           "driver_in":"<base64 whole driver block>",     code + data: the driver state
           "video_in":"<base64 A000:0000, 64000 bytes>","palette_in":"<base64 768 6-bit DAC>",
           "overscan_in":n,
           "far_in":{"<linear>":"<base64>"},              other memory read before written: the
                                                          port record and text in DGROUP, page
                                                          buffers (copy_rect/show_page), ...
           "inputs":[...],"calls":[{"target","args","ret"}],   INT/port traffic
           "result":{"ax","dx"},"regs_out":{...,"flags"},
           "driver_out":{"<linear>":"<base64>"},          changed driver bytes
           "video_out":{"<offset hex>":"<base64>"},       changed pixels (offset in A000)
           "palette_out":"<base64>" or null (unchanged),"overscan_out":n,
           "far_out":{"<linear>":"<base64>"},             final values of other bytes written
           "stack_window":[lo,hi]}]}                      SS offsets not tracked (caller stack)
```

The caller's stack window (`SP-400h` .. the argument words) is excluded from
`far_in/far_out`: its dead part is scratch, the arguments are in `args`, and a
Unicorn 2.1.4 read hook on the stack breaks MGRAPHIC's intra-segment `RETF` at
0e85 (it re-executes it with a stale CS). `_index.json` per driver lists slots,
entries, case counts and failures. The self-test checks that `show_page(n)`
puts page n (read from `far_in`) on the screen and that a full-screen
`copy_rect` from Rp1X to RpBackX writes the screen into page 1.

## What the C# side must know

* Segment values are the capture's (`segments`), and they appear in data: far
  pointers stored in DGROUP or the shared block (driver segments in the shared
  header, the dispatcher saving a far return at DGROUP 1266/1268 when the mouse
  flag 1265 is set, `int21.25/35` vectors, `int21.4a` ES). Compare such words
  modulo the segment mapping, or map the port's segments to these values.
* Driver stubs only return AX/DX: a port's driver layer must be mocked from
  `calls` (args checked, ret fed back), not executed. Driver side effects on
  memory (screen, driver tables) are not modelled; neither are DS/ES of INTs.
* The C# side must not depend on stack layout: masked bytes are not compared;
  functions that keep data on the stack beyond their frame (crt0 `__setargv`
  builds argv below the caller's SP) have that data inside the mask.
* Code-segment writes (the 00ca trampoline patches its own `jmp far` operand) are
  not reported.

## Limits

* Realistic coverage is what the three CODE routes reach (78 of the database
  functions); the rest (mouse, joystick, animation stubs, unreferenced library
  routines, 09bf/0a6f/0ab3) run from the workstation state with synthetic and
  fuzzed arguments.
* Model ranges are per variable, not joint: a "realistic" fuzz case can still
  combine values no session produces (they are valid states of the typed model).
  Bytes not listed in `oracle_config.GLOBALS` are only changed by free mutants.
* Fuzzed globals may be inconsistent with each other (e.g. a guess table that no
  real game state produces); they are valid machine states of the original code.
* Interrupt/driver defaults are a fixture, not DOS: an unusual service gets "no
  change"; budgets: 2 M instructions per segment between boundaries (20 M for
  `__astart`), 20 000 calls per case (50 000 for `_DoCode`/`__astart`).
* Skipped (not callable as functions): the zero bytes at 0000, the driver-call
  trampoline 00ca, the INT 0 handler 0959, the DOS-wrapper epilogue tails
  `__dosret0/__dosreturn/__dosretax`, and `__cltoasub/__cxtoa` (entered by jump
  with `_itoa`'s frame); reasons in `_index.json`. Their behaviour is exercised
  inside the vectors of their callers.
* Driver vectors cover the slots CODE uses (14 MGRAPHIC, 2 MISC, 1 sound);
  unused slots, and the register slots 016c/01d5/020f that the routes never
  reach, have none. MISC/sound cases need no video; their fields are still
  present.
* Only CODE has captured states so far. Other modules need routes in
  `capture_states.py` (GAME: `trace_game.py`) or use `Base.startup`.

## Status of the 2026-11-13 oracle round (done / left)

Done:

* BUG's fill cut at 0000:0602 (`MODULE_CUT_AT`, by module and address).
* Files served from the game folder (INT 21h 3Ch-42h, `_open/_read/_lseek/_close/_write`
  cuts as MSC 5.1, text-mode `_read`), file name and bytes in the call records;
  the vectors that touch files were regenerated in every module.
* `_draw_image_untracked` stand-in returns the driver's drawn-area record.
* unicorn 2.1.4 RETF workaround (`retf_guard`).
* FAR_DATA: FINAL 0fb4 / GAME 10e2 tracked in `far_in`/`far_out`; regenerated:
  the engine routines (FINAL 0100:abd2-bdc4, GAME 00f7:b3b0-c5a2), the functions
  that reference the segments and all their callers (FINAL 70, GAME 87).
* TAC scripted states (building generation, guards/alarm, cover, firing,
  grenades/gas/traps, `_tac_init` with 150M budget); GAME donor states for
  `_clear_person_knowledge`, `_release_person`, `_find_or_add_person_quiet`,
  `_clear_event_steps`.

Left:

* Far data segments start from the route's base image, not from the capture
  moment: the captures do not store them. A recapture (new tag) that stores
  the segments would make engine cases consistent with their DGROUP.
* Truly realistic states for `_release_person` / `_clear_person_knowledge` need a
  route where a known cast member travels (airport surveillance, 8755); the
  recorded routes never reach it. `_find_or_add_person_quiet` and
  `_clear_event_steps` have no caller in GAME (flagged cases only). TAC grenade,
  gas and trap states are made up (flagged): no recorded raid throws one.
* With real files served, some long scene/program functions no longer finish
  within budget (animation playback, title, briefing, `__astart`, CHASE
  dashboard, GAME building menu): `_index.json` says skipped; their older vectors
  (fatal-error exits) are kept in `<MODULE>/_stale/`.
