"""OPL vectors of the original AdLib sound driver (ASOUND.CA) for the C# rewrite's driver tests.

Runs the supplied driver's own instructions in Unicorn (tools/trace_adlib.py's fixture: synthetic caller, BIOS
stub, PIT clock and fake OPL whose timer status follows the modelled clock; one timer interrupt per requested IRQ)
through scenarios of play calls and timer interrupts, and writes, per call and per interrupt, what the driver does
to the card and the timer, as events:

    rr=vv       OPL register rr written with vv (port 388h <- rr, then port 389h <- vv)
    status      the OPL status read (port 388h); the card's detection reads it 201 times
    timer XXXX  timer channel 0 (the interrupt's rate) reprogrammed (port 43h <- 36h, port 40h low, high)
    detected / not detected   the start's result (AX 0 or not), after the start's other events

Left out: the end-of-interrupt write (port 20h) and the start's calibration of its write delays (PIT latches,
port 43h <- 00h, and the reads of port 40h), which reaches no register. Play calls push the command and two
further words (the car chase's gear, the raid's gunfire level and intensity); init pushes the 0 the game passes.

    python -B tools/asound_vectors.py --out build/oracle/ASOUND/opl.json

The output must be below a build folder; it is game-derived evidence and is never committed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from trace_adlib import AdlibProbe
from trace_sound import LOAD_SEGMENT, BIOS_SEGMENT, CALL_RETURN, ROOT

DATA = (LOAD_SEGMENT + 0x1ac) * 16
VOICES = [0x2322 + 26 * i for i in range(7)]
NOISE_TICKS = (0x23f2, 0x23f3)
TIMER_MODE = LOAD_SEGMENT * 16 + 0xbb8
COMMANDS = 47
QUIET_TAIL = 3
CAP = 1500


class VectorProbe(AdlibProbe):
    def instruction(self, machine, address, size, context):
        # only the return sentinels: the full instruction observations of trace_adlib are not needed here
        if self.get("cs") == BIOS_SEGMENT and self.get("ip") == self.return_ip:
            self.returned = True; machine.emu_stop()

    def memory_write(self, machine, access, address, size, value, context):
        pass

    def any_call(self, slot, args):
        before = self.registers()
        self.emit("call_enter", slot=slot, args=args)
        for value in reversed(args): self.push(value)
        self.push(BIOS_SEGMENT); self.push(CALL_RETURN)
        self.set(cs=LOAD_SEGMENT, ip=self.slots[slot])
        self.execute(CALL_RETURN)
        self.set(sp=(self.get("sp") + 2 * len(args)) & 0xffff)
        if self.get("ss") != before["ss"] or self.get("sp") != before["sp"]: raise ValueError("unbalanced call")
        self.emit("call_return", slot=slot, ax=self.get("ax"))

    def quiet(self):
        voices = [self.machine.mem_read(DATA + v, 1)[0] for v in VOICES]
        noise = [self.machine.mem_read(DATA + n, 1)[0] for n in NOISE_TICKS]
        return not any(voices) and not any(noise) and self.machine.mem_read(TIMER_MODE, 1)[0] == 1


def semantic(events):
    """The port events of a run of raw events as OPL/timer events; anything unexpected is an error."""
    out = []; index = None; timer = None
    for e in events:
        if e["kind"] == "call_return" and e["slot"] == 100:
            out.append("detected" if e["ax"] == 0 else "not detected"); continue
        if e["kind"] == "port_read":
            if e["port"] == 0x388: out.append("status")
            elif e["port"] != 0x40: raise ValueError(f"unexpected read {e['port']:x}")
            continue
        if e["kind"] != "port_write": continue
        port, value = e["port"], e["value"]
        if timer is not None:
            if port != 0x40: raise ValueError(f"interrupted timer write: {port:02x} {value:02x}")
            if timer == -1: timer = value; continue
            out.append(f"timer {timer | value << 8:04x}"); timer = None; continue
        if port == 0x388: index = value; continue
        if port == 0x389:
            if index is None: raise ValueError("data write without index")
            out.append(f"{index:02x}={value:02x}"); index = None; continue
        if port == 0x20 and value == 0x20: continue
        if port == 0x43 and value == 0x00: continue
        if port == 0x43 and value == 0x36: timer = -1; continue
        raise ValueError(f"unexpected write {port:02x} {value:02x}")
    if timer is not None or index is not None: raise ValueError("unfinished write")
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
        if action[0] == "init": probe.any_call(100, [0])
        elif action[0] == "stop": probe.any_call(102, [])
        elif action[0] == "play":
            args = list(action[1:]) + [0] * (3 - len(action[1:]))
            probe.any_call(101, args)
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
        args = (2,) if command in (26, 27) else (2, 3) if command == 44 else ()
        rows.append(dict(name=f"command {command}", ops=[("init",), ("irq", 3), ("play", command) + args, ("until_quiet",)]))
    rows += [
        dict(name="engine gears of both cars", ops=[("init",), ("play", 26, 4), ("irq", 40), ("play", 27, 1), ("irq", 40), ("play", 26, 0),
                                                    ("irq", 60), ("play", 27, 0), ("irq", 120), ("play", 26, 3), ("irq", 30), ("play", 0), ("irq", 4)]),
        dict(name="one car through its gears", ops=[("init",), ("play", 26, 1), ("irq", 30), ("play", 26, 3), ("irq", 100), ("play", 26, 6),
                                                    ("irq", 100), ("play", 26, 0), ("irq", 200)]),
        dict(name="gear 0 of a silent engine", ops=[("init",), ("play", 27, 0), ("irq", 5), ("play", 27, 2), ("irq", 60)]),
        dict(name="take-off and landing", ops=[("init",), ("play", 35), ("irq", 200), ("play", 36), ("irq", 200)]),
        dict(name="a car during the aeroplane", ops=[("init",), ("play", 35), ("irq", 20), ("play", 26, 2), ("irq", 40), ("play", 36), ("irq", 80)]),
        dict(name="reset during a glide", ops=[("init",), ("play", 35), ("irq", 20), ("play", 0), ("irq", 50)]),
        dict(name="pause and resume", ops=[("init",), ("play", 2), ("irq", 120), ("play", 37), ("irq", 30), ("play", 38), ("irq", 120),
                                           ("play", 1), ("until_quiet",)]),
        dict(name="pause while quiet", ops=[("init",), ("play", 37), ("irq", 5), ("play", 38), ("irq", 5)]),
        dict(name="pause lifted by an engine", ops=[("init",), ("play", 39), ("irq", 50), ("play", 37), ("play", 26, 2), ("irq", 60), ("play", 0), ("irq", 3)]),
        dict(name="effects over each other", ops=[("init",), ("play", 3), ("irq", 2), ("play", 4), ("irq", 2), ("play", 7), ("irq", 2),
                                                  ("play", 6), ("irq", 2), ("play", 34), ("until_quiet",)]),
        dict(name="an effect over the theme", ops=[("init",), ("play", 2), ("irq", 100), ("play", 45), ("irq", 60), ("play", 32), ("irq", 200),
                                                   ("play", 1), ("until_quiet",)]),
        dict(name="gunfire levels", ops=[("init",), ("play", 44, 1, 2), ("irq", 300), ("play", 44, 4, 2), ("irq", 300), ("play", 44, 5, 3),
                                         ("irq", 300), ("play", 44, 2, 1), ("irq", 300), ("play", 44, 4, 1), ("irq", 200), ("play", 44, 0, 0),
                                         ("irq", 100), ("until_quiet",)]),
        dict(name="gunfire barrage and back", ops=[("init",), ("play", 44, 5, 1), ("irq", 400), ("play", 44, 3, 4), ("irq", 300),
                                                   ("play", 44, 3, 0), ("until_quiet",)]),
        dict(name="gunfire at full intensity", ops=[("init",), ("play", 44, 1, 20), ("irq", 400), ("play", 0), ("irq", 3)]),
        dict(name="gunfire and the theme", ops=[("init",), ("play", 2), ("irq", 50), ("play", 44, 2, 2), ("irq", 300), ("play", 1), ("until_quiet",)]),
        dict(name="title and shutdown", ops=[("init",), ("play", 2), ("irq", 200), ("stop",), ("irq", 5)]),
        dict(name="title through", ops=[("init",), ("play", 2), ("irq", 3000), ("play", 1), ("until_quiet",)]),
        dict(name="every song through", ops=[("init",)] + [op for c in (39, 40, 41, 42, 43) for op in (("play", c), ("irq", 1200), ("play", 1), ("until_quiet",))]),
        dict(name="before init", ops=[("irq", 3), ("init",), ("play", 9), ("until_quiet",), ("stop",), ("irq", 2)]),
        dict(name="init twice", ops=[("init",), ("irq", 3), ("init",), ("play", 3), ("until_quiet",)]),
        dict(name="unknown command", ops=[("init",), ("play", 13), ("irq", 3), ("play", 60), ("until_quiet",)]),
    ]
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", type=Path, default=ROOT / "game/Covert Action/Covert")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if "build" not in args.out.resolve().parts: parser.error("output must be below a build folder")
    driver = (args.game / "ASOUND.CA").read_bytes()
    result = dict(schema=1, driver="ASOUND.CA", driver_sha256=hashlib.sha256(driver).hexdigest(),
                  fixture="tools/trace_adlib.py AdlibProbe with a present card; one IRQ per requested interrupt",
                  scenarios=[run(args.game, s) for s in scenarios()])
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    ops = [op for s in result["scenarios"] for op in s["ops"]]
    calls = sum(op["op"] in ("init", "stop", "play") for op in ops)
    irqs = sum(len(op["irqs"]) for op in ops)
    writes = sum(sum("=" in e for e in op["events"]) + sum("=" in e for i in op["irqs"] for e in i) for op in ops)
    print(json.dumps(dict(scenarios=len(result["scenarios"]), calls=calls, interrupts=irqs, opl_writes=writes, out=str(args.out))))


if __name__ == "__main__": main()
