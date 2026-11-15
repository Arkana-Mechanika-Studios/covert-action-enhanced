using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The campaign's walk: the screen of the city the agent is in, with what he comes back to from a mission, the menu
/// of the places he can go to (the airport, his hotel, the buildings he knows, his contacts, the data files), the
/// hotel, the city's picture, and the flight map between the cities.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="shared">The hand-off block: what the mission just played handed back, and what the next one is given.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="plot">The passing of time and the plot's progress.</param>
/// <param name="dataFiles">The menus of the data files.</param>
/// <param name="saveScreens">The screens that save the game to a slot and load it back (the hotel's choices).</param>
public sealed class CityScreens(CampaignState state, SharedState shared, Boundaries world, CampaignScreens screens, PlotEngine plot, DataFilesMenus dataFiles, LibrarySaves saveScreens)
{
    // What the part was entered for (entry_mode, the code of the mission it last handed over to) and what it hands
    // over to next (exit_mode, CampaignState.Quit): the hand-off's codes of the parts (the reconstructed state.h).
    private const int Walking = 0, BreakIn = 1, DecodeMessage = 2, FollowCar = 3, Wiretap = 4, Followed = 6,
        EscapeRaid = 7, JailBreakRaid = 8, AmbushRaid = 0xa, CarTracer = 0xc, NewCase = 0x63;

    /// <summary>The organisation of a menu line that is no building (the original's AtLoc of 0).</summary>
    private const int NoOrganisation = 0;

    /// <summary>The exit code of the player's quitting the game from the hotel.</summary>
    private const short QuitTheGame = -1;

    /// <summary><see cref="CampaignState.Sprung"/> and the hand-off's person when there is no one.</summary>
    private const int NoOne = -1;

    /// <summary>The organisation of the player's own agency (its building is the CIA's).</summary>
    private const int PlayerAgency = 1;

    /// <summary>The hand-off's result bit: the mission was left without being played (or failed before it began).</summary>
    private const int NotPlayed = 2;

    /// <summary><see cref="Core.Records.Person.Clues"/>: the prisoner was freed by his organisation (or exchanged).</summary>
    private const int Freed = 0x40;

    /// <summary><see cref="Core.Records.Person.Alert"/> of a prisoner who got away.</summary>
    private const int GotAway = 0x63;

    /// <summary><see cref="Core.Records.Location.Clues"/>: the building is identified (its organisation is known).</summary>
    private const int BuildingIdentified = 4;

    /// <summary>Flags of <see cref="Core.Shared.CampaignLibrary.AddLink"/>: the building is found by the car tracer (and is new), or from a won fight.</summary>
    private const int FoundByTracer = 8, FoundInFight = 0xc;

    /// <summary>The kind of message recorded for a jail break, and for the agent's arrival in a city.</summary>
    private const int MessageJailBreak = 0xa, MessageArrival = 4;

    /// <summary>A clue of a contact waiting in a city: the high bytes of its key and info words.</summary>
    private const int ContactKey = 2, ContactInfo = 8;

    /// <summary>The places of the city's menu before the buildings: the airport and the hotel.</summary>
    private const int Airport = 0, Hotel = 1, FirstBuilding = 2;

    /// <summary>The number of clue records the contacts are looked for in.</summary>
    private const int ClueRecords = 0x50;

    /// <summary>The organisations whose buildings may be in a city (the first, 0, is no one's).</summary>
    private const int Organisations = 0x10;

    /// <summary>The time a visit takes, in minutes; a wound takes six hours to heal.</summary>
    private const int VisitTime = 0x78, Hour = 60, HoursPerWound = 6;

    /// <summary>Colours of the game's palette used here.</summary>
    private const int PanelColour = 5, White = 0xf, Light = 7, Black = 0, Highlight = 0xb;

    /// <summary>The row of the screen the place and clock plate goes to.</summary>
    private const short StatusBarBottom = 0xae;

    /// <summary>What a box returns when the player backs out.</summary>
    private const int BackOut = -1;

    /// <summary>The menus of the buildings the agent enters from the city screen.</summary>
    public BuildingScreens Buildings { get; } = new(state, shared, world, screens, plot, dataFiles);

