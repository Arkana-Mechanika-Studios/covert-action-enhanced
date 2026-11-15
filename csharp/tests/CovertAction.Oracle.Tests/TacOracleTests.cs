using System.Buffers.Binary;
using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using CovertAction.Presentation.Raid;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Every function of the building raid (TAC.EXE) that the C# reconstructs, each against the recorded runs of the
/// original: same result in AX, same data segment and hand-off block afterwards, same calls across the boundaries in
/// the same order with the same arguments. Skipped without vectors.
/// </summary>
public sealed class TacOracleTests(ITestOutputHelper output)
{
    private const string Module = "TAC";

    /// <summary>The linear address of the BIOS keyboard flags, and their Num Lock bit.</summary>
    private const int KeyboardFlags = 0x417;
    private const int NumLockBit = 0x20;

    /// <summary>
    /// The C library's own variables that its file routines change beneath the <c>IFiles</c> boundary in TAC.EXE (the
    /// heap's pointers, the file and stream tables), as LibraryTextOracleTests has them.
    /// </summary>
    private static readonly (int Start, int End)[] FileRoutineInternals = [(0x3d50, 0x3d52), (0x3e10, 0x3e12), (0x3e84, 0x3e86), (0x8db0, 0x8dda)];

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it, and the recorded outside world.</summary>
    private sealed class Run
    {
        private readonly List<(int Start, int End)> _ignore;

        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            if (TacDgroup.Layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            TacDgroup.Layout.Load(oracleCase.DgroupIn, State);
            if (oracleCase.SharedIn is { } shared)
            {
                SharedBlockCodec.Layout.Load(shared, Shared);
                // the campaign's tables, which a part that takes the campaign over reads from the block
                SharedBlockCodec.LoadCampaign(shared, Shared);
            }
            // not compared: the stack, and the original's working buffers that the rewrite does not keep
            _ignore = [.. oracleCase.Ignore, .. TacDgroup.Scratch, .. LibraryLayout.TacScratch, .. CampaignCopyLayout.TacScratch];
            World = new RecordingBoundaries(oracleCase.Calls, o => TacDgroup.Layout.AddressOf(State, o), State.Images);
            // D-501: the video BIOS's "read the cursor" call of tac_init has no counterpart in the rewrite
            World.WithoutCounterpart("int10.03");
            // the BIOS keyboard flags (0040:0017): bit 0x20 is Num Lock
            if (oracleCase.FarIn.TryGetValue(KeyboardFlags, out var flags))
            {
                World.NumLockOn = (flags[0] & NumLockBit) != 0;
            }
            var world = new Boundaries(World, World, World, World, World, World);
            Raid = new BuildingRaid(State, Shared, world, OriginalBehaviour.Faithful);
            Menus = Raid.Menus;
        }

        /// <summary>The library's message box on this part's state.</summary>
        public LibraryMenus Menus { get; }

        public OracleCase Case { get; }

        public RaidState State { get; } = new();

        public SharedState Shared { get; } = new();

        public RecordingBoundaries World { get; }

        public BuildingRaid Raid { get; }

        /// <summary>The original's bugs as the oracle ran them.</summary>
        public OriginalBehaviour Behaviour { get; } = OriginalBehaviour.Faithful;

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>The text a pointer argument points at in the original's data segment.</summary>
        public string Text(int index)
        {
            var text = Case.DgroupIn.AsSpan((ushort)Arg(index));
            return System.Text.Encoding.Latin1.GetString(text[..text.IndexOf((byte)0)]);
        }

        /// <summary>A text argument as the raid's text buffer when it points there, else as a buffer of its own.</summary>
        public CovertAction.Core.Shared.TextBuffer TextBuffer(int index)
        {
            const int Txt = 0x8dda;
            if ((ushort)Arg(index) == Txt)
            {
                return State.MessageText;
            }
            var text = Text(index);
            var buffer = new CovertAction.Core.Shared.TextBuffer(text.Length + 1) { Text = text };
            return buffer;
        }

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
        /// Sets aside what reading a text file through the C library leaves in the data segment beneath the IFiles
        /// boundary (its stream and file tables, and the near heap from the stream's buffer on: the heap lies behind
        /// every variable, at the data segment's end), and the bytes of a text buffer behind the text's end mark,
        /// where the words read before the last one are left.
        /// </summary>
        public void ReadATextFile(int textAt, int textSize)
        {
            _ignore.AddRange(StdioInternals);
            foreach (var call in Case.Calls.Where(c => c.Target == "read"))
            {
                var buffer = (ushort)call.Args[1].GetInt64();
                _ignore.Add((buffer - 8, Case.DgroupIn.Length));
            }
            var end = Array.IndexOf(Case.DgroupOut, (byte)0, textAt);
            _ignore.Add((end + 1, textAt + textSize));
        }

