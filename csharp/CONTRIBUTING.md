# Contributing to the C# rewrite

Rules for all C# code in this folder. ARCHITECTURE.md says how the rewrite is built; this file says how
a change must look. `dotnet build` and `dotnet test` hold what a machine can hold of it; the last section
lists which rule is checked by what.

## 1. Readable code first

The code must be human readable: easily read and easily modified by a developer who knows C# but neither the
game nor the original's C. This rule comes before the others; where it seems to clash with faithfulness, keep
both: faithfulness is held by the oracle tests, not by the shape of the code.

* Idiomatic, plain C#. Not C transliterated: no pointer-style indexing through raw buffers, no C-string
  helpers over byte spans, no raw offsets, no magic numbers in game code where a typed model, a `string`, a
  list, a named constant or an enum says it.
* State is typed and named. A flag is a `bool`, a set of option bits a `[Flags] enum`, a text a `string`
  (one byte per character, Latin-1), a table of letters a `char[]`. A 16-bit variable of the original that is
  kept between calls is a `short` field; a local, a parameter and a boundary argument is an `int`.
* `C16` and `CStr` only where exact 16-bit or C-library behaviour changes a result (a shift by a variable
  count, a division, a counter that wraps), each use with a `// C:` comment that says what would go wrong
  without it.
* Byte layouts, addresses and offsets only in `CovertAction.Persistence` (and in tests that read vectors).
* Split long functions into private helpers with telling names; name every constant after what it means in
  the game (`CellWidth`, `ScreenGreen`), not after its value.
* Comments explain the game and the why (section 2).
* No DOS machine: never a hardware port, a software interrupt, a processor register or a memory segment in
  product code. What the original gets from the PC is a named call of a boundary interface (ARCHITECTURE.md,
  "No emulator").

## 1a. KISS, DRY, single responsibility

* Plain classes and records. No framework, no dependency container, no service locator, no global
  instance, no assignable static field. State is passed in; chance is an `IRandomSource` passed in.
* A rule is a small function over state. A type owns one concept.
* One implementation per rule. Extract a shared mechanism only where the original provably shares it (the
  same function compiled into several modules, the same code on the same data): then one method, whose
  comment lists every original copy. Two original functions that merely look alike stay two methods.
* Respect the layers (ARCHITECTURE.md, section 1). The game reaches the outside world only through the
  interfaces of `CovertAction.Platform`; byte layouts live only in `CovertAction.Persistence`.
* No game bytes: no file of the game, no table copied out of an executable, no oracle vector. Constants the
  code needs (a threshold, a colour number) are written as code with a comment saying what they are.

## 2. Comments

Write for a reader who knows C# and not the game.

* Every class and every public or internal member has an XML comment (`///`): what it is **in the game's
  terms**, what parameters and results mean, their units and ranges (minutes of game time, palette index,
  pixels of the 320x200 screen, 0..3 difficulty).
* Inside a method, comment the step that is not obvious: a magic number, a unit conversion, an original
  quirk, why a narrowing or a helper of `C16` is there. Do not restate the code.
* Names from the original that mean nothing by themselves (`CODEdiff`, `NoHi`, `Rp1`) get the explanation
  in their comment. The name stays.
* Keep the three kinds of comment apart: `///` explains, `// orig:` traces, `// C:`, `// BUG:`,
  `// DIVERGENCE:`, `// UNCERTAIN:`, `// DORMANT:` mark.

## 3. Traceability

Every reconstructed operation says where it comes from, how sure the source is, and which tests cover it.
An address proves nothing by itself: the tests named in the comment do.

### Coordinates

| Kind | Written as | Meaning |
|---|---|---|
| Function | `GAME 00f7:5035` | module, frame and offset in load-image paragraphs, as in the reconstruction (`/* place: game 00f7:5035 */`) and `symbols/names.txt` (`sub_game_00f7_5035`) |
| Data | `CODE DS:1d72` | offset in the module's data segment (DGROUP), as `D_code_1d72` in `symbols/names.txt` |
| Shared block | `SHARED+0x4e` | offset in the 12000-byte hand-off block (`src/c/include/state.h`) |
| Record type | `struct RasterPort (src/c/include/gfx.h)` | a type of the headers; a field as `+0x0c` |
| Driver thunk | `CODE 0000:0198 draw_string thunk` | the routine's name and its slot in frame 0000 of the module |

