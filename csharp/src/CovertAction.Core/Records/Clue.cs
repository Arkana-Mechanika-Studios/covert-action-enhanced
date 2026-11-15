namespace CovertAction.Core.Records;

/// <summary>
/// A clue the player holds: something learnt about a member of the cast, and how it can be matched to a person.
/// </summary>
// orig: struct Clue, 12 bytes (src/c/include/campaign.h); cl[]: 80 records
public sealed class Clue
{
    /// <summary>The organisation the clue came from; -1 when the clue was removed.</summary>
    public sbyte Sorg { get; set; }

    /// <summary>The city the clue came from.</summary>
    public sbyte Sloc { get; set; }

    /// <summary>The member of the cast the clue is about; -1 marks a free record.</summary>
    public short Act { get; set; }

    /// <summary>What the clue tells: a kind in the high byte (1 or 2 a person, 4 an organisation, 8 a city, 0x10 a member of the cast) and an index in the low byte.</summary>
    public short Info1 { get; set; }

    /// <summary>The clue's key, coded the same way: the car, weapon, address ... that ties it to a person; -1 for none.</summary>
    public short Key1 { get; set; }

    /// <summary>A second subject; nothing in the PC version reads it.</summary>
    public short Info2 { get; set; }

    /// <summary>A second key; nothing in the PC version reads it.</summary>
    public short Key2 { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Clue other)
    {
        Sorg = other.Sorg;
        Sloc = other.Sloc;
        Act = other.Act;
        Info1 = other.Info1;
        Key1 = other.Key1;
        Info2 = other.Info2;
        Key2 = other.Key2;
    }
}
