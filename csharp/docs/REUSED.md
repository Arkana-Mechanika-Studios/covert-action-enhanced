# Reused code

Files of the C# rewrite that come from the **Darklands Restoration Project** (same author; GNU General Public
License, version 3, which is this project's licence too: `LICENSE` at the repository root). They were copied, never
edited in place there. Each file says in its first line where it came from; `RepositoryTests.EveryReusedFileIsListed`
keeps this list and those lines in step. A reused file keeps its own comments: its `// orig:` and `// DIVERGENCE:`
lines speak of Darklands and follow that project's rules, so the traceability tests of this rewrite leave it out.

Third-party parts with other licences (SDL 3, mt32emu, the VGA font, the .NET runtime in the release) are in
`THIRD_PARTY_NOTICES.md` at the repository root.

| File here | From (Darklands Restoration Project) | Changes |
|---|---|---|
| `src/CovertAction.Host.Sdl/SdlNative.cs` | `src/Darklands.Host.Sdl/SdlNative.cs` | namespace; environment variables named `COVERT_...` |
| `src/CovertAction.Host.Sdl/SdlKeyMap.cs` | `src/Darklands.Host.Sdl/SdlKeyMap.cs` | namespace; `Keys` is `HostKeys` |
| `src/CovertAction.Host.Sdl/SdlAudioOutput.cs` | `src/Darklands.Host.Sdl/SdlAudioOutput.cs` | namespace; the PCM interfaces come from `CovertAction.Platform`; environment variables named `COVERT_...`; the callback renders what SDL asks for, not whole buffers (docs/PERFORMANCE.md) |
| `src/CovertAction.Host.Sdl/SdlHost.cs` | `src/Darklands.Host.Sdl/SdlHost.cs` | adapted: a short version (one window, the picture, the text page, the keyboard); no picture filters, console or Darklands's keyboard timing; the text page is shown at the picture's 4:3 place; the PC keyboard's repeat, the BIOS's 15-key buffer, precise frame waits and a timing trace added (docs/PERFORMANCE.md); the window's size, fullscreen, scaling and key repeat from the settings (docs/SETTINGS.md); the mouse as a player option (the system's pointer, through `PictureLayout`; docs/MOUSE-SUPPORT.md) |
| `src/CovertAction.Host.Sdl/MissingSdl.cs` | `src/Darklands.Host.Sdl/MissingSdl.cs` | namespace; the names and texts |
| `src/CovertAction.Settings/GameFolderSearch.Linux.cs` | `src/Darklands.Platform/GameFolderSearch.Linux.cs` | Covert Action's product id, folder names and Wine install folders; Steam's libraries also searched for folders named like the game |
| `src/CovertAction.Host.Sdl/Native/linux-x64/libSDL3.so.0` | `src/Darklands.Host.Sdl/Native/linux-x64/libSDL3.so.0` | none in kind: SDL 3.4.20 built from SDL's source release by the same script (our own build, so another hash; in `THIRD_PARTY_NOTICES.md`) |
| `src/CovertAction.Host.Sdl/Native/win-x64/SDL3.dll` | `src/Darklands.Host.Sdl/Native/win-x64/SDL3.dll` | none: SDL 3.4.20, the official release (hash in `THIRD_PARTY_NOTICES.md`) |
| `src/CovertAction.Platform/IHost.cs` | `src/Darklands.Platform/IHost.cs` | adapted: cut down to picture, keys, sound device and time; the picture is passed as pixels and colours; the clock tells the time of day; the mouse over the picture (`IInput.Pointer`, docs/MOUSE-SUPPORT.md) |
| `src/CovertAction.Host.Sdl/PictureLayout.cs` | `src/Darklands.Platform/DisplayLayout.cs` | adapted: Covert Action's 4:3 place of the picture and the text page (`SdlHost`'s own arithmetic); SDL's window coordinates to drawing-area pixels (high pixel density); positions over the black bars are outside the picture; the text page's cells; no window-size options |
| `src/CovertAction.Platform/Pcm.cs` | `src/Darklands.Audio/Pcm.cs` | adapted: the two interfaces only; a silent output added |
| `src/CovertAction.Audio/Synthesis/Opl3.cs` | `src/Darklands.Audio/Synthesis/Opl3.cs` | namespace |
| `src/CovertAction.Platform/HostKeys.cs` | `src/Darklands.Platform/Keys.cs` | namespace; the class is `HostKeys` |
| `src/CovertAction.Platform/TextScreen.cs` | `src/Darklands.Platform/TextScreen.cs` | namespace; no XML comments required |
| `src/CovertAction.Host.Sdl/TextModeRaster.cs` | `src/Darklands.Platform/TextModeRaster.cs` | namespace (it renders a page, so it lives in the SDL host, not in the boundary); no XML comments required |
| `src/CovertAction.Host.Sdl/TextFont.cs` | `src/Darklands.Platform/TextFont.cs` | adapted: namespace (SDL host); only the built-in font (no font file from the player, no stand-in glyphs, no 8x8 font); `Builtin` is Darklands' `Fallback` |
| `src/CovertAction.Host.Sdl/Resources/vga-8x16.b64` | `src/Darklands.Platform/Resources/vga-8x16.b64` | none: the VGA 8x16 text font as DOSBox-X supplies it (notice in `THIRD_PARTY_NOTICES.md`); embedded as `CovertAction.Host.Sdl.vga-8x16.b64` |
| `src/CovertAction.Audio/Synthesis/Midi.cs` | `src/Darklands.Audio/Midi.cs` | namespace; a member without an XML comment is let through |
| `src/CovertAction.Audio.Mt32/Mt32Synth.cs` | `src/Darklands.Audio.Mt32/Mt32Synth.cs` | namespace; `IMidiSynth` is `CovertAction.Audio.Synthesis`'; `RolandModule` (from `src/Darklands.Audio/SoundCard.cs`) is in the file; members without an XML comment are let through |
| `src/CovertAction.Settings/GameFolderSearch.cs` | `src/Darklands.Platform/GameFolderSearch.cs` | adapted: Covert Action's GOG product id, Steam app id, folder names, files and subfolders; the registry and the environment are read through `IMachineLookup` (the Settings project may reference .NET's `Microsoft.Win32.Registry` for it) |
| `src/CovertAction.Settings/GameSettings.cs` | `src/Darklands.Platform/GameSettings.cs` | adapted: Covert Action's keys (the sound card, the controls, the picture's scaling, the key repeat); internal |
| `src/CovertAction.Settings/SettingsFile.cs` | `src/Darklands.Platform/SettingsFile.cs` | adapted: the file's name and folders, Covert Action's keys and template; the template is written at the launcher's first save |
| `src/CovertAction.Settings/SettingsFileEdits.cs` | `src/Darklands.Platform/SettingsFileEdits.cs` | namespace; internal; without the game-folder-only edit |
| `src/CovertAction.Settings/ILauncher.cs` | `src/Darklands.Launcher/ILauncher.cs` | adapted: Covert Action's context (the run's settings, the font, the saves folder, a picture of the player's game); a surface of one design size; no packs, previews or reports |
| `src/CovertAction.Settings/LauncherModel.cs` | `src/Darklands.Launcher/LauncherModel.cs` | adapted: Covert Action's rows; window sizes of the 4:3 picture |
| `src/CovertAction.Settings/ReleaseNotes.cs` | `src/Darklands.Launcher/ReleaseNotes.cs` | namespace |
| `src/CovertAction.Settings/Drawing/Canvas.cs` | `src/Darklands.Launcher/Drawing/Canvas.cs` | namespace |
| `src/CovertAction.Settings/Drawing/Png.cs` | `src/Darklands.Launcher/Drawing/Png.cs` | namespace; a writer added |
| `src/CovertAction.Host.Sdl/SdlDialogs.cs` | `src/Darklands.Host.Sdl/SdlDialogs.cs` | namespace |
| `src/CovertAction.Host.Sdl/SdlLauncherWindow.cs` | `src/Darklands.Host.Sdl/SdlLauncherSurface.cs` | adapted: a framed window of its own; the picture fitted to it; no hit test, typing or frameless handling |
| `src/CovertAction.Host.Sdl/LauncherHost.cs` | `src/Darklands.Host.Sdl/LauncherHost.cs` | adapted: Covert Action's context and saving; no packs, previews, reports or links |
| `src/CovertAction.Host.Launcher/Program.cs` | `src/Darklands.Host.Launcher/Program.cs` | the program's names |

