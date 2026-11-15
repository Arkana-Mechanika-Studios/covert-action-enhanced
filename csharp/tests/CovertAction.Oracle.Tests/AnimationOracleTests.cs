using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using CovertAction.Core.Animation;
using CovertAction.Core.Campaign;
using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The animation player (<see cref="LibraryAnimation"/>) against the recorded runs of both programs that carry it:
/// the menus (FINAL) and the campaign (GAME), where the same routines go by other names. Skipped without vectors.
/// </summary>
/// <remarks>
/// <para>
/// The player keeps its sprites and its program's stack in a far data segment of the program, and the animation's
/// program in memory of its own. A vector gives that memory as the bytes the original read (<c>far_in</c>) and wrote
/// (<c>far_out</c>). Vectors made before the oracle recorded the far data segment have neither for it: the original
/// then ran on what the route's first image held, which the test reads from build/oracle/states when it is there,
/// and what the original wrote to the segment cannot be compared.
/// </para>
/// <para>
/// Not here: the routines that read the animation file. The rewrite reads the file at once (D-13), so their calls
/// cannot be compared one by one; they are proven on the game's files (LibraryAnimationTests).
/// </para>
/// </remarks>
public sealed partial class AnimationOracleTests(ITestOutputHelper output)
{
    private const int ProgramBytes = 0x10000;

    /// <summary>One recorded case replayed on a program's state.</summary>
    private sealed class Run
    {
        private readonly PanEngineLayout.Places _places;
        private readonly Action<bool> _assertDataSegment;
        private readonly byte[] _farBefore = new byte[PanEngineLayout.FarDataSize];
        private readonly int _farAt = -1;
        private readonly int _programSegment;
        private readonly List<(int Start, int End)> _byHand = [];

        public Run(string module, OracleCase oracleCase)
        {
            Case = oracleCase;
            ILibraryState state;
            Func<object, int?> addressOf;
            if (module == "FINAL")
            {
                _places = PanEngineLayout.FinalPlaces;
                var final = Load(FinalDgroup.Layout, new FinalState(), oracleCase.DgroupIn);
                (state, Pan) = (final, final.Pan);
                addressOf = o => FinalDgroup.Layout.AddressOf(final, o);
                _assertDataSegment = unmodelledChangesFail => OracleRun.AssertImage(FinalDgroup.Layout, final, Case.DgroupIn, Case.DgroupOut,
                    [.. Case.Ignore, .. _places.Scratch, .. LibraryLayout.FinalScratch, .. _byHand], unmodelledChangesFail);
            }
            else
            {
                _places = PanEngineLayout.GamePlaces;
                var game = Load(GameDgroup.Layout, new CampaignState(), oracleCase.DgroupIn);
                (state, Pan) = (game, game.Pan);
                addressOf = o => GameDgroup.Layout.AddressOf(game, o);
                _assertDataSegment = unmodelledChangesFail => OracleRun.AssertImage(GameDgroup.Layout, game, Case.DgroupIn, Case.DgroupOut,
                    [.. Case.Ignore, .. _places.Scratch, .. LibraryLayout.GameScratch, .. _byHand], unmodelledChangesFail);
            }

            // the words the layout leaves to the test: the two program segments, the running request, the name pointer
            foreach (var word in new[] { _places.PanSegment, _places.ScriptSegment, _places.AnimationPtr, _places.PanParams })
            {
                _byHand.Add((word, word + 2));
            }
            var baseImage = BaseImage(module, oracleCase.Origin);

            // the loaded program, and the running one where it is another
            _programSegment = WordIn(_places.PanSegment);
            Pan.PanSegment = Memory(_programSegment * 16, ProgramBytes, baseImage?.Memory);
            var running = WordIn(_places.ScriptSegment);
            Pan.ScriptSegment = running == _programSegment ? Pan.PanSegment : Memory(running * 16, ProgramBytes, baseImage?.Memory);

            // the far data segment: where the vector's memory has it, else where the route's image has it
            int[] loads = baseImage is { } image ? [image.Load, .. oracleCase.LoadSegments.Select(l => (int)l)] : [.. oracleCase.LoadSegments.Select(l => (int)l)];
            foreach (var load in loads)
            {
                var at = (load + _places.FarSegmentFrame) * 16;
                var inVector = Touches(Case.FarIn, at) || Touches(Case.FarOut, at);
                if (inVector || (_farAt < 0 && baseImage?.Load == load))
                {
                    _farAt = at;
                    FarDataInVector |= inVector;
                }
            }
            if (_farAt >= 0)
            {
                _farBefore = Memory(_farAt, PanEngineLayout.FarDataSize, baseImage?.Memory);
            }
            PanEngineLayout.LoadFarData(_farBefore, Pan, _programSegment, Pan.PanSegment);

            // the requests: the player's own record names its file through a pointer
            Pan.PanParams.File = TextAt(WordIn(_places.PanParams));
            var runningRequest = WordIn(_places.AnimationPtr);
            Pan.AnimationPtr = runningRequest == 0 ? null : RequestAt(runningRequest);

            World = new RecordingBoundaries(oracleCase.Calls, addressOf, Pan.Images);
            // D-13, D-14: the memory the original asks of DOS for a loaded program, and gives back when an animation stops
            World.WithoutCounterpart("int21.48", "int21.49");
            var world = new Boundaries(World, World, World, World, World, World);
            var (libraryOutput, libraryInput) = (new LibraryOutput(state, world), new LibraryInput(state, world));
            Animation = new LibraryAnimation(state, Pan, world, libraryOutput, libraryInput, new LibraryPictures(world));
            // the message box of the two programs: it runs the animation beside it through the player; what else it
            // calls in its own program is not wired here
            Menus = new LibraryMenus(state, new SharedState(), world, libraryOutput, libraryInput, player: Animation,
                refreshAnimationArea: () => throw new OutsideModelException("the box redraws the program's own panel beside the animation, which is not wired here"),
                drawBackdropFace: (_, _, _) => throw new OutsideModelException("the box draws a face behind it (draw_portrait), which is not wired here"));
            BoxRequest = module == "FINAL" ? FinalBoxRequest : GameBoxRequest;
        }

