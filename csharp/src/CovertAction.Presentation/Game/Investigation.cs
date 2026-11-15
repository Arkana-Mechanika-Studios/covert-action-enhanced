using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// What a search of the CIA's agents yields: how much the persons of the case give away of themselves, which clues
/// of the cast's activities and which parts of intercepted messages the player gets, and, when a search brings
/// nothing, a hint.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="plot">The passing of time and the plot's progress (the finding of persons).</param>
public sealed class Investigation(CampaignState state, CampaignScreens screens, PlotEngine plot)
{
    /// <summary>The bits of <see cref="Core.Records.Activity.Clued"/> that count the clues of an activity (five), and the bits of a message's knowledge (four).</summary>
    private const int ActivityClueBits = 0x1f, MessageKnowledgeBits = 0xf;

    /// <summary>
    /// <see cref="Core.Records.Person.Clues"/>: the bits that count as things the player knows of a person (0x70f), among
    /// them the person's name, organisation and city; and the bit that says the person's messages were shown as evidence.
    /// </summary>
    private const int PersonKnowledgeBits = 0x70f, KnowsName = 2, KnowsOrganization = 4, KnowsCity = 8, EvidenceShown = 0x10;

    /// <summary>The word <see cref="Core.Records.Person.Clues"/> of a person the player knows nothing of.</summary>
    private const int NothingKnown = 0;

    /// <summary>Bits of <see cref="Core.Records.Event.Status"/>: the event's kind, in the first three digits, and the two kinds that give a message a heading.</summary>
    private const int EventKindBits = 0xfff, CodedKind = 0x200, MeetingKind = 0x800, MessageKinds = 0xf00;

    /// <summary>The message knowledge bits: when, with whom (the second person), the organisation of the second person, the city of the second.</summary>
    private const int KnowsWhen = 1, KnowsWho = 2, KnowsWhoseOrganization = 4, KnowsWhere = 8;

    /// <summary>Everything the player may know of a message.</summary>
    private const int MessageKnown = 0xf;

    /// <summary>What the two parts of a message the player learns need to be marked known at once (the second person's bits).</summary>
    private const int SecondPersonBits = 0xe;

    /// <summary>The minutes of a day on the clock: the day number of a message.</summary>
    private const int MinutesPerDay = 0x5a0;

    /// <summary>The game's colours of the message box and its text.</summary>
    private const int BoxColourCga = 4, BoxColour = 3, TextColour = 0xb, BoxPen = 3;

    /// <summary>
    /// Works out what a search of an organisation's agents in a city gives the player. Every person of the case gets
    /// nearer to the player's search by how close to it the person is on the map (by organisation and by city); the
    /// cast's activities give up clues of their own; the messages of the person give up the parts the person's growing
    /// intelligence allows, one box at a time; what the player knows of the person grows. When nothing new came of it
    /// and a hint is wanted, a hint is shown: a city, an organisation, a face, a name or a new building.
    /// </summary>
    /// <param name="org">The organisation the search is made as (the CIA, 1).</param>
    /// <param name="city">The city searched.</param>
    /// <param name="strength">How strong the search is (0 and below finds nothing, 100 is a full local scan).</param>
    /// <param name="hint">Not 0: show a hint when nothing was found (kept in <see cref="CampaignState.LastHint"/> for the clue screen).</param>
    /// <returns>The number of new facts found.</returns>
    // orig: GAME 00f7:3fa6 search_yield (src/c/game/game_00f7_3fa6.c) matched; tests: GameOracleTests
    public int SearchYield(int org, int city, int strength, int hint)
    {
        state.LastHint = (short)hint;
        if (strength <= 0)
        {
            return 0;
        }
        var found = 0;
        for (var person = 0; person < state.Nperson; person++)
        {
            if (state.Ps[person].Face == 0)
            {
                continue;
            }
            found += SearchPerson(person, org, city, strength);
        }
        if (found == 0 && hint != 0)
        {
            ShowHint();
        }
        return found;
    }

