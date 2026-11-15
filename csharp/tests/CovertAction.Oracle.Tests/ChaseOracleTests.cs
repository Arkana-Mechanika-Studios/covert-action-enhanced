using System.Buffers.Binary;
using System.Text;
using CovertAction.Core;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Presentation;
using CovertAction.Presentation.Chase;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Every function of the car chase (CHASE.EXE) that the C# reconstructs, each against the recorded runs of the
/// original: same result in AX, same data segment and hand-off block afterwards, same calls across the boundaries in
/// the same order with the same arguments. Skipped without vectors.
/// </summary>
public sealed class ChaseOracleTests(ITestOutputHelper output)
{
    private const string Module = "CHASE";

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it, and the recorded outside world.</summary>
    private sealed class Run
    {
        private readonly List<(int Start, int End)> _ignore;

        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            if (ChaseDgroup.Layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            ChaseDgroup.Layout.Load(oracleCase.DgroupIn, State);
            if (oracleCase.SharedIn is { } shared)
            {
                SharedBlockCodec.Layout.Load(shared, Shared);
                SharedBlockCodec.LoadCampaign(shared, Shared);
            }
            // not compared: the stack, and the original's working buffers that the rewrite does not keep
            _ignore = [.. oracleCase.Ignore, .. ChaseDgroup.Scratch];
            World = new RecordingBoundaries(oracleCase.Calls, o => ChaseDgroup.Layout.AddressOf(State, o), State.Images);
            Screen = new CarChase(State, Shared, new Boundaries(World, World, World, World, World, World), OriginalBehaviour.Faithful);
            // the original gives the cut images' memory back to DOS (D-402)
            World.WithoutCounterpart("int21.49");
        }

        public OracleCase Case { get; }

        public ChaseState State { get; } = new();

        public SharedState Shared { get; } = new();

        public RecordingBoundaries World { get; }

        public CarChase Screen { get; }

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>The text a pointer argument points at in the original's data segment.</summary>
        public string Text(int index)
        {
            var text = Case.DgroupIn.AsSpan((ushort)Arg(index));
            return Encoding.Latin1.GetString(text[..text.IndexOf((byte)0)]);
        }

        /// <summary>Whether a pointer argument points at the message buffer.</summary>
        public bool IsMessageBuffer(int index) => ChaseDgroup.Layout.AddressOf(State, State.MessageText.Bytes) == (ushort)Arg(index);

        /// <summary>The word at the address a pointer argument holds, before the call.</summary>
        public int Input(int index) => BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupIn.AsSpan((ushort)Arg(index)));

