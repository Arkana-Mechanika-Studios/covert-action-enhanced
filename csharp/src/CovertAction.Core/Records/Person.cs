namespace CovertAction.Core.Records;

/// <summary>
/// Someone the player can learn about: a member of the plot, a decoy or a bystander of an organisation. The members of a case's cast are mapped onto these records.
/// </summary>
// orig: struct Person, 16 bytes (src/c/include/campaign.h); ps[]: 64 records
public sealed class Person
{
    /// <summary>The organisation the person belongs to; -1 marks a free record.</summary>
    public short Org { get; set; }

    /// <summary>The person's looks and name in one word: bit 0 male, bits 1-4 the first name, bits 5-8 the surname, the higher bits the portrait.</summary>
    public short Face { get; set; }

    /// <summary>The person's home city.</summary>
    public short Loc { get; set; }

    /// <summary>What the player knows, one bit each: 0x01 photo, 0x02 name, 0x04 organisation, 0x08 spotted in the city, 0x10 known as a participant, 0x20 further information, 0x40 selected, 0x80 photo forgotten, 0x100 rank, 0x200 and 0x400 recruiting information.</summary>
    public short Clues { get; set; }

    /// <summary>Bit n: the person is member n of the cast; 0x8000: a decoy (a "red herring"). Only the set-up of a case writes it.</summary>
    public short Role { get; set; }

    /// <summary>Bit n: the person was seen as cast member n.</summary>
    public short Rknown { get; set; }

    /// <summary>The running score of the investigation into the person; the debriefing reuses it for what was known at the end of the case.</summary>
    public short Intel { get; set; }

    /// <summary>From 0 on: at large, the strength (0..99) of the trail, which fades with every plot step. -1 arrested, -2 in hiding, -3 turned (a double agent), -4 in custody.</summary>
    public short Alert { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Person other)
    {
        Org = other.Org;
        Face = other.Face;
        Loc = other.Loc;
        Clues = other.Clues;
        Role = other.Role;
        Rknown = other.Rknown;
        Intel = other.Intel;
        Alert = other.Alert;
    }
}