        /// <summary>The library's message box with the player behind it.</summary>
        public LibraryMenus Menus { get; }

        /// <summary>Where the program keeps the box's request record (<c>animation</c>).</summary>
        public int BoxRequest { get; }

        // orig: FINAL DS:5d76 animation, GAME DS:574a (struct anim_params; Persistence.Layout.LibraryLayout has the fields the box sets)
        private const int FinalBoxRequest = 0x5d76, GameBoxRequest = 0x574a;

        public OracleCase Case { get; }

        public PanEngineState Pan { get; }

        public RecordingBoundaries World { get; }

        public LibraryAnimation Animation { get; }

        /// <summary>Whether the vector records the far data segment (what the original wrote there can then be compared).</summary>
        public bool FarDataInVector { get; }

        public int Arg(int index) => Case.Args[index];

        public int Address(int index) => (ushort)Case.Args[index];

        private int WordIn(int address) => BinaryPrimitives.ReadUInt16LittleEndian(Case.DgroupIn.AsSpan(address));

        private int WordOut(int address) => BinaryPrimitives.ReadUInt16LittleEndian(Case.DgroupOut.AsSpan(address));

        /// <summary>The text at an address of the data segment before the call; "" for a null pointer.</summary>
        public string TextAt(int address)
        {
            if (address == 0)
            {
                return "";
            }
            var end = Array.IndexOf(Case.DgroupIn, (byte)0, address);
            return Encoding.Latin1.GetString(Case.DgroupIn, address, (end < 0 ? Case.DgroupIn.Length : end) - address);
        }

        /// <summary>The request record at an address of the data segment: the player's own, or one read from there.</summary>
        public AnimationParams RequestAt(int address)
        {
            if (address == _places.PanParams)
            {
                return Pan.PanParams;
            }
            if (address + PanEngineLayout.ParamsSize > Case.DgroupIn.Length)
            {
                throw new OutsideModelException($"a request record at DS:{address:x4}, across the end of the data segment");
            }
            var request = new AnimationParams { File = TextAt(WordIn(address)) };
            PanEngineLayout.Params.Load(Case.DgroupIn.AsSpan(address, PanEngineLayout.ParamsSize), request);
            return request;
        }

