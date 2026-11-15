using CovertAction.Core.Shared;
using CovertAction.Core.Electronics;

namespace CovertAction.Core.Tests.Electronics;

/// <summary>
/// What a wiretap writes into the campaign, on hand-made tables, with expected values worked out from the C of
/// add_link (BUG 00e8:272b), find_or_create_person_quiet (bug_00e8_2627.c) and format_game_time (bug_00e8_50e5.c).
/// </summary>
public sealed class IntelligenceRulesTests
{
    [Fact]
    public void ANewBuildingIsAddedAndItsCityNotesTheOrganisation()
    {
        var state = new ElectronicsState { Nloc = 2 };
        Assert.Equal(2, AddLink(state, 5, 3, 12));
        Assert.Equal(3, state.Nloc);
        Assert.Equal((5, 3, 12), (state.Lc[2].Org, state.Lc[2].Loc, state.Lc[2].Clues));
        Assert.Equal(1 << 5, state.Ct[3].OrgClues);
    }

    [Fact]
    public void AKnownBuildingGetsTheNewBitsAdded()
    {
        var state = new ElectronicsState { Nloc = 1 };
        state.Lc[0].Org = 5;
        state.Lc[0].Loc = 3;
        state.Lc[0].Clues = 1;
        Assert.Equal(0, AddLink(state, 5, 3, 4));
        Assert.Equal(5, state.Lc[0].Clues);
        Assert.Equal(1, state.Nloc);
    }

    [Fact]
    public void NoBitsOnlyLooksUp()
    {
        var state = new ElectronicsState { Nloc = 1 };
        state.Lc[0].Org = 5;
        state.Lc[0].Loc = 3;
        Assert.Equal(-1, AddLink(state, 5, 3, 0));   // known, but without clue bits
        Assert.Equal(-1, AddLink(state, 6, 3, 0));   // unknown: nothing is added
        Assert.Equal(1, state.Nloc);
        Assert.Equal(0, state.Ct[3].OrgClues);
    }

    [Fact]
    public void AKnownPersonGetsTheRoleBitsAdded()
    {
        var state = new ElectronicsState { Nperson = 3 };
        state.Ps[2].Org = 4;
        state.Ps[2].Loc = 7;
        state.Ps[2].Role = 1;
        var before = state.Random.State;
        Assert.Equal(2, IntelligenceRules.FindOrCreatePersonQuiet(state, 4, 7, 8));
        Assert.Equal(9, state.Ps[2].Role);
        Assert.Equal(before, state.Random.State);   // no draw
    }

    [Fact]
    public void ANewPersonTakesAFreeSlotOtherThanTheMastermindsAndGetsAFace()
    {
        var state = new ElectronicsState();
        state.Random.Srand(7);
        // only slots 0 (the mastermind's) and 40 are free
        for (var i = 1; i < state.Ps.Length; i++)
        {
            state.Ps[i].Face = (short)(i == 40 ? 0 : 1);
        }
        Assert.Equal(40, IntelligenceRules.FindOrCreatePersonQuiet(state, 4, 7, 2));
        var person = state.Ps[40];
        Assert.Equal((4, 7, 2, 0), (person.Org, person.Loc, person.Role, person.Clues));
        Assert.InRange(person.Face, 1, 0x7fff);
    }

    [Fact]
    public void TheMastermindsOrganisationAndCityGiveTheMastermindsSlot()
    {
        var state = new ElectronicsState();
        state.Pl.BigOrg = 4;
        state.Pl.BigLoc = 7;
        state.Pl.Clues = 0x21;
        state.Pl.BigFace = 0x1234;
        state.Ps[0].Face = 1;   // whatever slot 0 holds
        Assert.Equal(0, IntelligenceRules.FindOrCreatePersonQuiet(state, 4, 7, 2));
        Assert.Equal((0x21, 0x1234), (state.Ps[0].Clues, state.Ps[0].Face));
    }

