# Covert Action (C# rewrite) 1.3.1

A small round on top of 1.3, the faithful rewrite of Sid Meier's Covert Action (1990). It is an unofficial fan project
that rebuilds the original from its own code, part by part. It reads your own copy of the game; none of the game's
files come with it. Everything new is a player option, off by default: with the options off, 1.3.1 plays exactly as
1.0 and the original.

## What's new

- **Two more pieces of the original's cut content.** Twenty chase grounds, and kitchens and bedrooms in the raids.
- **Three more Amiga pictures.** The choice of agent, the electronics workbench and Washington.
- **A deeper search of the original's leftovers.** Ten new entries in the register of its cut content.
- **Everything of 1.3 and before.** The cut content, the controls, the Amiga version, Linux and the whole faithful game.

## Getting started

1. Unpack the release into a folder of your own (not into the game's folder): the zip on Windows; on Linux the
   tar.gz, with `tar -xzf` (README-linux.txt in it says more).
2. Start **CovertAction.exe** (on Linux `./CovertAction`). If your copy of Covert Action is not found, the launcher asks
   for its folder (the one with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** (on Linux `CovertAction Launcher`) opens the settings at any time, as does Shift held
   while the game starts. The player options are on its Advanced, Cut content and Controls tabs. Its **Play** starts
   the game.

The release is one program, CovertAction.exe, with SDL 3, the MT-32 emulation and the launcher beside it, as in 1.3's
zip since it was replaced. Saves made with 1.0 to 1.3 (or with the original) load as they are. A settings file of 1.3 is read as
it is.

## What's new in detail

**Two more pieces of the original's cut content**, on the Cut content tab (docs/cut-content-options.md shows each):

- **All 20 chase grounds** (`cutContent.chaseGrounds`, D-664, CC-25): the car chase's sheet holds twenty ground
  pictures and the map generator asks for sixteen. With the option, the same draw takes all twenty: two copies of
  earlier grounds and two never seen, a striped block and a court with a plaza. The streets and places are the
  original's; only the grounds differ. The free car chase has it too.
- **Kitchens and bedrooms** (`cutContent.homeRooms`, D-665, CA-8, CC-26): the game's table of room types switches off
  the Bed Room and the Kitchen, so no building ever has one. With the option, a medium room may be either, with the
  furniture the game's own tables have for them: bed, range, refrigerator and television. The chest of drawers stays
  out: its picture points at the clue icons. The one-room ambush is unchanged. With the option off the buildings are
  the original's, checked over three hundred of them.

**Three more Amiga pictures** (the launcher's "Amiga graphics", read from your own Amiga files; AMIGA-PICTURES.md):

- The choice of agent (D-685): the Amiga's picture with its two halves exchanged, so each choice shows its person.
- The electronics workbench (D-686): the wire pieces and chips, the Amiga's strip moved one pixel into the game's cells.
- Washington (D-688): the same view of the Capitol in the Amiga's colours.
- Eight sheets the game recolours by number stay the PC's (D-687): their colours differ, not only their layout.

**The register of cut content** (docs/cut-content.md, CC-24 to CC-33): a third search of the original's sprite sheets,
sound drivers, data files and code. Among the finds: 24 tumbling poses of the agent in the raids that are never drawn,
tunes and engine sounds no command plays, four story endings that only the campaign-logic fix shows (CA-15), and a
dormant way to settle a break-in without playing it. Most are documented only; the two above are options.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options above and those of 1.1 to 1.3 (D-610, D-620, D-630 to D-636,
D-640 to D-642, D-650, D-660 to D-665, D-670 to D-672, D-680 to D-688).

## Known limits

- The two new options were checked by tests (the chase's draws, three hundred buildings), not yet played through by
  hand.
- As in 1.3: Linux is experimental (tried in Ubuntu 22.04 and 24.04 containers, sound and real desktops untried);
  there is no macOS build; the Amiga options need the Amiga version's own files; the joystick's readings of a real
  stick are still a model (U-12).
