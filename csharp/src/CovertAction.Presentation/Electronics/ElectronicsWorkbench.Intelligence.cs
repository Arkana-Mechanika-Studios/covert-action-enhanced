using CovertAction.Core.Electronics;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Electronics;

/// <summary>What a wiretap of the campaign teaches the player, and the screens that tell it.</summary>
public sealed partial class ElectronicsWorkbench
{
    /// <summary>
    /// Rewards a good result of a campaign wiretap with intelligence on the tapped organisation and city: the board is
    /// saved, the plain palette and colour tables set, the intelligence gathered at the campaign's own difficulty
    /// (not the one the agent's skill lowered), then the board comes back.
    /// </summary>
    /// <param name="strength">How much is learned: 8 for each new best count of dead phones, 16 times the campaign's difficulty for a finished board.</param>
    // orig: BUG 00e8:17bb wiretap_reveal_clue (src/c/bug/bug_00e8_17bb.c) matched; tests: BugOracleTests
    public void WiretapRevealClue(int strength)
    {
        const int plainPalette = 4, flickerPalettes = 4;
        // the popups are placed around the middle of the screen
        const int anchorX = 0xa0, anchorY = 0x64;
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        SelectPalette(plainPalette);
        state.PopupAnchorX = anchorX;
        state.PopupAnchorY = anchorY;
        var lowered = state.Diff;
        state.Diff = state.BaseLevel;
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
        // C: the last argument is passed as a long 1; the function takes its low word
        GatherIntelligence(state.AtLoc, state.InCity, strength, 1);
        world.Graphics.SetColorMaps(state.BoardColorMap, state.BoardColorMap);
        state.Diff = lowered;
        SelectPalette(state.TickCount & (flickerPalettes - 1));
        world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
    }

