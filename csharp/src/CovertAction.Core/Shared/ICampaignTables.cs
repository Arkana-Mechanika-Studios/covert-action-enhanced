using CovertAction.Core.Records;

namespace CovertAction.Core.Shared;

/// <summary>
/// The campaign's tables and counters as a part holds them, for the library's routines that read the case (the
/// message log, the dossier, the personnel file): every part that shows those screens keeps the same records under the
/// same names. A part's state implements this interface.
/// </summary>
public interface ICampaignTables
{
    /// <summary>The persons (<c>ps</c>, 64).</summary>
    IReadOnlyList<Person> Ps { get; }

    /// <summary>The organisations (<c>org</c>, 16).</summary>
    IReadOnlyList<Org> Orgs { get; }

    /// <summary>The cities (<c>ct</c>, 16).</summary>
    IReadOnlyList<City> Ct { get; }

    /// <summary>The clues (<c>cl</c>, 80).</summary>
    IReadOnlyList<Clue> Cl { get; }

    /// <summary>The intercepted messages (<c>sn</c>, 128).</summary>
    IReadOnlyList<Synopsis> Sn { get; }

    /// <summary>
    /// What the original reads and writes as message -1, the bytes in front of the message table: the routines look
    /// at the message before the first one, and use it as the pair of a message that has none. The part maps it onto
    /// its own variables there.
    /// </summary>
    Synopsis MessageBeforeFirst { get; }

    /// <summary>The events of the case (<c>ev</c>).</summary>
    IReadOnlyList<Event> Ev { get; }

    /// <summary>The cast of the case (<c>ac</c>).</summary>
    IReadOnlyList<Activity> Ac { get; }

    /// <summary>The case (<c>pl</c>).</summary>
    Plot Pl { get; }

    /// <summary>How often each city was seen in a clue (<c>active</c>, 16).</summary>
    IList<short> Active { get; }

    /// <summary>How often each organisation was seen in a clue (<c>org_counters</c>, 16).</summary>
    IList<short> OrgCounters { get; }

    /// <summary>The number of persons in use.</summary>
    short Nperson { get; }

    /// <summary>The number of messages in use.</summary>
    short NextSyn { get; }

    /// <summary>The number of cast members.</summary>
    short NACTIVITY { get; }

    /// <summary>The difficulty, 0..3 (the last <c>4 * diff</c> cast members are decoys).</summary>
    short Diff { get; }

    /// <summary>The crime file's number of the case.</summary>
    short TheCrime { get; }

    /// <summary>One bit per organisation whose personnel file the player has looked at (MicroProse's <c>InterPol</c>).</summary>
    short InterPol { get; set; }
}
