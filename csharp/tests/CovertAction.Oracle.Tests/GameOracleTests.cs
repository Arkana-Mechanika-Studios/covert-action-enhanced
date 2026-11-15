using System.Buffers.Binary;
using System.Text;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Presentation;
using CovertAction.Presentation.Game;
using CovertAction.Presentation.Library;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The campaign part (GAME.EXE) against the recorded runs of the original: each function the C# reconstructs is called
/// as the original was, from the data segment it had; the result, the data segment afterwards and the calls across the
/// boundaries must be the original's. Skipped without vectors.
/// </summary>
public sealed class GameOracleTests(ITestOutputHelper output)
{
    private const string Module = "GAME";

    /// <summary>One recorded case set up for replay: the C# state as the original's memory had it.</summary>
    private sealed class Run
    {
        public Run(OracleCase oracleCase)
        {
            Case = oracleCase;
            State = Load(oracleCase.DgroupIn);
            World = new RecordingBoundaries(oracleCase.Calls, o => GameDgroup.Layout.AddressOf(State, o), State.Images);
            // the animation loader takes the scene script's memory from DOS and gives it back (D-13: the library's loader
            // holds the file's parts itself)
            World.WithoutCounterpart("int21.48", "int21.49");
            var world = new Boundaries(World, World, World, World, World, World);
            if (oracleCase.SharedIn is { } block)
            {
                SharedBlockCodec.Layout.Load(block, Shared);
            }
            Screens = new CampaignScreens(State, world, Shared);
            // the open archive's number is the picture routines' own (GAME DS:449a data_file_handle)
            Screens.Pictures.ArchiveHandle = BinaryPrimitives.ReadInt16LittleEndian(oracleCase.DgroupIn.AsSpan(DataFileHandle));
            Menus = Screens.Menus;
            Saves = new CampaignSaves(State, Shared, world);
            Plot = new PlotEngine(State, Shared, world, Screens, Saves);
            Menus2 = new DataFilesMenus(State, Screens, Plot, Shared);
            SaveScreens = Saves.SaveScreens(Screens.Input, Menus, Plot.PopupMenuOnCleanScreen);
            Cities = new CityScreens(State, Shared, world, Screens, Plot, Menus2, SaveScreens);
            Part = new CampaignPart(State, Shared, world, OriginalBehaviour.Faithful);
            Part.Pictures.ArchiveHandle = Screens.Pictures.ArchiveHandle;
        }

        /// <summary>The city screen, the hotel and the flight map.</summary>
        public CityScreens Cities { get; }

        /// <summary>The library's save screens on the campaign's routines.</summary>
        public LibrarySaves SaveScreens { get; }

        /// <summary>
        /// Takes the campaign's tables of the hand-off block into the typed block, for a function that loads them;
        /// counts that do not fit the block are left to the loader, which says so itself.
        /// </summary>
        public Run WithCampaign()
        {
            try
            {
                SharedBlockCodec.LoadCampaign(Case.SharedIn, Shared);
            }
            catch (ArgumentException)
            {
            }
            return this;
        }

        /// <summary>
        /// Runs a function of the walk: it loads pictures (<see cref="LoadingPictures"/>) through calls deeper than the
        /// other functions', so that words of the stack below the C library's frames are left behind as well
        /// (<see cref="DeepStack"/>: a handle and a far pointer of the picture routines' locals).
        /// </summary>
        public void Walking(Action call)
        {
            ComparedByHand.AddRange(DeepStack);
            LoadingPictures(call);
        }

        /// <summary>The whole part on the case's state (its own screens and menus).</summary>
        public CampaignPart Part { get; }

        /// <summary>The menus of the data files.</summary>
        public DataFilesMenus Menus2 { get; }

        /// <summary>The passing of time and the plot's progress.</summary>
        public PlotEngine Plot { get; }

        /// <summary>The library's message box on this part's state.</summary>
        public LibraryMenus Menus { get; }

        public OracleCase Case { get; }

        public CampaignState State { get; }

        public RecordingBoundaries World { get; }

        /// <summary>The hand-off block as the case had it.</summary>
        public SharedState Shared { get; } = new();

        /// <summary>The campaign's save routines.</summary>
        public CampaignSaves Saves { get; }

        /// <summary>The far pointer the save routines move bytes through: working memory of one call.</summary>
        private static readonly (int Start, int End) SaveBufferPointer = (0x55ae, 0x55b2);

