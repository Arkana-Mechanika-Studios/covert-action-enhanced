"""Bounded original ISOUND instructions with an explicit synthetic IRQ fixture.

The supplied driver and game remain read-only. This is not full title playback
or measured DOS hardware timing: calls and IRQ delivery are host-scheduled.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct

from executables import load
from trace_startup import ROOT, REGISTERS, uc
from unicorn.x86_const import UC_X86_INS_IN, UC_X86_INS_OUT

LOAD_SEGMENT=0x3000
BIOS_SEGMENT=0xf000
BIOS_OFFSET=0x0020
CALL_RETURN=0xfe00
IRQ_RETURN=0xff08
DRIVER_SPAN=0x2000
FLAGS_MASK=0x0fd7
BIOS_BYTES=bytes.fromhex("501e31c08ed883066c040183166e0400b020e6201f58cf")
INITIAL_REGISTERS=dict(ax=0x1111,bx=0x2222,cx=0x3333,dx=0x4444,
                       si=0x5555,di=0x6666,bp=0x7777,ds=0x1234,es=0x5678,
                       ss=0x8000,sp=0xff00,cs=BIOS_SEGMENT,ip=CALL_RETURN,eflags=0x7202)
DEFAULT_ACTIONS=[dict(op="call",slot=100,args=[]),dict(op="irq",count=6),
                 dict(op="call",slot=101,args=[2]),dict(op="irq",count=64),
                 dict(op="call",slot=101,args=[1]),dict(op="irq",count=16),
                 dict(op="call",slot=102,args=[]),dict(op="irq",count=1)]


def call_evidence(game):
    """Require exact original caller/driver bytes, not translated-C assumptions."""
    final=load(game/"FINAL.EXE")
    checks=[(0x123c,"b8020050e8da01","FINAL0100:023c title command2"),
            (0x12aa,"b8010050e86c01","FINAL0100:02aa menu stop command1"),
            (0x1427,"ff76049a5b04000083c402","FINAL0100:0427 slot101 thunk call")]
    rows=[]
    for offset,expected,label in checks:
        actual=final.image[offset:offset+len(bytes.fromhex(expected))]
        if actual.hex()!=expected: raise ValueError("original sound caller evidence changed: "+label+" "+actual.hex())
        rows.append(dict(label=label,image_offset=offset,bytes=actual.hex()))
    return dict(final_image_sha256=hashlib.sha256(final.image).hexdigest(),checks=rows,
                intro="INTRO selects the I sound driver through ordinary222 setup; FINAL emits the title/menu calls.")


class SoundProbe:
    def __init__(self,game):
        self.game=game; self.machine=uc.Uc(uc.UC_ARCH_X86,uc.UC_MODE_16)
        self.machine.mem_map(0,0x100000)
        self.executable=load(game/"ISOUND.CA")
        image=bytearray(self.executable.image)
        for segment,offset in self.executable.relocations:
            address=segment*16+offset
            struct.pack_into("<H",image,address,(struct.unpack_from("<H",image,address)[0]+LOAD_SEGMENT)&0xffff)
        self.machine.mem_write(LOAD_SEGMENT*16,bytes(image))
        self.machine.mem_write(0x20,struct.pack("<HH",BIOS_OFFSET,BIOS_SEGMENT))
        self.machine.mem_write(BIOS_SEGMENT*16+BIOS_OFFSET,BIOS_BYTES)
        first,count=struct.unpack_from("<HH",image,0x2e)
        self.slots=dict(zip(range(first,first+count),struct.unpack_from("<"+"H"*count,image,0x32)))
        if [self.slots[i] for i in (100,101,102)]!=[0xa6,0xca,0xbc]:
            raise ValueError("ISOUND entry evidence changed")
        self.set(**INITIAL_REGISTERS)
        self.events=[]; self.operations=[]; self.op_index=-1; self.irq_index=0
        self.pit_cycles=0; self.pit0_divisor=0; self.pit0_low=None; self.port61=0x20
        self.instructions=0; self.return_ip=None; self.returned=False; self.last_control=None
        self.machine.hook_add(uc.UC_HOOK_CODE,self.instruction)
        self.machine.hook_add(uc.UC_HOOK_MEM_WRITE,self.memory_write)
        self.machine.hook_add(uc.UC_HOOK_INSN,self.port_read,None,1,0,UC_X86_INS_IN)
        self.machine.hook_add(uc.UC_HOOK_INSN,self.port_write,None,1,0,UC_X86_INS_OUT)
        self.machine.hook_add(uc.UC_HOOK_INTR,self.interrupt)

    def get(self,name): return self.machine.reg_read(REGISTERS[name])
    def set(self,**values):
        for name,value in values.items(): self.machine.reg_write(REGISTERS[name],value)
    def registers(self): return {name:self.get(name) for name in REGISTERS}
    def word(self,address): return struct.unpack("<H",self.machine.mem_read(address,2))[0]
    def emit(self,kind,**fields):
        self.events.append(dict(kind=kind,op_index=self.op_index,irq_index=self.irq_index,pit_cycles=self.pit_cycles,
                                cs=self.get("cs"),ip=self.get("ip"),**fields))
    def push(self,value):
        sp=(self.get("sp")-2)&0xffff; self.set(sp=sp)
        self.machine.mem_write(self.get("ss")*16+sp,struct.pack("<H",value&0xffff))

    def instruction(self,machine,address,size,context):
        if self.last_control is not None:
            self.events[self.last_control]["if_after"]=bool(self.get("eflags")&0x200)
            self.last_control=None
        if self.get("cs")==BIOS_SEGMENT and self.get("ip")==self.return_ip:
            self.returned=True; machine.emu_stop(); return
        self.instructions+=1
        code=bytes(machine.mem_read(address,size))
        if code[0] in (0xfa,0xfb,0xcf):
            self.emit({0xfa:"cli",0xfb:"sti",0xcf:"iret"}[code[0]],if_before=bool(self.get("eflags")&0x200),sp=self.get("sp"))
            if code[0] in (0xfa,0xfb): self.last_control=len(self.events)-1
        if address==BIOS_SEGMENT*16+BIOS_OFFSET: self.emit("bios_chain",sp=self.get("sp"))

    def memory_write(self,machine,access,address,size,value,context):
        if address<0x24 and address+size>0x20:
            self.emit("ivt_write",address=address,size=size,value=value,if_before=bool(self.get("eflags")&0x200))

    def port_read(self,machine,port,size,context):
        if (port,size)!=(0x61,1): raise ValueError(f"unsupported isolated sound read {port:04x}/{size}")
        self.emit("port_read",port=port,size=size,value=self.port61)
        return self.port61

    def port_write(self,machine,port,size,value,context):
        if size!=1 or port not in (0x20,0x40,0x42,0x43,0x61):
            raise ValueError(f"unsupported isolated sound write {port:04x}/{size}")
        self.emit("port_write",port=port,size=size,value=value)
        if port==0x61: self.port61=value
        elif port==0x43 and value==0x36: self.pit0_low=None
        elif port==0x40:
            if self.pit0_low is None: self.pit0_low=value
            else: self.pit0_divisor=self.pit0_low|(value<<8); self.pit0_low=None

    def interrupt(self,machine,number,context):
        raise ValueError(f"unexpected software interrupt {number:02x} in isolated driver")

    def execute(self,return_ip):
        self.return_ip=return_ip; self.returned=False
        self.machine.emu_start(self.get("cs")*16+self.get("ip"),0x100000,count=1_000_000)
        if not self.returned: raise ValueError("driver operation did not reach its exact return sentinel")

    def call(self,slot,args):
        if (slot,args) not in ((100,[]),(101,[2]),(101,[1]),(102,[])):
            raise ValueError("call is not one of the evidenced initialization/title/menu/shutdown operations")
        before=self.registers()
        self.emit("call_enter",slot=slot,args=args,registers=before)
        for value in reversed(args): self.push(value)
        self.push(BIOS_SEGMENT); self.push(CALL_RETURN)
        self.set(cs=LOAD_SEGMENT,ip=self.slots[slot])
        self.execute(CALL_RETURN)
        self.set(sp=(self.get("sp")+2*len(args))&0xffff)
        if self.get("ss")!=before["ss"] or self.get("sp")!=before["sp"]:
            raise ValueError("driver far call did not balance its caller stack")
        self.emit("call_return",slot=slot,registers=self.registers())

    def irq(self):
        self.pit_cycles+=self.pit0_divisor or 65536
        self.irq_index+=1
        self.set(ip=IRQ_RETURN)
        before=self.registers()
        if not before["eflags"]&0x200:
            self.emit("irq_deferred",registers=before); return False
        off,seg=struct.unpack("<HH",self.machine.mem_read(0x20,4))
        self.emit("irq_enter",target_segment=seg,target_offset=off,registers=before)
        self.push(before["eflags"]); self.push(before["cs"]); self.push(IRQ_RETURN)
        self.set(cs=seg,ip=off,eflags=before["eflags"]&~0x300)
        self.execute(IRQ_RETURN)
        after=self.registers()
        mismatches=[name for name in REGISTERS if name!="eflags" and before[name]!=after[name]]
        if (before["eflags"]^after["eflags"])&FLAGS_MASK: mismatches.append("eflags")
        self.emit("irq_return",registers=after,restored=not mismatches,mismatches=mismatches)
        if mismatches: raise ValueError("original IRQ failed to restore registers: "+str(mismatches))
        return True

    def snapshot(self,out,action):
        stem=f"op-{self.op_index:03}"
        data=bytes(self.machine.mem_read(0,0x100000))
        memory_file=stem+".memory.bin"; driver_file=stem+".driver.bin"; stack_file=stem+".stack.bin"
        (out/memory_file).write_bytes(data)
        driver=data[LOAD_SEGMENT*16:LOAD_SEGMENT*16+DRIVER_SPAN]
        stack=data[0x8f000:0x90000]
        (out/driver_file).write_bytes(driver); (out/stack_file).write_bytes(stack)
        off,seg=struct.unpack_from("<HH",data,0x20)
        registers=self.registers()
        state=dict(index=self.op_index,action=action,registers=registers,flags_defined=registers["eflags"]&FLAGS_MASK,
                   ivt8=dict(offset=off,segment=seg),bios_ticks=struct.unpack_from("<I",data,0x46c)[0],
                   pit_cycles=self.pit_cycles,pit0_divisor=self.pit0_divisor,pit0_effective_divisor=self.pit0_divisor or 65536,
                   port61=self.port61,memory_file=memory_file,driver_file=driver_file,stack_file=stack_file,
                   memory_sha256=hashlib.sha256(data).hexdigest(),driver_sha256=hashlib.sha256(driver).hexdigest(),
                   stack_sha256=hashlib.sha256(stack).hexdigest(),instructions=self.instructions)
        self.operations.append(state)

    def run(self,out,actions):
        out.mkdir(parents=True,exist_ok=True)
        (out/"initial.memory.bin").write_bytes(bytes(self.machine.mem_read(0,0x100000)))
        (out/"actions.json").write_text(json.dumps(actions,indent=2)+"\n")
        for index,action in enumerate(actions):
            self.op_index=index
            if action.get("op")=="call": self.call(action["slot"],action["args"])
            elif action.get("op")=="irq" and type(action.get("count")) is int and 0<action["count"]<=10000:
                for _ in range(action["count"]): self.irq()
            else: raise ValueError("unsupported sound fixture action")
            self.snapshot(out,action)
        fixture=dict(driver="ISOUND.CA",load_segment=LOAD_SEGMENT,driver_span=DRIVER_SPAN,
                     driver_sha256=hashlib.sha256((self.game/"ISOUND.CA").read_bytes()).hexdigest(),
                     load_image_sha256=hashlib.sha256(self.executable.image).hexdigest(),
                     initial_registers=INITIAL_REGISTERS,flags_mask=FLAGS_MASK,initial_port61=0x20,
                     initial_flags_reason="Explicit 0x7202 fixture matches pinned Darklands+ get_flags fixed 0x7002 bits; all injected stack bytes remain compared exactly, not normalized.",
                     bios_segment=BIOS_SEGMENT,bios_offset=BIOS_OFFSET,bios_bytes=BIOS_BYTES.hex(),
                     call_return=CALL_RETURN,irq_return=IRQ_RETURN,clock_hz=1193182,
                     schedule="Advance integer PIT cycles by the current channel0 divisor at each explicitly requested IRQ; no asynchronous delivery during calls")
        report=dict(schema=1,fixture=fixture,actions=actions,evidence=call_evidence(self.game),
                    operations=self.operations,events=self.events,
                    limits=["Original driver bytes, synthetic caller/BIOS/port61/IRQ schedule; not full title playback or real hardware timing.",
                            "CLI/STI/IRET and IVT writes are original-only instruction observations unless the native harness independently exposes them."])
        (out/"report.json").write_text(json.dumps(report,indent=2)+"\n")
        return report


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game",type=Path,default=ROOT/"game/Covert Action/Covert")
    parser.add_argument("--out",type=Path,default=ROOT/"build/sound-oracle/ibm")
    parser.add_argument("--actions",type=Path)
    parser.add_argument("--observe-title",action="store_true",help="Also observe the first real222 startup/title play call without scheduling timer IRQs")
    args=parser.parse_args()
    if ROOT/"build" not in args.out.resolve().parents: parser.error("output must be below COVERT/build")
    temporary=ROOT/"build/tmp"; temporary.mkdir(exist_ok=True)
    os.environ["TMP"]=os.environ["TEMP"]=str(temporary)
    actions=json.loads(args.actions.read_text()) if args.actions else DEFAULT_ACTIONS
    report=SoundProbe(args.game).run(args.out,actions)
    if args.observe_title:
        observe_title_call(args.game,args.out)
    print(json.dumps(dict(operations=len(report["operations"]),events=len(report["events"]),
                          instructions=report["operations"][-1]["instructions"],out=str(args.out))))


def observe_title_call(game,out):
    """Original COVERT/INTRO/FINAL prefix; only observe the first sound call.

    No original sound command is patched or forced. This prefix has no timer
    injection, so it establishes caller arguments, not sound timing fidelity.
    """
    from trace_final import FinalProbe

    class CallerProbe(FinalProbe):
        def __init__(self):
            self.sound_segment=None; self.sound_port61=0x20; self.sound_calls=[]
            super().__init__(game,"222 ",40_000_000,discovery=False,artifacts="validation")

        def emit(self,kind,**fields):
            super().emit(kind,**fields)
            if kind=="dos_overlay" and fields["filename"].upper()=="ISOUND.CA":
                self.sound_segment=int(fields["load_segment"],16)

        def port_read(self,machine,port,size,context):
            if (port,size)==(0x61,1):
                self.emit("port_read",port=port,size=size,value=self.sound_port61)
                return self.sound_port61
            return super().port_read(machine,port,size,context)

        def port_write(self,machine,port,size,value,context):
            if size==1 and port in (0x20,0x40,0x42,0x43,0x61):
                self.emit("port_write",port=port,size=size,value=value)
                if port==0x61: self.sound_port61=value
                return
            return super().port_write(machine,port,size,value,context)

        def instruction(self,machine,address,size,context):
            super().instruction(machine,address,size,context)
            if self.get("cs")!=self.sound_segment or self.get("ip") not in (0xa6,0xca,0xbc): return
            sp=self.get("sp"); stack=bytes(machine.mem_read(self.get("ss")*16+sp,16))
            row=dict(module=self.current["name"],entry=self.get("ip"),registers={n:self.get(n) for n in REGISTERS},
                     stack_hex=stack.hex(),return_offset=int.from_bytes(stack[:2],"little"),
                     return_segment=int.from_bytes(stack[2:4],"little"),
                     args=[] if self.get("ip")!=0xca else [int.from_bytes(stack[4:6],"little")])
            self.sound_calls.append(row)
            if self.get("ip")==0xca: self.stop("observed first actual ISOUND play call before execution")

    probe=CallerProbe(); report=probe.run()
    result=dict(schema=1,stop_reason=report["stop_reason"],instructions=report["instructions"],
                setup_keys="222 ",sound_calls=probe.sound_calls,
                limit="Original startup and title prefix with synthetic DOS/BIOS and zero timer IRQs; observes call arguments only")
    (out/"title-call.json").write_text(json.dumps(result,indent=2)+"\n")
    if not probe.sound_calls or probe.sound_calls[-1]["args"]!=[2]:
        raise ValueError("ordinary startup did not reach the byte-evidenced title command2: "+str(result))


if __name__=="__main__": main()
