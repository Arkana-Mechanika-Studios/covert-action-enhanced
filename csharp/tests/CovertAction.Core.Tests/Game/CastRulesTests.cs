using CovertAction.Core.Shared;
using CovertAction.Core.Campaign;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// Persons, buildings and events on hand-made campaigns, with expected values worked out from the C of
/// find_or_add_person_quiet (GAME 00f7:4d4b), release_person (49d1), find_or_add_link (4e4f),
/// clear_person_knowledge (91fa), try_select_person (951c) and clear_event_steps (3c5d).
/// </summary>
public sealed class CastRulesTests
{
    [Fact]
    public void AKnownPersonGetsTheCastBitsAddedWithoutADraw()
    {
        var state = new CampaignState { Nperson = 3 };
        state.Ps[2].Org = 4;
        state.Ps[2].Loc = 7;
        state.Ps[2].Role = 1;
        var before = state.Random.State;
        Assert.Equal(2, CastRules.FindOrAddPersonQuiet(state, 4, 7, 8));
        Assert.Equal(9, state.Ps[2].Role);
        Assert.Equal(before, state.Random.State);
    }

    [Fact]
    public void ANewPersonTakesAFreeSlotOtherThanTheMastermindsAndGetsAFace()
    {
        var state = new CampaignState();
        state.Pl.BigOrg = -1;
        state.Random.Srand(7);
        for (var i = 1; i < state.Ps.Length; i++)
        {
            state.Ps[i].Face = (short)(i == 40 ? 0 : 1);   // only slots 0 and 40 are free
        }
        Assert.Equal(40, CastRules.FindOrAddPersonQuiet(state, 4, 7, 2));
        var person = state.Ps[40];
        Assert.Equal((4, 7, 2, 0, 0), (person.Org, person.Loc, person.Role, person.Clues, person.Alert));
        Assert.InRange(person.Face, 1, 0x7fff);
    }

    [Fact]
    public void TheMastermindsOrganisationAndCityGiveTheMastermindsSlot()
    {
        var state = new CampaignState();
        state.Pl.BigOrg = 4;
        state.Pl.BigLoc = 7;
        state.Pl.Clues = 0x21;
        state.Pl.BigFace = 0x1234;
        state.Ps[0].Face = 1;
        Assert.Equal(0, CastRules.FindOrAddPersonQuiet(state, 4, 7, 2));
        Assert.Equal((0x21, 0x1234), (state.Ps[0].Clues, state.Ps[0].Face));
        Assert.Equal(-1, CastRules.FindOrAddPersonQuiet(state, 5, 7, 0));   // nothing to add: no one
    }

    [Fact]
    public void APersonOutsideTheCastIsReleased()
    {
        var state = new CampaignState();
        state.Ps[3].Face = 0x55;
        state.Ps[3].Org = 2;
        state.Ps[3].Loc = 5;
        state.Ps[3].Clues = 0xf;
        state.Ps[3].Role = unchecked((short)0x8000);   // a decoy is no member
        CastRules.ReleasePerson(state, 3);
        Assert.Equal((0, -1, -1, 0), (state.Ps[3].Face, state.Ps[3].Org, state.Ps[3].Loc, state.Ps[3].Clues));

        state.Ps[4].Face = 0x55;
        state.Ps[4].Role = 2;
        CastRules.ReleasePerson(state, 4);
        Assert.Equal(0x55, state.Ps[4].Face);
    }

    [Fact]
    public void ABuildingIsAddedOrGetsNewBits()
    {
        var state = new CampaignState { Nloc = 1 };
        state.Lc[0].Org = 5;
        state.Lc[0].Loc = 3;
        Assert.Equal(-1, AddLink(state, 5, 3, 0));    // known, but no bits
        Assert.Equal(-1, AddLink(state, 6, 3, 0));    // unknown: not added
        Assert.Equal(0, AddLink(state, 5, 3, 4));
        Assert.Equal(4, state.Lc[0].Clues);
        Assert.Equal(1, AddLink(state, 6, 3, 12));
        Assert.Equal((2, 6, 3, 12), (state.Nloc, state.Lc[1].Org, state.Lc[1].Loc, state.Lc[1].Clues));
        Assert.Equal(1 << 5 | 1 << 6, state.Ct[3].OrgClues);
    }

