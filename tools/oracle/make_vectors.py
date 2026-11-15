"""Produce function-level test vectors for a module from realistic states plus coverage-guided fuzzing.

    python -B tools/oracle/make_vectors.py CODE [labels or names...] [--tries N]

Needs captured states (tools/oracle/capture_states.py) under build/oracle/states/<MODULE>/.
Writes build/oracle/<MODULE>/<label>.json (format in tools/oracle/README.md), plus
_index.json (per-function summary, skips and reasons) and _boundaries.json (thunk slots).
Game-derived output: never commit it.
"""
import argparse
import copy
import gzip
import hashlib
import json
import pickle
import random
import struct
import sys
import time
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import oracle_common as oc  # noqa: E402
import oracle_config as cfg  # noqa: E402
import run_function as rf  # noqa: E402

SKIP = {
    "data": "not code (zero bytes at the start of the code segment; null far-call target)",
}
SKIP_LABELS = {
    "CODE": {
        "sub_code_0000_00ca": "driver-call trampoline, not a function: it takes its operand from its own "
                              "return address (the far pointer stored after each `call 00ca` thunk slot) and "
                              "returns through the slot caller's frame; exercised by every vector whose "
                              "boundary goes through a call-00ca slot (_show_page, 016c, 01d5)",
        "sub_code_0000_2fbc": "__dosret0: shared epilogue tail of the DOS wrappers (`mov sp,bp; pop bp; ret` "
                              "for the wrapper's frame), entered by jump; not callable on its own",
        "sub_code_0000_2fc4": "__dosreturn: shared epilogue tail of the DOS wrappers, entered by jump with the "
                              "wrapper's BP frame and carry flag; not callable on its own",
        "sub_code_0000_2fcf": "__dosretax: shared epilogue tail of the DOS wrappers, entered by jump with the "
                              "wrapper's BP frame and carry flag; not callable on its own",
        "sub_code_0000_35ca": "__cltoasub: library-internal helper that reads its caller's [bp+4..0a] frame "
                              "(ltoa/ultoa family) and falls into __cxtoa; covered by the _itoa vectors",
        "sub_code_0000_35d6": "__cxtoa: library-internal conversion tail entered by `jmp` from _itoa with "
                              "registers and _itoa's BP frame set up; covered by the _itoa vectors",
        "sub_code_0000_0959": "CPU divide-error handler (INT 0 vector installed by the line clipper 07ac around "
                              "its IDIVs): entered by a CPU exception with an IRET frame and it patches the "
                              "faulting IP (+4, compares it with 08ea); not callable as a function; the oracle "
                              "delivers the exception to it inside 07ac/_draw_line vectors",
    },
    "GAME": {
        "sub_game_0000_00ca": "driver-call trampoline, not a function (the same code as CODE's 00ca): called on its "
                              "own it pops its caller's return address and jumps far through CS:0000, running the "
                              "whole program to its exit; exercised by every vector whose boundary goes through it",
    },
}
KEY_POOL = ["ESC", "ENTER", "SPACE", "UP", "DOWN", "LEFT", "RIGHT", "F1", "F10", "ALT_Q", "ALT_P", "ALT_S",
            "ALT_J", "a", "b", "u", "x", "z", "1", "2", "y", "n", "{", "|", "`", "[", "WAIT"]
VALUES = [0, 1, 2, 3, 4, 5, 7, 10, 19, 20, 25, 26, 0x7f, 0x80, 0xff, 0x100, 0x13f, 0x140, 0xc7, 0xc8,
          0x7fff, 0x8000, 0xfffe, 0xffff]
# Key scripts given in turn to the realistic seeds of a function (fuzzing varies them further).
KEY_SCRIPTS = (["ESC"], ["u", "a"], ["F10"], ["F1"], ["ALT_S"], ["ENTER"], ["DOWN", "ENTER"], ["x", "y"])
SCRATCH = (0xf000, 0xf100, 0xf200)          # DGROUP areas for synthetic pointer arguments
SCRATCH_TEXT = (b"Hello, World 123\0", b"Help\0", b"\0")


def load_states(module):
    folders = sorted(p for p in (oc.STATES / module).iterdir() if p.is_dir() and (p / "meta.json").exists())
    bases, captures = {}, []
    for folder in folders:
        for which in ("start", "wait"):
            bases[(folder.name, which)] = rf.Base.from_states(folder, which)
        with gzip.open(folder / "captures.pkl.gz", "rb") as fh:
            for c in pickle.load(fh):
                c["route"] = folder.name
                if "dgroup_z" in c:     # capture_all.py stores DGROUP and shared block compressed
                    c["dgroup"] = zlib.decompress(c.pop("dgroup_z"))
                    z = c.pop("shared_z")
                    c["shared"] = zlib.decompress(z) if z else None
                captures.append(c)
    return bases, captures