With `Mt32Synth.cs` came the way Darklands links Munt's mt32emu: the project `CovertAction.Audio.Mt32` is
Darklands' `Darklands.Audio.Mt32` (the `Mt32emu.net` 1.0.0-rc.1 NuGet package, which brings the native libmt32emu;
LGPL, notice in `THIRD_PARTY_NOTICES.md`), and the SDL host's publish makes an empty `MT32_ROMS` folder beside the
program as Darklands' `Darklands.Host.Sdl.csproj` does. The ROM folder's lookup in `Launcher.SoundDriverChoice`
(`COVERT_MT32_ROMS`, else `MT32_ROMS` next to the program) and the fall-back to the AdLib driver without ROMs follow
Darklands' `src/Darklands.Audio.Mt32/SoundSelection.cs`, which is not copied (its sound set-up is Darklands').

A test helper, `tests/CovertAction.Launcher.Tests/Intro/PngWriter.cs` (from `src/Darklands.Presentation/Graphics/PngWriter.cs`, only the writer of a block of colours),
saves the rendered set-up page as a picture.

With `Opl3.cs` came its tests, `tests/CovertAction.Audio.Tests/Opl3Tests.cs` (from `tests/Darklands.Audio.Tests/Opl3Tests.cs`,
namespaces changed). The AdLib driver (`AdlibSoundDriver`, through `OplSynth`) writes the chip's registers and hands its samples to the
host's PCM output (user decision, 13 November 2026). Not to be reused: `src/Darklands.Audio/Machine` (an emulated PC that runs the original's
sound drivers), see ARCHITECTURE.md "No emulator".

The launcher follows Darklands' (`src/Darklands.Launcher`, `src/Darklands.Host.Launcher`, the launcher parts of `src/Darklands.Host.Sdl`): the files above come from it; its screen (`src/CovertAction.Settings/LauncherScreen.cs`), the text drawing (`Drawing/BitmapFont.cs`), the command line and precedence (`CommandLine.cs`, `StartSettings.cs`) and the game-folder search are new, as are the launcher's look and words (docs/SETTINGS.md).
