"""Where sessions live and how they are written: the paths, the key script, the recording format.

Shared by the original side (`original_side.py`) and read by the C# side (`CovertAction.Session.Tests`), so
everything a recording holds is described here once (csharp/docs/SESSION-PROOF.md, "Storage").

A session spec (committed, `tools/session/sessions/<part>/<name>.json`) holds only inputs a person wrote: the
part, the start image to start from (a captured state of `tools/oracle`, by name), changes to the hand-off
block, the time of day the game reads, and the key script. No game bytes.

A recording (`build/session/<PART>/<name>/original-<stamp>/`, never committed, never overwritten, never
deleted) holds what the original did with those inputs:

    meta.json         the spec, the SHA-256 of every input (game files, start image, original-side sources),
                      the stop reason, counts
    inputs.json       the resolved inputs both sides are fed: key words (null = a poll that finds no key),
                      the time of day
    handoff-in.bin    the 12000-byte hand-off block the part starts with (the start image's, or with `handoff_from`
                      the block an earlier session or capture of the original left)
    disk/<NAME>       with `disk`: files on the player's disk besides the game's (a saved game an earlier session
                      wrote), fed alike to the C# side
    handoff-out.bin   the block as the part left it (at its exit, or when the script ran out)
    written/<NAME>    the files the part created or opened to write, as they were at the end (the player's folder
                      is never written)
    checkpoints.jsonl one line per checkpoint (below)
    frames/<sha>.idx  each distinct picture once: 64000 palette indices, then 768 DAC bytes (6-bit, R G B)

A checkpoint is the moment the program takes the next entry of the key script: a key, or a poll that finds
no key. At that moment the picture on the screen, the colours, the hand-off block and the calls the part
made to the outside since the previous checkpoint (sound, disk) are recorded, and (format 2) the SHA-256 of every
file written so far when one of them changed since the previous checkpoint (`written`).
"""
import hashlib
import json
import os
import re
import subprocess
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
SESSIONS = HERE / "sessions"

# The 12000 bytes the launcher hands from part to part (the reconstructed state.h).
SHARED_SIZE = 12000
SCREEN_BYTES = 320 * 200
PALETTE_BYTES = 256 * 3
# 2: a checkpoint holds `written` (name -> SHA-256) when the written files changed since the previous one
FORMAT = 2
# The folder of a recording that holds the files the part created or opened to write, under their names.
WRITTEN = "written"
# The folder of a recording that holds the files found on the player's disk besides the game's (spec `disk`).
DISK = "disk"


def data_root():
    """The checkout that holds the player's game and build/ (game files and recordings are never in a linked working tree).

    COVERT_ROOT if set; else the main checkout of this git repository (the parent of its common git folder).
    """
    if os.environ.get("COVERT_ROOT"):
        return Path(os.environ["COVERT_ROOT"])
    try:
        common = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=REPO, capture_output=True,
                                text=True, check=True).stdout.strip()
        return (REPO / common).resolve().parent
    except (OSError, subprocess.CalledProcessError):
        return REPO


def game_dir():
    """The player's game folder: COVERT_GAME_DIR, else game/Covert Action/Covert under the data root."""
    env = os.environ.get("COVERT_GAME_DIR")
    return Path(env) if env else data_root() / "game/Covert Action/Covert"


def session_root():
    """Where recordings and comparisons go: COVERT_SESSION_DIR, else build/session under the data root."""
    env = os.environ.get("COVERT_SESSION_DIR")
    return Path(env) if env else data_root() / "build/session"


def states_root():
    """The captured start images of tools/oracle: COVERT_ORACLE_DIR/states, else build/oracle/states."""
    env = os.environ.get("COVERT_ORACLE_DIR")
    return (Path(env) if env else data_root() / "build/oracle") / "states"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def latest_recording(session):
    """The latest finished original recording of a session named "PART/name" (as the C# test picks it)."""
    part, name = session.split("/")
    folders = sorted(f for f in (session_root() / part.upper() / name).glob("original-*") if (f / "meta.json").is_file())
    if not folders:
        raise FileNotFoundError(f"no recording of {session}: record it first")
    return folders[-1]


def new_run_folder(part, name, side):
    """A fresh folder for one run: <session root>/<PART>/<name>/<side>-<UTC stamp>. Never reuses one."""
    stamp = time.strftime("%Y%m%d-%H%M%S", time.gmtime())
    base = session_root() / part.upper() / name
    folder = base / f"{side}-{stamp}"
    n = 1
    while folder.exists():
        n += 1
        folder = base / f"{side}-{stamp}-{n}"
    folder.mkdir(parents=True)
    (folder / "frames").mkdir()
    return folder


