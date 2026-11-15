using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Persistence;
using CovertAction.Platform;

namespace CovertAction.Presentation.Library;

/// <summary>
/// Handing the campaign on: the two routines with which a part takes the campaign over into its own variables when
/// it starts and gives it back when it ends, and with which the menus part reads and writes the save files
/// (CV0.SVE ...). The menus part, the building raid and the electronics part carry them.
/// </summary>
/// <remarks>
/// <para>
/// The campaign is the 24 scalars of <see cref="ICampaignCopy"/>, which are words of the hand-off block, and the
/// twelve tables. Under the name "env.sve" (the file the launcher once passed the state in) the tables go to and come
/// from the hand-off block itself, where they are typed records (<see cref="SharedState.Campaign"/>); under any
/// other name they are written to or read from that file behind its head, which is bytes 0x10 to 0x3ff of the
/// hand-off block (<see cref="SaveFileCodec"/>).
/// </para>
/// <para>
/// The building raid counts its guards in the first of the city counters, which it hands back with the campaign
/// (docs/original-bugs.md CA-1). That is the raid's affair, and its switch is where the raid counts; these routines
/// hand on whatever the part's copy holds.
/// </para>
/// </remarks>
/// <param name="state">The part's copy of the campaign.</param>
/// <param name="shared">The hand-off block.</param>
/// <param name="world">The drivers and the files.</param>
/// <param name="messageBox">
/// The part's own message box (text, left edge, top edge), which says so when a save file cannot be opened: FINAL's
/// menu_choice, TAC's show_message_box, BUG's message_box.
/// </param>
public sealed class LibraryHandOff(ICampaignCopy state, SharedState shared, Boundaries world, Action<TextBuffer, int, int> messageBox)
{
    /// <summary>The permission the original creates a save file with (the owner may write).</summary>
    private const int MayWrite = 0x80;

    /// <summary>Where the "File Error" box stands on the screen.</summary>
    private const int ErrorBoxX = 100, ErrorBoxY = 80;

    /// <summary>The graphics driver's letter for the four-colour display.</summary>
    private const char CgaLetter = 'C';

    /// <summary>The routines that hand on one table.</summary>
    public LibraryChunks Chunks { get; } = new(state.HandOff, world);

    /// <summary>
    /// Gives the campaign back: under the name of the hand-off ("env.sve") into the hand-off block, under any other
    /// name into that file.
    /// </summary>
    /// <param name="fileName">The save file's name, or the hand-off's.</param>
    // orig: FINAL 0100:a2f6 write_env_state (src/c/final/final_0100_a2f6.c) matched; also TAC 00e8:c7d2, BUG 00e8:659e; tests: HandOffOracleTests, LibraryHandOffTests
    public void WriteEnvState(string fileName)
    {
        var handOff = state.HandOff;
        // the plot's head keeps a copy of the clock: the load screen shows it for each save
        state.Pl.Date = state.Gtime;
        ExportScalars();
        if (fileName != handOff.StrEnvSve)
        {
            // C: the original does not look at what open answers; a file that cannot be made is written to all the same
            handOff.StateHandle = (short)world.Files.Open(fileName, OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, MayWrite);
            world.Files.Write(handOff.StateHandle, SaveFileCodec.Header(shared));
            // the city the agent is in names the save in the load screen; it goes into the plot's head after the
            // file's own head was written
            // C: the name has 12 bytes there; a longer one would run on into the agent's rank (none of the game's
            // cities has more than 11 letters)
            state.Pl.City.Text = state.Ct[state.InCity].Name;
            handOff.SharedCursor = 0;
        }
        else
        {
            handOff.SharedCursor = SharedState.TablesOffset;
        }

        // the twelve tables, each handed to write_chunk as one run of bytes
        var toFile = handOff.SharedCursor == 0;
        var tables = toFile ? new CampaignTables() : shared.Campaign;
        ExportTables(tables);
        var bytes = new byte[CampaignTablesCodec.Size(tables.Cast.Count, tables.Events.Count)];
        CampaignTablesCodec.Store(tables, bytes);
        var at = 0;
        foreach (var size in CampaignTablesCodec.TableSizes(tables))
        {
            Chunks.WriteChunk(handOff.StateHandle, bytes.AsSpan(at, size));
            at += size;
        }
        if (toFile)
        {
            world.Files.Close(handOff.StateHandle);
        }
    }

