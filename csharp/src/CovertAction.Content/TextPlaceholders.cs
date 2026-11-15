namespace CovertAction.Content;

/// <summary>
/// The placeholders of the game's texts: a text of TEXT.DTA, CLUES.TXT or PLOT.TXT names what is to be filled in
/// with words that start with a dollar sign ("Greetings from $SNDLOC."), and the game puts the case's names there.
/// </summary>
/// <remarks>
/// As the menu part does it (FINAL's substitute_macros, FINAL 0100:75f9): the placeholders are taken
/// in the order of the list, and each replaces its first occurrence in the line only. The raid's and the
/// electronics part's routine (replace_tokens) is other code and is in the electronics part.
/// </remarks>
public static class TextPlaceholders
{
    /// <summary>Fills a line's placeholders in.</summary>
    /// <param name="line">The line as the text file has it.</param>
    /// <param name="values">Each placeholder, with its dollar sign, and what stands for it.</param>
    public static string FillIn(string line, IEnumerable<(string Placeholder, string Value)> values)
    {
        foreach (var (placeholder, value) in values)
        {
            var at = line.IndexOf(placeholder, StringComparison.Ordinal);
            if (at >= 0)
            {
                line = line[..at] + value + line[(at + placeholder.Length)..];
            }
        }
        return line;
    }
}
