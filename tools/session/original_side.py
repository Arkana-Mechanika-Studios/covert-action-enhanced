"""The original side of the session proof: one part of the original game run whole in a minimal DOS machine.

The part's own program (here CODE.EXE) runs from its first instruction to its exit in Unicorn, with the real
drivers the launcher had loaded (MGRAPHIC.EXE draws into the mode-13h screen at A000:0000, MISC.EXE reads
the keyboard through the BIOS, the sound driver is called). Nothing of the program or the drivers is
replaced. Around it is the smallest machine that program needs, each service written out below:

* the start image: a 1 MiB memory image of tools/oracle's captured states (`base_start.mem`: the moment DOS
  has loaded the part, the drivers resident, the hand-off block written by the menus part); the session's
  hand-off block is written over the one in it;
* the keyboard (INT 16h, and DOS's INT 21h 01/06/07/08/0Bh): the key script, one entry per poll (below);
* the clock: the C library's `time` is the boundary (ARCHITECTURE.md section 4, `IGameTimer.Time`): it is not
  run but answered with the session's time of day, as the oracle's library cut does; the display's retrace
  bit (port 3DAh) alternates, so each frame wait passes;
* the colours: the VGA's colour registers (ports 3C7h/3C8h/3C9h, INT 10h AX=1010h/1012h/1015h/1017h);
* DOS memory, vectors, files (read from the player's game folder, never written: what the part writes is kept in
  memory and recorded) and the mouse (absent);
* MicroProse's floppy search `find_file_on_disk` is a boundary (`IFiles.FindFileOnDisk`): answered "found";
* the disk's refusals (spec `dos`, item 14): game files missing from the player's folder (`missing`: not found by
  open, "does it exist" or `find_file_on_disk`) and files that cannot be made or opened to write (`cannot_create`:
  DOS error 5, access denied, as for a read-only file).

The key script, the same entries fed to the C# side: a key word is taken by the next poll or read; an entry
None is one poll that finds no key. A poll that finds the keyboard empty takes the next entry; a key it finds
stays waiting (in the BIOS buffer) until read, and a read waits through None entries for a key. When the
script is used up the run stops ("script exhausted"), as the C# host's window is closed at that moment.

Every time the program takes an entry, a checkpoint is recorded (session_store.RecordingWriter).

The design is csharp/docs/SESSION-PROOF.md; it follows the Darklands Restoration Project's lockstep tester
(the Darklands Restoration Project's tools/oracle: orig_side.py, vis10.py, recording.py).

    python -B tools/session/original_side.py tools/session/sessions/code/*.json
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import session_store as store

sys.path.insert(0, str(store.REPO / "tools" / "oracle"))
sys.path.insert(0, str(store.REPO / "tools"))
import oracle_common as oc  # noqa: E402
import unicorn as uc  # noqa: E402
from unicorn.x86_const import (UC_X86_INS_IN, UC_X86_INS_OUT, UC_X86_REG_AX, UC_X86_REG_BP,  # noqa: E402
                               UC_X86_REG_BX, UC_X86_REG_CS, UC_X86_REG_CX, UC_X86_REG_DI,
                               UC_X86_REG_DS, UC_X86_REG_DX, UC_X86_REG_EFLAGS, UC_X86_REG_ES,
                               UC_X86_REG_IP, UC_X86_REG_SI, UC_X86_REG_SP, UC_X86_REG_SS)

REG = dict(ax=UC_X86_REG_AX, bx=UC_X86_REG_BX, cx=UC_X86_REG_CX, dx=UC_X86_REG_DX, si=UC_X86_REG_SI,
           di=UC_X86_REG_DI, bp=UC_X86_REG_BP, sp=UC_X86_REG_SP, cs=UC_X86_REG_CS, ip=UC_X86_REG_IP,
           ds=UC_X86_REG_DS, es=UC_X86_REG_ES, ss=UC_X86_REG_SS, eflags=UC_X86_REG_EFLAGS)
MEMORY = 0x200000
SCREEN = 0xA0000
CARRY, ZERO = 0x0001, 0x0040
SHARED_POINTER = 0x4F0           # 0000:04F0, where the launcher leaves the hand-off block's far pointer
BIOS_KEYBOARD_FLAGS = 0x417      # 0040:0017, the BIOS keyboard flags (bit 20h: Num Lock)

# The hand-off fields a spec may change, by name: offset, size (the reconstructed state.h).
HANDOFF_FIELDS = {"next_module": (0x00, 1), "level": (0x3A, 2), "flags": (0x24, 2), "skill_bits": (0x4E, 2),
                  "level_copy": (0x50, 2), "gfx_letter": (0x44, 2), "person": (0x08, 1), "value": (0x09, 1),
                  "result": (0x0a, 1), "not_hurt": (0x0d, 1)}
MESSAGE_OFFSET, MESSAGE_SIZE = 0x200, 0x200
# The driver segments the launcher writes into the block (seg_gfx, seg_misc, seg_snd: the reconstructed state.h).
DRIVER_SEGMENTS = slice(0x60, 0x66)
# The VGA's indexed registers (sequencer, graphics controller, CRT controller): index port -> data port. They
# set the display mode only; they are kept so that what the program writes reads back.
INDEXED_REGISTERS = {0x3C4: 0x3C5, 0x3CE: 0x3CF, 0x3D4: 0x3D5}

# The names of the sound driver's "play" thunk slot in the parts' symbol tables.
SOUND_SLOT_NAMES = ("_play_sound", "_sound_driver_play")

# Source files whose change makes a recording stale (their hashes are in meta.json).
ORIGINAL_SIDE_SOURCES = ("tools/session/original_side.py", "tools/session/session_store.py",
                         "tools/oracle/oracle_common.py", "tools/executables.py")


class ServiceNotModelled(Exception):
    """The program asked the machine for something it does not provide: the run stops, nothing is guessed."""


class OriginalSession:
    """One part of the original run whole from a start image, fed by a key script and a time of day."""

    def __init__(self, module, start_folder, handoff, entries, unix_time, game, disk=None, dos=None):
        self.module = module.upper()
        oc.ROOT, oc.GAME, oc.NAMES = store.data_root(), Path(game), store.data_root() / "symbols/names.txt"
        self.info = oc.module_info(self.module)
        self.game = Path(game)
        meta = json.loads((Path(start_folder) / "meta.json").read_text(encoding="utf-8"))
        self.psp, self.load, self.block_end = meta["psp"], meta["load"], meta["block_end"]
        self.image = (Path(start_folder) / "base_start.mem").read_bytes()
        self.entries, self.next_entry = list(entries), 0
        self.unix_time = unix_time
        self.m = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.m.mem_map(0, MEMORY)
        self.m.mem_write(0, self.image)
        off, seg = struct.unpack_from("<HH", self.image, SHARED_POINTER)
        self.shared = seg * 16 + off
        self.m.mem_write(self.shared, bytes(handoff))
        self.handoff_in = bytes(handoff)
        # the machine's own state
        self.dac = bytearray(store.PALETTE_BYTES)
        self.dac_write, self.dac_read, self.dac_phase = 0, 0, 0
        self.retrace_reads = 0
        self.ticks = 0x1233              # the BIOS tick count: the first read gives 1234h
        self.vga_index, self.vga_registers = {}, {}
        self.held_key = None             # the key in the BIOS buffer: taken from the script, not yet read
        self.dos_scan_code = None        # DOS getch's second half of a function key
        self.files, self.dta = {}, 0
        # name -> bytes of the files the part created or opened to write, recorded at the end (as the C# side's
        # write folder holds every file opened to write)
        self.written = {}
        self.disk = dict(disk or {})     # name -> bytes: files on the player's disk besides the game's (spec `disk`)
        dos = dos or {}
        # spec `dos` (item 14): the player's files that are not there, and the files DOS refuses to make or to open to write
        self.missing = {name.upper() for name in dos.get("missing", [])}
        self.cannot_create = {name.upper() for name in dos.get("cannot_create", [])}
        self.written_seen = {}          # name -> SHA-256 of the written files at the previous checkpoint
        self.memory_top = 0x9FFF         # the last paragraph of conventional memory
        self.allocations = {}            # segment -> paragraphs, above the part's own block
        self.calls_since = []            # boundary calls since the last checkpoint
        self.writer = None
        self.stop_reason, self.exit_code, self.pending = None, None, None
        self.instructions = 0

    # ---------------------------------------------------------------- registers
    def get(self, r):
        return self.m.reg_read(REG[r])

    def put(self, **values):
        for r, v in values.items():
            self.m.reg_write(REG[r], v & 0xFFFF if r != "eflags" else v)

    def flag(self, mask, on):
        f = self.get("eflags")
        self.put(eflags=(f | mask) if on else (f & ~mask))

    def word(self, linear):
        return struct.unpack("<H", self.m.mem_read(linear, 2))[0]

    def stop(self, reason, pending=None):
        if self.stop_reason is None:
            self.stop_reason, self.pending = reason, pending
        self.m.emu_stop()

    # ---------------------------------------------------------------- checkpoints
    def take_entry(self, service):
        """The program takes the next script entry: record the checkpoint; None when the script is used up."""
        if self.next_entry >= len(self.entries):
            self.checkpoint(None, service, end=True)
            self.stop("script exhausted")
            return "end"
        entry = self.entries[self.next_entry]
        self.checkpoint(entry, service)
        self.next_entry += 1
        return entry

    def checkpoint(self, entry, service, end=False):
        if self.writer is None:
            return
        # the files written so far, when they changed since the previous checkpoint (a save is compared at the
        # checkpoint after it, not only at the end)
        written = {name: store.sha256(bytes(data)) for name, data in sorted(self.written.items())}
        changed = written if written != self.written_seen else None
        self.written_seen = written
        self.writer.checkpoint(entry="end" if end else entry, service=service,
                               screen=self.m.mem_read(SCREEN, store.SCREEN_BYTES), palette=self.dac,
                               handoff=self.m.mem_read(self.shared, store.SHARED_SIZE),
                               calls=self.calls_since, frames=self.retrace_reads // 2, written=changed)
        self.calls_since = []

    # ---------------------------------------------------------------- the keyboard
    def num_lock(self):
        """Num Lock as the BIOS keeps it: bit 20h of the keyboard flags at 0040:0017 (the raid writes it directly)."""
        return bool(self.m.mem_read(BIOS_KEYBOARD_FLAGS, 1)[0] & 0x20)

    def press(self, entry):
        """The key an entry puts into the BIOS buffer: a keypad key is given by Num Lock at that moment."""
        return store.resolve_keypad(entry, self.num_lock())

    def poll_key(self, service):
        """A look at the keyboard that leaves the key there: the held key, else the next entry (None: no key)."""
        if self.held_key is None:
            entry = self.take_entry(service)
            if entry == "end":
                return None
            self.held_key = self.press(entry)
        return self.held_key

    def read_key(self, service):
        """A read that waits for a key: polls that find no key take the None entries, then the key is taken."""
        while self.held_key is None:
            entry = self.take_entry(service)
            if entry == "end":
                return None
            self.held_key = self.press(entry)
        key, self.held_key = self.held_key, None
        return key

    # ---------------------------------------------------------------- interrupts
    def on_interrupt(self, mu, n, _):
        try:
            self.interrupt(n)
        except ServiceNotModelled as e:
            self.stop(f"not modelled: {e}")

    def interrupt(self, n):
        ax = self.get("ax")
        ah, al = ax >> 8, ax & 0xFF
        ip_linear = self.get("cs") * 16 + self.get("ip")
        software = bytes(self.m.mem_read(ip_linear - 2, 2)) == bytes([0xCD, n])
        if n == 0 and not software:
            self.cpu_exception(n)
        elif n == 0x10:
            self.video(ah, al, ax)
        elif n == 0x16:
            self.bios_keyboard(ah)
        elif n == 0x21:
            self.dos(ah, al)
        elif n == 0x33:
            self.put(ax=0)                 # no mouse driver
        elif n == 0x1A and ah == 0:
            # the PC's tick count (_bios_timeofday): one tick more at each read, from 1234h (the trace tools'
            # fixture). The animations only wait for a span of ticks, so no wall clock is needed; never midnight
            self.ticks += 1
            self.put(ax=0, cx=self.ticks >> 16, dx=self.ticks)
        else:
            raise ServiceNotModelled(f"INT {n:02X}h AX={ax:04X} at {self.get('cs'):04X}:{self.get('ip'):04X}")

    def cpu_exception(self, n):
        """A CPU exception (divide overflow) through the vector the program installed: IRET frame, faulting IP."""
        off, seg = struct.unpack("<HH", self.m.mem_read(n * 4, 4))
        if seg == 0 and off == 0:
            raise ServiceNotModelled(f"CPU exception {n} with no handler")
        sp = (self.get("sp") - 6) & 0xFFFF
        frame = struct.pack("<HHH", self.get("ip"), self.get("cs"), self.get("eflags") & 0xFFFF)
        self.m.mem_write(self.get("ss") * 16 + sp, frame)
        self.put(sp=sp, cs=seg, ip=off, eflags=self.get("eflags") & ~0x0200)

    def video(self, ah, al, ax):
        es_dx = self.get("es") * 16 + self.get("dx")
        bx, cx = self.get("bx"), self.get("cx")
        if ah == 0x00:
            self.m.mem_write(0x449, bytes([al & 0x7F]))
        elif ah == 0x0F:
            self.put(ax=(40 << 8) | self.m.mem_read(0x449, 1)[0], bx=bx & 0xFF)
        elif ah == 0x05:
            self.calls_since.append(["select_display_page", al])
        elif ah == 0x03:
            # the cursor's position and shape of page BH, as the BIOS keeps them (0040:0050 + 2*page, 0040:0060). The
            # raid asks it at its start (TAC 00e8:0018, AX=0305h: meant as the keyboard's repeat rate of INT 16h);
            # only its register buffer receives the answer
            page = bx >> 8 & 7
            self.put(dx=self.word(0x450 + 2 * page), cx=self.word(0x460))
        elif ax == 0x1010:
            self.dac[bx * 3:bx * 3 + 3] = bytes([self.get("dx") >> 8 & 63, cx >> 8 & 63, cx & 63])
        elif ax == 0x1012:
            data = bytes(self.m.mem_read(es_dx, cx * 3))
            self.dac[bx * 3:(bx + cx) * 3] = bytes(b & 63 for b in data)[:max(0, (256 - bx) * 3)]
        elif ax == 0x1015:
            r, g, b = self.dac[bx * 3:bx * 3 + 3]
            self.put(dx=(self.get("dx") & 0xFF) | r << 8, cx=g << 8 | b)
        elif ax == 0x1017:
            self.m.mem_write(es_dx, bytes(self.dac[bx * 3:(bx + cx) * 3]))
        elif ax in (0x1000, 0x1001, 0x1002):
            pass                               # attribute controller (border colour): not part of the picture
        else:
            raise ServiceNotModelled(f"INT 10h AX={ax:04X}")

    def bios_keyboard(self, ah):
        if ah in (0x00, 0x10):
            key = self.read_key(f"int16.{ah:02x}")
            if key is not None:
                self.put(ax=key)
        elif ah in (0x01, 0x11):
            key = self.poll_key(f"int16.{ah:02x}")
            self.flag(ZERO, key is None)
            if key is not None:
                self.put(ax=key)
        elif ah == 0x02:
            self.put(ax=self.get("ax") & 0xFF00)   # no shift key held
        else:
            raise ServiceNotModelled(f"INT 16h AH={ah:02X}")

    def dos(self, ah, al):
        handler = getattr(self, f"dos_{ah:02x}", None)
        if handler is None:
            raise ServiceNotModelled(f"INT 21h AX={self.get('ax'):04X} at {self.get('cs'):04X}:{self.get('ip'):04X}")
        handler(al)

    def ok(self, **values):
        self.flag(CARRY, False)
        self.put(**values)

    def fail(self, code):
        self.flag(CARRY, True)
        self.put(ax=code)

    def dos_30(self, al):        # DOS version: 5.0
        self.ok(ax=5, bx=0, cx=0)

    def dos_25(self, al):        # set an interrupt vector
        self.m.mem_write(al * 4, struct.pack("<HH", self.get("dx"), self.get("ds")))

    def dos_35(self, al):        # get an interrupt vector
        off, seg = struct.unpack("<HH", self.m.mem_read(al * 4, 4))
        self.put(bx=off, es=seg)

    def dos_4a(self, al):        # resize a block: the part's own block may grow up to the first allocation
        seg, size = self.get("es"), self.get("bx")
        limit = min(self.allocations, default=self.memory_top + 1) - seg - 1 if seg == self.psp else None
        if seg != self.psp and seg not in self.allocations:
            return self.fail(9)
        if seg == self.psp:
            if size > limit:
                self.fail(8)
                return self.put(bx=limit)
            self.block_end = seg + size
        else:
            self.allocations[seg] = size
        self.ok()

    def dos_48(self, al):        # allocate: above the part's block and the earlier allocations
        size = self.get("bx")
        start = max([self.block_end] + [s + n for s, n in self.allocations.items()]) + 1
        if start + size > self.memory_top:
            self.fail(8)
            return self.put(bx=max(0, self.memory_top - start))
        self.allocations[start] = size
        self.ok(ax=start)

    def dos_49(self, al):        # free
        if self.allocations.pop(self.get("es"), None) is None:
            return self.fail(9)
        self.ok()

    def dos_44(self, al):        # IOCTL "device information": handles 0..4 are character devices
        if al != 0:
            raise ServiceNotModelled(f"INT 21h AX=44{al:02X}")
        self.ok(dx=0x80D3 if self.get("bx") < 5 else 0)

    def dos_19(self, al):        # current drive: C
        self.put(ax=(self.get("ax") & 0xFF00) | 2)

    def dos_0e(self, al):        # select drive: three drives
        self.put(ax=(self.get("ax") & 0xFF00) | 3)

    def dos_1a(self, al):        # set the disk transfer address
        self.dta = self.get("ds") * 16 + self.get("dx")

    def dos_0b(self, al):        # is a key waiting (kbhit)
        waiting = self.dos_scan_code is not None or self.poll_key("int21.0b") is not None
        self.put(ax=(self.get("ax") & 0xFF00) | (0xFF if waiting else 0))

    def dos_getch(self, service):
        """DOS's character read: the key's character; a function key gives 0, then its scan code."""
        if self.dos_scan_code is not None:
            byte, self.dos_scan_code = self.dos_scan_code, None
        else:
            key = self.read_key(service)
            if key is None:
                return
            byte = key & 0xFF
            if byte == 0:
                self.dos_scan_code = key >> 8
        self.put(ax=(self.get("ax") & 0xFF00) | byte)

    def dos_07(self, al):
        self.dos_getch("int21.07")

    def dos_08(self, al):
        self.dos_getch("int21.08")

    def dos_2a(self, al):
        raise ServiceNotModelled("DOS date: the program's clock is the cut of time()")

    def dos_2c(self, al):
        raise ServiceNotModelled("DOS time: the program's clock is the cut of time()")

    def file_name(self):
        raw = bytes(self.m.mem_read(self.get("ds") * 16 + self.get("dx"), 128)).split(b"\0")[0]
        return raw.decode("latin-1").replace("\\", "/").rsplit("/", 1)[-1].rsplit(":", 1)[-1].upper()

    def existing(self, name):
        """A file's bytes as the program would find them: one it wrote, else the player's; None when there is none."""
        if name in self.written:
            return self.written[name]
        if name in self.disk:
            return self.disk[name]
        if name in self.missing:
            return None
        path = self.game / name
        return path.read_bytes() if path.is_file() else None

    def new_handle(self, name, data):
        handle = next(h for h in range(5, 20) if h not in self.files)
        self.files[handle] = [name, data, 0]
        return handle

    def dos_3d(self, al):        # open: to read, from the player's game folder (or a file the part wrote)
        name = self.file_name()
        self.calls_since.append(["open", name] + (["write"] if al & 3 else []))
        data = self.existing(name)
        if data is None:
            return self.fail(2)
        if al & 3 and name in self.cannot_create:
            return self.fail(5)                # access denied: a read-only file
        if al & 3:
            # to write: the player's folder is never changed; the file becomes a written one (a copy of its bytes)
            self.written[name] = data = bytearray(data)
        self.ok(ax=self.new_handle(name, data))

    def dos_3c(self, al):        # create (or empty) a file: kept in memory, recorded at the end of the run
        name = self.file_name()
        self.calls_since.append(["create", name])
        if name in self.cannot_create:
            return self.fail(5)                # access denied: a read-only file (or a full directory)
        self.written[name] = bytearray()
        self.ok(ax=self.new_handle(name, self.written[name]))

    def dos_43(self, al):        # file attributes: only the question "does it exist" (an archive file)
        if al != 0:
            raise ServiceNotModelled(f"INT 21h AX=43{al:02X} (set attributes)")
        name = self.file_name()
        self.calls_since.append(["exists", name])
        if self.existing(name) is None:
            return self.fail(2)
        self.ok(cx=0x20)

    def dos_3f(self, al):        # read
        handle, count = self.get("bx"), self.get("cx")
        if handle not in self.files:
            return self.fail(6)
        name, data, pos = self.files[handle]
        chunk = bytes(data[pos:pos + count])
        self.m.mem_write(self.get("ds") * 16 + self.get("dx"), chunk)
        self.files[handle][2] = pos + len(chunk)
        self.ok(ax=len(chunk))

    def dos_42(self, al):        # seek
        handle = self.get("bx")
        if handle not in self.files or al > 2:
            return self.fail(6 if handle not in self.files else 1)
        offset = struct.unpack("<i", struct.pack("<HH", self.get("dx"), self.get("cx")))[0]
        pos = (0, self.files[handle][2], len(self.files[handle][1]))[al] + offset
        self.files[handle][2] = pos
        self.ok(ax=pos & 0xFFFF, dx=pos >> 16 & 0xFFFF)

    def dos_3e(self, al):        # close
        if self.files.pop(self.get("bx"), None) is None and self.get("bx") >= 5:
            return self.fail(6)
        self.ok()

    def dos_40(self, al):        # write: to the console devices (nothing shown), or into a written file
        handle, count = self.get("bx"), self.get("cx")
        if handle < 5:
            return self.ok(ax=count)
        if handle not in self.files or not isinstance(self.files[handle][1], bytearray):
            return self.fail(5 if handle in self.files else 6)
        name, data, pos = self.files[handle]
        if count == 0:
            del data[pos:]                     # DOS: a write of no bytes cuts the file at the position
        else:
            if pos > len(data):
                data.extend(bytes(pos - len(data)))
            data[pos:pos + count] = bytes(self.m.mem_read(self.get("ds") * 16 + self.get("dx"), count))
        self.files[handle][2] = pos + count
        self.calls_since.append(["write", name, count])
        self.ok(ax=count)

    def dos_4c(self, al):        # the part ends
        self.exit_code = al
        self.stop("exit")

    # ---------------------------------------------------------------- ports
    def on_in(self, mu, port, size, _):
        if port == 0x3DA:
            # the display's retrace bit: on and off in turn, so every wait for a frame passes
            self.retrace_reads += 1
            return 8 if self.retrace_reads % 2 else 0
        if port == 0x3C9:
            value = self.dac[self.dac_read * 3 + self.dac_phase]
            self.dac_phase += 1
            if self.dac_phase == 3:
                self.dac_phase, self.dac_read = 0, (self.dac_read + 1) & 0xFF
            return value
        if port == 0x201:
            return 0xFF                        # no joystick
        if port in INDEXED_REGISTERS.values():
            index_port = next(i for i, d in INDEXED_REGISTERS.items() if d == port)
            return self.vga_registers.get((port, self.vga_index.get(index_port, 0)), 0)
        self.stop(f"not modelled: IN {port:03X}h")
        return 0

    def on_out(self, mu, port, size, value, _):
        if port == 0x3C8:
            self.dac_write, self.dac_phase = value & 0xFF, 0
        elif port == 0x3C7:
            self.dac_read, self.dac_phase = value & 0xFF, 0
        elif port == 0x3C9:
            self.dac[self.dac_write * 3 + self.dac_phase] = value & 63
            self.dac_phase += 1
            if self.dac_phase == 3:
                self.dac_phase, self.dac_write = 0, (self.dac_write + 1) & 0xFF
        elif port in INDEXED_REGISTERS:
            self.vga_index[port] = value & 0xFF
            if size == 2:                      # an index and its value in one word
                self.vga_registers[(INDEXED_REGISTERS[port], value & 0xFF)] = value >> 8
        elif port in INDEXED_REGISTERS.values():
            index_port = next(i for i, d in INDEXED_REGISTERS.items() if d == port)
            self.vga_registers[(port, self.vga_index.get(index_port, 0))] = value & 0xFF
        elif port in (0x3C0, 0x3D8, 0x3D9, 0x20, 0x21, 0x201):
            # the attribute controller, CGA mode registers, the interrupt controller; the game port's one-shots
            # (a joystick read starts with a write; with no joystick the port then reads FFh)
            pass
        else:
            self.stop(f"not modelled: OUT {port:03X}h,{value:02X}")

    # ---------------------------------------------------------------- boundary cuts
    def code_address(self, name):
        f = next(g for g in self.info["functions"].values() if g["name"] == name)
        return self.load * 16 + f["linear"], f["far"]

    def on_time(self, mu, address, size, far):
        """time(&t): answered with the session's time of day, not run (the C library over DOS's clock)."""
        self.stop("cut", ("time", far))

    def on_find_file(self, mu, address, size, far):
        self.stop("cut", ("find_file_on_disk", far))

    def on_sound(self, mu, address, size, nargs):
        """The part calls the sound driver's entry: its arguments are the words above the far return address."""
        ss_sp = self.get("ss") * 16 + self.get("sp")
        self.calls_since.append(["play_sound"] + [self.word(ss_sp + 4 + 2 * i) for i in range(nargs)])

    def on_pan_load(self, mu, address, size, far):
        """The animation player loads a .PAN file: its name now, whether it loaded (1) or not (0) when it returns.

        Only watched. When the images do not fit in the DOS memory left (D-11: the rewrite's never runs short), the
        original goes on with a half-loaded animation; the comparison needs to know (`pan_load` in the calls).
        """
        ss_sp = self.get("ss") * 16 + self.get("sp")
        ret = (self.word(ss_sp + 2) if far else self.get("cs")) * 16 + self.word(ss_sp)
        raw = bytes(self.m.mem_read(self.get("ds") * 16 + self.word(ss_sp + (4 if far else 2)), 14)).split(b"\0")[0]
        call = ["pan_load", raw.decode("latin-1").upper(), None]
        self.calls_since.append(call)

        def returned(mu, address, size, hook):
            call[2] = 1 if self.get("ax") else 0
            mu.hook_del(hook[0])
        hook = []
        hook.append(mu.hook_add(uc.UC_HOOK_CODE, returned, hook, ret, ret))

    def on_pan_release(self, mu, address, size, far):
        """The animation player gives an animation's memory back (watched only)."""
        self.calls_since.append(["pan_release"])

    def finish_cut(self, name, far):
        ss_sp = self.get("ss") * 16 + self.get("sp")
        ret_ip = self.word(ss_sp)
        ret_cs = self.word(ss_sp + 2) if far else self.get("cs")
        args = ss_sp + (4 if far else 2)
        if name == "time":
            pointer = self.word(args)
            if pointer:
                self.m.mem_write(self.get("ds") * 16 + pointer, struct.pack("<I", self.unix_time))
            self.calls_since.append(["time", self.unix_time])
            self.put(ax=self.unix_time & 0xFFFF, dx=self.unix_time >> 16)
        else:
            raw = bytes(self.m.mem_read(self.get("ds") * 16 + self.word(args), 64)).split(b"\0")[0]
            self.calls_since.append(["find_file_on_disk", raw.decode("latin-1"), self.word(args + 2)])
            # found, unless the spec takes the file away (the disk prompt's test files: final.exe, game.exe, tac.exe, hq.pan)
            self.put(ax=0xFFFF if raw.decode("latin-1").upper() in self.missing else 0)
        self.put(cs=ret_cs, ip=ret_ip, sp=self.get("sp") + (4 if far else 2))

    # ---------------------------------------------------------------- the run
    def run(self, writer, budget=400_000_000):
        """Runs the part from its entry until it exits, the script is used up, or something is not modelled."""
        self.writer = writer
        exe = self.info["exe"]
        L = self.load
        self.put(cs=L + exe.cs, ip=exe.ip, ss=L + exe.ss, sp=exe.sp, ds=self.psp, es=self.psp,
                 ax=0, bx=0, cx=0, dx=0, si=0, di=0, bp=0, eflags=0x0202)
        m = self.m
        m.hook_add(uc.UC_HOOK_INTR, self.on_interrupt)
        m.hook_add(uc.UC_HOOK_INSN, self.on_in, None, 1, 0, UC_X86_INS_IN)
        m.hook_add(uc.UC_HOOK_INSN, self.on_out, None, 1, 0, UC_X86_INS_OUT)
        at, far = self.code_address("_time")
        m.hook_add(uc.UC_HOOK_CODE, self.on_time, far, at, at)
        at, far = self.code_address("_find_file_on_disk")
        m.hook_add(uc.UC_HOOK_CODE, self.on_find_file, far, at, at)
        # the sound driver's "play" entry: CODE and CHASE call it _play_sound, FINAL _sound_driver_play (same slot)
        slot, entry = next((s, v) for s, v in self.info["slots"].items() if v["name"] in SOUND_SLOT_NAMES)
        m.hook_add(uc.UC_HOOK_CODE, self.on_sound, max(1, entry["nargs"]), L * 16 + slot, L * 16 + slot)
        # the animation player's file loads and releases are watched, not changed (the parts that have the player)
        for name, hook in (("_load_pan_file", self.on_pan_load), ("_release_pan_memory", self.on_pan_release)):
            if any(f["name"] == name for f in self.info["functions"].values()):
                at, far = self.code_address(name)
                m.hook_add(uc.UC_HOOK_CODE, hook, far, at, at)
        chunk = 5_000_000
        while self.instructions < budget:
            self.stop_reason = None
            start = self.get("cs") * 16 + self.get("ip")
            try:
                m.emu_start(start, MEMORY, count=chunk)
            except uc.UcError as e:
                self.stop_reason = f"emulator fault: {e} at {self.get('cs'):04X}:{self.get('ip'):04X}"
            self.instructions += chunk if self.stop_reason is None else 0
            if self.stop_reason == "cut":
                self.finish_cut(*self.pending)
                continue
            if self.stop_reason is not None:
                break
        else:
            self.stop_reason = "instruction budget exhausted"
        writer.close()
        return self.stop_reason


