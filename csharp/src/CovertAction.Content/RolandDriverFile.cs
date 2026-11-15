using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// What the rewrite's Roland MT-32 sound driver needs from the player's RSOUND.CA, the original's driver for a Roland
/// MT-32 on an MPU-401 ("RolandMT-32g15-10-90"): its data segment, which holds the tunes (byte streams the driver
/// plays as MIDI), the system-exclusive messages it sends the MT-32 at its start (the display's text, the system
/// settings, the patches and the timbres) and its gunfire's rhythm tables. The driver's code is not used; it is
/// reimplemented in CovertAction.Audio.
/// </summary>
/// <remarks>
/// New code. A driver file is an MZ program; at image offset 0x2a it holds the paragraph of its data segment. The
/// driver changes tune bytes as it plays (random notes, the raid's gunfire), so each driver has its own copy
/// (<see cref="SetByte"/>).
/// </remarks>
public sealed class RolandDriverFile
{
    private const int NameOffset = 0x10;
    private const int DataSegmentWord = 0x2a;

    // the last place the driver reads at a fixed offset: the end of its last system-exclusive message
    private const int LastFixedByte = 0x2d9e;
    private static ReadOnlySpan<byte> Name => "RolandMT-32g15-10-90"u8;

    private readonly byte[] _data;

    private RolandDriverFile(byte[] data) => _data = data;

    /// <summary>Reads the driver file.</summary>
    /// <param name="file">The whole of RSOUND.CA.</param>
    /// <exception cref="InvalidDataException">The file is not the Roland sound driver.</exception>
    public static RolandDriverFile Read(ReadOnlySpan<byte> file)
    {
        var image = Executable.Read(file).Image;
        if (image.Length < DataSegmentWord + 2 || !image.AsSpan(NameOffset, Name.Length).SequenceEqual(Name))
        {
            throw new InvalidDataException("not the Roland MT-32 sound driver (RSOUND.CA)");
        }
        var data = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(DataSegmentWord)) * 16;
        var driver = new RolandDriverFile(image[data..]);
        // a short file fails here, not in the middle of the game
        _ = driver.Byte(LastFixedByte);
        return driver;
    }

    /// <summary>The byte at <paramref name="offset"/> of the data segment.</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public byte Byte(int offset) => offset >= 0 && offset < _data.Length
        ? _data[offset]
        : throw new InvalidDataException($"RSOUND.CA ends before DS:{offset:x4}");

    /// <summary>The word at <paramref name="offset"/> of the data segment.</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public ushort Word(int offset) => offset >= 0 && offset + 2 <= _data.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset))
        : throw new InvalidDataException($"RSOUND.CA ends before DS:{offset:x4}");

    /// <summary>Changes a byte of this copy of the data (a tune's byte the driver rewrites as it plays).</summary>
    /// <exception cref="InvalidDataException">The offset is behind the end of the file.</exception>
    public void SetByte(int offset, byte value)
    {
        _ = Byte(offset);
        _data[offset] = value;
    }
}
