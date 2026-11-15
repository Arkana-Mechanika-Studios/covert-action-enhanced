"""Speaker vectors of the original IBM sound driver (ISOUND.CA) for the C# rewrite's driver tests.

Runs the supplied driver's own instructions in Unicorn (tools/trace_sound.py's fixture: synthetic caller, BIOS
stub and PIT clock; one timer interrupt per requested IRQ) through scenarios of play calls and timer interrupts,
and writes, per call and per interrupt, what the driver does to the speaker and the timer, as events:

    tone        timer channel 2 set to square-wave mode (port 43h <- b6h)
    div XXXX    channel 2 divisor (port 42h, low byte then high byte)
    on / off    speaker gate and data bits of port 61h both set / both cleared
    timer XXXX  timer channel 0 (the interrupt's rate) reprogrammed (port 43h <- 36h, port 40h low, high)

The end-of-interrupt write (port 20h) is left out. A play call that waits for its sound to end (command 28) gets
one timer interrupt before each check of its waiting loop (0000:05a5); those interrupts are listed with the call.

    python -B tools/isound_vectors.py --out build/oracle/ISOUND/speaker.json

The output must be below a build folder; it is game-derived evidence and is never committed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from trace_sound import SoundProbe, LOAD_SEGMENT, BIOS_SEGMENT, CALL_RETURN, ROOT

WAIT_LOOP = 0x05a5
DATA = (LOAD_SEGMENT + 0x6f) * 16
BUSY_WORDS = (0xf95, 0xf9d, 0xfa7, 0xfa5)
TIMER_MODE = 0xfc3
COMMANDS = 47
QUIET_TAIL = 3
CAP = 3000


class VectorProbe(SoundProbe):
    def __init__(self, game):
        super().__init__(game)
        self.in_call = False; self.skip_wait = False; self.at_wait = False

    def instruction(self, machine, address, size, context):
        if self.in_call and address == LOAD_SEGMENT * 16 + WAIT_LOOP:
            if self.skip_wait: self.skip_wait = False
            else:
                self.at_wait = True; machine.emu_stop(); return
        super().instruction(machine, address, size, context)

    def run_from_here(self):
        self.returned = False; self.at_wait = False
        self.machine.emu_start(self.get("cs") * 16 + self.get("ip"), 0x100000, count=1_000_000)
        if not (self.returned or self.at_wait): raise ValueError("driver did not return or reach its waiting loop")

    def any_call(self, slot, args):
        """A far call of a driver slot with its stack arguments; timer interrupts in the waiting loop."""
        before = self.registers()
        self.emit("call_enter", slot=slot, args=args)
        for value in reversed(args): self.push(value)
        self.push(BIOS_SEGMENT); self.push(CALL_RETURN)
        self.set(cs=LOAD_SEGMENT, ip=self.slots[slot])
        self.return_ip = CALL_RETURN; self.in_call = True
        in_irq = False
        while True:
            self.run_from_here()
            if self.returned: break
            if in_irq:
                self.emit("irq_return"); in_irq = False; self.skip_wait = True; continue
            if not self.get("eflags") & 0x200: raise ValueError("waiting loop with interrupts off")
            self.pit_cycles += self.pit0_divisor or 65536; self.irq_index += 1
            off, seg = struct.unpack("<HH", self.machine.mem_read(0x20, 4))
            self.emit("irq_enter", target_segment=seg, target_offset=off)
            flags = self.get("eflags")
            self.push(flags); self.push(self.get("cs")); self.push(self.get("ip"))
            self.set(cs=seg, ip=off, eflags=flags & ~0x300)
            in_irq = True
            if len(self.events) > 2_000_000: raise ValueError("waiting loop does not end")
        self.in_call = False
        self.set(sp=(self.get("sp") + 2 * len(args)) & 0xffff)
        if self.get("ss") != before["ss"] or self.get("sp") != before["sp"]: raise ValueError("unbalanced call")
        self.emit("call_return", slot=slot)

    def quiet(self):
        words = [struct.unpack("<H", self.machine.mem_read(DATA + o, 2))[0] for o in BUSY_WORDS]
        return not any(words) and self.machine.mem_read(DATA + TIMER_MODE, 1)[0] == 0xff


def semantic(events):
    """The port writes of a run of raw events as speaker/timer events; anything unexpected is an error."""
    out = []; pending = None
    for e in events:
        if e["kind"] != "port_write": continue
        port, value = e["port"], e["value"]
        if pending is not None:
            kind, low = pending
            if kind == "div" and port == 0x42 and low is not None:
                out.append(f"div {low | value << 8:04x}"); pending = None; continue
            if kind == "div" and port == 0x42: pending = ("div", value); continue
            if kind == "timer" and port == 0x40 and low is None: pending = ("timer", value); continue
            if kind == "timer" and port == 0x40: out.append(f"timer {low | value << 8:04x}"); pending = None; continue
            raise ValueError(f"interrupted {kind} write: {port:02x} {value:02x}")
        if port == 0x20 and value == 0x20: continue
        if port == 0x43 and value == 0xb6: out.append("tone"); continue
        if port == 0x43 and value == 0x36: pending = ("timer", None); continue
        if port == 0x42: pending = ("div", value); continue
        if port == 0x61 and value & 3 == 3: out.append("on"); continue
        if port == 0x61 and value & 3 == 0: out.append("off"); continue
        raise ValueError(f"unexpected write {port:02x} {value:02x}")
    if pending is not None: raise ValueError("unfinished two-byte write")
    return out


def split(events):
    """Events of one operation: those outside interrupts, and one list per interrupt."""
    outside = []; irqs = []; current = None
    for e in events:
        if e["kind"] == "irq_enter": current = []; continue
        if e["kind"] == "irq_return": irqs.append(semantic(current)); current = None; continue
        (outside if current is None else current).append(e)
    return semantic(outside), irqs


def run(game, scenario):
    probe = VectorProbe(game); ops = []
    for action in scenario["ops"]:
        start = len(probe.events)
        if action[0] == "init": probe.any_call(100, [])
        elif action[0] == "stop": probe.any_call(102, [])
        elif action[0] == "play": probe.any_call(101, [action[1], action[2] if len(action) > 2 else 0])
        elif action[0] == "irq":
            for _ in range(action[1]): probe.irq()
        elif action[0] == "until_quiet":
            tail = 0; count = 0
            while tail < QUIET_TAIL and count < CAP:
                probe.irq(); count += 1; tail = tail + 1 if probe.quiet() else 0
        else: raise ValueError(action)
        calls, irqs = split(probe.events[start:])
        ops.append(dict(op=action[0], args=list(action[1:]), events=calls, irqs=irqs))
        probe.events.clear()
    return dict(name=scenario["name"], ops=ops)


def scenarios():
    rows = []
    for command in range(COMMANDS):
        param = 2 if command in (26, 27) else 0
        rows.append(dict(name=f"command {command}", ops=[("init",), ("irq", 3), ("play", command, param), ("until_quiet",)]))
    rows += [
        dict(name="engine gears", ops=[("init",), ("play", 26, 4), ("irq", 4), ("play", 27, 1), ("irq", 4), ("play", 0), ("irq", 4)]),
        dict(name="priority", ops=[("init",), ("play", 7), ("irq", 20), ("play", 33), ("irq", 20), ("play", 3), ("until_quiet",)]),
        dict(name="pause and resume", ops=[("init",), ("play", 2), ("irq", 120), ("play", 37), ("irq", 30), ("play", 38), ("irq", 120),
                                           ("play", 1), ("until_quiet",)]),
        dict(name="pause while quiet", ops=[("init",), ("play", 37), ("irq", 5), ("play", 38), ("irq", 5)]),
        dict(name="teleprinter loop", ops=[("init",), ("play", 31), ("irq", 400), ("play", 3), ("until_quiet",)]),
        dict(name="teleprinter cleared by an unknown command", ops=[("init",), ("play", 31), ("irq", 50), ("play", 60), ("until_quiet",)]),
        dict(name="teleprinter cleared by a refused sound", ops=[("init",), ("play", 31), ("irq", 3), ("play", 24), ("irq", 2),
                                                                 ("play", 31), ("irq", 30), ("play", 16), ("until_quiet",)]),
        dict(name="refused sound moves the repeat", ops=[("init",), ("play", 6), ("irq", 3), ("play", 35), ("until_quiet",)]),
        dict(name="wait for the end", ops=[("init",), ("play", 28), ("irq", 3), ("play", 7), ("irq", 10), ("play", 28), ("until_quiet",)]),
        dict(name="sweep then noise", ops=[("init",), ("play", 5), ("irq", 3), ("play", 3), ("irq", 40), ("play", 13), ("until_quiet",)]),
        dict(name="noise then sweep without trill", ops=[("init",), ("play", 3), ("irq", 12), ("play", 18), ("until_quiet",)]),
        dict(name="title and shutdown", ops=[("init",), ("play", 2), ("irq", 200), ("stop",), ("irq", 5)]),
        dict(name="title through", ops=[("init",), ("play", 2), ("irq", 2000), ("play", 1), ("until_quiet",)]),
        dict(name="before init", ops=[("irq", 3), ("init",), ("play", 9), ("until_quiet",), ("stop",), ("irq", 2)]),
    ]
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", type=Path, default=ROOT / "game/Covert Action/Covert")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if "build" not in args.out.resolve().parts: parser.error("output must be below a build folder")
    driver = (args.game / "ISOUND.CA").read_bytes()
    result = dict(schema=1, driver="ISOUND.CA", driver_sha256=hashlib.sha256(driver).hexdigest(),
                  fixture="tools/trace_sound.py SoundProbe; one IRQ per requested interrupt; waiting loop: one IRQ before each check",
                  scenarios=[run(args.game, s) for s in scenarios()])
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    irqs = sum(len(op["irqs"]) for s in result["scenarios"] for op in s["ops"])
    events = sum(len(op["events"]) + sum(len(i) for i in op["irqs"]) for s in result["scenarios"] for op in s["ops"])
    print(json.dumps(dict(scenarios=len(result["scenarios"]), interrupts=irqs, events=events, out=str(args.out))))


if __name__ == "__main__": main()
