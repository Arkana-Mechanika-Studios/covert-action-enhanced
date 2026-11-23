"""Make a release of the C# rewrite: one folder and its zip (Windows) or tar.gz (Linux).

    python -B csharp/tools/publish_release.py                    version 1.3.0, into build/release/1.3.0
    python -B csharp/tools/publish_release.py --version 0.2.0 --out D:/somewhere
    python -B csharp/tools/publish_release.py --platform linux-x64    the Linux x64 release (experimental): CovertAction-<version>-linux-x64.tar.gz

The folder (build/release/<version>/CovertAction-<version>-win-x64) holds:
    CovertAction.exe            the game (the SDL host, self-contained: the .NET runtime is beside it)
    CovertAction Launcher.exe   the launcher program (.NET Framework 4.6.2, which every Windows 10 and 11 has):
                                it starts CovertAction.exe --setup, the settings screen
    SDL3.dll, libmt32emu.dll    SDL 3 (zlib licence) and Munt's MT-32 emulation (LGPL 2.1 or later)
    MT32_ROMS/                  empty: the player's own MT-32 ROM files go here
    README.txt                  getting started (csharp/releases/README.txt)
    RELEASE_NOTES.md            what is new (csharp/releases/<version>/release-notes.md; the launcher shows it)
    LICENSE, THIRD_PARTY_NOTICES.md, licences/   the project's licence, the notices, the third parties' licences
and the zip beside it holds that folder. Nothing of the game is in either: the player supplies his own copy.

The Linux x64 folder holds CovertAction (the game, one self-contained file), "CovertAction Launcher" (a three-line shell
script that starts it with --setup), libSDL3.so.0 (SDL 3, zlib licence) and libmt32emu.so (Munt's MT-32 emulation), MT32_ROMS/,
the documents and licences; beside it a tar.gz that keeps the executable bits (0755 on the program and the script, set
explicitly: a zip made on Windows would lose them) and a .sha256 file. It can be made on Windows, where dotnet publish
cross-compiles it.

Reads only this repository and the .NET SDK's own licence files; writes only under the output folder.
"""
import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path

CSHARP = Path(__file__).resolve().parent.parent
ROOT = CSHARP.parent
HOST = CSHARP / "src" / "CovertAction.Host.Sdl" / "CovertAction.Host.Sdl.csproj"
PLATFORMS = ("win-x64", "linux-x64")

# file types of the original game and of things made from it: none may be in a release
GAME_FILES = {".pic", ".pan", ".dta", ".sve", ".ca", ".cv", ".cat"}
GAME_PROGRAMS = {"covert.exe", "intro.exe", "final.exe", "game.exe", "tac.exe", "bug.exe", "chase.exe", "code.exe", "misc.exe",
                 "mgraphic.exe", "egraphic.exe", "cgraphic.exe", "tgraphic.exe"}


def dotnet_root():
    """The folder of the dotnet program (its LICENSE.txt and ThirdPartyNotices.txt are the runtime's)."""
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        sys.exit("dotnet is not on the PATH")
    return Path(os.path.realpath(dotnet)).parent


