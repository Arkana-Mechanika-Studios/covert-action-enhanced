using CovertAction.Core.Animation;
using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Final;

/// <summary>
/// The state of the menus part (the original's FINAL program): the title screen and main menu, the creation of the
/// agent, the briefing that sets up a new case, the debriefing that rates a finished one, the agent's career and the
/// Hall of Fame. It holds this part's copy of the campaign tables, the case being set up, the tables and texts of the
/// program, and the variables of the routines this part shares with the rest of the game.
/// </summary>
/// <remarks>
/// <para>
/// The campaign tables are those of every part (<see cref="CampaignTables"/>), but FINAL keeps them in fixed arrays
/// of its data segment: room for 32 members of the cast and 64 events, of which <see cref="NACTIVITY"/> and
/// <see cref="NEVENT"/> are in use.
/// </para>
/// <para>
/// Names are those of the reconstructed final.h and of the FINAL sources (MicroProse's own where the Amiga symbols give
/// them: <c>ps</c>, <c>lc</c>, <c>ct</c>, <c>org</c>, <c>ac</c>, <c>ev</c>, <c>sn</c>, <c>pl</c>, <c>Nperson</c>,
/// <c>Nloc</c>, <c>NextSyn</c>, <c>NACTIVITY</c>, <c>NEVENT</c>, <c>diff</c>, <c>TheOrg1</c> ...), in C#
/// capitalisation. Several variables of the original are fields of the career record <see cref="Pl"/> under other
/// names: <c>big_org</c> is <c>Pl.BigOrg</c>, <c>big_city</c> <c>Pl.BigLoc</c>, <c>big_look</c> <c>Pl.BigFace</c>,
/// <c>big_known</c> <c>Pl.Clues</c>, <c>career_months</c> <c>Pl.Ncase</c>. Nothing here has a built-in value: the
/// tables and texts are the initialised data of the player's FINAL.EXE.
/// </para>
/// </remarks>
// orig: FINAL DGROUP (src/c/include/final.h, docs/data-model/final.md)
public sealed partial class FinalState : ILibraryState, ILibraryTextState, ILibraryScreenState, ICampaignTables
{
    /// <summary>Room for members of the cast in <see cref="Ac"/> and <see cref="Ac2"/>.</summary>
    public const int CastCapacity = 32;

    /// <summary>Room for events in <see cref="Ev"/>.</summary>
    public const int EventCapacity = 64;

    /// <summary>The 26 MasterMinds a case can be led by.</summary>
    public const int MastermindCount = 26;

    /// <summary>Words of one row of <see cref="MastermindLooks"/>.</summary>
    public const int MastermindLookWords = 8;

    /// <summary>Places in the Hall of Fame.</summary>
    public const int FameEntries = 6;

    /// <summary>Months of the year: the career's months are named round them.</summary>
    public const int MonthCount = 12;

    /// <summary>Bytes of <see cref="ClockText"/>: "hh:mm AM Mon dd" and its end mark.</summary>
    public const int ClockTextLength = 16;

    /// <summary>Bytes of the text buffer <see cref="Txt"/>.</summary>
    public const int TxtLength = 1024;

