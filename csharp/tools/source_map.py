"""Write docs/source-map/original-functions.csv: every function of the original and where the C# has it.

    python -B csharp/tools/source_map.py            rewrite the file
    python -B csharp/tools/source_map.py --check    exit 1 when the file on disk is out of date

One row per original function:
    coordinate  <MODULE> <frame>:<offset>, the function's place in the original program (hex, as in symbols/)
    name        its name in symbols/names.txt without the leading underscore (MicroProse's own where known),
                or the generated label when it has none
    source      src/c/<module>/<file>.c for compiler output, the library member for run-time library routines, empty otherwise
                (the readable C is not published: it is read from src/c, or from the private copy local/src/c;
                without either the tool prints a message and leaves the file as it is)
    status      matched  readable C that compiles to the original bytes
                lib      a Microsoft C 5.1 library routine
                thunk    a routine of frame 0000 below the compiler's output: a far-call thunk into a loaded
                         driver (graphics, sound, keyboard/joystick) or MicroProse's assembly library; no C
                asm      other hand-written assembly (symbols/assembly.txt, and named entries without C)
                driver   a routine of a driver program the launcher loads (MGRAPHIC.EXE, MISC.EXE, ISOUND.CA, ASOUND.CA, RSOUND.CA, TSOUND.CA): hand-written
                         assembly behind a thunk; listed by hand in docs/source-map/additional-entries.csv
                data     not code (symbols/assembly.txt)
    csharp      the C# member that reconstructs it (Namespace.Type.Member), or "not ported"
    note        for a function that is not ported and will not be as it stands: the reason
                (docs/source-map/not-ported.csv); empty while the function simply has not been reached yet

The csharp column is read from the "// orig:" comments in csharp/src (CONTRIBUTING.md "Traceability"): the first
coordinate of a comment and every further full coordinate after "also" map to the declaration under the comment.

docs/source-map/additional-entries.csv lists places the project's symbol files do not have yet, one per line:
coordinate, name, status. These are the driver routines the C# reconstructs (the drivers have no symbol file) and
unnamed routines of MicroProse's assembly library inside the modules.

docs/source-map/not-ported.csv holds the reasons, one rule per line: module, match, reason. A match is a coordinate
(0000:052d), a name (attach_driver_segment), a status with an optional name pattern (lib, lib:_str*, thunk:sub_*) or *.
The first rule that matches a row wins.

Reads only our own sources and symbol files; no game bytes.
"""
import csv
import fnmatch
import io
import re
import sys
from pathlib import Path

CSHARP = Path(__file__).resolve().parent.parent
ROOT = CSHARP.parent
OUT = CSHARP / "docs" / "source-map" / "original-functions.csv"
REASONS = CSHARP / "docs" / "source-map" / "not-ported.csv"
ADDITIONAL = CSHARP / "docs" / "source-map" / "additional-entries.csv"
MODULES = ["COVERT", "INTRO", "FINAL", "GAME", "TAC", "BUG", "CHASE", "CODE", "MGRAPHIC", "MISC", "ISOUND", "ASOUND", "RSOUND", "TSOUND"]
NOT_PORTED = "not ported"
# The readable C of the original's functions is not published; the private copy lives in local/src/c.
C_SOURCES = next((p for p in (ROOT / "src" / "c", ROOT / "local" / "src" / "c") if p.is_dir()), None)

LABEL = re.compile(r"^sub_([a-z]+)_([0-9a-f]{4})_([0-9a-f]{4})\s+(\S+)\s*(?:;\s*(.*))?$")
ASM = re.compile(r"^sub_([a-z]+)_([0-9a-f]{4})_([0-9a-f]{4})\s+(asm|data)\b")
PLACE = re.compile(r"/\* place: ([a-z]+) ([0-9a-f]{4}):([0-9a-f]{4}) \*/")
REFERENCE = re.compile(r"\bsub_([a-z]+)_([0-9a-f]{4})_([0-9a-f]{4})\b")
LIBRARY = re.compile(r"^(\S+\.LIB\([^)]*\))")
COORDINATE = re.compile(r"\b(" + "|".join(MODULES) + r") ([0-9a-f]{4}:[0-9a-f]{4})\b")
ORIG = re.compile(r"^\s*// orig: (.*)$")
TYPE = re.compile(r"\b(?:class|struct|interface|record)\s+([A-Za-z_]\w*)")
NAMESPACE = re.compile(r"^namespace\s+([\w.]+)\s*;")
MEMBER = re.compile(r"([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(")


