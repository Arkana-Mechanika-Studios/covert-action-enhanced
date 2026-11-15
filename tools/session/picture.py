"""Pictures of a recording for a person to look at: a checkpoint's frame as PNG, or both sides and their difference.

    python -B tools/session/picture.py frame <frames/sha.idx> out.png
    python -B tools/session/picture.py diff <original.idx> <csharp.idx> out.png

A frame (session_store) is 64000 palette indices and 768 six-bit DAC bytes. The diff picture shows the
original, the C# and, on the right, the C# picture greyed with the differing pixels in red, each three times
enlarged. Needs Pillow (compare-side helper only; the recordings do not depend on it).
"""
import sys
from pathlib import Path

from PIL import Image

WIDTH, HEIGHT, SCALE = 320, 200, 3


def frame_image(data):
    """A frame's 320x200 picture in its own colours (six-bit DAC values widened to eight bits)."""
    pixels, dac = data[:WIDTH * HEIGHT], data[WIDTH * HEIGHT:]
    picture = Image.frombytes("P", (WIDTH, HEIGHT), bytes(pixels))
    picture.putpalette(bytes((v << 2) | (v >> 4) for v in dac))
    return picture.convert("RGB")


def diff_image(original, csharp):
    """Original | C# | the differing pixels in red over the C# picture in grey."""
    a, b = frame_image(original), frame_image(csharp)
    marks = b.convert("L").convert("RGB")
    px = marks.load()
    for i in range(WIDTH * HEIGHT):
        if original[i] != csharp[i]:
            px[i % WIDTH, i // WIDTH] = (255, 0, 0)
    out = Image.new("RGB", (WIDTH * 3 + 8, HEIGHT))
    for n, part in enumerate((a, b, marks)):
        out.paste(part, (n * (WIDTH + 4), 0))
    return out.resize((out.width * SCALE, out.height * SCALE), Image.NEAREST)


def main():
    mode, *paths = sys.argv[1:]
    if mode == "frame":
        frame_image(Path(paths[0]).read_bytes()).resize((WIDTH * SCALE, HEIGHT * SCALE), Image.NEAREST).save(paths[1])
    elif mode == "diff":
        diff_image(Path(paths[0]).read_bytes(), Path(paths[1]).read_bytes()).save(paths[2])
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
