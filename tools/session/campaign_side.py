"""The original side of a whole session (csharp/docs/SESSION-PROOF.md, item 7): the launcher COVERT.EXE itself.

`original_side.py` runs one part from a captured start image. This runs the game as the player starts it: COVERT.EXE
from its first instruction, at the DOS prompt of an empty machine, and every program it starts, until it ends. The
launcher's own code runs INTRO.EXE (the set-up screen, in text mode), loads the drivers the player chose (EXEC mode
3: ?GRAPHIC.EXE with FONTS.CV, MISC.EXE, ?SOUND.CA), starts them, and runs the part each hand-off code names (EXEC
mode 0: FINAL, GAME, TAC, ...), clearing Num Lock before each, until a part leaves -1. Nothing of the programs or the
drivers is replaced; around them is OriginalSession's machine (keys, frames, colours, files, the cuts of `time` and
`find_file_on_disk`, the sound requests, the animation files), with what a chain of programs needs besides, each
written out below (never a guess: anything else stops the run with "not modelled: ..."):

* DOS processes: an environment block, a PSP per program (INT 20h at 0, the top of its memory, its parent, the
  environment, the command tail), its image loaded and relocated into the largest free block, EXEC 4B00h (the parent
  waits), 4C00h / INT 20h (the child's memory and files are given back and the parent resumes), 4Dh (the child's exit
  code), 4B03h (an overlay: a driver image loaded and relocated at a segment, no PSP), 51h/62h, 29h, 47h;
* DOS memory: blocks with owners (48h/49h/4Ah) in conventional memory of the "compact" layout
  (tools/memory_layout.py: the environment at 0101h, programs from 01F0h, the top at A000h), one paragraph kept between
  blocks for DOS's arena header;
* the text page of video mode 3 (B800:0000) and the BIOS's text services (INT 10h AH=00/01/02/03/06/07/08/09/0A/0E/0F),
  which the set-up screen prints with; DOS's console output (02h, 06h, 09h, 40h to handles 1 and 2) goes through the
  teletype, and DOS's character input 01h echoes;
* the cuts and watches of the part's functions are placed at every program's load address when it starts and taken
  away when it ends (a later program is loaded over the same memory).

The graphics question of the set-up screen (D-600: the rewrite has one graphics driver and does not ask) is answered
from the spec's `graphics_key` ("2", MCGA): that key is not a script entry and its moment is not a checkpoint; it is
recorded in meta.json (`unasked`). Every other key the programs read is a script entry, as in original_side.py.

A checkpoint carries the program that took the entry (`part`: COVERT, INTRO, FINAL, GAME, ...) and, while the display
is in text mode, the text page (`text`: SHA-256 of the 4000 bytes at B800:0000 and the cursor's row and column; the page
itself in texts/<sha>.txt); each distinct hand-off block is kept whole (blocks/<sha>.bin).

    python -B tools/session/campaign_side.py tools/session/sessions/covert/*.json
"""
import argparse
import json
import re
import struct
import sys
from pathlib import Path

import session_store as store
import original_side as side
from original_side import CARRY, REG, ServiceNotModelled, oc, uc

sys.path.insert(0, str(store.REPO / "tools"))
import executables  # noqa: E402
from memory_layout import resolve_layout  # noqa: E402
from trace_adlib import AdlibHardware  # noqa: E402

LAUNCHER = "COVERT.EXE"
INTRO = "INTRO.EXE"
LAYOUT = resolve_layout("compact")
# The environment every program gets (as tools/trace_startup.py: the game started from C:\COVERT).
ENVIRONMENT = b"PATH=C:\\COVERT\0COMSPEC=C:\\COMMAND.COM\0\0\1\0C:\\COVERT\\COVERT.EXE\0"
TEXT_PAGE = 0xB8000
COLUMNS, ROWS = 80, 25
TEXT_BYTES = COLUMNS * ROWS * 2
BLANK = 0x07                    # light grey on black: what a mode set leaves in every cell
TEXT_MODE, GRAPHICS_MODE = 0x03, 0x13
BIOS_MODE, BIOS_COLUMNS, BIOS_CURSOR, BIOS_CURSOR_SHAPE = 0x449, 0x44A, 0x450, 0x460
# The shared block's driver segments (D-602): written by the launcher once it has loaded the drivers.
DRIVER_SEGMENTS = side.DRIVER_SEGMENTS
# The ROM's IRET that the timer vectors point at until a program installs its own handler (offset, segment).
BIOS_STUB = (0xFF53, 0xF000)
# The machine's speed against the timer's 1.193182 MHz clock: about 10 million instructions a second. Only the music of
# a sound driver runs on INT 8 (the parts count display frames, not ticks), so the speed sets how many instructions a
# driver's wait for its tune to end takes, not anything compared.
INSTRUCTIONS_PER_PIT_CLOCK = 8
# Two stubs in the ROM, so that the device stream knows where a timer tick and a call of the sound driver end (item 8):
# INT 8 is delivered with a frame that returns to TICK_STUB (INT F8h, which records the tick's end, then IRET to the
# interrupted instruction), and a call of the sound slot returns through RETURN_STUB (INT F9h, then a far jump back).
TICK_STUB, TICK_END = (0xFF40, 0xF000), 0xF8
RETURN_STUB, SOUND_RETURN = (0xFF30, 0xF000), 0xF9
# The sound devices' ports (item 8): the AdLib card's OPL2 (index 388h, data 389h; its status at 388h), the MPU-401 of
# the Roland MT-32 (data 330h, command and status 331h), the Tandy's SN76496 (C0h); the PC speaker is the timer's
# channel 2 (43h <- B6h, 42h) and port 61h's gate and data bits.
OPL_INDEX, OPL_DATA = 0x388, 0x389
MPU_DATA, MPU_STATUS, MPU_ACK = 0x330, 0x331, 0xFE
PSG = 0xC0
# The timer's latched count (43h <- 00h, then two reads of 40h), which ASOUND and RSOUND read to calibrate their delays:
# it falls by a fixed number of clocks at each latch, as tools/rsound_vectors.py's fixture (the delays only take
# instructions; nothing compared depends on them)
PIT_FALL = 3000
TEXTS = "texts"
BLOCKS = "blocks"
# Source files whose change makes a whole-session recording stale.
SOURCES = side.ORIGINAL_SIDE_SOURCES + ("tools/session/campaign_side.py", "tools/memory_layout.py")