def original_functions():
    """coordinate -> [name, source, status] from src/c, symbols/names.txt and symbols/assembly.txt."""
    names, libraries = {}, {}
    for line in (ROOT / "symbols" / "names.txt").read_text(encoding="utf-8", errors="replace").splitlines():
        m = LABEL.match(line)
        if not m:
            continue
        key = f"{m[1].upper()} {m[2]}:{m[3]}"
        names.setdefault(key, m[4].lstrip("_") if m[4].startswith("_") and not m[4].startswith("__") else m[4])
        lib = LIBRARY.match(m[5] or "")
        if lib:
            libraries.setdefault(key, lib[1])
    rows = {}
    for key, lib in libraries.items():
        rows[key] = [names[key], lib, "lib"]
    for line in (ROOT / "symbols" / "assembly.txt").read_text(encoding="utf-8").splitlines():
        m = ASM.match(line)
        if m:
            key = f"{m[1].upper()} {m[2]}:{m[3]}"
            rows[key] = [names.get(key, f"sub_{m[1]}_{m[2]}_{m[3]}"), "", m[4]]
    assembly = dict(rows)
    texts = {}      # module -> [(file, text)]
    first_c = {}    # module -> lowest offset of compiler output in frame 0000
    for path in sorted(C_SOURCES.glob("*/*.c")):
        text = path.read_text(encoding="utf-8", errors="replace")
        m = PLACE.search(text)
        if not m:
            raise SystemExit(f"{path}: no '/* place: module ffff:oooo */' line")
        key = f"{m[1].upper()} {m[2]}:{m[3]}"
        source = "src/c/" + path.relative_to(C_SOURCES).as_posix()
        rows[key] = [names.get(key, f"sub_{m[1]}_{m[2]}_{m[3]}"), source, "matched"]
        texts.setdefault(m[1].upper(), []).append((source, text))
        if m[2] == "0000":
            first_c[m[1].upper()] = min(first_c.get(m[1].upper(), "ffff"), m[3])

    def classify(key, name):
        """A named or referenced entry without a file of its own: a further function of a src/c file, a routine of
        frame 0000 below the compiler's output (driver thunks, MicroProse's assembly library), or other assembly."""
        module, place = key.split()
        frame, offset = place.split(":")
        definition = re.compile(r"^[A-Za-z_][\w \*]*\b" + re.escape(name) + r"\s*\([^;]*$", re.M)
        source = next((file for file, text in texts.get(module, []) if definition.search(text)), None)
        if source:
            return [name, source, "matched"]
        if frame == "0000" and offset < first_c.get(module, "ffff"):
            return [name, "", "thunk"]
        return [name, "", "asm"]

    for key, name in names.items():
        if key not in rows:
            rows[key] = classify(key, name)
    # labels the C calls that have no name yet (unnamed driver slots such as sub_code_0000_0208)
    for module, files in texts.items():
        for _, text in files:
            for m in REFERENCE.finditer(text):
                key = f"{m[1].upper()} {m[2]}:{m[3]}"
                if key not in rows:
                    rows[key] = classify(key, m[0])
    # the hand-written routines of frame 0000 listed in assembly.txt are part of the same thunk and library area
    for key, row in assembly.items():
        module, place = key.split()
        frame, offset = place.split(":")
        if row[2] == "asm" and frame == "0000" and offset < first_c.get(module, "ffff"):
            row[2] = "thunk"
    # places listed by hand: driver routines, unnamed routines of the assembly library
    if ADDITIONAL.exists():
        with ADDITIONAL.open(encoding="utf-8", newline="") as f:
            for entry in csv.DictReader(f):
                key = entry["coordinate"]
                if key in rows:
                    raise SystemExit(f"{ADDITIONAL.name}: {key} is already known as {rows[key][0]}")
                rows[key] = [entry["name"], "", entry["status"]]
    return rows


