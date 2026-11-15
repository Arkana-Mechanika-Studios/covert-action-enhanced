using CovertAction.Core.Campaign;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Persistence;
using CovertAction.Platform;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// Saving the campaign: to one of the save files (CV0.SVE ...), or into the hand-off block for the part of the game
/// that runs next (the original calls that "env.sve", after the file its launcher once passed the state in).
/// </summary>
/// <remarks>
/// A save is the campaign's scalars (place, time, the counts of the tables, the options, the skills ...) as words of
/// the hand-off block, and the twelve tables of the campaign in the order of the campaign's header: the plot's
/// head, the cast and its progress, the events, the cities and organisations of the world, the persons, their
/// locations, the clues, the message log, the plot's objects and the activity counters. A save file is bytes 0x10 to
/// 0x3ff of the hand-off block followed by the tables (<see cref="SaveFileCodec"/>); a hand-off is the tables
/// themselves, as typed records (<see cref="SharedState.Campaign"/>).
/// </remarks>
/// <param name="state">The campaign.</param>
/// <param name="shared">The hand-off block.</param>
/// <param name="world">The drivers and the files.</param>
public sealed class CampaignSaves(CampaignState state, SharedState shared, Boundaries world)
{
    /// <summary>The permission the original creates a save file with (the owner may write).</summary>
    private const int MayWrite = 0x80;

    /// <summary>The library's routines that hand on one table (this part calls them write_table and read_table).</summary>
    public LibraryChunks Chunks { get; } = new(state, world);

    /// <summary>
    /// Saves the campaign: under the name of the hand-off ("env.sve") into the hand-off block's tables, under any
    /// other name into that file.
    /// </summary>
    /// <param name="fileName">The save file's name, or the hand-off's.</param>
    // orig: GAME 00f7:aad4 save_game (src/c/game/game_00f7_aad4.c) matched; tests: GameOracleTests
    public void SaveGame(string fileName)
    {
        // the plot's head keeps a copy of the clock: the load screen shows it for each save
        state.Pl.Date = state.Gtime;
        ExportScalars();
        if (fileName != state.StrEnvSve)
        {
            // C: the original does not look at what open answers; a file that cannot be made is written to all the same
            state.SaveHandle = (short)world.Files.Open(fileName, OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, MayWrite);
            world.Files.Write(state.SaveHandle, SaveFileCodec.Header(shared));
            // the city the agent is in names the save in the load screen; it goes into the plot's head after the
            // file's own head was written
            state.Pl.City.Text = state.Ct[state.InCity].Name;
            state.SaveMemOffset = 0;
        }
        else
        {
            state.SaveMemOffset = SharedState.TablesOffset;
        }

        // the twelve tables, each handed to write_table (the library's write_chunk) as one run of bytes
        var tables = state.SaveMemOffset == 0 ? new CampaignTables() : shared.Campaign;
        ExportTables(tables);
        var bytes = new byte[CampaignTablesCodec.Size(tables.Cast.Count, tables.Events.Count)];
        CampaignTablesCodec.Store(tables, bytes);
        var at = 0;
        foreach (var size in CampaignTablesCodec.TableSizes(tables))
        {
            Chunks.WriteChunk(state.SaveHandle, bytes.AsSpan(at, size));
            at += size;
        }
        if (state.SaveMemOffset == 0)
        {
            world.Files.Close(state.SaveHandle);
        }
    }

