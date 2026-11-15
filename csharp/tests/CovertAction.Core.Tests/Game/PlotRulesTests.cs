using CovertAction.Core.Campaign;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// The plot step's rules on hand-made plots, with expected values worked out from the C of advance_world_clock
/// (GAME 00f7:8755), event_ready (92b9) and queue_message (7834).
/// </summary>
public sealed class PlotRulesTests
{
    [Theory]
    [InlineData(9, 1, 11, 1, false, false)]    // still the morning of the same day
    [InlineData(11, 1, 12, 1, false, true)]    // noon passed
    [InlineData(14, 1, 15, 1, false, false)]   // afternoon
    [InlineData(23, 1, 0, 2, false, true)]     // a new day
    [InlineData(14, 1, 15, 1, true, true)]     // forced
    public void AStepIsTakenAtNoonAndAtANewDay(int hourBefore, int dayBefore, int hour, int day, bool force, bool step)
    {
        var state = new CampaignState { Hour = (short)hour, Day = (short)day, ChronTime = 4 };
        Assert.Equal(step, PlotRules.BeginPlotStep(state, hourBefore, dayBefore, force));
        Assert.Equal(step ? 5 : 4, state.ChronTime);
    }

    [Fact]
    public void TrailsAndTheAlarmFade()
    {
        var state = new CampaignState { Hour = 12, Diff = 1, Nperson = 3, Alerted = 40, ExcludedPerson = 7 };
        state.Ps[0].Face = 1;
        state.Ps[0].Alert = 50;                    // 50 - 50 / 4
        state.Ps[1].Alert = 50;                    // a free slot: unchanged
        state.Ps[2].Face = 1;
        state.Ps[2].Alert = -2;                    // in hiding: unchanged
        state.Ct[3].Clued = 5;
        Assert.True(PlotRules.BeginPlotStep(state, 11, 0, false));
        Assert.Equal((38, 50, -2), (state.Ps[0].Alert, state.Ps[1].Alert, state.Ps[2].Alert));
        Assert.Equal(30, state.Alerted);
        Assert.Equal(0, state.Ct[3].Clued);
        Assert.Equal(PlotRules.NoOneExcludedYet, state.ExcludedPerson);
    }

    [Fact]
    public void TheCaseEndsWhenNoOneIsLeftOrTimeIsUp()
    {
        var state = new CampaignState { Gtime = 1000 };
        Assert.True(PlotRules.CaseIsOver(state, true, 0));
        Assert.False(PlotRules.CaseIsOver(state, true, 2));
        state.Gtime = unchecked((short)0x9c41);   // past the last minute, read unsigned
        Assert.True(PlotRules.CaseIsOver(state, false, 2));
    }

    private static CampaignState Pair()
    {
        // events 0 (member 0 sends to member 1, id 7) and 1 (member 1 receives, id 7); 2 another with id 7; 3 the marker
        var state = new CampaignState { NEVENT = 4, NACTIVITY = 2, ChronTime = 3, NextSyn = 6 };
        state.Ac[0].Who = 10;
        state.Ac[1].Who = 11;
        state.Ev[0].Act = 0;
        state.Ev[0].Status = 0x0201;
        state.Ev[0].Synch = 7;
        state.Ev[1].Act = 1;
        state.Ev[1].Status = 0x0300;
        state.Ev[1].Synch = 7;
        state.Ev[2].Act = 0;
        state.Ev[2].Synch = 7;
        state.Ev[3].Act = -1;
        foreach (var e in state.Ev)
        {
            e.Chron = CastRules.ChronPending;
        }
        state.Ac2[0].Chron = -1;
        state.Ac2[1].Chron = 2;
        return state;
    }

    [Fact]
    public void AnEventWithAPartnerAnswersThePartnersEventOfTheSameId()
    {
        var state = Pair();
        var check = PlotRules.EventReady(state, 0);
        Assert.Equal(1, check.Result);           // event 1 is the partner's of the same id
        Assert.Empty(check.OutOfHiding);
        Assert.Equal(-1, PlotRules.EventReady(state, 2).Result);   // member 0's event 0 is still pending
    }

