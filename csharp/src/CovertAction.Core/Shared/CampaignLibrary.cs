using CovertAction.Core.Records;
using CovertAction.Msc;

namespace CovertAction.Core.Shared;

/// <summary>
/// The library's routines on the campaign's records that several parts share: naming a person, the kind of a
/// building, finding or making the person behind an organisation and a city, the phrase of a clue sentence, and the
/// small clean-ups of the person, clue and building tables. They work on the records themselves (<see cref="Person"/>,
/// <see cref="Clue"/> ...), which every part keeps under the same layout. The comment on a method lists the copies
/// it stands for (docs/source-map/shared-functions.csv, under the common name of twin-names.csv).
/// </summary>
public static class CampaignLibrary
{
    /// <summary><see cref="Person.Clues"/>: the player knows the person's real name.</summary>
    private const int NameKnown = 0x02;

    /// <summary><see cref="Person.Role"/>: the bits of the cast members mapped onto the person (bit 15, the decoy mark, does not count).</summary>
    private const int CastMembers = 0x7fff;

    /// <summary>Letters of the alphabet: an unidentified person is "Agent" and the letter of their number modulo this.</summary>
    private const int Letters = 26;

    /// <summary>The persons that can be made for a new organisation and city: 1..63 (0 is the MasterMind's record).</summary>
    private const int OtherPersons = 0x3f;

    /// <summary>The range of a new face word: 0..32766.</summary>
    private const int Faces = 0x7fff;

    /// <summary>The low six bits of a face word, the part the original compares (see <see cref="FindOrCreatePerson"/>).</summary>
    private const int FaceLowBits = 0x3f;

    /// <summary>The kinds of building a <c>building_level</c> can name, 1..5 (hideout, agent, safehouse, active cell, office).</summary>
    private const int BuildingKinds = 5;

    /// <summary>The clue table's last record: the table holds 80.</summary>
    private const int LastClue = 0x4f;

    /// <summary>
    /// Appends a person's name to the text: the real name when the player has identified the person, else the code
    /// name "Agent" and a letter (the person's number modulo 26, A for 0).
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="persons">The persons.</param>
    /// <param name="orgs">The organisations (a person's organisation picks the ethnic group of the names).</param>
    /// <param name="names">The part's name tables.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: BUG 00e8:4b50 append_person_name (src/c/bug/bug_00e8_4b50.c) matched; also FINAL 0100:851a, TAC 00e8:b3d4; tests: LibraryTextOracleTests
    // Other code under the same name: GAME 00f7:08a6 (CampaignTexts.AppendPersonName; docs/source-map/shared-functions.csv).
    public static void AppendPersonName(TextBuffer text, IReadOnlyList<Person> persons, IReadOnlyList<Org> orgs, PersonNameTexts names, int person)
    {
        if ((persons[person].Clues & NameKnown) != 0)
        {
            AppendRealName(text, persons, orgs, names, person);
            return;
        }
        text.Text += names.AgentPlaceholder;
        // the last character of the code name is replaced by the person's letter
        text[text.Length - 1] = (char)(person % Letters + 'A');
    }

    /// <summary>
    /// Appends a person's full name to the text: first name, a blank, surname (FINAL and TAC call it append_full_name). The names are picked by the person's
    /// face word (bit 0 the sex, bits 1..4 the first name, bits 5..8 the surname) from the rows of the name tables
    /// that belong to the ethnic group of the person's organisation.
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="persons">The persons.</param>
    /// <param name="orgs">The organisations.</param>
    /// <param name="names">The part's name tables.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: BUG 00e8:4b98 append_real_name (src/c/bug/bug_00e8_4b98.c) matched; also FINAL 0100:8562, TAC 00e8:b41c; tests: LibraryTextOracleTests
    // Other code under the same name: GAME 00f7:08ee (CampaignTexts.AppendFullName; docs/source-map/shared-functions.csv).
    public static void AppendRealName(TextBuffer text, IReadOnlyList<Person> persons, IReadOnlyList<Org> orgs, PersonNameTexts names, int person)
    {
        int face = persons[person].Face;
        var group = 2 * orgs[persons[person].Org].Race;
        text.Text += names.FirstNames[face & 1][group + (face >> 1 & 0xf)];
        text.Text += names.Space;
        text.Text += names.Surnames[group + (face >> 5 & 0xf)];
    }

