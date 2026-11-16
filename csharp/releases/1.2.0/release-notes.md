# Covert Action (C# rewrite) 1.2

The third round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.2
plays exactly as 1.0 and the original.

## What's new

- **The Amiga version's music, sound effects and pictures**, read from the Amiga version's files you own (none come
  with the program; on the launcher's Advanced tab, choose the folder that holds the files of the Amiga disks).
  - **Music** (D-640): the Amiga's tunes in place of the sound card's, played by a player of their own. Where the
    Amiga version has no music (the equipment and car choices, the vacations), another of its tunes plays, or nothing
    with "Only where the Amiga had it".
  - **Sound effects** (D-641): the Amiga's effects where its game plays the same sound (27 of the 34 matched to the
    PC's by where the two programs play them); the sound card's effects elsewhere, or silence.
  - **Pictures** (D-642): 32 pictures whose layout is the PC's: the faces, icons, three flight maps, the hotel, the
    raid's item icons, the car chase's windscreen and most city pictures, in the Amiga's colours. Pictures the Amiga
    redrew in another layout, those the game reads back, and the animations stay the PC's.
- **Everything of 1.1.5, 1.1 and 1.0.** The mouse in the menus and the action parts, arrow keys that walk in a raid,
  music and effects on different sound cards, and the whole faithful game.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. The player
   options, the Amiga ones among them, are on its Advanced tab. Its **Play** starts the game.

Saves made with 1.0, 1.1, 1.1.5 (or with the original) load as they are.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options above and those of 1.1 and 1.1.5 (D-610, D-620, D-630 to D-636,
D-640 to D-642).

## Known limits

- The Amiga options need the Amiga version's own files; without them the PC's music, effects and pictures play.
- Amiga pictures: the raid's screen flashes do not tint the Amiga-coloured parts (the face icons of the raid's panel).
  The Amiga's animations and the pictures it redrew are not used.
- Amiga effects: how often an effect repeats is read from the Amiga program, not heard on an Amiga; the
  cryptography workstation's effect is a probable match.
- The mouse works everywhere but the car chase. The joystick's readings of a real stick are still a model (U-12).
- Windows only (64-bit).
