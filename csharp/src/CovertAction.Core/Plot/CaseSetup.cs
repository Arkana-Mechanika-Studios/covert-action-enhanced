using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Final;

/// <summary>
/// The rules by which a new case is laid out on the world map: where the organisations of a world have buildings
/// and how big, which persons stand for the members of the cast, which buildings the player is told of, and the
/// MasterMind's face. They need no screen and no file; the briefing (<c>setup_new_case</c>) calls them.
/// </summary>
/// <remarks>
/// The map is a grid of 16 x 16 cells; a city's and an organisation's <c>Climate</c> word is its cell (the column in
/// the low four bits, the row in the next four), and <see cref="GameLibrary.KeyDistance"/> measures between cells.
/// </remarks>
public static class CaseSetup
{
    /// <summary>The number of tries after which <see cref="PlaceCastMember"/> gives up.</summary>
    private const int MaxTries = 999;

    /// <summary>A random organisation for a cast member lies at most this far (map cells, <see cref="GameLibrary.KeyDistance"/>) from the plot's first organisation ...</summary>
    private const int OrgReach = 12;

    /// <summary>... and in a map column above this one.</summary>
    private const int WesternmostOrgColumn = 4;

    /// <summary>A random city for a cast member lies at least this far from the plot's first city.</summary>
    private const int CityDistance = 8;

    /// <summary>The organisation of the agent's own side: never the home of a suspect.</summary>
    private const int OwnSide = 1;

    /// <summary>The largest building an organisation has in a city.</summary>
    private const int LargestBuilding = 5;

    /// <summary>The bit of a person's look word that makes the person a man.</summary>
    private const short MaleBit = 1;

    /// <summary>What the search for a person answers when there is none.</summary>
    private const int NoPerson = -1;

    /// <summary>
    /// FINAL's dice: a random whole number from 0 to <paramref name="limit"/> - 1, one draw of the generator.
    /// </summary>
    /// <param name="state">The part's state, whose generator is drawn from.</param>
    /// <param name="limit">The number of outcomes.</param>
    // FINAL's copy (FINAL 0100:c169 random) is the library's random_below under another name
    // (docs/source-map/twin-names.csv): GameLibrary.RandomBelow stands for it, and this is only FINAL's short form.
    public static int Random(FinalState state, int limit) => GameLibrary.RandomBelow(state.Random, limit);

    /// <summary>
    /// How big a building organisation <paramref name="orgNo"/> has in city <paramref name="cityNo"/>: 0 for none, else
    /// 1 (a hideout) to 5 (an office). The organisation's size and the city's police weight, divided by the distance
    /// between them on the map and scaled by how covert the organisation is.
    /// </summary>
    /// <remarks>
    /// The MasterMind's own organisation always has a building (of size 1) in his city; at level 0 so has the first
    /// cast member's organisation in city 1.
    /// </remarks>
    /// <param name="state">The world and the case.</param>
    /// <param name="cityNo">The city, 0..15.</param>
    /// <param name="orgNo">The organisation, 0..15.</param>
    /// <exception cref="DivideErrorException">An organisation whose <c>Covert</c> weight is 0, where the original stops with a divide error.</exception>
    // orig: FINAL 0100:8467 building_level (src/c/final/final_0100_8467.c) matched; tests: CaseSetupTests, FinalOracleTests
    public static int BuildingLevel(FinalState state, int cityNo, int orgNo)
    {
        var city = state.Ct[cityNo];
        var organisation = state.Org[orgNo];
        var distance = GameLibrary.KeyDistance(city.Climate, organisation.Climate);
        // C: 16-bit product and signed division
        int level = C16.Div(C16.Int((city.Police + 4) * organisation.Size), C16.Int(distance + 1));
        if (level > 0)
        {
            // C: the original also works out 60 * level / covert + 10 here and keeps it nowhere; the division is kept
            // because with a covert weight of 0 it is where the original stops
            _ = C16.Div(C16.Int(0x3c * level), organisation.Covert);
            level = C16.Div(C16.Int(6 * level), organisation.Covert) + 1;
            if (level > LargestBuilding)
            {
                level = LargestBuilding;
            }
        }
        if (cityNo == state.Pl.BigLoc && orgNo == state.Pl.BigOrg)
        {
            level = 1;
        }
        // C: while the cast is not placed yet, its first member's person is -1: the original reads ps[-1].org, which is
        // the word "en" of the text "env.sve" in front of the table (FINAL DS:0178), and no organisation has that number
        if (level <= 0 && state.Diff == 0 && cityNo == 1 && state.Ac[0].Who >= 0 && state.Ps[state.Ac[0].Who].Org == orgNo)
        {
            level = 1;
        }
        return level;
    }

