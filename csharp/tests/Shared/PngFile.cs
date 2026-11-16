using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CovertAction.Tests;

/// <summary>A PNG writer for the tools of the tests that look at the screen: an 8-bit picture with a palette.</summary>
internal static class PngFile
{
    private const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>The 320 x 200 screen as a PNG file of 8-bit colours with the display's palette.</summary>
    /// <param name="screen">The 64000 pixels, row by row.</param>
    /// <param name="palette">The display's 256 colours, three bytes each, 0..63 as the VGA takes them.</param>
    public static byte[] Screen(ReadOnlySpan<byte> screen, byte[] palette)
    {
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var row = 0; row < ScreenHeight; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(screen.Slice(row * ScreenWidth, ScreenWidth));
            }
        }
        var colours = new byte[3 * 256];
        for (var i = 0; i < colours.Length; i++)
        {
            // each 6-bit component widened to 8 bits
            colours[i] = (byte)(palette[i] << 2 | palette[i] >> 4);
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        Chunk(png, "IHDR", [0, 0, 1, 0x40, 0, 0, 0, 200, 8, 3, 0, 0, 0]);
        Chunk(png, "PLTE", colours);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(body));
        stream.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xedb88320u ^ crc >> 1 : crc >> 1;
            }
        }
        return ~crc;
    }
}
