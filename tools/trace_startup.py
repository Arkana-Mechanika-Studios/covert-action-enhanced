"""Bounded original-byte COVERT -> INTRO -> FINAL startup probe.

DOS/BIOS services below are explicitly simulated; game and driver instructions
come from the supplied files. No installed game file is modified. Stops before
executing FINAL, or at the first unimplemented service/instruction budget.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "build/python-deps"))
import unicorn as uc
from unicorn.x86_const import *
from executables import load

REGISTERS = {name: globals()["UC_X86_REG_" + name.upper()]
             for name in ("ax", "bx", "cx", "dx", "si", "di", "bp", "sp", "cs", "ip", "ds", "es", "ss", "eflags")}


class Probe:
    def __init__(self, game, keys, budget, *, environment_segment=0x0800):
        self.game, self.keys, self.budget = game, list(keys), budget
        self.environment_segment=environment_segment
        self.machine = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.machine.mem_map(0, 0x200000)
        self.events, self.stack, self.processes, self.files = [], [], [], {}
        self.calls = Counter()
        self.instructions, self.next_handle, self.exit_code = 0, 5, 0
        self.blocks = {} # segment -> (paragraphs, owner PSP)
        self.stop_reason, self.pending = None, None
        self.retrace_reads, self.sequencer_index = 0, 0
        self.sequencer = {}
        self.machine.mem_write(self.environment_segment*16, b"PATH=C:\\COVERT\0COMSPEC=C:\\COMMAND.COM\0\0\1\0C:\\COVERT\\COVERT.EXE\0")
        self.machine.mem_write(0x410, struct.pack("<H", 0x0021))
        self.machine.mem_write(0x413, struct.pack("<H", 640))
        self.machine.mem_write(0x449, b"\3\x50\0")
        self.machine.mem_write(0x44c, struct.pack("<H", 4000))
        self.machine.mem_write(0x463, struct.pack("<H", 0x3d4))
        self.machine.hook_add(uc.UC_HOOK_INTR, self.interrupt)
        self.machine.hook_add(uc.UC_HOOK_CODE, self.instruction)
        self.machine.hook_add(uc.UC_HOOK_MEM_WRITE, self.memory_write)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_read, None, 1, 0, UC_X86_INS_IN)
        self.machine.hook_add(uc.UC_HOOK_INSN, self.port_write, None, 1, 0, UC_X86_INS_OUT)

    def get(self, name): return self.machine.reg_read(REGISTERS[name])
    def set(self, **values):
        for name, value in values.items(): self.machine.reg_write(REGISTERS[name], value)
    def word(self, address): return struct.unpack("<H", self.machine.mem_read(address, 2))[0]
    def write_word(self, address, value): self.machine.mem_write(address, struct.pack("<H", value & 0xffff))
    def address(self, segment, offset): return segment * 16 + offset
    def string(self, address, terminator=0, limit=260):
        data = bytes(self.machine.mem_read(address, limit))
        return data.split(bytes([terminator]), 1)[0].decode("cp437")
    def location(self):
        return dict(module=self.current["name"], cs=f"{self.get('cs') - self.current['load']:04x}",
                    ip_after_int=f"{self.get('ip'):04x}", load=f"{self.current['load']:04x}")
    def emit(self, kind, **fields):
        self.events.append(dict(kind=kind, **self.location(), **fields))
    def flags(self, mask, enabled):
        self.set(eflags=(self.get("eflags") | mask) if enabled else (self.get("eflags") & ~mask))
    def success(self, **values): self.flags(1, False); self.set(**values)
    def error(self, number): self.flags(1, True); self.set(ax=number)
    def stop(self, reason): self.stop_reason = reason; self.machine.emu_stop()
    def shared(self):
        off, seg = struct.unpack("<HH", self.machine.mem_read(0x4f0, 4))
        return dict(pointer=f"{seg:04x}:{off:04x}", address=seg * 16 + off,
                    first_112_bytes=bytes(self.machine.mem_read(seg * 16 + off, 112)).hex())

    def free_regions(self):
        cursor = 0xff0
        for segment, (size, _) in sorted(self.blocks.items()):
            if segment > cursor: yield cursor, segment - cursor
            cursor = max(cursor, segment + size + 1)
        if cursor < 0x9fff: yield cursor, 0x9fff - cursor

    def allocate(self, size, owner):
        for segment, available in self.free_regions():
            if available >= size:
                self.blocks[segment] = (size, owner)
                return segment
        return None

    def start_process(self, name, tail=b"", parent=0):
        path = self.game / name.upper()
        exe = load(path)
        # DOS EXEC grants the largest available block; CRT subsequently shrinks it.
        segment, size = max(self.free_regions(), key=lambda region: region[1])
        needed = max((len(exe.image) + 15) // 16 + 16,
                     exe.ss + (exe.sp + 15) // 16 + 16)
        if needed > size: raise ValueError("insufficient simulated DOS memory")
        self.blocks[segment] = (size, segment)
        load_segment = segment + 16
        image = bytearray(exe.image)
        for s, o in exe.relocations:
            location = s * 16 + o
            value = struct.unpack_from("<H", image, location)[0]
            struct.pack_into("<H", image, location, (value + load_segment) & 0xffff)
        self.machine.mem_write(segment * 16, bytes(size * 16))
        self.machine.mem_write(load_segment * 16, bytes(image))
        self.machine.mem_write(segment * 16, b"\xcd\x20")
        self.write_word(segment * 16 + 2, segment + size)
        self.write_word(segment * 16 + 0x16, parent)
        self.write_word(segment * 16 + 0x2c, self.environment_segment)
        self.machine.mem_write(segment * 16 + 0x80, bytes([len(tail)]) + tail + b"\r")
        self.current = dict(name=name.upper(), psp=segment, load=load_segment,
                            original_sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        self.processes.append(dict(self.current))
        self.set(ax=0, bx=0, cx=0, dx=0, si=0, di=0, bp=0,
                 cs=load_segment + exe.cs, ip=exe.ip, ss=load_segment + exe.ss,
                 sp=exe.sp, ds=segment, es=segment, eflags=0x202)
        self.emit("process_start", psp=f"{segment:04x}", command_tail=tail.hex(), shared=self.shared())

    def instruction(self, machine, address, size, context):
        self.instructions += 1
        if self.instructions >= self.budget: self.stop("instruction budget exhausted")

    def memory_write(self, machine, access, address, size, value, context):
        if address in (0x4f0, 0x4f2):
            self.emit("shared_pointer_write", address=f"{address:05x}", size=size, value=value)
        off, seg = struct.unpack("<HH", machine.mem_read(0x4f0, 4))
        shared = seg * 16 + off
        if seg and shared <= address < shared + 0x70:
            self.emit("shared_write", offset=f"{address - shared:04x}", size=size, value=value)

    def port_read(self, machine, port, size, context):
        if port == 0x3da and size == 1:
            self.retrace_reads += 1
            value = 8 if self.retrace_reads % 2 else 0
        elif port == 0x3c5 and size == 1:
            value = self.sequencer.get(self.sequencer_index, 0)
        else:
            self.stop(f"unsupported input port {port:04x}/{size}"); return 0
        self.emit("port_read", port=port, size=size, value=value)
        return value

    def port_write(self, machine, port, size, value, context):
        if port == 0x3c4 and size == 1: self.sequencer_index = value
        elif port == 0x3c5 and size == 1: self.sequencer[self.sequencer_index] = value
        elif port != 0x3d8 or size != 1:
            self.stop(f"unsupported output port {port:04x}/{size}={value:04x}"); return
        self.emit("port_write", port=port, size=size, value=value)

    def interrupt(self, machine, number, context):
        ax, bx, cx, dx = (self.get(r) for r in ("ax", "bx", "cx", "dx"))
        ah, al = ax >> 8, ax & 255
        self.calls[f"{self.current['name']} int {number:02x} AH={ah:02x}"] += 1
        self.emit("interrupt", number=number, ax=ax, bx=bx, cx=cx, dx=dx,
                  ds=self.get("ds"), es=self.get("es"))
        if number == 0x10:
            if ah == 0:
                self.emit("bios_video_mode", mode=al)
                machine.mem_write(0x449, bytes([al]))
            elif ah == 2:
                self.write_word(0x450 + (bx >> 8) * 2, dx)
            elif ah == 3:
                self.set(dx=self.word(0x450 + (bx >> 8) * 2), cx=0x0607)
            elif ah == 0xf:
                self.set(ax=(80 << 8) | machine.mem_read(0x449, 1)[0], bx=bx & 255)
            elif ah in (6, 7, 9, 0xa, 0xe):
                if ah == 0xe: self.emit("bios_character", char=chr(al))
            elif ax == 0x1012:
                palette = bytes(machine.mem_read(self.get("es") * 16 + dx, cx * 3))
                self.emit("bios_dac", start=bx, count=cx, palette_hex=palette.hex())
            else: self.stop(f"unsupported BIOS int 10 AX={ax:04x}")
            return
        if number == 0x16:
            if ah in (0, 0x10):
                if not self.keys: self.stop("keyboard input exhausted"); return
                key = self.keys.pop(0)
                self.set(ax={"1":0x0231, "2":0x0332, "4":0x0534}.get(key, ord(key)))
                self.emit("bios_key", key=key)
            elif ah in (1, 0x11):
                self.flags(0x40, not self.keys)
                if self.keys: self.set(ax=ord(self.keys[0]))
            else: self.stop(f"unsupported BIOS int 16 AX={ax:04x}")
            return
        if number == 0x1a and ah == 0:
            self.set(ax=0, cx=0, dx=0x1234); return
        if number == 0x20:
            self.pending = ("exit", 0); machine.emu_stop(); return
        if number != 0x21:
            self.stop(f"unsupported interrupt {number:02x}"); return
        ds_dx = self.address(self.get("ds"), dx)
        if ah == 0x30: self.success(ax=5, bx=0, cx=0)
        elif ah == 0x0b: self.success(ax=ax & 0xff00) # No pending DOS console input.
        elif ah == 1:
            if not self.keys: self.stop("DOS keyboard input exhausted"); return
            key = self.keys.pop(0)
            self.success(ax=(ax & 0xff00) | ord(key)); self.emit("dos_key", key=key)
        elif ah == 0x29:
            source = self.string(self.get("ds") * 16 + self.get("si"))
            if source.strip(" \t\r"):
                self.stop("FCB parser only supports the observed empty command tail"); return
            destination = self.get("es") * 16 + self.get("di")
            machine.mem_write(destination, b"\0" + b" " * 11)
            self.success(ax=(ax & 0xff00))
            self.emit("dos_empty_fcb", source_text=source)
        elif ah in (0x51, 0x62): self.success(bx=self.current["psp"])
        elif ah == 0x35:
            off, seg = struct.unpack("<HH", machine.mem_read(al * 4, 4)); self.success(bx=off, es=seg)
        elif ah == 0x25:
            machine.mem_write(al * 4, struct.pack("<HH", dx, self.get("ds"))); self.success()
            self.emit("dos_set_vector", vector=al, handler=f"{self.get('ds'):04x}:{dx:04x}")
        elif ah == 0x4a:
            seg = self.get("es")
            if seg not in self.blocks: self.error(9); return
            old, owner = self.blocks[seg]
            next_block = min((s - 1 for s in self.blocks if s > seg), default=0x9fff)
            if seg + bx > next_block: self.error(8); self.set(bx=next_block - seg); return
            self.blocks[seg] = (bx, owner); self.success()
            self.emit("dos_resize", segment=f"{seg:04x}", old_paragraphs=old, paragraphs=bx)
        elif ah == 0x48:
            segment = self.allocate(bx, self.current["psp"])
            if segment is None:
                self.error(8); self.set(bx=max((n for _, n in self.free_regions()), default=0))
            else:
                self.success(ax=segment); self.emit("dos_allocate", segment=f"{segment:04x}", paragraphs=bx)
        elif ah == 0x49:
            seg = self.get("es")
            if seg in self.blocks: del self.blocks[seg]; self.success()
            else: self.error(9)
            self.emit("dos_free", segment=f"{seg:04x}")
        elif ah == 0x44 and al == 0: self.success(dx=0x80d3 if bx < 5 else 0)
        elif ah == 0x3d:
            name = self.string(ds_dx).replace("\\", "/").rsplit("/", 1)[-1].upper()
            path = self.game / name
            if not path.is_file(): self.error(2); self.emit("dos_open_missing", name=name); return
            handle = self.next_handle; self.next_handle += 1
            self.files[handle] = dict(name=name, data=path.read_bytes(), position=0, owner=self.current["psp"])
            self.success(ax=handle); self.emit("dos_open", name=name, access=al, handle=handle)
        elif ah == 0x3e:
            self.files.pop(bx, None); self.success()
        elif ah == 0x3f:
            if bx not in self.files: self.error(6); return
            file = self.files[bx]; data = file["data"][file["position"]:file["position"] + cx]
            machine.mem_write(ds_dx, data); file["position"] += len(data)
            self.success(ax=len(data)); self.emit("dos_read", name=file["name"], count=len(data), destination=f"{self.get('ds'):04x}:{dx:04x}")
        elif ah == 0x42:
            if bx not in self.files or al > 2: self.error(6); return
            file = self.files[bx]; offset = struct.unpack("<i", struct.pack("<HH", dx, cx))[0]
            position = (0, file["position"], len(file["data"]))[al] + offset
            if position < 0: self.error(1); return
            file["position"] = position; self.success(ax=position & 65535, dx=position >> 16)
        elif ah == 0x40:
            text = bytes(machine.mem_read(ds_dx, cx)).decode("cp437")
            if bx > 4: self.stop("file write blocked"); return
            self.emit("dos_console", handle=bx, text=text); self.success(ax=cx)
        elif ah == 9:
            self.emit("dos_console", text=self.string(ds_dx, ord('$'), 4096)); self.success()
        elif ah == 0x19: self.success(ax=(ax & 0xff00) | 2)
        elif ah == 0x47:
            machine.mem_write(self.address(self.get("ds"), self.get("si")), b"COVERT\0"); self.success()
        elif ah == 0x1a: self.success()
        elif ah == 0x4b:
            filename = self.string(ds_dx)
            block = bytes(machine.mem_read(self.address(self.get("es"), bx), 4 if al == 3 else 14))
            if al == 3:
                destination, relocation = struct.unpack("<HH", block)
                path = self.game / filename.replace("\\", "/").rsplit("/", 1)[-1].upper()
                exe = load(path); image = bytearray(exe.image)
                for s, o in exe.relocations:
                    at = s * 16 + o
                    struct.pack_into("<H", image, at, (struct.unpack_from("<H", image, at)[0] + relocation) & 65535)
                machine.mem_write(destination * 16, bytes(image)); self.success()
                self.emit("dos_overlay", mode=3, filename=filename,
                          load_segment=f"{destination:04x}", relocation_factor=f"{relocation:04x}",
                          image_bytes=len(image), parameter_block=block.hex())
                return
            environment, tailoff, tailseg = struct.unpack_from("<3H", block)
            tailptr = tailseg * 16 + tailoff
            tail = bytes(machine.mem_read(tailptr + 1, machine.mem_read(tailptr, 1)[0])) if al == 0 else b""
            self.emit("dos_exec", mode=al, filename=filename, parameter_block=block.hex(),
                      environment=f"{environment:04x}", command_tail=tail.hex(),
                      command_tail_text=tail.decode("cp437"), shared=self.shared())
            if filename.upper().endswith("FINAL.EXE"): self.stop("reached first FINAL.EXE EXEC"); return
            if al != 0: self.stop(f"unsupported EXEC mode {al}"); return
            self.pending = ("exec", filename, tail); machine.emu_stop()
        elif ah == 0x4c:
            self.emit("dos_exit", code=al, shared=self.shared())
            self.pending = ("exit", al); machine.emu_stop()
        elif ah == 0x4d:
            self.success(ax=self.exit_code); self.emit("dos_child_status", code=self.exit_code)
        else: self.stop(f"unsupported DOS int 21 AX={ax:04x}")

    def run(self):
        self.start_process("COVERT.EXE")
        while not self.stop_reason:
            self.pending = None
            try:
                self.machine.emu_start(self.get("cs") * 16 + self.get("ip"), 0x200000, count=self.budget)
            except uc.UcError as error:
                self.stop_reason = f"Unicorn: {error} at {self.location()}"; break
            if not self.pending:
                if not self.stop_reason: self.stop_reason = "execution stopped without transition"
                break
            if self.pending[0] == "exec":
                _, filename, tail = self.pending
                self.stack.append((dict(self.current), {name:self.get(name) for name in REGISTERS}))
                self.start_process(filename.replace("\\", "/").rsplit("/", 1)[-1], tail, self.current["psp"])
            else:
                self.exit_code = self.pending[1]
                owner = self.current["psp"]
                self.blocks = {seg:block for seg, block in self.blocks.items() if block[1] != owner}
                self.files = {handle:file for handle, file in self.files.items() if file["owner"] != owner}
                if not self.stack: self.stop_reason = "root process exited"; break
                self.current, regs = self.stack.pop(); self.set(**regs); self.success(ax=0)
                self.emit("process_resume", child_exit_code=self.exit_code, shared=self.shared())
        return dict(schema=1, stop_reason=self.stop_reason, instructions=self.instructions,
                    services=dict(sorted(self.calls.items())), processes=self.processes, events=self.events,
                    final_shared=self.shared(), remaining_keys=self.keys,
                    simulated_services="DOS allocation/PSP/environment/process lifecycle, read-only files, text BIOS and deterministic keyboard/time",
                    limits="Original guest instructions; simulated DOS/BIOS. No gameplay, sound, rendering, or original-DOS timing claim.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, default=ROOT / "game/Covert Action/Covert")
    parser.add_argument("--keys", default="112", help="VGA, no sound, no joystick by default")
    parser.add_argument("--budget", type=int, default=2_000_000)
    parser.add_argument("--out", type=Path, default=ROOT / "build/startup-oracle/startup.json")
    args = parser.parse_args()
    if ROOT / "build/startup-oracle" not in args.out.resolve().parents:
        parser.error("output must be inside build/startup-oracle")
    report = Probe(args.game, args.keys, args.budget).run()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(report["stop_reason"])
    print(json.dumps(report["services"], indent=2))
    print(json.dumps(report["final_shared"], indent=2))


if __name__ == "__main__": main()
