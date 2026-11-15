namespace CovertAction.Core.Graphics;

/// <summary>
/// The images a part of the game holds, each with the number the original knows it by. The original keeps an
/// image as a word in its variables (the image's place in memory); the rewrite keeps a reference to an
/// <see cref="Image"/>. Only where a part's variables are written as the original's bytes, or read from them, is
/// the word needed: this register hands it out and finds the image again.
/// </summary>
/// <remarks>
/// New code, for the state layouts and the oracle tests. A word nobody has given an image yet stands for an empty
/// image of its own: enough to tell images apart, which is all a comparison with a recorded run needs.
/// </remarks>
public sealed class ImageRegister
{
    private readonly Dictionary<int, Image> _images = [];

    /// <summary>The image a word stands for; null for 0, the original's "no image".</summary>
    /// <param name="name">The word.</param>
    public Image? For(int name)
    {
        name = (ushort)name;
        if (name == 0)
        {
            return null;
        }
        if (!_images.TryGetValue(name, out var image))
        {
            _images[name] = image = new Image(0, 0, []);
        }
        return image;
    }

    /// <summary>Makes a word stand for an image (one read from the original's memory, for example).</summary>
    /// <param name="name">The word.</param>
    /// <param name="image">The image.</param>
    public void Set(int name, Image image) => _images[(ushort)name] = image;

    /// <summary>The word of an image; 0 for null. An image without a word yet gets the next free one.</summary>
    /// <param name="image">The image.</param>
    public int NameOf(Image? image)
    {
        if (image is null)
        {
            return 0;
        }
        foreach (var (name, known) in _images)
        {
            if (ReferenceEquals(known, image))
            {
                return name;
            }
        }
        var free = Enumerable.Range(1, ushort.MaxValue).First(name => !_images.ContainsKey(name));
        _images[free] = image;
        return free;
    }
}