Module names in capitals: COVERT, INTRO, FINAL, GAME, TAC, BUG, CHASE, CODE. Hex digits in lower case.
Unlike in the Darklands restoration there is no neutrality constraint: the reconstruction's own files (`src/c`) and
labels are cited by name. The reconstruction is not published; the author keeps it in `local/src/c`, which
`TraceabilityTests` and `tools/source_map.py` read when `src/c` is absent and skip without.

### Source status

| Status | Meaning |
|---|---|
| `matched` | reconstructed readable C that compiles to the original bytes (MSC 5.1) |
| `lib` | a routine of the Microsoft C 5.1 library, read from its instructions in the game |
| `thunk` | a named routine of frame 0000 below the compiler's output: a far-call thunk into a loaded driver, or MicroProse's assembly library; no C |
| `driver` | a routine of a driver program (MGRAPHIC.EXE, MISC.EXE), reconstructed from its instructions in `CovertAction.Drivers`; listed by hand in `docs/source-map/additional-entries.csv`, because the drivers have no symbol file |
| `asm` | other hand-written assembly (`symbols/assembly.txt`): reconstructed from the disassembly; say `checked against asm` |

### The `// orig:` comment

One per reconstructed operation (method, or class for a data structure), on the line above the
declaration, after the XML comment:

```csharp
// orig: CODE 0000:18a4 check_solution (src/c/code/code_0000_18a4.c) matched; tests: CryptoRulesTests, CodeOracleTests
// orig: CODE 0000:2aa2 clamp (src/c/code/code_0000_2aa2.c) matched; also GAME 00f7:d504, TAC 00e8:d21c; tests: GameLibraryTests, CodeOracleTests
// orig: CODE 0000:3652 rand (SLIBCR.LIB rand.c) lib; also FINAL 0100:e8da; tests: MscRandomTests
// orig: CODE 0000:0198 draw_string thunk; also FINAL 0000:0224
// orig: CODE DS:1982 holdrand (the long the two routines below share)
// orig: SHARED+0x00..0x65 struct shared_state (src/c/include/state.h)
```

Function form: `MODULE ffff:oooo name (source) status; also <other copies>; tests: <test classes>`.
The name is the one in `symbols/names.txt` without the leading underscore; the source is the `src/c` file
of the reconstruction for `matched` (required), the library member for `lib`. `tests:` is required for `matched` and names test
classes that exist. Every coordinate after `also` maps to the same method in the source map.

### Markers

Each on its own line where it applies:

* `// C: ...` a point of C the port had to honour: 16-bit wrap-around, signed/unsigned comparison or
  division, integer promotion, right-to-left argument evaluation (the order of random draws), aliasing
  through pointers, a side effect inside an expression, an uninitialised local or stale global the original
  reads.
* `// BUG: CA-n ...` an original bug or quirk, by its entry in `docs/original-bugs.md` (project root); when
  it has a switch, name it: `OriginalBehaviour.FixRaidGuardCountInCityCounter`.
* `// DIVERGENCE: D-n ...` the C# differs on purpose; what the original does instead. Entry in
  `docs/faithfulness/DIVERGENCES.md`.
* `// UNCERTAIN: U-n ...` the meaning or mapping is a guess. Entry in `docs/faithfulness/UNCERTAIN.md`,
  with what would settle it.
* `// DORMANT: ...` a branch the original has and nothing reaches: kept, with why it is unreachable and
  under which state it would run.

### Rules

1. Every branch of the C is preserved, dormant and odd ones included. A branch that truly cannot be
   expressed is a `DIVERGENCE` with its condition.
2. Review explicitly: arithmetic width and sign, evaluation order, aliasing, side effects, the order of
   random draws and of boundary calls. Note each case that matters with `// C:`.
3. Never simplify silently: an omission is a `DIVERGENCE`, a guess is `UNCERTAIN`.
4. For `asm` and `thunk` sources, read the instructions (disassemble the module's EXE at
   `ffff:oooo`) and say `checked against asm`.
5. Use MicroProse's original name where `symbols/names.txt` has one (see `docs/amiga.md`), unchanged; our
   own names in C# capitalisation. Explain the name in the XML comment.
6. A method of `CovertAction.Core` or `CovertAction.Presentation` that reconstructs nothing (a helper
   split off a long function, glue between modules) is new code: list it in
   `docs/source-map/new-code.csv` with the reason.

### Registers and the source map

