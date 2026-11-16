using System.Text;
using CovertAction.Content;
using CovertAction.Launcher;

namespace CovertAction.Session.Tests;

/// <summary>
/// A tool for looking at the Amiga pictures option (D-642) in the game's own screens: the recorded sessions of
/// csharp/docs/SESSION-PROOF.md are played with the option on, and every screen that shows a replaced picture in colours not
/// seen before is written as a PNG (with an index of the pictures loaded before it). Skipped unless the recordings, the game,
/// the player's Amiga files (COVERT_AMIGA_DIR) and the folder to write to (COVERT_AMIGA_SCREENS_DIR) are given; the files it
/// writes show the player's Amiga pictures and are not kept in the repository.
/// </summary>
public sealed class AmigaPictureScreensTests
{
    private const string ScreensVariable = "COVERT_AMIGA_SCREENS_DIR";
    private const string AmigaVariable = "COVERT_AMIGA_DIR";

    /// <summary>The most screens written for one session: the first screens with new colours.</summary>
    private const int PerSession = 40;

    /// <summary>A colour counts for a screen's signature when this many pixels show it.</summary>
    private const int Significant = 150;

    /// <summary>The first display colour of the pictures' own colours (<c>PictureColours.First</c>).</summary>
    private const int FirstPictureColour = 128;

    /// <summary>How many times of day the campaign's and the whole sessions are played: the recorded one and others.</summary>
    private const int Variants = 3;

    /// <summary>How many times of day the sessions that travel from city to city are played.</summary>
    private const int TravelVariants = 60;

    [SkippableFact]
    public void TheScreensShowingAmigaPicturesAreWrittenAsPngFilesWhenAskedTo()
    {
        var screens = Environment.GetEnvironmentVariable(ScreensVariable);
        var amiga = Environment.GetEnvironmentVariable(AmigaVariable);
        Skip.If(string.IsNullOrEmpty(screens) || string.IsNullOrEmpty(amiga), $"set {ScreensVariable} and {AmigaVariable} to look at the Amiga pictures in the recorded sessions");
        var root = Environment.GetEnvironmentVariable(SessionProofTests.SessionDirVariable);
        Skip.If(string.IsNullOrEmpty(root) || !Directory.Exists(root), $"set {SessionProofTests.SessionDirVariable}");
        var folder = GameFolder.FromEnvironment();
        Skip.If(folder is null, "no game files: set COVERT_GAME_DIR");
        var states = Path.Combine(Environment.GetEnvironmentVariable("COVERT_ORACLE_DIR") ?? "", "states");
        Directory.CreateDirectory(screens!);
        foreach (var old in Directory.EnumerateFiles(screens!, "*.png"))
        {
            File.Delete(old);
        }

        var seen = new HashSet<string>();
        var index = new StringBuilder();
        var written = Path.Combine(Path.GetTempPath(), "covert-amiga-screens-" + Environment.ProcessId);
        foreach (var part in new[] { "COVERT", "GAME", "FINAL", "TAC", "CHASE", "BUG", "CODE" })
        {
            // the campaign and whole sessions are played again at other times of day: another seed deals another case, so other
            // cities and maps than the recorded ones come on the screen
            // (the sessions that travel are played at many times of day, the others at a few)
            IEnumerable<long> Shifts(SessionRecording r) =>
                part is not ("COVERT" or "GAME") ? [0L]
                : Enumerable.Range(0, r.Name.Contains("travel") || r.Name.Contains("city") ? TravelVariants : Variants).Select(n => n * 100_003L);
            foreach (var (recording, shift) in SessionRecording.LatestOfPart(root!, part).SelectMany(r => Shifts(r).Select(s => (r, s))))
            {
                var recent = new List<string>();
                var all = new SortedSet<string>();
                var taken = 0;
                var options = new GameOptions(AmigaPictures: true, AmigaFolder: amiga);
                void Created(CovertAction.Launcher.Game game) => game.AmigaPictures!.Replaced += name => { all.Add(name); if (!recent.Contains(name)) { recent.Add(name); } };
                void Look(int number, CovertAction.Launcher.Game game)
                {
                    var signature = Signature(game.Graphics.Screen, game.Graphics.Palette);
                    if (signature.Length == 0 || taken >= PerSession || !seen.Add(signature))
                    {
                        return;
                    }
                    taken++;
                    var file = $"{part}-{recording.Name}{(shift == 0 ? "" : $"+{shift}")}-{number:D4}.png";
                    File.WriteAllBytes(Path.Combine(screens!, file), CovertAction.Tests.PngFile.Screen(game.Graphics.Screen, game.Graphics.Palette));
                    index.AppendLine($"{file}\t{string.Join(" ", recent)}");
                    recent.Clear();
                }
                CovertAction.Launcher.Game? current = null;
                try
                {
                    var wrote = Path.Combine(written, $"{part}-{recording.Name}+{shift}", "written");
                    if (part == "COVERT")
                    {
                        RewriteSide.RunWhole(recording, folder!, wrote, options: options, created: g => { current = g; Created(g); }, checkpointTaken: n => Look(n, current!), timeShift: shift);
                    }
                    else
                    {
                        RewriteSide.Run(recording, folder!, recording.UnixTime + shift, writeFolder: wrote, startScreen: recording.StartScreen(states), options: options,
                            created: g => { current = g; Created(g); }, checkpointTaken: n => Look(n, current!));
                    }
                }
                catch (Exception e)
                {
                    // another seed may take the recorded keys where the game does not follow them; what was seen before stays
                    index.AppendLine($"# {part}-{recording.Name}+{shift}: {e.GetType().Name}: {e.Message}");
                }
                index.AppendLine($"# {part}-{recording.Name}+{shift} replaced: {string.Join(" ", all)}");
            }
        }
        File.WriteAllText(Path.Combine(screens!, "index.txt"), index.ToString());
        if (Directory.Exists(written))
        {
            Directory.Delete(written, recursive: true);
        }
        Assert.True(seen.Count > 0, "no recorded session showed an Amiga picture");
    }

    /// <summary>The colours of the pictures' range that a screen shows in numbers (each with its pixel count of at least <see cref="Significant"/>), as a text; empty when it shows none.</summary>
    private static string Signature(ReadOnlySpan<byte> screen, byte[] palette)
    {
        var counts = new int[256];
        foreach (var pixel in screen)
        {
            counts[pixel]++;
        }
        var colours = Enumerable.Range(FirstPictureColour, 256 - FirstPictureColour).Where(c => counts[c] >= Significant)
            .Select(c => $"{palette[3 * c]:x2}{palette[3 * c + 1]:x2}{palette[3 * c + 2]:x2}");
        return string.Join("-", colours);
    }
}
