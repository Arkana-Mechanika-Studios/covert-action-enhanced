"""Validated MZ / Microsoft EXEPACK reader. All offsets are load-image relative.

EXEPACK algorithm and dead-zone correction follow CovertActionTools (MIT),
ec5eb257e452eda1ed8054cb530293622021f908. See THIRD_PARTY_NOTICES.md.
"""
from dataclasses import dataclass
from pathlib import Path
import struct


def word(data, offset):
    if offset < 0 or offset + 2 > len(data):
        raise ValueError(f"truncated word at {offset:#x}")
    return struct.unpack_from("<H", data, offset)[0]


@dataclass
class Executable:
    image: bytes
    cs: int
    ip: int
    ss: int
    sp: int
    relocations: list
    packed: bool
    dead_zone: int = 0
    rounding: int = 0

    def mz_bytes(self):
        """Materialize an unpacked analysis EXE; never overwrite the source."""
        header_size = (28 + 4 * len(self.relocations) + 15) & ~15
        total = header_size + len(self.image)
        # Allocate enough extra memory for the original stack, including BSS.
        extra = max(0, (self.ss * 16 + self.sp - len(self.image) + 15) // 16)
        header = bytearray(header_size)
        struct.pack_into("<2s13H", header, 0, b"MZ", total % 512,
                         (total + 511) // 512, len(self.relocations), header_size // 16,
                         extra, 0xffff, self.ss, self.sp, 0, self.ip, self.cs, 28, 0)
        for i, (seg, off) in enumerate(self.relocations):
            struct.pack_into("<HH", header, 28 + i * 4, off, seg)
        return bytes(header) + self.image


def decompress(packed, size):
    """Return exact payload, untouched prefix length and paragraph correction."""
    output = bytearray(size)
    pos, dest = len(packed) - 1, size
    while pos >= 0 and packed[pos] == 0xff:
        pos -= 1
    while True:
        if pos < 2:
            raise ValueError("EXEPACK lacks a final command")
        cmd = packed[pos]
        count = word(packed, pos - 2)
        pos -= 3
        dest -= count
        if dest < 0:
            raise ValueError("EXEPACK output underflow")
        if cmd & 0xfe == 0xb0:
            if pos < 0:
                raise ValueError("EXEPACK truncated fill")
            output[dest:dest + count] = bytes([packed[pos]]) * count
            pos -= 1
        elif cmd & 0xfe == 0xb2:
            if pos + 1 < count:
                raise ValueError("EXEPACK truncated literal")
            output[dest:dest + count] = packed[pos + 1 - count:pos + 1]
            pos -= count
        else:
            raise ValueError(f"unknown EXEPACK command {cmd:#x}")
        if cmd & 1:
            break
    prefix = pos + 1
    correction = dest - prefix
    if not 0 <= correction < 16:
        raise ValueError(f"unexpected EXEPACK paragraph correction {correction}")
    return packed[:prefix] + bytes(output[dest:]), prefix, correction


def read_executable(data):
    if len(data) < 28 or data[:2] != b"MZ":
        raise ValueError("not an MZ executable")
    h = struct.unpack_from("<14H", data)
    size = (h[2] - 1) * 512 + (h[1] or 512)
    header_size = h[4] * 16
    if not 28 <= header_size <= size <= len(data):
        raise ValueError("invalid MZ sizes")
    if h[24 // 2] + h[3] * 4 > header_size:
        raise ValueError("MZ relocation table outside header")
    if size != len(data):
        raise ValueError("trailing overlay data needs a separate loader")
    payload = data[header_size:size]
    seg = h[11] * 16
    packed = h[10] == 0x10 and payload[seg + 14:seg + 16] == b"RB"
    if not packed:
        relocs = [(word(data, h[12] + i * 4 + 2), word(data, h[12] + i * 4))
                  for i in range(h[3])]
        result = Executable(payload, h[11], h[10], h[7], h[8], relocs, False)
    else:
        real_ip, real_cs, _, stub_size, real_sp, real_ss, paras, _ = struct.unpack_from("<8H", payload, seg)
        if stub_size < 16 or seg + stub_size > len(payload):
            raise ValueError("EXEPACK stub outside file")
        stub = payload[seg:seg + stub_size]
        error = b"Packed file is corrupt"
        cursor = stub.find(error)
        if cursor < 16:
            raise ValueError("EXEPACK relocation marker missing")
        cursor += len(error)
        relocs = []
        for block in range(16):
            count = word(stub, cursor)
            cursor += 2
            for _ in range(count):
                off = word(stub, cursor)
                cursor += 2
                # At FFFF the original stub increments ES and subtracts 0x10
                # from DI (FFFF -> FFEF), preserving the word's linear address
                # while avoiding a word spanning a segment offset boundary.
                relocs.append((block * 0x1000 + 1, 0xffef) if off == 0xffff
                              else (block * 0x1000, off))
        image, prefix, correction = decompress(payload[:seg], paras * 16)
        result = Executable(image, real_cs, real_ip, real_ss, real_sp, relocs,
                            True, prefix, correction)
    for s, o in result.relocations:
        if s * 16 + o + 2 > len(result.image):
            raise ValueError("relocation outside load image")
    if result.cs * 16 + result.ip >= len(result.image):
        raise ValueError("entry point outside load image")
    return result


def load(path):
    return read_executable(Path(path).read_bytes())
