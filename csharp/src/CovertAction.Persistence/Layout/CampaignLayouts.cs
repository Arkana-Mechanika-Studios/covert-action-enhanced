using CovertAction.Core.Records;

namespace CovertAction.Persistence.Layout;

/// <summary>
/// The campaign's records as the original stores them: for each record type of <c>CovertAction.Core.Records</c>
/// its size and the place of every field. The same bytes are in the hand-off block between the parts of the game,
/// in a saved campaign and in each part's own memory.
/// </summary>
public static class CampaignLayouts
{
    /// <summary>The fields of a <see cref="City"/> at their offsets; 34 bytes.</summary>
    // orig: struct City (src/c/include/campaign.h)
    public static StateLayout<City> City { get; } = new StateLayout<City>("City")
        .Text(0x00, 12, "+0x00 name", r => r.Name, (r, v) => r.Name = v)
        .Text(0x0c, 12, "+0x0c name2", r => r.Name2, (r, v) => r.Name2 = v)
        .Int16(0x18, "+0x18 climate", r => r.Climate, (r, v) => r.Climate = v)
        .Int16(0x1a, "+0x1a police", r => r.Police, (r, v) => r.Police = v)
        .Int16(0x1c, "+0x1c OrgClues", r => r.OrgClues, (r, v) => r.OrgClues = v)
        .Int16(0x1e, "+0x1e clued", r => r.Clued, (r, v) => r.Clued = v)
        .Char(0x20, "+0x20 geoX", r => (sbyte)r.GeoX, (r, v) => r.GeoX = (byte)v)
        .Char(0x21, "+0x21 geoY", r => (sbyte)r.GeoY, (r, v) => r.GeoY = (byte)v);

    /// <summary>The size of a <see cref="City"/> in bytes.</summary>
    public const int CitySize = 34;

    /// <summary>The fields of a <see cref="Org"/> at their offsets; 36 bytes.</summary>
    // orig: struct Org (src/c/include/campaign.h)
    public static StateLayout<Org> Org { get; } = new StateLayout<Org>("Org")
        .Text(0x00, 6, "+0x00 sname", r => r.Sname, (r, v) => r.Sname = v)
        .Text(0x06, 20, "+0x06 name", r => r.Name, (r, v) => r.Name = v)
        .Int16(0x1a, "+0x1a climate", r => r.Climate, (r, v) => r.Climate = v)
        .Int16(0x1c, "+0x1c size", r => r.Size, (r, v) => r.Size = v)
        .Int16(0x1e, "+0x1e covert", r => r.Covert, (r, v) => r.Covert = v)
        .Int16(0x20, "+0x20 clues", r => r.Clues, (r, v) => r.Clues = v)
        .Char(0x22, "+0x22 race", r => r.Race, (r, v) => r.Race = v)
        .Char(0x23, "+0x23 race_hi", r => r.RaceHi, (r, v) => r.RaceHi = v);

    /// <summary>The size of a <see cref="Org"/> in bytes.</summary>
    public const int OrgSize = 36;

    /// <summary>The fields of a <see cref="Person"/> at their offsets; 16 bytes.</summary>
    // orig: struct Person (src/c/include/campaign.h)
    public static StateLayout<Person> Person { get; } = new StateLayout<Person>("Person")
        .Int16(0x00, "+0x00 org", r => r.Org, (r, v) => r.Org = v)
        .Int16(0x02, "+0x02 face", r => r.Face, (r, v) => r.Face = v)
        .Int16(0x04, "+0x04 loc", r => r.Loc, (r, v) => r.Loc = v)
        .Int16(0x06, "+0x06 clues", r => r.Clues, (r, v) => r.Clues = v)
        .Int16(0x08, "+0x08 role", r => r.Role, (r, v) => r.Role = v)
        .Int16(0x0a, "+0x0a rknown", r => r.Rknown, (r, v) => r.Rknown = v)
        .Int16(0x0c, "+0x0c intel", r => r.Intel, (r, v) => r.Intel = v)
        .Int16(0x0e, "+0x0e alert", r => r.Alert, (r, v) => r.Alert = v);

    /// <summary>The size of a <see cref="Person"/> in bytes.</summary>
    public const int PersonSize = 16;

