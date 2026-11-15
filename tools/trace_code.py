"""Trace the original FINAL practice menu into CODE without invented game state."""
import argparse
import json
from pathlib import Path

from trace_final import FinalProbe, KEY_NAMES, ROOT, bios_key_code

PRACTICE_ACTIONS=["DOWN","DOWN","ENTER","ENTER","DOWN","DOWN","ENTER"]


class CodeProbe(FinalProbe):
    def __init__(self,*args,**kwargs):
        super().__init__(*args,**kwargs)
        self.allowed_programs.add("CODE.EXE")
        self.keyboard_programs.add("CODE.EXE")
        self.code_visits=0
        self.pending_dos_key=None

    def interrupt(self,machine,number,context):
        ax=self.get("ax"); ah=ax>>8
        if number==0x21 and self.current["name"]=="CODE.EXE" and ah in (0xb,7,8):
            self.code_cache.clear(); self.code_byte_owners.clear()
            self.record_interrupt(number); self.poll_count+=1
            available=self.pending_dos_key is not None or bool(self.keys)
            if ah==0xb:
                self.set(ax=(ax&0xff00)|(255 if available else 0))
                self.input_status_boundary(available)
                return
            self.snapshot(f"key-{self.poll_count}")
            if not available: self.stop("keyboard input exhausted"); return
            if self.pending_dos_key is not None:
                code=self.pending_dos_key; byte=code>>8; self.pending_dos_key=None
            else:
                code=bios_key_code(self.keys.pop(0)); byte=code&255
                if byte==0: self.pending_dos_key=code
            self.set(ax=(ax&0xff00)|byte); self.empty_polls=0
            self.emit("dos_getch",code=code,byte=byte)
            return
        super().interrupt(machine,number,context)

    def start_process(self,name,tail=b"",parent=0):
        super().start_process(name,tail,parent)
        if name.upper()=="CODE.EXE":
            self.code_visits+=1
            address=self.shared()["address"]
            self.emit("code_start",visit=self.code_visits,shared_address=address,
                      header_hex=bytes(self.machine.mem_read(address,112)).hex(),
                      message_hex=bytes(self.machine.mem_read(address+0x200,512)).hex())
        elif name.upper()=="FINAL.EXE" and self.code_visits:
            self.emit("code_return",visit=self.code_visits,shared=self.shared())


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,default=ROOT/"game/Covert Action/Covert")
    parser.add_argument("--keys",default="212 ")
    parser.add_argument("--actions",default=json.dumps(PRACTICE_ACTIONS),help="Complete input action list from FINAL's main menu")
    parser.add_argument("--budget",type=int,default=100_000_000)
    parser.add_argument("--light-discovery",action="store_true")
    parser.add_argument("--artifacts",choices=("full","validation"),default="full")
    parser.add_argument("--interactive",action="store_true")
    parser.add_argument("--memory-layout",choices=("legacy","compact"),default="compact")
    parser.add_argument("--out",type=Path,default=ROOT/"build/code-oracle/entry")
    args=parser.parse_args()
    if ROOT/"build/code-oracle" not in args.out.resolve().parents: parser.error("output must be below build/code-oracle")
    probe=CodeProbe(args.game,args.keys,args.budget,json.loads(args.actions),discovery=not args.light_discovery,artifacts=args.artifacts,memory_layout=args.memory_layout)
    phase=0
    while True:
        report=probe.run(resume=phase>0)
        out=args.out/f"phase-{phase:03}" if args.interactive else args.out
        probe.save(out,report)
        print(report["stop_reason"],"instructions",report["instructions"],"out",out,flush=True)
        print(json.dumps(report["final_registers"]),flush=True)
        if not args.interactive or report["stop_reason"] not in ("stable keyboard poll boundary","animated keyboard poll boundary"): break
        action=input("NEXT (key name or literal characters, QUIT to finish): ")
        if action=="QUIT": break
        action=KEY_NAMES.get(action,action)
        probe.keys.extend(action); probe.emit("scheduled_input",keys=action)
        probe.stable_polls=probe.empty_polls=0
        phase+=1


if __name__=="__main__": main()