    [Fact]
    public void ActorAndPartnerInHidingComeOut()
    {
        var state = Pair();
        state.Ps[10].Alert = PlotRules.InHiding;
        state.Ps[11].Alert = PlotRules.InHiding;
        var check = PlotRules.EventReady(state, 0);
        Assert.Equal(new[] { 10, 11 }, check.OutOfHiding);
        Assert.Equal((0, 0), (state.Ps[10].Alert, state.Ps[11].Alert));
    }

    [Fact]
    public void APairHappensTogetherAndCancelsTheOtherEventsOfItsId()
    {
        var state = Pair();
        PlotRules.ScheduleEvent(state, 0, 1);
        Assert.Equal((3, 3, -1), (state.Ev[0].Chron, state.Ev[1].Chron, state.Ev[2].Chron));
        Assert.Equal((3, 2), (state.Ac2[0].Chron, state.Ac2[1].Chron));   // the first step is kept
    }

    [Fact]
    public void APairOfOnePersonIsCancelled()
    {
        var state = Pair();
        state.Ac[1].Who = 10;
        PlotRules.ScheduleEvent(state, 0, 1);
        Assert.Equal((-1, -1), (state.Ev[0].Chron, state.Ev[1].Chron));
    }

    [Fact]
    public void AnEventAnsweringOneThatHappenedSharesItsStep()
    {
        var state = Pair();
        state.Ev[1].Chron = 2;
        PlotRules.ScheduleEvent(state, 0, 1);
        Assert.Equal(2, state.Ev[0].Chron);
    }

    [Fact]
    public void AMessageOfAnEventNamesBothSides()
    {
        var state = Pair();
        state.Gtime = 600;
        state.Ps[10].Loc = 4;
        state.Ps[10].Alert = 33;
        state.Ps[11].Loc = 9;
        Assert.False(PlotRules.QueueMessage(state, PlotRules.MessageOfEvent, 0));
        var message = state.Sn[6];
        Assert.Equal((600, 0, 0, 33), (message.Time, message.Type, message.Data, message.Alert));
        Assert.Equal((10, 4, 11, 9), (message.Who, message.Loc, message.Who2, message.Loc2));
        Assert.Equal(7, state.NextSyn);
    }

    [Fact]
    public void TheLogIsFullAtItsLastMessage()
    {
        var state = new CampaignState { NextSyn = 127 };
        Assert.True(PlotRules.QueueMessage(state, PlotRules.MessageOutOfHiding, 5));
        Assert.Equal(-1, state.Sn[127].Who);
    }

    [Fact]
    public void ACastMemberAtLargeHidesOnceTheLogHasGrown()
    {
        var state = Pair();
        state.NextSyn = 9;
        state.InCity = 5;                          // the player is elsewhere
        state.Ev[0].Chron = 3;
        state.Ev[2].Chron = 3;                     // member 0 (person 10) has nothing pending
        state.Ev[1].Chron = 3;
        state.NACTIVITY = 1;                       // only member 0 can be picked
        Assert.Equal(10, PlotRules.TryHide(state, 1, false, out var noOne));
        Assert.False(noOne);
        Assert.Equal(PlotRules.InHiding, state.Ps[10].Alert);
        Assert.Equal(-1, PlotRules.TryHide(state, 1, true, out noOne));   // now no one is at large
        Assert.True(noOne);
    }

    [Fact]
    public void NoCastMemberIsEverSeenLeaving()
    {
        var state = new CampaignState { NACTIVITY = 1, InCity = 2, TextBeforePersons = "One moment please...\n" };
        state.Pl.BigOrg = -1;
        state.Ac[0].Status = 0x08;
        state.Ac[0].Who = 5;
        state.Ps[5].Clues = 0x08;
        state.Ps[5].Alert = 99;
        state.Ps[5].Org = 3;
        state.Ps[5].Face = 1;
        state.Orgs[3].Size = 50;
        state.Orgs[3].Covert = 1;                  // present everywhere
        foreach (var person in state.Ps)
        {
            person.Face = 1;                      // no free slot is ever needed: the lookup adds no one
        }
        state.Ps[40].Face = 0;                     // the one free slot the lookup's draw finds
        state.Random.Srand(3);
        Assert.Equal(-1, PlotRules.TryDepart(state, out var from));
        Assert.Equal(-1, from);
        Assert.Equal(0, state.Ps[5].Loc);
    }
}
