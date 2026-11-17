Covert Action (C# rewrite) - an unofficial fan project, Linux x64 build (experimental)
=====================================================================================

A rewrite of Sid Meier's Covert Action (MicroProse, 1990) in C#, made from the original's own code. This is the
Linux x64 build: it is experimental, tried so far in clean Ubuntu 22.04 and 24.04 containers only. RELEASE_NOTES.md says
what is new in this version.

Covert Action (c) 1990 MicroProse Software, Inc. This program is not affiliated with, endorsed or supported by
MicroProse or the game's rights holders.

What you need
-------------

* Your own copy of Sid Meier's Covert Action for DOS (the folder with COVERT.EXE, INTRO.EXE, FINAL.EXE, GAME.EXE,
  MGRAPHIC.EXE, FONTS.CV and the pictures; the GOG or Steam copy will do). None of the game's files come with this
  program, and your copy is never changed. File names may be in any case.
* Linux x64 with glibc 2.34 or later (Ubuntu 22.04 or later, for example; tried
  only in Ubuntu 22.04 and 24.04) and a
  desktop (X11 or Wayland). Nothing else to install: the .NET runtime is inside the program and SDL 3 (libSDL3.so.0)
  is beside it, so systems without an SDL 3 of their own (Ubuntu 24.04) work too. Delete libSDL3.so.0 to use the
  system's SDL 3 instead; COVERT_SDL3_PATH names another library. The .NET runtime needs libicu (every desktop has it;
  a bare container needs: apt install libicu70, or libicu74 on Ubuntu 24.04).
* For the Roland MT-32 sound card only: your own MT-32 (or CM-32L) ROM files, in MT32_ROMS.

Getting started
---------------

1. Unpack the archive anywhere you like (not into the game's own folder): tar -xzf CovertAction-<version>-linux-x64.tar.gz
2. Start ./CovertAction (or "CovertAction Launcher", which opens the settings screen first).
   - If your copy of the game is beside this folder, or Steam, Heroic, Lutris, GOG's installer or a Wine prefix put it in
     one of their usual places, it is found by itself. If not, the launcher opens: press Change... and choose the
     folder of your copy (or drop the folder on the window), then Play.

Where things are kept
---------------------

The settings (covertaction.settings.json) and the saved games (Saves) are kept in this folder when it can be written to,
else in $XDG_CONFIG_HOME/CovertAction (~/.config/CovertAction) and $XDG_DATA_HOME/CovertAction/Saves
(~/.local/share/CovertAction/Saves). SETTINGS.md lists every setting.

The rest of the game works as on Windows: see the Windows release's README for the launcher's tabs and the controls.
Licences: LICENSE, THIRD_PARTY_NOTICES.md and licences/.
