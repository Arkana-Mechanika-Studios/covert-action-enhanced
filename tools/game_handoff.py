"""Byte-evidenced GAME env.sve import, separate from disk-save layout.

GAME 00f7:afcd starts its shared-memory cursor at 0400. The twelve calls at
b18f..b261 consume 8758 fixed bytes, 0030*N, 0006*N and 002c*M. N/M are
imported from shared+003e/+0040 at b0dc/b0ed. The copier ae62 advances the
16-bit cursor. Disk saves omit the first 0010 bytes; see game_state.py.
"""
import hashlib


GAME_IMAGE_SHA256 = "dce222d29d7b3ae01a5b1405da31ef8ec5473a191d9f36a8dc171cb27eceb021"
GAME_CODE_BASE = 0x00F7 * 16
# End-exclusive CS-relative ranges. None contains a relocation operand.
GAME_LOADER_GUARDS = (
    (0x00CF, 0x00D9, "d2722248fb1421688a8cf2fb3408600f48d28abcae794f9789a947e87bf32462", "env.sve entry call"),
    (0xAE62, 0xAEBB, "85bd8ca18940cff7037d3fef3b5e925675c0ba730b046332a6f4cceb313df3f5", "shared cursor copier"),
    (0xAEC3, 0xAED7, "903bb6265dced1da6c85f29424151937da3f1092b5996dd3f7c741b68571e9a7", "env.sve branch"),
    (0xAFCD, 0xB267, "ad742fd215c14afd77cf3528dff03ab5ab7ba08b2b06cfd6354c08257296125c", "cursor, counts and twelve imports"),
)


def validate_game_loader(executable):
    """Refuse to infer a layout from a different executable or changed code."""
    image = executable.image
    relocations = {segment * 16 + offset for segment, offset in executable.relocations}
    for start, end, expected, label in GAME_LOADER_GUARDS:
        first, last = GAME_CODE_BASE + start, GAME_CODE_BASE + end
        if hashlib.sha256(image[first:last]).hexdigest() != expected:
            raise ValueError("GAME handoff byte evidence changed: " + label)
        if any(address < last and address + 2 > first for address in relocations):
            raise ValueError("GAME handoff evidence unexpectedly contains a relocation")
    for offset in (0x0076, 0x41A8):
        address = 0x1206 * 16 + offset
        if image[address:address + 8] != b"env.sve\0":
            raise ValueError("GAME handoff env.sve string changed")
    if hashlib.sha256(image).hexdigest() != GAME_IMAGE_SHA256:
        raise ValueError("GAME handoff requires the pinned load image")
    return dict(schema=1, image_sha256=GAME_IMAGE_SHA256,
                extent="9782 + 54*N + 44*M", count_offsets=[0x3E, 0x40],
                guards=[dict(segment=0x00F7, start=start, end=end, sha256=digest, purpose=label)
                        for start, end, digest, label in GAME_LOADER_GUARDS])


def game_handoff_extent(header):
    """Return a nonwrapping import extent; never emulate truncated IMUL sizes."""
    if len(header) < 112:
        raise ValueError("GAME handoff lacks its complete control header")
    n = int.from_bytes(header[0x3E:0x40], "little")
    m = int.from_bytes(header[0x40:0x42], "little")
    if n > 0x7FFF or m > 0x7FFF:
        raise ValueError("GAME handoff has a negative signed record count")
    # IMUL returns AX; subsequent near destinations and the shared cursor must
    # also remain in their original segments. These are address bounds, not
    # invented limits on valid case difficulty or record counts.
    for destination, size in ((0x97A6, 48*n), (0xA55E, 6*n), (0x83C6, 44*m)):
        if size > 0xFFFF or destination + size > 0x10000:
            raise ValueError("GAME import count exceeds its destination segment")
    extent = 9782 + 54*n + 44*m
    if extent > 0xFFFF:
        raise ValueError("GAME handoff cursor would wrap")
    return extent


def game_handoff_bounds(header, offset, segment):
    """Validate the exact far pointer before reading any complete payload."""
    if any(type(value) is not int or not 0 <= value <= 0xFFFF for value in (offset, segment)):
        raise ValueError("invalid GAME shared far pointer")
    extent = game_handoff_extent(header)
    address = segment * 16 + offset
    if offset + extent > 0x10000 or address + extent > 0x100000:
        raise ValueError("GAME shared handoff exceeds its captured segment or memory")
    return extent


def validate_game_capture(event):
    """Accept historical captures only when their own counts explain the bytes."""
    data = bytes.fromhex(event["handoff_hex"])
    extent = game_handoff_extent(data)
    if len(data) != extent:
        raise ValueError("GAME entry length disagrees with original loader counts")
    if "handoff_size" in event and (type(event["handoff_size"]) is not int or event["handoff_size"] != extent):
        raise ValueError("GAME declared handoff size disagrees with captured bytes")
    address = event.get("shared_address")
    if type(address) is not int or address < 0 or address + extent > 0x100000:
        raise ValueError("GAME capture exceeds the one-megabyte address space")
    if "shared_offset" in event or "shared_segment" in event:
        if "shared_offset" not in event or "shared_segment" not in event:
            raise ValueError("GAME capture has an incomplete shared far pointer")
        offset, segment = event["shared_offset"], event["shared_segment"]
        game_handoff_bounds(data, offset, segment)
        if address != segment * 16 + offset:
            raise ValueError("GAME capture far pointer disagrees with its address")
    return data