    /// <summary>
    /// Takes the campaign over: under the name of the hand-off ("env.sve") from the hand-off block, under any other
    /// name from that file. A file that cannot be opened is reported in a box, and the part's copy stays as it was.
    /// </summary>
    /// <param name="fileName">The save file's name, or the hand-off's.</param>
    // orig: FINAL 0100:a6dd read_env_state (src/c/final/final_0100_a6dd.c) matched; also TAC 00e8:cbb9, BUG 00e8:6985; tests: HandOffOracleTests, LibraryHandOffTests
    // C: the original answers 0 after the error box and has no return statement otherwise (the caller gets what the
    // last call left); none of its callers looks at the answer, so there is none here.
    public void ReadEnvState(string fileName)
    {
        var handOff = state.HandOff;
        if (fileName != handOff.StrEnvSveRead)
        {
            handOff.StateHandle = (short)world.Files.Open(fileName, OpenMode.Read | OpenMode.Binary);
            if (handOff.StateHandle == -1)
            {
                state.MessageText.Text = handOff.StrFileError + fileName + handOff.StrFileErrorEnd;
                messageBox(state.MessageText, ErrorBoxX, ErrorBoxY);
                return;
            }
            ReadHeader(handOff.StateHandle);
            handOff.SharedCursor = 0;
        }
        else
        {
            handOff.SharedCursor = SharedState.TablesOffset;
        }
        var fromFile = handOff.SharedCursor == 0;
        ImportScalars(fromFile);

        // the twelve tables, each taken from read_chunk as one run of bytes. From a file they are read over the
        // tables the part has, so a file that ends early leaves the rest as it was.
        var tables = fromFile ? new CampaignTables() : shared.Campaign;
        if (fromFile)
        {
            ExportTables(tables);
        }
        else if (tables.Cast.Count != state.NACTIVITY || tables.Events.Count != state.NEVENT)
        {
            throw new MemoryOverrunException($"the hand-off block's tables hold a cast of {tables.Cast.Count} and {tables.Events.Count} events, its counts say {state.NACTIVITY} and {state.NEVENT}: the original reads the bytes that lie there");
        }
        var bytes = new byte[CampaignTablesCodec.Size(tables.Cast.Count, tables.Events.Count)];
        CampaignTablesCodec.Store(tables, bytes);
        var at = 0;
        foreach (var size in CampaignTablesCodec.TableSizes(tables))
        {
            Chunks.ReadChunk(handOff.StateHandle, bytes.AsSpan(at, size));
            at += size;
        }
        CampaignTablesCodec.Load(bytes, tables.Cast.Count, tables.Events.Count, tables);
        ImportTables(tables);
        if (fromFile)
        {
            world.Files.Close(handOff.StateHandle);
        }
    }

    /// <summary>
    /// Reads the head of a save file into the hand-off block. The places the launcher loaded the drivers at are part
    /// of those bytes, and are kept as they are in this session.
    /// </summary>
    private void ReadHeader(int handle)
    {
        var header = new byte[SaveFileCodec.HeaderSize];
        if (world.Files.Read(handle, header) < header.Length)
        {
            // C: the original reads into a buffer on its stack and copies all of it into the hand-off block
            throw new UninitialisedReadException("the head of a save file that is shorter than its 1008 bytes: the rest is what the stack held");
        }
        var (graphics, input, sound) = (shared.SegGfx, shared.SegMisc, shared.SegSnd);
        SaveFileCodec.LoadHeader(header, shared);
        (shared.SegGfx, shared.SegMisc, shared.SegSnd) = (graphics, input, sound);
    }

    /// <summary>The campaign's scalars into their words of the hand-off block.</summary>
    private void ExportScalars()
    {
        shared.City = state.InCity;
        shared.Org = state.AtLoc;
        shared.Flags = state.Status;
        shared.CaseNo = state.TheCrime;
        shared.PlotOrg2 = state.TheOrg1;
        shared.PlotOrg3 = state.TheOrg2;
        shared.PlotCity2 = state.TheCity1;
        shared.PlotCity3 = state.TheCity2;
        shared.Clock = state.Gtime;
        shared.NPersons = state.Nperson;
        shared.NLinks = state.Nloc;
        shared.PlotStep = state.ChronTime;
        shared.MacGuffin = state.MacGuffin;
        shared.Level = state.Diff;
        shared.RandomSeed = state.TheSeed;
        shared.NCast = state.NACTIVITY;
        shared.NEvents = state.NEVENT;
        shared.SelPerson = state.Sprung;
        // C: the graphics letter is a char, widened with its sign
        shared.GfxLetter = state.GMode;
        shared.NMsgs = state.NextSyn;
        shared.CityMask = state.Doubles;
        shared.InterPol = state.InterPol;
        shared.SkillBits = state.Skills;
        shared.Heat = state.Alerted;
    }

