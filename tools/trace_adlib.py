"""Original ASOUND instructions under a bounded, explicitly scheduled fixture.

Hardware status is derived from a PIT-cycle clock, never forced to make the
driver detect an AdLib card. This is an isolated driver test, not an audio synth.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct

from executables import load
from trace_startup import ROOT, REGISTERS, uc
from trace_sound import (SoundProbe, LOAD_SEGMENT, BIOS_SEGMENT, BIOS_OFFSET,
                         CALL_RETURN, IRQ_RETURN, FLAGS_MASK, BIOS_BYTES,
                         INITIAL_REGISTERS, call_evidence)
from unicorn.x86_const import UC_X86_INS_IN, UC_X86_INS_OUT

PIT_HZ = 1193182
IO_ADVANCE_CYCLES = 1193
DRIVER_SPAN = 0x4050
DRIVER_SHA256 = "2f8deec7eff48e1f31dc8723e2e60772a0734ee796ba9a12dcef8b78832a01b5"
TITLE_ACTIONS = [dict(op="call", slot=100, args=[0]), dict(op="irq", count=6),
                   dict(op="call", slot=101, args=[2]), dict(op="irq", count=64),
                   dict(op="call", slot=101, args=[1]), dict(op="irq", count=16),
                   dict(op="call", slot=102, args=[]), dict(op="irq", count=1)]
EFFECT_ACTIONS = TITLE_ACTIONS[:6] + [dict(op="call", slot=101, args=[45]),
                   dict(op="irq", count=64), dict(op="call", slot=101, args=[1]),
                   dict(op="irq", count=16), dict(op="call", slot=101, args=[34]),
                   dict(op="irq", count=64), dict(op="call", slot=101, args=[1]),
                   dict(op="irq", count=16)] + TITLE_ACTIONS[6:]
DEFAULT_ACTIONS = EFFECT_ACTIONS


def effect_evidence(game):
    image = load(game/"CODE.EXE").image
    checks = [(0x16ff, "b82d0050e8b7f583c402", "CODE00b4:0bbf command45 after a letter pair"),
              (0xcbd, "558becf606781d04740bff76049acf03000083c4025dc3", "CODE00b4:017d sound-enabled slot101 wrapper")]
    rows = []
    for offset, expected, label in checks:
        actual = image[offset:offset+len(bytes.fromhex(expected))].hex()
        if actual != expected:
            raise ValueError("CODE effect caller evidence changed: "+label)
        rows.append(dict(image_offset=offset, bytes=actual, label=label))
    return dict(code_image_sha256=hashlib.sha256(image).hexdigest(), checks=rows,
                observed_original="build/sound-live-oracle/title-code/original.json records actual slot101 command45; this fixture replays its argument independently on ASOUND")


def tac_effect_evidence(game):
    image = load(game/"TAC.EXE").image
    checks = [(0x3346, "b8220050e895e183c402", "TAC00e8:24c6 command34 call"),
              (0x14e2, "558becf606689b047413ff36b6cbff36b2cbff76049a5b04000083c4065dc3", "TAC00e8:0662 sound-enabled slot101 wrapper")]
    rows = []
    for offset, expected, label in checks:
        actual = image[offset:offset+len(bytes.fromhex(expected))].hex()
        if actual != expected:
            raise ValueError("TAC effect caller evidence changed: "+label+" "+actual)
        rows.append(dict(image_offset=offset, bytes=actual, label=label))
    return dict(tac_image_sha256=hashlib.sha256(image).hexdigest(), checks=rows,
                limit="Static original caller bytes; not a full TAC playback trace. The TAC wrapper's two auxiliary words are unused by ASOUND18c4.")


class AdlibHardware:
    """Bounded host input model, separate from original driver instructions."""
    def __init__(self, present=True):
        self.present = present
        self.cycles = 0
        self.divisor = 0
        self.reload_cycle = 0
        self.low_byte = None
        self.latched = None
        self.latch_high = False
        self.opl_index = 0
        self.opl_registers = bytearray(256)
        self.control = self.status = 0
        self.timer_start = [0, 0]

    @property
    def period(self):
        return self.divisor or 65536

    def counter(self):
        if self.period & 1:
            raise ValueError("odd mode3 reload is outside the evidenced ASOUND fixture")
        return (self.period-2*((self.cycles-self.reload_cycle) % (self.period//2))) & 0xffff

    def output(self, port, value):
        if port == 0x43:
            if value == 0:
                if self.latched is None:
                    self.latched = self.counter()
                    self.latch_high = False
            elif value == 0x36:
                self.low_byte = None
                self.latched = None
                self.latch_high = False
            else:
                raise ValueError(f"unsupported PIT control {value:02x}")
        elif port == 0x40:
            if self.low_byte is None:
                self.low_byte = value
            else:
                divisor = self.low_byte | value << 8
                if divisor & 1:
                    raise ValueError("odd PIT reload is outside the evidenced fixture")
                self.divisor = divisor
                self.reload_cycle = self.cycles
                self.low_byte = None
        elif port == 0x20:
            if value != 0x20:
                raise ValueError("unsupported PIC command")
        elif port in (0x388, 0x389):
            if not self.present:
                return
            if port == 0x388:
                self.opl_index = value
            else:
                reg = self.opl_index
                self.opl_registers[reg] = value
                if reg == 4:
                    if value & 0x80:
                        self.status = 0
                    else:
                        for index in range(2):
                            mask = 1 << index
                            if value & mask and not self.control & mask:
                                self.timer_start[index] = self.cycles*1_000_000//PIT_HZ
                        self.control = value
        else:
            raise ValueError(f"unsupported AdLib output port {port:04x}")

    def input(self, port, *, advance=True):
        if advance:
            self.cycles += IO_ADVANCE_CYCLES
        if port == 0x40:
            if self.latched is None:
                raise ValueError("unlatched PIT read is outside the evidenced fixture")
            value = self.latched >> 8 if self.latch_high else self.latched & 0xff
            if self.latch_high:
                self.latched = None
            self.latch_high = not self.latch_high
            return value
        if port in (0x388, 0x389):
            if not self.present:
                return 0xff
            for index, (mask, overflow, micros) in enumerate(((0x40, 0xc0, 80), (0x20, 0xa0, 320))):
                duration = micros * (256-self.opl_registers[index+2])
                if (self.control & (1 << index) and not self.control & mask and
                    self.cycles*1_000_000//PIT_HZ-self.timer_start[index] >= duration):
                    self.status |= overflow
            return self.status | 6
        raise ValueError(f"unsupported AdLib input port {port:04x}")

    def state(self):
        return dict(cycles=self.cycles, divisor=self.divisor, reload_cycle=self.reload_cycle,
                    low_byte=self.low_byte, latched=self.latched, latch_high=self.latch_high,
                    opl_present=self.present, opl_index=self.opl_index, opl_control=self.control,
                    opl_status=self.status, opl_timer_start_us=self.timer_start.copy(),
                    opl_registers_hex=self.opl_registers.hex())


class AdlibProbe(SoundProbe):
    """Reuse only neutral register/IRQ helpers; load and execute ASOUND itself."""
    def __init__(self, game, present=True):
        self.game = game
        source = (game/"ASOUND.CA").read_bytes()
        if hashlib.sha256(source).hexdigest() != DRIVER_SHA256:
            raise ValueError("ASOUND source hash changed")
        self.executable = load(game/"ASOUND.CA")
        image = bytearray(self.executable.image)
        for segment, offset in self.executable.relocations:
            address = segment*16+offset
            struct.pack_into("<H", image, address, (struct.unpack_from("<H", image, address)[0]+LOAD_SEGMENT)&0xffff)
        first, count = struct.unpack_from("<HH", image, 0x2e)
        self.slots = dict(zip(range(first, first+count), struct.unpack_from("<"+"H"*count, image, 0x32)))
        if [self.slots[i] for i in (100,101,102)] != [0x18a7,0x18c4,0x18df]:
            raise ValueError("ASOUND public entries changed")
        self.machine = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.machine.mem_map(0, 0x100000)
        self.machine.mem_write(LOAD_SEGMENT*16, bytes(image))
        self.machine.mem_write(0x20, struct.pack("<HH", BIOS_OFFSET, BIOS_SEGMENT))
        self.machine.mem_write(BIOS_SEGMENT*16+BIOS_OFFSET, BIOS_BYTES)
        self.set(**INITIAL_REGISTERS)
        self.hardware = AdlibHardware(present)
        self.events = []; self.operations = []; self.op_index = -1; self.irq_index = 0
        self.instructions = 0; self.return_ip = None; self.returned = False; self.last_control = None
        self.port61 = 0x20
        self.previous_ss = self.get("ss")
        self.previous_location = None
        self.machine.hook_add(uc.UC_HOOK_CODE, self.instruction)
        self.machine.hook_add(uc.UC_HOOK_MEM_WRITE, self.memory_write)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_read, None, 1, 0, UC_X86_INS_IN)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_write, None, 1, 0, UC_X86_INS_OUT)
        self.machine.hook_add(uc.UC_HOOK_INTR, self.interrupt)

    @property
    def pit_cycles(self):
        return self.hardware.cycles

    @pit_cycles.setter
    def pit_cycles(self, value):
        self.hardware.cycles = value

    @property
    def pit0_divisor(self):
        return self.hardware.divisor

    def instruction(self, machine, address, size, context):
        if self.get("ss") != self.previous_ss:
            self.emit("stack_segment_switch", previous_ss=self.previous_ss,
                      previous_instruction=self.previous_location,
                      ss=self.get("ss"), sp=self.get("sp"))
            self.previous_ss = self.get("ss")
        # Record the SP after the two byte-evidenced private-stack setup/restore
        # instructions, in addition to the exact all-memory boundary snapshots.
        if self.get("cs") == LOAD_SEGMENT and self.get("ip") in (0x0ea5, 0x0ecd):
            self.emit("private_stack_ready", ss=self.get("ss"), sp=self.get("sp"))
        self.previous_location = dict(cs=self.get("cs"), ip=self.get("ip"))
        super().instruction(machine, address, size, context)

    def port_read(self, machine, port, size, context):
        if size != 1:
            raise ValueError("ASOUND fixture supports byte port reads only")
        value = self.hardware.input(port)
        self.emit("port_read", port=port, size=size, value=value)
        return value

    def port_write(self, machine, port, size, value, context):
        if size != 1:
            raise ValueError("ASOUND fixture supports byte port writes only")
        self.emit("port_write", port=port, size=size, value=value)
        self.hardware.output(port, value)

    def call(self, slot, args):
        if (slot, args) not in ((100,[0]), (101,[2]), (101,[1]), (101,[45]), (101,[34]), (102,[])):
            raise ValueError("call is not an evidenced ASOUND init/title/menu/shutdown operation")
        before = self.registers()
        self.emit("call_enter", slot=slot, args=args, registers=before)
        for value in reversed(args):
            self.push(value)
        self.push(BIOS_SEGMENT); self.push(CALL_RETURN)
        self.set(cs=LOAD_SEGMENT, ip=self.slots[slot])
        self.execute(CALL_RETURN)
        self.set(sp=(self.get("sp")+2*len(args))&0xffff)
        if self.get("ss") != before["ss"] or self.get("sp") != before["sp"]:
            raise ValueError("ASOUND call did not restore its caller stack")
        self.emit("call_return", slot=slot, registers=self.registers())

    def snapshot(self, out, action):
        # The inherited snapshot always includes the entire MiB. Replace its
        # small IBM convenience slice with ASOUND's full allocation including BSS.
        super().snapshot(out, action)
        row = self.operations[-1]
        driver = bytes(self.machine.mem_read(LOAD_SEGMENT*16, DRIVER_SPAN))
        (out/row["driver_file"]).write_bytes(driver)
        row["driver_sha256"] = hashlib.sha256(driver).hexdigest()
        row["hardware"] = self.hardware.state()

    def run(self, out, actions):
        out.mkdir(parents=True, exist_ok=True)
        (out/"initial.memory.bin").write_bytes(bytes(self.machine.mem_read(0,0x100000)))
        (out/"actions.json").write_text(json.dumps(actions,indent=2)+"\n")
        for index, action in enumerate(actions):
            self.op_index = index
            if action.get("op") == "call":
                self.call(action["slot"], action["args"])
            elif action.get("op") == "irq" and type(action.get("count")) is int and 0 < action["count"] <= 10000:
                for _ in range(action["count"]):
                    self.irq()
            else:
                raise ValueError("unsupported AdLib fixture action")
            self.snapshot(out, action)
        evidence = call_evidence(self.game)
        evidence.pop("intro", None)
        evidence["code_effect"] = effect_evidence(self.game)
        evidence["tac_effect"] = tac_effect_evidence(self.game)
        fixture = dict(driver="ASOUND.CA", load_segment=LOAD_SEGMENT, driver_span=DRIVER_SPAN,
                       driver_sha256=DRIVER_SHA256,
                       load_image_sha256=hashlib.sha256(self.executable.image).hexdigest(),
                       initial_registers=INITIAL_REGISTERS, flags_mask=FLAGS_MASK,
                       initial_port61=0x20, opl_present=self.hardware.present,
                       bios_segment=BIOS_SEGMENT, bios_offset=BIOS_OFFSET, bios_bytes=BIOS_BYTES.hex(),
                       call_return=CALL_RETURN, irq_return=IRQ_RETURN, clock_hz=PIT_HZ,
                       io_advance_cycles=IO_ADVANCE_CYCLES,
                       pit_count_model="mode3-even: (period-2*((cycles-reload_cycle)%(period/2)))&0xffff",
                       pit_latch="OUT43=00 freezes low/high count; repeated latch ignored until both bytes consumed",
                       opl_timer_model="Pinned Darklands+ policy: floor each absolute PIT-cycle timestamp to microseconds, subtract timer start, compare with 80us*(256-T1) and 320us*(256-T2); present status|06, absent ff",
                       schedule="1193 PIT cycles before every IN; no OUT advance; each requested IRQ advances one current PIT period; no automatic or nested IRQ delivery during calls")
        report = dict(schema=1, fixture=fixture, actions=actions, evidence=evidence,
                      operations=self.operations, events=self.events,
                      limits=["Synthetic caller, BIOS, hardware and explicit IRQ schedule; not full-title execution or measured hardware timing.",
                              "Complete MiB, including ASOUND BSS and both stacks, is retained without memory normalization.",
                              "CLI/STI/IRET, IVT writes and private stack switches are original instruction observations; native coverage requires its separate comparator.",
                              "OPL timers/status only: no FM synthesis or audible output is produced."])
        if not self.hardware.present:
            report["limits"].append("Calls after AX0041 detection failure test isolated driver robustness; they do not claim that the game continues after failed card detection.")
        (out/"report.json").write_text(json.dumps(report, indent=2)+"\n")
        return report


def observe_init_call(game, out):
    """Stop before the first ASOUND instruction in ordinary 242 setup."""
    from trace_final import FinalProbe

    class CallerProbe(FinalProbe):
        def __init__(self):
            self.sound_segment = None
            self.sound_calls = []
            super().__init__(game, "242 ", 40_000_000,
                             discovery=False, artifacts="validation")

        def emit(self, kind, **fields):
            super().emit(kind, **fields)
            if kind == "dos_overlay" and fields["filename"].upper() == "ASOUND.CA":
                self.sound_segment = int(fields["load_segment"], 16)

        def instruction(self, machine, address, size, context):
            if self.get("cs") == self.sound_segment and self.get("ip") == 0x18a7:
                stack = bytes(machine.mem_read(self.get("ss")*16+self.get("sp"), 16))
                self.sound_calls.append(dict(module=self.current["name"], entry=0x18a7,
                    registers={n:self.get(n) for n in REGISTERS}, stack_hex=stack.hex(),
                    return_offset=int.from_bytes(stack[:2], "little"),
                    return_segment=int.from_bytes(stack[2:4], "little"),
                    args=[int.from_bytes(stack[4:6], "little")]))
                self.stop("observed actual ASOUND initialization before execution")
                return
            super().instruction(machine, address, size, context)

    probe = CallerProbe()
    report = probe.run()
    result = dict(schema=1, setup_keys="242 ", stop_reason=report["stop_reason"],
                  instructions=report["instructions"], sound_calls=probe.sound_calls,
                  title_menu_evidence=call_evidence(game),
                  limit="Original startup with synthetic DOS/BIOS; stopped before ASOUND initialization and hardware access. Title/menu arguments are separately byte-validated call sites, not executed by this prefix.")
    result["title_menu_evidence"].pop("intro", None)
    out.mkdir(parents=True, exist_ok=True)
    (out/"init-call.json").write_text(json.dumps(result, indent=2)+"\n")
    if len(probe.sound_calls) != 1:
        raise ValueError("ordinary 242 setup did not reach the original ASOUND init entry: "+str(result))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, default=ROOT/"game/Covert Action/Covert")
    parser.add_argument("--out", type=Path, default=ROOT/"build/sound-oracle/adlib")
    parser.add_argument("--observe-init", action="store_true")
    parser.add_argument("--absent", action="store_true")
    parser.add_argument("--actions", type=Path)
    parser.add_argument("--effect", action="store_true", help="Compatibility alias for the default title/CODE45/TAC34 fixture")
    parser.add_argument("--title-only", action="store_true", help="Use the shorter slow-handler title/stop fixture")
    args = parser.parse_args()
    if ROOT/"build" not in args.out.resolve().parents:
        parser.error("output must be below COVERT/build")
    temporary = ROOT/"build/tmp"
    temporary.mkdir(exist_ok=True)
    os.environ["TMP"] = os.environ["TEMP"] = str(temporary)
    if args.observe_init:
        print(json.dumps(observe_init_call(args.game, args.out)))
        return
    actions = json.loads(args.actions.read_text()) if args.actions else TITLE_ACTIONS if args.title_only else DEFAULT_ACTIONS
    report = AdlibProbe(args.game, not args.absent).run(args.out, actions)
    print(json.dumps(dict(out=str(args.out), operations=len(report["operations"]),
                         instructions=report["operations"][-1]["instructions"],
                         events=len(report["events"]), opl_present=not args.absent)))


if __name__ == "__main__":
    main()
