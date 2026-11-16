using CovertAction.Audio.Amiga;
using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Chase;
using CovertAction.Core.Crypto;
using CovertAction.Core.Electronics;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Msc;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Chase;
using CovertAction.Presentation.Crypto;
using CovertAction.Presentation.Final;
using CovertAction.Core.Final;
using CovertAction.Presentation.Game;
using CovertAction.Presentation.Electronics;
using CovertAction.Presentation.Raid;
using CovertAction.Presentation.Intro;
using CovertAction.Presentation.Library;
using CovertAction.Core.Intro;

namespace CovertAction.Launcher;

/// <summary>
/// The game as one program: the player's game files, the game's own drivers on top of a host, and the parts of the
/// game started as plain method calls. This is what the launcher COVERT.EXE is in the original, which loads the
/// drivers and runs one program after the other; here a part is an object and the hand-off block between the parts
/// (<see cref="SharedState"/>) is passed to it.
/// </summary>
/// <remarks>
/// The game has the set-up screen (INTRO), the menus (FINAL), the campaign (GAME), the building raid (TAC), the
/// electronics workbench (BUG), the car chase and the cryptography workstation; <see cref="Main()"/> is the launcher's
/// own flow (COVERT). The rest is new code: the parts' start from the player's programs, and the practice sessions a
/// developer can start directly.
/// </remarks>
public sealed class Game
{
    private const string CryptoProgram = "CODE.EXE";
    private const string ChaseProgram = "CHASE.EXE";
    private const string MenusProgram = "FINAL.EXE";
    private const string CampaignProgram = "GAME.EXE";
    private const string RaidProgram = "TAC.EXE";
    private const string ElectronicsProgram = "BUG.EXE";
    private const string IntroProgram = "INTRO.EXE";

    /// <summary>The hand-off's "next part" code of the campaign (GAME).</summary>
    private const sbyte Campaign = 0;

    // the hand-off's "next part" codes, as the original's launcher reads them (the reconstructed state.h)
    private const sbyte FollowCar = 3, VariantChase = 5, Followed = 6, PracticeChase = 0x0e, Cryptography = 0x0f, MenusFresh = 0x0b, MenusBack = 0x11, MenusAfterCase = 9;
    // the hand-off's codes of the building raid (1 break-in, 7 escape, 8 jailbreak, 0xa ambush, 0xd practice) and of the electronics workbench (4 wiretap, 0xc car tracer, 0x10 practice)
    private const sbyte BreakInRaid = 1, EscapeRaid = 7, JailbreakRaid = 8, AmbushRaid = 0x0a, PracticeRaid = 0x0d;
    // the cryptography workstation from the campaign (a coded message to decode); 0x0f is its practice session
    private const sbyte DecodeMessage = 2;
    private const sbyte Wiretap = 4, CarTracerTap = 0x0c, PracticeWiretap = 0x10;
    private const string GraphicsDriverFile = "MGRAPHIC.EXE";
    private const string FontFile = "FONTS.CV";

    /// <summary>The letter of the 256-colour graphics driver, which the parts put into the names of the picture files they load.</summary>
    private const char McgaLetter = 'M';

    /// <summary>The letter of the "no sound" driver: the set-up's first sound choice.</summary>
    private const char NoSoundLetter = 'N';

    /// <summary>The hand-off's "next part" code that ends the game.</summary>
    private const sbyte LeaveGame = -1;

    /// <summary>The skills of the main menu's practice, in the order of its menu (FINAL's start_practice_module adds 0x0d for the part's code).</summary>
    private enum PracticeSkill
    {
        BreakingIn,
        Driving,
        Cryptography,
        Electronics,
    }

    private readonly GameFolder _folder;
    private readonly IHost _host;
    private readonly HostLink _link;
    private readonly Boundaries _world;
    private readonly GameFolderFiles _files;
    private readonly SoundDriverChoice _sound;
    private readonly GameOptions _options;

    /// <summary>The original's bugs the game reproduces or fixes: chosen by the two fix options, the same for every part.</summary>
    private readonly OriginalBehaviour _behaviour;
    private readonly KeyboardDriver _keyboard;