    /// <summary>The words of the hand-off block into the campaign's scalars.</summary>
    /// <param name="fromFile">The block's head was just read from a save file.</param>
    private void ImportScalars(bool fromFile)
    {
        state.InCity = shared.City;
        state.AtLoc = shared.Org;
        state.Status = shared.Flags;
        state.TheCrime = shared.CaseNo;
        state.TheOrg1 = shared.PlotOrg2;
        state.TheOrg2 = shared.PlotOrg3;
        state.TheCity1 = shared.PlotCity2;
        state.TheCity2 = shared.PlotCity3;
        state.Gtime = shared.Clock;
        state.Nperson = shared.NPersons;
        state.Nloc = shared.NLinks;
        state.ChronTime = shared.PlotStep;
        state.MacGuffin = shared.MacGuffin;
        state.Diff = shared.Level;
        state.TheSeed = shared.RandomSeed;
        state.NACTIVITY = shared.NCast;
        state.NEVENT = shared.NEvents;
        state.Sprung = shared.SelPerson;
        if (!fromFile)
        {
            // a save file's graphics letter is that of the machine it was saved on: the part keeps its own
            state.GMode = unchecked((sbyte)shared.GfxLetter);
        }
        state.CGA = state.GMode == (sbyte)CgaLetter;
        state.NextSyn = shared.NMsgs;
        state.Doubles = shared.CityMask;
        state.InterPol = shared.InterPol;
        state.Skills = shared.SkillBits;
        state.Alerted = shared.Heat;
    }

    /// <summary>The part's tables as hand-off records: the cast and the events as far as the campaign has them, the others whole.</summary>
    private void ExportTables(CampaignTables tables)
    {
        tables.Plot.CopyFrom(state.Pl);
        Fill(tables.Cast, state.Ac, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        Fill(tables.CastProgress, state.Ac2, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        Fill(tables.Events, state.Ev, state.NEVENT, (to, from) => to.CopyFrom(from));
        Copy(tables.Cities, state.Ct, (to, from) => to.CopyFrom(from));
        Copy(tables.Orgs, state.Org, (to, from) => to.CopyFrom(from));
        Copy(tables.Persons, state.Ps, (to, from) => to.CopyFrom(from));
        Copy(tables.Locations, state.Lc, (to, from) => to.CopyFrom(from));
        Copy(tables.Clues, state.Cl, (to, from) => to.CopyFrom(from));
        Copy(tables.Synopses, state.Sn, (to, from) => to.CopyFrom(from));
        Copy(tables.MacGuffins, state.Mc, (to, from) => to.CopyFrom(from));
        // the counters of the cities, then those of the organisations
        state.Active.CopyTo(tables.Active, 0);
        state.OrgCounters.CopyTo(tables.Active, state.Active.Length);
    }

    /// <summary>The hand-off records into the part's tables.</summary>
    private void ImportTables(CampaignTables tables)
    {
        state.Pl.CopyFrom(tables.Plot);
        Copy(state.Ac, tables.Cast, (to, from) => to.CopyFrom(from));
        Copy(state.Ac2, tables.CastProgress, (to, from) => to.CopyFrom(from));
        Copy(state.Ev, tables.Events, (to, from) => to.CopyFrom(from));
        Copy(state.Ct, tables.Cities, (to, from) => to.CopyFrom(from));
        Copy(state.Org, tables.Orgs, (to, from) => to.CopyFrom(from));
        Copy(state.Ps, tables.Persons, (to, from) => to.CopyFrom(from));
        Copy(state.Lc, tables.Locations, (to, from) => to.CopyFrom(from));
        Copy(state.Cl, tables.Clues, (to, from) => to.CopyFrom(from));
        Copy(state.Sn, tables.Synopses, (to, from) => to.CopyFrom(from));
        Copy(state.Mc, tables.MacGuffins, (to, from) => to.CopyFrom(from));
        tables.Active.AsSpan(0, state.Active.Length).CopyTo(state.Active);
        tables.Active.AsSpan(state.Active.Length, state.OrgCounters.Length).CopyTo(state.OrgCounters);
    }

    /// <summary>Makes a list of the first <paramref name="count"/> records of a part's table.</summary>
    private static void Fill<T>(List<T> target, T[] source, int count, Action<T, T> copy)
        where T : new()
    {
        if (count < 0 || count > source.Length)
        {
            throw new MemoryOverrunException($"a table of {count} records is handed on from one of {source.Length}: the original goes on with what lies behind it");
        }
        target.Clear();
        for (var i = 0; i < count; i++)
        {
            var record = new T();
            copy(record, source[i]);
            target.Add(record);
        }
    }

    /// <summary>Copies the records of a table over the first records of another, which has room for them.</summary>
    private static void Copy<T>(T[] target, IReadOnlyList<T> source, Action<T, T> copy)
    {
        if (source.Count > target.Length)
        {
            throw new MemoryOverrunException($"a table of {source.Count} records is taken over into one of {target.Length}: the original writes on behind it");
        }
        for (var i = 0; i < source.Count; i++)
        {
            copy(target[i], source[i]);
        }
    }
}
