"""Driver oracle: vectors for the entry slots of MicroProse's hand-written drivers (MGRAPHIC, MISC,
the sound driver) as a module uses them, so a port's driver layer can be proven pixel by pixel.

    python -B tools/oracle/driver_vectors.py capture [routes...]   # once
    python -B tools/oracle/driver_vectors.py vectors [--tries N]

capture: plays the CODE routes of capture_states.py with the same probe and records the whole
machine (1 MiB, zlib), registers, the DAC palette and overscan each time execution enters a driver
through one of CODE's thunk slots (hits 1..8, 12, 16, 24, ... per entry).
Output: build/oracle/states/DRIVERS/<route>-<tag>/captures.pkl.gz (+ meta.json); reused if present.

vectors: runs each captured entry again in Unicorn (only the driver's own instructions; INT 10h and
the DAC/retrace ports are modelled, every interrupt and port access is recorded), plus bounded
variations of the stack arguments (oracle_config.DRIVER_ARGS). Writes
build/oracle/<DRIVER>/<slot name>.json (format in tools/oracle/README.md). Game-derived: never commit.
"""
import argparse
import base64
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
import unicorn as uc  # noqa: E402

REG = rf.REG
VIDEO = 0xa0000
VIDEO_SIZE = 64000
STATES = oc.STATES / "DRIVERS"
HITS = set(range(1, 9)) | {12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 4096, 8192}


def code_slots():
    """CODE's thunk slots that CODE calls: slot offset -> (name, convention, nargs)."""
    info = oc.module_info("CODE")
    return {a: s for a, s in info["slots"].items() if s["call_sites"]}


def slot_targets(mem, load):
    """Driver entry linear address -> slot offset, read from the patched thunk table."""
    out = {}
    for a, s in code_slots().items():
        at = load * 16 + a + (1 if s["how"] == "jmp" else 3)
        off, seg = struct.unpack_from("<HH", mem, at)
        if seg:
            out[seg * 16 + off] = a
    return out


# ------------------------------------------------------------------------------ capture
def capture(route, out):
    import capture_states as cs

    class DriverProbe(cs.CaptureProbe):
        def __init__(self, *a, **k):
            super().__init__(*a, **k)
            self.targets, self.dhits, self.dcaps, self.driver_blocks = {}, {}, [], {}

        def instruction(self, machine, address, size, context):
            super().instruction(machine, address, size, context)
            if self.current["name"] != "CODE.EXE" or self.module_exited:
                return
            if not self.targets:
                # the thunk table is patched by attach_driver_segment early in main; the compact
                # layout is deterministic, so read the targets from the CODE state capture
                base = rf.Base.from_states(oc.STATES / "CODE" / "decode-v1", "wait")
                if base.load != self.current["load"]:
                    raise SystemExit("CODE load segment differs from the CODE state capture")
                self.targets = slot_targets(base.mem, base.load)
            slot = self.targets.get(address)
            if slot is None:
                return
            n = self.dhits.get(slot, 0) + 1
            self.dhits[slot] = n
            if n not in HITS:
                return
            seg = address // 16
            seg = next((s for s, (size, _) in self.blocks.items() if s * 16 <= address < (s + size) * 16), None)
            self.dcaps.append(dict(
                slot=slot, hit=n, address=address, regs={r: self.get(r) for r in REG},
                block=(seg, self.blocks[seg][0]) if seg is not None else None,
                palette=bytes(self.palette), overscan=self.overscan,
                memory=zlib.compress(bytes(machine.mem_read(0, 0x100000)), 1)))

    probe = DriverProbe(oc.GAME, "212 ", 60_000_000, cs.ROUTES[route], discovery=False,
                        artifacts="validation", memory_layout="compact")
    t = time.time()
    report = probe.run()
    out.mkdir(parents=True, exist_ok=False)
    with gzip.open(out / "captures.pkl.gz", "wb") as f:
        pickle.dump(probe.dcaps, f)
    meta = dict(route=route, stop_reason=report["stop_reason"], seconds=round(time.time() - t, 1),
                hits={f"{k:04x}": v for k, v in sorted(probe.dhits.items())}, captures=len(probe.dcaps),
                load=probe.meta.get("load"), dgroup=probe.meta.get("dgroup"))
    (out / "meta.json").write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")
    print(route, report["stop_reason"], "driver captures", len(probe.dcaps), flush=True)


# ------------------------------------------------------------------------------ the runner
def _runs(byte_map):
    return rf._runs(byte_map)


