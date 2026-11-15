# Covert Action in C#: architecture

A C# (.NET 10) rewrite of Sid Meier's Covert Action (MicroProse, 1990, DOS). One game process replaces
the original's eight programs (COVERT launcher, INTRO, FINAL menus/briefing/debrief, GAME campaign, TAC
building raid, BUG electronics, CHASE car chase, CODE cryptography).

What it stands on:

* The original's functions, reconstructed as readable C for every function of the eight programs, each compiling
  back to the original bytes with Microsoft C 5.1. This is the specification of behaviour (the reconstruction
  itself is not published; the `// orig:` comments name its files).
* `docs/data-model/*.md`: the data model (the shared block, the saved campaign
  records, one header per module); `symbols/names.txt`: the names, many of them MicroProse's own.
* `docs/original-bugs.md`: the register of the original's bugs (CA-1 ...) and how each is handled.
* `tools/oracle/`: runs single original functions in an emulator and records what a port must reproduce.

No game bytes are in this folder. The player supplies the game; vectors derived from it live in
`build/oracle/` and are never committed (`RepositoryTests`).

**One program.** The rewrite is one program, not eight. What the original runs as eight executables that
hand a block of memory to one another are parts of one game here: a part is an object, starting it is a method
call, and what the parts tell each other is a typed value (`SharedState`). Nothing is loaded, chained or
executed at run time but the rewrite's own C#.

## Principles

* **Readable first (the first rule).** The code must be easy for a human developer to read and to change:
  idiomatic, plain C#, not C written in C# syntax. Typed models, strings, lists, enums, named constants and
  small named helpers where the original has offsets, flag words, raw buffers and magic numbers. The 16-bit
  helpers (`C16`, `CStr`) appear only where exact 16-bit behaviour changes a result, each use with a comment
  saying why. Faithfulness is held by the oracle tests, not by imitating the shape of the C. Byte layouts
  exist only in `CovertAction.Persistence`. Comments explain the game and the why (CONTRIBUTING.md, rule 1).
* **No emulator.** The game runs as plain C#: no x86 execution, no DOS or BIOS service layer, no hardware
  ports, interrupts, processor registers or memory segments at run time (section 4, "No emulator").
* **Domain first.** State is typed C# objects with named fields. DOS addresses, segment values and record
  offsets exist only in `CovertAction.Persistence`.
* **KISS, DRY, single responsibility.** Plain classes and records, no framework, no service locator, no
  global instance. One implementation per rule; the copies of a helper that every module carries become one
  method only where the copies are provably the same function (`GameLibrary.Clamp` lists its six originals).
* **Deterministic.** All chance comes from an injected `IRandomSource`; in play it is `MscRandom`, the
  generator of the original's C library. No file, clock, console or host randomness outside the boundary
  interfaces (`LayerTests`).
* **Faithful by default, fixable by choice.** The original is reproduced bug for bug
  (`OriginalBehaviour.Faithful`, what the oracle runs). Data-damaging bugs and plain visual glitches have a
  switch in `OriginalBehaviour` and are fixed in `OriginalBehaviour.Player`; other quirks are reproduced
  always. The list of switches is the single place where the two differ.
* **Traceable.** Every reconstructed operation cites its original place, its source status and its tests;
  every deliberate difference and every guess is in a register (CONTRIBUTING.md).
* **Proven against the original.** A function counts as ported when its oracle test passes: same result,
  same memory, same calls to the outside.

## 1. Layers and projects

Dependencies point down; `LayerTests` checks them on the compiled assemblies.

```
CovertAction.Host.Sdl     the program players start: an SDL 3 window, keyboard, sound device (referenced by nobody)
CovertAction.Host.Headless  a host without a window: scripted keys, instant time (tests, scripted runs)
        |
CovertAction.Launcher     the one program: builds the game from the player's files and a host, starts its parts
        |
CovertAction.Drivers      the game's own drawing, keyboard, timer and sound code behind the boundary interfaces
CovertAction.Presentation the original's functions that reach a boundary: screens, menus, module loops
        |
CovertAction.Platform     the boundaries: what the game's code asks of its drivers, and what the drivers ask of a host
CovertAction.Content      the player's game files (finding them; later pictures, fonts, texts, crime files)
CovertAction.Persistence  the only layer that knows byte layouts: shared block, saves, module data segments
        |
CovertAction.Core         typed state and the rules: functions that reach no boundary
        |
CovertAction.Msc          Microsoft C 5.1 semantics: 16-bit arithmetic, rand, the C string routines
```