        /// <summary>Compares the head of the hand-off block (up to the campaign's tables) with the original's, after everything else.</summary>
        public void FinishHandOffHead()
        {
            Finish();
            var actual = (byte[])Case.SharedIn!.Clone();
            SharedBlockCodec.Layout.Store(Shared, actual);
            Assert.True(actual.AsSpan(0, SharedState.TablesOffset).SequenceEqual(Case.SharedOut.AsSpan(0, SharedState.TablesOffset)), "the head of the hand-off block differs from the original's");
        }

        /// <summary>
        /// Compares the hand-off block with the original's after a save: its head field by field, and, when the
        /// save went into the block, the campaign's tables behind it byte by byte.
        /// </summary>
        public void FinishSave()
        {
            ComparedByHand.Add(SaveBufferPointer);
            Finish();
            var (before, after) = (Case.SharedIn!, Case.SharedOut!);
            var actual = (byte[])before.Clone();
            SharedBlockCodec.Layout.Store(Shared, actual);
            Assert.True(actual.AsSpan(0, SharedState.TablesOffset).SequenceEqual(after.AsSpan(0, SharedState.TablesOffset)), "the head of the hand-off block differs from the original's");
            if (State.SaveMemOffset != 0)
            {
                SharedBlockCodec.StoreCampaign(Shared, actual);
                var end = (ushort)State.SaveMemOffset;
                Assert.True(end == SharedState.TablesOffset + CampaignTablesCodec.Size(Shared.Campaign.Cast.Count, Shared.Campaign.Events.Count), "the tables end elsewhere than the original's");
                var differs = Enumerable.Range(SharedState.TablesOffset, end - SharedState.TablesOffset).Where(i => actual[i] != after[i]).ToList();
                Assert.True(differs.Count == 0, $"the hand-off block's tables differ from the original's in {differs.Count} bytes, the first at +0x{differs.FirstOrDefault():x}");
            }
            else
            {
                Assert.True(before.AsSpan(SharedState.TablesOffset).SequenceEqual(after.AsSpan(SharedState.TablesOffset)), "a save to a file changed the hand-off block's tables in the original");
            }
        }

        public CampaignScreens Screens { get; }

        /// <summary>An argument word, as a signed number.</summary>
        public int Arg(int index) => Case.Args[index];

        /// <summary>An argument word that is a near pointer, as an address of the data segment.</summary>
        public int Address(int index) => (ushort)Case.Args[index];

        /// <summary>The text at an address of the data segment before the call (a C string: up to its end mark).</summary>
        public string TextAt(int address) => CString(Case.DgroupIn, address);

        /// <summary>The texts of a table of near pointers at an address of the data segment.</summary>
        public string[] TextTableAt(int address, int count) =>
            [.. Enumerable.Range(0, count).Select(i => TextAt(BinaryPrimitives.ReadUInt16LittleEndian(Case.DgroupIn.AsSpan(address + 2 * i))))];

        /// <summary>Ranges of the data segment a test compared by hand (a caller's buffer on the stack), start inclusive, end exclusive.</summary>
        public List<(int Start, int End)> ComparedByHand { get; } = [];

        /// <summary>
        /// Runs a function that loads pictures. The library's loader decodes a picture at once (D-12), so the working
        /// memory of the original's row-by-row reader and decoder is left out of the comparison (<see cref="PictureLoaderScratch"/>).
        /// </summary>
        public void LoadingPictures(Action call)
        {
            ComparedByHand.AddRange(PictureLoaderScratch);
            call();
        }

        /// <summary>Compares everything the function left behind with the original.</summary>
        public void Finish()
        {
            World.AssertAllCallsMade();
            // the animation player's working memory, and its words of DOS segments and record addresses (D-13)
            var player = OracleRun.ComparePlayerWordsByHand(PanEngineLayout.GamePlaces, State.Pan, Case.DgroupIn, Case.DgroupOut);
            OracleRun.AssertImage(GameDgroup.Layout, State, Case.DgroupIn, Case.DgroupOut,
                [.. Case.Ignore, .. ComparedByHand, .. LibraryLayout.GameScratch, .. StdioScratch, .. PanEngineLayout.GamePlaces.Scratch, .. player],
                Case.IgnoreDeclared);
        }
    }

