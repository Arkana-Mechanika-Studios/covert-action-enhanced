using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Chase;

/// <summary>Which car chase the campaign asked for (the hand-off's "next part" byte picks it).</summary>
// orig: CHASE_TAIL .. CHASE_FLEE (src/c/include/chase.h)
public enum ChaseMode : short
{
    /// <summary>The player's two cars tail the quarry, car 0, to wherever it is going ("Follow car").</summary>
    Tail = 0,

    /// <summary>A second tailing chase; the code treats it as <see cref="Tail"/>, except that it prints no result text.</summary>
    Variant = 1,

    /// <summary>The player drives car 0 and the two cars behind are pursuers ("you are being followed").</summary>
    Flee = 2,
}

/// <summary>How a car chase ended, handed back to the campaign.</summary>
// orig: OUTCOME_* (src/c/include/chase.h)
public enum ChaseOutcome : short
{
    /// <summary>Tailing: the quarry got away or went in unseen. Fleeing: the player reached a building.</summary>
    Lost = 0,

    /// <summary>Tailing: the quarry was seen pulling over and going in. Fleeing: the pursuers caught the player.</summary>
    Spotted = 1,

    /// <summary>The player ran the quarry off the road and arrested him (F1).</summary>
    Arrest = 2,
}

/// <summary>A pair of offsets, in minimap pixels, where the road segment drawn for one direction starts and ends.</summary>
// orig: struct line_ends (src/c/include/chase.h); road_x_ends CHASE DS:17ba, road_y_ends DS:17ca
public sealed class LineEnds
{
    /// <summary>The start offset.</summary>
    public short From { get; set; }

    /// <summary>The end offset.</summary>
    public short To { get; set; }
}

/// <summary>Where one kerb strip of a 32 x 32 street tile starts, and how long it is, along one axis.</summary>
// orig: struct span (src/c/include/chase.h); tile_edge_x CHASE DS:189c, tile_edge_y DS:18ac
public sealed class TileSpan
{
    /// <summary>The offset inside the tile, in pixels.</summary>
    public short Off { get; set; }

    /// <summary>The length, in pixels.</summary>
    public short Size { get; set; }
}

/// <summary>The two colours a car picture of the offer screen is painted in, for one level of conspicuousness.</summary>
// orig: struct color_pair (src/c/include/chase.h); car_level_colors CHASE DS:1862
public sealed class ColorPair
{
    /// <summary>The colour that replaces the picture's light body colour (11).</summary>
    public short ForLight { get; set; }

    /// <summary>The colour that replaces its dark body colour (3).</summary>
    public short ForDark { get; set; }
}

/// <summary>A rectangle of a picture sheet, both corners inclusive.</summary>
// orig: struct rect4 (src/c/include/chase.h); gauge_rects CHASE DS:170c
public sealed class SheetRect
{
    /// <summary>Left edge.</summary>
    public short X1 { get; set; }

    /// <summary>Top edge.</summary>
    public short Y1 { get; set; }

    /// <summary>Right edge, inclusive.</summary>
    public short X2 { get; set; }

    /// <summary>Bottom edge, inclusive.</summary>
    public short Y2 { get; set; }
}

/// <summary>
/// The state of the car chase (the original's CHASE program): the city map, the three cars, the locations the
/// quarry drives between, the street view's camera, the dashboard's variables, and the variables of the routines this
/// part shares with the rest of the game.
/// </summary>
/// <remarks>
/// <para>
/// The city is 24 x 24 tiles of 32 world units. Each tile is a cell of <see cref="MapCells"/>: the low four bits are
/// its sides (set = open), the high four its ground picture. Sixteen locations (<see cref="LocX"/>, <see cref="LocY"/>)
/// are the buildings of the city's organisations; the quarry drives from <see cref="StartLoc"/> to
/// <see cref="DestLoc"/>.
/// </para>
/// <para>
/// Names are those of the reconstructed chase.h, MicroProse's own where the Amiga symbols give them (<c>diff</c>,
/// <c>Night</c>, <c>CGA</c>, <c>Status</c>, <c>Gtime</c>, <c>InCity</c>, <c>AtLoc</c>, <c>skills</c>, <c>Rp1</c>,
/// <c>RpBack</c>, <c>RpSprite</c>, <c>RpGrid</c>), in C# capitalisation. Nothing here has a built-in value: the
/// tables and texts are the initialised data of the player's CHASE.EXE.
/// </para>
/// </remarks>
// orig: CHASE DGROUP (src/c/include/chase.h, docs/data-model/chase.md)
public sealed class ChaseState : ILibraryState
{
    /// <summary>The city is this many tiles wide and high.</summary>
    public const int MapTiles = 24;

