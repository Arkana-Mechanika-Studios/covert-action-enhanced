"""Write build/csharp-reference/content.json: what the C# readers of the game's files must produce, from the
project's already verified decoders. The C# data tests (PictureTests, TextFileTests) compare with it; they skip
when it is absent.

    (once: build/validation/assets must hold the verified pictures, made by the project's earlier tooling)
    python -B csharp/tools/reference_content.py [game folder]

Pictures. build/validation/assets has every .PIC and every picture of the .CAT archives as decoded by the upstream
reference decoder (reference-pic2png) and, pixel-identical, by the project's own reference decoder. Those files are
RGBA. To compare palette indices exactly, this tool decodes each picture with a transliteration of that decoder and
requires that one fixed table of 16 colours turns its indices into exactly the reference's pixels, for every
picture of the game; then it records the SHA-256 of the indices.

Texts. For every tag of the tagged text files it records the SHA-256 of the entry's lines as the original's reader
sees them (FINAL's read_cleared_text, FINAL 0100:750d: fscanf("%[^\\n]\\n") reads a line and skips all
blank space after it), written here independently of the C#.

The output is derived from the game and stays in build/ (never committed).
"""
import hashlib
import json
import struct
import sys
from pathlib import Path

from PIL import Image

CSHARP = Path(__file__).resolve().parent.parent
ROOT = CSHARP.parent
ASSETS = ROOT / "build" / "validation" / "assets"
OUT = ROOT / "build" / "csharp-reference" / "content.json"
TEXT_FILES = ["TEXT.DTA", "CLUES.TXT", "PLOT.TXT", "PROSE.DTA"]


def decode_picture(data):
    """The reference decoder's ca_pic_decode: (width, height, indices)."""
    fmt, width, height = struct.unpack_from("<HHH", data)
    offset = 23 if fmt == 15 else 7
    max_width = data[offset - 1]
    stream = data[offset:]
    prefix, suffix, stack = [0] * 2048, [0] * 2048, []
    state = dict(top=256, width=9, previous=0, first=0, bit=0)

    def next_byte():
        if not stack:
            code = 0
            for i in range(state["width"]):
                code |= ((stream[state["bit"] // 8] >> (state["bit"] % 8)) & 1) << i
                state["bit"] += 1
            saved = index = code
            if index == state["top"]:
                index = state["previous"]
                stack.append(state["first"])
            while index >= 256:
                stack.append(suffix[index])
                index = prefix[index]
            state["first"] = index
            stack.append(index)
            prefix[state["top"]] = state["previous"]
            suffix[state["top"]] = index
            state["previous"] = saved
            state["top"] += 1
            if state["top"] == 1 << state["width"]:
                state["width"] += 1
            if state["width"] > max_width:
                state.update(top=256, width=9, previous=0, first=0)
        return stack.pop()

    row_bytes = (width + 1) // 2
    pixels = bytearray(width * height)
    written, previous = 0, 0
    while written < row_bytes * height:
        value, repeats = next_byte(), 1
        if value == 0x90:
            count = next_byte()
            if count == 0:
                value = 0x90
            else:
                value, repeats = previous, count - 1
        for _ in range(repeats):
            y, x = divmod(written, row_bytes)
            pixels[y * width + 2 * x] = value & 15
            if 2 * x + 1 < width:
                pixels[y * width + 2 * x + 1] = value >> 4
            written += 1
        previous = value
    return width, height, bytes(pixels)


def pictures(game):
    """name -> (bytes of the picture, the reference image's file)."""
    for source in sorted(game.glob("*.PIC")):
        yield source.name, source.read_bytes(), ASSETS / (source.name + ".png")
    for source in sorted(game.glob("*.CAT")):
        data = source.read_bytes()
        for index in range(struct.unpack_from("<H", data)[0]):
            name, _, size, offset = struct.unpack_from("<12sIII", data, 2 + index * 24)
            yield f"{source.name}/{name.rstrip(bytes(1)).decode('latin-1')}", data[offset:offset + size], ASSETS / f"{source.name}_{index:03}.png"


def text_entries(data):
    """tag -> lines, as the original's reader sees the file."""
    end = data.find(b"\x1a")
    text = (data if end < 0 else data[:end]).decode("latin-1").replace("\r\n", "\n")
    lines = [line.lstrip(" \t\r\v\f") for line in text.split("\n")]
    lines = [line for line in lines if line]
    entries = {}
    for i, line in enumerate(lines):
        if not line.startswith("*") or line in entries:
            continue    # the first of two equal tags wins
        j = i + 1
        while j < len(lines) and lines[j].startswith("*"):
            j += 1
        body = []
        while j < len(lines) and not lines[j].startswith("*"):
            body.append(lines[j])
            j += 1
        entries[line] = body
    return entries


def main():
    game = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "game" / "Covert Action" / "Covert"
    result = {"pictures": {}, "texts": {}}
    colour_of, index_of = {}, {}
    for name, data, reference in pictures(game):
        width, height, indices = decode_picture(data)
        with Image.open(reference) as image:
            assert image.size == (width, height), name
            rgba = image.convert("RGBA").tobytes()
        for i, index in enumerate(indices):
            colour = rgba[4 * i:4 * i + 4]
            if colour_of.setdefault(index, colour) != colour or index_of.setdefault(colour, index) != index:
                raise SystemExit(f"{name}: pixel {i} is index {index} here and {colour.hex()} in the reference, against the table so far")
        result["pictures"][name] = {"width": width, "height": height, "sha256": hashlib.sha256(indices).hexdigest()}
    for name in TEXT_FILES:
        path = next((p for p in game.iterdir() if p.name.upper() == name), None)
        if path is None or not path.read_bytes().lstrip().startswith(b"*"):
            continue
        result["texts"][name] = {tag: hashlib.sha256("\n".join(body).encode("latin-1")).hexdigest()
                                 for tag, body in text_entries(path.read_bytes()).items()}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(result, indent=1) + "\n", encoding="utf-8")
    print(f"{OUT.relative_to(ROOT)}: {len(result['pictures'])} pictures agree with the reference under one table of {len(colour_of)} colours; "
          + ", ".join(f"{name} {len(tags)} tags" for name, tags in result["texts"].items()))


if __name__ == "__main__":
    main()