    /// <summary>
    /// Whether organisation <paramref name="targetOrg"/>'s building in city <paramref name="targetCity"/> is within
    /// reach of organisation <paramref name="fromOrg"/>: one and a half times the map distance between the two
    /// organisations, plus the distance from <paramref name="fromOrg"/> to the city, must be below 16 plus four times
    /// the size of the building.
    /// </summary>
    /// <param name="state">The world and the case.</param>
    /// <param name="fromOrg">The organisation that reaches out, 0..15.</param>
    /// <param name="targetOrg">The organisation reached, 0..15.</param>
    /// <param name="targetCity">The city, 0..15.</param>
    // orig: FINAL 0100:82b3 org_reaches_city (src/c/final/final_0100_82b3.c) matched; tests: CaseSetupTests, FinalOracleTests
    public static bool OrgReachesCity(FinalState state, int fromOrg, int targetOrg, int targetCity)
    {
        var from = state.Org[fromOrg].Climate;
        // distances are at most 45 map cells: no 16-bit concern
        var distance = GameLibrary.KeyDistance(from, state.Org[targetOrg].Climate) * 3 / 2 + GameLibrary.KeyDistance(from, state.Ct[targetCity].Climate);
        return BuildingLevel(state, targetCity, targetOrg) * 4 + 0x10 > distance;
    }

    /// <summary>
    /// The person who stands for the organisation <paramref name="personOrg"/> in city <paramref name="personCity"/>:
    /// the record of that pair, to which the cast bits <paramref name="castBits"/> are added; or, when there is none
    /// and bits are given, a new record in a free place drawn at random. The MasterMind's pair is always person 0,
    /// which keeps what the player knew of him and gets his face.
    /// </summary>
    /// <remarks>
    /// A new person other than the MasterMind gets a random look word (never 0, which marks a free record), and in one
    /// case of three the "male" bit set on top. The draws for the free place happen even when no record is made.
    /// </remarks>
    /// <param name="state">The persons and the career (the MasterMind).</param>
    /// <param name="personOrg">The organisation.</param>
    /// <param name="personCity">The city.</param>
    /// <param name="castBits">The cast members (bit n: member n) the person stands for; 0 only looks the person up.</param>
    /// <returns>The person's index, or -1 when there is none and <paramref name="castBits"/> is 0.</returns>
    // orig: FINAL 0100:5ff1 find_or_create_person (src/c/final/final_0100_5ff1.c) matched; tests: CaseSetupTests, FinalOracleTests
    // FINAL's own variant (docs/source-map/shared-functions.csv: GAME's, TAC's and BUG's copies are other code).
    public static int FindOrCreatePerson(FinalState state, int personOrg, int personCity, int castBits)
    {
        for (var p = 0; p < state.Nperson; p++)
        {
            if (state.Ps[p].Org == personOrg && state.Ps[p].Loc == personCity)
            {
                state.Ps[p].Role |= (short)castBits;
                return p;
            }
        }
        int free;
        do
        {
            free = Random(state, CampaignTables.PersonCount);
        }
        while (state.Ps[free].Face != 0 || free == 0);
        if (personOrg == state.Pl.BigOrg && personCity == state.Pl.BigLoc)
        {
            free = 0;
        }
        if (castBits == 0)
        {
            return NoPerson;
        }
        var person = state.Ps[free];
        person.Org = (short)personOrg;
        person.Loc = (short)personCity;
        person.Clues = free != 0 ? (short)0 : state.Pl.Clues;
        person.Role = (short)castBits;
        person.Rknown = 0;
        person.Intel = 0;
        if (free != 0)
        {
            person.Face = (short)(Random(state, 0x7ffe) + 1);
            if (Random(state, 3) == 0)
            {
                person.Face |= MaleBit;
            }
        }
        else
        {
            person.Face = state.Pl.BigFace;
        }
        person.Alert = 0;
        return free;
    }

