# Whole-session proof

Every original function is ported and most are proven one by one against the original (`tools/oracle`,
vectors in `build/oracle`, `CovertAction.Oracle.Tests`). A function oracle cuts the program at its
boundaries and checks one call. It cannot show that the parts fit together: that the C# calls the right
functions in the right order, with the right state between calls, through the game's own drivers, frame by
frame. The session proof does that. The same inputs (keys, time of day, hand-off block, game files) go through
the whole original program and through the rewrite. The two must give the same screens, colours, sounds and
hand-off block, and later the same written files.

Rule (ARCHITECTURE.md, "No emulator"): the original runs only in the tool (`tools/session/`, Python and
Unicorn). The product does not know it. The C# side is the product's own path (`Game.RunPart` on the headless
host for one part, `Game.Main` for a whole session), watched through two events meant for tests and tools.

Status (14 November 2026): **working for CODE (cryptography practice and a campaign message), CHASE (practice
and a campaign car chase), FINAL (the menus part, the debriefing, loading a saved game), GAME (the campaign walk,
interrogations, the end of a case, a loaded game, the returns from CODE, CHASE and TAC, a capture and its exchange or
escape, an ambush) and TAC (the building raid: practice at each level, campaign raids, a bug planted, a disguise, a
capture, the escape and the street ambush) and BUG (the wiring puzzle: practice at each level, campaign wiretaps
solved, alarmed, timed out, given up and quit, with their returns)**. 7 + 3 + 20 + 22 + 13 + 9 committed sessions are
recorded and agree, apart from four registered differences (D-17, D-11, D-22, CA-1).

Status (15 November 2026): **whole sessions (item 7)**: the original's launcher COVERT.EXE run from the DOS prompt with
every program it starts, against the rewrite's `Game.Main`: the set-up screen in text mode, INTRO, FINAL, a new case and a
loaded one, GAME, a wiretap, a raid, a car chase, the practices and their returns, a saved game, the PC speaker's driver,
the joystick answer, and the game left from GAME, CODE and the menus. 5 committed whole sessions (8553 checkpoints) agree
across every part boundary, apart from registered differences (D-600, D-602, D-11, D-17, CA-1). All 79 sessions agree.

Status (15 November 2026): **the joystick moving (item 12)**: a stick on both sides' game port in a whole session
(joystick-menu, 70 checkpoints): the calibration, the menus moved by the stick and its buttons, into a practice; agrees
apart from D-600 and D-602.

