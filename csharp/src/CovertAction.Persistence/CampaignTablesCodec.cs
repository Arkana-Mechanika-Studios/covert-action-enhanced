using System.Buffers.Binary;
using CovertAction.Core.Records;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence;

/// <summary>
/// The campaign tables as the original's bytes: the twelve tables one after the other, as they lie in the hand-off
/// block from byte 0x400 on and in a saved campaign behind its header. The cast and the events are as long as their
/// counts say (two words of the block's scalars), so the place of every later table depends on them.
/// </summary>
/// <remarks>
/// Like every layout of this layer, storing writes the modelled fields over an image and leaves the rest of the
/// image as it is (the bytes of a name's buffer behind its end, for example).
/// </remarks>
// orig: the campaign tables at SHARED+0x400, in the order write_env_state writes them (src/c/final/final_0100_a2f6.c): DGROUP tables pl, ac, ac2, ev, ct, org, ps, lc, cl, sn, mc, active
public static class CampaignTablesCodec
{
    /// <summary>The size of the tables for a case with the given cast and events.</summary>
    public static int Size(int cast, int events) =>
        CampaignLayouts.PlotSize + cast * (CampaignLayouts.ActivitySize + CampaignLayouts.Activity2Size) + events * CampaignLayouts.EventSize
        + CampaignTables.WorldSize * (CampaignLayouts.CitySize + CampaignLayouts.OrgSize)
        + CampaignTables.PersonCount * CampaignLayouts.PersonSize + CampaignTables.LocationCount * CampaignLayouts.LocationSize
        + CampaignTables.ClueCount * CampaignLayouts.ClueSize + CampaignTables.SynopsisCount * CampaignLayouts.SynopsisSize
        + CampaignTables.MacGuffinCount * CampaignLayouts.MacGuffinsSize + CampaignTables.ActiveWords * sizeof(short);

    /// <summary>
    /// The sizes of the twelve tables in bytes, in the order they are handed on and saved: the original writes and
    /// reads each table with a call of its own.
    /// </summary>
    /// <param name="tables">The tables; the cast and the events are as long as they are here.</param>
    public static int[] TableSizes(CampaignTables tables) =>
    [
        CampaignLayouts.PlotSize,
        CampaignLayouts.ActivitySize * tables.Cast.Count,
        CampaignLayouts.Activity2Size * tables.CastProgress.Count,
        CampaignLayouts.EventSize * tables.Events.Count,
        CampaignLayouts.CitySize * tables.Cities.Length,
        CampaignLayouts.OrgSize * tables.Orgs.Length,
        CampaignLayouts.PersonSize * tables.Persons.Length,
        CampaignLayouts.LocationSize * tables.Locations.Length,
        CampaignLayouts.ClueSize * tables.Clues.Length,
        CampaignLayouts.SynopsisSize * tables.Synopses.Length,
        CampaignLayouts.MacGuffinsSize * tables.MacGuffins.Length,
        sizeof(short) * tables.Active.Length,
    ];

    /// <summary>Reads the tables.</summary>
    /// <param name="image">The bytes, from the first table on.</param>
    /// <param name="cast">The number of members of the cast (the hand-off block's NCast).</param>
    /// <param name="events">The number of events (the hand-off block's NEvents).</param>
    /// <param name="tables">Receives the records.</param>
    /// <exception cref="ArgumentException">The image is shorter than the tables.</exception>
    public static void Load(ReadOnlySpan<byte> image, int cast, int events, CampaignTables tables)
    {
        if (cast < 0 || events < 0 || Size(cast, events) > image.Length)
        {
            throw new ArgumentException($"campaign tables for a cast of {cast} and {events} events do not fit {image.Length} bytes");
        }
        Resize(tables.Cast, cast);
        Resize(tables.CastProgress, cast);
        Resize(tables.Events, events);
        var at = 0;
        CampaignLayouts.Plot.Load(Next(image, ref at, CampaignLayouts.PlotSize), tables.Plot);
        LoadTable(image, ref at, CampaignLayouts.Activity, CampaignLayouts.ActivitySize, tables.Cast);
        LoadTable(image, ref at, CampaignLayouts.Activity2, CampaignLayouts.Activity2Size, tables.CastProgress);
        LoadTable(image, ref at, CampaignLayouts.Event, CampaignLayouts.EventSize, tables.Events);
        LoadTable(image, ref at, CampaignLayouts.City, CampaignLayouts.CitySize, tables.Cities);
        LoadTable(image, ref at, CampaignLayouts.Org, CampaignLayouts.OrgSize, tables.Orgs);
        LoadTable(image, ref at, CampaignLayouts.Person, CampaignLayouts.PersonSize, tables.Persons);
        LoadTable(image, ref at, CampaignLayouts.Location, CampaignLayouts.LocationSize, tables.Locations);
        LoadTable(image, ref at, CampaignLayouts.Clue, CampaignLayouts.ClueSize, tables.Clues);
        LoadTable(image, ref at, CampaignLayouts.Synopsis, CampaignLayouts.SynopsisSize, tables.Synopses);
        LoadTable(image, ref at, CampaignLayouts.MacGuffins, CampaignLayouts.MacGuffinsSize, tables.MacGuffins);
        for (var i = 0; i < tables.Active.Length; i++)
        {
            tables.Active[i] = BinaryPrimitives.ReadInt16LittleEndian(Next(image, ref at, sizeof(short)));
        }
    }

