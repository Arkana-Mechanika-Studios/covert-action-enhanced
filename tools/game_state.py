"""Extract GAME's evidenced save/load state from a one-megabyte snapshot.

The original 00f7:aad4 serializer and 00f7:aebb loader use the same twelve
blocks. A real CVn.SVE file starts with shared+0010, length03f0; env.sve instead
selects the in-memory process handoff. The file length is 9766 + 54*N + 44*M,
where N=DS:97a2 and M=DS:9688, stored at file offsets002e and0030.
This helper never changes guest state.
"""
from __future__ import annotations

import hashlib
import struct
from typing import Sequence

GAME_DS_PARAGRAPH = 0x1206
SHARED_POINTER_LINEAR = 0x04F0
DRIVER_SHARED_OFFSETS = (0x60, 0x62, 0x64)

# (DS offset, fixed bytes or multiplier, count key). The order is the file order.
# Writer CALLs 00f7:ad1c..adee; reader CALLs 00f7:b18f..b261.
SERIALIZED_BLOCKS = (
    (0x760A, 0x028E, None),
    (0x97A6, 0x0030, "n"),
    (0xA55E, 0x0006, "n"),
    (0x83C6, 0x002C, "m"),
    (0x7A9E, 0x0220, None),
    (0x9FB4, 0x0240, None),
    (0x0094, 0x0400, None),
    (0x0494, 0x0C00, None),
    (0x1094, 0x03C0, None),
    (0x7CC2, 0x0700, None),
    (0xA414, 0x0048, None),
    (0xA628, 0x0040, None),
)

# Header words imported to these DS destinations at 00f7:afdd..b180.
# Shared+44 -> DS:a61e is intentionally absent: disk loads preserve the current
# byte, while the env.sve memory-handoff path imports it at b105..b11a.
HEADER_SCALARS = (
    (0x20, 0xA626), (0x22, 0xAA58), (0x24, 0x789A),
    (0x26, 0x9690), (0x28, 0x9694), (0x2A, 0x9698),
    (0x2C, 0x8EDC), (0x2E, 0x8EDE), (0x30, 0x7608),
    (0x32, 0x7CC0), (0x34, 0x8ECE), (0x36, 0x8ED0),
    (0x38, 0xA86E), (0x3A, 0x7604), (0x3C, 0x968A),
    (0x3E, 0x97A2), (0x40, 0x9688), (0x42, 0x1454),
    (0x46, 0x8EC6), (0x4A, 0x97A0), (0x4C, 0xA668),
    (0x4E, 0x9FB0), (0x52, 0x978C),
)


def extract_game_state(
    memory: bytes,
    load_segment: int,
    *,
    drivers: Sequence[tuple[str, int]] | None = None,
    include_hex: bool = True,
) -> dict:
    """Return named raw ranges and an optional identity-checked normalized state.

    ``drivers`` must give (module name, loaded segment) for graphics, miscellaneous
    and sound, in shared-header order. Only those three words may be normalized,
    and only after each value matches its declared loaded module. Compare both
    ``driver_modules`` and ``normalized_state_hex`` across original/native runs.

    The state byte stream is file-layout ranges followed by the 23 live restored
    DS words. It is not a newly serialized save file: gameplay can change DS while
    the copied header remains stale. ``file_layout_size`` predicts a fresh save's
    size for these counts. Save-file bytes require a separate exact comparison.
    """
    if len(memory) != 0x100000:
        raise ValueError("GAME state requires a complete one-megabyte snapshot")
    if not isinstance(load_segment, int) or not 0 <= load_segment <= 0xFFFF:
        raise ValueError("invalid GAME load segment")
    ds = (load_segment + GAME_DS_PARAGRAPH) & 0xFFFF

    def read(segment: int, offset: int, length: int) -> bytes:
        if offset < 0 or length < 0 or offset + length > 0x10000:
            raise ValueError("state range crosses a 16-bit segment boundary")
        address = segment * 16 + offset
        if address + length > len(memory):
            raise ValueError("state range exceeds the captured address space")
        return bytes(memory[address:address + length])

    def word(segment: int, offset: int) -> int:
        return struct.unpack("<H", read(segment, offset, 2))[0]

    shared_offset, shared_segment = struct.unpack_from("<HH", memory, SHARED_POINTER_LINEAR)
    prefix = read(shared_segment, shared_offset + 0x10, 0x3F0)
    counts = {"n": word(ds, 0x97A2), "m": word(ds, 0x9688)}
    # Original IMUL narrows its count result to AX. Reject an invalid snapshot
    # rather than silently wrap a guessed range or read unrelated memory.
    if any(value > 0x7FFF for value in counts.values()):
        raise ValueError("negative signed GAME record count")
    ranges = []
    pieces = []
    file_offset = 0

    def add_range(name: str, segment: int, offset: int, data: bytes) -> None:
        nonlocal file_offset
        ranges.append({"name": name, "segment": segment, "offset": offset,
                       "linear": segment * 16 + offset, "length": len(data),
                       "file_offset": file_offset,
                       "sha256": hashlib.sha256(data).hexdigest()})
        pieces.append(data)
        file_offset += len(data)

    add_range("shared_0010", shared_segment, shared_offset + 0x10, prefix)
    for offset, amount, count_key in SERIALIZED_BLOCKS:
        length = amount * counts[count_key] if count_key else amount
        if length > 0xFFFF:
            raise ValueError("GAME record byte count overflows original AX")
        add_range(f"ds_{offset:04x}", ds, offset, read(ds, offset, length))

    scalar_bytes = b"".join(read(ds, offset, 2) for _, offset in HEADER_SCALARS)
    scalars = [{"shared_offset": shared, "file_offset": shared - 0x10,
                "ds_offset": offset, "value": word(ds, offset),
                "saved_header_value": word(shared_segment, shared_offset + shared)}
               for shared, offset in HEADER_SCALARS]
    layout = b"".join(pieces)
    raw = layout + scalar_bytes
    result = {
        "schema": 1, "load_segment": load_segment, "ds": ds,
        "shared_segment": shared_segment, "shared_offset": shared_offset,
        "counts": counts,
        "header_counts": {"n": word(shared_segment, shared_offset + 0x3E),
                          "m": word(shared_segment, shared_offset + 0x40)},
        "file_layout_size": len(layout), "state_size": len(raw),
        "ranges": ranges, "restored_scalars": scalars,
        "raw_layout_sha256": hashlib.sha256(layout).hexdigest(),
        "raw_state_sha256": hashlib.sha256(raw).hexdigest(),
        "driver_words": [word(shared_segment, shared_offset + offset)
                         for offset in DRIVER_SHARED_OFFSETS],
    }
    if include_hex:
        result["raw_state_hex"] = raw.hex()
    if drivers is not None:
        if len(drivers) != 3:
            raise ValueError("exactly three loaded driver identities are required")
        normalized = bytearray(raw)
        names = []
        for offset, (name, segment), actual in zip(DRIVER_SHARED_OFFSETS, drivers, result["driver_words"]):
            if not name or segment != actual:
                raise ValueError(f"driver identity mismatch at shared+{offset:04x}")
            names.append(name.upper())
            normalized[offset - 0x10:offset - 0x10 + 2] = b"\0\0"
        result["driver_modules"] = names
        result["normalized_state_sha256"] = hashlib.sha256(normalized).hexdigest()
        if include_hex:
            result["normalized_state_hex"] = normalized.hex()
    return result
