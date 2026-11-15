namespace CovertAction.Core.Final;

/// <summary>
/// The bits of a cast member's <c>Status</c> word that say where the new case puts the member: in which of the plot's
/// organisations and cities, whether the member is the MasterMind, and whether a woman. The crime file sets them;
/// <see cref="CaseSetup.PlaceCastMember"/> reads them.
/// </summary>
// orig: CAST_* (src/c/include/campaign.h)
[Flags]
public enum CastPlacement : short
{
    /// <summary>No placement asked for: a random organisation and city near the plot.</summary>
    None = 0,

    /// <summary>In the plot's third organisation (<see cref="FinalState.TheOrgF"/>).</summary>
    OrgF = 0x0001,

    /// <summary>In the plot's third city (<see cref="FinalState.TheCityF"/>).</summary>
    CityF = 0x0002,

    /// <summary>In the plot's second city (<see cref="FinalState.TheCity2"/>).</summary>
    City2 = 0x0010,

    /// <summary>In the plot's first city (<see cref="FinalState.TheCity1"/>).</summary>
    City1 = 0x0020,

    /// <summary>In the plot's second organisation (<see cref="FinalState.TheOrg2"/>).</summary>
    Org2 = 0x0040,

    /// <summary>In the plot's first organisation (<see cref="FinalState.TheOrg1"/>).</summary>
    Org1 = 0x0080,

    /// <summary>The MasterMind: placed with the MasterMind's organisation and city.</summary>
    Mastermind = 0x0100,

    /// <summary>A woman: the person's look word loses its "male" bit.</summary>
    Female = 0x0200,
}
