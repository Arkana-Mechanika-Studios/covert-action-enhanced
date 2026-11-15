namespace CovertAction.Core.Final;

/// <summary>
/// One place in the Hall of Fame: an agent's best case, with the score that ranks it (the case's rating times the
/// level of play plus one), the code name, and what the case was.
/// </summary>
// orig: struct fame_entry, 44 bytes (src/c/include/final.h); fame_table FINAL DS:b1c0, 6 records
public sealed class FameEntry
{
    /// <summary>The score that ranks the entry; -1 marks an empty place.</summary>
    public short Score { get; set; }

    /// <summary>The agent's code name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The level of play, 0..3, plus 0x80 when the agent was a woman.</summary>
    public short Level { get; set; }

    /// <summary>The plot of the case, 0..15.</summary>
    public short Plot { get; set; }

    /// <summary>The variant of the plot.</summary>
    public short Variant { get; set; }

    /// <summary>The case's rating (its efficiency points).</summary>
    public short Ep { get; set; }

    /// <summary>The month of the agent's career the case ended in; its name is month % 12, its year 1990 + month / 12.</summary>
    public short Month { get; set; }

    /// <summary>Takes over every field of another entry: how the Hall of Fame moves an entry down a place.</summary>
    /// <param name="other">The entry to copy.</param>
    public void CopyFrom(FameEntry other)
    {
        Score = other.Score;
        Name = other.Name;
        Level = other.Level;
        Plot = other.Plot;
        Variant = other.Variant;
        Ep = other.Ep;
        Month = other.Month;
    }
}
