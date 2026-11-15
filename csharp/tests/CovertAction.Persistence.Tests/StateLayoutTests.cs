using CovertAction.Core.Graphics;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Tests;

/// <summary>The layout table: fields are read and written little-endian at their offsets, and nothing else is touched.</summary>
public sealed class StateLayoutTests
{
    /// <summary>A small state with one field of each kind.</summary>
    private sealed class Sample
    {
        public short Word { get; set; }

        public sbyte Letter { get; set; }

        public uint Seed { get; set; }

        public byte[] Text { get; } = new byte[4];

        public short[] Counts { get; } = new short[3];

        public RasterPort Port { get; } = new();
    }

    private static StateLayout<Sample> Layout() => new StateLayout<Sample>("sample")
        .Int16(0x02, "word", s => s.Word, (s, v) => s.Word = v)
        .Char(0x05, "letter", s => s.Letter, (s, v) => s.Letter = v)
        .UInt32(0x08, "seed", s => s.Seed, (s, v) => s.Seed = v)
        .Bytes(0x0c, 4, "text", s => s.Text)
        .Int16Array(0x10, 3, "counts", s => s.Counts)
        .Part(0x20, "port", RasterPortLayout.Layout, s => s.Port);

    private static byte[] Image()
    {
        var image = new byte[0x40];
        for (var i = 0; i < image.Length; i++)
        {
            image[i] = (byte)(0xA0 + i % 0x50);
        }
        return image;
    }

    [Fact]
    public void LoadReadsLittleEndianValuesAtTheirOffsets()
    {
        var image = Image();
        image[0x02] = 0x34;
        image[0x03] = 0x12;
        image[0x05] = 0xFF;
        new byte[] { 0xEF, 0xBE, 0xAD, 0xDE }.CopyTo(image, 0x08);
        "ABCD"u8.CopyTo(image.AsSpan(0x0c));
        new byte[] { 1, 0, 0xFE, 0xFF, 0x00, 0x80 }.CopyTo(image, 0x10);
        image[0x20 + 0x0c] = 15; // the port's APen
        image[0x20 + 0x0d] = 0;

        var state = new Sample();
        Layout().Load(image, state);

        Assert.Equal(0x1234, state.Word);
        Assert.Equal(-1, state.Letter);
        Assert.Equal(0xDEADBEEFu, state.Seed);
        Assert.Equal("ABCD"u8.ToArray(), state.Text);
        Assert.Equal(new short[] { 1, -2, -32768 }, state.Counts);
        Assert.Equal(15, state.Port.APen);
    }

    [Fact]
    public void StoreAfterLoadGivesTheSameBytes()
    {
        var image = Image();
        var state = new Sample();
        Layout().Load(image, state);
        var copy = (byte[])image.Clone();
        Layout().Store(state, copy);
        Assert.Equal(image, copy);
    }

    [Fact]
    public void StoreChangesOnlyTheBytesOfChangedFields()
    {
        var image = Image();
        var layout = Layout();
        var state = new Sample();
        layout.Load(image, state);

        state.Word = -2;
        state.Counts[1] = 0x0102;
        state.Port.FontNr = 3;
        var after = (byte[])image.Clone();
        layout.Store(state, after);

        var changed = Enumerable.Range(0, image.Length).Where(i => image[i] != after[i]).ToArray();
        Assert.Equal(new[] { 0x02, 0x03, 0x12, 0x13, 0x30, 0x31 }, changed);
        Assert.Equal(new byte[] { 0xFE, 0xFF }, after[0x02..0x04]);
        Assert.Equal(new byte[] { 0x02, 0x01 }, after[0x12..0x14]);
        Assert.Equal(new byte[] { 0x03, 0x00 }, after[0x30..0x32]);
    }

    [Fact]
    public void FieldAtNamesTheFieldOfAByteOrNothing()
    {
        var layout = Layout();
        Assert.Equal("word", layout.FieldAt(0x03)?.Name);
        Assert.Null(layout.FieldAt(0x04));
        Assert.Equal("counts", layout.FieldAt(0x15)?.Name);
        Assert.Equal("port.FontNr", layout.FieldAt(0x31)?.Name);
        Assert.Null(layout.FieldAt(0x32));
        Assert.Equal(0x32, layout.End);
    }

