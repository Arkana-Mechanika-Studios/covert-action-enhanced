using System.Buffers.Binary;
using System.Text;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Final;
using CovertAction.Presentation.Game;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Library routines that call a routine of their part (a hook: the part's clock text, its event text, its clue label
/// and subject), replayed on the part's own state and layout, with the part's ported routines as the hooks: the
/// campaign (GAME) and the menus (FINAL). Skipped without vectors.
/// </summary>
public sealed class LibraryModuleOracleTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData("append_activity_text")]
    [InlineData("show_chronology")]
    [InlineData("trim_text_to_width")]
    [InlineData("format_clock_time")]
    public void GamesCopyDoesWhatTheOriginalDid(string name)
    {
        var tally = TwinCopies.CasesOrTwin("GAME", name, () => OracleRun.ForEachCase("GAME", name, oracleCase =>
        {
            var state = new CampaignState();
            var reasons = GameDgroup.Layout.OutsideModel(oracleCase.DgroupIn);
            if (reasons.Count > 0)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            GameDgroup.Layout.Load(oracleCase.DgroupIn, state);
            var world = new RecordingBoundaries(oracleCase.Calls, o => GameDgroup.Layout.AddressOf(state, o), state.Images);
            var screens = new CampaignScreens(state, new Boundaries(world, world, world, world, world, world));
            var image = new Image(oracleCase);
            int? result = null;
            switch (name)
            {
                case "append_activity_text":
                    const int phraseTable = 0x2b3e, phrases = 40, period = 0x31c2;
                    CampaignLibrary.AppendActivityText(state.Txt, state.Cl, image.TextTable(phraseTable, phrases), image.TextAt(period), oracleCase.Args[0],
                        key => CampaignTexts.AppendClueLabel(state, key), (clue, subject) => CampaignTexts.AppendClueSubject(state, clue, subject));
                    break;
                case "show_chronology":
                    // through the part's own wiring: its tables, its texts from its layout, its hooks
                    screens.ShowChronology(oracleCase.Args[0] != 0);
                    break;
                case "trim_text_to_width":
                    screens.Text.TrimTextToWidth(oracleCase.Args[0]);
                    break;
                case "format_clock_time":
                    image.CompareClock(0x1e0a, state.Gtime, oracleCase.Args[0]);
                    break;
            }
            Finish(oracleCase, world, GameDgroup.Layout, state, image, result);
        }), output);
        Report("GAME", name, tally);
    }

    [SkippableTheory]
    [InlineData("show_chronology")]
    [InlineData("trim_text_to_width")]
    [InlineData("format_clock_time")]
    public void FinalsCopyDoesWhatTheOriginalDid(string name)
    {
        var tally = OracleRun.ForEachCase("FINAL", name, oracleCase =>
        {
            var state = new FinalState();
            var reasons = FinalDgroup.Layout.OutsideModel(oracleCase.DgroupIn);
            if (reasons.Count > 0)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            FinalDgroup.Layout.Load(oracleCase.DgroupIn, state);
            var world = new RecordingBoundaries(oracleCase.Calls, o => FinalDgroup.Layout.AddressOf(state, o), state.Images);
            var hq = new Headquarters(state, new SharedState(), new Boundaries(world, world, world, world, world, world), OriginalBehaviour.Faithful);
            var image = new Image(oracleCase);
            switch (name)
            {
                case "show_chronology":
                    var dossiers = new LibraryDossiers(state, new FinalCampaign(state), state, ChronologyTexts(image, FinalChronology), hq.Output, hq.Text, hq.Screens, state, state.Random);
                    dossiers.ShowChronology(oracleCase.Args[0] != 0, minutes => CaseReview.FormatGameTime(state, minutes, OriginalBehaviour.Faithful), hq.ShowEventMessage);
                    break;
                case "trim_text_to_width":
                    hq.Text.TrimTextToWidth(oracleCase.Args[0]);
                    break;
                case "format_clock_time":
                    image.CompareClock(0x4706, state.Gtime, oracleCase.Args[0]);
                    break;
            }
            Finish(oracleCase, world, FinalDgroup.Layout, state, image, null);
        });
        Report("FINAL", name, tally);
    }

    private void Report(string module, string name, OracleTally? own)
    {
        if (own is not { } tally)
        {
            return;
        }
        output.WriteLine($"TALLY {module} {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {name} {line}");
        }
    }

    private static void Finish<TState>(OracleCase oracleCase, RecordingBoundaries world, StateLayout<TState> layout, TState state, Image image, int? result)
    {
        if (result is { } value)
        {
            Assert.True(unchecked((short)value) == oracleCase.Ax, $"the C# returns {value}, the original {oracleCase.Ax}");
        }
        world.AssertAllCallsMade();
        OracleRun.AssertImage(layout, state, oracleCase.DgroupIn, oracleCase.DgroupOut, [.. oracleCase.Ignore, .. image.ComparedByHand], oracleCase.IgnoreDeclared);
    }

    /// <summary>Where the chronology's texts lie: titles, further page's titles, the joining words, quotes, "Meeting Noted", "...more", "none".</summary>
    private static readonly int[] FinalChronology = [0x471f, 0x472a, 0x4762, 0x476d, 0x472f, 0x473d, 0x4748, 0x474a, 0x474c, 0x475a, 0x4772, 0x3fde];

    private static DossierTexts ChronologyTexts(Image image, int[] at)
    {
        var texts = new DossierTexts
        {
            ChronologyTitle = image.TextAt(at[0]), NewsTitle = image.TextAt(at[1]), ChronologyHeader = image.TextAt(at[2]), NewsHeader = image.TextAt(at[3]),
            SentMsgTo = image.TextAt(at[4]), MetWith = image.TextAt(at[5]), NewsQuoteOpen = image.TextAt(at[6]), NewsQuoteClose = image.TextAt(at[7]),
            MeetingNoted = image.TextAt(at[8]), ChronologyMore = image.TextAt(at[9]), NoEntries = image.TextAt(at[10]), Ellipsis = image.TextAt(at[11]),
        };
        return texts;
    }

    /// <summary>A case's memory, for the texts and buffers a test reads or compares by hand.</summary>
    private sealed class Image(OracleCase oracleCase)
    {
        public List<(int Start, int End)> ComparedByHand { get; } = [];

        public string TextAt(int address)
        {
            var end = Array.IndexOf(oracleCase.DgroupIn, (byte)0, address);
            return Encoding.Latin1.GetString(oracleCase.DgroupIn, address, end - address);
        }

        public string[] TextTable(int address, int count) =>
            [.. Enumerable.Range(0, count).Select(i => TextAt(BinaryPrimitives.ReadUInt16LittleEndian(oracleCase.DgroupIn.AsSpan(address + 2 * i))))];

        /// <summary>Runs format_clock_time on the part's clock text (not in the part's state yet) and compares it by hand; the result is the text's address.</summary>
        public void CompareClock(int address, int gtime, int seconds)
        {
            const int length = 8;
            var text = Encoding.Latin1.GetString(oracleCase.DgroupIn, address, length).ToCharArray();
            LibraryTexts.FormatClockTime(text, gtime, seconds);
            Assert.Equal(Encoding.Latin1.GetString(oracleCase.DgroupOut, address, length), new string(text));
            Assert.True((ushort)oracleCase.Ax == address, "the original returns its clock text");
            ComparedByHand.Add((address, address + length));
        }
    }

    /// <summary>The campaign's tables of the menus part, as the library's routines see them.</summary>
    private sealed class FinalCampaign(FinalState state) : ICampaignTables
    {
        public IReadOnlyList<Person> Ps => state.Ps;
        public IReadOnlyList<Org> Orgs => state.Org;
        public IReadOnlyList<City> Ct => state.Ct;
        public IReadOnlyList<Clue> Cl => state.Cl;
        public IReadOnlyList<Synopsis> Sn => state.Sn;
        public Synopsis MessageBeforeFirst => throw new OutsideModelException("message -1 of the menus part is not modelled here");
        public IReadOnlyList<Event> Ev => state.Ev;
        public IReadOnlyList<Activity> Ac => state.Ac;
        public Plot Pl => state.Pl;
        public IList<short> Active => throw new OutsideModelException("not used here");
        public IList<short> OrgCounters => throw new OutsideModelException("not used here");
        public short Nperson => state.Nperson;
        public short NextSyn => state.NextSyn;
        public short NACTIVITY => state.NACTIVITY;
        public short Diff => state.Diff;
        public short TheCrime => state.TheCrime;
        public short InterPol { get => throw new OutsideModelException("the menus part's InterPol is not modelled"); set => throw new OutsideModelException("the menus part's InterPol is not modelled"); }
    }
}