    /// <summary>The two port pointers point at their records, as in the program's initialised data.</summary>
    public FinalState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
        Pan = new PanEngineState(Images);
        // the dossier's number buffer is the one digits_msg is: the same bytes of the data segment
        Dossier = new DossierTexts { NumberText = DigitsMsgBuffer };
    }

    // ---- the screen and the routines shared with the other parts ----

    /// <summary>FINAL DS:4b46: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>FINAL DS:155e: the drawing port record of the off-screen page kept under boxes.</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>FINAL DS:4b58 Rp1: the port all drawing of this part goes through; in the running game <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>FINAL DS:1570 RpBack: the port the screen is saved to and restored from; in the running game <see cref="RpBackX"/>.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>FINAL DS:8090 Status (MicroProse's name): this part's copy of the player's options (sound, animation, a female agent ...).</summary>
    public Options Status { get; set; }

    /// <summary>FINAL DS:abf8 quit_code: what the main loop does after a box (0 go on, -1 leave the game).</summary>
    public short Quit { get; set; }

    /// <summary>The first colour table handed to the graphics driver with a box. FINAL DS:1592 (the place is in Persistence.Layout.LibraryLayout).</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>The second colour table, FINAL DS:15a2.</summary>
    public byte[] ColorMapB { get; } = new byte[16];

    /// <summary>FINAL DS:79e2 Txt (MicroProse's name): the text buffer in which every box, report and message is put together.</summary>
    public TextBuffer Txt { get; } = new(TxtLength);

    /// <summary>The library's name for <see cref="Txt"/>.</summary>
    TextBuffer ILibraryState.MessageText => Txt;

    /// <summary>The message box's variables. Their places in FINAL's data are in Persistence.Layout.LibraryLayout.</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The variables of the animation beside a box. Their places in FINAL's data are in Persistence.Layout.LibraryLayout.</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The variables of the insert-disk prompt. Their places in FINAL's data are in Persistence.Layout.LibraryLayout.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>The joystick and mouse variables. Their places in FINAL's data are in Persistence.Layout.LibraryLayout.</summary>
    public InputState Input { get; } = new();

    /// <summary>FINAL DS:5a1e: this part's random generator (its state is the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    // ---- this part's copy of the campaign ----

    /// <summary>FINAL DS:0188 ps: the 64 persons. A record whose look word (<see cref="Person.Face"/>) is 0 is free; person 0 is the MasterMind.</summary>
    public Person[] Ps { get; } = Records<Person>(CampaignTables.PersonCount);

    /// <summary>FINAL DS:0588 lc: the buildings, one per organisation and city the player knows of; <see cref="Nloc"/> are in use.</summary>
    public Location[] Lc { get; } = Records<Location>(CampaignTables.LocationCount);

    /// <summary>FINAL DS:1188 cl: the clues.</summary>
    public Clue[] Cl { get; } = Records<Clue>(CampaignTables.ClueCount);

    /// <summary>FINAL DS:8294 ct: the world's 16 cities.</summary>
    public City[] Ct { get; } = Records<City>(CampaignTables.WorldSize);

    /// <summary>FINAL DS:a7be org: the world's 16 organisations. Organisation 1 is the agent's own side (never a suspect).</summary>
    public Org[] Org { get; } = Records<Org>(CampaignTables.WorldSize);

    /// <summary>FINAL DS:9fa6 ac: the cast of the case, as read from its crime file; <see cref="NACTIVITY"/> are in use.</summary>
    public Activity[] Ac { get; } = Records<Activity>(CastCapacity);

    /// <summary>FINAL DS:ad6c ac2: the cast's second records, in the same order.</summary>
    public Activity2[] Ac2 { get; } = Records<Activity2>(CastCapacity);

    /// <summary>FINAL DS:8bca ev: the events of the plot; <see cref="NEVENT"/> are in use.</summary>
    public Event[] Ev { get; } = Records<Event>(EventCapacity);

    /// <summary>FINAL DS:84b8 sn: the case log, one record per message or sighting; <see cref="NextSyn"/> are in use.</summary>
    public Synopsis[] Sn { get; } = Records<Synopsis>(CampaignTables.SynopsisCount);

    /// <summary>FINAL DS:ac20 mc: the objects of the plot.</summary>
    public MacGuffins[] Mc { get; } = Records<MacGuffins>(CampaignTables.MacGuffinCount);

    /// <summary>
    /// FINAL DS:7df2 pl: the head of the campaign, which in this part is the agent's career: the plot and its variant,
    /// the MasterMind, the points and months of the career, the code name and the record of the cases played.
    /// </summary>
    public Records.Plot Pl { get; } = new();

    /// <summary>FINAL DS:84b6 Nperson: the person records in use; every search looks at the first this many.</summary>
    public short Nperson { get; set; }

    /// <summary>FINAL DS:96d0 Nloc: the buildings in use in <see cref="Lc"/>.</summary>
    public short Nloc { get; set; }

    /// <summary>FINAL DS:96ca NextSyn: the records in use in the case log <see cref="Sn"/>.</summary>
    public short NextSyn { get; set; }

    /// <summary>FINAL DS:9fa2 NACTIVITY: the members of the cast.</summary>
    public short NACTIVITY { get; set; }

    /// <summary>FINAL DS:9e88 NEVENT: the events of the plot.</summary>
    public short NEVENT { get; set; }

    /// <summary>FINAL DS:7de6 diff: the level of play, 0 (the easiest) to 3.</summary>
    public short Diff { get; set; }

    // ---- the case being set up: the organisations and cities the plot is laid out around ----

    /// <summary>FINAL DS:9e94 TheOrg1: the first organisation of the plot; the others are chosen near it on the map.</summary>
    public short TheOrg1 { get; set; }

    /// <summary>FINAL DS:9e98 TheOrg2: the second organisation of the plot.</summary>
    public short TheOrg2 { get; set; }

    /// <summary>FINAL DS:abf6 TheOrgF: the third organisation of the plot.</summary>
    public short TheOrgF { get; set; }

    /// <summary>FINAL DS:96da TheCity1: the first city of the plot; the others are chosen at a distance from it.</summary>
    public short TheCity1 { get; set; }

    /// <summary>FINAL DS:96dc TheCity2: the second city of the plot.</summary>
    public short TheCity2 { get; set; }

    /// <summary>FINAL DS:ac1e TheCityF: the third city of the plot.</summary>
    public short TheCityF { get; set; }

    // (the campaign's variables a new case starts afresh, InCity ... OrgCounters, are in FinalState.HandOff.cs)

    /// <summary>FINAL DS:3010 red_herring_role: the role of the cast members added at the higher levels ("Red Herring").</summary>
    public string RedHerringRole { get; set; } = "";

    // ---- the tables of the program ----

    /// <summary>
    /// FINAL DS:1cfe mastermind_looks: the faces of the 26 MasterMinds, eight words each: [0] 1 for a woman, [1] the
    /// head variant, [2..5] the four face parts (1..8), [6] the colour shade, [7] the clothes picture.
    /// </summary>
    public short[][] MastermindLooks { get; } = Table(MastermindCount, MastermindLookWords);

    /// <summary>FINAL DS:b1c0 fame_table: the Hall of Fame, best first (FAME.DTA holds the same bytes).</summary>
    public FameEntry[] FameTable { get; } = Records<FameEntry>(FameEntries);

    // ---- the clock of the reports ----

    /// <summary>
    /// FINAL DS:2a0a clock_text: a date and time as "hh:mm AM Mon dd", written by <c>format_game_time</c> over the
    /// template it holds. A table of characters with its end mark, because the time is written into it by position.
    /// </summary>
    public char[] ClockText { get; } = new char[ClockTextLength];

    /// <summary>FINAL DS:2a05 clock_day_text: what <c>format_game_time</c> puts behind the month's name (a blank and two digits of the template).</summary>
    public string ClockDayText { get; set; } = "";

    /// <summary>FINAL DS:367c month_names: the names of the twelve months (through a table of pointers to the texts at DS:3546).</summary>
    public string[] MonthNames => Dossier.Words.MonthNames;

    /// <summary>FINAL DS:9bea game_minute: the minute of the hour of the time last formatted.</summary>
    public short GameMinute { get; set; }

    /// <summary>FINAL DS:9e82 game_hour: the hour of the day (0..23) of the time last formatted.</summary>
    public short GameHour { get; set; }

    /// <summary>FINAL DS:7dea game_day: the day of the month (from 1) of the time last formatted.</summary>
    public short GameDay { get; set; }

    // ---- the display ----

    /// <summary>FINAL DS:8092 CGA: the four-colour display is in use (frames are drawn in colour 3, no highlights).</summary>
    public bool CGA { get; set; }

    // ---- the roster of the debriefing: one cell of 80 x 52 pixels per member of the cast, four to a row ----

    /// <summary>FINAL DS:b30c n_roster: the members of the cast shown in the roster.</summary>
    public short NRoster { get; set; }

    /// <summary>FINAL DS:2a03: what parts a role's name into the two lines of its roster label (a blank).</summary>
    public string RosterLabelSeparator { get; set; } = "";

    // ---- texts ----

    /// <summary>FINAL DS:3fdc: a line break, as a text.</summary>
    public string StrNewline { get; set; } = "";

    // ---- the files of a case ----

    /// <summary>Bytes of <see cref="CrimeFileName"/> and <see cref="WorldFileName"/>.</summary>
    public const int FileNameLength = 12;

    /// <summary>The place of the number in <see cref="CrimeFileName"/> and <see cref="WorldFileName"/> ("crime0", "world1").</summary>
    public const int FileNumberAt = 5;

    /// <summary>FINAL DS:3276 crime_file_name: "crime0.dta", whose number is written in by position (one or two digits).</summary>
    public char[] CrimeFileName { get; } = new char[FileNameLength];

    /// <summary>FINAL DS:326e str_ten_dta: "10.dta", the end of the name of a crime file with a two-digit number.</summary>
    public string StrTenDta { get; set; } = "";

    /// <summary>FINAL DS:3282 world_file_name: "world1.dta", whose number is written in by position.</summary>
    public char[] WorldFileName { get; } = new char[FileNameLength];

    // ---- the editing of a text field (the agent's code name) ----

    /// <summary>FINAL DS:5cb6 willy: the place of the cursor in the text being edited.</summary>
    public short Willy { get; set; }

    /// <summary>FINAL DS:5cba willyX: the left edge, in pixels, of the character under the cursor, as last drawn.</summary>
    public short WillyX { get; set; }

    /// <summary>FINAL DS:5cb8 willyW: the width, in pixels, of the character under the cursor, as last drawn.</summary>
    public short WillyW { get; set; }

    // ---- the animation beside the briefing ----

    /// <summary>Plots in <see cref="Plots"/>.</summary>
    public const int PlotCount = 16;

    /// <summary>FINAL DS:1e9e plots: the sixteen plots a case can be a variant of; plot 0 is the training case.</summary>
    public PlotDefinition[] Plots { get; } = [.. Enumerable.Range(0, PlotCount).Select(_ => new PlotDefinition())];

    /// <summary>
    /// The variables of the animation player (the library's: the campaign has the same player). FINAL DS:4ad8 to
    /// 4b45, DS:5e20 to 5e53 and others; their places are in Persistence.Layout.PanEngineLayout.
    /// </summary>
    public PanEngineState Pan { get; }

    /// <summary>Words of <see cref="ScriptVars"/>.</summary>
    public const int ScriptVarCount = PanEngineState.ScriptVarCount;

    /// <summary>
    /// FINAL DS:b30e script_vars: values the game hands to the animation's program (which scene to play, what the
    /// briefing shows) and gets back; 51 words, the player's own (<see cref="PanEngineState.ScriptVars"/>).
    /// </summary>
    public short[] ScriptVars => Pan.ScriptVars;

    /// <summary>FINAL DS:4b3c animation_screen: the screen value of the animation (which page it is played on).</summary>
    public short AnimationScreen
    {
        get => Pan.AnimationScreen;
        set => Pan.AnimationScreen = value;
    }

    /// <summary>FINAL DS:5e2e animation_steps: the steps the animation has played since it started; it can be skipped from the third on.</summary>
    public short AnimationSteps
    {
        get => Pan.AnimationSteps;
        set => Pan.AnimationSteps = value;
    }

    /// <summary>FINAL DS:ae30 ignore_skip: the next key that would skip the animation is to be ignored once.</summary>
    public bool IgnoreSkip
    {
        get => Pan.IgnoreSkip != 0;
        set => Pan.IgnoreSkip = (short)(value ? 1 : 0);
    }

    /// <summary>FINAL DS:b31a skip_key_state: set to -1 when a skip key was ignored. It is the seventh of the animation program's variables (script_vars[6]).</summary>
    public short SkipKeyState
    {
        get => Pan.ScriptVars[PanEngineState.KeyPressedVariable];
        set => Pan.ScriptVars[PanEngineState.KeyPressedVariable] = value;
    }

    // ---- the clue texts ----

    /// <summary>Kinds of clue object in <see cref="ClueLabels"/>.</summary>
    public const int ClueLabelKinds = 8;

    /// <summary>Labels of each kind in <see cref="ClueLabels"/>.</summary>
    public const int ClueLabelsPerKind = 8;

    /// <summary>
    /// FINAL DS:3b00 clue_labels: the words that name a clue's object, by its kind (a vehicle, a weapon, an address,
    /// a ticket, a telegram, a sum of hundreds, a sum of thousands, an identity) and one of eight variants; a number
    /// follows them. Through a table of pointers to the texts at DS:3875.
    /// </summary>
    public string[][] ClueLabels { get; } = [.. Enumerable.Range(0, ClueLabelKinds).Select(_ => new string[ClueLabelsPerKind])];

    /// <summary>FINAL DS:5d1c digits_msg: the buffer in which a number of a clue text is written out.</summary>
    public string DigitsMsg
    {
        get => DigitsMsgBuffer.Text;
        set => DigitsMsgBuffer.Text = value;
    }

    /// <summary>The bytes of <see cref="DigitsMsg"/>: the library's dossier writes its day numbers into them too.</summary>
    public TextBuffer DigitsMsgBuffer { get; } = new(DigitsMsgLength);

    /// <summary>Bytes of <see cref="DigitsMsgBuffer"/> (DS:5d1c up to the message tag behind it).</summary>
    public const int DigitsMsgLength = 0x20;

    /// <summary>FINAL DS:3ca2 str_suffix_2: what follows the number of a sum of hundreds ("00").</summary>
    public string StrSuffix2 { get; set; } = "";

    /// <summary>FINAL DS:3ca5 str_suffix_3: what follows the number of a sum of thousands ("000").</summary>
    public string StrSuffix3 { get; set; } = "";

    // ---- the library's screens: faces, photos, report frames (ILibraryScreenState) ----

    /// <summary>Entries of <see cref="SpriteTable"/>.</summary>
    public const int SpriteCount = 0x78;

    /// <summary>The images this part holds, by the place the original gave each in memory.</summary>
    public ImageRegister Images { get; } = new();

    /// <summary>
    /// FINAL DS:9e9c image_table: the part's images: face parts (female 0..39, male 40..79), the tiles and strips of the
    /// screens, the clothes (0x68.. and 0x70..) and the icons of the plot's objects.
    /// </summary>
    public Image?[] SpriteTable { get; } = new Image?[SpriteCount];

    /// <summary>The library's name for <see cref="SpriteTable"/>.</summary>
    IReadOnlyList<Image?> ILibraryScreenState.Sprite => SpriteTable;

    /// <summary>FINAL DS:ae2c GMode (MicroProse's name): the graphics mode letter the game was started with ('C' for CGA, 'M' for MCGA).</summary>
    public sbyte GMode { get; set; }

    /// <summary>FINAL DS:8bb8 pending_portrait: the face shown beside the next box; -1 for none. The library's box keeps it as its backdrop code.</summary>
    public short Picface
    {
        get => Animation.BackdropCode;
        set => Animation.BackdropCode = value;
    }

    /// <summary>FINAL DS:1612 cga_map_faces: the colour table the driver uses while a face or photo is drawn.</summary>
    public byte[] PhotoColorMap { get; } = new byte[16];

    // ---- the library's text readers (ILibraryTextState) ----

    /// <summary>FINAL DS:ac6c macro_scratch: the placeholder routine's scratch buffer.</summary>
    public TextBuffer TokenTail { get; } = new(ILibraryTextState.LineCapacity);

    /// <summary>FINAL DS:3da8.. (through the pointers of macro_tokens' neighbour at DS:3eca): "$VICTIM" ... "$OBJECT".</summary>
    public string[] VariableNameTexts { get; } = new string[ILibraryTextState.MessageVariables];

    /// <summary>FINAL DS:3de0.. (through the pointers of macro_values at DS:3ed8): the placeholders' current values.</summary>
    public string[] VariableValueTexts { get; } = new string[ILibraryTextState.MessageVariables];

    /// <inheritdoc/>
    IReadOnlyList<string> ILibraryTextState.VariableNames => VariableNameTexts;

    /// <inheritdoc/>
    IList<string> ILibraryTextState.VariableValues => VariableValueTexts;

    /// <summary>FINAL DS:3f00 str_clearance: "This information requires security clearance: ".</summary>
    public string ClearanceNotice { get; set; } = "";

    /// <summary>FINAL DS:3ef7: what the readers put behind every line they copy (a blank).</summary>
    public string LineSeparator { get; set; } = "";

    // ---- the Hall of Fame's file ----

    /// <summary>FINAL DS:328e, DS:3297, DS:32a0, DS:32a9: the name of the Hall of Fame's file, as tested and opened for reading, as tested and opened for writing ("fame.dta" each).</summary>
    public string[] FameFileNames { get; } = new string[4];

    // ---- the training screen ----

    /// <summary>The agent's four skills.</summary>
    public const int SkillCount = 4;

    /// <summary>FINAL DS:24a8 skill_colors: the colour of each skill's rating box.</summary>
    public short[] SkillColors { get; } = new short[SkillCount];

    /// <summary>FINAL DS:2488, DS:2490, DS:2495, DS:249f: the names of the four ratings of a skill ("Average" ... "Awesome").</summary>
    public string[] RatingNames { get; } = new string[SkillCount];

    /// <summary>FINAL DS:247e skill_names: the four skills, Combat, Driving, Cryptography, Electronics (through a table of pointers to the texts at DS:2427).</summary>
    public string[] SkillNames { get; } = new string[SkillCount];


    /// <summary>FINAL DS:ac1c menu_selection: the item a box opens on, and in the practice menu the skill chosen (the library's <see cref="MenuState.MenuStartItem"/>).</summary>
    public short MenuSelection
    {
        get => Menu.MenuStartItem;
        set => Menu.MenuStartItem = value;
    }

    /// <summary>FINAL DS:23c8: the question of the level of play and its four answers ("Which difficulty level?\n Local Disturbance\n ...").</summary>
    public string StrWhichLevel { get; set; } = "";

    /// <summary>FINAL DS:2457: the training screen's picture ("training.pic").</summary>
    public string TrainingPicture { get; set; } = "";

    /// <summary>FINAL DS:2464, DS:2466, DS:2472: the start of the skills menu (a blank) and what follows a skill's name in the practice and the training menu (" practice\n ", " training\n ").</summary>
    public string[] StrSkillMenu { get; } = ["", "", ""];

    // ---- the messages of the plot's events ----

    /// <summary>FINAL DS:9e90 TheCrime: the number of the case's crime file, which the message tags carry.</summary>
    public short TheCrime { get; set; }

    /// <summary>FINAL DS:3fb4: the name of the file of message texts ("text.dta").</summary>
    public string TextFileName
    {
        get => Dossier.MessageFile;
        set => Dossier.MessageFile = value;
    }

    /// <summary>FINAL DS:3fbd: the template of a message's tag ("*MSG0000"), whose digits are the crime and the message.</summary>
    public string MessageTemplate
    {
        get => Dossier.MessageTemplate;
        set => Dossier.MessageTemplate = value;
    }

    /// <summary>FINAL DS:5d3c text_tag: the tag of the message being read, built from <see cref="MessageTemplate"/>.</summary>
    public TextBuffer MessageTag => Dossier.MessageTag;

    // ---- the case log ----

    /// <summary>FINAL DS:7df0 Gtime (MicroProse's name): the campaign's clock, in minutes since the case began.</summary>
    public short Gtime { get; set; }

    /// <summary>FINAL DS:4710 str_log_overflow: the warning shown when the case log is full.</summary>
    public string StrLogOverflow { get; set; } = "";

    // ---- the roster's pictures ----

    /// <summary>FINAL DS:28ac work_portX: the drawing port record of the page the icons of the plot's objects are copied from.</summary>
    public RasterPort WorkPortX { get; } = new();

    // ---- the choice of the next case ----

    /// <summary>FINAL DS:28c0 plot_tag: the tag of the plot's text in PLOT.TXT ("*PLnnvp": plot, variant, page), whose digits are written in by position.</summary>
    public TextBuffer PlotTag { get; } = new(8);

    /// <summary>FINAL DS:233e: the template of the tag of a plot's first briefing ("*PL0090").</summary>
    public string StrPlotBriefingTag { get; set; } = "";

    /// <summary>FINAL DS:2346: the briefing's animation file ("briefing.pan"), as the choice of the next case names it.</summary>
    public string CaseBriefingPanFile { get; set; } = "";

    /// <summary>FINAL DS:2353 plot_txt_name_b: the file of the plots' texts ("plot.txt").</summary>
    public string PlotTextFile { get; set; } = "";

    /// <summary>FINAL DS:ae32 briefing_shown: a case's first briefing has been shown.</summary>
    public bool BriefingShown { get; set; }

    /// <summary>FINAL DS:235c, DS:2376: the case's two objects for a variant 7 of a plot ("Max Remington's apartment", "a mattress full of cash").</summary>
    public string[] ApartmentItems { get; } = ["", ""];

    /// <summary>FINAL DS:238e, DS:2396: the templates of the tag of a plot variant's briefing, with its pages lettered A, B, ... for the first two variants while both are open, a, b, ... otherwise ("*PL000A", "*PL000a").</summary>
    public string[] PlotVariantTags { get; } = ["", ""];

    /// <summary>FINAL DS:239e apartment_pan_name: the animation behind the briefing of a plot's variant ("briefing.pan").</summary>
    public string ApartmentPanFile { get; set; } = "";

    /// <summary>FINAL DS:23ab plot_txt_name: the file of the plots' texts ("plot.txt"), as the variant's briefing names it.</summary>
    public string ApartmentTextFile { get; set; } = "";

    /// <summary>FINAL DS:ae7a Sleepy (MicroProse's name): a campaign counter a new case starts at 0 (the hand-off block's).</summary>
    public short Sleepy { get; set; }

    /// <summary>FINAL DS:96d6 VP (MicroProse's name): the plot's points a new case starts at 0 (the hand-off block's).</summary>
    public short VP { get; set; }

    // ---- the cipher practice ----

    /// <summary>FINAL DS:1674: the hand-off's name ("env.sve"), as the start of a practice mission gives it.</summary>
    public string PracticeHandOffFile { get; set; } = "";

    /// <summary>The parts of a practice message: where it is sent from and to, by whom and to whom, and the rest.</summary>
    public const int PracticeMessageParts = 5;

    /// <summary>
    /// FINAL DS:167d, DS:168e, DS:169f, DS:16b0, DS:16c1: the tags of the practice message's parts in TEXT.DTA ("*SLOC00",
    /// "*RLOC00", "*SORG00", "*RORG00", "*FLUF00"), whose two digits the text reader overwrites with the variant drawn.
    /// </summary>
    public TextBuffer[] PracticeTags { get; } = [.. Enumerable.Range(0, PracticeMessageParts).Select(_ => new TextBuffer(8))];

    /// <summary>FINAL DS:1685, DS:1696, DS:16a7, DS:16b8, DS:16c9: the name of the file each part is read from ("text.dta" each).</summary>
    public string[] PracticeTagFiles { get; } = new string[PracticeMessageParts];

    // ---- the code name ----

    /// <summary>FINAL DS:23b4: the prompt of the code name's box ("Max's code name is:").</summary>
    public string StrCodeNamePrompt { get; set; } = "";

    /// <summary>FINAL DS:1622 cga_map_code_name: the colour table the driver uses while the code name's box is drawn.</summary>
    public byte[] CodeNameColorMap { get; } = new byte[16];

    // ---- the case's action log ----

    /// <summary>FINAL DS:28c8 to DS:29f7: the texts of the action log (<see cref="LogText"/> names them).</summary>
    public string[] LogTexts { get; } = new string[(int)LogText.Count];

    // ---- the career summary ----

    /// <summary>FINAL DS:2c91 to DS:2d47: the texts of the career summary (<see cref="CareerText"/> names them).</summary>
    public string[] CareerTexts { get; } = new string[(int)CareerText.Count];

    /// <summary>FINAL DS:5cdc digits_career: the buffer in which the career summary writes out its numbers (what a longer number left behind the end mark stays).</summary>
    public TextBuffer DigitsCareer { get; } = new(16);

    /// <summary>FINAL DS:ac68 masterminds_caught: the MasterMinds the agent has arrested, counted on the career summary's gallery; 26 ends the career.</summary>
    public short MastermindsCaught { get; set; }

    // ---- the Hall of Fame screen ----

    /// <summary>The crimes of the crime files, CRIME0.DTA to CRIME12.DTA.</summary>
    public const int CrimeCount = 13;

    /// <summary>FINAL DS:2d5c crime_names: the name of each crime file's crime (through a table of pointers to the texts at DS:2afc).</summary>
    public string[] CrimeNames { get; } = new string[CrimeCount];

    /// <summary>FINAL DS:5d0c digits_fame: the buffer in which the Hall of Fame writes out its numbers (the last one stays). (what a longer number left behind the end mark stays)</summary>
    public TextBuffer DigitsFame { get; } = new(16);

    /// <summary>FINAL DS:32b2 to DS:331e: the texts of the Hall of Fame screen (<see cref="FameText"/> names them).</summary>
    public string[] FameTexts { get; } = new string[(int)FameText.Count];

    // ---- the vacation after a case ----

    /// <summary>Rows of <see cref="VacationTable"/>, and words of a row.</summary>
    public const int VacationRows = 21, VacationRowWords = 5;

    /// <summary>The places of a vacation: the LaundroMat, the secretarial pool, the beach, the casino.</summary>
    public const int VacationPlaces = 4;

    /// <summary>
    /// FINAL DS:2f3e vacation_table: by how well the case went (0 the worst), the place of the vacation ([0], 0..3) and
    /// the numbers of the up to four people who come along ([1..4], -1 for none).
    /// </summary>
    public short[][] VacationTable { get; } = Table(VacationRows, VacationRowWords);

    /// <summary>FINAL DS:2f36 vacation_places: the prefixes of the four places' picture names (through a table of pointers to the texts at DS:2dd6).</summary>
    public string[] VacationPlaceNames { get; } = new string[VacationPlaces];

    /// <summary>FINAL DS:2de6 to DS:2f30: the vacation's captions and file names (<see cref="VacationText"/> names them).</summary>
    public string[] VacationTexts { get; } = new string[(int)VacationText.Count];

    /// <summary>FINAL DS:5cec digits_vacation: the buffer in which the vacation writes out a picture's number. (what a longer number left behind the end mark stays)</summary>
    public TextBuffer DigitsVacation { get; } = new(16);

    /// <summary>
    /// FINAL DS:8082 vacation_background, DS:8084 vacation_layers[5], DS:808e vacation_extra: the pictures of the
    /// vacation cut into images: the place ([0]), the agent's hand ([1]), the people ([2..5]) and the man who may
    /// stand there ([6]; none when there is no such image).
    /// </summary>
    public Image?[] VacationImages { get; } = new Image?[7];

    /// <summary>The places in <see cref="VacationImages"/>: the background, the hand (the first of the five layers), the extra man.</summary>
    public const int VacationBackground = 0, VacationHand = 1, VacationExtra = 6;

    // ---- the retirement ----

    /// <summary>FINAL DS:2daa: the picture of the agent, Max on the left and Maxine on the right ("gender.pic"), as the retirement screen names it.</summary>
    public string RetirementPicture { get; set; } = "";

    /// <summary>FINAL DS:2db5, DS:2db9: the agent's first name and surname around the code name on the retirement screen ("Max", "Remington").</summary>
    public string[] RetirementName { get; } = ["", ""];

    /// <summary>FINAL DS:2dc3, DS:2dcc: the stamp on an arrested MasterMind's face, as its shadow and as itself ("ARRESTED" both).</summary>
    public string[] ArrestedStamp { get; } = ["", ""];

    // ---- the efficiency report ----

    /// <summary>The ranks of the cast.</summary>
    public const int RankCount = 8;

    /// <summary>FINAL DS:3ae0 rank_names: the ranks of the cast, Recruit to MasterMind (through a table of pointers to the texts at DS:3806).</summary>
    public string[] RankNames => Dossier.RankNames;

    /// <summary>
    /// FINAL DS:4526 first_names, DS:4626 surnames, DS:4013 "Agent A", DS:4523 " ": the names of the persons, by sex and
    /// ethnic group, and the code name of a person not yet identified.
    /// </summary>
    public PersonNameTexts Names => Dossier.Names;

    /// <summary>FINAL DS:1632 cga_map_report: the colour table the driver uses while the efficiency report's photos are drawn.</summary>
    public byte[] ReportColorMap { get; } = new byte[16];

    /// <summary>FINAL DS:5ccc digits_report: the buffer in which the efficiency report writes out its numbers (the last one stays). (what a longer number left behind the end mark stays)</summary>
    public TextBuffer DigitsReport { get; } = new(16);

    /// <summary>FINAL DS:7de2 objects_seen_mask: the plot's objects (bit n: object n) that changed hands during the case.</summary>
    public short ObjectsSeenMask { get; set; }

    /// <summary>FINAL DS:7dee objects_taken_mask: the plot's objects the agent confiscated.</summary>
    public short ObjectsTakenMask { get; set; }

    /// <summary>FINAL DS:b308 ep_earned: the efficiency points the agent earned in the case.</summary>
    public short EpEarned { get; set; }

    /// <summary>FINAL DS:9be8 ep_possible: the efficiency points the case offered.</summary>
    public short EpPossible { get; set; }

    /// <summary>
    /// FINAL DS:abfa case_flags: how the case ended: <see cref="CaseFlagMastermindCaught"/>,
    /// <see cref="CaseFlagCrimeHappened"/>, and in the low bits the count of members arrested or turned.
    /// </summary>
    public short CaseFlags { get; set; }

    /// <summary>The bit of <see cref="CaseFlags"/> set when the MasterMind was arrested or turned.</summary>
    public const short CaseFlagMastermindCaught = 0x80;

    /// <summary>The bit of <see cref="CaseFlags"/> set when an event worth 5 or more points of the plot happened: the crime was carried out.</summary>
    public const short CaseFlagCrimeHappened = 0x40;

    /// <summary>FINAL DS:2a1a to DS:2af8: the texts of the efficiency report (<see cref="ReportText"/> names them).</summary>
    public string[] ReportTexts { get; } = new string[(int)ReportText.Count];

    // ---- the briefing ----

    /// <summary>Bytes of a palette table handed to the graphics driver: 16 colours and the border.</summary>
    public const int PaletteLength = 17;

    /// <summary>FINAL DS:1652 ega_palette: the palette of the menus' screens (colour 5 is black).</summary>
    public byte[] EgaPalette { get; } = new byte[PaletteLength];

    /// <summary>FINAL DS:24b0 ega_palette_faces: the palette of the screen of fifteen faces (colour 13 is white, for the frame).</summary>
    public byte[] EgaPaletteFaces { get; } = new byte[PaletteLength];

    /// <summary>FINAL DS:2d76 mastermind_names: the names of the 26 MasterMinds' organisations, by MasterMind (through a table of pointers to the texts at DS:2b80).</summary>
    public string[] MastermindNames { get; } = new string[MastermindCount];

    /// <summary>FINAL DS:a7b0 menu_draw_only: the next box is only drawn, not waited on (the library's <see cref="MenuState.MenuPauseMode"/>).</summary>
    public bool MenuDrawOnly
    {
        get => Menu.MenuPauseMode;
        set => Menu.MenuPauseMode = value;
    }

    /// <summary>FINAL DS:24c1, DS:24ee: the question of the faces screen, before and after the organisation's name ("Max, I'm sure you recognize the head of\nthe ", " among these faces.\n").</summary>
    public string[] StrFacesQuestion { get; } = ["", ""];

    /// <summary>
    /// FINAL DS:301c to DS:3236: the texts of the briefing's first pages, in the order of the original's data: the
    /// region of the operation, the organisation known to be involved, the mission and the Double Agents, the answer
    /// to the copy-protection question (<see cref="BriefingText"/> names them).
    /// </summary>
    public string[] BriefingTexts { get; } = new string[(int)BriefingText.Count];

    /// <summary>FINAL DS:15d2 cga_map_briefing: the colour table the driver uses while the briefing's and the debriefing's scenes are shown.</summary>
    public byte[] BriefingColorMap { get; } = new byte[16];

    /// <summary>FINAL DS:3261: the briefing's animation file ("briefing.pan").</summary>
    public string BriefingPanFile { get; set; } = "";

    private static short[][] Table(int outer, int inner) => [.. Enumerable.Range(0, outer).Select(_ => new short[inner])];

    private static T[] Records<T>(int count) where T : new() => [.. Enumerable.Range(0, count).Select(_ => new T())];
}