    /// <summary>Sets the game up: reads the fonts and colours from the player's files and puts the drivers on the host.</summary>
    /// <param name="folder">The player's copy of the game.</param>
    /// <param name="host">The program hosting the game: window, keys, sound device, time.</param>
    /// <param name="savesFolder">The folder the saved games are written to and read back from; null: the game cannot save.</param>
    /// <exception cref="FileNotFoundException">A file of the game is not in the folder.</exception>
    /// <exception cref="InvalidDataException">A file of the game is not what it should be.</exception>
    public Game(GameFolder folder, IHost host, string? savesFolder = null)
        : this(folder, host, savesFolder, new GameOptions())
    {
    }

    /// <summary>Sets the game up with the player's options of the launcher (new code; the defaults are the original's).</summary>
    /// <param name="folder">The player's copy of the game.</param>
    /// <param name="host">The program hosting the game: window, keys, sound device, time.</param>
    /// <param name="savesFolder">The folder the saved games are written to and read back from; null: the game cannot save.</param>
    /// <param name="options">The sound card and the controls chosen in the settings (the set-up's answers), the MT-32 ROMs' folder.</param>
    /// <exception cref="FileNotFoundException">A file of the game is not in the folder.</exception>
    /// <exception cref="InvalidDataException">A file of the game is not what it should be.</exception>
    public Game(GameFolder folder, IHost host, string? savesFolder, GameOptions options)
    {
        _options = options;
        _behaviour = options.Behaviour;
        _folder = folder;
        var fonts = new FontSet(folder.Read(FontFile));
        var driverFile = McgaDriverFile.Read(folder.Read(GraphicsDriverFile));
        Graphics = new McgaGraphicsDriver(fonts, driverFile.DefaultColours) { Traits = driverFile.Traits };
        var link = new HostLink(Graphics, host);
        // the slow routines of the graphics code (the dissolve) show their progress frame by frame
        Graphics.FrameShown = link.WaitForFrame;
        _sound = new SoundDriverChoice(folder, host, romFolder: options.Mt32RomFolder, musicLetter: options.MusicLetter, effectsLetter: options.EffectsLetter,
            amigaFolder: options.AmigaFolder, amigaOnlyWhereOriginal: options.AmigaOnlyWhereOriginal, fixTandySecondPause: options.FixOriginalBugs);
        _files = new GameFolderFiles(folder, savesFolder);
        // DIVERGENCE: D-642 a player's option: the Amiga version's pictures, read from the player's own Amiga files, in place of the PC's where the layouts agree.
        var pictures = AmigaPictures = options.AmigaPictures ? AmigaPictureSource.Open(Graphics.PictureColours, options.AmigaFolder, folder.Path) : null;
        _world = new Boundaries(Graphics, _sound, _keyboard = new KeyboardDriver(link), MouseDriver(host, options), new GameTimer(link), _files, pictures);
        _host = host;
        _link = link;
    }

    /// <summary>The game's drawing code and its screen.</summary>
    public McgaGraphicsDriver Graphics { get; }

    /// <summary>The Amiga version's pictures that replace the game's (D-642), or null: the option is off, or the Amiga files are missing.</summary>
    public AmigaPictureSource? AmigaPictures { get; }

    /// <summary>The mouse driver the game's parts get (for tests: without the mouse option it is <see cref="NoMouseDriver"/>; new code).</summary>
    internal IMouseDriver Mouse => _world.Mouse;

    /// <summary>
    /// The PC's mouse: none (the original's assumption, and every test's and the oracle's), unless the player turned the
    /// mouse on and the host has a pointer (new code).
    /// </summary>
    // DIVERGENCE: D-630 the mouse is a player option, off by default; with it off the game's world has no mouse, as the original assumes.
    private static IMouseDriver MouseDriver(IHost host, GameOptions options) =>
        options.Mouse && host.Input.Pointer is { } pointer ? new HostMouseDriver(pointer) : new NoMouseDriver();

    /// <summary>The text page the host shows while the display is in text mode (the set-up screen), else null (for tests and tools; new code).</summary>
    public TextScreen? TextPage => _link.TextPage;

