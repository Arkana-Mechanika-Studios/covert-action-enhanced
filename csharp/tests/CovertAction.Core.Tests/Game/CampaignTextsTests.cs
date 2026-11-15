using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// The campaign's pieces of text on hand-made states with made-up texts, with expected values worked out from the C
/// of append_person_name (GAME 00f7:08a6), append_full_name (08ee), format_game_time (95ab),
/// strip_spaces (3f69), replace_in_text_buffer (2f56), replace_substrings (7c85), append_clue_subject (55b3),
/// append_clue_label (5668) and activity_phrase_index (557c).
/// </summary>
public sealed class CampaignTextsTests
{
    private static CampaignState Texts()
    {
        var state = new CampaignState
        {
            StrAgentLetter = "Spy ?",
            StrNameSpace = "_",
            StrThisFace = "a face",
            StrCodeNameReal = "Real",
            StrCodeNameDecoy = "Decoy",
            ClueSuffixHundreds = "h",
            ClueSuffixThousands = "k",
            ClockDayText = " 99",
        };
        for (var i = 0; i < CampaignState.NamesPerRow; i++)
        {
            state.FirstNames[0][i] = $"F{i}";
            state.FirstNames[1][i] = $"M{i}";
            state.Surnames[i] = $"S{i}";
        }
        for (var month = 0; month < CampaignState.Months; month++)
        {
            state.MonthNames[month] = $"m{month:00}";
        }
        for (var kind = 0; kind < CampaignState.ClueLabelRows; kind++)
        {
            for (var variant = 0; variant < CampaignState.ClueLabelsPerRow; variant++)
            {
                state.ClueLabels[kind][variant] = $"L{kind}{variant}#";
            }
        }
        state.Txt.Text = "<";
        return state;
    }

    [Theory]
    [InlineData(0, "<Spy A")]
    [InlineData(27, "<Spy B")]
    [InlineData(63, "<Spy L")]
    public void APersonNotYetIdentifiedIsNamedByALetter(int person, string text)
    {
        var state = Texts();
        CampaignTexts.AppendPersonName(state, person);
        Assert.Equal(text, state.Txt.Text);
    }

    [Fact]
    public void AnIdentifiedPersonGetsTheFullName()
    {
        var state = Texts();
        state.Ps[5].Clues = 2;
        state.Ps[5].Org = 3;
        state.Orgs[3].Race = 4;                               // the names of rows 8.. of the tables
        state.Ps[5].Face = 1 | 3 << 1 | 6 << 5 | 0x7e00;      // a man, first name 3, surname 6, higher bits ignored
        CampaignTexts.AppendPersonName(state, 5);
        Assert.Equal("<M11_S14", state.Txt.Text);
    }

    [Theory]
    [InlineData(0, 0, "12:00 AM m00 01")]
    [InlineData(13 * 60 + 5, 2, "01:05 PM m02 01")]
    [InlineData(3 * 1440 + 11 * 60 + 59, 13, "11:59 AM m01 04")]
    [InlineData(65535, 0, "12:15 PM m00 46")]                  // the minutes are unsigned
    public void TheClockIsTwelveHoursMonthAndDay(int minutes, int month, string text)
    {
        var state = Texts();
        state.ClockText.Text = "xx:xx xM mmm dd";
        state.Pl.Ncase = (short)month;
        Assert.Equal(text, CampaignTexts.FormatGameTime(state, minutes));
        Assert.Equal(text, state.ClockText.Text);
        var clock = (ushort)minutes;
        Assert.Equal((clock % 60, clock / 60 % 24, clock / 1440 + 1), (state.Min, state.Hour, state.Day));
    }

    [Fact]
    public void SpacesAreRemoved()
    {
        var text = new TextBuffer(16) { Text = " New  York " };
        CampaignTexts.StripSpaces(text);
        Assert.Equal("NewYork", text.Text);
    }

    [Fact]
    public void EveryPlaceholderInTheTextIsReplaced()
    {
        var state = Texts();
        state.Txt.Text = "$CITY by night, $CITY by day.";
        CampaignTexts.ReplaceInTextBuffer(state, "$CITY", "Rome");
        Assert.Equal("Rome by night, Rome by day.", state.Txt.Text);
    }