    /// <summary>
    /// What the search gives of one person: how near the search is (on the map) to his organisation and his city
    /// raises his intelligence, which loosens the clues of the activities he is in and the parts of his messages, and
    /// the knowledge of the person himself.
    /// </summary>
    /// <returns>The facts newly found: the clues of the activities, and one when the person's knowledge grew.</returns>
    private int SearchPerson(int person, int org, int city, int strength)
    {
        var who = state.Ps[person];
        var found = 0;
        // the person's organisation and city as they are when the search begins
        var (home, homeCity) = (who.Org, who.Loc);
        var distanceOfOrg = WorldRules.MapDistance(state.Orgs[org].Climate, state.Orgs[home].Climate);
        var distanceOfCity = WorldRules.MapDistance(state.Ct[city].Climate, state.Ct[homeCity].Climate);
        // C: the products are 16-bit ints, widened to long for the shift and the division, narrowed again for the result
        var orgScale = C16.Int(C16.LDiv(C16.LShl((distanceOfOrg + 2) * (distanceOfCity + 2), 8), strength + 1));
        var clueScale = C16.Int(C16.LDiv(C16.LShl((distanceOfOrg + 3) * (distanceOfCity + 6), 6), strength + 1));
        var step = C16.Div(C16.Div(10000, C16.Int(state.Diff + 2)), orgScale);
        who.Intel = C16.Int(who.Intel + C16.Int(C16.LDiv(C16.LMul(step, step), C16.Int(C16.Div(who.Intel, 2) + step + 1))));
        // C: the original also divides 2500 by the new intelligence + 1 here and never uses the quotient

        found += LoosenActivityClues(person, org, city, clueScale);
        if (who.Clues == NothingKnown && distanceOfOrg != 0 && distanceOfCity != 0)
        {
            return found;
        }
        var knowledge = (ushort)who.Clues;
        LoosenMessages(person, distanceOfOrg + distanceOfCity, ref knowledge);
        knowledge = GrowKnowledge(who, (org, city), (home, homeCity), knowledge);
        if ((ushort)who.Clues != knowledge)
        {
            plot.FindOrCreatePerson(home, homeCity, knowledge, true);
            found++;
        }
        return found;
    }