        /// <summary>
        /// A password drawn through read_text_line: what reading the text file leaves (<see cref="ReadATextFile"/>), and
        /// the word chosen and its mask, compared only when the record carries the C library's notes on the file (its
        /// table of open files, written by the oracle since November 2026). Older records read the file as plain bytes,
        /// so the library's fseek stepped onto another word than in the game; RaidPasswordTests holds the word then.
        /// </summary>
        public void ReadAPasswordLine()
        {
            ReadATextFile(PasswordWordAt, PasswordWordSize);
            if (Case.Calls.Any(c => c.FileFlags is not null))
            {
                return;
            }
            NotCompared(PasswordWordAt, PasswordWordSize);
            NotCompared(PasswordMaskAt, PasswordMaskSize);
        }

        /// <summary>Leaves a range of the data segment out of the comparison; the caller says why.</summary>
        public void NotCompared(int address, int size) => _ignore.Add((address, address + size));

        /// <summary>Compares everything the function left behind with the original.</summary>
        public void Finish(int? result)
        {
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == Case.Ax, $"the C# returns {value}, the original {Case.Ax}");
            }
            World.AssertAllCallsMade();
            // the C library's file routines keep a stream record and a 512-byte buffer on the near heap (the read
            // buffer's address is the second argument of each read), and change their own tables beneath the boundary
            foreach (var call in Case.Calls.Where(c => c.Target == "read"))
            {
                var buffer = (ushort)call.Args[1].GetInt64();
                _ignore.Add((buffer - 8, buffer + 512));
            }
            if (Case.Calls.Any(c => c.Target == "open"))
            {
                _ignore.AddRange(FileRoutineInternals);
            }
            if (Case.FarOut.TryGetValue(KeyboardFlags, out var flags))
            {
                Assert.True(World.NumLockOn == ((flags[0] & NumLockBit) != 0), $"Num Lock is {(World.NumLockOn ? "on" : "off")} in the C#, the original left the flags {flags[0]:x2}");
            }
            OracleRun.AssertImage(TacDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut, _ignore, Case.IgnoreDeclared);
            if (Case.SharedIn is not null && Case.SharedOut is not null)
            {
                OracleRun.AssertHandedOverBlock(Case, Shared, State.HandOff.SharedCursor);
            }
        }
    }

    /// <summary>
    /// The picture reader's and decoder's working memory (tables, buffers, the read position): rewritten by every picture
    /// the raid loads, never read by the game afterwards. Found as the bytes the recorded tac_init changes outside the model.
    /// </summary>
    private const int PictureReaderAt = 0x3d54, PictureReaderSize = 0x2000;

    /// <summary>Where TAC keeps the password, and the room it has there.</summary>
    // orig: TAC DS:c846 password_word
    private const int PasswordWordAt = 0xc846, PasswordWordSize = 0x26;

    /// <summary>Where TAC keeps the password's mask.</summary>
    // orig: TAC DS:c98c password_mask
    private const int PasswordMaskAt = 0xc98c, PasswordMaskSize = 12;

    /// <summary>
    /// The C library's own variables that its buffered files change beneath the IFiles boundary in TAC's data: the
    /// near heap's end (DS:3c90), the error words, the table of open files, the stream records and their second table,
    /// and the variables of its scanner in front of the text buffer (the heap's blocks themselves are set aside by
    /// the address of each read).
    /// </summary>
    private static readonly (int Start, int End)[] StdioInternals = [(0x3c90, 0x3c92), (0x3cf0, 0x3d30), (0x3d40, 0x3e80), (0x8da8, 0x8dda)];

    /// <summary>
    /// The functions and how each is called from a recorded case: the arguments from the case, and the result to
    /// compare with the original's AX (null for a function whose result is nothing or garbage; a yes or no is 1 or 0).
    /// </summary>
    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        // the building
        ["set_wall_edge"] = r => Nothing(() => BuildingRules.SetWallEdge(r.State, r.Arg(0), r.Arg(1), r.Arg(2))),
        ["init_building_grid"] = r => Nothing(() => BuildingRules.InitBuildingGrid(r.State)),
        ["add_wall_segments"] = r => Nothing(() => BuildingRules.AddWallSegments(r.State)),
        ["label_rooms"] = r => YesNo(BuildingRules.LabelRooms(r.State)),
        ["connect_rooms"] = r => YesNo(BuildingRules.ConnectRooms(r.State)),
        ["prune_rooms"] = r => BuildingRules.PruneRooms(r.State),
        ["remove_room"] = r => Nothing(() => BuildingRules.RemoveRoom(r.State, r.Arg(0))),
        ["furnish_room"] = r => Nothing(() => BuildingRules.FurnishRoom(r.State, r.Arg(0))),
        ["find_door_cell"] = r =>
        {
            int x = r.Input(2), y = r.Input(3);
            var found = BuildingRules.FindDoorCell(r.State, r.Arg(0), r.Arg(1), ref x, ref y);
            Assert.Equal((r.Output(2), r.Output(3)), (x, y));
            return YesNo(found);
        },
        ["approx_distance"] = r => BuildingRules.ApproxDistance(r.Arg(0), r.Arg(1)),
        // the library's key_distance; TAC calls its copy grid_distance
        ["grid_distance"] = r => GameLibrary.KeyDistance(r.Arg(0), r.Arg(1)),

        ["generate_building"] = r => Nothing(r.Raid.GenerateBuilding),
        ["load_sprites_pic"] = r => Nothing(r.Raid.LoadSpritesPic),
        ["choose_door"] = r => Nothing(r.Raid.ChooseDoor),
        ["equipment_screen"] = r => Nothing(() => r.Raid.EquipmentScreen(r.Arg(0) != 0)),
        ["tac_init"] = r => Nothing(() =>
        {
            r.Raid.TacInit();
            // the password it draws: as for read_text_line
            r.ReadAPasswordLine();
        }),

        // combat
        ["plot_point"] = r => Nothing(() => r.Raid.PlotPoint(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["mark_guard_dead"] = r => Nothing(() => r.Raid.MarkGuardDead(r.Arg(0))),

        // sound, pictures, radar, panel
        ["DoSound"] = r => Nothing(() => r.Raid.DoSound(r.Arg(0))),
        ["set_palette"] = r => Nothing(() => r.Raid.SetPalette(r.Arg(0))),
        ["draw_sprite"] = r => Nothing(() => r.Raid.DrawSprite(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_icon"] = r => Nothing(() => r.Raid.DrawIcon(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_minimap"] = r => Nothing(() => r.Raid.DrawMinimap(r.Arg(0), r.Arg(1))),
        ["draw_status_panel"] = r => Nothing(r.Raid.DrawStatusPanel),
        ["refresh_status_panel"] = r => Nothing(r.Raid.RefreshStatusPanel),
        ["redraw_cell"] = r => Nothing(() => r.Raid.RedrawCell(r.Arg(0), r.Arg(1))),
        ["update_projectiles"] = r => Nothing(() => r.Raid.UpdateProjectiles(r.Arg(0))),
        ["exit_cleanup"] = r => Nothing(() => r.Raid.ExitCleanup(r.Arg(0) != 0)),

        // the guards' turn, grenades, the message box
        ["show_message_box"] = r => r.Raid.ShowMessageBox(r.TextBuffer(0), r.Arg(1), r.Arg(2)),
        ["fire_projectile"] = r => Nothing(() => r.Raid.FireProjectile(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["update_guard"] = r => Nothing(() => r.Raid.UpdateGuard(r.Arg(0))),
        ["plant_bug"] = r => Nothing(r.Raid.PlantBug),
        ["org_presence_in_city"] = r => RaidIntelligenceRules.OrgPresenceInCity(r.State, r.Arg(0), r.Arg(1)),
        ["org_plausible_in_city"] = r => YesNo(RaidIntelligenceRules.OrgPlausibleInCity(r.State, r.Arg(0), r.Arg(1), r.Arg(2))),
        ["tac_end_message"] = r => Nothing(r.Raid.TacEndMessage),
        ["reveal_password_letter"] = r => Nothing(r.Raid.RevealPasswordLetter),
        // The file calls, the random draw and everything else are compared; the word chosen and its mask too when the
        // record carries the C library's notes on the file (ReadAPasswordLine). In older records the oracle answered
        // open and read without them, so the library's fseek stepped as in a file of plain bytes (onto another word
        // than in the game); RaidPasswordTests holds the word against the library's instructions.
        ["read_text_line"] = r => Nothing(() =>
        {
            r.Raid.ReadTextLine();
            r.ReadAPasswordLine();
        }),
        ["draw_input_field"] = r => Nothing(() => r.Raid.DrawInputField(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["input_line"] = r => r.Raid.InputLine(r.Arg(0), r.Arg(1), r.Arg(2)),

        // the screen and the keys
        ["save_screen"] = r => Nothing(r.Raid.SaveScreen),
        ["restore_full_screen"] = r => Nothing(r.Raid.RestoreFullScreen),
        // TAC's restore_screen is the library's save_screen_area under another name
        ["restore_screen"] = r => Nothing(() => r.Menus.SaveScreenArea(r.Arg(0))),
        // the library's message box on TAC's copies: draw_menu_lines is draw_menu_box under another name, and TAC's
        // quit_prompt is the library's with TAC's own DoSound and read_key behind it
        ["draw_menu_lines"] = r => r.Menus.DrawMenuBox(r.Text(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["quit_prompt"] = r => r.Menus.QuitPrompt(r.Text(0), r.Arg(1), r.Arg(2)),
        ["CheckForDisk"] = r => r.Menus.CheckForDisk(r.Arg(0), r.Arg(1)),
        ["start_animation"] = r => r.Menus.StartAnimation(r.Text(0), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["show_status_line"] = r => Nothing(() => r.Raid.ShowStatusLine(r.Text(0))),
        ["draw_status_clock"] = r => Nothing(r.Raid.DrawStatusClock),
        ["draw_film_counter"] = r => Nothing(r.Raid.DrawFilmCounter),
        ["read_key"] = r => r.Raid.ReadKey(),
        // TAC's wait_key is the library's wait_for_key, with TAC's own read_key behind it
        ["wait_key"] = r => r.Raid.Screens.WaitForKey(),
        ["draw_speech_box"] = r => Nothing(r.Raid.DrawSpeechBox),
        ["gather_intelligence"] = r => r.Raid.GatherIntelligence(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["evidence_found_text"] = r => Nothing(r.Raid.EvidenceFoundText),
        ["arrest_prompt"] = r => Nothing(r.Raid.ArrestPrompt),
        ["take_photo"] = r => Nothing(r.Raid.TakePhoto),
        ["draw_input_box"] = r => Nothing(() => r.Raid.DrawInputBox(r.Text(0))),
        ["file_room_search"] = r => Nothing(r.Raid.FileRoomSearch),
        ["draw_room_view"] = r => Nothing(() => r.Raid.DrawRoomView(r.Arg(0))),
        ["aim_and_fire"] = r => Nothing(() => r.Raid.AimAndFire(r.Arg(0), r.Arg(1))),
        ["draw_actors"] = r => Nothing(r.Raid.DrawActors),
        ["update_player"] = r => Nothing(r.Raid.UpdatePlayer),
        ["frame_step"] = r => Nothing(r.Raid.FrameStep),
        ["use_object_in_front"] = r => Nothing(r.Raid.UseObjectInFront),
        // a password typed draws a new one through read_text_line: compared as in its own test (ReadAPasswordLine)
        ["password_prompt"] = r => Nothing(() =>
        {
            r.Raid.PasswordPrompt();
            r.ReadAPasswordLine();
        }),

        // the guards
        ["room_cost"] = r => GuardRules.RoomCost(r.State, r.Arg(0), r.Arg(1)),
        ["choose_next_room"] = r => GuardRules.ChooseNextRoom(r.State, r.Arg(0), r.Arg(1)),
        ["raise_alert"] = r => Nothing(() => GuardRules.RaiseAlert(r.State, r.Arg(0), r.Arg(1))),
        ["build_path_map"] = r => YesNo(GuardRules.BuildPathMap(r.State, r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4), r.Arg(5))),
        ["spawn_guard"] = r => Nothing(() => GuardRules.SpawnGuard(r.State, r.Arg(0))),
        ["guard_sees_player"] = r => GuardRules.GuardSeesPlayer(r.State, r.Arg(0)),
        ["line_of_sight_to_player"] = r => GuardRules.LineOfSightToPlayer(r.State, r.Arg(0)),
        ["line_blocked_count"] = r => GuardRules.LineBlockedCount(r.State, r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["guard_behaviour"] = r => GuardRules.GuardBehaviour(r.State, r.Arg(0)),
        ["direction_from_vector_coarse"] = r => GuardRules.DirectionFromVectorCoarse(r.Arg(0), r.Arg(1)),
        ["direction_from_vector"] = r => GuardRules.DirectionFromVector(r.Arg(0), r.Arg(1)),
        ["find_front_cell"] = r => Nothing(() => GuardRules.FindFrontCell(r.State)),

        // the raid
        // the result is the address of the clock text, which the memory comparison covers
        ["time_string"] = r => Nothing(() => LibraryTexts.FormatClockTime(r.State.ClockText, r.State.Gtime, r.Arg(0))),
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
            var result = Functions[name](run);
            run.Finish(result);
        });
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }
}