def _diff_runs(before, after, base_addr=0):
    return _runs({base_addr + i: after[i] for i in range(len(before)) if before[i] != after[i]})


def run_driver(cap, args=None, budget=5_000_000, script=None, nargs=8):
    """Run one driver entry from a capture. `args` replaces the stack argument words."""
    mem = bytearray(zlib.decompress(cap["memory"]))
    regs = dict(cap["regs"])
    ss_sp = regs["ss"] * 16 + regs["sp"]
    ret_ip, ret_cs = struct.unpack_from("<HH", mem, ss_sp)
    if args is not None:
        for i, v in enumerate(args):
            struct.pack_into("<H", mem, ss_sp + 4 + 2 * i, v & 0xffff)
    seg, size = cap["block"]
    lo, hi = seg * 16, (seg + size) * 16
    palette = bytearray(cap["palette"])
    pal_in = bytes(palette)
    state = dict(overscan=cap["overscan"], dac_w=0, dac_r=0, dac_k=0, stop=None, fault=None)
    script = script or rf.Script()
    calls = []
    m = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
    m.mem_map(0, rf.MEM)
    m.mem_write(0, bytes(mem))
    for r in ("ax", "bx", "cx", "dx", "si", "di", "bp", "sp", "ds", "es", "ss", "cs", "ip", "eflags"):
        m.reg_write(REG[r], regs[r])
    get = lambda r: m.reg_read(REG[r])  # noqa: E731
    far_read, far_written, coverage = {}, set(), set()

    def stop(why):
        if state["stop"] is None:
            state["stop"] = why
        m.emu_stop()

    def on_outside(mu, address, size_, _):
        if (get("cs"), get("ip")) == (ret_cs, ret_ip):
            stop("return")
        else:
            state["fault"] = f"to {get('cs'):04x}:{get('ip'):04x} (return {ret_cs:04x}:{ret_ip:04x})"
            stop("left the driver")

    def on_block(mu, address, size_, _):
        coverage.add(address - lo)

    def on_intr(mu, n, _):
        ax, bx, cx, dx = get("ax"), get("bx"), get("cx"), get("dx")
        ah, al = ax >> 8, ax & 255
        target = f"int{n:02x}.{ah:02x}"
        call = dict(target=target, args=[get(r) for r in rf.GENERAL])
        if n == 0x10 and ah == 0x10:
            if al == 0x12:
                palette[bx * 3:(bx + cx) * 3] = mu.mem_read(get("es") * 16 + dx, cx * 3)
            elif al == 0x10:
                palette[bx * 3:bx * 3 + 3] = bytes([dx >> 8, cx >> 8, cx & 255])
            elif al == 0x01:
                state["overscan"] = bx >> 8
            elif al == 0x17:
                mu.mem_write(get("es") * 16 + dx, bytes(palette[bx * 3:(bx + cx) * 3]))
            elif al == 0x15:
                r, g, b = palette[bx * 3:bx * 3 + 3]
                mu.reg_write(REG["dx"], (r << 8) | (dx & 255))
                mu.reg_write(REG["cx"], (g << 8) | b)
            call["ret"] = get("ax")
            calls.append(call)          # modelled hardware: no scripted input
            return
        if n in (0, 1, 3, 4, 5, 6, 7, 8, 13) and bytes(mu.mem_read(get("cs") * 16 + get("ip") - 2, 2)) != bytes([0xcd, n]):
            state["fault"] = f"cpu exception {n:02x}"
            stop("fault")
            return
        try:
            r = script.interrupt(n, {k: get(k) for k in rf.GENERAL})
        except rf.ReplayExhausted:
            stop("inputs exhausted")
            return
        for k, v in r.items():
            if k in ("ax", "bx", "cx", "dx", "si", "di", "es"):
                mu.reg_write(REG[k], v)
        fl = get("eflags")
        fl = (fl | 1) if r.get("cf") else (fl & ~1)
        if "zf" in r:
            fl = (fl | 0x40) if r["zf"] else (fl & ~0x40)
        mu.reg_write(REG["eflags"], fl)
        call["ret"] = get("ax")
        calls.append(call)

    def on_in(mu, port, size_, _):
        if port == 0x3c9:       # DAC data read
            v = palette[state["dac_r"] * 3 + state["dac_k"]]
            state["dac_k"] += 1
            if state["dac_k"] == 3:
                state["dac_k"], state["dac_r"] = 0, (state["dac_r"] + 1) & 255
            calls.append(dict(target="inb", args=[port], ret=v))
            return v
        try:
            v = script.port_in(port, size_)
        except rf.ReplayExhausted:
            stop("inputs exhausted")
            return 0
        calls.append(dict(target="inb" if size_ == 1 else "inw", args=[port], ret=v))
        return v

    def on_out(mu, port, size_, value, _):
        calls.append(dict(target="outb" if size_ == 1 else "outw", args=[port, value], ret=0))
        if port == 0x3c8:
            state["dac_w"], state["dac_k"] = value & 255, 0
        elif port == 0x3c7:
            state["dac_r"], state["dac_k"] = value & 255, 0
        elif port == 0x3c9:
            palette[state["dac_w"] * 3 + state["dac_k"]] = value & 63
            state["dac_k"] += 1
            if state["dac_k"] == 3:
                state["dac_k"], state["dac_w"] = 0, (state["dac_w"] + 1) & 255

    def on_far_read(mu, access, address, size_, value, _):
        for a in range(address, address + size_):
            if a not in far_written and a not in far_read:
                far_read[a] = mu.mem_read(a, 1)[0]

    def on_far_write(mu, access, address, size_, value, _):
        far_written.update(range(address, address + size_))

    m.hook_add(uc.UC_HOOK_CODE, on_outside, None, 0, lo - 1)
    m.hook_add(uc.UC_HOOK_CODE, on_outside, None, hi, rf.MEM - 1)
    m.hook_add(uc.UC_HOOK_BLOCK, on_block, None, lo, hi - 1)
    m.hook_add(uc.UC_HOOK_INTR, on_intr)
    m.hook_add(uc.UC_HOOK_INSN, on_in, None, 1, 0, rf.UC_X86_INS_IN)
    m.hook_add(uc.UC_HOOK_INSN, on_out, None, 1, 0, rf.UC_X86_INS_OUT)
    # The caller's stack is not tracked: its dead part below SP is scratch, the argument words are
    # recorded as args, and (Unicorn 2.1.4) a MEM_READ hook on the stack breaks the driver's
    # intra-segment RETF at MGRAPHIC 0e85 (it re-executes the RETF with a stale CS).
    stack_window = (ss_sp - 0x400, ss_sp + 4 + 2 * nargs)
    known = sorted([(lo, hi), (VIDEO, VIDEO + VIDEO_SIZE), stack_window])
    cursor = 0
    for a, b in known + [(rf.MEM, rf.MEM)]:
        if a > cursor:
            m.hook_add(uc.UC_HOOK_MEM_READ, on_far_read, None, cursor, a - 1)
            m.hook_add(uc.UC_HOOK_MEM_WRITE, on_far_write, None, cursor, a - 1)
        cursor = max(cursor, b)
    try:
        m.emu_start(get("cs") * 16 + get("ip"), rf.MEM, count=budget)
    except uc.UcError as e:
        state["stop"], state["fault"] = "fault", f"unicorn: {e} at {get('cs'):04x}:{get('ip'):04x}"
    if state["stop"] is None:
        state["stop"] = "budget"
    after = bytes(m.mem_read(0, 0x100000))
    return dict(
        status=state["stop"], fault=state["fault"], coverage=coverage, calls=calls, inputs=script.inputs,
        regs_in={r: regs[r] for r in rf.GENERAL + ("bp", "sp", "ds", "es", "ss")},
        regs_out={r: get(r) for r in rf.GENERAL + ("bp", "sp", "ds", "es")} | {"flags": get("eflags") & 0x8d5},
        result=dict(ax=get("ax"), dx=get("dx")),
        driver_in=bytes(mem[lo:hi]), driver_out=_diff_runs(mem[lo:hi], after[lo:hi], lo),
        video_in=bytes(mem[VIDEO:VIDEO + VIDEO_SIZE]),
        video_out=_runs({i: after[VIDEO + i] for i in range(VIDEO_SIZE) if mem[VIDEO + i] != after[VIDEO + i]}),
        palette_in=pal_in, palette_out=bytes(palette), overscan_in=cap["overscan"], overscan_out=state["overscan"],
        far_in=_runs(far_read), far_out=_runs({a: after[a] for a in far_written if a < 0x100000}),
        stack=bytes(mem[ss_sp:ss_sp + 64]), block=(seg, size),
        stack_window=[stack_window[0] - regs["ss"] * 16, stack_window[1] - regs["ss"] * 16])


