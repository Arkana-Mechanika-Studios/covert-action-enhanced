using System.Buffers.Binary;
using CovertAction.Core.Graphics;
using System.Text;
using CovertAction.Msc;

namespace CovertAction.Persistence.Layout;

/// <summary>One field of a <see cref="StateLayout{TState}"/>: where it lies in the original's bytes and what it is called.</summary>
/// <param name="Offset">Offset of the field's first byte in the image.</param>
/// <param name="Length">Length in bytes.</param>
/// <param name="Name">The field's name for messages, with its original coordinate (for example "DS:1d72 CODEdiff").</param>
public readonly record struct LayoutField(int Offset, int Length, string Name);

/// <summary>
/// The table that ties a typed state object to the original's bytes: for each field, its offset in a memory image
/// (a part's data segment, the hand-off block, a save file) and how to read and write it. One table serves both
/// directions, so loading and storing cannot drift apart. This is where the original's representation ends: a
/// 16-bit word that is 0 or 1 becomes a <see cref="bool"/>, a zero-terminated text a <see cref="string"/>, a pointer
/// a reference.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Store"/> writes only the listed fields, over an image the caller supplies. Bytes no field covers stay
/// as they were in that image. That is what makes a round trip byte-identical before every byte of a data segment has
/// a meaning: load the state from the original's bytes, run a function, store the state over a copy of the same
/// bytes, and the result differs from the input exactly where the function changed a modelled field.
/// </para>
/// <para>
/// All numbers are little-endian, as on the PC; a character is one byte. Fields may not overlap.
/// </para>
/// </remarks>
/// <typeparam name="TState">The typed state the table describes.</typeparam>
/// <param name="name">What the image is, for messages ("CODE DGROUP", "shared block").</param>
public sealed class StateLayout<TState>(string name)
{
    private delegate void Loader(ReadOnlySpan<byte> field, TState state);

    /// <summary>Reads a field that needs more of the image than its own bytes: a pointer, whose target lies elsewhere in the same image.</summary>
    private delegate void ImageLoader(ReadOnlySpan<byte> field, ReadOnlySpan<byte> image, TState state);

    private delegate void Storer(TState state, Span<byte> field);

    /// <summary>Says why the bytes of a field are a value the typed state cannot hold, or null when they are fine.</summary>
    private delegate string? Checker(ReadOnlySpan<byte> field);

    /// <summary>One field: where it is, how it is read and written; for a byte buffer, the buffer; for some kinds, a check of the bytes.</summary>
    private sealed record Entry(LayoutField Field, Loader Load, Storer Store, Func<TState, byte[]>? Buffer = null, Checker? Check = null, ImageLoader? LoadFromImage = null);

    private readonly List<Entry> _entries = [];

    /// <summary>The records placed with <see cref="Part{TPart}"/>: their offset and how to get them from the state.</summary>
    private readonly List<(int Offset, Func<TState, object> Get)> _parts = [];

    /// <summary>What the image is, for messages.</summary>
    public string Name { get; } = name;

    /// <summary>The fields, in the order they were declared.</summary>
    public IEnumerable<LayoutField> Fields => _entries.Select(e => e.Field);

    /// <summary>The offset just past the last field: the smallest image the table can be used on.</summary>
    public int End => _entries.Count == 0 ? 0 : _entries.Max(e => e.Field.Offset + e.Field.Length);

    /// <summary>A 16-bit word.</summary>
    public StateLayout<TState> Int16(int offset, string name, Func<TState, short> get, Action<TState, short> set) =>
        Add(offset, 2, name,
            (field, state) => set(state, BinaryPrimitives.ReadInt16LittleEndian(field)),
            (state, field) => BinaryPrimitives.WriteInt16LittleEndian(field, get(state)));

    /// <summary>
    /// A 16-bit word the original uses as a yes or no: 0 is false, anything else true. Storing writes 1 or 0 only
    /// when the answer differs from what the image holds, so an unusual "true" in an image survives a round trip.
    /// </summary>
    public StateLayout<TState> Flag(int offset, string name, Func<TState, bool> get, Action<TState, bool> set) =>
        Add(offset, 2, name,
            (field, state) => set(state, BinaryPrimitives.ReadInt16LittleEndian(field) != 0),
            (state, field) => StoreFlag(field, get(state)));

