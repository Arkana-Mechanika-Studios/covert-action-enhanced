# Settings, the launcher and the release

The player's options of the SDL host (`CovertAction.exe`) live in one file, `covertaction.settings.json`, which the
launcher (the settings screen) edits. Modelled on the Darklands Restoration Project's `docs/SETTINGS.md`; the code is
`src/CovertAction.Settings` (the file, the precedence, the launcher's screen) and `src/CovertAction.Host.Sdl`
(`Program`, `LauncherHost`, `SdlLauncherWindow`, `SdlSoundTest`). None of it is part of the original. The game starts
as COVERT.EXE did, with its set-up screen, but shows only the screen's title for two seconds: the answers to its
questions are settings here (the sound card and the controls; the graphics answer is always MCGA: D-600, D-603).

## The settings file

Plain JSON with `//` and `/* */` comments and trailing commas allowed. Unknown keys and invalid values are reported on
the console (stderr) with the file name and the key, then ignored; a broken file never stops the program (an
unparsable one is reported and every setting ignored). `""` (empty text) means the default for text and folders, and a
key left out takes its default. Relative folders are taken relative to the file's folder; `%VARIABLES%` are expanded.

### Where it is looked for

First found wins:

1. `--settings <file>` (also where the launcher saves then);
2. `covertaction.settings.json` next to the program;
3. `%APPDATA%\CovertAction\covertaction.settings.json`.

With none of them the defaults are used and nothing is written until the launcher saves a change: it then writes the
commented template (every key, its default, its command-line option) next to the program when that folder can be
written, else in `%APPDATA%\CovertAction`, and sets the changed keys in it. The launcher's edits keep the file's
comments and layout (`SettingsFileEdits`, from Darklands).

### Precedence

**Command line > environment variables > settings file > defaults.** A `--scale` or `--window` on the command line
replaces both window keys of the file. A setting the command line or an environment variable gives is shown on the
launcher but cannot be changed there (its details say what sets it); the launcher saves only what the player changed.

### Keys

| Key | Default | Command line / environment | Meaning |
|---|---|---|---|
| `gameFolder` | searched (below) | first argument; `COVERT_GAME_DIR` | your copy of the game: the folder with COVERT.EXE, INTRO.EXE, FINAL.EXE, GAME.EXE, MGRAPHIC.EXE, FONTS.CV ... (or a folder above it with a `Covert` subfolder) |
| `savesFolder` | `Saves` next to the program (`%LOCALAPPDATA%\CovertAction\Saves` when the program's folder cannot be written to) | `--saves` | saved games (CV0.SVE .. CV3.SVE) and the Hall of Fame; the game folder is never written to. A save this folder does not hold is read from the game folder, so the saves made with the original load; a save in the same slot then goes here and hides the original's |
| `display.scale` | 3 | `--scale N` | the window is 320x240 times N (the picture's 4:3 shape), 1 to 8 |
| `display.resolution` | | `--window WxH` | any window size, e.g. `"1280x960"`; when set it replaces `scale` |
| `display.fullscreen` | false | `--fullscreen` | the window fills the screen (the picture keeps its shape) |
| `display.scaling` | `"pixelart"` | `--scaling <name>` | how the 320x200 picture (and the set-up's text page) is enlarged: `pixelart` (every game pixel the same size, only the edges between them blended; SDL 3.4), `nearest` (hard pixels, some a window pixel wider than others), `smooth` (linear) |
| `sound.card` | `"adlib"` | `--sound <card>` | the card the game plays on, the set-up screen's sound answer (D-603): `none`, `speaker`, `tandy`, `adlib`, `mt32`. An earlier file's `"setup"` (the set-up screen asked) reads as the default, without a message |
| `sound.mt32Roms` | `MT32_ROMS` next to the program | `--mt32-roms`; `COVERT_MT32_ROMS` | your MT-32 (or CM-32L) ROM files (found by themselves in the usual places, below); without them the AdLib driver plays instead (D-20) |
| `input.controls` | `"joystick"` | `--controls <joystick\|keyboard>` | the set-up screen's controls answer (D-603): `joystick` ("1. Joystick and Keyboard" when a joystick or gamepad is plugged in at the start: the menus part then calibrates it; with none, "keyboard only") or `keyboard` ("2. Keyboard only") |
| `input.keyRepeat` | `"pc"` | `--key-repeat <pc\|windows>`; `COVERT_KEY_REPEAT=system` | how a held key repeats: as the PC keyboard did (after half a second, 10.9 a second; PERFORMANCE.md) or at Windows' own rate |

An earlier file's `startup.skipSetup` is ignored without a message: there is nothing left to skip.

The joystick itself has no setting, as in the original: the first joystick or gamepad SDL finds (one plugged in later
is taken when it arrives) is the PC's analog joystick, which the game uses when `input.controls` is `joystick` (the
set-up's "1. Joystick and Keyboard") or the player presses Alt-J. A gamepad's left stick or directional pad is the stick and its bottom, right,
left and top face buttons the joystick's four buttons; a plain joystick gives its first two axes and four buttons.
The game calibrates the stick where it is when the joystick is switched on, and learns the ends as the stick reaches
them. With no controller the game sees no joystick, as on a PC without one (`SdlJoystick`, `JoystickReading`).

Other command-line options: `--setup` (the launcher first), `--no-launcher` (never the launcher: a missing game ends the
program with a message), `--launcher-screenshot <file.png>` (the launcher as it would open, drawn into a PNG without a
window), `--practice crypto|chase` and `--level 0..3` (a developer's shortcut into one practice), `--help`. Developer
variables stay as they were: `COVERT_FRAME_TRACE`, `COVERT_INPUT_SCRIPT` and the others of PERFORMANCE.md.

### Finding the game

With no folder named, the program looks, without scanning any disk and in this order, in: its own folder, the folder above
it, the current folder; GOG's registration of the game (`HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1207660463` and
`SOFTWARE\GOG.com\Games\1207660463`, value `path`); Steam's libraries (Steam's own folder from the registry, the
libraries of `steamapps\libraryfolders.vdf`, in each `steamapps\common\Sid Meiers Covert Action` and its likely spellings
and any folder there with `Covert Action` in its name); Windows' list of installed programs (a program whose name has
`Covert Action` in it: its install folder, else the folder of its uninstaller); then the usual folders: GOG Games,
`Games`, `SteamLibrary\steamapps\common` and GOG Galaxy's and Steam's default folders under `Program Files (x86)`, with
the game's folder names, and `COVERT`, `Games\COVERT`, at the root of each fixed drive. The registry is read only when
the places before it hold no game, and a key that cannot be read gives no place. In each place, the folder itself or its
`Covert`, `COVERT`, `Covert Action\Covert`, `Covert Action` or `CA` subfolder (or a subfolder with `covert` in its name,
or that folder's `Covert`) counts when it holds the files above. The launcher says which place each folder came from. A
folder named that holds no game is not used: the launcher asks.

GOG's product id (1207660463) is from the GOG database (gogdb.org) and Steam's app id (327390) from the Steam store; the
folder Steam installs to is not confirmed, hence the search by name.

### Finding the MT-32 ROMs

A folder you name (`sound.mt32Roms`, `--mt32-roms`, `COVERT_MT32_ROMS`) is used as it is. With none named, the first of
these places that holds a usable set (an MT-32's, or a CM-32L's, control and PCM ROM files) is used, and the launcher says
where it was found: `MT32_ROMS` beside the program; `MT32_ROMS`, `mt32-roms` and `roms` in the game folder, then in the
folder above it and the one above that (a store's install folder); DOSBox Staging's `mt32-roms` folder
(`%LOCALAPPDATA%\DOSBox`, the older `%APPDATA%\dosbox-staging` and `%LOCALAPPDATA%\dosbox-staging`, `~/Library/Preferences/DOSBox`,
`~/.config/dosbox`); DOSBox-X's and Munt's usual folders (conventions only: neither has a default of its own). With no
set anywhere the launcher shows the folder beside the program and that there are no ROMs.

## The launcher

The settings screen, drawn by the SDL host in a window of its own before the game's (its design is 1280 x 720; the
window is the largest of 1280x720, 1120x630, 960x540 that fits the screen, resizable, the picture fitted to it with
even pixels). It comes up:

* with `--setup`, which `CovertAction Launcher.exe` (beside the program) passes: its Play starts the game;
* while **Shift** is held as `CovertAction.exe` starts;
* when the game folder is not found (or a folder named holds no game);
* when the game could not start (a file missing or damaged), with the reason.

Otherwise `CovertAction.exe` goes straight to the game.

**Left: the case file.** What is new (the items of `RELEASE_NOTES.md` next to the program, up to "Getting started": each
item's bold lead and the first part of its sentence; the build copies `csharp/releases/<version>/release-notes.md`
there), what the project is, and the small print (Covert Action © 1990 MicroProse Software, Inc.; an unofficial fan
project, not affiliated with or endorsed by MicroProse or the game's rights holders; GNU GPL v3).

**Right: the settings**, on two tabs.

* **General**: Game installation (the folder, **Change…**, a line saying it was found or what is missing), Display
  (window size, fullscreen, picture scaling), Sound and controls (the sound card with **Test sound**, a line about the
  card or the ROMs; the controls: Keyboard and joystick, or Keyboard only), Saved games (the folder, **Change…**).
* **Advanced**: Roland MT-32 (the ROM folder, **Change…**, found or missing with the D-20 note), Keyboard (key
  repeat), and room for the player options planned for later (the arrow keys walking in a building, the mouse, mixing
  the sound drivers), shown as planned and not offered.

Rows show `‹ value ›`: a click on ‹ or › (or ←/→) steps through the values, a click on the row or Enter/Space steps on.
Folders: **Change…** (or Enter on the field) opens Windows' folder dialog; a folder can also be dropped on the window
(the game folder when it holds the game, the ROM folder when it holds ROMs, the saves folder while its row is chosen)
or pasted (Ctrl+V); ←/→ on the game folder go through the folders found. Tab / Shift+Tab and ↑/↓ go through the rows
of both tabs and the buttons; Ctrl+Tab changes the tab. The **details box** above the buttons shows the last message
(why the launcher came up, what an action did, why it could not), then what the row under the pointer, or chosen, does.
**Test sound** plays the theme tune for twelve seconds on the chosen card through the game's own driver (the player's
?SOUND.CA; the MT-32 with the ROMs, else the AdLib). **Play** saves the changes and starts the game; **Save and quit**,
Esc and the window's close button save and leave.

Nothing of the game's art is in the repository or the release: the launcher is drawn in code, in the VGA 8x16 text
font the set-up screen shows. Behind it, faint and in one colour, is a picture of the player's own game (HOTEL.PIC,
read from the game folder at run time through `CovertAction.Content`). The art is data-driven as Darklands': a PNG in
`src/CovertAction.Settings/Resources/art` named `background`, `card`, `panel` or `title` (embedded by the build)
replaces the part drawn in code; there is none yet.

The launcher is its own assembly, `CovertAction.Settings`, which references no other assembly of the product and draws
into its own pixels; the SDL host gives it a window (`SdlLauncherWindow`) and its services (`LauncherHost`). Another
launcher fits in by implementing `ILauncher`. Tests: `tests/CovertAction.Settings.Tests` (the file, the precedence,
what the launcher saves read back, and its layout: nothing of the panel or the card overlaps, everything stays in its
panel, in every state drawn); its pictures are written to `build/launcher` for the eye.

## The release

`python -B csharp/tools/publish_release.py [--version 1.0.0] [--out folder]` publishes the SDL host self-contained for
win-x64 into `build/release/<version>/CovertAction-<version>-win-x64` and zips that folder beside it. The folder holds
`CovertAction.exe` (and the .NET runtime), `CovertAction Launcher.exe` (built for .NET Framework 4.6.2, which Windows 10
and 11 have, so it is a few kilobytes; as Darklands' `DarklandsRP Launcher.exe`), `SDL3.dll`, `libmt32emu.dll`, an
empty `MT32_ROMS`, `README.txt` (csharp/releases/README.txt), `RELEASE_NOTES.md`, `SETTINGS.md`, `LICENSE`,
`THIRD_PARTY_NOTICES.md` and `licences/` (SDL's zlib licence, the LGPL 2.1 of mt32emu with where its source is, the .NET
runtime's licence and notices). The script refuses a folder with any of the game's files in it. The player supplies the
game.
