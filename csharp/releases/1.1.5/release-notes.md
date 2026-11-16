# Covert Action (C# rewrite) 1.1.5

The second round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.1.5
plays exactly as 1.0 and the original.

## What's new

- **The mouse in the action parts.** At the electronics workbench, pointing at a chip's slot moves the cursor there, a left click swaps the chip and a right click is Esc (D-634). At the cryptography workstation, a left click on a letter of the coded message selects it as its key does; the replacement is still typed, and a right click is Esc (D-635). In a building raid the agent walks towards the pointer while the left button is held, and a right click is the fire button (the joystick's first button); keys and the joystick keep working alongside (D-636). No mouse in the car chase. Turned on in the launcher's Advanced tab, as in 1.1 (D-630).
- **Everything of 1.1 and 1.0.** The mouse in the menus, lists and on the flight map, arrow keys that walk in a raid, music and effects on different sound cards (all options, off by default), and the whole faithful game with the joystick, portable saves, and the game and the MT-32 ROMs found automatically.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. The player
   options are on its Advanced tab. Its **Play** starts the game.

Saves made with 1.0 or 1.1 (or with the original) load as they are.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options above and those of 1.1 (D-610, D-620, D-630 to D-636).

## Known limits

- The mouse works everywhere but the car chase, where the keyboard and the joystick play as in the original. At the
  workstation only the choice of a letter is by mouse; the replacement is typed.
- The joystick is tried with a real gamepad, but how a real stick's position becomes the game's readings is still a
  model (U-12). The two outcomes 1.0 and 1.1 had proven function by function only, a jailbreak foiled and a campaign
  wiretap solved above level 0, are now also proven in recorded sessions against the original.
- Windows only (64-bit).
