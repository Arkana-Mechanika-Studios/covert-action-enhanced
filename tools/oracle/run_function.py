"""Run ONE original function in Unicorn from a module state and record what a port must reproduce.

    python -B tools/oracle/run_function.py CODE sub_code_0000_2aa2 --args 5 0 3
    ... --state build/oracle/states/CODE/decode-v1 [--capture N] [--keys ua] [--json out.json]

Module-internal code (C, assembly, C library) runs for real. Execution stops at
every boundary and records the crossing as a call {target, args, ret}:

* driver calls: execution leaving the module's DOS memory block (thunk slots in
  frame 0000 jump far into the driver segments). Stub = `retf` with AX/DX from
  the scripted return value.
* INT n (DOS 21h, BIOS 10h/16h/1Ah, mouse 33h, ...): never executed; registers
  come from the scripted input dict.
* port I/O (IN = scripted value, OUT = recorded only).

See tools/oracle/README.md for conventions and the vector format.
"""
import argparse
import base64
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import oracle_common as oc  # noqa: E402
import oracle_config as cfg  # noqa: E402
import calendar  # noqa: E402
import unicorn as uc  # noqa: E402  (build/python-deps first, Unicorn 2.1.4)
from unicorn.x86_const import *  # noqa: E402,F401,F403

REG = {n: globals()["UC_X86_REG_" + n.upper()] for n in
       ("ax", "bx", "cx", "dx", "si", "di", "bp", "sp", "cs", "ip", "ds", "es", "ss", "eflags")}
GENERAL = ("ax", "bx", "cx", "dx", "si", "di")
MEM = 0x200000
KEY = {"ESC": 0x011b, "ENTER": 0x1c0d, "SPACE": 0x3920, "UP": 0x4800, "DOWN": 0x5000, "LEFT": 0x4b00,
       "RIGHT": 0x4d00, "BACKSPACE": 0x0e08, "TAB": 0x0f09, "ALT_Q": 0x1000, "ALT_P": 0x1900,
       "ALT_S": 0x1f00, "ALT_J": 0x2400, "ALT_D": 0x2000, "ALT_X": 0x2d00}
KEY.update({f"F{i}": (0x3a + i) << 8 for i in range(1, 11)})
for _start, _row in ((0x10, "qwertyuiop"), (0x1e, "asdfghjkl"), (0x2c, "zxcvbnm")):
    for _i, _ch in enumerate(_row):
        KEY[_ch] = (_start + _i) << 8 | ord(_ch)
        KEY[_ch.upper()] = (_start + _i) << 8 | ord(_ch.upper())
for _i in range(10):
    KEY[str(_i)] = ((_i or 10) + 1) << 8 | ord(str(_i))


def key_word(k):
    if isinstance(k, int):
        return k & 0xffff
    if k in KEY:
        return KEY[k]
    if len(k) == 1:
        return ord(k)
    raise ValueError(f"unknown key {k!r}")


# ---------------------------------------------------------------- scripted inputs
# The graphics driver's "free memory" entry (MGRAPHIC e50a): INT 21h/48h with BX=FFFF, AX = the BX
# DOS answers (driver vectors: _image_memory_free.json).
IMAGE_MEMORY_FREE = ("_image_memory_free", "sub_final_0000_019f")
# The driver's image store (MGRAPHIC ea02, 092e, 0a5b, 0951, 099c; names as in the vectors' calls):
# begin_image_cuts takes the largest free block, every image made in it gets the next paragraphs (its
# handle = its segment, a different one each call), end_image_cuts gives the rest back.
BEGIN_IMAGE_CUTS = ("_begin_image_cuts", "sub_final_0000_020f", "sub_tac_0000_020f")
END_IMAGE_CUTS = ("_end_image_cuts", "sub_final_0000_01c9", "sub_tac_0000_01c9")
CUT_IMAGE = ("_cut_image",)
BEGIN_IMAGE_BUILD = ("_begin_image_build", "_begin_image")
FINISH_IMAGE_BUILD = ("_finish_image_build", "_finish_image")


