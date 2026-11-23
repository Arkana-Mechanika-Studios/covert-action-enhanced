using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The menus of the data files (at the CIA building and on the road): the summary of an organisation, the documents
/// of the "Inside Information".
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="plot">The passing of time (its box on a clean screen).</param>
/// <param name="shared">The hand-off block (a message picked for decoding goes there).</param>
public sealed class DataFilesMenus(CampaignState state, CampaignScreens screens, PlotEngine plot, SharedState shared)
{
    /// <summary>What a box returns when the player backs out.</summary>
    private const int BackOut = -1;

    /// <summary>The screens of the data files, in the order of their menu.</summary>
    private enum DataFile
    {
        Clues,
        Suspects,
        InsideInformation,
        NewsBulletins,
        OrganizationSummaries,
        CitySummaries,
        ActivityReports,
    }

    /// <summary>
    /// The menu of the data files ("Data Files..." over the filing cabinet): the clues, the suspects, the inside
    /// information, the news bulletins, the summaries of the organisations and the cities, and the activity reports.
    /// Each choice shows its screen and comes back to the menu, until the player backs out.
    /// </summary>
    // orig: GAME 00f7:63ad data_files_menu (src/c/game/game_00f7_63ad.c) matched; tests: GameOracleTests
    public void DataFilesMenu()
    {
        const int cabinetImage = 0x65, cabinetX = 0x40, cabinetY = 4, allClues = -1;
        int choice;
        do
        {
            screens.ClearScreen();
            screens.Output.DrawImage(state.Sprite, cabinetImage, cabinetX, cabinetY);
            state.Txt.Text = state.CityWords.DataFilesMenuStart;
            state.Txt.Text += state.CityWords.DataFilesMenuEnd;
            choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x1f);
            screens.ClearScreen();
            switch ((DataFile)choice)
            {
                case DataFile.Clues:
                    screens.ClueLists.ShowClues(allClues);
                    break;
                case DataFile.Suspects:
                    SuspectsMenu();
                    break;
                case DataFile.InsideInformation:
                    DocumentsMenu();
                    break;
                case DataFile.NewsBulletins:
                    screens.ShowChronology(false);
                    break;
                case DataFile.OrganizationSummaries:
                    OrganizationSummary();
                    break;
                case DataFile.CitySummaries:
                    CityFilesMenu();
                    break;
                case DataFile.ActivityReports:
                    screens.ShowActivityReport();
                    break;
            }
        }
        while (choice != BackOut);
    }

    /// <summary>
    /// The organisation summary: the player picks one of the organisations he knows, and sees its allies (closer than
    /// 8 on the map), the buildings of it he knows of, and its associates (8 to 11 away), until he backs out.
    /// </summary>
    // orig: GAME 00f7:6a18 organization_summary (src/c/game/game_00f7_6a18.c) matched; tests: GameOracleTests
    public void OrganizationSummary()
    {
        const int titleField = 0xc, closeAllies = 8, farAssociates = 0xc;
        while (true)
        {
            state.Txt.Text = state.StrWhichOrganization;
            state.Mindex = 0;
            CampaignTexts.ListKnownOrganizations(state);
            screens.Menus.ClearAnimationArea();
            var choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
            if (choice == BackOut)
            {
                return;
            }
            int org = state.Mitem[choice];
            screens.Screens.DrawScreenFrame(state.Orgs[org].Name, titleField);
            screens.Text.DrawTextCentered(state.Orgs[org].Name, 0xa0, 0x10, 5);
            state.Txt.Text = state.StrAllies;
            AppendOrgsAtDistance(org, 0, closeAllies);
            // the original's "nothing listed" flag is never cleared: "none" ends every list
            state.Txt.Text += state.StrNone;
            state.Menu.MenuPauseMode = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x17);
            state.Txt.Text = state.StrKnownLocations;
            AppendKnownBuildings(org);
            state.Menu.MenuPauseMode = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x90, 0x17);
            screens.Output.DrawLine(0x18, 0x19, 0x127, 0x19, 4);
            state.Txt.Text = state.StrAssociates;
            AppendOrgsAtDistance(org, closeAllies, farAssociates);
            state.Txt.Text += state.StrNone;
            state.Menu.NoHi = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x67);
        }
    }

    /// <summary>Appends the organisations from 1 on (other than <paramref name="org"/>) whose distance on the map from it is at least <paramref name="from"/> and below <paramref name="below"/>.</summary>
    private void AppendOrgsAtDistance(int org, int from, int below)
    {
        for (var other = 1; other < CampaignState.WorldSize; other++)
        {
            var distance = WorldRules.MapDistance(state.Orgs[org].Climate, state.Orgs[other].Climate);
            if (distance >= from && distance < below && other != org)
            {
                state.Txt.Text += state.Orgs[other].Name;
                state.Txt.Text += state.StrOrgListLine;
            }
        }
    }

    /// <summary>Appends the organisation's buildings the player knows of: present in the city, seen there, and the building's knowledge bit 2 set.</summary>
    private void AppendKnownBuildings(int org)
    {
        const int buildingKnown = 4;
        for (var city = 0; city < CampaignState.WorldSize; city++)
        {
            // C: a shift by a variable count of a 16-bit int, and the evaluation stops at the first false test
            if (WorldRules.OrgPresence(state, city, org) != 0 && (state.Ct[city].OrgClues & C16.Shl(1, org)) != 0
                && (BuildingGlue.LinkClues(state, CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, 0, out _)) & buildingKnown) != 0)
            {
                CampaignLibrary.AppendBuildingType(state.Txt, state.BuildingKinds, (c, o) => WorldRules.OrgPresence(state, c, o), city, org);
                state.Txt.Text += state.StrBuildingIn;
                state.Txt.Text += state.Ct[city].Name;
                state.Txt.Text += state.StrBuildingLine;
            }
        }
    }

    /// <summary>
    /// The documents of the "Inside Information": the Master Plan and the personnel files the player has seen (or
    /// "none"); the one picked is shown, until the player backs out.
    /// </summary>
    // orig: GAME 00f7:704a documents_menu (src/c/game/game_00f7_704a.c) matched; tests: GameOracleTests
    public void DocumentsMenu()
    {
        const int titleField = 7;
        int document;
        do
        {
            screens.Screens.DrawScreenFrame(state.StrDocuments, titleField);
            state.Txt.Text = state.StrSelectDocument;
            state.Mitem[0] = BackOut;
            state.Mindex = 1;
            for (var org = 0; org < CampaignState.WorldSize; org++)
            {
                // C: a shift by a variable count of a 16-bit int
                if ((state.InterPol & C16.Shl(1, org)) == 0)
                {
                    continue;
                }
                state.Mitem[state.Mindex++] = (short)org;
                if (org != 0)
                {
                    state.Txt.Text += state.Orgs[org].Name;
                    state.Txt.Text += state.StrPersonnelFileLine;
                }
                else
                {
                    state.Txt.Text += state.StrMasterPlanLine;
                }
            }
            if (state.Mindex == 1)
            {
                state.Txt.Text += state.StrNoDocuments;
                state.Mitem[1] = BackOut;
            }
            // the rows are counted from the question's line: row -1 (backing out) is the entry in front
            document = state.Mitem[plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x17) + 1];
            if (document != BackOut)
            {
                screens.DataFiles.ShowPersonnelFile(document, false);
            }
        }
        while (document != BackOut);
    }

    /// <summary>
    /// The suspects' files: the player picks a person from the list of suspects and sees the dossier, until he backs out.
    /// </summary>
    // orig: GAME 00f7:7129 suspects_menu (src/c/game/game_00f7_7129.c) matched; tests: GameOracleTests
    public void SuspectsMenu()
    {
        const int titleField = 7;
        while (true)
        {
            screens.Screens.DrawScreenFrame(state.StrSuspectFiles, titleField);
            state.Mindex = 1;
            state.Mitem[0] = BackOut;
            var person = ClueListMenu();
            if (person == BackOut)
            {
                return;
            }
            screens.DataFiles.DrawPersonDossier(person, 0, 0);
            screens.Screens.WaitForKey();
        }
    }

    /// <summary>
    /// The list of suspects: every person the player knows something of, by name, with the organisation and the city
    /// where known, then on a line of its own the roles he is known to play and whether arrested, in hiding, turned or
    /// in custody (when any). Each line notes its person in <see cref="CampaignState.Mitem"/> (from the entry
    /// <see cref="CampaignState.Mindex"/> the caller set).
    /// </summary>
    /// <returns>The person picked, or -1.</returns>
    // orig: GAME 00f7:716a clue_list_menu (src/c/game/game_00f7_716a.c) matched; tests: GameOracleTests
    public int ClueListMenu()
    {
        const int rolesListed = 15, inCustody = -4;
        state.Txt.Text = state.StrSuspectListStart;
        for (var person = 0; person < state.Nperson; person++)
        {
            var who = state.Ps[person];
            if (who.Clues == 0)
            {
                continue;
            }
            CampaignTexts.AppendPersonName(state, person);
            if ((who.Clues & OrgKnown) != 0)
            {
                state.Txt.Text += state.StrSuspectOfThe;
                state.Txt.Text += state.Orgs[who.Org].Sname;
            }
            if ((who.Clues & CityKnown) != 0)
            {
                state.Txt.Text += state.StrSuspectIn;
                state.Txt.Text += state.Ct[who.Loc].Name;
            }
            state.Txt.Text += state.StrSuspectLineEnd;
            var notes = false;
            for (var member = 0; member < rolesListed; member++)
            {
                if ((who.Rknown & 1 << member) != 0)
                {
                    state.Txt.Text += state.StrSuspectRoleStart;
                    state.Txt.Text += state.Ac[member].Name;
                    state.Txt.Text += state.StrSuspectRoleEnd;
                    notes = true;
                }
            }
            foreach (var (alert, note) in new[] { (-1, state.StrSuspectArrested), (-2, state.StrSuspectHiding), (-3, state.StrSuspectTurned), (inCustody, state.StrSuspectInCustody) })
            {
                if (who.Alert == alert)
                {
                    state.Txt.Text += note;
                    notes = true;
                }
            }
            state.Mitem[state.Mindex++] = (short)person;
            if (notes)
            {
                state.Txt.Text += state.StrSuspectNotesEnd;
                state.Mitem[state.Mindex++] = (short)person;
            }
        }
        if (state.Mindex == 1)
        {
            state.Txt.Text += state.StrNoSuspects;
            state.Mitem[state.Mindex] = BackOut;
        }
        return state.Mitem[plot.PopupMenuOnCleanScreen(state.Txt.Text, 10, 0x11) + 1];
    }

    /// <summary><see cref="Core.Records.Person.Clues"/>: the player knows the person's organisation, city.</summary>
    private const int OrgKnown = 0x04, CityKnown = 0x08;

    /// <summary>
    /// The list of intercepted messages the player can decode (messages sent or received, or of no event, that tell
    /// something and were not decoded yet): the sender and the start of the message, cut to the box's width. Unless the
    /// player is back from a mission, the one picked goes to the cryptography workstation: its number and its text
    /// (<see cref="CodedMessages.ComposeMessageText"/>) are put into the hand-off block.
    /// </summary>
    // orig: GAME 00f7:736f coded_messages_menu (src/c/game/game_00f7_736f.c) matched; tests: GameOracleTests
    public void CodedMessagesMenu()
    {
        const int titleField = 7, boxWidth = 0x124, skippedWords = 11, previewSize = 64;
        screens.Screens.DrawScreenFrame(state.StrCodedMessages, titleField);
        state.Mindex = 1;
        state.Mitem[0] = BackOut;
        state.Txt.Text = state.StrCodedListStart;
        for (var msg = 0; msg < state.NextSyn; msg++)
        {
            var message = state.Sn[msg];
            if (!IsCodedMessage(message))
            {
                continue;
            }
            CampaignTexts.AppendPersonName(state, message.Who);
            state.Txt.Text += state.StrCodedAfterName;
            var start = state.Txt.Length;
            CampaignTexts.AppendMessageText(state, msg);
            // the preview skips the first 11 characters of the message's line, and the line is cut there too
            var preview = new TextBuffer(previewSize) { Text = state.Txt.Text[(start + skippedWords)..] };
            while (screens.Text.TextWidth(preview.Text) > boxWidth)
            {
                preview.Truncate(preview.Length - 1);
            }
            state.Txt.Truncate(start + skippedWords);
            state.Txt.Text += state.StrCodedEllipsis;
            state.Txt.Text += preview.Text;
            state.Txt.Text += state.StrCodedLineEnd;
            state.Mitem[state.Mindex++] = (short)msg;
            state.Mitem[state.Mindex++] = (short)msg;
        }
        if (state.EntryMode != 0)
        {
            return;
        }
        if (state.Mindex == 1)
        {
            state.Txt.Text += state.StrNoCodedMessages;
            state.Mitem[state.Mindex] = BackOut;
        }
        int picked = state.Mitem[plot.PopupMenuOnCleanScreen(state.Txt.Text, 10, 15) + 1];
        if (picked != BackOut)
        {
            shared.Person = (sbyte)picked;
            screens.Messages.ComposeMessageText(picked);
            state.Txt.Bytes.AsSpan(0, shared.Text.Bytes.Length).CopyTo(shared.Text.Bytes);
        }
    }

    /// <summary>A message the player may decode: of an event sent or received (or of none), telling something, not decoded yet.</summary>
    private bool IsCodedMessage(Core.Records.Synopsis message)
    {
        const int eventKind = 0xf00, sent = 0x200, received = 0x300;
        const short decoded = unchecked((short)0x8000);
        if (message.Type != 0 || message.Clues == 0 || (message.Clues & decoded) != 0)
        {
            return false;
        }
        // C: the original reads the kind of "event" -1 too (bytes in front of the events table) before it tests for
        // no event; the reading changes nothing
        int happening = message.Data;
        return happening < 0 || (state.Ev[happening].Status & eventKind) is sent or received;
    }

    /// <summary>The wiretaps: each tapped building (strength above 1) by organisation, city and strength in percent, or "none".</summary>
    // orig: GAME 00f7:692a show_wiretaps (src/c/game/game_00f7_692a.c) matched; tests: GameOracleTests
    public void ShowWiretaps()
    {
        const int titleField = 7;
        screens.Screens.DrawScreenFrame(state.StrWireTaps, titleField);
        state.Txt.Text = "";
        var any = false;
        for (var link = 0; link < state.Nloc; link++)
        {
            var building = state.Lc[link];
            if (building.Tapped <= 1)
            {
                continue;
            }
            state.Txt.Text += state.Orgs[building.Org].Name;
            state.Txt.Text += state.StrTapSlash;
            state.Txt.Text += state.Ct[building.Loc].Name;
            state.Txt.Text += state.StrTapDots;
            CStr.Itoa(building.Tapped, state.PercentText.Bytes, 10);
            state.Txt.Text += state.PercentText.Text;
            state.Txt.Text += state.StrTapPercent;
            any = true;
        }
        if (!any)
        {
            state.Txt.Text += state.StrNoWireTaps;
        }
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x32, 0x15);
    }

    /// <summary>
    /// The city summaries: the player picks a city, and sees the organisations present there whose building he knows
    /// (its kind, or "Unknown building." when he knows of the building but not what it is), the suspects spotted
    /// there with their organisation and the roles he knows, and the clues that point to the city; until he backs out.
    /// </summary>
    /// <remarks>The original has a "none" of its own for each list (DS:3768, 3787, 3799), all the same text; the rewrite reads the first.</remarks>
    // orig: GAME 00f7:6cfa city_files_menu (src/c/game/game_00f7_6cfa.c) matched; tests: GameOracleTests
    public void CityFilesMenu()
    {
        const int titleField = 2, citySubject = 8, clueColumns = 0x10;
        while (true)
        {
            screens.Menus.ClearAnimationArea();
            state.Txt.Text = state.StrWhichCity;
            state.Mindex = 0;
            CampaignTexts.ListCities(state);
            var choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x15);
            if (choice == BackOut)
            {
                return;
            }
            int city = state.Mitem[choice];
            screens.Screens.DrawScreenFrame(state.Ct[city].Name, titleField);
            screens.Text.DrawTextCentered(state.Ct[city].Name, 0xa0, 0xf, 5);
            state.Txt.Text = state.StrCityKnownLocations;
            var any = false;
            // organisation 0 is not listed
            for (var org = 1; org < CampaignState.WorldSize; org++)
            {
                if (WorldRules.OrgPresence(state, city, org) == 0)
                {
                    continue;
                }
                var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, 0, out _);
                if (link == -1)
                {
                    continue;
                }
                if ((state.Lc[link].Clues & BuildingKnown) != 0)
                {
                    state.Txt.Text += state.Orgs[org].Name;
                    CampaignLibrary.AppendBuildingType(state.Txt, state.BuildingKinds, (c, o) => WorldRules.OrgPresence(state, c, o), city, org);
                }
                else
                {
                    state.Txt.Text += state.StrUnknownBuilding;
                }
                state.Txt.Text += state.StrCityListLine;
                any = true;
            }
            EndList(any);
            state.Menu.MenuPauseMode = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0xa0, 0x19);
            state.Txt.Text = state.StrCitySuspects;
            any = false;
            for (var person = 0; person < CampaignState.PersonCount; person++)
            {
                var who = state.Ps[person];
                if ((who.Clues & CityKnown) == 0 || who.Loc != city)
                {
                    continue;
                }
                CampaignTexts.AppendPersonName(state, person);
                if ((who.Clues & OrgKnown) != 0)
                {
                    state.Txt.Text += state.StrCitySuspectSlash;
                    state.Txt.Text += state.Orgs[who.Org].Sname;
                }
                for (var member = 0; member < state.NACTIVITY; member++)
                {
                    // C: a shift by a variable count of a 16-bit int
                    if ((who.Rknown & C16.Shl(1, member)) != 0)
                    {
                        state.Txt.Text += state.StrCitySuspectRole;
                        state.Txt.Text += state.Ac[member].Name;
                    }
                }
                state.Txt.Text += state.StrCitySuspectLineEnd;
                any = true;
            }
            EndList(any);
            state.Menu.MenuPauseMode = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x19);
            screens.Output.DrawLine(0x18, 0x19, 0x127, 0x19, 0xa);
            state.Txt.Text = state.StrCityClues;
            any = false;
            for (var clue = 0; clue < CampaignState.ClueCount; clue++)
            {
                var subject = state.Cl[clue].Info1;
                if (subject >> 8 != citySubject || (subject & 0xff) != city)
                {
                    continue;
                }
                state.Txt.Text += state.StrCityClueStart;
                AppendActivityText(clue);
                state.Txt.Text += state.StrCityClueEnd;
                any = true;
            }
            EndList(any);
            LibraryTexts.WrapTextBuffer(state.Txt, clueColumns, state.LineEnd);
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x71);
            screens.Menus.ClearAnimationArea();
        }
    }

    /// <summary><see cref="Core.Records.Location.Clues"/>: the player knows what kind of building it is.</summary>
    private const int BuildingKnown = 4;

    /// <summary>Ends an empty list with "none".</summary>
    private void EndList(bool any)
    {
        if (!any)
        {
            state.Txt.Text += state.StrCityNone;
        }
    }

    /// <summary>The sentence of a clue (the library's <c>append_activity_text</c> with GAME's labels and subjects).</summary>
    private void AppendActivityText(int clue) =>
        CampaignLibrary.AppendActivityText(state.Txt, state.Cl, state.ActivityPhrases, state.StrCluePeriod, clue,
            key => CampaignTexts.AppendClueLabel(state, key), (c, subject) => CampaignTexts.AppendClueSubject(state, c, subject));
}