    /// <summary>
    /// Appends the kind of an organisation's building in a city to the text (" hideout", " agent", " safehouse",
    /// " active cel", " office" for the levels 1..5; nothing for 0) and returns the level.
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="kinds">The part's five texts of the kinds, for the levels 1..5.</param>
    /// <param name="buildingLevel">The part's <c>org_presence</c> (city, organisation): the level of the building there, 0 for none.</param>
    /// <param name="city">The city.</param>
    /// <param name="org">The organisation.</param>
    /// <returns>The level, 0..5.</returns>
    // orig: BUG 00e8:4a1e append_building_type (src/c/bug/bug_00e8_4a1e.c) matched; also FINAL 0100:83e8, GAME 00f7:0774, TAC 00e8:b2a2; tests: LibraryTextOracleTests
    public static int AppendBuildingType(TextBuffer text, IReadOnlyList<string> kinds, Func<int, int, int> buildingLevel, int city, int org)
    {
        var level = buildingLevel(city, org);
        if (level is >= 1 and <= BuildingKinds)
        {
            text.Text += kinds[level - 1];
        }
        return level;
    }

    /// <summary>
    /// Finds the person of an organisation in a city and adds knowledge bits to what the player knows of them; makes
    /// the person when there is none. The person's index is returned, or -1 when the player would still know nothing.
    /// </summary>
    /// <remarks>
    /// A new person takes a random free record (face word 0) among 1..63, or record 0 when the organisation and city
    /// are the MasterMind's (who then also gets the plot's knowledge of him). The draw for the free record happens
    /// even when nothing is made. The new face word is drawn again while it collides with another person's; the
    /// original's test of a collision is <c>(other.face == 0x3f) &amp; new.face &amp; 0x3f</c>, which holds only for
    /// a person whose whole face word is 63 and an odd new face, so in practice the first draw stays.
    /// </remarks>
    /// <param name="persons">The persons.</param>
    /// <param name="personCount">How many records are in use (<c>Nperson</c>).</param>
    /// <param name="plot">The case (the MasterMind's organisation, city and what is known of him).</param>
    /// <param name="random">The part's random numbers.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="bits">The knowledge bits to add (<see cref="Person.Clues"/>).</param>
    /// <param name="show">Whether to tell the player what is new.</param>
    /// <param name="revealPersonFacts">The part's screen that tells it (the person, the bits known before).</param>
    /// <returns>The person, or -1.</returns>
    // orig: BUG 00e8:2175 find_or_create_person (src/c/bug/bug_00e8_2175.c) matched; also TAC 00e8:89f9, FINAL 0100:5b3f; tests: LibraryTextOracleTests
    // Other code under the same name: GAME 00f7:4899; and FINAL 0100:5ff1, which is the library's quiet variant (docs/source-map/twin-names.csv).
    public static int FindOrCreatePerson(IReadOnlyList<Person> persons, int personCount, Plot plot, IRandomSource random,
        int org, int city, int bits, bool show, Action<int, int> revealPersonFacts)
    {
        int person, before;
        for (person = 0; person < personCount; person++)
        {
            var known = persons[person];
            if (known.Org != org || known.Loc != city)
            {
                continue;
            }
            before = known.Clues;
            known.Clues |= (short)bits;
            if (known.Clues == 0)
            {
                return -1;
            }
            return Reveal(person, before);
        }
        do
        {
            person = GameLibrary.RandomBelow(random, OtherPersons) + 1;
        }
        while (persons[person].Face != 0);
        if (org == plot.BigOrg && city == plot.BigLoc)
        {
            person = 0;
            bits = C16.Int(bits) | plot.Clues;
        }
        if (C16.Int(bits) == 0)
        {
            return -1;
        }
        var made = persons[person];
        before = made.Clues;
        made.Org = (short)org;
        made.Loc = (short)city;
        made.Clues = (short)bits;
        made.Role = 0;
        made.Rknown = 0;
        bool unique;
        do
        {
            unique = true;
            made.Face = (short)GameLibrary.RandomBelow(random, Faces);
            for (var other = 0; other < personCount; other++)
            {
                if (((persons[other].Face == FaceLowBits ? 1 : 0) & made.Face & FaceLowBits) != 0 && person != other)
                {
                    unique = false;
                }
            }
        }
        while (!unique);
        return Reveal(person, before);

        int Reveal(int found, int knownBefore)
        {
            if (show)
            {
                revealPersonFacts(found, knownBefore);
            }
            return found;
        }
    }