    /// <summary>
    /// The activities of the cast a person is in (that have begun) get a share of the search's intelligence; as long
    /// as an activity has more than its clues' worth of it, one more clue of it is given.
    /// </summary>
    /// <returns>The new facts the clues were.</returns>
    private int LoosenActivityClues(int person, int org, int city, int clueScale)
    {
        const int bitsToWeight = 5;
        var found = 0;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            var activity = state.Ac[member];
            var times = state.Ac2[member];
            if (activity.Who != person || times.Chron > state.ChronTime || times.Chron == -1)
            {
                continue;
            }
            times.Intel = C16.Int(times.Intel + C16.Div(C16.Div(5000, C16.Int(state.Diff + 3)), C16.Int(clueScale)));
            var cluesGiven = state.BitCount[activity.Clued & ActivityClueBits];
            // C: the product is a 16-bit int, the right side shifted as one
            while (C16.Int(times.Intel * (activity.Cover + 2)) > C16.Int((cluesGiven + 2) * (cluesGiven + 2) << bitsToWeight)
                && (activity.Clued & ActivityClueBits) != ActivityClueBits)
            {
                found += screens.AddRandomClue(member, org, city) ? 1 : 0;
                cluesGiven = state.BitCount[activity.Clued & ActivityClueBits];
            }
        }
        return found;
    }

    /// <summary>
    /// The messages a person sends or receives give up more of themselves the more intelligence the search has
    /// brought: each part not yet known (when, with whom, whose organisation, where) is shown in a box, and the
    /// person on the other side of a message is found with what the player now knows of him.
    /// </summary>
    /// <param name="person">The person.</param>
    /// <param name="distance">How far the search is from the person (organisation and city together): 0 for a search that hit him.</param>
    /// <param name="knowledge">What the player knows of the person; the evidence bit may be set or cleared.</param>
    private void LoosenMessages(int person, int distance, ref ushort knowledge)
    {
        const int evidenceShare = 0x60, firstNeed = 2;
        var who = state.Ps[person];
        var need = firstNeed;
        for (var message = 0; message < state.NextSyn; message++)
        {
            var log = state.Sn[message];
            if (log.Who != person)
            {
                continue;
            }
            var happening = state.Ev[log.Data];
            var activity = state.Ac[happening.Act];
            while (C16.Div(C16.Int(activity.Cover * who.Intel), evidenceShare) > 2 * state.BitCount[log.Clues & MessageKnowledgeBits] + need
                && (log.Clues & MessageKnowledgeBits) != MessageKnown)
            {
                var part = C16.Shl(1, GameLibrary.RandomBelow(state.Random, 4));
                if ((log.Clues & part) == 0)
                {
                    LearnPartOfMessage(person, message, part, distance, ref knowledge);
                }
            }
            need++;
            if ((happening.Status & MessageKinds) != 0 && (log.Clues & SecondPersonBits) != 0)
            {
                var other = state.Ps[log.Who2];
                plot.FindOrCreatePerson(other.Org, other.Loc, log.Clues & (KnowsWho | KnowsWhoseOrganization), false);
                if (log.Loc2 == other.Loc)
                {
                    plot.FindOrCreatePerson(other.Org, other.Loc, log.Clues & KnowsWhere, false);
                }
            }
        }
    }

    /// <summary>
    /// Shows the player one more part of a message of a person (its date, the other person, his organisation or his
    /// city): the person's dossier, then a box with what is now known of the message, then the dossier again. A message
    /// that is no meeting or coded message is shown at once in full.
    /// </summary>
    private void LearnPartOfMessage(int person, int message, int part, int distance, ref ushort knowledge)
    {
        var who = state.Ps[person];
        var log = state.Sn[message];
        var happening = state.Ev[log.Data];
        var words = state.InvestigationWords;
        screens.DataFiles.DrawPersonDossier(person, 0, 0);
        state.Txt.Text = "";
        if ((happening.Status & CodedKind) != 0)
        {
            state.Txt.Text = words.CodedMessageHeading;
        }
        if ((happening.Status & MeetingKind) != 0)
        {
            state.Txt.Text = words.MeetingNotesHeading;
        }
        state.Txt.Text += log.Clues == 0 ? words.NewInformation : words.AdditionalInformation;
        CampaignTexts.AppendPersonName(state, person);
        state.Txt.Text += words.NameSpace;
        CampaignTexts.AppendMessageText(state, message);
        state.Txt.Text += words.DotsLine;
        if ((part & KnowsWhen) != 0)
        {
            state.Txt.Text += words.On;
            state.Txt.Text += state.MonthNames[state.Pl.Ncase % CampaignState.Months];
            state.Txt.Text += words.DateSpace;
            state.NumberText.Text = (log.Time / MinutesPerDay + 1).ToString();
            state.Txt.Text += state.NumberText.Text;
        }
        if ((happening.Status & EventKindBits) != 0)
        {
            var other = state.Ps[log.Who2];
            if ((part & KnowsWho) != 0)
            {
                other.Clues |= KnowsName;
                CampaignTexts.AppendPersonName(state, log.Who2);
            }
            if ((part & KnowsWhoseOrganization) != 0)
            {
                state.Txt.Text += words.OfThe;
                state.Txt.Text += state.Orgs[other.Org].Name;
            }
            if ((part & KnowsWhere) != 0)
            {
                state.Txt.Text += words.In;
                state.Txt.Text += state.Ct[log.Loc2].Name;
            }
            state.Txt.Text += words.LineEnd;
            screens.Output.DrawFramedBox(0x10, 0x60, 0x108, 0x30, state.CGA ? BoxColourCga : BoxColour);
            state.Rp1.BPen = BoxPen;
            screens.Text.DrawWrappedText(0x20, 0x14, 0x64, TextColour);
            screens.Screens.WaitForKey();
            log.Clues |= (short)part;
            screens.DataFiles.DrawPersonDossier(person, 0, 0);
            screens.Screens.WaitForKey();
            who.Clues |= EvidenceShown;
        }
        else
        {
            log.Clues |= SecondPersonBits;
            screens.Screens.WaitForKey();
        }
        log.Clues |= (short)part;
        var actBit = C16.Shl(1, happening.Act);
        if ((who.Rknown & actBit) == 0)
        {
            // a search that hit the person on the easiest level, or a message with no parties, proves what it tells
            if ((state.Diff < 1 && distance == 0 && (log.Clues & MessageKnowledgeBits) == MessageKnown) || (happening.Status & EventKindBits) == 0)
            {
                who.Rknown |= (short)actBit;
                knowledge |= EvidenceShown;
                who.Clues &= unchecked((short)~EvidenceShown);
                log.Clues |= MessageKnown;
            }
        }
    }

    /// <summary>
    /// Lets the player know more of a person as far as the person's intelligence reaches: first the organisation if
    /// the search was of it, then the city if the search was there, then other things at random, up to a number
    /// of facts that grows with the intelligence.
    /// </summary>
    /// <returns>The person's new knowledge word.</returns>
    private ushort GrowKnowledge(Core.Records.Person who, (int Org, int City) searched, (int Org, int City) home, ushort knowledge)
    {
        const int factWeight = 25;
        const int knowledgeLow = 0xf, knowledgeKinds = 12, highShift = 8;
        while (knowledge != 0
            && (state.BitCount[(knowledge & PersonKnowledgeBits) >> highShift] + state.BitCount[knowledge & knowledgeLow]) * factWeight < who.Intel
            && (knowledge & PersonKnowledgeBits) != PersonKnowledgeBits)
        {
            if (searched.Org == home.Org && (knowledge & KnowsOrganization) == 0)
            {
                knowledge |= KnowsOrganization;
                break;
            }
            if (searched.City == home.City && (knowledge & KnowsCity) == 0)
            {
                knowledge |= KnowsCity;
                break;
            }
            int fact;
            do
            {
                fact = C16.Shl(1, GameLibrary.RandomBelow(state.Random, knowledgeKinds));
            }
            while ((fact & knowledge & PersonKnowledgeBits) != 0 || (fact & PersonKnowledgeBits) == 0);
            knowledge |= (ushort)fact;
        }
        return knowledge;
    }

    /// <summary>
    /// A search that found nothing may give a hint instead, by chance: a city where something is going on, an
    /// organisation that takes part, how a member of the cast looks, a member's name, or a building of an organisation
    /// the player did not know.
    /// </summary>
    private void ShowHint()
    {
        const int cityHint = 0, organizationHint = 1, faceHint = 2, nameHint = 3, hintKinds = 8;
        const int noPicture = -1, buildingPicture = -2, buildingBits = 12, orgKnown = 1;
        state.Txt.Text = "";
        var member = state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who;
        var who = state.Ps[member];
        state.Picface = noPicture;
        var kind = GameLibrary.RandomBelow(state.Random, hintKinds - state.Diff);
        switch (kind)
        {
            case cityHint:
                state.Txt.Text = state.Ct[who.Loc].Name;
                state.Active[who.Loc]++;
                break;
            case organizationHint:
                state.Txt.Text = state.Orgs[who.Org].Name;
                state.OrgCounters[who.Org]++;
                state.Orgs[who.Org].Clues |= orgKnown;
                break;
            case faceHint:
                state.Txt.Text = state.InvestigationWords.LooksHint;
                state.Picface = who.Face;
                break;
            case nameHint:
                // the name is put into the box without the player coming to know it: the knowledge is put back
                var before = who.Clues;
                who.Clues |= KnowsName;
                state.Txt.Text = "";
                CampaignTexts.AppendPersonName(state, member);
                who.Clues = before;
                break;
            case 4 or 6:
                // a building of an organisation of the cast in a city of the cast
                ShowBuildingHint(state.AtLoc, state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Loc, buildingPicture, buildingBits, orgKnown);
                break;
            default:
                ShowBuildingHint(state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Org, state.InCity, buildingPicture, buildingBits, orgKnown);
                break;
        }
        if (state.Txt.Text.Length > 0)
        {
            CampaignScreens.EmptyStub();
            screens.Screens.WaitForKey();
        }
    }

    /// <summary>
    /// The hint of a building: when the building is plausible, the city has no building of the organisation known
    /// yet and the organisation is there, the player comes to know it ("Org Kind in City"); otherwise there is no hint.
    /// </summary>
    private void ShowBuildingHint(int org, int city, int buildingPicture, int buildingBits, int orgKnown)
    {
        if (WorldRules.BuildingPlausible(state, state.AtLoc, org, city)
            && (state.Ct[city].OrgClues & C16.Shl(1, org)) == 0
            && WorldRules.OrgPresence(state, city, org) != 0)
        {
            state.Ct[city].OrgClues |= C16.Shl(1, org);
            BuildingGlue.FindOrAddLink(state, org, city, buildingBits);
            state.Orgs[org].Clues |= (short)orgKnown;
            state.Txt.Text = state.Orgs[org].Name;
            BuildingGlue.AppendBuildingType(state, city, org);
            state.Txt.Text += state.InvestigationWords.BuildingIn;
            state.Txt.Text += state.Ct[city].Name;
            state.Picface = (short)buildingPicture;
        }
        else
        {
            state.Txt.Text = "";
        }
    }
}