    /// <summary>Raised by <see cref="Main()"/> before each part it starts, with the hand-off's "next part" code (for tests and tools; new code).</summary>
    public event Action<sbyte>? PartStarting;

    /// <summary>
    /// Raised with the arguments of every sound a part asks the sound driver for (the effect's number, and the values the
    /// car chase adds), before the driver plays it (for tests and tools; new code).
    /// </summary>
    public event Action<int[]>? SoundRequested
    {
        add => _sound.Requested += value;
        remove => _sound.Requested -= value;
    }

    /// <summary>
    /// Raised with everything the sound driver the set-up chose does to its device (the AdLib card's registers, the
    /// MPU-401's bytes, the Tandy chip's bytes, the PC speaker) and to the PC timer (its rate, each interrupt); listened to
    /// before the set-up's choice (for tests and tools: the session proof compares it with the original's port writes; new code).
    /// </summary>
    public event Action<CovertAction.Audio.SoundDeviceEvent>? SoundDeviceWritten
    {
        add => _sound.DeviceEvent += value;
        remove => _sound.DeviceEvent -= value;
    }

    /// <summary>
    /// The game from its start to the player's leaving, as the original's launcher runs it: the set-up screen, then
    /// the menus part, then every part a hand-off block names in turn (a practice, the campaign, a minigame, the
    /// menus again) until one of them leaves "leave the game" in the block.
    /// </summary>
    /// <exception cref="NotSupportedException">A part hands over to a part the rewrite does not have.</exception>
    public void Main() => Main(new SharedState());

    /// <summary>
    /// <see cref="Main()"/> on a hand-off block the caller holds, so that a tool can watch it between the parts (the
    /// session proof compares it with the original's at every checkpoint; new code).
    /// </summary>
    /// <param name="shared">A new hand-off block, as the launcher's is at its start.</param>
    /// <exception cref="InvalidDataException">A part stopped on a game file it could not read, or where the original's behaviour is undefined.</exception>
    // orig: COVERT 0000:0010 main (src/c/covert/covert_0000_0010.c) matched; tests: LauncherTests
    // DIVERGENCE: D-602 nothing is spawned or loaded: the set-up and the parts are method calls, the drivers objects made with the game; the far pointer to the hand-off block, the drivers' start and end calls and the text mode at the end have no counterpart.
    public void Main(SharedState shared)
    {
        // DIVERGENCE: D-603 the set-up screen shows its title only: the sound card and the controls are the launcher's settings; the proof asks the original's questions.
        try
        {
            RunIntro(shared);
        }
        catch (ProgramExitException)
        {
            // the player closed the window during the set-up
            return;
        }
        // the launcher loads the sound driver the set-up chose (COVERT 011a:0140, D-602)
        _sound.Choose((char)shared.SndDriver);
        shared.NextModule = MenusFresh;
        shared.Entered = 0;
        try
        {
            do
            {
                PartStarting?.Invoke(shared.NextModule);
                // BUG: CA-2 the original runs MCV.EXE through the search path for the code 0x63, which no part writes; RunPart refuses it.
                RunPart(shared);
            }
            // the host's window closed: every part has ended at its next frame, and the player has left (new code)
            while (shared.NextModule != LeaveGame && !_host.Input.QuitRequested);
        }
        catch (Exception e) when (e is UninitialisedReadException or MemoryOverrunException or DivideErrorException)
        {
            // the game reached a place where the original's behaviour is undefined (ARCHITECTURE.md, "Out of the typed
            // model"): with a game file missing that the original reads without looking (TEXT.DTA in a raid), or a saved
            // game it cannot hold. The host says it (new code; the original goes on with whatever memory holds)
            throw new InvalidDataException($"the game stopped where the original's behaviour is undefined (is a game file missing or damaged?): {e.Message}", e);
        }
    }

