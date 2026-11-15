using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>
/// This part's campaign tables as the library's screens about a case see them (<see cref="ICampaignTables"/>), and
/// the library's texts of those screens that this part has.
/// </summary>
public sealed partial class CampaignState : ICampaignTables
{
    /// <summary>
    /// The texts of the library's dossier screens in this part's data: so far the chronology's (GAME DS:3895 ...
    /// DS:38e8) and the ellipsis; the others join with the routines that use them.
    /// </summary>
    public DossierTexts DossierTexts { get; } = new();

    IReadOnlyList<Person> ICampaignTables.Ps => Ps;

    IReadOnlyList<Org> ICampaignTables.Orgs => Orgs;

    IReadOnlyList<City> ICampaignTables.Ct => Ct;

    IReadOnlyList<Clue> ICampaignTables.Cl => Cl;

    IReadOnlyList<Synopsis> ICampaignTables.Sn => Sn;

    IReadOnlyList<Event> ICampaignTables.Ev => Ev;

    IReadOnlyList<Activity> ICampaignTables.Ac => Ac;

    IList<short> ICampaignTables.Active => Active;

    IList<short> ICampaignTables.OrgCounters => OrgCounters;

    /// <summary>
    /// Message -1: in this part's data the 14 bytes in front of the case log, which are the end of the last city's
    /// record, a word of the message box and the count of persons (GAME DS:7cb4 ... DS:7cc1): its minute is city 15's
    /// police figure, its knowledge word city 15's sighting counter, its second city the high byte of
    /// <see cref="Nperson"/>, as <c>CampaignTexts</c> reads them.
    /// </summary>
    // orig: sn[-1] in GAME; time at DS:7cb6 (ct[15].police), clues at DS:7cba (ct[15].clued), loc2 at DS:7cc1
    // DIVERGENCE: D-200 a change to message -1 is not written back into city 15's sighting counter
    Synopsis ICampaignTables.MessageBeforeFirst => new()
    {
        Time = Ct[WorldSize - 1].Police,
        Clues = Ct[WorldSize - 1].Clued,
        Loc2 = unchecked((sbyte)(Nperson >> 8)),
    };
}