| Project | May reference | Holds |
|---|---|---|
| `CovertAction.Msc` | nothing | `C16`, `MscRandom`/`IRandomSource`, `CStr`, the two "the original would have crashed" exceptions |
| `CovertAction.Core` | Msc | state per subsystem and its rules: `Shared` (hand-off, common helpers, option bits), `Crypto` (CODE), `Menus` (message box, animation and disk prompt variables), `Machine` (joystick and mouse variables), `Graphics.RasterPort`, `Records` (the campaign's record types and `CampaignTables`, shared by all parts and frozen); then `Campaign`, `Plot` (GAME, FINAL), `Electronics` (BUG), `Chase`, `Raid` (TAC); `OriginalBehaviour` |
| `CovertAction.Platform` | Core, Msc | the game's boundaries `IGraphicsDriver`, `ISoundDriver`, `IInputDriver`, `IMouseDriver`, `IGameTimer`, `IFiles`; the host's services `IHost` (`IVideo`, `IInput`, `IPcmOutput`, `IClock`); interfaces and the values their calls carry |
| `CovertAction.Content` | Core, Msc | `GameFolder`; the readers of the game's files: `Executable` (start values and tables of a program), `McgaDriverFile`, `FontSet`, `PictureFile`, `CatArchive`, `PanFile` and `PanPlayer` (animations), `TextFile`, `TextPlaceholders`, `WorldFile` |
| `CovertAction.Persistence` | Core, Msc | `Layout.StateLayout<T>`, `SharedBlockCodec`, `Dgroup.*` (one layout per module), save files |
| `CovertAction.Presentation` | all of the above | the functions that draw, play, read input, files or the clock. `Library`: the routines every part has, one implementation each (`LibraryOutput`, `LibraryInput`, `LibraryMenus`, over `Core.Shared.ILibraryState`); per part its own: `Crypto.CryptoWorkstation` (the minigame); `Boundaries` bundles the six interfaces |
| `CovertAction.Drivers` | Core, Msc, Platform, Content | `McgaGraphicsDriver` (the drawing code and the game's screen), `LineClipper`, `KeyboardDriver`, `GameTimer`, `NoMouseDriver`, `HostMouseDriver` (the mouse of the player option, D-630), `SilentSoundDriver`, `HostLink` |
| `CovertAction.Audio` | Platform, Content | the sound drivers' logic as C#: `Speaker.IbmSoundDriver` (ISOUND.CA, the PC speaker); `Adlib.AdlibSoundDriver` (ASOUND.CA, the AdLib card); `Roland.RolandSoundDriver` (RSOUND.CA, the Roland MT-32, its MIDI through `Roland.IMpu401`); `Tandy.TandySoundDriver` (TSOUND.CA, the Tandy 1000's and PCjr's SN76496 chip, its bytes through `Tandy.ISoundChip`); `ClockedSoundDriver` (a driver as `ISoundDriver` and `IPcmSource`, its timer interrupts timed by the frames the device asks for); `SoundCommands`, `SplitSoundDriver` and `PcmMixer` (the player's option of music and effects on two drivers, D-620, docs/SOUND-MIXING.md); `Synthesis.SpeakerVoice` (the PC speaker), `Synthesis.Sn76496Voice` (the Tandy's sound chip), `Synthesis.Opl3` (the AdLib card's FM chip, reused, docs/REUSED.md), `Synthesis.OplSynth` (it at the device's rate), `Synthesis.MidiStream` and `IMidiSynth` (MIDI bytes as messages, reused) and `Synthesis.MidiPort` (a ready MPU-401 in UART mode with a MIDI module behind it) |
| `CovertAction.Audio.Mt32` | Platform, Content, Audio | `Mt32Synth` (reused from Darklands): Munt's mt32emu with the player's MT-32 or CM-32L ROMs as an `IMidiSynth`; the only product assembly with a third-party library (the `Mt32emu.net` package) |
| `CovertAction.Launcher` | all of the above | `Game` (the parts started as method calls), `PracticeMessage`, `GameFolderFiles` (the game's file calls on the player's folder and a folder for what the game writes), `SoundDriverChoice` (the driver of the set-up's sound letter, on the host's sound device) |
| `CovertAction.Host.Headless` | Platform, Content, Launcher | `HeadlessHost`; a command-line program for scripted runs |
| `CovertAction.Host.Sdl` | Platform, Content, Launcher | `SdlHost`, `SdlNative` (the SDL 3 bindings), `SdlAudioOutput`, `SdlKeyMap`; the program `CovertAction` |

**Core or Presentation?** A function belongs to Core when neither it nor anything it calls reaches a
boundary; otherwise to Presentation. The original mixes rules and drawing freely inside functions, and one
C# method reconstructs one original function, so the split follows what a function *does*, not what it is
called. A Core function has no boundary calls in its vectors by construction. Both layers are closed: only
`Content` (and the hosts) may open a file; `Msc`, `Core`, `Platform`, `Persistence`, `Presentation` and
`Drivers` use no `System.IO`, clock, console, thread or host random generator, and keep no assignable static
field. Time, keys and the window reach the drivers only through `IHost`.

**One program, typed hand-off.** The original's modules exchange everything through a 12000-byte block the
launcher owns (the reconstructed `state.h`). Here a module is an object that `Launcher.Game` creates and calls,
and the hand-off is a typed `SharedState` passed to it and returned: the question and answer bytes, the campaign scalars, the message
text, and (with GAME/FINAL) the campaign tables. Each module keeps its own typed copy of the campaign as
the original does, and its state-exchange functions (GAME aebb/aad4, TAC cbb9/c7d2, ...) are ported like any
other function: that is what keeps bugs such as CA-1 reproducible. `SharedBlockCodec` writes a
`SharedState` as the original's exact bytes, for the oracle and for saved campaigns (a save starts with
bytes 0x10..0x3ff of the block).

**Blocking control flow is kept.** The original waits in loops (`read_key`, `wait_vsync`). The ports keep
those loops and block inside boundary calls; the drivers keep the host's window alive from inside those
calls (`HostLink`). No async rewrite.

## 2. 16-bit C semantics (`CovertAction.Msc`)

The original was compiled by Microsoft C 5.1, small model: `int` is 16 bits. C# computes every `short`
expression in 32 bits. A port gives the original's result when each value is narrowed where the original's
was. The helper layer is small and heavily tested (`C16Tests`, `MscRandomTests`, `CStrTests`: 141 tests,
written from the instructions of the library routines in the game itself).

| C | C# | Notes |
|---|---|---|
| `int` | `short` | fields, parameters and results of ported functions always use the mirror type |
| `unsigned` | `ushort` | |
| `char` | `sbyte` | MSC's `char` is signed; a char read from a text buffer is promoted with `C16.CharToInt` |
| `unsigned char` | `byte` | |
| `long`, `unsigned long` | `int`, `uint` | DX:AX; `C16.Long`, `LoWord`, `HiWord` |
| `char buf[n]`, strings | `byte[]` | never .NET strings; `CStr` routines; literals with `CStr.Literal` |
| near pointer | a reference or an index | never a number, except where the original computes with the address |
| far pointer | not represented | the shared block and drivers are objects |

**The rule.** `+ - * & | ^ ~ <<` give the same low 16 bits whether computed in 16 or 32 bits, so an
expression made only of them is computed in C# `int` and narrowed once, where it is stored or passed:
`x = C16.Int(a * b + c)` (or a `(short)` cast; overflow checking is off for the whole solution). Every
operation that looks at the upper bits needs its operands narrowed first, and has a helper where the corner
cases differ from C#:

* `/` and `%`: `C16.Div`, `Rem`, `UDiv`, `URem`. Truncation is the same as C#'s; division by zero and
  -32768 / -1 raise `DivideErrorException` (the original stops with a divide error).
* `>>`: `C16.Sar` for `int` (sign copied), `C16.Shr` for `unsigned`; a variable `<<` goes through `C16.Shl`
  (the 8086's count rule: the count is not reduced to five bits; docs/faithfulness/DECISIONS.md). Constant shifts below 16 may use the C# operators on
  narrowed operands.
* comparisons: narrow both sides first. An `int` compared with an `unsigned` is an unsigned comparison in
  C: cast both to `ushort`.
* widening to `long`: narrow first, then widen the way the original does (`(long)x` signed, an `unsigned`
  through `ushort`).
* `long` arithmetic: `C16.LMul`, `LDiv`, `LRem`, `LShl`, `LShr` are the compiler's run-time helpers
  (`__aNlmul` ...). `LDiv` does not fault on -2147483648 / -1; the long shifts take counts up to 255.
  The game's dice are `(long)n * rand() >> 15`: `GameLibrary.RandomBelow`.
* `abs`, unary minus: `C16.Abs`, `C16.Neg` (-(-32768) stays -32768).

**Plain C# `int` is allowed** for a local whose range is bounded by the code itself well inside 16 bits:
a loop counter with a constant or state-bounded limit, a count of at most a table's length, an index. Say so
in a comment when it is not obvious. State fields, parameters and return values never are.

**`rand()`.** `MscRandom`: `state = state * 214013 + 2531011`, result bits 16..30, state 1 at start,
`srand` stores the unsigned 16-bit seed and clears the high word (read from CODE 0000:3640/3652; the
library source is not among dos/msc510). Its state is a field of the module state because it lives in the
module's data segment (`holdrand`, CODE DS:1982): an oracle test sees every draw. The number and order of
draws is behaviour.

**Strings.** `CStr.Strlen/Strcpy/Strcat/Strcmp/Strncmp/Strncpy/Strupr/Itoa` follow the library's
instructions, including the order bytes move in (it decides overlapping copies) and the exact return values
(-1/0/1, bytes unsigned). They work on spans and throw `MemoryOverrunException` instead of leaving the span.
Where the original relies on running past a buffer (a text without end mark), the port hands the routine the
bytes the original really reaches, in the data segment's order, and marks it `// C:`.

## 3. State and the state codec

Each module's global variables are a typed state object in Core (`CryptoState` for CODE): one property per
variable, named as in the module's header, typed by the table above, arrays with the original's length.
Records the modules share are shared types (`RasterPort`, `Registers`, the campaign records). A ported
function takes the state it uses (static rule functions in Core, small classes holding state and
boundaries in Presentation).

`StateLayout<TState>` (Persistence) is the codec: a table of fields (offset, kind, getter, setter, name with
the original coordinate). `Load(image, state)` reads all fields from the original's bytes;
`Store(state, image)` writes them **over** an image and leaves every other byte alone. That makes the round
trip byte-identical from the first ported function on, before the whole data segment has a meaning.
Layouts exist for the hand-off block (`SharedBlockCodec`), for each module's DGROUP (`Dgroup.CodeDgroup`,
offsets from the headers and `symbols/names.txt`) and, with GAME/FINAL, for the save files. A variable joins
its layout with the first ported function that uses it. Fields may not overlap; a variable the original
views two ways (the low byte of a word) is one field.

**Start values and texts come from the player's game.** A state class has no built-in values. The
initialised data of the original program (texts such as "PAUSED", colour tables, the joystick direction
table, the start values of the ports) are fields of the state like any variable, and the start state is
loaded through the same layout from the data segment of the player's own executable (`CryptoState` from
CODE.EXE; the loader that unpacks the executable belongs to Content and is not written yet). The C# source
therefore holds no game text, and a fuzzed oracle case that alters a text is still inside the model.