    /// <summary>
    /// The C library's private data of its buffered files (the stream table, the file buffer on the heap and its stack
    /// frames below the text buffer), which the text readers use behind <c>IFiles</c>; the same ranges as the library's
    /// text tests leave out for GAME (LibraryTextOracleTests).
    /// </summary>
    private static readonly (int Start, int End)[] StdioScratch = [(0x4e98, 0x4ea0), (0x4f50, 0x50a0), (0x71d0, 0x7202), (0xb200, 0xc000)];

    /// <summary>The state as a data segment of the original holds it.</summary>
    private static CampaignState Load(byte[] dgroup)
    {
        if (GameDgroup.Layout.OutsideModel(dgroup) is { Count: > 0 } reasons)
        {
            throw new OutsideModelException(string.Join("; ", reasons.Take(4)));
        }
        var state = new CampaignState();
        GameDgroup.Layout.Load(dgroup, state);
        return state;
    }

    private static string CString(byte[] image, int address)
    {
        var end = Array.IndexOf(image, (byte)0, address);
        return Encoding.Latin1.GetString(image, address, end - address);
    }

    /// <summary>
    /// The working memory of GAME's picture reader and decoder, which the library's loader does not reproduce (D-12):
    /// DS:57b0 file_buffer_handle and the decoder's tables up to 6fce, 7191..71cd, 8ed8 file_read_ptr, 9da8
    /// file_refill_function and 9dac file_block_buffer, and the row buffer at a870.
    /// </summary>
    private static readonly (int Start, int End)[] PictureLoaderScratch = [(0x57b0, 0x6fd0), (0x7191, 0x71ce), (0x8ed8, 0x8eda), (0x9da8, 0x9fac), (0xa870, 0xa9b0)];

    /// <summary>Words of the stack below the frames <see cref="StdioScratch"/> leaves out, which the deeper calls of the walk reach.</summary>
    private static readonly (int Start, int End)[] DeepStack = [(0x718d, 0x7191), (0x71ce, 0x71d0)];

    /// <summary>The address of the picture routines' open archive number in the data segment.</summary>
    private const int DataFileHandle = 0x449a;

    /// <summary>The address of <see cref="CampaignState.Txt"/> in the data segment.</summary>
    private const int TxtAddress = 0x7202;

