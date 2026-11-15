using CovertAction.Core.Graphics;

namespace CovertAction.Core.Shared;

/// <summary>
/// A part's variables for the library's screens of the campaign (the report frame, faces and photos): the parts
/// that show those screens carry their own copies among their data, and a part's state implements this interface.
/// </summary>
public interface ILibraryScreenState
{
    /// <summary>The graphics mode letter the game was started with ('C' for CGA, 'M' for MCGA ...).</summary>
    sbyte GMode { get; }

    /// <summary>Whether the screen is a four-colour CGA screen (the report frame then uses another field colour).</summary>
    bool CGA { get; }

    /// <summary>The face shown beside the next box; -1 for none. Drawing a face clears it.</summary>
    short Picface { get; set; }

    /// <summary>The colour tables the driver uses while a photo is drawn (on displays with fewer colours: its sepia look).</summary>
    byte[] PhotoColorMap { get; }

    /// <summary>The part's table of images (face parts, the ends of a title bar ...), filled by its picture loaders.</summary>
    IReadOnlyList<Image?> Sprite { get; }
}
