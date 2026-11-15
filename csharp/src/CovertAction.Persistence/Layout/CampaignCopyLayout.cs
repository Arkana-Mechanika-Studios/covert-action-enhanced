using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Persistence.Layout;

/// <summary>
/// Where a program keeps its copy of the campaign (the 24 scalars and twelve tables it takes over from the hand-off
/// block and hands back) and the variables of the hand-off routines: the part of a data segment's layout that goes
/// with <see cref="ICampaignCopy"/>. One table of places per program; the fields are the same for all.
/// </summary>
/// <remarks>
/// <para>
/// The places were read from each program's copy of <c>read_env_state</c> and <c>write_env_state</c>, which name
/// them one after the other in the order of the hand-off block's words (docs/source-map/shared-functions.csv says the
/// copies are the same code but for such places).
/// </para>
/// <para>
/// A place the program's own layout has given before stays the program's (most programs placed the tables and
/// scalars their own functions use long before the hand-off was ported): call this after the program's own fields.
/// Not part of this: the text buffer <c>Txt</c>, which the program's layout places.
/// </para>
/// </remarks>
public static class CampaignCopyLayout
{
    /// <summary>Records a program has room for in the cast's tables and in the events' (the same in every program).</summary>
    private const int CastCapacity = 32, EventCapacity = 64;

    /// <summary>The bytes of the texts with their end marks: "env.sve", "File Error: " and a line break.</summary>
    private const int EnvSveBytes = 8, FileErrorBytes = 13, LineBreakBytes = 2;

    /// <summary>The far pointer the routines move bytes through: an offset and a segment.</summary>
    private const int FarPointerBytes = 4;

    /// <summary>The places of one program's copies.</summary>
    private sealed record Addresses(
        int InCity, int AtLoc, int Status, int TheCrime, int TheOrg1, int TheOrg2, int TheCity1, int TheCity2, int Gtime, int Nperson,
        int Nloc, int ChronTime, int MacGuffin, int Diff, int TheSeed, int NACTIVITY, int NEVENT, int Sprung, int GMode, int CGA,
        int NextSyn, int Doubles, int InterPol, int Skills, int Alerted,
        int Pl, int Ac, int Ac2, int Ev, int Ct, int Org, int Ps, int Lc, int Cl, int Sn, int Mc, int Active,
        int SharedCursor, int StateHandle, int ChunkPointer, int StrEnvSve, int StrEnvSveRead, int StrFileError, int StrFileErrorEnd);

    // orig: FINAL DGROUP, the campaign's copy (FINAL's names: src/c/include/final.h; state_offset, state_handle, state_ptr)
    private static readonly Addresses Final = new(
        InCity: 0xae36, AtLoc: 0xb374, Status: 0x8090, TheCrime: 0x9e90, TheOrg1: 0x9e94, TheOrg2: 0x9e98, TheCity1: 0x96da, TheCity2: 0x96dc,
        Gtime: 0x7df0, Nperson: 0x84b6, Nloc: 0x96d0, ChronTime: 0x96d2, MacGuffin: 0xb07e, Diff: 0x7de6, TheSeed: 0x9e8a, NACTIVITY: 0x9fa2,
        NEVENT: 0x9e88, Sprung: 0x1548, GMode: 0xae2c, CGA: 0x8092, NextSyn: 0x96ca, Doubles: 0x9fa0, InterPol: 0xae78, Skills: 0xa7b2,
        Alerted: 0x9f8c,
        Pl: 0x7df2, Ac: 0x9fa6, Ac2: 0xad6c, Ev: 0x8bca, Ct: 0x8294, Org: 0xa7be, Ps: 0x0188, Lc: 0x0588, Cl: 0x1188, Sn: 0x84b8, Mc: 0xac20,
        Active: 0xae38,
        SharedCursor: 0x5e0c, StateHandle: 0x5e0e, ChunkPointer: 0x5df8, StrEnvSve: 0x4a30, StrEnvSveRead: 0x4a38, StrFileError: 0x4a40,
        StrFileErrorEnd: 0x4a4d);

