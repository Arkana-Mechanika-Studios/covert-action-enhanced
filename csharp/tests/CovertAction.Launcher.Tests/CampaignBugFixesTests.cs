using System.Reflection;
using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Final;
using CovertAction.Presentation.Game;
using Xunit.Abstractions;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The fixes of the option "Also fix campaign-logic bugs" that run on the game's own files (docs/original-bugs.md CA-12 to
/// CA-15, CA-42, CA-44, CA-46): the crime files' events are driven through the plot engine's rules, the debriefing and the
/// career on FINAL's own data. Each bug is shown with the option off (what release 1.0 plays) and fixed with it on.
/// Skipped without the game files, except where a hand-made file is enough.
/// </summary>
public sealed class CampaignBugFixesTests(ITestOutputHelper output) : IDisposable
{
    private static readonly OriginalBehaviour Off = OriginalBehaviour.ForPlayer(false, false), On = OriginalBehaviour.ForPlayer(false, true);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-campaignfix-" + Guid.NewGuid().ToString("N"));
    private readonly List<GameFolderFiles> _files = [];

    public void Dispose()
    {
        _files.ForEach(f => f.Dispose());
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "FINAL.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    // ---- the plot's events (CA-12, CA-13, CA-14) ----

    /// <summary>
    /// A campaign of crime <paramref name="crime"/> as the plot engine plays it with nothing else happening: every cast member
    /// is a person at large, the log has messages. Plot steps are taken until no event can happen; one event is chosen at
    /// random among those that can, as in the game (<see cref="PlotRules.EventReady"/>, <see cref="PlotRules.ScheduleEvent"/>).
    /// </summary>
    /// <returns>For each event whether it happened (false: cancelled, or never reached).</returns>
    private static bool[] PlayPlot(GameFolder game, int crime, int seed, OriginalBehaviour behaviour)
    {
        var tables = new CampaignTables();
        CrimeFileCodec.Load(game.Read($"CRIME{crime}.DTA"), tables);
        var state = new CampaignState { Behaviour = behaviour, NEVENT = (short)tables.Events.Count, NACTIVITY = (short)tables.Cast.Count, NextSyn = 6, ChronTime = 1, Nperson = CampaignState.PersonCount };
        for (var member = 0; member < tables.Cast.Count; member++)
        {
            state.Ac[member].CopyFrom(tables.Cast[member]);
            state.Ac[member].Who = (short)member;
            state.Ac2[member].Chron = -1;
            state.Ps[member].Face = 1;
            state.Ps[member].Loc = (short)member;
        }
        for (var q = 0; q < tables.Events.Count; q++)
        {
            state.Ev[q].CopyFrom(tables.Events[q]);
            state.Ev[q].Chron = CastRules.ChronPending;
        }
        state.Random.Srand(seed);
        for (var step = 0; step < 200; step++)
        {
            state.ChronTime++;
            var ready = Enumerable.Range(0, state.NEVENT - 1).Count(q => PlotRules.EventReady(state, q).Result != -1);
            if (ready == 0)
            {
                break;
            }
            int chosen, answered;
            do
            {
                chosen = GameLibrary.RandomBelow(state.Random, state.NEVENT - 1);
                answered = PlotRules.EventReady(state, chosen).Result;
            }
            while (answered == -1);
            PlotRules.ScheduleEvent(state, chosen, answered);
            for (var q = 0; q < state.NEVENT; q++)
            {
                if (state.Ev[q].Chron == state.ChronTime)
                {
                    PlotRules.QueueMessage(state, PlotRules.MessageOfEvent, q);
                }
            }
        }
        return [.. Enumerable.Range(0, state.NEVENT).Select(q => state.Ev[q].Chron != -1 && state.Ev[q].Chron != CastRules.ChronPending)];
    }

    private static int Happened(GameFolder game, int crime, int eventNo, OriginalBehaviour behaviour, int seeds = 300) =>
        Enumerable.Range(1, seeds).Count(seed => PlayPlot(game, crime, seed, behaviour)[eventNo]);

    [SkippableFact]
    public void Ca12CrimeOnesStoleBlueprintsNeverHappens()
    {
        var game = GameFiles();
        // E8 (member 2, announced, id 2) shares its id with the pair E15/E0, which runs before it can: it is always cancelled
        Assert.Equal(0, Happened(game, 1, 8, Off));
        output.WriteLine("TALLY CA-12: CRIME1 'Stole Blueprints' happened in 0 of 300 plots (option off)");
    }

    [SkippableFact]
    public void Ca12WithTheFixItHappensInEveryPlotAndNothingElseIsLost()
    {
        var game = GameFiles();
        Assert.Equal(300, Happened(game, 1, 8, On));
        // the other events of CRIME1 happen as often as before (the pair E15/E0 still cancels what it should)
        foreach (var other in new[] { 5, 7, 9, 13, 16 })
        {
            Assert.Equal(Happened(game, 1, other, Off, 60), Happened(game, 1, other, On, 60));
        }
    }

    [SkippableFact]
    public void Ca12OnlyCrimeOneSharesAMessageIdBetweenAnAnnouncedEventAndAPair()
    {
        var game = GameFiles();
        var shared = new List<string>();
        for (var crime = 0; crime < 13; crime++)
        {
            var tables = new CampaignTables();
            CrimeFileCodec.Load(game.Read($"CRIME{crime}.DTA"), tables);
            var events = tables.Events.TakeWhile(e => e.Act != -1).ToList();
            for (var q = 0; q < events.Count; q++)
            {
                if ((events[q].Status & 0xfff) == 0 && events.Any(e => e.Synch == events[q].Synch && (e.Status & 0xfff) != 0))
                {
                    shared.Add($"CRIME{crime} E{q} {events[q].Name}");
                }
            }
        }
        output.WriteLine("events without partner that share a message id with a pair: " + string.Join("; ", shared));
        Assert.Equal(["CRIME1 E8 Stole Blueprints"], shared);
    }

    [SkippableFact]
    public void Ca13TheReceiverNamingItsOwnActorMostlyNeverHappens()
    {
        var game = GameFiles();
        // CRIME2 E5 (member 1 "receives from" member 1) and CRIME8 E25 (the Driver from himself)
        var crime2 = Happened(game, 2, 5, Off);
        var crime8 = Happened(game, 8, 25, Off);
        output.WriteLine($"TALLY CA-13: CRIME2 E5 happened in {crime2} of 300 plots, CRIME8 E25 in {crime8} (option off)");
        Assert.InRange(crime2, 1, 120);
        Assert.InRange(crime8, 1, 120);
    }

    [SkippableFact]
    public void Ca13WithTheFixTheReceiverAnswersItsSendersEvent()
    {
        var game = GameFiles();
        Assert.Equal(300, Happened(game, 2, 5, On));
        Assert.Equal(300, Happened(game, 2, 3, On));       // its sender, the other half of the pair
        Assert.Equal(300, Happened(game, 8, 25, On));
        Assert.Equal(300, Happened(game, 8, 18, On));
    }

    [SkippableFact]
    public void Ca13TheLogNamesTheSenderAsThePartnerOfTheFixedReceiver()
    {
        var game = GameFiles();
        var tables = new CampaignTables();
        CrimeFileCodec.Load(game.Read("CRIME2.DTA"), tables);
        var state = new CampaignState { Behaviour = On, NEVENT = (short)tables.Events.Count, NACTIVITY = (short)tables.Cast.Count };
        for (var q = 0; q < tables.Events.Count; q++)
        {
            state.Ev[q].CopyFrom(tables.Events[q]);
        }
        Assert.Equal(0, PlotRules.PartnerMemberOf(state, 5));                    // the receive of "Financed operation" is from the first member
        Assert.Equal(1, PlotRules.PartnerMemberOf(state, 3));                    // the send is to member 1
        state.Behaviour = Off;
        Assert.Equal(1, PlotRules.PartnerMemberOf(state, 5));                    // the original's: himself
    }

    [SkippableFact]
    public void Ca14TwoSendersOfOneIdToOneReceiverAreAlternativesAndStayThat()
    {
        // CRIME6 E2 and E4 send message 1 to the one receive entry E12: exactly one of the two happens, with or without the option.
        // The data reads as the designers' alternatives (either sender, never both), so CA-14 is left as the original has it.
        var game = GameFiles();
        for (var seed = 1; seed <= 100; seed++)
        {
            foreach (var behaviour in new[] { Off, On })
            {
                var happened = PlayPlot(game, 6, seed, behaviour);
                Assert.True(happened[2] ^ happened[4], $"seed {seed}: E2 {happened[2]}, E4 {happened[4]}");
            }
        }
    }

    // ---- CA-12: the bulletin's text ----

    [Fact]
    public void Ca12TheBulletinReadsItsOwnEntryWhereTheFileUsesTheTagTwice()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "TEXT.DTA"), "*MSG0100\r\nA message.\r\n*MSG0102\r\nRumors from the pair.\r\n*MSG0402\r\n*MSG0102\r\nBulletin: blueprints missing.\r\n*MSG0103\r\nNext.\r\n");
        var events = new List<Event>
        {
            new() { Act = 0, Synch = 2, Status = 0x0300 },     // the receiving half of the pair
            new() { Act = 1, Synch = 2, Status = 0x2000 },     // the announced event with the same message id
        };
        Assert.Equal((0, 0), (LibraryTexts.EntriesBeforeEventText(events, 2, 0, On), LibraryTexts.EntriesBeforeEventText(events, 2, 1, Off)));
        Assert.Equal(1, LibraryTexts.EntriesBeforeEventText(events, 2, 1, On));
        string Read(int skip)
        {
            var state = new CampaignState();
            var files = new GameFolderFiles(new GameFolder(_folder));
            _files.Add(files);
            var world = new Boundaries(null!, new SilentSoundDriver(), null!, new NoMouseDriver(), null!, files);
            var text = new CovertAction.Presentation.Library.LibraryText(state, state, world, new CovertAction.Presentation.Library.LibraryOutput(state, world), state.Random, On);
            text.ReadClearedText("TEXT.DTA", "*MSG0102", [], [], 0, skip);
            return state.Txt.Text;
        }
        Assert.Contains("Rumors", Read(0), StringComparison.Ordinal);
        Assert.Contains("Bulletin: blueprints missing", Read(1), StringComparison.Ordinal);
        Assert.DoesNotContain("Rumors", Read(1), StringComparison.Ordinal);
    }

    // CA-7

    [Theory]
    [InlineData(0, 0)]       // a decode before any raid or chase
    [InlineData(2, 60)]      // after a raid that arrested its target
    [InlineData(100, 3000)]  // after a car chase at full alert: fifty hours
    public void Ca7TheVisitAfterTheWorkstationChargesTheLastRaidOrChaseResult(int leftover, int minutes) =>
        Assert.Equal(minutes, HeadquartersScreens.DecodeVisitCharge(Off, leftover));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(100)]
    public void Ca7WithTheFixTheVisitChargesNothingFurther(int leftover)
    {
        Assert.Equal(0, HeadquartersScreens.DecodeVisitCharge(On, leftover));
        Assert.False(OriginalBehaviour.ForPlayer(true, false).FixDecodeVisitLeftoverCharge);
    }

    // ---- FINAL: CA-15, CA-42, CA-44, CA-46 ----

    private FinalState FinalData(GameFolder game)
    {
        var state = new FinalState();
        FinalDgroup.Layout.Load(Executable.Read(game.Read("FINAL.EXE")).DataSegment(), state);
        return state;
    }

    private Boundaries FilesOnly(GameFolder game, IGraphicsDriver? graphics = null, IInputDriver? input = null)
    {
        var files = new GameFolderFiles(game);
        _files.Add(files);
        return new Boundaries(graphics ?? new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]), new SilentSoundDriver(), input ?? new AlternatingKeys(), new NoMouseDriver(), new NoWaitTimer(), files);
    }

    /// <summary>A clock that does not wait for the screen's frames.</summary>
    private sealed class NoWaitTimer : IGameTimer
    {
        public int Time() => 600_000_000;

        public void WaitForFrame()
        {
        }

        public bool ClockTicks(out int ticks)
        {
            ticks = 0;
            return true;
        }
    }

    /// <summary>A keyboard that shows no key waiting once before each key it gives (a box flushes the keys when it opens, then waits).</summary>
    private sealed class AlternatingKeys(params int[] keys) : IInputDriver
    {
        private readonly Queue<int> _keys = new(keys);
        private bool _flushed;

        public bool NoKeyWaiting()
        {
            if (!_flushed)
            {
                _flushed = true;
                return true;
            }
            return _keys.Count == 0 && keys.Length != 0 ? true : false;
        }

        public int ReadKeyRaw()
        {
            _flushed = false;
            return _keys.Count > 0 ? _keys.Dequeue() : 0x1c0d;
        }

        public void CalibrateJoystick()
        {
        }

        public int ReadJoystickAxes() => 0;

        public int ReadJoystickButtons() => 0xff;

        public bool Kbhit() => !NoKeyWaiting();

        public int Getch() => ReadKeyRaw() & 0xff;

        public bool NumLock() => true;

        public void SetNumLock(bool on)
        {
        }
    }

    // CA-15

    private CaseFlagsAfterReport RunReportOfCrime(GameFolder game, int crime, int lastEvent, OriginalBehaviour behaviour)
    {
        var state = FinalData(game);
        var headquarters = new Headquarters(state, new SharedState(), FilesOnly(game), behaviour);
        var tables = new CampaignTables();
        CrimeFileCodec.Load(game.Read($"CRIME{crime}.DTA"), tables);
        state.NEVENT = (short)tables.Events.Count;
        state.NACTIVITY = (short)tables.Cast.Count;
        for (var q = 0; q < tables.Events.Count; q++)
        {
            state.Ev[q].CopyFrom(tables.Events[q]);
        }
        for (var member = 0; member < tables.Cast.Count; member++)
        {
            state.Ac[member].CopyFrom(tables.Cast[member]);
            state.Ac[member].Who = (short)member;
            state.Ps[member].Face = 1;
        }
        state.TheCrime = (short)crime;
        state.NRoster = 0;
        state.CaseFlags = state.EpEarned = state.ObjectsSeenMask = state.EpPossible = 0;
        // the log of the case holds the plot's last event, the crime itself
        state.NextSyn = 1;
        state.Sn[0].Type = 0;
        state.Sn[0].Data = (short)lastEvent;
        headquarters.ShowEfficiencyReport();
        return new(state.CaseFlags, state.EpEarned, state.EpPossible);
    }

    private readonly record struct CaseFlagsAfterReport(int Flags, int Earned, int Possible);

    [SkippableFact]
    public void Ca15CrimeTwelveIsAlwaysPreventedBecauseNoEventIsWorthFive()
    {
        var game = GameFiles();
        // E17 "Made Product", worth 4, is the best event of CRIME12 and has happened
        var report = RunReportOfCrime(game, 12, 17, Off);
        Assert.Equal(0, report.Flags & 0x40);
        Assert.Equal(100 + 40 * 4, report.Possible);          // the "prevented crime" card's 100 points are counted
    }

    [SkippableFact]
    public void Ca15WithTheFixCrimeTwelveCountsAsCarriedOutWhenItsBestEventHappened()
    {
        var game = GameFiles();
        var report = RunReportOfCrime(game, 12, 17, On);
        Assert.Equal(0x40, report.Flags & 0x40);
        Assert.Equal(40 * 4, report.Possible);                // no "prevented crime" card
    }

    [SkippableFact]
    public void Ca15TheOtherCrimesAreCountedAsBefore()
    {
        var game = GameFiles();
        // CRIME1's best event is worth 7 (E16), its other (E13, worth 3) does not count as the crime: unchanged by the fix
        Assert.Equal(RunReportOfCrime(game, 1, 13, Off), RunReportOfCrime(game, 1, 13, On));
        Assert.Equal(RunReportOfCrime(game, 1, 16, Off), RunReportOfCrime(game, 1, 16, On));
        Assert.Equal(0x40, RunReportOfCrime(game, 1, 16, On).Flags & 0x40);
        Assert.Equal(0, RunReportOfCrime(game, 1, 13, On).Flags & 0x40);
    }

    // CA-46

    private static void EnterIntoCareer(Headquarters headquarters, int rating) =>
        typeof(Headquarters).GetMethod("EnterIntoCareer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(headquarters, [rating]);

    /// <summary>Two cases entered into the career, the first rated 255 (low byte 0xff), and the career record read as the career summary reads it.</summary>
    private List<(int Plot, int Rating)> CareerAfterTwoCases(OriginalBehaviour behaviour, int firstRating, int secondRating)
    {
        var state = new FinalState();
        var headquarters = new Headquarters(state, new SharedState(), new Boundaries(null!, null!, null!, null!, null!, null!), behaviour);
        state.Pl.History[0] = 0xff;
        state.Pl.Type = 1;
        state.Pl.Index = 0;
        state.CaseFlags = 0;
        EnterIntoCareer(headquarters, firstRating);
        state.Pl.Type = 2;
        state.Pl.Index = 1;
        EnterIntoCareer(headquarters, secondRating);
        // the summary's walk: a record is four bytes, five when its second byte has the MasterMind-arrested bit
        var records = new List<(int, int)>();
        for (var at = 0; state.Pl.History[at] != 0xff && at < 0x100;)
        {
            records.Add((state.Pl.History[at] >> 4, (short)((state.Pl.History[at + 2] << 8) + state.Pl.History[at + 3])));
            at += (state.Pl.History[at + 1] & 0x80) != 0 ? 5 : 4;
        }
        return records;
    }

    [Fact]
    public void Ca46ACaseRatedTwoFiftyFiveIsWrittenOverByTheNextCase()
    {
        // the original takes the rating's low byte 0xff for the end of the record: the second case lands on it
        var career = CareerAfterTwoCases(Off, 255, 300);
        Assert.NotEqual([(1, 255), (2, 300)], career);
        Assert.NotEqual(255, career[0].Rating);
    }

    [Fact]
    public void Ca46WithTheFixTheNextCaseIsAppendedAndTheFileKeepsItsFormat()
    {
        Assert.Equal([(1, 255), (2, 300)], CareerAfterTwoCases(On, 255, 300));
        Assert.Equal([(1, 511), (2, 1023)], CareerAfterTwoCases(On, 511, 1023));
        // without a 0xff in a rating both behave the same, byte for byte
        Assert.Equal(CareerAfterTwoCases(Off, 100, 200), CareerAfterTwoCases(On, 100, 200));
    }

    [Fact]
    public void Ca46ACareerWrittenWithTheFixIsTheSameFileAndReadsBackWithTheOptionOff()
    {
        var state = new FinalState();
        var headquarters = new Headquarters(state, new SharedState(), new Boundaries(null!, null!, null!, null!, null!, null!), On);
        state.Pl.History[0] = 0xff;
        foreach (var rating in new[] { 255, 511, 40 })
        {
            state.Pl.Type = 3;
            state.Pl.Index = 2;
            EnterIntoCareer(headquarters, rating);
        }
        // the plot record of the campaign (the career file's part) has one size and layout whichever way it was written
        var image = new byte[654];
        CampaignLayouts.Plot.Store(state.Pl, image);
        var back = new Plot();
        CampaignLayouts.Plot.Load(image, back);
        Assert.Equal(state.Pl.History, back.History);
        Assert.Equal(512, back.History.Length);
        Assert.Equal(3, state.Pl.Ncase);
        // the original's reader (and the rewrite's with the option off) walks four bytes a record: three cases, then the end mark
        Assert.Equal([0x32, 0x00, 0x00, 0xff, 0x32, 0x00, 0x01, 0xff, 0x32, 0x00, 0x00, 40, 0xff], back.History[..13]);
    }

    // CA-44

    /// <summary>The variant the apartment scene settles on after a retry: whether the crime file it keeps is one of the case's own plot.</summary>
    private bool ApartmentSceneKeepsThePlot(GameFolder game, OriginalBehaviour behaviour, int seed)
    {
        var state = FinalData(game);
        var shared = new SharedState { GfxLetter = (short)'M' };
        // only the world files: the briefing's texts and animation files are missing, so no box is shown
        Directory.CreateDirectory(_folder);
        foreach (var world in new[] { "WORLD0.DTA", "WORLD1.DTA", "WORLD2.DTA" })
        {
            File.WriteAllBytes(Path.Combine(_folder, world), game.Read(world));
        }
        var worldFiles = new GameFolderFiles(new GameFolder(_folder));
        _files.Add(worldFiles);
        var boundaries = new Boundaries(new McgaGraphicsDriver(new FontSet(game.Read("FONTS.CV")), new byte[3 * McgaDriverFile.Colours]), new SilentSoundDriver(), new AlternatingKeys(), new NoMouseDriver(), new NoWaitTimer(), worldFiles);
        var headquarters = new Headquarters(state, shared, boundaries, behaviour);
        headquarters.LoadWorldFile(0);
        state.BriefingShown = true;
        state.Diff = 1;
        state.Pl.Ncase = 1;
        state.Pl.Type = 1;
        state.Pl.Index = -1;
        state.Pl.Done = 0x3f;                      // every one of the plot's six variants was played: no variant fits
        state.Random.Srand(seed);
        headquarters.ApartmentScene();
        return state.Plots[state.Pl.Type].CrimeFile[state.Pl.Index] == state.Pl.Crime;
    }

    [SkippableFact]
    public void Ca44AfterANewCaseTheOldPlotsVariantsAreStillSearched()
    {
        var game = GameFiles();
        var results = Enumerable.Range(1, 12).Select(seed => ApartmentSceneKeepsThePlot(game, Off, seed)).ToList();
        output.WriteLine("TALLY CA-44: crime file belongs to the case's plot in " + results.Count(r => r) + " of 12 seeds (option off)");
        Assert.Contains(false, results);
    }

    [SkippableFact]
    public void Ca44WithTheFixTheNewPlotsVariantsAreSearched()
    {
        var game = GameFiles();
        Assert.All(Enumerable.Range(1, 12), seed => Assert.True(ApartmentSceneKeepsThePlot(game, On, seed), $"seed {seed}"));
    }

    // CA-42

    private int[] TrainedSkills(GameFolder game, OriginalBehaviour behaviour, params int[] choices)
    {
        const int down = 0x5000, enter = 0x1c0d, escape = 0x011b;
        var state = FinalData(game);
        var keys = new List<int>();
        foreach (var choice in choices)
        {
            keys.AddRange(Enumerable.Repeat(down, choice));
            keys.Add(enter);
            keys.AddRange(Enumerable.Repeat(0x4800, choice));      // back up for the next choice
        }
        if (choices.Length < 4)
        {
            keys.Add(escape);
        }
        var headquarters = new Headquarters(state, new SharedState(), FilesOnly(game, input: new AlternatingKeys([.. keys])), behaviour);
        state.Skills = 0;
        state.Diff = 2;
        headquarters.TrainingMenu(0);
        return [.. Enumerable.Range(0, 4).Select(skill => state.Skills >> 2 * skill & 3)];
    }

    [SkippableFact]
    public void Ca42TheSkillBehindTheLastRaisedOneIsLoweredAfterItsRatingWasShown()
    {
        var game = GameFiles();
        // skills 0, 0, 1, 2 chosen: ratings 2, 1, 1, 0 before the lowering. The rating tested is [4 - spent] = [0] when all four points are spent
        var skills = TrainedSkills(game, Off, 0, 0, 1, 2);
        output.WriteLine("skills after 0,0,1,2: " + string.Join(",", skills));
        Assert.Equal(1, skills[0]);                // shown as 2, played as 1
    }

    [SkippableFact]
    public void Ca42WithTheFixTheShownRatingsAreTheSkills()
    {
        var game = GameFiles();
        var skills = TrainedSkills(game, On, 0, 0, 1, 2);
        Assert.Equal([2, 1, 1, 0], skills);
    }
}
