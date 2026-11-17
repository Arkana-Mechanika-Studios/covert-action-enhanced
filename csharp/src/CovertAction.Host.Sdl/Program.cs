using System.Runtime.InteropServices;
using CovertAction.Content;
using CovertAction.Launcher;
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>
/// The program players start: the game from its set-up screen on, as the original's COVERT.EXE starts it, with the
/// player's settings (csharp/docs/SETTINGS.md: the command line over the environment over the settings file over the
/// defaults). The launcher, the settings screen, comes first with --setup, while Shift is held as it starts, or when
/// the game is not found. A developer can go straight into one practice session of a part instead.
/// </summary>
internal static partial class Program
{
    private static int Main(string[] args)
    {
        AttachToParentConsole();
        var cli = CommandLine.Parse(args);
        if (cli.Error is { } error)
        {
            Console.Error.WriteLine("CovertAction: " + error);
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }
        if (cli.Help)
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        // the settings file: the one named, else the first found; the changes go to the one read, else to a new one
        var found = SettingsFile.Locate(cli.SettingsFile);
        var load = found is null ? new SettingsLoad(new GameSettings(), null, []) : SettingsFile.Load(found);
        foreach (var problem in load.Problems)
        {
            Console.Error.WriteLine(problem);
        }
        var settingsPath = found ?? SafeWritablePath();
        var settings = load.Settings;
        var start = StartSettings.Resolve(cli, Environment.GetEnvironmentVariable, settings);
        var gameFolder = GameFolderFor(start);

        if (cli.LauncherScreenshot is { } shot)
        {
            return Screenshot(shot, gameFolder is null ? LauncherReason.GameNotFound : LauncherReason.Asked, settings, start, settingsPath, cli.LauncherScreenshotTab);
        }

        string? startProblem = null;
        var reason = gameFolder is null ? LauncherReason.GameNotFound : LauncherReason.Asked;
        var showLauncher = cli.Setup || ShiftHeld() || gameFolder is null;
        while (true)
        {
            if (showLauncher)
            {
                if (cli.NoLauncher)
                {
                    Console.Error.WriteLine(startProblem ?? "Covert Action was not found: name the folder of your copy of the game (see --help).");
                    return 1;
                }
                try
                {
                    var chosen = LauncherHost.Run(reason, startProblem, settings, start, settingsPath);
                    if (chosen is null)
                    {
                        return 0;
                    }
                    settings = chosen;
                }
                catch (Exception e) when (e is DllNotFoundException or SdlHostException)
                {
                    Console.Error.WriteLine("SDL 3 could not be started (SDL3.dll belongs next to the program): " + e.Message);
                    return 1;
                }
                start = StartSettings.Resolve(cli, Environment.GetEnvironmentVariable, settings);
                gameFolder = GameFolderFor(start);
                if (gameFolder is null)
                {
                    (reason, showLauncher) = (LauncherReason.GameNotFound, true);
                    continue;
                }
            }
            try
            {
                return Play(cli, start, gameFolder!);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
            {
                startProblem = $"the game files could not be read from {gameFolder}: {e.Message}";
                (reason, showLauncher) = (LauncherReason.StartFailed, true);
                if (cli.NoLauncher)
                {
                    Console.Error.WriteLine(startProblem);
                    return 1;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or SdlHostException)
            {
                Console.Error.WriteLine("SDL 3 could not be started (SDL3.dll belongs next to the program): " + e.Message);
                return 1;
            }
        }
    }

    /// <summary>The game in its window with the settings of this run, from its set-up screen (or the practice asked for).</summary>
    private static int Play(CommandLine cli, StartSettings start, string gameFolder)
    {
        var scaling = start.Scaling switch
        {
            "nearest" => PictureScaling.Nearest,
            "smooth" => PictureScaling.Smooth,
            _ => PictureScaling.PixelArt,
        };
        using var host = new SdlHost("Covert Action", new SdlWindowOptions(start.Width, start.Height, start.Fullscreen, scaling, start.SystemKeyRepeat, Mouse: start.Mouse,
            Remap: new KeyRemap(start.KeyMap, start.PadMap)));
        var options = new GameOptions(GameSettings.SoundLetter(start.SoundCard), start.Joystick, start.Mt32Roms, ArrowKeysWalk: start.ArrowKeysWalk,
            MusicLetter: GameSettings.PartLetter(start.MusicCard), EffectsLetter: GameSettings.PartLetter(start.EffectsCard), Mouse: start.Mouse,
            AmigaFolder: start.AmigaFiles, AmigaOnlyWhereOriginal: start.AmigaOriginalOnly, AmigaPictures: start.AmigaPictures,
            FixOriginalBugs: start.FixOriginalBugs, FixCampaignBugs: start.FixCampaignBugs, MouseChase: start.MouseChase, CutCapturedDossier: start.CutCapturedDossier, CutStandaloneChase: start.CutStandaloneChase, CutHardestCircuit: start.CutHardestCircuit, CutFillerTexts: start.CutFillerTexts);
        var game = new Game(new GameFolder(gameFolder), host, start.SavesFolder ?? SavesFolder.Default, options);
        game.PartStarting += code => host.PartStarting(code);
        if (cli.Practice == "")
        {
            game.Main();
        }
        else if (cli.Practice == "chase")
        {
            var chase = game.PlayChasePractice(cli.Level);
            Console.WriteLine($"The chase is over: outcome {chase.Outcome}, the quarry's suspicion {chase.Suspicion}%, {chase.Minutes} minutes.");
        }
        else
        {
            var result = game.PlayCryptoPractice(cli.Level);
            Console.WriteLine(result.Solved ? "Message decoded." : "Message not decoded.");
        }
        return 0;
    }

    /// <summary>The game folder of this run: the one named (or a usual subfolder of it), else the first found in the usual places; null: none.</summary>
    private static string? GameFolderFor(StartSettings start) =>
        start.GameFolder is { } named
            ? GameFolderSearch.GameFolderIn(named)
            : GameFolderSearch.FindFirst(AppContext.BaseDirectory, Environment.CurrentDirectory)?.Folder;

    /// <summary>--launcher-screenshot: the launcher as it would open, drawn into a PNG file without a window.</summary>
    private static int Screenshot(string file, LauncherReason reason, GameSettings settings, StartSettings start, string? settingsPath, int tab)
    {
        try
        {
            var context = LauncherHost.Context(reason, null, settings, start, settingsPath, null);
            var picture = Settings.Launcher.Picture(context, tab: tab);
            Settings.Drawing.Png.Write(file, picture.Pixels, picture.Width, picture.Height);
            Console.WriteLine($"The launcher's picture ({picture.Width} x {picture.Height}) is in {Path.GetFullPath(file)}.");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot write {file}: {e.Message}");
            return 1;
        }
    }

    private static string? SafeWritablePath()
    {
        try
        {
            return SettingsFile.WritablePath();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Windows: Shift held as the program starts, and the console of the command line that started it ----

    private const int VirtualKeyShift = 0x10;
    private const int AttachParentProcess = -1;
    private const int StandardOutput = -11;

    /// <summary>Shift held as the program starts (Windows tells the keyboard's state before any window): the player asks for the launcher.</summary>
    private static bool ShiftHeld() => OperatingSystem.IsWindows() && (GetAsyncKeyState(VirtualKeyShift) & 0x8000) != 0;

    /// <summary>
    /// The program is a windowed one (no console window opens with the game); started from a command line, its messages
    /// (--help, the warnings) go to that command line's console. Output sent to a file or a pipe stays there.
    /// </summary>
    private static void AttachToParentConsole()
    {
        if (OperatingSystem.IsWindows() && GetStdHandle(StandardOutput) == IntPtr.Zero)
        {
            AttachConsole(AttachParentProcess);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int handle);
}
