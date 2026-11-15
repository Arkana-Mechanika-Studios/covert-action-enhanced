using System.Buffers.Binary;
using System.Text;
using CovertAction.Core.Crypto;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Presentation;
using CovertAction.Presentation.Crypto;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Every function of the cryptography part (CODE.EXE) that the C# reconstructs, each against the recorded runs of
/// the original: same result in AX, same data segment and hand-off block afterwards, same calls across the
/// boundaries in the same order with the same arguments. Skipped without vectors.
/// </summary>
public sealed class CodeOracleTests(ITestOutputHelper output)
{
    private const string Module = "CODE";

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it, and the recorded outside world.</summary>
    private sealed class Run
    {
        private readonly List<(int Start, int End)> _ignore;

        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            if (CodeDgroup.Layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            CodeDgroup.Layout.Load(oracleCase.DgroupIn, State);
            if (oracleCase.SharedIn is { } shared)
            {
                SharedBlockCodec.Layout.Load(shared, Shared);
            }
            // not compared: the stack, and the original's working buffers that the rewrite does not keep
            _ignore = [.. oracleCase.Ignore, .. CodeDgroup.Scratch];
            World = new RecordingBoundaries(oracleCase.Calls, o => CodeDgroup.Layout.AddressOf(State, o));
            Code = new CryptoWorkstation(State, Shared, new Boundaries(World, World, World, World, World, World));
        }

        public OracleCase Case { get; }

        public CryptoState State { get; } = new();

        public SharedState Shared { get; } = new();

        public RecordingBoundaries World { get; }

        public CryptoWorkstation Code { get; }

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>The text a pointer argument points at in the original's data segment.</summary>
        public string Text(int index)
        {
            var text = Case.DgroupIn.AsSpan((ushort)Arg(index));
            return Encoding.Latin1.GetString(text[..text.IndexOf((byte)0)]);
        }

        /// <summary>Whether a pointer argument points at the message buffer.</summary>
        public bool IsMessageBuffer(int index) => CodeDgroup.Layout.AddressOf(State, State.MessageText.Bytes) == (ushort)Arg(index);

        /// <summary>The word at the address a pointer argument holds, before the call.</summary>
        public int Input(int index) => BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupIn.AsSpan((ushort)Arg(index)));