    [Fact]
    public void NothingLearnedAddsNoOne()
    {
        var state = new ElectronicsState();
        Assert.Equal(-1, IntelligenceRules.FindOrCreatePersonQuiet(state, 4, 7, 0));
        Assert.All(state.Ps, person => Assert.Equal(0, person.Role));
    }

    /// <summary>City 2 (police 6) and organisation 3 (size 5, secrecy 10), keys given as (column, row) nibbles.</summary>
    private static ElectronicsState World(int cityKey, int orgKey, int covert = 10)
    {
        var state = new ElectronicsState { Diff = 1 };
        state.Pl.BigLoc = state.Pl.BigOrg = -1;
        state.Ct[2].Police = 6;
        state.Ct[2].Climate = (short)cityKey;
        state.Org[3].Size = 5;
        state.Org[3].Covert = (short)covert;
        state.Org[3].Climate = (short)orgKey;
        return state;
    }

    [Theory]
    [InlineData(0x00, 0x00, 5)]     // distance 0: (6 + 4) * 5 / 1 = 50; 6 * 50 / 10 + 1 = 31, at most 5
    [InlineData(0x00, 0x09, 2)]     // distance 18: 50 / 19 = 2; 6 * 2 / 10 + 1 = 2
    [InlineData(0x00, 0xff, 1)]     // distance 45: 50 / 46 = 1; 6 * 1 / 10 + 1 = 1
    public void PresenceFallsWithTheDistanceOfTheKeys(int cityKey, int orgKey, int presence)
    {
        Assert.Equal(presence, IntelligenceRules.OrgPresence(World(cityKey, orgKey), 2, 3));
    }
    [Fact]
    public void TheMastermindPairIsAlwaysPresent()
    {
        var state = World(0x00, 0xff);
        state.Ct[2].Police = -4;   // (police + 4) * size = 0: not present at all
        Assert.Equal(0, IntelligenceRules.OrgPresence(state, 2, 3));
        state.Pl.BigLoc = 2;
        state.Pl.BigOrg = 3;
        Assert.Equal(1, IntelligenceRules.OrgPresence(state, 2, 3));
    }

    [Fact]
    public void AnOrganisationWithoutSecrecyEndsTheGameWithADivideError()
    {
        var state = World(0x00, 0x00, covert: 0);
        Assert.Throws<CovertAction.Msc.DivideErrorException>(() => IntelligenceRules.OrgPresence(state, 2, 3));
    }

    [Fact]
    public void ANearOrganisationIsPlausibleAFarOneNot()
    {
        var state = World(0x00, 0x00);
        state.Org[0].Climate = 0x00;
        Assert.True(IntelligenceRules.OrgLinkPlausible(state, 0, 3, 2));      // 4 * 5 + 16 > 0
        state.Org[0].Climate = 0xff;                                          // 45 * 3 / 2 + 45 = 112
        Assert.False(IntelligenceRules.OrgLinkPlausible(state, 0, 3, 2));
    }

    [Theory]
    [InlineData(0, 0, "00:00:00")]
    [InlineData(600, 75, "10:01:15")]
    [InlineData(1439, 60, "00:00:00")]     // midnight wraps
    [InlineData(100, -1, "01:40:0/")]      // -1 / 60 is 0 minutes; -1 % 60 is -1: its units digit is the character before '0'
    public void TheClockIsTheCampaignsClockPlusTheSeconds(int gtime, int seconds, string text)
    {
        var state = new ElectronicsState { Gtime = (short)gtime, ClockText = "00:00:00" };
        Assert.Equal(text, IntelligenceRules.FormatGameTime(state, seconds));
        Assert.Equal(text, state.ClockText);
    }

    /// <summary>The library's add_link on the part's tables, keeping the part's count of buildings.</summary>
    private static int AddLink(ElectronicsState state, int org, int city, int flags)
    {
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, flags, out var count);
        state.Nloc = (short)count;
        return link;
    }
}