    /// <summary>The fields of a <see cref="Activity"/> at their offsets; 48 bytes.</summary>
    // orig: struct Activity (src/c/include/campaign.h)
    public static StateLayout<Activity> Activity { get; } = new StateLayout<Activity>("Activity")
        .Int16(0x00, "+0x00 who", r => r.Who, (r, v) => r.Who = v)
        .Int16(0x02, "+0x02 cover", r => r.Cover, (r, v) => r.Cover = v)
        .Text(0x04, 32, "+0x04 name", r => r.Name, (r, v) => r.Name = v)
        .Int16(0x24, "+0x24 key", r => r.Key, (r, v) => r.Key = v)
        .Int16(0x26, "+0x26 status", r => r.Status, (r, v) => r.Status = v)
        .Int16(0x28, "+0x28 clued", r => r.Clued, (r, v) => r.Clued = v)
        .Int16(0x2a, "+0x2a ctype", r => r.Ctype, (r, v) => r.Ctype = v)
        .Int16(0x2c, "+0x2c rank", r => r.Rank, (r, v) => r.Rank = v)
        .Int16(0x2e, "+0x2e dummy", r => r.Dummy, (r, v) => r.Dummy = v);

    /// <summary>The size of a <see cref="Activity"/> in bytes.</summary>
    public const int ActivitySize = 48;

    /// <summary>The fields of a <see cref="Activity2"/> at their offsets; 6 bytes.</summary>
    // orig: struct Activity2 (src/c/include/campaign.h)
    public static StateLayout<Activity2> Activity2 { get; } = new StateLayout<Activity2>("Activity2")
        .Int16(0x00, "+0x00 chron", r => r.Chron, (r, v) => r.Chron = v)
        .Int16(0x02, "+0x02 intel", r => r.Intel, (r, v) => r.Intel = v)
        .Int16(0x04, "+0x04 firstwho", r => r.Firstwho, (r, v) => r.Firstwho = v);

    /// <summary>The size of a <see cref="Activity2"/> in bytes.</summary>
    public const int Activity2Size = 6;

    /// <summary>The fields of a <see cref="Event"/> at their offsets; 44 bytes.</summary>
    // orig: struct Event (src/c/include/campaign.h)
    public static StateLayout<Event> Event { get; } = new StateLayout<Event>("Event")
        .Char(0x00, "+0x00 act", r => r.Act, (r, v) => r.Act = v)
        .Char(0x01, "+0x01 act_hi", r => r.ActHi, (r, v) => r.ActHi = v)
        .Int16(0x02, "+0x02 chron", r => r.Chron, (r, v) => r.Chron = v)
        .Int16(0x04, "+0x04 synch", r => r.Synch, (r, v) => r.Synch = v)
        .Text(0x06, 32, "+0x06 name", r => r.Name, (r, v) => r.Name = v)
        .Int16(0x26, "+0x26 status", r => r.Status, (r, v) => r.Status = v)
        .Int16(0x28, "+0x28 macguff", r => r.Macguff, (r, v) => r.Macguff = v)
        .Int16(0x2a, "+0x2a VP", r => r.VP, (r, v) => r.VP = v);

    /// <summary>The size of a <see cref="Event"/> in bytes.</summary>
    public const int EventSize = 44;

    /// <summary>The fields of a <see cref="Location"/> at their offsets; 12 bytes.</summary>
    // orig: struct Location (src/c/include/campaign.h)
    public static StateLayout<Location> Location { get; } = new StateLayout<Location>("Location")
        .Int16(0x00, "+0x00 org", r => r.Org, (r, v) => r.Org = v)
        .Int16(0x02, "+0x02 loc", r => r.Loc, (r, v) => r.Loc = v)
        .Int16(0x04, "+0x04 clues", r => r.Clues, (r, v) => r.Clues = v)
        .Int16(0x06, "+0x06 tapped", r => r.Tapped, (r, v) => r.Tapped = v)
        .UInt32(0x08, "+0x08 seen", r => r.Seen, (r, v) => r.Seen = v);

    /// <summary>The size of a <see cref="Location"/> in bytes.</summary>
    public const int LocationSize = 12;

    /// <summary>The fields of a <see cref="Clue"/> at their offsets; 12 bytes.</summary>
    // orig: struct Clue (src/c/include/campaign.h)
    public static StateLayout<Clue> Clue { get; } = new StateLayout<Clue>("Clue")
        .Char(0x00, "+0x00 sorg", r => r.Sorg, (r, v) => r.Sorg = v)
        .Char(0x01, "+0x01 sloc", r => r.Sloc, (r, v) => r.Sloc = v)
        .Int16(0x02, "+0x02 act", r => r.Act, (r, v) => r.Act = v)
        .Int16(0x04, "+0x04 info1", r => r.Info1, (r, v) => r.Info1 = v)
        .Int16(0x06, "+0x06 key1", r => r.Key1, (r, v) => r.Key1 = v)
        .Int16(0x08, "+0x08 info2", r => r.Info2, (r, v) => r.Info2 = v)
        .Int16(0x0a, "+0x0a key2", r => r.Key2, (r, v) => r.Key2 = v);

    /// <summary>The size of a <see cref="Clue"/> in bytes.</summary>
    public const int ClueSize = 12;