    /// <summary>
    /// Saves the campaign to the save file named by the save screens' path (its extension is forced to "sve"). A file
    /// that cannot be made is said in a box ("File Error"); otherwise a box says the game is being saved, without
    /// waiting for a key, and the campaign is written.
    /// </summary>
    /// <param name="path">The file's name (the save screens' path).</param>
    /// <param name="box">The part's box on a clean screen (text, left edge, top).</param>
    /// <returns>Whether the file could be made.</returns>
    // orig: GAME 00f7:a833 save_to_named_file (src/c/game/game_00f7_a833.c) matched; tests: CampaignTests
    public bool SaveToNamedFile(string path, Func<string, int, int, int> box)
    {
        const int extensionAt = 6, errorBoxX = 0x64, errorBoxY = 0x50, progressBoxX = 4, progressBoxY = 0x64;
        var screens = state.Saves;
        // the name is the screens' own buffer: the extension is written into it
        screens.SavePath = path[..extensionAt] + screens.Texts[(int)SaveText.WriteExtension];
        path = screens.SavePath;
        // the file is made to see that it can be, and closed again; save_game makes it anew
        screens.SlotFileHandle = (short)world.Files.Open(path, OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, MayWrite);
        if (screens.SlotFileHandle == -1)
        {
            state.Txt.Text = screens.Texts[(int)SaveText.FileError] + path + screens.Texts[(int)SaveText.LineBreak];
            box(state.Txt.Text, errorBoxX, errorBoxY);
            return false;
        }
        world.Files.Close(screens.SlotFileHandle);
        // the progress is only drawn: the box does not wait for a key
        state.Menu.MenuPauseMode = true;
        state.Txt.Text = path + screens.Texts[(int)SaveText.SaveInProgress];
        box(state.Txt.Text, progressBoxX, progressBoxY);
        SaveGame(path);
        return true;
    }

    /// <summary>
    /// The library's save screens (drive, slots, load, save) on the campaign's own routines: a slot is loaded with
    /// <see cref="LoadGame"/> and written with <see cref="SaveToNamedFile"/>, all in the part's box.
    /// </summary>
    /// <param name="input">The part's key routines.</param>
    /// <param name="menus">The part's message box and disk prompt.</param>
    /// <param name="box">The part's box on a clean screen (text, left edge, top).</param>
    public LibrarySaves SaveScreens(LibraryInput input, LibraryMenus menus, Func<string, int, int, int> box) =>
        new(state, world, input, menus, path => LoadGame(path, box), (text, x, y) => box(text.Text, x, y), path => SaveToNamedFile(path, box));

    /// <summary>
    /// Loads a campaign: under the name of the hand-off ("env.sve") from the hand-off block's tables, under any other
    /// name from that save file, whose head goes into the hand-off block first. A save file that cannot be opened is
    /// named in a box, and nothing is loaded.
    /// </summary>
    /// <param name="fileName">The save file's name, or the hand-off's.</param>
    /// <param name="errorBox">The part's box on a clean screen (text, left edge, top), for the file error.</param>
    /// <exception cref="UninitialisedReadException">A save file shorter than its head: the original copies what its stack held.</exception>
    /// <exception cref="MemoryOverrunException">The hand-off block's tables do not have the size its counts say.</exception>
    // orig: GAME 00f7:aebb load_game (src/c/game/game_00f7_aebb.c) matched; tests: GameOracleTests
    // C: the original answers 0 for a file error and leaves no answer otherwise; its callers do not read it.
    public void LoadGame(string fileName, Func<string, int, int, int> errorBox)
    {
        const int errorBoxX = 100, errorBoxY = 0x50;
        var words = state.CityWords;
        if (fileName != words.HandOffName)
        {
            state.SaveHandle = (short)world.Files.Open(fileName, OpenMode.Read | OpenMode.Binary);
            if (state.SaveHandle == -1)
            {
                state.Txt.Text = words.FileError + fileName + words.FileErrorEnd;
                errorBox(state.Txt.Text, errorBoxX, errorBoxY);
                return;
            }
            ReadHeader(state.SaveHandle);
            state.SaveMemOffset = 0;
        }
        else
        {
            state.SaveMemOffset = SharedState.TablesOffset;
        }
        var fromFile = state.SaveMemOffset == 0;
        ImportScalars(fromFile);

        // the twelve tables, each taken from read_table (the library's read_chunk) as one run of bytes. From a file
        // they are read over the tables the part has, so a file that ends early leaves the rest as it was.
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
            Chunks.ReadChunk(state.SaveHandle, bytes.AsSpan(at, size));
            at += size;
        }
        CampaignTablesCodec.Load(bytes, tables.Cast.Count, tables.Events.Count, tables);
        ImportTables(tables);
        if (fromFile)
        {
            world.Files.Close(state.SaveHandle);
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

    /// <summary>The words of the hand-off block into the campaign's scalars.</summary>
    /// <param name="fromFile">The block's head was just read from a save file.</param>
    private void ImportScalars(bool fromFile)
    {
        const sbyte cgaLetter = (sbyte)'C';
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
            state.GMode = C16.Char(shared.GfxLetter);
        }
        state.CGA = state.GMode == cgaLetter;
        state.NextSyn = shared.NMsgs;
        state.Doubles = shared.CityMask;
        state.InterPol = shared.InterPol;
        state.Skills = shared.SkillBits;
        state.Alerted = shared.Heat;
    }

