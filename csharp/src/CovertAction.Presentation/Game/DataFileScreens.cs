using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The documents of the data files and the "Inside Information" the player gathers: the Master Plan with the plot's
/// cast, and an organisation's personnel file.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="screens">The campaign's screens (their drawing and text routines).</param>
public sealed class DataFileScreens(CampaignState state, CampaignScreens screens)
{
    /// <summary>The document that is the Master Plan (the others are the organisations' personnel files).</summary>
    private const int MasterPlan = 0;

    /// <summary>The colour field of the documents' frame.</summary>
    private const int DocumentFrame = 9;

    /// <summary>Colours used here (palette indices).</summary>
    private const int PanelColour = 5, Yellow = 0xe, Red = 0xc;

    /// <summary><see cref="Core.Records.Person.Alert"/> of a person the player has arrested, who is in hiding, or turned double agent.</summary>
    private const int Arrested = -1, InHiding = -2, Turned = -3;

    /// <summary><see cref="Core.Records.Person.Clues"/>: the player has a photo of the person, and has forgotten it.</summary>
    private const int PhotoKnown = 0x01, PhotoForgotten = 0x80;

    /// <summary><see cref="Core.Records.Person.Clues"/>: name, organisation and city known (what a personnel file reveals).</summary>
    private const short IdentityKnown = 0x0e;

    /// <summary><see cref="Core.Records.Person.Clues"/>: the player knows the person's city.</summary>
    private const int CityKnown = 0x08;

    /// <summary><see cref="Core.Records.Person.Role"/>: the person has done something in the plot (bit 15 aside).</summary>
    private const int ActiveRoles = 0x7fff;

    /// <summary>
    /// Shows a document of the "Inside Information" and marks it as seen (bit <paramref name="org"/> of
    /// <see cref="CampaignState.InterPol"/>, which the list of documents shows). Document 0 is the Master Plan: the
    /// plot's members but the MasterMind, each with the portrait (unless its photo was forgotten), what became of
    /// them (arrested, in hiding, turned) and the role. Any other is the personnel file of that organisation: every
    /// person of it who has done something in the plot, by name and, where known, city, or "(No activity)". Then
    /// waits for a key.
    /// </summary>
    /// <param name="org">The document: 0 the Master Plan, otherwise the organisation.</param>
    /// <param name="markKnown">Whether the file reveals its persons (name, organisation and city become known).</param>
    // orig: GAME 00f7:0978 show_personnel_file (src/c/game/game_00f7_0978.c) matched; tests: GameOracleTests
    // GAME's copy is other code than the library's show_personnel_file of FINAL, TAC and BUG (shared-functions.csv)
    public void ShowPersonnelFile(int org, bool markKnown)
    {
        if (org == MasterPlan)
        {
            ShowMasterPlan();
        }
        else
        {
            ShowActionTeam(org, markKnown);
        }
        screens.Screens.WaitForKey();
    }

    /// <summary>The Master Plan: the members of the plot, four to a row, in two rows.</summary>
    private void ShowMasterPlan()
    {
        const int stamps = 5, firstRowY = 0x20, secondRowStep = 0x50, columnWidth = 0x48, firstColumn = 0x1c, stagger = 8;
        state.InterPol |= 1;
        screens.Screens.DrawScreenFrame(state.StrMasterPlan, DocumentFrame);
        state.Txt.Text = "";
        for (var i = 0; i < stamps; i++)
        {
            state.Txt.Text += state.StrSecret;
        }
        screens.Output.DrawTextFlag0(state.Txt.Text, 0xc, 0x10, Yellow);
        screens.Output.DrawTextFlag0(state.Txt.Text, 0xc, 0xbc, Yellow);
        var y = firstRowY;
        for (var member = 0; state.NACTIVITY - state.Diff * 4 > member; member++)
        {
            if ((state.Ac[member].Status & CastRules.CastMastermind) != 0)
            {
                continue;
            }
            var x = (member & 3) * columnWidth + firstColumn;
            if (member == 4)
            {
                y += secondRowStep;
            }
            var person = state.Ps[state.Ac[member].Who];
            if ((person.Clues & (PhotoKnown | PhotoForgotten)) != PhotoForgotten)
            {
                screens.Screens.DrawPortrait(person.Face, x, y);
            }
            state.Txt.Text = "";
            if (person.Alert == Arrested)
            {
                state.Txt.Text = state.StrArrested;
            }
            if (person.Alert == InHiding)
            {
                state.Txt.Text = state.StrInHiding;
            }
            if (person.Alert == Turned)
            {
                state.Txt.Text = state.StrTurned;
            }
            // the word over the portrait, with a shadow one pixel to the right
            screens.Output.DrawTextFlag0(state.Txt.Text, x - 1, y + 0x10, PanelColour);
            screens.Output.DrawTextFlag0(state.Txt.Text, x - 2, y + 0x10, Red);
            state.Txt.Text = state.Ac[member].Name;
            screens.Text.DrawTextCentered(state.Txt.Text, x + 0x19, y + 0x38, Red);
            // every other portrait stands a little lower
            y ^= stagger;
        }
    }

