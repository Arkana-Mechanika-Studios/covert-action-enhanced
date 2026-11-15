using System.Text;
using CovertAction.Msc;

namespace CovertAction.Core.Shared;

/// <summary>
/// A text kept the way the original keeps a text it changes in place: a buffer of fixed size in which the text runs
/// up to an end mark (a zero byte), and whatever was written there before stays behind the end mark. Most of the
/// game's texts are plain strings in the rewrite; this type is for the few the original also reads <em>by
/// position</em>, even past the text's end (the cryptography screen takes three characters of the message at fixed
/// positions for its title), so that those positions hold what they hold in the original.
/// </summary>
/// <remarks>Each character is one byte of the game's character set (the PC's; letters and digits are ASCII).</remarks>
/// <param name="capacity">The buffer's size in bytes, end mark included.</param>
public sealed class TextBuffer(int capacity)
{
    /// <summary>The buffer's bytes: the text, its end mark, and what lies behind it.</summary>
    public byte[] Bytes { get; } = new byte[capacity];

    /// <summary>The number of characters before the end mark.</summary>
    /// <exception cref="MemoryOverrunException">The buffer holds no end mark (the original would read on past it).</exception>
    public int Length
    {
        get
        {
            var end = Array.IndexOf(Bytes, (byte)0);
            return end >= 0 ? end : throw new MemoryOverrunException($"a text of {Bytes.Length} bytes has no end mark");
        }
    }

    /// <summary>The character at a position of the buffer, whether before or behind the text's end.</summary>
    public char this[int index]
    {
        get => (char)Bytes[index];
        set => Bytes[index] = (byte)value;
    }

    /// <summary>
    /// The text up to its end mark. Setting it writes the characters and an end mark over the start of the buffer
    /// and leaves the rest as it is.
    /// </summary>
    /// <exception cref="MemoryOverrunException">The text set does not fit the buffer.</exception>
    public string Text
    {
        get => Encoding.Latin1.GetString(Bytes, 0, Length);
        set
        {
            if (value.Length >= Bytes.Length)
            {
                throw new MemoryOverrunException($"a text of {value.Length} characters into a buffer of {Bytes.Length} bytes");
            }
            Encoding.Latin1.GetBytes(value, Bytes);
            Bytes[value.Length] = 0;
        }
    }

    /// <summary>Makes this buffer a byte-for-byte copy of another of the same size.</summary>
    public void CopyFrom(TextBuffer other) => other.Bytes.CopyTo(Bytes, 0);

    /// <summary>Removes the character at a position: the rest of the text, with its end mark, moves one place to the left.</summary>
    public void RemoveAt(int index)
    {
        var end = Length;
        Array.Copy(Bytes, index + 1, Bytes, index, end - index);
    }

    /// <summary>Ends the text after <paramref name="length"/> characters by putting an end mark there.</summary>
    public void Truncate(int length) => Bytes[length] = 0;

    /// <summary>Turns the letters a..z of the text into capitals; every other character, accented letters included, stays.</summary>
    public void UpperCase()
    {
        for (var i = Length - 1; i >= 0; i--)
        {
            if (Bytes[i] is >= (byte)'a' and <= (byte)'z')
            {
                Bytes[i] -= 'a' - 'A';
            }
        }
    }
}