    /// <summary>The hand-off records into the campaign's tables.</summary>
    private void ImportTables(CampaignTables tables)
    {
        state.Pl.CopyFrom(tables.Plot);
        Take(state.Ac, tables.Cast, (to, from) => to.CopyFrom(from));
        Take(state.Ac2, tables.CastProgress, (to, from) => to.CopyFrom(from));
        Take(state.Ev, tables.Events, (to, from) => to.CopyFrom(from));
        Take(state.Ct, tables.Cities, (to, from) => to.CopyFrom(from));
        Take(state.Orgs, tables.Orgs, (to, from) => to.CopyFrom(from));
        Take(state.Ps, tables.Persons, (to, from) => to.CopyFrom(from));
        Take(state.Lc, tables.Locations, (to, from) => to.CopyFrom(from));
        Take(state.Cl, tables.Clues, (to, from) => to.CopyFrom(from));
        Take(state.Sn, tables.Synopses, (to, from) => to.CopyFrom(from));
        Take(state.Mc, tables.MacGuffins, (to, from) => to.CopyFrom(from));
        tables.Active.AsSpan(0, state.Active.Length).CopyTo(state.Active);
        tables.Active.AsSpan(state.Active.Length, state.OrgCounters.Length).CopyTo(state.OrgCounters);
    }

    /// <summary>Copies the records of a hand-off table over the first records of the campaign's, which has room for them.</summary>
    private static void Take<T>(T[] target, IReadOnlyList<T> source, Action<T, T> copy)
    {
        if (source.Count > target.Length)
        {
            throw new MemoryOverrunException($"a table of {source.Count} records is loaded into one of {target.Length}: the original writes on behind it");
        }
        for (var i = 0; i < source.Count; i++)
        {
            copy(target[i], source[i]);
        }
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

    /// <summary>The campaign's tables as hand-off records: the cast and the events as far as the campaign has them, the others whole.</summary>
    private void ExportTables(CampaignTables tables)
    {
        tables.Plot.CopyFrom(state.Pl);
        Fill(tables.Cast, state.Ac, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        Fill(tables.CastProgress, state.Ac2, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        Fill(tables.Events, state.Ev, state.NEVENT, (to, from) => to.CopyFrom(from));
        Copy(tables.Cities, state.Ct, (to, from) => to.CopyFrom(from));
        Copy(tables.Orgs, state.Orgs, (to, from) => to.CopyFrom(from));
        Copy(tables.Persons, state.Ps, (to, from) => to.CopyFrom(from));
        Copy(tables.Locations, state.Lc, (to, from) => to.CopyFrom(from));
        Copy(tables.Clues, state.Cl, (to, from) => to.CopyFrom(from));
        Copy(tables.Synopses, state.Sn, (to, from) => to.CopyFrom(from));
        Copy(tables.MacGuffins, state.Mc, (to, from) => to.CopyFrom(from));
        // the counters of the cities, then those of the organisations
        state.Active.CopyTo(tables.Active, 0);
        state.OrgCounters.CopyTo(tables.Active, state.Active.Length);
    }

    private static void Fill<T>(List<T> target, T[] source, int count, Action<T, T> copy)
        where T : new()
    {
        if (count < 0 || count > source.Length)
        {
            throw new MemoryOverrunException($"a table of {count} records is saved from one of {source.Length}: the original writes on with what lies behind it");
        }
        target.Clear();
        for (var i = 0; i < count; i++)
        {
            var record = new T();
            copy(record, source[i]);
            target.Add(record);
        }
    }

    private static void Copy<T>(T[] target, T[] source, Action<T, T> copy)
    {
        for (var i = 0; i < target.Length; i++)
        {
            copy(target[i], source[i]);
        }
    }
}
