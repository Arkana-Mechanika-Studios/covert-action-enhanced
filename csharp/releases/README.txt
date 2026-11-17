Covert Action (C# rewrite) - an unofficial fan project
======================================================

A rewrite of Sid Meier's Covert Action (MicroProse, 1990) in C#, made from the original's own code,
part by part, to play as it did. The whole game plays; the player options, all off by default, add
to it (RELEASE_NOTES.md says what is new in this version and what is not there yet).

Covert Action (c) 1990 MicroProse Software, Inc. This program is not affiliated with, endorsed or
supported by MicroProse or the game's rights holders.


What you need
-------------

* Your own copy of Sid Meier's Covert Action for DOS (the folder with COVERT.EXE, INTRO.EXE,
  FINAL.EXE, GAME.EXE, MGRAPHIC.EXE, FONTS.CV and the pictures). None of the game's files come
  with this program, and your copy is never changed.
* Windows 10 or 11, 64-bit. Nothing else to install: the .NET runtime is in this folder.
* For the Roland MT-32 sound card only: your own MT-32 (or CM-32L) ROM files, in MT32_ROMS (or
  where you already keep them: in or next to your game's folder, or in DOSBox Staging's mt32-roms
  folder; the launcher shows where they were found).
* For the Amiga options only: the files of your own Amiga version's disks, in a folder you choose
  on the launcher's Advanced tab.


Getting started
---------------

1. Unpack this folder anywhere you like (not into the game's own folder).
2. Start CovertAction.exe.
   - If your copy of the game is beside this folder (or in it), or was installed by GOG or Steam,
     it is found by itself.
   - If not, the launcher opens: press Change... and choose the folder of your copy (or drop the
     folder on the window), then Play.
3. The game starts with the title of the original's set-up screen for two seconds, then goes on.
   Its questions are not asked: the picture is always the MCGA's, and the sound card (AdLib by
   default) and the controls (keyboard and joystick by default) are settings of the launcher.

A joystick or gamepad works as the original's joystick: plug it in and keep the launcher's
controls at "Keyboard and joystick" (the default); the game calibrates it at the start (or press
Alt-J in the game) with the stick at rest. "Keyboard only" leaves the joystick out. The left
stick or the directional pad is the stick; the bottom face button (A) is the first button, the
right one (B) the second.

To change the settings later, start "CovertAction Launcher.exe" (or hold Shift while
CovertAction.exe starts). Its settings:
   General:  the game folder, the window size, fullscreen, the picture's scaling (pixel art,
             nearest, smooth), the sound card (with a sound test), the controls (keyboard and
             joystick, or keyboard only), the saved games' folder.
   Advanced: the MT-32 ROM folder, and player options, all off by default: the Amiga
             version's music, effects and pictures (with the folder of your Amiga files),
             music and effects on different sound cards, and the fixes for the original's
             bugs (two options: those that leave the story as it is, and those that
             change it).
   Cut content: the original's unused content, as options, all off by default.
   Controls: the keyboard's repeat (the PC's, or Windows' own), and options, all off by
             default: arrow keys that walk in a raid, the mouse (in the menus, or in the
             car chase too), your own keys for the original's keys, and the gamepad's
             other buttons.
Play saves them and starts the game; Save and quit (or Esc) saves them and closes. They are kept
in covertaction.settings.json beside the program (or in %APPDATA%\CovertAction when this folder
cannot be written); SETTINGS.md describes the file and the command-line options
(CovertAction.exe --help).

Your saved games and the Hall of Fame go to the Saves folder beside the program (or to
%LOCALAPPDATA%\CovertAction\Saves when this folder cannot be written), unless you choose another
folder: the whole program folder can be moved or copied with them. The saves you made with the
original (CV0.SVE to CV3.SVE in your game folder) can be loaded as they are; your game folder
is never written to.


Files
-----

CovertAction.exe            the game
CovertAction Launcher.exe   the settings (it starts CovertAction.exe --setup)
SDL3.dll, libmt32emu.dll    SDL 3 (window, keyboard, joystick, sound) and Munt's MT-32 emulation
MT32_ROMS\                  for your MT-32 ROM files
RELEASE_NOTES.md            what is new
SETTINGS.md                 the settings file and the command line
LICENSE                     this program's licence: the GNU General Public License, version 3
THIRD_PARTY_NOTICES.md, licences\   the other parts' notices and licences
