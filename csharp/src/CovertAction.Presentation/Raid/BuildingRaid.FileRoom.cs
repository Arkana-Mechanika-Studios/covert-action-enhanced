using CovertAction.Core.Raid;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Raid;

/// <summary>The file room's terminal: its input box and the search of the organisation's files.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The alert a wrong password (or, now and then, a search) raises in the room.</summary>
    private const int TerminalAlert = 0xf0;

    /// <summary>The tries the player has at a search.</summary>
    private const int SearchTries = 3;

    /// <summary>The longest text the player can type in the search box.</summary>
    private const int SearchLength = 0xf;

    /// <summary>The organisations 0 and 1 cannot be searched for by name.</summary>
    private const int FirstSearchableOrg = 2;

    /// <summary>The plot objects of the case (<see cref="RaidState.Mc"/>).</summary>
    private const int PlotObjectCount = 4;

    // palette indices
    private const int InputBoxDark = 8, InputBoxField = 5, InputBoxLight = 7, InputBoxText = 2, NoDataColor = 7;

    /// <summary>
    /// Draws the terminal's input box (the password and the search): frames of grey and blue with light edges, and the
    /// text centred at its top.
    /// </summary>
    /// <param name="text">The prompt; empty for none.</param>
    // orig: TAC 00e8:3b0b draw_input_box (src/c/tac/tac_00e8_3b0b.c) matched; tests: TacOracleTests
    public void DrawInputBox(string text)
    {
        world.Graphics.SetColorMaps(state.CgaMapInputBox, state.CgaMapInputBox);
        Output.FillRectCorners(0x63, 0x55, 0xdd, 0x72, InputBoxDark);
        Output.FillRectCorners(0x64, 0x57, 0xdc, 0x71, InputBoxField);
        Output.FillRectCorners(0x65, 0x58, 0xdb, 0x70, InputBoxDark);
        Output.FillRectCorners(0x69, 0x65, 0xd7, 0x6e, InputBoxField);
        Output.DrawLine(0x65, 0x56, 0xdb, 0x56, InputBoxLight);
        Output.DrawLine(0x67, 0x63, 0xd9, 0x63, InputBoxField);
        Output.DrawLine(0x66, 0x70, 0xda, 0x70, InputBoxField);
        Output.DrawLine(0x68, 0x65, 0x68, 0x6e, InputBoxLight);
        Output.DrawLine(0xd8, 0x65, 0xd8, 0x6e, InputBoxLight);
        Text.DrawTextCentered(text, 0xa1, 0x5a, InputBoxText);
    }

    /// <summary>The use of a terminal (the key F4): the password prompt, when the piece in front is a terminal.</summary>
    // orig: TAC 00e8:33ca use_object_in_front (src/c/tac/tac_00e8_33ca.c) matched; tests: TacOracleTests
    public void UseObjectInFront()
    {
        GuardRules.FindFrontCell(state);
        var content = state.CellMap[RaidState.Cell(state.FrontCellX, state.FrontCellY)];
        if (content != RaidState.CellEmpty && (TileBits(content & RaidState.CellTile) & TileFlags.Terminal) != 0)
        {
            PasswordPrompt();
        }
    }

    /// <summary>
    /// The terminal: asks for the password (up to 12 characters). The right one (in capitals or not) opens the file
    /// room's search, after which the alarm may go off (the more likely the harder the level); a wrong one raises the
    /// alert and holds the player a second and a half. Either way a new password is drawn. Escape leaves at once.
    /// </summary>
    // orig: TAC 00e8:3a3e password_prompt (src/c/tac/tac_00e8_3a3e.c) matched; tests: TacOracleTests
    public void PasswordPrompt()
    {
        SaveScreen();
        DrawInputBox(state.StrEnterPassword);
        var txt = state.MessageText;
        txt[0] = '\0';
        if (InputLine(0x74, 0x64, 0xc) != -1)
        {
            DrawInputBox(state.StrPasswordEmpty);
            if (SameIgnoringCase(txt.Text, state.PasswordWord))
            {
                Text.DrawTextCentered(state.StrAccessConfirmed, 0xa0, 0x67, 0xa);
                FileRoomSearch();
                if (GameLibrary.RandomBelow(state.Random, 3) < state.CampaignDiff)
                {
                    GuardRules.RaiseAlert(state, state.InRoom, TerminalAlert);
                }
            }
            else
            {
                Text.DrawTextCentered(state.StrAccessDenied, 0xa0, 0x67, 0xc);
                GuardRules.RaiseAlert(state, state.InRoom, TerminalAlert);
                Output.WaitVsync(0x5a);
            }
            // a new password is drawn after every attempt, right or wrong
            ReadTextLine();
        }
        state.Rp1.BPen = 1;
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        RestoreFullScreen();
        DrawStatusPanel();
    }

    /// <summary>Whether two texts are the same but for the case of the letters a to z, as the C library's <c>strcmpi</c> answers 0.</summary>
    private static bool SameIgnoringCase(string a, string b) => string.Equals(UpperCase(a), UpperCase(b), StringComparison.Ordinal);

    /// <summary>
    /// The file room's search: up to three times the player types a text, which is looked up, in capitals and as a
    /// part of a name, among the persons of the case (their dossier, with all facts known), the cities (whether the
    /// building's organisation has a building there), the organisations (whether they have one in this city), the
    /// clues of the cast and the plot objects (the city each is in). "No Data" when nothing matches; an empty text
    /// ends the search.
    /// </summary>
    // orig: TAC 00e8:3c1c file_room_search (src/c/tac/tac_00e8_3c1c.c) matched; tests: TacOracleTests
    public void FileRoomSearch()
    {
        Output.WaitVsync(0x3c);
        var tries = 0;
        do
        {
            DrawInputBox(state.StrSearchFor);
            var txt = state.MessageText;
            txt[0] = '\0';
            InputLine(0x6a, 0x64, SearchLength);
            Input.FlushInput();
            var wanted = UpperCase(txt.Text);
            if (wanted.Length == 0)
            {
                return;
            }
            if (SearchPersons(wanted) || SearchCities(wanted) || SearchOrgs(wanted) || SearchClues(wanted) || SearchPlotObjects(wanted))
            {
                return;
            }
            DrawInputBox(state.StrSearchEmpty);
            Text.DrawTextCentered(state.StrNoData, 0xa0, 0x67, NoDataColor);
            Output.WaitVsync(0x5a);
        }
        while (++tries < SearchTries);
    }

    /// <summary>
    /// The persons of the case whose full name holds the text: the first one in this city or organisation (or any, below
    /// the two hardest levels) becomes known in all five facts (and in its roles, when in both) and its dossier is shown
    /// (file_room_search).
    /// </summary>
    /// <returns>Whether one was found (the search ends).</returns>
    private bool SearchPersons(string wanted)
    {
        var txt = state.MessageText;
        for (var p = 0; p < RaidState.Persons; p++)
        {
            var person = state.Ps[p];
            if (person.Face == 0)
            {
                continue;
            }
            txt[0] = '\0';
            CampaignLibrary.AppendRealName(txt, state.Ps, state.Org, state.Dossier.Names, p);
            if (!UpperCaseHolds(wanted))
            {
                continue;
            }
            if (person.Loc != state.InCity && person.Org != state.AtLoc && state.CampaignDiff >= 2)
            {
                continue;
            }
            // the five facts are the low byte of the knowledge word
            person.Clues |= 0x1f;
            if (person.Loc == state.InCity && person.Org == state.AtLoc)
            {
                person.Rknown |= (short)(person.Role & CastMembers);
            }
            Dossiers.ShowSuspectDossier(p, 0, 0);
            Screens.WaitForKey();
            return true;
        }
        return false;
    }

    /// <summary>The cities whose name holds the text: the first tells whether the building's organisation has a building there (file_room_search).</summary>
    /// <returns>Whether one was found.</returns>
    private bool SearchCities(string wanted)
    {
        var txt = state.MessageText;
        for (var city = 0; city < CampaignTables.WorldSize; city++)
        {
            txt.Text = state.Ct[city].Name;
            if (!UpperCaseHolds(wanted))
            {
                continue;
            }
            txt.Text = state.Org[state.AtLoc].Name;
            txt.Text += state.StrSearchHas;
            var found = RaidIntelligenceRules.OrgPresenceInCity(state, city, state.AtLoc) != 0;
            if (found)
            {
                txt.Text += state.StrSearchA;
                LearnBuilding(state.AtLoc, city);
            }
            else
            {
                txt.Text += state.StrNoOperatives;
            }
            txt.Text += state.StrSearchCityIn;
            txt.Text += state.Ct[city].Name;
            txt.Text += state.StrSearchCityEnd;
            ShowSearchAnswer(found);
            return true;
        }
        return false;
    }

    /// <summary>The organisations whose name holds the text: the first tells whether it has a building in this city (file_room_search).</summary>
    /// <returns>Whether one was found.</returns>
    private bool SearchOrgs(string wanted)
    {
        var txt = state.MessageText;
        for (var org = FirstSearchableOrg; org < CampaignTables.WorldSize; org++)
        {
            txt.Text = state.Org[org].Name;
            // BUG: CA-61 only the display name is searched, and "Stassi" does not hold "STASI"; the short key is searched too unless the original's search is kept
            // DIVERGENCE: D-650 a player's option: CA-61, the organisation's short key also matches
            if (!UpperCaseHolds(wanted) && !(behaviour.FixFileRoomShortKey && OrgKeyHolds(state.Org[org].Sname, wanted)))
            {
                continue;
            }
            txt.Text = state.Org[org].Name;
            var found = RaidIntelligenceRules.OrgPresenceInCity(state, state.InCity, org) != 0;
            if (found)
            {
                LearnBuilding(org, state.InCity);
            }
            else
            {
                txt.Text += state.StrHasNoOperatives;
            }
            txt.Text += state.StrSearchOrgIn;
            txt.Text += state.Ct[state.InCity].Name;
            txt.Text += state.StrSearchOrgEnd;
            ShowSearchAnswer(found);
            return true;
        }
        return false;
    }

    /// <summary>The organisation's building in the city becomes known: its kind is named in the text buffer and the building is linked (file_room_search).</summary>
    private void LearnBuilding(int org, int city)
    {
        CampaignLibrary.AppendBuildingType(state.MessageText, state.BuildingKinds, (c, o) => RaidIntelligenceRules.OrgPresenceInCity(state, c, o), city, org);
        state.Ct[city].OrgClues |= (short)(1 << org);
        CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, HintLink, out var count);
        state.Nloc = (short)count;
    }

    /// <summary>The answer of a search: a building found is shown in the speech balloon with its picture, else in a message box (file_room_search).</summary>
    private void ShowSearchAnswer(bool buildingFound)
    {
        if (buildingFound)
        {
            state.BalloonX = 0x64;
            state.BalloonY = 0x64;
            state.Picface = BuildingPicture;
            DrawSpeechBox();
            Screens.WaitForKey();
        }
        else
        {
            ShowMessageBox(state.MessageText, 0x64, 0x50);
        }
    }

    /// <summary>The cast members whose clue's item holds the text: the first that still has a clue to give gives it (file_room_search).</summary>
    /// <returns>Whether a clue was shown.</returns>
    private bool SearchClues(string wanted)
    {
        var txt = state.MessageText;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            var key = Dossiers.LookupClueText(member, false);
            txt[0] = '\0';
            CampaignLibrary.AppendKeyText(txt, state.Dossier.ClueLabels, state.Dossier.NumberText, state.Dossier.Hundreds, state.Dossier.Thousands, key);
            if (UpperCaseHolds(wanted) && Dossiers.RevealCastClue(member, state.AtLoc, state.InCity))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The plot objects in play whose name holds the text: the first tells the city it is now in (file_room_search).</summary>
    /// <returns>Whether one was found.</returns>
    private bool SearchPlotObjects(string wanted)
    {
        var txt = state.MessageText;
        for (var i = 0; i < PlotObjectCount; i++)
        {
            var macGuffin = state.Mc[i];
            if (macGuffin.Who == -1)
            {
                continue;
            }
            txt.Text = macGuffin.Name;
            if (!UpperCaseHolds(wanted))
            {
                continue;
            }
            txt.Text = macGuffin.Name;
            txt.Text += state.StrSearchNowIn;
            txt.Text += state.Ct[state.Ps[macGuffin.Who].Loc].Name;
            txt.Text += state.StrSearchObjectEnd;
            ShowMessageBox(txt, 0x64, 0x50);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Puts the text buffer into capitals where it stands, as the C library's <c>strupr</c> does, and tells whether it
    /// holds the text searched for (<c>strstr</c>).
    /// </summary>
    private bool UpperCaseHolds(string wanted)
    {
        var txt = state.MessageText;
        txt.Text = UpperCase(txt.Text);
        return txt.Text.Contains(wanted, StringComparison.Ordinal);
    }

    /// <summary>Whether an organisation's short key, in capitals, holds the text searched for (the player's option of CA-61).</summary>
    private static bool OrgKeyHolds(string key, string wanted) => UpperCase(key).Contains(wanted, StringComparison.Ordinal);

    /// <summary>A text in capitals as the C library's <c>strupr</c> makes it: only the letters a to z change.</summary>
    private static string UpperCase(string text) =>
        string.Concat(text.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 'a' + 'A') : c));
}
