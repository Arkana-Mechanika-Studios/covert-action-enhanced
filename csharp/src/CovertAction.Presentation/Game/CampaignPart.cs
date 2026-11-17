using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The campaign part of the game (the original's GAME program): the agent's walk from city to city between the
/// missions. The launcher runs it with the hand-off block, from which it takes the campaign, and in which it leaves
/// the campaign and the mission to run next.
/// </summary>
public sealed class CampaignPart
{
    // what the part hands over to (exit_mode): the hand-off's codes of the parts (the reconstructed state.h)
    private const int BreakIn = 1, FollowCar = 3, EscapeRaid = 7, JailBreakRaid = 8, Debriefing = 9, AmbushRaid = 0xa,
        DebriefingToo = 0xb, PracticeRaid = 0xd;

    /// <summary>The mode of the part's first entry into a case (entry_mode).</summary>
    private const short NewCase = 0x63;

    /// <summary>The organisation of the agent's side (the CIA), whose building a new case starts in.</summary>
    private const short AgentsOrg = 1;

    /// <summary>The hand-off's outcome of a tailing in which the quarry was caught, and its result bit of a raid not played.</summary>
    private const int QuarryCaught = 2, RaidNotPlayed = 2;

    /// <summary>The disks of the next part: the missions' disk, the menus' disk.</summary>
    private const int MissionsDisk = 3, MenusDisk = 1, AskUntilThere = 1;

    /// <summary>The font of the boxes, and the colour behind letters.</summary>
    private const int MenuFont = 1, White = 0xf;

    private readonly CampaignState state;
    private readonly SharedState shared;
    private readonly Boundaries world;
    private readonly CampaignScreens screens;
    private readonly CampaignSaves saves;
    private readonly PlotEngine plot;

    /// <summary>The part on its variables, the hand-off block and the outside world.</summary>
    /// <param name="state">The part's variables, as the program's initialised data has them.</param>
    /// <param name="shared">The hand-off block: the campaign comes in and goes out through it.</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce.</param>
    public CampaignPart(CampaignState state, SharedState shared, Boundaries world, OriginalBehaviour behaviour)
    {
        this.state = state;
        this.shared = shared;
        this.world = world;
        screens = new CampaignScreens(state, world, shared, behaviour);
        saves = new CampaignSaves(state, shared, world);
        plot = new PlotEngine(state, shared, world, screens, saves, behaviour);
        // Alt-Q confirmed in one of the campaign's boxes saves the campaign and shows the farewell picture
        // (GAME 00f7:c6ae popup_menu calls save_and_hand_off after the confirmed Alt-Q)
        screens.Menus.LeaveTheGame = plot.SaveAndHandOff;
        DataFiles = new DataFilesMenus(state, screens, plot, shared);
        // the library's save screens on the campaign's own load and save routines and its box
        var saveScreens = saves.SaveScreens(screens.Input, screens.Menus, plot.PopupMenuOnCleanScreen);
        Cities = new CityScreens(state, shared, world, screens, plot, DataFiles, saveScreens);
        // the investigation is the building menus' own; the briefing searches with it
        Briefing = new CaseBriefing(state, world, screens, plot, Cities.Buildings.Investigation);
    }

    /// <summary>The passing of time and the plot's scenes (the capture, the interrogation).</summary>
    public PlotEngine Plot => plot;

    /// <summary>The start of a case: the hidden turns and the chief's briefing.</summary>
    public CaseBriefing Briefing { get; }

    /// <summary>The city screen, the hotel and the flight map.</summary>
    public CityScreens Cities { get; }

    /// <summary>The menus of the data files.</summary>
    public DataFilesMenus DataFiles { get; }

    /// <summary>The library's picture and archive files, as this part opens them.</summary>
    public LibraryPictures Pictures => screens.Pictures;

    /// <summary>
    /// The campaign part from start to end: takes the campaign from the hand-off block; on the first entry into a
    /// case plays the first hidden turns and gives the briefing (<see cref="CaseBriefing"/>), otherwise lets the time the mission took pass (rounded up to half hours; not after a
    /// tailing that caught the quarry, nor after a raid that was not played). Then the agent walks: the city screen and
    /// the flight map by turns, until a mission or the end of the game is chosen. Last, the disk of the next part is
    /// asked for, and the part saves the campaign into the hand-off block and hands over.
    /// </summary>
    /// <exception cref="ProgramExitException">The case ended (the part handed over to the debriefing), or the player quit from a box.</exception>
    // orig: GAME 00f7:005a game_main (src/c/game/game_00f7_005a.c) matched; tests: GameOracleTests, CampaignTests
    public void GameMain()
    {
        const int halfHour = 0x1e, roundUp = halfHour - 1, oneMomentX = 0x64, oneMomentY = 2;
        // DIVERGENCE: D-2 the original attaches the graphics, input and sound drivers here (attach_driver_segment three times).
        state.Rp1.FontNr = MenuFont;
        state.Rp1.BPen = White;
        screens.InitSpriteSheets();
        world.Graphics.SetPalette(state.EgaPalette);
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
        saves.LoadGame(state.CityWords.HandOffLoad, plot.PopupMenuOnCleanScreen);
        LibraryRandom.SeedRandom(state.Random, world.Timer);
        screens.ClearScreen();
        state.Pic = -1;
        state.MissionsPlayed = 1;
        if (shared.Entered == 0)
        {
            state.Sprung = -1;
            Briefing.StartCaseBriefing();
            shared.Entered = 1;
            state.EntryMode = NewCase;
            state.AtLoc = AgentsOrg;
            // the agent starts with the sound and the picture animations on
            state.Status |= Options.Sound | Options.Animation;
        }
        else
        {
            state.Diff = shared.LevelCopy;
            state.EntryMode = shared.GameMode;
            screens.DrawPlaceClock();
            var missionFree = (state.EntryMode == FollowCar && shared.Value == QuarryCaught)
                || (state.EntryMode == BreakIn && (shared.Result & RaidNotPlayed) != 0);
            if (!missionFree)
            {
                // C: the minutes are rounded up to half hours as an unsigned 16-bit number
                plot.PassTime(C16.Int(C16.UDiv(C16.Unsigned(shared.Elapsed + roundUp), halfHour) * halfHour));
            }
            state.Begun = 1;
        }
        shared.NextModule = -1;
        screens.Input.FlushInput();
        do
        {
            Cities.CityScreen(state.InCity);
            if (state.Quit == 0)
            {
                Cities.TravelMenu();
            }
        }
        while (state.Quit == 0);
        AskForTheNextDisk();
        state.Menu.MenuPauseMode = true;
        // the "one moment" text that lies in front of the persons table
        plot.PopupMenuOnCleanScreen(state.TextBeforePersons, oneMomentX, oneMomentY);
        plot.SaveAndHandOff();
    }

    /// <summary>Asks for the disk of the part handed over to: the missions' disk for a raid, the menus' for the debriefing.</summary>
    private void AskForTheNextDisk()
    {
        switch (state.Quit)
        {
            case BreakIn or EscapeRaid or JailBreakRaid or AmbushRaid or PracticeRaid:
                screens.Menus.CheckForDisk(MissionsDisk, AskUntilThere);
                break;
            case Debriefing or DebriefingToo:
                screens.Menus.CheckForDisk(MenusDisk, AskUntilThere);
                break;
        }
    }
}