    [Fact]
    public void ForgettingAPhotoClearsItEverywhere()
    {
        var state = new CampaignState { NEVENT = 3 };
        state.Ps[9].Clues = 0x0f;
        state.Ps[9].Intel = 41;
        state.Ac[2].Who = 9;
        state.Ac[2].Clued = 0x1f;
        state.Ac2[2].Intel = 30;
        state.Ac[1].Who = 8;
        state.Ac[1].Clued = 0x1f;
        state.Ev[0].Act = 2;
        state.Ev[1].Act = 1;
        state.Ev[2].Act = -1;                        // the marker event: no member
        foreach (var clue in state.Cl)
        {
            clue.Act = CastRules.FreeClue;
        }
        state.Cl[4].Act = 2;
        state.Cl[4].Info1 = 0x0109;                  // a face clue about member 2
        state.Cl[5].Act = 2;
        state.Cl[5].Info1 = 0x0809;                  // a city clue: kept
        CastRules.ClearPersonKnowledge(state, 9, 1);
        Assert.Equal(0x8e, state.Ps[9].Clues);
        Assert.Equal(20, state.Ps[9].Intel);
        Assert.Equal((0x1e, 15), (state.Ac[2].Clued, state.Ac2[2].Intel));
        Assert.Equal(0x1f, state.Ac[1].Clued);
        Assert.Equal((CastRules.FreeClue, (short)2), (state.Cl[4].Act, state.Cl[5].Act));
    }

    private static CampaignState Arrests(int diff)
    {
        var state = new CampaignState { Diff = (short)diff, NACTIVITY = (short)(3 + 4 * diff), ExcludedPerson = -1 };
        state.Ac[1].Status = CastRules.CastMastermind;
        state.Ac[1].Who = 5;
        state.Ps[6].Alert = CastRules.Arrested;
        return state;
    }

    [Fact]
    public void APersonUnderArrestIsChosenOnTheHigherLevels()
    {
        var state = Arrests(2);
        Assert.True(CastRules.TrySelectPerson(state, 6));
        Assert.Equal((5, 6, 6), (state.FlaggedCastPerson, state.ExcludedPerson, state.Sprung));
    }

    [Fact]
    public void NoOneIsChosenOnTheLowerLevelsOrWhenTheMastermindIsArrestedOrThePersonSelected()
    {
        Assert.False(CastRules.TrySelectPerson(Arrests(1), 6));
        var mastermindArrested = Arrests(2);
        mastermindArrested.Ps[5].Alert = CastRules.Arrested;
        Assert.False(CastRules.TrySelectPerson(mastermindArrested, 6));
        var selected = Arrests(2);
        selected.Ps[6].Clues = CastRules.KnownSelected;
        Assert.False(CastRules.TrySelectPerson(selected, 6));
        Assert.False(CastRules.TrySelectPerson(Arrests(3), 7));   // not under arrest
    }

    [Fact]
    public void OnceSomeoneIsExcludedTheChoiceIsARoll()
    {
        var state = Arrests(3);
        state.ExcludedPerson = 2;
        var before = state.Random.State;
        var chosen = CastRules.TrySelectPerson(state, 6);
        Assert.NotEqual(before, state.Random.State);   // one draw of 0..7 against the level
        Assert.Equal(chosen, state.Sprung == 6);
    }

    [Fact]
    public void EveryEventBecomesPending()
    {
        var state = new CampaignState { NEVENT = 4 };
        state.Ev[4].Chron = 5;
        CastRules.ClearEventSteps(state);
        Assert.All(state.Ev.Take(4), e => Assert.Equal(CastRules.ChronPending, e.Chron));
        Assert.Equal(5, state.Ev[4].Chron);
    }

    /// <summary>The library's add_link on a part's tables, keeping the part's count of buildings.</summary>
    private static int AddLink(CampaignState state, int org, int city, int flags)
    {
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, flags, out var count);
        state.Nloc = (short)count;
        return link;
    }
}
