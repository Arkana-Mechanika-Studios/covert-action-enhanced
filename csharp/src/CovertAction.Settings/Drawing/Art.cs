namespace CovertAction.Settings.Drawing;

/// <summary>
/// The launcher's art, data-driven as the Darklands Restoration Project's: a picture embedded from Resources/art
/// (&lt;name&gt;.png, 8-bit RGB or RGBA) replaces the part the launcher otherwise draws in code; none is in the repository
/// yet, so everything is drawn in code. Names: background (the whole window, 1280x720), card (the left card's face),
/// panel (the settings panel's face), title (the title, in place of the drawn letters). New code.
/// </summary>
internal static class Art
{
    private static readonly Dictionary<string, Image?> Cache = [];

    /// <summary>The picture of that name, or null: the launcher draws its own.</summary>
    public static Image? Get(string name)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var image))
            {
                using var stream = typeof(Art).Assembly.GetManifestResourceStream("CovertAction.Settings.art." + name + ".png");
                if (stream is not null)
                {
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    try
                    {
                        image = Png.Read(copy.ToArray());
                    }
                    catch (InvalidDataException)
                    {
                        image = null;
                    }
                }
                Cache[name] = image;
            }
            return image;
        }
    }
}