/// <summary>
/// One of the sixteen plots of the game (the "KeyHole" satellite, the stealth fighter ...): its title, which
/// MasterMinds fit it, and for each of its variants the crime file the case is read from and the two things the
/// crime is about, as the briefing and the Hall of Fame name them.
/// </summary>
// orig: struct plot, 74 bytes (src/c/include/final.h); FINAL DS:1e9e plots, 16 of them
public sealed class PlotDefinition
{
    /// <summary>Variants a plot has room for.</summary>
    public const int Variants = 8;

    /// <summary>+0x00: the plot's title, at most 23 characters.</summary>
    public string Title { get; set; } = "";

    /// <summary>+0x18: bits 8 to 11 say which MasterMinds fit the plot (held against the high byte of an organisation's MasterMind word).</summary>
    public short OrgMask { get; set; }

    /// <summary>+0x1a: the number of the crime file (CRIMEn.DTA) of each variant; -1 for a variant the plot does not have.</summary>
    public short[] CrimeFile { get; } = new short[Variants];

    /// <summary>+0x2a: the first thing each variant's crime is about ("the uplink ground station"); "" for none. The original keeps a pointer to the text.</summary>
    public string[] ItemA { get; } = [.. Enumerable.Repeat("", Variants)];

    /// <summary>+0x2c: the second thing; "" for none.</summary>
    public string[] ItemB { get; } = [.. Enumerable.Repeat("", Variants)];
}
