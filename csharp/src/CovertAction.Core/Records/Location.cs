namespace CovertAction.Core.Records;

/// <summary>
/// A building: one organisation in one city. The original calls it a Location; the player taps it, searches it, raids it.
/// </summary>
// orig: struct Location, 12 bytes (src/c/include/campaign.h); lc[]: 256 records
public sealed class Location
{
    /// <summary>The organisation.</summary>
    public short Org { get; set; }

    /// <summary>The city.</summary>
    public short Loc { get; set; }

    /// <summary>What is known about the building; bit 4: the player knows where it is.</summary>
    public short Clues { get; set; }

    /// <summary>How well the building is wiretapped, in per cent.</summary>
    public short Tapped { get; set; }

    /// <summary>One bit per room: set when the room was explored in a raid.</summary>
    public uint Seen { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Location other)
    {
        Org = other.Org;
        Loc = other.Loc;
        Clues = other.Clues;
        Tapped = other.Tapped;
        Seen = other.Seen;
    }
}
