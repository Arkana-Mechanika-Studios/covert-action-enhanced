using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Tests.Final;

/// <summary>
/// The rules that lay a new case out, on hand-made states; the expected values are worked out from the reconstructed C of
/// FINAL (building_level, org_reaches_city, add_org_city_link, find_or_create_person, free_person,
/// place_cast_member, pack_mastermind_look, SetChron, random, and setup_new_case after the briefing, which the recorded
/// runs do not reach), not from the C#.
/// </summary>
public sealed class CaseSetupTests
{
    /// <summary>A world whose organisations and cities all sit in map cell (15, 0), with police 0, size 4 and covert 2; nobody is the MasterMind.</summary>
    private static FinalState World()
    {
        var state = new FinalState { Diff = 1 };
        foreach (var city in state.Ct)
        {
            city.Climate = 0x0f;
        }
        foreach (var org in state.Org)
        {
            org.Climate = 0x0f;
            org.Size = 4;
            org.Covert = 2;
        }
        state.Pl.BigOrg = -1;
        state.Pl.BigLoc = -1;
        return state;
    }

    [Fact]
    public void ABuildingsSizeComesFromTheWeightsAndTheDistance()
    {
        var state = World();
        // (0 + 4) * 4 / (0 + 1) = 16; 6 * 16 / 2 + 1 = 49, at most 5
        Assert.Equal(5, CaseSetup.BuildingLevel(state, 3, 7));
        // a city 15 columns away: distance 2 * 15 = 30; 16 / 31 = 0: no building
        state.Ct[3].Climate = 0;
        Assert.Equal(0, CaseSetup.BuildingLevel(state, 3, 7));
        // ... except for the MasterMind's own pair
        state.Pl.BigOrg = 7;
        state.Pl.BigLoc = 3;
        Assert.Equal(1, CaseSetup.BuildingLevel(state, 3, 7));
    }

    [Fact]
    public void AtLevelZeroTheFirstMembersOrganisationHasABuildingInCityOne()
    {
        var state = World();
        state.Ct[1].Climate = 0;
        state.Ac[0].Who = 2;
        state.Ps[2].Org = 7;
        Assert.Equal(0, CaseSetup.BuildingLevel(state, 1, 7));
        state.Diff = 0;
        Assert.Equal(1, CaseSetup.BuildingLevel(state, 1, 7));
        Assert.Equal(0, CaseSetup.BuildingLevel(state, 1, 6));
    }

    [Fact]
    public void AnOrganisationWithoutCovertWeightStopsTheOriginalWithADivideError()
    {
        var state = World();
        state.Org[7].Covert = 0;
        Assert.Throws<DivideErrorException>(() => CaseSetup.BuildingLevel(state, 3, 7));
    }

    [Fact]
    public void AnOrganisationReachesACityWithinSixteenAndFourTimesTheBuilding()
    {
        var state = World();
        // all in one cell: 0 + 0 < 4 * 5 + 16
        Assert.True(CaseSetup.OrgReachesCity(state, 2, 7, 3));
        // the reaching organisation 15 columns away: 30 * 3 / 2 + 30 = 75, not below 16 + 4 * 5
        state.Org[2].Climate = 0;
        Assert.False(CaseSetup.OrgReachesCity(state, 2, 7, 3));
    }

    [Fact]
    public void ABuildingIsAddedOnceAndItsBitsGrow()
    {
        var state = World();
        Assert.Equal(-1, AddLink(state, 4, 9, 0));
        Assert.Equal(0, state.Nloc);
        Assert.Equal(0, AddLink(state, 4, 9, 2));
        Assert.Equal(1, state.Nloc);
        Assert.Equal(1 << 4, state.Ct[9].OrgClues);
        Assert.Equal(0, AddLink(state, 4, 9, 8));
        Assert.Equal(10, state.Lc[0].Clues);
        Assert.Equal(1, state.Nloc);
    }

    [Fact]
    public void APersonIsFoundByOrganisationAndCity()
    {
        var state = World();
        state.Nperson = 3;
        state.Ps[2].Org = 4;
        state.Ps[2].Loc = 9;
        Assert.Equal(2, CaseSetup.FindOrCreatePerson(state, 4, 9, 0x10));
        Assert.Equal(0x10, state.Ps[2].Role);
    }