class Script:
    """Deterministic provider of boundary return values; records every consumed input.

    With `replay` (a recorded inputs list) values are taken from it in order instead.
    """

    def __init__(self, keys=(), idle_key="ESC", idle_polls=8, clock=(12, 0, 0), ticks=0x1234,
                 replay=None, overrides=None, files=None, memory=None):
        batches, cur = [], []
        for k in keys:
            if k == "WAIT":
                batches.append(cur)
                cur = []
            else:
                cur.append(key_word(k))
        batches.append(cur)
        self.keys = batches[0]
        self.later = batches[1:]     # key batches typed after the program saw an empty buffer
        # one idle key, or a list taken in turn (oracle_config.IDLE_KEYS: ESC, then ENTER for a text field)
        idle = [idle_key] if isinstance(idle_key, str) else list(idle_key or ())
        self.idle_keys = [key_word(k) for k in idle]
        self.idle_count = 0
        self.idle_polls = idle_polls
        self.empty = 0
        self.dos_pending = None
        self.seconds = clock[0] * 3600 + clock[1] * 60 + clock[2]
        self.ticks = ticks
        self.ports = {}
        self.replay = None if replay is None else list(replay)
        self.overrides = overrides or {}
        self.inputs = []
        self.files = files          # {NAME: bytes}; None = the game folder, read-only
        self.written = {}           # NAME -> bytearray: files created or written in this run (memory only)
        self.handles = {}           # handle -> DosFile
        self.last_file = None       # file name of the last library file call (recorded in the call)
        self.names = {}             # replay: handle -> file name, from the recorded open results
        # DOS memory above the program's block: [first free paragraph, end) (run() sets it from the
        # base: block_end + 1 .. A000). None = no free memory (every allocation fails).
        self.memory = memory
        self.blocks = {}            # segment -> paragraphs, allocated in this run
        self.image_pool = None      # [segment, next free paragraph] between begin and end_image_cuts
        self.building = None        # (handle, paragraphs) between begin_ and finish_image_build

    # keyboard model ----------------------------------------------------------
    def _poll(self):
        """True when a key is available; an idle key arrives after idle_polls empty polls.
        A "WAIT" in the key script holds the following keys back until a poll finds the buffer
        empty (so a flush before a menu does not eat them)."""
        if self.keys or self.dos_pending is not None:
            self.empty = 0
            return True
        if self.later:
            self.keys = self.later.pop(0)
            return False
        self.empty += 1
        if self.idle_keys and self.empty >= self.idle_polls:
            self.keys.append(self._idle())
            self.empty = 0
        return False

    def _idle(self):
        k = self.idle_keys[self.idle_count % len(self.idle_keys)]
        self.idle_count += 1
        return k

    def _pop(self):
        self.empty = 0
        if not self.keys and self.later:
            self.keys = self.later.pop(0)
        if not self.keys:
            if not self.idle_keys:
                return 0
            self.keys.append(self._idle())
        return self.keys.pop(0)

    def _take(self, value):
        if self.replay is not None:
            if not self.replay:
                raise ReplayExhausted()
            value = self.replay.pop(0)
        self.inputs.append(value)
        return value

    # boundaries ----------------------------------------------------------------
    def driver(self, name, args):
        if self.replay is not None:
            return self._take(None)
        if name in self.overrides:
            v = self.overrides[name]
            v = v.pop(0) if isinstance(v, list) and v else (v if not isinstance(v, list) else 0)
            return self._take(int(v))
        if name == "_no_key_waiting":
            v = 0 if self._poll() else 1
        elif name == "_read_key_raw":
            v = self._pop()
        elif "*" in self.overrides and self.overrides["*"]:
            pool = self.overrides["*"]          # fuzzing: rotate through these driver results
            v = pool[len(self.inputs) % len(pool)]
        elif name == "_char_width":
            v = 6
        elif name == "_find_file_on_disk":
            v = 0                   # found (CheckForDisk tests != -1)
        elif name in IMAGE_MEMORY_FREE:
            v = self.largest_free()     # the driver's INT 21h/48h BX=FFFF: the largest free block
        elif name in BEGIN_IMAGE_CUTS:
            v = self._begin_image_cuts()
        elif name in END_IMAGE_CUTS:
            v = self._end_image_cuts()
        elif name in CUT_IMAGE and len(args) >= 5:
            v = self._new_image(args[3] * args[4])
        elif name in BEGIN_IMAGE_BUILD and len(args) >= 2:
            v = self._next_image()
            self.building = (v, args[0] * args[1])
        elif name == "_add_image_row":
            v = 1 if self.building and self.building[0] else 0      # the row was stored
        elif name in FINISH_IMAGE_BUILD:
            v = self._new_image(self.building[1]) if self.building else 0
            self.building = None
        else:
            v = 0
        return self._take(v)

    def value(self, v):
        """A boundary result the oracle computes itself (recorded as an input like any other)."""
        return self._take(None if self.replay is not None else v)

    def interrupt(self, n, regs, read=None):
        """`read(offset, n)` reads n bytes at DS:offset (file names and written bytes of INT 21h)."""
        if self.replay is not None:
            return self._take(None)
        ax = regs["ax"]
        ah, al = ax >> 8, ax & 255
        r = {}
        if n == 0x21:
            if ah == 0x0b:
                r = dict(ax=(ah << 8) | (0xff if self._poll() else 0))
            elif ah in (0x01, 0x07, 0x08):
                if self.dos_pending is not None:
                    b, self.dos_pending = self.dos_pending, None
                else:
                    k = self._pop()
                    b = k & 255
                    if b == 0:
                        self.dos_pending = k >> 8
                r = dict(ax=(ah << 8) | b)
            elif ah == 0x30:
                r = dict(ax=0x0005, bx=0, cx=0)
            elif ah == 0x19:
                r = dict(ax=(ah << 8) | 2)
            elif ah == 0x0e:
                r = dict(ax=(ah << 8) | 3)
            elif ah == 0x2a:
                r = dict(ax=(ah << 8) | 1, cx=1990, dx=0x0b05)
            elif ah == 0x2c:
                s = self.seconds
                self.seconds += 1
                r = dict(cx=((s // 3600) % 24) << 8 | (s // 60) % 60, dx=(s % 60) << 8)
            elif ah == 0x11:
                r = dict(ax=(ah << 8) | 0)
            elif ah == 0x29:
                r = dict(ax=(ah << 8) | 0)
            elif ah == 0x35:
                r = dict(bx=0, es=0)
            elif ah == 0x44:
                r = dict(dx=0x80d3 if regs["bx"] < 5 else 0)
            elif ah in (0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x42):
                r = self._dos_file(ah, al, regs, read)
            elif ah == 0x48:
                r = self._allocate(regs["bx"])
            elif ah == 0x49:
                r = self._free(regs.get("es"))
            else:
                r = {}
        elif n == 0x16:
            if ah in (0x00, 0x10):
                r = dict(ax=self._pop())
            elif ah in (0x01, 0x11):
                avail = self._poll()
                r = dict(ax=self.keys[0], zf=0) if avail and self.keys else dict(zf=1)
            elif ah in (0x02, 0x12):
                r = dict(ax=(ah << 8) | 0)
        elif n == 0x1a and ah == 0:
            t = self.ticks
            self.ticks += 1
            r = dict(ax=0, cx=t >> 16, dx=t & 0xffff)
        elif n == 0x33:
            r = dict(ax=0) if ax == 0 else (dict(bx=0, cx=0, dx=0) if ax in (3, 5, 6) else {})
        elif n == 0x10:
            if ah == 0x0f:
                r = dict(ax=(40 << 8) | 0x13, bx=regs["bx"] & 0x00ff)   # BH = page 0, BL unchanged
        return self._take(r)

    # DOS memory: one free region above the program's block, handed out from the bottom ----------
    def _top(self):
        return max([self.memory[0]] + [seg + n for seg, n in self.blocks.items()])

    def largest_free(self):
        """Paragraphs of the largest block DOS could still give (an MCB paragraph comes first)."""
        if not self.memory:
            return 0
        return max(0, self.memory[1] - self._top() - 1)

    def _allocate(self, paragraphs):
        """INT 21h/48h: a block of BX paragraphs (AX = its segment), or CF + 8 and BX = the largest."""
        largest = self.largest_free()
        if not self.memory or paragraphs > largest:
            return dict(ax=8, bx=largest, cf=1)
        seg = self._top() + 1
        self.blocks[seg] = paragraphs
        return dict(ax=seg)

    def _free(self, seg):
        """INT 21h/49h: frees the block at ES; a block from before the run (unknown here) is freed too."""
        self.blocks.pop(seg, None)
        return {}

    # the driver's images: a handle is the segment of the image's pixels ----------------------------
    def _begin_image_cuts(self):
        largest = self.largest_free()
        if self.image_pool is None and largest:
            seg = self._allocate(largest)["ax"]
            self.image_pool = [seg, seg]
        return 1 if self.image_pool else 0

    def _end_image_cuts(self):
        if self.image_pool:
            seg, nxt = self.image_pool
            self.blocks[seg] = nxt - seg
            self.image_pool = None
        return 0

    def _next_image(self):
        if self.image_pool:
            return self.image_pool[1]
        return self._top() + 1 if self.memory else 0

    def _new_image(self, pixels):
        """The handle of a new image of `pixels` bytes (a 16-byte head, then the pixels)."""
        handle = self._next_image()
        size = 1 + (pixels + 15) // 16
        if self.image_pool:
            self.image_pool[1] += size
        elif handle:
            self.blocks[handle] = size          # made outside a cut session: a block of its own
        return handle & 0xffff

    # files: the player's game folder, read-only; writes stay in memory for the run ----------
    @staticmethod
    def _file_name(name):
        return name.replace("\\", "/").rsplit("/", 1)[-1].rsplit(":", 1)[-1].upper()

    def _file(self, name):
        name = self._file_name(name)
        if name in self.written:
            return name, self.written[name]
        if self.files is not None:
            return name, self.files.get(name)
        path = oc.GAME / name
        return name, (path.read_bytes() if path.is_file() else None)

    def _new_handle(self, name, data, *, write, text=False, create=False):
        """DOS gives the lowest free handle from 5 (0-4 are the standard devices). A handle opened for
        writing works on an in-memory copy (self.written); the game folder is never written."""
        if create or write:
            data = self.written[name] = bytearray(b"" if create else data)
        h = next(n for n in range(5, 20) if n not in self.handles)
        self.handles[h] = DosFile(name, data, text=text)
        return h

    def _dos_file(self, ah, al, regs, read):
        """INT 21h 3Ch create, 3Dh open, 3Eh close, 3Fh read, 40h write, 42h seek on the game folder.
        DOS error codes: 1 invalid function, 2 file not found, 4 too many open files, 5 access denied,
        6 invalid handle. "file" (name) and "data" (bytes read, written at DS:DX) go in the call record."""
        bx, cx, dx = regs["bx"], regs["cx"], regs["dx"]
        if ah in (0x3c, 0x3d):
            if read is None:
                return dict(ax=2, cf=1)
            raw = bytes(read(dx, 128)).split(b"\0", 1)[0].decode("cp437", "replace")
            name, data = self._file(raw)
            if ah == 0x3d and data is None:
                return dict(ax=2, cf=1, file=name)
            if len(self.handles) >= 15:
                return dict(ax=4, cf=1, file=name)
            return dict(ax=self._new_handle(name, data, write=ah == 0x3c or al & 3 != 0, create=ah == 0x3c),
                        file=name)
        f = self.handles.get(bx)
        if f is None:
            if bx < 5:
                return dict(ax=cx if ah == 0x40 else 0)    # devices: end of file on read, all written
            return dict(ax=6, cf=1)
        if ah == 0x3e:
            del self.handles[bx]
            return dict(file=f.name)
        if ah == 0x3f:
            chunk = bytes(f.data[f.pos:f.pos + cx])
            f.pos += len(chunk)
            return dict(ax=len(chunk), data=base64.b64encode(chunk).decode(), file=f.name)
        if ah == 0x40:
            if not isinstance(f.data, bytearray):
                return dict(ax=5, cf=1, file=f.name)          # opened read-only: access denied
            f.write(bytes(read(dx, cx)) if cx and read is not None else b"")
            return dict(ax=cx, file=f.name)
        if al > 2:
            return dict(ax=1, cf=1, file=f.name)
        f.pos = ((0, f.pos, len(f.data))[al] + struct.unpack("<i", struct.pack("<HH", dx, cx))[0]) & 0xffffffff
        return dict(ax=f.pos & 0xffff, dx=f.pos >> 16, file=f.name)

    # C library routines cut as one call (oracle_config.LIBRARY_CUTS) ----------------------

    def _replay_file(self, name, args, cstr, ret, out):
        """The file name of a replayed library call, from the recorded handles (self.names)."""
        if name in ("_open", "_dos_open", "_access"):
            fname = self._file_name(cstr(args[0]).decode("cp437", "replace"))
            h = ret if name == "_open" else (struct.unpack("<H", out)[0] if out else None)
            if h is not None and h != 0xffff:
                self.names[h] = fname
            return fname
        if name in ("_read", "_write", "_close", "_lseek"):
            return (self.names.pop if name == "_close" else self.names.get)(args[0], None)
        return None

    def library(self, name, args, cstr):
        """Returns (ret, out bytes or None). `cstr(ptr)` reads a C string of the caller's DGROUP."""
        self.osfile_bits = None
        if self.replay is not None:
            v = self._take(None)
            ret, out = (v["ret"], base64.b64decode(v["out"]) if "out" in v else None) if isinstance(v, dict) \
                else (v, None)
            self.last_file = self._replay_file(name, args, cstr, ret, out)
            if isinstance(v, dict):
                self.osfile_bits = v.get("osfile")
            return ret, out
        ret, out = 0, None
        self.last_file = None
        if name == "_time":
            # 12:00:00 local on 5 Nov 1990 (the INT 21h fixture) in MSC's default TZ (PST8PDT) = 20:00 UTC
            ret = calendar.timegm((1990, 11, 5, 20, 0, 0)) + self.seconds - 12 * 3600
            self.seconds += 1
            if args[0]:
                out = struct.pack("<I", ret)
        elif name in ("_open", "_dos_open", "_access"):
            fname, data = self._file(cstr(args[0]).decode("cp437", "replace"))
            self.last_file = fname
            oflag = args[1]
            if name == "_access":
                ret = 0 if data is not None and not args[1] & 2 else 0xffff
            elif name == "_open":
                # MSC 5.1 open (GAME ed64): O_CREAT 0x100 creates a missing file, O_CREAT|O_EXCL 0x500 fails
                # on an existing one, O_TRUNC 0x200 empties it; text mode unless O_BINARY 0x8000 (_fmode is
                # taken as its default, O_TEXT)
                if (data is None and not oflag & 0x100) or (data is not None and oflag & 0x500 == 0x500) \
                        or len(self.handles) >= 15:
                    ret = 0xffff
                else:
                    ret = self._new_handle(fname, data, write=oflag & 3 != 0, text=not oflag & 0x8000,
                                           create=data is None or oflag & 0x200 != 0)
            elif data is None or args[1] & 3:
                ret = 2
            else:
                h = self._new_handle(fname, data, write=False)
                out = struct.pack("<H", h)
        elif name in ("_read", "_write", "_close", "_lseek"):
            f = self.handles.get(args[0])
            self.last_file = f.name if f else None
            if f is None:
                if name == "_lseek":
                    ret = 0xffffffff
                elif name == "_write" and args[0] < 5:
                    ret = args[2]            # standard devices take everything
                elif name == "_read" and args[0] < 5:
                    ret = 0
                else:
                    ret = 0xffff
            elif name == "_read":
                was_eof = f.eof
                ret, out = f.c_read(args[2])
                if f.text and args[2] and not was_eof:
                    # the library's notes in _osfile[handle] after a text read: 0x04 = the bytes read began
                    # with a line feed, 0x02 = Ctrl-Z seen (GAME ef42..ef67)
                    self.osfile_bits = (4 if f.began_with_lf else 0) | (2 if f.eof else 0)
            elif name == "_write":
                # the cut does not see the caller's bytes: the file length and position are kept
                ret = f.write(b"\0" * args[2]) if isinstance(f.data, bytearray) else 0xffff
            elif name == "_close":
                del self.handles[args[0]]
            else:
                ret = f.c_lseek(struct.unpack("<i", struct.pack("<HH", args[1], args[2]))[0], args[3])
        value = ret if out is None else dict(ret=ret, out=base64.b64encode(out).decode())
        if self.osfile_bits is not None:
            value = dict(ret=ret, osfile=self.osfile_bits) | (
                {} if out is None else dict(out=base64.b64encode(out).decode()))
        self._take(value)
        return ret, out

    def port_in(self, port, size):
        if self.replay is not None:
            return self._take(None)
        if port == 0x3da:
            c = self.ports[port] = self.ports.get(port, 0) + 1
            v = 8 if c % 2 else 0
        elif port == 0x201:
            v = 0xff
        else:
            v = 0
        return self._take(v)


class ReplayExhausted(Exception):
    pass


class DosFile:
    """One open handle: data (bytes = read-only game folder file, bytearray = in-memory copy that takes
    writes), position, and the MSC _osfile flags the C library cuts model (text mode, Ctrl-Z seen)."""

    def __init__(self, name, data, *, text=False):
        self.name, self.data, self.pos, self.text, self.eof = name, data, 0, text, False
        self.began_with_lf = False

    def write(self, b):
        end = self.pos + len(b)
        if end > len(self.data):
            self.data.extend(b"\0" * (end - len(self.data)))
        self.data[self.pos:end] = b
        self.pos = end
        return len(b)

    def c_read(self, n):
        """MSC 5.1 _read (GAME ef08) -> (count, buffer bytes). Binary: the DOS read. Text: the DOS read of
        n bytes, then compacted in place: CR LF -> LF, a lone CR stays, Ctrl-Z stops the copy and sets the
        handle's end-of-file flag (later reads return 0 until an lseek). A CR as the last byte read peeks
        one more byte: end of file -> CR kept; LF with n = 1 -> LF returned; LF -> CR dropped and the LF put
        back (lseek -1); anything else -> CR kept and the byte put back. The bytes after the count keep
        the raw data, as in the real buffer."""
        if n == 0 or self.eof:
            return 0, None
        raw = bytes(self.data[self.pos:self.pos + n])
        self.pos += len(raw)
        if not self.text:
            return len(raw), raw
        self.began_with_lf = bool(raw) and raw[0] == 0x0a
        buf, out, i = bytearray(raw), 0, 0
        while i < len(raw):
            c = raw[i]
            i += 1
            if c == 0x1a:
                self.eof = True
                break
            if c == 0x0d:
                if i < len(raw):
                    if raw[i] == 0x0a:
                        continue                    # CR LF: the LF is copied next
                else:
                    peek = self.data[self.pos:self.pos + 1]
                    if peek and peek[0] == 0x0a:
                        if n == 1:
                            self.pos += 1
                            c = 0x0a
                        else:
                            continue                # CR dropped, the LF comes with the next read
            buf[out] = c
            out += 1
        return out, bytes(buf)

    def c_lseek(self, off, whence):
        """MSC 5.1 lseek (GAME ecea): a negative result or whence > 2 fails (-1, position unchanged);
        success clears the end-of-file flag."""
        if whence > 2 or (0, self.pos, len(self.data))[whence] + off < 0:
            return 0xffffffff
        self.pos = (0, self.pos, len(self.data))[whence] + off
        self.eof = False
        return self.pos


# ---------------------------------------------------------------- module state
class Base:
    """A one-megabyte machine image around one loaded module (PSP, image, DGROUP, shared, drivers)."""

    def __init__(self, module, mem, psp, load, block_end, shared, source):
        self.module = module.upper()
        self.info = oc.module_info(self.module)
        self.mem = bytes(mem)
        self.psp, self.load, self.block_end = psp, load, block_end
        self.dgroup = load + self.info["dgroup_frame"]
        self.dsize = min(0x10000, (block_end - self.dgroup) * 16)
        self.shared = shared            # (seg, off) or None
        self.source = source

    @property
    def shared_linear(self):
        return None if not self.shared or not self.shared[0] else self.shared[0] * 16 + self.shared[1]

    def dgroup_bytes(self, mem=None):
        mem = self.mem if mem is None else mem
        return mem[self.dgroup * 16:self.dgroup * 16 + self.dsize]

    def shared_bytes(self, mem=None):
        mem = self.mem if mem is None else mem
        lin = self.shared_linear
        return None if lin is None else mem[lin:lin + oc.SHARED_SIZE]

    @classmethod
    def from_states(cls, folder, which="wait"):
        folder = Path(folder)
        meta = json.loads((folder / "meta.json").read_text(encoding="utf-8"))
        mem = (folder / f"base_{which}.mem").read_bytes()
        module = folder.parent.name
        off, seg = struct.unpack_from("<HH", mem, 0x4f0)
        return cls(module, mem, meta["psp"], meta["load"], meta["block_end"], (seg, off),
                   f"{folder.name}:{which}")

    @classmethod
    def startup(cls, module, shared=None):
        """Load the module with the existing DOS fixtures (trace_final.FinalProbe) and run its own
        crt0 until main's entry. `shared` (12000 bytes) is placed in a separate DOS block."""
        import trace_final
        info = oc.module_info(module)
        probe = trace_final.FinalProbe(oc.GAME, "", 50_000_000, (), discovery=False,
                                       artifacts="validation", memory_layout="compact")
        seg = probe.allocate((oc.SHARED_SIZE + 15) // 16 + 1, 0xfff0)
        probe.machine.mem_write(seg * 16, bytes(shared or bytes(oc.SHARED_SIZE)))
        probe.machine.mem_write(0x4f0, struct.pack("<HH", 0, seg))
        probe.start_process(module.upper() + ".EXE")
        load = probe.current["load"]
        main = info["functions"][info["main"]]["linear"] + load * 16
        probe.stop_reason = None
        probe.machine.emu_start(probe.get("cs") * 16 + probe.get("ip"), main, count=50_000_000)
        if probe.get("cs") * 16 + probe.get("ip") != main:
            raise RuntimeError(f"{module} crt0 did not reach main ({probe.stop_reason})")
        psp = probe.current["psp"]
        mem = bytes(probe.machine.mem_read(0, 0x100000))
        base = cls(module, mem, psp, load, psp + probe.blocks[psp][0], (seg, 0), "startup")
        base.main_regs = {r: probe.get(r) for r in REG}
        return base


# ---------------------------------------------------------------- the runner
class Result(dict):
    pass


def _runs(byte_map):
    """{linear: byte} -> {"%05x": base64} of contiguous runs."""
    out, start, buf, prev = {}, None, bytearray(), None
    for a in sorted(byte_map):
        if prev is None or a != prev + 1:
            if buf:
                out[f"{start:05x}"] = base64.b64encode(bytes(buf)).decode()
            start, buf = a, bytearray()
        buf.append(byte_map[a])
        prev = a
    if buf:
        out[f"{start:05x}"] = base64.b64encode(bytes(buf)).decode()
    return out


def _pointer_arg(m, ds, value, spec, args):
    """{"ptr": value, "str"|"mem": base64} for a pointer argument (spec from oracle_config)."""
    base = ds * 16
    if spec == "str" or spec == "dollar":
        end = b"$" if spec == "dollar" else b"\0"
        data = bytes(m.mem_read(base + value, min(1024, 0x10000 - value)))
        cut = data.find(end)
        data = data[:cut if cut >= 0 else 1024]
        return {"ptr": value, "str": base64.b64encode(data).decode()}
    _, length = spec
    if isinstance(length, str):
        length = args[int(length[3:])] if length.startswith("arg") else length
    length = max(0, min(int(length), 0x10000 - value))
    return {"ptr": value, "mem": base64.b64encode(bytes(m.mem_read(base + value, length))).decode()}


def _drawn_area(m, dcs, dip, ss, args):
    """The MGRAPHIC image draw (entry 0b15, slot _draw_image_untracked) returns DX:AX = its own
    record of the area drawn: x, y, width, height after clipping to the port (driver 0b4f; all zero when
    there is no image or it lies wholly outside). args = (port, x, y, image segment); port +6/+8 = last
    column/row. Returns (DX:AX, the 8 record bytes). The record offset is the driver's `mov ax, imm` at
    entry+0x30 (07a6), checked against the driver code."""
    code = bytes(m.mem_read(dcs * 16 + dip, 0x36))
    if code[0x30] != 0xb8 or code[0x33:0x35] != bytes((0x8c, 0xca)):
        raise RuntimeError(f"_drawn_area: not the MGRAPHIC image draw at {dcs:04x}:{dip:04x}")
    rec = struct.unpack_from("<H", code, 0x31)[0]
    s16 = lambda v: v - 0x10000 if v & 0x8000 else v     # noqa: E731
    port, x, y, seg = args
    w, h = struct.unpack("<HH", m.mem_read(seg * 16, 4))
    out = (0, 0, 0, 0)
    if seg and w and h:
        top = left = bottom = right = 0
        x, y = s16(x), s16(y)
        last_x, last_y = (s16(v) for v in struct.unpack("<HH", m.mem_read(ss * 16 + port + 6, 4)))
        ok = True
        if y < 0:
            ok, top = -y < h, -y
        if ok:
            ok = last_y >= 0 and y <= last_y
            bottom = max(0, s16((y + h - 1 - last_y) & 0xffff))
        if ok and x < 0:
            ok, left = -x < w, -x
        if ok:
            ok = last_x >= 0 and x <= last_x
            right = max(0, s16((x + w - 1 - last_x) & 0xffff))
        if ok:
            out = (left + x, top + y, w - left - right, h - top - bottom)
    return (dcs << 16) | rec, struct.pack("<4H", *(v & 0xffff for v in out))


def _slot_of_far_call(m, info, L, ret_cs, ret_ip, target_cs, target_ip):
    """Thunk slot whose far pointer an indirect `call far [mem]` used (e.g. CODE 0ad9 calls
    es:[016f] with ES = the module segment), else the slot holding the same far pointer."""
    ret = ret_cs * 16 + ret_ip
    for k in (5, 4, 6, 3, 7):
        ins = list(oc.MD.disasm(bytes(m.mem_read(ret - k, k)), 0))
        if len(ins) == 1 and ins[0].size == k and ins[0].mnemonic == "lcall" and ins[0].operands:
            op = ins[0].operands[-1]
            if op.type == 3:   # memory operand
                d = op.mem.disp & 0xffff
                for slot in (d - 1, d - 3):
                    if slot in info["slots"]:
                        return slot
    hits = []
    for slot, s in info["slots"].items():
        at = L * 16 + slot + (1 if s["how"] == "jmp" else 3)
        if struct.unpack("<HH", m.mem_read(at, 4)) == (target_ip, target_cs):
            hits.append(slot)
    return hits[0] if len(hits) == 1 else None


def _osfile_after(m, base, name, a, ret, read_bits):
    """The C library's table of open files (oracle_config.OSFILE: _osfile[handle]) as the real _open,
    _read, _close and _lseek leave it, since the cut routines do not run (GAME ed64, ef08, ...): open =
    0x01 | 0x80 for text | 0x20 for O_APPEND; close = 0; a successful lseek clears 0x02 (end of file);
    a text read clears 0x04, then sets 0x04 when its bytes began with a line feed and 0x02 at a Ctrl-Z.
    Writes the byte into DGROUP and returns it (None when the table is not touched)."""
    table = cfg.OSFILE.get(base.module)
    if table is None or name not in ("_open", "_read", "_close", "_lseek"):
        return None
    h = ret if name == "_open" else a[0]
    if not 0 <= h < 20 or (name == "_open" and ret == 0xffff):
        return None
    at = base.dgroup * 16 + table + h
    b = m.mem_read(at, 1)[0]
    if name == "_open":
        b = 0x01 | (0 if a[1] & 0x8000 else 0x80) | (0x20 if a[1] & 0x08 else 0)
    elif name == "_close":
        if ret != 0:
            return None
        b = 0
    elif name == "_lseek":
        if ret == 0xffffffff:
            return None
        b &= ~0x02
    else:
        if read_bits is None:
            return None
        b = (b & ~0x04) | read_bits
    m.mem_write(at, bytes([b]))
    return b


def run(base, label, args=(), *, dgroup=None, shared=None, sp=None, regs=None, script=None,
        budget=3_000_000, max_calls=20000, track_reads=False, entry=False, on_machine=None):
    """Run function `label` once. Returns a Result with the vector fields plus diagnostics."""
    info = base.info
    f = info["functions"][label]
    script = script or Script()
    if script.memory is None and script.replay is None:
        script.memory = (base.block_end + 1, 0xa000)
    m = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_16)
    m.mem_map(0, MEM)
    m.mem_write(0, base.mem)
    D = base.dgroup * 16
    dgroup_in = bytes(dgroup if dgroup is not None else base.dgroup_bytes())
    assert len(dgroup_in) == base.dsize
    m.mem_write(D, dgroup_in)
    sh = base.shared_linear
    shared_in = None
    if sh is not None:
        shared_in = bytes(shared if shared is not None else base.shared_bytes())
        m.mem_write(sh, shared_in)
    L = base.load
    code_lo, code_hi = L * 16, base.block_end * 16
    retsize = 6 if f.get("iret") else (4 if f["far"] else 2)
    nargs = len(args)
    get = lambda r: m.reg_read(REG[r])  # noqa: E731

    def put(**kv):
        for k, v in kv.items():
            m.reg_write(REG[k], v & 0xffff if k != "eflags" else v)

    if entry:
        # Program entry (crt0 __astart): DOS register state, no return address.
        exe = info["exe"]
        put(cs=L + exe.cs, ip=exe.ip, ss=L + exe.ss, sp=exe.sp, ds=base.psp, es=base.psp,
            ax=0, bx=0, cx=0, dx=0, si=0, di=0, bp=0, eflags=0x202)
        sentinel = None
        sp_entry = None
    else:
        if sp is None:
            sp = info["stack_top"] - 0x100 - 2 * nargs - retsize
        sp_entry = sp
        sentinel_cs, sentinel_ip = L + (0 if f["far"] else f["frame"]), 0
        sentinel = sentinel_cs * 16 + sentinel_ip
        frame = struct.pack("<H", sentinel_ip) + (struct.pack("<H", sentinel_cs) if f["far"] else b"")
        if f.get("iret"):
            frame += struct.pack("<H", 0x0202)     # FLAGS image popped by IRET
        frame += b"".join(struct.pack("<H", a & 0xffff) for a in args)
        m.mem_write(D + sp_entry, frame)
        put(cs=L + f["frame"], ip=f["offset"], ss=base.dgroup, sp=sp_entry, ds=base.dgroup,
            es=base.dgroup, eflags=0x202)
        for r in GENERAL + ("bp",):
            put(**{r: 0})
    if regs:
        put(**{k: v for k, v in regs.items() if k in REG and k not in ("cs", "ip", "sp", "ss")})
    regs_in = {r: get(r) for r in GENERAL + ("bp", "ds", "es", "ss", "sp")}
    dgroup_in = bytes(m.mem_read(D, base.dsize))      # as the function sees it (argument frame included)

    state = dict(stop=None, last_slot=None, calls=[], exit=None, fault=None)
    far_read, far_written = {}, set()
    coverage = set()
    reads = set()
    writes = set()
    slots = info["slots"]
    slot_lo = min(slots) if slots else 0
    slot_hi = max(slots) + 7 if slots else 0

    def stop(why):
        if state["stop"] is None:
            state["stop"] = why
        m.emu_stop()

    # The sentinel is a non-code address; inside a function body it also needs the caller's SP.
    strict = sentinel is not None and any(
        g["linear"] <= sentinel - L * 16 < g["linear"] + g["size"] for g in info["functions"].values()
        if g["kind"] != "data")

    def on_sentinel(mu, address, size, _):
        if not strict or get("sp") >= sp_entry + retsize:
            stop("return")

    def on_outside(mu, address, size, _):
        stop("boundary")

    def on_slot(mu, address, size, _):
        rel = address - L * 16
        if rel in slots:
            state["last_slot"] = rel

    def on_block(mu, address, size, _):
        coverage.add(address - L * 16)

    def on_intr(mu, n, _):
        ax = get("ax")
        ah = ax >> 8
        regs_now = {r: get(r) for r in GENERAL}
        regs_now["es"] = get("es")      # read by INT 21h/49h; not recorded in the call's args
        target = f"int{n:02x}.{ah:02x}"
        ip_lin = get("cs") * 16 + get("ip")
        software = bytes(mu.mem_read(ip_lin - 2, 2)) == bytes([0xcd, n]) or (
            n == 3 and mu.mem_read(ip_lin - 1, 1)[0] == 0xcc)
        if n in (0, 1, 3, 4, 5, 6, 7, 8, 13) and not software:
            # CPU exception (IP = faulting instruction, 286+ semantics). Delivered through the
            # IVT when the module installed a handler (e.g. CODE 07ac's divide-overflow handler).
            off, seg = struct.unpack("<HH", mu.mem_read(n * 4, 4))
            if code_lo <= seg * 16 + off < D:
                state["exception"] = (n, seg, off)
                stop("exception")
            else:
                state["fault"] = f"cpu exception {n:02x}"
                stop("fault")
            return
        if (n == 0x21 and ah in (0x00, 0x4c, 0x31)) or n == 0x20:
            state["calls"].append(dict(target=target, args=[regs_now[r] for r in GENERAL], ret=0))
            state["exit"] = ax & 255 if n == 0x21 and ah == 0x4c else 0
            stop("exit")
            return
        try:
            r = script.interrupt(n, regs_now, lambda off, k: mu.mem_read(get("ds") * 16 + off, k))
        except ReplayExhausted:
            stop("inputs exhausted")
            return
        for k, v in r.items():
            if k in ("ax", "bx", "cx", "dx", "si", "di", "es"):
                put(**{k: v})
        fl = get("eflags")
        fl = (fl | 1) if r.get("cf") else (fl & ~1)
        if "zf" in r:
            fl = (fl | 0x40) if r["zf"] else (fl & ~0x40)
        put(eflags=fl)
        if "data" in r:
            at, data = get("ds") * 16 + get("dx"), base64.b64decode(r["data"])
            m.mem_write(at, data)
            far_written.update(a for a in range(at, at + len(data)) if not code_lo <= a < D + base.dsize
                               and not (sh is not None and sh <= a < sh + oc.SHARED_SIZE))
        call = dict(target=target, args=[regs_now[k] for k in GENERAL], ret=get("ax"))
        if "file" in r:
            call["file"] = r["file"]
        if n == 0x21 and ah == 0x3f and "data" in r:
            call["out"] = r["data"]
        spec = cfg.INT_POINTERS.get((n, ah))
        if spec is not None:
            length = spec
            if isinstance(spec, tuple) and spec[1] == "cx":
                length = ("mem", regs_now["cx"])
            call["args_mem"] = {"3": _pointer_arg(mu, get("ds"), regs_now["dx"], length, [])["str" if spec in (
                "str", "dollar") else "mem"]}
        state["calls"].append(call)
        if len(state["calls"]) > max_calls:
            stop("call limit")

    def on_in(mu, port, size, _):
        try:
            v = script.port_in(port, size)
        except ReplayExhausted:
            stop("inputs exhausted")
            return 0
        state["calls"].append(dict(target="inb" if size == 1 else "inw", args=[port], ret=v))
        if len(state["calls"]) > max_calls:
            stop("call limit")
        return v

    def on_out(mu, port, size, value, _):
        state["calls"].append(dict(target="outb" if size == 1 else "outw", args=[port, value], ret=0))
        if len(state["calls"]) > max_calls:
            stop("call limit")

    def retf_guard(mu, address):
        """unicorn 2.1.4 loads a wrong IP for RETF / RETF n when its stack words lie under a
        UC_HOOK_MEM_READ hook (README "Emulator workaround"). On the read of the return IP the far
        return is noted and the run stopped; the main loop then puts CS:IP and SP right."""
        ss, sp = get("ss"), get("sp")
        if address != ss * 16 + sp:
            return
        op = bytes(mu.mem_read(get("cs") * 16 + get("ip"), 3))
        if op[0] in (0xca, 0xcb):
            ip, cs = struct.unpack("<HH", mu.mem_read(address, 4))
            extra = struct.unpack_from("<H", op, 1)[0] if op[0] == 0xca else 0
            state["retf"] = (cs, ip, (sp + 4 + extra) & 0xffff)
            stop("retf")

    def on_far_read(mu, access, address, size, value, _):
        retf_guard(mu, address)
        for a in range(address, address + size):
            if a not in far_written and a not in far_read:
                far_read[a] = mu.mem_read(a, 1)[0]

    def on_far_write(mu, access, address, size, value, _):
        far_written.update(range(address, address + size))

    def on_dread(mu, access, address, size, value, _):
        retf_guard(mu, address)
        for a in range(address, address + size):
            if a not in writes:
                reads.add(a)

    def on_dwrite(mu, access, address, size, value, _):
        writes.update(range(address, address + size))

    if sentinel is not None:
        m.hook_add(uc.UC_HOOK_CODE, on_sentinel, None, sentinel, sentinel)
    # C library routines recorded as one call (oracle_config.LIBRARY_CUTS), except the function under test
    # and MicroProse library routines by module and address (oracle_config.MODULE_CUT_AT)
    for cut_label, cut_name in cfg.cut_labels(base.module, info["functions"]).items():
        if cut_label != label:
            g = info["functions"][cut_label]

            def on_cut(mu, address, size, data):
                state["cut"] = data
                stop("library")
            m.hook_add(uc.UC_HOOK_CODE, on_cut, (cut_name, cut_label), L * 16 + g["linear"], L * 16 + g["linear"])
    m.hook_add(uc.UC_HOOK_CODE, on_outside, None, 0, code_lo - 1)
    m.hook_add(uc.UC_HOOK_CODE, on_outside, None, code_hi, MEM - 1)
    if slots:
        m.hook_add(uc.UC_HOOK_CODE, on_slot, None, L * 16 + slot_lo, L * 16 + slot_hi)
    m.hook_add(uc.UC_HOOK_BLOCK, on_block, None, code_lo, D - 1)
    m.hook_add(uc.UC_HOOK_INTR, on_intr)
    m.hook_add(uc.UC_HOOK_INSN, on_in, None, 1, 0, UC_X86_INS_IN)
    m.hook_add(uc.UC_HOOK_INSN, on_out, None, 1, 0, UC_X86_INS_OUT)
    # Far memory = everything except module code, DGROUP and the shared block; the module's own far data
    # segments (oracle_config.FAR_DATA, e.g. the FINAL/GAME animation engine segment) count as far memory.
    known = [(code_lo, D + base.dsize)]
    for a, b in cfg.FAR_DATA.get(base.module, ()):
        lo_, hi_ = L * 16 + a * 16, L * 16 + b * 16
        known = [part for k_lo, k_hi in known
                 for part in ((k_lo, min(k_hi, lo_)), (max(k_lo, hi_), k_hi)) if part[0] < part[1]]
    known = sorted(known + ([(sh, sh + oc.SHARED_SIZE)] if sh is not None else []))
    cursor = 0
    for lo, hi in known + [(MEM, MEM)]:
        if lo > cursor:
            m.hook_add(uc.UC_HOOK_MEM_READ, on_far_read, None, cursor, lo - 1)
            m.hook_add(uc.UC_HOOK_MEM_WRITE, on_far_write, None, cursor, lo - 1)
        cursor = max(cursor, hi)
    if track_reads:
        for lo, hi in ((D, D + info["stack_lo"]), (D + info["stack_top"], D + base.dsize)) + (
                ((sh, sh + oc.SHARED_SIZE),) if sh is not None else ()):
            if hi > lo:
                m.hook_add(uc.UC_HOOK_MEM_READ, on_dread, None, lo, hi - 1)
                m.hook_add(uc.UC_HOOK_MEM_WRITE, on_dwrite, None, lo, hi - 1)

    if on_machine:
        on_machine(m)
    while True:
        state["stop"] = None
        try:
            m.emu_start(get("cs") * 16 + get("ip"), MEM, count=budget)
        except uc.UcError as e:
            state["fault"] = f"unicorn: {e} at {get('cs'):04x}:{get('ip'):04x}"
            state["stop"] = "fault"
            break
        why = state["stop"]
        if why == "boundary":
            sp_now, ss = get("sp"), get("ss")
            ret_ip, ret_cs = struct.unpack("<HH", m.mem_read(ss * 16 + sp_now, 4))
            target_cs, target_ip = get("cs"), get("ip")
            slot = state["last_slot"]
            state["last_slot"] = None
            if slot is not None:
                s = slots[slot]
                name, conv, n = s["name"], s["convention"], s["nargs"]
            else:
                if not code_lo + 5 <= ret_cs * 16 + ret_ip < D:
                    state["fault"] = f"left the module to {target_cs:04x}:{target_ip:04x} without a far return"
                    state["stop"] = "fault"
                    break
                slot = _slot_of_far_call(m, info, L, ret_cs, ret_ip, target_cs, target_ip)
                if slot is not None:
                    name = slots[slot]["name"]
                    # an indirect caller's own clean-up decides between stack and register arguments
                    n = oc.cleanup_words(bytes(m.mem_read(ret_cs * 16 + ret_ip - 5, 16)), 0) or 0
                    conv = "stack" if n else "regs"
                else:
                    name = f"far:{target_cs:04x}:{target_ip:04x}"
                    n = oc.cleanup_words(bytes(m.mem_read(ret_cs * 16 + ret_ip - 5, 16)), 0)
                    conv = "stack" if n else "regs"
                    n = n or 0
                    state.setdefault("unknown_far", 0)
                    state["unknown_far"] += 1
            if conv == "stack":
                a = list(struct.unpack(f"<{n}H", m.mem_read(ss * 16 + sp_now + 4, 2 * n))) if n else []
            else:
                a = [get(r) for r in GENERAL]
            drawn = None
            if (base.module, slot) in cfg.DRAWN_AREA_SLOTS:
                a = list(struct.unpack("<4H", m.mem_read(ss * 16 + sp_now + 4, 8)))
                drawn = _drawn_area(m, target_cs, target_ip, ss, a)
            try:
                v = script.driver(name, a) if drawn is None else script.value(drawn[0])
            except ReplayExhausted:
                state["stop"] = "inputs exhausted"
                break
            shown = list(a)
            if conv == "stack":
                for i, spec in cfg.DRIVER_POINTERS.get(name, {}).items():
                    if i < len(a):
                        shown[i] = _pointer_arg(m, get("ds"), a[i], spec, a)
            call = dict(target=name, args=shown, ret=v)
            if drawn is not None:
                at = (v >> 16) * 16 + (v & 0xffff)
                m.mem_write(at, drawn[1])
                far_written.update(range(at, at + len(drawn[1])))
                call["out"] = base64.b64encode(drawn[1]).decode()
            state["calls"].append(call)
            put(ax=v & 0xffff, dx=(v >> 16) & 0xffff, sp=sp_now + 4, cs=ret_cs, ip=ret_ip)
            if len(state["calls"]) > max_calls:
                state["stop"] = "call limit"
                break
            continue
        if why == "library":
            name, cut_label = state.pop("cut")
            spec = cfg.CUTS[name]
            far = info["functions"][cut_label]["far"]
            sp_now, ss, ds = get("sp"), get("ss"), get("ds")
            ret_ip, ret_cs = struct.unpack("<HH", m.mem_read(ss * 16 + sp_now, 4))
            if not far:
                ret_cs = get("cs")
            size = 4 if far else 2
            n = oc.cleanup_words(bytes(m.mem_read(ret_cs * 16 + ret_ip - (5 if far else 3), 16)), 0) or spec["nargs"]
            a = list(struct.unpack(f"<{n}H", m.mem_read(ss * 16 + sp_now + size, 2 * n))) if n else []

            def cstr(ptr):
                data = bytes(m.mem_read(ds * 16 + ptr, min(260, 0x10000 - ptr)))
                return data.split(b"\0", 1)[0]
            shown = list(a)
            for i, p in spec["ptrs"].items():
                if i < len(a):
                    shown[i] = _pointer_arg(m, ds, a[i], p, a)
            try:
                if name in cfg.MODULE_CUTS:
                    ret, out = script.driver(name, a), None
                else:
                    ret, out = script.library(name, a, cstr)
            except ReplayExhausted:
                state["stop"] = "inputs exhausted"
                break
            call = dict(target=name, args=shown, ret=ret)
            if name in cfg.LIBRARY_CUTS and script.last_file:
                call["file"] = script.last_file
            flags = _osfile_after(m, base, name, a, ret, script.osfile_bits if name == "_read" else None)
            if flags is not None and name == "_read":
                call["osfile"] = flags
            if out is not None:
                at = {"_time": 0, "_dos_open": 2, "_read": 1}[name]
                m.mem_write(ds * 16 + a[at], out)
                call["out"] = base64.b64encode(out).decode()
            state["calls"].append(call)
            if spec["ret"] == "long" or ret > 0xffff:
                put(ax=ret & 0xffff, dx=(ret >> 16) & 0xffff)
            else:
                put(ax=ret & 0xffff)
            put(sp=sp_now + size, ip=ret_ip, cs=ret_cs)
            if len(state["calls"]) > max_calls:
                state["stop"] = "call limit"
                break
            continue
        if why == "retf":
            cs_, ip_, sp_ = state.pop("retf")
            put(cs=cs_, ip=ip_, sp=sp_)
            continue
        if why == "exception":
            n, seg, off = state.pop("exception")
            sp_now = (get("sp") - 6) & 0xffff
            m.mem_write(get("ss") * 16 + sp_now, struct.pack("<HHH", get("ip"), get("cs"), get("eflags") & 0xffff))
            put(sp=sp_now, cs=seg, ip=off, eflags=get("eflags") & ~0x300)
            continue
        if why is None:
            state["stop"] = "budget"
        break

    mem_after = bytes(m.mem_read(0, 0x100000))
    dgroup_out = bytearray(mem_after[D:D + base.dsize])
    mask = None
    if sp_entry is not None:
        lo = min(info["stack_lo"], sp_entry)
        hi = min(base.dsize, sp_entry + retsize + 2 * nargs)
        dgroup_out[lo:hi] = dgroup_in[lo:hi]
        mask = [lo, hi]
    else:   # program entry: the whole stack segment is scratch
        lo, hi = info["stack_lo"], info["stack_top"]
        dgroup_out[lo:hi] = dgroup_in[lo:hi]
        mask = [lo, hi]
    res = Result(
        args=[a & 0xffff for a in args],
        dgroup_in=dgroup_in, shared_in=shared_in,
        inputs=script.inputs,
        result=dict(ax=get("ax"), dx=get("dx")),
        dgroup_out=bytes(dgroup_out),
        shared_out=None if sh is None else mem_after[sh:sh + oc.SHARED_SIZE],
        calls=state["calls"],
        far_in=_runs(far_read),
        far_out=_runs({a: mem_after[a] for a in far_written if a < 0x100000}),
        regs_in=regs_in,
        regs_out={r: get(r) for r in GENERAL + ("bp", "sp", "ds", "es", "ss")} | {"flags": get("eflags") & 0x8d5},
        stack_mask=mask, status=state["stop"], fault=state["fault"], exit=state["exit"],
        unknown_far=state.get("unknown_far", 0), coverage=coverage,
        reads=sorted(reads) if track_reads else None)
    return res


def encode(b):
    return None if b is None else base64.b64encode(b).decode()


def vector_case(res, case_id, *, with_regs):
    """The agreed JSON case object (plus documented optional fields)."""
    c = dict(id=case_id, args=[a - 0x10000 if a >= 0x8000 else a for a in res["args"]],
             dgroup_in=encode(res["dgroup_in"]), shared_in=encode(res["shared_in"]),
             inputs=res["inputs"], result=res["result"], dgroup_out=encode(res["dgroup_out"]),
             shared_out=encode(res["shared_out"]), calls=res["calls"])
    if res["far_in"] or res["far_out"]:
        c["far_in"], c["far_out"] = res["far_in"], res["far_out"]
    c["stack_mask"] = res["stack_mask"]
    if with_regs:
        c["regs_in"], c["regs_out"] = res["regs_in"], res["regs_out"]
    if res["exit"] is not None:
        c["exit"] = res["exit"]
    return c


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("module")
    p.add_argument("label", help="sub_<module>_<frame>_<offset> or a names.txt name")
    p.add_argument("--args", nargs="*", type=lambda s: int(s, 0), default=[])
    p.add_argument("--state", help="capture folder (build/oracle/states/<MODULE>/<route>); default: crt0 startup")
    p.add_argument("--which", default="wait", choices=("wait", "start"))
    p.add_argument("--keys", nargs="*", default=[], help="scripted keys (names from KEY or single chars)")
    p.add_argument("--json", type=Path, help="write the case JSON here")
    a = p.parse_args()
    info = oc.module_info(a.module)
    label = a.label
    if label not in info["functions"]:
        label = next(l for l, f in info["functions"].items() if f["name"] == label)
    base = Base.from_states(a.state, a.which) if a.state else Base.startup(a.module)
    res = run(base, label, a.args, script=Script(keys=a.keys))
    case = vector_case(res, "cli", with_regs=True)
    print(json.dumps(dict(status=res["status"], fault=res["fault"], result=res["result"],
                          calls=res["calls"][:50], inputs=res["inputs"][:50], far_in=list(res["far_in"]),
                          far_out=list(res["far_out"]), exit=res["exit"],
                          dgroup_changed=[f"{i:04x}" for i in range(len(res["dgroup_in"]))
                                          if res["dgroup_in"][i] != res["dgroup_out"][i]][:64]), indent=1))
    if a.json:
        a.json.write_text(json.dumps(case) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
