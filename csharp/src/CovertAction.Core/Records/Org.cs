namespace CovertAction.Core.Records;

/// <summary>
/// One of the 16 organisations of the world: the four intelligence services first, then the groups a case can involve.
/// </summary>
// orig: struct Org, 36 bytes (src/c/include/campaign.h); org[]: 16 records, from WORLDn.DTA
public sealed class Org
{
    /// <summary>The short name (the label on the car chase's map, the electronics part's message log).</summary>
    public string Sname { get; set; } = "";

    /// <summary>The organisation's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The map grid cell of the organisation's base, coded as a city's.</summary>
    public short Climate { get; set; }

    /// <summary>How strong the organisation is: the numerator when its presence in a city is worked out.</summary>
    public short Size { get; set; }

    /// <summary>How hidden it is: the divisor of the same computation.</summary>
    public short Covert { get; set; }

    /// <summary>Two meanings, one per phase. In the world file and while a case is set up: the low byte is the number of the MasterMind who leads the organisation and the high byte a mask of the plots it can take part in (-1: no MasterMind). After the set-up has cleared it: bit 0 says the player knows the organisation.</summary>
    public short Clues { get; set; }

    /// <summary>Where the names of the organisation's people start in the tables of first names and surnames.</summary>
    public sbyte Race { get; set; }

    /// <summary>The high byte of the file's word; always 0, never read.</summary>
    public sbyte RaceHi { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Org other)
    {
        Sname = other.Sname;
        Name = other.Name;
        Climate = other.Climate;
        Size = other.Size;
        Covert = other.Covert;
        Clues = other.Clues;
        Race = other.Race;
        RaceHi = other.RaceHi;
    }
}