    /// <summary>
    /// Which phrase of a clue sentence joins its subject and its item (" tied to ", " registered to " ...): five
    /// phrases per kind of item (bits 8..10 of <paramref name="item"/>), and among them the one for the subject's kind
    /// (the position of the highest set bit of the subject's kind: face 0, name 1, organisation 2, city 3, cast member 4).
    /// </summary>
    /// <param name="subject">The clue's subject word (kind in the high byte).</param>
    /// <param name="item">The clue's item key (kind of item in bits 8..10).</param>
    /// <returns>The phrase's index.</returns>
    // orig: BUG 00e8:2e58 clue_kind_index (src/c/bug/bug_00e8_2e58.c) matched; also TAC 00e8:96dc, FINAL 0100:6822, GAME 00f7:557c; tests: LibraryTextOracleTests, FinalOracleTests, GameOracleTests
    public static int ClueKindIndex(int subject, int item)
    {
        const int phrasesPerItem = 5;
        var index = (item >> 8 & 7) * phrasesPerItem;
        // C: an arithmetic shift of a 16-bit int: a negative subject's kind stays negative and adds nothing
        for (var kind = C16.Sar(C16.Int(subject), 8); kind > 1; kind >>= 1)
        {
            index++;
        }
        return index;
    }

    /// <summary>
    /// Builds the sentence of a clue in the text buffer: hands the clue's subject and item to the part's sentence
    /// builder (<c>expand_clue_text</c>).
    /// </summary>
    /// <param name="clues">The clues.</param>
    /// <param name="clue">The clue, 0..79.</param>
    /// <param name="expandClueText">The part's sentence builder (subject, item, clue).</param>
    // orig: BUG 00e8:2fc9 activity_clue_text (src/c/bug/bug_00e8_2fc9.c) matched; also GAME 00f7:56ed, TAC 00e8:984d, FINAL 0100:6993; tests: LibraryTextOracleTests
    public static void ActivityClueText(IReadOnlyList<Clue> clues, int clue, Action<int, int, int> expandClueText) =>
        expandClueText(clues[clue].Info1, clues[clue].Key1, clue);

    /// <summary>
    /// Removes a clue from the clue table: every later clue moves one place down, up to the first free record
    /// (<see cref="Clue.Act"/> -1), whose copy is marked removed (organisation and city -1).
    /// </summary>
    /// <param name="clues">The clue table (80 records).</param>
    /// <param name="clue">The clue to remove.</param>
    // orig: BUG 00e8:28c5 delete_clue (src/c/bug/bug_00e8_28c5.c) matched; also FINAL 0100:628f, GAME 00f7:4fe9, TAC 00e8:9149; tests: LibraryTextOracleTests
    public static void DeleteClue(IReadOnlyList<Clue> clues, int clue)
    {
        for (var i = clue; i < LastClue; i++)
        {
            clues[i].CopyFrom(clues[i + 1]);
            if (clues[i].Act == -1)
            {
                clues[i].Sorg = -1;
                clues[i].Sloc = -1;
                return;
            }
        }
    }

    /// <summary>
    /// Frees a person's record when no member of the cast is mapped onto it any more: the record loses its face,
    /// organisation, city and what the player knew of it.
    /// </summary>
    /// <param name="persons">The persons.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: BUG 00e8:22ad clear_unused_person (src/c/bug/bug_00e8_22ad.c) matched; also TAC 00e8:8b31, FINAL 0100:5c77; tests: LibraryTextOracleTests, FinalOracleTests
    public static void ClearUnusedPerson(IReadOnlyList<Person> persons, int person)
    {
        var record = persons[person];
        if ((record.Role & CastMembers) != 0)
        {
            return;
        }
        record.Face = 0;
        record.Org = -1;
        record.Loc = -1;
        record.Clues = 0;
    }