        /// <summary>Bytes of the recorded session's memory: the route's first image where there is one, with what the vector says the original read over it.</summary>
        private byte[] Memory(int linear, int length, byte[]? image)
        {
            var bytes = new byte[length];
            if (image is not null && linear < image.Length)
            {
                Array.Copy(image, linear, bytes, 0, Math.Min(length, image.Length - linear));
            }
            Overlay(Case.FarIn, linear, bytes);
            return bytes;
        }

        private static void Overlay(IReadOnlyDictionary<int, byte[]> runs, int linear, byte[] bytes)
        {
            foreach (var (start, run) in runs)
            {
                for (var i = 0; i < run.Length; i++)
                {
                    var at = start + i - linear;
                    if (at >= 0 && at < bytes.Length)
                    {
                        bytes[at] = run[i];
                    }
                }
            }
        }

        private static bool Touches(IReadOnlyDictionary<int, byte[]> runs, int farAt) =>
            runs.Any(run => run.Key < farAt + PanEngineLayout.FarDataSize && run.Key + run.Value.Length > farAt);

        /// <summary>A word of the caller the function writes through a pointer argument: compared here, and left out of the data segment's comparison.</summary>
        public void AssertWordWritten(int address, int? value, string what)
        {
            var expected = value is { } written ? unchecked((ushort)written) : WordIn(address);
            Assert.True(WordOut(address) == expected, $"{what}: the C# leaves {expected:x4} at DS:{address:x4}, the original {WordOut(address):x4}");
            _byHand.Add((address, address + 2));
        }

        /// <summary>Compares everything the function left behind with the original.</summary>
        /// <param name="request">The address of the request the function was given, when it was given one.</param>
        public void Finish(int? request = null)
        {
            World.AssertAllCallsMade();
            // a routine that was given a request may have loaded a file: the original's file buffer and picture decoder
            // then leave their working memory behind, which the rewrite does not have (D-12, D-13); the modelled
            // variables are compared all the same
            _assertDataSegment(Case.IgnoreDeclared && request is null);

            // the words compared by hand: equal segments mean the same program; the running request is the one given
            if (WordOut(_places.ScriptSegment) != WordIn(_places.ScriptSegment))
            {
                Assert.True(WordOut(_places.ScriptSegment) == WordOut(_places.PanSegment) && ReferenceEquals(Pan.ScriptSegment, Pan.PanSegment),
                    "script_segment: the original made the loaded program the running one, the C# did not");
            }
            // (a routine that was given a request may have loaded an animation: the program's memory is then new)
            Assert.True(request is not null || WordOut(_places.PanSegment) == WordIn(_places.PanSegment), "pan_segment changed: the original loaded a program");
            var runningRequest = WordOut(_places.AnimationPtr);
            if (runningRequest != WordIn(_places.AnimationPtr))
            {
                Assert.True(runningRequest == (request ?? -1), $"animation_ptr: the original left DS:{runningRequest:x4}");
                Assert.True(Pan.AnimationPtr is not null && ReferenceEquals(Pan.AnimationPtr, Pan.PanParams) == (runningRequest == _places.PanParams),
                    "animation_ptr: the C# has another request running than the original");
            }

            // the far data segment: every byte the original wrote must be what the C# state holds there, and the C#
            // may have changed no byte the original left alone (where the vector records the segment at all)
            // (a sprite of a program loaded by the call runs in the segment the original's loader got: pan_segment after)
            var after = (byte[])_farBefore.Clone();
            PanEngineLayout.StoreFarData(Pan, after, WordOut(_places.PanSegment), Pan.PanSegment);
            var written = new bool[after.Length];
            foreach (var (start, run) in Case.FarOut)
            {
                for (var i = 0; i < run.Length; i++)
                {
                    var at = start + i - _farAt;
                    if (_farAt >= 0 && at >= 0 && at < after.Length)
                    {
                        written[at] = true;
                        Assert.True(after[at] == run[i], $"far data +0x{at:x4}: the C# has {after[at]:x2}, the original wrote {run[i]:x2}");
                    }
                }
            }
            if (FarDataInVector)
            {
                for (var at = 0; at < after.Length; at++)
                {
                    Assert.True(written[at] || after[at] == _farBefore[at], $"far data +0x{at:x4}: the C# changed {_farBefore[at]:x2} to {after[at]:x2}, the original wrote nothing there");
                }
            }
        }