def sdl_licence():
    """SDL's zlib licence, as THIRD_PARTY_NOTICES.md quotes it."""
    notices = (ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
    section = notices[notices.index("## SDL 3"):]
    match = re.search(r"```text\n(.*?)```", section, re.S)
    if match is None:
        sys.exit("THIRD_PARTY_NOTICES.md: the SDL section has no licence text")
    return match.group(1)


# what a Linux archive makes executable (everything else is 0644, folders 0755)
EXECUTABLES = {"CovertAction", "CovertAction Launcher"}


def make_tar(folder, archive, name):
    """The folder as <name>/... in a tar.gz, the modes set explicitly (a file system that has none, Windows', says nothing
    of them), owner root and the files' times fixed."""
    if archive.exists():
        archive.unlink()

    def entry(info):
        info.uid = info.gid = 0
        info.uname = info.gname = "root"
        info.mtime = 0
        info.mode = 0o755 if info.isdir() or Path(info.name).name in EXECUTABLES else 0o644
        return info

    with tarfile.open(archive, "w:gz", compresslevel=9, format=tarfile.PAX_FORMAT) as t:
        t.add(folder, arcname=name, recursive=False, filter=entry)
        for path in sorted(folder.rglob("*")):
            t.add(path, arcname=f"{name}/{path.relative_to(folder).as_posix()}", recursive=False, filter=entry)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", default="1.3.2")
    parser.add_argument("--out", default=str(ROOT / "build" / "release"), help="the folder the version's folder is made in")
    parser.add_argument("--platform", default="win-x64", choices=PLATFORMS, help="the release to make (default win-x64)")
    args = parser.parse_args()
    version = args.version
    runtime = args.platform
    linux = runtime == "linux-x64"
    notes = CSHARP / "releases" / version / "release-notes.md"
    if not notes.exists():
        sys.exit(f"no release notes for {version}: write {notes} first")

    out = Path(args.out) / version
    name = f"CovertAction-{version}-{runtime}"
    folder = out / name
    if folder.exists():
        shutil.rmtree(folder)
    out.mkdir(parents=True, exist_ok=True)

    # the game, self-contained in one folder (the launcher program and MT32_ROMS come with it: the host's publish targets)
    # (the program is one compressed single file; SDL 3, mt32emu and the launcher program or script come beside it)
    publish = ["dotnet", "publish", str(HOST), "-c", "Release", "-r", runtime, "--self-contained", "true", "-nologo",
               "-o", str(folder), f"-p:Version={version}", "-p:DebugType=none", "-p:SatelliteResourceLanguages=en",
               "-p:GenerateDocumentationFile=false"]
    # one compressed single-file program on both systems, as the Darklands Restoration Project's: only the native
    # libraries (SDL 3, mt32emu) and the launcher stay beside it
    publish += ["-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
                "-p:AllowedReferenceRelatedFileExtensions=.none"]
    subprocess.run(publish, check=True)

    # the documents
    shutil.copyfile(CSHARP / "releases" / ("README-linux.txt" if linux else "README.txt"), folder / "README.txt")
    shutil.copyfile(notes, folder / "RELEASE_NOTES.md")
    shutil.copyfile(ROOT / "LICENSE", folder / "LICENSE")
    shutil.copyfile(ROOT / "THIRD_PARTY_NOTICES.md", folder / "THIRD_PARTY_NOTICES.md")
    (folder / "SETTINGS.md").write_bytes((CSHARP / "docs" / "SETTINGS.md").read_bytes())
    SDL_LIBRARY = "libSDL3.so.0" if linux else "SDL3.dll"
    licences = folder / "licences"
    licences.mkdir()
    (licences / "SDL3-zlib.txt").write_text(sdl_licence(), encoding="utf-8")
    shutil.copyfile(ROOT / "docs" / "licenses" / "LGPL-2.1.txt", licences / "mt32emu-LGPL-2.1.txt")
    MT32_LIBRARY = "libmt32emu.so" if linux else "libmt32emu.dll"
    (licences / "mt32emu-SOURCE.txt").write_text(
        f"{MT32_LIBRARY} is Munt's mt32emu (https://github.com/munt/munt; copyright Dean Beeler, Jerome Fisher and\n"
        "Sergey V. Mikayev), as the NuGet package Mt32emu.net 1.0.0-rc.1 (https://github.com/gregdivis/mt32emu.net) builds\n"
        "it, unmodified. Mt32emu.dll is that package's .NET binding (copyright Greg Divis), unmodified. Both are under the\n"
        "GNU Lesser General Public License, version 2.1 or later (mt32emu-LGPL-2.1.txt). Their source code is at those\n"
        "addresses. The program loads both at run time: libraries built from that source (or changed ones) put in their\n"
        "place are used instead.\n", encoding="utf-8")
    shutil.copyfile(ROOT / "docs" / "licenses" / "CovertActionTools.txt", licences / "CovertActionTools-MIT.txt")
    root = dotnet_root()
    for licence, target in [("LICENSE.txt", "dotnet-LICENSE.txt"), ("ThirdPartyNotices.txt", "dotnet-ThirdPartyNotices.txt")]:
        if not (root / licence).exists():
            sys.exit(f"the .NET SDK has no {licence} at {root}: the release needs it")
        shutil.copyfile(root / licence, licences / target)
    (licences / "README.txt").write_text(
        "The licences of what this release carries besides the project's own code (LICENSE, GNU GPL version 3):\n"
        f"  SDL3-zlib.txt                 {SDL_LIBRARY}, SDL 3 (zlib licence)\n"
        f"  mt32emu-LGPL-2.1.txt          {MT32_LIBRARY}, Munt's MT-32 emulation, and Mt32emu.dll, its .NET binding\n"
        "                                (LGPL 2.1 or later); where their source is: mt32emu-SOURCE.txt\n"
        "  dotnet-LICENSE.txt            the .NET runtime beside the program (MIT licence)\n"
        "  dotnet-ThirdPartyNotices.txt  the .NET runtime's third-party notices\n"
        "  CovertActionTools-MIT.txt     CovertActionTools (MIT): the readers of the game's packed programs and\n"
        "                                animations follow its code\n"
        "The VGA 8x16 text font is DOSBox-X's (GNU GPL v2 or later); more in THIRD_PARTY_NOTICES.md.\n", encoding="utf-8")
    (folder / "MT32_ROMS").mkdir(exist_ok=True)
    (folder / "MT32_ROMS" / "README.txt").write_text(
        "Put the ROM files of your own Roland MT-32 (or CM-32L) here, for the sound card \"Roland MT-32\":\n"
        "the control ROM and the PCM ROM (for example MT32_CONTROL.ROM and MT32_PCM.ROM). None come with the program.\n"
        "Without them the AdLib driver plays instead. The launcher's Advanced tab can name another folder.\n", encoding="utf-8")

    # nothing of the game may have come along
    for path in folder.rglob("*"):
        if path.is_file() and (path.suffix.lower() in GAME_FILES or path.name.lower() in GAME_PROGRAMS):
            sys.exit(f"a game file is in the release: {path}")
    programs = (["CovertAction", "CovertAction Launcher", "libSDL3.so.0", "libmt32emu.so", "Mt32emu.dll"] if linux
                else ["CovertAction.exe", "CovertAction Launcher.exe", "SDL3.dll", "libmt32emu.dll", "Mt32emu.dll"])
    for needed in [*programs, "README.txt", "RELEASE_NOTES.md", "LICENSE",
                   "THIRD_PARTY_NOTICES.md", "licences/README.txt", "licences/SDL3-zlib.txt", "licences/mt32emu-LGPL-2.1.txt",
                   "licences/mt32emu-SOURCE.txt", "licences/dotnet-LICENSE.txt", "licences/dotnet-ThirdPartyNotices.txt",
                   "licences/CovertActionTools-MIT.txt"]:
        if not (folder / needed).exists():
            sys.exit(f"the release lacks {needed}")

    if linux:
        archive = out / f"{name}.tar.gz"
        make_tar(folder, archive, name)
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        (out / f"{archive.name}.sha256").write_text(f"{digest} *{archive.name}\n", encoding="utf-8", newline="\n")
        kind = "tar.gz"
    else:
        archive = out / f"{name}.zip"
        if archive.exists():
            archive.unlink()
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for path in sorted(folder.rglob("*")):
                z.write(path, Path(name) / path.relative_to(folder))
        kind = "zip"
    size = archive.stat().st_size
    files = sum(1 for p in folder.rglob("*") if p.is_file())
    print(f"release folder: {folder} ({files} files)")
    print(f"{kind}: {archive} ({size / 1024 / 1024:.1f} MB)")


if __name__ == "__main__":
    main()
