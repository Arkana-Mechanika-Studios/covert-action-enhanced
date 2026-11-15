using System.Buffers.Binary;
using System.Text;
using CovertAction.Core;
using CovertAction.Core.Electronics;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Msc;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Electronics;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The electronics part (BUG.EXE) against the recorded runs of the original. Two kinds of test:
/// <list type="bullet">
/// <item>the functions the C# reconstructs, each called as the original was: same data segment afterwards, same
/// calls across the boundaries;</item>
/// <item>the circuit rules (<see cref="CircuitRules"/>), which the original works out inside functions that also draw
/// (<c>build_circuit_board</c>, <c>bug_status_message</c>). Those functions wait for picture routines of the graphics
/// boundary; until then their recorded runs are replayed through the rules alone, in the order of the original's C,
/// with the drawing left out, and the board they leave is compared with the original's.</item>
/// </list>
/// Skipped without vectors.
/// </summary>
public sealed class BugOracleTests(ITestOutputHelper output)
{
    private const string Module = "BUG";

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it, and the recorded outside world.</summary>
    private sealed class Run
    {
        private readonly List<(int Start, int End)> _ignore;

        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            State = Load(oracleCase.DgroupIn);
            if (oracleCase.SharedIn is { } shared)
            {
                SharedBlockCodec.Layout.Load(shared, Shared);
                // the campaign's tables, which a part that takes the campaign over reads from the block
                SharedBlockCodec.LoadCampaign(shared, Shared);
            }
            _ignore = [.. oracleCase.Ignore, .. BugDgroup.Scratch, .. CampaignCopyLayout.BugScratch];
            World = new RecordingBoundaries(oracleCase.Calls, o => BugDgroup.Layout.AddressOf(State, o), State.Images);
            Bench = new ElectronicsWorkbench(State, Shared, new Boundaries(World, World, World, World, World, World), OriginalBehaviour.Faithful);
        }

        public OracleCase Case { get; }

        public ElectronicsState State { get; }

        public SharedState Shared { get; } = new();

        public RecordingBoundaries World { get; }

        public ElectronicsWorkbench Bench { get; }

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>The text a pointer argument points at in the original's data segment.</summary>
        public string Text(int index)
        {
            var text = Case.DgroupIn.AsSpan((ushort)Arg(index));
            return Encoding.Latin1.GetString(text[..text.IndexOf((byte)0)]);
        }

        /// <summary>Whether a pointer argument points at the text buffer.</summary>
        public bool IsMessageBuffer(int index) => BugDgroup.Layout.AddressOf(State, State.MessageText.Bytes) == (ushort)Arg(index);

