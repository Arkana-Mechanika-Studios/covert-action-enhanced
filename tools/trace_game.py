"""Continue the legitimate FINAL briefing into original GAME headquarters code."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

from trace_final import KEY_NAMES, ROOT
from trace_code import CodeProbe
from minigame_handoff import MODULES as HANDOFF_MODULES, capture_capability, handoff_extent
from session_seed import is_session_name
from game_handoff import game_handoff_bounds, validate_game_loader

BRIEFING_ACTIONS=["ENTER","ENTER","MAX","ENTER","ENTER","ENTER","ENTER","ENTER","ESC",
                  "ENTER","ENTER","ENTER","ENTER","ENTER","UP","LEFT","ENTER","ENTER"]


class GameProbe(CodeProbe):
    def __init__(self,*args,seed_session=None,discovery_modules=None,**kwargs):
        super().__init__(*args,**kwargs)
        # Campaign CODE remains an explicit process boundary until the caller
        # permits its original instructions with --continue-program CODE.EXE.
        self.allowed_programs.discard("CODE.EXE")
        self.allowed_programs.add("GAME.EXE")
        self.keyboard_programs.add("GAME.EXE")
        from session_seed import read_seed_session
        self.virtual_files,self.seed_files=read_seed_session(seed_session)
        self.blocking_input_staging=True
        self.discovery_modules=None if discovery_modules is None else {name.upper() for name in discovery_modules}
        self.game_handoff_evidence=None

    def save_name(self):
        path=self.string(self.get("ds")*16+self.get("dx")).replace("\\","/")
        return path.rsplit("/",1)[-1].rsplit(":",1)[-1].upper().rstrip()

    def tac_handoff(self,kind):
        address=self.shared()["address"]
        header=bytes(self.machine.mem_read(address,112))
        n=int.from_bytes(header[0x3e:0x40],"little"); m=int.from_bytes(header[0x40:0x42],"little")
        size=9782+54*n+44*m
        if n>0x7fff or m>0x7fff or address+size>0x100000: raise ValueError("invalid TAC shared-state extent")
        self.emit(kind,shared_address=address,handoff_size=size,handoff_hex=bytes(self.machine.mem_read(address,size)).hex())

    def minigame_handoff(self,phase):
        module=self.current["name"]
        address=self.shared()["address"]
        size=handoff_extent(module,bytes(self.machine.mem_read(address,112)))
        offset=self.word(0x4f0)
        if offset+size>0x10000 or address+size>0x100000:
            raise ValueError("minigame shared handoff exceeds its captured segment")
        self.emit(module.split(".")[0].lower()+"_"+phase,shared_address=address,
                  handoff_size=size,handoff_hex=bytes(self.machine.mem_read(address,size)).hex())

    def interrupt(self,machine,number,context):
        ax=self.get("ax"); ah=ax>>8
        if number==0x21 and ah in (0x3c,0x3d,0x43):
            name=self.save_name()
            if ah==0x3c or is_session_name(name):
                self.code_cache.clear(); self.code_byte_owners.clear()
                self.record_interrupt(number)
                if not is_session_name(name):
                    self.stop("unsupported writable filename "+repr(name)); return
                if ah==0x43:
                    if ax&255: self.stop("unsupported save attribute update"); return
                    if name not in self.virtual_files and not (self.game/name).is_file():
                        self.error(2); self.emit("dos_attributes",name=name,exists=False,attributes=None); return
                    self.success(cx=0x20)
                    self.emit("dos_attributes",name=name,exists=True,attributes=0x20); return
                handle=next((n for n in range(5,20) if n not in self.files),None)
                if handle is None: self.error(4); return
                if ah==0x3c: self.virtual_files[name]=b""
                if name in self.virtual_files: data=self.virtual_files[name]
                elif (self.game/name).is_file(): data=(self.game/name).read_bytes()
                else:
                    self.error(2); self.emit("dos_open_missing",name=name); return
                writable=ah==0x3c or (ax&3)!=0
                if writable: self.virtual_files[name]=data
                self.files[handle]=dict(name=name,data=data,position=0,owner=self.current["psp"],writable=writable)
                self.success(ax=handle)
                requested=self.string(self.get("ds")*16+self.get("dx")).upper()
                self.emit("dos_open",name=requested,access=ax&255 if ah==0x3d else 2,handle=handle,created=ah==0x3c)
                return
        if number==0x21 and ah==0x40 and self.get("bx")>4:
            self.code_cache.clear(); self.code_byte_owners.clear(); self.record_interrupt(number)
            file=self.files.get(self.get("bx"))
            if file is None: self.error(6); return
            if not file.get("writable") or file["name"] not in self.virtual_files:
                self.stop("unsupported write to read-only opened file"); return
            count=self.get("cx"); position=file["position"]; name=file["name"]
            data=bytes(machine.mem_read(self.get("ds")*16+self.get("dx"),count))
            original=self.virtual_files[name]
            if count:
                original=original.ljust(position,b"\0")
                updated=original[:position]+data+original[position+count:]
            else: updated=original[:position].ljust(position,b"\0")
            self.virtual_files[name]=updated
            for opened in self.files.values():
                if opened["name"]==name: opened["data"]=updated
            file["position"]+=count; self.success(ax=count)
            self.emit("dos_write",name=name,offset=position,count=count,source=f"{self.get('ds'):04x}:{self.get('dx'):04x}",sha256=hashlib.sha256(data).hexdigest())
            return
        exiting=number==0x21 and ah==0x4c
        super().interrupt(machine,number,context)
        if exiting and self.pending and self.pending[0]=="exit":
            if self.current["name"]=="TAC.EXE": self.tac_handoff("tac_exit")
            elif self.current["name"] in HANDOFF_MODULES: self.minigame_handoff("exit")

    def start_process(self,name,tail=b"",parent=0):
        if name.upper()=="GAME.EXE" and self.game_handoff_evidence is None:
            from executables import load
            self.game_handoff_evidence=validate_game_loader(load(self.game/"GAME.EXE"))
        if self.discovery_modules is not None: self.discovery=name.upper() in self.discovery_modules
        super().start_process(name,tail,parent)
        if name.upper()=="GAME.EXE":
            offset,segment=self.word(0x4f0),self.word(0x4f2)
            address=segment*16+offset
            if offset+112>0x10000 or address+112>0x100000:
                raise ValueError("GAME shared header exceeds its captured segment or memory")
            size=game_handoff_bounds(bytes(self.machine.mem_read(address,112)),offset,segment)
            self.emit("game_start",shared_address=address,shared_offset=offset,shared_segment=segment,
                      handoff_size=size,handoff_hex=bytes(self.machine.mem_read(address,size)).hex())
        elif name.upper()=="TAC.EXE":
            self.tac_handoff("tac_start")
        elif name.upper() in HANDOFF_MODULES:
            self.minigame_handoff("start")

    def run(self,resume=False):
        report=super().run(resume)
        report["handoff_capture"]=capture_capability()
        if self.game_handoff_evidence is not None: report["game_handoff_evidence"]=self.game_handoff_evidence
        if self.discovery_modules is not None: report["instrumentation"]["discovery_modules"]=sorted(self.discovery_modules)
        report["seed_files"]=self.seed_files
        report["session_files"]=[dict(name=name,size=len(data),sha256=hashlib.sha256(data).hexdigest(),bytes_hex=data.hex())
                                 for name,data in sorted(self.virtual_files.items())]
        if self.current["name"]=="GAME.EXE":
            from game_state import extract_game_state
            memory=bytes(self.machine.mem_read(0,0x100000))
            modules={int(e["load_segment"],16):e["filename"].upper() for e in self.events if e["kind"]=="dos_overlay"}
            address=self.shared()["address"]
            segments=[int.from_bytes(memory[address+offset:address+offset+2],"little") for offset in (0x60,0x62,0x64)]
            report["campaign_state"]=extract_game_state(memory,self.current["load"],drivers=[(modules[seg],seg) for seg in segments])
        return report

    def save(self,out,report):
        super().save(out,report)
        if self.virtual_files:
            directory=out/"session-files"; directory.mkdir(exist_ok=True)
            for name,data in self.virtual_files.items(): (directory/name).write_bytes(data)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,default=ROOT/"game/Covert Action/Covert")
    parser.add_argument("--keys",default="212 ")
    inputs=parser.add_mutually_exclusive_group()
    inputs.add_argument("--actions",default=json.dumps(BRIEFING_ACTIONS))
    inputs.add_argument("--actions-file",type=Path,help="JSON action list, including literal mixed-character batches")
    parser.add_argument("--budget",type=int,default=150_000_000)
    parser.add_argument("--light-discovery",action="store_true")
    parser.add_argument("--artifacts",choices=("full","validation"),default="full")
    parser.add_argument("--interactive",action="store_true")
    parser.add_argument("--idle-polls",type=int,default=256,help="External input/checkpoint delay in empty polls (1..4096; default 256)")
    parser.add_argument("--memory-layout",choices=("legacy","compact"),default="compact")
    parser.add_argument("--seed-session",type=Path,help="Read-only CV0..3.SVE/FAME.DTA seed file or flat directory below build")
    parser.add_argument("--continue-program",action="append",default=[],help="Record an EXEC boundary, then allow this original module to run")
    parser.add_argument("--discover-module",action="append",help="Discover dynamic transfers only while these modules run; count every instruction throughout")
    parser.add_argument("--out",type=Path,default=ROOT/"build/game-oracle/entry")
    args=parser.parse_args()
    if not 1<=args.idle_polls<=4096: parser.error("idle poll threshold must be from 1 to 4096")
    temporary=ROOT/"build/tmp"; temporary.mkdir(exist_ok=True)
    os.environ["TMP"]=os.environ["TEMP"]=str(temporary)
    if ROOT/"build/game-oracle" not in args.out.resolve().parents: parser.error("output must be below build/game-oracle")
    actions=json.loads(args.actions_file.read_text(encoding="utf-8") if args.actions_file else args.actions)
    continuations={name.upper() for name in args.continue_program}
    if any(not re.fullmatch(r"[A-Z0-9_]+\.EXE",name) for name in continuations): parser.error("continuation module must be a simple DOS executable filename")
    probe=GameProbe(args.game,args.keys,args.budget,actions,discovery=not args.light_discovery,artifacts=args.artifacts,seed_session=args.seed_session,discovery_modules=args.discover_module,idle_polls=args.idle_polls,memory_layout=args.memory_layout)
    phase=0
    while True:
        report=probe.run(resume=phase>0)
        out=args.out/f"phase-{phase:03}" if args.interactive or continuations else args.out
        probe.save(out,report)
        print(report["stop_reason"],"instructions",report["instructions"],"out",out,flush=True)
        print(json.dumps(report["final_registers"]),flush=True)
        if probe.pending_exec_boundary and probe.pending_exec_boundary["filename"] in continuations:
            name=probe.pending_exec_boundary["filename"]
            probe.allowed_programs.add(name); probe.keyboard_programs.add(name)
            print("Continuing original",name,"after recorded EXEC boundary",flush=True)
            phase+=1; continue
        if not args.interactive or (report["stop_reason"] not in ("stable keyboard poll boundary","animated keyboard poll boundary") and probe.pending_bios_read is None): break
        action=input("NEXT (key name or literal characters, QUIT to finish): ")
        if action=="QUIT": break
        action=KEY_NAMES.get(action,action)
        probe.keys.extend(action); probe.emit("scheduled_input",keys=action)
        if probe.pending_bios_read is None: probe.stable_polls=probe.empty_polls=0
        phase+=1


if __name__=="__main__": main()