    /// <summary>
    /// Puts cast member <paramref name="castMember"/> on the map for a new case: gives it a random key, chooses its
    /// organisation and city (the plot's, when its placement bits ask for them, else random ones near the plot where the
    /// organisation has a building), and maps it onto the person of that pair. The MasterMind goes to his own
    /// organisation and city.
    /// </summary>
    /// <remarks>
    /// Every choice that does not fit (no building, the MasterMind's pair, a person already used, a person out of play)
    /// is made again; all tries together are counted, and after 999 the member is placed where the last try left it.
    /// </remarks>
    /// <param name="state">The world, the cast and the persons.</param>
    /// <param name="castMember">The member, 0..31.</param>
    /// <returns>Whether the member was placed in fewer than 999 tries (otherwise the caller lays the whole case out again).</returns>
    // orig: FINAL 0100:46df place_cast_member (src/c/final/final_0100_46df.c) matched; tests: CaseSetupTests, FinalOracleTests
    public static bool PlaceCastMember(FinalState state, int castMember)
    {
        var member = state.Ac[castMember];
        var placement = (CastPlacement)member.Status;
        // C: a shift by a variable count (a member is 0..31: from member 16 on the bit is 0, as on the 8086)
        var memberBit = C16.Shl(1, castMember);
        var tries = 0;
        int person;
        while (true)
        {
            member.Key = (short)Random(state, 0x7fff);
            member.Clued = 0;
            state.Ac2[castMember].Chron = -1;
            state.Ac2[castMember].Intel = 0;

            int org, city;
            while (true)
            {
                (org, city) = ChoosePlace(state, placement, ref tries);
                // C: '&& ++tries < 999' counts a failed try only when the test before it holds
                if (BuildingLevel(state, city, org) == 0 && ++tries < MaxTries)
                {
                    continue;
                }
                if (placement.HasFlag(CastPlacement.Mastermind))
                {
                    org = state.Pl.BigOrg;
                    city = state.Pl.BigLoc;
                }
                else if (org == state.Pl.BigOrg && city == state.Pl.BigLoc && ++tries < MaxTries)
                {
                    continue;
                }
                if (state.Diff == 0 && castMember == 0)
                {
                    city = 1;
                }
                person = FindOrCreatePerson(state, org, city, 0);
                if (person != -1 && state.Ps[person].Role != 0 && ++tries < MaxTries)
                {
                    continue;
                }
                break;
            }

            if (tries < MaxTries)
            {
                person = FindOrCreatePerson(state, org, city, memberBit);
                if (placement.HasFlag(CastPlacement.Mastermind) && state.Ps[person].Clues != 0)
                {
                    state.Ps[person].Rknown |= memberBit;
                }
                if (state.Ps[person].Alert < 0)
                {
                    state.Ps[person].Role &= (short)~memberBit;
                }
            }
            // C: when the tries ran out with no person of the last pair (it happens in play), the original goes on
            // with person -1: it reads the "alert" of the 16 bytes in front of the person table, which cannot change
            // the outcome (the tries are used up, so the loop ends either way)
            if (person != NoPerson && state.Ps[person].Alert < 0 && ++tries < MaxTries)
            {
                continue;
            }
            break;
        }
        member.Who = (short)person;
        // C: for person -1 the original clears the male bit in front of the table, in the hand-off file name at
        // FINAL DS:0178 ("env.sve": the "v", whose bit 0 is already clear), so it changes nothing; the case is then
        // laid out again (the caller sees the failure)
        if (person != NoPerson && placement.HasFlag(CastPlacement.Female))
        {
            state.Ps[person].Face &= ~MaleBit;
        }
        return tries < MaxTries;
    }