    /// <summary>The fields of a <see cref="Synopsis"/> at their offsets; 14 bytes.</summary>
    // orig: struct Synopsis (src/c/include/campaign.h)
    public static StateLayout<Synopsis> Synopsis { get; } = new StateLayout<Synopsis>("Synopsis")
        .Int16(0x00, "+0x00 type", r => r.Type, (r, v) => r.Type = v)
        .Int16(0x02, "+0x02 time", r => r.Time, (r, v) => r.Time = v)
        .Int16(0x04, "+0x04 data", r => r.Data, (r, v) => r.Data = v)
        .Int16(0x06, "+0x06 clues", r => r.Clues, (r, v) => r.Clues = v)
        .Int16(0x08, "+0x08 alert", r => r.Alert, (r, v) => r.Alert = v)
        .Char(0x0a, "+0x0a who", r => r.Who, (r, v) => r.Who = v)
        .Char(0x0b, "+0x0b loc", r => r.Loc, (r, v) => r.Loc = v)
        .Char(0x0c, "+0x0c who2", r => r.Who2, (r, v) => r.Who2 = v)
        .Char(0x0d, "+0x0d loc2", r => r.Loc2, (r, v) => r.Loc2 = v);

    /// <summary>The size of a <see cref="Synopsis"/> in bytes.</summary>
    public const int SynopsisSize = 14;

    /// <summary>The fields of a <see cref="MacGuffins"/> at their offsets; 18 bytes.</summary>
    // orig: struct MacGuffins (src/c/include/campaign.h)
    public static StateLayout<MacGuffins> MacGuffins { get; } = new StateLayout<MacGuffins>("MacGuffins")
        .Text(0x00, 16, "+0x00 name", r => r.Name, (r, v) => r.Name = v)
        .Char(0x10, "+0x10 type", r => r.Type, (r, v) => r.Type = v)
        .Char(0x11, "+0x11 who", r => r.Who, (r, v) => r.Who = v);

    /// <summary>The size of a <see cref="MacGuffins"/> in bytes.</summary>
    public const int MacGuffinsSize = 18;

    /// <summary>The fields of a <see cref="Plot"/> at their offsets; 654 bytes.</summary>
    // orig: struct Plot (src/c/include/bug.h)
    public static StateLayout<Plot> Plot { get; } = new StateLayout<Plot>("Plot")
        .Int16(0x00, "+0x00 type", r => r.Type, (r, v) => r.Type = v)
        .Int16(0x02, "+0x02 done", r => r.Done, (r, v) => r.Done = v)
        .Int16(0x04, "+0x04 clues", r => r.Clues, (r, v) => r.Clues = v)
        .Int16(0x06, "+0x06 BigOrg", r => r.BigOrg, (r, v) => r.BigOrg = v)
        .Int16(0x08, "+0x08 BigLoc", r => r.BigLoc, (r, v) => r.BigLoc = v)
        .Int16(0x0a, "+0x0a BigFace", r => r.BigFace, (r, v) => r.BigFace = v)
        .Int16(0x0c, "+0x0c BigIndex", r => r.BigIndex, (r, v) => r.BigIndex = v)
        .Int16(0x0e, "+0x0e index", r => r.Index, (r, v) => r.Index = v)
        .Int16(0x10, "+0x10 crime", r => r.Crime, (r, v) => r.Crime = v)
        .Int16(0x12, "+0x12 World", r => r.World, (r, v) => r.World = v)
        .Int16(0x14, "+0x14 VPs", r => r.VPs, (r, v) => r.VPs = v)
        .Int16(0x16, "+0x16 ncase", r => r.Ncase, (r, v) => r.Ncase = v)
        .Int16(0x18, "+0x18 date", r => r.Date, (r, v) => r.Date = v)
        .Bytes(0x1a, 12, "+0x1a city", r => r.City.Bytes)
        .Int16(0x26, "+0x26 rank", r => r.Rank, (r, v) => r.Rank = v)
        .Int16(0x28, "+0x28 dummy", r => r.Dummy, (r, v) => r.Dummy = v)
        .Bytes(0x2a, 40, "+0x2a object", r => r.Object.Bytes)
        .Bytes(0x52, 40, "+0x52 object2", r => r.Object2.Bytes)
        .Text(0x7a, 16, "+0x7a handle", r => r.Handle, (r, v) => r.Handle = v)
        .Bytes(0x8a, 512, "+0x8a history", r => r.History)
        .UInt32(0x28a, "+0x28a bigs", r => r.Bigs, (r, v) => r.Bigs = v);

    /// <summary>The size of a <see cref="Plot"/> in bytes.</summary>
    public const int PlotSize = 654;
}