| File | Content |
|---|---|
| `../docs/original-bugs.md` | the original's bugs and quirks, CA-n; the handling of each |
| `docs/faithfulness/DIVERGENCES.md` | every deliberate difference, D-n |
| `docs/faithfulness/UNCERTAIN.md` | every guess, U-n |
| `docs/source-map/original-functions.csv` | every original function: coordinate, name, source, status, C# member or `not ported`, and for a function that will not be ported the reason |
| `docs/source-map/not-ported.csv` | the reasons: rules by module and coordinate, name or status (C run-time, driver loading, library internals behind a boundary call) |
| `docs/source-map/new-code.csv` | non-private methods of the game (Core, Presentation) that reconstruct nothing, with the reason; private helpers are covered by the comment of the method that calls them |
| `docs/source-map/additional-entries.csv` | places of the original the project's symbol files lack: driver routines and unnamed assembly routines the C# reconstructs |
| `docs/faithfulness/DECISIONS.md` | questions the original leaves open and how they were decided, by whom and when (the 8086 as the processor, the MCGA driver) |
| `docs/source-map/shared-functions.csv` | generated by `tools/shared_functions.py`: the functions several programs contain, and which copies are the same code; the only ground for one method standing for several copies |
| `docs/REUSED.md` | files taken from the Darklands Restoration Project; such a file says so in its first line and keeps its own comments (the traceability tests leave it out) |

`original-functions.csv` is generated: run `python -B tools/source_map.py` after adding, moving or
renaming a `// orig:` comment or changing `not-ported.csv` (it reads the reconstruction's `src/c`, `symbols/`, the comments and
the reasons; no game bytes). A module is complete when none of its rows is open: every function has a C#
member or a reason. `TraceabilityTests` holds the complete modules (CODE so far) to that.

## 4. Porting a function: the steps

1. Read the reconstructed C and its header; check `docs/original-bugs.md` and the data-model spec for it.
2. Add the variables it uses to the module's state class (mirror types, header names, XML comments with
   meaning and coordinate) and to the module's layout in `CovertAction.Persistence/Dgroup`. A text or table
   of the original's initialised data is such a variable: never spell it in the C#.
3. Write the method in Core (no boundary reached) or Presentation, one method for the one function, with
   the `// orig:` comment and the markers. Add boundary interface members it needs, with their coordinates.
4. For a rule (Core), write unit tests on hand-made states, with expected values worked out from the C,
   not from the C#. They reach the branches the vectors do not.
5. Add the function to the module's oracle test (`CodeOracleTests`: one line saying how it is called from
   a recorded case). It must agree on every realistic case; look at each case reported as outside the model
   and make sure the reason is the case's, not the port's.
6. `python -B tools/source_map.py`, `dotnet build`, `dotnet test`.

## 5. What is checked, and by what

| Rule | Checked by |
|---|---|
| public types and members have XML comments | the compiler (CS1591 is an error in `src/`) |
| warnings are errors, nullable on | `Directory.Build.props` |
| layer references, no cycle | `LayerTests` |
| no file, clock, console, thread or host randomness outside Content | `LayerTests.TheGameReachesTheOutsideWorldOnlyThroughTheBoundaryInterfaces` |
| the boundary is interfaces only | `LayerTests.TheBoundaryIsInterfacesOnly` |
| no assignable static field | `LayerTests.TheGameKeepsNoStateInStaticFields` |
| `// orig:` names a real place, with the right name, source and status | `TraceabilityTests` |
| a reconstructed function names existing tests | `TraceabilityTests` |
| a reconstructed function keeps the original's name | `TraceabilityTests` |
| the source map agrees with the comments and lists all of the reconstruction's files | `TraceabilityTests` |
| every function of a complete module is ported or has a reason | `TraceabilityTests` |
| every method of Core and Presentation is a reconstruction or registered new code | `TraceabilityTests` |
| `DIVERGENCE` and `UNCERTAIN` markers and their registers match both ways | `TraceabilityTests` |
| `BUG` markers cite the register and real switches; each switch cites its entry | `TraceabilityTests` |
| no game file, no vector, no large file in this folder | `RepositoryTests` |
| behaviour equals the original's | `CovertAction.Oracle.Tests` |

Left to review, because no test can judge it: whether a comment explains, whether a function is simple,
whether a shared mechanism is really the same in the original, whether every branch of the C is there.

## 6. Commits

Do not commit game files, vectors or build output (`csharp/.gitignore`, `build/` is ignored at the root).
Commit the regenerated `docs/source-map/original-functions.csv` with the code that changes it.