    /// <summary>The organisation and city a cast member's placement bits ask for, random ones near the plot where they ask for none.</summary>
    private static (int Org, int City) ChoosePlace(FinalState state, CastPlacement placement, ref int tries)
    {
        var org = -1;
        if (placement.HasFlag(CastPlacement.OrgF))
        {
            org = state.TheOrgF;
        }
        if (placement.HasFlag(CastPlacement.Org1))
        {
            org = state.TheOrg1;
        }
        if (placement.HasFlag(CastPlacement.Org2))
        {
            org = state.TheOrg2;
        }
        if (org == -1)
        {
            // an organisation other than the agent's own, near the first of the plot and not in the far west
            bool unfit;
            do
            {
                org = Random(state, 15) + 1;
                var distance = GameLibrary.KeyDistance(state.Org[org].Climate, state.Org[state.TheOrg1].Climate);
                unfit = distance > OrgReach || (state.Org[org].Climate & 0xf) <= WesternmostOrgColumn || org == OwnSide;
            }
            while (unfit && ++tries < MaxTries);
        }

        var city = -1;
        if (placement.HasFlag(CastPlacement.City1))
        {
            city = state.TheCity1;
        }
        if (placement.HasFlag(CastPlacement.City2))
        {
            city = state.TheCity2;
        }
        if (placement.HasFlag(CastPlacement.CityF))
        {
            city = state.TheCityF;
        }
        if (city == -1)
        {
            // a city away from the first of the plot, where the organisation has a building
            bool unfit;
            do
            {
                city = Random(state, CampaignTables.WorldSize);
                var distance = GameLibrary.KeyDistance(state.Ct[city].Climate, state.Ct[state.TheCity1].Climate);
                unfit = distance < CityDistance || BuildingLevel(state, city, org) == 0;
            }
            while (unfit && ++tries < MaxTries);
        }
        return (org, city);
    }

    /// <summary>
    /// MasterMind <paramref name="n"/>'s face as a look word, the form a person's face is kept in: bit 0 set for a man,
    /// bit 1 the head variant, bits 2-3 the colour shade, then the four face parts (1..8) as three bits each from bit 4
    /// on, less one.
    /// </summary>
    /// <param name="state">The table of the MasterMinds' faces.</param>
    /// <param name="n">The MasterMind, 0..25.</param>
    // orig: FINAL 0100:11b7 pack_mastermind_look (src/c/final/final_0100_11b7.c) matched; tests: CaseSetupTests, FinalOracleTests
    public static short PackMastermindLook(FinalState state, int n)
    {
        var look = state.MastermindLooks[n];
        const int female = 0, headVariant = 1, firstPart = 2, colourShade = 6;
        var word = (look[headVariant] << 1) + (look[colourShade] << 2) - look[female] + 1;
        for (var i = 0; i < 4; i++)
        {
            word += (look[firstPart + i] - 1) << (3 * i + 4);
        }
        // C: the word is a 16-bit int; the last part's bits run into the sign
        return (short)word;
    }

