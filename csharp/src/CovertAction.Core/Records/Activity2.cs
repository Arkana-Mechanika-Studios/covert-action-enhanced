namespace CovertAction.Core.Records;

/// <summary>
/// The second, short record of a member of the cast: when the member acts and how far the investigation into the member has come.
/// </summary>
// orig: struct Activity2, 6 bytes (src/c/include/campaign.h); ac2[]: one record per member of the cast
public sealed class Activity2
{
    /// <summary>The step of the plot at which the member acts; -1 when never.</summary>
    public short Chron { get; set; }

    /// <summary>What the investigation has gathered about the member; with the cover it decides when another clue turns up.</summary>
    public short Intel { get; set; }

    /// <summary>Kept and carried; its use is not established yet.</summary>
    public short Firstwho { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Activity2 other)
    {
        Chron = other.Chron;
        Intel = other.Intel;
        Firstwho = other.Firstwho;
    }
}
