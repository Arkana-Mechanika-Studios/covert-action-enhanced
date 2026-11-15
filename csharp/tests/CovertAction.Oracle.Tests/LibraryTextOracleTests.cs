using System.Buffers.Binary;
using System.Text;
using CovertAction.Core;
using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The library's text, name and campaign routines (package L6) against the recorded runs of every program that has
/// them: the menus (FINAL), the campaign (GAME), the raid (TAC) and the electronics (BUG). One C# method stands for
/// the copies of one group of docs/source-map/shared-functions.csv; each copy is replayed here on its own program's
/// memory. The test builds, for each case, a small layout of the variables these routines use at the program's
/// addresses (<see cref="LibraryPart"/>), so every byte the original changed outside them still fails the test.
/// Skipped without vectors.
/// </summary>
public sealed class LibraryTextOracleTests(ITestOutputHelper output)
{
    /// <summary>The functions and how each is called from a recorded case; the result is compared with AX (null: nothing to compare).</summary>
    private static readonly Dictionary<string, Func<LibraryRun, int?>> Functions = new()
    {
        // ---- reading the text files ----
        ["read_cleared_text"] = r =>
        {
            var count = r.Arg(4);
            return YesNo(r.Text.ReadClearedText(r.TextAt(r.Address(0)), r.TextAt(r.Address(1)), r.TextTable(r.Address(2), count), r.TextTable(r.Address(3), count), count));
        },
        // C: the original's function ends without a return statement: AX is what read_cleared_text left there
        ["read_text_with_vars"] = r => YesNo(r.Text.ReadTextWithVars(r.TextAt(r.Address(0)), r.TextAt(r.Address(1)))),
        ["read_random_variant"] = r =>
        {
            var tag = r.BufferByHand(r.Address(1));
            r.Text.ReadRandomVariant(r.TextAt(r.Address(0)), tag, r.Arg(2));
            r.CompareByHand(r.Address(1), tag);
            return null;
        },
        ["replace_tokens"] = r =>
        {
            // the line is a buffer of its caller (a line on the stack, or the text buffer): replayed and compared by hand
            var count = r.Arg(3);
            var line = r.Address(0) == r.At.Txt ? r.Part.MessageText : r.BufferByHand(r.Address(0), ILibraryTextState.LineCapacity);
            LibraryTexts.ReplaceTokens(line, r.TextTable(r.Address(1), count), r.TextTable(r.Address(2), count), count, r.Part.TokenTail);
            if (line != r.Part.MessageText)
            {
                r.CompareByHand(r.Address(0), line);
            }
            return null;
        },
        ["load_message_text"] = r => Nothing(() => LibraryTexts.LoadMessageText(r.Part.MessageTag, r.Part.MessageTemplate, r.Arg(0), r.Arg(1))),
        ["wrap_text_buffer"] = r => Nothing(() => LibraryTexts.WrapTextBuffer(r.Part.MessageText, r.Arg(0), r.Part.LineEnd)),

        // ---- measuring and drawing ----
        ["text_width"] = r => r.Text.TextWidth(r.TextAt(r.Address(0))),
        ["draw_text_centered"] = r => Nothing(() => r.Text.DrawTextCentered(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["draw_wrapped_text"] = r => r.Text.DrawWrappedText(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3)),

        // ---- names and the campaign's tables ----
        ["append_person_name"] = r => Nothing(() => CampaignLibrary.AppendPersonName(r.Part.MessageText, r.Part.Ps, r.Part.Orgs, r.Part.Names, r.Arg(0))),
        ["append_real_name"] = r => Nothing(() => CampaignLibrary.AppendRealName(r.Part.MessageText, r.Part.Ps, r.Part.Orgs, r.Part.Names, r.Arg(0))),
        // building_level is another function with tests of its own: here it answers what it answered the original,
        // which append_building_type returns
        ["append_building_type"] = r => CampaignLibrary.AppendBuildingType(r.Part.MessageText, r.Part.BuildingKinds, (_, _) => r.Case.Ax, r.Arg(0), r.Arg(1)),
        ["find_or_create_person"] = r => CampaignLibrary.FindOrCreatePerson(r.Part.Ps, r.Part.Nperson, r.Part.Pl, r.Part.Random, r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3) != 0,
            (person, before) => r.Dossiers.RevealPersonFacts(person, before,
                (_, _, _) => throw new OutsideModelException("the part's message box needs the menu's variables, which this test does not load"))),
        ["clue_kind_index"] = r => CampaignLibrary.ClueKindIndex(r.Arg(0), r.Arg(1)),
        ["delete_clue"] = r => Nothing(() => CampaignLibrary.DeleteClue(r.Part.Cl, r.Arg(0))),
        ["clear_unused_person"] = r => Nothing(() => CampaignLibrary.ClearUnusedPerson(r.Part.Ps, r.Arg(0))),
        ["remove_link"] = r => Nothing(() => CampaignLibrary.RemoveLink(r.Part.Lc, r.Part.Nloc, r.Part.Ct, r.Arg(0), r.Arg(1))),

        // ---- the screens ----
        ["draw_face"] = r => Nothing(() => r.Screens.DrawFace(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_portrait"] = r => Nothing(() => r.Screens.DrawPortrait(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_screen_frame"] = r => Nothing(() => r.Screens.DrawScreenFrame(r.TextAt(r.Address(0)), r.Arg(1))),
        ["draw_title_bar"] = r => Nothing(() => r.Screens.DrawTitleBar(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["draw_panel"] = r => Nothing(() => r.Screens.DrawPanel(r.Arg(0), r.Arg(1))),
        ["draw_film_border"] = r => Nothing(r.Screens.DrawFilmBorder),
        ["draw_shape_list"] = r =>
        {
            var shapes = r.ShapesAt(r.Address(0));
            r.Screens.DrawShapeList(shapes);
            r.CompareShapes(r.Address(0), shapes);
            return null;
        },
        ["wait_for_key"] = r => r.Screens.WaitForKey(),

        // ---- the dossiers ----
        ["add_link"] = r =>
        {
            var link = CampaignLibrary.AddLink(r.Part.Lc, r.Part.Ct, r.Part.Nloc, r.Arg(0), r.Arg(1), r.Arg(2), out var count);
            r.Part.Nloc = (short)count;
            return link;
        },
        ["append_msg_log_entry"] = r => Nothing(() => CampaignLibrary.AppendMsgLogEntry(r.Part.MessageText, r.Part, r.Part.Names, r.Part.Dossier.Words, r.Part.Dossier.NumberText, r.Arg(0))),
        ["keep_marked_words"] = r => Nothing(() => LibraryTexts.KeepMarkedWords(r.Part.MessageText, r.Arg(0), r.Arg(1), r.Part.Dossier.Ellipsis)),
        ["load_event_text"] = r => Nothing(() => r.Dossiers.LoadEventText(r.Arg(0))),
        ["show_personnel_file"] = r => Nothing(() => r.Dossiers.ShowPersonnelFile(r.Arg(0), r.Arg(1) != 0)),
        ["show_suspect_dossier"] = r => Nothing(() => r.Dossiers.ShowSuspectDossier(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["append_key_text"] = r => Nothing(() => CampaignLibrary.AppendKeyText(r.Part.MessageText, r.Part.Dossier.ClueLabels, r.Part.Dossier.NumberText, r.Part.Dossier.Hundreds, r.Part.Dossier.Thousands, r.Arg(0))),
        ["expand_clue_text"] = r => Nothing(() => r.Dossiers.ExpandClueText(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["activity_clue_text"] = r => Nothing(() => CampaignLibrary.ActivityClueText(r.Part.Cl, r.Arg(0), r.Dossiers.ExpandClueText)),
        ["clue_target_code"] = r => CampaignLibrary.ClueTargetCode(r.Part.MessageText, r.Part, r.Arg(0), r.Arg(1)),
        ["lookup_clue_text"] = r => r.Dossiers.LookupClueText(r.Arg(0), r.Arg(1) != 0),
        ["append_subject_text"] = r => Nothing(() => r.Dossiers.AppendSubjectText(r.Arg(0), r.Arg(1))),
        ["reveal_cast_clue"] = r => YesNo(r.Dossiers.RevealCastClue(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["reveal_person_facts"] = r => Nothing(() => r.Dossiers.RevealPersonFacts(r.Arg(0), r.Arg(1),
            (_, _, _) => throw new OutsideModelException("the part's message box needs the menu's variables, which this test does not load"))),
        ["show_clue_report"] = r => Nothing(() => r.Dossiers.ShowClueReport(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        ["describe_activity"] = r => Nothing(() => CampaignLibrary.AppendActivityText(r.Part.MessageText, r.Part.Cl, r.Part.Dossier.ActivityPhrases, r.Part.Dossier.ActivityPeriod, r.Arg(0),
            key => CampaignLibrary.AppendKeyText(r.Part.MessageText, r.Part.Dossier.ClueLabels, r.Part.Dossier.NumberText, r.Part.Dossier.Hundreds, r.Part.Dossier.Thousands, key),
            r.Dossiers.AppendSubjectText)),
    };

    /// <summary>Each program's name of a routine where it is not the common one (docs/source-map/twin-names.csv).</summary>
    private static readonly Dictionary<(string Module, string Name), string> TwinNames = new()
    {
        [("GAME", "read_cleared_text")] = "read_text_entry",
        [("TAC", "read_text_with_vars")] = "read_text_entry",
        [("FINAL", "read_text_with_vars")] = "read_text_tag",
        [("FINAL", "read_random_variant")] = "read_random_text_tag",
        [("FINAL", "replace_tokens")] = "substitute_macros",
        [("GAME", "replace_tokens")] = "replace_substrings",
        [("FINAL", "wrap_text_buffer")] = "insert_line_breaks",
        [("TAC", "wrap_text_buffer")] = "wrap_text_buf",
        [("FINAL", "find_or_create_person")] = "add_person_knowledge",
        [("FINAL", "clue_kind_index")] = "clue_phrase_index",
        [("GAME", "clue_kind_index")] = "activity_phrase_index",
        [("GAME", "delete_clue")] = "remove_activity_entry",
        [("TAC", "delete_clue")] = "remove_activity_entry",
        [("FINAL", "clear_unused_person")] = "free_person",
        [("FINAL", "append_real_name")] = "append_full_name",
        [("TAC", "append_real_name")] = "append_full_name",
        [("FINAL", "draw_face")] = "draw_portrait",
        [("FINAL", "draw_portrait")] = "draw_photo",
        [("TAC", "draw_portrait")] = "draw_photo_box",
        [("GAME", "draw_screen_frame")] = "draw_titled_screen",
        [("TAC", "draw_screen_frame")] = "draw_titled_frame",
        [("GAME", "draw_title_bar")] = "draw_title_plate",
        [("FINAL", "draw_panel")] = "draw_report_card",
        [("GAME", "wait_for_key")] = "wait_for_input",
        [("TAC", "wait_for_key")] = "wait_key",
        [("FINAL", "add_link")] = "add_org_city_link",
        [("GAME", "add_link")] = "find_or_add_link",
        [("FINAL", "keep_marked_words")] = "filter_words_by_mask",
        [("GAME", "keep_marked_words")] = "filter_words_by_mask",
        [("TAC", "keep_marked_words")] = "elide_unknown_words",
        [("FINAL", "load_event_text")] = "show_event_message",
        [("TAC", "load_event_text")] = "build_event_text",
        [("FINAL", "append_key_text")] = "append_numbered_label",
        [("GAME", "append_key_text")] = "append_clue_label",
        [("TAC", "append_key_text")] = "append_place_text",
        [("FINAL", "expand_clue_text")] = "format_clue_text",
        [("TAC", "expand_clue_text")] = "build_clue_sentence",
        [("FINAL", "activity_clue_text")] = "append_clue_text",
        [("TAC", "clue_target_code")] = "encode_clue",
        [("FINAL", "reveal_person_facts")] = "announce_new_facts",
        [("TAC", "reveal_person_facts")] = "set_person_known",
        [("FINAL", "append_subject_text")] = "append_clue_target",
        [("TAC", "reveal_cast_clue")] = "reveal_fact",
        [("FINAL", "describe_activity")] = "append_clue_sentence",
        [("TAC", "describe_activity")] = "append_activity_text",
    };

    /// <summary>The copies that are other code under the same name, or that a program does not have.</summary>
    private static readonly HashSet<(string Module, string Name)> NotThisRoutine =
    [
        ("GAME", "append_person_name"),     // GAME 00f7:08ee's group differs: CampaignTexts
        ("GAME", "append_real_name"),       // GAME's append_full_name is other code: CampaignTexts
        ("GAME", "find_or_create_person"),  // GAME 00f7:4899 is other code
        ("GAME", "clear_unused_person"),    // GAME has none
        ("FINAL", "draw_film_border"), ("TAC", "draw_film_border"),     // GAME and BUG only
        ("FINAL", "draw_shape_list"), ("TAC", "draw_shape_list"),
        ("GAME", "append_msg_log_entry"),   // GAME's append_message_text is other code
        ("GAME", "show_personnel_file"),    // GAME 00f7:0978 is other code
        ("GAME", "show_suspect_dossier"), ("GAME", "load_event_text"),  // GAME has none
        ("GAME", "expand_clue_text"),       // GAME has none
        ("GAME", "activity_clue_text"),     // GAME's calls its own append_clue_text, other code
        ("GAME", "clue_target_code"), ("GAME", "lookup_clue_text"), ("GAME", "append_subject_text"), ("GAME", "show_clue_report"),
        ("GAME", "reveal_cast_clue"), ("GAME", "reveal_person_facts"),       // GAME's add_random_clue calls GAME's own clue report
        ("GAME", "describe_activity"),      // proven with GAME's own hooks in LibraryModuleOracleTests
    ];

    private static readonly string[] Modules = ["FINAL", "GAME", "TAC", "BUG"];

    /// <summary>One test case per routine and program.</summary>
    public static IEnumerable<object[]> Copies =>
        from name in Functions.Keys
        from module in Modules
        where !NotThisRoutine.Contains((module, name))
        select new object[] { module, name };

    [SkippableTheory]
    [MemberData(nameof(Copies))]
    public void TheCopyDoesWhatTheOriginalDid(string module, string name)
    {
        var label = TwinNames.GetValueOrDefault((module, name), name);
        var own = TwinCopies.CasesOrTwin(module, label, () => OracleRun.ForEachCase(module, label, oracleCase =>
        {
            var run = new LibraryRun(module, oracleCase);
            var result = Functions[name](run);
            run.Finish(result);
        }), output);
        if (own is not { } tally)
        {
            return;
        }
        output.WriteLine($"TALLY {module} {label}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {module} {label} {line}");
        }
    }

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    private static int? YesNo(bool answer) => answer ? 1 : 0;
}

/// <summary>
/// Where a program keeps the variables and texts of the library's L6 routines in its data segment (from the
/// instructions of each copy and symbols/names.txt). 0: the program has no such variable for these routines.
/// </summary>
internal sealed record PartAddresses(
    int Txt, int TxtLength, int Rp1, int Status, int Holdrand,
    int TokenTail, int VariableNames, int VariableValues, int ClearanceNotice, int LineSeparator, int LineEnd,
    int MessageTag, int MessageTemplate, int[] BuildingKinds, int AgentPlaceholder, int NameSpace, int FirstNames, int Surnames,
    int Ps, int Orgs, int Ct, int Lc, int Cl, int Pl, int Nperson, int Nloc,
    int GMode, int Cga, int Picface, int SaveWidth, int Sprite, int ColorMapA, int ColorMapB, int PhotoColorMap)
{
    /// <summary>The images of a part's table that the face, photo and title bar routines use (up to the clothes, 0x73).</summary>
    public const int SpriteCount = 0x78;

    public static readonly IReadOnlyDictionary<string, PartAddresses> ByModule = new Dictionary<string, PartAddresses>
    {
        ["FINAL"] = new(0x79e2, 1024, 0x4b58, 0x8090, 0x5a1e,
            0xac6c, 0x3eca, 0x3ed8, 0x3f00, 0x3ef7, 0x3fdc,
            0x5d3c, 0x3fbd, [0x3fe4, 0x3fed, 0x3ff4, 0x3fff, 0x400b], 0x4013, 0x4523, 0x4526, 0x4626,
            0x0188, 0xa7be, 0x8294, 0x0588, 0x1188, 0x7df2, 0x84b6, 0x96d0,
            0xae2c, 0x8092, 0x8bb8, 0x47c0, 0x9e9c, 0x1592, 0x15a2, 0x1612),
        ["GAME"] = new(0x7202, 1024, 0x436e, 0x789a, 0x5234,
            0xa45e, 0x3a10, 0x3a1e, 0x3a46, 0x3a3d, 0x16e0,
            0x5554, 0x3b03, [0x16e8, 0x16f1, 0x16f8, 0x1703, 0x170f], 0, 0, 0, 0,
            0x0094, 0x9fb4, 0x7a9e, 0x0494, 0x1094, 0x760a, 0x7cc0, 0x8ece,
            0xa61e, 0x789c, 0x83c2, 0x42fe, 0x969c, 0x149e, 0x14ae, 0x151e),
        ["TAC"] = new(0x8dda, 1024, 0x37b0, 0x9b68, 0x4026,
            0xc9ea, 0x2e10, 0x2e1e, 0x2e46, 0x2e3d, 0x2f22,
            0x72d4, 0x2f03, [0x2f2a, 0x2f33, 0x2f3a, 0x2f45, 0x2f51], 0x2f59, 0x3469, 0x346c, 0x356c,
            0x06fe, 0xc604, 0x9e18, 0x0afe, 0x16fe, 0x98d8, 0xa1e2, 0xb400,
            0xcbae, 0x9b6c, 0xa8e6, 0x369e, 0xbb8c, 0x1b08, 0x1b18, 0x1b88),
        ["BUG"] = new(0x5542, 512, 0x2f08, 0x5c26, 0x377c,
            0x8278, 0x230e, 0x231c, 0x2344, 0x233b, 0x2420,
            0x3a2e, 0x2401, [0x2428, 0x2431, 0x2438, 0x2443, 0x244f], 0x2457, 0x2967, 0x296a, 0x2a6a,
            0x0172, 0x7fc0, 0x5e32, 0x0572, 0x1172, 0x5992, 0x6054, 0x7268,
            0x8438, 0x5c2c, 0x6758, 0x2df6, 0x764c, 0x157c, 0x158c, 0x15fc),
    };
}

/// <summary>
/// The variables of the L6 routines as one program has them, in the typed form: a test's stand-in for a part's state
/// (a part's own state implements the same interfaces).
/// </summary>
internal sealed class LibraryPart(int txtLength) : ILibraryState, ILibraryTextState, ILibraryScreenState, ICampaignTables
{
    public RasterPort Rp1 { get; } = new();
    public RasterPort RpBack { get; } = new();
    public Options Status { get; set; }
    public short Quit { get; set; }
    public byte[] ColorMapA { get; } = new byte[16];
    public byte[] ColorMapB { get; } = new byte[16];
    public TextBuffer MessageText { get; } = new(txtLength);
    public MenuState Menu { get; } = new();
    public AnimationState Animation { get; } = new();
    public DiskPromptState Disk { get; } = new();
    public InputState Input { get; } = new();

    public TextBuffer TokenTail { get; } = new(ILibraryTextState.LineCapacity);
    public string[] VariableNameTexts { get; } = new string[ILibraryTextState.MessageVariables];
    public string[] VariableValueTexts { get; } = new string[ILibraryTextState.MessageVariables];
    public IReadOnlyList<string> VariableNames => VariableNameTexts;
    public IList<string> VariableValues => VariableValueTexts;
    public string ClearanceNotice { get; set; } = "";
    public string LineSeparator { get; set; } = "";
    public string LineEnd { get; set; } = "";
    public TextBuffer MessageTag => Dossier.MessageTag;
    public DossierTexts Dossier { get; } = new();
    public string MessageTemplate { get; set; } = "";
    public string[] BuildingKinds { get; } = new string[5];
    public PersonNameTexts Names => Dossier.Names;

    public Person[] Ps { get; } = [.. Enumerable.Range(0, CampaignTables.PersonCount).Select(_ => new Person())];
    public Org[] Orgs { get; } = [.. Enumerable.Range(0, CampaignTables.WorldSize).Select(_ => new Org())];
    public City[] Ct { get; } = [.. Enumerable.Range(0, CampaignTables.WorldSize).Select(_ => new City())];
    public Location[] Lc { get; } = [.. Enumerable.Range(0, CampaignTables.LocationCount).Select(_ => new Location())];
    public Clue[] Cl { get; } = [.. Enumerable.Range(0, CampaignTables.ClueCount).Select(_ => new Clue())];
    public Plot Pl { get; } = new();
    public short Nperson { get; set; }
    public short Nloc { get; set; }
    public MscRandom Random { get; } = new();

    public sbyte GMode { get; set; }
    public bool CGA { get; set; }
    public short Picface { get; set; }
    public byte[] PhotoColorMap { get; } = new byte[16];
    public Image?[] SpriteTable { get; } = new Image?[PartAddresses.SpriteCount];
    public IReadOnlyList<Image?> Sprite => SpriteTable;
    public ImageRegister Images { get; } = new();

    public Synopsis[] Sn { get; } = [.. Enumerable.Range(0, CampaignTables.SynopsisCount).Select(_ => new Synopsis())];
    public Synopsis MessageBeforeFirst { get; } = new();
    public Event[] Ev { get; } = [.. Enumerable.Range(0, 64).Select(_ => new Event())];
    public Activity[] Ac { get; } = [.. Enumerable.Range(0, 32).Select(_ => new Activity())];
    public short NextSyn { get; set; }
    public short[] ActiveCounts { get; } = new short[16];
    public short[] OrgCounts { get; } = new short[16];
    IList<short> ICampaignTables.Active => ActiveCounts;
    IList<short> ICampaignTables.OrgCounters => OrgCounts;
    public short NACTIVITY { get; set; }
    public short Diff { get; set; }
    public short TheCrime { get; set; }
    public short InterPol { get; set; }
    IReadOnlyList<Person> ICampaignTables.Ps => Ps;
    IReadOnlyList<Org> ICampaignTables.Orgs => Orgs;
    IReadOnlyList<City> ICampaignTables.Ct => Ct;
    IReadOnlyList<Synopsis> ICampaignTables.Sn => Sn;
    IReadOnlyList<Clue> ICampaignTables.Cl => Cl;
    IReadOnlyList<Event> ICampaignTables.Ev => Ev;
    IReadOnlyList<Activity> ICampaignTables.Ac => Ac;
}

/// <summary>One recorded case of an L6 routine set up for replay on its program's addresses.</summary>
internal sealed class LibraryRun
{
    private readonly StateLayout<LibraryPart> _layout;
    private readonly List<(int Start, int End)> _ignore;

    public LibraryRun(string module, OracleCase oracleCase)
    {
        Case = oracleCase;
        _module = module;
        At = PartAddresses.ByModule[module];
        Part = new LibraryPart(At.TxtLength);
        _layout = BuildLayout(At, oracleCase.DgroupIn);
        AddCampaign(_layout, CampaignAddresses.ByModule[module], oracleCase.DgroupIn);
        if (_layout.OutsideModel(oracleCase.DgroupIn) is { Count: > 0 } reasons)
        {
            throw new OutsideModelException(string.Join("; ", reasons));
        }
        _layout.Load(oracleCase.DgroupIn, Part);
        LoadTexts();
        _ignore = [.. oracleCase.Ignore];
        World = new RecordingBoundaries(oracleCase.Calls, o => o is byte[] bytes ? _layout.AddressOf(Part, bytes) : _layout.AddressOf(Part, o), Part.Images);
        var boundaries = new Boundaries(World, World, World, World, World, World);
        Output = new LibraryOutput(Part, boundaries);
        Input = new LibraryInput(Part, boundaries);
        Text = new LibraryText(Part, Part, boundaries, Output, Part.Random, OriginalBehaviour.Faithful);
        Screens = new LibraryScreens(Part, Part, boundaries, Output, Input, Text);
        Dossiers = new LibraryDossiers(Part, Part, Part, Part.Dossier, Output, Text, Screens, Part, Part.Random);
    }

    public OracleCase Case { get; }

    public PartAddresses At { get; }

    public LibraryPart Part { get; }

    public RecordingBoundaries World { get; }

    public LibraryOutput Output { get; }

    public LibraryInput Input { get; }

    public LibraryText Text { get; }

    public LibraryScreens Screens { get; }

    public LibraryDossiers Dossiers { get; }

    /// <summary>An argument word, as a signed number.</summary>
    public int Arg(int index) => Case.Args[index];

    /// <summary>An argument word as an address of the data segment.</summary>
    public int Address(int index) => (ushort)Case.Args[index];

    /// <summary>The text at an address of the data segment before the call (a C string: up to its end mark).</summary>
    public string TextAt(int address) => CString(Case.DgroupIn, address);

    /// <summary>The texts of a table of near pointers at an address of the data segment.</summary>
    public string[] TextTable(int address, int count) =>
        [.. Enumerable.Range(0, Math.Max(count, 0)).Select(i => TextAt(BinaryPrimitives.ReadUInt16LittleEndian(Case.DgroupIn.AsSpan(address + 2 * i))))];

    /// <summary>
    /// A buffer of the caller's that the routine changes (a tag, a line), loaded from the data segment: the text and
    /// its end mark, or <paramref name="capacity"/> bytes. It is compared by hand afterwards (<see cref="CompareByHand"/>).
    /// </summary>
    public TextBuffer BufferByHand(int address, int capacity = 0)
    {
        if (capacity == 0)
        {
            capacity = TextAt(address).Length + 1;
        }
        var buffer = new TextBuffer(capacity);
        Case.DgroupIn.AsSpan(address, capacity).CopyTo(buffer.Bytes);
        return buffer;
    }

    /// <summary>Compares a buffer replayed by hand with the original's bytes after the call, and leaves its bytes out of the image comparison.</summary>
    public void CompareByHand(int address, TextBuffer buffer)
    {
        Assert.Equal(Convert.ToHexString(Case.DgroupOut.AsSpan(address, buffer.Bytes.Length)), Convert.ToHexString(buffer.Bytes));
        _ignore.Add((address, address + buffer.Bytes.Length));
    }

    /// <summary>The shape records of a table at an address, up to and with its end record.</summary>
    public List<Shape> ShapesAt(int address)
    {
        var shapes = new List<Shape>();
        for (var at = address; ; at += 12)
        {
            short Word(int i) => BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupIn.AsSpan(at + 2 * i));
            var shape = new Shape { Kind = Word(0), X1 = Word(1), Y1 = Word(2), X2 = Word(3), Y2 = Word(4), Color = Word(5) };
            shapes.Add(shape);
            if (shape.Kind == Shape.End || shapes.Count > 200)
            {
                return shapes;
            }
        }
    }

    /// <summary>Compares a shape table replayed by hand with the original's bytes after the call.</summary>
    public void CompareShapes(int address, List<Shape> shapes)
    {
        var bytes = new byte[12 * shapes.Count];
        for (var i = 0; i < shapes.Count; i++)
        {
            short[] words = [shapes[i].Kind, shapes[i].X1, shapes[i].Y1, shapes[i].X2, shapes[i].Y2, shapes[i].Color];
            for (var w = 0; w < words.Length; w++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12 * i + 2 * w), words[w]);
            }
        }
        Assert.Equal(Convert.ToHexString(Case.DgroupOut.AsSpan(address, bytes.Length)), Convert.ToHexString(bytes));
        _ignore.Add((address, address + bytes.Length));
    }

    /// <summary>Ranges the C library's file routines change beneath the boundary: their stream records and buffers.</summary>
    public void IgnoreLibraryInternals(int start, int end) => _ignore.Add((start, end));

    /// <summary>Compares everything the function left behind with the original.</summary>
    public void Finish(int? result)
    {
        if (result is { } value)
        {
            Assert.True(unchecked((short)value) == Case.Ax, $"the C# returns {value}, the original {Case.Ax}");
        }
        World.AssertAllCallsMade();
        // the C library's file routines keep a stream record and a 512-byte buffer on the near heap; the read
        // buffer's address is the second argument of each read
        foreach (var call in Case.Calls.Where(c => c.Target == "read"))
        {
            var buffer = (ushort)call.Args[1].GetInt64();
            _ignore.Add((buffer - 8, buffer + 512));
        }
        if (Case.Calls.Any(c => c.Target == "open"))
        {
            _ignore.AddRange(LibraryInternals[_module]);
        }
        if (_messageBeforeFirst != 0)
        {
            // what the C# wrote into message -1 goes to the variables it lies over, and its word is compared by hand
            var image = (byte[])Case.DgroupIn.Clone();
            _layout.Store(Part, image);
            var clues = _messageBeforeFirst + 6;
            BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(clues), Part.MessageBeforeFirst.Clues);
            _layout.Load(image, Part);
            var original = BinaryPrimitives.ReadInt16LittleEndian(Case.DgroupOut.AsSpan(clues));
            Assert.True(original == Part.MessageBeforeFirst.Clues, $"message -1's knowledge word: C# {Part.MessageBeforeFirst.Clues}, original {original}");
            _ignore.Add((clues, clues + 2));
        }
        OracleRun.AssertImage(_layout, Part, Case.DgroupIn, Case.DgroupOut, _ignore, Case.IgnoreDeclared);
    }

    /// <summary>
    /// The C library's own variables that its file routines change beneath the <c>IFiles</c> boundary: the near heap's
    /// pointers and block headers, the file table, the stream table and the streams' second table. They have no
    /// symbols; the ranges are where the recorded runs that read a file change bytes no routine of the game owns.
    /// </summary>
    private static readonly Dictionary<string, (int Start, int End)[]> LibraryInternals = new()
    {
        ["FINAL"] = [(0x5680, 0x5690), (0x5740, 0x5890), (0x79b0, 0x79e2), (0xbb00, 0xc000)],
        ["GAME"] = [(0x4e98, 0x4ea0), (0x4f50, 0x50a0), (0x71d0, 0x7202), (0xb200, 0xc000)],
        ["TAC"] = [(0x3d50, 0x3d52), (0x3e10, 0x3e12), (0x3e84, 0x3e86), (0x8db0, 0x8dda)],
        // from the donor runs of 20261116 (BUG reads no file on the recorded practice route)
        ["BUG"] = [(0x33e8, 0x33ea), (0x34a8, 0x34a9), (0x34d9, 0x34da), (0x3568, 0x356c), (0x35c8, 0x35d0), (0x5510, 0x5542), (0x9800, 0xa000)],
    };

    private readonly string _module;

    /// <summary>Where message -1 lies, or 0.</summary>
    private int _messageBeforeFirst;

    private void LoadTexts()
    {
        var image = Case.DgroupIn;
        TextTable(At.VariableNames, ILibraryTextState.MessageVariables).CopyTo(Part.VariableNameTexts, 0);
        Part.ClearanceNotice = TextAt(At.ClearanceNotice);
        Part.LineSeparator = TextAt(At.LineSeparator);
        Part.LineEnd = TextAt(At.LineEnd);
        Part.MessageTemplate = TextAt(At.MessageTemplate);
        for (var i = 0; i < Part.BuildingKinds.Length; i++)
        {
            Part.BuildingKinds[i] = TextAt(At.BuildingKinds[i]);
        }
        if (At.FirstNames != 0)
        {
            Part.Names.AgentPlaceholder = TextAt(At.AgentPlaceholder);
            Part.Names.Space = TextAt(At.NameSpace);
            TextTable(At.FirstNames, PersonNameTexts.NamesPerTable).CopyTo(Part.Names.FirstNames[0], 0);
            TextTable(At.FirstNames + 2 * PersonNameTexts.NamesPerTable, PersonNameTexts.NamesPerTable).CopyTo(Part.Names.FirstNames[1], 0);
            TextTable(At.Surnames, PersonNameTexts.NamesPerTable).CopyTo(Part.Names.Surnames, 0);
        }
        _ = image;
        var c = CampaignAddresses.ByModule[_module];
        var d = Part.Dossier;
        if (c.TryGetValue("MonthNames", out var months))
        {
            TextTable(months, 12).CopyTo(d.Words.MonthNames, 0);
        }
        if (c.TryGetValue("RankNames", out var ranks))
        {
            TextTable(ranks, 8).CopyTo(d.RankNames, 0);
        }
        d.MessageTemplate = Part.MessageTemplate;
        if (c.TryGetValue("ClueLabels", out var labels))
        {
            for (var kind = 0; kind < d.ClueLabels.Length; kind++)
            {
                TextTable(labels + 16 * kind, 8).CopyTo(d.ClueLabels[kind], 0);
            }
        }
        if (c.TryGetValue("ClueMethods", out var methods))
        {
            TextTable(methods, 7).CopyTo(d.ClueMethods, 0);
            TextTable(c["ActivityPhrases"], 40).CopyTo(d.ActivityPhrases, 0);
        }
        if (c.TryGetValue("ClueTokens", out var tokens))
        {
            TextTable(tokens, 5).CopyTo(d.ClueTokens, 0);
        }
        if (c.TryGetValue("Sn", out var sn))
        {
            // message -1 lies over other variables (the last city's record, the count of persons): read from the image,
            // and its knowledge word written back through the image after the call (see Finish)
            var before = sn - CampaignLayouts.SynopsisSize;
            Part.MessageBeforeFirst.Time = BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(before + 2));
            Part.MessageBeforeFirst.Clues = BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(before + 6));
            Part.MessageBeforeFirst.Loc2 = unchecked((sbyte)image[before + 0x0d]);
            _messageBeforeFirst = before;
        }
        foreach (var property in typeof(DossierTexts).GetProperties().Concat(typeof(MessageLogTexts).GetProperties()))
        {
            if (property.PropertyType == typeof(string) && property.CanWrite && c.TryGetValue(property.Name, out var address))
            {
                property.SetValue(property.DeclaringType == typeof(DossierTexts) ? d : d.Words, TextAt(address));
            }
        }
    }

    /// <summary>The campaign tables and buffers of the dossier routines, where the program has them.</summary>
    private static void AddCampaign(StateLayout<LibraryPart> layout, IReadOnlyDictionary<string, int> at, byte[] image)
    {
        void Word(string name, Func<LibraryPart, short> get, Action<LibraryPart, short> set)
        {
            if (at.TryGetValue(name, out var address))
            {
                layout.Int16(address, $"DS:{address:x4} {name}", get, set);
            }
        }
        Word("NextSyn", s => s.NextSyn, (s, v) => s.NextSyn = v);
        Word("NACTIVITY", s => s.NACTIVITY, (s, v) => s.NACTIVITY = v);
        Word("Diff", s => s.Diff, (s, v) => s.Diff = v);
        Word("TheCrime", s => s.TheCrime, (s, v) => s.TheCrime = v);
        Word("InterPol", s => s.InterPol, (s, v) => s.InterPol = v);
        Word("ClueMethod", s => s.Dossier.ClueMethod, (s, v) => s.Dossier.ClueMethod = v);
        Word("CodedFlag", s => s.Dossier.CodedFlag, (s, v) => s.Dossier.CodedFlag = v);
        Word("Paused", s => s.Dossier.Paused, (s, v) => s.Dossier.Paused = v);
        if (at.TryGetValue("Active", out var active))
        {
            layout.Int16Array(active, 16, $"DS:{active:x4} active", s => s.ActiveCounts)
                .Int16Array(at["OrgCounters"], 16, $"DS:{at["OrgCounters"]:x4} org_counters", s => s.OrgCounts);
        }
        if (at.TryGetValue("Itoa", out var itoa))
        {
            layout.Bytes(itoa, 10, $"DS:{itoa:x4} itoa_buf", s => s.Dossier.NumberText.Bytes);
        }
        if (at.TryGetValue("Sn", out var sn))
        {
            Records(layout, sn, "sn", CampaignLayouts.SynopsisSize, CampaignLayouts.Synopsis, s => s.Sn);
        }
        if (at.TryGetValue("Ev", out var ev))
        {
            Records(layout, ev, "ev", CampaignLayouts.EventSize, CampaignLayouts.Event, s => s.Ev);
        }
        if (at.TryGetValue("Ac", out var ac))
        {
            Records(layout, ac, "ac", CampaignLayouts.ActivitySize, CampaignLayouts.Activity, s => s.Ac);
        }
        if (at.TryGetValue("Values", out var values))
        {
            // the placeholders' buffers: each up to the next one, the last 52 bytes (up to the table of their names)
            var pointers = Enumerable.Range(0, ILibraryTextState.MessageVariables).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(values + 2 * i))).ToArray();
            for (var i = 0; i < pointers.Length; i++)
            {
                var index = i;
                var length = i + 1 < pointers.Length && pointers[i + 1] > pointers[i] ? pointers[i + 1] - pointers[i] : 0x34;
                layout.FixedPointer(values + 2 * i, $"DS:{values + 2 * i:x4} msg_subst[{i}]", pointers[i])
                    .Text(pointers[i], length, $"DS:{pointers[i]:x4} msg_subst[{i}] text", s => s.VariableValueTexts[index], (s, v) => s.VariableValueTexts[index] = v);
            }
        }
    }

    private static StateLayout<LibraryPart> BuildLayout(PartAddresses at, byte[] image)
    {
        var port = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at.Rp1));
        var layout = new StateLayout<LibraryPart>("L6 variables")
            .Bytes(at.Txt, at.TxtLength, $"DS:{at.Txt:x4} Txt", s => s.MessageText.Bytes)
            .Part(port, $"DS:{port:x4} Rp1X", RasterPortLayout.Layout, s => s.Rp1)
            .FixedPointer(at.Rp1, $"DS:{at.Rp1:x4} Rp1", port)
            .Int16(at.Status, $"DS:{at.Status:x4} Status", s => (short)s.Status, (s, v) => s.Status = (Options)v)
            .UInt32(at.Holdrand, $"DS:{at.Holdrand:x4} holdrand", s => s.Random.State, (s, v) => s.Random.State = v)
            .Bytes(at.TokenTail, ILibraryTextState.LineCapacity, $"DS:{at.TokenTail:x4} token_tail_buf", s => s.TokenTail.Bytes)
            .Bytes(at.MessageTag, 9, $"DS:{at.MessageTag:x4} msg_tag_buf", s => s.MessageTag.Bytes)
            .Part(at.Pl, $"DS:{at.Pl:x4} pl", CampaignLayouts.Plot, s => s.Pl)
            .Int16(at.Nperson, $"DS:{at.Nperson:x4} Nperson", s => s.Nperson, (s, v) => s.Nperson = v)
            .Int16(at.Nloc, $"DS:{at.Nloc:x4} Nloc", s => s.Nloc, (s, v) => s.Nloc = v)
            .Char(at.GMode, $"DS:{at.GMode:x4} GMode", s => s.GMode, (s, v) => s.GMode = v)
            .Flag(at.Cga, $"DS:{at.Cga:x4} CGA", s => s.CGA, (s, v) => s.CGA = v)
            .Int16(at.Picface, $"DS:{at.Picface:x4} picface", s => s.Picface, (s, v) => s.Picface = v)
            .Int16(at.SaveWidth, $"DS:{at.SaveWidth:x4} save_width", s => s.Animation.SaveWidth, (s, v) => s.Animation.SaveWidth = v)
            .ImageArray(at.Sprite, PartAddresses.SpriteCount, $"DS:{at.Sprite:x4} sprite", s => s.SpriteTable, s => s.Images)
            .Bytes(at.ColorMapA, 16, $"DS:{at.ColorMapA:x4} colour_map_a", s => s.ColorMapA)
            .Bytes(at.ColorMapB, 16, $"DS:{at.ColorMapB:x4} colour_map_b", s => s.ColorMapB)
            .Bytes(at.PhotoColorMap, 16, $"DS:{at.PhotoColorMap:x4} photo colour map", s => s.PhotoColorMap);
        Records(layout, at.Ps, "ps", CampaignLayouts.PersonSize, CampaignLayouts.Person, s => s.Ps);
        Records(layout, at.Orgs, "org", CampaignLayouts.OrgSize, CampaignLayouts.Org, s => s.Orgs);
        Records(layout, at.Ct, "ct", CampaignLayouts.CitySize, CampaignLayouts.City, s => s.Ct);
        Records(layout, at.Lc, "lc", CampaignLayouts.LocationSize, CampaignLayouts.Location, s => s.Lc);
        Records(layout, at.Cl, "cl", CampaignLayouts.ClueSize, CampaignLayouts.Clue, s => s.Cl);
        return layout;
    }

