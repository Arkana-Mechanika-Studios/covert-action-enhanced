using System.Text;

namespace CovertAction.Content;

/// <summary>
/// One of the game's text files (TEXT.DTA, CLUES.TXT, PLOT.TXT): plain lines, in which a line that starts with '*'
/// is a tag ("*SLOC03") and the lines after it, up to the next tag, are the text filed under it. Several tags in a
/// row share the text that follows them.
/// </summary>
/// <remarks>
/// New code. It reads an entry the way the original's reader does (FINAL's read_cleared_text, FINAL 0100:750d):
/// the tag must be a whole line, the first match wins, tag lines right after it are skipped. Unlike the original it
/// stops at the end of the file (docs/original-bugs.md CA-17). A character is one byte (Latin-1): the game draws
/// the texts with its own fonts.
/// </remarks>
public sealed class TextFile
{
    private readonly List<string> _lines = [];

    /// <summary>
    /// Splits the file into lines as the original's reader sees them: it reads up to the end of a line and then skips
    /// all blank space, so empty lines and the blanks at the start of a line never reach it. A Ctrl-Z ends the file.
    /// </summary>
    /// <param name="file">The whole file.</param>
    public TextFile(ReadOnlySpan<byte> file)
    {
        const byte endOfFile = 0x1a;
        var end = file.IndexOf(endOfFile);
        var text = Encoding.Latin1.GetString(end >= 0 ? file[..end] : file);
        foreach (var line in text.Split('\n'))
        {
            var seen = line.TrimStart().TrimEnd('\r');
            if (seen.Length > 0)
            {
                _lines.Add(seen);
            }
        }
    }

    /// <summary>The lines filed under a tag, in order; empty when the file has no such tag.</summary>
    /// <param name="tag">The tag as it stands in the file, with its '*'.</param>
    public IReadOnlyList<string> Entry(string tag)
    {
        var at = _lines.IndexOf(tag);
        if (at < 0)
        {
            return [];
        }
        // further tags right under the tag share its text
        var first = _lines.FindIndex(at + 1, line => !IsTag(line));
        return first < 0 ? [] : _lines.Skip(first).TakeWhile(line => !IsTag(line)).ToList();
    }

    private static bool IsTag(string line) => line.StartsWith('*');
}
