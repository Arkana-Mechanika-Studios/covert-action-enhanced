using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// One of the game's pictures (a .PIC file, or a picture inside a .CAT archive), decoded: its size and one palette
/// index (0..15) per pixel. The game's pictures are 16-colour pictures; which colours the 16 indices show is the
/// business of the palette the game sets.
/// </summary>
/// <remarks>
/// <para>
/// The format: three words (the format number 7 or 15, the width, the height); in format 15 a table of 16 bytes,
/// one per colour, for displays with four colours (the CGA driver's choice of colour and pattern; the 256-colour
/// driver has no use for it); one byte, the largest code width of the compression (9 to 11 bits); then the
/// compressed pixels. The pixels are packed two to a byte, the left one in the low half, each row starting on a
/// new byte; those bytes are run-length coded (a byte 90h followed by a count repeats the byte before it), and
/// the result is compressed with LZW (codes of 9 bits growing to the largest width, then starting over).
/// </para>
/// <para>
/// This is the data-level decoder: it reads a whole picture at once. The original decodes a picture row by row
/// into a page of its graphics driver (the launcher's library); what it shows is these pixels. Proven
/// on every picture of the game against the project's verified decoders (PictureTests). The format follows the
/// project's own reference decoder, which follows jarikomppa/covert_action (Unlicense).
/// </para>
/// </remarks>
public sealed class PictureFile
{
    private const int PlainFormat = 7;
    private const int FormatWithColourTable = 15;
    private const int ColourTableSize = 16;
    private const int LargestSide = 2048;

    private const byte RepeatMark = 0x90;