    /// <summary>An array of yes-or-no words (see <see cref="Flag"/>); the state owns an array of that length.</summary>
    public StateLayout<TState> FlagArray(int offset, int count, string name, Func<TState, bool[]> array) =>
        Add(offset, 2 * count, name,
            (field, state) =>
            {
                var values = Sized(array(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReadInt16LittleEndian(field[(2 * i)..]) != 0;
                }
            },
            (state, field) =>
            {
                var values = Sized(array(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    StoreFlag(field.Slice(2 * i, 2), values[i]);
                }
            });

    private static void StoreFlag(Span<byte> word, bool value)
    {
        if (BinaryPrimitives.ReadInt16LittleEndian(word) != 0 != value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(word, (short)(value ? 1 : 0));
        }
    }

    /// <summary>A single signed byte (a C <c>char</c> used as a small number).</summary>
    public StateLayout<TState> Char(int offset, string name, Func<TState, sbyte> get, Action<TState, sbyte> set) =>
        Add(offset, 1, name,
            (field, state) => set(state, unchecked((sbyte)field[0])),
            (state, field) => field[0] = unchecked((byte)get(state)));

    /// <summary>A 32-bit number.</summary>
    public StateLayout<TState> UInt32(int offset, string name, Func<TState, uint> get, Action<TState, uint> set) =>
        Add(offset, 4, name,
            (field, state) => set(state, BinaryPrimitives.ReadUInt32LittleEndian(field)),
            (state, field) => BinaryPrimitives.WriteUInt32LittleEndian(field, get(state)));

    /// <summary>A table of bytes of a fixed length (a colour table, a text buffer the game reads by position); the state owns a buffer of that length.</summary>
    public StateLayout<TState> Bytes(int offset, int length, string name, Func<TState, byte[]> buffer) =>
        Add(offset, length, name,
            (field, state) => field.CopyTo(Sized(buffer(state), length, name)),
            (state, field) => Sized(buffer(state), length, name).CopyTo(field),
            buffer);

    /// <summary>A table of characters of a fixed length, one byte each (the cipher alphabet); the state owns an array of that length.</summary>
    public StateLayout<TState> Chars(int offset, int length, string name, Func<TState, char[]> table) =>
        Add(offset, length, name,
            (field, state) => Encoding.Latin1.GetChars(field, Sized(table(state), length, name)),
            (state, field) => Encoding.Latin1.GetBytes(Sized(table(state), length, name), field));

    /// <summary>
    /// A text in a buffer of <paramref name="length"/> bytes: the characters up to the first zero byte. Storing
    /// writes the characters and the end mark and leaves the rest of the buffer as it is.
    /// </summary>
    /// <exception cref="MemoryOverrunException">On storing: the text does not fit the buffer with its end mark.</exception>
    public StateLayout<TState> Text(int offset, int length, string name, Func<TState, string> get, Action<TState, string> set) =>
        Add(offset, length, name,
            (field, state) =>
            {
                var end = field.IndexOf((byte)0);
                set(state, Encoding.Latin1.GetString(end < 0 ? field : field[..end]));
            },
            (state, field) =>
            {
                var text = get(state);
                if (text.Length >= length)
                {
                    throw new MemoryOverrunException($"{Name}: {name}: a text of {text.Length} characters into a buffer of {length} bytes");
                }
                Encoding.Latin1.GetBytes(text, field);
                // DIVERGENCE: D-22 the state keeps the text only: the bytes of the original's buffer behind its end mark are not kept.
                field[text.Length] = 0;
            },
            check: field => field.IndexOf((byte)0) < 0 ? $"{name} has no end mark" : null);

    /// <summary>
    /// A table of images: in the original one word per image (its place in memory, 0 for none), in the state a
    /// reference per image. The state's register of images translates between the two.
    /// </summary>
    public StateLayout<TState> ImageArray(int offset, int count, string name, Func<TState, Image?[]> images, Func<TState, ImageRegister> register) =>
        Add(offset, 2 * count, name,
            (field, state) =>
            {
                var values = Sized(images(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    values[i] = register(state).For(BinaryPrimitives.ReadUInt16LittleEndian(field[(2 * i)..]));
                }
            },
            (state, field) =>
            {
                var values = Sized(images(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(field[(2 * i)..], (ushort)register(state).NameOf(values[i]));
                }
            });

    /// <summary>An array of 16-bit words of a fixed number of elements; the state owns an array of that length.</summary>
    public StateLayout<TState> Int16Array(int offset, int count, string name, Func<TState, short[]> array) =>
        Add(offset, 2 * count, name,
            (field, state) =>
            {
                var values = Sized(array(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReadInt16LittleEndian(field[(2 * i)..]);
                }
            },
            (state, field) =>
            {
                var values = Sized(array(state), count, name);
                for (var i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(field[(2 * i)..], values[i]);
                }
            });

    /// <summary>
    /// A near pointer variable: two bytes holding the address of one of a few known objects of the same image. The
    /// state holds a reference to the object; the table translates between reference and address.
    /// </summary>
    /// <remarks>
    /// An address that is none of the known ones cannot be held by the typed state: <see cref="OutsideModel"/> reports
    /// it, and <see cref="Load"/> then leaves the state's reference as it is.
    /// </remarks>
    /// <typeparam name="TTarget">The type of the objects the pointer can point at.</typeparam>
    /// <param name="offset">Offset of the pointer variable.</param>
    /// <param name="name">Its name for messages.</param>
    /// <param name="get">Reads the reference from the state.</param>
    /// <param name="set">Stores the reference in the state.</param>
    /// <param name="targets">The objects the pointer may point at, each with its address in the image.</param>
    public StateLayout<TState> Pointer<TTarget>(int offset, string name, Func<TState, TTarget> get, Action<TState, TTarget> set,
        params (int Address, Func<TState, TTarget> Target)[] targets)
        where TTarget : class =>
        Add(offset, 2, name,
            (field, state) =>
            {
                var address = BinaryPrimitives.ReadUInt16LittleEndian(field);
                foreach (var (known, target) in targets)
                {
                    if (known == address)
                    {
                        set(state, target(state));
                    }
                }
            },
            (state, field) =>
            {
                var target = get(state);
                var address = targets.First(t => ReferenceEquals(t.Target(state), target)).Address;
                BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)address);
            },
            check: field => PointsAt(field, name, [.. targets.Select(t => t.Address)]));

    /// <summary>
    /// A near pointer variable that is either null or points at one fixed place: the state only knows whether it is set.
    /// </summary>
    public StateLayout<TState> PointerFlag(int offset, string name, int address, Func<TState, bool> get, Action<TState, bool> set) =>
        Add(offset, 2, name,
            (field, state) => set(state, BinaryPrimitives.ReadUInt16LittleEndian(field) == address),
            (state, field) => BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)(get(state) ? address : 0)),
            check: field => PointsAt(field, name, [0, address]));

    /// <summary>
    /// A near pointer variable that always points at the same place (an entry of a table of text pointers). The state
    /// has nothing for it; an image in which it points elsewhere is outside the model.
    /// </summary>
    public StateLayout<TState> FixedPointer(int offset, string name, int address) =>
        Add(offset, 2, name, (_, _) => { }, (_, _) => { }, check: field => PointsAt(field, name, [address]));

    /// <summary>
    /// A near pointer to a text of the program (<c>char *</c>): two bytes holding the address of a zero-terminated
    /// text elsewhere in the same image. The state holds the text. Storing writes nothing: the pointer and the text
    /// it names are the program's own data and stay as the image has them, so a pointer the original changes shows
    /// as a difference in this field.
    /// </summary>
    /// <remarks>Only for a layout that is used on the whole image the pointer refers to (a data segment), by itself or as a part of that image's layout.</remarks>
    public StateLayout<TState> TextPointer(int offset, string name, Action<TState, string> set)
    {
        _entries.Add(new Entry(Clear(offset, 2, name), (_, _) => { }, (_, _) => { },
            LoadFromImage: (field, image, state) =>
            {
                var address = BinaryPrimitives.ReadUInt16LittleEndian(field);
                var text = address < image.Length ? image[address..] : [];
                var end = text.IndexOf((byte)0);
                set(state, Encoding.Latin1.GetString(end < 0 ? text : text[..end]));
            }));
        return this;
    }

    private static string? PointsAt(ReadOnlySpan<byte> field, string name, int[] allowed)
    {
        var address = BinaryPrimitives.ReadUInt16LittleEndian(field);
        return allowed.Contains(address) ? null : $"{name} points at 0x{address:x4}, not at an object of the model";
    }

    /// <summary>A record inside the state that has a layout of its own (a drawing port, a register block), placed at <paramref name="offset"/>.</summary>
    /// <typeparam name="TPart">The record's type.</typeparam>
    public StateLayout<TState> Part<TPart>(int offset, string name, StateLayout<TPart> layout, Func<TState, TPart> part)
        where TPart : class
    {
        foreach (var entry in layout._entries)
        {
            Add(offset + entry.Field.Offset, entry.Field.Length, $"{name}.{entry.Field.Name}",
                (field, state) => entry.Load(field, part(state)),
                (state, field) => entry.Store(part(state), field),
                entry.Buffer is { } buffer ? state => buffer(part(state)) : null,
                entry.Check is { } check ? field => check(field) : null);
            if (entry.LoadFromImage is { } fromImage)
            {
                // a pointer inside the record names a place of the whole image this layout is used on
                _entries[^1] = _entries[^1] with { LoadFromImage = (field, image, state) => fromImage(field, image, part(state)) };
            }
        }
        _parts.Add((offset, part));
        return this;
    }

    /// <summary>Reads every field of <paramref name="state"/> from <paramref name="image"/>.</summary>
    /// <exception cref="ArgumentException">The image is shorter than the table.</exception>
    public void Load(ReadOnlySpan<byte> image, TState state)
    {
        CheckSize(image.Length);
        foreach (var entry in _entries)
        {
            var field = image.Slice(entry.Field.Offset, entry.Field.Length);
            if (entry.LoadFromImage is { } fromImage)
            {
                fromImage(field, image, state);
            }
            else
            {
                entry.Load(field, state);
            }
        }
    }

    /// <summary>Writes every field of <paramref name="state"/> into <paramref name="image"/>; all other bytes of the image are left as they are.</summary>
    /// <exception cref="ArgumentException">The image is shorter than the table.</exception>
    public void Store(TState state, Span<byte> image)
    {
        CheckSize(image.Length);
        foreach (var entry in _entries)
        {
            entry.Store(state, image.Slice(entry.Field.Offset, entry.Field.Length));
        }
    }

    /// <summary>
    /// Why the typed state cannot hold what <paramref name="image"/> contains: one reason per field whose bytes are no
    /// value of the model (a pointer to an unknown place, a text without its end mark). Empty for every image the
    /// program itself can produce.
    /// </summary>
    public IReadOnlyList<string> OutsideModel(ReadOnlySpan<byte> image)
    {
        CheckSize(image.Length);
        var reasons = new List<string>();
        foreach (var entry in _entries)
        {
            if (entry.Check?.Invoke(image.Slice(entry.Field.Offset, entry.Field.Length)) is { } reason)
            {
                reasons.Add(reason);
            }
        }
        return reasons;
    }

    /// <summary>The address in the image of a record of <paramref name="state"/> that was placed with <see cref="Part{TPart}"/>, or null.</summary>
    public int? AddressOf(TState state, object record)
    {
        foreach (var (offset, get) in _parts)
        {
            if (ReferenceEquals(get(state), record))
            {
                return offset;
            }
        }
        return null;
    }

    /// <summary>The address in the image of a byte buffer of <paramref name="state"/> (a field declared with <see cref="Bytes"/>), or null.</summary>
    public int? AddressOf(TState state, byte[] buffer)
    {
        foreach (var entry in _entries)
        {
            if (entry.Buffer is { } get && ReferenceEquals(get(state), buffer))
            {
                return entry.Field.Offset;
            }
        }
        return null;
    }

    /// <summary>The field that covers the byte at <paramref name="offset"/>, or null when no field does (a byte the state does not model).</summary>
    public LayoutField? FieldAt(int offset)
    {
        foreach (var entry in _entries)
        {
            if (offset >= entry.Field.Offset && offset < entry.Field.Offset + entry.Field.Length)
            {
                return entry.Field;
            }
        }
        return null;
    }

    private StateLayout<TState> Add(int offset, int length, string name, Loader load, Storer store,
        Func<TState, byte[]>? buffer = null, Checker? check = null)
    {
        _entries.Add(new Entry(Clear(offset, length, name), load, store, buffer, check));
        return this;
    }

    /// <summary>A new field's place, after making sure no other field lies there.</summary>
    private LayoutField Clear(int offset, int length, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var clash = _entries.FirstOrDefault(e => offset < e.Field.Offset + e.Field.Length && e.Field.Offset < offset + length);
        if (clash is not null)
        {
            throw new InvalidOperationException($"{Name}: {name} at 0x{offset:x} overlaps {clash.Field.Name} at 0x{clash.Field.Offset:x}");
        }
        return new LayoutField(offset, length, name);
    }

    private void CheckSize(int imageLength)
    {
        if (imageLength < End)
        {
            throw new ArgumentException($"{Name}: the image has {imageLength} bytes, the layout needs {End}");
        }
    }

    private T[] Sized<T>(T[] array, int length, string name) =>
        array.Length == length
            ? array
            : throw new InvalidOperationException($"{Name}: {name} must have {length} elements, the state's has {array.Length}");
}
