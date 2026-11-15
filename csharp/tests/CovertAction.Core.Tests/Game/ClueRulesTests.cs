using CovertAction.Core.Campaign;

namespace CovertAction.Core.Tests.Game;

/// <summary>What a clue is about, on hand-made campaigns, with expected values worked out from the C of make_clue_subject (GAME 00f7:5393).</summary>
public sealed class ClueRulesTests
{
    private static CampaignState OneClue()
    {
        var state = new CampaignState();
        state.Cl[3].Act = 2;           // clue 3 is about cast member 2
        state.Ac[2].Who = 9;           // who is person 9
        state.Ps[9].Org = 6;
        state.Ps[9].Loc = 11;
        state.Orgs[6].Name = "Org6";
        state.Txt.Text = "<";
        return state;
    }

    [Fact]
    public void ACityClueCountsTheCity()
    {
        var state = OneClue();
        Assert.Equal(0x080b, ClueRules.MakeClueSubject(state, 3, ClueRules.SubjectCity));
        Assert.Equal(1, state.Active[11]);
    }

    [Fact]
    public void AnOrganisationClueCountsItMakesItKnownAndNamesIt()
    {
        var state = OneClue();
        Assert.Equal(0x0406, ClueRules.MakeClueSubject(state, 3, ClueRules.SubjectOrg));
        Assert.Equal((1, 1), (state.OrgCounters[6], state.Orgs[6].Clues));
        Assert.Equal("<Org6", state.Txt.Text);
    }

    [Theory]
    [InlineData(ClueRules.SubjectFace, 0x0109)]
    [InlineData(ClueRules.SubjectName, 0x0209)]
    [InlineData(ClueRules.SubjectCastMember, 0x1002)]
    [InlineData(3, 3)]                 // no kind: the kind itself
    public void APersonOrCastMemberClueNamesThem(int kind, int subject)
    {
        var state = OneClue();
        Assert.Equal(subject, ClueRules.MakeClueSubject(state, 3, kind));
        Assert.Equal("<", state.Txt.Text);
    }
}
