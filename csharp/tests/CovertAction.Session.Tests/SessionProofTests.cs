using CovertAction.Content;

namespace CovertAction.Session.Tests;

/// <summary>
/// Whole sessions against the original (csharp/docs/SESSION-PROOF.md): every session of a part recorded by
/// tools/session/original_side.py is played on the rewrite with the same keys, time of day and hand-off block, and
/// must give the same picture, colours and hand-off block at every checkpoint and the same block at the end.
/// Skipped without recordings or game files. Each run writes a comparison folder beside the recording
/// (compare-&lt;stamp&gt;: report.txt, the rewrite's checkpoints, a picture of the first difference).
/// </summary>
public sealed class SessionProofTests
{
    /// <summary>The variable that names the folder of recorded sessions (default: build/session beside build/oracle).</summary>
    public const string SessionDirVariable = "COVERT_SESSION_DIR";

    [SkippableFact]
    public void EveryRecordedCryptographySessionAgreesWithTheOriginal() => AssertPartAgrees("CODE");

    [SkippableFact]
    public void EveryRecordedCarChaseSessionAgreesWithTheOriginal() => AssertPartAgrees("CHASE");

    [SkippableFact]
    public void EveryRecordedMenusSessionAgreesWithTheOriginal() => AssertPartAgrees("FINAL");

    [SkippableFact]
    public void EveryRecordedCampaignSessionAgreesWithTheOriginal() => AssertPartAgrees("GAME");

    [SkippableFact]
    public void EveryRecordedBuildingRaidSessionAgreesWithTheOriginal() => AssertPartAgrees("TAC");

    [SkippableFact]
    public void EveryRecordedElectronicsSessionAgreesWithTheOriginal() => AssertPartAgrees("BUG");

    /// <summary>
    /// Whole sessions (item 7): the original's launcher COVERT.EXE run with every program it starts
    /// (tools/session/campaign_side.py) against the rewrite's <c>Game.Main</c>, compared at every checkpoint across the
    /// parts' boundaries.
    /// </summary>
    [SkippableFact]
    public void EveryRecordedWholeSessionAgreesWithTheOriginal() => AssertPartAgrees("COVERT");