def block_of(memory):
    """The hand-off block in a 1 MiB memory image, at the far pointer the launcher leaves at 0000:04F0."""
    off, seg = struct.unpack_from("<HH", memory, SHARED_POINTER)
    return bytes(memory[seg * 16 + off:seg * 16 + off + store.SHARED_SIZE])


def block_source(spec):
    """The block a session starts from when it does not take the start image's (spec `handoff_from`), and its origin.

    * {"recording": "PART/name"}: the block an earlier session's latest original recording left (its handoff-out.bin):
      a session that continues where the part handed over (GAME -> CHASE -> GAME), as the launcher would pass it on;
    * {"memory": path, "sha256": ...}: the block in a 1 MiB memory image of an earlier capture of the original (a path
      under the data root, checked against its SHA-256): a block the original left in a played route.
    None when the spec has neither (the start image's block).
    """
    source = spec.get("handoff_from")
    if source is None:
        return None, None
    if "recording" in source:
        folder = store.latest_recording(source["recording"])
        block = (folder / "handoff-out.bin").read_bytes()
        return block, dict(recording=str(folder), sha256=store.sha256(block))
    path = store.data_root() / source["memory"]
    memory = path.read_bytes()
    if store.sha256(memory) != source["sha256"]:
        raise ValueError(f"{path}: SHA-256 {store.sha256(memory)}, the spec names {source['sha256']}")
    block = block_of(memory)
    return block, dict(memory=str(path), sha256=store.sha256(block))