    /// <summary>
    /// Records what the player learnt of an organisation's building in a city: with any knowledge bits the city counts
    /// the organisation as seen there; the bits are added to the building's record, which is made at the end of the
    /// table when there is none yet (and there are bits to record).
    /// </summary>
    /// <param name="buildings">The buildings (<c>lc</c>).</param>
    /// <param name="cities">The cities.</param>
    /// <param name="buildingCount">How many buildings are in use (<c>Nloc</c>).</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="flags">The knowledge bits to add (<see cref="Location.Clues"/>); 0 only looks the building up.</param>
    /// <param name="newCount">The number of buildings in use afterwards: one more when a building was made.</param>
    /// <returns>The building's index, or -1 when it is not known or nothing is known of it.</returns>
    // orig: BUG 00e8:272b add_link (src/c/bug/bug_00e8_272b.c) matched; also TAC 00e8:8faf, FINAL 0100:60f5, GAME 00f7:4e4f; tests: LibraryTextOracleTests, FinalOracleTests, GameOracleTests, BugOracleTests
    public static int AddLink(IReadOnlyList<Location> buildings, IReadOnlyList<City> cities, int buildingCount, int org, int city, int flags, out int newCount)
    {
        newCount = buildingCount;
        if (flags != 0)
        {
            // C: a shift by a variable count, as the 8086 takes it
            cities[city].OrgClues |= C16.Shl((short)1, org);
        }
        for (var i = 0; i < buildingCount; i++)
        {
            var building = buildings[i];
            if (building.Org == org && building.Loc == city)
            {
                building.Clues |= (short)flags;
                return building.Clues != 0 ? i : -1;
            }
        }
        if (flags == 0)
        {
            return -1;
        }
        var added = buildings[buildingCount];
        added.Org = (short)org;
        added.Loc = (short)city;
        added.Clues = (short)flags;
        added.Tapped = 0;
        added.Seen = 0;
        newCount = buildingCount + 1;
        return buildingCount;
    }

    /// <summary><see cref="Synopsis.Clues"/> and <see cref="Person.Clues"/>: what is known of a message and its party.</summary>
    private const short DateKnown = 0x01, PartyNameKnown = 0x02, PartyOrgKnown = 0x04, PartyCityKnown = 0x08;

    /// <summary><see cref="Event.Status"/>: the bits that name the event's partner and kind; 0 for an event without a partner.</summary>
    private const int EventPartnerBits = 0xfff;

    /// <summary>Minutes in a day of the campaign's clock.</summary>
    private const short MinutesPerDay = 1440;

    /// <summary>
    /// Appends a line about an intercepted message to the text buffer: what happened ("Sent msg to" ...), the other
    /// party as far as the player knows them (name or "someone", organisation, city), and the date if the player
    /// knows it.
    /// </summary>
    /// <remarks>
    /// A message of an event with a partner is one of a pair recorded at the same minute (the sender's and the
    /// receiver's): the next message with the same minute, or the one before, which wins; without a pair it is the
    /// bytes in front of the table (<see cref="ICampaignTables.MessageBeforeFirst"/>). What the player knows of one
    /// side passes to the other: a party identified by name, or by organisation and city, gives the message the name
    /// and organisation bits and the person learns them from it (and the city when it is the person's own).
    /// </remarks>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="campaign">The part's campaign tables.</param>
    /// <param name="names">The part's name tables.</param>
    /// <param name="words">The words of a log line.</param>
    /// <param name="numberText">The part's buffer of <c>itoa</c>, which keeps the day's digits.</param>
    /// <param name="msg">The message, 0..127.</param>
    // orig: BUG 00e8:37fd append_msg_log_entry (src/c/bug/bug_00e8_37fd.c) matched; also FINAL 0100:71c7, TAC 00e8:a081; tests: LibraryTextOracleTests
    // Other code that does much the same: GAME's append_message_text (CampaignTexts.AppendMessageText).
    public static void AppendMsgLogEntry(TextBuffer text, ICampaignTables campaign, PersonNameTexts names, MessageLogTexts words, TextBuffer numberText, int msg)
    {
        var message = campaign.Sn[msg];
        int eventIndex = message.Data;
        text.Text += eventIndex >= 0 ? campaign.Ev[eventIndex].Name : (eventIndex & 1) != 0 ? words.Received : words.Sent;
        // C: the original tests the event's partner bits before the sign of the index; for a negative index the bits it reads do not change the outcome
        if (eventIndex < 0 || (campaign.Ev[eventIndex].Status & EventPartnerBits) != 0)
        {
            var sender = campaign.Ps[message.Who];
            int other = message.Who2;
            var receiver = campaign.Ps[other];
            var pair = campaign.MessageBeforeFirst;
            if (MessageAt(campaign, msg + 1).Time == message.Time)
            {
                pair = campaign.Sn[msg + 1];
            }
            if (MessageAt(campaign, msg - 1).Time == message.Time)
            {
                pair = MessageAt(campaign, msg - 1);
            }
            if ((message.Clues & PartyNameKnown) != 0)
            {
                text.Text += words.Space;
                AppendPersonName(text, campaign.Ps, campaign.Orgs, names, other);
            }
            else
            {
                text.Text += words.Someone;
            }
            if ((message.Clues & PartyOrgKnown) != 0)
            {
                text.Text += words.OfThe;
                text.Text += campaign.Orgs[receiver.Org].Sname;
            }
            if ((message.Clues & PartyCityKnown) != 0)
            {
                text.Text += words.In;
                text.Text += campaign.Ct[message.Loc2].Name;
            }
            ShareKnowledge(message, receiver, pair, sender);
            ShareKnowledge(pair, sender, message, receiver);
        }
        if ((message.Clues & DateKnown) != 0)
        {
            text.Text += words.On;
            text.Text += words.MonthNames[campaign.Pl.Ncase % words.MonthNames.Length];
            text.Text += words.Space;
            CStr.Itoa(C16.Int(C16.Div(message.Time, MinutesPerDay) + 1), numberText.Bytes, 10);
            text.Text += numberText.Text;
        }
        text.Text += words.End;
    }

