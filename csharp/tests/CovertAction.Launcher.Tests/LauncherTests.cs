using CovertAction.Content;
using CovertAction.Host.Headless;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The launcher's own flow (COVERT's main) on the headless host with the player's game files (skipped without them):
/// the set-up screen, the menus, the parts the hand-off blocks ask for, until the player leaves.
/// </summary>
public sealed class LauncherTests
{
    private const sbyte MenusFresh = 0x0b, MenusBack = 0x11, PracticeCryptography = 0x0f;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "COVERT.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    private static readonly int[] Pause = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];

    /// <summary>Each key with a few moments without a key before it (the parts throw away keys pressed early, and animate).</summary>
    private static IEnumerable<int> Paced(params int[] keys) => keys.SelectMany(key => Pause.Append(key));

    /// <summary>
    /// The whole way of a player who starts the game and practises: the set-up's title (the launcher's settings: no
    /// sound, keyboard only), the title and the main menu, "Practice" of the cryptography at the first level, the
    /// session given up, back to the main menu, and the game left with Alt-Q. Nothing throws, and the parts come in that order.
    /// </summary>
    [SkippableFact]
    public void TheGameStartsWithTheSetUpsTitleGoesThroughTheMenusToAPracticeAndBackAndCanBeLeft()
    {
        int[] keys =
        [
            // the title is ended by a key; then Practice (third choice), level 0, the third skill (cryptography)
            .. Paced(HostKeys.Space, HostKeys.Down, HostKeys.Down, HostKeys.Enter, HostKeys.Enter, HostKeys.Down, HostKeys.Down, HostKeys.Enter),
            // at the workstation: give up (Esc), and a key for the box that says so
            .. Paced(HostKeys.Escape, HostKeys.Enter),
            // back in the main menu: Alt-Q, and "Yes" (the second answer) to leaving the game
            .. Paced(HostKeys.AltQ, HostKeys.Down, HostKeys.Enter),
            .. Pause,
        ];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host, null, new GameOptions(SoundLetter: 'N', Joystick: false));
        var parts = new List<sbyte>();
        game.PartStarting += parts.Add;

        game.Main();