def disk_files(spec):
    """The files a session finds on the player's disk besides the game's (spec `disk`): name -> bytes, and their origins.

    {"CV0.SVE": {"recording": "GAME/travel-save"}}: the file as an earlier session's latest original recording wrote it
    (its written/ folder), as the player's disk holds it after that session. Read like the player's files; written, it
    becomes a written file.
    """
    files, origins = {}, {}
    for name, source in spec.get("disk", {}).items():
        folder = store.latest_recording(source["recording"])
        data = (folder / store.WRITTEN / name.upper()).read_bytes()
        files[name.upper()] = data
        origins[name.upper()] = dict(recording=str(folder), sha256=store.sha256(data))
    return files, origins


def build_handoff(image, spec, block=None):
    """The hand-off block of the start image (or the one the spec takes from elsewhere) with the spec's changes.

    A block taken from elsewhere keeps the start image's driver segments (0x60..0x65): the launcher writes them into
    the block in every run (COVERT 0000:0078/0095/00b2), and they must name this machine's drivers, not the ones of
    the run the block comes from.
    """
    start_block = block_of(image)
    block = bytearray(block if block is not None else start_block)
    block[DRIVER_SEGMENTS] = start_block[DRIVER_SEGMENTS]
    for name, value in spec.get("handoff", {}).items():
        if name == "message":
            text = value.encode("latin-1")[:MESSAGE_SIZE - 1]
            block[MESSAGE_OFFSET:MESSAGE_OFFSET + MESSAGE_SIZE] = text + bytes(MESSAGE_SIZE - len(text))
            continue
        offset, size = HANDOFF_FIELDS[name]
        block[offset:offset + size] = (value & (1 << 8 * size) - 1).to_bytes(size, "little")
    return bytes(block)


