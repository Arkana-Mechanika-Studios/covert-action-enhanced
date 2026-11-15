"""Joystick oracle: vectors for the joystick routines of the input driver MISC.EXE, run on a fed game port.

    set COVERT_DATA_ROOT=<the folder that holds build/ and game/>
    set COVERT_ORACLE_OUT=build/oracle-joystick-20261115
    python -B tools/oracle/joystick_vectors.py

The driver is small and self-contained (MISC.EXE, 468 bytes of image: its own data at paragraph 15h of the
image, no call outside it), so it runs in Unicorn on its own: the player's MISC.EXE is loaded and relocated at a
fixed segment, and each step of a case far-calls one entry with the game port modelled:

    calibrate_joystick  MISC 0000:0080 (CODE's thunk 0000:03ac)
    read_joystick_axes  MISC 0000:00aa (CODE's thunk 0000:03b3)

The game port: a write to port 201h starts the axis timers; each read of the port afterwards gives bit 0 (the
first joystick's X timer) and bit 1 (its Y timer) set while fewer reads than the step's count for that axis have
been made since the write, and the step's high bits. A count of null is an axis without a stick (its timer never
ends). The driver's data starts as the loaded file has it (zero) at the first step of each case and carries on
from step to step, as in a game.

Writes <out>/MISC/_joystick.json:
    {"driver": "MISC", "entries": {...}, "cases": [{"id", "origin", "steps": [{"call": "calibrate" | "read",
     "counts": [x, y], "buttons": n, "ax": n (read only), "reads": n, "data": [words DS:50..83]}]}]}
Game-derived: never commit.
"""
import json
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import oracle_common as oc  # noqa: E402
import unicorn as uc  # noqa: E402
from unicorn.x86_const import (UC_X86_INS_IN, UC_X86_INS_OUT, UC_X86_REG_AX, UC_X86_REG_CS,  # noqa: E402
                               UC_X86_REG_DS, UC_X86_REG_IP, UC_X86_REG_SP, UC_X86_REG_SS)

LOAD = 0x1000                   # the driver's segment
STACK = 0x2000                  # the caller's stack segment
RETURN = (0x3000, 0x0000)       # where the far calls return to (a HLT)
ENTRIES = {"calibrate": 0x0080, "read": 0x00aa}
DATA_PARAGRAPH = 0x15           # the driver's DS: its image paragraph 15h (the relocated "mov ax, 15h")
DATA_FIRST, DATA_END = 0x50, 0x84
GAME_PORT = 0x201
NEVER = None
LOOP_TURNS = 0xffff             # CX of the timing loop


def load_driver():
    exe = oc.executables.load(oc.GAME / "MISC.EXE")
    image = bytearray(exe.image)
    for seg, off in exe.relocations:
        at = seg * 16 + off
        struct.pack_into("<H", image, at, (struct.unpack_from("<H", image, at)[0] + LOAD) & 0xffff)
    return bytes(image)


class Machine:
    def __init__(self, image):
        self.m = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.m.mem_map(0, 0x100000)
        self.m.mem_write(LOAD * 16, image)
        self.m.mem_write(RETURN[0] * 16 + RETURN[1], b"\xf4")
        self.counts, self.buttons, self.since, self.reads, self.writes = (0, 0), 0xf0, None, 0, 0
        self.m.hook_add(uc.UC_HOOK_INSN, self.port_in, None, 1, 0, UC_X86_INS_IN)
        self.m.hook_add(uc.UC_HOOK_INSN, self.port_out, None, 1, 0, UC_X86_INS_OUT)

    def port_in(self, mu, port, size, _):
        assert port == GAME_PORT and size == 1, (hex(port), size)
        bits = self.buttons & 0xf0 | 0x0c           # no second joystick: its two timers never end
        for axis, count in enumerate(self.counts):
            if count is NEVER or (self.since is not None and self.since < count):
                bits |= 1 << axis
        if self.since is not None:
            self.since += 1
        self.reads += 1
        return bits

    def port_out(self, mu, port, size, value, _):
        assert port == GAME_PORT, hex(port)
        self.since = 0
        self.writes += 1

    def call(self, entry, counts, buttons):
        self.counts, self.buttons, self.since, self.reads, self.writes = tuple(counts), buttons, None, 0, 0
        m = self.m
        sp = 0xfff0
        m.mem_write(STACK * 16 + sp, struct.pack("<HH", RETURN[1], RETURN[0]))
        for reg, v in ((UC_X86_REG_SS, STACK), (UC_X86_REG_SP, sp), (UC_X86_REG_DS, 0x4000),
                       (UC_X86_REG_AX, 0x5555), (UC_X86_REG_CS, LOAD), (UC_X86_REG_IP, ENTRIES[entry])):
            m.reg_write(reg, v)
        m.emu_start(LOAD * 16 + ENTRIES[entry], RETURN[0] * 16 + RETURN[1], count=2_000_000)
        cs, ip = m.reg_read(UC_X86_REG_CS), m.reg_read(UC_X86_REG_IP)
        assert (cs, ip) == RETURN, f"{entry} ended at {cs:04x}:{ip:04x}"
        assert m.reg_read(UC_X86_REG_DS) == 0x4000, "DS not restored"
        assert self.writes == 1, self.writes
        base = (LOAD + DATA_PARAGRAPH) * 16
        data = list(struct.unpack(f"<{(DATA_END - DATA_FIRST) // 2}H",
                                  bytes(m.mem_read(base + DATA_FIRST, DATA_END - DATA_FIRST))))
        return m.reg_read(UC_X86_REG_AX), data


