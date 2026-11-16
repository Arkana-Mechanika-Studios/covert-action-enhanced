using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The pieces of text the campaign's messages, reports and lists are put together from: names of persons, the
/// clock, what a clue is about and the label of a clue item, and the filling in of placeholders.
/// </summary>
/// <remarks>Most of these append to <see cref="CampaignState.Txt"/>, the text being built.</remarks>
public static class CampaignTexts
{
    /// <summary><see cref="Records.Person.Clues"/>: the person is positively identified (the player knows the name).</summary>
    private const short KnownName = 0x02;

    /// <summary>Letters of the alphabet: a person not yet identified is "Agent" and the letter of their number modulo this.</summary>
    private const int Letters = 26;

    /// <summary>Kinds of clue item (the high nibble of a clue key) followed by a word after their number: sums in hundreds and in thousands.</summary>
    private const int ItemMoneyHundreds = 5;

    private const int ItemMoneyThousands = 6;

    /// <summary>The value of a clue key that stands for no item.</summary>
    private const int NoItem = -1;

    /// <summary>
    /// Appends a person's name to the text: the real name when the player has identified the person, else "Agent"
    /// and a letter (the person's number modulo 26, A for 0).
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: GAME 00f7:08a6 append_person_name (src/c/game/game_00f7_08a6.c) matched; tests: CampaignTextsTests, GameOracleTests
    // Other code under the same name: FINAL 0100:851a, TAC 00e8:b3d4, BUG 00e8:4b50 (not this method; docs/source-map/shared-functions.csv).
    public static void AppendPersonName(CampaignState state, int person)
    {
        if ((state.Ps[person].Clues & KnownName) != 0)
        {
            AppendFullName(state, person);
            return;
        }
        state.Txt.Text += state.StrAgentLetter;
        // the last character of the text ("Agent A") is replaced by the person's letter
        state.Txt[state.Txt.Length - 1] = (char)(byte)(person % Letters + 'A');
    }

    /// <summary>
    /// Appends a person's full name to the text: first name, a space, surname. The names are picked by the person's
    /// face word (bit 0 the sex, bits 1..4 the first name, bits 5..8 the surname) from the rows of the name tables that
    /// belong to the ethnic group of the person's organisation.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: GAME 00f7:08ee append_full_name (src/c/game/game_00f7_08ee.c) matched; tests: CampaignTextsTests, GameOracleTests
    // Other code under the same name: FINAL 0100:8562, TAC 00e8:b41c (not this method; docs/source-map/shared-functions.csv).
    public static void AppendFullName(CampaignState state, int person)
    {
        var face = state.Ps[person].Face;
        var group = 2 * state.Orgs[state.Ps[person].Org].Race;
        state.Txt.Text += state.FirstNames[face & 1][group + (face >> 1 & 0xf)];
        state.Txt.Text += state.StrNameSpace;
        state.Txt.Text += state.Surnames[group + (face >> 5 & 0xf)];
    }

    /// <summary>
    /// Writes a moment of the campaign as "hh:mm AM Mon dd" into <see cref="CampaignState.ClockText"/> (a 12-hour
    /// clock, the month the case runs in, the day of the case) and sets <see cref="CampaignState.Min"/>,
    /// <see cref="CampaignState.Hour"/> and <see cref="CampaignState.Day"/>.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="minutes">Minutes since the case began, 0..65535.</param>
    /// <returns>The clock text.</returns>
    // orig: GAME 00f7:95ab format_game_time (src/c/game/game_00f7_95ab.c) matched; tests: CampaignTextsTests, GameOracleTests
    // Other code under the same name: BUG 00e8:50e5, CHASE 00df:2bd0, FINAL 0100:2c94 (not this method; docs/source-map/shared-functions.csv).
    public static string FormatGameTime(CampaignState state, int minutes)
    {
        const int hourDigits = 0, minuteDigits = 3, halfOfDay = 6, month = 9, dayDigits = 13;
        var text = state.ClockText;
        // C: the minutes are unsigned
        var time = (ushort)minutes;
        state.Min = (short)(time % 60);
        PutTwoDigits(text, minuteDigits, state.Min);
        time /= 60;
        state.Hour = (short)(time % 24);
        PutTwoDigits(text, hourDigits, (state.Hour + 11) % 12 + 1);
        text[halfOfDay] = state.Hour > 11 ? 'P' : 'A';
        PutText(text, month, state.MonthNames[state.Pl.Ncase % CampaignState.Months]);
        // the day's two placeholder digits follow the month, then the day is written over them
        text.Text += state.ClockDayText;
        state.Day = (short)(time / 24 + 1);
        PutTwoDigits(text, dayDigits, state.Day);
        return text.Text;
    }

