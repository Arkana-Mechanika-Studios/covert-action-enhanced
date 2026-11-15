namespace CovertAction.Core.Final;

/// <summary>
/// Texts kept in tables of characters with an end mark ('\0'), for the few variables of the menus part that the
/// original writes into by position (a clock template, a file name with its number put in).
/// </summary>
public static class CharTable
{
    /// <summary>Writes a text and its end mark into a table at <paramref name="at"/>, as the original's strcpy does; the rest of the table stays.</summary>
    /// <param name="table">The table.</param>
    /// <param name="at">Where the text begins.</param>
    /// <param name="text">The text.</param>
    public static void Put(char[] table, int at, string text)
    {
        text.CopyTo(0, table, at, text.Length);
        table[at + text.Length] = '\0';
    }

    /// <summary>The text a table holds: its characters up to the end mark.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="ArgumentOutOfRangeException">The table has no end mark.</exception>
    public static string TextOf(char[] table) => new(table, 0, Array.IndexOf(table, '\0'));
}
