// Adapted from the Darklands Restoration Project, src/Darklands.Launcher/Drawing/Png.cs; same author.
// Adapted: the namespace; a writer added (the launcher's picture as a PNG file: --launcher-screenshot and the tests).
using System.Buffers.Binary;
using System.IO.Compression;

namespace CovertAction.Settings.Drawing;

/// <summary>
/// Reads the launcher's art: 8-bit RGB or RGBA PNG files, not interlaced, as an image program saves them
/// them (the launcher references no picture library). Anything else is refused (<see cref="InvalidDataException"/>).
/// </summary>
internal static class Png
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Image Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8 || !file[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("not a PNG file");
        }
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        for (var at = 8; ;)
        {
            if (at + 12 > file.Length)
            {
                throw new InvalidDataException("PNG ends before IEND");
            }
            var length = BinaryPrimitives.ReadInt32BigEndian(file[at..]);
            if (length < 0 || at + 12 + length > file.Length)
            {
                throw new InvalidDataException("PNG chunk runs past the end");
            }
            var type = file.Slice(at + 4, 4);
            var data = file.Slice(at + 8, length);
            at += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                channels = data[9] switch { 2 => 3, 6 => 4, _ => 0 };
                if (data[8] != 8 || channels == 0 || data[12] != 0 || width <= 0 || height <= 0 || (long)width * height > 1 << 24)
                {
                    throw new InvalidDataException("only 8-bit RGB or RGBA PNG files, not interlaced");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var z = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }
        var previous = new byte[stride];
        var current = new byte[stride];
        var argb = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            var line = raw.AsSpan(y * (stride + 1), stride + 1);
            Unfilter(line[0], line[1..], previous, current, channels);
            for (var x = 0; x < width; x++)
            {
                var i = x * channels;
                var a = channels == 4 ? (uint)current[i + 3] : 0xFF;
                argb[y * width + x] = a << 24 | (uint)current[i] << 16 | (uint)current[i + 1] << 8 | current[i + 2];
            }
            (previous, current) = (current, previous);
        }
        return new Image(width, height, argb);
    }

    /// <summary>Writes <paramref name="argb"/> (row by row, 0xAARRGGBB; the alpha is ignored) as an 8-bit RGB PNG file.</summary>
    public static void Write(string path, ReadOnlySpan<uint> argb, int width, int height)
    {
        using var file = File.Create(path);
        file.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR"u8, header);
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            for (var x = 0; x < width; x++)
            {
                var p = argb[y * width + x];
                raw[row + 1 + x * 3] = (byte)(p >> 16);
                raw[row + 2 + x * 3] = (byte)(p >> 8);
                raw[row + 3 + x * 3] = (byte)p;
            }
        }
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }
        Chunk(file, "IDAT"u8, packed.ToArray());
        Chunk(file, "IEND"u8, []);
    }

    private static void Chunk(Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        file.Write(word);
        file.Write(type);
        file.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        file.Write(word);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc;
    }

    private static void Unfilter(byte filter, ReadOnlySpan<byte> line, byte[] previous, byte[] current, int bpp)
    {
        for (var i = 0; i < line.Length; i++)
        {
            int left = i >= bpp ? current[i - bpp] : 0;
            int up = previous[i];
            int upLeft = i >= bpp ? previous[i - bpp] : 0;
            var predicted = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"PNG row filter {filter} unknown"),
            };
            current[i] = (byte)(line[i] + predicted);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