    /// <summary>Writes the tables over an image, for the cast and events the tables hold.</summary>
    /// <param name="tables">The records.</param>
    /// <param name="image">The bytes to write over, from the first table on.</param>
    /// <exception cref="ArgumentException">The image is shorter than the tables.</exception>
    public static void Store(CampaignTables tables, Span<byte> image)
    {
        if (tables.CastProgress.Count != tables.Cast.Count || Size(tables.Cast.Count, tables.Events.Count) > image.Length)
        {
            throw new ArgumentException($"campaign tables for a cast of {tables.Cast.Count} and {tables.Events.Count} events do not fit {image.Length} bytes");
        }
        var at = 0;
        CampaignLayouts.Plot.Store(tables.Plot, NextToWrite(image, ref at, CampaignLayouts.PlotSize));
        StoreTable(image, ref at, CampaignLayouts.Activity, CampaignLayouts.ActivitySize, tables.Cast);
        StoreTable(image, ref at, CampaignLayouts.Activity2, CampaignLayouts.Activity2Size, tables.CastProgress);
        StoreTable(image, ref at, CampaignLayouts.Event, CampaignLayouts.EventSize, tables.Events);
        StoreTable(image, ref at, CampaignLayouts.City, CampaignLayouts.CitySize, tables.Cities);
        StoreTable(image, ref at, CampaignLayouts.Org, CampaignLayouts.OrgSize, tables.Orgs);
        StoreTable(image, ref at, CampaignLayouts.Person, CampaignLayouts.PersonSize, tables.Persons);
        StoreTable(image, ref at, CampaignLayouts.Location, CampaignLayouts.LocationSize, tables.Locations);
        StoreTable(image, ref at, CampaignLayouts.Clue, CampaignLayouts.ClueSize, tables.Clues);
        StoreTable(image, ref at, CampaignLayouts.Synopsis, CampaignLayouts.SynopsisSize, tables.Synopses);
        StoreTable(image, ref at, CampaignLayouts.MacGuffins, CampaignLayouts.MacGuffinsSize, tables.MacGuffins);
        foreach (var counter in tables.Active)
        {
            BinaryPrimitives.WriteInt16LittleEndian(NextToWrite(image, ref at, sizeof(short)), counter);
        }
    }

    private static void LoadTable<T>(ReadOnlySpan<byte> image, ref int at, StateLayout<T> layout, int size, IList<T> records)
    {
        foreach (var record in records)
        {
            layout.Load(Next(image, ref at, size), record);
        }
    }

    private static void StoreTable<T>(Span<byte> image, ref int at, StateLayout<T> layout, int size, IList<T> records)
    {
        foreach (var record in records)
        {
            layout.Store(record, NextToWrite(image, ref at, size));
        }
    }

    private static ReadOnlySpan<byte> Next(ReadOnlySpan<byte> image, ref int at, int size)
    {
        at += size;
        return image.Slice(at - size, size);
    }

    private static Span<byte> NextToWrite(Span<byte> image, ref int at, int size)
    {
        at += size;
        return image.Slice(at - size, size);
    }

    /// <summary>Makes a list of records exactly as long as its count says, keeping the records it has.</summary>
    private static void Resize<T>(List<T> records, int count)
        where T : new()
    {
        while (records.Count < count)
        {
            records.Add(new T());
        }
        records.RemoveRange(count, records.Count - count);
    }
}
