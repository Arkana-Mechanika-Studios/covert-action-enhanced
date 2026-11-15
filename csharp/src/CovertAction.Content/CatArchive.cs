using System.Buffers.Binary;
using System.Text;

namespace CovertAction.Content;

/// <summary>
/// One of the game's archives (a .CAT file): several files, mostly pictures, kept in one, so that a part of the
/// game finds what it shows together on one floppy disk.
/// </summary>
/// <remarks>
/// The format: a word with the number of files, then one record of 24 bytes per file (its name in 12 bytes, padded
/// with zeros when shorter; four bytes the game does not read, a date and time by their look; the file's size; its
/// place in the archive, both 32-bit), then the files. This is the data-level reader; the original's routines
/// that open an archive and look a name up in it are in the launcher's library. The format follows the
/// project's own reference reader (not published).
/// </remarks>
public sealed class CatArchive
{
    private const int RecordSize = 24;
    private const int NameSize = 12;
    private const int SizeAt = 16;
    private const int OffsetAt = 20;

    private readonly byte[] _file;

    /// <summary>Reads an archive's list of files.</summary>
    /// <param name="file">The whole archive.</param>
    /// <exception cref="InvalidDataException">The list does not fit the file.</exception>
    public CatArchive(byte[] file)
    {
        _file = file;
        if (file.Length < 2)
        {
            throw new InvalidDataException("an archive starts with the number of its files");
        }
        int count = BinaryPrimitives.ReadUInt16LittleEndian(file);
        var listEnd = 2 + count * RecordSize;
        if (count == 0 || listEnd > file.Length)
        {
            throw new InvalidDataException("the archive is shorter than its list of files");
        }
        var entries = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            var record = file.AsSpan(2 + i * RecordSize, RecordSize);
            var name = record[..NameSize];
            var nameEnd = name.IndexOf((byte)0);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(record[SizeAt..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[OffsetAt..]);
            if (offset < listEnd || offset > file.Length || size > file.Length - offset)
            {
                throw new InvalidDataException($"file {i} of the archive lies outside it");
            }
            entries.Add(new Entry(Encoding.Latin1.GetString(nameEnd < 0 ? name : name[..nameEnd]), (int)offset, (int)size));
        }
        Entries = entries;
    }

    /// <summary>The files of the archive, in the order of its list.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>The bytes of a file of the archive.</summary>
    public ReadOnlySpan<byte> Contents(Entry entry) => _file.AsSpan(entry.Offset, entry.Size);

    /// <summary>The file with a name (DOS names have no letter case), or null when the archive has none.</summary>
    public Entry? Find(string name) => Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>One file of an archive.</summary>
    /// <param name="Name">Its DOS name, for example "BCHBACK.PIC".</param>
    /// <param name="Offset">Where it starts in the archive.</param>
    /// <param name="Size">Its size in bytes.</param>
    public sealed record Entry(string Name, int Offset, int Size);
}