    /// <summary>
    /// When a message and its party identify the party (both know the name, or the message knows organisation and city
    /// and the person is known by both), the name and organisation bits pass between message, pair and person, and the
    /// city bit too when the message's city is the person's own.
    /// </summary>
    private static void ShareKnowledge(Synopsis message, Person party, Synopsis pair, Person pairParty)
    {
        const short nameAndOrg = PartyNameKnown | PartyOrgKnown;
        var identified = ((message.Clues & PartyNameKnown) != 0 && (party.Clues & PartyNameKnown) != 0)
            || ((message.Clues & PartyOrgKnown) != 0 && (message.Clues & PartyCityKnown) != 0
                && (party.Clues & PartyOrgKnown) != 0 && (party.Clues & PartyCityKnown) != 0);
        if (!identified)
        {
            return;
        }
        message.Clues |= (short)(party.Clues & nameAndOrg);
        pair.Clues |= (short)(pairParty.Clues & nameAndOrg);
        party.Clues |= (short)(message.Clues & nameAndOrg);
        if (message.Loc2 == party.Loc)
        {
            message.Clues |= (short)(party.Clues & PartyCityKnown);
            party.Clues |= (short)(message.Clues & PartyCityKnown);
        }
    }

    /// <summary>A message by the original's index; -1 is what lies in front of the table.</summary>
    private static Synopsis MessageAt(ICampaignTables campaign, int msg) => msg == -1 ? campaign.MessageBeforeFirst : campaign.Sn[msg];

    /// <summary>
    /// Forgets what the player knows of an organisation's building in a city: the building's knowledge, wiretap and
    /// explored rooms are cleared (the record stays in the table), and the city no longer counts the organisation as
    /// seen there.
    /// </summary>
    /// <param name="buildings">The buildings (<c>lc</c>).</param>
    /// <param name="buildingCount">How many are in use (<c>Nloc</c>).</param>
    /// <param name="cities">The cities.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    // orig: BUG 00e8:27cc remove_link (src/c/bug/bug_00e8_27cc.c) matched; also FINAL 0100:6196, GAME 00f7:4ef0, TAC 00e8:9050; tests: LibraryTextOracleTests
    public static void RemoveLink(IReadOnlyList<Location> buildings, int buildingCount, IReadOnlyList<City> cities, int org, int city)
    {
        for (var i = 0; i < buildingCount; i++)
        {
            var building = buildings[i];
            if (building.Org != org || building.Loc != city)
            {
                continue;
            }
            building.Clues = 0;
            building.Tapped = 0;
            building.Seen = 0;
            // C: a shift by a variable count, as the 8086 takes it
            cities[city].OrgClues &= (short)~C16.Shl((short)1, org);
            return;
        }
    }

    /// <summary>
    /// Appends the sentence of a clue to the text buffer: the label of its item (a car, a weapon ...) and the phrase
    /// that joins item and subject (" tied to " ...), then its subject (a name, an organisation, a city ...) and a full
    /// stop. A clue without an item (key -1) or without a subject (-1) leaves that half out.
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="clues">The clues.</param>
    /// <param name="phrases">The part's joining phrases, five per kind of item (see <see cref="ClueKindIndex"/>).</param>
    /// <param name="period">The full stop after the subject.</param>
    /// <param name="clue">The clue, 0..79.</param>
    /// <param name="appendClueLabel">The part's routine that appends an item's label (the key).</param>
    /// <param name="appendClueSubject">The part's routine that appends a subject (the clue, its subject word).</param>
    // orig: GAME 00f7:5513 append_activity_text (src/c/game/game_00f7_5513.c) matched; also TAC 00e8:9673, FINAL 0100:67b9, BUG 00e8:2def; tests: LibraryModuleOracleTests
    public static void AppendActivityText(TextBuffer text, IReadOnlyList<Clue> clues, IReadOnlyList<string> phrases, string period, int clue,
        Action<int> appendClueLabel, Action<int, int> appendClueSubject)
    {
        const int none = -1;
        var record = clues[clue];
        if (record.Key1 != none)
        {
            appendClueLabel(record.Key1);
            text.Text += phrases[ClueKindIndex(record.Info1, record.Key1)];
        }
        if (record.Info1 != none)
        {
            appendClueSubject(clue, record.Info1);
            text.Text += period;
        }
    }