    /// <summary>
    /// Lays a new case out on the world map, once its crime and world files are read: clears the clues and the
    /// counters, picks the organisations with Double Agents (one draw per level), then chooses the plot's three
    /// organisations and three cities, moves the eastern organisations a little on the map and places every member of
    /// the cast. Whenever a choice cannot be met in 999 tries all together, the persons are cleared and everything from
    /// the plot's organisations on is chosen again (the organisations' map cells keep moving each time).
    /// </summary>
    /// <param name="state">The world, the cast and the case's variables.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce (CA-58); the original's own when left out.</param>
    /// <param name="attemptLimit">The attempts after which the lay-out gives up with an exception (a test's way to see the original's endless loop); none when left out.</param>
    /// <exception cref="InvalidOperationException">More attempts than <paramref name="attemptLimit"/> were made.</exception>
    /// <remarks>The first part of <c>setup_new_case</c> (FINAL 0100:3f4c), up to the briefing; registered as new code.</remarks>
    // BUG: CA-58 the organisations' moves are never undone between attempts and the loop has no end; reproduced as coded unless OriginalBehaviour.FixCaseLayOutDrift.
    public static void LayOutCase(FinalState state, OriginalBehaviour? behaviour = null, int attemptLimit = int.MaxValue)
    {
        foreach (var clue in state.Cl)
        {
            clue.Act = -1;
        }
        const short unseen = 2;
        Array.Fill(state.OrgCounters, unseen);
        Array.Fill(state.Active, unseen);
        state.Doubles = 0;
        for (var level = 0; level < state.Diff; level++)
        {
            // C: an int shift by 0..15; bit 15 is the sign of the 16-bit word
            state.Doubles |= unchecked((short)(1 << Random(state, CampaignTables.WorldSize)));
        }
        // the agent's own side never has a Double Agent
        state.Doubles &= ~(1 << OwnSide);

        // DIVERGENCE: D-650 a player's option: CA-58, the organisations' positions as the world file gave them, put back before every attempt
        var positions = behaviour?.FixCaseLayOutDrift == true ? state.Org.Select(organisation => organisation.Climate).ToArray() : null;
        for (var attempt = 1; ; attempt++)
        {
            if (attempt > attemptLimit)
            {
                throw new InvalidOperationException($"the case is not laid out after {attemptLimit} attempts");
            }
            if (positions is not null)
            {
                for (var org = 0; org < positions.Length; org++)
                {
                    state.Org[org].Climate = positions[org];
                }
            }
            ClearPersons(state);
            var tries = ChoosePlotOrgsAndCities(state);
            if (tries >= MaxTries)
            {
                continue;
            }
            MoveEasternOrganisations(state);
            for (var member = 0; member < state.NACTIVITY; member++)
            {
                tries += PlaceCastMember(state, member) ? 0 : MaxTries;
            }
            if (tries < MaxTries)
            {
                break;
            }
        }
        SetChron(state);
    }

    /// <summary>Frees every person record and starts the plot's clock afresh; the first step of every try of <see cref="LayOutCase"/>.</summary>
    private static void ClearPersons(FinalState state)
    {
        foreach (var person in state.Ps)
        {
            person.Org = -1;
            person.Face = 0;
            person.Clues = 0;
            person.Alert = 0;
        }
        state.Nperson = CampaignTables.PersonCount;
        const short firstStep = 0x63;
        state.ChronTime = firstStep;
        state.MacGuffin = -1;
        // C: the original looks for the end of the events (an actor byte of 255) and keeps nothing of the search. It
        // cannot run past the table: the events were just read from CRIMEn.DTA, and every one of the 13 shipped files
        // ends its list (18 to 30 events, of the table's 64) with that end mark. A file without one would make the C#
        // throw where the original reads on.
        var end = -1;
        while ((byte)state.Ev[++end].Act != 0xff)
        {
        }
    }