        /// <summary>The word at the address a pointer argument holds, before the call.</summary>
        public int Input(int index) => BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupIn.AsSpan((ushort)Arg(index)));

        /// <summary>The word the original left at the address a pointer argument holds (an output), which is then not compared as memory.</summary>
        public int Output(int index)
        {
            var address = (ushort)Arg(index);
            _ignore.Add((address, address + 2));
            return BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupOut.AsSpan(address));
        }

        /// <summary>
        /// Runs a function that loads pictures. The library's loader decodes a picture at once (D-12), so the working
        /// memory of the original's row-by-row reader and decoder is left out of the comparison (<see cref="PictureLoaderScratch"/>).
        /// </summary>
        public void LoadingPictures(Action call)
        {
            _ignore.AddRange(PictureLoaderScratch);
            call();
        }

        /// <summary>Compares everything the function left behind with the original.</summary>
        public void Finish(bool exited)
        {
            Assert.True(exited == Case.Exit is not null, exited ? "the C# ended the program, the original did not" : "the original ended the program, the C# did not");
            World.AssertAllCallsMade();
            // the C library's file routines keep a stream record and a 512-byte buffer on the near heap, beneath the
            // IFiles boundary; the buffer's address is the second argument of each read
            foreach (var call in Case.Calls.Where(c => c.Target == "read"))
            {
                var buffer = (ushort)call.Args[1].GetInt64();
                _ignore.Add((buffer - 8, buffer + 512));
            }
            if (Case.Calls.Any(c => c.Target == "open"))
            {
                _ignore.AddRange(LibraryFileInternals);
            }
            OracleRun.AssertImage(BugDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut, _ignore, Case.IgnoreDeclared);
            if (Case.SharedIn is not null && Case.SharedOut is not null)
            {
                OracleRun.AssertHandedOverBlock(Case, Shared, State.HandOff.SharedCursor);
            }
        }
    }

    /// <summary>
    /// The C library's own variables that its file routines change beneath the <c>IFiles</c> boundary in BUG's data:
    /// the near heap's pointer, the file table, the stream record, the streams' second table and the heap's block
    /// headers. They have no symbols; the ranges are where BUG's recorded runs that read a file change bytes no routine
    /// of the game owns (found on load_event_text, now the library's).
    /// </summary>
    private static readonly (int Start, int End)[] LibraryFileInternals =
        [(0x33e8, 0x33ea), (0x34a0, 0x3580), (0x35c0, 0x35e0), (0x5510, 0x5540), (0x9a10, 0xa000)];

    /// <summary>
    /// The working memory of BUG's row-by-row picture reader and decoder, which no routine of the game reads
    /// afterwards: the decoder's tables at 3dfe..5316 and 5457..5515, the read pointer at 726e, the 512-byte block
    /// buffer at 7dba and the row buffer at 8e56. Found as the bytes the recorded load_bugs_pic changes outside the model.
    /// </summary>
    private static readonly (int Start, int End)[] PictureLoaderScratch = [
        (0x3af6, 0x5316),   // pic_handle, the memory source's position and buffer, and the decoder's tables (bug_init's three pictures reach below 3dfe)
        (0x5457, 0x5515), (0x726e, 0x7270),
        (0x7db6, 0x7dba),   // pic_read_hook: the far callback the decoder reads its input through (found on bug_init)
        (0x7dba, 0x7fba), (0x8e56, 0x8f97),
    ];

    /// <summary>The state as a data segment of the original holds it.</summary>
    private static ElectronicsState Load(byte[] dgroup)
    {
        if (BugDgroup.Layout.OutsideModel(dgroup) is { Count: > 0 } reasons)
        {
            throw new OutsideModelException(string.Join("; ", reasons));
        }
        var state = new ElectronicsState();
        BugDgroup.Layout.Load(dgroup, state);
        return state;
    }

    /// <summary>The functions and how each is called from a recorded case.</summary>
    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        ["select_palette"] = r => Nothing(() => r.Bench.SelectPalette(r.Arg(0))),
        ["shutdown_sound"] = _ => Nothing(ElectronicsWorkbench.ShutdownSound),
        ["draw_timer"] = r => Nothing(r.Bench.DrawTimer),
        ["draw_image"] = r => Nothing(() => r.Bench.Output.DrawImage(r.State.Sprite, r.Arg(0), r.Arg(1), r.Arg(2))),
        ["seed_random"] = r => Nothing(() => r.State.RandomSeed = LibraryRandom.SeedRandom(r.State.Random, r.World)),
        ["draw_puzzle_image"] = r => Nothing(() => r.Bench.DrawPuzzleImage(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_wire_cell"] = r => Nothing(() => r.Bench.DrawWireCell(r.Arg(0), r.Arg(1), r.Arg(2))),
        // the last argument is a long (0L or 1L): two words
        ["draw_chip_slot"] = r => Nothing(() => r.Bench.DrawChipSlot(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3) != 0 || r.Arg(4) != 0)),
        ["message_box"] = r => Nothing(() => r.Bench.MessageBox(r.Text(0), r.Arg(1), r.Arg(2))),
        ["message_box_default"] = r => Nothing(() => r.Bench.MessageBoxDefault(r.Text(0))),
        ["power_wire"] = r => Nothing(() => r.Bench.PowerWire(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3) != 0)),
        ["advance_timer"] = r => Nothing(() => r.Bench.AdvanceTimer(r.Arg(0))),
        ["bug_status_message"] = r => Nothing(() => r.Bench.BugStatusMessage(r.Arg(0) != 0)),
        ["wiretap_reveal_clue"] = r => Nothing(() => r.Bench.WiretapRevealClue(r.Arg(0))),
        ["gather_intelligence"] = r => r.Bench.GatherIntelligence(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["draw_intel_popup"] = r => Nothing(r.Bench.DrawIntelPopup),
        ["build_circuit_board"] = r => Nothing(r.Bench.BuildCircuitBoard),
        ["puzzle_input_step"] = r => Nothing(r.Bench.PuzzleInputStep),
        ["load_bugs_pic"] = r => Nothing(() => r.LoadingPictures(r.Bench.LoadBugsPic)),
        ["bug_init"] = r => Nothing(() => r.LoadingPictures(r.Bench.BugInit)),

        // BUG's copies of the routines every part has (one implementation each, in Presentation/Library and Core/Shared)
        ["clamp"] = r => GameLibrary.Clamp(r.Arg(0), r.Arg(1), r.Arg(2)),
        ["random"] = r => GameLibrary.RandomBelow(r.State.Random, r.Arg(0)),
        ["DoSound"] = r => Nothing(() => r.Bench.Output.DoSound(r.Arg(0))),
        ["set_display_page"] = r => Nothing(() => r.Bench.Output.SetDisplayPage(r.Arg(0))),
        ["draw_line"] = r => Nothing(() => r.Bench.Output.DrawLine(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_string"] = r => Nothing(() => r.Bench.Output.DrawText(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["draw_string_no_bg"] = r => Nothing(() => r.Bench.Output.DrawTextFlag0(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["wait_vsync"] = r => Nothing(() => r.Bench.Output.WaitVsync(r.Arg(0))),
        ["fill_rect_corners"] = r => Nothing(() => r.Bench.Output.FillRectCorners(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_framed_box"] = r => Nothing(() => r.Bench.Output.DrawFramedBox(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_rect_outline"] = r => Nothing(() => r.Bench.Output.DrawRectOutline(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["read_key"] = r => r.Bench.Input.ReadKey(),
        ["wait_for_input"] = r => Nothing(r.Bench.Input.FlushInput),
        ["joystick_as_key"] = r => r.Bench.Input.PollJoystickKey(),
        ["joystick_direction"] = r =>
        {
            int dx = r.Input(0), dy = r.Input(1);
            var direction = r.Bench.Input.ReadJoystickDir(ref dx, ref dy);
            var (leftX, leftY) = (r.Output(0), r.Output(1));
            if (r.Arg(0) != r.Arg(1))
            {
                Assert.Equal((leftX, leftY), (dx, dy));
            }
            return direction;
        },
        ["joystick_button"] = r => r.Bench.Input.JoystickButtonDown(r.Arg(0)) ? 1 : 0,
        ["quit_prompt"] = r => r.IsMessageBuffer(0)
            ? r.Bench.Menus.QuitPrompt(r.State.MessageText, r.Arg(1), r.Arg(2))
            : r.Bench.Menus.QuitPrompt(r.Text(0), r.Arg(1), r.Arg(2)),
        ["draw_menu_box"] = r => r.Bench.Menus.DrawMenuBox(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["CheckForDisk"] = r => r.Bench.Menus.CheckForDisk(r.Arg(0), r.Arg(1)),
        ["start_animation"] = r => r.Bench.Menus.StartAnimation(r.Text(0), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["save_screen_area"] = r => Nothing(() => r.Bench.Menus.SaveScreenArea(r.Arg(0))),
        ["clear_animation_area"] = r => Nothing(r.Bench.Menus.ClearAnimationArea),
        ["refresh_animation_area"] = r => Nothing(r.Bench.Menus.RefreshAnimationArea),
        ["animation_begin"] = r => Nothing(r.Bench.Menus.AnimationBegin),
        ["animation_step"] = r => Nothing(r.Bench.Menus.AnimationStep),
        ["animation_end"] = r => Nothing(r.Bench.Menus.AnimationEnd),

        // the campaign
        ["org_presence"] = r => IntelligenceRules.OrgPresence(r.State, r.Arg(0), r.Arg(1)),
        ["org_link_plausible"] = r => IntelligenceRules.OrgLinkPlausible(r.State, r.Arg(0), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["add_link"] = r =>
        {
            var link = CampaignLibrary.AddLink(r.State.Lc, r.State.Ct, r.State.Nloc, r.Arg(0), r.Arg(1), r.Arg(2), out var count);
            r.State.Nloc = (short)count;
            return link;
        },
        ["find_or_create_person_quiet"] = r => IntelligenceRules.FindOrCreatePersonQuiet(r.State, r.Arg(0), r.Arg(1), r.Arg(2)),
        // the result is the address of the clock text, which the memory comparison covers
        ["format_game_time"] = r => Nothing(() => IntelligenceRules.FormatGameTime(r.State, r.Arg(0))),
    };

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    /// <summary>The names of the functions under test, one test case each.</summary>
    public static IEnumerable<object[]> FunctionNames => Functions.Keys.Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(FunctionNames))]
    public void TheFunctionDoesWhatTheOriginalDid(string name)
    {
        var tally = OracleRun.ForEachCase(Module, name, oracleCase =>
        {
            var run = new Run(oracleCase);
            int? result = null;
            var exited = false;
            try
            {
                result = Functions[name](run);
            }
            catch (ProgramExitException)
            {
                exited = true;
                run.World.ProgramEnded();
            }
            if (result is { } value && !exited)
            {
                Assert.True(unchecked((short)value) == oracleCase.Ax, $"the C# returns {value}, the original {oracleCase.Ax}");
            }
            run.Finish(exited);
        });
        Report(name, tally);
    }

    /// <summary>
    /// The board of every recorded <c>build_circuit_board</c> (BUG 00e8:0395): the wall pixels the original read are
    /// turned into wire cells as it does, then the rules lay out the wires, choose chips until the board is fair,
    /// reveal and fix chips and choose the carried chip, drawing from the same generator. The wires, chips, wiring,
    /// signals, terminals, cursor and the generator's state must be the original's. (The powered marks of the wire
    /// cells are set by the drawing of the current, <c>power_wire</c>, and are not compared.)
    /// </summary>
    [SkippableFact]
    public void TheCircuitRulesBuildTheBoardsOfTheOriginal()
    {
        var tally = OracleRun.ForEachCase(Module, "build_circuit_board", oracleCase =>
        {
            var state = Load(oracleCase.DgroupIn);
            var expected = Load(oracleCase.DgroupOut);
            ReadWallsFromThePixels(state, oracleCase.Calls.Where(c => c.Target == "get_pixel").ToList());
            BuildAsTheOriginalDoes(state);

            for (var column = 0; column < ElectronicsState.Columns; column++)
            {
                Assert.Equal(expected.BUGchip[column], state.BUGchip[column]);
                Assert.Equal(expected.Connect[column], state.Connect[column]);
            }
            for (var row = 0; row < ElectronicsState.PinRows; row++)
            {
                Assert.Equal(expected.Pin[row], state.Pin[row]);
            }
            Assert.Equal(expected.TerminalFlags, state.TerminalFlags);
            for (var x = 0; x < ElectronicsState.BoardWidth; x++)
            {
                Assert.Equal(expected.Board[x].Select(Unpowered), state.Board[x].Select(Unpowered));
            }
            Assert.Equal((expected.CarriedChip, expected.CursorCol, expected.CursorRow, expected.ChipTypeLimit, expected.Diff),
                (state.CarriedChip, state.CursorCol, state.CursorRow, state.ChipTypeLimit, state.Diff));
            Assert.Equal(expected.Random.State, state.Random.State);
        });
        Report("build_circuit_board (circuit rules)", tally);
    }

    /// <summary>
    /// The signals after every recorded <c>bug_status_message</c> (BUG 00e8:09cc), the evaluation of the board after
    /// each swap: <see cref="CircuitRules.PropagateSignals"/> on the board the original had must give the signals it
    /// left. (The current the evaluation draws runs the clock, but never to its last second, so it cannot evaluate the
    /// board again in between.)
    /// </summary>
    [SkippableFact]
    public void TheCircuitRulesWorkOutTheSignalsOfTheOriginal()
    {
        var tally = OracleRun.ForEachCase(Module, "bug_status_message", oracleCase =>
        {
            var state = Load(oracleCase.DgroupIn);
            var expected = Load(oracleCase.DgroupOut);
            CircuitRules.PropagateSignals(state, null);
            for (var row = 0; row < ElectronicsState.PinRows; row++)
            {
                Assert.Equal(expected.Pin[row], state.Pin[row]);
            }
        });
        Report("bug_status_message (circuit rules)", tally);
    }

    /// <summary>A wire cell without its powered mark.</summary>
    private static short Unpowered(short cell) => (short)(cell & ~(int)Wire.Powered);

    /// <summary>
    /// The first step of build_circuit_board: every cell of the board gets a wire piece in each direction in which
    /// the background picture has the wire colour two pixels from the cell's centre (colour 3 on CGA, else 10). The
    /// original asks the driver for each pixel; here the answers it got are read back, and the pixels asked for are
    /// checked.
    /// </summary>
    private static void ReadWallsFromThePixels(ElectronicsState state, List<BoundaryCall> pixels)
    {
        var wireColour = state.CGA ? 3 : 10;
        var next = 0;
        for (var x = 0; x < ElectronicsState.BoardWidth; x++)
        {
            for (var y = 0; y < ElectronicsState.BoardHeight; y++)
            {
                var cell = Wire.None;
                for (var direction = 0; direction < ElectronicsState.Directions; direction++)
                {
                    var call = pixels[next++];
                    Assert.Equal(2 * state.BUGMoveX[direction] + 8 * x + 4, call.Args[1].GetInt32());
                    Assert.Equal(2 * state.BUGMoveY[direction] + 8 * y + 4, call.Args[2].GetInt32());
                    if (unchecked((short)(call.Ret ?? 0)) == wireColour)
                    {
                        cell |= (Wire)(1 << direction);
                    }
                }
                state.Board[x][y] = (short)cell;
            }
        }
        Assert.Equal(pixels.Count, next);
    }

    /// <summary>The rest of build_circuit_board (BUG 00e8:0395), its drawing left out.</summary>
    private static void BuildAsTheOriginalDoes(ElectronicsState state)
    {
        state.ChipTypeLimit = (short)GameLibrary.Clamp(2 * state.Diff + 6, 0, ElectronicsState.ChipTypes - 1);
        CircuitRules.LayOutWires(state);
        CircuitRules.ResetSignals(state);
        do
        {
            CircuitRules.PlaceRandomChips(state);
            CircuitRules.PropagateSignals(state, null);
            // the evaluation (bug_status_message without drawing) would end the puzzle here: not a board the rules make
            var target = state.Mode == ElectronicsMode.PhoneTaps ? 6 - state.Diff / 2 : 5;
            if (state.AlarmCount != 0 || state.TimeLeft == 0 || state.PhonesOk >= target)
            {
                throw new OutsideModelException("the evaluation during the build would end the puzzle (alarm count, time or phones already set)");
            }
        }
        while (!CircuitRules.AssignTerminals(state));
        CircuitRules.RevealChips(state);
        CircuitRules.LockChips(state);
        state.CarriedChip = CircuitRules.RandomChipType(state);
        CircuitRules.ResetSignals(state);
        if (state.TickCount != 0)
        {
            throw new OutsideModelException("the clock already runs: the drawing of the current would advance it");
        }
        CircuitRules.PropagateSignals(state, null);
        state.CursorCol = 3;
        state.CursorRow = 2;
    }

    private void Report(string name, OracleTally tally)
    {
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }
}
