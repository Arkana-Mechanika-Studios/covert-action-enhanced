using CovertAction.Content;
using CovertAction.Core.Electronics;
using CovertAction.Core.Final;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Persistence;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// Handing the campaign on (<see cref="LibraryHandOff"/>) on states made up for the test, with the expected values
/// worked out from the original's C (FINAL 0100:a2f6 and a6dd.c): which variable goes into which word
/// of the hand-off block and back, what a save file holds, and the box for a file that is not there. The recorded
/// runs of the original do not tell all the scalars apart (several are 0 in every one of them); these do.
/// </summary>
public sealed class LibraryHandOffTests : IDisposable
{
    private readonly DirectoryInfo _game = Directory.CreateTempSubdirectory("covert-game-");
    private readonly DirectoryInfo _saves = Directory.CreateTempSubdirectory("covert-saves-");
    private readonly List<GameFolderFiles> _made = [];

    public void Dispose()
    {
        _made.ForEach(files => files.Dispose());
        _game.Delete(recursive: true);
        _saves.Delete(recursive: true);
    }

    /// <summary>The three parts that carry the routines, each with an empty state.</summary>
    public static IEnumerable<object[]> Parts =>
    [
        [new Func<ICampaignCopy>(() => new FinalState())],
        [new Func<ICampaignCopy>(() => new RaidState())],
        [new Func<ICampaignCopy>(() => new ElectronicsState())],
    ];

    /// <summary>What the message box was last asked to show, and where.</summary>
    private (string Text, int X, int Y)? _box;

    private LibraryHandOff HandOff(ICampaignCopy state, SharedState shared)
    {
        // the texts are the program's own; a state loaded from the player's program has them
        state.HandOff.StrEnvSve = "env.sve";
        state.HandOff.StrEnvSveRead = "env.sve";
        state.HandOff.StrFileError = "File Error: ";
        state.HandOff.StrFileErrorEnd = "\n";
        _made.Add(new GameFolderFiles(new GameFolder(_game.FullName), _saves.FullName));
        // the hand-off reaches nothing but the files
        var world = new Boundaries(null!, null!, null!, null!, null!, _made[^1]);
        return new LibraryHandOff(state, shared, world, (text, x, y) => _box = (text.Text, x, y));
    }

    /// <summary>A campaign in which every scalar has a value of its own, and each table a record that can be told.</summary>
    private static void Fill(ICampaignCopy state)
    {
        state.InCity = 3;
        state.AtLoc = 0x102;
        state.Status = (Options)0x0304;
        state.TheCrime = 0x104;
        state.TheOrg1 = 0x105;
        state.TheOrg2 = 0x106;
        state.TheCity1 = 0x107;
        state.TheCity2 = 0x108;
        state.Gtime = 0x109;
        state.Nperson = 0x10a;
        state.Nloc = 0x10b;
        state.ChronTime = 0x10c;
        state.MacGuffin = 0x10d;
        state.Diff = 0x10e;
        state.TheSeed = 0x10f;
        state.NACTIVITY = 3;
        state.NEVENT = 5;
        state.Sprung = 0x112;
        state.GMode = (sbyte)'C';
        state.NextSyn = 0x114;
        state.Doubles = 0x115;
        state.InterPol = 0x116;
        state.Skills = 0x117;
        state.Alerted = 0x118;
        state.Pl.Crime = 7;
        state.Ac[2].Who = 21;
        state.Ct[3].Name = "Rome";
        state.Org[15].Size = 9;
        state.Ps[63].Org = 4;
        state.Active[15] = 0x201;
        state.OrgCounters[0] = 0x202;
        state.OrgCounters[15] = 0x203;
    }

