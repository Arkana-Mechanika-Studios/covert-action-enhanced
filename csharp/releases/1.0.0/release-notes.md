# Covert Action (C# rewrite) 1.0

The first full release of the C# rewrite of Sid Meier's Covert Action (1990). It is an unofficial fan project that
rebuilds the original from its own code, part by part, so that it plays as the original did. It reads your own copy of
the game; none of the game's files come with it. 1.0 is the faithful version and stays the reference. Options beyond
the original, such as the mouse, will come in later versions.

## What's new

- **The whole game, faithful to the original.** It runs from the set-up screen to the end of a career: the menus, the campaign, the building raids, the car chase, the electronics workbench and the cryptography workstation. Every part is rebuilt from the original's programs and checked against them, function by function and session by session.
- **A short start.** The game opens on the set-up screen's red title for two seconds, then goes on. It asks no questions: the sound card and the controls are set in the launcher, and the picture is always the MCGA's.
- **The joystick, as in the original.** A gamepad or joystick works as the PC's analog joystick, and the game calibrates it as it did. Keyboard and joystick is the default; with no controller plugged in, the game uses the keyboard only.
- **The game and the MT-32 ROMs are found automatically.** The launcher finds a GOG or Steam copy, an installed one or one beside the program. It finds MT-32 ROM files beside the program, near the game or in DOSBox's folders.
- **Portable.** Saved games and the Hall of Fame go in a Saves folder beside the program. Saves you made with the original still load until you save over them. Your copy of the game is never changed.
- **AdLib by default.** The sound card is a launcher setting: none, PC speaker, Tandy, AdLib or the Roland MT-32 (Munt's emulation, with your own ROM files).

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. Its
   **Play** starts the game.

## Differences from the original a player could notice

All are listed with their reasons in csharp/docs/faithfulness/.

- The set-up screen asks no questions (D-600, D-603). The picture is always the MCGA's: MCGA, EGA, VGA and Tandy show
  the same 16 colours in this game, and CGA is not offered. The sound card and the controls come from the launcher.
- The Roland MT-32 is Munt's emulation and needs your own MT-32 or CM-32L ROM files. Without them, the AdLib music
  plays instead of nothing (D-20).
- The set-up's "Custom" sound card is not offered. In the original, it ended the game, since its driver was never shipped (D-18).
- A saved game that is cut short or damaged is shown as "(EMPTY)" in the load screen and cannot be loaded. The original
  loaded it into a broken campaign (D-105).
- When a game file is missing, the game stops and the launcher names the file. The original printed the name in text
  mode and started the same part again (D-12).

## Known limits

- Two outcomes are proven function by function but not yet in a recorded session: a jailbreak foiled (the agent
  winning the jailbreak raid) and a campaign wiretap solved above level 0.
- The joystick is not yet tried with a real controller. The game's reading of it is proven against the original, but
  the counts a gamepad gives are a model of the PC's game port (U-12).
- A save on a full disk is cut short, as in the original: the game still says "Game has been saved.", and the slot then
  shows "(EMPTY)". A save that cannot be made at all (a read-only file or folder) says "File Error", as in the original.
- Windows only (64-bit).
