using CovertAction.Core.Campaign;
using CovertAction.Msc;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// The map of the campaign on hand-made worlds, with expected values worked out from the C of map_distance
/// (GAME 00f7:06b3), building_plausible (063f), org_presence (07f3) and raise_suspicion (39bb).
/// </summary>
public sealed class WorldRulesTests
{
    /// <summary>A place of the map: column in the low four bits, row in the high four.</summary>
    private static short Place(int column, int row) => (short)(row << 4 | column);

    [Theory]
    [InlineData(0x23, 0x88, 17)]    // 5 across, 6 down: 2 * 6 + 5
    [InlineData(0xc6, 0xc6, 0)]
    [InlineData(0x23, 0xac, 26)]    // 9 across, 8 down: 2 * 9 + 8
    [InlineData(0x1f00, 0x0001, 2)] // bits above the byte are ignored
    public void TheDistanceCountsTheLongerAxisTwice(int a, int b, int distance)
    {
        Assert.Equal(distance, WorldRules.MapDistance(a, b));
    }

    private static CampaignState World()
    {
        var state = new CampaignState();
        state.Pl.BigOrg = -1;
        state.Pl.BigLoc = -1;
        state.Diff = 1;
        return state;
    }

    [Fact]
    public void PresenceGrowsWithPoliceAndSizeAndFallsWithDistanceAndSecrecy()
    {
        var state = World();
        state.Ct[3].Climate = Place(2, 2);
        state.Ct[3].Police = 6;
        state.Orgs[5].Climate = Place(3, 2);  // distance 2
        state.Orgs[5].Size = 9;
        state.Orgs[5].Covert = 10;
        // (6 + 4) * 9 / (2 + 1) = 30; 6 * 30 / 10 + 1 = 19, at most 5
        Assert.Equal(5, WorldRules.OrgPresence(state, 3, 5));
        state.Orgs[5].Covert = 60;
        // 6 * 30 / 60 + 1 = 4
        Assert.Equal(4, WorldRules.OrgPresence(state, 3, 5));
        state.Orgs[5].Size = 0;
        Assert.Equal(0, WorldRules.OrgPresence(state, 3, 5));
    }

    [Fact]
    public void TheMastermindsOrganisationIsAlwaysInTheMastermindsCity()
    {
        var state = World();
        state.Orgs[5].Covert = 1;
        state.Pl.BigOrg = 5;
        state.Pl.BigLoc = 3;
        Assert.Equal(1, WorldRules.OrgPresence(state, 3, 5));
    }

    [Fact]
    public void OnTheEasiestLevelTheFirstCastMembersOrganisationIsInCityOne()
    {
        var state = World();
        state.Orgs[5].Covert = 1;
        state.Ac[0].Who = 7;
        state.Ps[7].Org = 5;
        Assert.Equal(0, WorldRules.OrgPresence(state, 1, 5));
        state.Diff = 0;
        Assert.Equal(1, WorldRules.OrgPresence(state, 1, 5));
        Assert.Equal(0, WorldRules.OrgPresence(state, 2, 5));
    }

    [Fact]
    public void AnOrganisationWithoutSecrecyStopsTheOriginal()
    {
        var state = World();
        state.Ct[3].Police = 6;
        state.Orgs[5].Size = 9;
        Assert.Throws<DivideErrorException>(() => WorldRules.OrgPresence(state, 3, 5));
    }

    [Fact]
    public void ABuildingIsPlausibleNearTheOrganisationsBase()
    {
        var state = World();
        state.Orgs[2].Climate = Place(0, 0);
        state.Orgs[5].Climate = Place(4, 0);   // 8 from org 2
        state.Orgs[5].Covert = 1;
        state.Ct[3].Climate = Place(0, 2);     // 4 from org 2
        // 4 + 8 * 3 / 2 = 16 against 16 + 4 * presence 0
        Assert.False(WorldRules.BuildingPlausible(state, 2, 5, 3));
        state.Ct[3].Climate = Place(0, 1);     // 2 from org 2: 14 < 16
        Assert.True(WorldRules.BuildingPlausible(state, 2, 5, 3));
    }

    [Fact]
    public void SuspicionRisesMoreForPersonsNearTheAction()
    {
        var state = World();
        state.Nperson = 3;
        state.Ps[0].Face = 1;                       // at the place of the action: (4 * 2 << 8) / 21 = 97, 2500 / 97 = 25
        state.Ps[1].Face = 1;
        state.Ps[1].Loc = 1;                        // city 1 is 6 away: (4 * 8 << 8) / 21 = 390, 2500 / 390 = 6
        state.Ct[1].Climate = Place(3, 0);
        state.Ps[2].Face = 1;
        state.Ps[2].Alert = -1;                     // under arrest: not changed
        WorldRules.RaiseSuspicion(state, 0, 0, 20);
        Assert.Equal(10, state.Alerted);
        Assert.Equal(25, state.Ps[0].Alert);
        Assert.Equal(6, state.Ps[1].Alert);
        Assert.Equal(-1, state.Ps[2].Alert);
    }

    [Fact]
    public void NoNoiseRaisesNothing()
    {
        var state = World();
        state.Nperson = 1;
        state.Ps[0].Face = 1;
        WorldRules.RaiseSuspicion(state, 0, 0, 0);
        Assert.Equal((0, 0), (state.Alerted, state.Ps[0].Alert));
    }
}