    private static void Records<TRecord>(StateLayout<LibraryPart> layout, int offset, string name, int size, StateLayout<TRecord> record, Func<LibraryPart, TRecord[]> table)
        where TRecord : class
    {
        var count = table(new LibraryPart(1)).Length;
        for (var i = 0; i < count; i++)
        {
            var index = i;
            layout.Part(offset + size * index, $"DS:{offset:x4} {name}[{index}]", record, s => table(s)[index]);
        }
    }

    private static string CString(byte[] image, int address)
    {
        var end = Array.IndexOf(image, (byte)0, address);
        if (end < 0)
        {
            return "";
        }
        return Encoding.Latin1.GetString(image, address, end - address);
    }
}

/// <summary>
/// Where a program keeps the campaign tables, buffers and texts of the dossier routines (from each copy's instructions
/// and symbols/names.txt). A name the dictionary lacks is a variable the program has no use for here.
/// </summary>
internal static class CampaignAddresses
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ByModule = new Dictionary<string, IReadOnlyDictionary<string, int>>
    {
        ["FINAL"] = new Dictionary<string, int>
        {
            ["Sn"] = 0x84b8, ["Ev"] = 0x8bca, ["Ac"] = 0x9fa6, ["NextSyn"] = 0x96ca, ["NACTIVITY"] = 0x9fa2, ["Diff"] = 0x7de6,
            ["InterPol"] = 0xae78, ["TheCrime"] = 0x9e90, ["Itoa"] = 0x5d1c, ["Values"] = 0x3ed8,
            ["MonthNames"] = 0x367c, ["RankNames"] = 0x3ae0, ["MessageFile"] = 0x3fb4, ["Ellipsis"] = 0x3fde,
            ["Received"] = 0x3d6d, ["Sent"] = 0x3d79, ["Space"] = 0x3d85, ["Someone"] = 0x3d87, ["OfThe"] = 0x3d90, ["In"] = 0x3d99, ["On"] = 0x3d9e, ["End"] = 0x3da5,
            ["PersonnelFile"] = 0x46d8, ["ActionTeam"] = 0x46e7, ["MasterPlan"] = 0x46a6, ["Secret"] = 0x46b2, ["Arrested"] = 0x46bd,
            ["InHiding"] = 0x46c6, ["Turned"] = 0x46d0, ["Comma"] = 0x46f4, ["NoActivity"] = 0x46f7,
            ["Name"] = 0x3cc7, ["Rank"] = 0x3cce, ["Org"] = 0x3cd5, ["City"] = 0x3cdc, ["Recruited"] = 0x3ce4, ["By"] = 0x3cee,
            ["Period"] = 0x3cf8, ["Role"] = 0x3cfa, ["RoleNotInvolved"] = 0x3d02, ["RoleUnknown"] = 0x3d16, ["UnderArrest"] = 0x3d25,
            ["Hiding"] = 0x3d34, ["TurnedNote"] = 0x3d40, ["NotDecoded"] = 0x3d4e, ["More"] = 0x3d64, ["Quote"] = 0x3d4a,
            ["ClueLabels"] = 0x3b00, ["Hundreds"] = 0x3ca2, ["Thousands"] = 0x3ca5, ["ClueTagBasic"] = 0x3ca9, ["ClueTagCase"] = 0x3cae,
            ["ClueTagCast"] = 0x3cb5, ["ClueTokens"] = 0x3c76, ["CluesFile"] = 0x3cbc, ["ClueMethod"] = 0x9e8c,
            ["PlotPrefix"] = 0x3696, ["PlotSuffix"] = 0x3697, ["IdentifiedPrefix"] = 0x36cb, ["IdentifiedMiddle"] = 0x36cc, ["IdentifiedSuffix"] = 0x36f0, ["PhotoPrefix"] = 0x36f2, ["PhotoSuffix"] = 0x3714, ["SpottedPrefix"] = 0x3717, ["SpottedMiddle"] = 0x3718, ["SpottedSuffix"] = 0x372e, ["MemberPrefix"] = 0x3730, ["MemberMiddle"] = 0x3731, ["MemberSuffix"] = 0x375a, ["InformationPrefix"] = 0x375c, ["InformationSuffix"] = 0x3790, ["RankPrefix"] = 0x3792, ["RankSuffix"] = 0x37b2, ["RecruitingPrefix"] = 0x37b4, ["RecruitingSuffix"] = 0x37db, ["RecruiterPrefix"] = 0x37dd, ["RecruiterSuffix"] = 0x3804, ["Paused"] = 0xa7b0,
            ["Active"] = 0xae38, ["OrgCounters"] = 0xae58, ["CodedFlag"] = 0x8bc8, ["ClueMethods"] = 0x3c3a, ["ClueTag"] = 0x3c64, ["ActivityPhrases"] = 0x35fc, ["ActivityPeriod"] = 0x3c80, ["ThisFace"] = 0x3c82, ["CodeNameReal"] = 0x3c8d, ["CodeNameDecoy"] = 0x3c96, ["ClueTitle"] = 0x3c01, ["Source"] = 0x3c06, ["Slash"] = 0x3c10, ["ReportNewline"] = 0x3c12, ["MethodLabel"] = 0x3c14, ["RelatedClues"] = 0x3c1f, ["NoneRelated"] = 0x3c31,
        },
        ["GAME"] = new Dictionary<string, int>
        {
            ["Ellipsis"] = 0x16e2, ["Values"] = 0x3a1e, ["Itoa"] = 0x5514,
            ["ClueLabels"] = 0x3042, ["Hundreds"] = 0x31e4, ["Thousands"] = 0x31e7,
        },
        ["TAC"] = new Dictionary<string, int>
        {
            ["Sn"] = 0xa1e6, ["Ev"] = 0xa8f0, ["Ac"] = 0xbdf6, ["NextSyn"] = 0xb3f0, ["NACTIVITY"] = 0xbdf2, ["Diff"] = 0x91e0,
            ["InterPol"] = 0xcbfc, ["TheCrime"] = 0xbb7e, ["Itoa"] = 0x72b4, ["Values"] = 0x2e1e,
            ["MonthNames"] = 0x25c2, ["RankNames"] = 0x2a26, ["MessageFile"] = 0x2efa, ["Ellipsis"] = 0x2f24,
            ["Received"] = 0x2cb3, ["Sent"] = 0x2cbf, ["Space"] = 0x2ccb, ["Someone"] = 0x2ccd, ["OfThe"] = 0x2cd6, ["In"] = 0x2cdf, ["On"] = 0x2ce4, ["End"] = 0x2ceb,
            ["PersonnelFile"] = 0x361e, ["ActionTeam"] = 0x362d, ["MasterPlan"] = 0x35ec, ["Secret"] = 0x35f8, ["Arrested"] = 0x3603,
            ["InHiding"] = 0x360c, ["Turned"] = 0x3616, ["Comma"] = 0x363a, ["NoActivity"] = 0x363d,
            ["Name"] = 0x2c0d, ["Rank"] = 0x2c14, ["Org"] = 0x2c1b, ["City"] = 0x2c22, ["Recruited"] = 0x2c2a, ["By"] = 0x2c34,
            ["Period"] = 0x2c3e, ["Role"] = 0x2c40, ["RoleNotInvolved"] = 0x2c48, ["RoleUnknown"] = 0x2c5c, ["UnderArrest"] = 0x2c6b,
            ["Hiding"] = 0x2c7a, ["TurnedNote"] = 0x2c86, ["NotDecoded"] = 0x2c94, ["More"] = 0x2caa, ["Quote"] = 0x2c90,
            ["ClueLabels"] = 0x2a46, ["Hundreds"] = 0x2be8, ["Thousands"] = 0x2beb, ["ClueTagBasic"] = 0x2bef, ["ClueTagCase"] = 0x2bf4,
            ["ClueTagCast"] = 0x2bfb, ["ClueTokens"] = 0x2bbc, ["CluesFile"] = 0x2c02, ["ClueMethod"] = 0xbb76,
            ["PlotPrefix"] = 0x25dc, ["PlotSuffix"] = 0x25dd, ["IdentifiedPrefix"] = 0x2611, ["IdentifiedMiddle"] = 0x2612, ["IdentifiedSuffix"] = 0x2636, ["PhotoPrefix"] = 0x2638, ["PhotoSuffix"] = 0x265a, ["SpottedPrefix"] = 0x265d, ["SpottedMiddle"] = 0x265e, ["SpottedSuffix"] = 0x2674, ["MemberPrefix"] = 0x2676, ["MemberMiddle"] = 0x2677, ["MemberSuffix"] = 0x26a0, ["InformationPrefix"] = 0x26a2, ["InformationSuffix"] = 0x26d6, ["RankPrefix"] = 0x26d8, ["RankSuffix"] = 0x26f8, ["RecruitingPrefix"] = 0x26fa, ["RecruitingSuffix"] = 0x2721, ["RecruiterPrefix"] = 0x2723, ["RecruiterSuffix"] = 0x274a, ["Paused"] = 0xc5fe,
            ["Active"] = 0xcbbc, ["OrgCounters"] = 0xcbdc, ["CodedFlag"] = 0xa8ee, ["ClueMethods"] = 0x2b80, ["ClueTag"] = 0x2baa, ["ActivityPhrases"] = 0x2542, ["ActivityPeriod"] = 0x2bc6, ["ThisFace"] = 0x2bc8, ["CodeNameReal"] = 0x2bd3, ["CodeNameDecoy"] = 0x2bdc, ["ClueTitle"] = 0x2b47, ["Source"] = 0x2b4c, ["Slash"] = 0x2b56, ["ReportNewline"] = 0x2b58, ["MethodLabel"] = 0x2b5a, ["RelatedClues"] = 0x2b65, ["NoneRelated"] = 0x2b77,
        },
        ["BUG"] = new Dictionary<string, int>
        {
            ["Sn"] = 0x6058, ["Ev"] = 0x6760, ["Ac"] = 0x77b4, ["NextSyn"] = 0x7260, ["NACTIVITY"] = 0x77b0, ["Diff"] = 0x1652,
            ["InterPol"] = 0x8c50, ["TheCrime"] = 0x7640, ["Itoa"] = 0x3a0e, ["Values"] = 0x231c,
            ["MonthNames"] = 0x1ac0, ["RankNames"] = 0x1f24, ["MessageFile"] = 0x23f8, ["Ellipsis"] = 0x2422,
            ["Received"] = 0x21b1, ["Sent"] = 0x21bd, ["Space"] = 0x21c9, ["Someone"] = 0x21cb, ["OfThe"] = 0x21d4, ["In"] = 0x21dd, ["On"] = 0x21e2, ["End"] = 0x21e9,
            ["PersonnelFile"] = 0x2b1c, ["ActionTeam"] = 0x2b2b, ["MasterPlan"] = 0x2aea, ["Secret"] = 0x2af6, ["Arrested"] = 0x2b01,
            ["InHiding"] = 0x2b0a, ["Turned"] = 0x2b14, ["Comma"] = 0x2b38, ["NoActivity"] = 0x2b3b,
            ["Name"] = 0x210b, ["Rank"] = 0x2112, ["Org"] = 0x2119, ["City"] = 0x2120, ["Recruited"] = 0x2128, ["By"] = 0x2132,
            ["Period"] = 0x213c, ["Role"] = 0x213e, ["RoleNotInvolved"] = 0x2146, ["RoleUnknown"] = 0x215a, ["UnderArrest"] = 0x2169,
            ["Hiding"] = 0x2178, ["TurnedNote"] = 0x2184, ["NotDecoded"] = 0x2192, ["More"] = 0x21a8, ["Quote"] = 0x218e,
            ["ClueLabels"] = 0x1f44, ["Hundreds"] = 0x20e6, ["Thousands"] = 0x20e9, ["ClueTagBasic"] = 0x20ed, ["ClueTagCase"] = 0x20f2,
            ["ClueTagCast"] = 0x20f9, ["ClueTokens"] = 0x20ba, ["CluesFile"] = 0x2100, ["ClueMethod"] = 0x763c,
            ["PlotPrefix"] = 0x1ada, ["PlotSuffix"] = 0x1adb, ["IdentifiedPrefix"] = 0x1b0f, ["IdentifiedMiddle"] = 0x1b10, ["IdentifiedSuffix"] = 0x1b34, ["PhotoPrefix"] = 0x1b36, ["PhotoSuffix"] = 0x1b58, ["SpottedPrefix"] = 0x1b5b, ["SpottedMiddle"] = 0x1b5c, ["SpottedSuffix"] = 0x1b72, ["MemberPrefix"] = 0x1b74, ["MemberMiddle"] = 0x1b75, ["MemberSuffix"] = 0x1b9e, ["InformationPrefix"] = 0x1ba0, ["InformationSuffix"] = 0x1bd4, ["RankPrefix"] = 0x1bd6, ["RankSuffix"] = 0x1bf6, ["RecruitingPrefix"] = 0x1bf8, ["RecruitingSuffix"] = 0x1c1f, ["RecruiterPrefix"] = 0x1c21, ["RecruiterSuffix"] = 0x1c48, ["Paused"] = 0x7fbc,
            ["Active"] = 0x8c10, ["OrgCounters"] = 0x8c30, ["CodedFlag"] = 0x675e, ["ClueMethods"] = 0x207e, ["ClueTag"] = 0x20a8, ["ActivityPhrases"] = 0x1a40, ["ActivityPeriod"] = 0x20c4, ["ThisFace"] = 0x20c6, ["CodeNameReal"] = 0x20d1, ["CodeNameDecoy"] = 0x20da, ["ClueTitle"] = 0x2045, ["Source"] = 0x204a, ["Slash"] = 0x2054, ["ReportNewline"] = 0x2056, ["MethodLabel"] = 0x2058, ["RelatedClues"] = 0x2063, ["NoneRelated"] = 0x2075,
        },
    };
}