    /// <summary>
    /// Turns intelligence gained at an organisation in a city into what the player knows of the campaign. For each
    /// person, the nearer their organisation and city are to the tapped ones (by key distance) and the stronger the
    /// tap, the more the player's file on them grows (<see cref="Person.Intel"/>); with it grow the files of the cast
    /// members they are (clues of the plot appear) and what is known of the messages they sent (a screen per new fact:
    /// the date, the receiver, the receiver's organisation or city); then the person's knowledge bits grow at random
    /// while the file allows. When nobody's knowledge changed and <paramref name="showText"/> is set, one random fact
    /// is shown in a popup instead: a city, an organisation, a face, a name, or a new building.
    /// </summary>
    /// <param name="tapOrg">The tapped organisation, 0..15.</param>
    /// <param name="tapCity">The tapped city, 0..15.</param>
    /// <param name="strength">The tap's strength; 0 or less teaches nothing.</param>
    /// <param name="showText">1 for the screens of a wiretap (kept in <see cref="ElectronicsState.CodedFlag"/>, where 1 makes the popup a coded message); 0 for none.</param>
    /// <returns>The persons and cast members of whom the player learned something.</returns>
    // orig: BUG 00e8:1882 gather_intelligence (src/c/bug/bug_00e8_1882.c) matched; tests: BugOracleTests
    // Other code under the same name: FINAL 0100:524c, TAC 00e8:8106 (not this method; docs/source-map/shared-functions.csv).
    public int GatherIntelligence(int tapOrg, int tapCity, int strength, int showText)
    {
        var learned = 0;
        state.CodedFlag = (short)showText;
        if (strength <= 0)
        {
            return 0;
        }
        for (var person = 0; person < state.Nperson; person++)
        {
            var subject = state.Ps[person];
            if (subject.Face == 0)
            {
                continue;
            }
            int org = subject.Org, city = subject.Loc;
            var orgDistance = GameLibrary.KeyDistance(state.Org[tapOrg].Climate, state.Org[org].Climate);
            var cityDistance = GameLibrary.KeyDistance(state.Ct[tapCity].Climate, state.Ct[city].Climate);
            var castDivisor = GrowFile(subject, orgDistance, cityDistance, strength);
            learned += GrowCastFiles(person, castDivisor, tapOrg, tapCity);
            if (subject.Clues == 0 && orgDistance != 0 && cityDistance != 0)
            {
                continue;
            }
            // C: the knowledge bits are an unsigned word
            var bits = (ushort)subject.Clues;
            bits = RevealMessages(person, bits, orgDistance + cityDistance);
            bits = GrowKnowledge(subject, org, city, bits, tapOrg, tapCity);
            if ((ushort)subject.Clues == bits)
            {
                continue;
            }
            CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, org, city, (short)bits, true, RevealPersonFacts, behaviour);
            learned++;
        }
        if (learned == 0 && showText != 0)
        {
            ShowSingleClue();
        }
        return learned;
    }

    /// <summary>
    /// Grows a person's file by the tap: the gain is the larger the nearer the person and the stronger the tap, and
    /// shrinks as the file fills. Returns the divisor that scales the growth of the person's cast files.
    /// </summary>
    private int GrowFile(Person subject, int orgDistance, int cityDistance, int strength)
    {
        const int gainBase = 10000, castBase = 2500;
        // C: 16-bit products widened to long, shifted and divided as longs, then cut back to 16 bits
        var tapDivisor = C16.Int(strength + 1);
        var personDivisor = C16.LoWord(C16.LDiv(C16.LShl(C16.Int((orgDistance + 2) * (cityDistance + 2)), 8), tapDivisor));
        var castDivisor = C16.LoWord(C16.LDiv(C16.LShl(C16.Int((orgDistance + 3) * (cityDistance + 6)), 6), tapDivisor));
        var gain = C16.Div(C16.Div(gainBase, C16.Int(state.Diff + 2)), personDivisor);
        subject.Intel += C16.LoWord(C16.LDiv(C16.LMul(gain, gain), C16.Int(C16.Div(subject.Intel, 2) + gain + 1)));
        // C: the original works out 2500 / (file + 1) here and never uses it; only its division counts
        _ = C16.Div(castBase, C16.Int(subject.Intel + 1));
        return castDivisor;
    }

    /// <summary>
    /// Grows the files of the cast members the person is (those whose step of the plot has come), and reveals a clue
    /// of the plot for each of them while the file outweighs what is already known (five clues at most).
    /// </summary>
    /// <returns>The clues revealed.</returns>
    private int GrowCastFiles(int person, int castDivisor, int tapOrg, int tapCity)
    {
        const int castBase = 5000, allClues = 0x1f, neverActs = -1;
        var revealed = 0;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            var cast = state.Ac[member];
            var file = state.Ac2[member];
            if (cast.Who != person || file.Chron > state.ChronTime || file.Chron == neverActs)
            {
                continue;
            }
            file.Intel += C16.Div(C16.Div(castBase, C16.Int(state.Diff + 3)), C16.Int(castDivisor));
            int known = state.BitCount[cast.Clued & allClues];
            // C: the file times the cover is a 16-bit product
            while (C16.Int(file.Intel * (cast.Cover + 2)) > (known + 2) * (known + 2) << 5 && (cast.Clued & allClues) != allClues)
            {
                revealed += RevealCastClue(member, tapOrg, tapCity);
                known = state.BitCount[cast.Clued & allClues];
            }
        }
        return revealed;
    }

    /// <summary>The facts of a message: its date, its receiver, the receiver's organisation, the receiver's city.</summary>
    [Flags]
    private enum MessageFacts : short
    {
        Date = 1,
        Receiver = 2,
        ReceiverOrg = 4,
        ReceiverCity = 8,
        All = Date | Receiver | ReceiverOrg | ReceiverCity,
        AllButDate = Receiver | ReceiverOrg | ReceiverCity,
    }

    /// <summary>
    /// Walks the messages the person sent and reveals their facts while the person's file allows (each further
    /// message needs one more step of the file), a screen for each new fact; the receivers the player now knows of are
    /// noted. Returns the person's knowledge bits with the plot role marked when the message's role became known.
    /// </summary>
    private ushort RevealMessages(int person, ushort bits, int distance)
    {
        // the event status bits: a coded message, the notes of a meeting, a message at all (the low twelve bits)
        const int coded = 0x200, meeting = 0x800, anyMessage = 0xfff, contact = 0xf00;
        const int roleKnown = 0x10;
        var subject = state.Ps[person];
        var threshold = 2;
        for (var index = 0; index < state.NextSyn; index++)
        {
            var message = state.Sn[index];
            if (message.Who != person)
            {
                continue;
            }
            var plotEvent = state.Ev[message.Data];
            while (C16.Div(C16.Int(state.Ac[plotEvent.Act].Cover * subject.Intel), 0x60) > 2 * state.BitCount[message.Clues & (short)MessageFacts.All] + threshold
                && (message.Clues & (short)MessageFacts.All) != (short)MessageFacts.All)
            {
                var fact = (short)(1 << GameLibrary.RandomBelow(state.Random, 4));
                if ((message.Clues & fact) != 0)
                {
                    continue;
                }
                ShowSuspectDossier(person, 0, 0);
                if ((plotEvent.Status & anyMessage) != 0)
                {
                    ShowMessageFact(person, index, plotEvent, (MessageFacts)fact, coded, meeting);
                }
                else
                {
                    ComposeMessageHeading(person, index, plotEvent, (MessageFacts)fact, coded, meeting);
                    message.Clues |= (short)MessageFacts.AllButDate;
                    Screens.WaitForKey();
                }
                message.Clues |= fact;
                // C: a shift by a variable count
                var role = C16.Shl(1, plotEvent.Act);
                if ((subject.Rknown & role) == 0
                    && (state.Diff < 1 && distance == 0 && (message.Clues & (short)MessageFacts.All) == (short)MessageFacts.All || (plotEvent.Status & anyMessage) == 0))
                {
                    subject.Rknown |= role;
                    bits |= roleKnown;
                    subject.Clues &= ~roleKnown;
                    message.Clues |= (short)MessageFacts.All;
                }
            }
            threshold++;
            if ((plotEvent.Status & contact) != 0 && (message.Clues & (short)MessageFacts.AllButDate) != 0)
            {
                var receiver = state.Ps[message.Who2];
                CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, receiver.Org, receiver.Loc,
                    message.Clues & (short)(MessageFacts.Receiver | MessageFacts.ReceiverOrg), false, RevealPersonFacts, behaviour);
                if (message.Loc2 == receiver.Loc)
                {
                    CampaignLibrary.FindOrCreatePerson(state.Ps, state.Nperson, state.Pl, state.Random, receiver.Org, receiver.Loc,
                        message.Clues & (short)MessageFacts.ReceiverCity, false, RevealPersonFacts, behaviour);
                }
            }
        }
        return bits;
    }

    /// <summary>
    /// Writes the heading of a message screen into the text buffer: what kind of message, whether it is new, the
    /// sender, the message's direction and, for <see cref="MessageFacts.Date"/>, its date.
    /// </summary>
    private void ComposeMessageHeading(int person, int index, Event plotEvent, MessageFacts fact, int coded, int meeting)
    {
        const int minutesPerDay = 0x5a0, monthCount = 12;
        var texts = state.IntelTexts;
        var message = state.Sn[index];
        var text = state.MessageText;
        text.Text = "";
        if ((plotEvent.Status & coded) != 0)
        {
            text.Text = texts.CodedMessage;
        }
        if ((plotEvent.Status & meeting) != 0)
        {
            text.Text = texts.MeetingNotes;
        }
        text.Text += message.Clues == 0 ? texts.NewInformation : texts.AdditionalInformation;
        CampaignLibrary.AppendPersonName(text, state.Ps, state.Org, state.Names, person);
        text.Text += texts.AfterName;
        AppendMsgLogEntry(index);
        text.Text += texts.Learned;
        if ((fact & MessageFacts.Date) != 0)
        {
            text.Text += texts.On + state.MonthNames[C16.Rem(state.Pl.Ncase, monthCount)] + texts.BeforeDay
                + (C16.Div(message.Time, minutesPerDay) + 1);
        }
    }

    /// <summary>
    /// The screen of one new fact of a message: the heading, then the receiver, the receiver's organisation or city as
    /// the fact says, in a box; then the sender's dossier again. The sender's plot role is marked as a lead.
    /// </summary>
    private void ShowMessageFact(int person, int index, Event plotEvent, MessageFacts fact, int coded, int meeting)
    {
        const int leadMark = 0x10;
        const int boxX = 0x10, boxY = 0x60, boxWidth = 0x108, boxHeight = 0x30, textColumns = 0x20, textX = 0x14, textY = 0x64;
        var texts = state.IntelTexts;
        var message = state.Sn[index];
        var text = state.MessageText;
        ComposeMessageHeading(person, index, plotEvent, fact, coded, meeting);
        var receiver = message.Who2;
        if ((fact & MessageFacts.Receiver) != 0)
        {
            state.Ps[receiver].Clues |= (short)MessageFacts.Receiver;
            CampaignLibrary.AppendPersonName(text, state.Ps, state.Org, state.Names, receiver);
        }
        if ((fact & MessageFacts.ReceiverOrg) != 0)
        {
            text.Text += texts.OfThe + state.Org[state.Ps[receiver].Org].Name;
        }
        if ((fact & MessageFacts.ReceiverCity) != 0)
        {
            text.Text += texts.In + state.Ct[message.Loc2].Name;
        }
        text.Text += texts.LineEnd;
        Output.DrawFramedBox(boxX, boxY, boxWidth, boxHeight, state.CGA ? 4 : 3);
        state.Rp1.BPen = 3;
        Text.DrawWrappedText(textColumns, textX, textY, 0xb);
        Screens.WaitForKey();
        message.Clues |= (short)fact;
        ShowSuspectDossier(person, 0, 0);
        Screens.WaitForKey();
        state.Ps[person].Clues |= leadMark;
    }

    /// <summary>
    /// Lets a person's knowledge bits grow while their file outweighs what is known: first the tapped organisation or
    /// city if it is theirs (one bit, then stop), otherwise a random bit of the twelve not yet known (the four facts
    /// and the three ranks; bits 4..7 are never drawn into).
    /// </summary>
    private ushort GrowKnowledge(Person subject, int org, int city, ushort bits, int tapOrg, int tapCity)
    {
        const int facts = 0x70f, orgKnown = 4, cityKnown = 8;
        while (bits != 0 && (state.BitCount[(bits & facts) >> 8] + state.BitCount[bits & 0xf]) * 25 < subject.Intel && (bits & facts) != facts)
        {
            if (tapOrg == org && (bits & orgKnown) == 0)
            {
                return (ushort)(bits | orgKnown);
            }
            if (tapCity == city && (bits & cityKnown) == 0)
            {
                return (ushort)(bits | cityKnown);
            }
            int bit;
            do
            {
                bit = 1 << GameLibrary.RandomBelow(state.Random, 12);
            }
            while ((bit & bits & facts) != 0 || (bit & facts) == 0);
            bits |= (ushort)bit;
        }
        return bits;
    }

    /// <summary>
    /// When the wiretap taught nothing: one random fact about a random member of the cast (by the difficulty, the
    /// harder the fewer kinds), shown in the popup: their city, their organisation, their face, their name, or a
    /// building of an organisation that could plausibly be in a city (the tapped organisation in a cast member's city,
    /// or a cast member's organisation in the agent's city) and is not known yet.
    /// </summary>
    private void ShowSingleClue()
    {
        const int noFace = -1, building = -2, nameKnown = 2, buildingClues = 12, orgSeen = 1;
        var text = state.MessageText;
        text.Text = state.IntelTexts.Empty;
        var person = state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who;
        var subject = state.Ps[person];
        state.PopupFace = noFace;
        switch (GameLibrary.RandomBelow(state.Random, 8 - state.Diff))
        {
            case 0:
                text.Text = state.Ct[subject.Loc].Name;
                state.Active[subject.Loc]++;
                break;
            case 1:
                text.Text = state.Org[subject.Org].Name;
                state.OrgCounters[subject.Org]++;
                state.Org[subject.Org].Clues |= orgSeen;
                break;
            case 2:
                text.Text = state.IntelTexts.FaceOnly;
                state.PopupFace = subject.Face;
                break;
            case 3:
                // the name is shown as if known, and the knowledge put back
                var known = subject.Clues;
                subject.Clues |= nameKnown;
                text.Text = "";
                CampaignLibrary.AppendPersonName(text, state.Ps, state.Org, state.Names, person);
                subject.Clues = known;
                break;
            case 4 or 6:
                // the tapped organisation, in the city of a random member of the cast
                NewBuilding(state.AtLoc, state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Loc);
                break;
            case 5 or 7:
                // the organisation of a random member of the cast, in the agent's city
                NewBuilding(state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Org, state.InCity);
                break;
        }
        if (text[0] != '\0')
        {
            DrawIntelPopup();
            Screens.WaitForKey();
        }

        void NewBuilding(int org, int city)
        {
            // C: a shift by a variable count
            var orgBit = C16.Shl(1, org);
            // C: the presence is tested for being non-zero: a negative presence counts as there
            if (IntelligenceRules.OrgLinkPlausible(state, state.AtLoc, org, city) && (state.Ct[city].OrgClues & orgBit) == 0
                && IntelligenceRules.OrgPresence(state, city, org) != 0)
            {
                state.Ct[city].OrgClues |= orgBit;
                CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, buildingClues, out var buildings);
                state.Nloc = (short)buildings;
                state.Org[org].Clues |= orgSeen;
                text.Text = state.Org[org].Name;
                CampaignLibrary.AppendBuildingType(text, state.BuildingKinds, (c, o) => IntelligenceRules.OrgPresence(state, c, o), city, org);
                text.Text += state.IntelTexts.BuildingIn + state.Ct[city].Name;
                state.PopupFace = building;
            }
            else
            {
                text.Truncate(0);
            }
        }
    }

    /// <summary>
    /// Draws the intelligence popup (146 x 102 pixels) near the anchor point: its frame picture, the person's portrait
    /// or a building's picture, the text of the text buffer in 14 columns with a shadow, and around it a frame, or for a
    /// coded message a random bar code along the bottom.
    /// </summary>
    // orig: BUG 00e8:1613 draw_intel_popup (src/c/bug/bug_00e8_1613.c) matched; tests: BugOracleTests
    public void DrawIntelPopup()
    {
        const int width = 0x92, height = 0x66, preferredLeft = 0x1f, preferredTop = 0x31, building = -2;
        const int textColumns = 0xe, shadowColor = 3, textColor = 5, frameColor = 0xf;
        var left = GameLibrary.Clamp(preferredLeft, state.PopupAnchorX - width, state.PopupAnchorX);
        var top = GameLibrary.Clamp(preferredTop, state.PopupAnchorY - height, state.PopupAnchorY);
        world.Graphics.DrawImagePort(state.Rp1, left, top, state.PopupFrameImage);
        if (state.PopupFace > -1)
        {
            Screens.DrawPortrait(state.PopupFace, left + 0x3a, top + 0x18);
        }
        if (state.PopupFace == building)
        {
            world.Graphics.DrawImagePort(state.Rp1, left + 0x3e, top + 0xa, state.BuildingImage);
        }
        state.Rp1.BPen = 0xb;
        var textY = top + 0x43;
        Text.DrawWrappedText(textColumns, left + 0x14, textY, shadowColor);
        Text.DrawWrappedText(textColumns, left + 0x13, textY, textColor);
        if (state.CodedFlag != 1)
        {
            Output.DrawRectOutline(left + 0xd, top + 0xa, 0x78, 0x4e, frameColor);
            return;
        }
        // a coded message: bars of random length with a short raised gap after each, to 0x72 pixels in
        var baseline = top + 0x40;
        var raised = top + 0x30;
        var offset = 0x10;
        do
        {
            var bar = GameLibrary.RandomBelow(state.Random, 0x10) + 2;
            var x = left + offset;
            Output.DrawLine(x, baseline, bar + x, baseline, frameColor);
            offset += bar;
            x = left + offset;
            Output.DrawLine(x, baseline, x, raised, frameColor);
            Output.DrawLine(x, raised, x + 4, raised, frameColor);
            offset += 4;
            x = left + offset;
            Output.DrawLine(x, baseline, x, raised, frameColor);
        }
        while (offset < 0x72);
        Output.DrawLine(left + offset, baseline, left + 0x82, baseline, frameColor);
    }

    // ---- the library's screens about persons, on this part's campaign tables (LibraryDossiers) ----

    private LibraryDossiers? _dossiers;

    /// <summary>The library's screens about persons (dossier, personnel file, clue report), on this part's campaign tables.</summary>
    public LibraryDossiers Dossiers => _dossiers ??= new LibraryDossiers(state, state, state, state.Dossier, Output, Text, Screens, state, state.Random);

    /// <summary>The library's <c>reveal_cast_clue</c>: a clue of the plot about a cast member, on its report screen; 1 when one was shown.</summary>
    // calls BUG 00e8:282e reveal_cast_clue (the library's port: LibraryDossiers.RevealCastClue)
    private int RevealCastClue(int member, int tapOrg, int tapCity) => Dossiers.RevealCastClue(member, tapOrg, tapCity) ? 1 : 0;

    /// <summary>The library's <c>show_suspect_dossier</c>: the dossier screen of a person.</summary>
    // calls BUG 00e8:31b8 show_suspect_dossier (the library's port: LibraryDossiers.ShowSuspectDossier)
    private void ShowSuspectDossier(int person, int x, int y) => Dossiers.ShowSuspectDossier(person, x, y);

    /// <summary>The library's <c>append_msg_log_entry</c>: appends the direction of a message ("Sent msg to ...") to the text buffer.</summary>
    // calls BUG 00e8:37fd append_msg_log_entry (the library's port: CampaignLibrary.AppendMsgLogEntry)
    private void AppendMsgLogEntry(int message) =>
        CampaignLibrary.AppendMsgLogEntry(state.MessageText, state, state.Dossier.Names, state.Dossier.Words, state.Dossier.NumberText, message);

    /// <summary>Tells the player what is new about a person, in this part's message boxes (the library's <c>reveal_person_facts</c>).</summary>
    /// <remarks>
    /// The library's <c>paused</c> is, in BUG, the message box's own variable (DS:7fbc <c>menu_pause_mode</c>): the two
    /// models of it are kept the same on the way in, before each box and on the way out (as in the raid).
    /// </remarks>
    // calls BUG 00e8:22e0 reveal_person_facts (the library's port: LibraryDossiers.RevealPersonFacts)
    private void RevealPersonFacts(int person, int knownBefore)
    {
        state.Dossier.Paused = C16.Bool(state.Menu.MenuPauseMode);
        Dossiers.RevealPersonFacts(person, knownBefore, (_, x, y) =>
        {
            state.Menu.MenuPauseMode = state.Dossier.Paused != 0;
            Menus.QuitPrompt(state.MessageText, x, y);
            state.Dossier.Paused = C16.Bool(state.Menu.MenuPauseMode);
        });
        state.Menu.MenuPauseMode = state.Dossier.Paused != 0;
    }
}