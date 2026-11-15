namespace CovertAction.Core.Records;

/// <summary>
/// One of the up to four objects a plot turns on (the blueprints, the money, the hostage).
/// </summary>
// orig: struct MacGuffins, 18 bytes (src/c/include/campaign.h); mc[]: 4 records, from CRIMEn.DTA
public sealed class MacGuffins
{
    /// <summary>The object's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The number of the object's picture on the sheet of objects.</summary>
    public sbyte Type { get; set; }

    /// <summary>The person who holds it; -1 for nobody.</summary>
    public sbyte Who { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(MacGuffins other)
    {
        Name = other.Name;
        Type = other.Type;
        Who = other.Who;
    }
}
