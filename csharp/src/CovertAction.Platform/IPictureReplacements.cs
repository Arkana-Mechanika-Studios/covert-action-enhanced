namespace CovertAction.Platform;

/// <summary>
/// Pictures shown in place of the game's own (D-642, new code, outside the game): a player's option of the launcher, the
/// Amiga version's pictures read from the player's own Amiga files. The picture loader asks it after it has read the
/// game's picture, with the picture's name and size; the answer, when there is one, is put on the page in place of the
/// game's pixels. The game's files are read, and its drivers called, exactly as without it.
/// </summary>
public interface IPictureReplacements
{
    /// <summary>The picture to show in place of the game's picture of that name and size, or null for the game's own.</summary>
    /// <param name="name">The picture's file name as the game asks for it ("faces.pic", "vienna.pic").</param>
    /// <param name="width">The width of the game's picture in pixels; a replacement has to fit it.</param>
    /// <param name="height">The height of the game's picture in pixels.</param>
    /// <returns>The replacement, or null when there is none for this picture.</returns>
    ReplacementPicture? Find(string name, int width, int height);
}

/// <summary>A picture to put on a page: pixels that are numbers of the display's colours (0 to 255) with the picture's own colours already set.</summary>
/// <param name="Width">The width in pixels, that of the game's picture it replaces.</param>
/// <param name="Height">The height in pixels, that of the game's picture it replaces.</param>
/// <param name="Pixels">The pixels, row by row from the top: display colours.</param>
public readonly record struct ReplacementPicture(int Width, int Height, byte[] Pixels);
