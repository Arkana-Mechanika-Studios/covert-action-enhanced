"""Capture realistic module states for the function oracle from scripted original runs.

Drives the original COVERT -> INTRO -> FINAL -> CODE practice route with the
existing probes (tools/trace_code.py CodeProbe, its DOS/BIOS fixtures and key
scheduler) and, while CODE.EXE runs, records the machine at the entry of every
function in build/code-analysis/CODE/functions.json: registers, the stack words,
the whole DGROUP window and the 12000-byte shared block. Two full one-megabyte
base images are kept per route: at CODE process start (before crt0) and at the
first workstation keyboard wait (drivers attached, thunks patched).

Output (game-derived, never committed): build/oracle/states/CODE/<route>/
  base_start.mem, base_wait.mem   1 MiB memory images
  captures.pkl.gz                 list of entry captures
  meta.json                       segments, layout, counts, route keys

An existing route directory is reused, never overwritten (evidence retention);
pass a new --tag to capture again.
"""
import argparse
import gzip
import json
import pickle
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import trace_final  # noqa: E402
import trace_code  # noqa: E402
from trace_code import CodeProbe, PRACTICE_ACTIONS  # noqa: E402
from trace_final import ROOT, REGISTERS  # noqa: E402
import oracle_common as oc  # noqa: E402

# Extra BIOS key words for keys the existing fixture does not name.
EXTRA_KEYS = {"": 0x1f00, "": 0x2400, "": 0x2000}   # Alt-S, Alt-J, Alt-D
trace_final.KEY_NAMES.update(ALT_S="", ALT_J="", ALT_D="")
_orig_bios_key_code = trace_final.bios_key_code


def bios_key_code(key):
    return EXTRA_KEYS.get(key) or _orig_bios_key_code(key)


trace_final.bios_key_code = bios_key_code
trace_code.bios_key_code = bios_key_code

ROUTES = {
    # Correct decode and return to FINAL.
    "decode": PRACTICE_ACTIONS + ["ua", "hbrcsdffegdhwinlqmlnzoypirjsktmu", "ENTER"],
    # Hint, reset, wrong guess, sound toggles, pause, quit prompt (No), then ESC.
    "explore": PRACTICE_ACTIONS + ["F1", "F10", "ux", "ALT_S", "ab", "ALT_S", "F1",
                                   "ALT_P", " ", "ALT_Q", "ENTER", "ESC"],
    # Quit prompt answered Yes: GoodBye + exit.
    "quit": PRACTICE_ACTIONS + ["ALT_Q", "DOWN", "ENTER", "ENTER"],
}
# Hit numbers of a function at which its entry state is captured.
CAPTURE_HITS = (1, 2, 3, 4, 6, 9, 16, 33, 100, 333, 1000, 3333)


class CaptureProbe(CodeProbe):
    def __init__(self, *args, module="CODE", **kwargs):
        super().__init__(*args, **kwargs)
        self.blocking_input_staging = True
        self.module = module
        self.info = oc.module_info(module)
        self.entries = {}
        self.hits = {}
        self.captures = []
        self.base_start = self.base_wait = None
        self.main_seen = False
        self.meta = {}
        self.module_exited = False

    def start_process(self, name, tail=b"", parent=0):
        if self.base_start is not None and self.current.get("name") == self.module + ".EXE":
            self.module_exited = True
        super().start_process(name, tail, parent)
        if name.upper() == self.module + ".EXE" and self.base_start is None:
            load = self.current["load"]
            self.entries = {load * 16 + f["linear"]: label for label, f in self.info["functions"].items()}
            self.base_start = bytes(self.machine.mem_read(0, 0x100000))
            off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
            self.meta.update(psp=self.current["psp"], load=load,
                             dgroup=load + self.info["dgroup_frame"],
                             shared=[seg, off])
        if self.module_exited:
            self.stop(f"{self.module}.EXE exited")

    def instruction(self, machine, address, size, context):
        super().instruction(machine, address, size, context)
        label = self.entries.get(address)
        if label is None or self.current["name"] != self.module + ".EXE" or self.module_exited:
            return
        n = self.hits.get(label, 0) + 1
        self.hits[label] = n
        if label == self.info["main"]:
            self.main_seen = True
        if n not in CAPTURE_HITS:
            return
        regs = {r: self.get(r) for r in REGISTERS}
        ds = self.meta["dgroup"]
        psp = self.current["psp"]
        block_end = (psp + self.blocks[psp][0]) * 16
        dsize = min(0x10000, block_end - ds * 16)
        off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
        shared = bytes(self.machine.mem_read(seg * 16 + off, oc.SHARED_SIZE)) if seg else None
        ss_lin = regs["ss"] * 16 + regs["sp"]
        self.captures.append(dict(
            label=label, hit=n, instructions=self.instructions, after_main=self.main_seen,
            regs=regs, stack=bytes(self.machine.mem_read(ss_lin, 64)),
            dgroup=bytes(self.machine.mem_read(ds * 16, dsize)),
            shared=shared, shared_ptr=[seg, off]))

    def snapshot(self, label):
        super().snapshot(label)
        if (self.base_wait is None and label.startswith("wait")
                and self.current["name"] == self.module + ".EXE"):
            self.base_wait = bytes(self.machine.mem_read(0, 0x100000))
            psp = self.current["psp"]
            self.meta.update(block_end=psp + self.blocks[psp][0], wait_instructions=self.instructions)


def capture(route, out, budget):
    actions = ROUTES[route]
    probe = CaptureProbe(ROOT / "game/Covert Action/Covert", "212 ", budget, actions,
                         discovery=False, artifacts="validation", memory_layout="compact")
    started = time.time()
    report = probe.run()
    if probe.base_wait is None:
        raise SystemExit(f"{route}: never reached a CODE keyboard wait ({report['stop_reason']})")
    out.mkdir(parents=True, exist_ok=False)
    (out / "base_start.mem").write_bytes(probe.base_start)
    (out / "base_wait.mem").write_bytes(probe.base_wait)
    with gzip.open(out / "captures.pkl.gz", "wb") as f:
        pickle.dump(probe.captures, f)
    meta = dict(probe.meta, route=route, actions=actions, stop_reason=report["stop_reason"],
                instructions=report["instructions"], seconds=round(time.time() - started, 1),
                captures=len(probe.captures), hits=probe.hits,
                memory_layout=report.get("memory_layout"))
    (out / "meta.json").write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")
    print(route, report["stop_reason"], report["instructions"], "captures", len(probe.captures),
          "functions", len(probe.hits), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("routes", nargs="*", default=list(ROUTES))
    parser.add_argument("--budget", type=int, default=60_000_000)
    parser.add_argument("--tag", default="v1")
    args = parser.parse_args()
    for route in args.routes:
        out = oc.STATES / "CODE" / f"{route}-{args.tag}"
        if out.exists():
            print(route, "exists, reusing", out); continue
        capture(route, out, args.budget)


if __name__ == "__main__":
    main()