    /// <summary>
    /// Chooses the plot's organisations and cities. TheOrgF lies near the agent's side on the map; TheOrg1 away from
    /// the agent's side, near the MasterMind's organisation and east of column 4; TheOrg2 near TheOrg1, east of column 4.
    /// TheCity1 lies away from the agent's side, TheCity2 near TheCity1, TheCityF near the agent's side; each where its
    /// organisation has a building, and the first two not in the MasterMind's city.
    /// </summary>
    /// <returns>The tries used up, all choices together; 999 or more when a choice could not be met.</returns>
    private static int ChoosePlotOrgsAndCities(FinalState state)
    {
        const int near = 8, nearAgent = 10, cityNearCity = 12;
        var tries = 0;
        int OrgDistance(int a, int b) => GameLibrary.KeyDistance(state.Org[a].Climate, state.Org[b].Climate);
        int CityFromOrg(int org, int city) => GameLibrary.KeyDistance(state.Org[org].Climate, state.Ct[city].Climate);
        bool East(int org) => (state.Org[org].Climate & 0xf) > WesternmostOrgColumn;
        // C: each condition is followed by '&& ++tries < 999', which counts a failed try only

        do
        {
            state.TheOrgF = (short)(Random(state, 15) + 1);
        }
        while ((OrgDistance(OwnSide, state.TheOrgF) > nearAgent || state.TheOrgF == OwnSide) && ++tries < MaxTries);
        do
        {
            state.TheOrg1 = (short)(Random(state, 15) + 1);
        }
        while ((OrgDistance(OwnSide, state.TheOrg1) < near || OrgDistance(state.Pl.BigOrg, state.TheOrg1) > near || !East(state.TheOrg1)) && ++tries < MaxTries);
        do
        {
            state.TheOrg2 = (short)(Random(state, 15) + 1);
        }
        while ((OrgDistance(state.TheOrg1, state.TheOrg2) > near || !East(state.TheOrg2)) && ++tries < MaxTries);
        do
        {
            state.TheCity1 = (short)Random(state, CampaignTables.WorldSize);
        }
        while ((CityFromOrg(OwnSide, state.TheCity1) < near || BuildingLevel(state, state.TheCity1, state.TheOrg1) == 0 || state.Pl.BigLoc == state.TheCity1) && ++tries < MaxTries);
        do
        {
            state.TheCity2 = (short)Random(state, CampaignTables.WorldSize);
        }
        while ((GameLibrary.KeyDistance(state.Ct[state.TheCity1].Climate, state.Ct[state.TheCity2].Climate) > cityNearCity
                || BuildingLevel(state, state.TheCity2, state.TheOrg2) == 0 || state.Pl.BigLoc == state.TheCity2) && ++tries < MaxTries);
        do
        {
            state.TheCityF = (short)Random(state, CampaignTables.WorldSize);
        }
        while ((CityFromOrg(OwnSide, state.TheCityF) > nearAgent || BuildingLevel(state, state.TheCityF, state.TheOrgF) == 0) && ++tries < MaxTries);
        return tries;
    }

    /// <summary>Moves every organisation in the eastern half of the map (column 8 and on) by -2..+2 cells each way, kept on the map.</summary>
    private static void MoveEasternOrganisations(FinalState state)
    {
        const int eastFrom = 8, lastCell = 15;
        foreach (var organisation in state.Org)
        {
            var column = organisation.Climate & 0xf;
            if (column >= eastFrom)
            {
                // the column's draw comes first
                column = GameLibrary.Clamp(Random(state, 5) + column - 2, 0, lastCell);
                var row = GameLibrary.Clamp(organisation.Climate / 16 + Random(state, 5) - 2, 0, lastCell);
                organisation.Climate = (short)((row << 4) + column);
            }
        }
    }