    /// <summary>
    /// The comparison is not blind: the rewrite started a second later than the original's recording shuffles another
    /// cipher (the seed is the time of day), and the comparison reports the first checkpoint as different.
    /// </summary>
    [SkippableFact]
    public void AChangedInputIsReportedAtTheFirstCheckpointItChanges()
    {
        var (recordings, _) = Recordings("CODE");
        var recording = recordings[0];
        var difference = SessionComparison.Compare(recording, RewriteSide.Run(recording, GameFiles(), recording.UnixTime + 1), outFolder: null);
        Assert.NotNull(difference);
        Assert.StartsWith("checkpoint 0 ", difference, StringComparison.Ordinal);
        Assert.Contains("pixels differ", difference, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stick is seen (item 12): a whole session with the joystick moving, played without the stick (the port as with no
    /// joystick), is reported at the first checkpoint where the original's picture changed after the stick first left its
    /// rest: the menu's highlight did not move.
    /// </summary>
    [SkippableFact]
    public void AStickNotGivenIsReportedWhereTheOriginalFirstMoved()
    {
        var (recordings, _) = Recordings("COVERT");
        var recording = recordings.FirstOrDefault(r => r.StickAtStart is not null);
        Skip.If(recording is null, "no recorded whole session with a stick (tools/session/sessions/covert/joystick-menu.json)");
        var pushed = Enumerable.Range(0, recording.Sticks.Count).First(i => recording.Sticks[i] != recording.StickAtStart);
        var moved = recording.Checkpoints.First(c => c.Number > pushed && c.Picture != recording.Checkpoints[pushed].Picture).Number;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var outFolder = NewFolder(Path.Combine(Path.GetDirectoryName(recording.Folder)!, $"compare-{stamp}-without-stick"));
        var difference = SessionComparison.Compare(recording, RewriteSide.RunWhole(recording, GameFiles(), Path.Combine(outFolder, "written"), withStick: false), outFolder);
        Assert.NotNull(difference);
        Assert.StartsWith($"checkpoint {moved} ", difference, StringComparison.Ordinal);
        Assert.Contains("pixels differ", difference, StringComparison.Ordinal);
    }

    /// <summary>The latest recording of every session of a part, and the session root; skips without them.</summary>
    private static (List<SessionRecording> Recordings, string Root) Recordings(string part)
    {
        var root = SessionRoot() ?? throw new SkipException($"no recorded sessions: set {SessionDirVariable} (tools/session/original_side.py records them)");
        // COVERT_SESSION_ONLY: only the sessions whose names contain it (to look at one session while making it)
        var only = Environment.GetEnvironmentVariable("COVERT_SESSION_ONLY");
        var recordings = SessionRecording.LatestOfPart(root, part)
            .Where(r => string.IsNullOrEmpty(only) || r.Name.Contains(only, StringComparison.Ordinal))
            .ToList();
        Skip.If(recordings.Count == 0, $"no recorded {part} sessions under {root}");
        return (recordings, root);
    }

    private static void AssertPartAgrees(string part)
    {
        var (recordings, root) = Recordings(part);
        var folder = GameFiles();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var differences = new List<string>();
        foreach (var recording in recordings)
        {
            if (SpecFile(recording) is { } spec && recording.StaleAgainst(spec) is { } stale)
            {
                differences.Add($"{recording.Name}: {stale}");
                continue;
            }
            if (recording.StaleAgainstSources(root) is { } staleSource)
            {
                differences.Add($"{recording.Name}: {staleSource}");
                continue;
            }
            var outFolder = NewFolder(Path.Combine(Path.GetDirectoryName(recording.Folder)!, $"compare-{stamp}"));
            var rewrite = part == "COVERT"
                ? RewriteSide.RunWhole(recording, folder, Path.Combine(outFolder, "written"))
                : RewriteSide.Run(recording, folder, writeFolder: Path.Combine(outFolder, "written"), startScreen: recording.StartScreen(StatesRoot(root)));
            if (SessionComparison.Compare(recording, rewrite, outFolder) is { } difference)
            {
                differences.Add($"{recording.Name}: {difference}");
            }
        }
        Assert.True(differences.Count == 0, $"{differences.Count} of {recordings.Count} sessions differ from the original:\n" + string.Join("\n", differences));
    }

    /// <summary>tools/oracle's captured start images: COVERT_ORACLE_DIR/states, else oracle/states beside the session root.</summary>
    private static string StatesRoot(string sessionRoot) =>
        Environment.GetEnvironmentVariable("COVERT_ORACLE_DIR") is { Length: > 0 } oracle
            ? Path.Combine(oracle, "states")
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sessionRoot.TrimEnd('/', '\\')))!, "oracle", "states");

    /// <summary>The committed spec of a recorded session (tools/session/sessions/&lt;part&gt;/&lt;name&gt;.json above the tests), or null.</summary>
    private static string? SpecFile(SessionRecording recording)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var sessions = Path.Combine(d.FullName, "tools", "session", "sessions");
            if (Directory.Exists(sessions))
            {
                return Path.Combine(sessions, recording.Part.ToLowerInvariant(), recording.Name + ".json");
            }
        }
        return null;
    }

    /// <summary>A folder that did not exist: reruns never write into an earlier comparison.</summary>
    private static string NewFolder(string path)
    {
        var candidate = path;
        for (var n = 2; Directory.Exists(candidate); n++)
        {
            candidate = $"{path}-{n}";
        }
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    /// <summary>COVERT_SESSION_DIR; else build/session beside COVERT_ORACLE_DIR (build/oracle); else build/session above the tests.</summary>
    private static string? SessionRoot()
    {
        if (Environment.GetEnvironmentVariable(SessionDirVariable) is { Length: > 0 } dir)
        {
            return Directory.Exists(dir) ? dir : null;
        }
        if (Environment.GetEnvironmentVariable("COVERT_ORACLE_DIR") is { Length: > 0 } oracle)
        {
            var beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(oracle.TrimEnd('/', '\\')))!, "session");
            return Directory.Exists(beside) ? beside : null;
        }
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "build", "session");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>The player's game folder: COVERT_GAME_DIR, else game/Covert Action/Covert above the tests.</summary>
    private static GameFolder GameFiles()
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
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }
}