# ------------------------------------------------------------------------------ vectors
def b64(b):
    return None if b is None else base64.b64encode(b).decode()


def show_args(cap_mem, ds, words, name, conv):
    """Argument words with pointer objects (oracle_config.DRIVER_POINTERS) at call time."""
    shown = list(words)
    if conv != "stack":
        return shown
    for i, spec in cfg.DRIVER_POINTERS.get(name, {}).items():
        if i < len(words):
            v = words[i]
            if spec == "str":
                data = cap_mem[ds * 16 + v:ds * 16 + v + 1024]
                cut = data.find(b"\0")
                shown[i] = {"ptr": v, "str": b64(data[:cut if cut >= 0 else 1024])}
            else:
                shown[i] = {"ptr": v, "mem": b64(cap_mem[ds * 16 + v:ds * 16 + v + spec[1]])}
    return shown


def arg_ok(spec, v, cap_mem, ds):
    sv = v - 0x10000 if v & 0x8000 else v
    if spec in ("int", "seen"):
        return True
    if spec == "port":
        return v in cfg.PORTS
    if spec == "cstr":
        return 0x42 <= v < 0xfe00 and 0 in cap_mem[ds * 16 + v:ds * 16 + v + 1024]
    if spec[0] == "set":
        return sv in spec[1] or v in spec[1]
    return spec[0] <= sv <= spec[1]


