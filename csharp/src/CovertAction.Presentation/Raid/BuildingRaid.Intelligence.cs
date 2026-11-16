using CovertAction.Core.Raid;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Raid;

/// <summary>
/// What the player learns in the building: the intelligence a photo or a search gives about the persons of the case,
/// the notes boxes of the messages found, the hint shown when nothing new was learned, and the speech balloon.
/// </summary>
public sealed partial class BuildingRaid
{
    private LibraryDossiers? _dossiers;

    /// <summary>The library's screens about persons (dossier, personnel file, clue report), on this part's campaign tables.</summary>
    public LibraryDossiers Dossiers => _dossiers ??= new LibraryDossiers(state, state, state, state.Dossier, Output, Text, Screens, state, state.Random);

    /// <summary><see cref="Person.Clues"/>: the bits a person's dossier shows: name, organisation, city, ... and "new" (0x10).</summary>
    private const int NameKnown = 0x02, OrgKnown = 0x04, CityKnown = 0x08, NewFacts = 0x10, RevealableFacts = 0x70f;

    /// <summary><see cref="Synopsis.Clues"/>: what the player knows of a message: its date (1), sender (2), organisation (4), city (8).</summary>
    private const int DateKnown = 1, PartyKnown = 2, PartyOrgKnown = 4, PartyCityKnown = 8, AllMessageFacts = 0xf;

    /// <summary><see cref="Event.Status"/>: a coded message (0x200), meeting notes (0x800); the event has a partner (any of the low twelve bits).</summary>
    private const int CodedMessage = 0x200, MeetingNotes = 0x800, MessageKinds = 0xf00, HasPartner = 0xfff;

    /// <summary><see cref="Activity.Clued"/>: the five clues a cast member can give.</summary>
    private const int AllCastClues = 0x1f;

    /// <summary>The kind of link a hint makes known: a building of the organisation in the city (<see cref="CampaignLibrary.AddLink"/>).</summary>
    private const int HintLink = 12;

    /// <summary>Minutes of a day: a message's minute over this is its day.</summary>
    private const int MinutesPerDay = 0x5a0;

    /// <summary>A cast member that has not yet appeared in the plot (<see cref="Activity2.Chron"/>).</summary>
    private const int NotYetActive = -1;

