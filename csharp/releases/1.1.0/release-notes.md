# Covert Action (C# rewrite) 1.1

The first round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.1
plays exactly as 1.0 and the original.

## What's new

- **The mouse in the menus, lists and on the flight map.** Pointing at a choice highlights it, a left click takes it, a right click is Esc, and a click answers "press a key". Not in the original; turned on in the launcher's Advanced tab (D-630).
- **Arrow keys that walk in a building raid.** The grey arrow keys (and Home, End, PgUp, PgDn) walk the agent as the keypad's digits do, and Shift with an arrow lunges. Off by default, since the original's grey arrows lunge (D-610).
- **Music and sound effects on different sound cards.** For example the music on the Roland MT-32 and the effects on the AdLib. The launcher's Advanced tab has Music on, Effects on and a Test mix (D-620).
- **Everything of 1.0.** The whole game, faithful to the original, with the joystick, portable saves, and the game and the MT-32 ROMs found automatically.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. The player
   options are on its Advanced tab. Its **Play** starts the game.

Saves made with 1.0 (or with the original) load as they are.

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the three options above (D-610, D-620, D-630).

## Known limits

- The mouse works in the menus, lists and on the flight map, not in the action parts (the raid, the car chase, the
  workbench and the workstation), where the keyboard and the joystick play as in the original.
- The limits of 1.0 still hold: two outcomes are proven function by function only (a jailbreak foiled, a wiretap
  solved above level 0); the joystick is not yet tried with a real controller (U-12).
- Windows only (64-bit).
