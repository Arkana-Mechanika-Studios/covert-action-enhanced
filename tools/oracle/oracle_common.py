"""Shared definitions for the function oracle: module facts, signatures, boundary slots."""
import functools
import json
import os
import re
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
ROOT = TOOLS.parent
# Game-derived data (build/, game/) lives in the checkout; another folder can hold it, named by
# COVERT_DATA_ROOT. COVERT_ORACLE_OUT sends new vectors to a fresh folder (a rerun never overwrites).
DATA = Path(os.environ.get("COVERT_DATA_ROOT") or ROOT)
sys.path.insert(0, str(DATA / "build/python-deps"))
sys.path.insert(0, str(TOOLS))
import capstone  # noqa: E402
import executables  # noqa: E402

LABEL = re.compile(r"^sub_([a-z0-9]+)_([0-9a-f]{4})_([0-9a-f]{4})$")


def find_dgroup(exe):
    """MSC 5.1 crt0 loads DGROUP with a relocated `mov di,imm16` after the DOS version check."""
    entry = exe.cs * 16 + exe.ip
    relocated = {seg * 16 + off for seg, off in exe.relocations}
    for pos in range(entry, min(entry + 0x20, len(exe.image) - 2)):
        if exe.image[pos] == 0xBF and pos + 1 in relocated:
            return int.from_bytes(exe.image[pos + 1:pos + 3], "little")
    raise ValueError("crt0 DGROUP load not found")


GAME = DATA / "game/Covert Action/Covert"
STATES = DATA / "build/oracle/states"
OUT = Path(os.environ.get("COVERT_ORACLE_OUT") or DATA / "build/oracle")
SHARED_SIZE = 12000
NAMES = ROOT / "symbols/names.txt"
MD = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
MD.detail = True


def names(module):
    """label -> first name in symbols/names.txt (code labels of this module)."""
    out = {}
    prefix = f"sub_{module.lower()}_"
    for line in NAMES.read_text(encoding="utf-8").splitlines():
        parts = line.partition(";")[0].split()
        if len(parts) >= 2 and parts[0].startswith(prefix) and not line.startswith("#"):
            out.setdefault(parts[0], parts[1])
    return out


def label_of(module, linear):
    return f"sub_{module.lower()}_0000_{linear:04x}" if linear < 0x10000 else None


def c_params(module):
    """Function name (no underscore) -> number of int16 words in its C parameter list."""
    out = {}
    # The readable C is not published: its private copy is local/src/c. Without it the counts stay empty.
    folder = next((p for p in (ROOT / "src/c" / module.lower(), ROOT / "local/src/c" / module.lower()) if p.is_dir()), None)
    if folder is None:
        return out
    definition = re.compile(r"^[A-Za-z_][\w \t\*]*?\b(\w+)\s*\(([^;{}()]*)\)\s*$", re.M)
    for path in folder.glob("*.c"):
        text = path.read_text(encoding="utf-8", errors="replace")
        for m in definition.finditer(text):
            name, params = m.group(1), m.group(2).strip()
            if name in ("if", "while", "for", "switch", "return"):
                continue
            words = 0
            if params and params != "void":
                for p in params.split(","):
                    words += 2 if re.search(r"\blong\b", p) and "*" not in p else 1
                # K&R declarations between the header and the body.
                tail = text[m.end():m.end() + 400].split("{", 1)[0]
                for decl in re.finditer(r"\blong\s+([\w ,]+);", tail):
                    words += len([v for v in decl.group(1).split(",") if v.strip()])
            out[name] = words
    return out


def _insns(image, start, size):
    return list(MD.disasm(image[start:start + size], start))


