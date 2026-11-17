# Covert Action (C# rewrite) 1.3

The fourth round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.3
plays exactly as 1.0 and the original.

## What's new

- **The original's cut content** (D-660 to D-663): four things the game's own files carry and its programs never
  use, on the launcher's new Cut content tab. docs/cut-content-options.md shows each, and says what was left out and why.
- **The mouse in the car chase** (D-670): a click stands for one of the chase's keys; the keys work as before.
- **Your own keys and the gamepad's other buttons** (D-671, D-672): any key of your keyboard can send one of the
  original's keys, and the gamepad's shoulders, triggers, Start, Back and stick clicks can each send one.
- **More of the Amiga version** (D-681 to D-684): twelve more of its pictures, eleven of its animations, and its own
  unused dossier.
- **Linux x64, experimental**: a release for Linux, with SDL 3 beside the program.
- **A tidier launcher**: the keyboard, the mouse, your keys and the gamepad on a new Controls tab; every label fits.
- **Everything of 1.2, 1.1.5, 1.1 and 1.0.** The bug-fix options, the Amiga music, effects and pictures, the mouse
  in the menus and the action parts, and the whole faithful game.

## Getting started

1. Unpack the release into a folder of your own (not into the game's folder): the zip on Windows; on Linux the
   tar.gz, with `tar -xzf` (README-linux.txt in it says more).
2. Start **CovertAction.exe** (on Linux `./CovertAction`). If your copy of Covert Action is not found, the launcher asks
   for its folder (the one with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** (on Linux `CovertAction Launcher`) opens the settings at any time, as does Shift held
   while the game starts. The player options are on its Advanced, Cut content and Controls tabs. Its **Play** starts
   the game.

Saves made with 1.0 to 1.2 (or with the original) load as they are. A settings file of 1.2 is read as it is.

## What's new in detail

**The original's cut content**, on the Cut content tab (settings `cutContent.*`):

- **Captured dossier** (D-660): when a cast member is captured, arrested after a car chase or turned, the dossier page
  stamped CAPTURED that the game ships (CAPTRD01.PAN) plays after the jail scene and before the chief's comment, with
  a woman's photograph for a woman, as the animation's own unused switch meant.
- **Free car chase** (D-661): a fifth line in the practice menu, "Free Chase": the chase's stand-alone mode, on a random
  map with the places labelled Loc1 to Loc15, a random start and goal, and nothing handed to a campaign.
- **Hardest circuit board** (D-662): the electronics board's unused fourth difficulty, for the hardest level with an
  agent of the lowest electronics rating: only the last column's chips are shown.
- **All filler sentences** (D-663): coded messages draw their filler from all sixteen sentences, not the first twelve.
- Left out, each with its reason on the page: the arrest-only chase, the unshipped plot variants and Max Remington's
  apartment, the raid's corridor walk, and texts no code would draw.

**The mouse in the car chase** (`input.mouseChase`, on the Controls tab's Mouse row as "Menus and car chase"; it needs
the mouse option): a left click on the map or the street view is the arrow key of that side of your car; on the other
tail car's dot it switches cars, on a known place's dot it enters it; the windscreen shifts up, the bonnet down, the
auto-follow lamp is F10 and the clock steps through the speeds; a right click arrests. It gives nothing the keys do
not: a click on the quarry's dot does nothing, so a quarry out of sight stays hidden.

**Your own keys and the gamepad's other buttons** (`input.keyMap`, `input.padMap`, the Controls tab): choose one of the
original's keys and press the key of your keyboard that should send it; the original key keeps working. The
gamepad's stick and four face buttons stay the joystick's; its other buttons can each send a key. Both lists are empty
by default.

**More of the Amiga version** (the launcher's "Amiga graphics", read from your own Amiga files):

- Twelve more pictures (D-681, D-682): Paris, Moscow, Berlin, Amsterdam, Baghdad, Riyadh, London, Miami and Rome, and
  the break-in, goodbye and training pictures. Pictures the Amiga drew in another layout stay the PC's
  (AMIGA-PICTURES.md lists them and why).
- Eleven animations (D-683): the Amiga's animation format is read, and where its script is the PC's (the buildings, the
  bugged car, the bust-out, the escape, being followed, the jail, the interrogation and the surrender) the Amiga's
  images play with the PC's timing and sounds.
- Its own unused dossier (D-684): with the captured dossier on as well, the Amiga's CAPT1FPO.PAN plays, its photographs
  with faces.
- The Amiga's sound effects were checked against its program for where they are placed left and right (D-680): as in
  1.2, nothing changes.

**Linux x64, experimental**: CovertAction-1.3.0-linux-x64.tar.gz carries SDL 3.4.20, built from SDL's source on Ubuntu
22.04; it needs glibc 2.34 or later and libicu (every desktop system has it). Settings and saves stay beside the program,
or go to `~/.config/CovertAction` and `~/.local/share/CovertAction/Saves` when its folder cannot be written to. Your game
is found in Steam's libraries, Heroic, the usual GOG and Lutris folders and Wine prefixes, its file names in any case.

**The launcher**: key repeat, "Arrow keys walk" and "Mouse" moved to the Controls tab; on the Advanced tab the Amiga's
rows read "Amiga's sounds only" and "Amiga graphics", and the bug fixes are "Story-safe bugs" and "Campaign-logic bugs".
The settings keys are the same.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options above and those of 1.1 to 1.2 (D-610, D-620, D-630 to D-636,
D-640 to D-642, D-650, D-660 to D-663, D-670 to D-672, D-680 to D-684).

## Known limits

- Linux: run in clean Ubuntu 22.04 and 24.04 containers (a start without a window, the launcher's picture, the
  settings written); sound and real desktops are untried. Please report what you find. There is no macOS build.
- The car chase's mouse and the new keys were checked by tests, not yet played through by hand on every screen.
- The Amiga options need the Amiga version's own files; without them the PC's music, effects, pictures and animations
  play. Amiga pictures and animations drawn in another layout stay the PC's; the raid's screen flashes do not tint the
  Amiga-coloured parts. The chase's two Amiga sounds at the screen's edges have no PC counterpart and are not played.
- The cut-content dossier's photographs are the animation's own (on the PC a grey face); neither version shows the
  captured person.
- The bug fixes: as in 1.2 (its release notes, Known limits).
- The joystick's readings of a real stick are still a model (U-12).
