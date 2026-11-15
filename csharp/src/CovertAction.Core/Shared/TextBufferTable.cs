using System.Collections;

namespace CovertAction.Core.Shared;

/// <summary>
/// A table of texts the original keeps in buffers of fixed size that it copies each new text into (strcpy), so that
/// a shorter text leaves the end of a longer one behind its end mark (<see cref="TextBuffer"/>). Read and written as
/// texts; the buffers hold the bytes. The table's length is fixed: texts are replaced, never added or removed.
/// </summary>
/// <param name="capacities">The size of each buffer in bytes, end mark included.</param>
public sealed class TextBufferTable(params int[] capacities) : IList<string>, IReadOnlyList<string>
{
    /// <summary>The buffers, one per text.</summary>
    public TextBuffer[] Buffers { get; } = [.. capacities.Select(size => new TextBuffer(size))];

    /// <summary>The text of a buffer; setting it copies the text into the buffer as strcpy does.</summary>
    public string this[int index]
    {
        get => Buffers[index].Text;
        set => Buffers[index].Text = value;
    }

    /// <inheritdoc/>
    public int Count => Buffers.Length;

    /// <summary>Always false: the texts can be replaced (but not added or removed).</summary>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public int IndexOf(string item) => Array.FindIndex(Buffers, buffer => buffer.Text == item);

    /// <inheritdoc/>
    public bool Contains(string item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public void CopyTo(string[] array, int arrayIndex)
    {
        for (var i = 0; i < Buffers.Length; i++)
        {
            array[arrayIndex + i] = Buffers[i].Text;
        }
    }

    /// <inheritdoc/>
    public IEnumerator<string> GetEnumerator() => Buffers.Select(buffer => buffer.Text).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Not supported: the table has a fixed length.</summary>
    public void Add(string item) => throw new NotSupportedException("the table of texts has a fixed length");

    /// <summary>Not supported: the table has a fixed length.</summary>
    public void Clear() => throw new NotSupportedException("the table of texts has a fixed length");

    /// <summary>Not supported: the table has a fixed length.</summary>
    public void Insert(int index, string item) => throw new NotSupportedException("the table of texts has a fixed length");

    /// <summary>Not supported: the table has a fixed length.</summary>
    public bool Remove(string item) => throw new NotSupportedException("the table of texts has a fixed length");

    /// <summary>Not supported: the table has a fixed length.</summary>
    public void RemoveAt(int index) => throw new NotSupportedException("the table of texts has a fixed length");
}
