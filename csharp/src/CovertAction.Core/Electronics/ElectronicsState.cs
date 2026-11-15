using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Electronics;

/// <summary>What the electronics puzzle is used for: tapping phone lines, or installing a tracer on a car.</summary>
// orig: BUG DS:6056 mode, MODE_PHONES and MODE_TRACER (src/c/include/bug.h)
public enum ElectronicsMode : short
{
    /// <summary>A wiretap: the player must cut off the phones of the board (6 less half the difficulty of them).</summary>
    PhoneTaps = 0,

    /// <summary>The car tracer: five phones are enough, each one 20 percent of the tracer.</summary>
    CarTracer = 1,
}

/// <summary>
/// The state of the electronics minigame (the original's BUG program): a circuit board of seven columns of five chips
/// whose signals run from a supply on the left to ten terminals on the right, the wires drawn between them, the chip
/// the player carries, and the clock.
/// </summary>
/// <remarks>
/// <para>
/// How the circuit works. Each chip is a gate with two inputs and two outputs: chip <c>row</c> of a column takes the
/// signals <c>2 * row</c> (upper) and <c>2 * row + 1</c> (lower) of the ten lines coming into the column, and puts its
/// two results on the same two lines going out. Between two columns the wires may swap lines (<see cref="Connect"/>).
/// The lines going out of the last column end at the terminals: a terminal is a phone or an alarm
/// (<see cref="TerminalFlags"/>). A phone is tapped when its line carries no current; an alarm goes off when its line
/// does. The player swaps the chip they carry with chips of the board until enough phones are dead.
/// </para>
/// <para>
/// A chip is one word: its type (which of the twelve truth tables it follows) in the low six bits
/// (<see cref="ChipTypeMask"/>), and two marks, <see cref="ChipFixed"/> (it cannot be swapped) and <see cref="ChipShown"/>
/// (the player can see its type).
/// </para>
/// <para>
/// The names are the original's (the reconstructed bug.h), MicroProse's own where the Amiga version's symbols give them
/// (<c>BUGchip</c>, <c>BUGMoveX</c>, <c>connect</c>, <c>pin</c>, <c>board</c>, <c>diff</c>, <c>mode</c>). Nothing has a
/// built-in value: the tables are the initialised data of the player's BUG.EXE (CovertAction.Persistence knows where).
/// </para>
/// </remarks>
// orig: BUG DGROUP (src/c/include/bug.h)
public sealed partial class ElectronicsState : ILibraryState, ILibraryTextState, ILibraryScreenState
{
    /// <summary>A state whose two port pointers point at their records, as in the program's initialised data.</summary>
    public ElectronicsState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
        RpSprite = RpSpriteX;
    }

    // ---- the screen and the routines shared with the other parts ----

    /// <summary>BUG DS:2ef6: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>BUG DS:1548: the drawing port record of the off-screen copy of the screen kept under boxes.</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>BUG DS:2f08: the port all drawing of this part goes through; in the running game it is <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>BUG DS:155a: the port the screen is saved to and restored from; in the running game it is <see cref="RpBackX"/>.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>BUG DS:5c26 Status (MicroProse's name): this part's copy of the player's options (sound, joystick, ...).</summary>
    public Options Status { get; set; }

    /// <summary>BUG DS:157c: the first colour table handed to the graphics driver with the message box (used by the CGA driver).</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>BUG DS:158c: the second colour table.</summary>
    public byte[] ColorMapB { get; } = new byte[16];

    // the library's name for the part's variable (ILibraryState)
    short ILibraryState.Quit
    {
        get => DoneFlag;
        set => DoneFlag = value;
    }

    /// <summary>BUG DS:8206 done_flag: 0 while the puzzle runs; 1 when it is over; -1 to leave the whole game (the message box's Alt-Q).</summary>
    public short DoneFlag { get; set; }

    /// <summary>BUG DS:5542 Txt (MicroProse's name): the part's text buffer, where the messages of the board and the campaign's texts are built.</summary>
    public TextBuffer MessageText { get; } = new(512);

    /// <summary>The message box and menu.</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The picture animation beside a message box.</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The insert-disk prompt.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>Joystick variables (BUG has no mouse routines: the mouse words of this record are not in its data).</summary>
    public InputState Input { get; } = new();

    /// <summary>BUG DS:163e: the drawing port record of the page that holds the puzzle's picture sheet (bugs.pic).</summary>
    public RasterPort RpSpriteX { get; } = new();

    /// <summary>BUG DS:1650 RpSprite: the port the slot frames and terminal pictures are copied from; in the running game <see cref="RpSpriteX"/>.</summary>
    public RasterPort RpSprite { get; set; }

    /// <summary>The images this part holds, by the word the original knows each by (its place in memory).</summary>
    public ImageRegister Images { get; } = new();

    /// <summary>BUG DS:764c sprite (MicroProse's name): the images cut from the face sheets and the small pictures (face parts, tiles, caption bars, title caps).</summary>
    public Image?[] Sprite { get; } = new Image?[SpriteCount];

    /// <summary>BUG DS:773c: the puzzle's own images, cut from bugs.pic: 0..7 the wire pieces, 0x10.. the powered ones, 0x18.. the ones powered from the other side, 0x20..0x2b the chips by type.</summary>
    public Image?[] PuzzleImages { get; } = new Image?[PuzzleImageCount];

    /// <summary>Entries of <see cref="Sprite"/>.</summary>
    public const int SpriteCount = 0x74;

    /// <summary>Entries of <see cref="PuzzleImages"/>: up to the last chip picture.</summary>
    public const int PuzzleImageCount = 0x2c;

    /// <summary>The first chip picture of <see cref="PuzzleImages"/>; the chip of type t is this plus t.</summary>
    public const int FirstChipImage = 0x20;

    /// <summary>BUG DS:1760: the clock of the puzzle as last drawn, "hh:mm:ss".</summary>
    public string ClockStr { get; set; } = "";

    /// <summary>Chip columns on the board.</summary>
    public const int Columns = 7;

    /// <summary>Chips in each column.</summary>
    public const int Rows = 5;

    /// <summary>Signal lines between two columns: two per chip.</summary>
    public const int Lines = 2 * Rows;

    /// <summary>The kinds of chip: one truth table each.</summary>
    public const int ChipTypes = 12;

    /// <summary>Entries of one truth table: an output word for each line (upper, lower) for each of the four pairs of input signals.</summary>
    public const int TruthTableEntries = 8;

    /// <summary>Rows of <see cref="Pin"/>: the inputs and the outputs of each column.</summary>
    public const int PinRows = 2 * Columns;

    /// <summary>Cells of the board across: the 320-pixel screen in cells of 8 pixels.</summary>
    public const int BoardWidth = 40;

    /// <summary>Cells of the board down: the 200-pixel screen in cells of 8 pixels.</summary>
    public const int BoardHeight = 25;

    /// <summary>The directions a wire can leave a cell in (<see cref="Wire"/>).</summary>
    public const int Directions = 8;

    /// <summary>The palettes the puzzle switches between (four that make the current flicker, and the plain one).</summary>
    public const int PaletteCount = 5;

    /// <summary>Bytes of one palette: the colours of the 16 palette indices and the border.</summary>
    public const int PaletteSize = 17;

    /// <summary>The bits of a chip word that give the chip's type.</summary>
    public const short ChipTypeMask = 0x3f;

    /// <summary>The mark of a chip the player cannot swap (drawn with a frame).</summary>
    public const short ChipFixed = 0x40;

    /// <summary>The mark of a chip whose type the player can see.</summary>
    public const short ChipShown = 0x80;

    /// <summary>What a signal holds before the circuit has been worked out.</summary>
    public const short NoSignal = -1;

    // ---- the tables of the program ----

    /// <summary>BUG DS:0092: the step across, in cells, towards each of the eight neighbours of a cell (north first, clockwise).</summary>
    public short[] BUGMoveX { get; } = new short[Directions];

    /// <summary>BUG DS:00a2: the step down, in cells, towards each of the eight neighbours of a cell.</summary>
    public short[] BUGMoveY { get; } = new short[Directions];

    /// <summary>
    /// BUG DS:00b2: the twelve kinds of chip. For chip type t and input signals <c>upper</c>, <c>lower</c> (0 or 1),
    /// entry <c>2 * (2 * lower + upper)</c> of table t is the signal the chip puts on its upper line, the next entry
    /// the one on its lower line.
    /// </summary>
    public short[][] TruthTables { get; } = Table(ChipTypes, TruthTableEntries);

    /// <summary>BUG DS:1654: the palettes, <see cref="PaletteSize"/> bytes each.</summary>
    public byte[][] Palettes { get; } = [.. Enumerable.Range(0, PaletteCount).Select(_ => new byte[PaletteSize])];

    // ---- the board ----

    /// <summary>BUG DS:594c: the chips, by column and row: type and marks (see <see cref="ChipTypeMask"/>).</summary>
    public short[][] BUGchip { get; } = Table(Columns, Rows);

    /// <summary>
    /// BUG DS:747e: the wiring between the columns. <c>Connect[c][line]</c> is the line of column c + 1 that output
    /// line <c>line</c> of column c leads to. Rows 0..5 are used; the seventh exists in memory only.
    /// </summary>
    public short[][] Connect { get; } = Table(Columns, Lines);

    /// <summary>
    /// BUG DS:750a: the signals, 0 or 1 (<see cref="NoSignal"/> before they are worked out). Row <c>2 * c</c> holds the
    /// ten lines going into column c (row 0 is the supply on the left), row <c>2 * c + 1</c> the ten coming out of it;
    /// the last row is what reaches the terminals (the original's <c>final_outputs</c>, DS:760e).
    /// </summary>
    public short[][] Pin { get; } = Table(PinRows, Lines);

    /// <summary>BUG DS:7622: for each terminal, true when it is an alarm (must stay unpowered), false when it is a phone (to be cut off).</summary>
    public bool[] TerminalFlags { get; } = new bool[Lines];

    /// <summary>BUG DS:843c: the wires as drawn, one <see cref="Wire"/> word per 8x8 cell of the screen, by column and row.</summary>
    public short[][] Board { get; } = Table(BoardWidth, BoardHeight);

    // ---- the puzzle ----

    /// <summary>BUG DS:1652: the difficulty, 0..3: the campaign's level less the agent's electronics skill, plus one.</summary>
    public short Diff { get; set; }

    /// <summary>BUG DS:6056: whether the board taps phones or installs the car tracer.</summary>
    public ElectronicsMode Mode { get; set; }

    /// <summary>BUG DS:5c2c: whether the display is CGA (then the board's wires are colour 3 on the background picture, else 10).</summary>
    public bool CGA { get; set; }

    /// <summary>BUG DS:8274: the highest chip type the difficulty allows, 2 * difficulty + 6 at most 11; set when the board is built and read nowhere.</summary>
    public short ChipTypeLimit { get; set; }

    /// <summary>BUG DS:5c30: the type of the chip the player carries (0..11).</summary>
    public short CarriedChip { get; set; }

    /// <summary>BUG DS:5c24: the chip column the cursor is on.</summary>
    public short CursorCol { get; set; }

    /// <summary>BUG DS:5c2e: the chip row the cursor is on.</summary>
    public short CursorRow { get; set; }

    /// <summary>BUG DS:8c0c: the phones cut off at the last evaluation.</summary>
    public short PhonesOk { get; set; }

    /// <summary>BUG DS:747a: the alarms that have gone off.</summary>
    public short AlarmCount { get; set; }

    /// <summary>BUG DS:7db4: seconds left on the clock.</summary>
    public short TimeLeft { get; set; }

    /// <summary>BUG DS:675c: animation steps of the clock; every eighth takes a second off <see cref="TimeLeft"/>. 0 until the puzzle has started.</summary>
    public short TickCount { get; set; }

    /// <summary>BUG DS:5c22: the most phones the player has had cut off at once so far; each new best is rewarded with intelligence.</summary>
    public short BestPhones { get; set; }

    /// <summary>BUG DS:675a: the phones cut off when the puzzle ended (0 after an alarm), which the part reports to the campaign (five per cent each).</summary>
    public short ResultPhones { get; set; }

    /// <summary>BUG DS:8f96: the campaign's difficulty before the agent's electronics skill lowered it (<see cref="Diff"/>); the strength of what the wiretap reveals.</summary>
    public short BaseLevel { get; set; }

    /// <summary>BUG DS:7636: set to 1 first thing when the part starts and never read by any routine of the program.</summary>
    public short StartMark { get; set; }

    // ---- the names of the picture files the part loads ----

    /// <summary>BUG DS:0074: the picture the intelligence popup's frame and the building picture are cut from ("camera.pic").</summary>
    public string PopupSheetName { get; set; } = "";

    /// <summary>BUG DS:007f: the picture of the board the puzzle is played on ("board.pic").</summary>
    public string BoardSheetName { get; set; } = "";

    /// <summary>BUG DS:240a: the picture sheet of the first 40 faces ("facesf.pic").</summary>
    public string FacesWomenName { get; set; } = "";

    /// <summary>BUG DS:2415: the picture sheet of the other 40 faces ("faces.pic").</summary>
    public string FacesMenName { get; set; } = "";

    /// <summary>BUG DS:16a9: the picture sheet of the puzzle's pieces, wires and chips ("bugs.pic").</summary>
    public string BugsSheetName { get; set; } = "";

    // ---- the texts of the puzzle's result ----

    /// <summary>BUG DS:16b2: "Car tracer ", the start of the tracer's progress message.</summary>
    public string StrCarTracer { get; set; } = "";

    /// <summary>BUG DS:16be: "% complete\n", after the tracer's percentage.</summary>
    public string StrPercentComplete { get; set; } = "";

    /// <summary>BUG DS:16ca: "Alarm!!!\n", shown when an alarm terminal gets current.</summary>
    public string StrAlarm { get; set; } = "";

    /// <summary>BUG DS:16d4: "Phone taps completed.\n", the wiretap's success.</summary>
    public string StrTapsCompleted { get; set; } = "";

    /// <summary>BUG DS:16eb: "Car tracer installed.\n", the tracer's success.</summary>
    public string StrTracerInstalled { get; set; } = "";

    /// <summary>BUG DS:1702: "Time expired.\n".</summary>
    public string StrTimeExpired { get; set; } = "";

    /// <summary>BUG DS:1711: " phones tapped.\n", after the number of phones when the time ran out.</summary>
    public string StrPhonesTapped { get; set; } = "";

    /// <summary>BUG DS:1722: "No", written instead of an alarm count of 0.</summary>
    public string StrNo { get; set; } = "";

    /// <summary>BUG DS:1725: " Alarms\n", which closes the result ("No Alarms").</summary>
    public string StrAlarms { get; set; } = "";

    /// <summary>BUG DS:172e: "Are you sure\nyou want to Quit?\n No\n Yes\n", the puzzle's Alt-Q box.</summary>
    public string StrQuitConfirm { get; set; } = "";

    /// <summary>BUG DS:1757: "PAUSED\n", the puzzle's Alt-P box.</summary>
    public string StrPaused { get; set; } = "";

    /// <summary>BUG DS:5c20: the seed this part drew from the clock.</summary>
    public short RandomSeed { get; set; }

    /// <summary>BUG DS:377c: this part's random generator (its state is the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    // ---- this part's copy of the campaign (what a wiretap teaches is written into it, then handed back) ----

    /// <summary>BUG DS:0172 ps (MicroProse's name): the campaign's persons. A record whose face (look word) is 0 is free.</summary>
    public Person[] Ps { get; } = Records<Person>(CampaignTables.PersonCount);

    /// <summary>BUG DS:0572 lc (MicroProse's name): the campaign's buildings, one per organisation and city the player knows of.</summary>
    public Location[] Lc { get; } = Records<Location>(CampaignTables.LocationCount);

    /// <summary>BUG DS:5e32 ct (MicroProse's name): the campaign's sixteen cities.</summary>
    public City[] Ct { get; } = Records<City>(CampaignTables.WorldSize);

    /// <summary>BUG DS:7fc0 org (MicroProse's name): the campaign's sixteen organisations.</summary>
    public Org[] Org { get; } = Records<Org>(CampaignTables.WorldSize);

    /// <summary>BUG DS:77b4 ac (MicroProse's name): the cast of the case, one record per member; entry 0 is the mastermind's.</summary>
    public Activity[] Ac { get; } = Records<Activity>(CastCapacity);

    /// <summary>
    /// Records of <see cref="Ac"/>: the room from DS:77b4 to <c>time_left</c> (DS:7db4), and the size the Amiga build's
    /// debug types give the table (<c>struct Activity ac[32]</c>). read_env_state (BUG 00e8:6985) copies NACTIVITY
    /// records, at most 20 (the 8 members of CRIME2.DTA, CRIME3.DTA or CRIME8.DTA and four decoys a level, up to level 3).
    /// </summary>
    public const int CastCapacity = 32;

    /// <summary>BUG DS:1172 cl (MicroProse's name): the clues the player holds (what a clue connects, and its item).</summary>
    public Clue[] Cl { get; } = Records<Clue>(CampaignTables.ClueCount);

    /// <summary>BUG DS:6058 sn (MicroProse's name): the messages of the case (intercepted, delivered or waiting).</summary>
    public Synopsis[] Sn { get; } = Records<Synopsis>(CampaignTables.SynopsisCount);

    /// <summary>BUG DS:6760 ev (MicroProse's name): the events of the case's plot.</summary>
    public Event[] Ev { get; } = Records<Event>(EventCapacity);

    /// <summary>Records of <see cref="Ev"/>: the room from the table's start to the next variable (<c>NextSyn</c>, DS:7260), 64 as in the other parts.</summary>
    public const int EventCapacity = 64;

    /// <summary>BUG DS:5992 pl (MicroProse's name): the case being played: its mastermind, crime and texts.</summary>
    public Plot Pl { get; } = new();

    /// <summary>BUG DS:6054 Nperson (MicroProse's name): the persons in use; the search for a person looks at the first this many.</summary>
    public short Nperson { get; set; }

    /// <summary>BUG DS:7268 Nloc (MicroProse's name): the buildings in use in <see cref="Lc"/>.</summary>
    public short Nloc { get; set; }

    /// <summary>BUG DS:594a Gtime (MicroProse's name): the campaign's clock, in minutes since the case began.</summary>
    public short Gtime { get; set; }

    /// <summary>BUG DS:2b4a: a clock as "hh:mm:ss", written by <c>format_game_time</c>.</summary>
    public string ClockText { get; set; } = "";

    /// <summary>BUG DS:8378 ac2 (MicroProse's name): the second record of each member of the cast (when they act, what the player has gathered on them).</summary>
    public Activity2[] Ac2 { get; } = Records<Activity2>(CastCapacity);

    /// <summary>BUG DS:77b0 NACTIVITY (MicroProse's name): the members of the cast in use.</summary>
    public short NACTIVITY { get; set; }

    /// <summary>BUG DS:7260 NextSyn (MicroProse's name): the messages in use in <see cref="Sn"/>.</summary>
    public short NextSyn { get; set; }

    /// <summary>BUG DS:726a ChronTime (MicroProse's name): the step the case's plot has reached.</summary>
    public short ChronTime { get; set; }

    /// <summary>BUG DS:8c0e InCity (MicroProse's name): the city the agent is in.</summary>
    public short InCity { get; set; }

    /// <summary>BUG DS:8fd8 AtLoc (MicroProse's name): the organisation whose building the agent is at (whose lines are tapped).</summary>
    public short AtLoc { get; set; }

    /// <summary>BUG DS:8c10 active (MicroProse's name): for each city, how often the player was told of it.</summary>
    public short[] Active { get; } = new short[CampaignTables.WorldSize];

    /// <summary>BUG DS:8c30: for each organisation, how often the player was told of it.</summary>
    public short[] OrgCounters { get; } = new short[CampaignTables.WorldSize];

    // ---- what a wiretap reveals: the intelligence screens ----

    /// <summary>BUG DS:1aa0: the number of set bits of each value 0..31.</summary>
    public byte[] BitCount { get; } = new byte[32];

    /// <summary>BUG DS:1ac0 month_names: the months' short names, "Jan" to "Dec" (the message log's, <see cref="Dossier"/>).</summary>
    public string[] MonthNames => Dossier.Words.MonthNames;

    /// <summary>
    /// BUG DS:675e coded_flag: whether the intelligence shown now is a coded message (its popup then has a bar code instead
    /// of a frame). The same variable is the clue report's source (BUG 00e8:2911 show_clue_report: 1 is "Electronic
    /// WireTap"), so it is kept in <see cref="Dossier"/>, where the library's clue report reads it.
    /// </summary>
    public short CodedFlag
    {
        get => Dossier.CodedFlag;
        set => Dossier.CodedFlag = value;
    }

    /// <summary>BUG DS:5944: the point the intelligence popup is placed by: its left edge is 31 where that lies between this and 146 pixels left of it, else the nearer of the two.</summary>
    public short PopupAnchorX { get; set; }

    /// <summary>BUG DS:5948: the same, downwards: the popup's top is 49 where that lies between this and 102 pixels above it.</summary>
    public short PopupAnchorY { get; set; }

    /// <summary>
    /// BUG DS:5c28: the intelligence popup's two pictures, side by side in the data: its background (146 x 102 pixels,
    /// <c>popup_frame_image</c>) and the picture of a building (<c>building_image</c>, DS:5c2a).
    /// </summary>
    public Image?[] PopupPictures { get; } = new Image?[2];

    /// <summary>The intelligence popup's background picture.</summary>
    public Image? PopupFrameImage => PopupPictures[0];

    /// <summary>The picture of a building, shown in the popup when a new building was found.</summary>
    public Image? BuildingImage => PopupPictures[1];

    /// <summary>
    /// BUG DS:6758 popup_face: the face the intelligence popup shows (a person's look word), -1 for none, -2 for the
    /// picture of a building. The same word is the face the library's routines show beside the next box.
    /// </summary>
    public short PopupFace
    {
        get => Animation.BackdropCode;
        set => Animation.BackdropCode = value;
    }

    /// <summary>BUG DS:159c: the colour tables the puzzle's screen is drawn with on CGA (handed to the driver as both of its tables).</summary>
    public byte[] BoardColorMap { get; } = new byte[16];

    /// <summary>BUG DS:2428..: the kinds of building by level 1..5, " hideout", " agent", " safehouse", " active cel", " office".</summary>
    public string[] BuildingKinds { get; } = new string[5];

    /// <summary>
    /// BUG DS:1f44 key_phrases: the names of a case's items by kind and number, "Ford Escort #", "Walther PPK #",
    /// "Rue Laverne ", "PanAm #" ...: eight kinds (cars, weapons, streets, airlines, wire services, two kinds of money,
    /// passports) of eight names. The original declares sixteen kinds; behind the eighth lie other data.
    /// </summary>
    public string[][] KeyPhrases => Dossier.ClueLabels;

    /// <summary>Kinds of item with names in <see cref="KeyPhrases"/>.</summary>
    public const int ItemKinds = 8;

    /// <summary>Names of each kind of item in <see cref="KeyPhrases"/>.</summary>
    public const int ItemNames = 8;

    /// <summary>BUG DS:20e6: "00", after the number of an amount of money of kind 5.</summary>
    public string StrAmount00
    {
        get => Dossier.Hundreds;
        set => Dossier.Hundreds = value;
    }

    /// <summary>BUG DS:20e9: "000", after the number of an amount of money of kind 6.</summary>
    public string StrAmount000
    {
        get => Dossier.Thousands;
        set => Dossier.Thousands = value;
    }

    /// <summary>BUG DS:2422: " ... ", where words of a dossier line the player does not know were left out.</summary>
    public string StrOmitted
    {
        get => Dossier.Ellipsis;
        set => Dossier.Ellipsis = value;
    }

    /// <summary>The texts of the intelligence screens.</summary>
    public IntelligenceTexts IntelTexts { get; } = new();

    // ---- the library's text and screen routines (ILibraryTextState, ILibraryScreenState) ----

    /// <summary>BUG DS:8278 token_tail_buf: the placeholder routine's scratch line.</summary>
    public TextBuffer TokenTail { get; } = new(ILibraryTextState.LineCapacity);

    /// <summary>BUG DS:21ec.. (table DS:230e): the message texts' placeholders, "$VICTIM" ... "$OBJECT".</summary>
    public string[] VariableNameTexts { get; } = new string[ILibraryTextState.MessageVariables];

    /// <summary>BUG DS:2224.. (table DS:231c): the placeholders' current values.</summary>
    public string[] VariableValueTexts { get; } = new string[ILibraryTextState.MessageVariables];

    IReadOnlyList<string> ILibraryTextState.VariableNames => VariableNameTexts;

    IList<string> ILibraryTextState.VariableValues => VariableValueTexts;

    /// <summary>BUG DS:7640 TheCrime (MicroProse's name): the number of the case's crime file, which numbers its messages.</summary>
    public short TheCrime { get; set; }

    /// <summary>BUG DS:23f8: "text.dta", the file of the game's message texts.</summary>
    public string MessageFileName
    {
        get => Dossier.MessageFile;
        set => Dossier.MessageFile = value;
    }

    /// <summary>BUG DS:2401: "*MSG0000", the template of a message text's tag.</summary>
    public string MessageTemplate
    {
        get => Dossier.MessageTemplate;
        set => Dossier.MessageTemplate = value;
    }

    /// <summary>BUG DS:3a2e msg_tag_buf: the tag of the message text being read.</summary>
    public TextBuffer MessageTag => Dossier.MessageTag;

    /// <summary>BUG DS:2344: "This information requires security clearance: ".</summary>
    public string ClearanceNotice { get; set; } = "";

    /// <summary>BUG DS:233b: the blank the text readers put behind every line.</summary>
    public string LineSeparator { get; set; } = "";

    /// <summary>BUG DS:8438 GMode: the graphics mode letter the game was started with ('C' CGA, 'E' EGA, 'M' MCGA ...).</summary>
    public sbyte GMode { get; set; }

    short ILibraryScreenState.Picface
    {
        get => PopupFace;
        set => PopupFace = value;
    }

    /// <summary>BUG DS:15fc: the colour tables a photo is drawn with on displays with fewer colours.</summary>
    public byte[] PhotoColorMap { get; } = new byte[16];

    IReadOnlyList<Image?> ILibraryScreenState.Sprite => Sprite;

    /// <summary>The names the game gives to persons (first names by sex, surnames, the agent's placeholder).</summary>
    public PersonNameTexts Names => Dossier.Names;

    private static short[][] Table(int outer, int inner) => [.. Enumerable.Range(0, outer).Select(_ => new short[inner])];

    private static T[] Records<T>(int count) where T : new() => [.. Enumerable.Range(0, count).Select(_ => new T())];
}