    /// <summary>
    /// Runs the set-up screen (the original's INTRO.EXE) once: its title with the launcher's settings as the answers
    /// (D-603), or, for the proof, the original's questions, whose answers count. Either way the answers go into the
    /// hand-off block. Its texts come from the player's INTRO.EXE.
    /// </summary>
    private void RunIntro(SharedState shared)
    {
        var state = new IntroState();
        IntroDgroup.Layout.Load(Executable.Read(_folder.Read(IntroProgram)).DataSegment(), state);
        // the set-up screen is the PC's text page, shown by the host in place of the game's picture
        _link.TextPage = new TextScreen();
        try
        {
            var setup = new HardwareSetup(state, shared, _world, _link.TextPage);
            if (_options.AskSetUpQuestions)
            {
                setup.HardwareSetupMenu();
            }
            else
            {
                setup.ShowTitle(_options.SoundLetter, _options.Joystick);
                // "Keyboard and joystick" means the joystick when one is there: with no controller found while the title
                // showed, the answer is "keyboard only", so that the menus part does not ask to centre a joystick the
                // player does not have (D-603)
                if (shared.Joystick != 0 && _host.Input.ReadJoystick() == JoystickReading.None)
                {
                    shared.Joystick = 0;
                }
            }
        }
        finally
        {
            // back to the 320x200 picture: in the original the launcher's driver set-up (COVERT 011a:0140, D-602) sets the graphics mode
            _link.TextPage = null;
            Graphics.SubCode0208(0);
        }
    }

    /// <summary>
    /// Plays one cryptography practice session, as the main menu's "Practice" starts it: a random message, all
    /// skills at their first level, at the difficulty the player chose. A developer's way into the part, past the
    /// set-up and the menus (new code; the hand-off is the menus part's own, <see cref="Practice"/>).
    /// </summary>
    /// <param name="level">The difficulty: 0 (Local Disturbance) to 3 (Global Crisis).</param>
    /// <returns>How the session ended.</returns>
    public PracticeResult PlayCryptoPractice(int level)
    {
        var shared = Practice(PracticeSkill.Cryptography, level);
        return new PracticeResult(shared.Value != 0, shared.Elapsed);
    }

    /// <summary>
    /// Plays one car chase as the main menu's "Practice" starts it: the player tails a car through a random city,
    /// all skills at their first level, at the difficulty chosen. A developer's way into the part (new code).
    /// </summary>
    /// <param name="level">The difficulty: 0 (Local Disturbance) to 3 (Global Crisis).</param>
    /// <returns>How the chase ended.</returns>
    public ChaseResult PlayChasePractice(int level)
    {
        var shared = Practice(PracticeSkill.Driving, level);
        return new ChaseResult(shared.Value, shared.Result, shared.Elapsed);
    }

    /// <summary>
    /// Starts the part of the game a hand-off block asks for, as the original's launcher runs the next program:
    /// the car chase for the codes 3 and 0x0e (tailing a car), 5 (its variant) and 6 (being followed), the
    /// cryptography workstation for 0x0f, the campaign for 0, the menus part for 0x0b, 0x11 and 9 (the code the campaign leaves when a case is over: the menus part then debriefs it). The part leaves its answer in the block.
    /// </summary>
    /// <param name="shared">The hand-off block; its "next part" code says which part.</param>
    /// <exception cref="NotSupportedException">The code names a part that is not in the rewrite yet.</exception>
    public void RunPart(SharedState shared)
    {
        // the launcher clears Num Lock before each part (the parts read the arrow keys of the number block); here, so that
        // a part run alone (a practice, the session proof) starts as under the launcher
        _world.Input.SetNumLock(false);
        _files.TakeConsoleOutput();
        switch (shared.NextModule)
        {
            case FollowCar or VariantChase or Followed or PracticeChase:
                _sound.AmigaBank = AmigaBank.Chase;
                PlayChase(shared);
                break;
            case DecodeMessage or Cryptography:
                _sound.AmigaBank = AmigaBank.Code;
                PlayCrypto(shared);
                break;
            case MenusFresh or MenusBack or MenusAfterCase:
                // the Amiga game plays no sound effect in the menus part (FINAL)
                _sound.AmigaBank = null;
                PlayMenus(shared);
                break;
            case Campaign:
                _sound.AmigaBank = AmigaBank.Game;
                PlayCampaign(shared);
                break;
            case BreakInRaid or EscapeRaid or JailbreakRaid or AmbushRaid or PracticeRaid:
                _sound.AmigaBank = AmigaBank.Tac;
                PlayRaid(shared);
                break;
            case Wiretap or CarTracerTap or PracticeWiretap:
                _sound.AmigaBank = AmigaBank.Bug;
                PlayElectronics(shared);
                break;
            default:
                throw new NotSupportedException($"the part of the game with the code {shared.NextModule} is not in the rewrite yet");
        }
    }

