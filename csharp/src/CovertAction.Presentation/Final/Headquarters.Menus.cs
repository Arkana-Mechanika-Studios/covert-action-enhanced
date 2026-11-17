using CovertAction.Core;
using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Platform;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Final;

public sealed partial class Headquarters
{
    // the hand-off's "next part" codes this part reads and writes (the reconstructed state.h)
    private const sbyte FreshStart = 0x0b, MenusAgain = 0x11, CampaignPart = 0, LeaveGame = -1, RaidPart = 0x0d;
    private const char CgaLetter = 'C', NoSoundLetter = 'N';
    private const int FirstEntry = 1, ReturnEntry = 0, HandOffEntry = -1;
    private const int TitleFont = 1, TitleBackground = 8, MenuBackground = 5, TheMenuX = 0x50, TheMenuY = 0x50;
    private const int JoystickBoxX = 0x50, JoystickBoxY = 0x5b, JoystickPauseFrames = 0x3c;
    private const int LabsPage = 0, HallOfFameWhole = -1, NoChoice = -1, SoundTitle = 2, SoundMenu = 1;
    private const int GenderBoxX = 0x60, GenderBoxY = 0x21, LevelBoxX = 0x60, LevelBoxY = 0x81, HardestLevel = 3;
    private const int ScreenBackground = 5, FirstAgentRank = 0x63, NoRecord = 0xff;

    /// <summary>What the title and the main menu lead to.</summary>
    private enum Start
    {
        /// <summary>A new agent: the briefing of his first case, then the apartment.</summary>
        NewAgent,
        /// <summary>Back from a finished case (or a loaded game saved after one): the next case's briefing if the last one ended.</summary>
        AfterCase,
        /// <summary>The apartment scene, as for a new agent whose briefing is over.</summary>
        Apartment,
        /// <summary>A practice session or a loaded game: the hand-off is written already.</summary>
        HandedOff,
    }

    /// <summary>
    /// The menus part from its start to its end: it sets the display up, reads the campaign back when it was entered
    /// from a case or from the title, shows the title and credits, and offers the main menu (create a new agent, load
    /// a saved game, practise a skill, review the Hall of Fame); then it writes the hand-off for the part that comes
    /// next (the campaign, or a practice part) and ends.
    /// </summary>
    /// <remarks>
    /// It was entered with the code 0x0b (fresh start, from the intro), 0x11 (back from a part) or, after a finished
    /// case, with the campaign's own code; it ends with the code of the part to run next in the hand-off block.
    /// </remarks>
    // orig: FINAL 0100:0056 title_startup (src/c/final/final_0100_0056.c) matched; tests: MenusTests
    // DIVERGENCE: D-2 the three attach_driver_segment-like calls at the start (FINAL 0000:05b9, one per driver segment of the hand-off block) are left out.
    public void TitleStartup()
    {
        var shared = _shared;
        var state = _state;
        _world.Graphics.SubCode0208(0);
        state.Rp1.FontNr = TitleFont;
        state.Rp1.BPen = TitleBackground;
        _world.Graphics.SetPalette(state.EgaPalette);
        _world.Graphics.SetColorMaps(state.TitleColorMapA, state.TitleColorMapB);
        state.TheSeed = LibraryRandom.SeedRandom(state.Random, _world.Timer);
        state.Pic = -1;
        state.MissionsPlayed = 1;
        if (shared.NextModule == FreshStart)
        {
            state.EntryMode = FirstEntry;
            state.GMode = shared.GfxDriver;
            state.CGA = state.GMode == CgaLetter;
        }
        if (shared.NextModule == MenusAgain)
        {
            state.EntryMode = HandOffEntry;
            HandOff.ReadEnvState(state.TitleHandOffFiles[0]);
        }
        shared.NextModule = LeaveGame;
        state.Status &= ~Options.Practice;
        if (state.EntryMode == FirstEntry)
        {
            SetUpPlayersDevices();
            Pictures.LoadPictureSwapped(state.TitleTexts[(int)TitleText.LabsPicture], LabsPage);
        }
        Pictures.LoadPictures(state, state.SpriteTable, state.FacesFileNames[0], state.FacesFileNames[1]);

        Start start;
        if (state.EntryMode == ReturnEntry)
        {
            HandOff.ReadEnvState(state.TitleHandOffFiles[1]);
            state.Status |= Options.Animation;
            DebriefWrapup();
            start = Start.AfterCase;
        }
        else
        {
            if (state.EntryMode == FirstEntry)
            {
                ShowTitle();
            }
            start = MainMenu();
        }
        RunStart(start);

        // the part that runs next: its pictures may be on another disk
        _world.Graphics.SetPalette(state.EgaPalette);
        Menus.CheckForDisk(shared.NextModule == RaidPart ? 3 : 2, 1);
        ShutdownHook();
    }