def record(spec_path):
    """Runs one session spec on the original; returns the recording folder."""
    spec = store.load_spec(spec_path)
    part = spec["part"].upper()
    start = store.states_root() / part / spec["start"]
    image = (start / "base_start.mem").read_bytes()
    source_block, block_origin = block_source(spec)
    handoff = build_handoff(image, spec, source_block)
    disk, disk_origins = disk_files(spec)
    entries = store.parse_keys(spec["keys"])
    game = store.game_dir()
    session = OriginalSession(part, start, handoff, entries, spec["unix_time"], game, disk, spec.get("dos"))
    folder = store.new_run_folder(part, spec["name"], "original")
    (folder / "handoff-in.bin").write_bytes(handoff)
    if disk:
        # the files the session found on the player's disk besides the game's: fed alike to the C# side
        (folder / store.DISK).mkdir()
        for name, data in disk.items():
            (folder / store.DISK / name).write_bytes(data)
    (folder / "inputs.json").write_text(json.dumps(dict(keys=entries, unix_time=spec["unix_time"]), indent=1))
    reason = session.run(store.RecordingWriter(folder))
    (folder / "handoff-out.bin").write_bytes(bytes(session.m.mem_read(session.shared, store.SHARED_SIZE)))
    if session.written:
        (folder / store.WRITTEN).mkdir()
        for name, data in session.written.items():
            (folder / store.WRITTEN / name).write_bytes(bytes(data))
    sources = {s: store.sha256((store.REPO / s).read_bytes()) for s in ORIGINAL_SIDE_SOURCES}
    meta = dict(format=store.FORMAT, spec=spec, stop=reason, exit_code=session.exit_code,
                entries_taken=session.next_entry, entries=len(entries), instructions=session.instructions,
                program=store.sha256((game / f"{part}.EXE").read_bytes()),
                start_image=store.sha256(image), sources=sources,
                checkpoints=len(session.writer.lines), pictures=len(session.writer.frames),
                written={name: store.sha256(bytes(data)) for name, data in sorted(session.written.items())},
                calls_after=session.calls_since, handoff_from=block_origin, disk=disk_origins)
    (folder / "meta.json").write_text(json.dumps(meta, indent=1))
    print(f"{spec['name']}: {reason}; {meta['checkpoints']} checkpoints, {meta['pictures']} pictures -> {folder}")
    return folder


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("specs", nargs="+", type=Path, help="session specs (tools/session/sessions/...)")
    for spec in parser.parse_args().specs:
        record(spec)


if __name__ == "__main__":
    main()
