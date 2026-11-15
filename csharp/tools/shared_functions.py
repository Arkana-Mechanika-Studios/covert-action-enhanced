"""Write docs/source-map/shared-functions.csv: which functions several modules contain, and which copies are the same code.

    python -B csharp/tools/shared_functions.py [game folder]

The original's programs were linked from one library of game routines, so the same function is in several of
them. A C# method may stand for several copies only where the copies are provably the same code (ARCHITECTURE.md,
"Twins"). This tool proves it on the instructions: it disassembles every copy of every name that two or more
modules have (docs/source-map/original-functions.csv) and compares them with the places of data and code left out,
since each program has its variables and routines at its own addresses.

One row per name:
    name      the function's name
    kind      what the copies are: matched (readable C), thunk (assembly below the C), lib (C library), asm
    copies    how many modules have it
    same      groups of modules whose copies are the same instructions and the same constants, each group
              "MOD ffff:oooo=MOD ffff:oooo", groups separated by " | "; a single group means: one function
    shape     the same grouping when numbers above 255 are ignored too (constants as well as addresses): copies that
              are in one group here but not in "same" differ only in such numbers and need a look
    note      "identical" (one group in same), "constants" (one group in shape only), "differs" (more groups)

docs/source-map/twin-names.csv (module, name, same_as) lists functions that carry another name in one program than
their copies elsewhere (BUG's random is the others' random_below); the tool compares them under the common name,
so the claim is checked like every other.

Reads the player's game files to compare; writes names and coordinates only, no game bytes.
"""
import csv
import collections
import io
import re
import sys
from pathlib import Path

import capstone

CSHARP = Path(__file__).resolve().parent.parent
ROOT = CSHARP.parent
sys.path.insert(0, str(ROOT / "tools"))
import executables  # noqa: E402  (the project's reader of the game's programs)

MAP = CSHARP / "docs" / "source-map" / "original-functions.csv"
OUT = CSHARP / "docs" / "source-map" / "shared-functions.csv"
TWINS = CSHARP / "docs" / "source-map" / "twin-names.csv"
FILES = {"COVERT": "COVERT.EXE", "INTRO": "INTRO.EXE", "FINAL": "FINAL.EXE", "GAME": "GAME.EXE", "TAC": "TAC.EXE",
         "BUG": "BUG.EXE", "CHASE": "CHASE.EXE", "CODE": "CODE.EXE"}
NUMBER = re.compile(r"0x[0-9a-f]+|\b\d+\b")
MEMORY = re.compile(r"\[[^\]]*\]")
BRANCH = ("call", "lcall", "jmp", "ljmp", "loop", "loope", "loopne", "jcxz")


def linear(coordinate):
    frame, offset = coordinate.split()[1].split(":")
    return int(frame, 16) * 16 + int(offset, 16)


def is_branch(mnemonic):
    return mnemonic in BRANCH or (mnemonic.startswith("j") and mnemonic != "jmp")


def signature(image, start, end, loose):
    """The instructions of image[start:end] as text, with what depends on where the program keeps things left out:
    branch and call targets, and displacements above 255 inside memory operands. With loose, every number above 255."""
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    lines = []
    for ins in md.disasm(bytes(image[start:end]), start):
        ops = ins.op_str

        def big(match):
            return "N" if int(match[0], 0) > 0xFF else match[0]

        if is_branch(ins.mnemonic):
            ops = NUMBER.sub("T", ops)
        elif loose:
            ops = NUMBER.sub(big, ops)
        else:
            ops = MEMORY.sub(lambda m: NUMBER.sub(big, m[0]), ops)
        # an address above 8000h is shown as a negative displacement: the same thing
        ops = ops.replace("- N]", "+ N]")
        lines.append(f"{ins.mnemonic} {ops}")
    # padding between functions is not part of either
    while lines and lines[-1].split()[0] in ("nop", "add") and lines[-1] in ("nop ", "add byte ptr [bx + si], al"):
        lines.pop()
    return "\n".join(lines)


def groups(signatures):
    by = collections.defaultdict(list)
    for coordinate, text in signatures.items():
        by[text].append(coordinate)
    return sorted(by.values(), key=lambda g: (-len(g), g))


def main():
    folder = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "game" / "Covert Action" / "Covert"
    rows = list(csv.DictReader(MAP.open(encoding="utf-8")))
    # a function that goes by another name in one program (twin-names.csv: module, name, same_as) is compared under the common name
    renamed = {}
    if TWINS.exists():
        with TWINS.open(encoding="utf-8", newline="") as f:
            renamed = {(r["module"], r["name"]): r["same_as"] for r in csv.DictReader(f)}
    paired = set(renamed.values())
    starts = collections.defaultdict(list)
    names = collections.defaultdict(dict)
    for row in rows:
        module = row["coordinate"].split()[0]
        if module not in FILES:
            continue
        starts[module].append(linear(row["coordinate"]))
        # an unnamed function takes part only when twin-names.csv pairs it with a copy elsewhere
        name = renamed.get((module, row["name"]), row["name"])
        if not name.startswith("sub_") or name in paired:
            names[name][module] = row
    images = {m: executables.load(str(folder / f)).image for m, f in FILES.items()}
    for module in starts:
        starts[module].sort()

    def extent(module, coordinate):
        start = linear(coordinate)
        later = [s for s in starts[module] if s > start]
        return start, min(later[0] if later else len(images[module]), len(images[module]))

    out = io.StringIO()
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(["name", "kind", "copies", "same", "shape", "note"])
    tally = collections.Counter()
    for name, copies in sorted(names.items()):
        if len(copies) < 2:
            continue
        strict, loose = {}, {}
        for module, row in copies.items():
            start, end = extent(module, row["coordinate"])
            strict[row["coordinate"]] = signature(images[module], start, end, False)
            loose[row["coordinate"]] = signature(images[module], start, end, True)
        same, shape = groups(strict), groups(loose)
        note = "identical" if len(same) == 1 else "constants" if len(shape) == 1 else "differs"
        kind = "+".join(sorted({row["status"] for row in copies.values()}))
        tally[(kind.split("+")[0] if "+" not in kind else kind, note)] += 1
        writer.writerow([name, kind, len(copies), " | ".join("=".join(g) for g in same), " | ".join("=".join(g) for g in shape), note])
    OUT.write_text(out.getvalue(), encoding="utf-8", newline="\n")
    print(f"{OUT.relative_to(ROOT)}: {sum(tally.values())} shared names")
    for (kind, note), count in sorted(tally.items()):
        print(f"  {kind:16} {note:10} {count}")


if __name__ == "__main__":
    main()