    /// <summary>
    /// After the briefing: which buildings the player is told of at the start of a case. Every organisation's
    /// buildings within reach of the agent's side (all of the CIA's), else its nearest two buildings; then every city
    /// gets the nearest of the organisations from 4 on that have a building there, up to two or three known in all.
    /// The organisations' known bits are cleared first; an organisation with a building within reach is known.
    /// </summary>
    /// <param name="state">The world and the buildings.</param>
    /// <remarks>The second part of <c>setup_new_case</c> (FINAL 0100:3f4c), after the briefing; registered as new code.</remarks>
    public static void LinkOrganisations(FinalState state)
    {
        const int known = 4, unreachable = 999, cia = 0, firstForeignOrg = 4;
        foreach (var organisation in state.Org)
        {
            organisation.Clues = 0;
        }
        for (var org = 0; org < CampaignTables.WorldSize; org++)
        {
            var nearestDistance = unreachable;
            int nearest = -1, secondNearest = -1;
            for (var city = 0; city < CampaignTables.WorldSize; city++)
            {
                var reached = OrgReachesCity(state, OwnSide, org, city);
                if (BuildingLevel(state, city, org) != 0)
                {
                    var distance = GameLibrary.KeyDistance(state.Ct[city].Climate, state.Org[org].Climate);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        secondNearest = nearest;
                        nearest = city;
                    }
                }
                if (reached || org == cia)
                {
                    TellOfBuilding(state, org, city);
                    state.Org[org].Clues |= 1;
                    // C: no later building can be nearer than -1: the two nearest stay those before the first reached
                    nearestDistance = -1;
                }
            }
            if (nearest != -1)
            {
                TellOfBuilding(state, org, nearest);
            }
            if (secondNearest != -1)
            {
                TellOfBuilding(state, org, secondNearest);
            }
        }
        for (var city = 0; city < CampaignTables.WorldSize; city++)
        {
            var reachable = 0;
            var nearestDistance = unreachable;
            int nearest = -1, secondNearest = -1;
            for (var org = firstForeignOrg; org < CampaignTables.WorldSize; org++)
            {
                var reached = OrgReachesCity(state, OwnSide, org, city);
                if (BuildingLevel(state, city, org) != 0)
                {
                    var distance = GameLibrary.KeyDistance(state.Ct[city].Climate, state.Org[org].Climate);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        secondNearest = nearest;
                        nearest = org;
                    }
                }
                if (reached)
                {
                    reachable++;
                }
            }
            if (reachable < 2 && nearest != -1)
            {
                TellOfBuilding(state, nearest, city);
            }
            if (reachable < 3 && secondNearest != -1)
            {
                TellOfBuilding(state, secondNearest, city);
            }
        }

        void TellOfBuilding(FinalState s, int org, int city)
        {
            // FINAL's add_org_city_link is the library's add_link
            CampaignLibrary.AddLink(s.Lc, s.Ct, s.Nloc, org, city, known, out var buildings);
            s.Nloc = (short)buildings;
            // C: add_org_city_link has set this bit already; the original sets it again
            s.Ct[city].OrgClues |= unchecked((short)(1 << org));
        }
    }

    /// <summary>
    /// At the higher levels, four more members of the cast per level, Red Herrings who have nothing to do with the plot:
    /// each a new person of a random organisation (4 and on, not the MasterMind's) in a city where it has a building
    /// and nobody stands for it yet, with a random cover (3..6), key (its bits 1-6 unlike every member's before it),
    /// plot step, clue type and rank.
    /// </summary>
    /// <param name="state">The cast and the persons.</param>
    /// <remarks>The last part of <c>setup_new_case</c> (FINAL 0100:3f4c); registered as new code.</remarks>
    public static void AddRedHerrings(FinalState state)
    {
        const int redHerringBit = unchecked((short)0x8000), keyBits = 0x7e;
        var cast = state.NACTIVITY;
        for (var h = cast; h < state.Diff * 4 + cast; h++)
        {
            int org, city;
            do
            {
                do
                {
                    org = Random(state, 12) + 4;
                }
                while (org == state.Pl.BigOrg);
                do
                {
                    city = Random(state, CampaignTables.WorldSize);
                }
                while (BuildingLevel(state, city, org) == 0);
            }
            while (FindOrCreatePerson(state, org, city, 0) != -1);
            var member = state.Ac[h];
            member.Who = (short)FindOrCreatePerson(state, org, city, redHerringBit);
            member.Name = state.RedHerringRole;
            member.Cover = (short)(Random(state, 4) + 3);
            bool unique;
            do
            {
                member.Key = (short)Random(state, 0x7fff);
                unique = true;
                for (var j = 0; j < h; j++)
                {
                    if ((member.Key & keyBits) == (state.Ac[j].Key & keyBits))
                    {
                        unique = false;
                    }
                }
            }
            while (!unique);
            member.Status = 0;
            member.Clued = 0;
            state.Ac2[h].Chron = (short)(Random(state, state.NEVENT / 2) + 4);
            state.Ac2[h].Intel = 0;
            member.Ctype = (short)Random(state, 7);
            member.Rank = (short)Random(state, 7);
        }
        state.NACTIVITY += (short)(state.Diff * 4);
    }

    /// <summary>Marks every event of the plot as not yet happened: its plot step becomes 999.</summary>
    /// <param name="state">The events.</param>
    // orig: FINAL 0100:46b5 SetChron (src/c/final/final_0100_46b5.c) matched; tests: CaseSetupTests, FinalOracleTests
    public static void SetChron(FinalState state)
    {
        const short notYetHappened = 999;
        for (var i = 0; i < state.NEVENT; i++)
        {
            state.Ev[i].Chron = notYetHappened;
        }
    }
}
