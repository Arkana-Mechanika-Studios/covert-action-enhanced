using System.Buffers.Binary;
using System.Text;

namespace CovertAction.Content;

/// <summary>
/// One of the game's three world files (WORLD0.DTA to WORLD2.DTA: Europe, Africa and the Middle East, Central
/// America): the 16 cities and 16 organisations a case is played among. So far only their names are read, which the
/// practice message needs.
/// </summary>
/// <remarks>
/// New code. Layout (the reconstructed campaign.h, docs/data-model/final.md): two words, the number of cities and of
/// organisations; then the city records of 34 bytes (name at +0, 12 bytes); then the organisation records of 36
/// bytes (name at +6, 20 bytes). Names are C strings.
/// </remarks>
public sealed class WorldFile
{
    private const int CityRecord = 34;
    private const int OrgRecord = 36;
    private const int OrgNameOffset = 6;
    private const int CityNameSize = 12;
    private const int OrgNameSize = 20;

    private WorldFile(string[] cityNames, string[] orgNames)
    {
        CityNames = cityNames;
        OrgNames = orgNames;
    }

    /// <summary>The names of the cities (at most 11 characters each).</summary>
    public string[] CityNames { get; }

    /// <summary>The names of the organisations (at most 19 characters each).</summary>
    public string[] OrgNames { get; }

    /// <summary>Reads a world file.</summary>
    /// <exception cref="InvalidDataException">The file is shorter than its own counts say.</exception>
    public static WorldFile Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4)
        {
            throw new InvalidDataException("a world file starts with two counts");
        }
        int cities = BinaryPrimitives.ReadUInt16LittleEndian(file);
        int orgs = BinaryPrimitives.ReadUInt16LittleEndian(file[2..]);
        var orgTable = 4 + cities * CityRecord;
        if (orgTable + orgs * OrgRecord > file.Length)
        {
            throw new InvalidDataException("the world file is shorter than its counts say");
        }
        var cityNames = new string[cities];
        for (var i = 0; i < cities; i++)
        {
            cityNames[i] = Name(file.Slice(4 + i * CityRecord, CityNameSize));
        }
        var orgNames = new string[orgs];
        for (var i = 0; i < orgs; i++)
        {
            orgNames[i] = Name(file.Slice(orgTable + i * OrgRecord + OrgNameOffset, OrgNameSize));
        }
        return new WorldFile(cityNames, orgNames);
    }

    /// <summary>A name as the file stores it: characters up to a zero byte.</summary>
    private static string Name(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }
}