    [Fact]
    public void OnlyTheFirstOccurrenceOfEachNameIsFilledIntoALine()
    {
        var state = Texts();
        var line = new TextBuffer(64) { Text = "$A met $B and $A." };
        LibraryTexts.ReplaceTokens(line, ["$A", "$B", "$C"], ["Ann", "Bob", "Cy"], 2, state.ReplaceTail);
        Assert.Equal("Ann met Bob and $A.", line.Text);
        Assert.Equal("$B and $A.", state.ReplaceTail.Text);   // the rest of the line at the last replacement
    }

    [Theory]
    [InlineData(0x0802, "<C2")]
    [InlineData(0x0403, "<O3")]
    [InlineData(0x0205, "<F10_S1")]
    [InlineData(0x0105, "<a face")]
    [InlineData(0x2005, "<")]
    public void TheSubjectOfAClueIsNamed(int subject, string text)
    {
        var state = Texts();
        state.Ct[2].Name = "C2";
        state.Orgs[3].Name = "O3";
        state.Ps[5].Face = 0x1234;
        state.Ps[5].Org = 3;
        CampaignTexts.AppendClueSubject(state, 0, subject);
        Assert.Equal(text, state.Txt.Text);
        Assert.Equal(subject == 0x0403 ? 1 : 0, state.Orgs[3].Clues);
        Assert.Equal(subject == 0x0105 ? 0x1234 : 0, state.Picface);
    }

    [Fact]
    public void ACastMembersCodeNameTellsADecoy()
    {
        var state = Texts();
        state.NACTIVITY = 9;
        state.Diff = 1;                 // members 5..8 are decoys
        state.Cl[0].Act = 4;
        state.Cl[1].Act = 5;
        CampaignTexts.AppendClueSubject(state, 0, 0x1000);
        CampaignTexts.AppendClueSubject(state, 1, 0x1000);
        Assert.Equal("<RealDecoy", state.Txt.Text);
    }

    [Theory]
    [InlineData(-1, "<")]
    [InlineData(0x0269, "<L26#42")]         // kind 2, variant 6, number 41 + 1
    [InlineData(0x0500, "<L50#1h")]
    [InlineData(0x063f, "<L63#64k")]
    public void AClueItemIsLabelAndNumber(int code, string text)
    {
        var state = Texts();
        CampaignTexts.AppendClueLabel(state, code);
        Assert.Equal(text, state.Txt.Text);
    }

    [Fact]
    public void AKindAboveTheTableIsOutsideTheModel()
    {
        Assert.Throws<IndexOutOfRangeException>(() => CampaignTexts.AppendClueLabel(Texts(), 0x0800));
    }

    private static CampaignState Messages()
    {
        var state = Texts();
        state.MessageText.ReceivedFrom = "Got";
        state.MessageText.SentTo = "Gave";
        state.MessageText.BeforeName = " ";
        state.MessageText.Someone = " anon";
        state.MessageText.OfThe = " of ";
        state.MessageText.In = " at ";
        state.MessageText.On = " on ";
        state.MessageText.BeforeDay = "/";
        state.MessageText.End = ".";
        state.NEVENT = 4;
        state.Ev[1].Name = "Sent";
        state.Ev[1].Status = 0x0202;          // a message to cast member 2
        state.Ev[2].Name = "Seen";
        state.Orgs[3].Sname = "O3";
        state.Ct[5].Name = "C5";
        state.Ct[15].Police = 7;
        return state;
    }

    /// <summary>A message about event 1 from person 10 (in city 4) to person 20 (in city 5), at minute 3000 (day 3).</summary>
    private static void Message(CampaignState state, int index, int clues, int from = 10, int to = 20, int toCity = 5, int time = 3000)
    {
        var message = state.Sn[index];
        message.Data = 1;
        message.Time = (short)time;
        message.Clues = (short)clues;
        message.Who = (sbyte)from;
        message.Who2 = (sbyte)to;
        message.Loc2 = (sbyte)toCity;
    }