**Pointers.** A near pointer variable is a reference in the state (`Rp1` is a `RasterPort`, pointing at
`Rp1X` or `RpBackX`); `StateLayout.Pointer` translates between the reference and the addresses of the
objects it may point at. A pointer argument of a function is a `Span<byte>` or a reference; the layout can
tell a test the original address of either (`AddressOf`) and the state memory at an address (`BytesAt`).
An address that is none of the known objects is outside the model (`OutsideModel`).

Not modelled, by decision: the stack (it shares the data segment; the oracle masks it), the C library's
private data, the near heap, and the working data of MicroProse's assembly graphics library
(`CodeDgroup.GraphicsLibraryScratch`), which lies beneath the `IGraphicsDriver` boundary.

## 4. Boundaries, drivers and hosts

### The game's boundaries (`CovertAction.Platform`)

Everything the game's code reaches outside itself is a call of a small interface that a test can record. The
cut is the call the original's C makes: a routine of frame 0000 or of the C library. Where that routine is a
driver entry, the member carries its name and arguments. Where the C reaches past the drivers into the PC (a
hardware port, a software interrupt), the member is the **named service** the game wants, with typed values:

| Original | Interface | What it is in the original |
|---|---|---|
| `copy_rect`, `recolor_rect`, `draw_string`, `char_width`, `set_color_maps`, `set_palette`, `show_page`, slot 0208 | `IGraphicsDriver` | far-call thunks into the loaded graphics driver |
| `fill_rect`, `draw_line_port` | `IGraphicsDriver` | MicroProse's assembly library in front of the driver: edge tables, the line clipper |
| `int86(0x10)` with AH = 5 | `IGraphicsDriver.SelectDisplayPage` | the video BIOS |
| `play_sound` | `ISoundDriver` | a thunk into the sound driver |
| `no_key_waiting`, `read_key_raw`, `calibrate_joystick`, `read_joystick_axes` | `IInputDriver` | thunks into the input driver MISC.EXE |
| `inp(0x201)`; `kbhit` | `IInputDriver.ReadJoystickButtons`, `Kbhit` | the game port; a DOS call |
| `int86(0x33)` with AX = 0..8 | `IMouseDriver`: `Reset`, `ShowCursor`, `HideCursor`, `GetStatus`, `SetPosition`, `ButtonPresses`, `ButtonReleases`, `SetHorizontalRange`, `SetVerticalRange` | the mouse driver |
| the loops on `inp(0x3DA)`; `time` | `IGameTimer.WaitForFrame`, `Time` | the display's retrace signal; the C library over DOS |
| `find_file_on_disk`; `_dos_getdrive` | `IFiles.FindFileOnDisk`, `CurrentDrive` | MicroProse's assembly (a search of the floppy drives); a DOS call |
| `exit` | none: the C# throws `ProgramExitException`, which whoever started the part catches | the C library's shutdown |

