# Covert Action (C# rewrite) 1.3.2

A fix release on top of 1.3.1, the faithful rewrite of Sid Meier's Covert Action (1990). It is an unofficial fan
project that rebuilds the original from its own code, part by part. It reads your own copy of the game; none of the
game's files come with it. Everything new is a player option, off by default: with the options off, 1.3.2 plays
exactly as 1.0 and the original.

## What's new

- **Two crashes fixed.** A building known only by its address, and a coded message after an arrest.
- **Everything of 1.3.1 and before.** The cut content, the controls, the Amiga version, Linux and the whole faithful game.

## Getting started

1. Unpack the release into a folder of your own (not into the game's folder): the zip on Windows; on Linux the
   tar.gz, with `tar -xzf` (README-linux.txt in it says more).
2. Start **CovertAction.exe** (on Linux `./CovertAction`). If your copy of Covert Action is not found, the launcher asks
   for its folder (the one with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** (on Linux `CovertAction Launcher`) opens the settings at any time, as does Shift held
   while the game starts. The player options are on its Advanced, Cut content and Controls tabs. Its **Play** starts
   the game.

Saves made with 1.0 to 1.3.1 (or with the original) load as they are. A settings file of 1.3.1 is read as it is.

## What's new in detail

Both crashes were reported by a player (GitHub issues #1 and #2), and both are places where the 1990 program reads
memory just outside a table and carries on. The rewrite stopped there with an error; it now reads the same bytes the
original reads, so the game goes on as the original did.

- **A building known only by its address** (#1, CA-67, D-689): the city screen can offer a building the player has
  no record of, through a contact. Choosing it crashed. The original reads the record in front of the buildings
  table, a byte of the last person's record, and names the building or calls it unknown by that byte; the rewrite
  now does the same. The data files' list of known buildings, which already modelled that read, took it four bytes
  off; both now read the same byte.
- **A coded message decoded after an arrest** (#2, CA-66): the interrogation queues its own report in the same minute
  as the suspect's messages, and the decoding takes it as the message's partner. That report has no person, so the
  original reads and writes two bytes in front of the persons table (the end of the text "One moment please...").
  The rewrite crashed there; it now does what the original does.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options of 1.1 to 1.3.1 (D-610, D-620, D-630 to D-636, D-640 to D-642,
D-650, D-660 to D-665, D-670 to D-672, D-680 to D-688).

## Known limits

- As in 1.3.1: the two newest cut-content options were checked by tests, not yet played through by hand; Linux is
  experimental (tried in Ubuntu 22.04 and 24.04 containers, sound and real desktops untried); there is no macOS
  build; the Amiga options need the Amiga version's own files; the joystick's readings of a real stick are still a
  model (U-12).