def csharp_members():
    """coordinate -> Namespace.Type[.Member] from the // orig: comments of csharp/src."""
    found = {}
    for path in sorted((CSHARP / "src").rglob("*.cs")):
        if {"bin", "obj"} & set(path.parts):
            continue
        lines = path.read_text(encoding="utf-8").splitlines()
        namespace, current_type = "", ""
        for i, line in enumerate(lines):
            ns = NAMESPACE.match(line)
            if ns:
                namespace = ns[1]
            if not line.lstrip().startswith("//"):
                t = TYPE.search(line)
                if t:
                    current_type = t[1]
            m = ORIG.match(line)
            if not m:
                continue
            coordinates = [f"{c[0]} {c[1]}" for c in COORDINATE.findall(m[1])]
            if not coordinates:
                continue  # a data coordinate (DS:, SHARED+): not a function
            declaration = next((l for l in lines[i + 1:] if l.strip() and not l.lstrip().startswith(("//", "["))), "")
            t = TYPE.search(declaration)
            if t:
                member = f"{namespace}.{t[1]}"
            else:
                name = MEMBER.search(declaration)
                if not name:
                    raise SystemExit(f"{path}:{i + 1}: no declaration under the // orig: comment")
                member = f"{namespace}.{current_type}.{name[1]}"
            for coordinate in coordinates:
                if found.setdefault(coordinate, member) != member:
                    raise SystemExit(f"{path}:{i + 1}: {coordinate} is already claimed by {found[coordinate]}")
    return found


def reasons():
    """The rules of not-ported.csv and of its per-module parts (not-ported-FINAL.csv, ...): [(module, match, reason)]."""
    rules = []
    for path in sorted(REASONS.parent.glob("not-ported*.csv")):
        with path.open(encoding="utf-8", newline="") as f:
            rules += [(r["module"], r["match"], r["reason"]) for r in csv.DictReader(f)]
    return rules


def reason_for(rules, key, name, status):
    module, place = key.split()
    for rule_module, match, reason in rules:
        if rule_module != module:
            continue
        if match in ("*", place, name, status):
            return reason
        if ":" in match and not re.fullmatch(r"[0-9a-f]{4}:[0-9a-f]{4}", match):
            rule_status, pattern = match.split(":", 1)
            if rule_status == status and fnmatch.fnmatchcase(name, pattern):
                return reason
    return ""


def render():
    rows, members, rules = original_functions(), csharp_members(), reasons()
    unknown = sorted(set(members) - set(rows))
    if unknown:
        raise SystemExit("// orig: comments cite functions the original does not have: " + ", ".join(unknown))
    out = io.StringIO()
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(["coordinate", "name", "source", "status", "csharp", "note"])
    order = {m: i for i, m in enumerate(MODULES)}
    for key in sorted(rows, key=lambda k: (order[k.split()[0]], k.split()[1])):
        member = members.get(key, NOT_PORTED)
        note = reason_for(rules, key, rows[key][0], rows[key][2]) if member == NOT_PORTED else ""
        writer.writerow([key, *rows[key], member, note])
    return out.getvalue()


def main():
    if C_SOURCES is None:
        print("source_map: the readable C of the original's functions (src/c, or its private copy local/src/c) is not "
              "here; the map's source and status columns cannot be rebuilt, so docs/source-map/original-functions.csv "
              "is left as it is.")
        return
    text = render()
    if "--check" in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding="utf-8") != text:
            raise SystemExit(f"{OUT} is out of date: run python -B csharp/tools/source_map.py")
        return
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    lines = list(csv.reader(io.StringIO(text)))[1:]
    print(f"{OUT.relative_to(ROOT)}: {len(lines)} functions")
    for module in MODULES:
        mine = [l for l in lines if l[0].startswith(module + " ")]
        ported = sum(1 for l in mine if l[4] != NOT_PORTED)
        reasoned = sum(1 for l in mine if l[4] == NOT_PORTED and l[5])
        print(f"  {module:6} {len(mine):4} functions: {ported:4} with a C# member, {reasoned:4} not ported with a reason, "
              f"{len(mine) - ported - reasoned:4} open")


if __name__ == "__main__":
    main()
