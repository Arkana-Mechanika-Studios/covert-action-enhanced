using System.Buffers.Binary;
using CovertAction.Core.Records;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence;

/// <summary>
/// A crime file (CRIME0.DTA to CRIME12.DTA): the script of one kind of case. It holds the cast (the roles of the
/// plot), the events (what the members of the cast do, in order) and the up to four objects the plot turns on, in
/// the same records the campaign keeps them in.
/// </summary>
/// <remarks>
/// The format: two words, the number of cast members and of events; then the cast's records, the events' records
/// and four object records. The set-up of a new case (FINAL) reads a crime file into the campaign's tables and then
/// casts persons into the roles. Proven on the player's thirteen files: each is read into the typed records and
/// written back from them alone, byte for byte (CrimeFileTests).
/// </remarks>
// orig: the CRIMEn.DTA format: struct Activity, struct Event, struct MacGuffins (src/c/include/campaign.h), read by FINAL's case set-up
public static class CrimeFileCodec
{
    private const int CountsSize = 4;

    /// <summary>The size of a crime file with the given cast and events.</summary>
    public static int Size(int cast, int events) =>
        CountsSize + cast * CampaignLayouts.ActivitySize + events * CampaignLayouts.EventSize + CampaignTables.MacGuffinCount * CampaignLayouts.MacGuffinsSize;

    /// <summary>Reads a crime file into the cast, the events and the objects of the campaign tables. The cast's second records become as many as the cast, each as a new record.</summary>
    /// <param name="file">The whole file.</param>
    /// <param name="tables">Receives the case's script.</param>
    /// <exception cref="ArgumentException">The file is not as long as its counts say.</exception>
    public static void Load(ReadOnlySpan<byte> file, CampaignTables tables)
    {
        if (file.Length < CountsSize)
        {
            throw new ArgumentException("a crime file starts with two counts");
        }
        int cast = BinaryPrimitives.ReadUInt16LittleEndian(file);
        int events = BinaryPrimitives.ReadUInt16LittleEndian(file[2..]);
        if (file.Length != Size(cast, events))
        {
            throw new ArgumentException($"a crime file of {file.Length} bytes for a cast of {cast} and {events} events");
        }
        tables.Cast.Clear();
        tables.CastProgress.Clear();
        tables.Events.Clear();
        var at = CountsSize;
        for (var i = 0; i < cast; i++, at += CampaignLayouts.ActivitySize)
        {
            var member = new Activity();
            CampaignLayouts.Activity.Load(file.Slice(at, CampaignLayouts.ActivitySize), member);
            tables.Cast.Add(member);
            tables.CastProgress.Add(new Activity2());
        }
        for (var i = 0; i < events; i++, at += CampaignLayouts.EventSize)
        {
            var step = new Event();
            CampaignLayouts.Event.Load(file.Slice(at, CampaignLayouts.EventSize), step);
            tables.Events.Add(step);
        }
        foreach (var thing in tables.MacGuffins)
        {
            CampaignLayouts.MacGuffins.Load(file.Slice(at, CampaignLayouts.MacGuffinsSize), thing);
            at += CampaignLayouts.MacGuffinsSize;
        }
    }

    /// <summary>A crime file with the cast, the events and the objects of the campaign tables.</summary>
    /// <param name="tables">The case's script.</param>
    public static byte[] Write(CampaignTables tables)
    {
        var file = new byte[Size(tables.Cast.Count, tables.Events.Count)];
        BinaryPrimitives.WriteUInt16LittleEndian(file, (ushort)tables.Cast.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), (ushort)tables.Events.Count);
        var at = CountsSize;
        foreach (var member in tables.Cast)
        {
            CampaignLayouts.Activity.Store(member, file.AsSpan(at, CampaignLayouts.ActivitySize));
            at += CampaignLayouts.ActivitySize;
        }
        foreach (var step in tables.Events)
        {
            CampaignLayouts.Event.Store(step, file.AsSpan(at, CampaignLayouts.EventSize));
            at += CampaignLayouts.EventSize;
        }
        foreach (var thing in tables.MacGuffins)
        {
            CampaignLayouts.MacGuffins.Store(thing, file.AsSpan(at, CampaignLayouts.MacGuffinsSize));
            at += CampaignLayouts.MacGuffinsSize;
        }
        return file;
    }
}
