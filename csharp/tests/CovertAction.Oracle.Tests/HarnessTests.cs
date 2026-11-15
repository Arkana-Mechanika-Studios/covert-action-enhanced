using System.Text.Json;
using CovertAction.Core.Crypto;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Crypto;
using CovertAction.Presentation.Library;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The oracle harness itself, on vectors made up here in the generator's format (no game bytes): the loader reads
/// what tools/oracle writes, the recording fake replays and checks boundary calls, the image comparison finds what
/// differs. These always run.
/// </summary>
public sealed class HarnessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-oracle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private const int DgroupSize = 0x2800;
    private const int Holdrand = 0x1982;
    private const int RandomSeed = 0x1d76;
    private const int StackStart = 0x1df0;
    private const int StackEnd = 0x25cc;

    /// <summary>
    /// A made-up vector for seed_random with the clock recorded as one call of the library routine time(), which
    /// answers 0x12345678: the seed is 0x5678, stored at DS:1d76 and in holdrand.
    /// </summary>
    private string WriteSeedRandomVector(bool withStackMask = true, Action<byte[]>? spoil = null, object[]? calls = null, object[]? inputs = null, bool realistic = true)
    {
        var before = new byte[DgroupSize];
        before[Holdrand] = 1;
        // the pointer variables as the program has them: the two ports and the four disk file names
        foreach (var (pointer, target) in new[] { (0x1056, 0x1044), (0x1262, 0x1250), (0x1246, 0x11ce), (0x1248, 0x11d8), (0x124a, 0x11e1), (0x124c, 0x11e9) })
        {
            before[pointer] = (byte)target;
            before[pointer + 1] = (byte)(target >> 8);
        }
        var after = (byte[])before.Clone();
        after[RandomSeed] = 0x78;
        after[RandomSeed + 1] = 0x56;
        after[Holdrand] = 0x78;
        after[Holdrand + 1] = 0x56;
        spoil?.Invoke(after);

        var oracleCase = new Dictionary<string, object?>
        {
            ["id"] = "00-realistic",
            ["origin"] = "made up",
            ["realistic"] = realistic,
            ["args"] = Array.Empty<int>(),
            ["dgroup_in"] = Convert.ToBase64String(before),
            ["shared_in"] = null,
            ["inputs"] = inputs ?? [0x12345678],
            ["result"] = new Dictionary<string, int> { ["ax"] = 0x5678, ["dx"] = 0x1234 },
            ["dgroup_out"] = Convert.ToBase64String(after),
            ["shared_out"] = null,
            ["calls"] = calls ?? [new Dictionary<string, object?> { ["target"] = "_time", ["args"] = new[] { 0 }, ["ret"] = 0x12345678 }],
        };
        if (withStackMask)
        {
            oracleCase["stack_mask"] = new[] { StackStart, StackEnd };
        }
        var vector = new Dictionary<string, object?>
        {
            ["module"] = "CODE",
            ["label"] = "sub_code_0000_2a72",
            ["name"] = "_seed_random",
            ["call"] = new Dictionary<string, object?> { ["far"] = false, ["nargs"] = 0, ["convention"] = "stack", ["entry"] = "00b4:1f32" },
            ["segments"] = new Dictionary<string, object?> { ["psp"] = "352e", ["load"] = "353e", ["dgroup"] = "38ab", ["dgroup_size"] = DgroupSize, ["shared"] = "037a:04c0" },
            ["cases"] = new[] { oracleCase },
        };
        Directory.CreateDirectory(Path.Combine(_folder, "CODE"));
        var path = Path.Combine(_folder, "CODE", "sub_code_0000_2a72.json");
        File.WriteAllText(path, JsonSerializer.Serialize(vector));
        // the generator's own tables lie in the same folder and are not vectors
        File.WriteAllText(Path.Combine(_folder, "CODE", "_index.json"), "{}");
        return path;
    }

    private OracleTally ReplaySeedRandom() => OracleRun.ForEachCase("CODE", "seed_random", c =>
    {
        var state = new CryptoState();
        CodeDgroup.Layout.Load(c.DgroupIn, state);
        var world = new RecordingBoundaries(c.Calls);
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, world);
        world.AssertAllCallsMade();
        OracleRun.AssertImage(CodeDgroup.Layout, state, c.DgroupIn, c.DgroupOut, c.Ignore, c.IgnoreDeclared);
    }, _folder);

    private static CryptoWorkstation Workstation(CryptoState state, RecordingBoundaries world) =>
        new(state, new SharedState(), new Boundaries(world, world, world, world, world, world));

    private static BoundaryCall Recorded(string target, int[] args, long ret = 0, object? input = null) =>
        new(target, [.. args.Select(a => JsonSerializer.SerializeToElement(a))], ret, input is null ? null : JsonSerializer.SerializeToElement(input), null);

    [Fact]
    public void TheLoaderReadsWhatTheGeneratorWrites()
    {
        var vector = OracleVectors.Load(WriteSeedRandomVector());
        Assert.Equal("CODE", vector.Module);
        Assert.Equal("sub_code_0000_2a72", vector.Label);
        Assert.Equal("seed_random", vector.Name);
        Assert.Equal(new OracleSegments(0x352e, 0x353e, 0x38ab, "037a:04c0"), vector.Segments);

        var c = Assert.Single(vector.Cases);
        Assert.Equal("00-realistic", c.Id);
        Assert.Equal("made up", c.Origin);
        Assert.True(c.Realistic);
        Assert.Empty(c.Args);
        Assert.Equal(DgroupSize, c.DgroupIn.Length);
        Assert.Null(c.SharedIn);
        Assert.Equal(0x5678, c.Ax);
        Assert.Equal(0x1234, c.Dx);
        var call = Assert.Single(c.Calls);
        Assert.Equal("time", call.Target);
        Assert.Equal(0x12345678L, call.Ret);
        Assert.Equal(0x12345678L, call.Input?.GetInt64());
        Assert.Equal(new[] { (StackStart, StackEnd) }, c.Ignore);
        Assert.True(c.IgnoreDeclared);
        Assert.Null(c.Exit);
    }

    [Fact]
    public void ScriptedAnswersGoToTheCallsThatConsumeThem()
    {
        // a port write and the program exit consume no input; the interrupt in between does
        object[] calls =
        [
            new Dictionary<string, object?> { ["target"] = "outb", ["args"] = new[] { 0x3c8, 5 }, ["ret"] = 0 },
            new Dictionary<string, object?> { ["target"] = "int33.00", ["args"] = new[] { 3, 0, 0, 0, 0, 0 }, ["ret"] = 3 },
            new Dictionary<string, object?> { ["target"] = "inb", ["args"] = new[] { 0x201 }, ["ret"] = 0xEF },
            new Dictionary<string, object?> { ["target"] = "int21.4c", ["args"] = new[] { 0x4c00, 0, 0, 0, 0, 0 }, ["ret"] = 0 },
        ];
        object[] inputs = [new Dictionary<string, int> { ["bx"] = 1, ["cx"] = 100, ["dx"] = 50 }, 0xEF];
        var c = OracleVectors.Load(WriteSeedRandomVector(calls: calls, inputs: inputs)).Cases[0];

        Assert.Null(c.Calls[0].Input);
        Assert.Equal(100, c.Calls[1].Input?.GetProperty("cx").GetInt32());
        Assert.Equal(0xEF, c.Calls[2].Input?.GetInt32());
        Assert.Null(c.Calls[3].Input);
    }

    [Fact]
    public void VectorsAreFoundByTheFunctionsName()
    {
        WriteSeedRandomVector();
        Assert.Single(OracleVectors.ForFunction("CODE", "seed_random", _folder));
        Assert.Empty(OracleVectors.ForFunction("CODE", "clamp", _folder));
        Assert.Empty(OracleVectors.ForFunction("GAME", "seed_random", _folder));
    }

    [Fact]
    public void AFunctionThatMatchesTheOriginalPasses()
    {
        WriteSeedRandomVector();
        Assert.Equal(1, ReplaySeedRandom().Compared);
    }

    [Fact]
    public void WithoutAStackMaskUnmodelledBytesAreNotHeldAgainstTheFunction()
    {
        WriteSeedRandomVector(withStackMask: false, spoil: after => after[0x2000] = 0xCC);
        ReplaySeedRandom();
    }

    [Fact]
    public void AWrongValueInAModelledFieldFailsAndNamesTheField()
    {
        WriteSeedRandomVector(spoil: after => after[RandomSeed] = 0x79);
        var failure = Assert.Throws<Xunit.Sdk.XunitException>(() => ReplaySeedRandom());
        Assert.Contains("DS:1d76 random_seed", failure.Message);
        Assert.Contains("case 00-realistic", failure.Message);
    }

    [Fact]
    public void ADifferenceInACaseThatIsNotRealisticOnlyCountsItAsOutsideTheModel()
    {
        // a second, realistic case would be needed for the test to count as run; with this one alone it is skipped
        WriteSeedRandomVector(spoil: after => after[RandomSeed] = 0x79, realistic: false);
        var skip = Assert.Throws<SkipException>(() => ReplaySeedRandom());
        Assert.Contains("none of the 1 cases", skip.Message);
    }

    [Fact]
    public void AChangeTheStateDoesNotModelFailsOutsideTheStack()
    {
        WriteSeedRandomVector(spoil: after => after[0x0040] = 0x99);
        var failure = Assert.Throws<Xunit.Sdk.XunitException>(() => ReplaySeedRandom());
        Assert.Contains("0x0040 (not modelled)", failure.Message);

        // inside the stack mask the same change is no difference
        WriteSeedRandomVector(spoil: after => after[StackStart + 0x10] = 0x99);
        ReplaySeedRandom();
    }

    [Fact]
    public void ARangeATestLeavesOutIsNotCompared()
    {
        var c = OracleVectors.Load(WriteSeedRandomVector(spoil: after => after[0x1b6e] = 1)).Cases[0];
        var state = new CryptoState();
        CodeDgroup.Layout.Load(c.DgroupIn, state);
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, new RecordingBoundaries(c.Calls));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => OracleRun.AssertImage(CodeDgroup.Layout, state, c.DgroupIn, c.DgroupOut, c.Ignore, c.IgnoreDeclared));
        OracleRun.AssertImage(CodeDgroup.Layout, state, c.DgroupIn, c.DgroupOut, [.. c.Ignore, (0x1b6e, 0x1b70)], c.IgnoreDeclared);
    }

    [Fact]
    public void ALibraryRoutineRecordedAsItsDosCallIsTakenFromThere()
    {
        // kbhit is DOS function 0Bh, whose AL is the answer (FF: a key is waiting); the other registers are leftovers
        var world = new RecordingBoundaries([Recorded("int21.0b", [0x0bff, 0x1250, 10, 0, 0, 0], ret: 0x0bff), Recorded("int21.0b", [0x0b00, 0, 0, 0, 0, 0], ret: 0x0b00)]);
        Assert.True(world.Kbhit());
        Assert.False(world.Kbhit());

        // _dos_getdrive is DOS function 19h, whose AL is the drive counted from 0
        world = new RecordingBoundaries([Recorded("int21.19", [0x1900, 0, 0, 0, 0, 0], ret: 0x1902)]);
        Assert.Equal(3, world.CurrentDrive());

        // the end of the program takes every DOS call the original's exit made
        world = new RecordingBoundaries([Recorded("int21.3e", [0x3e00, 5, 0, 0, 0, 0]), Recorded("int21.4c", [0x4c00, 0, 0, 0, 0, 0])]);
        world.ProgramEnded();
        world.AssertAllCallsMade();
    }

    [Fact]
    public void ACallTheOriginalDidNotMakeFails()
    {
        var world = new RecordingBoundaries([]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => world.PlaySound(1));
    }

    [Fact]
    public void AMissingOrDifferentCallFails()
    {
        var missing = new RecordingBoundaries([Recorded("play_sound", [0x2d])]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(missing.AssertAllCallsMade);

        var otherRoutine = new RecordingBoundaries([Recorded("play_sound", [0x2d])]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => otherRoutine.ReadKeyRaw());

        var otherArgument = new RecordingBoundaries([Recorded("play_sound", [0x2d])]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => otherArgument.PlaySound(0x2e));

        var same = new RecordingBoundaries([Recorded("play_sound", [0x2d])]);
        same.PlaySound(0x2d);
        same.AssertAllCallsMade();
    }

    [Fact]
    public void NumbersAreComparedAsSixteenBitWords()
    {
        // the generator writes words unsigned: 65533 is -3; and a C# number beyond 16 bits is the word it wraps to
        var world = new RecordingBoundaries([Recorded("play_sound", [65533]), Recorded("play_sound", [5])]);
        world.PlaySound(-3);
        world.PlaySound(65536 + 5);
        world.AssertAllCallsMade();
    }

    [Fact]
    public void ThunkAnswersComeBackAsTheGeneratorScriptedThem()
    {
        var world = new RecordingBoundaries([Recorded("char_width", [2, 77], ret: 6), Recorded("read_key_raw", [], ret: 0x1c0d), Recorded("no_key_waiting", [], ret: 65535), Recorded("no_key_waiting", [])]);
        Assert.Equal(6, world.CharWidth(2, 'M'));
        Assert.Equal(0x1c0d, world.ReadKeyRaw());
        Assert.True(world.NoKeyWaiting());
        Assert.False(world.NoKeyWaiting());
        world.AssertAllCallsMade();
    }

    [Fact]
    public void AFillRecordedAsTheDriverEntriesBeneathItIsTakenFromThere()
    {
        // BUG's copy of fill_rect is recorded as the four driver entries it calls: page (AX), colour (AX), rows (AX to CX), end
        var port = new RasterPort { PageNr = 1, YBgn = 10 };
        BoundaryCall[] Recording(int lastRow) =>
        [
            Recorded("sub_bug_0000_02e1", [1, 0x2ef6, 0, 0, 0, 0]), Recorded("sub_bug_0000_02b0", [7, 0, 0, 0, 0, 0]),
            Recorded("sub_bug_0000_02a2", [15, 0x1395, lastRow, 0, 0, 0]), Recorded("sub_bug_0000_02a9", [0, 0, 0, 0, 0, 0]),
        ];
        var world = new RecordingBoundaries(Recording(lastRow: 18));
        world.FillRect(port, 3, 5, 40, 4, 7);
        world.AssertAllCallsMade();
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => new RecordingBoundaries(Recording(lastRow: 19)).FillRect(port, 3, 5, 40, 4, 7));
    }

    [Fact]
    public void ARoutineIsRecognisedUnderTheNameAnotherProgramGivesIt()
    {
        var world = new RecordingBoundaries([Recorded("draw_string_port", [0x1250, 1, 2, 0x25a0], ret: 9)]);
        Assert.Equal(9, world.DrawString(new RasterPort(), 1, 2, "text"));
        world.AssertAllCallsMade();
    }

    [Fact]
    public void TheFileCallsAreTheLibrarysAndAReadGivesWhatTheOriginalRead()
    {
        var name = JsonSerializer.SerializeToElement(new { ptr = 0x2353, str = Convert.ToBase64String("plot.txt"u8) });
        var read = new BoundaryCall("read", [JsonSerializer.SerializeToElement(5), JsonSerializer.SerializeToElement(0xbdb6), JsonSerializer.SerializeToElement(4)], 3, null, [7, 8, 9]);
        var world = new RecordingBoundaries([
            new BoundaryCall("open", [name, JsonSerializer.SerializeToElement(0x4000), JsonSerializer.SerializeToElement(420)], 5, null, null),
            read, Recorded("lseek", [5, 42, 0, 0], ret: 42), Recorded("close", [5])]);
        Assert.Equal(5, world.Open("plot.txt", OpenMode.Read | OpenMode.Text, 420));
        var buffer = new byte[4];
        Assert.Equal(3, world.Read(5, buffer));
        Assert.Equal([7, 8, 9, 0], buffer);
        Assert.Equal(42, world.Seek(5, 42, 0));
        Assert.Equal(0, world.Close(5));
        world.AssertAllCallsMade();
    }

    [Fact]
    public void TheLibrarysDrawImageDrawsAnImageOfThePartsTableOnTheScreenPort()
    {
        // the part keeps its images in a table; the original passes the table entry, a word that names the image
        var state = new CryptoState();
        var images = new ImageRegister();
        Image?[] sprite = [null, images.For(0x6f10), images.For(0x6f44)];
        var call = new BoundaryCall("draw_image_port",
            [JsonSerializer.SerializeToElement(0x1250), JsonSerializer.SerializeToElement(30), JsonSerializer.SerializeToElement(40), JsonSerializer.SerializeToElement(0x6f44)], 0, null, null);
        var world = new RecordingBoundaries([call], o => ReferenceEquals(o, state.Rp1) ? 0x1250 : null, images);
        new LibraryOutput(state, new Boundaries(world, world, world, world, world, world)).DrawImage(sprite, 2, 30, 40);
        world.AssertAllCallsMade();
    }

    [Fact]
    public void TheJoystickButtonsAreAReadOfTheGamePort()
    {
        var world = new RecordingBoundaries([Recorded("inb", [0x201], ret: 0xEF)]);
        Assert.Equal(0xEF, world.ReadJoystickButtons());
        world.AssertAllCallsMade();
    }

    [Fact]
    public void AFrameWaitTakesThePortReadsUntilTheRetraceHasBegunAndEnded()
    {
        // the display is in its retrace at first: the original waits for it to end, to begin again, and to end
        var world = new RecordingBoundaries([
            Recorded("inb", [0x3da], ret: 0), Recorded("inb", [0x3da], ret: 0), Recorded("inb", [0x3da], ret: 8),
            Recorded("inb", [0x3da], ret: 8), Recorded("inb", [0x3da], ret: 0), Recorded("play_sound", [1])]);
        world.WaitForFrame();
        Assert.Equal(5, world.CallsMade);
    }

    [Fact]
    public void AMouseServiceIsItsInterruptWithTheRegistersItTakesAndGives()
    {
        // "get status" (interrupt 33h, AX = 3): buttons in BX, position in CX and DX; the 7 in BX is a leftover, not compared
        var world = new RecordingBoundaries([Recorded("int33.00", [3, 7, 0, 0, 0, 0], ret: 3, input: new { bx = 1, cx = 100, dx = 50 })]);
        Assert.Equal(new MouseStatus(1, 100, 50), world.GetStatus());

        // a register the handler did not set comes back as it went in
        world = new RecordingBoundaries([Recorded("int33.00", [5, 1, 9, 0, 0, 0], ret: 5, input: new { ax = 2, bx = 3 })]);
        Assert.Equal(new MouseButtonEvents(2, 3, 9, 0), world.ButtonPresses(1));

        // the values a service takes are compared
        world = new RecordingBoundaries([Recorded("int33.00", [4, 0, 10, 20, 0, 0])]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => world.SetPosition(10, 21));

        // the video BIOS: function 5 in AH, the page in AL
        world = new RecordingBoundaries([Recorded("int10.05", [0x0501, 0, 0, 0, 0, 0], ret: 0x0501)]);
        world.SelectDisplayPage(1);
        world.AssertAllCallsMade();
    }

    [Fact]
    public void APortIsComparedByAddressAndATextByContent()
    {
        var port = new RasterPort();
        var text = JsonSerializer.SerializeToElement(new { ptr = 0x10da, str = Convert.ToBase64String("OK"u8) });
        var call = new BoundaryCall("draw_string",
            [JsonSerializer.SerializeToElement(0x1250), JsonSerializer.SerializeToElement(10), JsonSerializer.SerializeToElement(20), text], 0, null, null);
        int? ScreenPort(object o) => ReferenceEquals(o, port) ? 0x1250 : null;

        // the test says the port is the screen port at DS:1250, and the text is the one the original drew
        new RecordingBoundaries([call], ScreenPort).DrawString(port, 10, 20, "OK");

        // another text
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => new RecordingBoundaries([call], ScreenPort).DrawString(port, 10, 20, "NO"));

        // another port
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => new RecordingBoundaries([call], _ => 0x1044).DrawString(port, 10, 20, "OK"));

        // a pointer word without the text behind it: the call is checked, the text cannot be
        var plain = Recorded("draw_string", [0x1250, 10, 20, 0x25a0]);
        new RecordingBoundaries([plain], ScreenPort).DrawString(port, 10, 20, "anything");
    }

    [Fact]
    public void ATableIsComparedByItsCapturedBytes()
    {
        var palette = new byte[17];
        palette[3] = 0x3f;
        var captured = JsonSerializer.SerializeToElement(new { ptr = 0x1174, mem = Convert.ToBase64String(palette) });
        new RecordingBoundaries([new BoundaryCall("set_palette", [captured], 0, null, null)]).SetPalette(palette);

        palette[3] = 0;
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => new RecordingBoundaries([new BoundaryCall("set_palette", [captured], 0, null, null)]).SetPalette(palette));
    }

    [Fact]
    public void AFunctionWithoutVectorsSkipsItsTest()
    {
        var skip = Assert.Throws<SkipException>(() => OracleRun.ForEachCase("CODE", "no_such_function", _ => { }, _folder));
        Assert.Contains("no oracle vectors for CODE no_such_function", skip.Message);
    }

    [Fact]
    public void AFileThatIsNotAVectorIsReportedAsSuch()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "CODE"));
        var path = Path.Combine(_folder, "CODE", "broken.json");
        File.WriteAllText(path, """{"module":"CODE","label":"broken"}""");
        Assert.Throws<InvalidDataException>(() => OracleVectors.Load(path));
    }
}