    /// <summary>
    /// Plays one building raid as the main menu's "Practice" starts it: a break-in at a random organisation's building
    /// in a random city, all skills at their first level, at the difficulty chosen. A developer's way in (new code).
    /// </summary>
    /// <param name="level">The difficulty: 0 (Local Disturbance) to 3 (Global Crisis).</param>
    public void PlayRaidPractice(int level) => Practice(PracticeSkill.BreakingIn, level);

    /// <summary>Plays one wiretap on the electronics workbench as the main menu's "Practice" starts it. A developer's way in (new code).</summary>
    /// <param name="level">The difficulty: 0 (Local Disturbance) to 3 (Global Crisis).</param>
    public void PlayElectronicsPractice(int level) => Practice(PracticeSkill.Electronics, level);

    /// <summary>
    /// Starts a practice of one skill with the menus part's own routine (FINAL's <c>start_practice_module</c>, on the
    /// values of the player's FINAL.EXE and random numbers seeded as the part seeds them), runs that part once and
    /// returns the hand-off block it left. The set-up screen's answers are taken as "no sound, keyboard only".
    /// </summary>
    private SharedState Practice(PracticeSkill skill, int level)
    {
        var shared = new SharedState { GfxDriver = (sbyte)McgaLetter, SndDriver = (sbyte)NoSoundLetter };
        var state = LoadMenus();
        state.GMode = (sbyte)McgaLetter;
        state.Diff = (short)level;
        state.TheSeed = LibraryRandom.SeedRandom(state.Random, _world.Timer);
        new Headquarters(state, shared, _world, _behaviour).StartPracticeModule((int)skill);
        RunPart(shared);
        return shared;
    }