    [Fact]
    public void TheMastermindsPairIsPersonZeroWithHisFace()
    {
        var state = World();
        state.Pl.BigOrg = 4;
        state.Pl.BigLoc = 9;
        state.Pl.BigFace = 0x1234;
        state.Pl.Clues = 6;
        Assert.Equal(0, CaseSetup.FindOrCreatePerson(state, 4, 9, 1));
        Assert.Equal(0x1234, state.Ps[0].Face);
        Assert.Equal(6, state.Ps[0].Clues);
        Assert.Equal(1, state.Ps[0].Role);
        // the draw for a free place happened anyway
        Assert.NotEqual(1u, state.Random.State);
    }

    [Fact]
    public void ALookUpOfAnUnknownPairMakesNoPerson()
    {
        var state = World();
        Assert.Equal(-1, CaseSetup.FindOrCreatePerson(state, 4, 9, 0));
        Assert.All(state.Ps, p => Assert.Equal(0, p.Face));
    }

    [Fact]
    public void APersonIsFreedOnlyWhenNoMemberIsMappedOntoIt()
    {
        var state = World();
        var person = state.Ps[5];
        person.Face = 0x77;
        person.Org = 3;
        person.Loc = 4;
        person.Clues = 1;
        person.Role = 2;
        CampaignLibrary.ClearUnusedPerson(state.Ps, 5);
        Assert.Equal(0x77, person.Face);
        // a decoy's mark alone does not keep the record
        person.Role = unchecked((short)0x8000);
        CampaignLibrary.ClearUnusedPerson(state.Ps, 5);
        Assert.Equal((0, -1, -1, 0), (person.Face, person.Org, person.Loc, person.Clues));
    }

    [Fact]
    public void TheMastermindIsPlacedWithHisOrganisationAndCity()
    {
        var state = World();
        state.Nperson = 64;
        state.Pl.BigOrg = 4;
        state.Pl.BigLoc = 9;
        state.Pl.BigFace = 0x0400;
        state.Ac[3].Status = (short)(CastPlacement.Mastermind | CastPlacement.Org1 | CastPlacement.City1 | CastPlacement.Female);
        state.TheOrg1 = 4;
        state.TheCity1 = 9;
        Assert.True(CaseSetup.PlaceCastMember(state, 3));
        Assert.Equal(0, state.Ac[3].Who);
        Assert.Equal(1 << 3, state.Ps[0].Role);
        Assert.Equal(0x0400, state.Ps[0].Face);
        Assert.Equal(-1, state.Ac2[3].Chron);
    }

    /// <summary>
    /// When the tries run out with nobody for the last pair, the original goes on with person -1 (it reads and writes
    /// the "env.sve" text in front of the table, changing nothing) and reports the failure, so the case is laid out
    /// again. Found in the first played session, where the C# stopped with an index error.
    /// </summary>
    [Fact]
    public void AMemberWhoCannotBePlacedInHisTriesIsReportedAndNotGivenAPerson()
    {
        var state = World();
        state.Nperson = 64;
        // the organisation asked for is too small to have a building anywhere, and nobody belongs to it yet
        state.Org[4].Size = 0;
        state.Ac[3].Status = (short)(CastPlacement.Org1 | CastPlacement.City1 | CastPlacement.Female);
        state.TheOrg1 = 4;
        state.TheCity1 = 9;

        Assert.False(CaseSetup.PlaceCastMember(state, 3));
        Assert.Equal(-1, state.Ac[3].Who);
    }

    [Fact]
    public void AMastermindsLookIsPackedIntoAWord()
    {
        var state = World();
        // a man (0), head 1, parts 2, 3, 4, 5, shade 3
        short[] look = [0, 1, 2, 3, 4, 5, 3, 0];
        look.CopyTo(state.MastermindLooks[0], 0);
        var expected = (1 << 1) + (3 << 2) + 1 + (1 << 4) + (2 << 7) + (3 << 10) + (4 << 13);
        Assert.Equal(unchecked((short)expected), CaseSetup.PackMastermindLook(state, 0));
    }

    [Fact]
    public void EveryEventInUseIsMarkedNotYetHappened()
    {
        var state = World();
        state.NEVENT = 2;
        CaseSetup.SetChron(state);
        Assert.Equal((999, 999, 0), (state.Ev[0].Chron, state.Ev[1].Chron, state.Ev[2].Chron));
    }