    /// <summary>The functions and how each is called from a recorded case.</summary>
    private static readonly Dictionary<string, Func<Run, int?>> Functions = new()
    {
        // the map
        // the library's message box on GAME's copies (popup_menu is quit_prompt and restore_screen_area is
        // save_screen_area under other names), with GAME's places of the library's variables
        ["popup_menu"] = r => r.Menus.QuitPrompt(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2)),
        ["draw_menu_box"] = r => r.Menus.DrawMenuBox(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2), r.Arg(3)),
        ["CheckForDisk"] = r => r.Menus.CheckForDisk(r.Arg(0), r.Arg(1)),
        ["start_animation"] = r => r.Menus.StartAnimation(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["restore_screen_area"] = r => Nothing(() => r.Menus.SaveScreenArea(r.Arg(0))),
        ["map_distance"] = r => WorldRules.MapDistance(r.Arg(0), r.Arg(1)),
        ["building_plausible"] = r => WorldRules.BuildingPlausible(r.State, r.Arg(0), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["org_presence"] = r => WorldRules.OrgPresence(r.State, r.Arg(0), r.Arg(1)),
        ["raise_suspicion"] = r => Nothing(() => WorldRules.RaiseSuspicion(r.State, r.Arg(0), r.Arg(1), r.Arg(2))),

        // persons, buildings, events
        ["find_or_add_person_quiet"] = r => CastRules.FindOrAddPersonQuiet(r.State, r.Arg(0), r.Arg(1), r.Arg(2)),
        ["release_person"] = r => Nothing(() => CastRules.ReleasePerson(r.State, r.Arg(0))),
        ["find_or_add_link"] = r =>
        {
            var link = CampaignLibrary.AddLink(r.State.Lc, r.State.Ct, r.State.Nloc, r.Arg(0), r.Arg(1), r.Arg(2), out var count);
            r.State.Nloc = (short)count;
            return link;
        },
        ["clear_person_knowledge"] = r => Nothing(() => CastRules.ClearPersonKnowledge(r.State, r.Arg(0), r.Arg(1))),
        ["try_select_person"] = r => CastRules.TrySelectPerson(r.State, r.Arg(0)) ? 1 : 0,
        ["clear_event_steps"] = r => Nothing(() => CastRules.ClearEventSteps(r.State)),

        // texts (the functions that append to Txt return what strcat left in AX; the memory comparison covers them)
        ["append_person_name"] = r => Nothing(() => CampaignTexts.AppendPersonName(r.State, r.Arg(0))),
        ["append_full_name"] = r => Nothing(() => CampaignTexts.AppendFullName(r.State, r.Arg(0))),
        ["format_game_time"] = r => Nothing(() => CampaignTexts.FormatGameTime(r.State, r.Arg(0))),
        ["strip_spaces"] = r => Nothing(() => CampaignTexts.StripSpaces(TxtAt(r, 0))),
        ["replace_in_text_buffer"] = r => Nothing(() => CampaignTexts.ReplaceInTextBuffer(r.State, r.TextAt(r.Address(0)), r.TextAt(r.Address(1)))),
        ["replace_substrings"] = ReplaceSubstrings,
        ["append_clue_subject"] = r => Nothing(() => CampaignTexts.AppendClueSubject(r.State, r.Arg(0), r.Arg(1))),
        ["append_clue_label"] = r => Nothing(() => CampaignTexts.AppendClueLabel(r.State, r.Arg(0))),
        ["activity_phrase_index"] = r => CampaignLibrary.ClueKindIndex(r.Arg(0), r.Arg(1)),
        ["append_message_text"] = r => Nothing(() => CampaignTexts.AppendMessageText(r.State, r.Arg(0))),
        ["list_cities"] = r => Nothing(() => CampaignTexts.ListCities(r.State)),
        ["list_known_organizations"] = r => Nothing(() => CampaignTexts.ListKnownOrganizations(r.State)),

        // clues
        ["cast_clue_code"] = r => r.Screens.Clues.CastClueCode(r.Arg(0), r.Arg(1) != 0),
        ["append_clue_text"] = r => Nothing(() => r.Screens.Clues.AppendClueText(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["make_clue_subject"] = r => ClueRules.MakeClueSubject(r.State, r.Arg(0), r.Arg(1)),

        // the screen
        ["clear_screen"] = r => Nothing(r.Screens.ClearScreen),
        ["draw_title_plate"] = r => Nothing(() => r.Screens.Screens.DrawTitleBar(r.Arg(0), r.Arg(1), r.Arg(2))),     // the library's draw_title_bar
        ["show_activity_report"] = r => Nothing(r.Screens.ShowActivityReport),
        ["wait_for_input"] = r => r.Screens.Screens.WaitForKey(),     // the library's wait_for_key
        ["draw_place_clock"] = r => Nothing(r.Screens.DrawPlaceClock),
        ["show_bulletin"] = r => Nothing(() => r.Screens.Screens.ShowBulletin(r.TextAt(r.Address(0)), r.State.RpPic, r.State.BulletinColorMap, r.State.BannerShapes, r.State.StrBulletinCaption)),
        ["load_prose_text"] = LoadProseText,
        ["draw_map_city"] = r => Nothing(() => r.Screens.DrawMapCity(r.Arg(0), r.Arg(1))),
        ["show_personnel_file"] = r => Nothing(() => r.Screens.DataFiles.ShowPersonnelFile(r.Arg(0), r.Arg(1) != 0)),
        ["compose_message_text"] = r => Nothing(() => r.Screens.Messages.ComposeMessageText(r.Arg(0))),
        ["play_pan_picture"] = r => Nothing(() => r.Screens.PlayPanPicture(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2))),
        ["init_sprite_sheets"] = r => Nothing(() => r.LoadingPictures(r.Screens.InitSpriteSheets)),
        ["cut_face_sprites"] = r => Nothing(() => r.LoadingPictures(r.Screens.CutFaceSprites)),
        // the box runs the animation beside it, whose sprite sheets go through the picture loader
        ["popup_menu_on_clean_screen"] = r => Animating(r, () => r.Plot.PopupMenuOnCleanScreen(r.TextAt(r.Address(0)), r.Arg(1), r.Arg(2))),
        ["pass_time"] = r => Nothing(() => r.Plot.PassTime(r.Arg(0))),
        ["advance_world_clock"] = r => Nothing(() => r.Plot.AdvanceWorldClock(r.Arg(0), r.Arg(1) != 0)),
        ["queue_message"] = r => Nothing(() => r.Plot.QueueMessage(r.Arg(0), r.Arg(1))),
        ["event_ready"] = r => r.Plot.EventReady(r.Arg(0)),
        ["seize_plot_object"] = r => Nothing(() => r.Plot.SeizePlotObject(r.Arg(0))),
        ["find_or_create_person"] = r => r.Plot.FindOrCreatePerson(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3) != 0),
        ["announce_new_knowledge"] = r => Nothing(() => r.Plot.AnnounceNewKnowledge(r.Arg(0), r.Arg(1))),
        ["append_alert_level"] = r => r.Plot.AppendAlertLevel(r.Arg(0), r.Arg(1)),
        ["draw_person_dossier"] = r => Nothing(() => r.Screens.DataFiles.DrawPersonDossier(r.Arg(0), r.Arg(1), r.Arg(2))),
        ["show_message_decode"] = r => Nothing(() => r.Screens.Messages.ShowMessageDecode(r.Arg(0))),
        ["offer_prisoner_exchange"] = r => r.Plot.OfferPrisonerExchange() ? 1 : 0,
        ["organization_summary"] = r => Nothing(r.Menus2.OrganizationSummary),
        ["documents_menu"] = r => Nothing(r.Menus2.DocumentsMenu),
        ["suspects_menu"] = r => Nothing(r.Menus2.SuspectsMenu),
        ["clue_list_menu"] = r => r.Menus2.ClueListMenu(),
        ["show_wiretaps"] = r => Nothing(r.Menus2.ShowWiretaps),
        ["wait_in_hotel"] = r => Nothing(r.Plot.WaitInHotel),
        // these three run an animation beside their boxes: its sprite sheets go through the picture loader, as in the walk
        ["surrender_scene"] = r => Nothing(() => r.Walking(r.Plot.SurrenderScene)),
        ["interrogate_suspect"] = r => Nothing(() => r.Walking(() => r.Plot.InterrogateSuspect(r.Arg(0)))),
        ["add_clue"] = r => Nothing(() => r.Screens.AddClue(r.Arg(0), r.Arg(1), r.Arg(2), r.Arg(3))),
        // the library's reveal_cast_clue with GAME's clue screen
        ["add_random_clue"] = r => r.Screens.AddRandomClue(r.Arg(0), r.Arg(1), r.Arg(2)) ? 1 : 0,
        ["city_files_menu"] = r => Nothing(r.Menus2.CityFilesMenu),
        ["show_clues"] = r => Nothing(() => r.Screens.ClueLists.ShowClues(r.Arg(0))),
        ["show_clue_detail"] = r => Nothing(() => r.Screens.ClueLists.ShowClueDetail(r.Arg(0))),
        ["resolve_infiltration"] = r => Nothing(() => r.Walking(() => r.Plot.ResolveInfiltration(r.Arg(0), r.Arg(1)))),
        ["empty_stub"] = r => Nothing(CampaignScreens.EmptyStub),
        ["describe_event"] = r => Nothing(() => r.Screens.DescribeEvent(r.Arg(0))),
        ["animate_travel"] = r => r.Screens.AnimateTravel(r.Arg(0), r.Arg(1)),

        // the walk: the city screen, the hotel, the data files, the city's picture, the flight map, the whole part
        ["city_screen"] = r => Nothing(() => r.Walking(() => r.Cities.CityScreen(r.Arg(0)))),
        // the save screens' drive question: the library's, on GAME's copy of its variables and texts
        ["ask_save_drive"] = r => r.SaveScreens.AskSaveDrive(),
        ["hotel_menu"] = r => Nothing(() => r.Walking(r.Cities.HotelMenu)),
        ["cia_building_menu"] = r => Nothing(() => r.Walking(r.Cities.Buildings.Headquarters.CiaBuildingMenu)),
        ["search_yield"] = SearchYield,
        ["data_files_menu"] = r => Nothing(r.Menus2.DataFilesMenu),
        ["draw_city_picture"] = r => Nothing(() => r.Walking(() => r.Cities.DrawCityPicture(r.Arg(0), r.Arg(1), r.Arg(2)))),
        ["load_picture_measure"] = r => Nothing(() => r.Walking(() => r.Cities.LoadPictureMeasure(r.TextAt(r.Address(0))))),
        ["travel_menu"] = r => Nothing(() => r.Walking(r.Cities.TravelMenu)),
        ["open_data_file"] = OpenDataFile,
        ["close_data_file"] = r => Nothing(r.Cities.CloseDataFile),
        ["game_main"] = r => Nothing(() => r.WithCampaign().Walking(r.Part.GameMain)),
    };

    /// <summary>A function with a result that runs an animation: its sprite sheets go through the picture loader, as in the walk (<see cref="Run.Walking"/>).</summary>
    private static int? Animating(Run run, Func<int> call)
    {
        var result = 0;
        run.Walking(() => result = call());
        return result;
    }

    /// <summary>search_yield(org, city, strength, hint): the number of new facts it found is its result.</summary>
    private static int? SearchYield(Run run)
    {
        var found = 0;
        run.Walking(() => found = run.Cities.Buildings.Investigation.SearchYield(run.Arg(0), run.Arg(1), run.Arg(2), run.Arg(3)));
        return found;
    }

    /// <summary>open_data_file keeps the archive's number in the picture routines' own variable (DS:449a), compared here.</summary>
    private static int? OpenDataFile(Run run)
    {
        var opened = run.Cities.OpenDataFile(run.TextAt(run.Address(0)));
        Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(run.Case.DgroupOut.AsSpan(DataFileHandle)), run.Screens.Pictures.ArchiveHandle);
        run.ComparedByHand.Add((DataFileHandle, DataFileHandle + 2));
        return opened ? 1 : 0;
    }

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    /// <summary>The text an argument points at, which must be <see cref="CampaignState.Txt"/> (the only text the callers pass).</summary>
    private static TextBuffer TxtAt(Run run, int argument) =>
        run.Address(argument) == TxtAddress
            ? run.State.Txt
            : throw new OutsideModelException($"the text argument points at 0x{run.Address(argument):x4}, not at Txt");

    /// <summary>
    /// <c>load_prose_text(key, width, repl, variant)</c>: the key is a text of the caller's in the program's data (a tag
    /// literal), which the function may change; it is replayed as a buffer of its own and compared by hand.
    /// </summary>
    private static int? LoadProseText(Run run)
    {
        var address = run.Address(0);
        var key = new TextBuffer(run.TextAt(address).Length + 1) { Text = run.TextAt(address) };
        run.Screens.LoadProseText(key, run.Arg(1), run.TextAt(run.Address(2)), run.Arg(3));
        Assert.Equal(run.Case.DgroupOut.AsSpan(address, key.Bytes.Length).ToArray(), key.Bytes);
        run.ComparedByHand.Add((address, address + key.Bytes.Length));
        return null;
    }

    /// <summary>
    /// <c>replace_substrings(line, names, values, count)</c>: its only caller (<c>read_text_entry</c>) passes a line of 256
    /// bytes on its stack and two tables of text pointers. The line is replayed as a buffer of its own and compared by
    /// hand, because the stack is not part of the compared memory.
    /// </summary>
    private static int? ReplaceSubstrings(Run run)
    {
        const int lineBuffer = 256;
        var address = run.Address(0);
        var count = run.Arg(3);
        var line = new TextBuffer(lineBuffer);
        run.Case.DgroupIn.AsSpan(address, lineBuffer).CopyTo(line.Bytes);
        LibraryTexts.ReplaceTokens(line, run.TextTableAt(run.Address(1), count), run.TextTableAt(run.Address(2), count), count, run.State.ReplaceTail);
        Assert.Equal(run.Case.DgroupOut.AsSpan(address, lineBuffer).ToArray(), line.Bytes);
        run.ComparedByHand.Add((address, address + lineBuffer));
        return null;
    }

    /// <summary>
    /// The rules split off functions that also show boxes and bulletins (<see cref="PlotRules"/>), replayed on the
    /// recorded runs of those functions in the original's order. A run in which the original drew (a bulletin, the
    /// "log full" box, an announcement) needs the screens, which are not ported yet, and is set aside.
    /// </summary>
    private static readonly Dictionary<string, Func<Run, int?>> Rules = new()
    {
        ["queue_message"] = r => Nothing(() => PlotRules.QueueMessage(r.State, r.Arg(0), r.Arg(1))),
        ["event_ready"] = r =>
        {
            var check = PlotRules.EventReady(r.State, r.Arg(0));
            foreach (var person in check.OutOfHiding)
            {
                PlotRules.QueueMessage(r.State, PlotRules.MessageOutOfHiding, person);
            }
            return check.Result;
        },
        ["find_or_create_person"] = r => PlotRules.FindOrCreatePerson(r.State, r.Arg(0), r.Arg(1), r.Arg(2), out _),
        ["seize_plot_object"] = r => Nothing(() => PlotRules.SeizePlotObject(r.State, r.Arg(0))),
    };

    /// <summary>The functions whose rules are replayed, one test case each.</summary>
    public static IEnumerable<object[]> RuleNames => Rules.Keys.Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(RuleNames))]
    public void TheRulesDoWhatTheOriginalDid(string name)
    {
        var tally = OracleRun.ForEachCase(Module, name, oracleCase =>
        {
            if (oracleCase.Calls.Count > 0)
            {
                throw new OutsideModelException($"the original drew ({oracleCase.Calls.Count} calls): its screens are not ported");
            }
            var run = new Run(oracleCase);
            var result = Rules[name](run);
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == oracleCase.Ax, $"the C# returns {value}, the original {oracleCase.Ax}");
            }
            run.Finish();
        });
        output.WriteLine($"TALLY {name} (rules): {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }

    /// <summary>A key of <see cref="LibraryIssues"/> that stands for every case of the function.</summary>
    private const string AllCases = "*";

    /// <summary>
    /// Cases that fail in a routine of the library, not in GAME's code, with the reason; reported to the library's
    /// owners, and to be removed when the routine is fixed.
    /// </summary>
    private static readonly Dictionary<(string Function, string Case), string> LibraryIssues = new()
    {
    };

    /// <summary>The names of the functions under test, one test case each.</summary>
    public static IEnumerable<object[]> FunctionNames => Functions.Keys.Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(FunctionNames))]
    public void TheFunctionDoesWhatTheOriginalDid(string name)
    {
        var tally = OracleRun.ForEachCase(Module, name, oracleCase =>
        {
            if (LibraryIssues.TryGetValue((name, oracleCase.Id), out var issue) || LibraryIssues.TryGetValue((name, AllCases), out issue))
            {
                throw new OutsideModelException(issue);
            }
            var run = new Run(oracleCase);
            int? result;
            try
            {
                result = Functions[name](run);
            }
            catch (Msc.ProgramExitException) when (oracleCase.Exit is not null)
            {
                // a file could not be opened: the original names it and ends the program, and so does the C#
                run.World.ProgramEnded();
                return;
            }
            Assert.True(oracleCase.Exit is null, "the original ended the program, the C# went on");
            if (result is { } value)
            {
                Assert.True(unchecked((short)value) == oracleCase.Ax, $"the C# returns {value}, the original {oracleCase.Ax}");
            }
            run.Finish();
        });
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }

    /// <summary>
    /// Bytes of the data segment the original changes after its failed open of the animation file (the program's report
    /// of the missing file and the library's stream flags), which the C# does not reproduce: it ends at the open.
    /// </summary>
    private static readonly (int Start, int End)[] AfterTheFailedOpen = [(0x3934, 0x393b), (0x3995, 0x3997), (0x4f09, 0x4f15), (0x55dc, 0x55de)];

    /// <summary>
    /// start_case_briefing has no vector of the current generation: with the game's files served, its run (the plot's
    /// hidden turns, then the animation) no longer ends within the generator's budget. The older vector, made when no
    /// file was served, holds the same run up to the point where the original opens "briefing.pan", is told there is
    /// no such file, and ends the program (exit code 99). Everything before that point is the original's own: the
    /// hidden turns with their searches, boxes and random draws (some 2800 calls). That is what is compared here;
    /// the animation and the box after it are not (they follow the file open), and are the library's start_animation
    /// and popup_menu_on_clean_screen, each compared by itself.
    /// </summary>
    [SkippableFact]
    public void TheCaseBriefingRunsAsTheOriginalDidUpToTheMissingAnimationFile()
    {
        var path = Path.Combine(OracleVectors.Folder, Module, "_stale", "sub_game_00f7_3b76.json");
        Skip.IfNot(File.Exists(path), $"no older oracle vector for GAME start_case_briefing under {path}");
        var vector = OracleVectors.Load(path);
        var compared = 0;
        foreach (var oracleCase in vector.Cases)
        {
            Assert.True(oracleCase.Exit is not null, "the case was expected to end at the missing file");
            var run = new Run(oracleCase);
            try
            {
                run.Walking(run.Part.Briefing.StartCaseBriefing);
                Assert.Fail("the C# went on past the animation file the original could not open");
            }
            catch (Msc.ProgramExitException)
            {
                run.World.ProgramEnded();
            }
            // what the original wrote after the failed open, in its error report and the library's stream flags
            run.ComparedByHand.AddRange(AfterTheFailedOpen);
            run.Finish();
            compared++;
        }
        output.WriteLine($"TALLY start_case_briefing (older vector, to the missing animation file): {compared} cases agree");
    }

    // ---- saving the campaign: to a file, and into the hand-off block ----

    /// <summary>The save routines; the hand-off block is compared after each.</summary>
    private static readonly Dictionary<string, Action<Run>> Saves = new()
    {
        ["save_game"] = r => r.Saves.SaveGame(r.TextAt(r.Address(0))),
        ["write_table"] = WriteTable,
        ["save_and_hand_off"] = r => r.LoadingPictures(r.Plot.SaveAndHandOff),
        ["load_game"] = r => r.WithCampaign().Saves.LoadGame(r.TextAt(r.Address(0)), r.Plot.PopupMenuOnCleanScreen),
        // the save screens (the library's, with GAME's load and save routines and box): they load and save games
        ["select_load_file"] = r => _ = r.WithCampaign().SaveScreens.SelectLoadFile(),
        ["list_save_slot"] = r => _ = r.WithCampaign().SaveScreens.ListSaveSlot(r.Arg(1) != 0 || r.Arg(2) != 0) ? 1 : 0,
        ["select_save_file"] = r => r.SaveScreens.SelectSaveFile(),
        ["save_to_named_file"] = r => _ = r.Saves.SaveToNamedFile(r.TextAt(r.Address(0)), r.Plot.PopupMenuOnCleanScreen),
        // not a save, but it writes the message to decode into the hand-off block, which is compared here
        ["coded_messages_menu"] = r => r.Menus2.CodedMessagesMenu(),
    };

    /// <summary>write_table hands on the bytes at its second argument; into the hand-off block they go as they are.</summary>
    private static void WriteTable(Run r)
    {
        var length = (ushort)r.Arg(2);
        if (r.Address(1) + length > r.Case.DgroupIn.Length)
        {
            throw new OutsideModelException("a table that runs past the end of the data segment");
        }
        var before = (ushort)r.State.SaveMemOffset;
        r.Saves.Chunks.WriteChunk(r.Arg(0), r.Case.DgroupIn.AsSpan(r.Address(1), length));
        if (before != 0)
        {
            // the typed hand-off is filled by save_game as records; here the original's copy is checked by itself
            Assert.True(before + length <= SharedState.Size, "the table runs past the hand-off block (docs/original-bugs.md CA-6)");
            Assert.True(r.Case.SharedOut.AsSpan(before, length).SequenceEqual(r.Case.DgroupIn.AsSpan(r.Address(1), length)), "the original put other bytes into the hand-off block");
            Assert.True((ushort)r.State.SaveMemOffset == before + length, "the place in the hand-off block did not move on by the table's length");
        }
    }

    /// <summary>The names of the save routines.</summary>
    public static IEnumerable<object[]> SaveNames => Saves.Keys.Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(SaveNames))]
    public void TheSavesDoWhatTheOriginalDid(string name)
    {
        var tally = OracleRun.ForEachCase(Module, name, oracleCase =>
        {
            if (oracleCase.SharedIn is null || oracleCase.SharedOut is null)
            {
                throw new OutsideModelException("the case has no hand-off block");
            }
            var run = new Run(oracleCase);
            Saves[name](run);
            if (name is "coded_messages_menu" or "load_game" or "select_load_file" or "list_save_slot" or "select_save_file" or "save_to_named_file")
            {
                if (name != "coded_messages_menu")
                {
                    run.ComparedByHand.Add((0x55ae, 0x55b2));     // the far pointer the head is moved through (or the table bytes)
                }
                run.FinishHandOffHead();
            }
            else if (name is "save_game" or "save_and_hand_off")
            {
                run.FinishSave();
            }
            else
            {
                run.ComparedByHand.Add((0x55ae, 0x55b2));     // the far pointer the bytes are moved through
                run.Finish();
            }
        });
        output.WriteLine($"TALLY {name}: {tally.Compared} cases agree, {tally.OutsideModel.Count} outside the typed model");
        foreach (var line in tally.OutsideModel)
        {
            output.WriteLine($"OUTSIDE {name} {line}");
        }
    }
}