def bp_arg_words(image, start, size, far):
    """Highest [bp+X] argument slot referenced -> number of argument words."""
    base = 6 if far else 4
    top = 0
    for i in _insns(image, start, size):
        for m in re.finditer(r"\[bp \+ (0x[0-9a-f]+|\d+)\]", i.op_str):
            x = int(m.group(1), 0)
            if x >= base:
                width = 4 if i.op_str.startswith("dword") else 2
                top = max(top, (x - base + width) // 2)
    return top


def cleanup_words(image, call_at):
    """Argument words removed by the caller after the call at `call_at`, or None."""
    ins = _insns(image, call_at, 12)
    if len(ins) < 2 or ins[0].mnemonic not in ("call", "lcall"):
        return None
    nxt = ins[1]
    m = re.fullmatch(r"sp, (0x[0-9a-f]+|\d+)", nxt.op_str)
    if nxt.mnemonic == "add" and m:
        return int(m.group(1), 0) // 2
    return 0


@functools.lru_cache(None)
def module_info(module):
    module = module.upper()
    exe = executables.load(GAME / f"{module}.EXE")
    image = exe.image
    relocs = {s * 16 + o for s, o in exe.relocations}
    dframe = find_dgroup(exe)
    db = json.loads((DATA / "build/code-analysis" / module / "functions.json").read_text(encoding="utf-8"))["functions"]
    nm = names(module)
    params = c_params(module)
    funcs = {}
    for label, r in db.items():
        linear = int(r["linear"], 16)
        funcs[label] = dict(label=label, linear=linear, frame=int(r["frame"], 16), offset=int(r["offset"], 16),
                            kind=r["kind"], size=r["size"], name=nm.get(label) or r.get("symbol"),
                            returns=r["returns"], reached_by=r["reached_by"], callers=r["caller_list"],
                            in_db=True)
    # Named entry points inside the image that the function database merged into a neighbour, and the unnamed
    # ones oracle_config.MERGED_ENTRIES lists (recorded under their label).
    import oracle_config  # noqa: E402  (imported here: the config's helpers do not need this module)
    merged = {label: None for label in oracle_config.MERGED_ENTRIES.get(module, {})}
    for label, name in list(nm.items()) + list(merged.items()):
        m = LABEL.match(label)
        if label in funcs or not m:
            continue
        frame, off = int(m.group(2), 16), int(m.group(3), 16)
        linear = frame * 16 + off
        owner = max((f for f in funcs.values() if f["linear"] <= linear), key=lambda f: f["linear"], default=None)
        size = owner["linear"] + owner["size"] - linear if owner else 0
        line = next((l for l in NAMES.read_text(encoding="utf-8").splitlines() if l.startswith(label)), "")
        kind = "lib" if ".LIB(" in line else (owner["kind"] if owner else "C")
        if size <= 0:
            size = 64
        rets = {}
        for i in _insns(image, linear, size):
            if i.mnemonic in ("ret", "retf"):
                rets[i.mnemonic] = rets.get(i.mnemonic, 0) + 1
        funcs[label] = dict(label=label, linear=linear, frame=frame, offset=off, kind=kind, size=size,
                            name=name, returns=rets, reached_by=["names.txt"], callers=[], in_db=False)
    # Callers from a linear scan of near/far call instructions (covers merged entries).
    by_linear = {f["linear"]: f for f in funcs.values()}
    call_sites = {}
    frames = sorted({f["frame"] for f in funcs.values()})
    for i in range(len(image) - 5):
        b = image[i]
        if b == 0xe8:
            rel = struct.unpack_from("<h", image, i + 1)[0]
            # near calls stay in the caller's frame (0000 or the C frame)
            for frame in frames:
                ip = i - frame * 16
                if 0 <= ip < 0x10000:
                    lin = frame * 16 + ((ip + 3 + rel) & 0xffff)
                    if lin in by_linear and by_linear[lin]["frame"] == frame:
                        call_sites.setdefault(lin, set()).add(i)
        elif b == 0x9a and (i + 3) in relocs:
            off, seg = struct.unpack_from("<HH", image, i + 1)
            lin = seg * 16 + off
            if lin in by_linear:
                call_sites.setdefault(lin, set()).add(i)
    # Execution frame: the labels normalise CODE's _TEXT to frame 0000, but C and library code
    # runs with CS = the entry frame (CODE 00b4); near pointers and jump tables depend on it.
    text = exe.cs * 16
    for f in funcs.values():
        if f["linear"] >= text and f["frame"] * 16 < text:
            f["frame"], f["offset"] = exe.cs, f["linear"] - text
    for f in funcs.values():
        f["iret"] = "iret" in f["returns"]
        far = "retf" in f["returns"] or f["iret"] or f["kind"] == "thunk" or (
            not f["returns"] and "far_call" in f["reached_by"])
        f["far"] = bool(far)
        sites = sorted(call_sites.get(f["linear"], ()))
        cleanup = [c for c in (cleanup_words(image, s) for s in sites) if c is not None]
        bp = bp_arg_words(image, f["linear"], f["size"], far) if f["kind"] not in ("thunk", "data") else 0
        cname = (f["name"] or "").lstrip("_")
        cp = params.get(cname) if f["kind"] == "C" else None
        n = max([bp] + cleanup + ([cp] if cp is not None else []))
        f["nargs"] = n
        f["c_params"] = cp
        f["call_sites"] = sites
        callers_c = any(_owner_kind(funcs, s) in ("C", "lib") for s in sites)
        f["convention"] = "stack" if (n or cp is not None or callers_c or f["kind"] == "C") else "regs"
        if f["name"] in LIB_SIGNATURES:
            f["nargs"], f["convention"] = LIB_SIGNATURES[f["name"]]
    # Driver thunk slots: `jmp far 0:0` and `call <dispatcher>` + far pointer entries.
    thunks = sorted(f["linear"] for f in funcs.values() if f["kind"] == "thunk")
    slots = {}
    if thunks:
        lo, hi = thunks[0] - 0x80, thunks[-1] + 0x80
        for a in range(max(lo, 0), hi):
            if image[a] == 0xea and image[a + 1:a + 5] == b"\0\0\0\0":
                slots[a] = "jmp"
            elif image[a] == 0xe8 and image[a + 3:a + 7] == b"\0\0\0\0":
                tgt = (a + 3 + struct.unpack_from("<h", image, a + 1)[0]) & 0xffff
                if tgt < thunks[0] and image[tgt:tgt + 2] == b"\x80\x3e":
                    slots[a] = "dispatch"
    slot_info = {}
    for a, how in slots.items():
        label = label_of(module, a)
        sites = sorted(call_sites.get(a, ()))
        if not sites:   # call sites to slots not in the database
            sites = [i for i in range(len(image) - 5) if image[i] == 0x9a and (i + 3) in relocs
                     and struct.unpack_from("<HH", image, i + 1) == (a, 0)]
        cleanup = [c for c in (cleanup_words(image, s) for s in sites) if c is not None]
        from_c = [s for s in sites if _owner_kind(funcs, s) in ("C", "lib")]
        conv = "stack" if from_c else ("regs" if sites else "stack")
        slot_info[a] = dict(label=label, name=nm.get(label) or label, how=how, convention=conv,
                            nargs=max(cleanup, default=0) if conv == "stack" else 0,
                            call_sites=len(sites))
    entry = exe.cs * 16 + exe.ip
    main = _find_main(image, entry, funcs)
    return dict(module=module, exe=exe, image=image, relocs=relocs, dgroup_frame=dframe,
                dgroup_linear=dframe * 16, stack_lo=(exe.ss - dframe) * 16,
                stack_top=(exe.ss - dframe) * 16 + exe.sp, functions=funcs, slots=slot_info,
                entry=entry, main=main)


# MSC 5.1 library entry points whose size/bp scan is misleading (shared bodies, register helpers).
LIB_SIGNATURES = {
    "_itoa": (3, "stack"), "_getch": (0, "stack"), "_getche": (0, "stack"), "_kbhit": (0, "stack"),
    "_rand": (0, "stack"), "_srand": (1, "stack"), "_abs": (1, "stack"), "_strlen": (1, "stack"),
    "_strcpy": (2, "stack"), "_strcat": (2, "stack"), "_strcmp": (2, "stack"), "_strncpy": (3, "stack"),
    "_strncmp": (3, "stack"), "_strupr": (1, "stack"), "_getenv": (1, "stack"), "_inp": (1, "stack"),
    "_time": (1, "stack"), "_int86": (3, "stack"), "__dos_getdrive": (1, "stack"),
    "__aNlmul": (4, "stack"), "__aNulmul": (4, "stack"), "__aNlshr": (0, "regs"),
    "__maperror": (0, "regs"), "__myalloc": (0, "regs"), "__amsg_exit": (0, "regs"),
    "__dosret0": (0, "regs"), "__dosreturn": (0, "regs"), "__dosretax": (0, "regs"),
    "__cxtoa": (0, "regs"), "__cltoasub": (0, "regs"), "_exit": (1, "stack"), "__exit": (1, "stack"),
}


def _owner_kind(funcs, address):
    owner = None
    for f in funcs.values():
        if f["linear"] <= address < f["linear"] + max(f["size"], 1) and f.get("in_db", True):
            owner = f
    return owner["kind"] if owner else None


def _find_main(image, entry, funcs):
    """crt0 `call _main` is the near call right before `push ax; call _exit`."""
    owner = next(f for f in funcs.values() if f["linear"] <= entry < f["linear"] + max(f["size"], 1))
    base = owner["frame"] * 16
    ins = list(MD.disasm(image[entry:entry + 0x100], entry - base))
    by_linear = {f["linear"]: label for label, f in funcs.items()}
    for a, b, c in zip(ins, ins[1:], ins[2:]):
        if a.mnemonic == "call" and b.mnemonic == "push" and b.op_str == "ax" and c.mnemonic == "call":
            lab = by_linear.get(base + (int(a.op_str, 0) & 0xffff))
            if lab:
                return lab
    return None


def entry_label(info):
    """Label of the function holding the EXE entry point (crt0 __astart)."""
    return next((l for l, f in info["functions"].items() if f["linear"] == info["entry"]), None)