    /// <summary>World units per tile.</summary>
    public const int TileUnits = 32;

    /// <summary>The number of cars.</summary>
    public const int CarCount = 3;

    /// <summary>The number of locations (0 is never a destination).</summary>
    public const int Locations = 16;

    /// <summary>The number of cities in the campaign tables.</summary>
    public const int Cities = 16;

    /// <summary>The value of <see cref="DoneFlag"/> while the chase runs.</summary>
    public const short Running = 0;

    /// <summary><see cref="DoneFlag"/>: the chase ended the normal way.</summary>
    public const short DoneNormal = 1;

    /// <summary><see cref="DoneFlag"/>: the quarry, very suspicious and unseen, got away.</summary>
    public const short DoneEscaped = 2;

    /// <summary><see cref="DoneFlag"/>: the player quit the whole game (Alt-Q).</summary>
    public const short DoneQuit = -1;

    /// <summary>The drawing ports point at their records, as in the program's initialised data.</summary>
    public ChaseState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
        RpSprite = RpSpriteX;
        RpGrid = RpGridX;
        GaugeSheet = GaugeSheetX;
    }

    // the library's names for two of the part's variables (ILibraryState)
    Options ILibraryState.Status
    {
        get => Status;
        set => Status = value;
    }

    short ILibraryState.Quit
    {
        get => DoneFlag;
        set => DoneFlag = value;
    }

    // ---- the chase ----

    /// <summary>CHASE DS:16f4 chase_mode: which chase this is.</summary>
    public ChaseMode ChaseMode { get; set; }

    /// <summary>CHASE DS:16f6 game_speed: 0 slow, 1 medium, 2 fast (keys S, D, F); a frame waits 8, 4 or 0 display frames.</summary>
    public short GameSpeed { get; set; }

    /// <summary>CHASE DS:4288 diff (MicroProse's name): the difficulty 0..3, the campaign's level less the agent's driving skill, plus one.</summary>
    public short Diff { get; set; }

    /// <summary>CHASE DS:4286 Night: the clock is between 20:00 and 08:00; the quarry is seen only from two tiles away.</summary>
    public bool Night { get; set; }

    /// <summary>CHASE DS:4560 CGA: the four-colour display is in use (lighter roads, outline boxes).</summary>
    public bool CGA { get; set; }

    /// <summary>CHASE DS:455e Status: this part's copy of the option flags.</summary>
    public Options Status { get; set; }

    /// <summary>CHASE DS:647e skills: the agent's skills, two bits each, and the game speeds above them (the hand-off's word 0x4e).</summary>
    public short Skills { get; set; }

    /// <summary>CHASE DS:428e Gtime: the campaign's clock, in minutes since the case began.</summary>
    public short Gtime { get; set; }

    /// <summary>CHASE DS:6844 InCity: the city the chase takes place in, an index into the campaign's city table.</summary>
    public short InCity { get; set; }

    /// <summary>CHASE DS:69d2 AtLoc: the organisation the player is at; the quarry's destination, and the building entered at the end.</summary>
    public short AtLoc { get; set; }

    /// <summary>CHASE DS:3e80 alert_level: how alert the organisation is (the hand-off's word 0x48); the higher, the sooner the quarry grows suspicious.</summary>
    public short AlertLevel { get; set; }

    /// <summary>CHASE DS:6846 alert_limit: the suspicion the quarry tolerates, from the difficulty and <see cref="AlertLevel"/>.</summary>
    public short AlertLimit { get; set; }

    /// <summary>CHASE DS:6842 alert_ratio: an opposing car's suspicion divided by <see cref="AlertLimit"/>; handed back as a percentage.</summary>
    public short AlertRatio { get; set; }

    /// <summary>CHASE DS:50de frame: frames since the chase began; 60 make a game minute.</summary>
    public short Frame { get; set; }

    /// <summary>CHASE DS:50dc outcome: how the chase ended.</summary>
    public ChaseOutcome Outcome { get; set; }

    /// <summary>CHASE DS:66ce done_flag: <see cref="Running"/> while the chase goes on; otherwise why it stopped.</summary>
    public short DoneFlag { get; set; }

    /// <summary>CHASE DS:17da cur_car: the car the keys steer (1 or 2 when tailing; 0 when fleeing).</summary>
    public short CurCar { get; set; }

    /// <summary>CHASE DS:50c8 view_frame: the cockpit picture 0..7; it advances with every unit the player's car drives.</summary>
    public short ViewFrame { get; set; }

    /// <summary>CHASE DS:50cc ram_ready: a tail car meets the quarry head-on within 8 units; F1 now makes the arrest.</summary>
    public bool RamReady { get; set; }

    /// <summary>CHASE DS:5c62 arrival_timer: frames left after the quarry reached its destination; 0 while it is on its way.</summary>
    public short ArrivalTimer { get; set; }

    /// <summary>CHASE DS:4784 watch_count: frames a tail car watched the quarry after its arrival; more than 16 end the chase.</summary>
    public short WatchCount { get; set; }

    /// <summary>CHASE DS:42cc weave_toggle: alternates the side a pursuer swerves to.</summary>
    public short WeaveToggle { get; set; }

    /// <summary>CHASE DS:5bfa sound_param: the gear of the current car, the second number handed to the sound driver.</summary>
    public short SoundParam { get; set; }

    /// <summary>CHASE DS:5bfc campaign_mode: the chase is part of a campaign (the original always sets it): the map comes from the city and the locations are the organisations.</summary>
    public bool CampaignMode { get; set; }

    /// <summary>CHASE DS:5c00 map_seed: mixed into the map's seed; nothing in CHASE writes it, so it stays 0.</summary>
    public short MapSeed { get; set; }

    /// <summary>CHASE DS:626a mark_follow_all: never written (0); were it set, every opposing car would be seen all the time.</summary>
    public bool MarkFollowAll { get; set; }

    /// <summary>CHASE DS:4290 cars: the quarry (or the player's car when fleeing) and the two tail cars (or pursuers).</summary>
    public Car[] Cars { get; } = [new(), new(), new()];

    /// <summary>CHASE DS:3e84 sight_flag: set by the line-of-sight test; false only when the two cars stand on the same spot.</summary>
    public bool SightFlag { get; set; }

    // ---- the city ----

    /// <summary>CHASE DS:17a0 dir_dx: the x step of each heading (0, 1, 0, -1).</summary>
    public short[] DirDx { get; } = new short[4];

    /// <summary>CHASE DS:17a8 dir_dy: the y step of each heading (-1, 0, 1, 0).</summary>
    public short[] DirDy { get; } = new short[4];

    /// <summary>CHASE DS:17b0 speed_pattern: per gear 0..4, a bit for each of the six frame phases (frame % 6) in which the car moves a unit.</summary>
    public short[] SpeedPattern { get; } = new short[5];

    /// <summary>
    /// CHASE DS:4788 map_cells: the 24 x 24 tiles, column by column (index x * 24 + y). Low four bits: the sides, bit n
    /// for side n (<see cref="Directions"/>), set = open; high four bits: the ground picture 0..15.
    /// </summary>
    public byte[] MapCells { get; } = new byte[MapTiles * MapTiles];

    /// <summary>CHASE DS:17ba road_x_ends: per direction, the x offsets of the road segment the map generator paints over.</summary>
    public LineEnds[] RoadXEnds { get; } = [new(), new(), new(), new()];

    /// <summary>CHASE DS:17ca road_y_ends: per direction, the y offsets of that segment.</summary>
    public LineEnds[] RoadYEnds { get; } = [new(), new(), new(), new()];

    /// <summary>CHASE DS:189c tile_edge_x: per side of a tile, the x offset and width of its kerb strip in the street view.</summary>
    public TileSpan[] TileEdgeX { get; } = [new(), new(), new(), new()];

    /// <summary>CHASE DS:18ac tile_edge_y: per side, the y offset and height of the kerb strip.</summary>
    public TileSpan[] TileEdgeY { get; } = [new(), new(), new(), new()];

    /// <summary>CHASE DS:6802 loc_x: the x of each location, in world units.</summary>
    public short[] LocX { get; } = new short[Locations];

    /// <summary>CHASE DS:6822 loc_y: the y of each location.</summary>
    public short[] LocY { get; } = new short[Locations];

    /// <summary>CHASE DS:5c02 start_loc: the location the quarry starts from.</summary>
    public short StartLoc { get; set; }

    /// <summary>CHASE DS:428c dest_loc: the location the quarry drives to.</summary>
    public short DestLoc { get; set; }

    /// <summary>CHASE DS:69ce dest_x: the destination's x.</summary>
    public short DestX { get; set; }

    /// <summary>CHASE DS:69d0 dest_y: the destination's y.</summary>
    public short DestY { get; set; }

    /// <summary>
    /// CHASE DS:4562 ct (MicroProse's name): this part's copy of the campaign's 16 cities. CHASE reads only
    /// <see cref="City.OrgClues"/>: bit n = the player knows the organisation at location n.
    /// </summary>
    public City[] Ct { get; } = [.. Enumerable.Range(0, Cities).Select(_ => new City())];

    /// <summary>
    /// CHASE DS:648a org (MicroProse's name): this part's copy of the campaign's 16 organisations. CHASE reads only
    /// <see cref="Records.Org.Sname"/>, the label of location n on the map.
    /// </summary>
    public Org[] Org { get; } = [.. Enumerable.Range(0, Locations).Select(_ => new Org())];

    // ---- the campaign, as this part keeps it ----

    /// <summary>The most cast records this part has room for.</summary>
    public const int CastRoom = 32;

    /// <summary>The most event records this part has room for.</summary>
    public const int EventRoom = 64;

    /// <summary>CHASE DS:42cc pl (MicroProse's name): this part's copy of the campaign's head record; CHASE never reads it.</summary>
    public Plot Pl { get; } = new();

    /// <summary>CHASE DS:5c6a ac: this part's copy of the cast (<see cref="NACTIVITY"/> of the 32 are filled); never read.</summary>
    public Activity[] Ac { get; } = [.. Enumerable.Range(0, CastRoom).Select(_ => new Activity())];

    /// <summary>CHASE DS:673e ac2: this part's copy of the cast's second records; never read.</summary>
    public Activity2[] Ac2 { get; } = [.. Enumerable.Range(0, CastRoom).Select(_ => new Activity2())];

    /// <summary>CHASE DS:50e0 ev: this part's copy of the plot's events (<see cref="NEVENT"/> of the 64 are filled); never read.</summary>
    public Event[] Ev { get; } = [.. Enumerable.Range(0, EventRoom).Select(_ => new Event())];

    /// <summary>CHASE DS:5c06 TheCrime: the case number (the hand-off's word 0x26); only kept.</summary>
    public short TheCrime { get; set; }

    /// <summary>CHASE DS:5c0a TheOrg1: the plot's second organisation (word 0x28); only kept.</summary>
    public short TheOrg1 { get; set; }

    /// <summary>CHASE DS:5c0e TheOrg2: the plot's third organisation (word 0x2a); only kept.</summary>
    public short TheOrg2 { get; set; }

    /// <summary>CHASE DS:5bee TheCity1: the second organisation's city (word 0x2c); only kept.</summary>
    public short TheCity1 { get; set; }

    /// <summary>CHASE DS:5bf0 TheCity2: the third organisation's city (word 0x2e); only kept.</summary>
    public short TheCity2 { get; set; }

    /// <summary>CHASE DS:4786 Nperson: person records in use (word 0x32); only kept.</summary>
    public short Nperson { get; set; }

    /// <summary>CHASE DS:5be6 Nloc: link records in use (word 0x34); only kept.</summary>
    public short Nloc { get; set; }

    /// <summary>CHASE DS:5be8 ChronTime: the plot's current step (word 0x36); only kept.</summary>
    public short ChronTime { get; set; }

    /// <summary>CHASE DS:684c MacGuffin: the hand-off's word 0x38; only kept.</summary>
    public short MacGuffin { get; set; }

    /// <summary>CHASE DS:5be4 TheSeed: the menus' random seed (word 0x3c); only kept.</summary>
    public short TheSeed { get; set; }

    /// <summary>CHASE DS:5c66 NACTIVITY: the number of cast records (word 0x3e).</summary>
    public short NACTIVITY { get; set; }

    /// <summary>CHASE DS:5bfe NEVENT: the number of event records (word 0x40).</summary>
    public short NEVENT { get; set; }

    /// <summary>CHASE DS:684a Sprung: the person under investigation (word 0x42); only kept.</summary>
    public short Sprung { get; set; }

    /// <summary>CHASE DS:5bea GMode: the graphics driver's letter as a word (word 0x44); only kept.</summary>
    public short GMode { get; set; }

    /// <summary>CHASE DS:5be0 NextSyn: message records in use (word 0x46); only kept.</summary>
    public short NextSyn { get; set; }

    /// <summary>CHASE DS:5c64 Doubles: one bit per city (word 0x4a); only kept.</summary>
    public short Doubles { get; set; }

    /// <summary>CHASE DS:6848 InterPol: the hand-off's word 0x4c; only kept.</summary>
    public short InterPol { get; set; }

    // ---- the street view and the dashboard ----

    /// <summary>CHASE DS:23ba cam_x: left edge of the street view, in world units; 0 means "not placed yet".</summary>
    public short CamX { get; set; }

    /// <summary>CHASE DS:23bc cam_y: top edge of the street view.</summary>
    public short CamY { get; set; }

    /// <summary>CHASE DS:23be cam_tile_x: the tile column the street view's tiles were last drawn from.</summary>
    public short CamTileX { get; set; }

    /// <summary>CHASE DS:23c0 cam_tile_y: the tile row they were last drawn from.</summary>
    public short CamTileY { get; set; }

    /// <summary>CHASE DS:18c2 clock_text: the dashboard clock, "h:mm:ss"; the digits are written into the template.</summary>
    public char[] ClockText { get; } = new char[7];

    /// <summary>CHASE DS:18bc speed_prefix: an empty text put before the dashboard's speed (the unit is part of the picture).</summary>
    public string SpeedPrefix { get; set; } = "";

    /// <summary>CHASE DS:18be shown_view_frame: the cockpit picture drawn last, -1 at start.</summary>
    public short ShownViewFrame { get; set; }

    /// <summary>CHASE DS:18c0 shown_needle_y: the height of the gauge needle drawn last, -1 when none is drawn.</summary>
    public short ShownNeedleY { get; set; }

    /// <summary>CHASE DS:23c2 gauge_depth: 63 less the quarry's distance (capped at 63); its square places the needle.</summary>
    public short GaugeDepth { get; set; }

    /// <summary>CHASE DS:23c4 gauge_dist: the quarry's distance in sight, times 1.5; 0 when it is not in sight.</summary>
    public short GaugeDist { get; set; }

    /// <summary>CHASE DS:23c6 needle_x: the needle's x in the cockpit window.</summary>
    public short NeedleX { get; set; }

    /// <summary>CHASE DS:23c8 needle_y: the needle's y in the cockpit window.</summary>
    public short NeedleY { get; set; }

    /// <summary>CHASE DS:23ca quarry_dx: the quarry's x less the current car's, for the tracking device.</summary>
    public short QuarryDx { get; set; }

    /// <summary>CHASE DS:23cc quarry_dy: the quarry's y less the current car's.</summary>
    public short QuarryDy { get; set; }

    /// <summary>CHASE DS:23ce dash_scratch: first the needle picture's number 0..15, then the alert bar's length in pixels.</summary>
    public short DashScratch { get; set; }

    /// <summary>CHASE DS:170c gauge_rects: where each of the 16 needle pictures lies in chase.pic.</summary>
    public SheetRect[] GaugeRects { get; } = [.. Enumerable.Range(0, 16).Select(_ => new SheetRect())];

    /// <summary>CHASE DS:5c12 gauge_images: the 16 needle pictures, cut from chase.pic when the part starts.</summary>
    public Image?[] GaugeImages { get; } = new Image?[16];

    /// <summary>The numbers the original knows this part's images by (for the layout and the oracle tests).</summary>
    public ImageRegister Images { get; } = new();

    /// <summary>CHASE DS:16fa palette_chase: the colours while driving (16 palette registers and the border).</summary>
    public byte[] PaletteChase { get; } = new byte[17];

    /// <summary>CHASE DS:178e palette_plain: the colours of the dialogs and of the turn animation.</summary>
    public byte[] PalettePlain { get; } = new byte[17];

    // ---- the start and the end of the chase ----

    /// <summary>CHASE DS:67fe gfx_letter: the graphics driver's letter from the hand-off ('C' is the four-colour display).</summary>
    public sbyte GfxLetter { get; set; }

    /// <summary>CHASE DS:178c chase_pic_buffer: the number of the memory the driver gave for page 3 (chase.pic).</summary>
    public short ChasePicBuffer { get; set; }

    /// <summary>CHASE DS:455c random_seed: the seed this part drew from the clock.</summary>
    public short RandomSeed { get; set; }

    /// <summary>CHASE DS:0074: the file name of the sheet of ground tiles, car sprites and dashboard parts ("chase.pic").</summary>
    public string StrChasePic { get; set; } = "";

    /// <summary>CHASE DS:007e: the file name of the eight windscreen pictures ("street.pic").</summary>
    public string StrStreetPic { get; set; } = "";

    /// <summary>CHASE DS:0089: the result when the tail lost the quarry ("We've totally lost track of this guy ...").</summary>
    public string StrLostTrack { get; set; } = "";

    /// <summary>CHASE DS:00d5: "He", first word of the escape text for a man.</summary>
    public string StrHeTurns { get; set; } = "";

    /// <summary>CHASE DS:00d8: "She", the same for a woman.</summary>
    public string StrSheTurns { get; set; } = "";

    /// <summary>CHASE DS:00dc: the escape text after the pronoun (" turns the corner ... crowded restaurant.").</summary>
    public string StrTurnsCorner { get; set; } = "";

    /// <summary>CHASE DS:0115: the escape text's last lines ("We'll have to be less obvious next time, Max.").</summary>
    public string StrLessObvious { get; set; } = "";

    /// <summary>CHASE DS:0146: the result when the fleeing player reached a building.</summary>
    public string StrDashIntoBuilding { get; set; } = "";

    /// <summary>CHASE DS:0182: "He", first word of the text of the quarry's arrival.</summary>
    public string StrHePullsOver { get; set; } = "";

    /// <summary>CHASE DS:0185: "She", the same for a woman.</summary>
    public string StrShePullsOver { get; set; } = "";

    /// <summary>CHASE DS:0189: the arrival text after the pronoun (" pulls over and walks into a building ...").</summary>
    public string StrPullsOver { get; set; } = "";

    /// <summary>CHASE DS:01cc: the result when the pursuers caught the fleeing player.</summary>
    public string StrRunOffRoad { get; set; } = "";

    /// <summary>CHASE DS:01f1: "You run ", the start of the arrest text.</summary>
    public string StrYouRun { get; set; } = "";

    /// <summary>CHASE DS:01fa: "his", in the arrest text for a man.</summary>
    public string StrHis { get; set; } = "";

    /// <summary>CHASE DS:01fe: "her", for a woman.</summary>
    public string StrHer { get; set; } = "";

    /// <summary>CHASE DS:0202: the end of the arrest text (" car off the road and make the arrest!").</summary>
    public string StrCarOffRoad { get; set; } = "";

    // ---- the car offer screen ----

    /// <summary>CHASE DS:1862 car_level_colors: per conspicuousness level, the colours of the offered car's picture.</summary>
    public ColorPair[] CarLevelColors { get; } = [new(), new(), new(), new()];

    /// <summary>CHASE DS:16e4 cga_pattern_cars: the colour table handed to the driver (as both tables) for the offer screen.</summary>
    public byte[] CgaPatternCars { get; } = new byte[16];

    /// <summary>CHASE DS:1818: the file name of the cars' pictures ("cars.pic").</summary>
    public string CarsPicName { get; set; } = "";

    /// <summary>CHASE DS:1821: "(Tracking)", for a car with a tracking device.</summary>
    public string StrTracking { get; set; } = "";

    /// <summary>CHASE DS:182c: the empty text put before an offered car's top speed.</summary>
    public string StrEmpty { get; set; } = "";

    /// <summary>CHASE DS:182d: " mph", after the top speed.</summary>
    public string StrMph { get; set; } = "";

    /// <summary>CHASE DS:1832: "Excellent" handling.</summary>
    public string StrExcellent { get; set; } = "";

    /// <summary>CHASE DS:183c: "Fair" handling.</summary>
    public string StrFair { get; set; } = "";

    /// <summary>CHASE DS:1841, 1845, 184e, 1853: the conspicuousness levels "Low", "Moderate", "High", "Extreme".</summary>
    public string[] StrConspicuousness { get; } = ["", "", "", ""];

    /// <summary>CHASE DS:185b: "Car #", before the number of a pick.</summary>
    public string StrCarNo { get; set; } = "";

    /// <summary>CHASE DS:18ca: "Loc", the map label of a location outside a campaign (never used: the campaign flag is always set).</summary>
    public string StrLoc { get; set; } = "";

    /// <summary>CHASE DS:1872 str_quit_confirm2: the quit question of the chase's own Alt-Q, with its choices No and Yes.</summary>
    public string StrQuitConfirm2 { get; set; } = "";

    // ---- the screen ----

    /// <summary>CHASE DS:19f6 Rp1X: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>CHASE DS:1600 RpBackX: the port record of the off-screen page the street view is put together on (and the screen is saved to under boxes).</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>CHASE DS:17dc RpSpriteX: the port record of the page holding chase.pic (ground tiles, car sprites, dashboard parts).</summary>
    public RasterPort RpSpriteX { get; } = new();

    /// <summary>CHASE DS:17f0 RpGridX: the port record of the page holding street.pic (the eight cockpit pictures).</summary>
    public RasterPort RpGridX { get; } = new();

    /// <summary>CHASE DS:1804 gauge_sheetX: the port record of the cockpit window on the screen, where the gauge needle is drawn.</summary>
    public RasterPort GaugeSheetX { get; } = new();

    /// <summary>CHASE DS:1a08 Rp1: the port the screen is drawn through; in the running game <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>CHASE DS:1612 RpBack: the off-screen page's port; in the running game <see cref="RpBackX"/>.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>CHASE DS:17ee RpSprite: the chase.pic page's port; in the running game <see cref="RpSpriteX"/>.</summary>
    public RasterPort RpSprite { get; set; }

    /// <summary>CHASE DS:1802 RpGrid: the street.pic page's port; in the running game <see cref="RpGridX"/>.</summary>
    public RasterPort RpGrid { get; set; }

    /// <summary>CHASE DS:1816 gauge_sheet: the cockpit window's port; in the running game <see cref="GaugeSheetX"/>.</summary>
    public RasterPort GaugeSheet { get; set; }

    /// <summary>CHASE DS:1634 cga_pattern_menu_a: the first colour table handed to the driver before a message box (16 bytes).</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>CHASE DS:1644 cga_pattern_menu_b: the second colour table handed over with it (160 bytes).</summary>
    public byte[] ColorMapB { get; } = new byte[160];

    /// <summary>CHASE DS:3e86 message_buf: text scratch (map labels, the dashboard's speed, result texts, the disk prompt).</summary>
    public TextBuffer MessageText { get; } = new(1024);

    /// <summary>CHASE DS:2132: this part's random generator (the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    // ---- the routines shared with the other parts ----

    /// <summary>The message box and menu.</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The picture animation beside a message box.</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The insert-disk prompt.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>Joystick, mouse and BIOS call variables.</summary>
    public InputState Input { get; } = new();

    /// <summary>The index of a tile in <see cref="MapCells"/>: column by column, as the original indexes it.</summary>
    /// <remarks>
    /// New code. The original computes <c>x * 24 + y</c> in place and does not check either coordinate, so a row of
    /// 24 is the first row of the next column; only an index outside the whole table is an error here.
    /// </remarks>
    public static int CellIndex(int x, int y) => x * MapTiles + y;
}
