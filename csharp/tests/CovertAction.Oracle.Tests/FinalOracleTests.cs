using System.Buffers.Binary;
using CovertAction.Core;
using CovertAction.Core.Final;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Final;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// Every function of the menus part (FINAL.EXE) that the C# reconstructs, each against the recorded runs of the
/// original: same result in AX, same data segment and hand-off block afterwards, same calls across the boundaries in
/// the same order with the same arguments. Skipped without vectors.
/// </summary>
public sealed class FinalOracleTests(ITestOutputHelper output)
{
    private const string Module = "FINAL";

    /// <summary>
    /// What FINAL's own picture loader (draw_picture_file and the routines beneath it) leaves in its data segment, which
    /// the C# replaces by the library's whole-file decoder (D-12): the unpacker's tables and words (DS:629a to 79af), the 512-byte block read from the
    /// file (DS:a5ae file_buffer) with its read pointer (DS:96d8 file_buffer_ptr),
    /// and the row handed to the driver (DS:b080 picture_row_buffer).
    /// </summary>
    private static readonly (int Start, int End)[] PictureLoaderScratch =
        [(0x629a, 0x79b0), (0x96d8, 0x96da), (0xa5ae, 0xa7ae), (0xb080, 0xb1c0)];

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it, and the recorded outside world.</summary>
    private sealed class Run
    {
        private readonly (int Start, int End)[] _ignore;

        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            if (FinalDgroup.Layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
            {
                throw new OutsideModelException(string.Join("; ", reasons));
            }
            FinalDgroup.Layout.Load(oracleCase.DgroupIn, State);
            if (oracleCase.SharedIn is { } shared)
            {
                SharedBlockCodec.Layout.Load(shared, Shared);
            }
            // not compared: the stack, and the library routines' working memory (the animation player's too: its row
            // buffer, name pointer and what MicroProse's library remembers of the tracked draws)
            _ignore = [.. oracleCase.Ignore, .. LibraryLayout.FinalScratch, .. CampaignCopyLayout.FinalScratch, .. PictureLoaderScratch,
                .. PanEngineLayout.FinalPlaces.Scratch];
            // FINAL's symbols have no name for the thunk in slot 0438 of the driver table: it is calibrate_joystick, the
            // routine of that slot in BUG and TAC (the message box calls it when the joystick option is switched on)
            var calls = oracleCase.Calls.Select(c => c.Target switch
            {
                "sub_final_0000_0438" => c with { Target = "calibrate_joystick" },
                // the dissolve onto the screen, GAME's slot 014b (the same place behind end_image_cuts in both programs)
                "sub_final_0000_01d7" => c with { Target = "sub_game_0000_014b" },
                _ => c,
            }).ToList();
            World = new RecordingBoundaries(calls, o => o is byte[] bytes ? FinalDgroup.Layout.AddressOf(State, bytes) : FinalDgroup.Layout.AddressOf(State, o), State.Images);
            Screen = new Headquarters(State, Shared, new Boundaries(World, World, World, World, World, World), OriginalBehaviour.Faithful);
            // the picture loader's open archive (FINAL DS:4c84 catalog_handle) is the library's, which holds it itself
            Screen.Pictures.ArchiveHandle = BinaryPrimitives.ReadInt16LittleEndian(oracleCase.DgroupIn.AsSpan(0x4c84));
            // the original gives a freed image's memory back to DOS (D-100); its animation loader takes the scene
            // script's memory from DOS and gives it back (D-13: the library's loader holds the file's parts itself)
            World.WithoutCounterpart("int21.48", "int21.49");
        }

        public OracleCase Case { get; }

        public FinalState State { get; } = new();

        public SharedState Shared { get; } = new();

        public RecordingBoundaries World { get; }

        public Headquarters Screen { get; }

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>The words a pointer argument points at in the original's data segment (an array of the caller).</summary>
        public int[] Words(int index, int count) =>
            [.. Enumerable.Range(0, count).Select(i => (int)BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupIn.AsSpan((ushort)Arg(index) + 2 * i)))];

        /// <summary>The text a pointer argument points at in the original's data segment, up to its end mark.</summary>
        public string Text(int index)
        {
            var start = (ushort)Arg(index);
            var end = Array.IndexOf(Case.DgroupIn, (byte)0, start);
            return System.Text.Encoding.Latin1.GetString(Case.DgroupIn, start, end - start);
        }

