using System.Globalization;
using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Final;

/// <summary>The reports of the debriefing: the messages of the plot's events and the efficiency report.</summary>
public sealed partial class Headquarters
{
    /// <summary>The placeholders <see cref="ShowEventMessage"/> fills: the case's two objects, the actor's organisation and city.</summary>
    private const int VictimValue = 0, SenderOrgValue = 1, SenderCityValue = 3, ObjectValue = 6;

    /// <summary>A person's <c>Alert</c> word once the agent has arrested him, or turned him into a Double Agent.</summary>
    private const short Arrested = -1, Turned = -3;

    /// <summary>The colours of the report's letters: black, the card's title red, the points blue and dark red.</summary>
    private const int CardBlack = 0, CardRed = 0xc, CardBlue = 1, CardDarkRed = 4;

    /// <summary>The objects of the plot the report has a card for.</summary>
    private const int PlotObjects = 4;

    /// <summary>
    /// Puts the message of plot event <paramref name="a"/> into the text buffer: fills in the placeholders (the case's
    /// two objects, and the organisation and city of the person who acts in the event), then reads the message whose
    /// tag is made of the crime file's number and the event's message number.
    /// </summary>
    /// <param name="a">The event; a negative number reads message <paramref name="a"/> itself.</param>
    // orig: FINAL 0100:7992 show_event_message (src/c/final/final_0100_7992.c) matched; tests: FinalOracleTests
    public void ShowEventMessage(int a)
    {
        var values = _state.VariableValueTexts;
        values[VictimValue] = _state.Pl.Object.Text;
        values[ObjectValue] = _state.Pl.Object2.Text;
        // C: the event is looked at even when a is negative, which reads in front of the table
        var actor = _state.Ps[_state.Ac[_state.Ev[a].Act].Who];
        values[SenderOrgValue] = _state.Org[actor.Org].Name;
        values[SenderCityValue] = _state.Ct[actor.Loc].Name;
        LibraryTexts.LoadMessageText(_state.MessageTag, _state.MessageTemplate, _state.TheCrime, a >= 0 ? _state.Ev[a].Synch : a);
        Text.ReadTextWithVars(_state.TextFileName, _state.MessageTag.Text);
    }

    /// <summary>
    /// The efficiency report at the end of a case: a card for every member of the cast in the roster (arrested, turned
    /// or at large, and the points earned of those possible: the MasterMind counts four times), for every object of the
    /// plot that changed hands (confiscated or not found), for every event of the plot worth points that happened (the
    /// crime was carried out when one was worth 5 or more), "Prevented crime" otherwise, the Double Agents left in the
    /// CIA, and last the efficiency rating. The cards are stacked down the screen, each a little to the side of the one
    /// before; each waits for a key. The points are added to the case's (<see cref="FinalState.EpEarned"/>,
    /// <see cref="FinalState.EpPossible"/>), and how the case ended to <see cref="FinalState.CaseFlags"/>.
    /// </summary>
    /// <returns>The rating: 25 times the points earned for every 40 possible, at least 0.</returns>
    /// <exception cref="DivideErrorException">Fewer than 40 points possible, where the original stops with a divide error.</exception>
    // orig: FINAL 0100:2e06 show_efficiency_report (src/c/final/final_0100_2e06.c) matched; tests: FinalOracleTests
    public int ShowEfficiencyReport()
    {
        const int titleField = 0xe;
        var texts = _state.ReportTexts;
        Screens.DrawScreenFrame(texts[(int)ReportText.Title], titleField);
        var card = new ReportCard(8, 0xb);
        _world.Graphics.SetColorMaps(_state.ReportColorMap, _state.ReportColorMap);
        for (var member = 0; member < _state.NRoster; member++)
        {
            ShowMemberCard(card, member);
        }
        for (var item = 0; item < PlotObjects; item++)
        {
            // C: a shift by a variable count, 0..3
            if ((_state.ObjectsSeenMask & 1 << item) != 0)
            {
                ShowObjectCard(card, item);
            }
        }
        for (var entry = 0; entry < _state.NextSyn; entry++)
        {
            const int plotEvent = 0;
            var record = _state.Sn[entry];
            if (record.Type == plotEvent && _state.Ev[record.Data].VP != 0)
            {
                ShowEventCard(card, record.Data);
            }
        }
        if ((_state.CaseFlags & FinalState.CaseFlagCrimeHappened) == 0)
        {
            const short prevented = 100;
            Screens.DrawPanel(card.X, card.Y);
            Text.DrawTextCentered(texts[(int)ReportText.PreventedCrime], card.X + 0x97, card.Y + 2, CardBlack);
            Output.DrawTextFlag0(texts[(int)ReportText.PreventedPoints], card.X + 0xf0, card.Y + 2, CardBlue);
            _state.EpEarned += prevented;
            _state.EpPossible += prevented;
            NextCard(card);
        }
        var doubles = 0;
        for (var org = 0; org < CampaignTables.WorldSize; org++)
        {
            if ((_state.Doubles & 1 << org) != 0)
            {
                doubles++;
            }
        }
        if (doubles != 0)
        {
            ShowDoubleAgentsCard(card, doubles);
        }
        return ShowRatingCard(card);
    }

