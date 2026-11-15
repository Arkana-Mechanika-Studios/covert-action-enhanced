"""Capture realistic states of every module (and driver entries) on the project's recorded routes.

    python -B tools/oracle/capture_all.py [routes...] [--tag v1]

Each route runs tools/trace_game.GameProbe (COVERT -> INTRO -> FINAL -> GAME / CODE / TAC / BUG /
CHASE, the existing DOS/BIOS fixtures, seeded sessions, key scheduler) with the documented memory
layout, idle-poll setting and action list of the route.
While a tracked module runs, the entry of each of its functions (functions.json + named entry
points) is captured at hits 1, 2, 3, 5, 9, 17, 33, 100, 333, 1000: registers, stack, DGROUP and
shared block (zlib). Per module two 1 MiB base images: at process start and at the first keyboard
wait (or, for a module that never waits, when it starts a child or exits). Driver entries reached
from a tracked module are captured too (whole machine, zlib, palette), keyed by driver entry.

Output: build/oracle/states/<MODULE>/<route>-<tag>/ (same layout as capture_states.py) and
build/oracle/states/DRIVERS/<route>-<tag>-all/. Existing folders are reused, never overwritten.
"""
import argparse
import gzip
import json
import pickle
import struct
import sys
import time
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import capture_states  # noqa: E402,F401  (extra key names, Alt-S etc.)
import trace_final  # noqa: E402
from trace_game import GameProbe, BRIEFING_ACTIONS  # noqa: E402
from trace_final import ROOT, REGISTERS  # noqa: E402
import oracle_common as oc  # noqa: E402

MODULES = ("COVERT", "INTRO", "FINAL", "GAME", "TAC", "BUG", "CHASE")      # CODE: capture_states.py
HITS = (1, 2, 3, 5, 9, 17, 33, 100, 333, 1000)
DRIVER_HITS = set(range(1, 7)) | {9, 16, 32, 64, 128, 256, 512, 1024, 4096}
GO = ROOT / "build/game-oracle"
TAC_PHOTO = (["DOWN", "ENTER", "C", "ENTER", "ENTER"] + ["DOWN"] * 6 + ["ENTER", "DOWN", "ENTER"]
             + ["RIGHT", "ENTER", "DOWN", "ENTER", "LEFT", "ENTER", "DOWN", "ENTER", "DOWN", "RIGHT", "ENTER", "ESC"]
             + ["ENTER", "UP", "LEFT", "F1", "F2"] + ["ENTER"] * 3 + ["2"] * 3)


