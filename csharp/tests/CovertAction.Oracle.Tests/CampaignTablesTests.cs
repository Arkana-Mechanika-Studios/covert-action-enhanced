using System.Security.Cryptography;
using System.Text.Json;
using CovertAction.Content;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The campaign's record types and their codec: on a made-up case, and on every hand-off block the oracle recorded
/// from the original (every module's vectors carry the block before and after each run). A block is read into the
/// typed tables and written back; where a case is in progress its world must be one of the player's three world
/// files, name for name. Skipped without vectors.
/// </summary>
public sealed class CampaignTablesTests(ITestOutputHelper output)
{
    [Fact]
    public void TheTablesAre8758BytesPlusTheCastAndTheEvents()
    {
        // the reconstructed state.h: 8758 + 54 * cast + 44 * events
        Assert.Equal(8758, CampaignTablesCodec.Size(0, 0));
        Assert.Equal(8758 + 54 * 8 + 44 * 30, CampaignTablesCodec.Size(8, 30));
    }

    [Fact]
    public void ACaseSurvivesTheWayThroughTheOriginalsBytes()
    {
        var shared = new SharedState { NCast = 2, NEvents = 1 };
        var tables = shared.Campaign;
        tables.Plot.BigOrg = 7;
        tables.Plot.Handle = "MAX";
        tables.Plot.Bigs = 0x0200_0001;
        tables.Cast.AddRange([new Activity { Who = 5, Name = "Courier", Rank = 2 }, new Activity { Who = -1, Name = "MasterMind", Status = 0x0100 }]);
        tables.CastProgress.AddRange([new Activity2 { Chron = 3 }, new Activity2 { Chron = -1, Intel = 40 }]);
        tables.Events.Add(new Event { Act = 1, Chron = 999, Synch = 2, Name = "Sent orders", Status = 0x0200, VP = 5 });
        tables.Cities[15].Name = "Tripoli";
        tables.Cities[15].GeoX = 200;
        tables.Orgs[4].Name = "Red Battalion";
        tables.Persons[63].Alert = -3;
        tables.Locations[255].Seen = 0x8000_0001;
        tables.Clues[79].Sorg = -1;
        tables.Synopses[127].Who2 = -1;
        tables.MacGuffins[3].Name = "Blueprints";
        tables.Active[31] = -2;

        var block = new byte[SharedState.Size];
        SharedBlockCodec.StoreCampaign(shared, block);
        var read = new SharedState { NCast = 2, NEvents = 1 };
        SharedBlockCodec.LoadCampaign(block, read);

        var back = read.Campaign;
        Assert.Equal((7, "MAX", 0x0200_0001u), (back.Plot.BigOrg, back.Plot.Handle, back.Plot.Bigs));
        Assert.Equal(("Courier", 5, 2, "MasterMind", -1, 0x0100), (back.Cast[0].Name, back.Cast[0].Who, back.Cast[0].Rank, back.Cast[1].Name, back.Cast[1].Who, back.Cast[1].Status));
        Assert.Equal((3, -1, 40), (back.CastProgress[0].Chron, back.CastProgress[1].Chron, back.CastProgress[1].Intel));
        Assert.Equal((1, 999, 2, "Sent orders", 0x0200, 5), (back.Events[0].Act, back.Events[0].Chron, back.Events[0].Synch, back.Events[0].Name, back.Events[0].Status, back.Events[0].VP));
        Assert.Equal(("Tripoli", 200, "Red Battalion", -3), (back.Cities[15].Name, back.Cities[15].GeoX, back.Orgs[4].Name, back.Persons[63].Alert));
        Assert.Equal((0x8000_0001u, -1, -1, "Blueprints", -2), (back.Locations[255].Seen, back.Clues[79].Sorg, back.Synopses[127].Who2, back.MacGuffins[3].Name, back.Active[31]));

        // the last counter is the last word of the tables
        var end = SharedState.TablesOffset + CampaignTablesCodec.Size(2, 1);
        Assert.Equal([0xfe, 0xff], block[(end - 2)..end]);
        Assert.All(block[end..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void TablesLongerThanTheirPlaceAreRefused()
    {
        var shared = new SharedState { NCast = 60, NEvents = 0 };
        Assert.Throws<ArgumentException>(() => SharedBlockCodec.LoadCampaign(new byte[SharedState.Size], shared));
    }

    /// <summary>Every distinct hand-off block of the vectors of every module.</summary>
    /// <param name="capturedOnly">Only the blocks of real sessions: no fuzzed run, whose blocks have bytes changed at random.</param>
    private static List<byte[]> RecordedBlocks(bool capturedOnly = false)
    {
        var blocks = new Dictionary<string, byte[]>();
        if (Directory.Exists(OracleVectors.Folder))
        {
            foreach (var file in Directory.EnumerateFiles(OracleVectors.Folder, "*.json", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).StartsWith('_')))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file));
                if (!document.RootElement.TryGetProperty("cases", out var cases))
                {
                    continue;
                }
                foreach (var oracleCase in cases.EnumerateArray())
                {
                    if (capturedOnly && (!oracleCase.GetProperty("realistic").GetBoolean() || (oracleCase.GetProperty("origin").GetString() ?? "").Contains("fuzz", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    foreach (var property in new[] { "shared_in", "shared_out" })
                    {
                        if (oracleCase.TryGetProperty(property, out var block) && block.ValueKind == JsonValueKind.String)
                        {
                            var bytes = block.GetBytesFromBase64();
                            blocks.TryAdd(Convert.ToHexString(SHA256.HashData(bytes)), bytes);
                        }
                    }
                }
            }
        }
        return blocks.Count > 0 ? [.. blocks.Values] : throw new SkipException("no oracle vectors with hand-off blocks under " + OracleVectors.Folder);
    }

    [SkippableFact]
    public void EveryRecordedHandOffBlockIsReadAndWrittenBackUnchanged()
    {
        var blocks = RecordedBlocks();
        int withCase = 0, beyond = 0;
        foreach (var block in blocks)
        {
            var shared = new SharedState();
            SharedBlockCodec.Layout.Load(block, shared);
            if (SharedState.TablesOffset + CampaignTablesCodec.Size(Math.Max(0, (int)shared.NCast), Math.Max(0, (int)shared.NEvents)) > block.Length || shared.NCast < 0 || shared.NEvents < 0)
            {
                beyond++;   // counts a fuzzed run made nonsense of, or the overrun of docs/original-bugs.md CA-6
                continue;
            }
            SharedBlockCodec.LoadCampaign(block, shared);
            var written = (byte[])block.Clone();
            SharedBlockCodec.Layout.Store(shared, written);
            SharedBlockCodec.StoreCampaign(shared, written);
            Assert.True(block.AsSpan().SequenceEqual(written), "a hand-off block changed on its way through the typed tables");
            withCase += shared.NCast > 0 ? 1 : 0;
        }
        output.WriteLine($"TALLY {blocks.Count} distinct hand-off blocks: all unchanged; {withCase} with a case in progress, {beyond} with counts beyond the block");
    }

    [SkippableFact]
    public void TheWorldOfARecordedCaseIsOneOfThePlayersWorldFiles()
    {
        var game = GameFolder.FromEnvironment() ?? new GameFolder(Path.Combine(CovertAction.Tests.Shared.Repository.Root, "game", "Covert Action", "Covert"));
        Skip.If(game.Find("WORLD0.DTA") is null, "no game files");
        var worlds = Enumerable.Range(0, 3).Select(n => WorldFile.Read(game.Read($"WORLD{n}.DTA"))).ToList();
        var checkedCases = 0;
        foreach (var block in RecordedBlocks(capturedOnly: true))
        {
            var shared = new SharedState();
            SharedBlockCodec.Layout.Load(block, shared);
            if (shared.NCast <= 0 || shared.NCast > 16 || shared.NEvents is < 0 or > 60)
            {
                continue;
            }
            SharedBlockCodec.LoadCampaign(block, shared);
            var tables = shared.Campaign;
            if (tables.Plot.World is < 0 or > 2 || tables.Cities.All(city => city.Name.Length == 0))
            {
                continue;   // a block recorded before a world was loaded into it
            }
            var world = worlds[tables.Plot.World];
            Assert.Equal(world.CityNames, tables.Cities.Select(c => c.Name));
            Assert.Equal(world.OrgNames, tables.Orgs.Select(o => o.Name));
            // every member of the cast has a role's name and is nobody yet or one of the 64 persons
            Assert.All(tables.Cast, member => Assert.True(member.Name.Length > 0 && member.Who is >= -1 and < CampaignTables.PersonCount, $"cast member '{member.Name}' played by {member.Who}"));
            // an event is done by a member of the cast, or is an empty record (actor -1)
            Assert.All(tables.Events, e => Assert.InRange(e.Act, -1, shared.NCast - 1));
            checkedCases++;
        }
        output.WriteLine($"TALLY {checkedCases} recorded cases: cities, organisations, cast and events as the world files and the counts say");
        Skip.If(checkedCases == 0, "no recorded hand-off block has a case in progress");
    }
}