# ---------------------------------------------------------------- the key script
# The notation of the headless host (csharp/src/CovertAction.Host.Headless/Program.cs): a character is its key,
# {name} names another, {wait} is one poll that finds no key, {wait n} is n of them.
NAMED_KEYS = {
    "enter": 0x1C0D, "esc": 0x011B, "up": 0x4800, "down": 0x5000, "left": 0x4B00, "right": 0x4D00,
    "f1": 0x3B00, "f10": 0x4400, "alt-q": 0x1000, "alt-p": 0x1900, "alt-s": 0x1F00, "space": 0x3920,
    "backspace": 0x0E08, "alt-a": 0x1E00, "alt-j": 0x2400,
    "f2": 0x3C00, "f3": 0x3D00, "f4": 0x3E00, "f5": 0x3F00, "f6": 0x4000, "f7": 0x4100, "f8": 0x4200, "f9": 0x4300,
    "home": 0x4700, "end": 0x4F00, "pgup": 0x4900, "pgdn": 0x5100,
}
# A key of the numeric keypad (item 5, the raid walks with it): HostKeys.Keypad (0x10000) over the cursor key's word.
# What the program reads depends on Num Lock at the moment the key is pressed (the BIOS keyboard flags at 0040:0017,
# bit 20h, which the raid switches): its digit (keypad 8: 4838h) while on, the cursor key (4800h) while off. Both sides
# resolve it when the key is taken (`resolve_keypad`, HostKeys.ResolveKeypad in the C#'s keyboard driver).
KEYPAD = 0x10000
KEYPAD_KEYS = {"kp1": 0x4F00, "kp2": 0x5000, "kp3": 0x5100, "kp4": 0x4B00, "kp5": 0x4C00, "kp6": 0x4D00,
               "kp7": 0x4700, "kp8": 0x4800, "kp9": 0x4900}
KEYPAD_DIGITS = {0x47: "7", 0x48: "8", 0x49: "9", 0x4B: "4", 0x4C: "5", 0x4D: "6", 0x4F: "1", 0x50: "2", 0x51: "3"}


def resolve_keypad(entry, num_lock):
    """The BIOS word of a script entry: a keypad key by Num Lock (its digit while on), any other key as it is."""
    if entry is None or not entry & KEYPAD:
        return entry
    scan = entry >> 8 & 0xFF
    return scan << 8 | ord(KEYPAD_DIGITS[scan]) if num_lock else entry & 0xFFFF
# Scan codes of A..Z (PC set 1), as HostKeys.LetterScanCodes.
LETTER_SCAN = [0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
               0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C]
PUNCTUATION = {"0": 0x0B30, " ": 0x3920, "-": 0x0C2D, "=": 0x0D3D, "+": 0x0D2B, "'": 0x2827, ".": 0x342E,
               ",": 0x332C}


def key_for_character(c):
    """The BIOS key word (scan code high, character low) of a typed character, as HostKeys.ForCharacter."""
    if "a" <= c <= "z" or "A" <= c <= "Z":
        return LETTER_SCAN[ord(c.lower()) - ord("a")] << 8 | ord(c)
    if "1" <= c <= "9":
        return (ord(c) - ord("1") + 2) << 8 | ord(c)
    if c in PUNCTUATION:
        return PUNCTUATION[c]
    raise ValueError(f"no key for {c!r} in the key script")


def parse_keys(script):
    """The entries of a key script: BIOS key words, None for a poll that finds no key."""
    entries, i = [], 0
    while i < len(script):
        close = script.find("}", i) if script[i] == "{" else -1
        name = script[i + 1:close].lower() if close > i else ""
        wait = re.fullmatch(r"wait(?: (\d+))?", name)
        if wait:
            entries.extend([None] * int(wait.group(1) or 1))
            i = close + 1
        elif close > i:
            if name in KEYPAD_KEYS:
                entries.append(KEYPAD | KEYPAD_KEYS[name])
            elif name not in NAMED_KEYS:
                raise ValueError(f"unknown key {{{name}}}")
            else:
                entries.append(NAMED_KEYS[name])
            i = close + 1
        else:
            entries.append(key_for_character(script[i]))
            i += 1
    return entries


def load_spec(path):
    """A session spec: {name, part, start, handoff_from?, handoff: {field: value}, disk?, unix_time, keys, note}."""
    spec = json.loads(Path(path).read_text(encoding="utf-8"))
    spec.setdefault("name", Path(path).stem)
    return spec


class RecordingWriter:
    """Writes one side's run in the recording format: checkpoints in order, each distinct picture once."""

    def __init__(self, folder):
        self.folder = Path(folder)
        self.lines = []
        self.frames = set()

    def checkpoint(self, *, entry, service, screen, palette, handoff, calls, frames, written=None):
        """Records the moment the program takes script entry number len(lines) (a key word or None)."""
        picture = bytes(screen) + bytes(palette)
        digest = sha256(picture)
        if digest not in self.frames:
            (self.folder / "frames" / f"{digest}.idx").write_bytes(picture)
            self.frames.add(digest)
        line = dict(n=len(self.lines), entry=entry, service=service, picture=digest,
                    handoff=sha256(bytes(handoff)), calls=calls, frames=frames)
        if written is not None:
            line["written"] = written
        self.lines.append(line)

    def close(self):
        with open(self.folder / "checkpoints.jsonl", "w", encoding="utf-8", newline="\n") as out:
            for line in self.lines:
                out.write(json.dumps(line, separators=(",", ":")) + "\n")
