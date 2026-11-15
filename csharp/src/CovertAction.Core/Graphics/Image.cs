namespace CovertAction.Core.Graphics;

/// <summary>
/// A small picture the game holds to draw it again and again: a sprite, a face, a piece cut out of a picture sheet.
/// Its pixels of colour 0 are see-through. The game gets an image from the graphics driver (cut from a page, or
/// built row by row) and hands it back to be drawn; it never looks inside.
/// </summary>
/// <remarks>
/// The original keeps an image in memory it asks DOS for and passes its address around; here it is this object.
/// The rows are stored as the original stores them, because that decides what is drawn: the see-through pixels at
/// the start of a row are counted, not stored; those at its end are dropped; those in between are stored and
/// skipped when the image is drawn. In the original's memory an image is two words (width, height) and then, per
/// row, two words (the number of stored pixels, the number of skipped ones) and the stored pixels.
/// </remarks>
/// <param name="width">The image's width in pixels.</param>
/// <param name="height">The image's height in rows, as it was asked for.</param>
/// <param name="rows">The rows, from the top. Fewer than <paramref name="height"/> when the image outgrew what one image can hold.</param>
public sealed class Image(int width, int height, IReadOnlyList<ImageRow> rows)
{
    /// <summary>The image's width in pixels.</summary>
    public int Width { get; } = width;

    /// <summary>The image's height in rows.</summary>
    public int Height { get; } = height;

    /// <summary>The rows, from the top.</summary>
    public IReadOnlyList<ImageRow> Rows { get; } = rows;
}

/// <summary>One row of an <see cref="Image"/>.</summary>
/// <param name="Skip">How many see-through pixels the row starts with.</param>
/// <param name="Pixels">The pixels after them, up to the row's last pixel that is not see-through; a 0 among them is see-through.</param>
public readonly record struct ImageRow(int Skip, byte[] Pixels);

/// <summary>The part of the screen an image was drawn on: what the driver tells the game after a clipped draw.</summary>
/// <param name="X">The left edge, relative to the port's window.</param>
/// <param name="Y">The top edge, relative to the port's window.</param>
/// <param name="Width">The width of what was drawn; 0 when nothing was.</param>
/// <param name="Height">The height of what was drawn; 0 when nothing was.</param>
public readonly record struct ImageArea(int X, int Y, int Width, int Height);
