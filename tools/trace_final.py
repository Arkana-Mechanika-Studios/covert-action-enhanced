"""Original-byte FINAL probe with explicitly simulated DOS/BIOS services.

Keeps the independently validated startup oracle unchanged. All game files are
read-only; outputs belong under build/final-oracle. Unsupported behavior stops.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import re
import time

from trace_startup import Probe, ROOT, REGISTERS, uc
from memory_layout import resolve_layout

KEY_NAMES={"ENTER":"\r","ESC":"\x1b","SPACE":" ","DOWN":"↓","UP":"↑","LEFT":"←","RIGHT":"→"}
KEY_NAMES.update({f"F{i}":chr(0xe000+i) for i in range(1,11)})
KEY_NAMES.update(ALT_P="\ue00b",ALT_Q="\ue00c")


def bios_key_code(key):
    codes={"\r":0x1c0d,"\n":0x1c0d," ":0x3920,"\x1b":0x011b,
           "\b":0x0e08,"\t":0x0f09,"↑":0x4800,"↓":0x5000,"←":0x4b00,"→":0x4d00,"0":0x0b30}
    codes.update({str(i):(i+1)*256+ord(str(i)) for i in range(1,10)})
    codes.update({chr(0xe000+i):(0x3a+i)<<8 for i in range(1,11)})
    codes.update({"\ue00b":0x1900,"\ue00c":0x1000})
    for start,row in ((0x10,"qwertyuiop"),(0x1e,"asdfghjkl"),(0x2c,"zxcvbnm")):
        for index,ch in enumerate(row):
            codes[ch]=(start+index)*256+ord(ch)
            codes[ch.upper()]=(start+index)*256+ord(ch.upper())
    return codes.get(key,ord(key))


class FinalProbe(Probe):
    def __init__(self, game, keys="212", budget=5_000_000, actions=(), *, discovery=True, artifacts="full", idle_polls=256, memory_layout="compact"):
        if type(idle_polls) is not int or not 1 <= idle_polls <= 4096:
            raise ValueError("idle poll threshold must be an integer from 1 to 4096")
        self.memory_layout=resolve_layout(memory_layout)
        super().__init__(game, keys, budget,environment_segment=self.memory_layout["environment_segment"])
        self.idle_polls=idle_polls
        self.palette = bytearray(256 * 3)
        self.checkpoints = []
        self.ticks = 0x1234
        self.overscan = 0
        self.dynamic_targets = {}
        self.previous_transfer = None
        self.module_ranges = []
        self.poll_count, self.stable_polls, self.last_poll_hash = 0, 0, None
        self.actions = list(actions)
        self.wait_count = 0
        self.empty_polls = 0
        self.saved_artifacts = set()
        self.code_cache, self.code_byte_owners = {}, {}
        self.shared_address = 0
        self.discovery = discovery
        if artifacts not in ("full", "validation"): raise ValueError("unknown artifact mode")
        self.artifacts = artifacts
        self.allowed_programs = {"INTRO.EXE", "FINAL.EXE"}
        self.keyboard_programs = {"FINAL.EXE"}
        self.pending_bios_read=None
        self.pending_exec_boundary=None
        self.blocking_input_staging=False

    def complete_bios_read(self):
        """Complete an already observed BIOS read without replaying its INT."""
        if not self.keys: return False
        if self.pending_bios_read is not None:
            expected=self.pending_bios_read
            actual=dict(module=self.current["name"],psp=self.current["psp"],registers={name:self.get(name) for name in REGISTERS})
            if expected!=actual: raise ValueError("pending BIOS read context changed before completion")
            if self.blocking_input_staging:
                self.wait_count+=1; self.snapshot(f"wait-{self.wait_count}")
        key=self.keys.pop(0); self.empty_polls=0
        self.set(ax=bios_key_code(key)); self.emit("bios_key",key=key,code=self.get("ax"))
        self.pending_bios_read=None
        return True

    def start_process(self, name, tail=b"", parent=0):
        self.code_cache.clear(); self.code_byte_owners.clear()
        super().start_process(name, tail, parent)
        # A later DOS child may occupy the exact physical region of its predecessor.
        # API writes alone must not leave compiled Unicorn blocks from that child.
        segment=self.current["psp"]
        self.machine.ctl_remove_cache(segment*16,(segment+self.blocks[segment][0])*16)
        from executables import load
        self.module_ranges.append((name.upper(), self.current["load"], len(load(self.game / name.upper()).image)))

    def identify(self, cs, ip):
        address = cs * 16 + ip
        for name, base, size in reversed(self.module_ranges):
            if base * 16 <= address < base * 16 + size:
                return dict(name=name, cs=f"{cs-base:04x}", ip=f"{ip:04x}", linear=f"{address-base*16:05x}")
        return dict(name="unknown", cs=f"{cs:04x}", ip=f"{ip:04x}", linear=f"{address:05x}")

    def instruction(self, machine, address, size, context):
        super().instruction(machine, address, size, context)
        if not self.discovery: return
        if self.previous_transfer:
            source = self.previous_transfer
            target = self.identify(self.get("cs"), self.get("ip"))
            key = (str(source), str(target))
            if key not in self.dynamic_targets:
                self.dynamic_targets[key] = dict(source=source, target=target, count=0)
            self.dynamic_targets[key]["count"] += 1
            self.previous_transfer = None
        cached=self.code_cache.get(address)
        if cached is None or cached[0]!=size:
            code=bytes(machine.mem_read(address,size)); at=0
            while at<len(code) and code[at] in (0x26,0x2e,0x36,0x3e,0x66,0x67,0xf2,0xf3): at+=1
            dynamic=at<len(code) and (code[at]==0xea or (code[at]==0xff and at+1<len(code) and (code[at+1]>>3)&7 in (2,3,4,5)))
            cached=(size,dynamic); self.code_cache[address]=cached
            for byte in range(address,address+size): self.code_byte_owners.setdefault(byte,set()).add(address)
        if cached[1]:
            code=bytes(machine.mem_read(address,size))
            self.previous_transfer = dict(**self.identify(self.get("cs"), self.get("ip")), bytes=code.hex())

    def memory_write(self,machine,access,address,size,value,context):
        # Cache only instrumentation classification; every code-byte write
        # invalidates overlapping instructions, including patched operands.
        if self.discovery:
            for byte in range(address,address+size):
                for owner in self.code_byte_owners.get(byte,()): self.code_cache.pop(owner,None)
        if address in (0x4f0,0x4f2):
            super().memory_write(machine,access,address,size,value,context)
            pointer=bytearray(machine.mem_read(0x4f0,4))
            pointer[address-0x4f0:address-0x4f0+size]=value.to_bytes(size,"little")
            off,seg=struct.unpack("<HH",pointer)
            self.shared_address=seg*16+off if seg else 0
        elif self.shared_address and self.shared_address<=address<self.shared_address+112:
            self.emit("shared_write",offset=f"{address-self.shared_address:04x}",size=size,value=value)

    def emit(self, kind, **fields):
        super().emit(kind, **fields)
        if kind == "dos_overlay":
            self.module_ranges.append((fields["filename"].upper(), int(fields["load_segment"],16), fields["image_bytes"]))
            begin=int(fields["load_segment"],16)*16
            self.machine.ctl_remove_cache(begin,begin+fields["image_bytes"])

    def record_interrupt(self, number):
        self.calls[f"{self.current['name']} int {number:02x} AH={self.get('ax')>>8:02x}"] += 1
        self.emit("interrupt", number=number, **{r:self.get(r) for r in ("ax","bx","cx","dx","ds","es")})

    def free_regions(self):
        # Same deterministic allocation fixture as the native host, not DOS MCBs.
        cursor = self.memory_layout["arena_start"]
        for segment, (size, _) in sorted(self.blocks.items()):
            if segment > cursor: yield cursor, segment - cursor
            cursor = max(cursor, segment + size)
        top=self.memory_layout["conventional_top"]
        if cursor < top: yield cursor, top - cursor

    def snapshot(self, label):
        pixels = bytes(self.machine.mem_read(0xa0000, 64000))
        result = dict(label=label, instructions=self.instructions, ticks=self.ticks,
                      empty_polls=self.empty_polls, stable_polls=self.stable_polls,
                      **self.location(), shared=self.shared(),
                      registers={name:self.get(name) for name in ("ax","bx","cx","dx","si","di","bp","sp","cs","ip","ds","es","ss")},
                      pixels_sha256=hashlib.sha256(pixels).hexdigest(),
                      palette_sha256=hashlib.sha256(self.palette).hexdigest())
        retain=self.artifacts=="full" or not label.startswith("key-")
        self.checkpoints.append((result, pixels if retain else None, bytes(self.palette) if retain else None))
        self.emit("checkpoint", **{k:v for k,v in result.items() if k not in self.location()})

    def stop(self, reason):
        if reason == "reached first FINAL.EXE EXEC":
            self.pending = ("exec", "FINAL.EXE", b"")
            self.machine.emu_stop()
            return
        super().stop(reason)

    def input_status_boundary(self,available):
        """Schedule input at measured guest polls; never alter game state."""
        if available:
            self.empty_polls=0; return
        self.empty_polls+=1
        digest=hashlib.sha256(self.machine.mem_read(0xa0000,64000)).hexdigest()
        self.stable_polls=self.stable_polls+1 if digest==self.last_poll_hash else 0
        self.last_poll_hash=digest
        if self.stable_polls==64 or self.empty_polls>=self.idle_polls:
            self.wait_count+=1
            self.snapshot(f"wait-{self.wait_count}")
            if self.actions:
                action=self.actions.pop(0); action=KEY_NAMES.get(action,action)
                self.keys.extend(action); self.emit("scheduled_input",keys=action)
                self.stable_polls=self.empty_polls=0
            else:
                self.stop("stable keyboard poll boundary" if self.stable_polls==64 else "animated keyboard poll boundary")

    def interrupt(self, machine, number, context):
        # Simulated services can overwrite guest buffers or load entire images.
        # Do not retain instrumentation classifications across host writes.
        self.code_cache.clear(); self.code_byte_owners.clear()
        ax, bx, cx, dx = (self.get(r) for r in ("ax", "bx", "cx", "dx"))
        ah = ax >> 8
        if number == 0x21 and ah == 0x3d:
            # DOS reuses released handles. A monotonically growing identifier
            # eventually exceeds the original CRT's 20-entry table (GAME eeD3).
            handle=next((n for n in range(5,20) if n not in self.files),None)
            if handle is None:
                self.record_interrupt(number); self.error(4); return
            self.next_handle=handle
        if number == 0x21 and ah == 0x11:
            self.record_interrupt(number)
            fcb=bytes(machine.mem_read(self.get("ds")*16+dx,12))
            stem=fcb[1:9].decode("ascii").rstrip(); ext=fcb[9:12].decode("ascii").rstrip()
            name=stem+("."+ext if ext else "")
            if fcb[0] not in (0,3) or any(ch in name for ch in "*?\\/:"):
                self.stop("unsupported FCB directory search "+repr(name)); return
            found=(self.game/name.upper()).is_file()
            self.set(ax=(ax&0xff00)|(0 if found else 255))
            self.emit("dos_fcb_find",name=name,found=found,dta_record_written=False)
            return
        if number == 0x21 and ax == 0x2900:
            source=self.string(self.get("ds")*16+self.get("si"))
            if source.strip(" \t\r"):
                match=re.match(r"([A-Za-z0-9_]{1,8})(?:\.([A-Za-z0-9_]{1,3}))?(?=$|[ \t\r])",source)
                if not match:
                    self.record_interrupt(number)
                    self.stop("unsupported nonempty FCB parse "+repr(source)); return
                self.record_interrupt(number)
                stem,ext=match.group(1).upper(),(match.group(2) or "").upper()
                data=b"\0"+stem.encode().ljust(8,b" ")+ext.encode().ljust(3,b" ")
                machine.mem_write(self.get("es")*16+self.get("di"),data)
                self.success(ax=0x2900,si=self.get("si")+match.end())
                self.emit("dos_fcb_parse",source=match.group(),bytes=data.hex())
                return
        if number == 0x21 and ax == 0x4300:
            self.record_interrupt(number)
            name=self.string(self.get("ds")*16+dx).replace("\\","/").rsplit("/",1)[-1].upper()
            exists=(self.game/name).is_file()
            if exists: self.success(cx=0x20)
            else: self.error(2)
            self.emit("dos_attributes",name=name,exists=exists,attributes=0x20 if exists else None)
            return
        if number == 0x1a and ah == 0:
            self.record_interrupt(number)
            self.set(ax=0, cx=self.ticks>>16, dx=self.ticks&65535)
            self.emit("bios_ticks", ticks=self.ticks)
            self.ticks += 1
            return
        if number == 0x16 and self.current["name"] in self.keyboard_programs and ah in (0,1,0x10,0x11):
            self.record_interrupt(number)
            self.poll_count += 1
            if ah in (1,0x11):
                self.flags(0x40, not self.keys)
                if self.keys: self.set(ax=bios_key_code(self.keys[0]))
                self.input_status_boundary(bool(self.keys))
            else:
                self.snapshot(f"key-{self.poll_count}")
                if not self.keys and self.blocking_input_staging and self.actions:
                    self.wait_count+=1; self.snapshot(f"wait-{self.wait_count}")
                    action=self.actions.pop(0); action=KEY_NAMES.get(action,action)
                    self.keys.extend(action); self.emit("scheduled_input",keys=action)
                if not self.keys:
                    self.pending_bios_read=dict(module=self.current["name"],psp=self.current["psp"],registers={name:self.get(name) for name in REGISTERS})
                    self.stop("keyboard input exhausted"); return
                self.complete_bios_read()
            return
        if number == 0x21 and ah in (0x2a,0x2c):
            self.record_interrupt(number)
            if ah == 0x2a:
                self.success(ax=(ax&0xff00)|1, cx=1990, dx=0x0b05)
                self.emit("dos_date", year=1990, month=11, day=5, weekday=1)
            else:
                self.success(cx=0x0c00, dx=0)
                self.emit("dos_time", hour=12, minute=0, second=0, hundredth=0)
            return
        if number == 0x10 and ax == 0x1001:
            self.record_interrupt(number)
            self.overscan = bx >> 8
            self.emit("bios_overscan", color=self.overscan)
            return
        if number == 0x21 and ah == 0x4b and ax & 255 == 0:
            filename = self.string(self.get("ds") * 16 + dx).upper()
            if filename not in self.allowed_programs:
                self.record_interrupt(number)
                block=bytes(machine.mem_read(self.get("es")*16+bx,14))
                environment,tailoff,tailseg=struct.unpack_from("<3H",block)
                tailptr=tailseg*16+tailoff
                tail=bytes(machine.mem_read(tailptr+1,machine.mem_read(tailptr,1)[0]))
                self.emit("dos_exec",mode=0,filename=filename,parameter_block=block.hex(),
                          environment=f"{environment:04x}",command_tail=tail.hex(),
                          command_tail_text=tail.decode("cp437"),shared=self.shared())
                self.emit("next_process_boundary", filename=filename, shared=self.shared())
                self.pending_exec_boundary=dict(filename=filename,tail=tail,context=dict(module=self.current["name"],psp=self.current["psp"],registers={name:self.get(name) for name in REGISTERS}))
                self.stop("reached next process " + filename)
                return
        if number == 0x10 and ax == 0x1012:
            self.palette[bx*3:(bx+cx)*3] = machine.mem_read(self.get("es") * 16 + dx, cx * 3)
        super().interrupt(machine, number, context)

    def run(self, resume=False):
        wall_started, cpu_started=time.perf_counter(),time.process_time()
        if not resume: self.start_process("COVERT.EXE")
        self.stop_reason = None
        if resume and self.pending_bios_read is not None and not self.complete_bios_read():
            self.stop_reason="keyboard input exhausted"
        if resume and self.pending_exec_boundary is not None:
            boundary=self.pending_exec_boundary
            context=dict(module=self.current["name"],psp=self.current["psp"],registers={name:self.get(name) for name in REGISTERS})
            if context!=boundary["context"]: raise ValueError("pending EXEC context changed before continuation")
            if boundary["filename"] not in self.allowed_programs:
                self.stop_reason="reached next process "+boundary["filename"]
            else:
                self.stack.append((dict(self.current),context["registers"]))
                parent=self.current["psp"]; self.pending_exec_boundary=None
                self.start_process(boundary["filename"],boundary["tail"],parent)
        while not self.stop_reason:
            self.pending = None
            try:
                self.machine.emu_start(self.get("cs")*16+self.get("ip"), 0x200000, count=self.budget)
            except uc.UcError as error:
                self.stop_reason=f"Unicorn: {error} at {self.location()}"; break
            if not self.pending:
                if not self.stop_reason: self.stop_reason="execution stopped without transition"
                break
            if self.pending[0] == "exec":
                _, filename, tail = self.pending
                self.stack.append((dict(self.current), {name:self.get(name) for name in REGISTERS}))
                self.start_process(filename.replace("\\","/").rsplit("/",1)[-1],tail,self.current["psp"])
            else:
                self.exit_code=self.pending[1]
                owner=self.current["psp"]
                self.blocks={seg:block for seg,block in self.blocks.items() if block[1]!=owner}
                self.files={handle:file for handle,file in self.files.items() if file["owner"]!=owner}
                if not self.stack: self.stop_reason="root process exited"; break
                self.current,regs=self.stack.pop(); self.set(**regs); self.success(ax=0)
                self.emit("process_resume",child_exit_code=self.exit_code,shared=self.shared())
        result=dict(schema=1, stop_reason=self.stop_reason, instructions=self.instructions,
                    services=dict(sorted(self.calls.items())),processes=self.processes,events=self.events,
                    final_shared=self.shared(),remaining_keys=self.keys)
        self.snapshot("stop")
        result.update(checkpoints=[r for r, _, _ in self.checkpoints],
                      memory_layout=dict(self.memory_layout),
                      instrumentation=dict(discovery=self.discovery,artifacts=self.artifacts),
                      input_fixture=dict(idle_polls=self.idle_polls,stable_matching_polls=64),
                      timings=dict(run_wall_seconds=time.perf_counter()-wall_started,run_cpu_seconds=time.process_time()-cpu_started),
                      session_files=[],
                      dynamic_targets=list(self.dynamic_targets.values()),
                      final_registers={name:self.get(name) for name in REGISTERS},
                      simulated_services="Startup fixtures plus explicitly recorded FINAL service fixtures; original game and driver instructions",
                      limits="Not a real DOS machine; unknown services stop. No claims beyond the captured path.")
        return result

    def save(self, out, report):
        wall_started, cpu_started=time.perf_counter(),time.process_time()
        out.mkdir(parents=True, exist_ok=True)
        (out / "stop.memory.bin").write_bytes(bytes(self.machine.mem_read(0, 0x100000)))
        for index, (item, pixels, palette) in enumerate(self.checkpoints):
            if index in self.saved_artifacts: continue
            if pixels is None:
                self.saved_artifacts.add(index); continue
            base = f"{index:03}-{item['label']}"
            (out / (base + ".pixels")).write_bytes(pixels)
            (out / (base + ".palette")).write_bytes(palette)
            if self.artifacts=="full":
                from PIL import Image
                picture = Image.frombytes("P", (320, 200), pixels)
                picture.putpalette([min(255, (v << 2) | (v >> 4)) for v in palette])
                picture.save(out / (base + ".png"))
            item["artifact_prefix"] = str((out / base).resolve())
            self.saved_artifacts.add(index)
        (out / "original.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        timing=dict(report["timings"],save_wall_seconds=time.perf_counter()-wall_started,save_cpu_seconds=time.process_time()-cpu_started)
        (out/"timings.json").write_text(json.dumps(timing,indent=2)+"\n",encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, default=ROOT / "game/Covert Action/Covert")
    parser.add_argument("--keys", default="212")
    parser.add_argument("--budget", type=int, default=5_000_000)
    parser.add_argument("--actions", default="[]", help="JSON list of keys supplied after each stable polling boundary")
    parser.add_argument("--interactive", action="store_true", help="Resume the same original machine after each inspected polling boundary")
    parser.add_argument("--memory-layout",choices=("legacy","compact"),default="compact")
    parser.add_argument("--light-discovery", action="store_true", help="Retain instruction counting and observables but omit dynamic-transfer discovery")
    parser.add_argument("--artifacts", choices=("full","validation"), default="full", help="Validation retains wait/stop raw frames, palettes, memory and hashes; omits PNG/key-image artifacts")
    parser.add_argument("--out", type=Path, default=ROOT / "build/final-oracle/entry")
    args = parser.parse_args()
    if ROOT / "build/final-oracle" not in args.out.resolve().parents:
        parser.error("output must be below build/final-oracle")
    probe = FinalProbe(args.game, args.keys, args.budget, json.loads(args.actions),discovery=not args.light_discovery,artifacts=args.artifacts,memory_layout=args.memory_layout)
    phase=0
    while True:
        report = probe.run(resume=phase>0)
        out=args.out / f"phase-{phase:03}" if args.interactive else args.out
        probe.save(out, report)
        print(report["stop_reason"],"instructions",report["instructions"],"out",out,flush=True)
        print(json.dumps(report["final_registers"]),flush=True)
        if not args.interactive or report["stop_reason"] not in ("stable keyboard poll boundary","animated keyboard poll boundary"): break
        action=input("NEXT (ENTER/ESC/DOWN/etc, QUIT to finish): ")
        if action=="QUIT": break
        action=KEY_NAMES.get(action,action)
        probe.keys.extend(action)
        probe.emit("scheduled_input",keys=action)
        probe.stable_polls=0; probe.empty_polls=0
        phase+=1


if __name__ == "__main__": main()