def _actions(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


# route -> (memory layout, idle polls, seed session or None, actions, budget)
ROUTES = {
    "campaign": ("legacy", 256, None, lambda: _actions(GO / "roundtrip-actions.json"), 300_000_000),
    "tacphoto": ("legacy", 255, GO / "save-pending/phase-001/session-files/CV0.SVE", lambda: TAC_PHOTO, 500_000_000),
    "stasi": ("compact", 127, GO / "investigation-code-return/phase-015/session-files/CV0.SVE",
              lambda: _actions(GO / "stasi-arrest-save-actions.json"), 600_000_000),
    "bug": ("legacy", 256, None, lambda: _actions(GO / "bug-practice-success-return-actions.json"), 100_000_000),
    "chase": ("compact", 15, None, lambda: _actions(ROOT / "build/chase-native-9283f7d5a1/actions.json"), 600_000_000),
    "chaseabort": ("legacy", 256, None, lambda: _actions(GO / "chase-practice-result-actions.json"), 200_000_000),
}


class MultiProbe(GameProbe):
    def __init__(self, *a, **k):
        super().__init__(*a, **k)
        self.blocking_input_staging = True
        self.allowed_programs |= {m + ".EXE" for m in MODULES + ("CODE",)}
        self.keyboard_programs |= {m + ".EXE" for m in MODULES + ("CODE",)}
        self.mods = {}
        self.drv_ranges = None
        self.drv_hits, self.drv_caps = {}, []
        self.last_slot, self.prev_module = None, False

    def mod(self):
        name = self.current["name"][:-4]
        st = self.mods.get(name)
        return st if st and st["meta"]["load"] == self.current["load"] else None

    def take_wait(self, why):
        st = self.mod()
        if st and st["base_wait"] is None:
            psp = self.current["psp"]
            st["base_wait"] = bytes(self.machine.mem_read(0, 0x100000))
            st["meta"].update(block_end=psp + self.blocks[psp][0], wait_reason=why, wait_instructions=self.instructions)

    def start_process(self, name, tail=b"", parent=0):
        if getattr(self, "current", None):
            self.take_wait("child " + name)
        super().start_process(name, tail, parent)
        m = name.upper()[:-4]
        if m in MODULES and m not in self.mods:
            info = oc.module_info(m)
            load = self.current["load"]
            off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
            self.mods[m] = dict(
                info=info, entries={load * 16 + f["linear"]: l for l, f in info["functions"].items()},
                slots={load * 16 + a: a for a in info["slots"]}, hits={}, captures=[], main_seen=False,
                base_start=bytes(self.machine.mem_read(0, 0x100000)), base_wait=None,
                meta=dict(psp=self.current["psp"], load=load, dgroup=load + info["dgroup_frame"], shared=[seg, off]))
        self.drv_ranges = None

    def interrupt(self, machine, number, context):
        if number == 0x21 and self.get("ax") >> 8 == 0x4c:
            self.take_wait("exit")
        super().interrupt(machine, number, context)

    def snapshot(self, label):
        super().snapshot(label)
        if label.startswith("wait"):
            self.take_wait(label)

    def driver_ranges(self):
        off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
        if not seg:
            return []
        sh = seg * 16 + off
        segs = struct.unpack("<3H", self.machine.mem_read(sh + 0x60, 6))
        out = []
        for s in segs:
            if s in self.blocks:
                out.append((s * 16, (s + self.blocks[s][0]) * 16, s))
        return out

    def instruction(self, machine, address, size, context):
        super().instruction(machine, address, size, context)
        st = self.mod()
        if st is None:
            self.prev_module = False
            return
        meta = st["meta"]
        psp = meta["psp"]
        inside = meta["load"] * 16 <= address < (psp + self.blocks.get(psp, (0xa000, 0))[0]) * 16
        if inside:
            slot = st["slots"].get(address)
            if slot is not None:
                self.last_slot = slot
            label = st["entries"].get(address)
            if label is not None:
                self.capture(st, label)
            self.prev_module = True
            return
        if self.prev_module:
            if self.drv_ranges is None:
                self.drv_ranges = self.driver_ranges()
            for lo, hi, seg in self.drv_ranges:
                if lo <= address < hi:
                    self.capture_driver(st, address, (seg, (hi - lo) // 16))
                    break
        self.prev_module = False

    def capture(self, st, label):
        n = st["hits"].get(label, 0) + 1
        st["hits"][label] = n
        if label == st["info"]["main"]:
            st["main_seen"] = True
        if n not in HITS:
            return
        meta = st["meta"]
        regs = {r: self.get(r) for r in REGISTERS}
        psp = self.current["psp"]
        dsize = min(0x10000, (psp + self.blocks[psp][0]) * 16 - meta["dgroup"] * 16)
        off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
        shared = bytes(self.machine.mem_read(seg * 16 + off, oc.SHARED_SIZE)) if seg else None
        st["captures"].append(dict(
            label=label, hit=n, instructions=self.instructions, after_main=st["main_seen"], regs=regs,
            stack=bytes(self.machine.mem_read(regs["ss"] * 16 + regs["sp"], 64)),
            dgroup_z=zlib.compress(bytes(self.machine.mem_read(meta["dgroup"] * 16, dsize)), 1),
            shared_z=zlib.compress(shared, 1) if shared else None, shared_ptr=[seg, off]))

    def capture_driver(self, st, address, block):
        key = (st["info"]["module"], address)
        n = self.drv_hits.get(key, 0) + 1
        self.drv_hits[key] = n
        if n not in DRIVER_HITS:
            return
        self.drv_caps.append(dict(
            module=st["info"]["module"], slot=self.last_slot, hit=n, address=address,
            regs={r: self.get(r) for r in REGISTERS}, block=block, palette=bytes(self.palette),
            overscan=self.overscan, memory=zlib.compress(bytes(self.machine.mem_read(0, 0x100000)), 1)))


def capture(route, tag):
    layout, idle, seed, actions, budget = ROUTES[route]
    probe = MultiProbe(oc.GAME, "212 ", budget, actions(), discovery=False, artifacts="validation",
                       seed_session=seed, idle_polls=idle, memory_layout=layout)
    t = time.time()
    report = probe.run()
    for _ in range(8):      # recorded EXEC boundaries of not-yet-allowed programs: continue like --continue-program
        if not (probe.pending_exec_boundary and report["stop_reason"].startswith("reached next process")):
            break
        report = probe.run(resume=True)
    print(route, report["stop_reason"], report["instructions"], round(time.time() - t), "s", flush=True)
    for name, st in probe.mods.items():
        out = oc.STATES / name / f"{route}-{tag}"
        if out.exists() or not st["captures"]:
            continue
        if st["base_wait"] is None:
            st["base_wait"] = bytes(probe.machine.mem_read(0, 0x100000))
            psp = st["meta"]["psp"]
            st["meta"].update(block_end=psp + probe.blocks.get(psp, (0x1000, 0))[0], wait_reason="end of run")
        out.mkdir(parents=True)
        (out / "base_start.mem").write_bytes(st["base_start"])
        (out / "base_wait.mem").write_bytes(st["base_wait"])
        with gzip.open(out / "captures.pkl.gz", "wb") as f:
            pickle.dump(st["captures"], f)
        meta = dict(st["meta"], route=route, layout=layout, idle_polls=idle, seed=str(seed) if seed else None,
                    stop_reason=report["stop_reason"], captures=len(st["captures"]), hits=st["hits"])
        (out / "meta.json").write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")
        print(" ", name, "captures", len(st["captures"]), "functions", len(st["hits"]), flush=True)
    if probe.drv_caps:
        out = oc.STATES / "DRIVERS" / f"{route}-{tag}-all"
        if not out.exists():
            out.mkdir(parents=True)
            with gzip.open(out / "captures.pkl.gz", "wb") as f:
                pickle.dump(probe.drv_caps, f)
            (out / "meta.json").write_text(json.dumps(dict(route=route, captures=len(probe.drv_caps)), indent=1) + "\n")
        print("  drivers captures", len(probe.drv_caps), flush=True)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("routes", nargs="*", default=list(ROUTES))
    p.add_argument("--tag", default="v1")
    a = p.parse_args()
    for r in a.routes:
        capture(r, a.tag)


if __name__ == "__main__":
    main()
