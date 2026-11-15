using CovertAction.Content;
using CovertAction.Core.Records;
using CovertAction.Persistence;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The crime files' codec on the player's thirteen crime files: each is read into the typed records and written back
/// from them alone, byte for byte, and holds what the data model says a crime file holds. Skipped without the game.
/// </summary>
public sealed class CrimeFileTests(ITestOutputHelper output)
{
    private const int CrimeFiles = 13;

    /// <summary>The cast's flag for the member who leads the plot (the reconstructed campaign.h CAST_MASTERMIND).</summary>
    private const int MasterMind = 0x0100;

    private static GameFolder Game()
    {
        var game = GameFolder.FromEnvironment() ?? new GameFolder(Path.Combine(Repository.Root, "game", "Covert Action", "Covert"));
        Skip.If(game.Find("CRIME0.DTA") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        return game;
    }

    [SkippableFact]
    public void EveryCrimeFileIsReadAndWrittenBackByteForByte()
    {
        var game = Game();
        for (var n = 0; n < CrimeFiles; n++)
        {
            var file = game.Read($"CRIME{n}.DTA");
            var tables = new CampaignTables();
            CrimeFileCodec.Load(file, tables);
            var written = CrimeFileCodec.Write(tables);
            var at = Enumerable.Range(0, file.Length).Where(i => file[i] != written[i]).ToList();
            Assert.True(at.Count == 0, at.Count == 0 ? "" : $"CRIME{n}.DTA: {at.Count} bytes differ, first at 0x{at[0]:x}: {string.Join(" ", at.Take(30).Select(i => i.ToString("x")))}");
        }
        output.WriteLine($"TALLY {CrimeFiles} crime files: each written back from the typed records, byte for byte");
    }

    [SkippableFact]
    public void ACrimeFileHoldsACastItsEventsAndItsObjects()
    {
        var game = Game();
        var emptyRecords = 0;
        for (var n = 0; n < CrimeFiles; n++)
        {
            var tables = new CampaignTables();
            CrimeFileCodec.Load(game.Read($"CRIME{n}.DTA"), tables);
            var where = $"CRIME{n}.DTA";
            Assert.True(tables.Cast.Count is >= 3 and <= 16, $"{where}: a cast of {tables.Cast.Count}");
            Assert.Equal(tables.Cast.Count, tables.CastProgress.Count);
            // in the file nobody plays a role yet, every role has a name, and exactly one leads the plot
            Assert.All(tables.Cast, member => Assert.True(member.Who == -1 && member.Name.Length > 0 && member.Rank is >= 0 and <= 7, $"{where}: role '{member.Name}'"));
            Assert.Single(tables.Cast, member => (member.Status & MasterMind) != 0);
            // an event is done by a member of the cast to a member of the cast and has not happened yet; the list
            // may end with empty records (actor -1, no name), which the counts include
            var real = tables.Events.TakeWhile(e => e.Act != -1).ToList();
            Assert.All(real, e => Assert.True(e.Act >= 0 && e.Act < tables.Cast.Count && (e.Status & 0xff) < tables.Cast.Count && e.Chron == 0 && e.Name.Length > 0,
                $"{where}: event '{e.Name}' by {e.Act}"));
            Assert.All(tables.Events.Skip(real.Count), e => Assert.True(e.Act == -1 && e.Name.Length == 0, $"{where}: event '{e.Name}' behind an empty one"));
            Assert.True(real.Count >= 10, $"{where}: {real.Count} events");
            emptyRecords += tables.Events.Count - real.Count;
            // an object nobody holds yet; a slot the plot does not use has no name
            Assert.All(tables.MacGuffins, thing => Assert.True(thing.Who == -1, $"{where}: object '{thing.Name}' held by {thing.Who}"));
            Assert.Contains(tables.MacGuffins, thing => thing.Name.Length > 0);
        }
        output.WriteLine($"TALLY {CrimeFiles} crime files hold casts, events and objects as the data model says; {emptyRecords} empty event records at the ends of their lists");
    }

    [Fact]
    public void AFileThatIsNotAsLongAsItsCountsSayIsRefused()
    {
        Assert.Throws<ArgumentException>(() => CrimeFileCodec.Load([1, 0, 1], new CampaignTables()));
        Assert.Throws<ArgumentException>(() => CrimeFileCodec.Load(new byte[CrimeFileCodec.Size(2, 3) - 1], new CampaignTables()));
    }
}