    /// <summary>
    /// The screen of a city. First what the mission just played brought: a jail break to stop (then the part is left
    /// for the raid), a prisoner who broke out or not, the car tracer's building, the end of a car chase, the escape
    /// from jail, time to recover from wounds, the end of an ambush. Then the menu of the places, which on the hardest
    /// levels may be cut short by an ambush; the choice is visited (the airport ends the screen) and the screen comes
    /// back, until the part is to be left.
    /// </summary>
    /// <param name="city">The city (the one the agent is in).</param>
    // orig: GAME 00f7:1608 city_screen (src/c/game/game_00f7_1608.c) matched; tests: GameOracleTests
    public void CityScreen(int city)
    {
        int choice;
        do
        {
            state.StatusBarY = StatusBarBottom;
            if (!SettleTheMission(city))
            {
                return;
            }
            while (true)
            {
                if (ChoosePlace(city) is not { } place)
                {
                    return;
                }
                choice = place;
                if (choice != state.Mindex)
                {
                    break;
                }
                dataFiles.DataFilesMenu();
            }
            Visit(choice);
        }
        while (choice > Airport && state.Quit == 0);
    }

    /// <summary>
    /// What the mission just played brought the city screen, by the mode the part was entered with.
    /// </summary>
    /// <returns>False when the part is to be left at once (a jail break to stop, a capture, an ambush lost, a jail).</returns>
    private bool SettleTheMission(int city)
    {
        if (state.EntryMode == Walking && state.Sprung != NoOne)
        {
            AnnounceJailBreak();
            return false;
        }
        if (state.EntryMode == JailBreakRaid)
        {
            EndJailBreak();
        }
        if (state.EntryMode == CarTracer)
        {
            WorldRules.RaiseSuspicion(state, state.AtLoc, state.InCity, 0x19 * shared.Value);
            if (shared.Person >= 0x19)
            {
                AddBuilding(state.AtLoc, city, FoundByTracer);
                state.Txt.Text = state.CityWords.CarTracerBuilding;
                plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x78);
            }
            else
            {
                state.EntryMode = Walking;
            }
        }
        if (state.EntryMode == FollowCar)
        {
            EndTailing(city);
        }
        if (state.EntryMode == Followed)
        {
            if (shared.Value != 0)
            {
                CapturedAfterChase();
                return false;
            }
            // the chase was shaken off at a building of the pursuers' organisation
            state.AtLoc = shared.OrgOut;
            state.Alerted = 0;
            state.EntryMode = FollowCar;
        }
        if (state.EntryMode == EscapeRaid)
        {
            if (shared.Person == NoOne)
            {
                plot.SurrenderScene();
            }
            if (state.Quit != 0)
            {
                return false;
            }
            state.EntryMode = Walking;
        }
        RecoverFromWounds();
        return state.EntryMode != AmbushRaid || EndAmbush(city);
    }

    /// <summary>
    /// The criminals try to free a prisoner of the player's: the warning, and the part is left for the jail break
    /// (exit mode 8) with the prisoner as the raid's target.
    /// </summary>
    private void AnnounceJailBreak()
    {
        var words = state.CityWords;
        var prisoner = state.Ps[state.Sprung];
        world.Graphics.SetColorMaps(state.WarningColorMap, state.WarningColorMap);
        screens.Menus.StartAnimation(words.WarningScene, 0xa8, 0);
        state.Txt.Text = words.JailBreakIn;
        state.Txt.Text += state.Ct[prisoner.Loc].Name;
        state.Txt.Text += words.JailBreakCityEnd;
        state.Txt.Text += state.Orgs[prisoner.Org].Name;
        state.Txt.Text += words.JailBreakAgentsFree;
        CampaignTexts.AppendPersonName(state, state.Sprung);
        state.Txt.Text += words.JailBreakEnd;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
        prisoner.Clues |= Freed;
        shared.Target = C16.Char(state.Sprung);
        state.Sprung = NoOne;
        state.Quit = JailBreakRaid;
        state.AtLoc = PlayerAgency;
    }

    /// <summary>
    /// The end of the jail-break raid: a prisoner the player did not keep escapes (alerted, his organisation's
    /// building lost, the escape recorded), or the attempt was foiled.
    /// </summary>
    private void EndJailBreak()
    {
        const int male = 1, female = -1;
        var words = state.CityWords;
        int target = shared.Target;
        var prisoner = state.Ps[target];
        if (shared.Person == NoOne)
        {
            world.Graphics.SetColorMaps(state.BustOutColorMap, state.BustOutColorMap);
            screens.Menus.StartAnimation(words.BustOutScene, 0xa8, 0);
            // the scene shows a man or a woman: bit 0 of the face is the prisoner's sex
            screens.Animation.SetScriptVar(0, (prisoner.Face & 1) != 0 ? female : male);
            CampaignTexts.AppendPersonName(state, target);
            state.Txt.Text += words.Escapes;
            prisoner.Alert = GotAway;
            prisoner.Clues |= Freed;
            CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, prisoner.Org, prisoner.Loc);
            plot.QueueMessage(MessageJailBreak, target);
        }
        else
        {
            screens.Menus.ClearAnimationArea();
            state.Txt.Text += words.EscapeFoiled;
        }
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x15);
        state.EntryMode = Walking;
    }

    /// <summary>
    /// The end of tailing a car: a quarry followed home shows his building; a quarry caught is questioned; the
    /// quarry's organisation becomes as suspicious as the chase made it.
    /// </summary>
    private void EndTailing(int city)
    {
        const int followedHome = 1, caught = 2;
        if (shared.Value == followedHome)
        {
            AddBuilding(state.AtLoc, city, FoundByTracer);
        }
        else
        {
            state.EntryMode = Walking;
        }
        if (shared.Value == caught)
        {
            plot.InterrogateSuspect(shared.Person);
        }
        WorldRules.RaiseSuspicion(state, state.AtLoc, city, shared.Result);
    }

    /// <summary>The pursuers caught the player: his capture, and the part is left for the ambush (exit mode 10) at their building.</summary>
    private void CapturedAfterChase()
    {
        screens.ClearScreen();
        state.Txt.Text = "";
        // C: the original passes no replacement and no variant (the reader takes what its stack holds); the tag does
        // not end in '0', and its prose text has no "$RPLC"
        screens.LoadProseText(state.CityWords.CarCaptureProseKey, 0x20, "", 0);
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x20);
        shared.Target = C16.Char(state.AtLoc);
        state.Quit = AmbushRaid;
    }

    /// <summary>
    /// A player who came back hurt from a raid, an ambush or the escape takes six hours for each wound to recover.
    /// </summary>
    private void RecoverFromWounds()
    {
        var hurt = ((shared.Result & NotPlayed) == 0 && shared.Person != NoOne && state.EntryMode == BreakIn)
            // DORMANT: the escape's mode was set back to walking above, unless the part was left
            || state.EntryMode == EscapeRaid
            || (state.EntryMode == AmbushRaid && shared.Person != NoOne);
        if (!hurt || shared.Wounds == 0)
        {
            return;
        }
        world.Graphics.SetColorMaps(state.WoundedColorMap, state.WoundedColorMap);
        screens.Menus.StartAnimation(state.CityWords.WoundedScene, 0xe0, 0);
        state.Txt.Text = state.CityWords.RecoverFromWounds;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x15);
        for (var wound = 0; shared.Wounds > wound; wound++)
        {
            for (var hour = 0; hour < HoursPerWound; hour++)
            {
                plot.PassTime(Hour);
            }
        }
        if (state.EntryMode != AmbushRaid)
        {
            state.EntryMode = Walking;
        }
    }

    /// <summary>
    /// The end of an ambush: the organisation's person in the city calms down; an ambush lost is a note pinned to the
    /// lapel on the easiest level and a capture on the others; an ambush won shows the attackers' organisation, and
    /// its building.
    /// </summary>
    /// <returns>False when the part is to be left (the capture ended in jail, or with the escape).</returns>
    private bool EndAmbush(int city)
    {
        var words = state.CityWords;
        int attackers = shared.Target;
        state.Alerted = 0;
        var local = plot.FindOrCreatePerson(attackers, city, 0, false);
        if (local != NoOne && state.Ps[local].Alert > 0)
        {
            state.Ps[local].Alert = 0;
        }
        if (shared.Person == NoOne)
        {
            if (state.Diff == 0)
            {
                ShowProse(words.AmbushLostProseKey, state.Orgs[attackers].Name);
            }
            else
            {
                state.AtLoc = (short)attackers;
                plot.SurrenderScene();
                if (state.Quit != 0)
                {
                    return false;
                }
            }
        }
        else
        {
            ShowProse(words.AmbushWonProseKey, state.Orgs[attackers].Name);
            AddBuilding(attackers, state.InCity, FoundInFight);
        }
        state.EntryMode = Walking;
        return true;
    }

    /// <summary>A prose text of the ambushes, with the attackers' organisation for "$RPLC", in a box.</summary>
    private void ShowProse(TextBuffer key, string organisation)
    {
        state.Txt.Text = "";
        // C: the original passes no variant (the reader takes what its stack holds); the tags do not end in '0'
        screens.LoadProseText(key, 0x20, organisation, 0);
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x20);
    }

    /// <summary>
    /// The menu of the city's places: the airport, the hotel, each building the player knows of (named when
    /// identified), each contact waiting in the city, and the data files. Walking, the player chooses; back from a
    /// mission, he goes on to the building he was at (to the CIA's after decoding a message). On the harder levels a
    /// walk to a building may end in an ambush by a random organisation of the cast that is alert enough.
    /// </summary>
    /// <returns>The place chosen (the data files are the menu's last line, <see cref="CampaignState.Mindex"/>); null when an ambush leaves the part.</returns>
    private int? ChoosePlace(int city)
    {
        const int safeAlertness = 0x32, doubledSafeAlertness = 0x14;
        screens.ClearScreen();
        state.Pic = -1;
        if (state.EntryMode == Walking)
        {
            DrawCityPicture(state.InCity, 0xf0, 0x8c);
        }
        state.Rp1.BPen = PanelColour;
        plot.PassTime(0);
        ListPlaces(city);
        if (state.EntryMode != Walking)
        {
            return PlaceOfTheMission();
        }
        var choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 9);
        // BUG: CA-57 the first building's line (2) is never ambushed: "lj > 2" in the original, maybe meant as "lj >= 2"
        // C: the organisation is drawn on every walk, also when no ambush can happen
        int attackers = state.Ps[state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY)].Who].Org;
        // C: a shift by a variable count of a 16-bit int
        var threshold = (state.Doubles & C16.Shl(1, state.InCity)) != 0 ? doubledSafeAlertness : safeAlertness;
        if (state.Diff != 0 && choice > FirstBuilding && choice < state.Mindex && threshold < plot.AppendAlertLevel(attackers, state.InCity))
        {
            shared.Target = C16.Char(attackers);
            state.Quit = AmbushRaid;
            state.Alerted = 0;
            screens.ClearScreen();
            state.Txt.Text = "";
            // C: the original passes no replacement and no variant; the tag does not end in '0', its text has no "$RPLC"
            screens.LoadProseText(state.CityWords.AmbushProseKey, 0x10, "", 0);
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x18, 0x1f);
            return null;
        }
        return choice;
    }

    /// <summary>Writes the menu of the city's places into the text buffer, and what each line leads to into <see cref="CampaignState.Mitem"/>.</summary>
    private void ListPlaces(int city)
    {
        var words = state.CityWords;
        state.Txt.Text = words.YouAreIn;
        state.Txt.Text += state.Ct[state.InCity].Name;
        state.Txt.Text += words.DoYouGoTo;
        state.Txt.Text += words.TheAirport;
        state.Txt.Text += words.YourHotel;
        state.Mindex = FirstBuilding;
        for (var org = 1; org < Organisations; org++)
        {
            var building = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, 0, out _);
            if (building == -1)
            {
                continue;
            }
            state.Mitem[state.Mindex++] = (short)org;
            if ((state.Lc[building].Clues & BuildingIdentified) != 0)
            {
                state.Txt.Text += state.Orgs[org].Name;
                CampaignLibrary.AppendBuildingType(state.Txt, state.BuildingKinds, (c, o) => WorldRules.OrgPresence(state, c, o), city, org);
            }
            else
            {
                state.Txt.Text += words.UnknownBuilding;
            }
            state.Txt.Text += words.BuildingLineEnd;
        }
        for (var clue = 0; clue < ClueRecords; clue++)
        {
            var record = state.Cl[clue];
            if (record.Act == -1)
            {
                continue;
            }
            var contact = state.Ps[state.Ac[record.Act].Who];
            if (record.Key1 >> 8 == ContactKey && record.Info1 >> 8 == ContactInfo && contact.Loc == state.InCity)
            {
                CampaignTexts.AppendClueLabel(state, record.Key1);
                state.Txt.Text += words.ContactLineEnd;
                state.Mitem[state.Mindex++] = contact.Org;
            }
        }
        state.Txt.Text += words.CheckData;
        state.Mitem[state.Mindex] = -1;
    }

    /// <summary>Back from a mission the player goes on to the building he was at, or to the CIA's after decoding a message.</summary>
    /// <exception cref="UninitialisedReadException">Another mode: the original goes on with a choice its stack holds.</exception>
    private int PlaceOfTheMission()
    {
        switch (state.EntryMode)
        {
            case BreakIn or FollowCar or Wiretap or CarTracer or NewCase:
                state.Mitem[FirstBuilding] = state.AtLoc;
                return FirstBuilding;
            case DecodeMessage:
                state.Mitem[FirstBuilding] = PlayerAgency;
                return FirstBuilding;
            default:
                // C: the choice is a local the original sets only for these modes; no part hands back another
                throw new UninitialisedReadException($"the city screen entered with the mode {state.EntryMode}: the original reads a choice it never set");
        }
    }

    /// <summary>
    /// Goes to the place chosen: the hotel, or (after the time a visit takes, unless back from a raid that was not
    /// played) the building of the organisation of the line; the airport and backing out go nowhere.
    /// </summary>
    private void Visit(int choice)
    {
        if (choice == Hotel)
        {
            HotelMenu();
        }
        else if (state.EntryMode != BreakIn || (shared.Result & NotPlayed) == 0)
        {
            plot.PassTime(VisitTime);
        }
        if (choice > Hotel)
        {
            state.AtLoc = state.Mitem[choice];
            if (state.AtLoc != NoOrganisation)
            {
                Buildings.BuildingMenu();
            }
        }
    }

    /// <summary>The hotel's choices, in the order of its menu (the sixth line is empty).</summary>
    private enum HotelChoice
    {
        Leave,
        Lounge,
        Sleep,
        Save,
        Load,
        Quit = 6,
    }

    /// <summary>
    /// The hotel: the player leaves it, visits the lounge (a shady character tells what is happening in town: a clue
    /// on each free member of the cast in the city, and the city becomes more suspicious), sleeps through the case,
    /// saves or loads the game, or quits it. The menu comes back until the player leaves the hotel or the part.
    /// </summary>
    // orig: GAME 00f7:6275 hotel_menu (src/c/game/game_00f7_6275.c) matched; tests: GameOracleTests
    public void HotelMenu()
    {
        const int loungeSuspicion = 0x14, noOrganisation = 0;
        var words = state.CityWords;
        do
        {
            screens.Pictures.LoadPictureSwapped(words.HotelPicture, 0);
            state.Txt.Text = words.HotelMenu;
            var choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x28);
            switch ((HotelChoice)choice)
            {
                case (HotelChoice)BackOut:
                case HotelChoice.Leave:
                    return;
                case HotelChoice.Sleep:
                    plot.WaitInHotel();
                    break;
                case HotelChoice.Lounge:
                    state.Txt.Text = "";
                    // C: the original passes no replacement and no variant; the tag does not end in '0', its text has no "$RPLC"
                    screens.LoadProseText(words.LoungeProseKey, 0x20, "", 0);
                    plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x14, 0x50);
                    for (var member = 0; member < state.NACTIVITY; member++)
                    {
                        var cast = state.Ac[member];
                        if (state.Ps[cast.Who].Loc == state.InCity && (cast.Status & CastRules.CastMastermind) == 0)
                        {
                            screens.AddRandomClue(member, noOrganisation, state.InCity);
                        }
                    }
                    WorldRules.RaiseSuspicion(state, noOrganisation, state.InCity, loungeSuspicion);
                    break;
                case HotelChoice.Save:
                    state.Status &= ~Options.SavedInFinal;
                    shared.LevelCopy = state.Diff;
                    saveScreens.SelectSaveFile();
                    break;
                case HotelChoice.Load:
                    var before = state.Status;
                    saveScreens.SelectLoadFile();
                    // a loaded game keeps the joystick option of this session
                    state.Status = (before & Options.Joystick) | (state.Status & ~Options.Joystick);
                    plot.PassTime(0);
                    break;
                case HotelChoice.Quit:
                    state.Quit = QuitTheGame;
                    break;
            }
        }
        while (state.Quit == 0);
    }

    /// <summary>
    /// Draws the picture of a city centred on a point, framed, with "city, country" above it. The picture (the
    /// city's name without spaces, cut to eight letters, ".pic", from the archive of city pictures) is loaded only
    /// when the picture page does not hold it already.
    /// </summary>
    /// <param name="city">The city.</param>
    /// <param name="x">The centre of the picture, in pixels from the left.</param>
    /// <param name="y">The centre of the picture, in pixels from the top.</param>
    // orig: GAME 00f7:3cfd draw_city_picture (src/c/game/game_00f7_3cfd.c) matched; tests: GameOracleTests
    public void DrawCityPicture(int city, int x, int y)
    {
        const int fileNameLetters = 8, captionGap = 10;
        var words = state.CityWords;
        if (city != state.CachedCity)
        {
            state.Txt.Text = state.Ct[city].Name;
            CampaignTexts.StripSpaces(state.Txt);
            if (state.Txt.Length > fileNameLetters)
            {
                // C: the original writes a word 0 there, the end mark and the letter after it, which ".pic" covers
                state.Txt.Truncate(fileNameLetters);
            }
            state.Txt.Text += words.PictureExtension;
            LoadPictureMeasure(state.Txt.Text);
            world.Graphics.CopyRect(state.RpBack, 0, 0, state.PictureExtentX, state.PictureExtentY, state.RpPic, 0, 0);
            state.CachedCity = (short)city;
            state.CachedPictureWidth = state.PictureExtentX;
            state.CachedPictureHeight = state.PictureExtentY;
        }
        y -= state.CachedPictureHeight / 2;
        state.Txt.Text = state.Ct[city].Name;
        state.Txt.Text += words.CountrySeparator;
        state.Txt.Text += state.Ct[city].Name2;
        screens.Text.DrawTextCentered(state.Txt.Text, x, y - captionGap, White);
        x -= state.CachedPictureWidth / 2;
        // C: the frame is drawn when the last picture measured has a width, whichever city it was
        if (state.PictureExtentX != 0)
        {
            screens.Output.DrawRectOutline(x - 1, y - 1, state.CachedPictureWidth + 1, state.CachedPictureHeight + 1, White);
            world.Graphics.CopyRect(state.RpPic, 0, 0, state.CachedPictureWidth, state.CachedPictureHeight, state.Rp1, x, y);
        }
        state.Rp1.BPen = PanelColour;
    }

    /// <summary>
    /// Loads a city picture from the archive of city pictures (on disk 3) onto the back page and measures it: its
    /// width along its first row and its height down its first column, up to the first pixel of the background (on
    /// the four-colour display, the last coloured pixel of each). Without disk 3 the picture has no size.
    /// </summary>
    /// <param name="name">The picture's name in the archive.</param>
    // orig: GAME 00f7:3e38 load_picture_measure (src/c/game/game_00f7_3e38.c) matched; tests: GameOracleTests
    public void LoadPictureMeasure(string name)
    {
        const int picturesDisk = 3, programDisk = 2, onlyLook = 0, askUntilThere = 1, backPage = 1;
        const int screenWidth = 320, screenHeight = 200, widest = 0xa0, highest = 0x64;
        if (screens.Menus.CheckForDisk(picturesDisk, onlyLook) == 0)
        {
            state.PictureExtentX = state.PictureExtentY = 0;
            screens.Menus.CheckForDisk(programDisk, askUntilThere);
            return;
        }
        world.Graphics.FillRect(state.RpBack, 0, 0, screenWidth, screenHeight, Black);
        // C: the original does not look at whether the archive opened; the picture is then looked for as a file
        OpenDataFile(state.CityWords.CityArchive);
        screens.Pictures.LoadPictureSwapped(name, backPage);
        CloseDataFile();
        screens.Menus.CheckForDisk(programDisk, askUntilThere);
        if (state.CGA)
        {
            for (var x = 0; x < widest; x++)
            {
                if (world.Graphics.GetPixel(backPage, x, 0) != 0)
                {
                    state.PictureExtentX = (short)x;
                }
            }
            for (var y = 0; y < highest; y++)
            {
                if (world.Graphics.GetPixel(backPage, 0, y) != 0)
                {
                    state.PictureExtentY = (short)y;
                }
            }
            return;
        }
        // the pixel is looked at before the limit: a picture as wide as the limit has its pixel there read too
        state.PictureExtentX = 0;
        while (world.Graphics.GetPixel(backPage, state.PictureExtentX, 0) != 0 && state.PictureExtentX < widest)
        {
            state.PictureExtentX++;
        }
        state.PictureExtentY = 0;
        while (world.Graphics.GetPixel(backPage, 0, state.PictureExtentY) != 0 && state.PictureExtentY < highest)
        {
            state.PictureExtentY++;
        }
    }

    /// <summary>Opens an archive of pictures, from which the picture loader takes them while it is open.</summary>
    /// <param name="name">The archive's file name.</param>
    /// <returns>Whether it could be opened.</returns>
    // orig: GAME 00f7:d95a open_data_file (src/c/game/game_00f7_d95a.c) matched; tests: GameOracleTests
    // GAME's own routine: other code than the library's open_archive (LibraryPictures.OpenArchive), which it resembles.
    public bool OpenDataFile(string name)
    {
        // DOS's access mode in the low byte (0: to read); the high bit is the C library's, DOS does not see it
        const int readOnly = 0x8000;
        if (world.Files.DosOpen(name, readOnly, out var handle) != 0)
        {
            return false;
        }
        screens.Pictures.ArchiveHandle = handle;
        return true;
    }

    /// <summary>Closes the open archive of pictures, if there is one; a failure to close it ends the program.</summary>
    // orig: GAME 00f7:d98a close_data_file (src/c/game/game_00f7_d95a.c) matched; tests: GameOracleTests
    public void CloseDataFile()
    {
        const int noArchive = -1;
        var pictures = screens.Pictures;
        if (pictures.ArchiveHandle == noArchive)
        {
            return;
        }
        if (world.Files.DosClose(pictures.ArchiveHandle) != 0)
        {
            pictures.FatalError("");
        }
        pictures.ArchiveHandle = noArchive;
    }

    /// <summary>The first city's third letter, which tells the world the case plays in (and so its map).</summary>
    private const char EuropeLetter = 's', AfricaLetter = 'm', CentralAmericaLetter = 'g';

    /// <summary>The line of the flight map's menu for staying in the city, and the number of cities.</summary>
    private const int StayHere = -1, Cities = 0x10;

    /// <summary>
    /// The flight map: the world's map with the menu of the cities; the player moves the highlight over the cities
    /// (each is marked on the map) and picks one, stays, or goes to the data files. A flight takes the time of its
    /// distance plus an hour, is recorded, and cools the general alarm to a third.
    /// </summary>
    // orig: GAME 00f7:33aa travel_menu (src/c/game/game_00f7_33aa.c) matched; tests: GameOracleTests
    // C: the original takes the city as an argument, and uses its place as a local before reading it
    public void TravelMenu()
    {
        const int checkData = Cities, flightRate = 8, halfHour = 0x1e, boarding = 0x3c;
        while (true)
        {
            var choice = ChooseDestination();
            if (choice == state.InCity)
            {
                choice = StayHere;
            }
            if (choice >= 0 && choice < Cities)
            {
                var minutes = screens.AnimateTravel(state.InCity, choice) / flightRate * halfHour + boarding;
                state.InCity = (short)choice;
                plot.QueueMessage(MessageArrival, choice);
                plot.PassTime(minutes);
                state.Alerted = (short)(state.Alerted / 3);
                return;
            }
            if (choice != checkData)
            {
                return;
            }
            dataFiles.DataFilesMenu();
        }
    }

    /// <summary>Shows the flight map with its menu and lets the player choose.</summary>
    /// <returns>The city chosen, <see cref="StayHere"/>, or 16 for the data files.</returns>
    private int ChooseDestination()
    {
        const int menuColumns = 0x28, menuX = 0xc2, menuY = 9, menuLeft = 0xc0, menuWidth = 0x80, headingHeight = 0x10;
        const int firstLine = 0x21, lineHeight = 8, mapWidth = 0xc0, screenWidth = 320, screenHeight = 200, joystickPause = 0xa;
        LoadWorldMap();
        world.Graphics.CopyRect(state.RpBack, 0, 0, screenWidth, screenHeight, state.Rp1, 0, 0);
        ListCities();
        state.Rp1.BPen = Black;
        screens.Text.DrawWrappedText(menuColumns, menuX, menuY, Light);
        world.Graphics.RecolorRect(state.Rp1, menuLeft, menuY, menuWidth, headingHeight, Light, White);
        world.Graphics.CopyRect(state.Rp1, 0, 0, screenWidth, screenHeight, state.RpBack, 0, 0);
        var line = StayHere;
        bool chosen = false, byJoystick = false;
        do
        {
            screens.DrawMapCity(state.InCity, state.CGA ? White : Light);
            if (line >= 0 && line < Cities)
            {
                screens.DrawMapCity(line, White);
            }
            world.Graphics.RecolorRect(state.Rp1, menuLeft, (line << 3) + firstLine, menuWidth, lineHeight, Light, Highlight);
            if (byJoystick)
            {
                screens.Output.WaitVsync(joystickPause);
            }
            var previous = line;
            int key;
            do
            {
                key = screens.Input.PollJoystickKey();
            }
            while (key == -1 && world.Input.NoKeyWaiting());
            byJoystick = world.Input.NoKeyWaiting();
            if (!byJoystick)
            {
                key = screens.Input.ReadKey();
            }
            switch (key)
            {
                case Keys.Down:
                    line++;
                    break;
                case Keys.Up:
                    line--;
                    break;
                case Keys.Esc:
                    line = StayHere;
                    chosen = true;
                    break;
                case Keys.Return or Keys.Space:
                    chosen = true;
                    break;
            }
            line = GameLibrary.Clamp(line, StayHere, Cities);
            if (!chosen)
            {
                // the map without the marks, and the old line without its highlight
                world.Graphics.CopyRect(state.RpBack, 0, 0, mapWidth, screenHeight, state.Rp1, 0, 0);
                world.Graphics.RecolorRect(state.Rp1, menuLeft, (previous << 3) + firstLine, menuWidth, lineHeight, Highlight, Light);
            }
        }
        while (!chosen);
        screens.Input.FlushInput();
        state.Rp1.BPen = Light;
        return line;
    }

    /// <summary>Loads the map of the world the case plays in onto the back page.</summary>
    private void LoadWorldMap()
    {
        const int backPage = 1;
        var words = state.CityWords;
        var map = state.Ct[0].Name.Length > 2 ? state.Ct[0].Name[2] : '\0';
        switch (map)
        {
            case EuropeLetter:
                screens.Pictures.LoadPictureSwapped(words.EuropeMap, backPage);
                break;
            case AfricaLetter:
                screens.Pictures.LoadPictureSwapped(words.AfricaMap, backPage);
                break;
            case CentralAmericaLetter:
                screens.Pictures.LoadPictureSwapped(words.CentralAmericaMap, backPage);
                break;
        }
    }

    /// <summary>Writes the flight map's menu into the text buffer: stay here, the sixteen cities, the data files.</summary>
    private void ListCities()
    {
        var words = state.CityWords;
        state.Txt.Text = words.YoureIn;
        state.Txt.Text += state.Ct[state.InCity].Name;
        state.Txt.Text += words.DoYouTravelTo;
        state.Txt.Text += words.StayHere;
        for (var city = 0; city < Cities; city++)
        {
            state.Txt.Text += words.CityIndent;
            state.Txt.Text += state.Ct[city].Name;
            state.Txt.Text += words.CityLineEnd;
        }
        state.Txt.Text += words.TravelCheckData;
    }

    /// <summary>Adds a building of an organisation in a city to the ones the player knows (the library's find_or_add_link).</summary>
    private void AddBuilding(int org, int city, int flags)
    {
        CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, flags, out var count);
        state.Nloc = (short)count;
    }
}