        Assert.Equal([MenusFresh, PracticeCryptography, MenusBack], parts);
        Assert.True(host.Frames > 0);
        // the set-up's title was shown as the text page (the frame's top left corner is on it); the menus then took the display back to the picture
        const byte frameTopLeft = 0xc9;
        Assert.Equal(frameTopLeft, host.TextPage.Characters[1 * TextScreen.Columns + 1]);
        Assert.False(host.ShowingText);
        // the player left from the menu (the hand-off's "leave the game"), before the script's keys ran out
        Assert.False(host.QuitRequested, "the game ended because the host had no more keys, not by the player's leaving");
    }

    /// <summary>
    /// The same way with the joystick: the launcher's "Keyboard and joystick" (the default), the menus part calibrates
    /// the stick at rest (a key answers each corner box), and in the main menu the stick pushed down twice and the first
    /// button choose "Practice" (the third choice) instead of the keys. The stick is a controller's as the headless host's
    /// game port gives it, changed as the game takes the entries of the key list.
    /// </summary>
    [SkippableFact]
    public void TheJoystickChosenInTheSettingsMovesThroughTheMainMenu()
    {
        var rest = JoystickReading.FromStick(0, 0, 0);
        var down = JoystickReading.FromStick(0, 1, 0);
        var firstButton = JoystickReading.FromStick(0, 0, buttonsHeld: 1);
        var entries = new List<(int Key, JoystickReading Stick)>();
        void Keys(IEnumerable<int> keys) => entries.AddRange(keys.Select(key => (key, rest)));
        // the two corner boxes of the calibration; the title
        Keys(Paced(HostKeys.Enter, HostKeys.Enter, HostKeys.Space));
        // the menu comes up: a few moments more before the stick moves
        Keys([.. Pause, .. Pause, .. Pause]);
        // the main menu: the stick pushed down for a moment and let go, twice; then the first button pressed and let go
        (int, JoystickReading)[] moment(JoystickReading stick) => [(HeadlessHost.NoKeyYet, stick), (HeadlessHost.NoKeyYet, rest), (HeadlessHost.NoKeyYet, rest)];
        entries.AddRange([.. moment(down), .. moment(down), .. moment(firstButton)]);
        // level 0, the cryptography; given up; Alt-Q, "Yes"
        Keys(Paced(HostKeys.Enter, HostKeys.Down, HostKeys.Down, HostKeys.Enter, HostKeys.Escape, HostKeys.Enter, HostKeys.AltQ, HostKeys.Down, HostKeys.Enter));
        Keys(Pause);
        var host = new HeadlessHost(entries.Select(e => e.Key), unixSeconds: 600_000_000) { Joystick = rest };
        var taken = 0;
        host.EntryTaken += _ => host.Joystick = taken < entries.Count ? entries[taken++].Stick : rest;
        var game = new Game(GameFiles(), host, null, new GameOptions(SoundLetter: 'N'));
        var parts = new List<sbyte>();
        game.PartStarting += parts.Add;
        var shared = new CovertAction.Core.Shared.SharedState();

        game.Main(shared);

        Assert.Equal((sbyte)1, shared.Joystick);
        Assert.Equal([MenusFresh, PracticeCryptography, MenusBack], parts);
        Assert.False(host.QuitRequested, "the game ended because the host had no more keys, not by the player's leaving");
    }

    /// <summary>
    /// The set-up screen as the player's game shows it (D-603): the framed title and "One Moment Please..." for two
    /// seconds of the display's frames, without a key read (the first key of the script is the title's), and the
    /// hand-off block gets the MCGA driver (D-600), the launcher's sound card and its controls; then the menus come.
    /// </summary>
    [SkippableFact]
    public void TheSetUpShowsItsTitleForTwoSecondsAndTakesTheAnswersFromTheSettings()
    {
        int[] keys = [.. Paced(HostKeys.Space, HostKeys.AltQ, HostKeys.Down, HostKeys.Enter), .. Pause];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host, null, new GameOptions(SoundLetter: 'I', Joystick: false));
        var taken = 0;
        host.EntryTaken += _ => taken++;
        var shared = new CovertAction.Core.Shared.SharedState();
        (int Taken, int Frames, long Milliseconds, string? Wait, sbyte Sound, sbyte Graphics, sbyte Joystick)? atMenus = null;
        game.PartStarting += code => atMenus ??= (taken, host.Frames, host.Milliseconds,
            game.TextPage is { } page ? System.Text.Encoding.Latin1.GetString(page.Characters, 16 * TextScreen.Columns + 29, 20) : null,
            shared.SndDriver, shared.GfxDriver, shared.Joystick);

        game.Main(shared);

        Assert.NotNull(atMenus);
        var seen = atMenus.Value;
        Assert.Equal(0, seen.Taken);
        Assert.InRange(seen.Frames, CovertAction.Presentation.Intro.HardwareSetup.TitleFrames, CovertAction.Presentation.Intro.HardwareSetup.TitleFrames + 2);
        Assert.InRange(seen.Milliseconds, 1900, 2100);
        Assert.Equal(((sbyte)'I', (sbyte)'M', (sbyte)0), (seen.Sound, seen.Graphics, seen.Joystick));
        // the page as the host showed it: the title and the last words, no question
        const byte frameTopLeft = 0xc9;
        Assert.Equal(frameTopLeft, host.TextPage.Characters[1 * TextScreen.Columns + 1]);
        Assert.Equal("One Moment Please...", System.Text.Encoding.Latin1.GetString(host.TextPage.Characters, 16 * TextScreen.Columns + 29, 20));
        Assert.DoesNotContain("Select", System.Text.Encoding.Latin1.GetString(host.TextPage.Characters), StringComparison.Ordinal);
        Assert.False(host.QuitRequested, "the game ended because the host had no more keys, not by the player's leaving");
    }

    /// <summary>
    /// "Keyboard and joystick" (the default) with no joystick or gamepad plugged in answers "keyboard only", so that
    /// the menus part does not ask to centre a joystick that is not there (D-603).
    /// </summary>
    [SkippableFact]
    public void KeyboardAndJoystickWithNoControllerAnswersKeyboardOnly()
    {
        int[] keys = [.. Paced(HostKeys.Space, HostKeys.AltQ, HostKeys.Down, HostKeys.Enter), .. Pause];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host, null, new GameOptions(SoundLetter: 'N', Joystick: true));
        var shared = new CovertAction.Core.Shared.SharedState();
        sbyte? joystick = null;
        game.PartStarting += _ => joystick ??= shared.Joystick;

        game.Main(shared);

        Assert.Equal((sbyte)0, joystick);
    }

    /// <summary>
    /// The original's set-up questions are still there for the proof (<see cref="GameOptions.AskSetUpQuestions"/>): the
    /// keys answer them (no sound, keyboard only) and the launcher's settings do not count.
    /// </summary>
    [SkippableFact]
    public void TheOriginalsQuestionsAskAndTheirAnswersCountForTheProof()
    {
        int[] keys = [.. Paced(HostKeys.ForCharacter('1'), HostKeys.ForCharacter('2')), .. Paced(HostKeys.Space, HostKeys.AltQ, HostKeys.Down, HostKeys.Enter), .. Pause];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var game = new Game(GameFiles(), host, null, new GameOptions(SoundLetter: 'A', Joystick: true, AskSetUpQuestions: true));
        var parts = new List<sbyte>();
        game.PartStarting += parts.Add;
        var shared = new CovertAction.Core.Shared.SharedState();

        game.Main(shared);

        Assert.Equal([MenusFresh], parts);
        Assert.Equal(((sbyte)'N', (sbyte)'M', (sbyte)0), (shared.SndDriver, shared.GfxDriver, shared.Joystick));
        const byte frameTopLeft = 0xc9;
        Assert.Equal(frameTopLeft, host.TextPage.Characters[1 * TextScreen.Columns + 1]);
        Assert.False(host.QuitRequested, "the game ended because the host had no more keys, not by the player's leaving");
    }
}