    /// <summary>Where the next card of the efficiency report goes: its left edge drifts at random, its top edge moves 10 pixels down.</summary>
    private sealed class ReportCard(int x, int y)
    {
        public int X { get; set; } = x;

        public int Y { get; set; } = y;
    }

    /// <summary>Moves on to the next card of the report and waits for the player's key.</summary>
    private void NextCard(ReportCard card)
    {
        card.X = CaseSetup.Random(_state, 0xe) + 2;
        card.Y += 0xa;
        Screens.WaitForKey();
    }

    /// <summary>A number as the report writes it out, through its digits buffer (the C library's itoa in base 10).</summary>
    private string ReportNumber(int value)
    {
        _state.DigitsReport.Text = value.ToString(CultureInfo.InvariantCulture);
        return _state.DigitsReport.Text;
    }

    /// <summary>
    /// The card of a member of the cast: the role; the photo, name, rank, organisation and city unless the member is
    /// the MasterMind still at large; arrested, turned or at large; the points. An arrest is worth 5 steps, a turned
    /// agent 10, else a step for knowing the member's main facts and one for one more; a step is worth 3 points and
    /// one more per rank, of 15 and five per rank possible.
    /// </summary>
    private void ShowMemberCard(ReportCard card, int memberNo)
    {
        var texts = _state.ReportTexts;
        var member = _state.Ac[memberNo];
        var txt = _state.Txt;
        // C: draw_report_card(f, g) first: each member's card is drawn on its panel too (show_efficiency_report, FINAL
        // 0100:2e06); test: DebriefTests.TheFirstCardOfTheEfficiencyReportIsDrawnOnItsPanel
        Screens.DrawPanel(card.X, card.Y);
        txt.Text = member.Name;
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 2, CardBlack);
        var personNo = member.Who;
        var person = _state.Ps[personNo];
        if (!((CastPlacement)member.Status).HasFlag(CastPlacement.Mastermind) || person.Alert == Arrested || person.Alert == Turned)
        {
            Screens.DrawPortrait(person.Face, card.X + 0x66, card.Y + 0x10);
            txt.Text = "";
            CampaignLibrary.AppendPersonName(txt, _state.Ps, _state.Org, _state.Names, personNo);
            txt.Text += texts[(int)ReportText.Comma] + _state.RankNames[member.Rank];
            Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 0x48, CardBlack);
            txt.Text = _state.Org[person.Org].Name + texts[(int)ReportText.Slash] + _state.Ct[person.Loc].Name;
            Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 0x50, CardBlack);
        }
        _world.Graphics.SetColorMaps(_state.ReportColorMap, _state.ReportColorMap);
        txt.Text = texts[(int)ReportText.AtLarge];
        var steps = 0;
        if (person.Alert == Arrested)
        {
            steps = 5;
            txt.Text = texts[(int)ReportText.Arrested];
            _state.CaseFlags++;
        }
        else if (person.Alert == Turned)
        {
            steps = 0xa;
            txt.Text = texts[(int)ReportText.Turned];
            _state.CaseFlags++;
        }
        else
        {
            // the low byte of what the agent learnt of the person: bits 1-3 and bit 0 are worth a step each
            const int mainFacts = 0xe, oneMore = 1;
            if ((person.Intel & mainFacts) == mainFacts)
            {
                steps++;
            }
            if ((person.Intel & oneMore) != 0)
            {
                steps++;
            }
        }
        const int mastermind = 0;
        if (personNo == mastermind && steps >= 5)
        {
            _state.CaseFlags |= FinalState.CaseFlagMastermindCaught;
        }
        Output.DrawTextFlag0(txt.Text, card.X + 0x18, card.Y + 2, CardRed);
        // C: 16-bit products
        var earned = C16.Int((member.Rank + 3) * steps);
        var possible = C16.Int(5 * member.Rank + 0xf);
        if (personNo == mastermind)
        {
            earned = C16.Int(earned << 2);
            possible = C16.Int(possible << 2);
        }
        txt.Text = texts[(int)ReportText.MemberPoints] + ReportNumber(earned);
        txt.Text += texts[(int)ReportText.MemberPossible] + ReportNumber(possible);
        Output.DrawTextFlag0(txt.Text, card.X + 0xf4, card.Y + 2, CardBlue);
        _state.EpEarned = (short)(_state.EpEarned + earned);
        _state.EpPossible = (short)(_state.EpPossible + possible);
        NextCard(card);
    }

    /// <summary>The card of an object of the plot that changed hands: 50 points possible, earned when the agent confiscated it.</summary>
    private void ShowObjectCard(ReportCard card, int item)
    {
        const short objectPoints = 0x32;
        var texts = _state.ReportTexts;
        var txt = _state.Txt;
        Screens.DrawPanel(card.X, card.Y);
        _state.EpPossible += objectPoints;
        txt.Text = _state.Mc[item].Name;
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 2, CardBlack);
        txt.Text = texts[(int)ReportText.ObjectNotEarned];
        if ((_state.ObjectsTakenMask & 1 << item) != 0)
        {
            Output.DrawTextFlag0(texts[(int)ReportText.Confiscated], card.X + 8, card.Y + 2, CardRed);
            txt.Text = texts[(int)ReportText.ObjectEarned];
            _state.EpEarned += objectPoints;
        }
        else
        {
            Output.DrawTextFlag0(texts[(int)ReportText.NotFound], card.X + 0x18, card.Y + 2, CardRed);
        }
        txt.Text += texts[(int)ReportText.ObjectPossible];
        Output.DrawTextFlag0(txt.Text, card.X + 0xf0, card.Y + 2, CardBlue);
        NextCard(card);
    }

    /// <summary>
    /// The card of an event of the plot that happened: its name (and the member who acted, unless the event says not
    /// to), none of its 40 points per point of the plot earned, and its message. An event of 5 or more marks the crime as
    /// carried out.
    /// </summary>
    private void ShowEventCard(ReportCard card, int eventNo)
    {
        const int crimeCarriedOut = 5, pointsPerPlotPoint = 0x28, noActorInName = 0x2000;
        var texts = _state.ReportTexts;
        var txt = _state.Txt;
        var happening = _state.Ev[eventNo];
        Screens.DrawPanel(card.X, card.Y);
        if (happening.VP >= crimeCarriedOut)
        {
            _state.CaseFlags |= FinalState.CaseFlagCrimeHappened;
        }
        var possible = C16.Int(pointsPerPlotPoint * happening.VP);
        txt.Text = happening.Name;
        if ((happening.Status & noActorInName) == 0)
        {
            txt.Text += texts[(int)ReportText.Blank] + _state.Ac[happening.Act].Name;
        }
        Text.DrawTextCentered(txt.Text, card.X + 0x97, card.Y + 2, CardBlack);
        txt.Text = texts[(int)ReportText.EventPoints] + ReportNumber(possible);
        Output.DrawTextFlag0(txt.Text, card.X + 0xf0, card.Y + 2, CardDarkRed);
        _state.EpPossible = (short)(_state.EpPossible + possible);
        txt.Text = "";
        ShowEventMessage(eventNo);
        Text.DrawWrappedText(0x20, card.X + 8, card.Y + 0x10, CardRed);
        NextCard(card);
    }

    /// <summary>The card of the Double Agents left in the CIA: 50 points possible each, none earned.</summary>
    private void ShowDoubleAgentsCard(ReportCard card, int doubles)
    {
        const int pointsEach = 0x32;
        var texts = _state.ReportTexts;
        var txt = _state.Txt;
        Screens.DrawPanel(card.X, card.Y);
        txt.Text = texts[(int)ReportText.Empty] + ReportNumber(doubles) + texts[(int)ReportText.DoubleAgents];
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 2, CardBlack);
        txt.Text = texts[(int)ReportText.RemainedUndetected];
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 0xa, CardBlack);
        txt.Text = texts[(int)ReportText.WithinTheCia];
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 0x12, CardBlack);
        txt.Text = texts[(int)ReportText.DoubleAgentPoints] + ReportNumber(pointsEach * doubles);
        Output.DrawTextFlag0(txt.Text, card.X + 0xf0, card.Y + 2, CardDarkRed);
        _state.EpPossible = (short)(_state.EpPossible + pointsEach * doubles);
        NextCard(card);
    }

    /// <summary>The last card: the efficiency rating and the case's points.</summary>
    private int ShowRatingCard(ReportCard card)
    {
        var texts = _state.ReportTexts;
        var txt = _state.Txt;
        Screens.DrawPanel(card.X, card.Y);
        // C: a 16-bit product and two signed divisions; fewer than 40 points possible divide by 0
        var rating = C16.Div(C16.Int(0x19 * _state.EpEarned), C16.Div(_state.EpPossible, 0x28));
        if (rating < 0)
        {
            rating = 0;
        }
        Output.DrawTextFlag0(texts[(int)ReportText.Rating], card.X + 0x64, card.Y + 2, CardBlack);
        txt.Text = texts[(int)ReportText.RatingOpen] + ReportNumber(rating) + texts[(int)ReportText.RatingClose];
        Output.DrawTextFlag0(txt.Text, card.X + 0x64, card.Y + 0x18, CardBlack);
        txt.Text = texts[(int)ReportText.TotalPoints] + ReportNumber(_state.EpEarned);
        txt.Text += texts[(int)ReportText.TotalPossible] + ReportNumber(_state.EpPossible);
        Output.DrawTextFlag0(txt.Text, card.X + 0xf0, card.Y + 2, CardBlue);
        Screens.WaitForKey();
        return rating;
    }
}
