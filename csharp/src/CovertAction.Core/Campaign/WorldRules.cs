using CovertAction.Msc;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The world of the campaign as a map: how far apart two places are, how strongly an organisation is present in a
/// city, and how the criminal world grows suspicious after a noisy action of the player.
/// </summary>
/// <remarks>
/// Cities and organisations have a place on a 16 by 16 map, packed into one byte (the <c>climate</c> word of
/// <see cref="Records.City"/> and <see cref="Records.Org"/>): the column in the low four bits, the row in the high
/// four. An organisation's place is its home base.
/// </remarks>
public static class WorldRules
{
    /// <summary>The highest strength of an organisation's presence in a city (an office).</summary>
    private const int StrongestPresence = 5;

    /// <summary>
    /// The distance between two places of the map: twice the larger of the two axis differences plus the smaller one.
    /// </summary>
    /// <param name="posA">A place: column in bits 0..3, row in bits 4..7; higher bits are ignored.</param>
    /// <param name="posB">The other place.</param>
    /// <returns>The distance, 0..45.</returns>
    // orig: GAME 00f7:06b3 map_distance (src/c/game/game_00f7_06b3.c) matched; tests: WorldRulesTests, GameOracleTests
    public static int MapDistance(int posA, int posB)
    {
        var across = Math.Abs(Column(posA) - Column(posB));
        var down = Math.Abs(Row(posA) - Row(posB));
        return across > down ? 2 * across + down : 2 * down + across;
    }

    /// <summary>The column of a place of the map, 0..15.</summary>
    private static int Column(int position) => position & 0xf;

    /// <summary>The row of a place of the map, 0..15.</summary>
    private static int Row(int position) => (position & 0xf0) >> 4;

    /// <summary>
    /// Whether a building of organisation <paramref name="orgB"/> in a city is plausible from the point of view of
    /// organisation <paramref name="orgA"/>: the way from A's base to the city plus one and a half times the way from
    /// A's base to B's must be shorter than 16 plus four times B's presence in the city.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="orgA">The organisation whose base the distances are measured from, 0..15.</param>
    /// <param name="orgB">The organisation that may have the building.</param>
    /// <param name="city">The city, 0..15.</param>
    /// <returns>True when it is plausible.</returns>
    // orig: GAME 00f7:063f building_plausible (src/c/game/game_00f7_063f.c) matched; tests: WorldRulesTests, GameOracleTests
    public static bool BuildingPlausible(CampaignState state, int orgA, int orgB, int city)
    {
        var baseA = state.Orgs[orgA].Climate;
        var distance = MapDistance(baseA, state.Ct[city].Climate) + MapDistance(baseA, state.Orgs[orgB].Climate) * 3 / 2;
        return OrgPresence(state, city, orgB) * 4 + 16 > distance;
    }

    /// <summary>
    /// How strongly an organisation is present in a city: 0 not at all, 1..5 a hideout, an agent, a safehouse, an
    /// active cell, an office (the building names of <c>append_building_type</c>). The presence grows with the city's
    /// police figure plus four and the organisation's size, and falls with the distance from the organisation's base
    /// and with its secrecy (<see cref="Records.Org.Covert"/>). The mastermind's own organisation is always present
    /// in the mastermind's city (as a hideout), and on the easiest level an organisation the cast's first member
    /// belongs to is at least a hideout in city 1.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="city">The city, 0..15.</param>
    /// <param name="org">The organisation, 0..15.</param>
    /// <returns>The strength, 0..5 (negative on numbers no world file has).</returns>
    /// <exception cref="DivideErrorException">The organisation's secrecy is 0: the original stops with a divide error.</exception>
    // orig: GAME 00f7:07f3 org_presence (src/c/game/game_00f7_07f3.c) matched; tests: WorldRulesTests, GameOracleTests
    // Other code under the same name: BUG 00e8:4a9d (not this method; docs/source-map/shared-functions.csv).
    public static int OrgPresence(CampaignState state, int city, int org)
    {
        var place = state.Ct[city];
        var organisation = state.Orgs[org];
        var distance = MapDistance(place.Climate, organisation.Climate);
        // C: 16-bit product and signed division
        var strength = (int)C16.Div(C16.Int((place.Police + 4) * organisation.Size), (short)(distance + 1));
        if (strength > 0)
        {
            // C: the original also works out 60 * strength / covert + 10 here and never uses it; the division is kept for its divide error
            _ = C16.Div(C16.Int(60 * strength), organisation.Covert);
            strength = C16.Div(C16.Int(6 * strength), organisation.Covert) + 1;
            if (strength > StrongestPresence)
            {
                strength = StrongestPresence;
            }
        }
        if (city == state.Pl.BigLoc && org == state.Pl.BigOrg)
        {
            strength = 1;
        }
        // C: the original's `ac` here is the first word of the cast table, the person of cast member 0
        if (strength <= 0 && state.Diff == 0 && city == 1 && state.Ps[state.Ac[0].Who].Org == org)
        {
            strength = 1;
        }
        return strength;
    }

    /// <summary>
    /// A noisy action of the player (a break-in, a shoot-out) of strength <paramref name="amount"/> at an
    /// organisation's building in a city: the general alarm rises by half the amount, and every person at large
    /// becomes more alert, the more the closer their organisation's base and their city are to the action's.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="org">The organisation where it happened.</param>
    /// <param name="city">The city where it happened.</param>
    /// <param name="amount">The noise; nothing happens unless it is positive.</param>
    /// <exception cref="DivideErrorException">An amount so large that a person's share rounds to 0 (above 2047 for the nearest persons): the original stops with a divide error.</exception>
    // orig: GAME 00f7:39bb raise_suspicion (src/c/game/game_00f7_39bb.c) matched; tests: WorldRulesTests, GameOracleTests
    public static void RaiseSuspicion(CampaignState state, int org, int city, int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        state.Alerted += (short)(amount / 2);
        for (var index = 0; index < state.Nperson; index++)
        {
            var person = state.Ps[index];
            if (person.Face == 0 || person.Alert < 0)
            {
                continue;
            }
            var orgDistance = MapDistance(state.Orgs[org].Climate, state.Orgs[person.Org].Climate);
            var cityDistance = MapDistance(state.Ct[city].Climate, state.Ct[person.Loc].Climate);
            // C: the product is a 16-bit int widened to long, the quotient narrowed back to an int, then a 16-bit division
            var remoteness = C16.Int(C16.LDiv(C16.Int((orgDistance + 4) * (cityDistance + 2)) << 8, C16.Int(amount + 1)));
            person.Alert += C16.Div(2500, remoteness);
        }
    }
}