        private static TState Load<TState>(StateLayout<TState> layout, TState state, byte[] dgroup)
        {
            if (layout.OutsideModel(dgroup) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons.Take(4)));
            }
            layout.Load(dgroup, state);
            return state;
        }
    }

    // ---- the route's first image (vectors made before the far data segment was recorded) ----

    [GeneratedRegex(@"^(?:capture|synthetic) ([a-z0-9-]+)")]
    private static partial Regex RouteOfOrigin();

    private static readonly Dictionary<string, (int Load, byte[] Memory)?> BaseImages = [];

    /// <summary>The memory of the recorded route when its module first waited for a key, with the module's load segment; null when it is not on this machine.</summary>
    private static (int Load, byte[] Memory)? BaseImage(string module, string origin)
    {
        var route = RouteOfOrigin().Match(origin);
        if (!route.Success)
        {
            return null;
        }
        var folder = Path.Combine(OracleVectors.Folder, "states", module, route.Groups[1].Value);
        lock (BaseImages)
        {
            if (!BaseImages.TryGetValue(folder, out var image))
            {
                string meta = Path.Combine(folder, "meta.json"), memory = Path.Combine(folder, "base_wait.mem");
                if (File.Exists(meta) && File.Exists(memory))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(meta));
                    image = (document.RootElement.GetProperty("load").GetInt32(), File.ReadAllBytes(memory));
                }
                BaseImages[folder] = image;
            }
            return image;
        }
    }

    // ---- the routines: FINAL's name, GAME's name, the call ----

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    private sealed record Routine(string Final, string Game, Func<Run, int?> Call, Func<Run, int?>? Request = null);

    private static readonly Routine[] Routines =
    [
        new("set_script_var", "script_set_global", r => Nothing(() => r.Animation.SetScriptVar(r.Arg(0), r.Arg(1)))),
        new("play_scene_sound", "script_play_sound", r => Nothing(() => r.Animation.PlaySceneSound(r.Arg(0)))),
        new("animation_skip_pressed", "skip_requested", r => r.Animation.AnimationSkipPressed() ? 1 : 0),
        new("play_animation", "run_pan_scene", r => Nothing(() => r.Animation.PlayAnimation(r.RequestAt(r.Address(0)))), r => r.Address(0)),
        new("start_animation", "setup_pan_scene", r => r.Animation.StartAnimation(r.RequestAt(r.Address(0))) ? 1 : 0, r => r.Address(0)),
        new("set_animation_screen", "sub_game_00f7_b515", r => r.Animation.SetAnimationScreen(r.Arg(0))),
        new("stop_animation", "release_pan_memory", r => Nothing(r.Animation.StopAnimation)),
        new("animation_step", "pan_scene_step", r => r.Animation.AnimationStep()),
        new("reset_animation_state", "reset_script_state", r => r.Animation.ResetAnimationState()),
        new("run_pan_frames", "run_script_frames", r => r.Animation.RunPanFrames(r.Arg(0))),
        new("wait_frame_tick", "frame_delay", r => Nothing(r.Animation.WaitFrameTick)),
        new("apply_pan_palette", "apply_pan_palette", r => Nothing(r.Animation.ApplyPanPalette)),
        new("sub_final_0100_af04", "sub_game_00f7_b6e2", r => Nothing(r.Animation.SubFinal0100Af04)),
        new("end_of_frame_pages", "swap_scene_buffers", r => Nothing(r.Animation.EndOfFramePages)),
        new("play_queued_sounds", "script_flush_sounds", r => Nothing(r.Animation.PlayQueuedSounds)),
        new("init_script", "script_reset", r => r.Animation.InitScript(0)),
        new("run_scene_script", "run_script", r => r.Animation.RunSceneScript()),
        new("find_free_sprite", "alloc_sprite_slot", r => r.Animation.FindFreeSprite()),
        new("clear_sprites", "clear_sprite_slots", r => Nothing(r.Animation.ClearSprites)),
        new("init_sprite", "init_sprite", r => Nothing(() => r.Animation.InitSprite(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4), r.Arg(5), r.Arg(6)))),
        new("step_sprites", "step_all_actors", r => Nothing(r.Animation.StepSprites)),
        new("draw_flagged_sprites", "draw_actors", r => Nothing(r.Animation.DrawFlaggedSprites)),
        new("draw_sprites", "draw_scene_actors", r => Nothing(r.Animation.DrawSprites)),
        new("draw_sprite", "draw_actor", r => Nothing(() => r.Animation.DrawSprite(r.Arg(0)))),
        new("clear_sprite_flags", "clear_actor_flags", r => Nothing(r.Animation.ClearSpriteFlags)),
        new("sprite_screen_pos", "actor_screen_pos", SpriteScreenPos),
        new("step_sprite", "step_actor_animation", r => Nothing(() => r.Animation.StepSprite(r.Arg(0)))),
        // the message box (GAME: popup_menu) with the player started, stepped and stopped through its hooks
        new("quit_prompt", "popup_menu", r => r.Menus.QuitPrompt(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2)), r => r.BoxRequest),
    ];

    /// <summary>sprite_screen_pos writes its answer through two pointers into the caller's variables.</summary>
    private static int? SpriteScreenPos(Run r)
    {
        int? x = null, y = null;
        if (r.Arg(0) is < 0 or >= PanEngineState.SpriteCount)
        {
            throw new OutsideModelException($"sprite {r.Arg(0)}: the original takes what lies beside its sprites for one");
        }
        r.Animation.SpriteScreenPos(r.Arg(0), ref x, ref y);
        r.AssertWordWritten(r.Address(1), x, "the sprite's left edge");
        r.AssertWordWritten(r.Address(2), y, "the sprite's top edge");
        return null;
    }

    /// <summary>The routines by FINAL's names.</summary>
    public static IEnumerable<object[]> RoutineNames => Routines.Select(routine => new object[] { routine.Final });

    [SkippableTheory]
    [MemberData(nameof(RoutineNames))]
    public void FinalsCopyDoesWhatTheOriginalDid(string name) => Replay("FINAL", Routines.Single(r => r.Final == name), name);

    [SkippableTheory]
    [MemberData(nameof(RoutineNames))]
    public void GamesCopyDoesWhatTheOriginalDid(string name)
    {
        var routine = Routines.Single(r => r.Final == name);
        Replay("GAME", routine, routine.Game);
    }

    private void Replay(string module, Routine routine, string name)
    {
        var withFarData = 0;
        var own = TwinCopies.CasesOrTwin(module, name, () => OracleRun.ForEachCase(module, name, oracleCase =>
        {
            if (oracleCase.Exit is not null)
            {
                throw new OutsideModelException("the recorded run ended the program: its file could not be opened");
            }
            var run = new Run(module, oracleCase);
            int? result;
            try
            {
                result = routine.Call(run);
            }
            catch (InvalidDataException e)
            {
                // the original gave the load up part of the way (no memory for the images, in the oracle's run) and
                // read no further; the rewrite takes the file apart at once and has too little of it then (D-13)
                throw new OutsideModelException($"the recorded run read only part of the animation file: {e.Message}");
            }
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == oracleCase.Ax, $"the C# returns {value}, the original {oracleCase.Ax}");
            }
            run.Finish(routine.Request?.Invoke(run));
            withFarData += run.FarDataInVector ? 1 : 0;
        }), output);
        if (own is not { } tally)
        {
            return;
        }
        output.WriteLine($"TALLY {module} {name}: {tally.Compared} cases agree ({withFarData} with the far data segment compared), {tally.OutsideModel.Count} outside the typed model");
        foreach (var reason in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {name} {reason}");
        }
    }
}