    /// <summary>
    /// Turns intelligence gathered in a building into knowledge of the case's persons. For every person of the case the
    /// score of what is known grows (the more the nearer the person's organisation and city are to the building's, and
    /// the more intelligence there is); cast members of the person may give up clues; each message of the person may
    /// show the player one more fact in a notes box (date, the other party, its organisation, its city); and while the
    /// score allows, more facts of the person become known. When nothing at all was learned and
    /// <paramref name="announce"/> is set, a random hint is shown in the speech balloon instead.
    /// </summary>
    /// <param name="org">The organisation of the building.</param>
    /// <param name="city">The city of the building.</param>
    /// <param name="gain">The intelligence gained; nothing happens below 1.</param>
    /// <param name="announce">Nonzero to show a hint when nothing new was learned; also kept as the clue report's source (<see cref="DossierTexts.CodedFlag"/>).</param>
    /// <returns>The number of facts learned.</returns>
    // orig: TAC 00e8:8106 gather_intelligence (src/c/tac/tac_00e8_8106.c) matched; tests: TacOracleTests
    // Other code under the same name: FINAL 0100:524c and BUG 00e8:1882 (docs/source-map/shared-functions.csv).
    public int GatherIntelligence(int org, int city, int gain, int announce)
    {
        var learned = 0;
        state.Dossier.CodedFlag = (short)announce;
        if (gain <= 0)
        {
            return 0;
        }
        for (var p = 0; p < state.Nperson; p++)
        {
            var person = state.Ps[p];
            if (person.Face == 0)
            {
                // not a person of the case
                continue;
            }
            int personOrg = person.Org, personCity = person.Loc;
            // how far the person's organisation and city are from the building's (0: the same)
            var orgDistance = GameLibrary.KeyDistance(state.Org[org].Climate, state.Org[personOrg].Climate);
            var cityDistance = GameLibrary.KeyDistance(state.Ct[city].Climate, state.Ct[personCity].Climate);
            // C: the products are 16-bit ints made long before the shift, divided as longs; the quotients go back to 16 bits
            var rate = C16.Int(C16.LDiv(C16.Int((orgDistance + 2) * (cityDistance + 2)) << 8, C16.Int(gain + 1)));
            var castRate = C16.Int(C16.LDiv(C16.Int((orgDistance + 3) * (cityDistance + 6)) << 6, C16.Int(gain + 1)));
            var step = C16.Div(C16.Div(10000, C16.Int(state.Diff + 2)), rate);
            person.Intel = C16.Int(person.Intel + C16.Int(C16.LDiv(step * step, C16.Int(person.Intel / 2 + step + 1))));
            // C: the quotient is never used; the division stays for its divide error at a score of -1
            _ = C16.Div(2500, C16.Int(person.Intel + 1));
            learned += RaiseCastProgress(p, org, city, castRate);
            if (person.Clues == 0 && orgDistance != 0 && cityDistance != 0)
            {
                continue;
            }
            int known = C16.Unsigned(person.Clues);
            known = ShowMessageNotes(p, orgDistance + cityDistance, known);
            known = RevealRandomFacts(person, org == personOrg, city == personCity, known);
            if (C16.Unsigned(person.Clues) == known)
            {
                continue;
            }
            CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, personOrg, personCity, known, true, RevealPersonFacts, behaviour);
            learned++;
        }
        if (learned == 0 && announce != 0)
        {
            ShowHint();
        }
        return learned;
    }

    /// <summary>
    /// The cast members of a person who have appeared in the plot gain progress; while it passes the threshold of their
    /// next clue, the clue is revealed (gather_intelligence).
    /// </summary>
    /// <returns>The clues revealed.</returns>
    private int RaiseCastProgress(int person, int org, int city, int castRate)
    {
        var revealed = 0;
        for (var f = 0; f < state.NACTIVITY; f++)
        {
            var member = state.Ac[f];
            var progress = state.Ac2[f];
            if (member.Who != person || progress.Chron > state.ChronTime || progress.Chron == NotYetActive)
            {
                continue;
            }
            progress.Intel = C16.Int(progress.Intel + C16.Div(C16.Div(5000, C16.Int(state.Diff + 3)), C16.Int(castRate)));
            var clues = state.PopCount[member.Clued & AllCastClues];
            // C: the product is a 16-bit int
            while (C16.Int(progress.Intel * (member.Cover + 2)) > (clues + 2) * (clues + 2) << 5 && (member.Clued & AllCastClues) != AllCastClues)
            {
                revealed += Dossiers.RevealCastClue(f, org, city) ? 1 : 0;
                clues = state.PopCount[member.Clued & AllCastClues];
            }
        }
        return revealed;
    }

    /// <summary>
    /// The messages of a person: each may show one more of its facts while the person's score allows, in a notes box
    /// over the person's dossier; a message whose facts are all known (or which has no partner) marks the person as
    /// seen by the cast member who sent it; and what a message tells of its other party becomes known of that person
    /// (gather_intelligence).
    /// </summary>
    /// <param name="person">The person.</param>
    /// <param name="distance">The organisation's and the city's distance from the building, added.</param>
    /// <param name="known">What is known of the person, as the caller keeps it.</param>
    /// <returns><paramref name="known"/>, with the "new" bit when a message was seen.</returns>
    private int ShowMessageNotes(int person, int distance, int known)
    {
        var who = state.Ps[person];
        var threshold = 2;
        for (var g = 0; g < state.NextSyn; g++)
        {
            var message = state.Sn[g];
            if (message.Who != person)
            {
                continue;
            }
            var happening = state.Ev[message.Data];
            // C: the product is a 16-bit int, divided as signed
            while (C16.Div(C16.Int(state.Ac[happening.Act].Cover * who.Intel), 0x60) > 2 * state.PopCount[message.Clues & AllMessageFacts] + threshold
                && (message.Clues & AllMessageFacts) != AllMessageFacts)
            {
                var fact = 1 << GameLibrary.RandomBelow(state.Random, 4);
                if ((message.Clues & fact) != 0)
                {
                    continue;
                }
                Dossiers.ShowSuspectDossier(person, 0, 0);
                if ((happening.Status & HasPartner) != 0)
                {
                    ShowNotesBox(person, g, fact);
                    message.Clues |= (short)fact;
                    Dossiers.ShowSuspectDossier(person, 0, 0);
                    Screens.WaitForKey();
                    who.Clues |= NewFacts;
                }
                else
                {
                    // the heading and the message line are put together but not shown
                    WriteNotesHeading(person, g, fact);
                    message.Clues |= 0xe;
                    Screens.WaitForKey();
                }
                message.Clues |= (short)fact;
                // C: a 16-bit shift by the sender's number
                var sender = C16.Shl(1, happening.Act);
                if ((who.Rknown & sender) == 0
                    && (state.Diff < 1 && distance == 0 && (message.Clues & AllMessageFacts) == AllMessageFacts || (happening.Status & HasPartner) == 0))
                {
                    who.Rknown |= (short)sender;
                    known |= NewFacts;
                    who.Clues &= ~NewFacts;
                    message.Clues |= AllMessageFacts;
                }
            }
            threshold++;
            if ((happening.Status & MessageKinds) != 0 && (message.Clues & 0xe) != 0)
            {
                var other = state.Ps[message.Who2];
                CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, other.Org, other.Loc, message.Clues & 6, false, RevealPersonFacts, behaviour);
                if (message.Loc2 == other.Loc)
                {
                    CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, other.Org, other.Loc, message.Clues & 8, false, RevealPersonFacts, behaviour);
                }
            }
        }
        return known;
    }

    /// <summary>The notes box's heading and message line in the text buffer: the kind of message, new or more, the person and the line of the log, and the date when it is the fact shown.</summary>
    private void WriteNotesHeading(int person, int msg, int fact)
    {
        var txt = state.MessageText;
        var message = state.Sn[msg];
        var status = state.Ev[message.Data].Status;
        txt.Text = "";
        if ((status & CodedMessage) != 0)
        {
            txt.Text = state.StrCodedMessage;
        }
        if ((status & MeetingNotes) != 0)
        {
            txt.Text = state.StrMeetingNotes;
        }
        txt.Text += message.Clues == 0 ? state.StrNewInformation : state.StrAdditionalInformation;
        AppendPersonName(person);
        txt.Text += state.StrNotesSpace;
        CampaignLibrary.AppendMsgLogEntry(txt, state, state.Dossier.Names, state.Dossier.Words, state.Dossier.NumberText, msg);
        txt.Text += state.StrNotesEllipsis;
        if ((fact & DateKnown) != 0)
        {
            txt.Text += state.StrOn;
            // the month is the case's number (TAC DS:98ee, month_index: the plot's ncase) modulo 12
            txt.Text += state.Dossier.Words.MonthNames[state.Pl.Ncase % 12];
            txt.Text += state.StrDateSpace;
            CStr.Itoa(C16.Int(C16.Div(message.Time, MinutesPerDay) + 1), state.Dossier.NumberText.Bytes, 10);
            txt.Text += state.Dossier.NumberText.Text;
        }
    }

    /// <summary>The notes box of a message's new fact: the heading, then the other party's name, organisation or city as the fact is, in a framed box; then the player's key.</summary>
    private void ShowNotesBox(int person, int msg, int fact)
    {
        WriteNotesHeading(person, msg, fact);
        var txt = state.MessageText;
        var message = state.Sn[msg];
        var other = state.Ps[message.Who2];
        if ((fact & PartyKnown) != 0)
        {
            other.Clues |= NameKnown;
            AppendPersonName(message.Who2);
        }
        if ((fact & PartyOrgKnown) != 0)
        {
            txt.Text += state.StrOfThe;
            txt.Text += state.Org[other.Org].Name;
        }
        if ((fact & PartyCityKnown) != 0)
        {
            txt.Text += state.StrIn;
            txt.Text += state.Ct[message.Loc2].Name;
        }
        txt.Text += state.StrNotesEnd;
        Output.DrawFramedBox(0x10, 0x60, 0x108, 0x30, state.CGA ? 4 : 3);
        state.Rp1.BPen = 3;
        Text.DrawWrappedText(0x20, 0x14, 0x64, 0xb);
        Screens.WaitForKey();
    }

    /// <summary>
    /// While the person's score is above 25 per known fact, one more fact becomes known: the organisation or the city
    /// first when they are the building's, else one at random of those not known (gather_intelligence).
    /// </summary>
    /// <returns>What is known of the person, as the caller keeps it.</returns>
    private int RevealRandomFacts(Person person, bool sameOrg, bool sameCity, int known)
    {
        while (known != 0 && (state.PopCount[(known & RevealableFacts) >> 8] + state.PopCount[known & 0xf]) * 25 < person.Intel
            && (known & RevealableFacts) != RevealableFacts)
        {
            if (sameOrg && (known & OrgKnown) == 0)
            {
                known |= OrgKnown;
                break;
            }
            if (sameCity && (known & CityKnown) == 0)
            {
                known |= CityKnown;
                break;
            }
            int fact;
            do
            {
                fact = 1 << GameLibrary.RandomBelow(state.Random, 12);
            }
            while ((fact & known & RevealableFacts) != 0 || (fact & RevealableFacts) == 0);
            known |= fact;
        }
        return known;
    }

    /// <summary>
    /// The hint when nothing new was learned, about a cast member picked at random: a city, an organisation, a face, a
    /// name, or a building of an organisation in a city; shown in the speech balloon when there is one (gather_intelligence).
    /// </summary>
    private void ShowHint()
    {
        var txt = state.MessageText;
        txt.Text = state.StrHintEmpty;
        var p = state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who;
        var person = state.Ps[p];
        state.Picface = -1;
        int org, city;
        switch (GameLibrary.RandomBelow(state.Random, 8 - state.Diff))
        {
            case 0:
                txt.Text = state.Ct[person.Loc].Name;
                state.Active[person.Loc]++;
                break;
            case 1:
                txt.Text = state.Org[person.Org].Name;
                state.OrgCounters[person.Org]++;
                state.Org[person.Org].Clues |= 1;
                break;
            case 2:
                txt.Text = state.StrHintFace;
                state.Picface = person.Face;
                break;
            case 3:
                var clues = person.Clues;
                person.Clues |= NameKnown;
                txt.Text = "";
                AppendPersonName(p);
                person.Clues = clues;
                break;
            case 4:
            case 6:
                org = state.AtLoc;
                city = state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Loc;
                ShowBuildingHint(org, city);
                break;
            case 5:
            case 7:
                org = state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Org;
                city = state.InCity;
                ShowBuildingHint(org, city);
                break;
        }
        if (txt[0] != 0)
        {
            DrawSpeechBox();
            Screens.WaitForKey();
        }
    }

    /// <summary>The hint of a building: when the organisation can be in the city, its building there is not yet known and there is one, it becomes known and is named (gather_intelligence).</summary>
    private void ShowBuildingHint(int org, int city)
    {
        var txt = state.MessageText;
        var plausible = RaidIntelligenceRules.OrgPlausibleInCity(state, state.AtLoc, org, city);
        if (plausible && (state.Ct[city].OrgClues & 1 << org) == 0 && RaidIntelligenceRules.OrgPresenceInCity(state, city, org) != 0)
        {
            state.Ct[city].OrgClues |= (short)(1 << org);
            CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, HintLink, out var count);
            state.Nloc = (short)count;
            state.Org[org].Clues |= 1;
            txt.Text = state.Org[org].Name;
            CampaignLibrary.AppendBuildingType(txt, state.BuildingKinds, (c, o) => RaidIntelligenceRules.OrgPresenceInCity(state, c, o), city, org);
            txt.Text += state.StrHintIn;
            txt.Text += state.Ct[city].Name;
            state.Picface = BuildingPicture;
        }
        else
        {
            txt.Text = "";
        }
    }

    /// <summary>The tries of <see cref="EvidenceFoundText"/>; a find ends them by setting the count far past.</summary>
    private const int EvidenceTries = 9, EvidenceFound = 0x3e7;

    /// <summary>The pieces of equipment a safe can hold that change something besides <see cref="RaidState.Equip"/>.</summary>
    private const int FragmentationGrenades = 8, GasGrenades = 9, StunGrenades = 0xa, FloorPlan = 0xf;

    /// <summary>The most grenades of a kind the player can carry.</summary>
    private const int MaxGrenades = 8;

    /// <summary><see cref="Person.Role"/>: the cast members mapped onto a person.</summary>
    private const int CastMembers = 0x7fff;

    /// <summary>
    /// Searching a safe: up to nine tries at a random find, until one is shown: the city of a known double agent of
    /// the CIA; an organisation's personnel file, or the city a plot object is now in; a piece of equipment (grenades
    /// add two of their kind, the floor plan shows every room on the map); or evidence about the person to arrest, who
    /// is identified as a participant in the plot or, once, can be turned into a double agent.
    /// </summary>
    // orig: TAC 00e8:3573 evidence_found_text (src/c/tac/tac_00e8_3573.c) matched; tests: TacOracleTests
    public void EvidenceFoundText()
    {
        var tries = 0;
        do
        {
            switch (GameLibrary.RandomBelow(state.Random, 5))
            {
                case 4:
                    if (FindDoubleAgent())
                    {
                        tries = EvidenceFound;
                    }
                    break;
                case 0:
                    if (FindOrgFile())
                    {
                        tries = EvidenceFound;
                    }
                    break;
                case 1:
                    if (FindEquipment())
                    {
                        tries = EvidenceFound;
                    }
                    break;
                case 2:
                case 3:
                    if (FindEvidenceOnPerson())
                    {
                        tries = EvidenceFound;
                    }
                    break;
            }
        }
        while (++tries < EvidenceTries);
    }

    /// <summary>The CIA's double agent in this city, or in a random one, when the player knows of one there (evidence_found_text).</summary>
    /// <returns>Whether it was shown.</returns>
    private bool FindDoubleAgent()
    {
        var city = (int)state.InCity;
        if ((state.Doubles & 1 << state.InCity) == 0)
        {
            city = GameLibrary.RandomBelow(state.Random, 0x10);
        }
        if ((state.Doubles & 1 << city) == 0)
        {
            return false;
        }
        var txt = state.MessageText;
        txt.Text = state.StrCiaAgentIn;
        txt.Text += state.Ct[city].Name;
        txt.Text += state.StrIsDoubleAgent;
        ShowMessageBox(txt, 0x14, 0x64);
        return true;
    }

    /// <summary>
    /// The personnel file of the building's organisation (or, for a person to arrest who is in the plot, now and then
    /// of organisation 0) unless the player has it; else where a plot object is now (evidence_found_text).
    /// </summary>
    /// <returns>Whether something was shown.</returns>
    private bool FindOrgFile()
    {
        var org = state.Who != -1 && (state.Ps[state.Who].Role & CastMembers) != 0 && GameLibrary.RandomBelow(state.Random, 3) == 0 ? 0 : (int)state.AtLoc;
        if ((state.InterPol & 1 << org) == 0)
        {
            Dossiers.ShowPersonnelFile(org, true);
            return true;
        }
        var macGuffin = state.Mc[GameLibrary.RandomBelow(state.Random, 4)];
        if (macGuffin.Who == -1)
        {
            return false;
        }
        var txt = state.MessageText;
        txt.Text = macGuffin.Name;
        txt.Text += state.StrNowIn;
        txt.Text += state.Ct[state.Ps[macGuffin.Who].Loc].Name;
        txt.Text += state.StrObjectEnd;
        ShowMessageBox(txt, 0x14, 0x64);
        return true;
    }

    /// <summary>A piece of equipment the player has not got (evidence_found_text).</summary>
    /// <returns>Whether one was found.</returns>
    private bool FindEquipment()
    {
        int item = state.ItemPick[GameLibrary.RandomBelow(state.Random, 8)];
        if ((state.Equip & 1 << item) != 0)
        {
            return false;
        }
        var txt = state.MessageText;
        txt.Text = state.StrYouFind;
        txt.Text += state.ItemNames[item];
        txt.Text += state.StrItemEnd;
        ShowMessageBox(txt, 0x14, 0x50);
        state.Equip |= (short)(1 << item);
        if (item == FloorPlan)
        {
            for (var room = 0; room <= state.Nroom; room++)
            {
                state.Rm[room].Status |= RoomStatus.Visited;
            }
        }
        if (item == FragmentationGrenades)
        {
            state.Grenades[0] = (short)GameLibrary.Clamp(state.Grenades[0] + 2, 0, MaxGrenades);
        }
        if (item == GasGrenades)
        {
            state.Grenades[1] = (short)GameLibrary.Clamp(state.Grenades[1] + 2, 0, MaxGrenades);
        }
        if (item == StunGrenades)
        {
            state.Grenades[2] = (short)GameLibrary.Clamp(state.Grenades[2] + 2, 0, MaxGrenades);
        }
        return true;
    }

    /// <summary>
    /// Evidence about the person to arrest, for a random cast member: if the member is the person's and not yet
    /// known, the person is identified as a participant; if known, the evidence will turn the person (once, and never
    /// person 0) (evidence_found_text).
    /// </summary>
    /// <returns>Whether something was shown.</returns>
    private bool FindEvidenceOnPerson()
    {
        // C: a 16-bit shift by a member number that can pass 15
        var member = C16.Shl(1, GameLibrary.RandomBelow(state.Random, state.NACTIVITY - state.CampaignDiff * 4));
        if (state.Who == -1)
        {
            return false;
        }
        var who = state.Ps[state.Who];
        var turned = (state.ResultBits & RaidResults.Incriminated) != 0;
        if ((who.Role & member) == 0 || turned && (who.Rknown & member) != 0)
        {
            return false;
        }
        var txt = state.MessageText;
        txt[0] = '\0';
        if ((member & who.Rknown) == 0)
        {
            who.Rknown |= member;
            Dossiers.ShowSuspectDossier(state.Who, 0, 0);
            txt.Text = state.StrEvidenceEmpty;
            AppendPersonName(state.Who);
            txt.Text += state.StrParticipant;
        }
        else if (!turned && state.Who != 0)
        {
            Dossiers.ShowSuspectDossier(state.Who, 0, 0);
            txt.Text = state.StrIncriminating;
            AppendPersonName(state.Who);
            txt.Text += state.StrIntoDoubleAgent;
            state.ResultBits |= RaidResults.Incriminated;
        }
        if (txt[0] == '\0')
        {
            return false;
        }
        ShowMessageBox(txt, 0x14, 0x64);
        return true;
    }

    /// <summary>Appends a person's name as the player knows it to the text buffer (the library's <c>append_person_name</c>).</summary>
    private void AppendPersonName(int person) => CampaignLibrary.AppendPersonName(state.MessageText, state.Ps, state.Org, state.Dossier.Names, person);

    /// <summary>Tells the player what is new about a person, in the raid's message boxes (the library's <c>reveal_person_facts</c>).</summary>
    /// <remarks>
    /// The library's <c>paused</c> is, in TAC, the message box's own variable (DS:c5fe <c>menu_pause_mode</c>): the
    /// two models of it are kept the same on the way in, before each box and on the way out.
    /// </remarks>
    private void RevealPersonFacts(int person, int before)
    {
        state.Dossier.Paused = C16.Bool(state.Menu.MenuPauseMode);
        Dossiers.RevealPersonFacts(person, before, (_, x, y) =>
        {
            state.Menu.MenuPauseMode = state.Dossier.Paused != 0;
            ShowMessageBox(state.MessageText, x, y);
            state.Dossier.Paused = C16.Bool(state.Menu.MenuPauseMode);
        });
        state.Menu.MenuPauseMode = state.Dossier.Paused != 0;
    }
    /// <summary><see cref="RaidState.Picface"/>: the balloon shows the picture of a building instead of a face.</summary>
    private const int BuildingPicture = -2;

    /// <summary>The balloon's corner is at x 31, y 49, moved so that it lies at most 146 and 102 pixels left of and above the point it points at, and not right of or below it.</summary>
    private const int BalloonMinX = 0x1f, BalloonMinY = 0x31, BalloonReachX = 0x92, BalloonReachY = 0x66;

    // palette indices
    private const int SpeechShadow = 3, SpeechLetters = 5, SpeechFrame = 0xf, SpeechBackground = 0xb;

    /// <summary>
    /// Draws the speech balloon with the text buffer in it: the balloon picture placed so that the point it points at
    /// (<see cref="RaidState.BalloonX"/>, <see cref="RaidState.BalloonY"/>) lies under its tail, the face of
    /// <see cref="RaidState.Picface"/> in a photo (or the picture of a building for -2), the text in 14 columns with
    /// a shadow one pixel to the right, and white frames around the text and the picture.
    /// </summary>
    // orig: TAC 00e8:4392 draw_speech_box (src/c/tac/tac_00e8_4392.c) matched; tests: TacOracleTests
    public void DrawSpeechBox()
    {
        var x = GameLibrary.Clamp(BalloonMinX, state.BalloonX - BalloonReachX, state.BalloonX);
        var y = GameLibrary.Clamp(BalloonMinY, state.BalloonY - BalloonReachY, state.BalloonY);
        world.Graphics.DrawImagePort(state.Rp1, x, y, state.BalloonImages[RaidState.BalloonBg]);
        if (state.Picface > -1)
        {
            Screens.DrawPortrait(state.Picface, x + 0x42, y + 0x18);
        }
        if (state.Picface == BuildingPicture)
        {
            world.Graphics.DrawImagePort(state.Rp1, x + 0x37, y + 7, state.BalloonImages[RaidState.BalloonPic]);
        }
        state.Rp1.BPen = SpeechBackground;
        Text.DrawWrappedText(0xe, x + 0x14, y + 0x43, SpeechShadow);
        Text.DrawWrappedText(0xe, x + 0x13, y + 0x43, SpeechLetters);
        Output.DrawRectOutline(x + 0xd, y + 0xa, 0x77, 0x4e, SpeechFrame);
        Output.DrawRectOutline(x + 0x39, y + 0x29, 0x1f, 0x10, SpeechFrame);
    }
}