    // orig: TAC DGROUP, the campaign's copy (TAC's names: src/c/include/tac.h; shared_cursor, env_handle, shared_ptr, env_sve_name)
    private static readonly Addresses Tac = new(
        InCity: 0xcbba, AtLoc: 0xd48a, Status: 0x9b68, TheCrime: 0xbb7e, TheOrg1: 0xbb84, TheOrg2: 0xbb88, TheCity1: 0xb918, TheCity2: 0xb94a,
        Gtime: 0x91e8, Nperson: 0xa1e2, Nloc: 0xb400, ChronTime: 0xb404, MacGuffin: 0xce00, Diff: 0x91e0, TheSeed: 0xbb74, NACTIVITY: 0xbdf2,
        NEVENT: 0xbb68, Sprung: 0x1abe, GMode: 0xcbae, CGA: 0x9b6c, NextSyn: 0xb3f0, Doubles: 0xbdf0, InterPol: 0xcbfc, Skills: 0xc600,
        Alerted: 0xbde0,
        Pl: 0x98d8, Ac: 0xbdf6, Ac2: 0xcaea, Ev: 0xa8f0, Ct: 0x9e18, Org: 0xc604, Ps: 0x06fe, Lc: 0x0afe, Cl: 0x16fe, Sn: 0xa1e6, Mc: 0xc99e,
        Active: 0xcbbc,
        SharedCursor: 0x7378, StateHandle: 0x737a, ChunkPointer: 0x7364, StrEnvSve: 0x36fc, StrEnvSveRead: 0x3704, StrFileError: 0x370c,
        StrFileErrorEnd: 0x3719);

    // orig: BUG DGROUP, the campaign's copy (BUG's names: src/c/include/bug.h; shared_cursor, state_handle, str_env_sve)
    private static readonly Addresses Bug = new(
        InCity: 0x8c0e, AtLoc: 0x8fd8, Status: 0x5c26, TheCrime: 0x7640, TheOrg1: 0x7644, TheOrg2: 0x764a, TheCity1: 0x7270, TheCity2: 0x7272,
        Gtime: 0x594a, Nperson: 0x6054, Nloc: 0x7268, ChronTime: 0x726a, MacGuffin: 0x8e54, Diff: 0x1652, TheSeed: 0x763a, NACTIVITY: 0x77b0,
        NEVENT: 0x7638, Sprung: 0x1532, GMode: 0x8438, CGA: 0x5c2c, NextSyn: 0x7260, Doubles: 0x77ac, InterPol: 0x8c50, Skills: 0x7fbe,
        Alerted: 0x779c,
        Pl: 0x5992, Ac: 0x77b4, Ac2: 0x8378, Ev: 0x6760, Ct: 0x5e32, Org: 0x7fc0, Ps: 0x0172, Lc: 0x0572, Cl: 0x1172, Sn: 0x6058, Mc: 0x822c,
        Active: 0x8c10,
        SharedCursor: 0x3ae2, StateHandle: 0x3ae4, ChunkPointer: 0x3ace, StrEnvSve: 0x2e54, StrEnvSveRead: 0x2e5c, StrFileError: 0x2e64,
        StrFileErrorEnd: 0x2e71);

    /// <summary>Adds the menus program's (FINAL.EXE) copy of the campaign to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout, with its own fields.</param>
    public static StateLayout<TState> AddFinal<TState>(StateLayout<TState> layout)
        where TState : class, ICampaignCopy => Add(layout, Final);

    /// <summary>Adds the building raid program's (TAC.EXE) copy of the campaign to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout, with its own fields.</param>
    public static StateLayout<TState> AddTac<TState>(StateLayout<TState> layout)
        where TState : class, ICampaignCopy => Add(layout, Tac);

    /// <summary>Adds the electronics program's (BUG.EXE) copy of the campaign to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout, with its own fields.</param>
    public static StateLayout<TState> AddBug<TState>(StateLayout<TState> layout)
        where TState : class, ICampaignCopy => Add(layout, Bug);