    /// <summary>An organisation's personnel file: its persons who have done something in the plot.</summary>
    private void ShowActionTeam(int org, bool markKnown)
    {
        const int listX = 0x50, firstLine = 0x20, lineHeight = 8;
        // C: a shift by a variable count of a 16-bit int
        state.InterPol |= C16.Shl(1, org);
        screens.Screens.DrawScreenFrame(state.StrPersonnelFile, DocumentFrame);
        state.Txt.Text = state.Orgs[org].Name;
        state.Txt.Text += state.StrActionTeam;
        screens.Output.DrawTextFlag0(state.Txt.Text, listX, 0x10, PanelColour);
        var y = firstLine;
        var none = true;
        // person 0 is not looked at
        for (var person = 1; person < state.Nperson; person++)
        {
            var who = state.Ps[person];
            if (who.Org != org || (who.Role & ActiveRoles) == 0)
            {
                continue;
            }
            if (markKnown)
            {
                who.Clues |= IdentityKnown;
            }
            state.Txt.Text = "";
            CampaignTexts.AppendPersonName(state, person);
            if ((who.Clues & CityKnown) != 0)
            {
                state.Txt.Text += state.StrNameCitySeparator;
                state.Txt.Text += state.Ct[who.Loc].Name;
            }
            screens.Output.DrawTextFlag0(state.Txt.Text, listX, y, Red);
            y += lineHeight;
            none = false;
        }
        if (none)
        {
            screens.Output.DrawTextFlag0(state.StrNoActivity, listX, y, Red);
        }
    }

    // ---- the dossier card of a person ----

    /// <summary><see cref="Core.Records.Person.Clues"/>: what the player knows of a person.</summary>
    private const int OrgKnown = 0x04, AllBasics = 0x1f, RankKnown = 0x100, RecruitedWhereKnown = 0x200, RecruitedByWhomKnown = 0x400;

    /// <summary><see cref="Core.Records.Synopsis.Clues"/>: the player decoded the message.</summary>
    private const int MessageDecoded = 0x8000;

    /// <summary><see cref="Core.Records.Event.Status"/>: the kind of event: a message sent, received; a meeting, a trip.</summary>
    private const int EventKind = 0xf00, Sent = 0x200, Received = 0x300, Meeting = 0x800, Trip = 0x900;

    /// <summary>Colours of the card (palette indices).</summary>
    private const int Label = 7, Grey = 4, Lettering = 0xc;

