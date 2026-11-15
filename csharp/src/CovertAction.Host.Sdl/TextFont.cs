// Reused from the Darklands Restoration Project, src/Darklands.Platform/TextFont.cs; same author.
// Adapted: the namespace; only the built-in font is kept (no font file supplied by the player, no stand-in shapes,
// no 8x8 font for the graphics screen); the missing comments of its members are not required here.
#pragma warning disable CS1591 // the members speak for themselves
using CovertAction.Platform;

namespace CovertAction.Host.Sdl;

/// <summary>
/// An 8×16 text-mode font: 256 code page 437 characters of 16 rows, one byte per row (bit 7 the
/// leftmost dot), as the VGA BIOS keeps it.
/// </summary>
public sealed class TextFont
{
    public const int GlyphHeight = 16;
    public const int ByteLength = 256 * GlyphHeight;

    private readonly byte[] _rows;

    private TextFont(byte[] rows) => _rows = rows;

    public byte Row(byte character, int row) => _rows[character * GlyphHeight + row];

    /// <summary>
    /// The built-in font: the VGA 8×16 text font as DOSBox-X provides it (Resources/vga-8x16.b64, see
    /// THIRD_PARTY_NOTICES.md), the font a VGA card's text mode shows.
    /// </summary>
    public static TextFont Builtin => _builtin ??= LoadEmbedded();

    private static TextFont LoadEmbedded()
    {
        using var stream = typeof(TextFont).Assembly.GetManifestResourceStream("CovertAction.Host.Sdl.vga-8x16.b64")
            ?? throw new InvalidDataException("The built-in text font is missing from the assembly.");
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
        var bytes = Convert.FromBase64String(reader.ReadToEnd().Trim());
        return bytes.Length == ByteLength ? new TextFont(bytes) : throw new InvalidDataException("The built-in text font is not 4096 bytes.");
    }

    private static TextFont? _builtin;
}
