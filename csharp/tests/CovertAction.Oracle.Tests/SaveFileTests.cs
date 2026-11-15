using System.Security.Cryptography;
using CovertAction.Content;
using CovertAction.Core.Shared;
using CovertAction.Persistence;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The saved campaign's codec on the original's own save files: the files the original wrote during the project's
/// verified campaign routes (build/game-oracle and build/final) and in the
/// session proof's recordings (the files the original program wrote, <c>original-*/written/</c> under the session
/// folder, csharp/docs/SESSION-PROOF.md section 5). Each is read into the typed state and written back from the state
/// alone; the result must be the file, byte for byte. Skipped where there are no such files.
/// </summary>
public sealed class SaveFileTests(ITestOutputHelper output)
{
    /// <summary>The environment variable that names a folder with save files of the original (searched with its subfolders).</summary>
    private const string EnvironmentVariable = "COVERT_SAVES_DIR";

    /// <summary>Every distinct save file found, with one of the places it was found at.</summary>
    private static List<(string Path, byte[] Bytes)> SaveFiles()
    {
        var folders = Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } named
            ? [named]
            : new[] { Path.Combine(Repository.Root, "build", "game-oracle"), Path.Combine(Repository.Root, "build", "final") }
                .Concat(WrittenBySessions()).ToArray();
        var files = new Dictionary<string, (string, byte[])>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.sve", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive }))
            {
                if (Path.GetFileName(path).StartsWith("cv", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = File.ReadAllBytes(path);
                    files.TryAdd(Convert.ToHexString(SHA256.HashData(bytes)), (path, bytes));
                }
            }
        }
        return files.Count > 0 ? [.. files.Values] : throw new SkipException($"no save files of the original (set {EnvironmentVariable})");
    }

    /// <summary>
    /// The folders of the files the original program wrote in the session proof's recordings: <c>original-*/written</c>
    /// under COVERT_SESSION_DIR, else under build/session beside COVERT_ORACLE_DIR, else under build/session of the
    /// repository (as the session tests find their recordings).
    /// </summary>
    private static IEnumerable<string> WrittenBySessions()
    {
        var root = Environment.GetEnvironmentVariable("COVERT_SESSION_DIR") is { Length: > 0 } sessions
            ? sessions
            : Environment.GetEnvironmentVariable("COVERT_ORACLE_DIR") is { Length: > 0 } oracle && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(oracle)) is { } build
                ? Path.Combine(build, "session")
                : Path.Combine(Repository.Root, "build", "session");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "written", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
                .Where(folder => Path.GetFileName(Path.GetDirectoryName(folder))?.StartsWith("original-", StringComparison.Ordinal) == true)
            : [];
    }

    [SkippableFact]
    public void EverySaveFileOfTheOriginalIsReadAndWrittenBackByteForByte()
    {
        var saves = SaveFiles();
        foreach (var (path, bytes) in saves)
        {
            var state = new SharedState();
            SaveFileCodec.Load(bytes, state);
            Assert.True(bytes.Length == SaveFileCodec.Size(state.NCast, state.NEvents), $"{path}: {bytes.Length} bytes for a cast of {state.NCast} and {state.NEvents} events");
            var written = SaveFileCodec.Write(state);
            var at = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] != written[i]).ToList();
            Assert.True(at.Count == 0, at.Count == 0 ? "" : $"{path}: {at.Count} bytes differ, first at 0x{at[0]:x} (the file has {bytes[at[0]]:x2}, written {written[at[0]]:x2}); at: {string.Join(" ", at.Take(40).Select(i => i.ToString("x")))}");
        }
        output.WriteLine($"TALLY {saves.Count} distinct save files of the original: each written back from the typed state, byte for byte");
    }

    [SkippableFact]
    public void ASaveFileHoldsAPlausibleCampaign()
    {
        var game = GameFolder.FromEnvironment() ?? new GameFolder(Path.Combine(Repository.Root, "game", "Covert Action", "Covert"));
        Skip.If(game.Find("WORLD0.DTA") is null, "no game files");
        var worlds = Enumerable.Range(0, 3).Select(n => WorldFile.Read(game.Read($"WORLD{n}.DTA"))).ToList();
        foreach (var (path, bytes) in SaveFiles())
        {
            var state = new SharedState();
            SaveFileCodec.Load(bytes, state);
            var campaign = state.Campaign;
            // the world is one of the three, name for name; the agent stands in one of its cities, and the header says which
            var world = worlds[campaign.Plot.World];
            Assert.Equal(world.CityNames, campaign.Cities.Select(c => c.Name));
            Assert.Equal(world.OrgNames, campaign.Orgs.Select(o => o.Name));
            Assert.InRange(state.City, 0, 15);
            Assert.True(campaign.Plot.City.Text == campaign.Cities[state.City].Name, $"{path}: the header names {campaign.Plot.City.Text}, the agent is in {campaign.Cities[state.City].Name}");
            // a person record is free (organisation -1) or belongs to an organisation and a city of the world
            Assert.Contains(campaign.Persons, p => p.Org >= 0);
            Assert.All(campaign.Persons, p => Assert.True(p.Org == -1 || (p.Org is >= 0 and < 16 && p.Loc is >= 0 and < 16), $"{path}: a person of organisation {p.Org} in city {p.Loc}"));
            // the routes play the agent the game proposes (MAX, ANNA): a name of capitals
            Assert.Matches("^[A-Z]{1,12}$", campaign.Plot.Handle);
            Assert.All(campaign.Cast, member => Assert.True(member.Name.Length > 0 && member.Who is >= -1 and < 64, $"{path}: cast member '{member.Name}'"));
            Assert.All(campaign.Events, e => Assert.InRange(e.Act, -1, state.NCast - 1));
            Assert.InRange(state.Level, 0, 3);
        }
    }
}
