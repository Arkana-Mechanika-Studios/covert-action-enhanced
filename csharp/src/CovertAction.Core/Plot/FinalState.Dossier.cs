using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Final;

/// <summary>
/// The part of this part's state that the library's screens about persons need (the suspect dossier and the
/// announcement of new facts) and the variables of the debriefing that no other function of the part uses.
/// </summary>
public sealed partial class FinalState
{
    /// <summary>
    /// The texts of the library's screens about a case (the dossier's labels, the sentences that announce a new fact,
    /// the words of a message's log line). The part's own names for some of them (<see cref="Names"/>,
    /// <see cref="RankNames"/>, <see cref="MonthNames"/>, <see cref="MessageTemplate"/>) are these same texts.
    /// </summary>
    // orig: FINAL DS:3696 to DS:3804 (the facts' sentences), DS:3cc7 to DS:3da5 (the dossier's labels), DS:3fde " ... "
    public DossierTexts Dossier { get; }

    /// <summary>FINAL DS:2504 to DS:289e: the texts of the debriefing, by <see cref="DebriefText"/>.</summary>
    public string[] DebriefTexts { get; } = new string[(int)DebriefText.Count];

    /// <summary>FINAL DS:5cbc digits_wrapup: the buffer in which the debriefing writes out a rank number (what a longer number left behind the end mark stays).</summary>
    public TextBuffer DigitsWrapup { get; } = new(16);

    /// <summary>
    /// FINAL DS:7dec career_write_pos: where the debriefing found the free place in the career record (<c>Pl.History</c>),
    /// or -1 when the record is full. A variable of the original that stays in memory after the debriefing.
    /// </summary>
    public short CareerWritePos { get; set; }

    // ---- the library's view of the campaign (ICampaignTables) ----

    IReadOnlyList<Person> ICampaignTables.Ps => Ps;

    IReadOnlyList<Org> ICampaignTables.Orgs => Org;

    IReadOnlyList<City> ICampaignTables.Ct => Ct;

    IReadOnlyList<Clue> ICampaignTables.Cl => Cl;

    IReadOnlyList<Synopsis> ICampaignTables.Sn => Sn;

    IReadOnlyList<Event> ICampaignTables.Ev => Ev;

    IReadOnlyList<Activity> ICampaignTables.Ac => Ac;

    Records.Plot ICampaignTables.Pl => Pl;

    IList<short> ICampaignTables.Active => Active;

    IList<short> ICampaignTables.OrgCounters => OrgCounters;

    /// <summary>
    /// Message -1: in this part's data the 14 bytes in front of the case log, which are the end of the last city's
    /// record, a word of the message box and the count of persons (DS:84aa to DS:84b7). The data is laid out as in the
    /// campaign part's, so the same words are read (see <c>CampaignState</c>): its minute is city 15's police figure,
    /// its knowledge word city 15's sighting counter, its second city the high byte of <see cref="Nperson"/>.
    /// </summary>
    // orig: sn[-1] in FINAL; time at DS:84ac (ct[15].police), clues at DS:84b0 (ct[15].clued), loc2 at DS:84b7
    // DIVERGENCE: D-103 a change to message -1 is not written back into city 15's sighting counter
    Synopsis ICampaignTables.MessageBeforeFirst => new()
    {
        Time = Ct[CampaignTables.WorldSize - 1].Police,
        Clues = Ct[CampaignTables.WorldSize - 1].Clued,
        Loc2 = unchecked((sbyte)(Nperson >> 8)),
    };
}
