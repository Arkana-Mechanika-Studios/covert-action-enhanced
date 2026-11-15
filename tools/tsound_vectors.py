"""Sound-chip vectors of the original Tandy sound driver (TSOUND.CA) for the C# rewrite's driver tests.

Runs the supplied driver's own instructions in Unicorn through scenarios of play calls and timer interrupts, with a
synthetic caller, the BIOS clock stub of tools/trace_sound.py and a BIOS that answers the Tandy installation check, and
writes, per call and per interrupt, what the driver does to the sound chip and the timer, as events:

    psg xx xx ..   bytes written to the SN76496's port (00C0h), consecutive bytes joined
    timer XXXX     timer channel 0 (the interrupt's rate) reprogrammed (port 43h <- 36h, port 40h low, high)

The start's BIOS call (int 1Ah, AH=81h: the Tandy sound-chip enable and installation check) is answered as a Tandy BIOS
does: AX comes back with its top four bits clear, which the driver takes as "the chip is there" (it then sets DS:00F4 to
FFh, which the probe checks; nothing in the driver reads that byte). Left out of the events: the end-of-interrupt writes
(port 20h). Every third interrupt the driver passes on to the BIOS stub, as on the PC. Interrupts are delivered with the
carry flag clear: the driver's random number adds the carry the interrupted program left, which no fixture can know.

Play calls push the command and two further words (the car chase's gear, the raid's gunfire level and intensity);
init pushes the 0 the game passes.

    python -B tools/tsound_vectors.py --out build/oracle/TSOUND/psg.json

The output must be below a build folder; it is game-derived evidence and is never committed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from executables import load
from trace_startup import ROOT, REGISTERS, uc
from trace_sound import LOAD_SEGMENT, BIOS_SEGMENT, BIOS_OFFSET, CALL_RETURN, IRQ_RETURN, BIOS_BYTES, INITIAL_REGISTERS, FLAGS_MASK
from unicorn.x86_const import UC_X86_INS_IN, UC_X86_INS_OUT

DRIVER = "TSOUND.CA"
PSG_PORT = 0xc0
DATA_SEGMENT = LOAD_SEGMENT + 0xa1
VOICES = [0x6b, 0x88, 0xa5, 0xc2, 0xdf]       # the five voices' tick bytes
GUNFIRE_ON = 0xec
BIOS_FOUND = 0xf4
COMMANDS = 47
QUIET_TAIL = 3
CAP = 400                  # interrupts an "until quiet" waits at most (the songs and the title go on until stopped)


class TandyProbe:
    """Loads TSOUND.CA at LOAD_SEGMENT and runs its entries and its timer interrupt."""

    def __init__(self, game):
        self.executable = load(game / DRIVER)
        image = bytearray(self.executable.image)
        for segment, offset in self.executable.relocations:
            address = segment * 16 + offset
            struct.pack_into("<H", image, address, (struct.unpack_from("<H", image, address)[0] + LOAD_SEGMENT) & 0xffff)
        first, count = struct.unpack_from("<HH", image, 0x2e)
        self.slots = dict(zip(range(first, first + count), struct.unpack_from("<" + "H" * count, image, 0x32)))
        if [self.slots[i] for i in (100, 101, 102)] != [0x00a6, 0x00d8, 0x010f]:
            raise ValueError("TSOUND public entries changed")
        self.machine = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.machine.mem_map(0, 0x100000)
        self.machine.mem_write(LOAD_SEGMENT * 16, bytes(image))
        self.machine.mem_write(0x20, struct.pack("<HH", BIOS_OFFSET, BIOS_SEGMENT))
        self.machine.mem_write(BIOS_SEGMENT * 16 + BIOS_OFFSET, BIOS_BYTES)
        for name, value in INITIAL_REGISTERS.items():
            self.machine.reg_write(REGISTERS[name], value)
        self.events = []
        self.timer_low = None
        self.bios_calls = 0
        self.return_ip = None; self.returned = False
        sentinels = BIOS_SEGMENT * 16
        self.machine.hook_add(uc.UC_HOOK_CODE, self.sentinel, None, sentinels + CALL_RETURN, sentinels + IRQ_RETURN)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_read, None, 1, 0, UC_X86_INS_IN)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_write, None, 1, 0, UC_X86_INS_OUT)
        self.machine.hook_add(uc.UC_HOOK_INTR, self.interrupt)

    def get(self, name): return self.machine.reg_read(REGISTERS[name])
    def set(self, **values):
        for name, value in values.items(): self.machine.reg_write(REGISTERS[name], value)
    def push(self, value):
        sp = (self.get("sp") - 2) & 0xffff; self.set(sp=sp)
        self.machine.mem_write(self.get("ss") * 16 + sp, struct.pack("<H", value & 0xffff))
    def byte(self, offset): return self.machine.mem_read(DATA_SEGMENT * 16 + offset, 1)[0]

    def sentinel(self, machine, address, size, context):
        if self.get("cs") == BIOS_SEGMENT and self.get("ip") == self.return_ip:
            self.returned = True; machine.emu_stop()

    def port_read(self, machine, port, size, context):
        raise ValueError(f"unexpected read {port:x}")

    def port_write(self, machine, port, size, value, context):
        if size != 1: raise ValueError("byte port writes only")
        if port == PSG_PORT: self.events.append(("psg", value)); return
        if port == 0x20 and value == 0x20: return
        if port == 0x43 and value == 0x36: self.timer_low = -1; return
        if port == 0x40 and self.timer_low is not None:
            if self.timer_low == -1: self.timer_low = value; return
            self.events.append(("timer", self.timer_low | value << 8)); self.timer_low = None; return
        raise ValueError(f"unexpected write {port:02x} {value:02x}")

    def interrupt(self, machine, number, context):
        if number == 0x1a and self.get("ax") >> 8 == 0x81:
            self.bios_calls += 1
            self.set(ax=0x0000)       # a Tandy BIOS: the check passes (top four bits of AX clear)
            return
        raise ValueError(f"unexpected software interrupt {number:02x}")

    def execute(self, return_ip):
        self.return_ip = return_ip; self.returned = False
        self.machine.emu_start(self.get("cs") * 16 + self.get("ip"), 0x100000, count=3_000_000)
        if not self.returned: raise ValueError("driver operation did not reach its return sentinel")

    def call(self, slot, args):
        ss, sp = self.get("ss"), self.get("sp")
        for value in reversed(args): self.push(value)
        self.push(BIOS_SEGMENT); self.push(CALL_RETURN)
        self.set(cs=LOAD_SEGMENT, ip=self.slots[slot])
        self.execute(CALL_RETURN)
        self.set(sp=(self.get("sp") + 2 * len(args)) & 0xffff)
        if self.get("ss") != ss or self.get("sp") != sp: raise ValueError("unbalanced call")
        return self.get("ax")

    def irq(self):
        self.set(ip=IRQ_RETURN, eflags=self.get("eflags") & ~1)
        before = {name: self.get(name) for name in REGISTERS}
        off, seg = struct.unpack("<HH", self.machine.mem_read(0x20, 4))
        self.push(before["eflags"]); self.push(before["cs"]); self.push(IRQ_RETURN)
        self.set(cs=seg, ip=off, eflags=before["eflags"] & ~0x300)
        self.execute(IRQ_RETURN)
        after = {name: self.get(name) for name in REGISTERS}
        changed = [n for n in REGISTERS if n != "eflags" and before[n] != after[n]]
        if changed or (before["eflags"] ^ after["eflags"]) & FLAGS_MASK: raise ValueError(f"IRQ changed {changed}")

    def quiet(self):
        return not any(self.byte(v) for v in VOICES) and not self.byte(GUNFIRE_ON)

    def take(self):
        """The events since the last take, chip bytes in a row joined into one event."""
        out = []
        for kind, value in self.events:
            if kind == "psg":
                if out and out[-1].startswith("psg "): out[-1] += f" {value:02x}"
                else: out.append(f"psg {value:02x}")
            else: out.append(f"timer {value:04x}")
        self.events.clear()
        return out


def run(game, scenario):
    probe = TandyProbe(game); ops = []
    for action in scenario["ops"]:
        irqs = []; events = []
        if action[0] == "init":
            probe.call(100, [0]); events = probe.take()
            if probe.bios_calls != 1 or probe.byte(BIOS_FOUND) != 0xff: raise ValueError("the driver did not take the BIOS as Tandy")
            probe.bios_calls = 0
        elif action[0] == "stop": probe.call(102, []); events = probe.take()
        elif action[0] == "play":
            probe.call(101, list(action[1:]) + [0] * (3 - len(action[1:]))); events = probe.take()
        elif action[0] == "irq":
            for _ in range(action[1]): probe.irq(); irqs.append(probe.take())
        elif action[0] == "until_quiet":
            tail = 0; cap = action[1] if len(action) > 1 else CAP
            while tail < QUIET_TAIL and len(irqs) < cap:
                probe.irq(); irqs.append(probe.take()); tail = tail + 1 if probe.quiet() else 0
        else: raise ValueError(action)
        ops.append(dict(op=action[0], args=list(action[1:]), events=events, irqs=irqs))
    return dict(name=scenario["name"], ops=ops)


def scenarios():
    rows = []
    for command in range(COMMANDS):
        args = (2,) if command in (26, 27) else (2, 3) if command == 44 else ()
        tail = [("irq", 300), ("play", 44, 0, 0), ("until_quiet",)] if command == 44 else [("until_quiet",), ("play", 1), ("until_quiet",)]
        rows.append(dict(name=f"command {command}", ops=[("init",), ("irq", 3), ("play", command) + args] + tail))
    rows += [
        dict(name="engine gears of both cars", ops=[("init",), ("play", 26, 4), ("irq", 40), ("play", 27, 1), ("irq", 40), ("play", 26, 0),
                                                    ("irq", 60), ("play", 27, 0), ("irq", 20), ("play", 26, 3), ("irq", 30), ("play", 26, 3),
                                                    ("play", 0), ("irq", 4)]),
        dict(name="one car through its gears", ops=[("init",), ("play", 26, 1), ("irq", 30), ("play", 26, 3), ("irq", 10), ("play", 26, 6),
                                                    ("irq", 10), ("play", 26, 0), ("irq", 5)]),
        dict(name="gear 0 of a silent engine", ops=[("init",), ("play", 27, 0), ("irq", 5), ("play", 27, 2), ("irq", 5)]),
        dict(name="idle engine", ops=[("init",), ("play", 23), ("irq", 120), ("play", 23), ("irq", 120), ("play", 0), ("irq", 3)]),
        dict(name="take-off and landing", ops=[("init",), ("play", 35), ("irq", 20), ("play", 36), ("irq", 20)]),
        dict(name="pause and resume", ops=[("init",), ("play", 2), ("irq", 120), ("play", 37), ("irq", 30), ("play", 38), ("irq", 120),
                                           ("play", 1), ("until_quiet",)]),
        dict(name="pause twice loses the voices", ops=[("init",), ("play", 2), ("irq", 120), ("play", 37), ("irq", 5), ("play", 37),
                                                       ("irq", 5), ("play", 38), ("irq", 60), ("play", 3), ("until_quiet",)]),
        dict(name="pause while quiet", ops=[("init",), ("play", 37), ("irq", 5), ("play", 38), ("irq", 5)]),
        dict(name="pause with the gunfire", ops=[("init",), ("play", 44, 2, 3), ("irq", 100), ("play", 37), ("irq", 20), ("play", 38),
                                                 ("irq", 100), ("play", 0), ("irq", 40), ("until_quiet",)]),
        dict(name="pause with the engine", ops=[("init",), ("play", 26, 3), ("irq", 30), ("play", 37), ("irq", 10), ("play", 38),
                                                ("irq", 10), ("play", 26, 3), ("irq", 10), ("play", 1), ("until_quiet",)]),
        dict(name="effects over each other", ops=[("init",), ("play", 3), ("irq", 2), ("play", 4), ("irq", 2), ("play", 7), ("irq", 2),
                                                  ("play", 6), ("irq", 2), ("play", 34), ("until_quiet",)]),
        dict(name="an effect over the theme", ops=[("init",), ("play", 2), ("irq", 100), ("play", 45), ("irq", 60), ("play", 32), ("irq", 200),
                                                   ("play", 12), ("irq", 40), ("play", 10), ("irq", 40), ("play", 1), ("until_quiet",)]),
        dict(name="overlays on every channel", ops=[("init",), ("play", 2), ("irq", 30), ("play", 13), ("irq", 80), ("play", 15), ("irq", 80),
                                                    ("play", 18), ("irq", 80), ("play", 30), ("irq", 80), ("play", 19), ("irq", 80),
                                                    ("play", 1), ("until_quiet",)]),
        dict(name="voices restarted", ops=[("init",), ("play", 2), ("irq", 50)] + [op for c in range(39, 44) for op in (("play", c), ("irq", 20))]
             + [("play", 1), ("until_quiet",)]),
        dict(name="gunfire levels", ops=[("init",), ("play", 44, 1, 2), ("irq", 300), ("play", 44, 4, 2), ("irq", 300), ("play", 44, 5, 3),
                                         ("irq", 300), ("play", 44, 2, 1), ("irq", 300), ("play", 44, 4, 1), ("irq", 200), ("play", 44, 0, 0),
                                         ("irq", 100), ("until_quiet",)]),
        dict(name="gunfire barrage and back", ops=[("init",), ("play", 44, 5, 1), ("irq", 400), ("play", 44, 3, 4), ("irq", 300),
                                                   ("play", 44, 3, 0), ("until_quiet",)]),
        dict(name="gunfire level 4 to 5 and back", ops=[("init",), ("play", 44, 4, 3), ("irq", 300), ("play", 44, 5, 2), ("irq", 300),
                                                        ("play", 44, 4, 2), ("irq", 300), ("play", 44, 1, 1), ("irq", 200), ("play", 0), ("irq", 3)]),
        dict(name="gunfire at full intensity", ops=[("init",), ("play", 44, 1, 20), ("irq", 400), ("play", 0), ("irq", 3)]),
        dict(name="gunfire and the theme", ops=[("init",), ("play", 2), ("irq", 50), ("play", 44, 2, 2), ("irq", 300), ("play", 1), ("until_quiet",)]),
        dict(name="title and shutdown", ops=[("init",), ("play", 2), ("irq", 200), ("stop",), ("irq", 5)]),
        dict(name="title through", ops=[("init",), ("play", 2), ("irq", 1500), ("play", 1), ("until_quiet",)]),
        dict(name="every song through", ops=[("init",)] + [op for c in (39, 40, 41, 42, 43) for op in (("play", c), ("irq", 700), ("play", 1), ("until_quiet",))]),
        dict(name="before init", ops=[("irq", 3), ("play", 3), ("init",), ("play", 9), ("until_quiet",), ("stop",), ("irq", 2)]),
        dict(name="init twice", ops=[("init",), ("irq", 3), ("init",), ("play", 3), ("until_quiet",)]),
        dict(name="unknown command", ops=[("init",), ("play", 21), ("irq", 3), ("play", 47), ("play", 60), ("irq", 5), ("until_quiet",)]),
    ]
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", type=Path, default=ROOT / "game/Covert Action/Covert")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if "build" not in args.out.resolve().parts: parser.error("output must be below a build folder")
    driver = (args.game / DRIVER).read_bytes()
    result = dict(schema=1, driver=DRIVER, driver_sha256=hashlib.sha256(driver).hexdigest(),
                  fixture="tools/tsound_vectors.py TandyProbe with a Tandy BIOS; one IRQ per requested interrupt, carry clear",
                  scenarios=[run(args.game, s) for s in scenarios()])
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    ops = [op for s in result["scenarios"] for op in s["ops"]]
    calls = sum(op["op"] in ("init", "stop", "play") for op in ops)
    irqs = sum(len(op["irqs"]) for op in ops)
    psg = sum(len(e.split()) - 1 for op in ops for e in op["events"] + [x for i in op["irqs"] for x in i] if e.startswith("psg"))
    print(json.dumps(dict(scenarios=len(result["scenarios"]), calls=calls, interrupts=irqs, chip_bytes=psg, out=str(args.out))))


if __name__ == "__main__": main()