    /// <summary>Kinds of clue item (bits 8..11 of a key) whose number is a sum of money, in hundreds and in thousands.</summary>
    private const int MoneyHundreds = 5, MoneyThousands = 6;

    /// <summary>
    /// Appends the label of a clue's item to the text (a car, a weapon, an address, a ticket ...): the label picked by
    /// the key's kind (bits 8..11) and variant (bits 4..6), then the key's number (bits 0..5, from 1), then for sums of
    /// money the zeros behind the number. A key of -1 appends nothing.
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="labels">The part's labels: eight per kind of item.</param>
    /// <param name="numberText">The part's buffer of <c>itoa</c>, which keeps the number's digits.</param>
    /// <param name="hundreds">What follows a sum in hundreds: "00".</param>
    /// <param name="thousands">What follows a sum in thousands: "000".</param>
    /// <param name="key">The clue's key.</param>
    /// <exception cref="IndexOutOfRangeException">A kind beyond the part's table: the original reads a label from behind it.</exception>
    // orig: BUG 00e8:2f44 append_key_text (src/c/bug/bug_00e8_2f44.c) matched; also TAC 00e8:97c8; tests: LibraryTextOracleTests
    // FINAL's append_numbered_label and GAME's append_clue_label are the same code, ported for now in their parts: to be replaced by this method.
    public static void AppendKeyText(TextBuffer text, IReadOnlyList<IReadOnlyList<string>> labels, TextBuffer numberText, string hundreds, string thousands, int key)
    {
        const int noItem = -1;
        if (key == noItem)
        {
            return;
        }
        var kind = key >> 8 & 0xf;
        text.Text += labels[kind][key >> 4 & 7];
        CStr.Itoa((short)((key & 0x3f) + 1), numberText.Bytes, 10);
        text.Text += numberText.Text;
        if (kind == MoneyHundreds)
        {
            text.Text += hundreds;
        }
        if (kind == MoneyThousands)
        {
            text.Text += thousands;
        }
    }

    /// <summary>
    /// What a clue of a cast member points at, as a subject word (the kind in the high byte, the index in the low
    /// byte): the person behind the cast member (by photo or name), the organisation (counted as seen, made known, and
    /// its name appended to the text buffer), the person's city (counted as seen), or the cast member itself.
    /// </summary>
    /// <param name="text">The part's text buffer.</param>
    /// <param name="campaign">The part's campaign tables.</param>
    /// <param name="clue">The clue, 0..79.</param>
    /// <param name="kind">The kind of clue: 1 photo, 2 name, 4 organisation, 8 city, 0x10 cast member.</param>
    /// <returns>The subject word.</returns>
    // orig: BUG 00e8:2c6f clue_target_code (src/c/bug/bug_00e8_2c6f.c) matched; also FINAL 0100:6639, TAC 00e8:94f3; tests: LibraryTextOracleTests
    public static int ClueTargetCode(TextBuffer text, ICampaignTables campaign, int clue, int kind)
    {
        const int photo = 1, name = 2, org = 4, city = 8, castMember = 0x10;
        // C: the person is looked up for every kind
        int person = campaign.Ac[campaign.Cl[clue].Act].Who;
        var who = campaign.Ps[person];
        switch (kind)
        {
            case city:
                campaign.Active[who.Loc]++;
                return (kind << 8) + who.Loc;
            case org:
                campaign.OrgCounters[who.Org]++;
                campaign.Orgs[who.Org].Clues |= 1;
                text.Text += campaign.Orgs[who.Org].Name;
                return (kind << 8) + who.Org;
            case photo:
            case name:
                return (kind << 8) + person;
            case castMember:
                return campaign.Cl[clue].Act + (kind << 8);
            default:
                // DORMANT: the clue report passes one of the five kinds; for another the original returns whatever AX holds
                return 0;
        }
    }
}