        /// <summary>The word the original left at the address a pointer argument holds (an output of the function), which is then not compared as memory.</summary>
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
        public void Finish(int? result, bool exited)
        {
            Assert.True(exited == Case.Exit is not null, exited ? "the C# ended the program, the original did not" : "the original ended the program, the C# did not");
            if (result is { } value && !exited)
            {
                Assert.True(unchecked((short)value) == Case.Ax, $"the C# returns {value}, the original {Case.Ax}");
            }
            World.AssertAllCallsMade();
            OracleRun.AssertImage(ChaseDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut, _ignore, Case.IgnoreDeclared);
            if (Case.SharedIn is { } before && Case.SharedOut is { } after)
            {
                OracleRun.AssertImage(SharedBlockCodec.Layout, Shared, before, after, [], unmodelledChangesFail: true);
            }
        }
    }

    /// <summary>
    /// The functions and how each is called from a recorded case: the arguments from the case, and the result to
    /// compare with the original's AX (null for a function whose result is nothing or garbage; a yes or no is 1 or 0).
    /// </summary>
    /// <summary>
    /// The working memory of CHASE's row-by-row picture reader and decoder, which no routine of the chase reads
    /// afterwards: pic_handle 2456, the LZW dictionary behind it, the decoder's pixel stack below its own stack top 3e75 and its words up to
    /// lzw_first_char 3c74; pic_buf_pos 5bec; pic_read_hook 6276 and the 512-byte read buffer 627a; the row buffer
    /// pic_line_buf 684e. Found as the bytes the recorded main and chase_dashboard change outside the model.
    /// </summary>
    private static readonly (int Start, int End)[] PictureLoaderScratch = [
        (0x2456, 0x3e75), (0x5bec, 0x5bee), (0x6276, 0x647a), (0x684e, 0x698e),
    ];

    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        // the rules
        ["set_leave_game"] = r => Nothing(() => ChaseRules.SetLeaveGame(r.Shared, r.Arg(0) != 0)),
        // the original returns the side bit itself (1 or 8); every caller only tests it for zero
        ["can_move"] = r => SameTruth(r, ChaseRules.CanMove(r.State, r.Arg(0), r.Arg(1), r.Arg(2))),
        ["sight_distance"] = r => ChaseRules.SightDistance(r.State, r.Arg(0)),
        ["init_cars"] = r => Nothing(() => ChaseRules.InitCars(r.State)),
        ["check_location_entry"] = r => YesNo(ChaseRules.CheckLocationEntry(r.State, r.Shared, r.Arg(0), r.Arg(1))),
        ["format_game_time"] = r =>
        {
            // the original returns the address of its clock text; the C# returns the text, which must be the template's
            var text = ChaseRules.FormatGameTime(r.State, r.Arg(0));
            Assert.Equal(new string(r.State.ClockText), text);
            return null;
        },
        ["import_shared_state"] = r => Nothing(() => ChaseRules.ImportSharedState(r.State, r.Shared)),
        ["map_stub"] = r => Nothing(ChaseRules.MapStub),
        ["on_tile_centre"] = r => Nothing(ChaseRules.OnTileCentre),

        // the screen
        ["plot_pixel"] = r => Nothing(() => r.Screen.PlotPixel(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["plot_map_dot"] = r => Nothing(() => r.Screen.PlotMapDot(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_car_sprite"] = r => Nothing(() => r.Screen.DrawCarSprite(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["draw_street_view"] = r => Nothing(() => r.Screen.DrawStreetView(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["generate_map"] = r => Nothing(r.Screen.GenerateMap),

        ["DoSound"] = r => Nothing(() => r.Screen.DoSound(r.Arg(0))),
        // the original takes the old heading as a byte; the callers pass a heading 0..3
        ["animate_turn"] = r => Nothing(() => r.Screen.AnimateTurn((sbyte)r.Arg(0), r.Arg(1))),
        ["draw_dashboard"] = r => Nothing(r.Screen.DrawDashboard),
        ["main"] = r => Nothing(() => r.LoadingPictures(r.Screen.Main)),
        ["seed_random"] = r => Nothing(() => r.State.RandomSeed = LibraryRandom.SeedRandom(r.State.Random, r.World)),
        ["chase_dashboard"] = r => Nothing(() => r.LoadingPictures(r.Screen.ChaseDashboard)),
        ["chase_tick"] = r => Nothing(r.Screen.ChaseTick),

        // the library routines this part shares with the others (the same methods as CODE's, on CHASE's data segment)
        ["clamp"] = r => GameLibrary.Clamp(r.Arg(0), r.Arg(1), r.Arg(2)),
        ["random_below"] = r => GameLibrary.RandomBelow(r.State.Random, r.Arg(0)),
        ["set_display_page"] = r => Nothing(() => r.Screen.Output.SetDisplayPage(r.Arg(0))),
        ["draw_line"] = r => Nothing(() => r.Screen.Output.DrawLine(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_text"] = r => Nothing(() => r.Screen.Output.DrawText(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["draw_text_flag0"] = r => Nothing(() => r.Screen.Output.DrawTextFlag0(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["wait_vsync"] = r => Nothing(() => r.Screen.Output.WaitVsync(r.Arg(0))),
        ["fill_rect_corners"] = r => Nothing(() => r.Screen.Output.FillRectCorners(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_framed_box"] = r => Nothing(() => r.Screen.Output.DrawFramedBox(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_rect_outline"] = r => Nothing(() => r.Screen.Output.DrawRectOutline(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["read_key"] = r => r.Screen.Input.ReadKey(),
        ["flush_input"] = r => Nothing(r.Screen.Input.FlushInput),
        ["poll_joystick_key"] = r => r.Screen.Input.PollJoystickKey(),
        ["read_joystick_dir"] = r =>
        {
            // two pointers to the caller's variables, which stay untouched when the joystick option is off
            int dx = r.Input(0), dy = r.Input(1);
            var direction = r.Screen.Input.ReadJoystickDir(ref dx, ref dy);
            var (leftX, leftY) = (r.Output(0), r.Output(1));
            if (r.Arg(0) != r.Arg(1))
            {
                Assert.Equal((leftX, leftY), (dx, dy));
            }
            return direction;
        },
        ["joystick_button_down"] = r =>
        {
            if ((byte)(r.Arg(0) + 4) > 31)
            {
                throw new OutsideModelException("a shift by more than 31: the C# shifts as the 8086 does, the oracle's emulator as a later processor (docs/faithfulness/DECISIONS.md)");
            }
            return YesNo(r.Screen.Input.JoystickButtonDown(r.Arg(0)));
        },
        ["mouse_reset"] = r => Nothing(r.Screen.Input.MouseReset),
        ["mouse_show_cursor"] = r => Nothing(r.Screen.Input.MouseShowCursor),
        ["mouse_hide_cursor"] = r => Nothing(r.Screen.Input.MouseHideCursor),
        ["mouse_get_status"] = r => r.Screen.Input.MouseGetStatus(),
        ["mouse_set_position"] = r => Nothing(() => r.Screen.Input.MouseSetPosition(r.Arg(0), r.Arg(1))),
        ["mouse_button_press"] = r => r.Screen.Input.MouseButtonPress(r.Arg(0)),
        ["mouse_button_release"] = r => r.Screen.Input.MouseButtonRelease(r.Arg(0)),
        // message box, disk prompt, animation hooks
        // a text in the message buffer is passed as the buffer, as the original passes its address
        ["quit_prompt"] = r => r.IsMessageBuffer(0)
            ? r.Screen.Menus.QuitPrompt(r.State.MessageText, r.Arg(1), r.Arg(2))
            : r.Screen.Menus.QuitPrompt(r.Text(0), r.Arg(1), r.Arg(2)),
        ["draw_menu_box"] = r => r.Screen.Menus.DrawMenuBox(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["CheckForDisk"] = r => r.Screen.Menus.CheckForDisk(r.Arg(0), r.Arg(1)),
        ["start_animation"] = r => YesNo(r.Screen.Menus.StartAnimation(r.Text(0), r.Arg(1), r.Arg(2))),
        ["save_screen_area"] = r => Nothing(() => r.Screen.Menus.SaveScreenArea(r.Arg(0))),
        ["clear_animation_area"] = r => Nothing(r.Screen.Menus.ClearAnimationArea),
        ["refresh_animation_area"] = r => Nothing(r.Screen.Menus.RefreshAnimationArea),
        ["draw_backdrop_face"] = r => Nothing(() => r.Screen.Menus.DrawBackdropFace(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["animation_begin"] = r => Nothing(r.Screen.Menus.AnimationBegin),
        ["animation_step"] = r => Nothing(r.Screen.Menus.AnimationStep),
        ["animation_end"] = r => Nothing(r.Screen.Menus.AnimationEnd),
        ["mouse_set_range"] = r => Nothing(() => r.Screen.Input.MouseSetRange(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3))),
    };

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    private static int? YesNo(bool answer) => answer ? 1 : 0;

    /// <summary>A yes or no compared only as such: the original's AX when the answers agree, else a value that differs from it.</summary>
    private static int? SameTruth(Run run, bool answer) => answer == (run.Case.Ax != 0) ? run.Case.Ax : run.Case.Ax + 1;

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
            run.Finish(result, exited);
        });
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }
}
