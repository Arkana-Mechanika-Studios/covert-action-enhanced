using CovertAction.Content;
using CovertAction.Core.Animation;
using CovertAction.Core.Final;
using CovertAction.Core.Graphics;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The game's animation player (<see cref="LibraryAnimation"/>) on the game's drawing code, with every animation
/// file of the player's game: loaded through the game's file calls and played frame by frame, the screen must show
/// what the data-level player (<see cref="PanPlayer"/>, proven against the project's verified player) shows after
/// the same number of frames. Skipped without the game's files.
/// </summary>
public sealed class LibraryAnimationTests(ITestOutputHelper output) : IDisposable
{
    private readonly GameFolder _game = FindGame();
    private readonly List<GameFolderFiles> _files = [];

    public void Dispose() => _files.ForEach(files => files.Dispose());

    private static GameFolder FindGame()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "CODE.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? new GameFolder(Path.Combine(AppContext.BaseDirectory, "no-game"));
    }

    /// <summary>A whole page as a port: what the programs' initialised data has for the player's five ports.</summary>
    private static void WholePage(RasterPort port, int page)
    {
        (port.PageNr, port.XBgn, port.YBgn, port.Width1, port.Length1) = ((short)page, 0, 0, McgaGraphicsDriver.Width - 1, McgaGraphicsDriver.Height - 1);
        (port.DrawMode, port.APen, port.BPen, port.FontNr) = (0, 15, 4, 0);
    }

    private (LibraryAnimation Animation, PanEngineState Pan, McgaGraphicsDriver Graphics) Setup()
    {
        Skip.If(_game.Find("FONTS.CV") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        var graphics = new McgaGraphicsDriver(new FontSet(_game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]);
        var link = new HostLink(graphics, new HeadlessHost([]));
        _files.Add(new GameFolderFiles(_game));
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), _files[^1]);
        var state = new FinalState();
        var pan = state.Pan;
        // the values of the programs' initialised data
        pan.PanMagic = "PANI";
        pan.PanVersion = 3;
        WholePage(pan.PanPortX, 0);
        WholePage(pan.PanWorkPortX, 2);
        WholePage(pan.PanScreenCopyX, 0);
        WholePage(pan.PanSavedScreenX, 1);
        WholePage(pan.PanBackgroundX, 2);
        var animation = new LibraryAnimation(state, pan, world, new LibraryOutput(state, world), new LibraryInput(state, world), new LibraryPictures(world));
        return (animation, pan, graphics);
    }

    private IEnumerable<string> AnimationFiles() =>
        Directory.EnumerateFiles(Path.GetDirectoryName(_game.Find("FONTS.CV")!)!, "*.PAN").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal);

    [SkippableFact]
    public void EveryAnimationOfTheGamePlaysAsTheDataLevelPlayerPlaysIt()
    {
        int[] checkpoints = [1, 2, 10, 30, 100, 300];
        var files = 0;
        Setup();    // skips the test without the game
        foreach (var name in AnimationFiles())
        {
            var (animation, pan, graphics) = Setup();
            var file = PanFile.Read(_game.Read(name));
            var reference = new PanPlayer(file);
            Assert.True(animation.StartAnimation(new AnimationParams { File = name, Speed = AnimationParams.Keep, X = 0, Y = 0, FrameLength = AnimationParams.Keep }), $"{name}: not loaded");
            Assert.Equal((file.Width - 1, file.Height - 1), ((int)pan.PanPort.Width1, (int)pan.PanPort.Length1));
            var frame = 0;
            foreach (var checkpoint in checkpoints)
            {
                var ended = false;
                for (; frame < checkpoint && !ended; frame++)
                {
                    var running = reference.Tick();
                    ended = animation.RunPanFrames(1) != 0;
                    Assert.True(ended == !running, $"{name}, frame {frame + 1}: the game's player {(ended ? "has ended" : "plays on")}, the data-level player {(running ? "plays on" : "has ended")}");
                }
                if (ended)
                {
                    break;
                }
                for (var y = 0; y < file.Height; y++)
                {
                    var shown = graphics.Screen.Slice(y * McgaGraphicsDriver.Width, file.Width);
                    Assert.True(shown.SequenceEqual(reference.Frame.AsSpan(y * file.Width, file.Width)), $"{name}, after {checkpoint} frames: row {y} differs from the data-level player's");
                }
            }
            animation.StopAnimation();
            files++;
        }
        output.WriteLine($"TALLY animations: {files} files, each compared after {string.Join(", ", checkpoints)} frames (until it ends)");
        Assert.True(files > 0, "no animation files in the game folder");
    }

    [SkippableFact]
    public void AFileThatIsNoAnimationIsRefused()
    {
        var (animation, pan, _) = Setup();
        // a picture file: its first bytes are not the animation mark
        var picture = Directory.EnumerateFiles(Path.GetDirectoryName(_game.Find("FONTS.CV")!)!, "*.PIC").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).First();
        Assert.False(animation.LoadPanFile(picture));
        Assert.Equal(1, pan.PanError);
    }
}
