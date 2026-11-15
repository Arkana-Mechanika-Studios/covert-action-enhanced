# Covert Action (C# rewrite) 0.2.0

A preview release of the C# rewrite of Sid Meier's Covert Action (1990): an unofficial fan project that rebuilds the
original from its own code, part by part, to play as it did. It reads your own copy of the game; none of the game's
files come with it. **A work in progress, not a finished port.**

## What's new

- **Portable.** Saved games and the Hall of Fame go to the Saves folder beside the program (or to a folder of your
  own): copy or move the program's folder and they go with it. The saves you made with the original load as they are;
  your game folder is never written to.
- **Player options, not in the original, all off by default** (the launcher's Advanced tab): the mouse in the menus,
  the lists and on the flight map; the arrow keys walking in a building raid; the music on one sound card and the
  effects on another.
- **Smoother play.** The PC keyboard's key repeat, precise frame timing and exact sound.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time; its Play starts the game.

## Known limits

- Not every path of the original is proven yet in a whole recorded session (the jailbreak raid, for one).
- One graphics driver: the 256-colour MCGA/VGA one (the set-up does not ask for the graphics card).
- No joystick yet; the Custom sound choice plays nothing.
- The MT-32 needs your own MT-32 (or CM-32L) ROM files in MT32_ROMS; without them the AdLib driver plays.