Status (15 November 2026): **DOS's refusals (item 14)**: a save that cannot be made ("File Error") and the floppy prompt
with TAC.EXE missing, recorded on both sides through the spec's `dos` (2 sessions, 655 checkpoints, agree); a full disk,
missing files and saved games that are not whole read from the original's code and tested on the rewrite. Three C# bugs
fixed (Windows' file exceptions let through, a failing part's file name dropped, three parts' fatal error taken for the
player's leaving); one register entry (D-105).

Status (15 November 2026): **the sound devices (item 9)**: whole sessions with the AdLib, Roland MT-32 and Tandy drivers
chosen at the set-up screen, comparing every OPL register write, MIDI byte and SN76496 byte (and the PC speaker) with the
timer's ticks at every checkpoint, and the Custom choice's "Filename not found" (D-18). 7 more whole sessions (21104
checkpoints) agree, apart from registered differences (D-18, D-20 without the ROMs, D-600, D-602, D-11, D-17).

## 1. What runs the original

`tools/session/original_side.py` runs **one part's program whole** (CODE.EXE, CHASE.EXE, ...) in Unicorn,
from its first instruction (`__astart`) to its `exit`. It runs with the **real drivers** the launcher had
loaded: MGRAPHIC.EXE draws into the mode-13h screen at A000:0000, MISC.EXE reads the keyboard through the BIOS,
and the sound driver is entered. Nothing of the program or the drivers is replaced or patched. Around the
program is the smallest machine it needs. Each service is written out and named, and anything else stops the
run with "not modelled: ..." (never a guess):

| Need | Model |
|---|---|
| memory at start | the 1 MiB start image of `tools/oracle` (`build/oracle/states/<PART>/<route>/base_start.mem`: the part loaded by DOS, drivers resident, the hand-off block the menus part wrote). The session's hand-off block is written over the one in it |
| keyboard | INT 16h 00/01/02/10/11 and DOS 07/08/0B, fed from the key script (section 2). A keypad key is given by Num Lock when it is taken: bit 20h of the BIOS keyboard flags at 0040:0017, which the raid writes directly (item 5) |
| cursor | INT 10h AH=03 answers the cursor's position and shape from the BIOS data (0040:0050, 0040:0060): the raid asks it once at its start (TAC 00e8:0018, AX=0305h, meant as INT 16h's repeat rate); only its register buffer gets the answer |
| time of day | the C library's `time` is a boundary (ARCHITECTURE.md section 4: `IGameTimer.Time`). It is not run; it returns the session's `unix_time`, as the oracle's library cut does. DOS 2A/2C stop the run |
| frames | the retrace bit (port 3DAh) alternates, so each wait for a frame passes after one frame |
| colours | the VGA colour registers: ports 3C7h/3C8h/3C9h (read-back included, for fades) and INT 10h AX=1010h/1012h/1015h/1017h, as Darklands' `vis10.py` tracks them |
| VGA mode registers | 3C4/3C5, 3CE/3CF, 3D4/3D5 are kept so that they read back; 3C0, 3D8/3D9, 20h/21h are ignored |
| DOS | version, vectors, memory blocks (4Ah/48h/49h), IOCTL 4400h, drives, DTA, open/read/seek/close from the player's game folder (read only), "does it exist" (4300h). A file created (3Ch) or opened to write (3Dh, a copy of the player's bytes) is kept in memory, read back from there and recorded at the end (`written/`); the player's folder is never written |
| PC clock ticks | the C library's `_bios_timeofday` reaches INT 1Ah AH=0: the count starts at 1234h and goes up by one at each read. The animations only wait for a span of ticks (`wait_frame_tick`), so the number of reads in a wait is not seen; the C# `GameTimer` counts its own ticks |
| floppy search | MicroProse's `find_file_on_disk` is a boundary (`IFiles.FindFileOnDisk`): it returns "found" and is recorded; "not found" for a file the spec's `dos` takes away (item 14) |
| the disk's refusals | spec `dos` (item 14): `missing` game files are not found by open, 4300h or the floppy search; `cannot_create` files are refused by create (3Ch) and open to write (3Dh) with DOS error 5 |
| animation files | watched, not changed: the animation player's `load_pan_file` (its file name, and whether it loaded) and `release_pan_memory` are recorded as calls (`pan_load`, `pan_release`), so that the comparison knows when the original plays a half-loaded animation (D-11) |
| CPU exceptions | divide overflow goes through the vector the program installed (CODE's handler at 07ac) |
| mouse, joystick | absent (INT 33h AX=0, port 201h = FFh, writes to it ignored), as `NoMouseDriver` and `KeyboardDriver`; a whole session may put a stick on the game port (item 12) |

**A whole session** (item 7, `tools/session/campaign_side.py`, `CampaignSession` over `OriginalSession`) needs no start
image: the machine starts empty at the DOS prompt and runs COVERT.EXE from its first instruction to its end, and every
program it starts. Besides the services above:

| Need | Model |
|---|---|
| DOS processes | an environment block, a PSP per program, its image loaded and relocated into the largest free block; EXEC 4B00h (the parent waits; its registers come back at the child's end), 4C00h and INT 20h (the child's memory and files are given back), 4Dh, 51h/62h, 29h (empty command tails), 47h; EXEC 4B03h loads a driver as an overlay at the segment the launcher allocated (?GRAPHIC.EXE with FONTS.CV, MISC.EXE, ?SOUND.CA, and INTRO's own MISC.EXE) |
| DOS memory | blocks with owners in the "compact" layout of `tools/memory_layout.py` (programs from 01F0h to A000h), one paragraph kept between blocks for DOS's arena header. The parts' memory is then the chain's own, not a start image's: D-11 (an animation that does not fit) is met where this arena runs short |
| the cuts and watches | `time`, `find_file_on_disk`, the sound slot and the animation player's file loads are placed at each program's load address when it starts and taken away when it ends |
| text mode | the page of video mode 3 at B800:0000 and the BIOS's text services (INT 10h AH=00/01/02/03/06/07/08/09/0A/0E/0F); DOS's console output (02h, 06h, 09h, 40h to handles 1 and 2) through the teletype, DOS 01h with its echo. A mode set to 13h clears A000:0000 |
| the launcher's keyboard look | COVERT's C library asks DOS whether a key is waiting before each EXEC (COVERT 0000:0EBB, INT 21h AH=0Bh). The rewrite has no counterpart (D-602), so that look takes no script entry; it answers whether a key is held (it never removes one) |
| the timer | INT 8 is delivered once a program has put its own handler on it (the PC speaker's driver ISOUND.CA plays its music on it, and BUG waits for a tune to end): the run goes in spans of one timer tick at the rate the driver programs (port 40h), at 8 instructions per tick of the timer's 1.193182 MHz clock (about 10 million instructions a second), each tick pushed as the interrupt controller would when interrupts are enabled. The BIOS's own INT 8 and INT 1Ch are an IRET. Ports 61h (kept), 42h/43h (the tone, accepted) |
| the graphics question | answered from the spec's `graphics_key` (D-600, below), not from the key script |

This continues the project's earlier native tooling. The DOS fixtures of `tools/trace_startup.py`,
`trace_final.py` and `trace_code.py` played
COVERT -> INTRO -> FINAL -> CODE and stopped at key waits. The session tool starts at the part instead, from a
captured image. That makes a CODE session take under a second and a 400-frame chase about 3 s, and the
hand-off block becomes an input that a session can vary.

Why Unicorn and not DOSBox: the oracle already trusts Unicorn and its start images, every input can be fed
exactly (no wall clock, no host keyboard), and the screen and colours are read straight from memory. The
Darklands project made the same choice (`dosmachine.py`).

## 2. Inputs, fed alike to both sides

A **session spec** is committed under `tools/session/sessions/<part>/<name>.json`. It holds only what a person
wrote and no game bytes:

```json
{ "part": "CODE", "start": "decode-v1", "handoff": {"level": 3, "skill_bits": 0},
  "unix_time": 600000123, "keys": "{wait 3}ab{wait 2}{f1}{wait 3}{esc}{wait 5}", "note": "..." }
```

* **Hand-off block**: the start image's block, with named fields changed (`level`, `flags`, `skill_bits`,
  `level_copy`, `gfx_letter`, `next_module`, `person`, `value`, `result`, `not_hurt`, `message`; offsets from
  the original's `state.h`). The 12000 bytes actually used are stored in the recording (`handoff-in.bin`). The C# side
  decodes exactly these bytes (`SharedBlockCodec`), so both start from the same block.
* **A block from elsewhere** (`handoff_from`, item 4b): `{"recording": "GAME/stasi-crypto"}` takes the block an
  earlier session's latest original recording left (`handoff-out.bin`): the session continues where that part handed
  over, as the launcher passes the block on (GAME -> CODE -> GAME, GAME -> CHASE -> GAME, GAME -> FINAL). `{"memory":
  path, "sha256": ...}` takes the block out of a 1 MiB memory image of an earlier capture of the original (a played
  route of `tools/trace_game.py`, checked by its SHA-256). Either way the start image's driver segments (0x60..0x65,
  written by the launcher in every run) are kept. The recording's meta names the source; a source recorded again
  since makes the recording stale (`SessionRecording.StaleAgainstSources`).
* **Files on the player's disk** (`disk`): `{"CV0.SVE": {"recording": "GAME/travel-save"}}` puts a file an earlier
  session wrote on the disk (stored in the recording's `disk/`). The original side reads it as one of the player's
  files; the C# side gets a copy of the game folder with it beside the comparison (`RewriteSide.PlayersDisk`). Written,
  it becomes a written file on both sides.
* **The disk's refusals** (`dos`, item 14): `{"missing": ["TAC.EXE"], "cannot_create": ["CV0.SVE"]}` takes game files off
  the player's disk (the C# side's copy of the game folder lacks them) and makes the disk refuse to make a file or open
  it to write (the C# side finds a folder of that name in its saves folder, which Windows does not open as a file).
* **Time of day** (`unix_time`): what `time()` returns on both sides. The C# headless host starts its clock
  there.
* **Random seed**: not a separate input. Every part seeds the C library's generator from `time()`
  (`LibraryRandom.SeedRandom`), so the time of day is the seed. A part that is handed a seed gets it in the
  hand-off block (`random_seed`).
* **Keys**: the headless host's script notation (`{enter}`, `{f1}` .. `{f10}`, `{alt-q}`, a character, `{wait n}`).
  `session_store.parse_keys` resolves it once into BIOS key words. The words are stored in `inputs.json`, and the
  C# side is fed those words, not the script, so both sides get the same codes by construction. A keypad key
  (`{kp1}` .. `{kp9}`, item 5) is stored as `HostKeys.Keypad` over its cursor word: what the program reads depends on
  Num Lock at the moment the key is taken (keypad 8 is 4838h while on, 4800h while off). The original side reads Num
  Lock from the BIOS flags, the C#'s keyboard driver from its own setting (`HostKeys.ResolveKeypad`); both start off,
  as the launcher clears it before each part (`Game.RunPart` now does, so that a part run alone starts as under the
  launcher).
* **Timer**: frames, not wall time. Both sides count display frames (the original's retrace waits, the C#'s
  `WaitForFrame`). The part's own clocks (CODE's elapsed time, the chase's movement) follow from them.
  Interrupt-driven time (INT 8, the sound drivers' timer) is not run on the original side, and the parts
  proven so far do not depend on it. Sessions that need it will feed timer ticks at script positions,
  as Darklands' `tkey:<n>` does (section 7).
* **Game files**: the player's folder (`COVERT_GAME_DIR`). Its executables' SHA-256 is stored in the recording.
* **A whole session** (`part` "COVERT", `tools/session/sessions/covert/`) has no start image and no block: it starts at
  the DOS prompt. Its keys begin with the set-up screen's answers (the sound card, the controls); `graphics_key` ("2",
  MCGA) answers the graphics question the rewrite does not ask (D-600). `part_times` gives the time of day (the seed) of
  each part the launcher starts, in order, the last one for the parts after (`unix_time` before the first): a part's
  session of items 1-6 can be continued with its own keys and time (the specs are written by taking the keys a recorded
  part session used). The C# headless host then tells that time standing still (`HeadlessHost.TimeOfDay`), set at
  `Game.PartStarting`; a whole session is too long for the host's moving clock to give the original's.
* **Joystick** (item 12, whole sessions only): `stick` [x, y, buttons] puts a stick on the game port at the start (x and
  y from -1 to 1, the buttons held as bits); `{stick x y buttons}` in the keys (not an entry) moves it from the next entry
  on. Without `stick` the port reads FFh (no joystick), as before. `inputs.json` holds the stick at each entry and the
  port's reading the tool's model gives for it; the C# side's headless host gets `JoystickReading.FromStick` of the same
  stick when the game takes the entry, after checking that the two models give the same readings.

**How a key reaches the program.** One script entry is one look at the keyboard. A look that finds the BIOS
buffer empty takes the next entry: a key goes into the buffer, a `{wait}` means "no key now". A key in the
buffer stays there until it is read, and a read waits through `{wait}` entries for a key. This is exactly what
the headless host and `KeyboardDriver` do (a `NoKeyYet` entry is a poll that finds nothing). When the script is
used up, the original side stops ("script exhausted") and the headless host closes its window.

## 3. What is compared, and when

A **checkpoint** is the moment the program takes the next script entry. Every key wait is one (a menu box, the
workstation's key loop), and so is every frame of a part that polls each frame (the chase). Checkpoint n on
one side is checkpoint n on the other, whatever the code in between.

| At each checkpoint | Original side | C# side |
|---|---|---|
| picture: 64000 palette indices of the visible screen | A000:0000 | `McgaGraphicsDriver.Screen` (page 0) |
| colours: 768 six-bit DAC values | the modelled colour registers | `McgaGraphicsDriver.Palette` |
| hand-off block: SHA-256 of the 12000 bytes | memory at the block | `SharedState` stored over `handoff-in.bin` |
| sound requests since the previous checkpoint | entries of the `play_sound` thunk slot, with its argument words | `Game.SoundRequested` |
| time and disk calls | recorded (`time`, `find_file_on_disk`, `open`) | not recorded: both are answered from the same inputs |

At the end of the run: the number of checkpoints, how the run ended (`exit` or `script exhausted`), the
sound requests after the last checkpoint, and the whole final hand-off block byte for byte (a differing byte
is named by its field, from `SharedBlockCodec.Layout`).

In a whole session each checkpoint also carries the program that took the entry (`part`: INTRO, FINAL, GAME, ...; on
the C# side the launcher table's program for the code `Game.PartStarting` names, COVERT's module_names), compared first;
while the display is in text mode, the text page and its cursor (`text`: 4000 bytes as at B800:0000, row, column; the
C#'s `Game.TextPage`) are compared in place of the picture. The original side keeps every distinct block whole
(`blocks/<sha>.bin`), so that CA-1 can be told apart in the blocks GAME goes on with after a raid.

To come, in the order the parts need them:

* **Written files**: done (items 3 and 4). The original side keeps every file created or opened to write in
  memory and stores it in the recording (`written/<NAME>`); the C# side writes into `compare-*/written`
  (`GameFolderFiles` with a write folder, which also holds every file opened to write). Compared byte for byte at
  the part's end, and (recording format 2, item 4) at every checkpoint where a written file changed: both sides
  record the SHA-256 of every written file then (`written` in the checkpoint line), so a saved game (CV?.SVE) is
  compared at the checkpoint after its creation and after its last write, not only at the end.
* **Text-mode screens**: done (item 7). The set-up screen's page and cursor at each of its key waits. The launcher
  shows no text of its own in a session: its text mode at the end comes after the last key (D-602).
* **The sound command stream** beneath the requests: the driver's port writes (OPL, speaker), for sessions with
  a sound driver, against the C# drivers' register writes (`CovertAction.Audio` already has register-level
  oracles). For the no-sound driver, the requests are the whole stream.

**Registered differences.** Where the rewrite differs on purpose (an entry of csharp/docs/faithfulness or a
fixed bug of docs/original-bugs.md that shows on screen), `KnownDifference` names the entry, the part and the
screen area, and may hold only under a condition the original side records (D-11: while the original's animation
did not load). One entry concerns the block, not the screen: D-22, bytes behind the end marks of names, counted only
in the final block and only when every decoded value agrees (`SessionComparison.BytesBehindTextEnds`). Those pixels
are not counted, and the report lists every entry that was met and at how many
checkpoints. A second block entry, for the raid only: CA-1 (docs/original-bugs.md, fixed for the player), the word of city
0's activity counter in TAC's final block, where the original's raid counts its awake guards
(`SessionComparison.GuardCountInCityCounter`); every other byte of the block is still compared. In a whole session two more entries, both about what the
rewrite has no counterpart of: D-600 (the set-up's graphics question: one key the original reads outside the script,
reported) and D-602 (the driver segments SHARED+0x60..0x65, which the original's launcher writes after INTRO, and the same
six bytes in a saved game: the comparison takes the original's six bytes, recorded in meta.json, where the rewrite has 0);
CA-1 is then counted in every block after a raid, not only the raid's final block. Nothing that is not
in a register may be masked. Data-damaging and visual fixes switched off for the oracle
(`OriginalBehaviour.Faithful`) are better compared in that mode once the C# side can run it.

## 4. How a difference is reported

`CovertAction.Session.Tests` plays every recorded session and writes a comparison folder beside the
recording. `report.txt` holds the counts, what was compared and the verdict. It names the **first** difference
only, because after it everything else can follow from it:

```
FIRST DIFFERENCE: checkpoint 8 (poll without a key, after 8 entries): 114 pixels differ (the first at
x 200, y 99: original 5, rewrite 0), 0 colours differ; picture: ...\checkpoint-0008.bmp
```

* the checkpoint, the entry it took and how many entries came before (reproduce it with a script cut there);
* for pictures: the differing pixel count, the first pixel and its two values, the differing colours. A
  picture `checkpoint-NNNN.bmp` shows the original, the rewrite, and the rewrite in grey with the differing
  pixels in red. The rewrite's frame is kept as `checkpoint-NNNN.rewrite.idx` (the original's is in the
  recording), and `tools/session/picture.py frame|diff` turns frames into PNGs;
* for the hand-off block: the differing fields by name;
* for sounds: both lists of requests.

The test fails when any session differs, and lists the first difference of each. A recording whose committed
spec changed since it was made is reported as stale. A recording that did not finish (anything other than
`exit` or `script exhausted`) is reported as unproven.

`AChangedInputIsReportedAtTheFirstCheckpointItChanges` checks that the comparison can fail: the rewrite
started one second later shuffles another cipher, and checkpoint 0 is reported.

## 5. Storage

Like Darklands' recordings (`tools/oracle/recording.py`, README "Original side and compare side"), the
original side is recorded once and later runs replay only the C#:

```
build/session/<PART>/<name>/
  original-<UTC stamp>/      tools/session/original_side.py: one per run, never overwritten
    meta.json                spec, stop reason, counts, SHA-256 of the program, the start image and the
                             original-side sources, calls after the last checkpoint
    inputs.json              key words (null = no key) and time of day: what both sides are fed
    handoff-in.bin, handoff-out.bin
    disk/<NAME>              files on the player's disk besides the game's (spec `disk`)
    checkpoints.jsonl        n, entry, service, picture, handoff, calls, frames
    frames/<sha256>.idx      each distinct picture once: 64000 indices + 768 DAC bytes (Darklands' .idx layout)
  compare-<UTC stamp>/       the test: report.txt, the rewrite's checkpoints.jsonl and handoff-out.bin,
                             checkpoint-NNNN.bmp / .rewrite.idx at a difference
```

* `build/` is never committed (game-derived). Only the specs and the tools are.
* Evidence is never deleted. A rerun makes a new folder (stamped, with `-2`, `-3` on a clash), and the test uses
  the latest `original-*` of each session.
* Roots: `COVERT_SESSION_DIR`, else `build/session` beside `COVERT_ORACLE_DIR`, else the main checkout's
  `build/session`. Game folder: `COVERT_GAME_DIR`.
* Staleness: a recording stores the hashes of everything that made it. The test checks the spec. A change to
  the original side's sources (`ORIGINAL_SIDE_SOURCES`) means: record again, keep the old folder. As in
  Darklands, editing the compare side never makes a recording stale. Before a release, every session is
  recorded again live.

```
python -B tools/session/original_side.py tools/session/sessions/code/*.json tools/session/sessions/chase/*.json tools/session/sessions/final/*.json tools/session/sessions/game/*.json tools/session/sessions/tac/*.json tools/session/sessions/bug/*.json
cd csharp && dotnet test tests/CovertAction.Session.Tests
```

A session that takes its block or a file from another (`handoff_from`, `disk`) is recorded after it: record the
chains of item 4b last, in their order (stasi-interrogation, mastermind-debrief, debriefing, load-saved-game,
loaded-game, campaign-decode, crypto-return, campaign-tail, chase-return), then those of item 5 (the practices
practice-breakin-1..3, the TAC sessions, raid-photo-return, raid-arrest-return, practice-raid-return), then those of
item 5b in their order (stasi-capture, capture-escape, escape-raid, capture-exchange; regional-breakin,
regional-capture, regional-ambush, street-ambush, ambush-return; item 11: street-ambush-won, ambush-won-return after
regional-ambush), then those of item 6 (the practices
practice-electronics-0, -1, -3, the BUG sessions but nervous-tap, practice-bug-return, tap-return, tap-alarm-return,
nervous-tap), and item 13's chain after FINAL/level3-new-case (level3-arrest-breakin, level3-arrest, jailbreak-warning,
jailbreak, jailbreak-return; item 15 after jailbreak-warning: jailbreak-foiled, jailbreak-foiled-return). The whole sessions (item 7) are recorded by their own tool, new-case-tap-save before loaded-game-raid (its
disk holds the former's save):

```
python -B tools/session/campaign_side.py tools/session/sessions/covert/new-case-tap-save.json tools/session/sessions/covert/loaded-game-raid.json tools/session/sessions/covert/practice-return-speaker.json tools/session/sessions/covert/joystick-quit.json tools/session/sessions/covert/new-case-chase.json
python -B tools/session/campaign_side.py tools/session/sessions/covert/practice-return-adlib.json tools/session/sessions/covert/practice-return-roland.json tools/session/sessions/covert/practice-return-tandy.json tools/session/sessions/covert/new-case-chase-adlib.json tools/session/sessions/covert/new-case-chase-roland.json tools/session/sessions/covert/new-case-chase-tandy.json tools/session/sessions/covert/custom-quits.json
python -B tools/session/campaign_side.py tools/session/sessions/covert/joystick-menu.json
```

Where the checkout has no `build/` or `game/`, set `COVERT_DATA_ROOT` to the folder that holds them: without it `oracle_common` finds no
`build/code-analysis` there, and the cuts (`time`, `find_file_on_disk`) and the sound slot are silently not placed. The
Roland sessions' MIDI is compared when `COVERT_MT32_ROMS` names the player's MT-32 ROMs (else D-20 is reported).

`COVERT_SESSION_ONLY=<text>` makes the test
play only the sessions whose names contain the text.

## 6. Order of the parts

| # | Part | Start image | What it adds to the tool | Status / estimate |
|---|---|---|---|---|
| 1 | CODE practice | CODE/decode-v1 | the machine, checkpoints, comparison | **done**: 6 sessions (levels 0-3, skill, hints, guesses, pause, sound toggle, quit box, solved), all agree |
| 2 | CHASE practice | CHASE/chase-v1 | frame-polled input; registered-difference masks | **done**: 2 sessions (413 and 340 checkpoints), agree with D-17 masked; next: the abort route (chaseabort-v1), a lost chase, Alt-Q |
| 3 | FINAL menus | FINAL/campaign-v1 | written files, PC clock ticks, the campaign tables in the hand-off block | **done**: 10 sessions, all agree (below) |
| 4 | GAME walk | GAME/campaign-v1, stasi-v1 | **written files** (save, at the checkpoint after each write), long scripts, the PAN animations, conditional registered differences | **done**: 8 sessions, all agree (D-11 masked where the original's animation did not load; below) |
| 4b | GAME's gaps | as 4, and CODE/CHASE/FINAL for the chains | **blocks from elsewhere** (an earlier session's hand-off, a played capture's), **files on the disk** | **done**: 9 sessions, all agree (D-22 in one final block; below) |
| 5 | TAC raid | TAC/tacphoto-v1, stasi-v1 | long frame-polled sessions (as CHASE); keypad keys by Num Lock; a block entry (CA-1) | **done**: 9 sessions, and 6 more for their blocks (practice levels 1-3) and returns, all agree (CA-1 in the final blocks; below) |
| 5b | TAC's gaps | as 5, and GAME for the chains | the start screen of a part that looks at the keyboard before it draws; nothing after the end of the script | **done**: 9 sessions, all agree (CA-1 in three final blocks; below) |
| 6 | BUG | BUG/bug-v1 | as CODE (nothing new: the tool runs BUG unchanged) | **done**: 9 sessions, and 6 more for their blocks (practice levels 0, 1, 3) and returns, all agree (below) |
| 7 | full campaign | none: the DOS prompt | **chaining**: the original side runs COVERT.EXE itself, with EXEC of each part as `trace_final.py` does. Checkpoints carry the part's name. The C# side runs `Game.Main`. Text pages; timer ticks for the sound drivers | **done**: 5 whole sessions (8553 checkpoints), all agree (D-600, D-602, D-11, D-17, CA-1; below) |
| 8 | sound devices | none: the DOS prompt | the OPL2, MPU-401, SN76496 and speaker ports; tick and call ends marked; the C#'s timer given the original's ticks | **done**: 7 whole sessions (21104 checkpoints) with AdLib, Roland, Tandy and Custom, all agree, and the 5 of item 7 recorded again (below) |

### Item 3: FINAL, as recorded (14 November 2026)

FINAL is started from its first instruction with the launcher's hand-off block (`next_module` 0x0b) and runs to
its `exit` (code 1 after a hand-off, 0 after Alt-Q), or until the script is used up.

| Session | What it covers | Checkpoints | End |
|---|---|---|---|
| title-main | the title animation, Space, the main menu | 52 | script exhausted |
| title-credits | the title and the credits played to their end, the main menu, Esc | 422 | script exhausted |
| practice-breakin / -driving / -decode / -electronics | Practice a skill at levels 0, 3, 1, 2, each skill: the hand-off codes 0x0d, 0x0e, 0x0f, 0x10 | 28, 20, 23, 18 | exit 1 |
| new-case | Maxine, a typed code name, training, the Chief's animations, the four briefing pages, the portrait pick: the hand-off to the campaign (code 0, with its tables) | 676 | exit 1 |
| plain-new-case | Alt-A (menus without the animations), Maximillian, Regional Conflict, Alt-Q answered No in the training menu, the briefing without animation, another portrait | 119 | exit 1 |
| options-fame | Alt-A/S/J, the Hall of Fame (the player's FAME.DTA opened to read and write: a written file), the main menu over it, an empty code name, Alt-Q Yes | 65 | exit 0 |
| menus-back | Load a Saved Game (the drive prompt, the four empty slots), Esc at the practice difficulty (level -1 is handed over: FINAL 0100:0c40, already noted in the C#) | 34 | exit 1 |

Times of day from 4:00 to 19:00 (the seed). Every checkpoint agrees (picture, colours, hand-off block, sound
requests), and so do the end, the final hand-off block (now with the campaign tables, `SharedBlockCodec.LoadCampaign`
/ `StoreCampaign`) and the written files. No registered difference was met.

What the work found:

* **C# bug, fixed** (`GameFolderFiles.Open`): a file of the player's folder opened to write without truncation
  (the Hall of Fame's FAME.DTA, FINAL 0100:4cd0 `read_fame_file`) was opened as a new empty file in the write
  folder, so a player's Hall of Fame entries were never read and were lost at the next write. Under DOS it is the
  player's file: its bytes are now copied into the write folder first (test:
  `GameFolderFilesTests.APlayersFileOpenedToWriteKeepsItsBytesAndThePlayersCopyIsNotChanged`). The player's own
  FAME.DTA is empty, so the sessions show the file only as written on both sides.
* **Test model, fixed**: the C# side compared only the hand-off fields, not the campaign tables behind them
  (SHARED+0x400 on); a practice and a new case leave tables there, which then differed. The tables are now loaded
  and stored around the run.
* Original behaviour seen and matched (not differences): Esc at the practice difficulty hands the practice over at
  level -1; the main menu is drawn over the Hall of Fame and the load screen without clearing them.
* Not yet covered: the CGA/EGA/Tandy display letters (one MCGA start image), a sound driver (the start image has
  none: only Alt-S's requests are seen), loading an actual saved game (no CV?.SVE in the player's folder), the
  debriefing after a case (needs the campaign's hand-off: item 7).

### Item 4: GAME, as recorded (14 November 2026)

GAME is started from its first instruction with the block of the start image: campaign-v1 (a new case, the legacy
memory layout) or stasi-v1 (a saved Stasi case loaded, at noon, the compact layout). It runs to its `exit` (the
hand-off to the next part, or the end of the game) or until the script is used up.

| Session | Start, time | What it covers | Checkpoints | End |
|---|---|---|---|---|
| hq-data | campaign, 20:00 | the case's clues, the Chief's office (BRIEFING, HQ, OFFICEF animations) and her pages, the Data Section (clue board, a clue, suspect files, documents), the city screen, the CIA building again, the Mossad cell's building: Place wiretap | 339 | exit, code 4 (electronics) |
| intel-scans | campaign, 08:00 | the Intelligence Section: the local scan's run of results (a clue, a coded message, a photograph, an identification), the international scan, the clue records; the MI6 office: Break into building | 365 | exit, code 1 (raid) |
| travel-save | campaign, 14:00 | the airport and the travel map, the flight to Cairo, the hotel: Save Game (drive prompt, slot list, CV0.SVE), the lounge, the map again, Sleep through case, Alt-Q Yes, the farewell picture | 459 | exit, code -1 (end of the game), CV0.SVE |
| stakeout-follow | campaign, 04:00 | a building's animation (BLDING03), Watch the building three times (BINOS3), Follow car | 218 | exit, code 3 (chase) |
| stasi-city | stasi, noon | Stay here twice, the city's Check Data (clue board, a clue, documents), the hotel, the map, the MI6 office: Break into building | 243 | exit, code 1 (raid) |
| stasi-travel | stasi | the flight to Rome, a building, four stakeouts and Wait | 264 | script exhausted |
| stasi-save | stasi | the hotel: Save Game (CV0.SVE), the CIA building in a running case (arrival, the Chief's office, her pages), the Crypto Branch (CRYPTO.PAN): no coded message yet, the chronology | 211 | script exhausted, CV0.SVE |
| stasi-crypto | stasi | the local scan's long run of results (dossiers, photographs, coded messages, the bulletin teletype), Active Wire Taps, the Crypto Branch: a coded message chosen | 523 | exit, code 2 (cryptography) |

2622 checkpoints; times of day from 04:00 to 20:00 over three days (the seed). Every checkpoint agrees (picture,
colours, hand-off block, sound requests, written files where they changed), and so do the ends, the final hand-off
blocks (with the campaign tables) and the saved games, byte for byte.

What the work found:

* **C# bug, fixed** (`LibraryMenus.AskToQuit`): Alt-Q confirmed in one of the campaign's boxes called the cryptography
  part's `GoodBye` and ended the program. GAME's copy of the box (00f7:c6ae `popup_menu`) calls its own
  `save_and_hand_off` instead: the campaign goes into the hand-off block (game mode -1 too) and the farewell picture
  AD.PIC waits for a key. `LibraryMenus.LeaveTheGame` is now the part's way of leaving, set by `CampaignPart`
  (test: `CampaignTests.AltQConfirmedOnTheCityScreenSavesTheCampaignAndShowsTheFarewellPicture`). Found by
  travel-save: the original had seven checkpoints more.
* **Registered difference met: D-11** (memory for images never runs out in the rewrite). Under the legacy start
  image the original's arena runs short once the city screen's archive is held: every later animation fails to load (`load_pan_file` answers 0, `read_pan_sprites` cannot build an
  image). Under the compact image the third stakeout fails, because the box gives an animation's memory back only if
  the animation is still running when the box closes. The original then plays a half-loaded animation (geometry
  from the new file, images and program partly the old ones) anywhere on the screen; the rewrite plays the
  animation. The original side now records `pan_load` / `pan_release`, and `KnownDifference` D-11 leaves the screen
  out only from a failed load to the next release or successful load: 78 checkpoints of hq-data, 75 of
  stakeout-follow, 48 of stasi-travel. Hand-off block, colours, sounds and written files are still compared there.
  The evidence is in DIVERGENCES.md D-11.
* No other difference: the C# campaign walk agreed at every other checkpoint from the first run.
* Not yet covered then (item 4b covers them, but the last two): interrogations, the coded-message decoding's return
  (DecodeMessage back from CODE), a case's end and the debriefing hand-off (code 9), Load Game of an actual saved
  game, the CGA/EGA/Tandy letters, a sound driver.

### Item 4b: GAME's gaps, as recorded (14 November 2026)

Every gap is reached on a path the player takes, with both sides recorded and compared at every checkpoint. No
block is made up: each session starts from the block the original itself left, either at the end of another recorded
session (`handoff_from` a recording) or at the end of a played raid captured earlier by `tools/trace_game.py` (the
whole chain COVERT -> INTRO -> FINAL (load) -> GAME -> TAC -> GAME: the block in the memory
image at GAME's first key wait after the raid; GAME writes only `next_module` into the block before it, set back to
the 0 TAC left, which the capture records at TAC's exit, and the first 112 bytes agree with that record).

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| GAME/stasi-interrogation | the played raid arresting Jane Brown (stasi-arrest-return-127) | the interrogation of an Organizer: accusation, evidence, two messages decoded on the spot, jail, the Chief, the CIA building | 188 | script exhausted |
| GAME/mastermind-debrief | the played raid arresting Yazid Nidal (mastermind-arrest-15) | the MasterMind's interrogation, his confession and the participants' dossiers, jail, the Chief, the teletypes, the end of the case: hand-off to the debriefing (code 9) | 163 | exit |
| FINAL/debriefing | GAME/mastermind-debrief | the debriefing: chronology, efficiency report, the President, the Hall of Fame (FAME.DTA written), the career, the next case's briefing (code 1) | 500 | exit |
| FINAL/load-saved-game | start image; disk: CV0.SVE of GAME/travel-save | Load a Saved Game: the slot list names the save, slot 1 loaded, hand-off to GAME | 17 | exit |
| GAME/loaded-game | FINAL/load-saved-game; same disk | the loaded case in Cairo, the hotel's Load Game (GAME's loader), Save Game over slot 1 (CV0.SVE written and compared) | 172 | script exhausted |
| CODE/campaign-decode | GAME/stasi-crypto | the campaign's coded message decoded (MESSAGE DECODED), hand-back (value 1) | 53 | exit |
| GAME/crypto-return | CODE/campaign-decode | back from CODE: the decoded message, the CIA building, the clue board | 149 | script exhausted |
| CHASE/campaign-tail | GAME/stakeout-follow | the campaign's car chase: the quarry followed home by the tail cars | 6013 | exit |
| GAME/chase-return | CHASE/campaign-tail | back from CHASE: the building he went into, Place wiretap (code 4) | 8 | exit |

7263 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests, written files), and so do
the ends, the final blocks and the written files (CV0.SVE, FAME.DTA), D-17 masked in the chase's dashboard row and D-22
counted in the debriefing's final block. All 35 sessions were recorded again with the extended tool (the old
recordings are kept): the 26 earlier ones agree as before.

What the work found:

* **C# bug, fixed** (`Headquarters.ShowMemberCard`): the efficiency report drew the cards of the cast without their
  panel. FINAL 0100:2e06 `show_efficiency_report` calls `draw_report_card(f, g)` before each member's name, as before
  every other card; the rewrite went straight to the name, so the first page showed the frame's yellow where the
  original has the panel's grey edges (934 pixels at checkpoint 189 of FINAL/debriefing). Test:
  `DebriefTests.TheFirstCardOfTheEfficiencyReportIsDrawnOnItsPanel`.
* **New register entry, D-22** (DIVERGENCES.md): the rewrite keeps a name of the campaign tables as its text; the
  bytes of the original's buffer behind the end mark are not kept. After the debriefing, the next case's tables (which
  the comparison stores over the block the menus part started with) differ from the original's in 402 bytes, every
  one behind the end mark of a name (13 in roles, 271 in events, 58 in cities, 60 in organisations), every decoded
  value agreeing. `SessionComparison` counts such bytes under D-22 in the final block only, and only when the bytes
  before the tables and every decoded record agree.
* **The crash of the made-up hand-off: not reachable in play; behaviour unchanged.** Reproduced with a raid's answer
  written by hand into GAME's block to TAC (`result` 2, "target arrested"): `PlotEngine.InterrogateSuspect` throws at
  `Ps[person]` with person -1, where the original writes into the bytes before the persons table (the rule "Out of
  the typed model" of DIVERGENCES.md). The person comes from `ResolveInfiltration`'s `find_or_create_person(org, city, 0)`
  (GAME 00f7:4899), which answers -1 only when nobody is at the building or the first person there has no clue known.
  TAC sets the arrest bit at one place only (tac_00e8_2d10.c line 85, `AskToArrest`), the prompt of the person to
  arrest; that piece is placed only when TAC found a person at the building (`FindTheTarget`, tac_init 00e8:0018: the
  last person of the organisation and city, not out of the game; `BuildingRules.FurnishRoom` leaves it out for -1 in a
  break-in), and the prompt marks his photo known (`Clues |= 1`). Every creator of a person (FINAL 0100:5ff1, GAME
  00f7:4899 and 4d4b, the library's 00e8:2175) first looks for one at the same organisation and city, so a building has
  one person: GAME then finds the arrested one, with a clue known. A real TAC answer cannot lead to the crash; the
  played arrests (stasi-interrogation, mastermind-debrief) agree. The same made-up block with a caught quarry (`value`
  2) runs without a crash, since CHASE stores `not_hurt` 1 and the made-up one does not.
* U-202 (the exit code at the end of a case) is half recorded: the original exits with 150 there (a stack word).
  Settled since (15 November 2026) by the launcher's code: COVERT 0000:0010 drops spawnl's result, so no one reads it.
* GAME's load screen lists only the saves on the disk: a session that loads in GAME must have the save on its disk
  too (`disk` on loaded-game), as the player's disk still holds it after FINAL loaded it.

Not yet covered: the raid (TAC) and the electronics part (BUG) themselves and their returns as sessions (items 5 and
6: the interrogations above take TAC's answer from the captured played routes), a quarry caught in a chase (it needs
a driven head-on approach), the CGA/EGA/Tandy letters, a sound driver.

### Item 5: TAC, as recorded (14 November 2026)

TAC is started from its first instruction and runs to its `exit` (the hand-off back, or Alt-Q) or until the script is
used up. Every raid starts from a block the game really produces: a practice handed over by a recorded FINAL session
(levels 0 to 3: practice-breakin and the new practice-breakin-1..3), a campaign raid handed over by a recorded GAME
session (GAME/intel-scans, GAME/stasi-city: Break into building, code 1), or the GAME hand-off in a start image's own
captured route (tacphoto-v1, stasi-v1). Two raids replay the captured routes' raid actions with the routes' input
timing (each key after 255 or 127 empty polls, the time of day the capture's DOS clock gives, 1990-11-05 12:00): their
blocks then come out as in the captures (stasi-arrest's final block is the played capture's
stasi-arrest-return-127 block, byte for byte, apart from next_module, which GAME writes, and the driver segments).

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| practice-lounge | FINAL/practice-breakin (level 0) | the kit as offered, the Computer Room entered from the top, walking with the keypad, lunges (arrow keys), shots, F10 and two grenades (F5, F6), the gas without a mask: the agent lies knocked out, the guards come | 897 | script exhausted |
| practice-photo | FINAL/practice-breakin-1 | the kit chosen (Uzi, camera, bugs), the Lounge, keypad walking, F2 ("Nothing to photograph."), F4, the boxes | 854 | script exhausted |
| practice-wounded | FINAL/practice-breakin-2 | Alt-P (the pause box), Alt-S, the speeds (f, d, s), walking; the guards shoot: "You slump to the ground, overcome by your wounds.", the hand-off to the menus part (code 0x11) | 731 | exit |
| practice-traps | FINAL/practice-breakin-3 | the Cipher Room, lunges, ten shots, a trap laid (F9) and fired (F8), the alert, overcome by wounds, hand-off (0x11) | 370 | exit |
| photo-raid | TAC/tacphoto-v1's (the Stasi hideout) | the captured route: the kit (gas mask, vest, camera, bugs, safecracking kit), Door #1, a lunge, F1 at the files (searched), F2: the files photographed, the clue and Agent S's coded message, walking out (2): "You quickly slip out of the building.", hand-off to GAME (code 0) | 5638 | exit |
| mi6-doors | GAME/intel-scans (the MI6 office) | the first door, walking against the walls, F4 at a door (it opens), long waits while a guard passes | 2257 | script exhausted |
| stasi-quit | GAME/stasi-city (compact image) | walking, Alt-Q answered No, then Yes: the game ends inside the raid | 170 | exit |
| stasi-wait | GAME/stasi-city, another time | walking, ten shots, 2000 polls of patrols; a guard comes in | 2811 | script exhausted |
| stasi-arrest | TAC/stasi-v1's (the Stasi building) | the captured route: Door #2, lunges through rooms and doors, F1 at the pieces, the alert and the guards' fire, shots, Jane Brown arrested (Enter), the way out: hand-off to GAME (code 0, result 2) | 10114 | exit |
| GAME/raid-photo-return | TAC/photo-raid | the hideout again, "extra guards have been posted", Place wiretap (code 4) | 8 | exit |
| GAME/raid-arrest-return | TAC/stasi-arrest | Jane Brown's interrogation from the session's own block (as stasi-interrogation from the capture's) | 188 | script exhausted |
| FINAL/practice-raid-return | TAC/practice-wounded | the menus part back at its main menu after the practice, a new character's code name box | 45 | script exhausted |

23842 checkpoints in the raids, 290 more in the practices' hand-offs and the returns; times of day from 02:06 to 20:43
over four days (the seed). Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the
ends and the final blocks, with CA-1 counted in the final block of every raid that hands back (four: photo-raid,
stasi-arrest, practice-wounded, practice-traps; Alt-Q and the scripts that ran out leave no answer in the block). All 50 sessions were
recorded again with the extended tool (the old recordings are kept): the 35 earlier ones agree as before.

What the work found:

* **C# bug, fixed** (`BuildingRaid.Fly`): a grenade flying over bare floor stopped the rewrite with an index out of
  range. Bare floor is the cell value FFh, whose open bit is set, so the flight's test for an open door (TAC
  00e8:26b0 line 147, `ob[cell & 0x3f].bits & TF_DOOR`) reads piece 63, one past the table; the original reads the
  word behind it (Move8X[0], no door bit). The rewrite indexed the table; it now goes through `TileBits`, which already
  models that word. Found by stasi-arrest (the guards' grenades); test:
  `RaidGrenadeTests.AGrenadeFliesOnOverBareFloor`.
* **Registered difference met: CA-1** (docs/original-bugs.md, fixed by default; until now only read, never seen in a
  run). The original's raid counts its awake guards in city 0's activity counter, which goes back to the campaign: in
  every raid that hands back, that one word of the final block differs (original 3 against the campaign's 2 in
  photo-raid, 1 against 3 in stasi-arrest, 8 against 0 in the practices), every other byte agreeing. The comparison
  counts it under CA-1 (`GuardCountInCityCounter`); the evidence is in the register entry.
* **Tool**: keypad keys by Num Lock, F2..F9 and the other cursor keys in the script, INT 10h AH=03 (asked at the raid's
  start with the keyboard's repeat-rate registers by mistake). `Game.RunPart` clears Num Lock as the launcher does
  before each part (it was cleared in `Game.Main` only, so a part run alone started with Num Lock on).
* Seen and not compared: Alt-Q in the raid ends the original with whatever its argument-less `exit()` finds (96 in
  stasi-quit); the rewrite throws `ProgramExitException(0)`. The exit code is not a compared output (as U-202).

Not yet covered: a disguise (running into a guard), a captured agent in a campaign raid (the captures above are
practices) and its return to GAME, the electronics bug planted in a raid (F3), the other raid kinds (the escape, the
jailbreak, the ambush: codes 0x13..0x15 need their GAME situations), the joystick (absent), the CGA/EGA/Tandy letters,
a sound driver.

### Item 5b: TAC's gaps, as recorded (14 November 2026)

Each gap is reached on a path the player takes, from a block the game really produced (a recorded session's final
block), with both sides recorded and compared at every checkpoint. Two chains: one in the level-0 Stasi case (GAME/
stasi-city's raid), one in a new case at level 2 (FINAL/plain-new-case, Regional Conflict), since an ambush needs a
level above 0 (GAME 00f7:1608 `city_screen`, `CityScreens.ChoosePlace`).

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| TAC/stasi-capture | GAME/stasi-city | the kit chosen (bugs); **F3**: "Plant bugged.", F3 again in the bugged room; two guards come in; the agent walks round the chair into one: knocked out, **the agent disguised** ("Disguise" in green, the guard's grenades taken, one wound), seen through by the other guard; **captured**: "overcome by your wounds" (person -1) | 2264 | exit |
| GAME/capture-escape | TAC/stasi-capture | **GAME's handling of the capture**: the capture scene, the offer declined, the spells in jail, the escape scene: code 7 | 20 | exit |
| TAC/escape-raid | GAME/capture-escape | **the escape raid**: its own kit, the agent in the office he was held in, the alarm, walking, F4, shots, 600 polls | 752 | script exhausted |
| GAME/capture-exchange | TAC/stasi-capture | the offer agreed: exchanged for two double agents (no suspect arrested), back in the city | 72 | script exhausted |
| GAME/regional-breakin | FINAL/plain-new-case | the level-2 case's clues, the CIA building, the city, the Rev. Guards hideout: Break into building | 154 | exit |
| TAC/regional-capture | GAME/regional-breakin | the level-2 raid: shots, the guards shoot: captured | 1094 | exit |
| GAME/regional-ambush | TAC/regional-capture | the exchange agreed (the hideout lost on this level); the walk to the Mossad cell cut short: **an ambush** ("As you walk to your car..."), code 0x0a | 73 | exit |
| TAC/street-ambush | GAME/regional-ambush | **the ambush raid** in the street: the attackers' fire, overcome by wounds (the ambush lost) | 184 | exit |
| GAME/ambush-return | TAC/street-ambush | the lost ambush on level 2: the capture, the offer declined, a satellite intercept in jail, the escape: code 7 | 46 | exit |

4659 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the ends and the
final blocks, with CA-1 counted in the three raids that hand back (stasi-capture: original 2 against 3; regional-capture:
7 against 2; street-ambush, the one-room mode where the bug's "all enemies eliminated" test lives: 5 against 7). All 59
sessions agree after the compare-side changes below (the original side is unchanged, so no recording became stale).

What the work found:

* **Test model, fixed** (`RewriteSide`, `SessionRecording.StartScreen`): the escape raid looks at the keyboard before it
  draws anything, so the original's checkpoint 0 shows what the program before it left on the screen (no part sets the
  video mode again; the launcher's drivers keep the picture: in the start image, GAME's last picture of the captured
  route). The rewrite's `Game` keeps its one `McgaGraphicsDriver` across parts as well, so the product behaves the same;
  only the comparison started the C# screen black. It now starts with the start image's screen (A000:0000, the image
  checked by its SHA-256 from meta.json). Every earlier session agrees as before (their parts draw before the first look).
* **Test model, fixed**: after the script is used up the original side stops at that poll, while the headless host lets
  the rewrite run on until it sees the window closed. The escape raid asks for its frame's music meanwhile; sounds asked
  for after the end of the script are no longer counted as the rewrite's (they are not the session's).
* No C# bug and no new register entry: the raid's disguise, bug, capture, escape and ambush, and GAME's capture,
  exchange, jail, escape and ambush agreed at every checkpoint from the first run.
* **Not reached: the jailbreak (code 8).** GAME leaves for it only when `Sprung` is set (`CityScreens.SettleTheMission`,
  GAME 00f7:1608) and `Sprung` is set only by `CastRules.TrySelectPerson` (GAME 00f7:951c) when the level is above 1 and
  a suspect is under arrest (not singled out, the MasterMind free) when the plot moves on. The only recorded arrests are
  in level-0 cases (stasi-arrest, mastermind-arrest-15), where the condition `Diff > 1` never holds; the level-2 case
  would first need a raid that finds and arrests a suspect, an interrogation with evidence, and then plot steps until
  one picks him. That is a long played route without a known path, left for later.
* Not reached either: an escape that ends (the Stasi office's only door leads further into the building; the level-2
  escape held for 3000 polls without the guards finding the agent), and an ambush won (both reached since: items 8 and
  11). The ambush lost, the capture and
  the escape's start cover the hand-off paths (codes 7 and 0x0a, person -1).

### Item 6: BUG, as recorded (14 November 2026)

BUG is started from its first instruction and runs to its `exit` (the hand-off back, or Alt-Q). Every board starts from
a block the game really produces: a practice handed over by a recorded FINAL session (practice-electronics at level 2,
and the new practice-electronics-0, -1, -3), a campaign wiretap handed over by a recorded GAME session (Place wiretap,
code 4: GAME/hq-data, GAME/raid-photo-return, GAME/chase-return, and the new GAME/tap-alarm-return). The swaps were
planned from the board the original built (an exploration copy of the original side read the chips, wiring and
terminals from BUG's data at the first checkpoint and searched for swaps with the circuit rules); the
clue screens a campaign tap opens were stepped through from contact sheets.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| practice-look | FINAL/practice-electronics (level 2) | the board built, the current run along it; a swap that powers an alarm: "Alarm!!!", hand-off (code 0x11, no phone) | 12 | exit |
| practice-solved | FINAL/practice-electronics-0 | five planned swaps: every phone cut off, "Phone taps completed. No Alarms", hand-off (0x11, 30 per cent) | 45 | exit |
| practice-timeout | FINAL/practice-electronics-1 | the cursor walked round the board (all eight directions), the ten minutes run down: "Time expired.", hand-off (0x11) | 4852 | exit |
| practice-giveup | FINAL/practice-electronics-3 | hidden and fixed chips: Alt-P (PAUSED), Alt-S twice, a hidden chip taken (shown for ten ticks), a fixed chip tried, Alt-Q answered No, Esc: given up (0x11, one phone) | 57 | exit |
| practice-quit | FINAL/practice-electronics, another time | Alt-Q, the quit box's second line: the game is left (next_module -1) | 19 | exit |
| campaign-tap | GAME/hq-data (the Mossad cell) | one minute on the clock; three swaps; the intercepted message (PFO); "Phone taps completed. No Alarms", hand-off to GAME (code 0) | 141 | exit |
| photo-tap-alarm | GAME/raid-photo-return (the Stasi hideout) | the cursor walked, a swap that powers an alarm, hand-off to GAME (one alarm) | 48 | exit |
| chase-tap-timeout | GAME/chase-return | one swap cuts two phones: the coded message and the dossiers it updates (**`show_suspect_dossier`, `reveal_person_facts`**: Agent K, S, L), then "Time expired. 2 phones tapped. No Alarms", hand-off (10 per cent) | 4959 | exit |
| nervous-tap | GAME/tap-alarm-return | the guards very nervous: two minutes; Alt-S, the time runs out, hand-off | 1016 | exit |
| FINAL/practice-electronics-0, -1, -3 | start image | Practice, Electronics at levels 0, 1, 3 (code 0x10) | 16, 17, 19 | exit |
| FINAL/practice-bug-return | BUG/practice-solved | the menus part back at its main menu, Practice, Esc | 34 | script exhausted |
| GAME/tap-return | BUG/campaign-tap | the building after the tap, Place wiretap again: "The guards are unsuspecting. You will have 9 minutes." (code 4) | 8 | exit |
| GAME/tap-alarm-return | BUG/photo-tap-alarm | the hideout "swarming with guards", Place wiretap: "The guards are very nervous. You will have 2 minutes." (code 4) | 8 | exit |

11149 checkpoints in the electronics part, 102 more in the practices' hand-offs and the returns; times of day from 00:00
to 23:00 over five days (the seed). Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do
the ends and the final blocks. No registered difference was met. The original side is unchanged, so no earlier recording
became stale; all 74 sessions agree.

What the work found:

* **C# gap, fixed** (D-300 retired): the workbench stopped with `NotSupportedException` at the first campaign tap whose
  intelligence opens a dossier (chase-tap-timeout: `show_suspect_dossier`, BUG 00e8:31b8). The four library screens a
  wiretap shows (`reveal_cast_clue` 282e, `show_suspect_dossier` 31b8, `append_msg_log_entry` 37fd, `reveal_person_facts`
  22e0) had been ported to the library (`LibraryDossiers`, `CampaignLibrary`) for TAC since D-300 was written; BUG now
  calls them on its own tables (`ElectronicsState : ICampaignTables`, `ElectronicsState.Library.cs`) and texts
  (`BugDgroup.Dossier.cs`: TAC's texts lie 0xb02 bytes lower in BUG, byte for byte, checked against both start images;
  the names, months, clue labels, message file and amounts BUG had mapped already are now the dossier's own). The same
  session agreed at every checkpoint afterwards. Tests: `BugDgroupTests.TheDossierTextsAreLoadedAndShareThePartsOwnNames`,
  `MessageMinusOneIsReadFromTheBytesInFrontOfTheMessageTable`.
* **New register entry, D-301** (DIVERGENCES-BUG.md): message -1 in BUG is the end of city 15's record and `mode`, read
  and not written back, as D-103, D-200 and D-500 in the other parts. Not met in a session.
* **Not covered**: the car tracer (code 0x0c, `ElectronicsMode.CarTracer`): no recorded GAME session hands it over (it
  follows a chase the agent wins); a campaign tap at level 1..3 (the recorded cases are level 0; a level-2 tap would follow
  GAME/regional-breakin's chain); the joystick, the CGA/EGA/Tandy letters, a sound driver.

### Item 7: whole sessions, as recorded (15 November 2026)

The original side runs the game as the player starts it: COVERT.EXE from the DOS prompt of an empty machine
(`tools/session/campaign_side.py`), with every program it starts by EXEC, until it ends. The C# side runs `Game.Main` on a
block it holds (`Game.Main(SharedState)`): the set-up screen, then the parts by their hand-off codes. Every checkpoint is
compared across the boundaries between the parts: the program that took the entry, the text page or the picture and
colours, the hand-off block, the sound requests and the written files; at the end the final block and the written files.

| Session | Set-up | Parts (checkpoints) | What it covers | End |
|---|---|---|---|---|
| new-case-tap-save | no sound, keyboard | INTRO 2, FINAL 676, GAME 339, BUG 141, GAME 142 | the new case of FINAL/new-case, GAME/hq-data's walk to the Mossad cell's wiretap, BUG/campaign-tap's board solved; back at the building: Leave, the city, the hotel, Save Game (CV0.SVE), Alt-Q, the farewell picture | exit (the launcher's own) |
| loaded-game-raid | no sound, keyboard; the disk holds new-case-tap-save's CV0.SVE | INTRO 2, FINAL 17, GAME 25, TAC 97, GAME 56 | Load a Saved Game; the loaded case in Washington; Break into building at the Stasi hideout; the raid: the kit, Door #1, walking out with keypad 2 (Num Lock as the launcher left it); back in GAME (keypad 2 is the menu's Down now), Alt-Q | exit |
| practice-return-speaker | IBM sound (the PC speaker), keyboard | INTRO 2, FINAL 16, BUG 45, FINAL 17, CODE 25 | Practice Electronics, the board solved, back to the menus (0x11), Practice Cryptography, a hint, Alt-Q in CODE: the game is left from a part. 683 timer ticks delivered | exit |
| joystick-quit | IBM sound, joystick and keyboard | INTRO 2, FINAL 20 | the joystick calibration (no joystick: keys answer it), the title, Alt-Q at the main menu | exit |
| new-case-chase | no sound, keyboard | INTRO 2, FINAL 676, GAME 218, CHASE 6013, GAME 20 | the new case, GAME/stakeout-follow's stakeouts and Follow car, CHASE/campaign-tail's 6013 frames, back at the building he went into, Alt-Q | exit |

8553 checkpoints. Every checkpoint agrees (the part, the text page or picture and colours, the hand-off block, the sound
requests, the written files), and so do the ends, the final blocks and the saved game, apart from registered
differences: D-600 (the graphics question, in each session), D-602 (the driver segments in the block from FINAL on and in
CV0.SVE), D-11 (in GAME, where the chain's own arena runs short: 65 checkpoints of new-case-tap-save, 75 of
new-case-chase), D-17 (the chase's dashboard row, 6005 checkpoints) and CA-1 (city 0's activity counter in the block GAME
goes on with after the raid, 56 checkpoints and the final block of loaded-game-raid). The original side of items 1-6
(`original_side.py`, `session_store.py`) is unchanged, so no earlier recording became stale; the compare side changed,
and all 74 earlier sessions agree as before.

What the work found:

* **C# bug, fixed** (`HardwareSetup.HardwareSetupMenu`, D-600 corrected): the set-up screen's page differed in 87 cells
  at its first question (the sound card). The original prints the graphics question (the prompt "Select graphics mode:"
  in yellow on row 13, the five cards from row 15) before it waits for its key; the sound question's blank rows then print
  over the texts, but DOS's output keeps a place's colour, so the prompt's yellow stays on row 13 and on the graphics
  menu's cells beyond the sound menu's lines. The rewrite printed nothing for the question it does not ask (D-600 said
  "the page has no other trace of it"). It now prints the question as `hardware_setup_menu` does (INTRO 0000:0010, the
  statements up to the first get_key), without reading its key; `IntroState` holds the prompt and the five cards
  (`IntroDgroup`: DS:03f0, gfx_menu_text at DS:04d4). Test:
  `HardwareSetupTests.TheGraphicsQuestionLeavesItsColoursUnderTheSoundQuestion`.
* **Registered difference met, D-602, written out** (DIVERGENCES-INTRO.md): the driver segments the original's launcher
  writes into the block after INTRO (here 137Bh, 15B8h, 15DFh) are 0 in the rewrite, which loads no driver programs; a
  saved game holds them too (CV0.SVE at 0x50..0x55), and neither side reads them back. Also under D-602: the launcher's
  C library asks DOS for a waiting key before each EXEC (COVERT 0000:0EBB); the rewrite has no such look, so it takes no
  script entry (without that, the look took the set-up's first key).
* **Registered difference met, CA-1, beyond the raid's end**: in a whole session the original's guard count goes on in
  the campaign: every GAME checkpoint after the raid differs in that one word of the block (original 3, rewrite 2), every
  other byte agreeing.
* **Tool**: the PC speaker's driver needs INT 8 (BUG waits for its tune to end inside the driver: without ticks the run
  went round in the driver's wait until the budget ran out); the timer's rate is the driver's own (port 40h). The
  rewrite's speaker driver needs no ticks (its waits sleep on the host's clock); the requests agree.
* Seen and matched: the joystick answer makes FINAL calibrate the joystick (two corner screens, a key each, as no
  joystick reads FFh) on both sides; Num Lock: the raid's keypad 2 walks (Num Lock off, cleared by the launcher) and
  the same key is the menu's Down in GAME afterwards.
* Not compared: the launcher's text mode at the end (after the last key, D-602), the exit codes (COVERT's own exit code
  is 3 here; not a compared output, as U-202).

Not covered by whole sessions: the AdLib, Tandy, Roland and custom sound drivers (the machine has no OPL or MT-32
model: their ports stop the run), the CGA/EGA/Tandy graphics letters (D-600: one graphics driver), a debriefing reached in
play (it needs a solved case: item 4b's chain starts from captured raids), the jailbreak, the car tracer, a capture or an
ambush in a whole session (their parts are proven alone in items 4b, 5b and 6), and MCV.EXE (code 0x63, CA-2: no part
writes it).

### Item 8: the remaining gameplay gaps, as recorded (15 November 2026)

The gaps left by items 5b, 6 and 7, each from a block the game really produced: a recorded session's final block, a save a
session wrote, or a full-chain capture under build/game-oracle. Both sides recorded and compared at every checkpoint.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| GAME/regional-tap | FINAL/plain-new-case (level 2) | the Rev. Guards hideout: Place wiretap, the guards box: code 4 in a case above level 0 | 154 | exit |
| BUG/regional-tap | GAME/regional-tap | **a campaign wiretap at level 2**: hidden chips, four minutes, two swaps, Esc: given up | 44 | exit |
| GAME/regional-tracer | FINAL/plain-new-case (stasi-v1, compact) | Watch the building, Wait, **Trace car** (GAME 00f7:23c9 `watch_building_exit`, the TRACE scene BUGCAR.PAN): code 0x0c | 249 | exit |
| BUG/regional-tracer | GAME/regional-tracer | **the car tracer** (`ElectronicsMode.CarTracer`): five minutes, three swaps, the third powers an alarm | 74 | exit |
| GAME/tracer-return | BUG/regional-tracer | back in mode 0x0c: the alarm's suspicion, no building found (person below 0x19), the city, the map | 32 | script exhausted |
| TAC/escape-out | GAME/capture-escape | **the escape raid played out**: office (room 6) east through its door (F1), room 10, north into room 3, west through the outer door into the street: "You quickly slip out of the building." (code 0, person 0) | 295 | exit |
| GAME/escape-return | TAC/escape-out | back in mode 7 with the agent free: no surrender scene, the city, a satellite intercept, the map | 58 | script exhausted |
| GAME/mastermind-save | the capture wiretap-checkpoint-reload (FINAL's Load of the played save 25ac3aa0..., GAME at its first key wait) | the hotel, Save Game: CV0.SVE, byte for byte the played save | 90 | script exhausted |
| COVERT/mastermind-debrief | disk: GAME/mastermind-save's CV0.SVE | **a debriefing in one whole session**: INTRO 2, FINAL 17 (Load), GAME 129 (the PFO hideout, Break into building), TAC 2855 (the played route arrest-native-pfo15, each action after 15 empty polls as in the capture mastermind-arrest-15: Yazid Nidal arrested, the way out), GAME 203 (interrogation, confession, jail, the end of the case: code 9), FINAL 623 (chronology, efficiency report, Hall of Fame: FAME.DTA written, career, the next case), GAME 225 (the next case) | 4054 | script exhausted |

5050 checkpoints. Every checkpoint agrees (in the whole session also the program, the text pages and the written files),
apart from registered differences: D-600 and D-602 in the whole session, CA-1 in its 826 checkpoints after the raid. The
original side is unchanged, so no earlier recording became stale; all 88 sessions of items 1-8 agree (the whole session
custom-quits that the parallel sound-driver work recorded into the shared folder is not part of this branch: its C# side is
not here, and it is the one session the test reports as differing).

What the work found:

* **C# bug, fixed** (`TacDgroup`, `BugDgroup`): the dossier the raid's arrest prompt shows (TAC 00e8:9a3c
  `show_suspect_dossier`) wrote "RecruitedAmman." where the original writes "Recruited in Amman." (125 pixels at checkpoint
  1507 of COVERT/mastermind-debrief, Yazid Nidal's dossier). The " in " before the city of the recruiting lies between
  " by " and "." (TAC DS:2c39, BUG DS:2137, as FINAL DS:3cf3 and GAME DS:3235) and was not loaded in TAC and BUG, so it
  stayed empty; the bytes were read from both start images. Test:
  `BugDgroupTests.TheRecruitmentCitysInIsLoadedInBugAndTac`. The same session agreed at every checkpoint afterwards.
* **D-11 avoided, not widened**: under the legacy start image (campaign-v1) GAME/regional-tracer's TRACE animation does not
  load (`pan_load` BUGCAR.PAN 0). The original's `setup_pan_scene` (GAME 00f7:b462) then returns before
  `apply_pan_palette`, so one colour (12) differs while the rewrite plays the animation with its palette, and the half-loaded
  animation is still on the screen at the hand-off's last checkpoint, after its release (7792 pixels). Both are D-11, but the
  comparison's mask covers neither (pixels only, and only until the release). The session is recorded in the compact image
  (stasi-v1), as a player with the compact layout runs it; there BUGCAR.PAN loads and every checkpoint agrees. The first
  recording is kept (original-20261115-053634).
* No difference in the level-2 wiretap, the car tracer's board, the escape's end or the debriefing reached in play.
* **Not reached: the jailbreak raid (code 8)**, with evidence. It needs a suspect under arrest in a case above level 1
  (`CastRules.TrySelectPerson`, GAME 00f7:951c). An arrest needs a raid on a building where TAC finds a person (`Who`) and an
  interrogation with evidence (a known role, `Rknown`). In the level-2 case of FINAL/plain-new-case the only enemy building
  the player knows in Washington, the Rev. Guards hideout, holds nobody: its raid has `Who` -1 (TAC DS:bddc, read from the
  raid's own data). The case's persons in Washington belong to organisations 11 and 12, whose buildings the case has not
  revealed, and none of them has a role known. The route would be: investigate until such a building is found, raid it and
  arrest its person after reading the hidden information (F1 at tile 3Ch) for evidence, survive the level-2 guards, then pass
  time until a plot step concerns the prisoner (an event of his pending, more than five messages in the log for an event
  without partner, and the 1-in-4 roll at level 2 once someone was excluded). That is a long played investigation with no
  known path; left with this evidence.

Recording order (after items 1-7): GAME/regional-tap, BUG/regional-tap, GAME/regional-tracer, BUG/regional-tracer,
GAME/tracer-return, TAC/escape-out, GAME/escape-return, GAME/mastermind-save, then
`campaign_side.py tools/session/sessions/covert/mastermind-debrief.json`.

### Item 9: the sound devices in whole sessions, as recorded (15 November 2026)

The sound drivers chosen at the set-up screen play on their own devices on both sides, and what each driver does to its
device is compared at every checkpoint: the AdLib card's OPL2 register writes (ASOUND.CA), the MPU-401's MIDI bytes and
commands (RSOUND.CA), the Tandy's SN76496 bytes (TSOUND.CA), and for completeness the PC speaker (ISOUND.CA), with the
timer's rate and every timer tick.

Original side (`campaign_side.py`): port 388h/389h through tools/trace_adlib.py's OPL model (its timers' status follows its
own clock, so the driver's detection finds the card), a ready MPU-401 at 330h/331h (every command acknowledged, as
tools/rsound_vectors.py), port C0h, the speaker's 43h <- B6h, 42h and 61h, and the timer's latched count (43h <- 00h; it falls
3000 clocks a latch) for ASOUND's and RSOUND's delay calibrations. INT 1Ah AH=81h is answered as a Tandy BIOS (D-21). Each
write is a call among the checkpoint's calls (`["opl", register, value]`, `["midi", byte]`, `["mpu", command]`,
`["psg", byte]`, `["speaker", ...]`, `["timer_rate", divisor]`). A tick is delivered with a frame that returns through a stub
(INT F8h: `["tick"]` ... `["tick_end"]`), and a call of the sound slot returns through another (INT F9h: `["sound_return"]`),
so the stream knows where a tick and a driver call end. The handler starts with the carry clear (U-11, below). EXEC 4B03h of
a driver the folder lacks fails with DOS error 2; meta.json keeps the text page the run ended with (`text_at_end`) and says
`device_stream`.

C# side: `Game.SoundDeviceWritten` (new, for tests and tools) tells every write of the chosen driver, through watching
decorators of `IOplChip`, `IMpu401`, `ISoundChip` and `ISpeaker` (`WatchedDevice`) and the `ClockedSoundDriver`'s rate and
interrupts. The headless host's device (`TickedOutput`) never asks for frames; the comparison gives the driver
(`ClockedSoundDriver.RunInterrupts`, new) as many ticks as the original's had before each sound request, at each checkpoint
and at the end of the last part (`DeviceStream.Schedule`), so both drivers get the same interrupts between the same
commands. The stream is compared in order with the sound requests (`DeviceStream.Normal`). A tick that falls inside a driver
call on the original would be moved after the call's own writes and counted under D-19 (met at no checkpoint here); the
launcher's stop of the driver after the last part is D-602 (counted, not compared).

| Session | Set-up | Parts (checkpoints) | Device stream | End |
|---|---|---|---|---|
| practice-return-adlib | 4) AdLib, keyboard | as practice-return-speaker: INTRO 2, FINAL 16, BUG 45, FINAL 17, CODE 25 | 988 OPL writes, 180 ticks: the title theme (2), BUG's effects (28, 29, 46), CODE's (45, 46) | exit |
| practice-return-roland | 5) Roland | as above | 6081 MIDI bytes, 3 MPU commands, 180 ticks | exit |
| practice-return-tandy | 3) Tandy | as above | 471 SN76496 bytes, 180 ticks | exit |
| new-case-chase-adlib | 4) AdLib | as new-case-chase: INTRO 2, FINAL 676, GAME 218, CHASE 6013, GAME 20 | 1795 OPL writes, 811 ticks: the chase's music (39), both engines (26, 27 in gear 2), effect 23 | exit |
| new-case-chase-roland | 5) Roland | as above | 6242 MIDI bytes, 810 ticks | exit |
| new-case-chase-tandy | 3) Tandy | as above | 2520 SN76496 bytes, 810 ticks | exit |
| custom-quits | 6) Custom | INTRO 2 | none: the launcher's EXEC of XSOUND.CA fails and the run ends with "One Moment Please...Filename not found" | exit (D-18) |

21104 checkpoints; every one agrees (the part, the picture or text page, the block, the requests, the written files and the
device stream), and so do the streams after the last checkpoint, apart from registered differences: D-600, D-602 (the block's
driver segments; the driver's stop at the launcher's end: 259 OPL, 115 MIDI and 7 speaker calls), D-17, D-11, and in
custom-quits D-18 (the rewrite goes on to the menus: its 4 checkpoints after the original's end are not compared). The 5
sessions of item 7 were recorded again with this tool (their streams are compared too: practice-return-speaker and
joystick-quit's speaker writes and ticks) and agree as before. Without `COVERT_MT32_ROMS` the Roland sessions report D-20
(the rewrite plays the AdLib driver) and compare the requests only; with the ROMs their MIDI agrees byte for byte.

What the work found:

* **Uncertainty met, U-11** (UNCERTAIN.md): practice-return-tandy first differed at checkpoint 97 (CODE's effect 45: tone
  channel 2's period byte C0h on the original, CAh on the rewrite). TSOUND's random number adds the carry the interrupted
  program left; the machine had delivered 19 of the 180 ticks with the carry set (the first at checkpoint 2), the C# and the
  vectors take it clear. Which instruction a tick interrupts is this machine's choice (its speed), not the game's, so the
  tick's handler now starts with the carry clear (the interrupted program gets its own flags back); the Tandy sessions agree.
  Not masked in the comparison: a different carry would show.
* **Registered differences met**: D-18 (Custom, written out with the evidence), D-20 (without the ROMs), D-602 (the
  driver's stop). No C# bug was found: the four reconstructed drivers agree with the originals over whole sessions.
* **Tool**: `oracle_common` needs `COVERT_DATA_ROOT` where the checkout has no `build/` (section 5): without it the cuts are not placed and FINAL
  ran into DOS's date (the first AdLib run, kept as evidence in build/session/COVERT/practice-return-adlib).
* Test: `SpeakerTests.InterruptsGivenAtOnceAreToldToTheWatcherUntilTheDriverGivesTheTimerBack`.

Not covered: a tick inside a driver command (D-19's reordering is implemented and was met at no checkpoint), the gunfire of
a raid with a sound card (TAC's command 44), a real OPL's or MT-32's sound (the bytes are compared, not the synthesis:
U-10 and Munt), and the original's longer play time (180-811 ticks a session: this machine runs about 10 million
instructions a second without waiting for a human).

### Item 10: campaign wiretaps at levels 1 and 3, as recorded (15 November 2026)

The level-0 taps (item 6) and the level-2 tap (item 8) left levels 1 and 3. As in item 8, each chain starts from a new
case the menus part really hands over: FINAL/plain-new-case's keys with the other difficulty chosen (the menu's order is
Local Disturbance, National Threat, Regional Conflict, Global Crisis: levels 0 to 3, checked in the block's `level`),
then GAME's way from the CIA building to a building of the case and Place wiretap (code 4), then BUG on that block, and
GAME again on BUG's answer. Both sides recorded and compared at every checkpoint.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| FINAL/level1-new-case | start image | a new character (BOND) at National Threat, training, the briefing, the portrait: the level-1 case handed over | 118 | exit |
| FINAL/level3-new-case | start image | the same at Global Crisis: the level-3 case | 120 | exit |
| GAME/level1-tap | FINAL/level1-new-case | six clues, the CIA building, Leave building, the city, the Rev. Guards hideout: Place wiretap, "The guards are unsuspecting. You will have 9 minutes." (code 4, level 1) | 180 | exit |
| GAME/level3-tap | FINAL/level3-new-case | two clues, the city, the Mercenaries agent's building: Place wiretap, the same box (code 4, level 3) | 128 | exit |
| BUG/level1-tap | GAME/level1-tap | **a campaign wiretap at level 1**: the chip in the hand placed cuts a phone off: the clue report (`reveal_cast_clue`, `show_clue_report`: Libyan Emb., "Method: Electronic WireTap", a related clue), then an alarm powered: "Alarm!!!", hand-off to GAME (code 0) | 23 | exit |
| BUG/level3-tap | GAME/level3-tap | **a campaign wiretap at level 3**: hidden chips, nine minutes; the chip placed, a swap that powers two alarms: "Alarm!!!", Esc, hand-off to GAME (code 0) | 44 | exit |
| GAME/level1-tap-return | BUG/level1-tap | back at the hideout, "A few guards are peering through the windows.", Place wiretap: "The guards are suspicious. You will have 6 minutes." (code 4) | 8 | exit |
| GAME/level3-tap-return | BUG/level3-tap | back at the building, "It appears extra guards have been posted.", Place wiretap: "The guards are nervous. You will have 2 minutes." (code 4) | 8 | exit |

629 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the ends and the
final blocks. No registered difference was met. The original side is unchanged, so no earlier recording became stale.

What the work found:

* **C# bug, fixed** (`ElectronicsState.CodedFlag`): BUG/level1-tap first differed at checkpoint 6 (853 pixels): the clue
  report of the tapped phone said "Method: Clandestine Photo" with the camera where the original says "Electronic
  WireTap" with the wiretap's icon. BUG's `coded_flag` (DS:675e) is one variable, set by `gather_intelligence` (00e8:1882)
  and read by `show_clue_report` (00e8:2911) as the source of a clue found in an enemy's building; the rewrite kept the
  first in `ElectronicsState` and the library's report read a second copy in the dossier texts, which BUG never set. The
  flag now lives in the dossier texts, as TAC's `announce_flag` does. No earlier session reached a cast clue in BUG (the
  level-0 and level-2 taps showed messages, dossiers and facts). The same session agreed at every checkpoint afterwards.
  Test: `BugDgroupTests.TheCodedFlagIsTheClueReportsSource`.
* The guards' boxes at each level (unsuspecting, suspicious, nervous: nine, six and two minutes) and the building lines
  after an alarm agree.
* Not covered here: a tap solved (every phone cut off) at levels 1 to 3; the alarms ended both boards. Item 16 does it.

Recording order (after items 1-9): FINAL/level1-new-case, FINAL/level3-new-case, GAME/level1-tap, GAME/level3-tap,
BUG/level1-tap, BUG/level3-tap, GAME/level1-tap-return, GAME/level3-tap-return.

### Item 11: an ambush won, as recorded (15 November 2026)

The ambush of item 5b (GAME/regional-ambush, level 2, the attackers the Marxists) played again from the same block and
the same time of day, this time won, and GAME's return after it. Both sides recorded and compared at every checkpoint.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| TAC/street-ambush-won | GAME/regional-ambush | **the ambush won**: the street (one room, 12 x 13 cells), the agent at its bottom edge with the ambush's kit (pistol, vest, four fragmentation grenades, three spare clips), five attackers (`Diff` 2); shots (space) at the nearest attacker in front, the keypad turning the agent towards them, two reloads; all five shot, one wound: "You have eliminated all of your enemies." (`TacEndMessage`: `Rtime` > 0x40, no guard awake), the hand-off to GAME (code 0, person 0, result 0) | 359 | exit |
| GAME/ambush-won-return | TAC/street-ambush-won | back in entry mode 0x0a with person 0 (`CityScreens.EndAmbush`, the branch not taken by ambush-return): time to recover from the wound, a satellite intercept, the ambush won's prose ("...conclusive evidence that they came from the Marxists."), the attackers' building added to the city (`AddBuilding`, found in the fight: "Marxists agent" in the menu), the airport, the travel map | 58 | script exhausted |

417 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the ends and the
final blocks, with CA-1 counted in the raid's final block (city 0's activity counter: the original's count of awake guards,
0 once all five are down, against the campaign's 7). The original side is unchanged, so no earlier recording became stale.

How the keys were found: the lost ambush's attackers shoot a still agent down within 90 frames, and a script written blind
fired its shots at nothing: a frame of the raid polls the keyboard twice (`UpdatePlayer`'s look for a key, then
`FlushInput`, which throws away a key it finds), so a key is acted on only when it is the frame's first look. An exploration
copy of the original side (kept out of the repository, as in item 5b) logged the raid's state at every look (TAC DS:bb72/bb7c
the agent, DS:c870 the actors' records, DS:a8ec `Rtime`, DS:cbac `Wounds`, DS:96f6 `Bullets`, DS:91ee the cell map) and
then chose each key itself, closed-loop: at the frame's first look, space when the nearest awake attacker lies in the
agent's cone (`direction_from_vector_coarse`, the facing or the one before it) with a chance (`aim_and_fire`: half his time
seeing the agent, plus one, less one per 48 pixels) of at least one, else the keypad key towards the nearest attacker, else
nothing. The keys it played were then fixed as the session's script: the session is an ordinary key script, replayed alike
on both sides. The first policy tried won (five down by frame 177, one wound of the vest's three).

What the work found:

* No C# bug and no new register entry: the ambush's shots, hits, reloads and its "all enemies eliminated" end, and GAME's
  won-ambush branch (the prose and the building found in the fight), agreed at every checkpoint at the first comparison.

Recording order (after item 5b's chain): TAC/street-ambush-won, GAME/ambush-won-return.

### Item 12: the joystick in a whole session, as recorded (15 November 2026)

A stick on the game port of both sides, moving: the set-up's "1. Joystick and Keyboard", FINAL's calibration with the
stick at rest, the stick moving the main menu's highlight and its buttons taking and cancelling, into a practice and out of
the game. Until now the joystick was proven per routine (MiscJoystickOracleTests on MISC.EXE alone) and on the rewrite side
alone (`LauncherTests.TheJoystickChosenInTheSettingsMovesThroughTheMainMenu`); `joystick-quit` had only the calibration
without a stick.

Inputs (section 2, "Joystick"): the spec's `stick` puts the stick on the port at the start, `{stick x y buttons}` in the keys
moves it from the next entry on (not an entry itself). A stick changes when the game takes an entry, on both sides: the
original side when `take_entry` gives the entry, the C# side at `HeadlessHost.EntryTaken` (`RewriteSide.GiveTheStick`).

Original side (`campaign_side.py`; additive: without `stick` the port reads FFh as before): a write to port 201h starts the
axis timers; each read gives bit 0 (X) and bit 1 (Y) set while fewer reads than the axis's count have been made since the
write, bits 2 and 3 set, the buttons in bits 4..7 clear while held: the port of tools/oracle/joystick_vectors.py, on which
MISC.EXE's calibrate, measure and scale routines are proven. The counts for a position are the model of
`JoystickReading.FromStick` (U-12: 3, 61 and 119 turns of MISC's loop at the left or top, the middle and the right or bottom),
written out in the tool, not taken from the product. `inputs.json` holds the stick and the port's reading at each entry; the
C# side computes `FromStick` of the same stick and stops if the two models' readings differ (they do not). meta.json counts
the port's reads (`game_port_reads`: 1516 here).

| Session | Set-up | Parts (checkpoints) | What it covers | End |
|---|---|---|---|---|
| joystick-menu | IBM sound, joystick and keyboard; a stick at rest | INTRO 2, FINAL 47, CODE 21 | the calibration at rest (Enter answers both corner boxes), the title; at the main menu the stick pushed down twice (one step each), held down for four entries (two steps, the second stopping at the last line), held up for two entries (one step), the first button taking Practice a skill, the first button taking the first level, the stick pushed down and right twice and the first button taking the cryptography; in CODE the second button and Enter (only the clock changes), Alt-Q, Yes | exit |

70 checkpoints, every one agrees (the part, the text pages and pictures, the blocks, the requests and the PC speaker's
stream with 182 ticks), apart from D-600 and D-602. `SessionProofTests.AStickNotGivenIsReportedWhereTheOriginalFirstMoved`
shows that the comparison sees the stick: the same recording played without the stick (the port as with no joystick) is
reported at checkpoint 19, where the original's highlight first moved (1899 pixels).

The 13 earlier whole sessions were recorded again with the changed tool: their checkpoints, inputs and final blocks are
byte for byte those of their previous recordings (mastermind-debrief's previous one predates item 9's device stream: its
calls differ by the stream and the process calls only; its pictures, blocks and every other field agree), and all 14 whole
sessions agree. The part sessions' tool (`original_side.py`, `session_store.py`) is unchanged.

What the work found:

* **No C# bug.** The rewrite's menus, calibration, scaling and button reads agreed at the first comparison.
* **A held stick moves the menu one step for every two entries, on both sides** (the one-step observation of a headless
  test): a box reads the stick once per pass, after its keyboard look (`WaitForInput`: `NoKeyWaiting`, then
  `PollJoystickKey`), and the pass then looks at the keyboard a second time (`!NoKeyWaiting() || deviceKey != -1`, the
  original's order), which takes the next entry; after a step the box waits 15 frames without a look. So a stick held for
  two entries is one step, for four entries two. With a real stick the host's reading follows the hand, not the entries:
  about five steps a second (the 15 frames). Not a difference.
* `FlushInput` waits, without a keyboard look, until both buttons are up: a script must let a button go with the next entry
  (as here), else both sides would wait for ever (the stick changes only when an entry is taken; not tried).
* U-12 is not settled: both sides use the same model, and the menus only ask whether an axis byte is beyond 0x60 of the
  centre.

Not covered: a real stick's counts (U-12), the stick in the raid, the chase, GAME's screens or the cryptography's
workstation (their joystick routines are the same functions as the menus' and proven by the function oracle), Alt-J, and the
calibration's corners with the stick pushed there.

Recording order: after items 7-9, `campaign_side.py tools/session/sessions/covert/joystick-menu.json` (it takes nothing
from another session).

### Item 13: the jailbreak raid, as recorded (15 November 2026)

The last gameplay gap (items 5b and 8, "Not reached: the jailbreak"). GAME leaves for the jail-break raid (code 8) when
`Sprung` is set (`CityScreens.SettleTheMission`, GAME 00f7:1608), and `CastRules.TrySelectPerson` (GAME 00f7:951c) sets it
when a plot step meets an event of a suspect under arrest, in a case above level 1, the suspect not singled out and the
MasterMind free. The chain below starts from the level-3 case of item 10 (FINAL/level3-new-case). Reading its block showed
the way: the Paymaster (Saadi Hammid, Libyan Emb., role bit 1) lives in Tehran, where his organisation has a building, and
his next event is "Withdrew money", which gives him the plot object Payoff. A building whose person holds a plot object
puts it in the target room as the hidden information (tile 3Ch, `BuildingRules.FurnishRoom`); the use key on it makes the
person's role known (`Rknown`), the evidence the interrogation needs to hold him.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| GAME/level3-arrest-breakin | FINAL/level3-new-case | two clues, the CIA building, the flight to Tehran, a satellite intercept; the hotel: **Sleep through case**, an hour a poll, until the plot gives the Payoff to the Paymaster (a bulletin teletype, cut short by two keys); awake, the Libyan Emb. active cell: Break into building (code 1) | 340 | exit |
| TAC/level3-arrest | GAME/level3-arrest-breakin | **a level-3 raid with the plot object present**: all eight guards in the target room (`MacHere`, 26 guards to come); the kit (Uzi, gas mask, Kevlar vest, gas and stun grenades); two rooms and two doors (F1); a stun and a gas grenade thrown through the open door (F10, F7, F6); the guards who come to the door shot; in the target room the **Payoff taken** (F1 at the hidden information: the Paymaster's role known, and the cell becomes tile 63, below), **Saadi Hammid arrested** (his dossier, the question, Enter); out through the two rooms with three wounds: "You quickly slip out of the building." (code 0, result 3: object and arrest, not turned) | 640 | exit |
| GAME/jailbreak-warning | TAC/level3-arrest | the Chief on the Payoff; the **interrogation** ("We know that your role was: Paymaster."), the local jail; in its four hours the plot reaches the Paymaster's event, now pending again (the seized Payoff): **`TrySelectPerson` makes him `Sprung`**; the Chief's comment, the CIA building, Leave building, and the city screen opens with the **warning**: "JailBreak in Tehran! Libyan Emb. agents are trying to free Saadi Hammid!" (code 8, the prisoner the target) | 106 | exit |
| TAC/jailbreak | GAME/jailbreak-warning | **the jail-break raid**: its fixed kit, the agent in the jail's cafeteria, the Libyan Emb. agents coming in; shots at those in sight, one of them down, the agent overcome by his wounds (code 0, person -1: the attempt not foiled) | 401 | exit |
| GAME/jailbreak-return | TAC/jailbreak | back in entry mode 8 (`CityScreens.EndJailBreak`): the bust-out scene, "Saadi Hammid escapes!" (alerted, freed, the Libyan Emb. building lost from the city's menu), the city, the travel map, Stay here | 45 | script exhausted |

1532 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the ends and the
final blocks, apart from registered differences: CA-1 in the two raids' final blocks (city 0's activity counter: 5 against
2 and 6 against 5), D-11 in GAME/jailbreak-warning (31 checkpoints, below). The original side is unchanged, so no earlier
recording became stale.

What the work found:

* **C# bug, fixed** (`BuildingRules.SetUpThePlotObjectsGuards`, from `BuildingRaid.FinishSetUp`): TAC/level3-arrest first
  differed at checkpoint 30, the raid's first frame: the frame's music asked for variant 2 where the original asked for 1,
  the random numbers having parted. The end of tac_init (TAC 00e8:0018) only sets `MacHere` (practice, person 0, a double
  agent's city); the rewrite assigned it, and so cleared the `MacHere` that `furnish_room` had set for the person's own
  plot object: 20 guards were left to come instead of 26, and they were placed at random instead of in the target room.
  A field-by-field comparison of TAC's data segment on both sides at the first entries named the two fields. No earlier
  session had a plot object held by the building's person (the practices and person 0 set it the other way). Test:
  `BuildingRulesTests.ThePlotObjectTheBuildingsPersonHoldsKeepsItsGuards`.
* **C# bug, fixed** (`RaidState.TileType`, `McgaGraphicsDriver.CopyRect`): the rewrite then stopped with an index out of
  range at the use key on the hidden information. `arrest_prompt` empties the cell (FFh) and goes on to `use_piece` with the
  plot object's tile, which opens: the open bit is flipped on bare floor, 7Fh, tile 63, one past the table of 62 kinds.
  The original reads that record on the movement tables behind the table (TAC DS:067a: the picture at MoveY[7], MoveY[8] =
  0, -2; the bits at Move8X[0], as `TileBits` already modelled), in `redraw_cell`, the radar and the room's view. Every
  read of a cell's record now goes through `TileType`. The picture's row -2 is looked up in front of MGRAPHIC's table of
  rows (165Ch, 165Eh: both 0 in the driver's file and never written), so copy_rect copies from the top of the tile sheet;
  the driver modelled only rows 0..199 for a copy (CA-24 is row 200 for the fills). Tests:
  `BuildingRulesTests.TileSixtyThreeIsReadOnTheMovementTablesBehindTheTable`,
  `McgaGraphicsDriverTests.ACopyFromAboveThePageStartsAtItsTop`. The session agreed at every checkpoint afterwards.
* **D-11 avoided where it could be, met where it could not**: under the legacy start image (campaign-v1) the CIA building's
  animation HQ.PAN does not load after the interrogation (`pan_load` 0, checkpoint 58), and at the warning, after the
  half-loaded picture, 27803 pixels differ at checkpoint 74, where the mask (until the release) has ended. That recording is
  kept (GAME/jailbreak-warning/original-20261115-185333). In the compact image (stasi-v1, as item 8's tracer) HQ.PAN loads and
  the warning's WARNING.PAN does not: the half-loaded warning animation is masked by D-11 to the end of the part (31
  checkpoints, pixels only; the colours agree). The block GAME hands on is the same in both images but for the driver
  segments, so TAC/jailbreak and GAME/jailbreak-return were recorded again on it (their first recordings are kept as well).
* No difference in GAME's sleep, the plot's `TrySelectPerson`, the interrogation with evidence, the warning, the jail-break
  raid or `EndJailBreak`'s escape branch.

How the keys were found: an exploration copy of the original side (kept out of the repository) read the case from the
hand-off block (the persons, roles, events, plot objects and buildings) and GAME's plot as it ran (`Gtime`, the plot
objects' holders, the events' `chron`, `Sprung` at DS:1454), which showed the hour the Payoff changed hands; the raids were
then played by a controller that read TAC's state at every look (the agent, the eight actors, the cell map, the doors, the
grenades) and chose each key at the frame's first look (items 5b and 11): waypoints between doors, F1 at closed doors, the
grenades by type, and shots at an awake guard with a clear line who sees the agent or stands close. A few tries of the
plan (the grenades' order, when to shoot) got the agent out alive; the keys it played are the sessions' ordinary scripts, replayed alike on both sides.

Not reached here: the jail break foiled (`EndJailBreak`'s other branch; the controller that shot the visible ones lost
by frame 200, standing still by frame 570): item 15.

Recording order (after item 10's FINAL/level3-new-case): GAME/level3-arrest-breakin, TAC/level3-arrest,
GAME/jailbreak-warning, TAC/jailbreak, GAME/jailbreak-return.

### Item 14: DOS outside the modelled services, as recorded (15 November 2026)

The last gap of RELEASE-1.0's list: what the player of the rewrite meets when the disk refuses something, a full disk, a
missing file, a floppy prompt, and a saved game that is not whole. Each case was read in the original's code (its
reconstructed functions and the C library's `write`, disassembled from GAME.EXE), and where it can be met on the player's hard disk and modelled on
both sides it was recorded. The session tool learnt one spec field, `dos` (section 2): `missing` takes game files off
the player's disk (DOS's open and "does it exist" answer error 2, `find_file_on_disk` answers "not found"), and
`cannot_create` makes DOS refuse to make a file or open it to write (error 5, access denied, as for a read-only file).
The C# side gets the same disk: a copy of the game folder without the missing files, and in the saves folder a folder of
the refused file's name, which Windows does not let the game open as a file (`RewriteSide`).

| Session | From | What it covers | Checkpoints | End |
|---|---|---|---|---|
| GAME/save-cannot-create | travel-save's keys, `cannot_create` CV0.SVE | a new case, the flight to Cairo, the hotel's Save Game into the first slot: the drive prompt, the slot list, and **"File Error: c:cv0.sve"** (GAME 00f7:a833 makes the file first to see that it can be, and says so when it cannot); nothing is written, and the game goes on with the rest of travel-save's keys to Alt-Q | 459 | exit |
| GAME/breakin-no-tac | regional-breakin's keys (after FINAL/plain-new-case), `missing` TAC.EXE | a level-2 case to Break into building with TAC.EXE gone: **"Please insert Disk C (if 3.5" insert Disk A). Press Return to continue."**, asked again at each Return, as GAME ends for the raid with `CheckForDisk(3, 1)` (GAME 00f7:005a) | 196 | script exhausted |

655 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests, written files), and so do the
ends and the final blocks; no registered difference was met. The original side's change only adds the `dos` field, so no
earlier recording became stale (their specs have none).

The cases, what the original does, and what the rewrite does:

1. **A save that cannot be written.** *Cannot be made* (a read-only CV?.SVE; on the rewrite's side also a saves folder
   Windows does not let it write, or a file another program holds): the original's save screens make the file first
   (GAME 00f7:a833 `save_to_named_file`, FINAL 0100:a055 `write_save_game`) and say "File Error: <path>"; nothing is
   written and the game goes on. The rewrite let Windows' exception through and stopped (**C# bug, fixed**, below); it now
   says "File Error" as the original, recorded in GAME/save-cannot-create (the menus part's save screen is the same library
   code, `LibrarySaves` over `Headquarters.WriteSaveGame`). *A full disk*: making the file takes no room, so the save goes
   on, and neither `save_game` (GAME 00f7:aad4) nor `write_env_state` (FINAL 0100:a2f6) looks at what `write` answers. The
   C library's `write` (GAME 00f7:efe6) returns what DOS wrote, fewer bytes than asked when the disk fills, and -1 (errno
   28) when DOS wrote nothing (00f7:f0f3..f109). The save screen then says "Game has been saved." over a cut file: its
   "Game NOT saved. Disk Full." and "Write access denied." are dead text, since `write_checked` (GAME 00f7:a8ef, FINAL
   0100:a111), the only code that sets `save_error`, has no caller, and the critical-error handler around the save (GAME
   00f7:001a: `mov al,0; iret`, "ignore") is for a write-protected or empty drive, not a full disk. The rewrite does the
   same: a write Windows refuses answers -1 and the game goes on with "Game has been saved." (`GameFolderFiles.Write` and
   `Close`, which let the exception through before); the cut file is then listed as "(EMPTY)" (case 4, D-105). Not
   recorded: the C# side has no full disk to give (the saves folder is a real folder; a model of free space would be new
   product code for a test), so the original's answer is read from its code. *The Hall of Fame*: `read_fame_file` (FINAL
   0100:4cd0) opens FAME.DTA to read and write; when that fails the table stays cleared, an empty Hall of Fame;
   `write_fame_file` (0100:4d1d) writes only a file it may write (`access(.., 2)`) and does not look at the write. The
   rewrite does the same, now also for a read-only FAME.DTA in the saves folder (`Access` honours the read-only mark) and
   a saves folder that cannot be made (`DiskFailureTests`).
2. **A game file missing.** *Pictures, animations and other resources* (`open_resource`): the original ends the part with `fatal_error` (text mode,
   `perror`: "<name>: No such file or directory", exit code 99), and its launcher, whose block still names the part, runs
   the part again (COVERT 0000:0010), to the same end. The rewrite ends the play and the launcher says it: "the campaign
   stopped on a file it could not read (hq.pic: No such file or directory) ..." (D-12, whose text now says so). Two **C#
   bugs, fixed**: the file's name, which the part prints on DOS's console, was dropped (`GameFolderFiles.Write` to handle
   2), so the launcher said only "one of its files"; and the raid, the electronics workbench and the cryptography
   workstation took the exit code 99 for the player's leaving, so the launcher ran the part again, for ever, without a
   word (`Game.PartFailed`; test `APictureMissingFromTheGameFolderEndsThePlayNamingIt`). *Cleared texts*: D-16 (the
   "security clearance" notice). *TEXT.DTA in a raid* (the computer's password, TAC 00e8:38bb): `fopen` is not looked at,
   and the original reads a null stream (undefined); the rewrite stops there (`UninitialisedReadException`, now naming
   TEXT.DTA), and `Game.Main` turns that and the other places the original's behaviour is undefined
   (`MemoryOverrunException`, `DivideErrorException`) into a message the launcher shows, not a crash (new code). *The
   world and crime files* (FINAL 0100:4c2c and the crime reader): `open` is not looked at; the reads fail and the tables
   keep FINAL.EXE's own values, in both. *Programs, drivers
   and fonts*: the launcher requires INTRO, FINAL, GAME, MGRAPHIC and FONTS.CV to find the game; any other is read when
   its part or driver starts, and a missing one ends the play with the launcher saying "CHASE.EXE is not in the game folder"
   (the original's EXEC fails and its launcher tries the same program again, for ever; D-18 shows its "Filename not
   found" for a driver). TAC.EXE is also a floppy disk's test file: case 3.
3. **A floppy prompt.** `CheckForDisk` (FINAL 0100:aaa0 and its copies in every part) looks for the disk's test file with
   `find_file_on_disk`: final.exe (disk 1), game.exe (2), tac.exe (3), hq.pan (4). On a hard disk with the whole game the
   test files are always there; the prompt is reachable only with one missing. FINAL.EXE and GAME.EXE are required by the
   launcher, so of the programs only TAC.EXE can be missing: GAME asks for disk 3 before every raid, and for ever, since
   Return looks again (GAME/breakin-no-tac; the rewrite's `FindFileOnDisk` looks in the game folder, the same answer).
   HQ.PAN is only probed (`CheckForDisk(4, 0)` before an animation, with animations on): the probe then asks for the
   part's own disk, which is there, so the animation is skipped without a prompt. A missing TAC.EXE also takes the probe
   `CheckForDisk(3, 0)` of `load_picture_measure` (GAME 00f7:3e38, checkpoint 84 of breakin-no-tac): the picture is not
   measured, alike on both sides. Critical errors (INT 24h): the parts install their "ignore" handler only around a save
   (D-101), and `find_file_on_disk` its own for an empty floppy drive; a hard disk does not raise them. The save screens'
   drive question accepts A and B: under DOS without a floppy the original says "No disk in drive A."; the rewrite has no
   floppy drives and every drive is the saves folder (`GameFolderFiles.VerifyDisk`, new code).
4. **A saved game that is not whole** (CV?.SVE cut short, or damaged). The original lists every slot whose file opens and
   loads whatever it holds: a head shorter than 1008 bytes is completed with what `load_game`'s stack held (GAME
   00f7:aebb), counts beyond the tables' 32 and 64 records read past them, and a file that ends early leaves the rest of
   the tables as the part had them (the title's in FINAL, the running case's in GAME). The rewrite stopped there
   (`UninitialisedReadException`, `MemoryOverrunException`). Now a file that does not hold its head and every table its
   counts say, or whose counts are beyond the tables, is listed as "(EMPTY)" and cannot be chosen (**D-105**,
   `SaveFileCodec.IsWhole`; a longer file is read as by the original). Test:
   `DiskFailureTests.ASaveThatIsNotWholeIsListedAsAnEmptySlot` (whole, one byte longer, one byte short, a short head, an
   empty file, a cast of 40).

Tests: `DiskFailureTests` (Launcher.Tests): a read-only save, a saves folder that cannot be made, a file another program
holds, a seek before the start, the console's text, the campaign's "File Error" box, D-105, a missing picture.

Recording order: GAME/save-cannot-create takes nothing from another session; GAME/breakin-no-tac after FINAL/plain-new-case.

Not recorded: a full disk (above), a missing picture (the original's part ends in text mode and its launcher starts it
again; a part session stops at the mode change, which it does not model, and the rewrite ends the play by design, D-12).


### Item 15: the jail break foiled, as recorded (16 November 2026)

The one branch left of item 13: the agent wins the jail-break raid, and GAME's `CityScreens.EndJailBreak` takes the other
side of `shared.Person == NoOne`. Both sessions start from item 13's block (GAME/jailbreak-warning, the same one
TAC/jailbreak was recorded on), so the jail, the attackers and the time of day are the same as in the lost raid.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| TAC/jailbreak-foiled | GAME/jailbreak-warning | **the jail-break raid won**: the fixed kit (Uzi, Kevlar vest, four fragmentation grenades), the agent in the jail's cafeteria (room 8, `Diff` 4) and the Libyan Emb. agents coming in through the one door from the corridor, one about every 32 frames; 19 keys (shots with a few steps to turn towards the next attacker), no wound; the last guard down and none awake: "You have eliminated all of your enemies." (`UpdatePlayer`'s end for the kill raid: `Rtime` > 0x40 and no guard awake, `InRoom` 0), the hand-off to GAME (code 0, person 0: not -1) | 548 | exit |
| GAME/jailbreak-foiled-return | TAC/jailbreak-foiled | back in entry mode 8 with person 0 (`CityScreens.EndJailBreak`, the branch item 13 did not take): the scene's area cleared, the box "Escape attempt foiled!", the city screen, the airport's travel map, Stay here, the city screen again | 45 | script exhausted |

593 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests), and so do the ends and the final
blocks, apart from CA-1 in the raid's final block (city 0's activity counter, 5 against 0: the original's count of awake
guards, 0 once all are down). The original side is unchanged, so no earlier recording became stale.

What the work found:

* No C# bug and no new register entry: the raid's shots, hits, the attackers' arrivals and its end (`TacEndMessage`'s
  "all enemies eliminated" branch in a kill raid), and `EndJailBreak`'s foiled branch, agreed at every checkpoint at the
  first comparison.
* The raid is won by killing the attackers, not by leaving the building: in a kill raid the raid is lost when an awake guard
  is in the target room while the agent is not (`CountGuard`, so the agent stays in the room), and it ends as won
  once the clock passes 0x40 frames and no guard is awake. The agent does not need to move from his start.

How the keys were found: the lost raid's controller chose each key from the state alone and lost to the shooting. This time
the exploration copy of the original side (kept out of the repository) could also branch: its memory and registers were
saved at each frame's first look at the keyboard (items 5b and 11), and for each candidate key (nothing, space, F1, the
walking keys) the next four frames were played on the original and scored (attackers down, wounds, bullets, the aim towards
the nearest awake attacker); the best key was played for real. A shot is a draw (`aim_and_fire`: a hit below the chance in
four), so this fires only the shots that hit: the attackers fell to 19 keys, without a wound. The keys it played are the
session's ordinary script, replayed alike on both sides. The script holds the polls as the original took them, taken from
the exploration run itself: a list rebuilt as two polls to a frame did not replay (the first recording of it, kept, ended
early with the agent lost).

Recording order (after item 13's TAC/jailbreak chain): TAC/jailbreak-foiled after GAME/jailbreak-warning, then
GAME/jailbreak-foiled-return.

### Item 16: a campaign wiretap solved, as recorded (16 November 2026)

Item 10's two level-1 and level-3 boards ended on an alarm. Here the same blocks (GAME/level1-tap and GAME/level3-tap's
Place wiretap) are played again with the board planned: every phone cut off, no alarm powered at any step. The boards are
the ones the part builds from the same hand-off block and time of day (the board is a function of the seed). Each was read
from the rewrite's board (a throwaway dump after `build_circuit_board`, not kept: the rewrite's board agrees with the
original's at every checkpoint of items 6 and 10) and searched breadth-first over the circuit rules, a swap at a time
(the carried chip against any chip that is not fixed, a hidden chip by its real type), keeping only the states with no
alarm powered; both boards fall to four swaps. The keys then walk the cursor to each slot; each phone newly cut off opens
its clue (a report, a message screen, a map: one or more keys, stepped through from contact sheets). GAME's return follows.

| Session | Block from | What it covers | Checkpoints | End |
|---|---|---|---|---|
| BUG/level1-tap-solved | GAME/level1-tap | **a campaign wiretap solved at level 1**: six phones, nine minutes; four swaps, the clue of each new phone (Libyan Emb. report, message screens); "Phone taps completed. No Alarms", Enter: hand-off to GAME (code 0, 30 per cent tapped) | 413 | exit |
| BUG/level3-tap-solved | GAME/level3-tap | **a campaign wiretap solved at level 3**: hidden and fixed chips, five phones; four swaps (two hidden chips shown for ten ticks as taken); the clues (messages, a map); "Phone taps completed. No Alarms", hand-off to GAME (code 0, 25 per cent tapped) | 475 | exit |
| GAME/level1-tap-solved-return | BUG/level1-tap-solved | back at the Rev. Guards hideout, Place wiretap: "The guards are unsuspecting. You will have 9 minutes." (code 4, 30 per cent tapped) | 8 | exit |
| GAME/level3-tap-solved-return | BUG/level3-tap-solved | back at the Mercenaries agent's building, Place wiretap again (code 4, 25 per cent tapped) | 8 | exit |

904 checkpoints. Every checkpoint agrees (picture, colours, hand-off block, sound requests) and so do the ends and the final
blocks. No registered difference was met and no C# bug was found: the solved board's clue screens (the report, the
message screens, a map), the tapped share handed back and the clock agree with the original. The original side is
unchanged, so no earlier recording became stale.

Not covered: a tap solved at level 2 as a campaign board (item 8's tap was ended by an alarm; level 2's board is the
same code), a tap solved against the clock's last seconds.

Recording order (after item 10): BUG/level1-tap-solved, BUG/level3-tap-solved, GAME/level1-tap-solved-return,
GAME/level3-tap-solved-return.

## Summary: what the session proof covers, and what it does not

Covered, against the original's own code run whole with its own drivers (103 recorded sessions after items 8, 9
and 10; the first 79 held 60814 checkpoints; every one agrees apart from registered differences):

* every part of the game: the set-up screen (INTRO), the menus (FINAL: title, credits, options, Hall of Fame, a new
  character and case, the briefing, the debriefing, loading a game, the practices), the campaign (GAME: the walk,
  Intelligence and Data, travel, the hotel, stakeouts, interrogations, the end of a case, captures, exchanges, escapes,
  ambushes, saving and loading), the raid (TAC: practices at each level, break-ins, photographs, bugs, disguise,
  capture, the escape, ambush and jail-break raids), the electronics workbench (BUG: practices and campaign taps), the car chase
  (CHASE: practice and campaign tails) and the cryptography workstation (CODE: practice and campaign messages);
* the hand-offs between them: each part's answer, byte for byte, taken by the next part (items 4b-6), and the
  launcher's own flow from the DOS prompt to the end of the game across every boundary (item 7);
* the written files (saved games, the Hall of Fame), the PC speaker's sound driver with its timer, the joystick answer,
  Num Lock between parts, the text-mode set-up screen.

Since then (items 8, 9 and 10): the AdLib, Roland MT-32 and Tandy drivers in whole sessions, compared at the level of
the writes to their chips (OPL registers, MIDI bytes, SN76496 writes) with their timer ticks; the Custom choice; the car
tracer; campaign taps at levels 1, 2 and 3 (and the returns to GAME after them); an escape raid played out of the building; a debriefing reached in one whole play.
Item 11: an ambush won (the five attackers shot) and GAME's return after it (2 more sessions, 417 checkpoints). Item 14:
DOS's refusals: a save that cannot be made ("File Error") and the floppy prompt with TAC.EXE missing (2 more sessions, 655
checkpoints), with a full disk, missing files and saves that are not whole read from the original's code and tested on the
rewrite. Item 12:
the joystick moving in a whole session (joystick-menu, 70 checkpoints), on a model of the stick's counts (U-12). Item 13:
the jailbreak raid (code 8), reached in a level-3 case from an arrest held by evidence, and GAME before and after it
(5 more sessions, 1532 checkpoints), and item 15: the same raid won, and GAME's return with the escape foiled (2 more
sessions, 593 checkpoints). Item 16: a campaign wiretap solved at levels 1 and 3, with GAME's returns (4 more
sessions, 904 checkpoints).

Not covered: the graphics drivers other than MCGA (D-600), the sound itself as heard (the chips' writes are compared,
not the synthesised sound), a real joystick's counts (item 12 uses a model, U-12) and the mouse,
the timing of a real PC (frames are counted, not timed), and a full disk on the
rewrite's side (item 14: the original's answer is read from its code, a model of free space would be new product code).
A session
proves only its own path: the rest of the game is proven function by function by the oracle, not as a whole.


## 7. Limits

* One start image per part. A session can only vary what the hand-off block and the inputs vary. State the
  image holds (the drivers loaded, the memory left behind, as D-17 shows) is the route's.
* No keyboard interrupt (INT 9) is delivered. The timer (INT 8) runs only in a whole session and only once a program
  has put its handler on it (a sound driver), at a fixed machine speed; a part session (items 1-6) runs without it.
* Frames are counted, not timed: a busy loop of the original that a real PC would cut short by the retrace is
  one frame here, as it is in the C#.
* The original side answers `find_file_on_disk` with "found", unless the spec takes the file away (`dos.missing`,
  item 14). A full disk is not modelled: the C# side writes into a real folder.

## Code

| File | Role |
|---|---|
| `tools/session/original_side.py` | the original side: the machine, the key model, the cuts, recording |
| `tools/session/campaign_side.py` | the original side of a whole session: COVERT.EXE from the DOS prompt, processes, text mode, the timer |
| `tools/session/session_store.py` | paths, key script, recording format (shared definitions) |
| `tools/session/picture.py` | frames and differences as PNG (Pillow) |
| `tools/session/sessions/<part>/*.json` | the committed session specs (code, chase, final, game, tac, bug; covert: whole sessions) |
| `csharp/tests/CovertAction.Session.Tests` | `RewriteSide` (the C# run), `SessionComparison` (first difference, report, picture), `KnownDifference`, `SessionProofTests` |
| `HeadlessHost.EntryTaken`, `Game.SoundRequested` | the two events the C# side watches (new code for tests and tools) |
| `Game.Main(SharedState)`, `Game.PartStarting`, `Game.TextPage`, `HeadlessHost.TimeOfDay` | what a whole session watches and sets on the C# side (new code for tests and tools) |