        /// <summary>The word the original left at the address a pointer argument holds (an output of the function), which is then not compared as memory.</summary>
        public int Output(int index)
        {
            var address = (ushort)Arg(index);
            _ignore.Add((address, address + 2));
            return BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupOut.AsSpan(address));
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
            OracleRun.AssertImage(CodeDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut, _ignore, Case.IgnoreDeclared);
            if (Case.SharedIn is { } before && Case.SharedOut is { } after)
            {
                OracleRun.AssertImage(SharedBlockCodec.Layout, Shared, before, after, [], unmodelledChangesFail: true);
            }
        }
    }

    /// <summary>
    /// The functions and how each is called from a recorded case: the arguments from the case, and the result to
    /// compare with the original's AX (null for a function whose result is nothing; a yes or no is 1 or 0).
    /// </summary>
    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        // the helpers every part carries
        ["clamp"] = r => GameLibrary.Clamp(r.Arg(0), r.Arg(1), r.Arg(2)),
        ["random_below"] = r => GameLibrary.RandomBelow(r.State.Random, r.Arg(0)),

        // the rules
        ["GoodBye"] = r => Nothing(() => CryptoRules.GoodBye(r.Shared, r.Arg(0) != 0)),
        ["setup_cipher"] = r => Nothing(() => CryptoRules.SetupCipher(r.State, r.Shared)),
        ["reveal_hint_letter"] = r => Nothing(() => CryptoRules.RevealHintLetter(r.State)),
        ["reset_guesses"] = r => Nothing(() => CryptoRules.ResetGuesses(r.State)),
        ["check_solution"] = r => YesNo(CryptoRules.CheckSolution(r.State)),

        // the workstation
        ["DoCode"] = r => Nothing(r.Code.DoCode),
        ["seed_random"] = r => Nothing(() => r.State.RandomSeed = LibraryRandom.SeedRandom(r.State.Random, r.World)),
        ["draw_crypto_screen"] = r => Nothing(r.Code.DrawCryptoScreen),
        ["hint_scan_animation"] = r => Nothing(r.Code.HintScanAnimation),
        ["blink_selected_letter"] = r => Nothing(() => r.Code.BlinkSelectedLetter(r.Arg(0))),
        ["crypto_key_step"] = r => Nothing(r.Code.CryptoKeyStep),
        ["redraw_guesses"] = r => Nothing(r.Code.RedrawGuesses),
        ["draw_timer"] = r => Nothing(r.Code.DrawTimer),

        // output
        ["DoSound"] = r => Nothing(() => r.Code.Output.DoSound(r.Arg(0))),
        ["set_display_page"] = r => Nothing(() => r.Code.Output.SetDisplayPage(r.Arg(0))),
        ["draw_line"] = r => Nothing(() => r.Code.Output.DrawLine(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_text"] = r => Nothing(() => r.Code.Output.DrawText(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["draw_text_flag0"] = r => Nothing(() => r.Code.Output.DrawTextFlag0(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["wait_vsync"] = r => Nothing(() => r.Code.Output.WaitVsync(r.Arg(0))),
        ["fill_rect_corners"] = r => Nothing(() => r.Code.Output.FillRectCorners(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_framed_box"] = r => Nothing(() => r.Code.Output.DrawFramedBox(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_rect_outline"] = r => Nothing(() => r.Code.Output.DrawRectOutline(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),

        // input
        ["read_key"] = r => r.Code.Input.ReadKey(),
        ["flush_input"] = r => Nothing(r.Code.Input.FlushInput),
        ["poll_joystick_key"] = r => r.Code.Input.PollJoystickKey(),
        ["read_joystick_dir"] = r =>
        {
            // two pointers to the caller's variables, which stay untouched when the joystick option is off
            int dx = r.Input(0), dy = r.Input(1);
            var direction = r.Code.Input.ReadJoystickDir(ref dx, ref dy);
            var (leftX, leftY) = (r.Output(0), r.Output(1));
            // with both pointers the same the second store of the original wins; the C# keeps two values
            if (r.Arg(0) != r.Arg(1))
            {
                Assert.Equal((leftX, leftY), (dx, dy));
            }
            return direction;
        },
        ["joystick_button_down"] = r =>
        {
            // the function shifts by its argument plus 4, taken as a byte
            if ((byte)(r.Arg(0) + 4) > 31)
            {
                throw new OutsideModelException("a shift by more than 31: the C# shifts as the 8086 does, the oracle's emulator as a later processor (docs/faithfulness/DECISIONS.md)");
            }
            return YesNo(r.Code.Input.JoystickButtonDown(r.Arg(0)));
        },
        ["mouse_reset"] = r => Nothing(r.Code.Input.MouseReset),
        ["mouse_show_cursor"] = r => Nothing(r.Code.Input.MouseShowCursor),
        ["mouse_hide_cursor"] = r => Nothing(r.Code.Input.MouseHideCursor),
        ["mouse_get_status"] = r => r.Code.Input.MouseGetStatus(),
        ["mouse_set_position"] = r => Nothing(() => r.Code.Input.MouseSetPosition(r.Arg(0), r.Arg(1))),
        ["mouse_button_press"] = r => r.Code.Input.MouseButtonPress(r.Arg(0)),
        ["mouse_button_release"] = r => r.Code.Input.MouseButtonRelease(r.Arg(0)),
        ["mouse_set_range"] = r => Nothing(() => r.Code.Input.MouseSetRange(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3))),

        // message box, disk prompt, animation hooks
        // a text in the message buffer is passed as the buffer, as the original passes its address
        ["quit_prompt"] = r => r.IsMessageBuffer(0)
            ? r.Code.Menus.QuitPrompt(r.State.MessageText, r.Arg(1), r.Arg(2))
            : r.Code.Menus.QuitPrompt(r.Text(0), r.Arg(1), r.Arg(2)),
        ["draw_menu_box"] = r => r.Code.Menus.DrawMenuBox(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["CheckForDisk"] = r => r.Code.Menus.CheckForDisk(r.Arg(0), r.Arg(1)),
        ["start_animation"] = r => YesNo(r.Code.Menus.StartAnimation(r.Text(0), r.Arg(1), r.Arg(2))),
        ["save_screen_area"] = r => Nothing(() => r.Code.Menus.SaveScreenArea(r.Arg(0))),
        ["clear_animation_area"] = r => Nothing(r.Code.Menus.ClearAnimationArea),
        ["refresh_animation_area"] = r => Nothing(r.Code.Menus.RefreshAnimationArea),
        ["draw_backdrop_face"] = r => Nothing(() => r.Code.Menus.DrawBackdropFace(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["animation_begin"] = r => Nothing(r.Code.Menus.AnimationBegin),
        ["animation_step"] = r => Nothing(r.Code.Menus.AnimationStep),
        ["animation_end"] = r => Nothing(r.Code.Menus.AnimationEnd),
    };

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    private static int? YesNo(bool answer) => answer ? 1 : 0;

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