        /// <summary>Compares everything the function left behind with the original.</summary>
        public void Finish(int? result)
        {
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == Case.Ax, $"the C# returns {value}, the original {Case.Ax}");
            }
            World.AssertAllCallsMade();
            CompareMemory();
        }

        /// <summary>Compares the memory the C# left with the original's at its exit, after both ended the program.</summary>
        public void FinishAtExit()
        {
            Finished = true;
            CompareMemory();
        }

        /// <summary>Whether <see cref="FinishAtExit"/> compared the case already.</summary>
        public bool Finished { get; private set; }

        private void CompareMemory()
        {
            OracleRun.AssertImage(FinalDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut, [.. _ignore, .. OracleRun.ComparePlayerWordsByHand(PanEngineLayout.FinalPlaces, State.Pan, Case.DgroupIn, Case.DgroupOut)], Case.IgnoreDeclared);
            if (Case.SharedIn is not { } before || Case.SharedOut is not { } after)
            {
                return;
            }
            if (before.AsSpan(SharedState.TablesOffset).SequenceEqual(after.AsSpan(SharedState.TablesOffset)))
            {
                OracleRun.AssertImage(SharedBlockCodec.Layout, Shared, before, after, [], unmodelledChangesFail: true);
                return;
            }
            // the original handed the campaign's tables on (write_env_state): the C#'s typed tables are written as the
            // hand-off's bytes and compared as such, the head of the block as its fields
            var actual = (byte[])before.Clone();
            SharedBlockCodec.Layout.Store(Shared, actual);
            SharedBlockCodec.StoreCampaign(Shared, actual);
            var differs = Enumerable.Range(0, actual.Length).Where(i => actual[i] != after[i]).ToList();
            Assert.True(differs.Count == 0, $"the hand-off block differs from the original's in {differs.Count} bytes, the first at +0x{differs.FirstOrDefault():x}");
        }
    }

    /// <summary>
    /// The functions and how each is called from a recorded case: the arguments from the case, and the result to
    /// compare with the original's AX (null for a function whose result is nothing or garbage; a yes or no is 1 or 0).
    /// </summary>
    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        // the rules of a new case
        ["random"] = r => CaseSetup.Random(r.State, r.Arg(0)),
        ["building_level"] = r => CaseSetup.BuildingLevel(r.State, r.Arg(0), r.Arg(1)),
        ["org_reaches_city"] = r => YesNo(CaseSetup.OrgReachesCity(r.State, r.Arg(0), r.Arg(1), r.Arg(2))),
        ["add_org_city_link"] = r =>
        {
            var link = CampaignLibrary.AddLink(r.State.Lc, r.State.Ct, r.State.Nloc, r.Arg(0), r.Arg(1), r.Arg(2), out var count);
            r.State.Nloc = (short)count;
            return link;
        },
        ["find_or_create_person"] = r => CaseSetup.FindOrCreatePerson(r.State, r.Arg(0), r.Arg(1), r.Arg(2)),
        ["free_person"] = r => Nothing(() => CampaignLibrary.ClearUnusedPerson(r.State.Ps, r.Arg(0))),
        ["place_cast_member"] = r => YesNo(CaseSetup.PlaceCastMember(r.State, r.Arg(0))),
        ["pack_mastermind_look"] = r => CaseSetup.PackMastermindLook(r.State, r.Arg(0)),
        ["SetChron"] = r => Nothing(() => CaseSetup.SetChron(r.State)),

        // the rules of the end of a case
        ["reveal_case_state"] = r => Nothing(() => CaseReview.RevealCaseState(r.State)),
        ["clear_fame_table"] = r => Nothing(() => CaseReview.ClearFameTable(r.State)),
        ["format_game_time"] = r =>
        {
            // the original returns the address of its clock text; the C# returns the text, which must be the buffer's
            var text = CaseReview.FormatGameTime(r.State, r.Arg(0), OriginalBehaviour.Faithful);
            Assert.Equal(new string(r.State.ClockText, 0, text.Length), text);
            return null;
        },
        ["clue_phrase_index"] = r => CampaignLibrary.ClueKindIndex(r.Arg(0), r.Arg(1)),

        // the texts of the boxes
        ["insert_line_breaks"] = r => Nothing(() => LibraryTexts.WrapTextBuffer(r.State.Txt, r.Arg(0), r.State.StrNewline)),

        // what the animation is told

        ["append_numbered_label"] = r => Nothing(() => FinalTexts.AppendNumberedLabel(r.State, r.Arg(0))),

        // the screens
        ["menu_choice"] = r => r.Screen.MenuChoice(r.State.Txt, r.Arg(1), r.Arg(2)),
        ["add_msg_record"] = r => Nothing(() => r.Screen.AddMsgRecord(r.Arg(0), r.Arg(1))),
        ["read_fame_file"] = r => Nothing(r.Screen.ReadFameFile),
        ["write_fame_file"] = r => Nothing(r.Screen.WriteFameFile),
        ["append_skill_rating"] = r => Nothing(() => r.Screen.AppendSkillRating(r.Words(0, FinalState.SkillCount))),
        ["draw_mastermind_face"] = r => Nothing(() => r.Screen.DrawMastermindFace(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_roster_cell"] = r => Nothing(() => r.Screen.DrawRosterCell(r.Arg(0))),
        ["redraw_person_cells"] = r => Nothing(() => r.Screen.RedrawPersonCells(r.Arg(0))),
        ["draw_held_objects"] = r => Nothing(() => r.Screen.DrawHeldObjects(r.Arg(0))),
        ["show_event_message"] = r => Nothing(() => r.Screen.ShowEventMessage(r.Arg(0))),
        ["fill_rect_corners"] = r => Nothing(() => r.Screen.FillRectCorners(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3), r.Arg(4))),
        ["draw_edit_field"] = r => Nothing(() => r.Screen.DrawEditField(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["shutdown_hook"] = r => Nothing(r.Screen.ShutdownHook),
        ["after_menu_clear_hook"] = r => Nothing(r.Screen.AfterMenuClearHook),
        ["after_hint_hook"] = r => Nothing(r.Screen.AfterHintHook),
        ["clear_text_box"] = r => Nothing(r.Screen.ClearTextBox),
        ["frame_roster_cell"] = r => Nothing(() => r.Screen.FrameRosterCell(r.Arg(0))),
        ["frame_person_cells"] = r => Nothing(() => r.Screen.FramePersonCells(r.Arg(0))),
        ["darken_person_cells"] = r => Nothing(() => r.Screen.DarkenPersonCells(r.Arg(0))),
        ["draw_jail_bars"] = r => Nothing(() => r.Screen.DrawJailBars(r.Arg(0))),
        ["draw_roster_label"] = r => Nothing(() => r.Screen.DrawRosterLabel(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["load_crime_file"] = r => Nothing(() => r.Screen.LoadCrimeFile(r.Arg(0))),
        ["load_world_file"] = r => Nothing(() => r.Screen.LoadWorldFile(r.Arg(0))),

        // the briefing
        ["briefing_faces_text"] = r => YesNo(r.Screen.BriefingFacesText()),
        ["briefing_intro_text"] = r => UntilTheProgramEnds(r, () => r.Screen.BriefingIntroText(r.Arg(0))),
        ["setup_new_case"] = r => UntilTheProgramEnds(r, r.Screen.SetupNewCase),
        ["start_case_briefing"] = r => UntilTheProgramEnds(r, () => WholeAnimationFile(r.Screen.StartCaseBriefing)),
        ["show_mastermind_faces"] = r => Nothing(r.Screen.ShowMastermindFaces),
        ["apartment_scene"] = r => UntilTheProgramEnds(r, () => WholeAnimationFile(r.Screen.ApartmentScene)),

        // the debriefing
        ["debrief_wrapup"] = r => UntilTheProgramEnds(r, () => WholeAnimationFile(r.Screen.DebriefWrapup)),
        ["announce_new_facts"] = r => Nothing(() => r.Screen.AnnounceNewFacts(r.Arg(0), r.Arg(1))),
        // the library's find_or_create_person as FINAL's add_person_knowledge calls it: the fourth word is "announce" (a long 1L fills two words)
        ["add_person_knowledge"] = r => CampaignLibrary.FindOrCreatePerson(r.State.Ps, r.State.Nperson, r.State.Pl, r.State.Random, r.Arg(0), r.Arg(1), r.Arg(2),
            r.Arg(3) != 0, r.Screen.AnnounceNewFacts),
        ["show_suspect_dossier"] = r => Nothing(() => r.Screen.Dossiers.ShowSuspectDossier(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["show_efficiency_report"] = r => r.Screen.ShowEfficiencyReport(),
        ["case_action_log"] = r => Nothing(r.Screen.CaseActionLog),

        // the creation of the agent
        // the practice menu's answer is what the original leaves in AX; the training's is not defined
        ["training_menu"] = r => r.Arg(0) != 0 ? r.Screen.TrainingMenu(r.Arg(0)) : Nothing(() => r.Screen.TrainingMenu(r.Arg(0))),
        ["choose_difficulty"] = r => r.Screen.ChooseDifficulty(),
        ["ask_code_name"] = r => Nothing(r.Screen.AskCodeName),
        ["make_practice_message"] = r => Nothing(r.Screen.MakePracticeMessage),
        ["start_practice_module"] = r => Nothing(() => r.Screen.StartPracticeModule(r.Arg(0))),
        ["edit_text_field"] = r => Nothing(() => r.Screen.EditTextField(r.Arg(0), r.Arg(1), r.Arg(2))),

        // the saved games (the library's save screens and the part's own write_save_game)
        ["ask_save_drive"] = r => r.Screen.Saves.AskSaveDrive(),
        ["select_load_file"] = r => r.Screen.Saves.SelectLoadFile(),
        ["list_save_slot"] = r => YesNo(r.Screen.Saves.ListSaveSlot(r.Arg(1) != 0 || r.Arg(2) != 0)),
        ["select_save_file"] = r => Nothing(r.Screen.Saves.SelectSaveFile),
        ["write_save_game"] = r => YesNo(r.Screen.WriteSaveGame(r.Text(0))),

        // the career
        ["retirement_screen"] = r => Nothing(r.Screen.RetirementScreen),
        ["show_career_summary"] = r => Nothing(r.Screen.ShowCareerSummary),
        ["open_catalog"] = r => YesNo(r.Screen.OpenCatalog(r.Text(0))),
        ["close_catalog"] = r => UntilTheProgramEnds(r, r.Screen.CloseCatalog),
        ["hall_of_fame_screen"] = r => YesNo(r.Screen.HallOfFameScreen(r.Arg(0))),
        ["insert_hall_of_fame"] = r => r.Screen.InsertHallOfFame(r.Arg(0)),
        ["post_case_vacation"] = r => Nothing(() => r.Screen.PostCaseVacation(r.Arg(0), r.Arg(1) != 0 || r.Arg(2) != 0 ? 1 : 0)),
    };

    /// <summary>
    /// The recorded runs of the briefing end where the briefing's animation is opened: the file is not there for the
    /// oracle, and the original ends the program (its fatal file error: the display back to text, the file's name and
    /// DOS's reason on the console, the exit), as the C# does. The run is compared as far as it went: every call up to
    /// the end, and the memory the original had at its exit.
    /// </summary>
    private static int? UntilTheProgramEnds(Run r, Action call)
    {
        try
        {
            call();
        }
        catch (ProgramExitException) when (r.Case.Exit is not null)
        {
            r.World.ProgramEnded();
            r.FinishAtExit();
        }
        return null;
    }

    /// <summary>
    /// A run that loads an animation beside a box: the library's loader takes the whole file at once (D-13), but the
    /// record holds only the parts the original read before the run ended, so the C# finds the file cut short. Every call
    /// up to the load has been compared by then; the case is set aside with that reason.
    /// </summary>
    private static void WholeAnimationFile(Action call)
    {
        try
        {
            call();
        }
        catch (InvalidDataException e)
        {
            throw new OutsideModelException($"the animation file ends where the record's reads end, and the library's loader reads it whole (D-13): {e.Message}");
        }
    }

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
        var endedWithTheProgram = 0;
        var tally = OracleRun.ForEachCase(Module, name, oracleCase =>
        {
            Run run;
            try
            {
                run = new Run(oracleCase);
            }
            catch (OutsideModelException e)
            {
                // say why the case is set aside, also when no case of the function is left
                output.WriteLine($"OUTSIDE {name} {oracleCase.Id}: {e.Message}");
                throw;
            }
            var result = Functions[name](run);
            if (run.Finished)
            {
                endedWithTheProgram++;
                return;
            }
            run.Finish(result);
        });
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree ({endedWithTheProgram} of them up to the end of the program), {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }
}