    /// <summary>Takes over what the launcher found out about the PC: the sound and the joystick (which is calibrated by the player).</summary>
    private void SetUpPlayersDevices()
    {
        var state = _state;
        // sound and animation on; the sound goes off again for the "no sound" driver
        state.Status |= Options.Sound | Options.Animation;
        if (_shared.SndDriver == NoSoundLetter)
        {
            state.Status &= ~Options.Sound;
        }
        if (_shared.Joystick != 0)
        {
            _world.Input.CalibrateJoystick();
            state.Status |= Options.Joystick;
            foreach (var corner in new[] { TitleText.JoystickUpperLeft, TitleText.JoystickLowerRight })
            {
                state.Txt.Text = state.TitleTexts[(int)corner];
                Menus.QuitPrompt(state.Txt, JoystickBoxX, JoystickBoxY);
                Input.FlushInput();
                if (corner == TitleText.JoystickUpperLeft)
                {
                    Output.WaitVsync(JoystickPauseFrames);
                }
            }
        }
    }

    /// <summary>The title animation and, when the player presses no key, the credits.</summary>
    private void ShowTitle()
    {
        Output.DoSound(SoundTitle);
        _world.Graphics.SetColorMaps(_state.TitlePanColorMap, _state.TitlePanColorMap);
        PlayPanPicture(_state.TitleTexts[(int)TitleText.TitleAnimation], 0, 0);
        if (_world.Input.NoKeyWaiting())
        {
            PlayPanPicture(_state.TitleTexts[(int)TitleText.CreditsAnimation], 0, 0);
        }
    }

    /// <summary>The practice the player started is the car chase on its own (D-661): the launcher runs the chase part without a campaign. False otherwise.</summary>
    public bool StandaloneChase { get; private set; }

    /// <summary>The practice menu's driving choice (the order of the four skills).</summary>
    private const int DrivingPractice = 1;

    /// <summary>The main menu, until the player has chosen a way to go on.</summary>
    private Start MainMenu()
    {
        var state = _state;
        while (true)
        {
            state.Rp1.BPen = MenuBackground;
            state.Txt.Text = state.TitleTexts[(int)TitleText.MainMenu];
            var choice = MenuChoice(state.Txt, TheMenuX, TheMenuY);
            state.Rp1.BPen = MenuBackground;
            Output.DoSound(SoundMenu);
            switch (choice)
            {
                case NoChoice:
                case 0:
                    ChooseGender();
                    return Start.NewAgent;
                case 1:
                    if (LoadSavedGame() is { } loaded)
                    {
                        return loaded;
                    }
                    break;
                case 2:
                    var skill = ChooseDifficulty();
                    if (skill != NoChoice)
                    {
                        // DIVERGENCE: D-661 the car chase on its own is the driving practice's hand-off, run without a campaign
                        StandaloneChase = skill == FreeChaseChoice;
                        StartPracticeModule(StandaloneChase ? DrivingPractice : skill);
                        return Start.HandedOff;
                    }
                    break;
                case 3:
                    ReadFameFile();
                    HallOfFameScreen(HallOfFameWhole);
                    break;
                default:
                    // C: a choice the menu cannot give leaves the switch for the apartment scene
                    return Start.Apartment;
            }
        }
    }

    /// <summary>
    /// "Load a Saved Game": the player chooses a slot, whose campaign is read; one saved at the end of a case goes on
    /// with the debriefing's next step, any other is handed to the campaign at once.
    /// </summary>
    /// <returns>Where to go on, or null to return to the main menu (cancelled or the slot is empty).</returns>
    private Start? LoadSavedGame()
    {
        var state = _state;
        var kept = state.Status;
        if (Saves.SelectLoadFile() == NoChoice)
        {
            return null;
        }
        // the joystick option is the PC's, not the saved game's
        state.Status = (kept & Options.Joystick) | (state.Status & ~Options.Joystick);
        if ((state.Status & Options.SavedInFinal) != 0)
        {
            return Start.AfterCase;
        }
        _shared.NextModule = CampaignPart;
        _shared.Entered = 1;
        _shared.GameMode = 0;
        _shared.Elapsed = 0;
        HandOff.WriteEnvState(state.TitleHandOffFiles[2]);
        return Start.HandedOff;
    }