# The joystick (item 12): the game port at 201h with a stick on it, for sessions whose spec gives one (`stick`). A write
# to the port starts the axis timers; each read afterwards gives bit 0 (X) and bit 1 (Y) set while fewer reads than the
# axis's count have been made since the write (tools/oracle/joystick_vectors.py's port, on which MISC.EXE's routines are
# proven), bits 2 and 3 set (no second joystick) and the buttons in bits 4..7, clear while held. The counts for a
# position are the model of the rewrite's JoystickReading.FromStick (U-12), written out here for the tool: the IBM game
# control adapter's timer (24.2 microseconds plus 0.011 per ohm, a stick of 0 to 100 kilohms) over one turn of MISC's
# timing loop (45 cycles of an 8086 at 4.77 MHz). Without `stick` the port reads FFh as before (no joystick).
GAME_PORT = 0x201
STICK_TOKEN = re.compile(r"\{stick ([^}]*)\}", re.IGNORECASE)
TIMER_BASE_MICROSECONDS, MICROSECONDS_PER_OHM, FULL_RESISTANCE_OHMS = 24.2, 0.011, 100_000
LOOP_TURN_CYCLES, CLOCK_MEGAHERTZ = 45, 14.31818 / 3
FIRST_JOYSTICK_PRESENT_BITS = 0x0C


def stick_count(position):
    """The turns of MISC's timing loop for a stick position from -1 to 1 along an axis (JoystickReading.Count)."""
    ohms = (min(max(position, -1), 1) + 1) / 2 * FULL_RESISTANCE_OHMS
    microseconds = TIMER_BASE_MICROSECONDS + MICROSECONDS_PER_OHM * ohms
    return round(microseconds / (LOOP_TURN_CYCLES / CLOCK_MEGAHERTZ))   # both round half to even


def stick_reading(stick):
    """The port's reading for a stick [x, y, buttons held] (JoystickReading.FromStick): [port bits, X count, Y count]."""
    x, y, held = stick
    return [FIRST_JOYSTICK_PRESENT_BITS | (~held & 0x0F) << 4, stick_count(x), stick_count(y)]


def parse_session_input(script, stick):
    """The entries of a whole session's key script, and the stick at each (None without a joystick).

    `{stick x y buttons}` in the script is not an entry: it puts the stick at x, y (each -1..1) with the buttons held
    (bit 0 the first button) from the next entry on. A stick is set when the game takes the entry, on both sides (the C#
    side sets HeadlessHost.Joystick at EntryTaken), so a stick given with an entry holds from the moment that entry is
    taken until the next is.
    """
    entries, sticks, at = [], [], stick
    pieces = STICK_TOKEN.split(script)
    for i, piece in enumerate(pieces):
        if i % 2:
            if stick is None:
                raise ValueError("{stick} in a session without a stick (the spec's `stick`)")
            values = piece.split()
            at = [float(values[0]), float(values[1]), int(values[2]) if len(values) > 2 else 0]
            continue
        keys = store.parse_keys(piece)
        entries.extend(keys)
        sticks.extend([at] * len(keys))
    return entries, sticks if stick is not None else None


class TextRecordingWriter(store.RecordingWriter):
    """The recording format with the part that took each entry and, in text mode, the text page."""

    def __init__(self, folder):
        super().__init__(folder)
        (self.folder / TEXTS).mkdir(exist_ok=True)
        (self.folder / BLOCKS).mkdir(exist_ok=True)
        self.texts, self.blocks = set(), set()

    def text(self, page):
        digest = store.sha256(page)
        if digest not in self.texts:
            (self.folder / TEXTS / f"{digest}.txt").write_bytes(page)
            self.texts.add(digest)
        return digest

    def checkpoint(self, *, part, text, **fields):
        super().checkpoint(**fields)
        line = self.lines[-1]
        # each distinct hand-off block once (blocks/<sha>.bin): across the parts' boundaries a registered difference of
        # the block (CA-1 after a raid) is told apart from any other by its bytes, not only by its hash
        if line["handoff"] not in self.blocks:
            (self.folder / BLOCKS / f"{line['handoff']}.bin").write_bytes(bytes(fields["handoff"]))
            self.blocks.add(line["handoff"])
        line["part"] = part
        if text is not None:
            line["text"] = self.text(text)