A member is added with the first ported function that calls it. `Presentation.Boundaries` bundles the six
for a part. The register blocks the original keeps as variables for its interrupt calls (`mouse_regs`,
`bios_regs`) are not state: they are working memory of one call (`CodeDgroup.Scratch`).

### No emulator

A binding requirement of the user (13 November 2026). The product contains no DOS machine in any form:

* no x86 execution and none of the original's code: the drivers are not loaded or run, they are written
  again in C#;
* no DOS or BIOS service layer: `CovertAction.Platform` has no port, interrupt, register or segment
  (`LayerTests.TheBoundaryHasNoPortsInterruptsOrRegisters`); input arrives as key codes, files through
  `IFiles`, time through an injected timer;
* no emulated video memory: the game's screen is 320x200 palette indices and 256 colours, owned by the
  game's drawing code (`McgaGraphicsDriver`), which a host only shows;
* no third-party library in the product (`LayerTests.TheProductLinksNothingButDotNetAndItself`) but one:
  `CovertAction.Audio.Mt32` links Munt's mt32emu (its NuGet binding and native library, as in the Darklands
  Restoration Project), an emulation of the MT-32 module the Roland driver's MIDI plays on, not of the PC; and
  native code of our own only in the SDL host (`OnlyTheSdlHostCallsNativeLibraries`).

