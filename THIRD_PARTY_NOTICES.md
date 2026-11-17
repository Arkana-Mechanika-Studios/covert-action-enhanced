# Third-party references and provenance

The supplied game, its images, text, sound and executable code remain the property of
their respective owners. This project does not include them in source distributions or in its releases.
Unpacked load images and recordings made from the game stay in ignored local `build/` output.

## The Windows release

`python -B csharp/tools/publish_release.py` makes the release (csharp/docs/SETTINGS.md "The release"). Besides this
project's own code (GNU GPL v3, `LICENSE`) it carries, unmodified, each with its licence in the release's `licences/`
folder (`licences/README.txt` lists them):

| In the release | What it is | Licence | In `licences/` |
|---|---|---|---|
| the .NET runtime (self-contained beside the program) | Microsoft's .NET 10 runtime | MIT | `dotnet-LICENSE.txt`, `dotnet-ThirdPartyNotices.txt` (copied from the .NET SDK) |
| `SDL3.dll` (Windows), `libSDL3.so.0` (Linux, experimental) | SDL 3.4.20 (below) | zlib | `SDL3-zlib.txt` |
| `libmt32emu.dll` (Windows), `libmt32emu.so` (Linux) | Munt's mt32emu, as the `Mt32emu.net` package builds it (below) | LGPL 2.1 or later | `mt32emu-LGPL-2.1.txt`, `mt32emu-SOURCE.txt` |
| `Mt32emu.dll` | the `Mt32emu.net` package's .NET binding of mt32emu, by Greg Divis (below) | LGPL 2.1 or later | the same |
| the launcher's and the set-up screen's text font (inside the program) | the VGA 8x16 text font as DOSBox-X supplies it (below) | GPL 2 or later | (this project's `LICENSE`, GPL 3) |
| the readers of the game's packed programs and animations (inside the program) | written after CovertActionTools' code (below) | MIT | `CovertActionTools-MIT.txt` |

The program loads `libmt32emu.dll` and `Mt32emu.dll` at run time, so libraries rebuilt from their source
(https://github.com/munt/munt, https://github.com/gregdivis/mt32emu.net) can be put in their place. The release has no
part of the game: the publish script refuses to finish if a game file is in it, and the launcher draws no art of the
game's (it shows a picture of the player's own copy, read at run time). Roland's MT-32 and CM-32L ROMs are not included.

## CovertActionTools

Source: https://github.com/RedMike/CovertActionTools

Revision: `ec5eb257e452eda1ed8054cb530293622021f908`.
MIT licensed, copyright 2025 Mike Roibu. EXEPACK parsing and native PAN behavior were
adapted from its code and documentation; the C# rewrite's readers of them (`csharp/src/CovertAction.Content/Executable.cs`,
`PanFile.cs`) follow those, so the release carries its notice. The development-only reference adapter links
its Core project. [Full MIT notice](docs/licenses/CovertActionTools.txt).

## Covert Action Modding Tools

Source: https://github.com/jarikomppa/covert_action

Revision: `71be4a96acfac256d8701b4fe635bbc2bbc1e1d9`.
Released under the Unlicense unless individually stated. Native PIC/CAT parsing follows
the formats and algorithms documented by deanolium and Sol_HSA. Its `pic2png` converter
is compiled only as an independent test reference, including its bundled stb writer.
[Full Unlicense notice](docs/licenses/covert_action.txt).

## Other development dependencies

The recording tools (`tools/oracle`, `tools/session`) use Unicorn to run the original's instructions, Capstone to
disassemble them and Pillow to write comparison images. Their packages (`requirements.txt`) retain their own licences;
they are not needed to run the game.

## Darklands Restoration Project (C# rewrite, `csharp/`)

Source: the Darklands Restoration Project, by the same author as this project; GNU General Public
License, version 3 (the licence of this project too, `LICENSE`). Some source files of the C# rewrite
are taken from it, as they are or adapted; each says so in its first line, and
[csharp/docs/REUSED.md](csharp/docs/REUSED.md) lists them with their origin.

## VGA 8x16 text font (C# rewrite, `csharp/`)

`csharp/src/CovertAction.Host.Sdl/Resources/vga-8x16.b64` is the 8x16 text-mode (INT 10h) font as supplied by
DOSBox-X (https://dosbox-x.com, GNU General Public License v2 or later), a recreation of the IBM VGA
BIOS font. It is used to show the set-up screen in text mode, as the original does on a VGA card. It is stored
base64-encoded (4,096 bytes of 8x16 glyphs), taken from the Darklands Restoration Project, which uses it for its start-up banner.

## mt32emu (C# rewrite, `csharp/`)

The set-up's Roland MT-32 choice plays on Munt's mt32emu (https://github.com/munt/munt; copyright Dean Beeler,
Jerome Fisher and Sergey V. Mikayev), an emulation of the Roland MT-32 and CM-32L, through its NuGet package
`Mt32emu.net` 1.0.0-rc.1 (https://github.com/gregdivis/mt32emu.net, copyright Greg Divis: the .NET binding
`Mt32emu.dll` with the native `libmt32emu.dll`), as the Darklands Restoration Project does; both under the GNU Lesser
General Public License, version 2.1 or later ([full text](docs/licenses/LGPL-2.1.txt)).
No mt32emu source code is included in this repository; `csharp/src/CovertAction.Audio.Mt32` links the package.
MT-32 and CM-32L control and PCM ROMs are Roland's and are not included: the player supplies them (the folder
MT32_ROMS next to the program, or the folder `COVERT_MT32_ROMS` names).

## SDL 3 (C# rewrite, `csharp/`)

`csharp/src/CovertAction.Host.Sdl/Native/win-x64/SDL3.dll` is SDL 3.4.20, unmodified, from the
official Windows x64 release (`SDL3-3.4.20-win32-x64.zip`, https://github.com/libsdl-org/SDL/releases,
SHA-256 of the archive 368bed8b24e44ad9ea372990814f2343fcfb9e468abb7f8f4a3bc146c7304752, of the DLL
66844f10251cc1c10619ed5d8e4e3224f564900ee6d69ba87d63de782addae40). The SDL host's build output
carries it next to the program. `csharp/src/CovertAction.Host.Sdl/Native/linux-x64/libSDL3.so.0` is the same
SDL 3.4.20, unmodified, built on Ubuntu 22.04 from the official source release (`SDL3-3.4.20.tar.gz`, SHA-256
of the archive f3bb06f55bed9557227a700af2ea62e49497ebdf58037732a731a5fb3ef8581f, of the library
8bd5e82d7371c77562b815c681883341e96805921061b0ec8a0baa5b4fd6f159) by `csharp/tools/linux/build_sdl3.sh` (as the
Darklands Restoration Project does); the Linux release carries it next to the program, where it is loaded before
the system's. Source code: https://www.libsdl.org/. Its licence (zlib):

```text
Simple DirectMedia Layer
Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```
