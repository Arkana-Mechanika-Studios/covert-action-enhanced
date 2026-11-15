using CovertAction.Core.Raid;
using CovertAction.Msc;

namespace CovertAction.Core.Tests.Raid;

/// <summary>The raid's world rules, with the values worked out from the original's C (TAC 00e8:b321, b16d).</summary>
public sealed class RaidIntelligenceRulesTests
{
    private static RaidState NewState()
    {
        var state = new RaidState { Diff = 1 };
        state.Pl.BigLoc = 9;
        state.Pl.BigOrg = 9;
        state.Ct[2].Climate = 0x11;
        state.Ct[2].Police = 2;
        state.Org[3].Climate = 0x34;
        state.Org[3].Size = 20;
        state.Org[3].Covert = 3;
        return state;
    }

    [Fact]
    public void PresenceGrowsWithPoliceAndSizeAndShrinksWithDistanceAndCovertness()
    {
        // distance 2 * 3 + 2 = 8; (2 + 4) * 20 / 9 = 13; 6 * 13 / 3 + 1 = 27, capped at 5
        Assert.Equal(5, RaidIntelligenceRules.OrgPresenceInCity(NewState(), 2, 3));
        var state = NewState();
        state.Org[3].Covert = 40;
        Assert.Equal(2, RaidIntelligenceRules.OrgPresenceInCity(state, 2, 3));      // 6 * 13 / 40 + 1
    }

    [Fact]
    public void ACovertOrganisationOfNoCovertnessStopsTheOriginalWithADivideError()
    {
        var state = NewState();
        state.Org[3].Covert = 0;
        Assert.Throws<DivideErrorException>(() => RaidIntelligenceRules.OrgPresenceInCity(state, 2, 3));
    }

    [Fact]
    public void TheHomeCityAndOrganisationAlwaysGiveOne()
    {
        var state = NewState();
        state.Org[9].Covert = 1;
        Assert.Equal(1, RaidIntelligenceRules.OrgPresenceInCity(state, 9, 9));
    }

    [Fact]
    public void AnOrganisationIsPlausibleWhereItIsPresentNearby()
    {
        var state = NewState();
        state.Org[1].Climate = 0x11;
        // from org 1: to city 2 distance 0, to org 3 distance 8 * 3 / 2 = 12; 5 * 4 + 16 = 36 > 12
        Assert.True(RaidIntelligenceRules.OrgPlausibleInCity(state, 1, 3, 2));
    }
}
