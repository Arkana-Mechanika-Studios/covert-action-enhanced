namespace CovertAction.Core.Records;

/// <summary>
/// The campaign's data: everything about the case being played and the world it is played in. The original keeps
/// it as twelve tables in a fixed order, which every part reads from the hand-off block when it starts and writes
/// back when it ends, and which a saved game stores after its header.
/// </summary>
/// <remarks>
/// The cast and the events are as long as the case's crime file makes them; the other tables have fixed sizes.
/// The names are the original's own short names for the tables (pl, ac, ev, ct, org, ps, lc, cl, sn, mc), spelled out.
/// </remarks>
// orig: the campaign tables at SHARED+0x400 (src/c/include/state.h, src/c/include/campaign.h); written in this order by write_env_state
public sealed class CampaignTables
{
    /// <summary>The number of cities and of organisations in a world.</summary>
    public const int WorldSize = 16;

    /// <summary>The number of person records.</summary>
    public const int PersonCount = 64;

    /// <summary>The number of building records.</summary>
    public const int LocationCount = 256;

    /// <summary>The number of clue records.</summary>
    public const int ClueCount = 80;

    /// <summary>The number of message records.</summary>
    public const int SynopsisCount = 128;

    /// <summary>The number of plot objects.</summary>
    public const int MacGuffinCount = 4;

    /// <summary>The number of words in the block of counters.</summary>
    public const int ActiveWords = 32;

    /// <summary>The head of the data: the plot, its MasterMind, the date, the agent's career.</summary>
    public Plot Plot { get; } = new();

    /// <summary>The cast of the case, one record per member.</summary>
    public List<Activity> Cast { get; } = [];

    /// <summary>The cast's second records, one per member, in the same order.</summary>
    public List<Activity2> CastProgress { get; } = [];

    /// <summary>The events of the plot.</summary>
    public List<Event> Events { get; } = [];

    /// <summary>The 16 cities.</summary>
    public City[] Cities { get; } = Make<City>(WorldSize);

    /// <summary>The 16 organisations.</summary>
    public Org[] Orgs { get; } = Make<Org>(WorldSize);

    /// <summary>The persons.</summary>
    public Person[] Persons { get; } = Make<Person>(PersonCount);

    /// <summary>The buildings.</summary>
    public Location[] Locations { get; } = Make<Location>(LocationCount);

    /// <summary>The clues.</summary>
    public Clue[] Clues { get; } = Make<Clue>(ClueCount);

    /// <summary>The messages.</summary>
    public Synopsis[] Synopses { get; } = Make<Synopsis>(SynopsisCount);

    /// <summary>The objects of the plot.</summary>
    public MacGuffins[] MacGuffins { get; } = Make<MacGuffins>(MacGuffinCount);

    /// <summary>
    /// The counters at the end of the tables. The first 16 count, per city, how often someone was spotted there
    /// (the campaign's activity report); the raid uses the first of them for its own count of guards
    /// (docs/original-bugs.md CA-1). The use of the other 16 is not established yet.
    /// </summary>
    public short[] Active { get; } = new short[ActiveWords];

    private static T[] Make<T>(int count)
        where T : new()
    {
        var records = new T[count];
        for (var i = 0; i < count; i++)
        {
            records[i] = new T();
        }
        return records;
    }
}