The oracle (the original run in the Unicorn emulator) stays in `tools/` and in the tests' vectors; nothing in
`src/` knows it. Reading a table, a text or a start value from the player's executables in `Content` is data
loading, not emulation: the bytes are decoded into typed state and never executed.

Two things the original's 16-bit arithmetic makes *visible* are kept as documented rules of the drawing code,
because the game's own first fill relies on them (docs/original-bugs.md CA-24): a pixel number from 64000 to
65535 is below the last row and is not drawn, and a fill of row 200 lands in row 128.

Sound follows the same rule. The tunes' and effects' logic is C# code (`CovertAction.Audio`), one class per
original driver, reading only the sounds from the player's driver file (`Content.SpeakerDriverFile`, `Content.AdlibDriverFile`,
`Content.RolandDriverFile`); a PC-speaker voice, the OPL2/OPL3 synthesiser of the Darklands Restoration Project (user
decision of 13 November 2026), and Darklands' MT-32 emulation (Munt's mt32emu with the player's ROMs, for the MIDI
the Roland driver sends) turn what it sets into PCM for the host's `IPcmOutput`. The device's frames are the clock:
`ClockedSoundDriver` runs the driver's timer interrupt where the timer's cycles fall among them (D-19). The original's
sound driver programs are not run. So far the PC speaker (ISOUND.CA), the Tandy chip (TSOUND.CA), the AdLib card (ASOUND.CA) and the Roland
MT-32 (RSOUND.CA; the AdLib driver without ROMs, D-20) play; Custom is silent (D-7, D-18).

### The game's drivers (`CovertAction.Drivers`)

The implementations of the boundaries that the game itself brings, all plain C#:

* `McgaGraphicsDriver`: what MGRAPHIC.EXE (the 256-colour driver, chosen in DECISIONS.md) draws, routine by
  routine, read from its instructions and proven against it pixel by pixel (section 5, "Driver oracle"). It
  owns the screen and the off-screen pages, the 256 colours and the fonts (the player's FONTS.CV through
  `Content.FontSet`). `FillRect` and `DrawLinePort` include MicroProse's library routines in front of the
  driver (`LineClipper`).