def run_case(image, steps):
    machine = Machine(image)
    out = []
    for call, counts, buttons in steps:
        ax, data = machine.call(call, counts, buttons)
        step = dict(call=call, counts=list(counts), buttons=buttons, reads=machine.reads, data=data)
        if call == "read":
            step["ax"] = ax
        out.append(step)
    return out


def hand_made():
    c, r = "calibrate", "read"
    centre, low, high = 61, 3, 119
    yield "no joystick: calibrated, then read", [(c, (NEVER, NEVER), 0xf0), (r, (NEVER, NEVER), 0xf0),
                                                 (r, (NEVER, NEVER), 0xf0)]
    yield "no joystick: read before any calibration", [(r, (NEVER, NEVER), 0xf0), (r, (NEVER, NEVER), 0xf0)]
    yield "a stick read before any calibration", [(r, (centre, centre), 0xf0), (r, (30, 90), 0xf0),
                                                  (r, (centre, centre), 0xf0)]
    sweep = [(r, (x, centre), 0xf0) for x in (61, 60, 40, 3, 4, 10, 30, 59, 60, 62, 63, 100, 119, 118, 90, 62, 61)]
    yield "calibrated at the centre, X swept", [(c, (centre, centre), 0xf0)] + sweep
    sweep = [(r, (centre, y), 0xf0) for y in (61, 3, 2, 3, 31, 60, 119, 120, 119, 61)]
    yield "calibrated at the centre, Y swept", [(c, (centre, centre), 0xf0)] + sweep
    yield "the corners", [(c, (centre, centre), 0xf0)] + [(r, xy, 0xf0) for xy in
                                                           ((low, low), (high, high), (low, high), (high, low),
                                                            (low + 1, high - 1), (centre, centre))]
    yield "recalibrated off centre", [(c, (centre, centre), 0xf0), (r, (low, high), 0xf0), (c, (70, 50), 0xf0),
                                      (r, (61, 61), 0xf0), (r, (low, high), 0xf0), (r, (40, 80), 0xf0)]
    yield "one axis without a stick", [(c, (centre, NEVER), 0xf0), (r, (low, NEVER), 0xf0), (r, (40, NEVER), 0xf0),
                                       (r, (high, 100), 0xf0), (r, (centre, 0), 0xf0)]
    yield "counts at the loop's end", [(c, (65534, 65535), 0xf0), (r, (65535, 65536), 0xf0),
                                       (r, (0, 70000), 0xf0), (r, (1, 65534), 0xf0), (r, (40000, 30000), 0xf0)]
    yield "zero counts", [(c, (0, 0), 0xf0), (r, (0, 0), 0xf0), (r, (1, 1), 0xf0), (r, (0, 2), 0xf0)]
    yield "buttons held", [(c, (centre, centre), 0xe0), (r, (centre, centre), 0xc0), (r, (low, high), 0x00)]


def random_cases(rng, n):
    for k in range(n):
        steps = []
        centre = (rng.randint(5, 250), rng.randint(5, 250))
        steps.append(("calibrate", centre, 0xf0))
        for _ in range(rng.randint(5, 25)):
            if rng.random() < 0.08:
                centre = (rng.randint(0, 300), rng.randint(0, 300))
                steps.append(("calibrate", centre, 0xf0))
                continue
            counts = tuple(max(0, c + rng.choice([0, 0, 1, -1, rng.randint(-60, 60), rng.randint(-200, 200)]))
                           for c in centre)
            steps.append(("read", counts, rng.choice([0xf0, 0xe0, 0xd0, 0x30])))
        yield f"random {k}", steps


def main():
    image = load_driver()
    rng = random.Random("joystick:MISC")
    cases = []
    for origin, steps in [*hand_made(), *random_cases(rng, 40)]:
        cases.append(dict(id=f"{len(cases):02d}", origin=origin, steps=run_case(image, steps)))
    out = oc.OUT / "MISC"
    out.mkdir(parents=True, exist_ok=True)
    vec = dict(driver="MISC", entries={k: f"0000:{v:04x}" for k, v in ENTRIES.items()},
               data=f"DS:{DATA_FIRST:02x}..{DATA_END - 1:02x} (DS = image paragraph {DATA_PARAGRAPH:02x}h)",
               cases=cases)
    (out / "_joystick.json").write_text(json.dumps(vec) + "\n", encoding="utf-8")
    reads = sum(1 for c in cases for s in c["steps"] if s["call"] == "read")
    print("MISC joystick cases", len(cases), "reads", reads, "->", out / "_joystick.json")


if __name__ == "__main__":
    main()
