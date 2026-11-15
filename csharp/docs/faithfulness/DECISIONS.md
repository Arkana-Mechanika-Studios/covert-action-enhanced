# Decisions

Questions the original leaves open, or answers differently on different machines, and how the rewrite answers
them. Each entry says who decided, when, and what follows from it. An entry that was an open point of
`UNCERTAIN.md` keeps its old number in brackets.

## The processor is the Intel 8086/8088 (user, 13 November 2026) [was U-4]

The original's minimum machine is a PC with an 8086 or 8088. Where later processors behave differently, the
rewrite does what the 8086 does.

**Variable shift counts.** `shl`, `shr` and `sar` by the CL register shift one bit as often as CL says, 0 to 255
times; the count is not reduced to five bits as on the 80286 and later. A 16-bit value shifted left or right
(unsigned) by 16 or more is 0; an arithmetic shift right by 16 or more is 0 or -1 by the sign. `C16.Shl`,
`C16.Shr` and `C16.Sar` implement this (`C16Tests`, against one-bit shifts repeated for every count); the long
shifts of the C library (`C16.LShl`, `C16.LShr`) loop in software and never depended on the processor.

*Consequence for the oracle.* The oracle runs the original in Unicorn, which emulates a later processor and takes
shift counts modulo 32. A case in which a shift count exceeds 31 therefore cannot be checked against it: the C#
and the emulator disagree by design. Such a case is flagged and left out, not compared. In CODE one function can
get there, `joystick_button_down` (it shifts by its argument plus 4); `CodeOracleTests` sets a case aside when
that count is above 31. No CODE vector does so today. The only other variable shift of CODE, the mark test of
the menu (`1 << choice`), has counts 0..31.

**Other differences between the 8086 and later processors**, to keep in mind where code depends on them:

* `PUSH SP` stores the value SP has after the push (later processors store the value before it). Start-up code
  uses this to tell the processors apart; none of the game's own code ported so far does.
* A divide error (interrupt 0) pushes the address of the instruction *after* the failing `DIV`/`IDIV` on the
  8086, and the address *of* that instruction on the 80286 and later. The line clipper of MicroProse's graphics
  library (CODE 0000:07ac) installs its own handler (0000:0959), which adds 4 to the return address to skip the
  four-byte `IDIV` and compares the address with that of its second `IDIV`: it was written for the later
  processors. On an 8086 it would return into the middle of the following instructions
  (docs/original-bugs.md CA-22). The rewrite's clipper gives the result the handler intends (a quotient of
  +-0x7f00; DIVERGENCES.md D-5); the lines the game draws never overflow the division. An `IDIV` whose
  quotient is exactly -32768 is refused by the 8086 and accepted by later processors; the clipper follows the 8086.
* Addresses wrap at 1 MiB (there is no A20 line): segment FFFF plus an offset above 0x000F reaches low memory.
  Nothing ported so far depends on it. The original drivers count pixels in 16-bit offsets inside one 64 KiB
  segment, on every processor; the rewrite has no segments, and keeps the two visible consequences as rules of
  its drawing code (`McgaGraphicsDriver.Put`, docs/original-bugs.md CA-24).

## The game draws on the MCGA driver (13 November 2026)

The original ships four graphics drivers. The rewrite reimplements the MCGA one (MGRAPHIC.EXE: 320x200, 256
colours, one byte per pixel), which shows the game as the VGA shows it and whose memory layout is the simplest to
prove pixel by pixel. The colour tables for the CGA driver (`set_color_maps`) are passed along and ignored, as
MGRAPHIC ignores them.

## No DOS emulator in the product (user, 13 November 2026)

The game runs as plain C#: no x86 execution, no DOS or BIOS service layer, no emulated video memory, ports or
interrupts at run time. The original's drivers become C# code proven against the driver oracle in tests; input
arrives as key codes, files through `IFiles`, time through an injected timer. The oracle and its emulator stay
in `tools/` and the tests. Reading tables and start values from the player's executables is data loading and
stays. What changed for it: the boundary's `IMachine` (ports, interrupts, register blocks) was replaced by named
services (`IGameTimer.WaitForFrame`, `IMouseDriver`, `IInputDriver.ReadJoystickButtons`, ...), and the drawing
code's pages are 64000 pixels instead of 64 KiB segments. ARCHITECTURE.md, section 4, has the rules and the
architecture tests that hold them.

## The host is SDL 3, apart from the game (user, 13 November 2026)

Window, keyboard and audio come from SDL 3, as in the Darklands Restoration Project, whose bindings and host
code are reused (docs/REUSED.md). The host implements the `IHost` interfaces of `CovertAction.Platform` and is
referenced by nobody; a headless host implements the same interfaces. No part of the game references a host.

## Sound: C# logic on an OPL2 synthesiser (user, 13 November 2026)

Sound is silent for now (DIVERGENCES.md D-7). When it is written, the tunes' and effects' logic is C# code; an
emulation of the OPL2 *chip* is fine and is the Darklands Restoration Project's own synthesiser
(`Opl3.cs`, written from the chip's documented behaviour). The original's sound *drivers* are not run in an
emulated PC.

## Licence (user, 13 November 2026)

The project is under the GNU General Public License, version 3 (`LICENSE` at the repository root). Code reused
from the Darklands Restoration Project (same author, same licence) needs no relicensing; third-party parts keep
their own licences (`THIRD_PARTY_NOTICES.md`: SDL 3 under the zlib licence).