    /// <summary>FINAL's working memory of the hand-off routines (the far pointer the bytes are moved through): a range an oracle test does not compare.</summary>
    public static IReadOnlyList<(int Start, int End)> FinalScratch { get; } = Scratch(Final);

    /// <summary>TAC's working memory of the hand-off routines.</summary>
    public static IReadOnlyList<(int Start, int End)> TacScratch { get; } = Scratch(Tac);

    /// <summary>BUG's working memory of the hand-off routines.</summary>
    public static IReadOnlyList<(int Start, int End)> BugScratch { get; } = Scratch(Bug);

    private static (int Start, int End)[] Scratch(Addresses at) => [(at.ChunkPointer, at.ChunkPointer + FarPointerBytes)];

    private static StateLayout<TState> Add<TState>(StateLayout<TState> layout, Addresses at)
        where TState : class, ICampaignCopy
    {
        string Name(int address, string name) => $"DS:{address:x4} {name}";

        // a place the program's own layout has given stays the program's
        bool Free(int address, int length = 1) => !layout.Fields.Any(f => address < f.Offset + f.Length && f.Offset < address + length);

        void Word(int address, string name, Func<TState, short> get, Action<TState, short> set)
        {
            if (Free(address))
            {
                layout.Int16(address, Name(address, name), get, set);
            }
        }

        void Text(int address, int length, string name, Func<TState, string> get, Action<TState, string> set)
        {
            if (Free(address))
            {
                layout.Text(address, length, Name(address, name), get, set);
            }
        }

        void Table<TRecord>(int address, string name, int count, int size, StateLayout<TRecord> record, Func<TState, TRecord[]> table)
            where TRecord : class
        {
            if (!Free(address, size * count))
            {
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var index = i;
                layout.Part(address + size * index, Name(address, $"{name}[{index}]"), record, s => table(s)[index]);
            }
        }

        // ---- the scalars, in the order of their words in the hand-off block ----
        Word(at.InCity, "InCity", s => s.InCity, (s, v) => s.InCity = v);
        Word(at.AtLoc, "AtLoc", s => s.AtLoc, (s, v) => s.AtLoc = v);
        Word(at.Status, "Status", s => (short)s.Status, (s, v) => s.Status = (Options)v);
        Word(at.TheCrime, "TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v);
        Word(at.TheOrg1, "TheOrg1", s => s.TheOrg1, (s, v) => s.TheOrg1 = v);
        Word(at.TheOrg2, "TheOrg2", s => s.TheOrg2, (s, v) => s.TheOrg2 = v);
        Word(at.TheCity1, "TheCity1", s => s.TheCity1, (s, v) => s.TheCity1 = v);
        Word(at.TheCity2, "TheCity2", s => s.TheCity2, (s, v) => s.TheCity2 = v);
        Word(at.Gtime, "Gtime", s => s.Gtime, (s, v) => s.Gtime = v);
        Word(at.Nperson, "Nperson", s => s.Nperson, (s, v) => s.Nperson = v);
        Word(at.Nloc, "Nloc", s => s.Nloc, (s, v) => s.Nloc = v);
        Word(at.ChronTime, "ChronTime", s => s.ChronTime, (s, v) => s.ChronTime = v);
        Word(at.MacGuffin, "MacGuffin", s => s.MacGuffin, (s, v) => s.MacGuffin = v);
        Word(at.Diff, "diff", s => s.Diff, (s, v) => s.Diff = v);
        Word(at.TheSeed, "TheSeed", s => s.TheSeed, (s, v) => s.TheSeed = v);
        Word(at.NACTIVITY, "NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v);
        Word(at.NEVENT, "NEVENT", s => s.NEVENT, (s, v) => s.NEVENT = v);
        Word(at.Sprung, "Sprung", s => s.Sprung, (s, v) => s.Sprung = v);
        if (Free(at.GMode))
        {
            layout.Char(at.GMode, Name(at.GMode, "GMode"), s => s.GMode, (s, v) => s.GMode = v);
        }
        if (Free(at.CGA))
        {
            layout.Flag(at.CGA, Name(at.CGA, "CGA"), s => s.CGA, (s, v) => s.CGA = v);
        }
        Word(at.NextSyn, "NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v);
        Word(at.Doubles, "Doubles", s => s.Doubles, (s, v) => s.Doubles = v);
        Word(at.InterPol, "InterPol", s => s.InterPol, (s, v) => s.InterPol = v);
        Word(at.Skills, "skills", s => s.Skills, (s, v) => s.Skills = v);
        Word(at.Alerted, "Alerted", s => s.Alerted, (s, v) => s.Alerted = v);

        // ---- the twelve tables, in the order they are handed on ----
        if (Free(at.Pl, CampaignLayouts.PlotSize))
        {
            layout.Part(at.Pl, Name(at.Pl, "pl"), CampaignLayouts.Plot, s => s.Pl);
        }
        Table(at.Ac, "ac", CastCapacity, CampaignLayouts.ActivitySize, CampaignLayouts.Activity, s => s.Ac);
        Table(at.Ac2, "ac2", CastCapacity, CampaignLayouts.Activity2Size, CampaignLayouts.Activity2, s => s.Ac2);
        Table(at.Ev, "ev", EventCapacity, CampaignLayouts.EventSize, CampaignLayouts.Event, s => s.Ev);
        Table(at.Ct, "ct", CampaignTables.WorldSize, CampaignLayouts.CitySize, CampaignLayouts.City, s => s.Ct);
        Table(at.Org, "org", CampaignTables.WorldSize, CampaignLayouts.OrgSize, CampaignLayouts.Org, s => s.Org);
        Table(at.Ps, "ps", CampaignTables.PersonCount, CampaignLayouts.PersonSize, CampaignLayouts.Person, s => s.Ps);
        Table(at.Lc, "lc", CampaignTables.LocationCount, CampaignLayouts.LocationSize, CampaignLayouts.Location, s => s.Lc);
        Table(at.Cl, "cl", CampaignTables.ClueCount, CampaignLayouts.ClueSize, CampaignLayouts.Clue, s => s.Cl);
        Table(at.Sn, "sn", CampaignTables.SynopsisCount, CampaignLayouts.SynopsisSize, CampaignLayouts.Synopsis, s => s.Sn);
        Table(at.Mc, "mc", CampaignTables.MacGuffinCount, CampaignLayouts.MacGuffinsSize, CampaignLayouts.MacGuffins, s => s.Mc);
        // the last table is two halves: a counter per city, then one per organisation
        var orgCounters = at.Active + sizeof(short) * CampaignTables.WorldSize;
        if (Free(at.Active))
        {
            layout.Int16Array(at.Active, CampaignTables.WorldSize, Name(at.Active, "active"), s => s.Active);
        }
        if (Free(orgCounters))
        {
            layout.Int16Array(orgCounters, CampaignTables.WorldSize, Name(orgCounters, "active, the organisations' half"), s => s.OrgCounters);
        }

        // ---- the hand-off routines' own variables ----
        Word(at.SharedCursor, "shared_cursor", s => s.HandOff.SharedCursor, (s, v) => s.HandOff.SharedCursor = v);
        Word(at.StateHandle, "state_handle", s => s.HandOff.StateHandle, (s, v) => s.HandOff.StateHandle = v);
        Text(at.StrEnvSve, EnvSveBytes, "\"env.sve\" (written)", s => s.HandOff.StrEnvSve, (s, v) => s.HandOff.StrEnvSve = v);
        Text(at.StrEnvSveRead, EnvSveBytes, "\"env.sve\" (read)", s => s.HandOff.StrEnvSveRead, (s, v) => s.HandOff.StrEnvSveRead = v);
        Text(at.StrFileError, FileErrorBytes, "\"File Error: \"", s => s.HandOff.StrFileError, (s, v) => s.HandOff.StrFileError = v);
        Text(at.StrFileErrorEnd, LineBreakBytes, "the error box's line break", s => s.HandOff.StrFileErrorEnd, (s, v) => s.HandOff.StrFileErrorEnd = v);
        return layout;
    }
}
