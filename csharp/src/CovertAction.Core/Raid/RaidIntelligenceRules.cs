using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Raid;

/// <summary>The raid's rules about the campaign's world: where an organisation is present, and where it may hide.</summary>
public static class RaidIntelligenceRules
{
    /// <summary>The strongest presence (a headquarters).</summary>
    private const int MaxPresence = 5;

    /// <summary>
    /// How strongly organisation <paramref name="org"/> is present in city <paramref name="city"/>: 0 not at all, 1..5
    /// the kind of building it has there. The nearer the two on the world's grid, the bigger the city's police and the
    /// organisation, the stronger; the more covert the organisation, the weaker. The campaign's home city and
    /// organisation always give 1, and at the lowest level so does the first city for the organisation of the cast's
    /// first member.
    /// </summary>
    /// <param name="state">The cities, organisations and cast.</param>
    /// <param name="city">The city, 0..15.</param>
    /// <param name="org">The organisation, 0..15.</param>
    /// <returns>The presence, 0..5 (negative values for none).</returns>
    // orig: TAC 00e8:b321 org_presence_in_city (src/c/tac/tac_00e8_b321.c) matched; tests: RaidIntelligenceRulesTests, TacOracleTests
    public static int OrgPresenceInCity(RaidState state, int city, int org)
    {
        var place = state.Ct[city];
        var organisation = state.Org[org];
        var distance = GameLibrary.KeyDistance(place.Climate, organisation.Climate);
        // C: 16-bit products and divisions
        var presence = (int)C16.Div(C16.Int((place.Police + 4) * organisation.Size), C16.Int(distance + 1));
        if (presence > 0)
        {
            // C: the original also works out 60 * presence / covert + 10 here and never uses it; the division is kept
            // for its divide error when the organisation's covertness is 0
            _ = C16.Div(C16.Int(60 * presence), organisation.Covert);
            presence = C16.Int(C16.Div(C16.Int(6 * presence), organisation.Covert) + 1);
            if (presence > MaxPresence)
            {
                presence = MaxPresence;
            }
        }
        // the MasterMind's own city and organisation
        if (city == state.Pl.BigLoc && org == state.Pl.BigOrg)
        {
            presence = 1;
        }
        if (presence <= 0 && state.Diff == 0 && city == 1 && state.Ps[state.Ac[0].Who].Org == org)
        {
            presence = 1;
        }
        return presence;
    }

    /// <summary>
    /// Whether organisation <paramref name="otherOrg"/> could plausibly be in city <paramref name="city"/> as seen from
    /// organisation <paramref name="org"/>: four times its presence there plus 16 exceeds the grid distance from
    /// <paramref name="org"/> to the city plus one and a half times that between the two organisations.
    /// </summary>
    /// <param name="state">The cities, organisations and cast.</param>
    /// <param name="org">The organisation looked from.</param>
    /// <param name="otherOrg">The organisation looked for.</param>
    /// <param name="city">The city.</param>
    /// <returns>Whether it is plausible.</returns>
    // orig: TAC 00e8:b16d org_plausible_in_city (src/c/tac/tac_00e8_b16d.c) matched; tests: RaidIntelligenceRulesTests, TacOracleTests
    public static bool OrgPlausibleInCity(RaidState state, int org, int otherOrg, int city)
    {
        var from = state.Org[org].Climate;
        // C: 16-bit sum and division
        var distance = C16.Int(GameLibrary.KeyDistance(from, state.Ct[city].Climate) + C16.Div(C16.Int(GameLibrary.KeyDistance(from, state.Org[otherOrg].Climate) * 3), 2));
        return C16.Int(OrgPresenceInCity(state, city, otherOrg) * 4 + 0x10) > distance;
    }
}
