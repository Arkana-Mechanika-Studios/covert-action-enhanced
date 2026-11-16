using CovertAction.Core.Animation;
using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The state of the campaign part (the original's GAME program): the case being played, the world it is played in,
/// what the player has learned so far, the clock, and the texts the campaign builds its messages from.
/// </summary>
/// <remarks>
/// <para>
/// A case is a plot of the criminal organisation behind it: a cast of participants (<see cref="Ac"/>), each mapped onto
/// a person of the world (<see cref="Ps"/>), who carry out events (<see cref="Ev"/>) in an order the plot engine works
/// out step by step. The player learns of persons, of the buildings of organisations in cities (<see cref="Lc"/>), of
/// clues (<see cref="Cl"/>) and of intercepted messages (<see cref="Sn"/>).
/// </para>
/// <para>
/// The names are the original's (the reconstructed game.h, campaign.h), MicroProse's own where the Amiga version's symbols
/// give them (<c>ps</c>, <c>lc</c>, <c>cl</c>, <c>ct</c>, <c>org</c>, <c>sn</c>, <c>mc</c>, <c>ac</c>, <c>ac2</c>,
/// <c>ev</c>, <c>Gtime</c>, <c>Nperson</c>, <c>Txt</c>, ...). Nothing has a built-in value: the texts and tables are
/// the initialised data of the player's GAME.EXE, the campaign comes with the hand-off from the other parts
/// (CovertAction.Persistence knows where each lies).
/// </para>
/// </remarks>
// orig: GAME DGROUP (src/c/include/game.h, src/c/include/campaign.h)
public sealed partial class CampaignState : ILibraryState, ILibraryTextState, ILibraryScreenState, ILibrarySaveState
{
    /// <summary>A state whose port pointers point at their records, as in the program's initialised data.</summary>
    public CampaignState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
        RpPic = RpPicX;
        Pan = new PanEngineState(Images);
    }

    /// <summary>
    /// The variables of the animation player (the library's: the menus have the same player). GAME DS:4248 to 42b5,
    /// DS:55d6 to 5609 and others; their places are in Persistence.Layout.PanEngineLayout.
    /// </summary>
    public PanEngineState Pan { get; }

    // ---- the screen ----

    /// <summary>GAME DS:435c: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>GAME DS:146a: the drawing port record of the off-screen copy of the screen kept under boxes.</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>GAME DS:1456: the drawing port record of the page that holds the current picture (a city, a building).</summary>
    public RasterPort RpPicX { get; } = new();

    /// <summary>GAME DS:436e Rp1 (MicroProse's name): the port all drawing of this part goes through; in the running game it is <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>GAME DS:147c RpBack (MicroProse's name): the port the screen is saved to under a box.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>GAME DS:1468 RpPic (MicroProse's name): the port of the picture page.</summary>
    public RasterPort RpPic { get; set; }

    /// <summary>Images in <see cref="Sprite"/>.</summary>
    public const int SpriteCount = 120;

    /// <summary>The images the part has cut and keeps (its place in memory, as the original's words hold it, is the register's business).</summary>
    public ImageRegister Images { get; } = new();

    /// <summary>
    /// GAME DS:969c sprite: the images cut from the campaign's picture sheets, by number: the icons and tags of the
    /// screens, then (from 40) the face parts that portraits are put together from.
    /// </summary>
    public Image?[] Sprite { get; } = new Image?[SpriteCount];

    /// <summary>Persons in the persons table (<c>ps</c>).</summary>
    public const int PersonCount = CampaignTables.PersonCount;

    /// <summary>Buildings in the buildings table (<c>lc</c>): one per organisation and city the player knows of.</summary>
    public const int LocationCount = CampaignTables.LocationCount;

    /// <summary>Clues in the clue table (<c>cl</c>).</summary>
    public const int ClueCount = CampaignTables.ClueCount;

    /// <summary>Messages in the message log (<c>sn</c>).</summary>
    public const int SynopsisCount = CampaignTables.SynopsisCount;

    /// <summary>Cities and organisations of the world (<c>ct</c>, <c>org</c>).</summary>
    public const int WorldSize = CampaignTables.WorldSize;

    /// <summary>Plot objects of a case (<c>mc</c>).</summary>
    public const int MacGuffinCount = CampaignTables.MacGuffinCount;

    /// <summary>Room for cast members in this part's memory (<c>ac</c>, <c>ac2</c>); a case uses <see cref="NACTIVITY"/> of them.</summary>
    public const int CastCapacity = 32;

    /// <summary>Room for events in this part's memory (<c>ev</c>); a case uses <see cref="NEVENT"/> of them.</summary>
    public const int EventCapacity = 64;

    /// <summary>Months of the year in <see cref="MonthNames"/>.</summary>
    public const int Months = 12;

    /// <summary>The first-name table: <see cref="FirstNameRows"/> rows (women's names, then men's) of this many names.</summary>
    public const int NamesPerRow = 64;

    /// <summary>Rows of the first-name table: 0 for a person whose face word has bit 0 clear (a woman), 1 for a man.</summary>
    public const int FirstNameRows = 2;

    /// <summary>The clue-label table: one row per kind of clue item (car, weapon, address ...), eight labels each.</summary>
    public const int ClueLabelRows = 8;

    /// <summary>Labels in one row of the clue-label table.</summary>
    public const int ClueLabelsPerRow = 8;

    // ---- the case and its world (this part's copy of the campaign tables) ----

    /// <summary>GAME DS:0094 ps (MicroProse's name): the persons. A record whose face word is 0 is free.</summary>
    public Person[] Ps { get; } = Records<Person>(PersonCount);

    /// <summary>GAME DS:0494 lc (MicroProse's name): the buildings, one per organisation and city the player knows of; <see cref="Nloc"/> in use.</summary>
    public Location[] Lc { get; } = Records<Location>(LocationCount);

    /// <summary>GAME DS:1094 cl (MicroProse's name): the clues the player has found; a record whose <see cref="Clue.Act"/> is -1 is free.</summary>
    public Clue[] Cl { get; } = Records<Clue>(ClueCount);

    /// <summary>GAME DS:7a9e ct (MicroProse's name): the sixteen cities.</summary>
    public City[] Ct { get; } = Records<City>(WorldSize);

    /// <summary>GAME DS:9fb4 org (MicroProse's name): the sixteen organisations; 1 is the player's agency.</summary>
    public Org[] Orgs { get; } = Records<Org>(WorldSize);

    /// <summary>GAME DS:7cc2 sn (MicroProse's name): the message log, <see cref="NextSyn"/> in use.</summary>
    public Synopsis[] Sn { get; } = Records<Synopsis>(SynopsisCount);

    /// <summary>GAME DS:a414 mc (MicroProse's name): the case's plot objects and who holds them.</summary>
    public MacGuffins[] Mc { get; } = Records<MacGuffins>(MacGuffinCount);

    /// <summary>GAME DS:97a6 ac (MicroProse's name): the cast, <see cref="NACTIVITY"/> members; the last 4 * <see cref="Diff"/> are decoys.</summary>
    public Activity[] Ac { get; } = Records<Activity>(CastCapacity);

    /// <summary>GAME DS:a55e ac2 (MicroProse's name): each cast member's progress in the plot.</summary>
    public Activity2[] Ac2 { get; } = Records<Activity2>(CastCapacity);

    /// <summary>GAME DS:83c6 ev (MicroProse's name): the plot's events, <see cref="NEVENT"/> of them.</summary>
    public Event[] Ev { get; } = Records<Event>(EventCapacity);

    /// <summary>GAME DS:a628 active (MicroProse's name): per city, how often a clue pointed there.</summary>
    public short[] Active { get; } = new short[WorldSize];

    /// <summary>GAME DS:a648: per organisation, how often a clue named it.</summary>
    public short[] OrgCounters { get; } = new short[WorldSize];

    /// <summary>
    /// GAME DS:760a pl (MicroProse's name): the case being played. The original also names some of its fields:
    /// <c>mastermind_known</c> is <see cref="Plot.Clues"/>, <c>mastermind_org</c> <see cref="Plot.BigOrg"/>,
    /// <c>mastermind_city</c> <see cref="Plot.BigLoc"/>, <c>mastermind_look</c> <see cref="Plot.BigFace"/>,
    /// <c>clock_month</c> <see cref="Plot.Ncase"/> (the month the case runs in, counted from January 1990).
    /// </summary>
    public Plot Pl { get; } = new();

    // ---- the campaign's scalars ----

    /// <summary>GAME DS:7604 diff (MicroProse's name): the difficulty, 0..3.</summary>
    public short Diff { get; set; }

    /// <summary>GAME DS:7608 Gtime (MicroProse's name): the clock, in minutes since the case began.</summary>
    public short Gtime { get; set; }

    /// <summary>GAME DS:93ea min: the minute of the hour, set when the clock is formatted.</summary>
    public short Min { get; set; }

    /// <summary>GAME DS:9682 hour: the hour of the day, 0..23, set when the clock is formatted.</summary>
    public short Hour { get; set; }

    /// <summary>GAME DS:7606 day: the day of the case, from 1, set when the clock is formatted.</summary>
    public short Day { get; set; }

    /// <summary>GAME DS:7cc0 Nperson (MicroProse's name): the persons in use; the searches for a person look at the first this many.</summary>
    public short Nperson { get; set; }

    /// <summary>GAME DS:8ece Nloc (MicroProse's name): the buildings in use in <see cref="Lc"/>.</summary>
    public short Nloc { get; set; }

    /// <summary>GAME DS:8ec6 NextSyn (MicroProse's name): the messages in the log.</summary>
    public short NextSyn { get; set; }

    /// <summary>GAME DS:8ed0 ChronTime (MicroProse's name): the plot steps taken so far (-1 before the first).</summary>
    public short ChronTime { get; set; }

    /// <summary>
    /// Which of the original's bugs the campaign's rules reproduce (the plot engine, the message texts): set by the part
    /// when it starts, the original's own when left alone. Not part of a saved campaign.
    /// </summary>
    public OriginalBehaviour Behaviour { get; set; } = OriginalBehaviour.Faithful;

    /// <summary>GAME DS:97a2 NACTIVITY (MicroProse's name): the cast members of the case.</summary>
    public short NACTIVITY { get; set; }

    /// <summary>GAME DS:9688 NEVENT (MicroProse's name): the events of the case; the last is a marker.</summary>
    public short NEVENT { get; set; }

    /// <summary>GAME DS:9690 TheCrime (MicroProse's name): the number of the case's crime file (CRIMEn.DTA).</summary>
    public short TheCrime { get; set; }

    /// <summary>GAME DS:978c Alerted (MicroProse's name): the criminal world's general alarm, raised by noisy actions, decaying every plot step.</summary>
    public short Alerted { get; set; }

    /// <summary>GAME DS:97a0 Doubles (MicroProse's name): one bit per city whose buildings are more alert.</summary>
    public short Doubles { get; set; }

    /// <summary>GAME DS:a626 InCity (MicroProse's name): the city the player is in.</summary>
    public short InCity { get; set; }

    /// <summary>GAME DS:aa58 AtLoc (MicroProse's name): the organisation whose building the player is at.</summary>
    public short AtLoc { get; set; }

    /// <summary>GAME DS:a66a Sleepy (MicroProse's name): 1 while the player waits in the hotel.</summary>
    public short Sleepy { get; set; }

    /// <summary>GAME DS:1454 Sprung (MicroProse's name): the person under arrest whom the next event concerns, -1 for none.</summary>
    public short Sprung { get; set; }

    /// <summary>GAME DS:557e excluded_person: the person a plot step must not pick; -2 before the step has chosen, -1 after its first look.</summary>
    public short ExcludedPerson { get; set; }

    /// <summary>GAME DS:5580 flagged_cast_person: the person of the cast's mastermind, as last looked up.</summary>
    public short FlaggedCastPerson { get; set; }

    /// <summary>
    /// GAME DS:83c2 picface (MicroProse's name): the face word of the portrait shown beside the next box, -1 for none.
    /// It is the box routine's own variable (the library's <see cref="AnimationState.BackdropCode"/>), under the
    /// campaign's name for it.
    /// </summary>
    public short Picface
    {
        get => Animation.BackdropCode;
        set => Animation.BackdropCode = value;
    }

    // ---- the scalars a save and a hand-off carry besides the ones above, and the save routine's own variables ----

    /// <summary>GAME DS:9694 TheOrg1 (MicroProse's name): the plot's second organisation.</summary>
    public short TheOrg1 { get; set; }

    /// <summary>GAME DS:9698 TheOrg2 (MicroProse's name): the plot's third organisation.</summary>
    public short TheOrg2 { get; set; }

    /// <summary>GAME DS:8edc TheCity1 (MicroProse's name): the plot's second city.</summary>
    public short TheCity1 { get; set; }

    /// <summary>GAME DS:8ede TheCity2 (MicroProse's name): the plot's third city.</summary>
    public short TheCity2 { get; set; }

    /// <summary>GAME DS:968a TheSeed (MicroProse's name): the random seed the case was set up with.</summary>
    public short TheSeed { get; set; }

    /// <summary>GAME DS:a86e MacGuffin (MicroProse's name): a word of the hand-off the campaign only carries along.</summary>
    public short MacGuffin { get; set; }

    /// <summary>GAME DS:a668 InterPol (MicroProse's name): the documents the player has seen, one bit per organisation (bit 0 the Master Plan; the hand-off block's word 0x4c).</summary>
    public short InterPol { get; set; }

    /// <summary>GAME DS:9fb0 skills: the agent's four skills, two bits each, and the game speed in bits 10 and 11.</summary>
    public short Skills { get; set; }

    /// <summary>GAME DS:55c4 save_handle: the number of the save file being written or read.</summary>
    public short SaveHandle { get; set; }

    /// <summary>GAME DS:55c2 save_mem_offset: 0 while a save goes to a file; else the place in the hand-off block the next table goes to.</summary>
    public short SaveMemOffset { get; set; }

    /// <summary>GAME DS:41a0: "env.sve", the name under which the campaign is handed to the next part instead of saved to a file.</summary>
    public string StrEnvSve { get; set; } = "";

    // ---- the variables of the routines every part has (the message box, the animation beside it, the insert-disk
    //      prompt, the joystick): GAME's copies, at the places Persistence.Layout.LibraryLayout lists ----

    /// <summary>GAME DS:789a Status (MicroProse's name): this part's copy of the player's options (sound, animation, joystick ...).</summary>
    public Options Status { get; set; }

    /// <summary>GAME DS:a3ee exit_mode: what the campaign's main loop does after a box: 0 to go on, another value to leave (-1: leave the game).</summary>
    public short Quit { get; set; }

    /// <summary>GAME DS:149e color_map_a: the first colour table handed to the graphics driver with a box.</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>GAME DS:14ae color_map_b: the second colour table.</summary>
    public byte[] ColorMapB { get; } = new byte[16];

    /// <summary>The library's name for <see cref="Txt"/>.</summary>
    TextBuffer ILibraryState.MessageText => Txt;

    /// <summary>The message box's variables (GAME calls the box routine <c>popup_menu</c>; its NoWait is <see cref="MenuState.MenuPauseMode"/>).</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The variables of the animation beside a box and of the face behind it (GAME's picface, FaceX and FaceY are its backdrop code and arguments).</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The variables of the insert-disk prompt.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>The joystick and mouse variables.</summary>
    public InputState Input { get; } = new();

    /// <summary>Rows a menu of the campaign can list in <see cref="Mitem"/>.</summary>
    public const int MenuRows = 32;

    /// <summary>GAME DS:969a Mindex (MicroProse's name): the rows of the menu being built (or of the list of clues).</summary>
    public short Mindex { get; set; }

    /// <summary>GAME DS:a9b0 Mitem (MicroProse's name): for each row of the menu being built, what it stands for (a city, an organisation, a cast member).</summary>
    public short[] Mitem { get; } = new short[MenuRows];

    /// <summary>GAME DS:5234: this part's random generator (its state is the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    // ---- texts the campaign writes ----

    /// <summary>GAME DS:7202 Txt (MicroProse's name): the text every message, report and list is put together in.</summary>
    public TextBuffer Txt { get; } = new(1024);

    /// <summary>GAME DS:3cf8 clock_text: the clock as "hh:mm AM Mon dd", written by <c>format_game_time</c>.</summary>
    public TextBuffer ClockText { get; } = new(16);

    /// <summary>GAME DS:5514 number_text: a number turned into text for a label or a date.</summary>
    public TextBuffer NumberText { get; } = new(16);

    /// <summary>GAME DS:a45e: where <c>replace_substrings</c> keeps the rest of a text while it puts a value in.</summary>
    public TextBuffer ReplaceTail { get; } = new(256);

    // ---- texts and tables of the program ----

    /// <summary>GAME DS:2bbe month_names: the names of the months, January first.</summary>
    public string[] MonthNames { get; } = new string[Months];

    /// <summary>GAME DS:3cf4: the space and two placeholder digits put behind the month in the clock text.</summary>
    public string ClockDayText { get; set; } = "";

    /// <summary>
    /// GAME DS:1c2a first_name_table: first names, by sex (row 0 women, row 1 men) and then by the organisation's
    /// ethnic group (two times <see cref="Org.Race"/>) plus four bits of the person's face word.
    /// </summary>
    public string[][] FirstNames { get; } = [new string[NamesPerRow], new string[NamesPerRow]];

    /// <summary>GAME DS:1d2a surname_table: surnames, by the organisation's ethnic group plus four bits of the face word.</summary>
    public string[] Surnames { get; } = new string[NamesPerRow];

    /// <summary>GAME DS:1717: "Agent" and a letter: how a person not yet identified is named (the letter is replaced by the person's).</summary>
    public string StrAgentLetter { get; set; } = "";

    /// <summary>GAME DS:1c27: the space between a first name and a surname.</summary>
    public string StrNameSpace { get; set; } = "";

    /// <summary>
    /// GAME DS:3042 clue_label_table: the labels of clue items, by kind of item (car, weapon, address, ticket,
    /// telegram, two kinds of money, papers) and a variant; a number follows the label.
    /// </summary>
    public string[][] ClueLabels { get; } = [.. Enumerable.Range(0, ClueLabelRows).Select(_ => new string[ClueLabelsPerRow])];

    /// <summary>GAME DS:31e4 clue_suffix_5: what follows the number of a sum of money in hundreds.</summary>
    public string ClueSuffixHundreds { get; set; } = "";

    /// <summary>GAME DS:31e7 clue_suffix_6: what follows the number of a sum of money in thousands.</summary>
    public string ClueSuffixThousands { get; set; } = "";

    /// <summary>GAME DS:31c4: the words a clue about a face ends with ("this face.").</summary>
    public string StrThisFace { get; set; } = "";

    /// <summary>GAME DS:31cf: the code name a clue gives a real participant of the plot.</summary>
    public string StrCodeNameReal { get; set; } = "";

    /// <summary>GAME DS:31d8: the code name a clue gives a decoy.</summary>
    public string StrCodeNameDecoy { get; set; } = "";

    /// <summary>GAME DS:3b0c: the picture sheet of the first 40 faces and the table's four tags ("facesf.pic").</summary>
    public string FaceSheetName { get; set; } = "";

    /// <summary>GAME DS:3b17: the picture sheet of the other 40 faces, the strips, tags and icons ("faces.pic").</summary>
    public string FaceSheet2Name { get; set; } = "";

    /// <summary>GAME DS:16ae ega_palette: the game's own palette (16 colours and the border), set again after a scene and when the part ends.</summary>
    public byte[] EgaPalette { get; } = new byte[17];

    /// <summary>GAME DS:16c2 scene_name: the name of the scene being played (13 characters and the end mark; "animation.pan" at start).</summary>
    public TextBuffer SceneName { get; } = new(14);

    /// <summary>
    /// GAME DS:42fa cached_city: the city whose picture the picture page holds, -1 for none. It is the box routine's
    /// own variable (the library's <see cref="AnimationState.CachedCity"/>), under the campaign's name for it.
    /// </summary>
    public short CachedCity
    {
        get => Animation.CachedCity;
        set => Animation.CachedCity = value;
    }

    /// <summary>The size of a tag of a text with variants ("*SLOC00" and its end mark).</summary>
    public const int VariantTagSize = 8;

    /// <summary>GAME DS:3a76: the tag of the paragraphs on the sender's city of a coded message ("*SLOC00"); its two digits keep the variant last read.</summary>
    public TextBuffer SenderCityTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3a87: the tag of the paragraphs on the receiver's city ("*RLOC00").</summary>
    public TextBuffer ReceiverCityTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3a98: the tag of the paragraphs on the sender's organisation ("*SORG00").</summary>
    public TextBuffer SenderOrgTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3aa9: the tag of the paragraphs on the receiver's organisation ("*RORG00").</summary>
    public TextBuffer ReceiverOrgTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3ac7: the tag of the filler paragraphs ("*FLUF00").</summary>
    public TextBuffer FillerTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3ae9: the tag of the paragraphs on a helping organisation ("*AIDD00").</summary>
    public TextBuffer HelperTag { get; } = new(VariantTagSize);

    /// <summary>GAME DS:3ad8: the tag of the paragraphs on the alarm a message raised, before its digit is filled in ("*ALRT00").</summary>
    public string AlertTagTemplate { get; set; } = "";

    /// <summary>GAME DS:3aba: the character that opens the message proper in a coded message's text (0x80: the screen shows it encoded).</summary>
    public string StrCodedStart { get; set; } = "";

    /// <summary>GAME DS:3ac5: the character that closes it (0x8f).</summary>
    public string StrCodedEnd { get; set; } = "";

    /// <summary>GAME DS:1daa: the title of the document of the plot's cast ("Master Plan").</summary>
    public string StrMasterPlan { get; set; } = "";

    /// <summary>GAME DS:1db6: the stamp repeated across the top and bottom of the Master Plan ("--SECRET--").</summary>
    public string StrSecret { get; set; } = "";

    /// <summary>GAME DS:1dc1: what the Master Plan writes over an arrested member ("ARRESTED").</summary>
    public string StrArrested { get; set; } = "";

    /// <summary>GAME DS:1dca: what it writes over a member in hiding ("IN HIDING").</summary>
    public string StrInHiding { get; set; } = "";

    /// <summary>GAME DS:1dd4: what it writes over a member turned double agent (" TURNED").</summary>
    public string StrTurned { get; set; } = "";

    /// <summary>GAME DS:1ddc: the title of an organisation's list of agents ("Personnel File").</summary>
    public string StrPersonnelFile { get; set; } = "";

    /// <summary>GAME DS:1deb: what follows the organisation's name in that list's heading (" Action Team").</summary>
    public string StrActionTeam { get; set; } = "";

    /// <summary>GAME DS:1df8: what stands between an agent's name and city in that list (", ").</summary>
    public string StrNameCitySeparator { get; set; } = "";

    /// <summary>GAME DS:1dfb: the list's only line when the organisation has no agent at work ("(No activity)").</summary>
    public string StrNoActivity { get; set; } = "";

    /// <summary>GAME DS:16e0: the line break the text wrapper puts at the end of a wrapped text (a newline).</summary>
    public string LineEnd { get; set; } = "";

    /// <summary>GAME DS:260c: the name of the file of the prose texts ("prose.dta": the scenes of captures, surprises, advice).</summary>
    public string ProseFileName { get; set; } = "";

    /// <summary>GAME DS:2616: the placeholder of a prose text for a value its caller gives ("$RPLC").</summary>
    public string ProseReplacementToken { get; set; } = "";

    /// <summary>GAME DS:262a: the placeholder of a prose text for the hero's name ("$NAME").</summary>
    public string ProseNameToken { get; set; } = "";

    /// <summary>GAME DS:261c: the hero's name put into the prose texts ("Max Remington").</summary>
    public string HeroName { get; set; } = "";

    /// <summary>GAME DS:2630: the placeholder of a prose text for the city the player is in ("$CITY").</summary>
    public string ProseCityToken { get; set; } = "";

    /// <summary>GAME DS:2636: the placeholder of a prose text for the organisation whose building the player is at ("$ORG").</summary>
    public string ProseOrgToken { get; set; } = "";

    /// <summary>GAME DS:263b: the placeholder of a prose text for the player's own agency ("$US").</summary>
    public string ProseAgencyToken { get; set; } = "";

    /// <summary>The placeholders of a clue sentence in CLUES.TXT: the item, the name, the organisation, the city, the role.</summary>
    public const int ClueVariables = 5;

    /// <summary>GAME DS:31b8 clue_var_names: the placeholders of the clue sentences, "$KEY", "$NAME", "$ORG", "$CITY", "$ROLE".</summary>
    public string[] ClueVariableNames { get; } = new string[ClueVariables];

    /// <summary>GAME DS:31ad: the name of the clue sentences' file ("clues.txt"), as <c>cast_clue_code</c> opens it.</summary>
    public string ClueCodeFileName { get; set; } = "";

    /// <summary>GAME DS:31a6: the tag of a decoy's clue sentence before its digits are filled in ("*C0000"), as <c>cast_clue_code</c> builds it.</summary>
    public string ClueCodeTagTemplate { get; set; } = "";

    /// <summary>GAME DS:31fe: the name of the clue sentences' file ("clues.txt"), as <c>append_clue_text</c> opens it.</summary>
    public string ClueFileName { get; set; } = "";

    /// <summary>GAME DS:31eb: the tag of a clue sentence about a face, name, organisation or city before its digits are filled in ("*C00").</summary>
    public string ClueTagTemplate { get; set; } = "";

    /// <summary>GAME DS:31f0: the tag of a real participant's role sentence before its digits are filled in ("*C0000").</summary>
    public string RoleClueTagTemplate { get; set; } = "";

    /// <summary>GAME DS:31f7: the tag of a decoy's role sentence before its digits are filled in ("*C0000").</summary>
    public string DecoyClueTagTemplate { get; set; } = "";

    /// <summary>
    /// GAME DS:968c clue_phrase_index: which way the clue was found (an entry of the "Method:" texts: photo, wiretap,
    /// surveillance ...), from the first digit of the clue sentence last read or from the last search.
    /// </summary>
    public short CluePhraseIndex { get; set; }

    /// <summary>GAME DS:379f list_item_prefix: what comes before a city's name in a menu of cities.</summary>
    public string ListItemPrefix { get; set; } = "";

    /// <summary>GAME DS:37a1 list_item_suffix: what comes after it (the end of the menu's row).</summary>
    public string ListItemSuffix { get; set; } = "";

    /// <summary>GAME DS:372c org_list_prefix: what comes before an organisation's name in a menu of organisations.</summary>
    public string OrgListPrefix { get; set; } = "";

    /// <summary>GAME DS:372e org_list_suffix: what comes after it.</summary>
    public string OrgListSuffix { get; set; } = "";

    /// <summary>
    /// GAME DS:007e: the "one moment" text that lies in front of the persons table. The plot step reads the record
    /// "person -1", which is the end of this text (see <see cref="PlotRules.TryDepart"/>).
    /// </summary>
    public string TextBeforePersons { get; set; } = "";

    // ---- the variables and texts of the library's text and screen routines ----

    /// <summary>GAME DS:a45e: the library's <see cref="ILibraryTextState.TokenTail"/> (the original's tail buffer of <c>replace_substrings</c>).</summary>
    TextBuffer ILibraryTextState.TokenTail => ReplaceTail;

    /// <summary>GAME DS:3a10 text_var_names: the placeholders of the message texts, "$VICTIM" ... "$OBJECT".</summary>
    public string[] VariableNames { get; } = new string[ILibraryTextState.MessageVariables];

    /// <summary>
    /// GAME DS:3a1e text_var_values: the value of each placeholder, as the campaign last wrote it, in the buffers the
    /// pointers lead to (DS:3926, 395a, 3974, 398e, 39a8, 39c2, 39dc): each value is copied over the last (strcpy),
    /// so a shorter one leaves the end of a longer one behind it.
    /// </summary>
    public TextBufferTable VariableValues { get; } = new(VariableValueSizes);

    /// <summary>The sizes of the buffers of <see cref="VariableValues"/> in bytes.</summary>
    public static readonly int[] VariableValueSizes = [52, 26, 26, 26, 26, 26, 52];

    IReadOnlyList<string> ILibraryTextState.VariableNames => VariableNames;

    IList<string> ILibraryTextState.VariableValues => VariableValues;

    /// <summary>GAME DS:3a46: what a text reader writes when a file has no entry under the tag.</summary>
    public string ClearanceNotice { get; set; } = "";

    /// <summary>GAME DS:3a3d: what a text reader puts behind every line.</summary>
    public string LineSeparator { get; set; } = "";

    /// <summary>GAME DS:a61e GMode (MicroProse's name): the letter of the graphics mode the game was started with.</summary>
    public sbyte GMode { get; set; }

    /// <summary>GAME DS:151e: the colour table the driver uses while a photo is drawn.</summary>
    public byte[] PhotoColorMap { get; } = new byte[16];

    IReadOnlyList<Image?> ILibraryScreenState.Sprite => Sprite;

    /// <summary>GAME DS:2750 plane_step_dx: the step across, in pixels, of the travel plane for each of its eight headings (north first, clockwise); signed bytes.</summary>
    public byte[] PlaneStepX { get; } = new byte[8];

    /// <summary>GAME DS:2758 plane_step_dy: the step down for each heading; signed bytes.</summary>
    public byte[] PlaneStepY { get; } = new byte[8];

    /// <summary>
    /// GAME DS:2760: the plane's heading towards a target by the signs of the way across and down: entry
    /// 4 + 3 * sign(across) + sign(down). The original's <c>plane_heading_by_sign</c> (DS:2764) is the middle entry,
    /// indexed with signs -1..1.
    /// </summary>
    public byte[] PlaneHeadings { get; } = new byte[9];

    /// <summary>Records of <see cref="BannerShapes"/>, the end record included.</summary>
    public const int BannerShapeCount = 49;

    /// <summary>GAME DS:1e22 incoming_banner_shapes: the figure behind a bulletin (its repeat records change while it is drawn).</summary>
    public Shape[] BannerShapes { get; } = Records<Shape>(BannerShapeCount);

    /// <summary>GAME DS:1e14: the caption under a bulletin's ticker.</summary>
    public string StrBulletinCaption { get; set; } = "";

    /// <summary>GAME DS:14fe: the colour table handed to the driver while a bulletin is up.</summary>
    public byte[] BulletinColorMap { get; } = new byte[16];

    /// <summary>GAME DS:5554 text_key: the tag of the message text being read ("*MSG0102").</summary>
    public TextBuffer MessageTag { get; } = new(9);

    /// <summary>GAME DS:3b03 msg_tag_template: the template of a message text's tag.</summary>
    public string MessageTemplate { get; set; } = "";

    /// <summary>GAME DS:3afa text_dta_name: the file of the message texts.</summary>
    public string TextFileName { get; set; } = "";

    /// <summary>GAME DS:3d0c status_bar_y: the row the place and clock plate is drawn at.</summary>
    public short StatusBarY { get; set; }

    /// <summary>GAME DS:3d08: what stands between a city's name and its country on the place and clock plate.</summary>
    public string StrPlaceSeparator { get; set; } = "";

    /// <summary>GAME DS:152e: the colour table handed to the driver while the place and clock plate is drawn.</summary>
    public byte[] PlateColorMap { get; } = new byte[16];

    /// <summary>GAME DS:789c CGA: whether the display is CGA (then some texts are drawn in other colours).</summary>
    public bool CGA { get; set; }

    /// <summary>GAME DS:369b: the title of the activity report page.</summary>
    public string StrActivityReportTitle { get; set; } = "";

    /// <summary>The words <c>append_message_text</c> describes an intercepted message with (GAME DS:32af..32e8).</summary>
    public MessageWords MessageText { get; } = new();

    /// <summary>GAME DS:3864: the title of the screen of a decoded message ("Message Decode").</summary>
    public string StrMessageDecode { get; set; } = "";

    /// <summary>GAME DS:3873: the label over the sender's dossier ("FROM:").</summary>
    public string StrDecodeFrom { get; set; } = "";

    /// <summary>GAME DS:3879: the label over the receiver's dossier ("TO:").</summary>
    public string StrDecodeTo { get; set; } = "";

    /// <summary>GAME DS:387d: the label over the message's text ("MESSAGE:").</summary>
    public string StrDecodeMessage { get; set; } = "";

    /// <summary>GAME DS:2696: the two answers to the offer of an exchange of prisoners ("No thanks, Squinty." and "Agree to exchange.").</summary>
    public string StrExchangeChoices { get; set; } = "";

    /// <summary>GAME DS:26c0: the start of the box of an exchange ("You are exchanged for ").</summary>
    public string StrExchangedFor { get; set; } = "";

    /// <summary>GAME DS:26d7: the end of it after the name.</summary>
    public string StrExchangedEnd { get; set; } = "";

    /// <summary>GAME DS:26da: what the player is exchanged for when no arrested suspect can be ("two double agents.").</summary>
    public string StrTwoDoubleAgents { get; set; } = "";

    /// <summary>GAME DS:36cf: the question of the organisation summary ("Which organization ...").</summary>
    public string StrWhichOrganization { get; set; } = "";

    /// <summary>GAME DS:36e7: the heading of an organisation's allies ("Allies:").</summary>
    public string StrAllies { get; set; } = "";

    /// <summary>GAME DS:36f2: the end of a line of a list of organisations and the start of the next.</summary>
    public string StrOrgListLine { get; set; } = "";

    /// <summary>GAME DS:36f5: what a list of allies or associates says when it is empty ("none"); the original always adds it.</summary>
    public string StrNone { get; set; } = "";

    /// <summary>GAME DS:36fa: the heading of an organisation's known buildings ("Known Locations:").</summary>
    public string StrKnownLocations { get; set; } = "";

    /// <summary>GAME DS:370d: between a building's kind and its city (" in ").</summary>
    public string StrBuildingIn { get; set; } = "";

    /// <summary>GAME DS:3712: the end of a line of the list of buildings and the start of the next.</summary>
    public string StrBuildingLine { get; set; } = "";

    /// <summary>GAME DS:3715: the heading of an organisation's associates ("Associates:").</summary>
    public string StrAssociates { get; set; } = "";

    /// <summary>GAME DS:37a3: the title of the list of documents ("Documents").</summary>
    public string StrDocuments { get; set; } = "";

    /// <summary>GAME DS:37ad: its question ("Select Document:").</summary>
    public string StrSelectDocument { get; set; } = "";

    /// <summary>GAME DS:37c0: the line of the Master Plan in that list.</summary>
    public string StrMasterPlanLine { get; set; } = "";

    /// <summary>GAME DS:37ce: what follows an organisation's name in that list (" Personel File", sic).</summary>
    public string StrPersonnelFileLine { get; set; } = "";

    /// <summary>GAME DS:37df: the list's line when the player has no document ("none").</summary>
    public string StrNoDocuments { get; set; } = "";

    /// <summary>Kinds of building an organisation has in a city, by its level 1..5.</summary>
    public const int BuildingKindCount = 5;

    /// <summary>GAME DS:16e8 on: the kinds of building by level (" hideout", " agent", " safehouse", " active cel", " office").</summary>
    public string[] BuildingKinds { get; } = new string[BuildingKindCount];

    /// <summary>GAME DS:37e6: the title of the list of suspects ("Suspect Files").</summary>
    public string StrSuspectFiles { get; set; } = "";

    /// <summary>GAME DS:37f4: the start of the list of suspects (a blank).</summary>
    public string StrSuspectListStart { get; set; } = "";

    /// <summary>GAME DS:37f6: before a suspect's organisation (" of the ").</summary>
    public string StrSuspectOfThe { get; set; } = "";

    /// <summary>GAME DS:37ff: before a suspect's city (" in ").</summary>
    public string StrSuspectIn { get; set; } = "";

    /// <summary>GAME DS:3804: the end of a suspect's line and the start of the next.</summary>
    public string StrSuspectLineEnd { get; set; } = "";

    /// <summary>GAME DS:3808: before a role of a suspect (the font's gap character 0x8c and a blank).</summary>
    public string StrSuspectRoleStart { get; set; } = "";

    /// <summary>GAME DS:380b: after it (a blank).</summary>
    public string StrSuspectRoleEnd { get; set; } = "";

    /// <summary>GAME DS:380d: the note on a suspect under arrest ("(Arrested)").</summary>
    public string StrSuspectArrested { get; set; } = "";

    /// <summary>GAME DS:3818: the note on a suspect in hiding ("(Hiding)").</summary>
    public string StrSuspectHiding { get; set; } = "";

    /// <summary>GAME DS:3821: the note on a suspect turned double agent ("(Turned)").</summary>
    public string StrSuspectTurned { get; set; } = "";

    /// <summary>GAME DS:382a: the note on a suspect in custody ("(In Custody)").</summary>
    public string StrSuspectInCustody { get; set; } = "";

    /// <summary>GAME DS:3837: the end of a line of notes and the start of the next.</summary>
    public string StrSuspectNotesEnd { get; set; } = "";

    /// <summary>GAME DS:383a: the list's line when the player knows of no one ("none").</summary>
    public string StrNoSuspects { get; set; } = "";

    /// <summary>GAME DS:3841: the title of the list of intercepted messages ("Coded Messages").</summary>
    public string StrCodedMessages { get; set; } = "";

    /// <summary>GAME DS:3850: the start of that list (a blank).</summary>
    public string StrCodedListStart { get; set; } = "";

    /// <summary>GAME DS:3852: after the sender's name (a blank).</summary>
    public string StrCodedAfterName { get; set; } = "";

    /// <summary>GAME DS:3854: between the line and the start of the message's text (a line break and "...").</summary>
    public string StrCodedEllipsis { get; set; } = "";

    /// <summary>GAME DS:385a: the end of an entry and the start of the next.</summary>
    public string StrCodedLineEnd { get; set; } = "";

    /// <summary>GAME DS:385d: the list's line when there is no message to decode ("none").</summary>
    public string StrNoCodedMessages { get; set; } = "";

    /// <summary>GAME DS:a624 entry_mode: how the part was entered (from the hand-off's game mode); 0 when not back from a mission.</summary>
    public short EntryMode { get; set; }

    /// <summary>GAME DS:36b3: the title of the list of wiretaps ("WireTaps").</summary>
    public string StrWireTaps { get; set; } = "";

    /// <summary>GAME DS:36bd: between a tapped organisation and its city.</summary>
    public string StrTapSlash { get; set; } = "";

    /// <summary>GAME DS:36bf: between the place and the tap's strength (" ... ").</summary>
    public string StrTapDots { get; set; } = "";

    /// <summary>GAME DS:36c5: after the strength ("%" and a line break).</summary>
    public string StrTapPercent { get; set; } = "";

    /// <summary>GAME DS:36c8: the list's line when no building is tapped ("none").</summary>
    public string StrNoWireTaps { get; set; } = "";

    /// <summary>GAME DS:5524 percent_text: a wiretap's strength turned into text.</summary>
    public TextBuffer PercentText { get; } = new(16);

    /// <summary>GAME DS:263f: the scene of the player's capture ("surndr01.pan").</summary>
    public string StrSurrenderScene { get; set; } = "";

    /// <summary>GAME DS:2655: the scene of a jail break ("warning.pan").</summary>
    public string StrWarningScene { get; set; } = "";

    /// <summary>GAME DS:2661: the start of the box of a jail break ("JailBreak in ").</summary>
    public string StrJailBreakIn { get; set; } = "";

    /// <summary>GAME DS:266f: after the city ("!" and a line break).</summary>
    public string StrJailBreakCityEnd { get; set; } = "";

    /// <summary>GAME DS:2672: after the organisation, before the prisoner (" agents free ").</summary>
    public string StrJailBreakAgentsFree { get; set; } = "";

    /// <summary>GAME DS:2680: after the prisoner ("!" and a line break).</summary>
    public string StrJailBreakEnd { get; set; } = "";

    /// <summary>GAME DS:2683: the scene of the player's escape ("escape.pan").</summary>
    public string StrEscapeScene { get; set; } = "";

    /// <summary>GAME DS:264c: the tag of the prose of the player's questioning ("*grilled"), a buffer the prose reader may change.</summary>
    public TextBuffer GrilledProseKey { get; } = new(9);

    /// <summary>GAME DS:268e: the tag of the prose of the player's escape ("*escape").</summary>
    public TextBuffer EscapeProseKey { get; } = new(8);

    /// <summary>GAME DS:15ae: the colour table of the capture scene.</summary>
    public byte[] SurrenderColorMap { get; } = new byte[16];

    /// <summary>GAME DS:167e: the colour table of the jail-break scene.</summary>
    public byte[] WarningColorMap { get; } = new byte[16];

    /// <summary>GAME DS:159e: the colour table of the escape scene.</summary>
    public byte[] EscapeColorMap { get; } = new byte[16];

    /// <summary>GAME DS:54ec leaving_building: set when the player is to leave the building screen.</summary>
    public short LeavingBuilding { get; set; }

    /// <summary>GAME DS:245a: the scene of an interrogation ("intrgate.pan").</summary>
    public string StrInterrogationScene { get; set; } = "";

    /// <summary>GAME DS:2467: the start of the accusation ("You accuse ").</summary>
    public string StrYouAccuse { get; set; } = "";

    /// <summary>GAME DS:2473: after the suspect's name, before the roles the player knows ("of taking part in the plot ...").</summary>
    public string StrAccuseRoles { get; set; } = "";

    /// <summary>GAME DS:24ae: after each role.</summary>
    public string StrAccuseRoleEnd { get; set; } = "";

    /// <summary>GAME DS:24b2: the start of the box of a suspect released for want of evidence.</summary>
    public string StrNoEvidence { get; set; } = "";

    /// <summary>GAME DS:24d0: after the name, for a man (".\nHe").</summary>
    public string StrNoEvidenceHe { get; set; } = "";

    /// <summary>GAME DS:24d5: after the name, for a woman (".\nShe").</summary>
    public string StrNoEvidenceShe { get; set; } = "";

    /// <summary>GAME DS:24db: the end of that box (" will have to be released.").</summary>
    public string StrReleased { get; set; } = "";

    /// <summary>GAME DS:2510: after a message put to the suspect ("," you ask. ").</summary>
    public string StrYouAsk { get; set; } = "";

    /// <summary>GAME DS:251d: the suspect's confession of a message.</summary>
    public string StrConfesses { get; set; } = "";

    /// <summary>GAME DS:254f: the suspect's denial of a message.</summary>
    public string StrDoesNotRemember { get; set; } = "";

    /// <summary>GAME DS:258c: the box of the MasterMind who lists the plot's participants.</summary>
    public string StrMastermindBreaks { get; set; } = "";

    /// <summary>GAME DS:25cf: the scene of the suspect taken to jail ("injail.pan").</summary>
    public string StrJailScene { get; set; } = "";

    /// <summary>GAME DS:25da: after the suspect's name (" is taken to the local jail.").</summary>
    public string StrTakenToJail { get; set; } = "";

    /// <summary>GAME DS:25f8: the scene of the chief's comment ("briefing.pan").</summary>
    public string StrBriefingScene { get; set; } = "";

    /// <summary>GAME DS:24f7: the tag of the prose of an arrest ("*inter1").</summary>
    public TextBuffer ArrestProseKey { get; } = new(8);

    /// <summary>GAME DS:24ff: the tag of the prose of a suspect turned double agent ("*doublea").</summary>
    public TextBuffer DoubleAgentProseKey { get; } = new(9);

    /// <summary>GAME DS:2508: the tag of the prose of a message put to a suspect ("*inter2").</summary>
    public TextBuffer QuestionProseKey { get; } = new(8);

    /// <summary>GAME DS:2605: the tag of the chief's comment on a capture ("*nice0"), whose digit the prose reader changes for good (CA-41).</summary>
    public TextBuffer CaptureProseKey { get; } = new(7);

    /// <summary>GAME DS:161e: the colour table of the interrogation scene.</summary>
    public byte[] InterrogationColorMap { get; } = new byte[16];

    /// <summary>GAME DS:166e: the colour table of the jail scene.</summary>
    public byte[] JailColorMap { get; } = new byte[16];

    /// <summary>GAME DS:168e: the colour table of the chief's scene.</summary>
    public byte[] BriefingColorMap { get; } = new byte[16];

    /// <summary>GAME DS:2b9e bit_count_table: for each number 0..31 how many of its bits are set.</summary>
    public byte[] BitCount { get; } = new byte[32];

    /// <summary>GAME DS:9fac FaceX (MicroProse's name): the column of the face drawn beside a box (the box routine's backdrop argument).</summary>
    public short FaceX
    {
        get => Animation.BackdropArgB;
        set => Animation.BackdropArgB = value;
    }

    /// <summary>GAME DS:9fb2 FaceY (MicroProse's name): the row of that face.</summary>
    public short FaceY
    {
        get => Animation.BackdropArgC;
        set => Animation.BackdropArgC = value;
    }

    /// <summary>
    /// GAME DS:16e2: what stands for the words of a message the player cannot read (" ... "). The library's screens
    /// use the same text: it is kept with their texts.
    /// </summary>
    public string StrEllipsis
    {
        get => DossierTexts.Ellipsis;
        set => DossierTexts.Ellipsis = value;
    }

    /// <summary>GAME DS:3143: the title of the screen of a new clue ("Clue").</summary>
    public string StrClueTitle { get; set; } = "";

    /// <summary>GAME DS:3148: before where the clue was found ("Source: ").</summary>
    public string StrClueSource { get; set; } = "";

    /// <summary>GAME DS:3152: between the organisation and the city.</summary>
    public string StrClueSlash { get; set; } = "";

    /// <summary>GAME DS:3154: after the city (a line break).</summary>
    public string StrClueSourceEnd { get; set; } = "";

    /// <summary>GAME DS:3156: before the way the clue was found ("Method: ").</summary>
    public string StrClueMethod { get; set; } = "";

    /// <summary>GAME DS:3161: the heading of the related clues ("Related Clues:").</summary>
    public string StrRelatedClues { get; set; } = "";

    /// <summary>GAME DS:3171: after each related clue (a line break).</summary>
    public string StrRelatedClueEnd { get; set; } = "";

    /// <summary>GAME DS:3173: what stands under the heading when there are none ("...none").</summary>
    public string StrNoRelatedClues { get; set; } = "";

    /// <summary>GAME DS:31c2: the full stop after a clue's subject.</summary>
    public string StrCluePeriod { get; set; } = "";

    /// <summary>Joining phrases of clue sentences: five per kind of clue item.</summary>
    public const int ActivityPhraseCount = 40;

    /// <summary>GAME DS:2b3e activity_phrase_table: the phrases that join a clue's item and subject (" tied to " ...), five per kind of item.</summary>
    public string[] ActivityPhrases { get; } = new string[ActivityPhraseCount];

    /// <summary>Ways a clue can be found.</summary>
    public const int ClueSourceCount = 7;

    /// <summary>GAME DS:317c clue_phrase_table: the ways a clue is found, by <see cref="CluePhraseIndex"/> ("Clandestine Photo" ... "Local Authorities").</summary>
    public string[] ClueSources { get; } = new string[ClueSourceCount];

    /// <summary>GAME DS:83c4 last_hint: what the last search of a building was told to look for, kept for the clue screen (1: a wiretap).</summary>
    public short LastHint { get; set; }

    /// <summary>GAME DS:3730: the question of the city summaries ("Which city ...").</summary>
    public string StrWhichCity { get; set; } = "";

    /// <summary>GAME DS:3740: the heading of the organisations known in a city ("Known Locations:").</summary>
    public string StrCityKnownLocations { get; set; } = "";

    /// <summary>GAME DS:3753: an organisation's line when its building there is not known ("Unknown building.").</summary>
    public string StrUnknownBuilding { get; set; } = "";

    /// <summary>GAME DS:3765: the end of a line of those lists and the start of the next.</summary>
    public string StrCityListLine { get; set; } = "";

    /// <summary>GAME DS:3768: what an empty list of a city summary says ("none").</summary>
    public string StrCityNone { get; set; } = "";

    /// <summary>GAME DS:376e: the heading of the suspects seen in a city ("Suspects:").</summary>
    public string StrCitySuspects { get; set; } = "";

    /// <summary>GAME DS:377a: between a suspect and his organisation.</summary>
    public string StrCitySuspectSlash { get; set; } = "";

    /// <summary>GAME DS:377c: before a role of a suspect (a line break, "...", the font's gap character 0x8c).</summary>
    public string StrCitySuspectRole { get; set; } = "";

    /// <summary>GAME DS:3783: the end of a suspect's entry and the start of the next.</summary>
    public string StrCitySuspectLineEnd { get; set; } = "";

    /// <summary>GAME DS:378d: the heading of the clues that point to the city ("Clues:").</summary>
    public string StrCityClues { get; set; } = "";

    /// <summary>GAME DS:3795: before each clue ("-").</summary>
    public string StrCityClueStart { get; set; } = "";

    /// <summary>GAME DS:3797: after each clue (a line break).</summary>
    public string StrCityClueEnd { get; set; } = "";

    /// <summary>GAME DS:3d0e: the note on the clue list's plate of an arrested member ("ARRESTED").</summary>
    public string StrCluesArrested { get; set; } = "";

    /// <summary>GAME DS:3d17: the note of a member turned double agent ("TURNED").</summary>
    public string StrCluesTurned { get; set; } = "";

    /// <summary>GAME DS:3d1e: the heading under the clue list ("Clues").</summary>
    public string StrCluesHeading { get; set; } = "";

    /// <summary>GAME DS:3d2e: the title of a member's clues ("Clue").</summary>
    public string StrClueDetailTitle { get; set; } = "";

    /// <summary>GAME DS:3d33: before where a clue was found ("Source: ").</summary>
    public string StrClueDetailSource { get; set; } = "";

    /// <summary>GAME DS:3d3d: between the organisation and the city.</summary>
    public string StrClueDetailSlash { get; set; } = "";

    /// <summary>GAME DS:3d3f: after the city (a line break).</summary>
    public string StrClueDetailSourceEnd { get; set; } = "";

    /// <summary>GAME DS:3d41: after the clue's sentence (a line break).</summary>
    public string StrClueDetailEnd { get; set; } = "";

    /// <summary>GAME DS:3d43: the note beside the photo of an arrested member ("ARRESTED").</summary>
    public string StrClueDetailArrested { get; set; } = "";

    /// <summary>GAME DS:3d4c: of a member in hiding ("IN HIDING").</summary>
    public string StrClueDetailInHiding { get; set; } = "";

    /// <summary>GAME DS:3d56: of a member turned double agent (" TURNED").</summary>
    public string StrClueDetailTurned { get; set; } = "";

    /// <summary>GAME DS:3d24 row_colours: the background colours of the clue list's rows, by row number modulo 4.</summary>
    public short[] RowColours { get; } = new short[4];

    /// <summary>GAME DS:3d2c clue_list_selection: the highlighted row of the clue list; kept between visits.</summary>
    public short ClueListSelection { get; set; }

    /// <summary>GAME DS:23fe: the chief's scene after a plot object was found in a raid ("briefing.pan").</summary>
    public string StrInfiltrationBriefingScene { get; set; } = "";

    /// <summary>GAME DS:240b: the chief's praise before the object ("Good job Max, You found the").</summary>
    public string StrFoundObject { get; set; } = "";

    /// <summary>GAME DS:2428: after the object ("That should really slow down their operation.").</summary>
    public string StrFoundObjectEnd { get; set; } = "";

    /// <summary>The words of a person's dossier card (GAME DS:3208..32ae, the rank names at DS:2d48).</summary>
    public DossierWords Dossier { get; } = new();

    /// <summary>The boxes of what the player learned of a person, and the words of alertness (GAME DS:2bd8..2d47, 2769..27a4).</summary>
    public KnowledgeWords Knowledge { get; } = new();

    /// <summary>The bulletins of the plot's progress (GAME DS:3b22..3cf3, 3886).</summary>
    public BulletinWords Bulletins { get; } = new();

    /// <summary>GAME DS:93ee pic (MicroProse's name): what the screen shows; -1 for a clean screen (no picture of a place).</summary>
    public short Pic { get; set; }

    /// <summary>GAME DS:16d0: the file the campaign is saved to when the part hands over to another ("env.sve").</summary>
    public string StrHandOffSave { get; set; } = "";

    /// <summary>GAME DS:16d8: the picture shown when the player leaves the game ("ad.pic").</summary>
    public string StrGoodbyePicture { get; set; } = "";

    private static T[] Records<T>(int count) where T : new() => [.. Enumerable.Range(0, count).Select(_ => new T())];
}
