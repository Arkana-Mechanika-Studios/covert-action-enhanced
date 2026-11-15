// Adapted from the Darklands Restoration Project, src/Darklands.Presentation/Graphics/PngWriter.cs; same author.
// Adapted: the namespace; only the writer of a block of colours is kept; a test helper (screenshots of the set-up page).
using System.IO.Compression;

namespace CovertAction.Launcher.Tests.Intro;

/// <summary>Writes pixels as a PNG file.</summary>
internal static class PngWriter
{
    /// <summary>Writes <paramref name="argb"/> (row by row, 0xAARRGGBB) as a truecolour PNG.</summary>
    public static void Write(ReadOnlySpan<uint> argb, int width, int height, string path)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;          // bit depth
        header[9] = 2;          // truecolour
        Chunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var colour = argb[y * width + x];
                    row[1 + x * 3] = (byte)(colour >> 16);
                    row[2 + x * 3] = (byte)(colour >> 8);
                    row[3 + x * 3] = (byte)colour;
                }
                z.Write(row);
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        data.CopyTo(body, 4);
        output.Write(body);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(body));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
