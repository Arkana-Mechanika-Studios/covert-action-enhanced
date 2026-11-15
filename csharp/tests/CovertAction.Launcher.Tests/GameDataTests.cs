using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CovertAction.Content;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The readers of the game's pictures, archives and text files: on small data made up for the tests, and on every
/// such file of the player's game against what the project's verified decoders make of it
/// (build/csharp-reference/content.json, written by tools/reference_content.py; skipped without it or the game).
/// </summary>
public sealed class GameDataTests
{
    // ---- pictures on made-up data ----

    /// <summary>A picture file: the header, then the given bytes as 9-bit codes (each byte its own code, lowest bit first).</summary>
    private static byte[] Picture(int width, int height, byte[] packed, byte[]? fourColourTable = null)
    {
        var bits = new List<bool>();
        foreach (var value in packed)
        {
            for (var bit = 0; bit < 9; bit++)
            {
                bits.Add((value >> bit & 1) != 0);
            }
        }
        var codes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            codes[i / 8] |= (byte)(bits[i] ? 1 << (i % 8) : 0);
        }
        byte format = fourColourTable is null ? (byte)7 : (byte)15;
        return [format, 0, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), .. fourColourTable ?? [], 11, .. codes];
    }

    [Fact]
    public void APictureIsTwoPixelsToAByteTheLeftOneInTheLowHalf()
    {
        var picture = PictureFile.Read(Picture(4, 2, [0x21, 0x43, 0x65, 0x87]));
        Assert.Equal((4, 2), (picture.Width, picture.Height));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], picture.Pixels);
        Assert.Null(picture.FourColourTable);
    }

    [Fact]
    public void ARowOfOddWidthEndsWithHalfAByte()
    {
        var picture = PictureFile.Read(Picture(3, 2, [0x21, 0x03, 0x54, 0x06]));
        Assert.Equal([1, 2, 3, 4, 5, 6], picture.Pixels);
    }

    [Fact]
    public void TheRepeatMarkRepeatsTheByteBeforeItAndWithACountOf0IsAByteItself()
    {
        // 21h, then "the same twice more"; then a plain byte 90h
        var picture = PictureFile.Read(Picture(4, 2, [0x21, 0x90, 0x03, 0x90, 0x00]));
        Assert.Equal([1, 2, 1, 2, 1, 2, 0, 9], picture.Pixels);
    }

    [Fact]
    public void ARepeatCountOf1Repeats256Times()
    {
        // docs/original-bugs.md CA-11
        var picture = PictureFile.Read(Picture(2, 257, [0x21, 0x90, 0x01]));
        Assert.Equal(514, picture.Pixels.Length);
        Assert.All(picture.Pixels.Chunk(2), pair => Assert.Equal([1, 2], pair));
    }

    [Fact]
    public void APictureOfFormat15CarriesATableForFourColourDisplays()
    {
        byte[] table = [.. Enumerable.Range(100, 16).Select(i => (byte)i)];
        var data = Picture(2, 1, [0x21], table);
        var picture = PictureFile.Read([.. data, 0xee, 0xee]);
        Assert.Equal(table, picture.FourColourTable);
        Assert.Equal([1, 2], picture.Pixels);
        Assert.Equal(data.Length, picture.Length);      // what follows the picture is not read
    }

    [Fact]
    public void ARepeatedSequenceComesFromTheTable()
    {
        // codes: 21h, 43h, then 256 (the table's first entry of this picture's own making: 21h 43h), then 257 right
        // as it is being made (43h 21h and its own first byte again)
        var bits = new List<bool>();
        foreach (var code in new[] { 0x21, 0x43, 257, 259 })
        {
            for (var bit = 0; bit < 9; bit++)
            {
                bits.Add((code >> bit & 1) != 0);
            }
        }
        var codes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            codes[i / 8] |= (byte)(bits[i] ? 1 << (i % 8) : 0);
        }
        // entry 256 is the stale one of the first code (CA-11); 257 = 21h 43h; 258 = 43h + 21h; 259 = (21h 43h) + its first byte
        var picture = PictureFile.Read([7, 0, 14, 0, 1, 0, 11, .. codes]);
        Assert.Equal([1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4, 1, 2], picture.Pixels);
    }

    [Fact]
    public void DamagedPicturesAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => PictureFile.Read([7, 0, 4]));
        Assert.Throws<InvalidDataException>(() => PictureFile.Read(Picture(4, 2, [0x21])));             // ends too early
        Assert.Throws<InvalidDataException>(() => PictureFile.Read(Picture(4, 2, [0x21, 0x90, 0x09]))); // runs past the last row
        Assert.Throws<InvalidDataException>(() => PictureFile.Read([9, 0, 4, 0, 2, 0, 11, 0, 0]));      // no such format
    }

    // ---- archives on made-up data ----

    [Fact]
    public void AnArchiveListsItsFilesAndGivesTheirBytes()
    {
        var file = new byte[2 + 2 * 24 + 5];
        file[0] = 2;
        "FIRST.PIC"u8.CopyTo(file.AsSpan(2));
        file[2 + 16] = 2;       // size
        file[2 + 20] = 50;      // place
        "TWELVECH.PIC"u8.CopyTo(file.AsSpan(26));
        file[26 + 16] = 3;
        file[26 + 20] = 52;
        byte[] contents = [1, 2, 3, 4, 5];
        contents.CopyTo(file, 50);
        var archive = new CatArchive(file);
        Assert.Equal(["FIRST.PIC", "TWELVECH.PIC"], archive.Entries.Select(e => e.Name));
        Assert.Equal([1, 2], archive.Contents(archive.Find("first.pic")!).ToArray());
        Assert.Equal([3, 4, 5], archive.Contents(archive.Entries[1]).ToArray());
        Assert.Null(archive.Find("OTHER.PIC"));

        file[26 + 16] = 4;      // one byte past the archive's end
        Assert.Throws<InvalidDataException>(() => new CatArchive(file));
    }

    // ---- placeholders ----

    [Fact]
    public void APlaceholderIsFilledInOnceInTheOrderOfTheList()
    {
        var values = new[] { ("$SNDORG", "the Mafia"), ("$SNDLOC", "Rome") };
        Assert.Equal("Rome: the Mafia, and $SNDORG again.", TextPlaceholders.FillIn("$SNDLOC: $SNDORG, and $SNDORG again.", values));
        Assert.Equal("nothing to fill in", TextPlaceholders.FillIn("nothing to fill in", values));
    }

    // ---- every file of the player's game ----

    private static (GameFolder Game, JsonElement Reference) GameAndReference()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var game = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            var reference = Path.Combine(dir.FullName, "build", "csharp-reference", "content.json");
            if (File.Exists(Path.Combine(game, "CODE.EXE")) && File.Exists(reference))
            {
                return (new GameFolder(game), JsonDocument.Parse(File.ReadAllBytes(reference)).RootElement);
            }
        }
        throw new SkipException("needs the game's files and build/csharp-reference/content.json (python -B csharp/tools/reference_content.py)");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [SkippableFact]
    public void EveryPictureOfTheGameDecodesToTheVerifiedPixels()
    {
        var (game, reference) = GameAndReference();
        var pictures = new Dictionary<string, PictureFile>();
        foreach (var file in Directory.EnumerateFiles(game.Path))
        {
            var name = Path.GetFileName(file).ToUpperInvariant();
            if (name.EndsWith(".PIC", StringComparison.Ordinal))
            {
                pictures[name] = PictureFile.Read(File.ReadAllBytes(file));
            }
            else if (name.EndsWith(".CAT", StringComparison.Ordinal))
            {
                var archive = new CatArchive(File.ReadAllBytes(file));
                foreach (var entry in archive.Entries)
                {
                    var picture = PictureFile.Read(archive.Contents(entry));
                    Assert.True(picture.Length <= entry.Size, $"{name}/{entry.Name}: the picture is longer than its place in the archive");
                    pictures[$"{name}/{entry.Name}"] = picture;
                }
            }
        }
        var expected = reference.GetProperty("pictures");
        Assert.Equal(expected.EnumerateObject().Select(p => p.Name).Order(), pictures.Keys.Order());
        foreach (var (name, picture) in pictures)
        {
            var verified = expected.GetProperty(name);
            Assert.True((picture.Width, picture.Height) == (verified.GetProperty("width").GetInt32(), verified.GetProperty("height").GetInt32()), $"{name}: size");
            Assert.True(Sha256(picture.Pixels) == verified.GetProperty("sha256").GetString(), $"{name}: the pixels differ from the verified decoders'");
        }
    }

    [SkippableFact]
    public void EveryEntryOfTheGamesTextFilesIsReadAsTheOriginalsReaderReadsIt()
    {
        var (game, reference) = GameAndReference();
        var entries = 0;
        foreach (var file in reference.GetProperty("texts").EnumerateObject())
        {
            var texts = new TextFile(game.Read(file.Name));
            foreach (var tag in file.Value.EnumerateObject())
            {
                var lines = string.Join("\n", texts.Entry(tag.Name));
                Assert.True(Sha256(Encoding.Latin1.GetBytes(lines)) == tag.Value.GetString(), $"{file.Name} {tag.Name}: the entry differs");
                entries++;
            }
        }
        Assert.True(entries > 700, $"only {entries} entries compared");
    }

    [SkippableFact]
    public void TheTextFilesKnownOdditiesAreRead()
    {
        var (game, _) = GameAndReference();
        // docs/original-bugs.md CA-16: the Bagman's clue tag has a blank behind it, so the tag asked for is not found
        var clues = new TextFile(game.Read("CLUES.TXT"));
        Assert.Empty(clues.Entry("*C0904"));
        Assert.NotEmpty(clues.Entry("*C0904 "));
        // CA-17: the last entry of PLOT.TXT has no tag after it; it ends with the file
        var plot = new TextFile(game.Read("PLOT.TXT"));
        Assert.NotEmpty(plot.Entry("*PL1526"));
        // CA-12: TEXT.DTA has *MSG0102 twice; the first is read
        var text = new TextFile(game.Read("TEXT.DTA"));
        var raw = Encoding.Latin1.GetString(game.Read("TEXT.DTA")).Replace("\r", "");
        var first = raw.IndexOf("*MSG0102\n", StringComparison.Ordinal);
        Assert.True(raw.IndexOf("*MSG0102\n", first + 1, StringComparison.Ordinal) > first);
        Assert.StartsWith(text.Entry("*MSG0102")[0], raw[(first + "*MSG0102\n".Length)..].TrimStart());
    }
}
