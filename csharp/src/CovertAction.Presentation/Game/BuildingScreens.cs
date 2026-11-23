using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The agent at a building of the city: what he may do there (place a wiretap, break in, watch who leaves, look at the
/// data files, leave, and for the CIA's own building arrive and go inside), and the stakeout of the street in front of it.
/// A wiretap or a break-in that is to be played, a car to follow or trace, and being followed on leaving, make the
/// part hand over to another one.
/// </summary>
public sealed class BuildingScreens
{
    // What the part was entered for (entry_mode) and what it hands over to next (exit_mode, CampaignState.Quit):
    // the hand-off's codes of the parts (the reconstructed state.h).
    private const int Walking = 0, BreakIn = 1, FollowCar = 3, Wiretap = 4, Followed = 6, CarTracer = 0xc, NewCase = 0x63;

    /// <summary>The organisation of the player's own agency (its building is the CIA's headquarters); no organisation is 0.</summary>
    private const int PlayerAgency = 1, NoOrganization = 0;

    /// <summary>What a box returns when the player backs out.</summary>
    private const int BackOut = -1;

    /// <summary>The menu's own mark for the CIA building, which has no menu of its own to choose from.</summary>
    private const int HeadquartersVisit = -2;

    /// <summary>The choices of a building's menu, in the order of its lines.</summary>
    private enum BuildingChoice
    {
        PlaceWiretap,
        BreakIn,
        WatchBuilding,
        CheckData,
        Leave,
    }

    /// <summary>The choices of the stakeout, in the order of its lines.</summary>
    private enum WatchChoice
    {
        Wait,
        FollowCar,
        TraceCar,
        CheckData,
    }

    /// <summary>Alert levels (0..99) and what the guards look like: a level in steps of 16 picks the hint of the menu.</summary>
    private const int AlertPerHint = 16, MostHints = 3;

    /// <summary>An alert level above which the agent is followed when he leaves (on the harder levels).</summary>
    private const int FollowedAbove = 0x32;

    /// <summary>The suspicion a break-in's visit raises on the agent's own side, and for a double agent's city on leaving the CIA.</summary>
    private const int CiaArrivalSuspicion = 0x14, DoubleAgentSuspicion = 0x28, WiretapSuspicionPerStep = 0x32;

    /// <summary>The time a break-in's preparation takes (an hour), and the longest and shortest time a wiretap has.</summary>
    private const int Hour = 60, LongestTap = 0xa, ShortestTap = 2;

    /// <summary>The place of the building's menu.</summary>
    private const int MenuX = 2, MenuY = 0x21;

    /// <summary>What the player knows of a building whose bugs have been planted (<see cref="Core.Records.Location.Clues"/> flags of the library's find_or_add_link, if it is new).</summary>
    private const int TappedBuilding = 4;

    private readonly CampaignState state;
    private readonly SharedState shared;
    private readonly Boundaries world;
    private readonly CampaignScreens screens;
    private readonly PlotEngine plot;
    private readonly DataFilesMenus dataFiles;

    /// <summary>The building menus on the campaign, the hand-off block, the drivers and the screens of the part.</summary>
    /// <param name="state">The campaign.</param>
    /// <param name="shared">The hand-off block: what a mission handed back, and what the next one is given.</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="screens">The campaign's screens.</param>
    /// <param name="plot">The passing of time and the plot's progress.</param>
    /// <param name="dataFiles">The menus of the data files.</param>
    public BuildingScreens(CampaignState state, SharedState shared, Boundaries world, CampaignScreens screens, PlotEngine plot, DataFilesMenus dataFiles)
    {
        this.state = state;
        this.shared = shared;
        this.world = world;
        this.screens = screens;
        this.plot = plot;
        this.dataFiles = dataFiles;
        Investigation = new Investigation(state, screens, plot);
        Headquarters = new HeadquartersScreens(state, shared, world, screens, plot, dataFiles, Investigation);
    }

    /// <summary>The CIA's building.</summary>
    public HeadquartersScreens Headquarters { get; }

    /// <summary>The searches the intelligence section makes.</summary>
    public Investigation Investigation { get; }

    /// <summary>
    /// The menu of the building of organisation <see cref="CampaignState.AtLoc"/> in <see cref="CampaignState.InCity"/>,
    /// shown over and over until the agent leaves it or the part hands over. It names the organisation and the kind of
    /// building (when the player has identified it) and what the guards look like, and offers a wiretap, a break-in, a
    /// stakeout and the data files. When the agent comes back from a mission, the menu takes what the mission brought
    /// as the choice. The CIA's building has no such menu: the agent arrives and goes inside. Leaving a building
    /// that is on its guard (on the harder levels) makes the agent followed. A double agent's city makes the CIA's
    /// building a suspicious place to leave.
    /// </summary>
    // orig: GAME 00f7:1e5a building_menu (src/c/game/game_00f7_1e5a.c) matched; tests: CampaignTests
    public void BuildingMenu()
    {
        state.LeavingBuilding = 0;
        // C: the building's level is read for the picture even when the building is not identified; on the first round
        // the original's variable is then the word city_screen's pass_time(0x78) left there (pass_time waits for the
        // retrace once the log holds more than five messages, as it does from the briefing on): wait_vretrace's frame
        // pointer, which inp (GAME 00f7:f7ae) saved at that place. It is a stack offset (the stack lies at
        // DS:aa60..b260), negative as an int, so the picture is the lowest level's, as here.
        var level = 0;
        do
        {
            if (state.EntryMode is FollowCar or CarTracer or NewCase)
            {
                state.EntryMode = Walking;
            }
            var city = state.InCity;
            var org = state.AtLoc;
            // how near the organisation's home is to the agency's; nothing reads it
            state.ClosenessPercent = (short)GameLibrary.Clamp(-(WorldRules.MapDistance(state.Orgs[PlayerAgency].Climate, state.Orgs[org].Climate) * 6 - 0x63), 0, 0x63);
            var choice = org == PlayerAgency ? HeadquartersVisit : ChooseWhatToDo(city, org, ref level);
            switch ((BuildingChoice)choice)
            {
                case (BuildingChoice)HeadquartersVisit:
                    ArriveAtCiaBuilding(org, city);
                    state.LeavingBuilding = 1;
                    break;
                case BuildingChoice.WatchBuilding:
                    WatchBuildingExit(org, city);
                    if (state.AtLoc == PlayerAgency)
                    {
                        state.LeavingBuilding = 1;
                    }
                    break;
                case BuildingChoice.BreakIn:
                    BreakInto(org, city);
                    break;
                case BuildingChoice.PlaceWiretap:
                    PlaceWiretap(org, city);
                    break;
                case BuildingChoice.CheckData:
                    dataFiles.DataFilesMenu();
                    break;
                case (BuildingChoice)BackOut or BuildingChoice.Leave:
                    Leave();
                    break;
            }
        }
        while (state.LeavingBuilding == 0 && state.Quit == 0);
        if (state.AtLoc == PlayerAgency && (state.Doubles & C16.Shl(1, state.InCity)) != 0)
        {
            WorldRules.RaiseSuspicion(state, state.TheOrg1, state.InCity, DoubleAgentSuspicion);
        }
    }

    /// <summary>
    /// Shows the building's picture and menu and takes the player's choice, or, back from a mission, takes the mission's
    /// as the choice (a wiretap that was played, or a break-in).
    /// </summary>
    /// <param name="city">The city.</param>
    /// <param name="org">The organisation whose building it is.</param>
    /// <param name="level">The building's level of the last round; set when the building is identified.</param>
    /// <returns>The line chosen (a <see cref="BuildingChoice"/>), or -1 when the player backed out.</returns>
    private int ChooseWhatToDo(int city, int org, ref int level)
    {
        var words = state.BuildingWords;
        state.Txt.Text = words.YouAreAt;
        // the building's record is there: the city's menu only lists buildings that have one
        var link = BuildingGlue.FindOrAddLink(state, org, city, 0);
        // C: no record (-1) is read all the same, from the bytes in front of lc (D-689)
        if ((BuildingGlue.LinkClues(state, link) & BuildingIdentified) != 0)
        {
            state.Txt.Text += state.Orgs[org].Name;
            level = BuildingGlue.AppendBuildingType(state, city, org);
        }
        else
        {
            state.Txt.Text += words.UnknownBuilding;
        }
        // the alertness is only wanted as a number: its word is taken off the text again
        var textEnd = state.Txt.Length;
        var alert = plot.AppendAlertLevel(org, city);
        state.Txt.Text = state.Txt.Text[..textEnd];
        state.Txt.Text += (alert / AlertPerHint) switch
        {
            0 => "",
            1 => words.FewGuards,
            2 => words.ExtraGuards,
            _ => words.SwarmingGuards,
        };
        state.Txt.Text += words.DoYouPlaceWiretap;
        state.Txt.Text += words.BreakIntoBuilding;
        state.Txt.Text += words.WatchCheckLeave;
        state.Rp1.BPen = 5;
        switch (state.EntryMode)
        {
            case Walking:
                ShowBuildingScene(ref level, alert);
                return plot.PopupMenuOnCleanScreen(state.Txt.Text, MenuX, MenuY);
            case BreakIn:
                return (int)BuildingChoice.BreakIn;
            case FollowCar or Wiretap:
                // DORMANT: a car chase's mode is cleared at the top of the round; only the wiretap's reaches here
                return (int)BuildingChoice.PlaceWiretap;
            default:
                // C: the choice is a local the original sets only for these modes; no part hands back another
                throw new UninitialisedReadException($"the building menu entered with the mode {state.EntryMode}: the original reads a choice it never set");
        }
    }

    /// <summary>The building's level picks one of four animations, with its own colours; the alert level tells the script which to show.</summary>
    private void ShowBuildingScene(ref int level, int alert)
    {
        const int lowestScene = 2, lastScene = BuildingScenes - 1, statusBarY = 4, firstDigit = 7, sceneAlertVariable = 10;
        var words = state.BuildingWords;
        // the original keeps the clamped number in the same variable: an unidentified building's next round starts from it
        level = GameLibrary.Clamp(level - lowestScene, 0, lastScene);
        var scene = level;
        state.BuildingPanName.Text = words.BuildingScene;
        // the digit in the template's name is the scene's number, counted from 1
        state.BuildingPanName.Bytes[firstDigit] = (byte)('1' + scene);
        state.StatusBarY = statusBarY;
        world.Graphics.SetColorMaps(state.BuildingColorMaps[scene], state.BuildingColorMaps[scene]);
        screens.Menus.StartAnimation(state.BuildingPanName.Text, 0, 0);
        screens.Animation.SetScriptVar(sceneAlertVariable, GameLibrary.Clamp(alert / AlertPerHint + 1, 1, MostHints + 1));
    }

    private const int BuildingScenes = CampaignState.BuildingScenes;

    /// <summary><see cref="Core.Records.Location.Clues"/>: the building is identified (its organisation is known).</summary>
    private const int BuildingIdentified = 4;

    /// <summary>
    /// A break-in: when the part is to play it, an hour passes, the sneak-in picture dissolves in (a pause after it on a
    /// floppy disk's machine) and the part hands over to the raid. Back from the raid its outcome is settled.
    /// </summary>
    private void BreakInto(int org, int city)
    {
        const int statusBarY = 4, intoPicturePage = 1, dissolveWait = 0x3c, firstHardDisk = 2;
        if (state.EntryMode == Walking && state.MissionsPlayed != 0)
        {
            state.StatusBarY = statusBarY;
            plot.PassTime(Hour);
            state.Quit = BreakIn;
            screens.Pictures.LoadPictureSwapped(state.BuildingWords.BreakInPicture, intoPicturePage);
            world.Graphics.SubGame014b(intoPicturePage);
            if (state.Animation.CurrentDrive < firstHardDisk)
            {
                screens.Output.WaitVsync(dissolveWait);
            }
        }
        else
        {
            plot.ResolveInfiltration(org, city);
            state.EntryMode = Walking;
        }
    }

    /// <summary>
    /// A wiretap: when the part is to play it, the picture and the minutes the tap will have (fewer where the guards
    /// are more alert, 2 to 10) are shown and the part hands over to the electronics. Back from it, the bugs the player
    /// planted raise the building's tap, and the visit costs an hour.
    /// </summary>
    private void PlaceWiretap(int org, int city)
    {
        const int pictureBoxX = 0x14, pictureBoxY = 0x14, alertPerMinute = 5;
        var words = state.BuildingWords;
        if (state.EntryMode == Walking && state.MissionsPlayed != 0)
        {
            screens.Pictures.LoadPictureSwapped(words.WiretapPicture, 0);
            state.Txt.Text = words.GuardsAre;
            var alert = plot.AppendAlertLevel(org, city);
            var minutes = GameLibrary.Clamp(-(alert / alertPerMinute - LongestTap), ShortestTap, LongestTap);
            state.Txt.Text += words.YouWillHave;
            state.BuildingPanName.Text = minutes.ToString();
            state.Txt.Text += state.BuildingPanName.Text;
            state.Txt.Text += words.MinutesEnd;
            state.Menu.MenuPauseMode = true;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, pictureBoxX, pictureBoxY);
            state.Quit = Wiretap;
            return;
        }
        state.EntryMode = Walking;
        // The bugs planted are only known when the part is run for the missions; otherwise the original reads a stale
        // word of its stack here, which cannot happen in play (the part's start sets the flag).
        if (state.MissionsPlayed == 0)
        {
            throw new UninitialisedReadException("the number of bugs of a wiretap when the part does not play its missions (the part's start sets the flag)");
        }
        int planted = shared.Person;
        WorldRules.RaiseSuspicion(state, state.AtLoc, state.InCity, WiretapSuspicionPerStep * shared.Value);
        if (planted != 0)
        {
            var link = BuildingGlue.FindOrAddLink(state, org, city, TappedBuilding);
            state.Lc[link].Tapped += (short)planted;
        }
        plot.PassTime(Hour);
    }

    /// <summary>
    /// Leaving the building. On the harder levels, when the building is on its guard (above 50), the agent is followed:
    /// the part hands over to the chase, with the building he left in the hand-off.
    /// </summary>
    private void Leave()
    {
        const int sceneX = 0, sceneY = 0, boxX = 0x18, boxY = 0x91;
        var words = state.BuildingWords;
        state.LeavingBuilding = 1;
        if (state.Diff == 0 || plot.AppendAlertLevel(state.AtLoc, state.InCity) <= FollowedAbove)
        {
            return;
        }
        screens.Menus.StartAnimation(words.FollowedScene, sceneX, sceneY);
        state.Txt.Text = "";
        // C: the original passes no replacement and no variant (the reader takes what its stack holds); the tag does not end in '0'
        screens.LoadProseText(words.FollowedProseKey, 0x20, "", 0);
        plot.PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
        world.Graphics.SetColorMaps(state.FollowedColorMap, state.FollowedColorMap);
        shared.Value = (sbyte)state.AtLoc;
        state.Quit = Followed;
    }

    /// <summary>
    /// Arrival at the building of the player's own agency: unless back from a mission, the visit raises suspicion a
    /// little, the CIA is marked as operating in the city, the HQ picture is shown and, on the easiest level, the agent
    /// checks with his contact, who gives advice. Then the CIA building's menu runs.
    /// </summary>
    /// <param name="org">The organisation (the agency).</param>
    /// <param name="city">The city.</param>
    // orig: GAME 00f7:231a arrive_at_cia_building (src/c/game/game_00f7_231a.c) matched; tests: CampaignTests
    public void ArriveAtCiaBuilding(int org, int city)
    {
        const int animationLeft = 0xa8, arriveBoxX = 0x10, arriveBoxY = 0x11;
        var words = state.BuildingWords;
        if (state.EntryMode == Walking)
        {
            WorldRules.RaiseSuspicion(state, org, city, CiaArrivalSuspicion);
            // the city now has the organisation marked as known there
            var organizationBit = C16.Shl(1, (sbyte)state.AtLoc);
            if ((state.Ct[state.InCity].Clued & organizationBit) == 0)
            {
                state.Ct[state.InCity].Clued |= (short)organizationBit;
            }
            if (state.AtLoc == PlayerAgency)
            {
                screens.ClearScreen();
                world.Graphics.SetColorMaps(state.HeadquartersColorMap, state.HeadquartersColorMap);
                if (!screens.Menus.StartAnimation(words.HeadquartersScene, animationLeft, 0))
                {
                    state.Status &= ~Options.Animation;
                }
                if (state.Diff == 0)
                {
                    state.Txt.Text = words.ArriveAtCia;
                    plot.PopupMenuOnCleanScreen(state.Txt.Text, arriveBoxX, arriveBoxY);
                    Headquarters.ChiefAdvice();
                }
            }
        }
        if (state.AtLoc == PlayerAgency)
        {
            Headquarters.CiaBuildingMenu();
        }
        state.EntryMode = Walking;
    }

    /// <summary>
    /// The stakeout of a building: the street, and someone leaving it, picked from the organisations that are in the city
    /// (the nearer to the building's, with chance, the likelier). The player may wait, follow the person's car (the part
    /// hands over to the chase or the tailing), trace it (the car tracer), or look at the data files. Waiting costs one
    /// or two hours and brings the next person.
    /// </summary>
    /// <param name="org">The organisation whose building is watched.</param>
    /// <param name="city">The city.</param>
    // orig: GAME 00f7:23c9 watch_building_exit (src/c/game/game_00f7_23c9.c) matched; tests: CampaignTests
    public void WatchBuildingExit(int org, int city)
    {
        const int screenWidth = 0x140, screenHeight = 0xc8, panelColour = 5, stakeoutBoxX = 2, stakeoutBoxY = 0x14, hourlyWait = 2;
        var words = state.BuildingWords;
        while (true)
        {
            world.Graphics.FillRect(state.Rp1, 0, 0, screenWidth, screenHeight, panelColour);
            plot.PassTime(GameLibrary.RandomBelow(state.Random, hourlyWait) * Hour + Hour);
            var (person, organization) = PickPersonLeaving(org, city);
            var who = state.Ps[person];
            ShowPersonLeaving(who);
            state.Txt.Text = words.YouNotice;
            state.Txt.Text += words.LeavingTheBuilding;
            state.Txt.Text += words.WatchMenu;
            var choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, stakeoutBoxX, stakeoutBoxY);
            if (choice == BackOut)
            {
                return;
            }
            switch ((WatchChoice)choice)
            {
                case WatchChoice.Wait or WatchChoice.CheckData:
                    // a person the player knows nothing more of is let go
                    if (who.Clues == SeenInTheStreet)
                    {
                        CastRules.ReleasePerson(state, person);
                    }
                    if ((WatchChoice)choice == WatchChoice.CheckData)
                    {
                        dataFiles.DataFilesMenu();
                    }
                    continue;
                case WatchChoice.FollowCar:
                    HandOverTheCar(person, organization);
                    return;
                case WatchChoice.TraceCar:
                    world.Graphics.SetColorMaps(state.TraceColorMap, state.TraceColorMap);
                    screens.PlayPanPicture(words.TraceScene, 0, 0);
                    state.Quit = CarTracer;
                    state.AtLoc = (short)organization;
                    return;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// The knowledge the stakeout gives of a person (the photograph, 1, and the place, 8): a person who has only that
    /// is nobody of the cast's and is let go again.
    /// </summary>
    private const int SeenInTheStreet = 9;

    /// <summary>
    /// Picks who leaves the building: the organisation present in the city whose home is the nearest to the building's
    /// organisation (plus chance: more where the organisation has no known person, or the player already knows its
    /// building), and a person of it, or, one time in three, of the building's own organisation. Persons who are
    /// arrested or in hiding, or the first of the table, are not taken: another is looked for.
    /// </summary>
    /// <returns>The person and the organisation he is picked from.</returns>
    private (int Person, int Organization) PickPersonLeaving(int org, int city)
    {
        const int worst = 0x3e7, firstOrganization = 1, organizations = 16, nearness = 16, unknownExtra = 24, knownBuildingExtra = 16, ofBuildingsOwn = 3;
        int person;
        int picked;
        do
        {
            var best = worst;
            picked = org;
            for (var candidate = firstOrganization; candidate < organizations; candidate++)
            {
                if (WorldRules.OrgPresence(state, city, candidate) == 0)
                {
                    continue;
                }
                var score = WorldRules.MapDistance(state.Orgs[org].Climate, state.Orgs[candidate].Climate) + GameLibrary.RandomBelow(state.Random, nearness);
                if (org == NoOrganization)
                {
                    score = GameLibrary.RandomBelow(state.Random, nearness);
                }
                var local = plot.FindOrCreatePerson(candidate, city, 0, false);
                if (local == -1 || state.Ps[local].Role == 0)
                {
                    score += GameLibrary.RandomBelow(state.Random, unknownExtra);
                }
                if ((state.Ct[city].OrgClues & C16.Shl(1, candidate)) != 0)
                {
                    score += GameLibrary.RandomBelow(state.Random, knownBuildingExtra);
                }
                if (score < best && candidate != org)
                {
                    best = score;
                    picked = candidate;
                }
            }
            person = GameLibrary.RandomBelow(state.Random, ofBuildingsOwn) != 0 || org == NoOrganization
                ? plot.FindOrCreatePerson(picked, city, SeenInTheStreet, false)
                : plot.FindOrCreatePerson(org, city, SeenInTheStreet, false);
            if (person == -1)
            {
                // C: the original reads the person table in front of its start here; with a new person asked for it does not come to this
                throw new UninitialisedReadException("the person picked for the stakeout is -1 (the library gave none although one was to be made)");
            }
        }
        while (state.Ps[person].Alert < 0 || person == 0);
        return (person, picked);
    }

    /// <summary>The person who leaves, as the player sees him: the binoculars' scene, or his face drawn by hand where it cannot be played.</summary>
    private void ShowPersonLeaving(Core.Records.Person who)
    {
        const int faceX = 0x1a, faceY = 0x2d, sceneX = 0x96, sceneY = 2, personScriptVariable = 0;
        const int carBox = 0x90, carBoxHeight = 0x60, faceDrawX = 0xe0, faceDrawY = 0x20, carPictureX = 0xd6, carPictureY = 0x39, carPictures = 0x68;
        state.FaceX = faceX;
        state.FaceY = faceY;
        state.Picface = who.Face;
        world.Graphics.SetColorMaps(state.BinocularsColorMap, state.BinocularsColorMap);
        if (screens.Menus.StartAnimation(state.BuildingWords.BinocularsScene, sceneX, sceneY))
        {
            screens.Animation.SetScriptVar(personScriptVariable, (who.Face & 1) != 0 ? -1 : 1);
        }
        else
        {
            world.Graphics.FillRect(state.Rp1, 0xa8, 8, carBox, carBoxHeight, 1);
            screens.Screens.DrawFace(state.Picface, faceDrawX, faceDrawY);
            screens.Output.DrawImage(state.Sprite, (state.Picface & 3) + carPictures, carPictureX, carPictureY);
        }
    }

    /// <summary>
    /// The player follows the person's car: a box says how the person looks (his alertness) and the person and the
    /// building are handed to the chase or the tailing; the agent is then in the person's organisation's city.
    /// </summary>
    private void HandOverTheCar(int person, int organization)
    {
        const int boxX = 0x14, boxY = 0x64;
        var words = state.BuildingWords;
        var who = state.Ps[person];
        state.Txt.Text = (who.Face & 1) != 0 ? words.HeLooks : words.SheLooks;
        plot.AppendAlertLevel(who.Org, who.Loc);
        state.Txt.Text += words.PeriodLine;
        state.Menu.MenuPauseMode = true;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
        shared.Person = (sbyte)person;
        shared.Value = (sbyte)state.AtLoc;
        shared.Result = (sbyte)(who.Face & 1);
        state.Quit = FollowCar;
        state.AtLoc = (short)organization;
    }
}
