namespace CovertAction.Core.Records;

/// <summary>
/// One of the 16 cities of the world a case is played in: its names, its place on the map, and what the player knows of the organisations there.
/// </summary>
// orig: struct City, 34 bytes (src/c/include/campaign.h); ct[]: 16 records, from WORLDn.DTA
public sealed class City
{
    /// <summary>The city's name; also the name of its picture file.</summary>
    public string Name { get; set; } = "";

    /// <summary>The second line of the name (the country), shown after a comma.</summary>
    public string Name2 { get; set; } = "";

    /// <summary>The city's cell of the map grid: column in the low four bits, row in the next four. Distances between cities and organisations are measured in cells.</summary>
    public short Climate { get; set; }

    /// <summary>The city's weight when the game works out how strongly an organisation is present in it.</summary>
    public short Police { get; set; }

    /// <summary>One bit per organisation: set when the player knows that the organisation operates here.</summary>
    public short OrgClues { get; set; }

    /// <summary>One bit per organisation: set when the player visited it here since the last step of the plot; every plot step clears it.</summary>
    public short Clued { get; set; }

    /// <summary>The city's position on the map picture, in pixels across; flight times come from the distance between positions.</summary>
    public byte GeoX { get; set; }

    /// <summary>The city's position on the map picture, in pixels down.</summary>
    public byte GeoY { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(City other)
    {
        Name = other.Name;
        Name2 = other.Name2;
        Climate = other.Climate;
        Police = other.Police;
        OrgClues = other.OrgClues;
        Clued = other.Clued;
        GeoX = other.GeoX;
        GeoY = other.GeoY;
    }
}