    /// <summary>
    /// Runs the building raid once with a hand-off block. The part starts from the values its program has on disk (the
    /// player's TAC.EXE); the original's bugs are fixed as for a player.
    /// </summary>
    private void PlayRaid(SharedState shared)
    {
        var state = new RaidState();
        TacDgroup.Layout.Load(Executable.Read(_folder.Read(RaidProgram)).DataSegment(), state);
        // DIVERGENCE: D-610 the player option "arrow keys walk" applies to the building raid only
        _keyboard.ArrowKeysWalk = _options.ArrowKeysWalk;
        try
        {
            new BuildingRaid(state, shared, _world, _behaviour).TacInit();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the player left the game from inside the part (Alt-Q, or the host's window was closed)
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the building raid", e);
        }
        finally
        {
            _keyboard.ArrowKeysWalk = false;
        }
    }

    /// <summary>Runs the electronics workbench once with a hand-off block (the player's BUG.EXE gives its start values).</summary>
    private void PlayElectronics(SharedState shared)
    {
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(Executable.Read(_folder.Read(ElectronicsProgram)).DataSegment(), state);
        try
        {
            new ElectronicsWorkbench(state, shared, _world, _behaviour).BugInit();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the player left the game from inside the part (Alt-Q, or the host's window was closed)
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the electronics workbench", e);
        }
    }

    /// <summary>
    /// Runs the car chase once with a hand-off block. Like every part it starts from the values its program has on
    /// disk (the player's CHASE.EXE); the original's bugs are handled as the fix options say (<see cref="OriginalBehaviour.ForPlayer"/>).
    /// </summary>
    private void PlayChase(SharedState shared)
    {
        var state = new ChaseState();
        ChaseDgroup.Layout.Load(Executable.Read(_folder.Read(ChaseProgram)).DataSegment(), state);
        try
        {
            new CarChase(state, shared, _world, _behaviour).Main();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the player left the game from inside the part (Alt-Q, or the host's window was closed)
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the car chase", e);
        }
    }

    /// <summary>
    /// Runs the menus part (title, main menu, briefing) once with a hand-off block; it leaves in the block the code of the
    /// part to run next (a practice part, or the campaign). The part starts from the values of the player's FINAL.EXE.
    /// </summary>
    private void PlayMenus(SharedState shared)
    {
        var state = LoadMenus();
        try
        {
            new Headquarters(state, shared, _world, _behaviour).TitleStartup();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the player left the game from inside the part
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the menus part", e);
        }
    }

    /// <summary>
    /// A part that ended with a code other than 0 gave up on a file the way the original does (<c>fatal_error</c>,
    /// exit code 99, D-12): it printed the file's name, which the launcher is to say. The original's C library adds
    /// DOS's reason (<c>perror</c>: "No such file or directory" for a file that is not there); the rewrite says it when
    /// the file is not in the game folder.
    /// </summary>
    /// <param name="part">The part, in words.</param>
    /// <param name="exit">How it ended.</param>
    private InvalidDataException PartFailed(string part, ProgramExitException exit)
    {
        var name = _files.TakeConsoleOutput().Trim();
        var what = name.Length == 0 ? "one of its files"
            : _folder.Find(name) is null ? $"{name}: No such file or directory"
            : name;
        return new InvalidDataException($"{part} stopped on a file it could not read ({what}); the original ends there too (exit code {exit.Code})", exit);
    }

    /// <summary>The menus part's start values, from the player's FINAL.EXE.</summary>
    private FinalState LoadMenus()
    {
        var state = new FinalState();
        FinalDgroup.Layout.Load(Executable.Read(_folder.Read(MenusProgram)).DataSegment(), state);
        return state;
    }

    /// <summary>
    /// Runs the campaign part once with a hand-off block (a new case is briefed first): the agent's walk between the cities until he chooses a
    /// mission or leaves the game; the block then holds the campaign and the part to run next. Like every part it
    /// starts from the values its program has on disk (the player's GAME.EXE); the original's bugs are handled as the fix options say
    /// (<see cref="OriginalBehaviour.ForPlayer"/>).
    /// </summary>
    private void PlayCampaign(SharedState shared)
    {
        var state = new CampaignState();
        GameDgroup.Layout.Load(Executable.Read(_folder.Read(CampaignProgram)).DataSegment(), state);
        try
        {
            new CampaignPart(state, shared, _world, _behaviour).GameMain();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the case ended (the part handed over to the debriefing), or the player left the game from a box
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the campaign", e);
        }
    }

    /// <summary>
    /// Runs the cryptography part once with a hand-off block. The part starts from the values its program has on
    /// disk: its texts, tables and start values are read from the player's CODE.EXE, not kept in the rewrite.
    /// </summary>
    /// <returns>Whether the message was decoded.</returns>
    private bool PlayCrypto(SharedState shared)
    {
        var state = new CryptoState();
        CodeDgroup.Layout.Load(Executable.Read(_folder.Read(CryptoProgram)).DataSegment(), state);
        try
        {
            new CryptoWorkstation(state, shared, _world).DoCode();
        }
        catch (ProgramExitException e) when (e.Code == 0)
        {
            // the player left the game from inside the part (Alt-Q, or the host's window was closed)
        }
        catch (ProgramExitException e)
        {
            throw PartFailed("the cryptography workstation", e);
        }
        return shared.Value != 0;
    }
}

/// <summary>How a car chase ended, as the part hands it back.</summary>
/// <param name="Outcome">The chase's outcome code (the hand-off's value byte).</param>
/// <param name="Suspicion">How suspicious the quarry became, in percent (the hand-off's result byte).</param>
/// <param name="Minutes">The game time the chase took.</param>
public readonly record struct ChaseResult(int Outcome, int Suspicion, int Minutes);

/// <summary>How a practice session ended.</summary>
/// <param name="Solved">Whether the message was decoded.</param>
/// <param name="Minutes">The game time the session took, as the part reports it.</param>
public readonly record struct PracticeResult(bool Solved, int Minutes);