def stack_args(capture, base, func):
    """Entry SP (DGROUP-relative) and the argument words of a captured entry."""
    regs = capture["regs"]
    lin = regs["ss"] * 16 + regs["sp"]
    sp = lin - base.dgroup * 16
    retsize = 6 if func.get("iret") else (4 if func["far"] else 2)
    n = func["nargs"]
    words = [int.from_bytes(capture["stack"][retsize + 2 * i:retsize + 2 * i + 2], "little") for i in range(n)]
    return sp, words


class Fuzzer:
    def __init__(self, module, label, bases, captures, rng, tries, max_cases, log, min_cases=3):
        self.module, self.label = module, label
        self.info = oc.module_info(module)
        self.f = self.info["functions"][label]
        self.bases, self.rng, self.tries, self.max_cases, self.log = bases, rng, tries, max_cases, log
        self.min_cases = min_cases
        self.mine = [c for c in captures if c["label"] == label]
        self.captures = captures
        self.cases, self.coverage, self.failures = [], set(), []
        self.with_regs = self.f["kind"] != "C"
        strings = json.loads((oc.DATA / "build/code-analysis" / module / "strings.json").read_text(encoding="utf-8"))
        self.strings = [int(k, 16) for k in strings if int(k, 16) >= 0x42]

    # the typed model (oracle_config) -------------------------------------------------------
    def specs(self):
        given = cfg.ARGS.get(self.module, {}).get(self.f["name"])
        n = self.f["nargs"]
        return list(given[:n]) + ["seen"] * (n - len(given)) if given else ["seen"] * n

    def retsize(self):
        return 6 if self.f.get("iret") else (4 if self.f["far"] else 2)

    def default_sp(self):
        return self.info["stack_top"] - 0x100 - 2 * self.f["nargs"] - self.retsize()

    def local_ptr(self, sp, k=0):
        return (sp if sp is not None else self.default_sp()) + self.retsize() + 2 * self.f["nargs"] + 2 + 2 * k

    def arg_ok(self, spec, v, pos, seed):
        sv = v - 0x10000 if v & 0x8000 else v
        if spec == "int":
            return True
        if spec == "port":
            return v in cfg.PORTS
        if spec == "seen":
            return v in self.seen_args.get(pos, ())
        if spec in ("cstr", "buf"):
            if not 0x42 <= v < 0xfe00 or self.info["stack_lo"] <= v < self.info["stack_top"]:
                return False
            return spec == "buf" or 0 in seed["dgroup"][v:v + 1024]
        if spec == "local":
            return v == self.local_ptr(seed["sp"], pos)
        if spec[0] == "set":
            return sv in spec[1] or v in spec[1]
        return spec[0] <= sv <= spec[1]

    def args_ok(self, seed):
        args = seed["args"]
        if not all(self.arg_ok(sp_, v & 0xffff, i, seed) for i, (sp_, v) in enumerate(zip(self.specs(), args))):
            return False
        check = cfg.ARG_CHECKS.get(self.module, {}).get(self.f["name"])
        signed = [v - 0x10000 if v & 0x8000 else v for v in (a & 0xffff for a in args)]
        return check is None or bool(check(signed))

    def arg_value(self, spec, pos, seed):
        rng = self.rng
        if spec == "int":
            return rng.choice(VALUES + [rng.randrange(0x10000)])
        if spec == "port":
            return rng.choice(cfg.PORTS)
        if spec == "seen":
            pool = sorted(self.seen_args.get(pos, ()))
            return rng.choice(pool) if pool else 0
        if spec == "cstr":
            return rng.choice(self.strings + [SCRATCH[0], SCRATCH[1]])
        if spec == "buf":
            return rng.choice(SCRATCH)
        if spec == "local":
            return self.local_ptr(seed["sp"], pos)
        if spec[0] == "set":
            return rng.choice(spec[1]) & 0xffff
        lo, hi = spec
        return rng.choice([lo, hi, min(lo + 1, hi), max(hi - 1, lo), rng.randint(lo, hi)]) & 0xffff

    def model_entries(self, reads):
        """In-model globals/shared fields this seed reads: (space, offset, name, count, width, allowed)."""
        out = []
        rs = set(reads)
        for space, table, bias in (("D", cfg.GLOBALS.get(self.module, {}), 0),
                                   ("S", cfg.SHARED.get(self.module, {}), 0x10000)):
            for off, (name, count, width, allowed, _) in table.items():
                if any(bias + off + i in rs for i in range(count * width)):
                    out.append((space, off, name, count, width, allowed))
        return out

    # seeds ---------------------------------------------------------------------------------
    def seeds(self):
        f = self.f
        out = []
        seen = set()
        self.seen_args = {}
        entry_label = oc.entry_label(self.info)
        for c in self.mine:
            if self.label != entry_label:
                base = self.bases[(c["route"], "wait" if c["after_main"] else "start")]
                _, args = stack_args(c, base, f)
                for i, v in enumerate(args):
                    self.seen_args.setdefault(i, set()).add(v)
        for c in self.mine:
            base = self.bases[(c["route"], "wait" if c["after_main"] else "start")]
            keys = KEY_SCRIPTS[len(out) % len(KEY_SCRIPTS)]
            if self.label == entry_label:
                for keys in KEY_SCRIPTS[:3]:
                    key = (hashlib.sha1(c["dgroup"] + (c["shared"] or b"")).hexdigest(), tuple(keys))
                    if key in seen:
                        continue
                    seen.add(key)
                    out.append(dict(base=base, entry=True, dgroup=c["dgroup"], shared=c["shared"], args=[],
                                    sp=None, regs=None, keys=keys, in_model=True,
                                    origin=f"capture {c['route']} hit {c['hit']}"))
                continue
            sp, args = stack_args(c, base, f)
            if not 0 <= sp < base.dsize:
                continue
            regs = {k: c["regs"][k] for k in rf.GENERAL + ("bp", "es")} if self.with_regs else None
            if regs and c["regs"]["es"] not in (base.dgroup,):
                regs["es"] = c["regs"]["es"]
            key = (tuple(args), tuple(sorted(regs.items())) if regs and f["convention"] == "regs" else None,
                   hashlib.sha1(c["dgroup"][:self.info["stack_lo"]]).hexdigest())
            if key in seen:
                continue
            seen.add(key)
            out.append(dict(base=base, entry=False, dgroup=c["dgroup"], shared=c["shared"], args=args, sp=sp,
                            regs=regs, keys=keys, in_model=True,
                            origin=f"capture {c['route']} hit {c['hit']} keys {' '.join(keys)}"))
        if not out:
            # Never reached on the scripted routes: realistic workstation state, arguments built from
            # the typed model (oracle_config.ARGS); positions without a model get 0 / 1 / scratch.
            for (route, which), base in sorted(self.bases.items()):
                if which != "wait":
                    continue
                dg = bytearray(base.dgroup_bytes())
                for at, text in zip(SCRATCH, SCRATCH_TEXT):
                    dg[at:at + len(text)] = text
                sp = self.default_sp()
                for variant in range(3):
                    seed = dict(base=base, entry=False, dgroup=bytes(dg), shared=base.shared_bytes(), args=[],
                                sp=sp, keys=["ESC"], origin=f"synthetic {route}")
                    args = []
                    for i, spec in enumerate(self.specs()):
                        if spec == "seen":
                            args.append((0, 1, SCRATCH[i % 3])[variant])
                        elif isinstance(spec, tuple) and spec[0] != "set":
                            args.append((spec[0], spec[1], (spec[0] + spec[1]) // 2)[variant] & 0xffff)
                        elif spec == "int":
                            args.append((0, 1, 5)[variant])
                        else:
                            args.append(self.arg_value(spec, i, seed))
                    regs = None
                    if self.with_regs:
                        regs = dict(ax=args[0] if args else 1, bx=SCRATCH[0], cx=2, dx=SCRATCH[1], si=SCRATCH[0],
                                    di=SCRATCH[1], bp=0, es=base.dgroup)
                    seed.update(args=args, regs=regs)
                    seed["in_model"] = self.args_ok(seed) and not (self.with_regs and f["convention"] == "regs")
                    out.append(seed)
                break
        # State donors (oracle_config.DONORS): states captured at another function's entry on the real routes
        donor = cfg.DONORS.get(self.module, {}).get(f["name"])
        if donor and (not self.mine or f["name"] in cfg.DONORS_ALWAYS.get(self.module, ())):
            labels, make_args = donor
            pool = [c for c in self.captures if c["label"] in labels]
            picked, seen_dg = [], set()
            for c in sorted(pool, key=lambda c: (-c["hit"], c["route"])):
                h = hashlib.sha1(c["dgroup"]).hexdigest()
                if h not in seen_dg and c["route"] not in {x["route"] for x in picked}:
                    seen_dg.add(h)
                    picked.append(c)
            for c in sorted(pool, key=lambda c: (-c["hit"], c["route"])):
                h = hashlib.sha1(c["dgroup"]).hexdigest()
                if len(picked) < cfg.DONOR_STATES and h not in seen_dg:
                    seen_dg.add(h)
                    picked.append(c)
            out = [s_ for s_ in out if not s_["origin"].startswith("synthetic")]
            for c in picked[:cfg.DONOR_STATES]:
                base = self.bases[(c["route"], "wait" if c["after_main"] else "start")]
                for args, realistic, why in make_args(c["dgroup"]):
                    out.append(dict(base=base, entry=False, dgroup=c["dgroup"], shared=c["shared"],
                                    args=[a & 0xffff for a in args], sp=self.default_sp(), regs=None, keys=["ESC"],
                                    in_model=realistic, scripted=True,
                                    origin=f"state of {c['label']} entry, capture {c['route']} hit {c['hit']}: {why}"))
        # Scripted cases (oracle_config.SCRIPTED): menu lines picked with Return, rare paths
        originals = list(out)       # scripted states start from captured / synthetic seeds, never from each other
        for k, script in enumerate(cfg.SCRIPTED.get(self.module, {}).get(f["name"], ())):
            base_seed = originals[k % len(originals)] if originals else None
            if base_seed is None:
                break
            seed = dict(base_seed)
            if "keys" in script:
                seed["keys"] = list(script["keys"])
            if "overrides" in script:
                seed["overrides"] = script["overrides"]
            if "patch" in script:
                name, *params = script["patch"] if isinstance(script["patch"], tuple) else (script["patch"],)
                seed["dgroup"] = PATCHES[name](seed["dgroup"], *params)
            if "set" in script:
                seed["dgroup"] = set_globals(seed["dgroup"], script["set"])
            if "args" in script:
                seed["args"] = [a & 0xffff for a in script["args"]]
            if "set" in script or "args" in script or isinstance(script.get("patch"), tuple):
                # a made-up state: realistic only when the entry says so (oracle_config.SCRIPTED)
                seed["in_model"] = bool(script.get("realistic", False))
            seed["scripted"] = True
            seed["origin"] = f"scripted: {script['why']} (keys {' '.join(seed['keys'])}; state {base_seed['origin']})"
            out.append(seed)
        return out

    def run(self, seed, track=False, script=None):
        script = script or rf.Script(keys=seed["keys"], overrides=copy.deepcopy(seed.get("overrides")),
                                     clock=seed.get("clock", (12, 0, 0)),
                                     idle_key=cfg.IDLE_KEYS.get(self.module, {}).get(self.f["name"], "ESC"))
        budget = cfg.BUDGETS.get(self.module, {}).get(self.f["name"]) or (
            2_000_000 if not seed["entry"] else 20_000_000)
        try:
            return self._run(seed, track, script, budget)
        except Exception as e:     # a wild mutant may drive the emulator off the map: not a case
            return dict(status="fault", fault=f"{type(e).__name__}: {e}", unknown_far=0, coverage=set(), reads=[])

    def _run(self, seed, track, script, budget):
        return rf.run(seed["base"], self.label, seed["args"], dgroup=seed["dgroup"], shared=seed["shared"],
                      sp=seed["sp"], regs=seed["regs"], script=script, budget=budget,
                      max_calls=cfg.MAX_CALLS.get(self.module, {}).get(self.f["name"]) or (
                          50_000 if seed["entry"] or self.label == self.info["main"] else 20_000),
                      track_reads=track, entry=seed["entry"])

    def unscripted(self):
        """Cases that count against --max-cases / --min-cases (scripted and donor cases come on top)."""
        return sum(why not in ("scripted",) for _, _, why in self.cases)

    def ok(self, res):
        return res["status"] in ("return", "exit") and not res["unknown_far"]

    def keep(self, seed, res, why):
        # The recorded inputs alone must reproduce the case.
        again = self.run(seed, script=rf.Script(replay=res["inputs"]))
        same = again.get("status") == res["status"] and all(
            again.get(k) == res[k] for k in ("dgroup_out", "shared_out", "calls", "result", "far_out", "exit"))
        if not same:
            self.failures.append(f"{seed['origin']}: replay from recorded inputs diverged ({again['status']})")
            return False
        self.cases.append((seed, res, why))
        self.coverage |= res["coverage"]
        return True

    # mutation ------------------------------------------------------------------------------
    def mutate(self, seed, reads, free):
        """One mutant. With free=False only typed-model values (oracle_config) are used, so the case
        stays realistic; free=True may also write unmodelled bytes, any argument or register."""
        rng = self.rng
        s = dict(seed)
        dg = bytearray(seed["dgroup"])
        sh = bytearray(seed["shared"]) if seed["shared"] is not None else None
        args = list(seed["args"])
        regs = dict(seed["regs"]) if seed["regs"] else None
        keys = list(seed["keys"])
        what = []
        in_model = seed["in_model"]
        entries = self.model_entries(reads)
        specs = self.specs()
        for _ in range(rng.randint(1, 4)):
            choice = rng.random()
            if args and choice < 0.3:
                i = rng.randrange(len(args))
                if free and rng.random() < 0.5:
                    pool = VALUES + [args[i] + 1, args[i] - 1, args[i] ^ 1, rng.randrange(0x10000)] + list(SCRATCH)
                    args[i] = rng.choice(pool) & 0xffff
                else:
                    args[i] = self.arg_value(specs[i], i, s)
                what.append(f"arg{i}")
            elif choice < 0.75 and (entries or (free and reads)):
                if entries and not (free and rng.random() < 0.5):
                    space, off, name, count, width, allowed = rng.choice(entries)
                    k = rng.randrange(count)
                    if allowed[0] == "set":
                        v = rng.choice(allowed[1])
                    else:
                        lo, hi = allowed
                        v = rng.choice([lo, hi, rng.randint(lo, hi), rng.randint(lo, hi)])
                    buf = dg if space == "D" else sh
                    at = off + k * width
                    buf[at:at + width] = (v & ((1 << 8 * width) - 1)).to_bytes(width, "little")
                    what.append(f"{name}[{k}]" if count > 1 else name)
                else:
                    a = rng.choice(reads)
                    width = rng.choice((1, 2))
                    v = rng.choice(VALUES + [rng.randrange(0x10000)])
                    if a < 0x10000:
                        dg[a:a + width] = (v & (0xff if width == 1 else 0xffff)).to_bytes(width, "little")[:len(dg) - a]
                        what.append(f"D{a:04x}")
                    elif sh is not None:
                        o = a - 0x10000
                        sh[o:o + width] = (v & (0xff if width == 1 else 0xffff)).to_bytes(width, "little")[:len(sh) - o]
                        what.append(f"S{o:04x}")
                    in_model = False
            elif free and regs and self.f["convention"] == "regs" and choice < 0.85:
                r = rng.choice(rf.GENERAL)
                regs[r] = rng.choice(VALUES + list(SCRATCH) + [rng.randrange(0x10000)])
                what.append(r)
                in_model = False
            elif choice < 0.93:
                keys = [rng.choice(KEY_POOL) for _ in range(rng.randint(1, 4))]
                what.append("keys")
            else:
                s["overrides"] = {"*": [rng.choice(VALUES + [rng.randrange(0x10000)]) for _ in range(4)]}
                s["clock"] = (rng.randrange(24), rng.randrange(60), rng.randrange(60))
                what.append("driver-results+clock")
        for at, text in zip(SCRATCH, SCRATCH_TEXT):     # pointer fodder for mutated pointer args
            if all(b == 0 for b in dg[at:at + 32]):
                dg[at:at + len(text)] = text
        s.update(dgroup=bytes(dg), shared=bytes(sh) if sh is not None else None, args=args, regs=regs,
                 keys=keys, origin=seed["origin"] + (" fuzz " if in_model else " fuzz(free) ") + ",".join(what))
        s["in_model"] = bool(in_model and self.args_ok(s))
        return s

    def phase(self, analysed, tries, free, accept):
        for _ in range(tries):
            if self.unscripted() >= (self.max_cases if accept == "coverage" else self.min_cases):
                break
            seed, reads, _ = self.rng.choice(analysed)
            m = self.mutate(seed, reads, free)
            res = self.run(m)
            if not self.ok(res):
                continue
            if accept == "coverage" and not res["coverage"] <= self.coverage:
                self.keep(m, res, "fuzz")
            elif accept == "outcome" and self.outcome(res) not in self.outcomes:
                self.outcomes.add(self.outcome(res))
                self.keep(m, res, "variant")

    def go(self):
        seeds = self.seeds()
        analysed = []
        for seed in seeds:
            t = time.time()
            res = self.run(seed, track=True)
            dt = time.time() - t
            if not self.ok(res):
                self.failures.append(f"{seed['origin']}: {res['status']} {res['fault'] or ''}".strip())
                continue
            base = seed["base"]
            D, sh = base.dgroup * 16, base.shared_linear
            reads = [a - D for a in res["reads"] if D <= a < D + base.dsize]
            if sh is not None:
                reads += [0x10000 + a - sh for a in res["reads"] if sh <= a < sh + oc.SHARED_SIZE]
            analysed.append((seed, reads, dt))
            new = not res["coverage"] <= self.coverage
            real = seed["origin"].startswith("capture")
            every = self.f["name"] in cfg.KEEP_CAPTURES.get(self.module, ())   # every captured state, not only new blocks
            if seed.get("scripted"):
                self.keep(seed, res, "scripted")
            elif (new or (real and (every or self.unscripted() < 3))) and self.unscripted() < self.max_cases:
                self.keep(seed, res, "realistic" if real else "synthetic")
        if not analysed:
            return
        slow = max(dt for _, _, dt in analysed)
        tries = self.tries if slow < 0.3 else max(4, int(self.tries * 0.3 / slow))
        # realistic (typed-model) mutants first; unrestricted ones only for blocks still unreached
        self.phase(analysed, tries, False, "coverage")
        self.phase(analysed, max(2, tries // 3), True, "coverage")
        # Several data points even when one case already covers every block: add mutants with a
        # different observable outcome (result, boundary calls or written memory).
        self.outcomes = {self.outcome(r) for _, r, _ in self.cases}
        self.phase(analysed, tries, False, "outcome")
        self.phase(analysed, tries // 2, True, "outcome")

    @staticmethod
    def outcome(res):
        def delta(a, b):
            return bytes(x for i in range(len(a)) if a[i] != b[i] for x in (i >> 8, i & 255, b[i])) if a else b""
        return hashlib.sha1(json.dumps([res["result"], res["calls"], res["exit"]]).encode()
                            + delta(res["dgroup_in"], res["dgroup_out"])
                            + delta(res["shared_in"], res["shared_out"])).hexdigest()


def _all_guessed(dg, correct=True):
    """Every letter used in the message already has a guess: reveal_hint_letter (132d) then draws
    999 (0x3e7) times without finding an unguessed letter."""
    dg = bytearray(dg)
    cipher, plain = dg[0x105a:0x105a + 26], dg[0x1078:0x1078 + 26]
    for b in range(26):
        c = cipher[b] - 0x41
        if 0 <= c < 26 and int.from_bytes(dg[0x1ac0 + 2 * c:0x1ac2 + 2 * c], "little"):
            dg[0x1096 + c] = plain[b] if correct else (plain[(b + 1) % 26])
    return bytes(dg)


def set_globals(dg, values):
    """DGROUP with values written: offset -> int (word), ("dword", int) or bytes."""
    dg = bytearray(dg)
    for off, v in values.items():
        if isinstance(v, tuple):
            data = struct.pack("<I", v[1] & 0xffffffff)
        elif isinstance(v, (bytes, bytearray)):
            data = bytes(v)
        else:
            data = struct.pack("<H", v & 0xffff)
        dg[off:off + len(data)] = data
    return bytes(dg)


def _tac_player_near_guard(dg, dx, dy, running=0, fire=0, sneak=0, facing=0, bullets=None):
    """TAC: the player stands (dx, dy) pixels from guard 1 (bg 0xc870, 28-byte records, status 0x80 =
    active, px/py at +2/+4, room +0x12; when slot 1 is idle the first active guard's record is copied into
    it), in its room (InRoom 0xbdde), so the guard's line of sight to the player crosses whatever lies
    between (walls, partial cover); running 0xbb60, fire_timer 0x96f4, the guard's ACT_SNEAK bit 4,
    player_facing 0x1c42 (0 = unchanged) and Bullets 0x96f6 (None = unchanged) as given. No active guard:
    state unchanged."""
    dg = bytearray(dg)
    one = 0xc870 + 28
    if not dg[one] & 0x80:
        src = next((0xc870 + 28 * i for i in range(2, 9) if dg[0xc870 + 28 * i] & 0x80), None)
        if src is None:
            return bytes(dg)
        dg[one:one + 28] = dg[src:src + 28]
    gx, gy = struct.unpack_from("<hh", dg, one + 2)
    room = struct.unpack_from("<h", dg, one + 0x12)[0]
    px, py = max(8, min(0x27 * 16 + 7, gx + dx)), max(8, min(0x1f * 16 + 7, gy + dy))
    for off, v in ((0xbb72, px), (0xbb7c, py), (0xc870 + 2, px), (0xc870 + 4, py), (0xbb60, running),
                   (0x96f4, fire), (0xbdde, room), (0xb3fc, px // 16), (0xb408, py // 16)):
        struct.pack_into("<H", dg, off, v & 0xffff)
    if facing:
        struct.pack_into("<H", dg, 0x1c42, facing)
        struct.pack_into("<H", dg, 0xc870 + 0x0c, facing)
    if bullets is not None:
        struct.pack_into("<H", dg, 0x96f6, bullets)
    dg[one] = (dg[one] & ~4) | (4 if sneak else 0)
    return bytes(dg)


def _tac_grenade(dg, pdx, pdy, gdx, gdy, time, kind, direction=3, slot=0):
    """TAC: the player near guard 1 (_tac_player_near_guard, pdx/pdy) and device record gr[slot] (0x9d78,
    10 bytes: px, py, dir, time, type) at guard 1's position + (gdx, gdy): time > 0 flight frames left,
    -0x3f the burst moment, -0x3e..-1 the burst running, -9999 remote trap, -9998 booby trap; kind 0 frag,
    1 gas, 2 stun, +0x80 thrown by a guard."""
    dg = bytearray(_tac_player_near_guard(dg, pdx, pdy))
    one = 0xc870 + 28
    if not dg[one] & 0x80:
        return bytes(dg)
    gx, gy = struct.unpack_from("<hh", dg, one + 2)
    x, y = max(8, min(0x27 * 16 + 7, gx + gdx)), max(8, min(0x1f * 16 + 7, gy + gdy))
    struct.pack_into("<5h", dg, 0x9d78 + 10 * slot, x, y, direction, time, kind)
    return bytes(dg)


PATCHES = {"all_guessed": _all_guessed, "tac_grenade": _tac_grenade, "all_guessed_wrong": lambda dg: _all_guessed(dg, False),
           "tac_player_near_guard": _tac_player_near_guard}


def library_owner(module, functions, label):
    """The cut routine whose internals function `label` is (itself included), or None. Cut routines
    are found by module and address (oracle_config.cut_labels), their family by name."""
    own = cfg.cut_labels(module, functions).get(label)
    if own:
        return own
    name = functions[label]["name"]
    for cut, spec in cfg.CUTS.items():
        if name in spec["family"]:
            return cut
    return None


def private_ranges(cases, name):
    """DGROUP [lo, hi) ranges a library routine's own runs changed, outside the stack mask and outside
    what it wrote through its pointer argument (time_t *)."""
    changed = set()
    for seed, res, _ in cases:
        lo, hi = res["stack_mask"]
        through = set()
        if name == "_time" and seed["args"] and seed["args"][0]:
            through = set(range(seed["args"][0], seed["args"][0] + 4))
        a, b = res["dgroup_in"], res["dgroup_out"]
        changed |= {i for i in range(len(a)) if a[i] != b[i] and not lo <= i < hi and i not in through}
    out = []
    for i in sorted(changed):
        if out and out[-1][1] == i:
            out[-1][1] = i + 1
        else:
            out.append([i, i + 1])
    return out


def segments(base):
    out = dict(psp=f"{base.psp:04x}", load=f"{base.load:04x}", dgroup=f"{base.dgroup:04x}",
               dgroup_size=base.dsize, shared=None if not base.shared else f"{base.shared[0]:04x}:{base.shared[1]:04x}")
    if base.module in cfg.FAR_DATA:
        out["far_data"] = {f"{a:04x}": dict(segment=f"{base.load + a:04x}", size=(b - a) * 16)
                           for a, b in cfg.FAR_DATA[base.module]}
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("module")
    p.add_argument("labels", nargs="*")
    p.add_argument("--tries", type=int, default=60)
    p.add_argument("--max-cases", type=int, default=8)
    p.add_argument("--min-cases", type=int, default=3)
    p.add_argument("--seed", type=int, default=1)
    a = p.parse_args()
    module = a.module.upper()
    info = oc.module_info(module)
    bases, captures = load_states(module)
    out = oc.OUT / module
    out.mkdir(parents=True, exist_ok=True)
    labels = a.labels or sorted(info["functions"], key=lambda l: info["functions"][l]["linear"])
    labels = [l if l in info["functions"] else next(k for k, f in info["functions"].items() if f["name"] == l)
              for l in labels]
    index_path = out / "_index.json"
    index = json.loads(index_path.read_text(encoding="utf-8")) if index_path.exists() else {}
    (out / "_boundaries.json").write_text(json.dumps(
        {s["name"]: dict(slot=f"{a_:04x}", label=s["label"], convention=s["convention"], nargs=s["nargs"],
                         dispatch=s["how"] == "dispatch") for a_, s in sorted(info["slots"].items())},
        indent=1) + "\n", encoding="utf-8")
    library_path = out / "_library.json"
    library = json.loads(library_path.read_text(encoding="utf-8")).get("private_ranges", {}) \
        if library_path.exists() else {}
    for label in labels:
        f = info["functions"][label]
        started = time.time()
        reason = SKIP.get(f["kind"]) or SKIP_LABELS.get(module, {}).get(label)
        entry = dict(name=f["name"], kind=f["kind"], far=f["far"], nargs=f["nargs"], convention=f["convention"],
                     in_functions_json=f["in_db"])
        if reason:
            entry.update(skipped=reason, cases=0)
            index[label] = entry
            print(f"{label} {f['name']} SKIP {reason[:60]}", flush=True)
            continue
        rng = random.Random(f"{a.seed}:{label}")
        fz = Fuzzer(module, label, bases, captures, rng, a.tries, a.max_cases, print, a.min_cases)
        fz.go()
        cases = [rf.vector_case(res, f"{i:02d}-{why}", with_regs=fz.with_regs)
                 | dict(origin=seed["origin"], realistic=bool(seed["in_model"]))
                 for i, (seed, res, why) in enumerate(fz.cases)]
        segs = {json.dumps(segments(seed["base"])) for seed, _, _ in fz.cases}
        vec = dict(module=module, label=label, name=f["name"],
                   call=dict(far=f["far"], iret=f.get("iret", False), nargs=f["nargs"], convention=f["convention"],
                             entry=f"{f['frame']:04x}:{f['offset']:04x}",
                             program_entry=bool(fz.cases and fz.cases[0][0]["entry"])),
                   segments=json.loads(next(iter(segs))) if len(segs) == 1 else [json.loads(s) for s in segs],
                   cases=cases)
        owner = library_owner(module, info["functions"], label)
        if owner and fz.cases:
            vec["ignore"] = private_ranges(fz.cases, f["name"])
            vec["ignore_reason"] = (f"private data of the C library routine {owner} (oracle_config.LIBRARY_CUTS): "
                                    "elsewhere it is recorded as one boundary call and these bytes are not written")
            library[f["name"]] = vec["ignore"]
        if cases:
            (out / f"{label}.json").write_text(json.dumps(vec) + "\n", encoding="utf-8")
        entry.update(cases=len(cases), blocks=len(fz.coverage), failures=fz.failures[:10],
                     reached=bool(fz.mine), origins=[c["origin"] for c in cases], seconds=round(time.time() - started, 1))
        if not cases:
            entry["skipped"] = "no case ran to completion: " + "; ".join(fz.failures[:3])
            stale = out / f"{label}.json"
            if stale.exists():      # an older vector no longer matches the fixtures: kept aside, not served
                (out / "_stale").mkdir(exist_ok=True)
                stale.replace(out / "_stale" / stale.name)
                entry["stale_vector"] = f"_stale/{stale.name}"
        index[label] = entry
        print(f"{label} {f['name']} cases {len(cases)} blocks {len(fz.coverage)} fails {len(fz.failures)} "
              f"{entry['seconds']}s", flush=True)
        index_path.write_text(json.dumps(index, indent=1) + "\n", encoding="utf-8")
    index_path.write_text(json.dumps(index, indent=1) + "\n", encoding="utf-8")
    cuts = {n: dict(spec, family=list(spec["family"]), ptrs={str(k): v for k, v in spec["ptrs"].items()},
                    linked=any(g["name"] == n for g in info["functions"].values()))
            for n, spec in cfg.LIBRARY_CUTS.items()}
    library_path.write_text(json.dumps(dict(cuts=cuts, private_ranges=library), indent=1) + "\n",
                            encoding="utf-8")


def summary(modules):
    """Markdown coverage table from build/oracle/<MODULE>/_index.json and the vectors."""
    rows = ["| module | functions (db) | with vectors | cases | realistic | reached on routes | skipped / no case |",
            "|---|---:|---:|---:|---:|---:|---|"]
    for module in modules:
        path = oc.OUT / module / "_index.json"
        if not path.exists():
            continue
        index = json.loads(path.read_text(encoding="utf-8"))
        db = {k: v for k, v in index.items() if v["in_functions_json"]}
        cases = real = 0
        for label in index:
            f = oc.OUT / module / f"{label}.json"
            if f.exists():
                for c in json.loads(f.read_text(encoding="utf-8"))["cases"]:
                    cases += 1
                    real += bool(c.get("realistic"))
        skipped = [f"{index[k]['name'] or k[-9:]}" for k in index if not index[k]["cases"]]
        rows.append(f"| {module} | {len(db)} | {sum(1 for v in db.values() if v['cases'])} (+{sum(1 for v in index.values() if v['cases'] and not v['in_functions_json'])} named entries) "
                    f"| {cases} | {100 * real // max(cases, 1)}% | {sum(1 for v in db.values() if v.get('reached'))} "
                    f"| {len(skipped)}: {', '.join(skipped[:12])}{' ...' if len(skipped) > 12 else ''} |")
    return "\n".join(rows)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--summary":
        print(summary(sys.argv[2:]))
        sys.exit(0)
    main()
