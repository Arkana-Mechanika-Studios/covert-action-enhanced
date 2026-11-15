using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Electronics;

/// <summary>
/// This part's campaign tables as the library's screens about persons see them (<see cref="ICampaignTables"/>), with the
/// texts of those screens: what a campaign wiretap shows (a clue of the plot, a person's dossier, a message's direction,
/// what is new about a person).
/// </summary>
public sealed partial class ElectronicsState : ICampaignTables
{
    /// <summary>The texts of the library's screens about persons (dossier, personnel file, clue report, names) and their buffers.</summary>
    public DossierTexts Dossier { get; } = new();

    IReadOnlyList<Person> ICampaignTables.Ps => Ps;

    IReadOnlyList<Org> ICampaignTables.Orgs => Org;

    IReadOnlyList<City> ICampaignTables.Ct => Ct;

    IReadOnlyList<Clue> ICampaignTables.Cl => Cl;

    IReadOnlyList<Synopsis> ICampaignTables.Sn => Sn;

    IReadOnlyList<Event> ICampaignTables.Ev => Ev;

    IReadOnlyList<Activity> ICampaignTables.Ac => Ac;

    IList<short> ICampaignTables.Active => Active;

    IList<short> ICampaignTables.OrgCounters => OrgCounters;

    /// <summary>
    /// Message -1: the 14 bytes in front of <see cref="Sn"/> (BUG DS:604a to DS:6057), which the message log reads as
    /// the partner of message 0 when that has no partner. In this part they are the end of the last city's record, a
    /// word of the message box and two variables: its minute is city 15's organisation clues (DS:604c), its knowledge
    /// word city 15's map position (DS:6050, x and y), its second city the high byte of <see cref="Mode"/> (DS:6057).
    /// </summary>
    // orig: sn[-1] in BUG; time at DS:604c (ct[15].OrgClues), clues at DS:6050 (ct[15].geoX, geoY), loc2 at DS:6057 (mode)
    // DIVERGENCE: D-301 a change to message -1 is not written back into city 15's record
    Synopsis ICampaignTables.MessageBeforeFirst => new()
    {
        Time = Ct[CampaignTables.WorldSize - 1].OrgClues,
        Clues = (short)(Ct[CampaignTables.WorldSize - 1].GeoX | Ct[CampaignTables.WorldSize - 1].GeoY << 8),
        Loc2 = unchecked((sbyte)((short)Mode >> 8)),
    };
}