    /// <summary>Writes the tens and the units of a number as two characters at a position of a text ('0' plus the digit, a byte as in the original).</summary>
    private static void PutTwoDigits(TextBuffer text, int position, int value)
    {
        text[position] = (char)(byte)(value / 10 + '0');
        text[position + 1] = (char)(byte)(value % 10 + '0');
    }

    /// <summary>Writes a text and its end mark at a position of a buffer, as <c>strcpy</c> to a place inside it does.</summary>
    private static void PutText(TextBuffer buffer, int position, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            buffer[position + i] = text[i];
        }
        buffer[position + text.Length] = '\0';
    }

    /// <summary>Removes every space from a text, in place.</summary>
    /// <param name="text">The text (the city screen passes <see cref="CampaignState.Txt"/>, a city's name to become a picture's file name).</param>
    // orig: GAME 00f7:3f69 strip_spaces (src/c/game/game_00f7_3f69.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void StripSpaces(TextBuffer text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            while (text[i] == ' ')
            {
                text.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Replaces every occurrence of a placeholder (such as "$CITY") in <see cref="CampaignState.Txt"/> by a value,
    /// from the left, until there is none left. A value that contains the placeholder never ends, as in the original.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="token">The placeholder.</param>
    /// <param name="value">What it is replaced by.</param>
    /// <exception cref="MemoryOverrunException">The rest of the text behind the placeholder is longer than the original's buffer for it (255 characters): the original writes past it.</exception>
    // orig: GAME 00f7:2f56 replace_in_text_buffer (src/c/game/game_00f7_2f56.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void ReplaceInTextBuffer(CampaignState state, string token, string value)
    {
        const int tailBuffer = 256;
        int found;
        while ((found = state.Txt.Text.IndexOf(token, StringComparison.Ordinal)) >= 0)
        {
            var tail = state.Txt.Text[found..];
            if (tail.Length >= tailBuffer)
            {
                throw new MemoryOverrunException($"a text of {tail.Length} characters behind \"{token}\" into a buffer of {tailBuffer} bytes");
            }
            state.Txt.Truncate(found);
            state.Txt.Text += value;
            state.Txt.Text += tail[token.Length..];
        }
    }

    /// <summary>
    /// Appends what a clue is about to the text, from the clue's subject word (kind in the high byte, index in the low
    /// byte): a city's name, an organisation's name (the organisation becomes known to the player), a person's full
    /// name, the words "this face." (the person's face is then shown beside the box: <see cref="CampaignState.Picface"/>),
    /// or the code name of the clue's cast member (one for a real participant, another for a decoy).
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="clue">The clue, 0..79 (its cast member is looked at for a code name).</param>
    /// <param name="subject">The subject word.</param>
    // orig: GAME 00f7:55b3 append_clue_subject (src/c/game/game_00f7_55b3.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void AppendClueSubject(CampaignState state, int clue, int subject)
    {
        var index = subject & 0xff;
        switch ((short)subject >> 8)
        {
            case ClueRules.SubjectCity:
                state.Txt.Text += state.Ct[index].Name;
                break;
            case ClueRules.SubjectOrg:
                state.Txt.Text += state.Orgs[index].Name;
                state.Orgs[index].Clues |= 1;
                break;
            case ClueRules.SubjectName:
                AppendFullName(state, index);
                break;
            case ClueRules.SubjectFace:
                state.Txt.Text += state.StrThisFace;
                // the original also returns the face; its only caller does not use it
                state.Picface = state.Ps[index].Face;
                break;
            case ClueRules.SubjectCastMember:
                var decoysFrom = state.NACTIVITY - state.Diff * 4;
                state.Txt.Text += state.Cl[clue].Act < decoysFrom ? state.StrCodeNameReal : state.StrCodeNameDecoy;
                break;
        }
    }

    /// <summary>
    /// Appends the label of a clue item to the text (a car, a weapon, an address, a ticket ...): the label picked by
    /// the key's kind (bits 8..11) and variant (bits 4..6), then the key's number (bits 0..5, from 1), then for sums of
    /// money the word after the number.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="code">The clue key; -1 appends nothing.</param>
    /// <exception cref="IndexOutOfRangeException">A kind above 7: the original reads a label from behind its table.</exception>
    // orig: GAME 00f7:5668 append_clue_label (src/c/game/game_00f7_5668.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void AppendClueLabel(CampaignState state, int code)
    {
        if (code == NoItem)
        {
            return;
        }
        var kind = code >> 8 & 15;
        state.Txt.Text += state.ClueLabels[kind][code >> 4 & 7];
        CStr.Itoa((short)((code & 0x3f) + 1), state.NumberText.Bytes, 10);
        state.Txt.Text += state.NumberText.Text;
        if (kind == ItemMoneyHundreds)
        {
            state.Txt.Text += state.ClueSuffixHundreds;
        }
        if (kind == ItemMoneyThousands)
        {
            state.Txt.Text += state.ClueSuffixThousands;
        }
    }

    /// <summary><see cref="Records.Synopsis.Clues"/>: what the player knows of an intercepted message.</summary>
    private const short MessageDateKnown = 0x01;

    private const short MessageNameKnown = 0x02;

    private const short MessageOrgKnown = 0x04;

    private const short MessageCityKnown = 0x08;

    /// <summary><see cref="Records.Event.Status"/>: the bits that name the event's partner and kind; 0 for an event without a partner.</summary>
    private const int EventPartnerBits = 0xfff;

    /// <summary>Minutes in a day of the campaign's clock.</summary>
    private const int MinutesPerDay = 1440;

    /// <summary>
    /// Appends a line about an intercepted message to the text: what happened ("Sent msg to" ...), the other party as
    /// far as the player knows them (name or "someone", organisation, city), and the date if the player knows it.
    /// </summary>
    /// <remarks>
    /// A message of an event with a partner is one of a pair recorded at the same minute (the sender's and the
    /// receiver's). What the player knows of one side passes to the other: a party identified by name, or by
    /// organisation and city, gives the message the name and organisation bits and the person learns them from the
    /// message (and the city when it is the person's own), for this message and for its pair. The pair is the next
    /// message with the same minute, or the one before, which wins.
    /// </remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="msg">The message, 0..127.</param>
    // orig: GAME 00f7:5f21 append_message_text (src/c/game/game_00f7_5f21.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void AppendMessageText(CampaignState state, int msg)
    {
        var words = state.MessageText;
        var message = state.Sn[msg];
        int eventIndex = message.Data;
        state.Txt.Text += eventIndex >= 0 ? state.Ev[eventIndex].Name : (eventIndex & 1) != 0 ? words.ReceivedFrom : words.SentTo;
        // C: the original tests the event's partner bits before the sign of the index; for a negative index the bits it reads do not change the outcome
        if (eventIndex < 0 || (state.Ev[eventIndex].Status & EventPartnerBits) != 0)
        {
            var sender = state.Ps[message.Who];
            int other = message.Who2;
            var receiver = state.Ps[other];
            // BUG: CA-32 a message without a pair is paired with message -1 (the end of city 15's record), and of two pairs sent in the same minute the later's first message pairs with the earlier's second; OriginalBehaviour.FixLoneMessagePairing
            var pair = state.Behaviour.FixLoneMessagePairing ? PairOf(state, msg) : -1;
            if (!state.Behaviour.FixLoneMessagePairing)
            {
                if (MessageTime(state, msg + 1) == message.Time)
                {
                    pair = msg + 1;
                }
                if (MessageTime(state, msg - 1) == message.Time)
                {
                    pair = msg - 1;
                }
            }
            if ((message.Clues & MessageNameKnown) != 0)
            {
                state.Txt.Text += words.BeforeName;
                AppendPersonName(state, other);
            }
            else
            {
                state.Txt.Text += words.Someone;
            }
            if ((message.Clues & MessageOrgKnown) != 0)
            {
                state.Txt.Text += words.OfThe;
                state.Txt.Text += state.Orgs[receiver.Org].Sname;
            }
            if ((message.Clues & MessageCityKnown) != 0)
            {
                state.Txt.Text += words.In;
                state.Txt.Text += state.Ct[message.Loc2].Name;
            }
            if (Identified(message.Clues, receiver.Clues))
            {
                message.Clues |= (short)(receiver.Clues & (MessageNameKnown | MessageOrgKnown));
                AddMessageClues(state, pair, sender.Clues & (MessageNameKnown | MessageOrgKnown));
                receiver.Clues |= (short)(message.Clues & (MessageNameKnown | MessageOrgKnown));
                if (message.Loc2 == receiver.Loc)
                {
                    message.Clues |= (short)(receiver.Clues & MessageCityKnown);
                    receiver.Clues |= (short)(message.Clues & MessageCityKnown);
                }
            }
            if (Identified(MessageClues(state, pair), sender.Clues))
            {
                AddMessageClues(state, pair, sender.Clues & (MessageNameKnown | MessageOrgKnown));
                message.Clues |= (short)(receiver.Clues & (MessageNameKnown | MessageOrgKnown));
                sender.Clues |= (short)(MessageClues(state, pair) & (MessageNameKnown | MessageOrgKnown));
                if (MessageCity2(state, pair) == sender.Loc)
                {
                    AddMessageClues(state, pair, sender.Clues & MessageCityKnown);
                    sender.Clues |= (short)(MessageClues(state, pair) & MessageCityKnown);
                }
            }
        }
        if ((message.Clues & MessageDateKnown) != 0)
        {
            state.Txt.Text += words.On;
            state.Txt.Text += state.MonthNames[state.Pl.Ncase % CampaignState.Months];
            state.Txt.Text += words.BeforeDay;
            CStr.Itoa((short)(message.Time / MinutesPerDay + 1), state.NumberText.Bytes, 10);
            state.Txt.Text += state.NumberText.Text;
        }
        state.Txt.Text += words.End;
    }

    /// <summary>Whether a message and a person together identify the person: both know the name, or the message knows organisation and city and the person is known by both.</summary>
    private static bool Identified(short messageClues, short personClues) =>
        ((messageClues & MessageNameKnown) != 0 && (personClues & MessageNameKnown) != 0)
        || ((messageClues & MessageOrgKnown) != 0 && (messageClues & MessageCityKnown) != 0
            && (personClues & MessageOrgKnown) != 0 && (personClues & MessageCityKnown) != 0);

    // The message -1 is what the original finds in front of the message log when a message has no pair, or is the
    // first: the end of city 15's record and the count of persons. Its minute is city 15's police figure, its
    // knowledge bits are city 15's sighting counter, its second city is the high byte of the count of persons.

    /// <summary>The "pair" of a message that has none (see <see cref="PairOf"/>).</summary>
    private const int NoPair = -2;

    /// <summary>
    /// The message that is the other half of a message's pair: the neighbour in the log, of the same minute, that names the same two
    /// people the other way round (the sender's message names the receiver as the other party and the receiver's the sender);
    /// the one before wins as in the original. <see cref="NoPair"/> when there is none.
    /// </summary>
    private static int PairOf(CampaignState state, int msg)
    {
        var message = state.Sn[msg];
        var pair = NoPair;
        foreach (var neighbour in new[] { msg + 1, msg - 1 })
        {
            if (neighbour < 0 || neighbour >= state.NextSyn)
            {
                continue;
            }
            var other = state.Sn[neighbour];
            if (other.Time == message.Time && other.Type == message.Type && other.Who == message.Who2 && other.Who2 == message.Who)
            {
                pair = neighbour;
            }
        }
        return pair;
    }

    /// <summary>The minute of a message; for -1 (in front of the log) city 15's police figure, as the original reads it.</summary>
    private static short MessageTime(CampaignState state, int msg) =>
        // C: sn[-1].time is the word at GAME DS:7cb6, ct[15].police
        msg == -1 ? state.Ct[CampaignState.WorldSize - 1].Police : state.Sn[msg].Time;

    /// <summary>The knowledge bits of a message; for -1 city 15's sighting counter, as the original reads and writes it.</summary>
    private static short MessageClues(CampaignState state, int msg) =>
        // C: sn[-1].clues is the word at GAME DS:7cba, ct[15].clued
        msg == NoPair ? (short)0 : msg == -1 ? state.Ct[CampaignState.WorldSize - 1].Clued : state.Sn[msg].Clues;

    /// <summary>Adds knowledge bits to a message; for -1 to city 15's sighting counter, as the original writes it.</summary>
    private static void AddMessageClues(CampaignState state, int msg, int bits)
    {
        if (msg == NoPair)
        {
            return;
        }
        if (msg == -1)
        {
            state.Ct[CampaignState.WorldSize - 1].Clued |= (short)bits;
        }
        else
        {
            state.Sn[msg].Clues |= (short)bits;
        }
    }

    /// <summary>The second city of a message; for -1 the high byte of the count of persons, as the original reads it.</summary>
    private static int MessageCity2(CampaignState state, int msg) =>
        // C: sn[-1].loc2 is the byte at GAME DS:7cc1, the high byte of Nperson
        msg == -1 ? (sbyte)(state.Nperson >> 8) : state.Sn[msg].Loc2;

    /// <summary>
    /// Appends one menu row per city (all sixteen, " name" and the row's end) to the text, and notes each row's city
    /// in <see cref="CampaignState.Mitem"/>.
    /// </summary>
    /// <param name="state">The campaign.</param>
    // orig: GAME 00f7:6ff1 list_cities (src/c/game/game_00f7_6ff1.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void ListCities(CampaignState state)
    {
        for (var city = 0; city < CampaignState.WorldSize; city++)
        {
            state.Txt.Text += state.ListItemPrefix;
            state.Txt.Text += state.Ct[city].Name;
            state.Txt.Text += state.ListItemSuffix;
            state.Mitem[state.Mindex++] = (short)city;
        }
    }

    /// <summary>
    /// Appends one menu row per organisation the player knows of (bit 0 of <see cref="Records.Org.Clues"/>; the
    /// agency's own, 0, never) to the text, and notes each row's organisation in <see cref="CampaignState.Mitem"/>.
    /// </summary>
    /// <param name="state">The campaign.</param>
    // orig: GAME 00f7:6c94 list_known_organizations (src/c/game/game_00f7_6c94.c) matched; tests: CampaignTextsTests, GameOracleTests
    public static void ListKnownOrganizations(CampaignState state)
    {
        for (var org = 1; org < CampaignState.WorldSize; org++)
        {
            if ((state.Orgs[org].Clues & 1) == 0)
            {
                continue;
            }
            state.Txt.Text += state.OrgListPrefix;
            state.Txt.Text += state.Orgs[org].Name;
            state.Txt.Text += state.OrgListSuffix;
            state.Mitem[state.Mindex++] = (short)org;
        }
    }

}