class CampaignSession(side.OriginalSession):
    """The game run whole, from COVERT.EXE's first instruction: every program it starts, fed one key script."""

    def __init__(self, entries, unix_time, game, disk=None, graphics_key=None, part_times=(), sticks=None, stick=None):
        # OriginalSession's machine without a start image: an empty PC at the DOS prompt
        oc.ROOT, oc.GAME, oc.NAMES = store.data_root(), Path(game), store.data_root() / "symbols/names.txt"
        self.game = Path(game)
        self.module = LAUNCHER
        self.entries, self.next_entry = list(entries), 0
        self.unix_time, self.part_times = unix_time, list(part_times)
        self.m = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
        self.m.mem_map(0, side.MEMORY)
        self.dac = bytearray(store.PALETTE_BYTES)
        self.dac_write, self.dac_read, self.dac_phase = 0, 0, 0
        self.retrace_reads = 0
        self.ticks = 0x1233
        self.vga_index, self.vga_registers = {}, {}
        self.held_key, self.dos_scan_code = None, None
        self.files, self.dta = {}, 0
        self.written, self.written_seen = {}, {}
        self.disk = dict(disk or {})
        # a whole session finds every file the game has and may make every file (the part sessions' spec `dos`, item 14)
        self.missing, self.cannot_create = set(), set()
        self.calls_since = []
        self.writer = None
        self.stop_reason, self.exit_code, self.pending = None, None, None
        self.instructions = 0
        # the processes: the running one, and the parents waiting for their EXEC to return (with their registers)
        self.current, self.stack, self.processes = None, [], []
        self.blocks = {}                 # segment -> (paragraphs, owner PSP)
        self.owners = {}                 # file handle -> owner PSP
        self.child_exit = 0
        self.infos = {}
        self.launcher_parts = 0          # the parts the launcher has started (FINAL first): the clock's steps
        self.graphics_key = graphics_key
        self.unasked = []                # the graphics question's answer (D-600), not a script entry
        self.handoff_in, self.driver_segments = None, None
        self.environment = LAYOUT["environment_segment"]
        self.port_b = 0x30              # the keyboard controller's port B (61h): speaker off
        self.pit_divisor, self.pit_low = 0x10000, None   # the timer's channel 0: 18.2 ticks a second
        self.timer_ticks = 0
        # the sound devices (item 8): tools/trace_adlib.py's OPL model (its timers' status follows its own clock, so that
        # the driver's detection of the card sees what it would on the PC), a ready MPU-401, the speaker's divisor
        self.opl = AdlibHardware(present=True)
        self.mpu_pending = []
        self.pit_count, self.pit_latched = 0xFFFF, None
        self.speaker_low = None
        # the joystick (item 12): the stick at each entry (None: no joystick, the port reads FFh), the reading now, and
        # the port reads since the last write (None: the timers were never started)
        self.sticks = sticks
        self.stick_now = stick_reading(stick) if stick is not None else None
        self.port_reads = None
        self.port_reads_total = 0
        self.bios_data()

    # ---------------------------------------------------------------- the machine at the DOS prompt
    def bios_data(self):
        m = self.m
        m.mem_write(self.environment * 16, ENVIRONMENT)
        m.mem_write(0x410, struct.pack("<H", 0x0021))      # equipment: 80-column colour display, a floppy
        m.mem_write(0x413, struct.pack("<H", 640))         # 640 KiB of conventional memory
        m.mem_write(BIOS_MODE, bytes([TEXT_MODE]))
        m.mem_write(BIOS_COLUMNS, struct.pack("<H", COLUMNS))
        m.mem_write(0x44C, struct.pack("<H", 4096))
        m.mem_write(BIOS_CURSOR_SHAPE, struct.pack("<H", 0x0607))
        m.mem_write(0x463, struct.pack("<H", 0x3D4))
        # Num Lock on, as on the PCs of the time (the C#'s KeyboardDriver starts so); the launcher clears it per part
        m.mem_write(side.BIOS_KEYBOARD_FLAGS, b"\x20")
        # the BIOS's timer handlers (INT 8 and the user hook INT 1Ch): an IRET in the ROM; a driver that chains to them
        # returns at once (the BIOS's tick count at 0040:006C is not kept: the programs read ticks through INT 1Ah)
        m.mem_write(BIOS_STUB[1] * 16 + BIOS_STUB[0], b"\xCF")
        m.mem_write(TICK_STUB[1] * 16 + TICK_STUB[0], bytes([0xCD, TICK_END, 0xCF]))
        for vector in (0x08, 0x1C):
            m.mem_write(vector * 4, struct.pack("<HH", *BIOS_STUB))
        self.clear_text()

    @property
    def shared(self):
        """The hand-off block's address: the far pointer at 0000:04F0, which the launcher sets first (0: not yet)."""
        off, seg = struct.unpack("<HH", self.m.mem_read(side.SHARED_POINTER, 4))
        return seg * 16 + off

    def block(self):
        at = self.shared
        return bytes(self.m.mem_read(at, store.SHARED_SIZE)) if at else bytes(store.SHARED_SIZE)

    # ---------------------------------------------------------------- DOS memory
    def free_regions(self):
        cursor = LAYOUT["arena_start"]
        for segment, (size, _) in sorted(self.blocks.items()):
            if segment > cursor:
                yield cursor, segment - cursor
            cursor = max(cursor, segment + size + 1)
        top = LAYOUT["conventional_top"] - 1
        if cursor < top:
            yield cursor, top - cursor

    def largest(self):
        return max((n for _, n in self.free_regions()), default=0)

    def dos_48(self, al):        # allocate: the first free block that is large enough
        size = self.get("bx")
        segment = next((s for s, n in self.free_regions() if n >= size), None)
        if segment is None:
            self.fail(8)
            return self.put(bx=self.largest())
        self.blocks[segment] = (size, self.current["psp"])
        self.ok(ax=segment)

    def dos_49(self, al):        # free
        if self.blocks.pop(self.get("es"), None) is None:
            return self.fail(9)
        self.ok()

    def dos_4a(self, al):        # resize: up to the next block
        seg, size = self.get("es"), self.get("bx")
        if seg not in self.blocks:
            return self.fail(9)
        limit = min((s - 1 for s in self.blocks if s > seg), default=LAYOUT["conventional_top"] - 1) - seg
        if size > limit:
            self.fail(8)
            return self.put(bx=limit)
        self.blocks[seg] = (size, self.blocks[seg][1])
        self.ok()

    # ---------------------------------------------------------------- processes
    def module_info(self, name):
        module = name.upper().rsplit(".", 1)[0]
        if module not in self.infos:
            try:
                self.infos[module] = oc.module_info(module)
            except FileNotFoundError:
                self.infos[module] = None        # a program the analysis does not know: nothing to cut or watch
        return self.infos[module]

    def load_image(self, name, segment):
        """A program's image read from the player's folder, relocated for a load at `segment`, written there."""
        exe = executables.load(self.game / name)
        image = bytearray(exe.image)
        for s, o in exe.relocations:
            at = s * 16 + o
            struct.pack_into("<H", image, at, (struct.unpack_from("<H", image, at)[0] + segment) & 0xFFFF)
        self.m.mem_write(segment * 16, bytes(image))
        self.m.ctl_remove_cache(segment * 16, segment * 16 + len(image))
        return exe, len(image)

    def start_process(self, name, tail=b""):
        """DOS EXEC: the program gets the largest free block, a PSP at its start and its image after it."""
        name = name.upper()
        if not (self.game / name).is_file():
            return False
        segment, size = max(self.free_regions(), key=lambda r: r[1])
        exe = executables.load(self.game / name)
        if max((len(exe.image) + 15) // 16, exe.ss + (exe.sp + 15) // 16) + 16 > size:
            raise ServiceNotModelled(f"EXEC {name}: not enough memory")
        parent = self.current["psp"] if self.current else 0
        self.blocks[segment] = (size, segment)
        self.m.mem_write(segment * 16, bytes(256))
        load = segment + 16
        self.load_image(name, load)
        psp = segment * 16
        self.m.mem_write(psp, b"\xCD\x20")
        self.m.mem_write(psp + 2, struct.pack("<H", segment + size))
        self.m.mem_write(psp + 0x16, struct.pack("<H", parent))
        self.m.mem_write(psp + 0x2C, struct.pack("<H", self.environment))
        self.m.mem_write(psp + 0x80, bytes([len(tail)]) + tail + b"\r")
        self.current = dict(name=name, psp=segment, load=load, hooks=[])
        self.processes.append(dict(name=name, psp=segment, load=load))
        self.put(cs=load + exe.cs, ip=exe.ip, ss=load + exe.ss, sp=exe.sp, ds=segment, es=segment,
                 ax=0, bx=0, cx=0, dx=0, si=0, di=0, bp=0, eflags=0x0202)
        self.place_cuts()
        if name == INTRO and self.handoff_in is None:
            self.handoff_in = self.block()
        if parent and name not in (INTRO,) and self.stack and self.stack[-1][0]["name"] == LAUNCHER:
            if self.driver_segments is None:
                self.driver_segments = self.block()[DRIVER_SEGMENTS]
            # the time of day a part reads (its seed): the spec's for the n-th part the launcher starts, the last one for
            # the parts after; the session's when the spec gives none
            if self.part_times:
                self.unix_time = self.part_times[min(self.launcher_parts, len(self.part_times) - 1)]
            self.launcher_parts += 1
        return True

    def place_cuts(self):
        """The cuts and watches of OriginalSession.run, at the running program's load address."""
        info = self.module_info(self.current["name"])
        if info is None:
            return
        m, L, hooks = self.m, self.current["load"], self.current["hooks"]
        functions = {f["name"]: f for f in info["functions"].values()}
        for name, hook in (("_time", self.on_time), ("_find_file_on_disk", self.on_find_file),
                           ("_load_pan_file", self.on_pan_load), ("_release_pan_memory", self.on_pan_release)):
            if name in functions:
                at = L * 16 + functions[name]["linear"]
                hooks.append(m.hook_add(uc.UC_HOOK_CODE, hook, functions[name]["far"], at, at))
        sound = next(((s, v) for s, v in info["slots"].items() if v["name"] in side.SOUND_SLOT_NAMES), None)
        if sound is not None:
            slot, entry = sound
            hooks.append(m.hook_add(uc.UC_HOOK_CODE, self.on_sound, max(1, entry["nargs"]), L * 16 + slot, L * 16 + slot))

    def end_process(self, code):
        """The running program ends: its memory and files are given back and its parent's EXEC returns."""
        self.child_exit = code
        self.calls_since.append(["exit", self.current["name"]])
        for hook in self.current["hooks"]:
            self.m.hook_del(hook)
        psp = self.current["psp"]
        self.blocks = {s: b for s, b in self.blocks.items() if b[1] != psp}
        for handle in [h for h, owner in self.owners.items() if owner == psp]:
            self.files.pop(handle, None)
            self.owners.pop(handle)
        if not self.stack:
            self.exit_code = code
            return self.stop("exit")
        self.current, registers = self.stack.pop()
        self.put(**registers)
        self.flag(CARRY, False)
        self.m.emu_stop()

    def dos_4b(self, al):        # EXEC
        name = self.file_name()
        if al == 3:
            # an overlay (a driver): the image at the segment of the parameter block, relocated by its factor
            block = self.get("es") * 16 + self.get("bx")
            segment, factor = struct.unpack("<HH", self.m.mem_read(block, 4))
            if not (self.game / name).is_file():
                # a driver the player's folder does not have (Custom's XSOUND.CA, never shipped: D-18): file not found
                self.calls_since.append(["overlay", name, "not found"])
                return self.fail(2)
            exe = executables.load(self.game / name)
            image = bytearray(exe.image)
            for s, o in exe.relocations:
                at = s * 16 + o
                struct.pack_into("<H", image, at, (struct.unpack_from("<H", image, at)[0] + factor) & 0xFFFF)
            self.m.mem_write(segment * 16, bytes(image))
            self.m.ctl_remove_cache(segment * 16, segment * 16 + len(image))
            self.calls_since.append(["overlay", name])
            return self.ok()
        if al != 0:
            raise ServiceNotModelled(f"EXEC mode {al}")
        block = self.get("es") * 16 + self.get("bx")
        _, tail_off, tail_seg = struct.unpack("<3H", self.m.mem_read(block, 6))
        tail_at = tail_seg * 16 + tail_off
        tail = bytes(self.m.mem_read(tail_at + 1, self.m.mem_read(tail_at, 1)[0]))
        registers = {r: self.get(r) for r in REG}
        parent = self.current
        self.calls_since.append(["exec", name])
        if not (self.game / name).is_file():
            return self.fail(2)
        self.stack.append((parent, registers))
        self.start_process(name, tail)
        self.m.emu_stop()

    def dos_4c(self, al):
        self.end_process(al)

    def dos_4d(self, al):        # the child's exit code (normal termination)
        self.ok(ax=self.child_exit & 0xFF)

    def dos_51(self, al):
        self.put(bx=self.current["psp"])

    def dos_62(self, al):
        self.put(bx=self.current["psp"])

    def dos_0b(self, al):
        """Is a key waiting: the launcher's own look (its C library's spawn asks DOS before each EXEC, COVERT
        0000:0EBB) takes no script entry. The rewrite's launcher spawns nothing (D-602) and has no such look; a key
        it would find stays in the buffer for the program it starts, so the answer is the held key, if any."""
        if self.current["name"] == LAUNCHER:
            self.calls_since.append(["launcher_kbhit"])
            waiting = self.dos_scan_code is not None or self.held_key is not None
            return self.put(ax=(self.get("ax") & 0xFF00) | (0xFF if waiting else 0))
        super().dos_0b(al)

    def dos_47(self, al):        # the current directory of a drive: \COVERT
        self.m.mem_write(self.get("ds") * 16 + self.get("si"), b"COVERT\0")
        self.ok()

    def dos_29(self, al):        # parse a file name into an FCB: the launcher's command tails are empty
        source = bytes(self.m.mem_read(self.get("ds") * 16 + self.get("si"), 64)).split(b"\0")[0]
        if source.strip(b" \t\r"):
            raise ServiceNotModelled(f"INT 21h AH=29h with a name ({source!r})")
        self.m.mem_write(self.get("es") * 16 + self.get("di"), b"\0" + b" " * 11)
        self.put(ax=self.get("ax") & 0xFF00)

    def new_handle(self, name, data):
        handle = super().new_handle(name, data)
        self.owners[handle] = self.current["psp"]
        return handle

    # ---------------------------------------------------------------- the console
    def dos_01(self, al):        # read a character and echo it
        self.dos_getch("int21.01")
        if self.dos_scan_code is None:
            self.teletype(self.get("ax") & 0xFF)

    def dos_02(self, al):
        self.teletype(self.get("dx") & 0xFF)

    def dos_06(self, al):
        if self.get("dx") & 0xFF == 0xFF:
            raise ServiceNotModelled("INT 21h AH=06h input")
        self.teletype(self.get("dx") & 0xFF)

    def dos_09(self, al):        # a text up to its "$"
        at = self.get("ds") * 16 + self.get("dx")
        for byte in bytes(self.m.mem_read(at, 4096)).split(b"$")[0]:
            self.teletype(byte)

    def dos_40(self, al):
        handle, count = self.get("bx"), self.get("cx")
        if handle in (1, 2):
            for byte in bytes(self.m.mem_read(self.get("ds") * 16 + self.get("dx"), count)):
                self.teletype(byte)
            return self.ok(ax=count)
        return super().dos_40(al)

    # ---------------------------------------------------------------- video: the text page and the mode set
    def mode(self):
        return self.m.mem_read(BIOS_MODE, 1)[0]

    def cursor(self):
        column, row = self.m.mem_read(BIOS_CURSOR, 2)
        return row, column

    def set_cursor(self, row, column):
        self.m.mem_write(BIOS_CURSOR, bytes([column, row]))

    def cell(self, row, column):
        return TEXT_PAGE + (row * COLUMNS + column) * 2

    def clear_text(self):
        self.m.mem_write(TEXT_PAGE, bytes([0x20, BLANK]) * (COLUMNS * ROWS))
        self.set_cursor(0, 0)

    def scroll(self, lines, attribute, top, left, bottom, right, up=True):
        bottom, right = min(bottom, ROWS - 1), min(right, COLUMNS - 1)
        height = bottom - top + 1
        if lines == 0 or lines >= height:
            lines = height
        rows = list(range(top, bottom + 1))
        source = [bytes(self.m.mem_read(self.cell(r, left), (right - left + 1) * 2)) for r in rows]
        blank = bytes([0x20, attribute]) * (right - left + 1)
        moved = source[lines:] + [blank] * lines if up else [blank] * lines + source[:height - lines]
        for r, data in zip(rows, moved):
            self.m.mem_write(self.cell(r, left), data)

    def teletype(self, byte):
        """The BIOS's teletype in text mode: the character at the cursor (its place keeps its colour), the cursor on."""
        if self.mode() != TEXT_MODE:
            # DOS's console in the graphics mode would draw the BIOS's glyphs: no program of the game does it in play
            self.calls_since.append(["console", byte])
            return
        row, column = self.cursor()
        if byte == 0x0D:
            column = 0
        elif byte == 0x0A:
            row += 1
        elif byte == 0x08:
            column = max(0, column - 1)
        elif byte == 0x07:
            pass
        else:
            self.m.mem_write(self.cell(row, column), bytes([byte]))
            column += 1
            if column == COLUMNS:
                column, row = 0, row + 1
        if row == ROWS:
            attribute = self.m.mem_read(self.cell(ROWS - 1, 0) + 1, 1)[0]
            self.scroll(1, attribute, 0, 0, ROWS - 1, COLUMNS - 1)
            row = ROWS - 1
        self.set_cursor(row, column)

    def video(self, ah, al, ax):
        bx, cx, dx = self.get("bx"), self.get("cx"), self.get("dx")
        if ah == 0x00:
            mode = al & 0x7F
            self.m.mem_write(BIOS_MODE, bytes([mode]))
            if mode == TEXT_MODE:
                self.m.mem_write(BIOS_COLUMNS, struct.pack("<H", COLUMNS))
                if not al & 0x80:
                    self.clear_text()
            elif mode == GRAPHICS_MODE:
                self.m.mem_write(BIOS_COLUMNS, struct.pack("<H", 40))
                if not al & 0x80:
                    self.m.mem_write(side.SCREEN, bytes(store.SCREEN_BYTES))
            else:
                raise ServiceNotModelled(f"INT 10h video mode {mode:02X}h")
            self.calls_since.append(["video_mode", mode])
        elif ah == 0x0F:
            columns = struct.unpack("<H", self.m.mem_read(BIOS_COLUMNS, 2))[0]
            self.put(ax=columns << 8 | self.mode(), bx=bx & 0xFF)
        elif ah == 0x01:
            self.m.mem_write(BIOS_CURSOR_SHAPE, struct.pack("<H", cx))
        elif ah in (0x02, 0x08, 0x09, 0x0A, 0x0E, 0x06, 0x07) and self.mode() != TEXT_MODE:
            raise ServiceNotModelled(f"INT 10h AH={ah:02X}h in video mode {self.mode():02X}h")
        elif ah == 0x02:
            if bx >> 8:
                raise ServiceNotModelled("INT 10h AH=02h on a page other than 0")
            self.set_cursor(dx >> 8, dx & 0xFF)
        elif ah in (0x06, 0x07):
            self.scroll(al, bx >> 8, cx >> 8, cx & 0xFF, dx >> 8, dx & 0xFF, up=ah == 0x06)
        elif ah == 0x08:
            char, attribute = self.m.mem_read(self.cell(*self.cursor()), 2)
            self.put(ax=attribute << 8 | char)
        elif ah in (0x09, 0x0A):
            row, column = self.cursor()
            at = self.cell(row, column)
            for i in range(min(cx, COLUMNS * ROWS - (row * COLUMNS + column))):
                self.m.mem_write(at + 2 * i, bytes([al, bx & 0xFF]) if ah == 0x09 else bytes([al]))
        elif ah == 0x0E:
            self.teletype(al)
        else:
            super().video(ah, al, ax)

    # ---------------------------------------------------------------- the PC speaker (the sound drivers' ports)
    def on_in(self, mu, port, size, _):
        if port == 0x61:
            # port B of the keyboard controller: the speaker's gate and data bits as last written
            return self.port_b
        if port in (OPL_INDEX, OPL_DATA):
            return self.opl.input(port)
        if port == MPU_STATUS:
            # bit 6 clear: ready to receive; bit 7 clear: a byte (an acknowledgement) waits
            return 0x00 if self.mpu_pending else 0x80
        if port == MPU_DATA:
            return self.mpu_pending.pop(0) if self.mpu_pending else 0xFF
        if port == 0x40 and self.pit_latched:
            value, self.pit_latched = self.pit_latched[0], self.pit_latched[1:]
            return value
        if port == GAME_PORT and self.stick_now is not None:
            return self.game_port()
        return super().on_in(mu, port, size, _)

    def game_port(self):
        """A read of the game port with a stick on it: the buttons, and each axis timer's bit while it is still on."""
        bits, *counts = self.stick_now
        for axis, count in enumerate(counts):
            if self.port_reads is not None and self.port_reads < count:
                bits |= 1 << axis
        if self.port_reads is not None:
            self.port_reads += 1
        self.port_reads_total += 1
        return bits

    def on_out(self, mu, port, size, value, _):
        if port == GAME_PORT and self.stick_now is not None:
            # the axis timers start (MISC's measure_joystick, MISC 0000:00c4)
            self.port_reads = 0
            return
        if port == 0x61:
            self.port_b = value & 0xFF
            bits = value & 3
            self.calls_since.append(["speaker", "on" if bits == 3 else "off" if bits == 0 else f"61 {value & 0xFF:02x}"])
        elif port == OPL_INDEX:
            self.opl.output(port, value & 0xFF)
        elif port == OPL_DATA:
            self.opl.output(port, value & 0xFF)
            self.calls_since.append(["opl", self.opl.opl_index, value & 0xFF])
        elif port == MPU_DATA:
            self.calls_since.append(["midi", value & 0xFF])
        elif port == MPU_STATUS:
            # a command to the interface: acknowledged (FEh in the data port)
            self.calls_since.append(["mpu", value & 0xFF])
            self.mpu_pending.append(MPU_ACK)
        elif port == PSG:
            self.calls_since.append(["psg", value & 0xFF])
        elif port == 0x43 and value & 0xFF == 0x00:
            # channel 0's count latched
            self.pit_count = (self.pit_count - PIT_FALL) & 0xFFFF
            self.pit_latched = [self.pit_count & 0xFF, self.pit_count >> 8]
        elif port == 0x43 and value & 0xFF == 0xB6:
            # channel 2 (the speaker's tone) set to a square wave; its divisor follows, low byte first
            self.speaker_low = None
            self.calls_since.append(["speaker", "tone"])
        elif port == 0x43:
            # the timer's mode register: the next byte to channel 0 is the low byte of its divisor
            self.pit_low = None
        elif port == 0x40:
            # channel 0's divisor (low byte, then high byte): the rate of INT 8, which a sound driver raises
            if self.pit_low is None:
                self.pit_low = value & 0xFF
            else:
                self.pit_divisor = (value & 0xFF) << 8 | self.pit_low or 0x10000
                self.pit_low = None
                self.calls_since.append(["timer_rate", self.pit_divisor])
        elif port == 0x42:
            # channel 2: the speaker's tone divisor, low byte then high byte
            if self.speaker_low is None:
                self.speaker_low = value & 0xFF
            else:
                self.calls_since.append(["speaker", f"div {(value & 0xFF) << 8 | self.speaker_low:04x}"])
                self.speaker_low = None
        else:
            super().on_out(mu, port, size, value, _)

    # ---------------------------------------------------------------- the timer interrupt
    def timer_installed(self):
        """Whether a program has put its own handler on INT 8 (a sound driver's music runs on it)."""
        return struct.unpack("<HH", self.m.mem_read(8 * 4, 4)) != BIOS_STUB

    def tick_instructions(self):
        """The instructions between two INT 8: the timer's divisor at the machine's speed (INSTRUCTIONS_PER_PIT_CLOCK)."""
        return max(1000, self.pit_divisor * INSTRUCTIONS_PER_PIT_CLOCK)

    def deliver_timer(self):
        """INT 8 between two instructions, as the interrupt controller would: when interrupts are enabled, the flags,
        CS and IP are pushed and the handler runs with interrupts off."""
        flags = self.get("eflags")
        if not flags & 0x0200:
            return False
        # two frames: the interrupted instruction's, beneath one that returns to TICK_STUB (which records the tick's end
        # and returns to the interrupted instruction with its flags)
        sp = (self.get("sp") - 12) & 0xFFFF
        self.m.mem_write(self.get("ss") * 16 + sp, struct.pack("<6H", TICK_STUB[0], TICK_STUB[1], flags & 0xFFFF,
                                                               self.get("ip"), self.get("cs"), flags & 0xFFFF))
        off, seg = struct.unpack("<HH", self.m.mem_read(8 * 4, 4))
        # the handler starts with the carry clear (U-11): TSOUND's random number adds the carry the interrupted program
        # left, which depends on the instruction the tick falls at, a moment this machine's speed chooses, not the game;
        # the vectors (tools/tsound_vectors.py) and the C# take it clear. The interrupted program gets its own flags back
        self.put(sp=sp, cs=seg, ip=off, eflags=flags & ~0x0301)
        self.timer_ticks += 1
        self.calls_since.append(["tick"])
        return True

    def on_sound(self, mu, address, size, nargs):
        """A call of the sound driver: recorded as OriginalSession's, and made to return through RETURN_STUB, so that the
        device stream knows where the call's own writes end (a tick may fall inside a call: D-19)."""
        super().on_sound(mu, address, size, nargs)
        at = self.get("ss") * 16 + self.get("sp")
        ip, cs = struct.unpack("<HH", self.m.mem_read(at, 4))
        stub = RETURN_STUB[1] * 16 + RETURN_STUB[0]
        self.m.mem_write(stub, bytes([0xCD, SOUND_RETURN, 0xEA]) + struct.pack("<HH", ip, cs))
        self.m.ctl_remove_cache(stub, stub + 7)
        self.m.mem_write(at, struct.pack("<HH", *RETURN_STUB))

    def interrupt(self, n):
        if n == 0x20:
            return self.end_process(0)
        if n == TICK_END and self.get("cs") == TICK_STUB[1]:
            return self.calls_since.append(["tick_end"])
        if n == SOUND_RETURN and self.get("cs") == RETURN_STUB[1]:
            return self.calls_since.append(["sound_return"])
        if n == 0x1A and self.get("ax") >> 8 == 0x81:
            # the Tandy BIOS's sound-chip enable and check (TSOUND's start): answered as a Tandy 1000's, the top four
            # bits of AX clear (the chip is there), as tools/tsound_vectors.py's fixture
            return self.put(ax=0)
        super().interrupt(n)

    # ---------------------------------------------------------------- checkpoints
    def text(self):
        """The text page and the cursor while the display is in text mode; None in the graphics mode."""
        if self.mode() != TEXT_MODE:
            return None
        row, column = self.cursor()
        return bytes(self.m.mem_read(TEXT_PAGE, TEXT_BYTES)) + bytes([row, column])

    def part(self):
        return self.current["name"].rsplit(".", 1)[0]

    def take_entry(self, service):
        if self.current["name"] == INTRO and self.graphics_key is not None and not self.unasked:
            # D-600: the graphics question, which the rewrite does not ask, gets the spec's answer, not a script entry
            text = self.text()
            self.unasked.append(dict(part=self.part(), service=service, key=self.graphics_key,
                                     text=self.writer.text(text) if text is not None else None))
            return self.graphics_key
        entry = super().take_entry(service)
        if entry != "end" and self.sticks is not None:
            # the stick of the entry just taken holds until the next is taken (as HeadlessHost.Joystick on the C# side)
            self.stick_now = stick_reading(self.sticks[self.next_entry - 1])
        return entry

    def checkpoint(self, entry, service, end=False):
        if self.writer is None:
            return
        written = {name: store.sha256(bytes(data)) for name, data in sorted(self.written.items())}
        changed = written if written != self.written_seen else None
        self.written_seen = written
        self.writer.checkpoint(entry="end" if end else entry, service=service,
                               screen=self.m.mem_read(side.SCREEN, store.SCREEN_BYTES), palette=self.dac,
                               handoff=self.block(), calls=self.calls_since, frames=self.retrace_reads // 2,
                               written=changed, part=self.part(), text=self.text())
        self.calls_since = []

    # ---------------------------------------------------------------- the run
    def run(self, writer, budget=4_000_000_000):
        self.writer = writer
        m = self.m
        m.hook_add(uc.UC_HOOK_INTR, self.on_interrupt)
        m.hook_add(uc.UC_HOOK_INSN, self.on_in, None, 1, 0, side.UC_X86_INS_IN)
        m.hook_add(uc.UC_HOOK_INSN, self.on_out, None, 1, 0, side.UC_X86_INS_OUT)
        self.start_process(LAUNCHER)
        while self.instructions < budget:
            self.stop_reason = None
            # with a program's handler on INT 8 the run goes in spans of one timer tick, and the tick is delivered
            # after each span; else in large spans (nothing is delivered)
            timed = self.timer_installed()
            chunk = self.tick_instructions() if timed else 5_000_000
            start = self.get("cs") * 16 + self.get("ip")
            try:
                m.emu_start(start, side.MEMORY, count=chunk)
            except uc.UcError as e:
                self.stop_reason = f"emulator fault: {e} at {self.get('cs'):04X}:{self.get('ip'):04X}"
            self.instructions += chunk if self.stop_reason is None else 0
            if timed and self.stop_reason is None:
                self.deliver_timer()
            if self.stop_reason == "cut":
                self.finish_cut(*self.pending)
                continue
            if self.stop_reason is not None:
                break
        else:
            self.stop_reason = "instruction budget exhausted"
        writer.close()
        return self.stop_reason


def record(spec_path):
    """Runs one whole-session spec on the original; returns the recording folder."""
    spec = store.load_spec(spec_path)
    if spec["part"].upper() != "COVERT":
        raise ValueError(f"{spec_path}: a whole session's part is COVERT")
    disk, disk_origins = side.disk_files(spec)
    stick = spec.get("stick")
    entries, sticks = parse_session_input(spec["keys"], stick)
    graphics = store.parse_keys(spec.get("graphics_key", "2"))
    if len(graphics) != 1:
        raise ValueError(f"{spec_path}: graphics_key is one key")
    game = store.game_dir()
    session = CampaignSession(entries, spec["unix_time"], game, disk, graphics[0], spec.get("part_times", ()), sticks, stick)
    folder = store.new_run_folder("COVERT", spec["name"], "original")
    if disk:
        (folder / store.DISK).mkdir()
        for name, data in disk.items():
            (folder / store.DISK / name).write_bytes(data)
    inputs = dict(keys=entries, unix_time=spec["unix_time"], part_times=spec.get("part_times", []))
    if stick is not None:
        # item 12: the stick at the start and at each entry ([x, y, buttons held]), and the port's reading for each
        # ([port bits, X count, Y count]) as this tool's model gives it; the C# side checks its own model against these
        inputs.update(stick_at_start=stick, joystick_at_start=stick_reading(stick), sticks=sticks,
                      joystick=[stick_reading(s) for s in sticks])
    (folder / "inputs.json").write_text(json.dumps(inputs, indent=1))
    writer = TextRecordingWriter(folder)
    reason = session.run(writer)
    (folder / "handoff-in.bin").write_bytes(session.handoff_in or bytes(store.SHARED_SIZE))
    (folder / "handoff-out.bin").write_bytes(session.block())
    if session.written:
        (folder / store.WRITTEN).mkdir()
        for name, data in session.written.items():
            (folder / store.WRITTEN / name).write_bytes(bytes(data))
    sources = {s: store.sha256((store.REPO / s).read_bytes()) for s in SOURCES}
    programs = sorted({p["name"] for p in session.processes})
    meta = dict(format=store.FORMAT, spec=spec, stop=reason, exit_code=session.exit_code,
                entries_taken=session.next_entry, entries=len(entries), instructions=session.instructions,
                programs={p: store.sha256((game / p).read_bytes()) for p in programs},
                processes=[p["name"] for p in session.processes], sources=sources,
                checkpoints=len(writer.lines), pictures=len(writer.frames), texts=len(writer.texts),
                written={name: store.sha256(bytes(data)) for name, data in sorted(session.written.items())},
                calls_after=session.calls_since, disk=disk_origins, unasked=session.unasked, timer_ticks=session.timer_ticks,
                driver_segments=session.driver_segments.hex() if session.driver_segments is not None else None,
                memory_layout=LAYOUT, device_stream=True,
                game_port_reads=session.port_reads_total if stick is not None else None,
                # the text page as the run ended in text mode (the launcher's last words: "Filename not found", D-18)
                text_at_end=writer.text(session.text()) if session.text() is not None else None)
    (folder / "meta.json").write_text(json.dumps(meta, indent=1))
    print(f"{spec['name']}: {reason}; {meta['checkpoints']} checkpoints, {meta['pictures']} pictures, "
          f"{meta['texts']} text pages, programs {' '.join(p['name'] for p in session.processes)} -> {folder}")
    return folder


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("specs", nargs="+", type=Path, help="whole-session specs (tools/session/sessions/covert/...)")
    for spec in parser.parse_args().specs:
        record(spec)


if __name__ == "__main__":
    main()
