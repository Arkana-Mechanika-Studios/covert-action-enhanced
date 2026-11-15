using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// What the rewrite's graphics driver needs from the player's MGRAPHIC.EXE, the original's driver for the 256-colour
/// MCGA/VGA mode: its table of colours. The driver's code is not used; it is reimplemented in CovertAction.Drivers.
/// </summary>
/// <remarks>
/// New code. A driver file is an MZ program that the launcher loads without running it. Its header area names the
/// file ("MGRAPHIC.EXE" at image offset 0x10) and holds, at 0x2a, the paragraph of the driver's data segment.
/// </remarks>
public sealed class McgaDriverFile
{
    /// <summary>Entries in the colour table.</summary>
    public const int Colours = 128;

    private const int NameOffset = 0x10;
    private const int DataSegmentWord = 0x2a;
    private const int ColourTableOffset = 0x80;

    private const int TraitsOffset = 0x1656;
    private const int TraitsSize = 10;

    private McgaDriverFile(byte[] defaultColours, byte[] traits)
    {
        DefaultColours = defaultColours;
        Traits = traits;
    }

    /// <summary>
    /// The driver's words about itself: ten bytes in front of its table of rows, of which the game asks for the first
    /// word (whether the display can switch between pages on its own). All zeros when the file is too short to have them.
    /// </summary>
    public byte[] Traits { get; }

    /// <summary>
    /// The driver's colour table: 128 colours of three bytes each (red, green, blue, 0..63 as the VGA takes them).
    /// The first 64 are the EGA's 64 colours, which the game's 16-entry palettes choose from; the driver loads all
    /// 128 into the display when it sets the graphics mode.
    /// </summary>
    // orig: MGRAPHIC DS:0080, 384 bytes (read by the set_palette and mode-set routines of the driver)
    public byte[] DefaultColours { get; }

    /// <summary>Reads the driver file.</summary>
    /// <param name="file">The whole of MGRAPHIC.EXE.</param>
    /// <exception cref="InvalidDataException">The file is not the MCGA driver.</exception>
    public static McgaDriverFile Read(ReadOnlySpan<byte> file)
    {
        var image = Executable.Read(file).Image;
        if (image.Length < DataSegmentWord + 2 || !image.AsSpan(NameOffset, 12).SequenceEqual("MGRAPHIC.EXE"u8))
        {
            throw new InvalidDataException("not the MCGA graphics driver (MGRAPHIC.EXE)");
        }
        var data = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(DataSegmentWord)) * 16;
        var table = data + ColourTableOffset;
        if (table + 3 * Colours > image.Length)
        {
            throw new InvalidDataException("MGRAPHIC.EXE is shorter than its colour table");
        }
        var traits = image.Length >= TraitsOffset + TraitsSize ? image.AsSpan(TraitsOffset, TraitsSize).ToArray() : new byte[TraitsSize];
        return new McgaDriverFile(image.AsSpan(table, 3 * Colours).ToArray(), traits);
    }
}