    [Fact]
    public void EveryOrganisationWithinReachIsKnownInAllItsCities()
    {
        // all in one cell: every organisation's building reaches the agent's side (setup_new_case, its second part)
        var state = World();
        state.Org[5].Clues = 7;
        CaseSetup.LinkOrganisations(state);
        Assert.Equal(CampaignTables.WorldSize * CampaignTables.WorldSize, state.Nloc);
        Assert.All(state.Ct, city => Assert.Equal(-1, city.OrgClues));
        Assert.All(state.Org, org => Assert.Equal(1, org.Clues));
        Assert.All(state.Lc, building => Assert.Equal(4, building.Clues));
    }

    [Fact]
    public void OutOfReachAnOrganisationIsKnownInItsNearestCityAndACityGetsItsNearestOrganisation()
    {
        // the agent's side 15 columns away: only the CIA (organisation 0) is linked everywhere; an organisation's
        // nearest building is its first city, 0 (no later one is nearer); the agent's side has no building at all;
        // every city gets organisation 4, the nearest of 4..15, because none of them reaches it
        var state = World();
        state.Org[1].Climate = 0x00;
        state.Org[5].Clues = 7;
        CaseSetup.LinkOrganisations(state);
        Assert.Equal(16 + 14 + 15, state.Nloc);
        Assert.Equal(unchecked((short)0xfffd), state.Ct[0].OrgClues);
        Assert.All(state.Ct.Skip(1), city => Assert.Equal(0x11, city.OrgClues));
        Assert.Equal([1, 0, 0, 0, 0, 0], state.Org.Take(6).Select(org => (int)org.Clues));
        Assert.Equal(-1, CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, 1, 0, 0, out _));
        Assert.Equal(-1, CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, 5, 3, 0, out _));
        Assert.NotEqual(-1, CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, 4, 9, 0, out _));
    }

    [Fact]
    public void EachLevelAddsFourRedHerringsWithPersonsOfTheirOwn()
    {
        var state = World();
        state.Diff = 2;
        state.NACTIVITY = 2;
        state.NEVENT = 10;
        state.Nperson = CampaignTables.PersonCount;
        state.Pl.BigOrg = 6;
        state.RedHerringRole = "Red Herring";
        state.Ac[0].Key = 0x0002;
        state.Ac[1].Key = 0x0004;
        CaseSetup.AddRedHerrings(state);
        Assert.Equal(10, state.NACTIVITY);
        var herrings = state.Ac.Skip(2).Take(8).ToList();
        Assert.All(herrings, member =>
        {
            Assert.Equal("Red Herring", member.Name);
            Assert.InRange(member.Cover, 3, 6);
            Assert.InRange(member.Ctype, 0, 6);
            Assert.InRange(member.Rank, 0, 6);
            Assert.Equal((0, 0), (member.Status, member.Clued));
            var person = state.Ps[member.Who];
            // a new person of an organisation from 4 on, not the MasterMind's, standing for "a Red Herring" only
            Assert.Equal(unchecked((short)0x8000), person.Role);
            Assert.InRange(person.Org, 4, 15);
            Assert.NotEqual(6, person.Org);
        });
        // no two members share the key bits 1-6, and every Red Herring's person is another
        Assert.Equal(10, state.Ac.Take(10).Select(member => member.Key & 0x7e).Distinct().Count());
        Assert.Equal(8, herrings.Select(member => member.Who).Distinct().Count());
        // plot steps 4 .. 4 + 10 / 2 - 1
        Assert.All(state.Ac2.Skip(2).Take(8), second => Assert.InRange(second.Chron, 4, 8));
    }

    [Fact]
    public void AtLevelZeroNoRedHerringJoins()
    {
        var state = World();
        state.Diff = 0;
        state.NACTIVITY = 2;
        CaseSetup.AddRedHerrings(state);
        Assert.Equal(2, state.NACTIVITY);
        Assert.Equal("", state.Ac[2].Name);
    }

    [Fact]
    public void TheDiceAreTheLibrarysOne()
    {
        var state = World();
        // rand() from seed 1 gives 41, then 18467: 41 * 100 >> 15 = 0, 18467 * 100 >> 15 = 56
        Assert.Equal(0, CaseSetup.Random(state, 100));
        Assert.Equal(56, CaseSetup.Random(state, 100));
    }

    /// <summary>The library's add_link on a part's tables, keeping the part's count of buildings.</summary>
    private static int AddLink(FinalState state, int org, int city, int flags)
    {
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, flags, out var count);
        state.Nloc = (short)count;
        return link;
    }
}