    private static void AssertSameCampaign(ICampaignCopy expected, ICampaignCopy actual)
    {
        Assert.Equal(
            new[] { expected.InCity, expected.AtLoc, (short)expected.Status, expected.TheCrime, expected.TheOrg1, expected.TheOrg2, expected.TheCity1, expected.TheCity2, expected.Gtime, expected.Nperson, expected.Nloc, expected.ChronTime, expected.MacGuffin, expected.Diff, expected.TheSeed, expected.NACTIVITY, expected.NEVENT, expected.Sprung, expected.NextSyn, expected.Doubles, expected.InterPol, expected.Skills, expected.Alerted },
            new[] { actual.InCity, actual.AtLoc, (short)actual.Status, actual.TheCrime, actual.TheOrg1, actual.TheOrg2, actual.TheCity1, actual.TheCity2, actual.Gtime, actual.Nperson, actual.Nloc, actual.ChronTime, actual.MacGuffin, actual.Diff, actual.TheSeed, actual.NACTIVITY, actual.NEVENT, actual.Sprung, actual.NextSyn, actual.Doubles, actual.InterPol, actual.Skills, actual.Alerted });
        Assert.Equal(expected.Pl.Crime, actual.Pl.Crime);
        Assert.Equal(expected.Pl.Date, actual.Pl.Date);
        Assert.Equal(expected.Ac[2].Who, actual.Ac[2].Who);
        Assert.Equal(expected.Ct[3].Name, actual.Ct[3].Name);
        Assert.Equal(expected.Org[15].Size, actual.Org[15].Size);
        Assert.Equal(expected.Ps[63].Org, actual.Ps[63].Org);
        Assert.Equal(expected.Active, actual.Active);
        Assert.Equal(expected.OrgCounters, actual.OrgCounters);
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void TheHandOffPutsEachScalarIntoItsWordAndTheTablesBehindThem(Func<ICampaignCopy> newState)
    {
        var (state, shared) = (newState(), new SharedState());
        Fill(state);
        HandOff(state, shared).WriteEnvState("env.sve");

        // the words of the block in their order from byte 0x20 on (the C's R[0x10] ... R[0x29])
        Assert.Equal(
            new short[] { 3, 0x102, 0x304, 0x104, 0x105, 0x106, 0x107, 0x108, 0x109, 0x10a, 0x10b, 0x10c, 0x10d, 0x10e, 0x10f, 3, 5, 0x112, (short)'C', 0x114, 0x115, 0x116, 0x117, 0x118 },
            new[] { shared.City, shared.Org, (short)shared.Flags, shared.CaseNo, shared.PlotOrg2, shared.PlotOrg3, shared.PlotCity2, shared.PlotCity3, shared.Clock, shared.NPersons, shared.NLinks, shared.PlotStep, shared.MacGuffin, shared.Level, shared.RandomSeed, shared.NCast, shared.NEvents, shared.SelPerson, shared.GfxLetter, shared.NMsgs, shared.CityMask, shared.InterPol, shared.SkillBits, shared.Heat });
        // the plot's head keeps the clock; the city's name goes there only for a save file
        Assert.Equal(0x109, state.Pl.Date);
        Assert.Equal("", state.Pl.City.Text);
        Assert.Equal(3, shared.Campaign.Cast.Count);
        Assert.Equal(5, shared.Campaign.Events.Count);
        Assert.Equal(21, shared.Campaign.Cast[2].Who);
        Assert.Equal(0x201, shared.Campaign.Active[15]);
        Assert.Equal(0x203, shared.Campaign.Active[31]);
        // the cursor stands behind the last table
        Assert.Equal(SharedState.TablesOffset + CampaignTablesCodec.Size(3, 5), state.HandOff.SharedCursor);

        var taken = newState();
        HandOff(taken, shared).ReadEnvState("env.sve");
        AssertSameCampaign(state, taken);
        Assert.Equal((sbyte)'C', taken.GMode);
        Assert.True(taken.CGA);
        Assert.Equal(SharedState.TablesOffset + CampaignTablesCodec.Size(3, 5), taken.HandOff.SharedCursor);
        Assert.Empty(_saves.GetFiles());
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void ASaveFileIsTheBlocksHeadAndTheTwelveTablesAndReadsBackTheSame(Func<ICampaignCopy> newState)
    {
        var (state, shared) = (newState(), new SharedState());
        Fill(state);
        HandOff(state, shared).WriteEnvState("CV0.SVE");

        var file = Assert.Single(_saves.GetFiles());
        Assert.Equal(SaveFileCodec.Size(3, 5), file.Length);
        Assert.Equal(0, state.HandOff.SharedCursor);
        // the city the agent is in names the save
        Assert.Equal("Rome", state.Pl.City.Text);
        // a save to a file leaves the hand-off block's tables alone
        Assert.Empty(shared.Campaign.Cast);

        // another session: its own graphics letter and drivers stay, whatever the file says
        var (taken, block) = (newState(), new SharedState { SegGfx = 0x1111, SegMisc = 0x2222, SegSnd = 0x3333 });
        taken.GMode = (sbyte)'M';
        HandOff(taken, block).ReadEnvState("CV0.SVE");
        AssertSameCampaign(state, taken);
        Assert.Equal("Rome", taken.Pl.City.Text);
        Assert.Equal((sbyte)'M', taken.GMode);
        Assert.False(taken.CGA);
        Assert.Equal((0x1111, 0x2222, 0x3333), (block.SegGfx, block.SegMisc, block.SegSnd));
        Assert.Equal(0, taken.HandOff.SharedCursor);
        Assert.Null(_box);
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void AFileThatIsNotThereIsReportedInABoxAndNothingIsTakenOver(Func<ICampaignCopy> newState)
    {
        var (state, shared) = (newState(), new SharedState { City = 9 });
        state.InCity = 2;
        HandOff(state, shared).ReadEnvState("CV3.SVE");
        Assert.Equal(("File Error: CV3.SVE\n", 100, 80), _box);
        Assert.Equal(2, state.InCity);
        Assert.Equal(-1, state.HandOff.StateHandle);
    }
}
