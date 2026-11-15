using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The library's screens about the persons of a case that several parts show (the menus, the raid and the
/// electronics part): an organisation's personnel file and the MasterMind's master plan, and a suspect's dossier
/// with the messages the player holds about the person. The comment on a method lists the copies it stands for
/// (docs/source-map/shared-functions.csv, under the common name of twin-names.csv).
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="campaign">The part's campaign tables.</param>
/// <param name="textState">The part's variables of the text routines (the message texts' placeholders).</param>
/// <param name="texts">The part's texts and buffers of these screens.</param>
/// <param name="output">The part's drawing routines.</param>
/// <param name="text">The part's text routines.</param>
/// <param name="screens">The part's report screens.</param>
/// <param name="screen">The part's variables of the report screens (its images, the face beside the next box).</param>
/// <param name="random">The part's random numbers.</param>
public sealed class LibraryDossiers(ILibraryState state, ICampaignTables campaign, ILibraryTextState textState, DossierTexts texts,
    LibraryOutput output, LibraryText text, LibraryScreens screens, ILibraryScreenState screen, IRandomSource random)
{
    // palette indices
    private const int BlueGrey = 5, Grey = 7, Red = 4, LightRed = 0xc, Yellow = 0xe, White = 0xf;

    /// <summary>The field colour of the personnel file and the dossier screens.</summary>
    private const int PersonnelFileField = 9, DossierField = 3;

    /// <summary><see cref="Core.Records.Activity.Status"/>: the cast member is the MasterMind.</summary>
    private const int CastMasterMind = 0x100;

    /// <summary><see cref="Core.Records.Person.Role"/>: the cast members mapped onto the person.</summary>
    private const int CastMembers = 0x7fff;

    /// <summary><see cref="Core.Records.Person.Clues"/>: what the player knows of a person.</summary>
    private const int PhotoKnown = 0x01, OrgKnown = 0x04, CityKnown = 0x08, FiveFacts = 0x1f, PhotoForgotten = 0x80, RankKnown = 0x100,
        NearestCityKnown = 0x200, RecruiterKnown = 0x400;

    /// <summary>Organisation, city and the person's place in it: what the personnel file tells of each member it lists.</summary>
    private const int MemberFacts = OrgKnown | CityKnown | 0x02;

    /// <summary><see cref="Core.Records.Person.Alert"/>: arrested, in hiding, turned.</summary>
    private const int Arrested = -1, InHiding = -2, Turned = -3;

    /// <summary><see cref="Core.Records.Synopsis.Clues"/>: the message was decoded.</summary>
    private const int Decoded = 0x8000;

    /// <summary><see cref="Core.Records.Event.Status"/>: the kind of an event's message (bits 8..11).</summary>
    private const int MessageKind = 0xf00, CodedA = 0x200, CodedB = 0x300, PartlyKnownA = 0x800, PartlyKnownB = 0x900;

    /// <summary>The placeholders <see cref="LoadEventText"/> fills: $VICTIM, $SNDORG, $SNDLOC, $OBJECT.</summary>
    private const int VictimValue = 0, SenderOrgValue = 1, SenderCityValue = 3, ObjectValue = 6;

    /// <summary>How many of a message's words the mask of <see cref="LibraryTexts.KeepMarkedWords"/> covers.</summary>
    private const int MessageWordPeriod = 5;

    /// <summary>The line at which a dossier's page is full.</summary>
    private const int PageBottom = 0xc0;

    /// <summary>The image of the paper clip over a dossier's photo.</summary>
    private const int PhotoClip = 0x6c;

    /// <summary>
    /// Shows an organisation's personnel file (the members of the case's action team in it, with their cities), or
    /// for organisation 0 the MasterMind's master plan (the photos and roles of the cast), and waits for a key. The
    /// organisation's bit is set in <see cref="ICampaignTables.InterPol"/>.
    /// </summary>
    /// <param name="org">The organisation, 0..15.</param>
    /// <param name="revealMembers">Whether the player learns name, organisation and city of every member listed.</param>
    // orig: BUG 00e8:4c22 show_personnel_file (src/c/bug/bug_00e8_4c22.c) matched; also FINAL 0100:85ec, TAC 00e8:b4a6; tests: LibraryTextOracleTests
    // Other code under the same name: GAME 00f7:0978 (docs/source-map/shared-functions.csv).
    public void ShowPersonnelFile(int org, bool revealMembers)
    {
        if (org == 0)
        {
            ShowMasterPlan();
        }
        else
        {
            ShowActionTeam(org, revealMembers);
        }
        screens.WaitForKey();
    }

    /// <summary>The members of an organisation's action team, one line each.</summary>
    private void ShowActionTeam(int org, bool revealMembers)
    {
        const int left = 0x50, title = 0x10;
        var txt = state.MessageText;
        // C: a shift by a variable count, as the 8086 takes it
        campaign.InterPol |= C16.Shl((short)1, org);
        screens.DrawScreenFrame(texts.PersonnelFile, PersonnelFileField);
        txt.Text = campaign.Orgs[org].Name;
        txt.Text += texts.ActionTeam;
        output.DrawTextFlag0(txt.Text, left, title, BlueGrey);
        var y = 0x20;
        var none = true;
        // the MasterMind's record, 0, is not listed
        for (var person = 1; person < campaign.Nperson; person++)
        {
            var member = campaign.Ps[person];
            if (member.Org != org || (member.Role & CastMembers) == 0)
            {
                continue;
            }
            if (revealMembers)
            {
                member.Clues |= MemberFacts;
            }
            txt.Truncate(0);
            CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
            if ((member.Clues & CityKnown) != 0)
            {
                txt.Text += texts.Comma;
                txt.Text += campaign.Ct[member.Loc].Name;
            }
            output.DrawTextFlag0(txt.Text, left, y, LightRed);
            y += 8;
            none = false;
        }
        if (none)
        {
            output.DrawTextFlag0(texts.NoActivity, left, y, LightRed);
        }
    }

    /// <summary>The MasterMind's master plan: the photo, state and role of every real cast member but the MasterMind, four to a row.</summary>
    private void ShowMasterPlan()
    {
        const int secretTimes = 5, column = 0x48, firstColumn = 0x1c, rowStep = 0x50;
        var txt = state.MessageText;
        campaign.InterPol |= 1;
        screens.DrawScreenFrame(texts.MasterPlan, PersonnelFileField);
        var y = 0x20;
        txt.Truncate(0);
        for (var i = 0; i < secretTimes; i++)
        {
            txt.Text += texts.Secret;
        }
        output.DrawTextFlag0(txt.Text, 0xc, 0x10, Yellow);
        output.DrawTextFlag0(txt.Text, 0xc, 0xbc, Yellow);
        // the decoys (the last 4 * difficulty members of the cast) are not part of the plan
        for (var member = 0; campaign.NACTIVITY - campaign.Diff * 4 > member; member++)
        {
            var cast = campaign.Ac[member];
            if ((cast.Status & CastMasterMind) != 0)
            {
                continue;
            }
            var x = (member & 3) * column + firstColumn;
            if (member == 4)
            {
                y += rowStep;
            }
            var person = campaign.Ps[cast.Who];
            if ((person.Clues & (PhotoKnown | PhotoForgotten)) != PhotoForgotten)
            {
                screens.DrawPortrait(person.Face, x, y);
            }
            txt.Truncate(0);
            switch (person.Alert)
            {
                case Arrested:
                    txt.Text = texts.Arrested;
                    break;
                case InHiding:
                    txt.Text = texts.InHiding;
                    break;
                case Turned:
                    txt.Text = texts.Turned;
                    break;
            }
            // a shadow, then the letters
            output.DrawTextFlag0(txt.Text, x - 1, y + 0x10, BlueGrey);
            output.DrawTextFlag0(txt.Text, x - 2, y + 0x10, LightRed);
            txt.Text = cast.Name;
            text.DrawTextCentered(txt.Text, x + 0x19, y + 0x38, LightRed);
            // the photos of a row stand alternately 8 pixels higher and lower
            y ^= 8;
        }
    }

    /// <summary>
    /// Fills the message placeholders for an event (the case's objects, the organisation and city of the person
    /// behind the event's cast member) and appends the event's message text to the text buffer.
    /// </summary>
    /// <param name="eventNo">The event (<c>ev</c>), or a negative message key of a message every case has.</param>
    // orig: BUG 00e8:3fc8 load_event_text (src/c/bug/bug_00e8_3fc8.c) matched; also TAC 00e8:a84c; tests: LibraryTextOracleTests
    // FINAL's show_event_message is the same code, ported for now as Headquarters.ShowEventMessage: to be replaced by this method.
    // C: the original reads the event's record also for a negative key (the bytes in front of the table); the C# throws
    // there. Play never passes one: the only caller is the dossier's message list, whose messages name the person
    // (Who), and only a message of an event does (queue_message, add_msg_record leave Who at -1 for the other kinds),
    // with the event's index as its number; TEXT.DTA has no texts for negative keys ("*MSG99nn") either.
    public void LoadEventText(int eventNo)
    {
        var values = textState.VariableValues;
        values[VictimValue] = campaign.Pl.Object.Text;
        values[ObjectValue] = campaign.Pl.Object2.Text;
        var who = campaign.Ps[campaign.Ac[campaign.Ev[eventNo].Act].Who];
        values[SenderOrgValue] = campaign.Orgs[who.Org].Name;
        values[SenderCityValue] = campaign.Ct[who.Loc].Name;
        LibraryTexts.LoadMessageText(texts.MessageTag, texts.MessageTemplate, campaign.TheCrime, eventNo >= 0 ? campaign.Ev[eventNo].Synch : eventNo);
        text.ReadTextWithVars(texts.MessageFile, texts.MessageTag.Text);
    }

    /// <summary>
    /// Shows a suspect's dossier: name, rank, organisation and city as far as the player knows them, who recruited the
    /// person and near which city, the photo, the roles in the case or "unknown", arrest, hiding or turning, and below
    /// it the messages the player holds about the person (decoded ones with their text, partly known ones with the
    /// words known), page after page. With <paramref name="y"/> 0 the dossier fills the screen; otherwise a compact
    /// form without photo and messages is drawn into another screen.
    /// </summary>
    /// <param name="person">The person, 0..63.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge of the compact form; 0 for the full screen.</param>
    // orig: BUG 00e8:31b8 show_suspect_dossier (src/c/bug/bug_00e8_31b8.c) matched; also FINAL 0100:6b82, TAC 00e8:9a3c; tests: LibraryTextOracleTests
    public void ShowSuspectDossier(int person, int x, int y)
    {
        var top = y;
        var compact = top != 0;
        var txt = state.MessageText;
        var who = campaign.Ps[person];
        if (compact)
        {
            x += 4;
        }
        else
        {
            DrawDossierFrame(person);
            y = 0x12;
            x += 0xa;
        }
        y += 2;
        txt.Text = texts.Name;
        CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
        output.DrawTextFlag0(txt.Text, x, y, Grey);
        if ((who.Clues & RankKnown) != 0)
        {
            var rank = who.Face >> 2 & 7;
            for (var member = 0; member < campaign.NACTIVITY; member++)
            {
                if (campaign.Ac[member].Who == person)
                {
                    rank = campaign.Ac[member].Rank;
                }
            }
            txt.Text = texts.Rank;
            txt.Text += texts.RankNames[rank];
            y += 8;
            output.DrawTextFlag0(txt.Text, x, y, Grey);
        }
        if ((who.Clues & OrgKnown) != 0)
        {
            campaign.Orgs[who.Org].Clues |= 1;
            txt.Text = texts.Org;
            txt.Text += campaign.Orgs[who.Org].Name;
            y += 8;
            output.DrawTextFlag0(txt.Text, x, y, Grey);
        }
        if ((who.Clues & CityKnown) != 0)
        {
            txt.Text = texts.City;
            txt.Text += campaign.Ct[who.Loc].Name;
            y += 8;
            output.DrawTextFlag0(txt.Text, x, y, Grey);
        }
        AppendRecruitment(person);
        // "Recruited" alone is not shown
        const int bareRecruited = 0xc;
        if (txt.Length > bareRecruited && !compact)
        {
            txt.Text += texts.Period;
            y += 8;
            output.DrawTextFlag0(txt.Text, x, y, Grey);
        }
        if ((who.Clues & PhotoKnown) != 0 && !compact)
        {
            screens.DrawPortrait(who.Face, 0x108, 8);
            output.DrawImage(screen.Sprite, PhotoClip, 0x12b, 0x28);
        }
        y = DrawRoles(person, x, y + 0xa);
        y += 2;
        // the messages start below the photo, and no lower than line 99
        const int photoHeight = 0x36, lowestStart = 0x63;
        y = GameLibrary.Clamp(y + 8, (who.Clues & PhotoKnown) != 0 ? top + photoHeight : 0, lowestStart);
        ListMessages(person, x, y, compact);
    }

    /// <summary>Clears the screen to the dossier's frame, titled with the person's name.</summary>
    private void DrawDossierFrame(int person)
    {
        var txt = state.MessageText;
        txt.Truncate(0);
        CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
        screens.DrawScreenFrame(txt.Text, DossierField);
    }

    /// <summary>Writes "Recruited", then by whom (the first cast member of the person's organisation) and near which city, as far as the player knows.</summary>
    private void AppendRecruitment(int person)
    {
        const int noCity = -1, farAway = 999;
        var txt = state.MessageText;
        var who = campaign.Ps[person];
        txt.Text = texts.Recruited;
        if ((who.Clues & RecruiterKnown) != 0)
        {
            for (var member = 0; member < campaign.NACTIVITY; member++)
            {
                var recruiter = campaign.Ac[member].Who;
                if (who.Org != campaign.Ps[recruiter].Org)
                {
                    continue;
                }
                if (person != recruiter)
                {
                    txt.Text += texts.By;
                    CampaignLibrary.AppendRealName(txt, campaign.Ps, campaign.Orgs, texts.Names, recruiter);
                }
                break;
            }
        }
        if ((who.Clues & NearestCityKnown) != 0)
        {
            var nearest = noCity;
            var best = farAway;
            for (var city = 0; city < campaign.Ct.Count; city++)
            {
                if (city != who.Loc && GameLibrary.CityDistance(campaign.Ct, city, who.Loc) < best)
                {
                    nearest = city;
                    best = GameLibrary.CityDistance(campaign.Ct, city, who.Loc);
                }
            }
            if (nearest != noCity)
            {
                txt.Text += texts.In;
                txt.Text += campaign.Ct[nearest].Name;
            }
        }
    }

    /// <summary>Draws the person's roles (or "NOT INVOLVED", "unknown") between two lines, and the arrest, hiding or turning; returns the last line's top.</summary>
    private int DrawRoles(int person, int x, int y)
    {
        const int ruleWidth = 0x80;
        var txt = state.MessageText;
        var who = campaign.Ps[person];
        output.DrawLine(x, y - 2, x + ruleWidth, y - 2, LightRed);
        var shown = false;
        for (var member = 0; member < campaign.NACTIVITY; member++)
        {
            // C: a shift by a variable count, as the 8086 takes it
            if ((who.Rknown & C16.Shl((short)1, member)) != 0)
            {
                txt.Text = texts.Role;
                txt.Text += campaign.Ac[member].Name;
                output.DrawTextFlag0(txt.Text, x, y, Grey);
                shown = true;
            }
        }
        if (!shown)
        {
            txt.Text = (who.Clues & FiveFacts) == FiveFacts && (who.Role & CastMembers) == 0 ? texts.RoleNotInvolved : texts.RoleUnknown;
            output.DrawTextFlag0(txt.Text, x, y, Grey);
        }
        foreach (var (alert, note) in new[] { (Arrested, texts.UnderArrest), (InHiding, texts.Hiding), (Turned, texts.TurnedNote) })
        {
            if (who.Alert == alert)
            {
                txt.Text = note;
                y += 8;
                output.DrawTextFlag0(txt.Text, x, y, BlueGrey);
            }
        }
        output.DrawLine(x, y + 8, x + ruleWidth, y + 8, LightRed);
        return y;
    }

    /// <summary>
    /// Lists the messages about the person that the player holds: each as its log line, then a decoded message's text
    /// in quotes, a partly known one's known words, or "(message not decoded)". A full page waits for a key and starts
    /// a new one with the same message. The compact form lists nothing.
    /// </summary>
    private void ListMessages(int person, int x, int y, bool compact)
    {
        const int logColumns = 0x25, textColumns = 0x24;
        var txt = state.MessageText;
        var msg = 0;
        if (!NextMessage(person, compact, ref msg, advance: false))
        {
            return;
        }
        while (true)
        {
            txt.Truncate(0);
            CampaignLibrary.AppendMsgLogEntry(txt, campaign, texts.Names, texts.Words, texts.NumberText, msg);
            y = text.DrawWrappedText(logColumns, x, y, BlueGrey);
            {
                var message = campaign.Sn[msg];
                int eventNo = message.Data;
                if ((message.Clues & Decoded) != 0)
                {
                    txt.Text = texts.Quote;
                    LoadEventText(eventNo);
                    txt.Text += texts.Quote;
                    y = text.DrawWrappedText(textColumns, x + 4, y, LightRed);
                }
                else
                {
                    // C: the event's record is read also for a message without one (a negative number); the C# throws
                    // there. Play never gets there: a message that names the person is always a message of an event,
                    // numbered with the event's index (see LoadEventText)
                    var kind = campaign.Ev[eventNo].Status & MessageKind;
                    if (kind is CodedA or CodedB)
                    {
                        output.DrawText(texts.NotDecoded, x + 4, y, Red);
                        y += 8;
                    }
                    if (kind is PartlyKnownA or PartlyKnownB)
                    {
                        txt.Truncate(0);
                        LoadEventText(eventNo);
                        LibraryTexts.KeepMarkedWords(txt, message.Clues, MessageWordPeriod, texts.Ellipsis);
                        y = text.DrawWrappedText(textColumns, x + 4, y, LightRed);
                    }
                }
            }
            if (y >= PageBottom)
            {
                output.DrawText(texts.More, 0x100, PageBottom, LightRed);
                screens.WaitForKey();
                DrawDossierFrame(person);
                y = 0x10;
                continue;
            }
            if (!NextMessage(person, compact, ref msg, advance: true))
            {
                return;
            }
        }
    }

    /// <summary>The next message about the person the player holds (from <paramref name="msg"/> itself, or the one after it); never one in the compact form.</summary>
    private bool NextMessage(int person, bool compact, ref int msg, bool advance)
    {
        for (msg += advance ? 1 : 0; msg < campaign.NextSyn; msg++)
        {
            var message = campaign.Sn[msg];
            if (message.Who == person && message.Clues != 0 && !compact)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary><see cref="Core.Records.Event.Status"/>: the event is news (a bulletin everyone hears of).</summary>
    private const int NewsEvent = 0x2000;

    /// <summary><see cref="Core.Records.Event.Status"/>: the message was sent (rather than a meeting held).</summary>
    private const int SentMessage = 0x200;

    /// <summary><see cref="Core.Records.Event.Status"/>: the event's partner, a cast member (bits 0..3).</summary>
    private const int EventPartner = 0xf;

    /// <summary><see cref="Core.Records.Synopsis.Type"/>: an unused message record; an event's message.</summary>
    private const int UnusedRecord = -1, EventMessage = 0;

    /// <summary>The field colour of the chronology's screen.</summary>
    private const int ChronologyField = 0xe;

    /// <summary>Where a time's date starts in the text <c>format_game_time</c> gives ("hh:mm AM Mon dd").</summary>
    private const int DateInClockText = 9;

    /// <summary>
    /// Shows the crime chronology (the events the player has decoded or noted: who sent a message to whom or met whom,
    /// with the message's text or its known words) or the news (the bulletins, each with its date), page after page,
    /// "none" when there is nothing, and waits for a key.
    /// </summary>
    /// <param name="chronology">True for the chronology, false for the news.</param>
    /// <param name="formatGameTime">The part's <c>format_game_time</c>: a moment of the case as "hh:mm AM Mon dd".</param>
    /// <param name="describeEvent">The part's routine that appends an event's message to the text buffer.</param>
    // orig: GAME 00f7:790c show_chronology (src/c/game/game_00f7_790c.c) matched; also FINAL 0100:8d04; tests: LibraryModuleOracleTests
    public void ShowChronology(bool chronology, Func<int, string> formatGameTime, Action<int> describeEvent)
    {
        const int left = 8, textLeft = 0xc, textColumns = 0x24, firstLine = 0x10;
        var txt = state.MessageText;
        screens.DrawScreenFrame(chronology ? texts.ChronologyTitle : texts.NewsTitle, ChronologyField);
        var y = firstLine;
        for (var msg = 0; msg < campaign.NextSyn; msg++)
        {
            var message = campaign.Sn[msg];
            if (message.Type == UnusedRecord)
            {
                continue;
            }
            while (true)
            {
                if (message.Type == EventMessage)
                {
                    var happening = campaign.Ev[message.Data];
                    var kind = happening.Status & MessageKind;
                    if ((happening.Status & NewsEvent) != 0)
                    {
                        output.DrawTextFlag0(formatGameTime(message.Time)[DateInClockText..], left, y, Grey);
                        y += 8;
                        txt.Truncate(0);
                        describeEvent(message.Data);
                        y = text.DrawWrappedText(textColumns, textLeft, y, 0);
                    }
                    else if (message.Clues != 0 && chronology)
                    {
                        if ((message.Clues & Decoded) != 0 && kind is CodedA or PartlyKnownA)
                        {
                            txt.Text = campaign.Ac[happening.Act].Name;
                            txt.Text += (happening.Status & SentMessage) != 0 ? texts.SentMsgTo : texts.MetWith;
                            txt.Text += campaign.Ac[happening.Status & EventPartner].Name;
                            output.DrawTextFlag0(txt.Text, left, y, Grey);
                            y += 8;
                            txt.Text = texts.NewsQuoteOpen;
                            describeEvent(message.Data);
                            txt.Text += texts.NewsQuoteClose;
                            y = text.DrawWrappedText(textColumns, textLeft, y, LightRed);
                        }
                        else if (kind == PartlyKnownA)
                        {
                            output.DrawTextFlag0(texts.MeetingNoted, left, y, Grey);
                            y += 8;
                            txt.Truncate(0);
                            describeEvent(message.Data);
                            LibraryTexts.KeepMarkedWords(txt, message.Clues, MessageWordPeriod, texts.Ellipsis);
                            y = text.DrawWrappedText(textColumns, textLeft, y, LightRed);
                        }
                    }
                }
                if (y < PageBottom)
                {
                    break;
                }
                // a full page: the same message again on a new one
                output.DrawText(texts.ChronologyMore, 0x100, PageBottom, LightRed);
                screens.WaitForKey();
                screens.DrawScreenFrame(chronology ? texts.ChronologyHeader : texts.NewsHeader, ChronologyField);
                y = firstLine;
            }
        }
        if (y == firstLine)
        {
            output.DrawText(texts.NoEntries, textLeft, y, Grey);
        }
        screens.WaitForKey();
    }

    /// <summary>The kinds of a clue's subject (its high byte): a photo, a person, an organisation, a city, a cast member.</summary>
    private const int SubjectFace = 1, SubjectPerson = 2, SubjectOrg = 4, SubjectCity = 8, SubjectCastMember = 0x10;

    /// <summary>Where the digits stand in a clue's tag ("*C" then item kind, subject kind, ...).</summary>
    private const int TagItemKind = 2, TagSubjectKind = 3, TagVariant = 5;

    /// <summary>The placeholders of a clue text: $KEY, the person's name, the organisation, the city (values 0..3; five in all).</summary>
    private const int KeyValue = 0, PersonValue = 1, OrgValue = 2, CityValue = 3, ClueVariables = 5;

    /// <summary>The sizes of a clue's tag buffer on the original's stack.</summary>
    private const int ClueTagCapacity = 8;

    /// <summary>
    /// Appends a clue's sentence to the text buffer from CLUES.TXT: the entry under a tag made of the kind of the clue's
    /// item and the kind of its subject (for a cast member: of the case and the member, or of the decoy's face), with the
    /// item's label, the person's name, the organisation's or the city's name filled in. The entry's first character (two
    /// for a cast member) is removed; its low four bits are the clue's source, kept in <see cref="DossierTexts.ClueMethod"/>.
    /// A photo clue sets the face shown beside the box.
    /// </summary>
    /// <param name="subject">The clue's subject word (kind in the high byte, index in the low byte).</param>
    /// <param name="key">The clue's item key.</param>
    /// <param name="clue">The clue, 0..79 (its cast member is read for a cast member subject).</param>
    // orig: BUG 00e8:2fe9 expand_clue_text (src/c/bug/bug_00e8_2fe9.c) matched; also FINAL 0100:69b3, TAC 00e8:986d; tests: LibraryTextOracleTests
    public void ExpandClueText(int subject, int key, int clue)
    {
        var txt = state.MessageText;
        var values = textState.VariableValues;
        var index = subject & 0xff;
        var tag = new TextBuffer(ClueTagCapacity) { Text = texts.ClueTagBasic };
        AddToDigit(tag, TagItemKind, key >> 8 & 0xf);
        for (var kind = C16.Sar((short)subject, 8); kind > 1; kind >>= 1)
        {
            AddToDigit(tag, TagSubjectKind, 1);
        }
        var start = txt.Length;
        CampaignLibrary.AppendKeyText(txt, texts.ClueLabels, texts.NumberText, texts.Hundreds, texts.Thousands, key);
        values[KeyValue] = txt.Text[start..];
        txt.Truncate(start);
        var strip = 1;
        switch (C16.Sar((short)subject, 8))
        {
            case SubjectFace:
                screen.Picface = campaign.Ps[index].Face;
                break;
            case SubjectPerson:
                CampaignLibrary.AppendRealName(txt, campaign.Ps, campaign.Orgs, texts.Names, index);
                values[PersonValue] = txt.Text[start..];
                txt.Truncate(start);
                break;
            case SubjectOrg:
                values[OrgValue] = campaign.Orgs[index].Name;
                break;
            case SubjectCity:
                values[CityValue] = campaign.Ct[index].Name;
                break;
            case SubjectCastMember:
                strip = 2;
                var member = campaign.Cl[clue].Act;
                if (member < campaign.NACTIVITY - campaign.Diff * 4)
                {
                    // a real member: the case's own text for the member
                    tag.Text = texts.ClueTagCase;
                    AddToDigit(tag, TagVariant, index);
                    AddToDigit(tag, TagSubjectKind, C16.Rem(campaign.TheCrime, 10));
                    AddToDigit(tag, TagItemKind, C16.Div(campaign.TheCrime, 10));
                }
                else
                {
                    // a decoy: a text picked by the face of the person behind the member
                    var face = campaign.Ps[campaign.Ac[member].Who].Face;
                    tag.Text = texts.ClueTagCast;
                    AddToDigit(tag, TagVariant, C16.Rem(face, 6));
                    AddToDigit(tag, TagSubjectKind, C16.Rem(C16.Sar(face, 3), 4));
                }
                break;
        }
        start = txt.Length;
        text.ReadClearedText(texts.CluesFile, tag.Text, texts.ClueTokens, [.. values.Take(ClueVariables)], ClueVariables);
        texts.ClueMethod = (short)(txt[start] & 0xf);
        // C: strcpy over itself, from the character behind the stripped ones up to the next end mark
        var rest = TextFrom(txt, start + strip);
        txt.Truncate(start);
        txt.Text += rest;
    }

    /// <summary>Adds a number to the character at a position of a tag, keeping the low byte as the original's char does.</summary>
    private static void AddToDigit(TextBuffer tag, int position, int value) => tag[position] = (char)C16.UChar(tag[position] + value);

    /// <summary>The characters of a buffer from a position up to the next end mark, as the original's pointer into the buffer reads them.</summary>
    private static string TextFrom(TextBuffer buffer, int start)
    {
        var end = Array.IndexOf(buffer.Bytes, (byte)0, start);
        if (end < 0)
        {
            throw new MemoryOverrunException($"no end mark behind position {start} of a text of {buffer.Bytes.Length} bytes");
        }
        return System.Text.Encoding.Latin1.GetString(buffer.Bytes, start, end - start);
    }

    /// <summary>The kind of clue that is a photo: the report shows the face.</summary>
    private const int PhotoClue = 1;

    /// <summary>The clues table holds 80 records; a record's cast member -1 marks it free.</summary>
    private const int ClueCount = 80, FreeClue = -1;

    /// <summary>The first of the images of the clues' sources, shown on the report.</summary>
    private const int MethodImages = 0x60;

    /// <summary>
    /// The code of a cast member's clue item: the member's key (half of it, or its low seven bits for
    /// <paramref name="whole"/>), and in the high byte the kind of item, for a real member its own, for a decoy the
    /// first character of a CLUES.TXT entry picked by the face of the person behind it.
    /// </summary>
    /// <param name="member">The cast member.</param>
    /// <param name="whole">Whether the key's low seven bits are taken (rather than half the key).</param>
    /// <returns>The item key.</returns>
    // orig: BUG 00e8:2d22 lookup_clue_text (src/c/bug/bug_00e8_2d22.c) matched; also FINAL 0100:66ec, TAC 00e8:95a6; tests: LibraryTextOracleTests
    public int LookupClueText(int member, bool whole)
    {
        var cast = campaign.Ac[member];
        var number = cast.Key & 0xff;
        number = whole ? number & 0x7f : number >> 1;
        if (campaign.NACTIVITY - (campaign.Diff << 2) > member)
        {
            return ((cast.Ctype & 0xff) << 8) + number;
        }
        var face = campaign.Ps[cast.Who].Face;
        var tag = new TextBuffer(ClueTagCapacity) { Text = texts.ClueTag };
        AddToDigit(tag, TagVariant, C16.Rem(face, 6));
        AddToDigit(tag, TagSubjectKind, C16.Rem(C16.Sar(face, 3), 4));
        var txt = state.MessageText;
        var start = txt.Length;
        text.ReadClearedText(texts.CluesFile, tag.Text, texts.ClueTokens, [.. textState.VariableValues.Take(ClueVariables)], ClueVariables);
        // the entry's text is not kept; its first character, the kind of item, stays behind the end mark
        txt.Truncate(start);
        return ((txt[start + 1] & 0xf) << 8) + number;
    }

    /// <summary>
    /// Appends a clue's subject to the text buffer: a city's or an organisation's name (the organisation becomes known),
    /// a person's real name, "this face." (the person's face is then shown beside the box), or the code name of the
    /// clue's cast member (one for a real member, another for a decoy).
    /// </summary>
    /// <param name="clue">The clue, 0..79.</param>
    /// <param name="subject">The subject word (kind in the high byte, index in the low byte).</param>
    // orig: BUG 00e8:2e8f append_subject_text (src/c/bug/bug_00e8_2e8f.c) matched; also FINAL 0100:6859, TAC 00e8:9713; tests: LibraryTextOracleTests
    // GAME's append_clue_subject is other code (CampaignTexts.AppendClueSubject).
    public void AppendSubjectText(int clue, int subject)
    {
        var txt = state.MessageText;
        var index = subject & 0xff;
        switch (C16.Sar((short)subject, 8))
        {
            case SubjectCity:
                txt.Text += campaign.Ct[index].Name;
                break;
            case SubjectOrg:
                txt.Text += campaign.Orgs[index].Name;
                campaign.Orgs[index].Clues |= 1;
                break;
            case SubjectPerson:
                CampaignLibrary.AppendRealName(txt, campaign.Ps, campaign.Orgs, texts.Names, index);
                break;
            case SubjectFace:
                txt.Text += texts.ThisFace;
                // the original also returns the face; its callers do not use it
                screen.Picface = campaign.Ps[index].Face;
                break;
            case SubjectCastMember:
                txt.Text += campaign.Cl[clue].Act < campaign.NACTIVITY - (campaign.Diff << 2) ? texts.CodeNameReal : texts.CodeNameDecoy;
                break;
        }
    }

    /// <summary>
    /// Shows one clue the player has not had yet about a cast member, of a kind picked at random (at the lowest level the
    /// cast member itself first; at the two lowest the city of a member of type 2 first), as a clue report.
    /// </summary>
    /// <param name="member">The cast member.</param>
    /// <param name="org">The organisation where the clue was found.</param>
    /// <param name="city">The city where it was found.</param>
    /// <param name="showClueReport">The part's clue report, where it is not the library's (the campaign has its own); null for <see cref="ShowClueReport"/>.</param>
    /// <returns>Whether a clue was shown (false when every kind was had).</returns>
    // orig: BUG 00e8:282e reveal_cast_clue (src/c/bug/bug_00e8_282e.c) matched; also FINAL 0100:61f8, TAC 00e8:90b2, GAME 00f7:4f52; tests: LibraryTextOracleTests
    public bool RevealCastClue(int member, int org, int city, Action<int, int, int, int>? showClueReport = null)
    {
        const int allKinds = 0x1f, cityKind = 8, castKind = 0x10, kinds = 5, cityFirstType = 2, cityFirstLevels = 2;
        var cast = campaign.Ac[member];
        if ((cast.Clued & allKinds) == allKinds)
        {
            return false;
        }
        int kind;
        do
        {
            // C: the draw is narrowed to a char before the shift
            kind = C16.Shl((short)1, (sbyte)GameLibrary.RandomBelow(random, kinds));
            if (campaign.Diff == 0 && (cast.Clued & castKind) == 0)
            {
                kind = castKind;
            }
            if (cast.Ctype != cityFirstType || campaign.Diff >= cityFirstLevels || (cast.Clued & cityKind) != 0)
            {
                continue;
            }
            kind = cityKind;
        }
        while ((cast.Clued & kind) != 0);
        (showClueReport ?? ShowClueReport)(org, city, member, kind);
        return true;
    }

    /// <summary>
    /// Records a new clue about a cast member in the first free record of the clue table and shows its report: where it
    /// was found, its sentence and source, the photo for a photo clue, and the related clues (those with the same item,
    /// then those with the same subject) on panels below; then waits for a key. Nothing when the member's clue of this
    /// kind was had already, or the table is full.
    /// </summary>
    /// <param name="org">The organisation where the clue was found.</param>
    /// <param name="city">The city where it was found.</param>
    /// <param name="member">The cast member.</param>
    /// <param name="method">The kind of clue (one of the five bits; higher bits count from bit 5).</param>
    // orig: BUG 00e8:2911 show_clue_report (src/c/bug/bug_00e8_2911.c) matched; also FINAL 0100:62db, TAC 00e8:9195; tests: LibraryTextOracleTests
    public void ShowClueReport(int org, int city, int member, int method)
    {
        const int reportField = 9, firstPanel = 0x4c, lastPanel = 0xb4, firstPhoto = 0x49, photoStep = 0x28;
        var cast = campaign.Ac[member];
        if ((cast.Clued & method) != 0)
        {
            return;
        }
        for (var clue = 0; clue < ClueCount; clue++)
        {
            var record = campaign.Cl[clue];
            if (record.Act != FreeClue)
            {
                continue;
            }
            // C: the organisation and the city arrive as chars
            record.Sorg = (sbyte)org;
            record.Sloc = (sbyte)city;
            record.Act = (short)member;
            if (method > 0x1f)
            {
                method >>= 5;
            }
            var txt = state.MessageText;
            record.Info1 = (short)CampaignLibrary.ClueTargetCode(txt, campaign, clue, method);
            record.Key1 = (short)LookupClueText(member, whole: false);
            cast.Clued |= (short)method;
            screens.DrawScreenFrame(texts.ClueTitle, reportField);
            state.Rp1.BPen = White;
            txt.Text = texts.Source;
            txt.Text += campaign.Orgs[record.Sorg].Name;
            txt.Text += texts.Slash;
            txt.Text += campaign.Ct[record.Sloc].Name;
            txt.Text += texts.ReportNewline;
            CampaignLibrary.ActivityClueText(campaign.Cl, clue, ExpandClueText);
            // a clue found in an enemy's building came by photo or wiretap
            if (record.Sorg > 1)
            {
                texts.ClueMethod = (short)(texts.CodedFlag == 1 ? 1 : 0);
            }
            txt.Text += texts.MethodLabel;
            txt.Text += texts.ClueMethods[texts.ClueMethod];
            text.DrawWrappedText(0x1f, 0x10, 0x11, BlueGrey);
            if (method == PhotoClue)
            {
                screens.DrawPortrait(screen.Picface, 0x108, 0x12);
                output.DrawImage(screen.Sprite, PhotoClip, 0x12b, 0x2e);
            }
            else
            {
                output.DrawImage(screen.Sprite, texts.ClueMethod + MethodImages, 0x110, 0x10);
            }
            output.DrawTextFlag0(texts.RelatedClues, 0x10, 0x42, BlueGrey);
            var y = firstPanel;
            var photoY = firstPhoto;
            var any = false;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var other = 0; other < ClueCount; other++)
                {
                    var related = campaign.Cl[other];
                    if (related.Act == FreeClue || clue == other)
                    {
                        continue;
                    }
                    if (y > lastPanel)
                    {
                        break;
                    }
                    // the first pass lists the clues with the same item, the second those with the same subject
                    if (!(pass != 0 && record.Info1 == related.Info1 || pass == 0 && record.Key1 == related.Key1))
                    {
                        continue;
                    }
                    txt.Truncate(0);
                    var x = GameLibrary.RandomBelow(random, 0xc) + 4;
                    screens.DrawPanel(x, y - 2);
                    if (C16.Sar(related.Info1, 8) == SubjectCastMember)
                    {
                        CampaignLibrary.ActivityClueText(campaign.Cl, other, ExpandClueText);
                    }
                    else
                    {
                        CampaignLibrary.AppendActivityText(txt, campaign.Cl, texts.ActivityPhrases, texts.ActivityPeriod, other,
                            key => CampaignLibrary.AppendKeyText(txt, texts.ClueLabels, texts.NumberText, texts.Hundreds, texts.Thousands, key), AppendSubjectText);
                    }
                    txt.Text += texts.ReportNewline;
                    any = true;
                    y = text.DrawWrappedText(0x20, x + 8, y, 0) + 4;
                    if (C16.Sar(related.Info1, 8) == PhotoClue)
                    {
                        screens.DrawPortrait(campaign.Ps[campaign.Ac[related.Act].Who].Face, 0x108, GameLibrary.Clamp(photoY, 0, 0x96));
                        output.DrawImage(screen.Sprite, PhotoClip, 0x12b, GameLibrary.Clamp(photoY + 0x18, 0, 0xb0));
                        photoY += photoStep;
                    }
                }
            }
            if (!any)
            {
                output.DrawTextFlag0(texts.NoneRelated, 0x10, 0x48, BlueGrey);
            }
            screens.WaitForKey();
            screen.Picface = -1;
            return;
        }
    }

    /// <summary>
    /// Tells the player, one fact at a time, what is new about a person: the person's record goes back to what was
    /// known before, and each new fact (part of the plot, identified, photo, city, organisation, more information,
    /// rank, recruiting) is added with the dossier redrawn and a message box; a city or an organisation learnt this way
    /// counts as seen, and the organisation becomes known. Ends on the dossier, waiting for a key.
    /// </summary>
    /// <remarks>A new bit that is none of these facts is never added, and the original then shows the dossier for ever.</remarks>
    /// <param name="person">The person, 0..63; the record holds what is known now.</param>
    /// <param name="before">What was known before (<see cref="Core.Records.Person.Clues"/>).</param>
    /// <param name="messageBox">The part's message box (text, left edge, top edge).</param>
    // orig: BUG 00e8:22e0 reveal_person_facts (src/c/bug/bug_00e8_22e0.c) matched; also FINAL 0100:5caa, TAC 00e8:8b64; tests: LibraryTextOracleTests
    public void RevealPersonFacts(int person, int before, Action<string, int, int> messageBox)
    {
        const int boxX = 0x14, boxY = 0x3c;
        const short plot = 0x10, identified = 0x02, photo = 0x01, spotted = 0x08, member = 0x04, information = 0x20, rank = 0x100,
            recruiting = 0x200, recruiter = 0x400;
        var txt = state.MessageText;
        var who = campaign.Ps[person];
        var known = who.Clues;
        who.Clues = (short)before;
        if (known == C16.Int(before))
        {
            return;
        }
        do
        {
            ShowSuspectDossier(person, 0, 0);
            texts.Paused = 0;
            known |= who.Clues;
            var fresh = known ^ who.Clues;
            if ((fresh & plot) != 0)
            {
                who.Clues |= plot;
                Announce(texts.PlotPrefix, texts.PlotSuffix);
            }
            else if ((fresh & identified) != 0)
            {
                // the code name first, then the real name it turns out to be
                txt.Text = texts.IdentifiedPrefix;
                CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
                txt.Text += texts.IdentifiedMiddle;
                who.Clues |= identified;
                CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
                txt.Text += texts.IdentifiedSuffix;
                messageBox(txt.Text, boxX, boxY);
            }
            else if ((fresh & photo) != 0)
            {
                who.Clues |= photo;
                Announce(texts.PhotoPrefix, texts.PhotoSuffix);
            }
            else if ((fresh & spotted) != 0)
            {
                who.Clues |= spotted;
                Announce(texts.SpottedPrefix, texts.SpottedMiddle + campaign.Ct[who.Loc].Name + texts.SpottedSuffix);
                campaign.Active[who.Loc]++;
            }
            else if ((fresh & member) != 0)
            {
                who.Clues |= member;
                Announce(texts.MemberPrefix, texts.MemberMiddle + campaign.Orgs[who.Org].Name + texts.MemberSuffix);
                campaign.Orgs[who.Org].Clues |= 1;
                campaign.OrgCounters[who.Org]++;
            }
            else if ((fresh & information) != 0)
            {
                who.Clues |= information;
                Announce(texts.InformationPrefix, texts.InformationSuffix);
            }
            else if ((fresh & rank) != 0)
            {
                who.Clues |= rank;
                Announce(texts.RankPrefix, texts.RankSuffix);
            }
            else if ((fresh & recruiting) != 0)
            {
                who.Clues |= recruiting;
                Announce(texts.RecruitingPrefix, texts.RecruitingSuffix);
            }
            else if ((fresh & recruiter) != 0)
            {
                who.Clues |= recruiter;
                Announce(texts.RecruiterPrefix, texts.RecruiterSuffix);
            }
        }
        while (who.Clues != known);
        ShowSuspectDossier(person, 0, 0);
        screens.WaitForKey();

        // the person's name between two texts, in a message box
        void Announce(string prefix, string suffix)
        {
            txt.Text = prefix;
            CampaignLibrary.AppendPersonName(txt, campaign.Ps, campaign.Orgs, texts.Names, person);
            txt.Text += suffix;
            messageBox(txt.Text, boxX, boxY);
        }
    }
}