* `KeyboardDriver`, `GameTimer`: keys and frames from the host (`HostLink` shows the screen and keeps the
  70 Hz pace of the original's display mode wherever the game waits).
* `NoMouseDriver`, `SilentSoundDriver`, `GameFolderFiles`: stand-ins and the one-folder file lookup.
* `HostMouseDriver`: the mouse driver's services on the host's pointer (`IInput.Pointer`), only when the player turns
  the mouse on (`input.mouse`, D-630; docs/MOUSE-SUPPORT.md); otherwise the game gets `NoMouseDriver`, as the original
  assumes.

A driver routine cites its original like any function: `// orig: MGRAPHIC 0000:107a draw_string driver`. The
drivers have no symbol file, so the routines the C# reconstructs are listed by hand in
`docs/source-map/additional-entries.csv` (status `driver`).

### Hosts

A host is the program around the game: it shows the picture, delivers key presses, has a sound device and a
clock. It implements `IHost` and nothing else of the game; the drivers ask it, the game's code never does.

* `CovertAction.Host.Sdl` is the program players start: SDL 3 (window and presentation, keyboard, audio)
  through the project's own bindings, reused from the Darklands Restoration Project (docs/REUSED.md). It is
  referenced by no project (`LayerTests.NothingReferencesTheSdlHost`), so another host can replace it without
  touching the game.
* `CovertAction.Host.Headless` implements the same interface without a window: keys from a script, time
  that passes as fast as the game asks, the last picture kept. Tests and scripted runs use it.
* No part of the game references a host (`LayerTests.NoPartOfTheGameReferencesAHost`).

Keys reach the game as the codes it has always read: the key's number in the high byte and its character in
the low byte (`HostKeys`), which the game's own `read_key` folds into its key codes.

Dropped, not ported (DOS housekeeping with no behaviour in one program): C start-up and exit code, the INT
24h critical-error handlers, the driver loader and `attach_driver_segment` (D-2), the driver-call
trampolines, the library's port and interrupt routines. Each has its reason in
`docs/source-map/not-ported.csv`.

## 5. Oracle test contract

The generator (`tools/oracle`, README there) runs one original function in an emulator from a captured or
fuzzed state. Vectors: `build/oracle/<MODULE>/<label>.json`, one file per function (label
`sub_<module>_<frame>_<offset>`; files starting with `_` are the generator's tables).

```
{"module","label","name","call":{...},"segments":{"psp","load","dgroup","dgroup_size","shared"},
 "cases":[{"id","origin","realistic":true,"args":[int16...],
           "dgroup_in":"<base64, the whole 64 KiB data segment>","shared_in":"<base64 12000 bytes or null>",
           "inputs":[...],"result":{"ax","dx"},"dgroup_out":"<base64>","shared_out":...,
           "calls":[{"target","args":[n or {"ptr":n,"str":b64} or {"ptr":n,"mem":b64}],"ret":n,"out":b64}],
           "stack_mask":[lo,hi],"regs_in","regs_out","far_in","far_out","exit"}],
 "ignore":[[lo,hi],...]}
```

The generator stops where execution leaves the module: driver thunks (by name), interrupts (`int21.0b`),
ports (`inb`), and the library routines on its cut list (`_time`). Pointer arguments of boundary calls are
captured at the time of the call (every drawn text, palettes, colour tables).

The C# side (`tests/CovertAction.Oracle.Tests`):

* `Harness/OracleVector.cs`: the loader. Finds the vectors of a function by its name (reading only the
  head of each file), pairs each call with the scripted input it consumed, keeps `stack_mask` and `ignore`
  as ranges not compared, reads `realistic` and the optional fields.
* `Harness/RecordingBoundaries.cs`: one recording fake behind all boundary interfaces. Each call of the C#
  must be the next recorded call: same target, same argument words; it returns the recorded answer. A
  pointer argument is compared by address (the layout says where a state buffer or port lies in the
  original's data segment) and by content where the generator captured it. Where the C# boundary is a
  routine above the generator's line, the fake recognises the calls beneath it: `fill_rect` as four driver
  slots (page, colour, first and last row are checked; x and width are not visible there), `draw_line_port`
  likewise (colour, and the end points when nothing is clipped), `find_file_on_disk`, `kbhit`,
  `_dos_getdrive` and `exit` as their DOS calls.
* `Harness/OracleRun.cs`: `ForEachCase(module, name, body)` and `AssertImage`.
* `CodeOracleTests`: one theory case per function of CODE; a table says how each is called from a case.

Rules of comparison:

* A difference in a modelled field fails, naming the field. A byte the original changed outside the
  modelled fields, the stack mask and the ranges a test names with a reason fails too: the function wrote a
  variable the state lacks.
* Without vectors the test is **skipped** (`[SkippableFact]`), never silently passed.
* **Realistic cases are strict.** A case with `"realistic": true` (a state of a real session, or a fuzzed
  one whose changed variables stayed inside the model's ranges, `tools/oracle/oracle_config.py`) must be
  reproduced: any difference or exception fails the test. A case that is not realistic is run too, but a
  difference or exception only counts it as outside the model.
* **Outside the model, with a stated reason.** Two conditions take a case out whatever its flag, each
  raised as an `OutsideModelException` with its reason: a pointer variable that points at no object of the
  model, and a `fill_rect` of more than 220 rows or from a negative row, which makes the original's assembly
  library overrun its edge tables (docs/original-bugs.md CA-19). Each test prints its tally and these reasons.
* Segment values inside data (`segments`): a test that meets a far pointer compares it through the vector's
  table or leaves the word out, with the reason.

State today, CODE: 48 functions, 195 cases: 193 agree with the original (among them eight whole sessions of
`DoCode`), 2 are outside the model with a stated reason (a fuzzed cipher that makes the original read an
uninitialised local, D-3; a file name longer than its buffer, CA-21). No CODE vector shifts by more than 31
(the one count the oracle's emulator and the 8086 treat differently, DECISIONS.md).

**Named services against recorded machine traffic.** The original's recorded runs contain port reads and
interrupts where the C# calls a named service (a frame wait, a mouse query, the current drive). The tests'
fake (`RecordingBoundaries`) takes, for each service call, the port reads or the interrupt the original made
for it and compares the registers that carry the service's values. The mapping lives in the tests only.

### Driver oracle

`tools/oracle/driver_vectors.py` records every entry of the original drivers that CODE reaches
(`build/oracle/MGRAPHIC`, `MISC`): the driver's own instructions run in the emulator from a captured state,
and the vector holds the screen, the pages, the colours, the driver's variables and the drawing port before
and after. `McgaOracleTests` puts the same picture and variables into `McgaGraphicsDriver`, makes the same
call, and compares every pixel of every page, the colours and border, the driver's variables, the result and
the port; a byte the original changed that the C# has no counterpart for fails the test unless it is on the
list of the driver's working memory. `MiscOracleTests` does the same for the two keyboard entries.

State today: MGRAPHIC 145 of 146 cases over 13 of 14 entries agree (text, character widths, copies,
recolouring, palette, page show, fills, lines, colour, page and port selection). The one case of the mode
switch (slot 0208) cannot be used: the oracle's stand-in BIOS answers "which mode?" with a changed BL, the
driver takes that for a display without the mode and ends the program. MISC: 10 of 10. The library routines
in front of the driver (`fill_rect`'s edge tables, the line clipper) have no vectors of their own: the fills
and lines they hand to the driver are proven, they themselves are held by hand-derived unit tests
(`LineClipperTests`, `McgaGraphicsDriverTests`).

## 6. Porting conventions

* **One C# method per original function**, named after it: MicroProse's name as it is (`DoCode`,
  `CheckForDisk`, `GoodBye`; fields `CODEdiff`, `Rp1`, `NoHi`), our own lower-case names in C#
  capitalisation (`check_solution` -> `CheckSolution`). `TraceabilityTests` checks the name against
  `symbols/names.txt`. Parameters and state fields keep the header's names in C# capitalisation.
* **Traceability comment** on the line above: `// orig: CODE 0000:18a4 check_solution
  (src/c/code/code_0000_18a4.c) matched; tests: CryptoRulesTests, CodeOracleTests`. Full convention in
  CONTRIBUTING.md.
* **Must not change:** the order and number of random draws; the order of boundary calls and of writes
  that another function can observe; integer widths, signedness and truncation; evaluation order where an
  operand has a side effect (MSC evaluates call arguments right to left); every branch of the C, including
  dead and odd ones (`// DORMANT:`); what is stored where (a value kept in a global stays in the state even
  if only one function uses it); reads of stale or uninitialised values the original performs (CA-4, CA-7):
  reproduce them from the state, mark them `// BUG:` or `// C:`.
* **May change:** `goto` and flag loops into structured loops; local names; a temporary removed or added;
  splitting a long function into private helpers with telling names (a private method is covered by the
  comment of the method that calls it; a non-private new method is registered in `docs/source-map/new-code.csv`);
  C idioms into their C# equivalent when the helper layer guarantees the same result.
* **Twins.** The original's programs were linked from one library, so the same function is in several of
  them. It is ported once only when the copies are provably the same code: `tools/shared_functions.py` compares
  the copies' instructions and writes `docs/source-map/shared-functions.csv`; one method stands for the copies
  of one group there, and its comment lists every one (`clamp`: all six programs). A copy in another group is
  other code under the same name and gets its own method (TAC's `read_key`, CHASE's `random_below`, which
  returns a long). The shared routines live in `Core/Shared` and `Presentation/Library` and work on any part
  through `ILibraryState`.
* **Assembly-only routines** (`symbols/assembly.txt`, statuses `asm` and `thunk` in the source map):
  driver thunks and trampolines become boundary interface members; C run-time start-up, DOS wrappers and
  long helpers are replaced by .NET or `C16` and listed as such; MicroProse's hand-written library that is
  game behaviour (the picture decoder with its LZW and RLE quirks, CA-11; INTRO's string routines) is ported
  from the disassembly as a method with status `asm`, "checked against asm" in its comment, and oracle
  vectors like any C function (the generator supports register conventions: `regs_in`/`regs_out`).
* **Original bugs.** Reproduced by default. A bug with a switch reads
  `if (behaviour.FixRaidGuardCountInCityCounter) { fixed } else { exact }` with
  `// BUG: CA-1 ... OriginalBehaviour.FixRaidGuardCountInCityCounter` above it; the rule takes the
  `OriginalBehaviour` as a parameter. A quirk without a switch has the `// BUG: CA-n` comment only. A new
  find goes into `docs/original-bugs.md` first.
* **Out of the typed model.** An index or a copy that leaves its table throws; it is never clamped
  silently. If real play reaches it, model what lies behind (the neighbouring fields) and say so.
* **Texts and tables are state.** A string constant or initialised table of the original is a field of the
  state, loaded from the player's executable; the port names it (`state.StrPaused`) and never spells it.
* **Empty and dormant functions are ported too.** A function the original has but never calls, or one that
  is empty in a module (CODE's animation hooks), keeps its method, marked `// DORMANT:` or documented as
  empty: the source map then accounts for every function, and the callers read like the original.

## 7. Order of work

In short:

1. **CODE (pilot): ported and playable.** Every function of the module has a C# member or a reason in the
   source map, and every one with vectors agrees with the original. It runs as a practice session from the
   SDL host (section 8) on the game's own drawing code, keyboard and timer.
2. **Shared library pieces**, as the next modules meet them: the PAN animation player, picture decoding
   (Content) and the driver's picture and sprite routines, the mouse, the joystick, sound (the tunes' and
   effects' logic in C#, on an OPL2 synthesiser and a PC-speaker voice).
3. **GAME and FINAL**: the campaign records (`campaign.h`) as Core types with their layouts, the shared
   tables, save and load, the plot engine, then the screens. Needs captured states for both (the oracle
   has CODE only).
4. **BUG**, 5. **CHASE**, 6. **TAC**: each minigame behind the typed hand-off, with its bugs' switches.
7. INTRO and the launcher's flow (title, set-up, copy protection) as host-level screens; hosts, settings,
   sound drivers.

## 8. Building and checking

```
cd csharp
dotnet build                    # warnings are errors; public members need XML comments
dotnet test                     # unit, layout, architecture and oracle tests (oracle: skipped without vectors)
python -B tools/source_map.py   # after adding or moving a // orig: comment

# play the cryptography practice (the player's game folder; --level 0..3)
dotnet run --project src/CovertAction.Host.Sdl -- "<game folder>" --level 0
# play a practice car chase
dotnet run --project src/CovertAction.Host.Sdl -- "<game folder>" --practice chase
# the same without a window: scripted keys ({wait 50}: fifty moments without a key), the last screen written as a picture
dotnet run --project src/CovertAction.Host.Headless -- "<game folder>" --keys "{f1}e{down}" --out screen.bmp
dotnet run --project src/CovertAction.Host.Headless -- "<game folder>" --practice chase --keys "{wait 3}{enter}{wait 3}{enter}{wait 3}{enter}{wait 300}" --out chase.bmp
```

The game folder may also be named by the environment variable `COVERT_GAME_DIR`. The game's files are read
from it at run time (CODE.EXE for the part's texts and start values, MGRAPHIC.EXE for the colours, FONTS.CV,
TEXT.DTA, WORLDn.DTA); none is in the repository.

| Test project | Checks |
|---|---|
| `CovertAction.Msc.Tests` | the C semantics helpers against the library's instructions |
| `CovertAction.Core.Tests` | rules on hand-made states |
| `CovertAction.Persistence.Tests` | layouts: offsets, byte-identical round trips |
| `CovertAction.Architecture.Tests` | layers, closedness, no static state, no emulator, hosts apart from the game, traceability, registers, source map, reused files, no game bytes |
| `CovertAction.Oracle.Tests` | ported functions and the game's drivers against the original's recorded runs; the harness itself on made-up vectors |
| `CovertAction.Drivers.Tests` | the drawing code on a made-up font, the line clipper, keyboard and timer on the headless host |
| `CovertAction.Session.Tests` | whole sessions against the original: the sessions `tools/session/original_side.py` recorded (the part's program run whole in Unicorn) played on the headless host, picture, colours, hand-off block and sounds compared at every key poll (docs/SESSION-PROOF.md); skipped without recordings |
| `CovertAction.Launcher.Tests` | the readers of the game's files on made-up files, and on every picture, archive and text file of the player's game against the project's verified decoders (`python -B tools/reference_content.py` writes the reference into build/; skipped without it); the practice message; whole sessions on the headless host |