    [Fact]
    public void OverlappingFieldsAreRefused()
    {
        var layout = Layout();
        Assert.Throws<InvalidOperationException>(() => layout.Int16(0x03, "clash", s => s.Word, (s, v) => s.Word = v));
        Assert.Throws<InvalidOperationException>(() => layout.Bytes(0x0f, 4, "clash", s => s.Text));
        layout.Char(0x04, "free", s => s.Letter, (s, v) => s.Letter = v); // the gap between word and letter
    }

    [Fact]
    public void AnImageShorterThanTheLayoutIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Layout().Load(new byte[0x31], new Sample()));
        Assert.Throws<ArgumentException>(() => Layout().Store(new Sample(), new byte[0x31]));
    }

    private sealed class Album
    {
        public CovertAction.Core.Graphics.Image?[] Pictures { get; } = new CovertAction.Core.Graphics.Image?[3];

        public CovertAction.Core.Graphics.ImageRegister Images { get; } = new();
    }

    [Fact]
    public void StoredImagesAreReferencesInTheStateAndWordsInTheBytes()
    {
        var layout = new StateLayout<Album>("album").ImageArray(2, 3, "pictures", a => a.Pictures, a => a.Images);
        byte[] image = [0xee, 0xee, 0x34, 0x12, 0, 0, 0x34, 0x12, 0xee];
        var album = new Album();
        layout.Load(image, album);
        // the same word is the same image; 0 is no image
        Assert.NotNull(album.Pictures[0]);
        Assert.Null(album.Pictures[1]);
        Assert.Same(album.Pictures[0], album.Pictures[2]);
        Assert.Same(album.Pictures[0], album.Images.For(0x1234));

        // an image the part made itself gets a word of its own when the state is written
        album.Pictures[1] = new CovertAction.Core.Graphics.Image(1, 1, []);
        var written = (byte[])image.Clone();
        layout.Store(album, written);
        Assert.Equal([0x34, 0x12], written[2..4]);
        Assert.NotEqual(0, written[4] | written[5] << 8);
        Assert.NotEqual(0x1234, written[4] | written[5] << 8);
        Assert.Equal([0x34, 0x12, 0xee], written[6..9]);
    }

    /// <summary>A record with a pointer to a text, and a state that holds two such records.</summary>
    private sealed class Named
    {
        public string Name { get; set; } = "";

        public short Number { get; set; }
    }

    private sealed class Pair
    {
        public Named First { get; } = new();

        public Named Second { get; } = new();
    }

    [Fact]
    public void APointerToATextLoadsTheTextFromWhereItPointsInTheWholeImage()
    {
        // a record of four bytes: a near pointer to its name, then a number; two of them at 0x10 and 0x14, their
        // names at 0x40 and 0x48 of the same image
        var record = new StateLayout<Named>("named")
            .TextPointer(0, "+0 name", (n, text) => n.Name = text)
            .Int16(2, "+2 number", n => n.Number, (n, v) => n.Number = v);
        var layout = new StateLayout<Pair>("image")
            .Part(0x10, "first", record, p => p.First)
            .Part(0x14, "second", record, p => p.Second);
        var image = new byte[0x60];
        "Berlin\0"u8.CopyTo(image.AsSpan(0x40));
        "Rome\0"u8.CopyTo(image.AsSpan(0x48));
        (image[0x10], image[0x12], image[0x14], image[0x16]) = (0x40, 7, 0x48, 9);

        var pair = new Pair();
        layout.Load(image, pair);
        Assert.Equal(("Berlin", 7, "Rome", 9), (pair.First.Name, (int)pair.First.Number, pair.Second.Name, (int)pair.Second.Number));

        // storing writes the number and leaves the pointer and the text it names as they are
        pair.First.Number = 8;
        var stored = (byte[])image.Clone();
        layout.Store(pair, stored);
        image[0x12] = 8;
        Assert.Equal(image, stored);
        Assert.Equal("first.+0 name", layout.FieldAt(0x11)?.Name);
    }
}
