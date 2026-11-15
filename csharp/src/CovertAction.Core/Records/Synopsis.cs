namespace CovertAction.Core.Records;

/// <summary>
/// A message waiting for the player or already delivered: an intercepted message of the plot, a sighting, the result of an interrogation.
/// </summary>
// orig: struct Synopsis, 14 bytes (src/c/include/campaign.h); sn[]: 128 records
public sealed class Synopsis
{
    /// <summary>What it is: 0 a message of an event, 1 a departure, 5 an arrival, 6 and 12 the result of an interrogation, 8 someone going into hiding, 11 a sighting; -1 marks a free record.</summary>
    public short Type { get; set; }

    /// <summary>The game minute at which it was queued.</summary>
    public short Time { get; set; }

    /// <summary>The event it carries, or the argument of the other kinds.</summary>
    public short Data { get; set; }

    /// <summary>What the player was told of it: bits 1-3 the parts of the message, 0x8000 decoded.</summary>
    public short Clues { get; set; }

    /// <summary>The strength of the person's trail when it was queued.</summary>
    public short Alert { get; set; }

    /// <summary>The person it belongs to (the sender); -1 for none.</summary>
    public sbyte Who { get; set; }

    /// <summary>The city that person was in.</summary>
    public sbyte Loc { get; set; }

    /// <summary>The other party (the receiver).</summary>
    public sbyte Who2 { get; set; }

    /// <summary>The city the other party was in.</summary>
    public sbyte Loc2 { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Synopsis other)
    {
        Type = other.Type;
        Time = other.Time;
        Data = other.Data;
        Clues = other.Clues;
        Alert = other.Alert;
        Who = other.Who;
        Loc = other.Loc;
        Who2 = other.Who2;
        Loc2 = other.Loc2;
    }
}