    /// <summary>
    /// Draws a person's dossier card: name, rank (when determined), organisation and city (when known; the
    /// organisation becomes known to the player), who recruited the person and where (when known), the portrait, the
    /// roles the person is known to play in the plot, whether arrested, in hiding or turned; and, on a card of its own
    /// screen, the messages of the person the player has intercepted, each with what the player made of it (the
    /// decoded text, or the readable words of a meeting or a trip), page by page.
    /// </summary>
    /// <param name="person">The person.</param>
    /// <param name="x">The card's left edge on a screen of its own (0 there), or in the caller's frame.</param>
    /// <param name="y">0 for a screen of its own (titled with the person's name); otherwise the card's top in the caller's frame, and then no portrait, no recruiting line and no messages.</param>
    // orig: GAME 00f7:58dc draw_person_dossier (src/c/game/game_00f7_58dc.c) matched; tests: GameOracleTests
    public void DrawPersonDossier(int person, int x, int y)
    {
        const int lineHeight = 8, ruleLength = 0x80, cardBottom = 0x63, portraitTop = 0x36;
        var inFrame = y != 0;
        var top = y;
        var who = state.Ps[person];
        if (inFrame)
        {
            x += 4;
        }
        else
        {
            DrawTitledScreen(person);
            y = 0x12;
            x += 0xa;
        }
        y += 2;
        state.Txt.Text = state.Dossier.Name;
        CampaignTexts.AppendPersonName(state, person);
        screens.Output.DrawTextFlag0(state.Txt.Text, x, y, Label);
        if ((who.Clues & RankKnown) != 0)
        {
            y += lineHeight;
            DrawLabelled(state.Dossier.Rank, state.Dossier.RankNames[RankOf(person)], x, y);
        }
        if ((who.Clues & OrgKnown) != 0)
        {
            state.Orgs[who.Org].Clues |= 1;
            y += lineHeight;
            DrawLabelled(state.Dossier.Org, state.Orgs[who.Org].Name, x, y);
        }
        if ((who.Clues & CityKnown) != 0)
        {
            y += lineHeight;
            DrawLabelled(state.Dossier.City, state.Ct[who.Loc].Name, x, y);
        }
        state.Txt.Text = state.Dossier.Recruited;
        if ((who.Clues & RecruitedByWhomKnown) != 0)
        {
            AppendRecruiter(person);
        }
        if ((who.Clues & RecruitedWhereKnown) != 0)
        {
            AppendRecruitingCity(who.Loc);
        }
        // "Recruited" alone has nine letters: the line is drawn when something was added
        if (state.Txt.Length > 0xc && !inFrame)
        {
            state.Txt.Text += state.Dossier.FullStop;
            y += lineHeight;
            screens.Output.DrawTextFlag0(state.Txt.Text, x, y, Label);
        }
        if ((who.Clues & PhotoKnown) != 0 && !inFrame)
        {
            screens.Screens.DrawPortrait(who.Face, 0x108, 8);
            screens.Output.DrawImage(state.Sprite, PortraitFrameImage, 0x12b, 0x28);
        }
        y += 0xa;
        screens.Output.DrawLine(x, y - 2, x + ruleLength, y - 2, Lettering);
        DrawRoles(person, x, y);
        y = DrawFate(who.Alert, x, y);
        screens.Output.DrawLine(x, y + lineHeight, x + ruleLength, y + lineHeight, Lettering);
        y += 2;
        y = GameLibrary.Clamp(y + lineHeight, (who.Clues & PhotoKnown) != 0 ? top + portraitTop : 0, cardBottom);
        // the original has a branch here that never runs ("if (0)": it copies three texts into Txt)
        if (!inFrame)
        {
            DrawMessagesOf(person, x, y);
        }
    }

    /// <summary>The image of the frame drawn beside a portrait.</summary>
    private const int PortraitFrameImage = 0x6c;

    /// <summary>A screen of its own for a card: the person's name as its title.</summary>
    private void DrawTitledScreen(int person)
    {
        const int titledFrame = 3;
        state.Txt.Text = "";
        CampaignTexts.AppendPersonName(state, person);
        screens.Screens.DrawScreenFrame(state.Txt.Text, titledFrame);
    }

    /// <summary>Draws a label and its value as one line of the card.</summary>
    private void DrawLabelled(string label, string value, int x, int y)
    {
        state.Txt.Text = label;
        state.Txt.Text += value;
        screens.Output.DrawTextFlag0(state.Txt.Text, x, y, Label);
    }