    /// <summary>The scenes that lead to a new case, and then the hand-off to the part that plays it (or to practice).</summary>
    private void RunStart(Start start)
    {
        var state = _state;
        if (start == Start.AfterCase)
        {
            // the next case is briefed when the last one ended with the MasterMind's arrest, or there was none
            if (state.Ps[0].Alert == -1 || state.Pl.Type == 0)
            {
                StartCaseBriefing();
            }
            start = Start.Apartment;
        }
        else if (start == Start.NewAgent)
        {
            StartCaseBriefing();
            start = Start.Apartment;
        }
        if (start == Start.Apartment)
        {
            ApartmentScene();
            _shared.NextModule = CampaignPart;
            _shared.Entered = 0;
            state.Sleepy = -1;
        }
        if (_shared.Entered != 0)
        {
            return;
        }
        // a new case is laid out; when the setup did not go on to the briefing's end the agent trains first
        SetupNewCase();
        // C: the original compares the first of the script variables (its symbol is the array's start)
        if (state.ScriptVars[0] != 1)
        {
            state.Diff = 0;
            var skill = TrainingMenu(1);
            StartPracticeModule(skill == NoChoice ? 0 : skill);
        }
        else
        {
            HandOff.WriteEnvState(state.TitleHandOffFiles[3]);
        }
    }

    /// <summary>
    /// The creation of a new agent: resets the career, shows the two agents to choose from (Maxine is a woman agent),
    /// asks for the code name and the level of play, and opens the training screen.
    /// </summary>
    // orig: FINAL 0100:067e choose_gender (src/c/final/final_0100_067e.c) matched; tests: MenusTests
    public void ChooseGender()
    {
        var state = _state;
        var career = state.Pl;
        career.VPs = 0;
        career.Ncase = 0;
        career.History[0] = NoRecord;
        career.Bigs = 0;
        career.Rank = FirstAgentRank;
        Pictures.LoadPictureSwapped(state.TitleTexts[(int)TitleText.GenderPicture], PicturePage);
        _world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
        state.Txt.Text = state.TitleTexts[(int)TitleText.GenderMenu];
        if (MenuChoice(state.Txt, GenderBoxX, GenderBoxY) == 1)
        {
            state.Status |= Options.Female;
        }
        AskCodeName();
        state.Txt.Text = state.TitleTexts[(int)TitleText.LevelMenu];
        state.Diff = (short)GameLibrary.Clamp(MenuChoice(state.Txt, LevelBoxX, LevelBoxY), 0, HardestLevel);
        TrainingMenu(0);
    }

    /// <summary>Plays an animation file over the screen as it is (the title and the credits), then restores the pen and the palette.</summary>
    /// <param name="fileName">The animation's file.</param>
    /// <param name="x">Left edge of the animation.</param>
    /// <param name="y">Top edge of the animation.</param>
    // orig: FINAL 0100:0434 play_pan_picture (src/c/final/final_0100_0434.c) matched; tests: MenusTests
    // Other code under the same name: GAME 00f7:023e (not this method; docs/source-map/shared-functions.csv).
    public void PlayPanPicture(string fileName, int x, int y)
    {
        var state = _state;
        const int speedFromFile = -1;
        _world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        state.PanPictureName.Text = fileName;
        var pen = state.Rp1.BPen;
        Animation.PlayPanFile(state.PanPictureName.Text, speedFromFile, x, y);
        state.Rp1.BPen = pen;
        _world.Graphics.SetPalette(state.EgaPalette);
        _world.Graphics.ShowPage(0);
    }

    /// <summary>
    /// Makes the save file named by the save path and writes the campaign to it; a file that cannot be made is said in a
    /// box ("File Error"). While the game is written, a box says so without waiting for a key.
    /// </summary>
    /// <param name="path">The file's name (the library's save path).</param>
    /// <returns>Whether the file could be made.</returns>
    // orig: FINAL 0100:a055 write_save_game (src/c/final/final_0100_a055.c) matched; tests: LibraryOracleTests
    public bool WriteSaveGame(string path)
    {
        const int extensionAt = 6, mayWrite = 0x80, errorBoxX = 0x64, errorBoxY = 0x50, progressBoxX = 4, progressBoxY = 0x64;
        var saves = _state.Saves;
        saves.SavePath = saves.SavePath[..extensionAt] + saves.Texts[(int)SaveText.WriteExtension];
        path = saves.SavePath;
        saves.SlotFileHandle = (short)_world.Files.Open(path, OpenMode.Write | OpenMode.Create | OpenMode.Truncate | OpenMode.Binary, mayWrite);
        if (saves.SlotFileHandle == -1)
        {
            _state.Txt.Text = saves.Texts[(int)SaveText.FileError] + path + saves.Texts[(int)SaveText.LineBreak];
            MenuChoice(_state.Txt, errorBoxX, errorBoxY);
            return false;
        }
        _world.Files.Close(saves.SlotFileHandle);
        // the progress is only drawn: the box does not wait for a key
        _state.MenuDrawOnly = true;
        _state.Txt.Text = path + saves.Texts[(int)SaveText.SaveInProgress];
        MenuChoice(_state.Txt, progressBoxX, progressBoxY);
        HandOff.WriteEnvState(path);
        return true;
    }
}
