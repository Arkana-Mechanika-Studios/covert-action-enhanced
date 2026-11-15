# Covert Action (C# rewrite) 0.1.0

The first preview release of the C# rewrite of Sid Meier's Covert Action (1990): an unofficial fan project that
rebuilds the original from its own code, part by part, to play as it did. It reads your own copy of the game; none of
the game's files come with it. **A work in progress, not a finished port:** a whole campaign has not yet been proven
against the original (csharp/docs/SESSION-PROOF.md says what has).

## What's new

- **The game from the set-up screen on.** The set-up, the menus, the campaign, the building raids, the car chase, the electronics workbench and the cryptography workstation, rebuilt from the original's programs and checked against them session by session.
- **Its sound, through your game's own drivers.** No sound, PC speaker, Tandy, AdLib, and the Roland MT-32 on Munt's emulation with your own ROM files.
- **A launcher for the settings.** CovertAction Launcher.exe (or Shift held as the game starts) opens it: the game folder, the window, the picture's scaling, the sound card, the saves folder and the keyboard.
- **Faithful by default.** The set-up screen asks for the sound card at every start, as COVERT.EXE did; a held key repeats as the PC's keyboard did.
- **Your copy is never changed.** Saved games and the Hall of Fame go to a folder of your own.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time; its Play starts the game.

## Known limits

- Not every path of the original is proven yet (the jailbreak raid, for one).
- One graphics driver: the 256-colour MCGA/VGA one (the set-up does not ask for the graphics card).
- No joystick and no mouse yet; the Custom sound choice plays nothing.
- The MT-32 needs your own MT-32 (or CM-32L) ROM files in MT32_ROMS; without them the AdLib driver plays.