    /// <summary>A person's rank: the rank of the cast member the person is (the last one), else bits 2..4 of the face.</summary>
    private int RankOf(int person)
    {
        var rank = state.Ps[person].Face >> 2 & 7;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            if (state.Ac[member].Who == person)
            {
                rank = state.Ac[member].Rank;
            }
        }
        return rank;
    }

    /// <summary>
    /// Appends " by " and the recruiter: the first cast member of the person's organisation, unless that is the person.
    /// </summary>
    private void AppendRecruiter(int person)
    {
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            var recruiter = state.Ac[member].Who;
            if (state.Ps[person].Org == state.Ps[recruiter].Org)
            {
                if (person != recruiter)
                {
                    state.Txt.Text += state.Dossier.By;
                    CampaignTexts.AppendFullName(state, recruiter);
                }
                return;
            }
        }
    }

    /// <summary>Appends " in " and the city nearest to the person's own (the first of the nearest).</summary>
    private void AppendRecruitingCity(int home)
    {
        var nearest = -1;
        var shortest = 999;
        for (var city = 0; city < CampaignState.WorldSize; city++)
        {
            if (city != home && GameLibrary.CityDistance(state.Ct, city, home) < shortest)
            {
                nearest = city;
                shortest = GameLibrary.CityDistance(state.Ct, city, home);
            }
        }
        if (nearest != -1)
        {
            state.Txt.Text += state.Dossier.In;
            state.Txt.Text += state.Ct[nearest].Name;
        }
    }

    /// <summary>The roles the player knows the person plays, all on one line (each over the last), or "not involved" / "unknown".</summary>
    private void DrawRoles(int person, int x, int y)
    {
        var who = state.Ps[person];
        var any = false;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            // C: a shift by a variable count of a 16-bit int: members from 16 on give no bit
            if ((who.Rknown & C16.Shl(1, member)) != 0)
            {
                DrawLabelled(state.Dossier.Role, state.Ac[member].Name, x, y);
                any = true;
            }
        }
        if (!any)
        {
            state.Txt.Text = (who.Clues & AllBasics) == AllBasics && (who.Role & ActiveRoles) == 0 ? state.Dossier.RoleNotInvolved : state.Dossier.RoleUnknown;
            screens.Output.DrawTextFlag0(state.Txt.Text, x, y, Label);
        }
    }

    /// <summary>The line of an arrested, hiding or turned person; returns the row reached.</summary>
    private int DrawFate(int alert, int x, int y)
    {
        const int lineHeight = 8;
        foreach (var (fate, words) in new[] { (Arrested, state.Dossier.UnderArrest), (InHiding, state.Dossier.InHiding), (Turned, state.Dossier.Turned) })
        {
            if (alert == fate)
            {
                state.Txt.Text = words;
                y += lineHeight;
                screens.Output.DrawTextFlag0(state.Txt.Text, x, y, PanelColour);
            }
        }
        return y;
    }

    /// <summary>
    /// The person's intercepted messages under the card: each message's line, then the decoded text in quotes, or for
    /// one not decoded a note (a message sent or received) or the readable words (a meeting or a trip). When the
    /// screen is full the player is asked for a key and the list goes on on a new screen, from the same message.
    /// </summary>
    private void DrawMessagesOf(int person, int x, int y)
    {
        const int pageEnd = 0xc0, newPageTop = 0x10, lineColumns = 0x25, textColumns = 0x24, wordsShown = 5, lineHeight = 8;
        var message = NextMessageOf(person, 0);
        while (message != -1)
        {
            state.Txt.Text = "";
            CampaignTexts.AppendMessageText(state, message);
            y = screens.Text.DrawWrappedText(lineColumns, x, y, PanelColour);
            var happening = state.Sn[message].Data;
            if ((state.Sn[message].Clues & MessageDecoded) != 0)
            {
                state.Txt.Text = state.Dossier.OpenQuote;
                screens.DescribeEvent(happening);
                state.Txt.Text += state.Dossier.CloseQuote;
                y = screens.Text.DrawWrappedText(textColumns, x + 4, y, Lettering);
            }
            else
            {
                var kind = state.Ev[happening].Status & EventKind;
                if (kind == Sent || kind == Received)
                {
                    screens.Output.DrawText(state.Dossier.MessageNotDecoded, x + 4, y, Grey);
                    y += lineHeight;
                }
                if (kind == Meeting || kind == Trip)
                {
                    state.Txt.Text = "";
                    screens.DescribeEvent(happening);
                    LibraryTexts.KeepMarkedWords(state.Txt, state.Sn[message].Clues, wordsShown, state.StrEllipsis);
                    y = screens.Text.DrawWrappedText(textColumns, x + 4, y, Lettering);
                }
            }
            if (y >= pageEnd)
            {
                screens.Output.DrawText(state.Dossier.More, 0x100, pageEnd, Lettering);
                screens.Screens.WaitForKey();
                DrawTitledScreen(person);
                y = newPageTop;
                // the original goes on with the same message, which is drawn again at the top of the new page
                continue;
            }
            message = NextMessageOf(person, message + 1);
        }
    }

    /// <summary>The first message from <paramref name="from"/> on that belongs to the person and tells the player something; -1 for none.</summary>
    private int NextMessageOf(int person, int from)
    {
        for (var message = from; message < state.NextSyn; message++)
        {
            if (state.Sn[message].Who == person && state.Sn[message].Clues != 0)
            {
                return message;
            }
        }
        return -1;
    }
}