    private PictureFile(int width, int height, byte[] pixels, byte[]? fourColourTable, int length)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        FourColourTable = fourColourTable;
        Length = length;
    }

    /// <summary>The picture's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The picture's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels, row by row from the top: palette indices 0..15.</summary>
    public byte[] Pixels { get; }

    /// <summary>The 16 bytes for four-colour displays that a picture of format 15 carries; null in format 7.</summary>
    public byte[]? FourColourTable { get; }

    /// <summary>How many bytes of the data the picture took (pictures can follow one another in a file).</summary>
    public int Length { get; }

    /// <summary>
    /// A picture made of pixels that are already decoded (D-683: an Amiga animation's image in the display's own colours, put in
    /// the place of one of the game's). Its <see cref="Length"/> is 0 and it has no colour table.
    /// </summary>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="pixels">One value per pixel, row by row from the top.</param>
    /// <returns>The picture.</returns>
    public static PictureFile FromPixels(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            throw new ArgumentException($"{pixels.Length} pixels are not a picture of {width} x {height}", nameof(pixels));
        }
        return new PictureFile(width, height, pixels, null, 0);
    }

    /// <summary>Decodes a picture.</summary>
    /// <param name="data">The picture's bytes, from its first word; more may follow.</param>
    /// <exception cref="InvalidDataException">The bytes are not a picture, or the picture is damaged.</exception>
    public static PictureFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            throw new InvalidDataException("a picture starts with three words and a code width");
        }
        int format = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (format is not (PlainFormat or FormatWithColourTable))
        {
            throw new InvalidDataException($"unknown picture format {format}");
        }
        if (width is 0 or > LargestSide || height is 0 or > LargestSide)
        {
            throw new InvalidDataException($"a picture of {width} by {height} pixels");
        }
        var header = 6;
        byte[]? fourColourTable = null;
        if (format == FormatWithColourTable)
        {
            if (data.Length < header + ColourTableSize + 1)
            {
                throw new InvalidDataException("the picture ends inside its colour table");
            }
            fourColourTable = data.Slice(header, ColourTableSize).ToArray();
            header += ColourTableSize;
        }
        var codes = new LzwCodes(data[(header + 1)..].ToArray(), data[header]);
        var pixels = DecodeRows(codes, width, height, twoToAByte: true);
        return new PictureFile(width, height, pixels, fourColourTable, header + 1 + codes.BytesRead);
    }

    /// <summary>
    /// Decodes the compressed rows of a picture whose head is not the PC's: the Amiga version's pictures carry the
    /// same compressed stream (the byte of the largest code width, then the codes) after a head of their own, and
    /// byte-swapped in 16-bit words (<see cref="AmigaPictureFile"/>).
    /// </summary>
    /// <param name="stream">The stream, from the byte with the largest code width.</param>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="twoToAByte">The rows are packed two pixels to a byte (16 colours); otherwise one pixel to a byte, the value kept whole.</param>
    /// <returns>One value per pixel, row by row from the top.</returns>
    /// <exception cref="InvalidDataException">The stream is damaged or ends too early.</exception>
    internal static byte[] DecodeStream(ReadOnlySpan<byte> stream, int width, int height, bool twoToAByte)
    {
        if (stream.Length < 2)
        {
            throw new InvalidDataException("a picture's stream starts with a code width and its codes");
        }
        return DecodeRows(new LzwCodes(stream[1..].ToArray(), stream[0]), width, height, twoToAByte);
    }

    /// <summary>The same for a stream that more follows (the Amiga animations' chunks): also tells how many bytes of the stream the picture took, to the next even byte.</summary>
    /// <param name="stream">The stream, from the byte with the largest code width; more may follow.</param>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="twoToAByte">Whether the rows are packed two pixels to a byte.</param>
    /// <param name="used">The bytes the picture took, rounded up to an even number.</param>
    /// <returns>One value per pixel, row by row from the top.</returns>
    /// <exception cref="InvalidDataException">The stream is damaged or ends too early.</exception>
    internal static byte[] DecodeStream(ReadOnlySpan<byte> stream, int width, int height, bool twoToAByte, out int used)
    {
        if (stream.Length < 2)
        {
            throw new InvalidDataException("a picture's stream starts with a code width and its codes");
        }
        var codes = new LzwCodes(stream[1..].ToArray(), stream[0]);
        var pixels = DecodeRows(codes, width, height, twoToAByte);
        used = (1 + codes.BytesRead + 1) & ~1;
        return pixels;
    }

    /// <summary>
    /// Runs the codes through the run-length decoding into the rows of the picture. With two pixels to a byte each row
    /// starts on a new byte, the left pixel in the low half; with one pixel to a byte the byte is the pixel.
    /// </summary>
    private static byte[] DecodeRows(LzwCodes codes, int width, int height, bool twoToAByte)
    {
        var rowBytes = twoToAByte ? (width + 1) / 2 : width;
        var pixels = new byte[width * height];
        var bytes = new RunLengthBytes(codes);
        for (var packed = 0; packed < rowBytes * height; packed++)
        {
            var value = bytes.Next();
            if (bytes.Pending > rowBytes * height - packed - 1)
            {
                throw new InvalidDataException("the picture's data runs past its last row");
            }
            var row = packed / rowBytes;
            var column = packed % rowBytes * (twoToAByte ? 2 : 1);
            if (!twoToAByte)
            {
                pixels[row * width + column] = value;
                continue;
            }
            pixels[row * width + column] = (byte)(value & 0x0f);
            if (column + 1 < width)
            {
                pixels[row * width + column + 1] = (byte)(value >> 4);
            }
        }
        return pixels;
    }

    /// <summary>
    /// Decodes a block of bytes compressed like a picture: the form the library's <c>load_picture_surface</c> reads.
    /// Two words (a format number, of which only the lowest bit counts, and the number of bytes), one byte with
    /// the largest code width, then the compressed bytes. With the format's lowest bit set, every byte of the data
    /// gives two bytes of the block, its low half first, as the pixels of a picture's row; without it the bytes
    /// are the block's as they are.
    /// </summary>
    /// <remarks>
    /// None of the game's files has this form and none of its programs asks for one: the reading follows the
    /// routine's instructions (checked against asm: CHASE 0000:0a8e, 0000:0ad6 and the byte decoder at 0000:0b83).
    /// </remarks>
    /// <param name="data">The block's bytes, from its first word; more may follow.</param>
    /// <returns>The bytes the original writes into its caller's buffer.</returns>
    /// <exception cref="InvalidDataException">The data ends inside the block's head.</exception>
    public static byte[] ReadBlock(ReadOnlySpan<byte> data)
    {
        const int header = 4;
        if (data.Length < header + 1)
        {
            throw new InvalidDataException("a packed block starts with two words and a code width");
        }
        var halves = (BinaryPrimitives.ReadUInt16LittleEndian(data) & 1) != 0;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var bytes = new RunLengthBytes(new LzwCodes(data[(header + 1)..].ToArray(), data[header]));
        // C: with two bytes of the block to a byte of data the original always writes both, so a block of an odd
        // number of bytes is one byte longer in its caller's buffer than its count says; and its counter is
        // decremented before it is tested, so a count of 0 runs through all 65536 values.
        var units = halves ? (count + 1) / 2 : count;
        if (units == 0)
        {
            units = 0x10000;
        }
        var block = new byte[halves ? units * 2 : units];
        for (var unit = 0; unit < units; unit++)
        {
            var value = bytes.Next();
            if (halves)
            {
                block[unit * 2] = (byte)(value & 0x0f);
                block[unit * 2 + 1] = (byte)(value >> 4);
            }
            else
            {
                block[unit] = value;
            }
        }
        return block;
    }

    /// <summary>
    /// The run-length side of the compression: a byte 90h followed by a count stands for the byte before it, as
    /// often again as the count says less one; 90h followed by 0 is a plain byte 90h.
    /// </summary>
    private sealed class RunLengthBytes(LzwCodes codes)
    {
        private byte _previous;

        /// <summary>How often the last byte handed out will be handed out again before the next byte of the data is looked at.</summary>
        public int Pending { get; private set; }

        public byte Next()
        {
            if (Pending > 0)
            {
                Pending--;
                return _previous;
            }
            var value = codes.NextByte();
            if (value == RepeatMark)
            {
                var count = codes.NextByte();
                if (count != 0)
                {
                    // BUG: CA-11 a count of 1 does not mean "no more": the original's counter passes through 0 and repeats 256 times.
                    Pending = (count == 1 ? 256 : count - 1) - 1;
                    return _previous;
                }
            }
            _previous = value;
            return value;
        }
    }

    /// <summary>
    /// The LZW side of the compression: turns the stream of codes back into bytes. A code below 256 is a byte; a
    /// higher one stands for a sequence seen before (a table entry: an earlier sequence plus one byte). Every code
    /// read adds an entry. Codes start 9 bits wide and grow by a bit whenever the table fills the current width;
    /// when they would grow past the picture's largest width, the table starts over. There is no "clear" code.
    /// </summary>
    private sealed class LzwCodes
    {
        private const int FirstEntry = 256;
        private const int FirstWidth = 9;
        private const int WidestCodes = 11;
        private const int TableSize = 1 << WidestCodes;

        private readonly byte[] _data;
        private readonly int _largestWidth;
        private readonly ushort[] _earlier = new ushort[TableSize];     // per entry: the sequence it extends
        private readonly byte[] _added = new byte[TableSize];           // per entry: the byte it adds
        private readonly Stack<byte> _pending = new();                  // the bytes of the current code not handed out yet
        private int _bit;
        private int _entries = FirstEntry;
        private int _width = FirstWidth;
        private int _previousCode;
        private byte _firstByte;

        public LzwCodes(byte[] data, int largestWidth)
        {
            if (largestWidth < FirstWidth)
            {
                throw new InvalidDataException($"a largest code width of {largestWidth} bits");
            }
            _data = data;
            // BUG: CA-11 a width above 11 is taken as 11.
            _largestWidth = Math.Min(largestWidth, WidestCodes);
        }

        /// <summary>The bytes of the data read so far, a started byte counted in full.</summary>
        public int BytesRead => (_bit + 7) / 8;

        public byte NextByte()
        {
            if (_pending.Count == 0)
            {
                Expand(NextCode());
            }
            return _pending.Pop();
        }

        /// <summary>The next code: its bits are stored lowest first.</summary>
        private int NextCode()
        {
            if (_bit + _width > _data.Length * 8)
            {
                throw new InvalidDataException("the picture's data ends too early");
            }
            var code = 0;
            for (var i = 0; i < _width; i++, _bit++)
            {
                code |= (_data[_bit / 8] >> (_bit % 8) & 1) << i;
            }
            return code;
        }

        /// <summary>Puts the bytes a code stands for on the pending stack (they come out of the table last byte first) and adds the table's next entry.</summary>
        private void Expand(int code)
        {
            if (code > _entries)
            {
                throw new InvalidDataException("a code the table does not have yet");
            }
            var entry = code;
            if (entry == _entries)
            {
                // the entry being made by this very code: the previous sequence followed by its own first byte
                entry = _previousCode;
                _pending.Push(_firstByte);
            }
            while (entry >= FirstEntry)
            {
                _pending.Push(_added[entry]);
                entry = _earlier[entry];
            }
            _firstByte = (byte)entry;
            _pending.Push(_firstByte);

            // the new entry: the previous code's sequence plus the first byte of this one
            // BUG: CA-11 the very first code has no previous one; the original adds an entry all the same (never used by the game's pictures).
            _earlier[_entries] = (ushort)_previousCode;
            _added[_entries] = _firstByte;
            _previousCode = code;
            _entries++;
            if (_entries == 1 << _width)
            {
                _width++;
            }
            if (_width > _largestWidth)
            {
                _entries = FirstEntry;
                _width = FirstWidth;
                _previousCode = 0;
                _firstByte = 0;
            }
        }
    }
}