def pages_of(driver_name, cap):
    """MGRAPHIC's page segment table (driver CS:17f0, 8 words: page n -> segment; 0 = A000)."""
    if driver_name != "MGRAPHIC":
        return None
    mem = zlib.decompress(cap["memory"])
    seg = cap["block"][0]
    return [f"{w:04x}" for w in struct.unpack_from("<8H", mem, seg * 16 + 0x17f0)]


def slot_meta(cap):
    """(module, slot offset, name, convention, nargs) of the thunk slot a capture came through."""
    module = cap.get("module", "CODE")
    info = oc.module_info(module)
    sl = info["slots"].get(cap["slot"]) if cap["slot"] is not None else None
    eoff = cap["address"] - cap["block"][0] * 16
    if sl is None:
        return module, cap["slot"], f"entry_{eoff:04x}", "regs", 0
    return module, cap["slot"], sl["name"], sl["convention"], sl["nargs"] if sl["convention"] == "stack" else 0


def make(driver_name, caps, tries, rng):
    # one vector per driver entry (offset in the driver), whichever modules call it
    by_slot = {}
    for c in caps:
        by_slot.setdefault(c["address"] - c["block"][0] * 16, []).append(c)
    slots = code_slots()
    strings_of = {}

    def strings(module):
        if module not in strings_of:
            strings_of[module] = [int(k, 16) for k in json.loads(
                (oc.DATA / "build/code-analysis" / module / "strings.json").read_text()) if int(k, 16) >= 0x42]
        return strings_of[module]
    index = {}
    # Slots CODE calls but the routes never reached: realistic driver state of another entry of the
    # same driver, the call frame rebuilt for this slot with arguments from the C (DRIVER_SYNTHETIC).
    for slot, arglists in ((a, cfg.DRIVER_SYNTHETIC.get(sl["name"])) for a, sl in slots.items()):
        if slot in by_slot or not arglists or not caps:
            continue
        donor = next((c for c in caps if c.get("module", "CODE") == "CODE"), None)
        if donor is None:
            continue
        mem = zlib.decompress(donor["memory"])
        target = None
        base = rf.Base.from_states(oc.STATES / "CODE" / "decode-v1", "wait")
        for addr, sl in slot_targets(base.mem, base.load).items():
            if sl == slot:
                target = addr
        if target is None or not donor["block"][0] * 16 <= target < (donor["block"][0] + donor["block"][1]) * 16:
            continue
        if target - donor["block"][0] * 16 in by_slot:
            continue
        for args in arglists:
            m2 = bytearray(mem)
            ss_sp = donor["regs"]["ss"] * 16 + donor["regs"]["sp"]
            for i, v in enumerate(args):
                struct.pack_into("<H", m2, ss_sp + 4 + 2 * i, v & 0xffff)
            regs = dict(donor["regs"], cs=donor["block"][0], ip=target - donor["block"][0] * 16)
            by_slot.setdefault(target - donor["block"][0] * 16, []).append(dict(donor, memory=zlib.compress(bytes(m2), 1), regs=regs,
                                                     address=target, slot=slot, synthetic=True,
                                                     hit=f"{donor['hit']} (state of slot {donor['slot']:04x})"))
    for eoff, items in sorted(by_slot.items()):
        metas = [slot_meta(c) for c in items]
        named = [m for m in metas if not m[2].startswith(("sub_", "entry_"))]
        module, slot, name, conv, nargs = (named or metas)[0]
        callers = sorted({(m[0], m[1] if m[1] is not None else -1, m[2]) for m in metas})
        specs = cfg.DRIVER_ARGS.get(name, ["seen"] * nargs)
        check = cfg.DRIVER_ARG_CHECKS.get(name)
        driver = None
        cases, cov, seen_out, fails = [], set(), set(), []
        t0 = time.time()

        def add(cap, args, origin, realistic, why):
            nonlocal driver, cov
            res = run_driver(cap, args, nargs=nargs)
            if res["status"] != "return":
                fails.append(f"{origin}: {res['status']} {res['fault'] or ''}")
                return False
            again = run_driver(cap, args, script=rf.Script(replay=res["inputs"]), nargs=nargs)
            if any(again[k] != res[k] for k in ("video_out", "driver_out", "far_out", "calls", "result", "palette_out")):
                fails.append(f"{origin}: replay diverged")
                return False
            outcome = hashlib.sha1(json.dumps([res["video_out"], res["driver_out"], res["far_out"], res["calls"],
                                               res["result"]]).encode()).hexdigest()
            new = not res["coverage"] <= cov
            if why != "realistic" and not new and outcome in seen_out:
                return False
            if why == "realistic" and not new and (outcome in seen_out or len(cases) >= 12):
                return False
            seen_out.add(outcome)
            cov |= res["coverage"]
            mem = zlib.decompress(cap["memory"])
            ds = cap["regs"]["ds"]
            words = list(struct.unpack_from(f"<{nargs}H", res["stack"], 4)) if nargs else []
            driver = res["block"]
            cases.append(dict(
                id=f"{len(cases):02d}-{why}", origin=origin, realistic=realistic,
                args=show_args(mem, ds, words, name, conv), regs_in=res["regs_in"],
                driver_in=b64(res["driver_in"]), video_in=b64(res["video_in"]),
                palette_in=b64(res["palette_in"]), overscan_in=res["overscan_in"],
                far_in=res["far_in"], inputs=res["inputs"], result=res["result"], regs_out=res["regs_out"],
                driver_out=res["driver_out"], video_out=res["video_out"],
                palette_out=None if res["palette_out"] == res["palette_in"] else b64(res["palette_out"]),
                overscan_out=res["overscan_out"], far_out=res["far_out"], calls=res["calls"],
                stack_window=res["stack_window"],
                # per case: captures of several routes/layouts are merged into one entry's vector
                segments=dict(driver=f"{cap['block'][0]:04x}", driver_size=cap["block"][1] * 16,
                              dgroup=f"{cap['regs']['ds']:04x}", pages=pages_of(driver_name, cap),
                              module=cap.get("module", "CODE"))))
            return True

        for cap in items:
            if cap.get("synthetic"):
                add(cap, None, f"synthetic: arguments from the C on the state of capture {cap['route']} hit "
                               f"{cap['hit']}", True, "synthetic")
            else:
                add(cap, None, f"capture {cap.get('module', 'CODE')} {cap['route']} hit {cap['hit']}", True,
                    "realistic")
        # bounded variations of the stack arguments (only for slots with a model)
        if nargs and name in cfg.DRIVER_ARGS:
            seen_vals = {}
            for cap in items:
                if cap.get("synthetic"):
                    continue
                ss_sp = cap["regs"]["ss"] * 16 + cap["regs"]["sp"]
                mem = zlib.decompress(cap["memory"])
                for i, v in enumerate(struct.unpack_from(f"<{nargs}H", mem, ss_sp + 4)):
                    seen_vals.setdefault(i, set()).add(v)
            for _ in range(tries):
                if len(cases) >= 28:
                    break
                real = [c for c in items if not c.get("synthetic")]
                if not real:
                    break
                cap = rng.choice(real)
                mem = zlib.decompress(cap["memory"])
                ss_sp = cap["regs"]["ss"] * 16 + cap["regs"]["sp"]
                args = list(struct.unpack_from(f"<{nargs}H", mem, ss_sp + 4))
                what = []
                for _k in range(rng.randint(1, 3)):
                    i = rng.randrange(nargs)
                    spec = specs[i]
                    if spec == "seen":
                        args[i] = rng.choice(sorted(seen_vals[i]))
                    elif spec == "int":
                        args[i] = rng.choice([0, 1, 2, 5, 100, 0xffff, rng.randrange(0x10000)])
                    elif spec == "port":
                        args[i] = rng.choice(cfg.PORTS)
                    elif spec == "cstr":
                        args[i] = rng.choice(strings(cap.get("module", "CODE")))
                    elif spec[0] == "set":
                        args[i] = rng.choice(spec[1]) & 0xffff
                    else:
                        args[i] = rng.choice([spec[0], spec[1], rng.randint(spec[0], spec[1])]) & 0xffff
                    what.append(f"arg{i}")
                ds = cap["regs"]["ds"]
                signed = [v - 0x10000 if v & 0x8000 else v for v in args]
                ok = all(arg_ok(sp_, v, mem, ds) for sp_, v in zip(specs, args)) and (check is None or check(signed))
                if not ok:
                    continue        # keep the driver cases inside the model: bounded variations only
                add(cap, args, f"capture {cap['route']} hit {cap['hit']} vary {','.join(what)}", True, "variation")
        if not cases:
            index[name] = dict(entry=f"{eoff:04x}", cases=0, failures=fails[:5])
            continue
        seg, size = driver
        entry = items[0]["address"]
        vec = dict(driver=driver_name, slot=name, slot_offset=f"{slot:04x}" if slot is not None else None,
                   module=module, callers=[dict(module=c[0], slot=f"{c[1]:04x}" if c[1] >= 0 else None, name=c[2])
                                           for c in callers],
                   entry=f"{seg:04x}:{entry - seg * 16:04x}", convention=conv, nargs=nargs,
                   segments=dict(driver=f"{seg:04x}", driver_size=size * 16, video="a000",
                                 pages=pages_of(driver_name, items[0]),
                                 dgroup=f"{items[0]['regs']['ds']:04x}"),
                   cases=cases)
        out = oc.OUT / driver_name
        out.mkdir(parents=True, exist_ok=True)
        (out / f"{name}.json").write_text(json.dumps(vec) + "\n", encoding="utf-8")
        index[name] = dict(slot=vec["slot_offset"], entry=vec["entry"], cases=len(cases), blocks=len(cov),
                           realistic=sum(c["id"].endswith("realistic") for c in cases), failures=fails[:5],
                           seconds=round(time.time() - t0, 1))
        print(driver_name, name, "cases", len(cases), "blocks", len(cov), "fails", len(fails), flush=True)
    return index


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=("capture", "vectors"))
    p.add_argument("routes", nargs="*")
    p.add_argument("--tag", default="v1")
    p.add_argument("--tries", type=int, default=60)
    a = p.parse_args()
    import capture_states as cs
    if a.command == "capture":
        for route in a.routes or list(cs.ROUTES):
            out = STATES / f"{route}-{a.tag}"
            if out.exists():
                print(route, "exists, reusing", out)
                continue
            capture(route, out)
        return
    caps = []
    for folder in sorted(STATES.iterdir()):
        if (folder / "captures.pkl.gz").exists():
            with gzip.open(folder / "captures.pkl.gz", "rb") as f:
                for c in pickle.load(f):
                    c["route"] = folder.name
                    caps.append(c)
    # name each capture's driver after the shared header's driver segments (they differ per layout)
    groups = {}
    for c in caps:
        if c["block"] is None:
            continue
        mem = zlib.decompress(c["memory"])
        off, seg = struct.unpack_from("<HH", mem, 0x4f0)
        sh = seg * 16 + off
        gfx, misc, snd = struct.unpack_from("<3H", mem, sh + 0x60)
        letter = chr(mem[sh + 2])
        names = {gfx: {"M": "MGRAPHIC", "E": "EGRAPHIC", "C": "CGRAPHIC", "T": "TGRAPHIC"}.get(letter, "GRAPHIC"),
                 misc: "MISC", snd: "SOUND"}
        groups.setdefault(names.get(c["block"][0], f"DRIVER_{c['block'][0]:04x}"), []).append(c)
    for name, items in groups.items():
        index = make(name, items, a.tries, random.Random(f"driver:{name}"))
        out = oc.OUT / name
        out.mkdir(parents=True, exist_ok=True)
        (out / "_index.json").write_text(json.dumps(index, indent=1) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
