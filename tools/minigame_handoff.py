"""Byte-evidenced BUG/CHASE shared-memory extents, not guessed game state.

BUG 00e8:6985/659e imports/exports twelve blocks after cursor 0400.
CHASE 00df:04a4 imports only the first six; its return updates header fields.
N and M are the shared words at 003e/0040 in both original loaders.
"""

CAPTURE_SCHEMA = 1
MODULES = ("BUG.EXE", "CHASE.EXE")
SCOPES = {"BUG.EXE": "complete-campaign-block", "CHASE.EXE": "imported-prefix-and-result-header"}


def capture_capability():
    return dict(schema=CAPTURE_SCHEMA, modules=list(MODULES))


def handoff_extent(module, header):
    if module not in MODULES:
        raise ValueError("unsupported minigame handoff module")
    if len(header) < 112:
        raise ValueError("minigame handoff lacks its complete control header")
    n = int.from_bytes(header[0x3e:0x40], "little")
    m = int.from_bytes(header[0x40:0x42], "little")
    extent = (9782 if module == "BUG.EXE" else 2798) + 54*n + 44*m
    if n > 0x7fff or m > 0x7fff or extent > 0x10000:
        raise ValueError("invalid minigame handoff counts or segment extent")
    return extent