    [Fact]
    public void AMessageNamesWhatThePlayerKnowsOfTheOtherParty()
    {
        var state = Messages();
        Message(state, 0, 0x0d);
        state.Ps[20].Org = 3;
        state.Pl.Ncase = 1;
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal("<Sent anon of O3 at C5 on m01/3.", state.Txt.Text);
    }

    [Fact]
    public void AnEventWithoutPartnerIsOnlyItsName()
    {
        var state = Messages();
        Message(state, 0, 0x0e);
        state.Sn[0].Data = 2;
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal("<Seen.", state.Txt.Text);
    }

    [Fact]
    public void WhatIsKnownPassesBetweenTheTwoMessagesOfAPair()
    {
        var state = Messages();
        Message(state, 0, 0x02);                                   // the sender's message: the receiver's name is known
        Message(state, 1, 0x00, from: 20, to: 10, toCity: 4);       // the receiver's message, same minute
        state.Ps[20].Clues = 0x02 | 0x04;                          // the player knows the receiver's name and organisation
        state.Ps[20].Loc = 5;
        state.Ps[20].Clues |= 0x08;                                // and city
        state.Ps[10].Clues = 0x06;                                 // and the sender's name and organisation
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal(0x0e, state.Sn[0].Clues);                     // name, organisation, and the city (the receiver's own)
        Assert.Equal(0x06, state.Sn[1].Clues);                     // the pair learns what the player knows of the sender
        Assert.Equal(0x0e, state.Ps[20].Clues);
    }

    [Fact]
    public void AMessageWithoutPairTakesCityFifteensCounterForOne()
    {
        var state = Messages();
        Message(state, 0, 0x02);                                   // alone in the log: its pair is "message -1"
        state.Ps[20].Clues = 0x02;
        state.Ps[10].Clues = 0x06;
        state.Ct[15].Police = 3000;                                 // the minute of message -1
        CampaignTexts.AppendMessageText(state, 0);
        Assert.Equal(0x06, state.Ct[15].Clued);                    // the sender's knowledge written into city 15's counter
        Assert.Equal(0x06, state.Ps[10].Clues);
    }

    [Fact]
    public void TheMenusListEveryCityAndTheKnownOrganisations()
    {
        var state = Texts();
        state.ListItemPrefix = "-";
        state.ListItemSuffix = "|";
        state.OrgListPrefix = "+";
        state.OrgListSuffix = "|";
        for (var i = 0; i < CampaignState.WorldSize; i++)
        {
            state.Ct[i].Name = $"{i:x}";
            state.Orgs[i].Name = $"o{i:x}";
        }
        state.Orgs[0].Clues = 1;                                   // the agency's own is never listed
        state.Orgs[4].Clues = 1;
        state.Orgs[15].Clues = 3;
        state.Txt.Text = "";
        CampaignTexts.ListCities(state);
        Assert.Equal("-0|-1|-2|-3|-4|-5|-6|-7|-8|-9|-a|-b|-c|-d|-e|-f|", state.Txt.Text);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (short)i), state.Mitem.Take(16));
        state.Txt.Text = "";
        state.Mindex = 0;
        CampaignTexts.ListKnownOrganizations(state);
        Assert.Equal("+o4|+of|", state.Txt.Text);
        Assert.Equal((2, 4, 15), (state.Mindex, state.Mitem[0], state.Mitem[1]));
    }

    [Theory]
    [InlineData(0x0100, 0x0000, 0)]
    [InlineData(0x0808, 0x0269, 13)]        // item kind 2: 10; city: 3
    [InlineData(0x1000, 0x0700, 39)]        // item kind 7: 35; cast member: 4
    [InlineData(0x0000, 0x0f00, 35)]        // bits 8..10 only; no kind: 0
    public void ThePhraseDependsOnTheItemAndTheSubject(int what, int when, int index)
    {
        Assert.Equal(index, CampaignLibrary.ClueKindIndex(what, when));
    }
}
