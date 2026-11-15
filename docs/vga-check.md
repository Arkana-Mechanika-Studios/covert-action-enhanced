# VGA check (bug register CA-48)

Question: the set-up's graphics menu gives "1) VGA (256 color)" the letter `E`, so COVERT loads `EGRAPHIC.EXE`.
Does that driver set an EGA 16-colour mode, or does it detect a VGA and do something else (256 colours, the 64-colour
palette on a VGA)? Is there anything else in the game that reacts to the letter for a VGA?

Verdict: **confirmed.** Choosing 1 "VGA (256 color)" gives the plain 16-colour EGA driver, exactly the same as
choosing 3 "EGA (16 color)". `EGRAPHIC.EXE` has no VGA detection and no 256-colour path. Whether the mismatch was
intended is unclear (see the end), but the menu's words and the driver's behaviour disagree.

## 1. Evidence

Method: the driver files are MZ images loaded without being run; each is a table of far routines (word offsets at
image offset 0x32; slot numbers as the start-up's driver table). Decoded by hand with capstone and run slot by slot
in Unicorn with a fake BIOS and fake ports (the probe only reads the driver file and records every int 10h,
int 21h and port access).

### 1.1 The menu and the launcher

* INTRO DS:04f6 is `"EMETC"` for the five menu lines VGA, MCGA, EGA, Tandy 1000, CGA
  (`INTRO 0000:0010`: `gfx_letter_by_choice[m] = gfx_driver_letters[e]`); the folding of `H` to `C`
  and `V` to `M` after the choice can never trigger (CC-8).
* COVERT turns the letter into the file name (`COVERT 0000:0010`: "'?graphic.exe' becomes e.g.
  'Mgraphic.exe'"): choice 1 loads `Egraphic.exe`, choice 2 `Mgraphic.exe`, choice 3 `Egraphic.exe`, choice 4
  `Tgraphic.exe`, choice 5 `Cgraphic.exe`. The runtime trace of the start-up (keys `112`) agrees:
  `EGRAPHIC.EXE` is loaded.
* The five file names that INTRO also keeps unused (DS:04fc) say the same, in the same order.

### 1.2 `EGRAPHIC.EXE`: what it does

Whole file: 9878 bytes of image, 32 relocations; no int 33h.

| entry | code | behaviour |
|---|---|---|
| set video mode (slot 36, image 0x9c6) | `test [arg], 2`: if bit 1 is set, mode **0Eh** (640x200, 16 colours; 0x400 paragraphs a page, 80-byte rows); else mode **0Dh** (320x200, 16 colours; 0x200 paragraphs a page, 40-byte rows). Calls `int 10h AX=mode`, then `int 10h AH=0Fh` and compares AL with the mode asked for; if different: `int 10h` text mode 3, prints "EGA is not supported on your machine" (DOS function 9) and exits (4Ch) | probe: argument 0 gives `int 10h AX=000Dh` then the check `AX=0F0Dh`; argument 2 gives mode 0Eh |
| palette (slot 26, image 0x1a1) | takes the 17 bytes (16 colours and the border); for each: `(c & 0x0f) + 8 & 0x17`, so colours 0..7 stay 0..7 and 8..15 become 0x10..0x17 (bit 4 is the register bit that carries intensity on an RGBI colour display); then `int 10h AX=1002h` (set all palette registers and the border) | probe, colours 0..15 and border 0: registers `00 01 02 03 04 05 06 07 10 11 12 13 14 15 16 17`, border `00` |
| one palette register (image 0x180) | the same mapping, `int 10h AX=1000h` | read from the code |
| blank / unblank (0xa54, 0xa6d) | wait for retrace on port 3DAh, write attribute-controller register 12h (`3C0h`: index 0x12, then 0 or 0x0F, then 0x20): turns the colour planes off or on | probe on the code |
| drawing | write mode and bit-mask programming through the graphics controller (`3CEh`, 30 sites) and sequencer map mask (`3C4h`, 12 sites), planar memory at A000h; CRTC `3D4h` (2 sites, row offsets) | decoded |

The only int 10h calls in the whole file are the five at image 0x19e (1000h), 0x1c3 (1002h), 0xa0d (set mode),
0xa11 (get mode) and 0xa23 (text mode on failure). **There is no `AX=1A00h` (display combination), no `AH=12h BL=10h`
(EGA information), no `AX=13h`-style mode, no 3C6h/3C7h/3C8h/3C9h DAC access, no VGA-only register**: the file never
asks what the display is, and it cannot produce 256 colours. On a VGA the VGA BIOS runs mode 0Dh (the VGA BIOS
supports the EGA modes) and the game gets 16 colours from the VGA's attribute palette.

### 1.3 `MGRAPHIC.EXE` (what choice 2 "MCGA (256 color)" loads)

Mode set (slot 36, image 0x45c): `int 10h` with AL = `0x13 | (arg & 0x80)` (mode **13h**, 320x200, 256 colours; bit 7
keeps the screen), then `AH=0Fh` and compare; failure prints "MCGA is not supported on your machine". Colours:
`int 10h AX=1012h` (set a block of DAC registers) with 128 table entries (probe: first 0, count 128); the palette routine
(0x118) writes 16 DAC registers from the driver's 128-entry colour table (`int 10h AX=1010h/1012h`, plus
`AX=1001h` border), and wraps the writes in the screen-off bit (port 3C4h index 1 bit 5, 3D8h). So it is the
VGA-DAC driver, and the one the C# rewrite reimplements (`McgaGraphicsDriver`, which is verified against it).

### 1.4 `TGRAPHIC.EXE` and `CGRAPHIC.EXE`

Tandy 1000 and CGA drivers, written for their own hardware (a byte scan finds port accesses to 3D8h, 3DAh, 3D4h
in both, and to 3DDh/3DFh in TGRAPHIC; int 10h sites: TGRAPHIC 10, CGRAPHIC 4; not decoded further). Neither is
chosen by the VGA line (letters `T` and `C`), so neither matters for the question. The CGA letter is the only one
programs test (below).

### 1.5 Who checks the letter

The letter is copied into each program as `GMode` (shared block byte 0x44). Searching all of the reconstructed C:

* Every comparison is `GMode == 0x43` or `!= 0x43`: **'C' (CGA) only** (`CGA = GMode == 0x43`; FINAL `a6dd`/`0056`,
  GAME `aebb`/`81a6`/`830f`, BUG `6985`/`4134`/`429d`, TAC `a9b8`/`ab21`, CHASE `001c`; photo recolouring, the colour of
  highlights, outline boxes). The letters `E`, `M`, `T` are never told apart by any program (the TAC comment "M, C, E
  or T" in `tac_00e8_06f6.c` describes the driver's `load_palette`, which is the slot, not a test of the letter).
* The set-up's `'V'`/`'H'` folding is dead (CC-8).
* So a VGA owner who picks 1 gets exactly the behaviour of an EGA owner: same programs, same game logic, the
  16-colour driver (the only difference between the choices is which driver file is loaded). The C# rewrite always uses
  the MCGA driver (`Game.cs` sets `GMode = 'M'`), which for game logic is the same as `E` (not `C`).
* The game calls the mode set with argument 0 only (`code_0000_0b5a.c`, `Launcher/Game.cs`, `CryptoWorkstation.cs`,
  `Headquarters.Menus.cs`), so the EGA driver's 640x200 mode 0Eh (argument bit 1) is never asked for.

## 2. What the evidence settles, and what not

Settled from the code: choice 1 loads a driver whose only mode is EGA mode 0Dh with the 16-colour palette registers;
it contains no VGA detection, and no program changes behaviour for `E` versus `M` (only for `C`).

Not settled from the files:

* Whether MicroProse meant choice 1 to be a 256-colour VGA entry: the label says "256 color", the letter says EGA.
  Three things point to a slip in this build's table rather than a deliberate EGA-on-VGA entry: (a) the label says
  256 colours, which EGRAPHIC cannot give; (b) the same program folds a letter `V` into `M` (an older table had a `V`
  for VGA that used the MCGA driver); (c) the line below it, "MCGA (256 color)", gives exactly the VGA driver. Against
  a slip: INTRO's own unused name table also pairs the VGA line with `Egraphic.exe`, so the pairing was written twice.
  Nothing in the shipped documents (README.DOC, the batch files) mentions VGA, MCGA or colour counts, so no document
  settles the intent.
* How the colours of the 0x10..0x17 palette values look on a real VGA: they select attribute-register values that
  depend on the VGA BIOS's DAC defaults for mode 0Dh; not decidable from the driver code.
* The original was not run on a VGA here (no hardware or DOSBox run in this study); the driver code and the runtime
  trace of choice 1 are the evidence.

## 3. Proposed CA-48 text (for `docs/original-bugs.md`)

| ID | Where | What the code does | Visible effect | Status | Found by |
|---|---|---|---|---|---|
| CA-48 | INTRO `hardware_setup_menu` (INTRO 0000:0010; driver letters "EMETC" at INTRO DS:04f6) | The graphics menu's first choice, "1) VGA (256 color)", stores the letter E, so COVERT loads EGRAPHIC.EXE, whose only modes are EGA mode 0Dh (320x200, 16 colours; 0Eh when asked for, never asked) with palette registers set through int 10h 1002h; it has no VGA detection and no 256-colour path (decoded and run in the emulator, `docs/vga-check.md`). "MCGA (256 color)" (letter M, MGRAPHIC.EXE, mode 13h with the VGA DAC) is the only choice that gives 256 colours. No program tells E from M: all test only for C. The code still folds H and V into C and M, as if an earlier table had a V for VGA. | A VGA owner who picks "VGA (256 color)" gets the same 16-colour EGA graphics as with "EGA (16 color)"; the 256-colour choice is the second line. Game logic is the same for E and M. | Confirmed from the drivers' code and the recorded run (14 Nov 2026); a menu/driver mismatch of the original. Intent unclear: a slip in the letter table or an undocumented EGA-on-VGA line; kept exactly (the rewrite uses the MCGA driver for every choice) | C# rewrite, 14 Nov 2026 |

Notes for the other registers: CC-8 can say "confirmed in `docs/vga-check.md`" for the VGA = Egraphic pairing; the
C# rewrite's set-up should show the same five lines and leave the driver choice to the host (it always draws with
`McgaGraphicsDriver`); a player-facing note could say that choice 2 is the 256-colour choice in the original.
