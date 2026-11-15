using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// The game's fonts: the player's FONTS.CV, which the launcher loads behind the graphics driver. Three small bitmap
/// fonts with characters of different widths; the game names a font by its number, 1 to 3.
/// </summary>
/// <remarks>
/// <para>
/// New code; the format is read the way the original's MCGA driver reads it (MGRAPHIC 0000:11ab, the set-up of its
/// text routine). The file starts with the number of fonts and one word per font: where in the file the font's
/// pictures begin. Eight bytes before that place is the font's header (first and last character, bytes per picture
/// row, fixed width or 0, rows, padding columns, padding rows, one unused byte), and before the header one byte per
/// character with its width.
/// </para>
/// <para>
/// The pictures are stored row by row for the whole font: first the top row of every character, then the second row
/// of every character, and so on. A row of a character is one or two bytes, most significant bit on the left.
/// </para>
/// <para>
/// Nothing is copied out of the file: a font looks its values up in the file's bytes, at the places the driver
/// computes. So a character the font does not have is looked up beside the tables, as in the original, as long as
/// that is still inside the file; past the file's ends it is an error (<see cref="IndexOutOfRangeException"/>).
/// </para>
/// </remarks>
public sealed class FontSet
{
    private readonly byte[] _file;

    /// <summary>Takes the file.</summary>
    /// <param name="file">The whole of FONTS.CV.</param>
    /// <exception cref="InvalidDataException">The file is too short for its own table of fonts.</exception>
    public FontSet(byte[] file)
    {
        if (file.Length < 2 || file.Length < 2 + 2 * BinaryPrimitives.ReadUInt16LittleEndian(file))
        {
            throw new InvalidDataException("the font file is shorter than its table of fonts");
        }
        _file = file;
        Count = BinaryPrimitives.ReadUInt16LittleEndian(file);
    }

    /// <summary>The number of fonts in the file.</summary>
    public int Count { get; }

    /// <summary>The font with a number, or null when the file has no such font. Numbers start at 1.</summary>
    /// <param name="number">The font number as the game passes it (a 16-bit word).</param>
    public Face? Font(int number)
    {
        var index = (ushort)number;
        if (index == 0 || index > Count)
        {
            return null;
        }
        return new Face(_file, BinaryPrimitives.ReadUInt16LittleEndian(_file.AsSpan(2 * index)));
    }

    /// <summary>One font of the file.</summary>
    public readonly struct Face
    {
        // the header's bytes, counted back from the place where the pictures begin
        private const int FirstCharacterAt = -8;
        private const int LastCharacterAt = -7;
        private const int BytesPerRowAt = -6;
        private const int FixedWidthAt = -5;
        private const int RowsAt = -4;
        private const int PaddingColumnsAt = -3;
        private const int PaddingRowsAt = -2;
        private const int WidthOfLastCharacterAt = -9;

        private readonly byte[] _file;
        private readonly int _pictures;

        internal Face(byte[] file, int pictures)
        {
            _file = file;
            _pictures = pictures;
        }

        /// <summary>The code of the first character the font has (32, the space, in the game's fonts).</summary>
        public byte FirstCharacter => _file[_pictures + FirstCharacterAt];

        /// <summary>The code of the last character the font has.</summary>
        public byte LastCharacter => _file[_pictures + LastCharacterAt];

        /// <summary>The height of a character's picture in pixel rows.</summary>
        public sbyte Rows => (sbyte)_file[_pictures + RowsAt];

        /// <summary>Empty rows below a character when texts are drawn with their background (the space between lines).</summary>
        public sbyte PaddingRows => (sbyte)_file[_pictures + PaddingRowsAt];

        /// <summary>Empty columns to the right of every character (the space between characters).</summary>
        public byte PaddingColumns => _file[_pictures + PaddingColumnsAt];

        /// <summary>The width every character has in a font of fixed width; 0 in a font whose characters have their own widths (all three of the game's).</summary>
        public byte FixedWidth => _file[_pictures + FixedWidthAt];

        /// <summary>How many bytes one row of one character's picture takes: 1 or 2.</summary>
        private int BytesPerRow => _file[_pictures + BytesPerRowAt];

        /// <summary>The number of characters the font has.</summary>
        private int Characters => LastCharacter - FirstCharacter + 1;

        /// <summary>A character's position in the font's tables. The original does not check that the font has the character; the position then lies beside the tables.</summary>
        private byte IndexOf(byte character) => (byte)(character - FirstCharacter);

        /// <summary>How far a character moves the pen: its width (or the font's fixed width) plus the padding columns. A sum beyond 255 wraps, as the original computes in a byte.</summary>
        /// <param name="character">The character's code.</param>
        public byte Advance(byte character)
        {
            var width = FixedWidth != 0
                ? FixedWidth
                : _file[_pictures + WidthOfLastCharacterAt - (Characters - 1) + IndexOf(character)];
            return (byte)(width + PaddingColumns);
        }

        /// <summary>
        /// One row of a character's picture as 16 bits, the left-most pixel in the highest bit. In a font with one
        /// byte per row the low eight bits are the same row of the next character: the original reads two bytes
        /// whatever the font, which shows only when more than eight columns are drawn.
        /// </summary>
        /// <param name="character">The character's code.</param>
        /// <param name="row">The row, 0 at the top.</param>
        public ushort RowBits(byte character, int row)
        {
            var rowBytes = Characters * BytesPerRow;
            var at = _pictures + IndexOf(character) * BytesPerRow + row * rowBytes;
            return BinaryPrimitives.ReadUInt16BigEndian(_file.AsSpan(at, 2));
        }
    }
}
