using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Raid;

/// <summary>
/// The state of the building raid (the original's TAC program): the building's map, rooms and doors, the guards and the
/// player, the grenades in flight, the alarm, and the campaign tables the raid reads.
/// </summary>
/// <remarks>
/// <para>
/// The building is a map of 40 x 32 cells of 16 x 16 pixels. Each cell has a byte in each of the per-cell tables
/// (<see cref="CellMap"/>, <see cref="RoomAtCell"/>, <see cref="WallFlags"/>), stored column by column as the original
/// stores them: the index of cell (x, y) is <c>x * 32 + y</c> (<see cref="Cell"/>). Sides of a cell are numbered 0 north,
/// 1 east, 2 south, 3 west; facings of a person 1..8, 1 north, going clockwise.
/// </para>
/// <para>
/// Names are those of the reconstructed tac.h, MicroProse's own where the Amiga symbols give them (<c>rm</c>, <c>dr</c>,
/// <c>bg</c>, <c>gr</c>, <c>rms</c>, <c>ob</c>, <c>nroom</c>, <c>MacGuff</c>, <c>Guard</c>, ...), in C# capitalisation. Nothing
/// here has a built-in value: the tables and texts are the initialised data of the player's TAC.EXE.
/// </para>
/// </remarks>
// orig: TAC DGROUP (src/c/include/tac.h, docs/data-model/tac.md)
public sealed partial class RaidState : ILibraryState
{
    /// <summary>Cells across the map.</summary>
    public const int MapW = 40;

    /// <summary>Cells down the map; also the stride of the per-cell tables.</summary>
    public const int MapH = 32;

    /// <summary>The number of cells, the length of each per-cell table.</summary>
    public const int Cells = MapW * MapH;

    /// <summary>Room records; rooms 1..23 can be used, room 0 is the outside.</summary>
    public const int MaxRooms = 0x18;

    /// <summary>Door records.</summary>
    public const int MaxDoors = 0x60;

    /// <summary>Kinds of room in <see cref="Rms"/>.</summary>
    public const int RoomTypes = 10;

    /// <summary>Kinds of tile in <see cref="Ob"/>.</summary>
    public const int TileTypes = 62;

    /// <summary>Persons in the campaign table <see cref="Ps"/>.</summary>
    public const int Persons = 64;

    /// <summary>Plot objects in <see cref="Mc"/>.</summary>
    public const int PlotObjects = 4;

    /// <summary>Person slots: 0 the player, 1..8 guards.</summary>
    public const int Actors = 9;

    /// <summary>Entries of a per-facing table: 0 (none) and the facings 1..8.</summary>
    public const int Facings = 9;

    /// <summary>The value of <see cref="GameMode"/> for the escape raid (the player starts in the target room, which holds the plot object).</summary>
    public const short EscapeMode = 1;

    /// <summary>The value of <see cref="GameMode"/> for a raid to kill or free someone: the target room is alarmed, guards come in from outside.</summary>
    public const short KillMode = 2;

    /// <summary>The value of <see cref="GameMode"/> for the one-room raid (an ambush in the street).</summary>
    public const short OneRoomMode = 3;

    /// <summary>The index of cell (<paramref name="x"/>, <paramref name="y"/>) in a per-cell table.</summary>
    /// <remarks>
    /// New code. The original indexes <c>table[x][y]</c> in place and checks neither coordinate, so a row of 32 or more
    /// is a row of the next column; only an index outside the whole table is an error here.
    /// </remarks>
    public static int Cell(int x, int y) => x * MapH + y;

    // ---- the tables of the program ----

    /// <summary>TAC DS:00b2 rms (MicroProse's name, "rooms"): the ten kinds of room.</summary>
    public RoomType[] Rms { get; } = [.. Enumerable.Range(0, RoomTypes).Select(_ => new RoomType())];

    /// <summary>TAC DS:018e ob (MicroProse's name, "objects"): the 62 kinds of tile.</summary>
    public ObjectType[] Ob { get; } = [.. Enumerable.Range(0, TileTypes).Select(_ => new ObjectType())];

    /// <summary>
    /// The record of tile type <paramref name="tile"/> (0..63) as the original reads <c>ob[tile]</c>: one of <see cref="Ob"/>,
    /// or for the types 62 and 63, behind the table, the words of the movement tables that lie there (DS:0666 on:
    /// <see cref="MoveX"/>, <see cref="MoveY"/>, <see cref="Move8X"/>, <see cref="Move8Y"/>). Type 63 is the low six bits of
    /// bare floor whose open bit was turned off: the use key on the cell where the hidden information was taken.
    /// </summary>
    /// <param name="tile">The type, the low six bits of a cell.</param>
    /// <returns>The record (for 62 and 63 a new one, made of those words).</returns>
    public ObjectType TileType(int tile)
    {
        if (tile < TileTypes)
        {
            return Ob[tile];
        }
        // C: ob[62] begins at DS:0666, MoveX[0]; each record is ten words
        short[] behind = [.. MoveX, .. MoveY, .. Move8X, .. Move8Y];
        var at = (tile - TileTypes) * 10;
        var record = new ObjectType
        {
            SpriteX = behind[at + 6],
            SpriteY = behind[at + 7],
            Bits = (TileFlags)behind[at + 8],
            Where = behind[at + 9],
        };
        for (var i = 0; i < ObjectType.NameLength; i++)
        {
            var word = behind[at + i / 2];
            record.Name[i] = (char)(i % 2 == 0 ? C16.LoByte(word) : C16.HiByte(word));
        }
        return record;
    }

    /// <summary>TAC DS:06ae Mve4X (MicroProse's name): the x step to the neighbour across each side 0..3; entry 4 repeats entry 0.</summary>
    public short[] Mve4X { get; } = new short[5];

    /// <summary>TAC DS:06b8 Mve4Y (MicroProse's name): the y step to the neighbour across each side 0..3; entry 4 repeats entry 0.</summary>
    public short[] Mve4Y { get; } = new short[5];

    /// <summary>TAC DS:1ff0 wall_tile_table: for each combination of wall bits of a cell, the wall tile drawn in it.</summary>
    public byte[] WallTileTable { get; } = new byte[16];

    /// <summary>
    /// TAC DS:1bca building_kind_mask: the kind of building as a bit, tested against <see cref="RoomType.Where"/>. Nothing
    /// writes it; it stays 1 (docs/original-bugs.md CA-8).
    /// </summary>
    public short BuildingKindMask { get; set; }

    // ---- the building ----

    /// <summary>
    /// TAC DS:91ee cell_map: the tile in each cell. Low six bits the tile type (<see cref="Ob"/>), <see cref="CellUsed"/>
    /// and <see cref="CellOpen"/> above; <see cref="CellEmpty"/> for bare floor.
    /// </summary>
    public byte[] CellMap { get; } = new byte[Cells];

    /// <summary>TAC DS:b40a room_at_cell: the room each cell belongs to, 0 outside every room.</summary>
    public byte[] RoomAtCell { get; } = new byte[Cells];

    /// <summary>TAC DS:cf8a wall_flags: while the building is generated, the walls around each cell, bit n for side n.</summary>
    public byte[] WallFlags { get; } = new byte[Cells];

    /// <summary>TAC DS:a05e rm (MicroProse's name, "rooms"): the room records.</summary>
    public Room[] Rm { get; } = [.. Enumerable.Range(0, MaxRooms).Select(_ => new Room())];

    /// <summary>TAC DS:96f8 dr (MicroProse's name, "doors"): the door records; <see cref="Ndoor"/> are used.</summary>
    public Door[] Dr { get; } = [.. Enumerable.Range(0, MaxDoors).Select(_ => new Door())];

    /// <summary>TAC DS:4354 room_door_count: the number of doors of each room.</summary>
    public short[] RoomDoorCount { get; } = new short[MaxRooms];

    /// <summary>TAC DS:b3fa nroom (MicroProse's name): the number of rooms the generator made.</summary>
    public short Nroom { get; set; }

    /// <summary>TAC DS:cf46 ndoor (MicroProse's name): the number of doors.</summary>
    public short Ndoor { get; set; }

    /// <summary>TAC DS:b912 MacGuff (MicroProse's name): the target room, the biggest inner room; it holds the plot object and the person to arrest.</summary>
    public short MacGuff { get; set; }

    /// <summary>TAC DS:96ee Guard (MicroProse's name): the guards' home room, the best-connected room on the outer wall.</summary>
    public short Guard { get; set; }

    /// <summary>TAC DS:bdde InRoom (MicroProse's name): the room the player is in; the generator leaves the last removed room with doors in it.</summary>
    public short InRoom { get; set; }

    /// <summary>TAC DS:a1e4 GameMode (MicroProse's name): the kind of raid; <see cref="OneRoomMode"/> is the one-room ambush.</summary>
    public short GameMode { get; set; }

    /// <summary>TAC DS:bddc Who (MicroProse's name): the person to arrest in the target room, a <see cref="Ps"/> index; -1 for nobody.</summary>
    public short Who { get; set; }

    /// <summary>TAC DS:bb6a MacHere (MicroProse's name): the plot object lies in the target room.</summary>
    public bool MacHere { get; set; }

    /// <summary>TAC DS:cf88 guards_left: how many more guards may come into the building.</summary>
    public short GuardsLeft { get; set; }

    /// <summary>TAC DS:d48a AtLoc (MicroProse's name): the organisation whose building is raided.</summary>
    public short AtLoc { get; set; }

    /// <summary>TAC DS:0666 MoveX (MicroProse's name): a person's x step per frame for each facing 1..8 (2 or 3 pixels); entry 0 is no move.</summary>
    public short[] MoveX { get; } = new short[Facings];

    /// <summary>TAC DS:0678 MoveY (MicroProse's name): the y step per frame for each facing.</summary>
    public short[] MoveY { get; } = new short[Facings];

    /// <summary>TAC DS:068a Move8X (MicroProse's name): the unit x step for each facing 1..8 (the slow step, and the eight neighbours of a cell).</summary>
    public short[] Move8X { get; } = new short[Facings];

    /// <summary>TAC DS:069c Move8Y (MicroProse's name): the unit y step for each facing.</summary>
    public short[] Move8Y { get; } = new short[Facings];

    // ---- the people ----

    /// <summary>TAC DS:c870 bg (MicroProse's name, "bad guys"): slot 0 the player, slots 1..8 the guards.</summary>
    public BadGuy[] Bg { get; } = [.. Enumerable.Range(0, Actors).Select(_ => new BadGuy())];

    /// <summary>TAC DS:438e path_dist: per person slot, the distance map of the guard's way to his target cell: steps + 1, 0 not reached.</summary>
    public byte[][] PathDist { get; } = [.. Enumerable.Range(0, Actors).Select(_ => new byte[Cells])];

    /// <summary>TAC DS:bb72 Px (MicroProse's name): the player's x, pixels.</summary>
    public short Px { get; set; }

    /// <summary>TAC DS:bb7c Py (MicroProse's name): the player's y, pixels.</summary>
    public short Py { get; set; }

    /// <summary>TAC DS:b3fc Cx (MicroProse's name): the player's cell column.</summary>
    public short Cx { get; set; }

    /// <summary>TAC DS:b408 Cy (MicroProse's name): the player's cell row.</summary>
    public short Cy { get; set; }

    /// <summary>TAC DS:1c42 player_facing: the player's facing 1..8.</summary>
    public short PlayerFacing { get; set; }

    /// <summary>TAC DS:b406 front_cell_x: the column of the cell the player acts on (the object in front, or his own cell).</summary>
    public short FrontCellX { get; set; }

    /// <summary>TAC DS:b90c front_cell_y: the row of that cell.</summary>
    public short FrontCellY { get; set; }

    /// <summary>TAC DS:bb60 running: the '5' key's slow, quiet step is on.</summary>
    public bool Running { get; set; }

    /// <summary>TAC DS:96f4 fire_timer: frames the player's last shot still shows (4 after a shot).</summary>
    public short FireTimer { get; set; }

    /// <summary>TAC DS:91dc Disguised (MicroProse's name): the player wears a knocked-out guard's uniform; guards ignore him until he acts.</summary>
    public bool Disguised { get; set; }

    // ---- the alarm ----

    /// <summary>TAC DS:a8ec Rtime (MicroProse's name): frames since the raid began.</summary>
    public short Rtime { get; set; }

    /// <summary>TAC DS:91e8 Gtime (MicroProse's name): the campaign's clock when the raid began, in minutes of the day.</summary>
    public short Gtime { get; set; }

    /// <summary>TAC DS:364c clock_text: the clock shown during the raid, "hh:mm:ss"; the digits are written into the template.</summary>
    public char[] ClockText { get; } = new char[8];

    /// <summary>TAC DS:91e0 Diff (MicroProse's name): the difficulty of the raid, 0..4.</summary>
    public short Diff { get; set; }

    /// <summary>TAC DS:91de Night (MicroProse's name): the raid is between 20:00 and 08:00: more guards hunt.</summary>
    public bool Night { get; set; }

    /// <summary>TAC DS:8dd0 shared_alert: how alert the organisation is (the hand-off's word 0x48); the more, the more guards patrol.</summary>
    public short SharedAlert { get; set; }

    /// <summary>TAC DS:91ea Eseen (MicroProse's name): the alarm, counting down each frame; 0 no alarm, at most 9999.</summary>
    public short Eseen { get; set; }

    /// <summary>TAC DS:c86e ToRoom (MicroProse's name): the room the alarm was raised in; the guards there hunt the player.</summary>
    public short ToRoom { get; set; }

    /// <summary>TAC DS:2032 alert_cached_room: -1, and nothing writes it: the alarm distances are always worked out again.</summary>
    public short AlertCachedRoom { get; set; }

    /// <summary>TAC DS:a03a alert_cell_x: the column where the player was last seen (0: nowhere).</summary>
    public short AlertCellX { get; set; }

    /// <summary>TAC DS:a05c alert_cell_y: the row where the player was last seen.</summary>
    public short AlertCellY { get; set; }

    /// <summary>TAC DS:a1ae room_last_visit: per room, the frame after a guard last chose it as the next room (patrolling guards go where nobody went for longest).</summary>
    public short[] RoomLastVisit { get; } = new short[MaxRooms];

    /// <summary>TAC DS:b91a exit_dist: per room, the doors to pass to the guards' home room (the alarm distances from it, copied at the first frame).</summary>
    public short[] ExitDist { get; } = new short[MaxRooms];

    /// <summary>TAC DS:b3f8 guard_sees: the last guard tested has the player in his line of sight (the line-of-sight answer, 0 for no).</summary>
    public short GuardSees { get; set; }

    /// <summary>TAC DS:bb70 cover_bits: from the last sight line: bit 0 clear = a solid piece lies on its first half, bit 1 clear = on its second half.</summary>
    public short CoverBits { get; set; }

    // ---- the campaign tables ----

    /// <summary>TAC DS:06fe ps (MicroProse's name, "persons"): the campaign's persons.</summary>
    public Person[] Ps { get; } = [.. Enumerable.Range(0, Persons).Select(_ => new Person())];

    /// <summary>TAC DS:9e18 ct (MicroProse's name, "cities"): the campaign's 16 cities.</summary>
    public City[] Ct { get; } = [.. Enumerable.Range(0, 16).Select(_ => new City())];

    /// <summary>TAC DS:c604 org (MicroProse's name): the campaign's 16 organisations.</summary>
    public Org[] Org { get; } = [.. Enumerable.Range(0, 16).Select(_ => new Org())];

    /// <summary>TAC DS:bdf6 ac (MicroProse's name): the plot's cast; <see cref="NACTIVITY"/> are in use, the first member is the MasterMind.</summary>
    public Activity[] Ac { get; } = [.. Enumerable.Range(0, CastCapacity).Select(_ => new Activity())];

    /// <summary>TAC DS:c99e mc (MicroProse's name): the plot's objects.</summary>
    public MacGuffins[] Mc { get; } = [.. Enumerable.Range(0, PlotObjects).Select(_ => new MacGuffins())];

    // ---- the screen and the routines shared with the other parts ----

    /// <summary>The drawing ports point at their records, as in the program's initialised data.</summary>
    public RaidState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
        SavedScreen = SavedScreenX;
        RpSprite = RpSpriteX;
        RpGuys = RpGuysX;
    }

    /// <summary>TAC DS:379e: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>TAC DS:1ad4: the port record of the off-screen copy of the room (the backdrop the people are drawn over).</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>TAC DS:37b0 Rp1 (MicroProse's name): the port the screen is drawn through; in the running game <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>TAC DS:1ae6 RpBack (MicroProse's name): the backdrop's port; in the running game <see cref="RpBackX"/>.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>TAC DS:06ea: the port record of the page the whole screen is saved on while a box is up.</summary>
    public RasterPort SavedScreenX { get; } = new();

    /// <summary>TAC DS:06fc saved_screen: the port the screen is saved to; in the running game <see cref="SavedScreenX"/>.</summary>
    public RasterPort SavedScreen { get; set; }

    /// <summary>TAC DS:1d87: "GAS", the warning the status box shows in a gassed room.</summary>
    public string StrGas { get; set; } = "";

    /// <summary>TAC DS:1d8b: "Disguise", what the status box shows while the player is disguised.</summary>
    public string StrDisguise { get; set; } = "";

    /// <summary>TAC DS:a038 PhotosTaken (MicroProse's name): the shots of the film used (36 to a film).</summary>
    public short PhotosTaken { get; set; }

    /// <summary>TAC DS:9b68 Status (MicroProse's name): this part's copy of the player's options (sound, joystick, ...).</summary>
    public Options Status { get; set; }

    /// <summary>TAC DS:c86c level_done: the raid is over (the player left, was caught or knocked out, or quit: -1 ends the game).</summary>
    public short LevelDone { get; set; }

    /// <summary>TAC DS:1b08 cga_map_menu_a: the first colour table handed to the driver before a message box.</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>TAC DS:1b18 cga_map_menu_b: the second colour table.</summary>
    public byte[] ColorMapB { get; } = new byte[16];

    /// <summary>TAC DS:8dda Txt (MicroProse's name): the text buffer messages are put together in.</summary>
    public TextBuffer MessageText { get; } = new(1024);

    /// <summary>The message box and menu.</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The picture animation beside a message box.</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The insert-disk prompt.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>Joystick, mouse and BIOS call variables.</summary>
    public InputState Input { get; } = new();

    // the library's name for the raid's "leave" variable (ILibraryState)
    short ILibraryState.Quit
    {
        get => LevelDone;
        set => LevelDone = value;
    }

    /// <summary>TAC DS:06c2: the port record of the page holding the tile sheet (tiles, people, the radar's small tiles).</summary>
    public RasterPort RpSpriteX { get; } = new();

    /// <summary>TAC DS:06d4 RpSprite (MicroProse's name): the tile sheet's port; in the running game <see cref="RpSpriteX"/>.</summary>
    public RasterPort RpSprite { get; set; }

    /// <summary>TAC DS:c5fa BaseX (MicroProse's name): the screen x where the room view's first cell is drawn.</summary>
    public short BaseX { get; set; }

    /// <summary>TAC DS:c602 BaseY (MicroProse's name): the screen y of the room view's first cell.</summary>
    public short BaseY { get; set; }

    /// <summary>TAC DS:1bce palette_sets: five palettes of 16 colours and a border colour (0 normal, 1 the room's, 2 and 4 flashes).</summary>
    public byte[][] PaletteSets { get; } = [.. Enumerable.Range(0, PaletteCount).Select(_ => new byte[PaletteSize])];

    /// <summary>The number of <see cref="PaletteSets"/>.</summary>
    public const int PaletteCount = 5;

    /// <summary>The bytes of one palette: 16 colours and the border.</summary>
    public const int PaletteSize = 17;

    /// <summary>TAC DS:8dd8 palette_dirty: a flash palette is set; the next frame puts the room's back.</summary>
    public bool PaletteDirty { get; set; }

    /// <summary>TAC DS:cbb2 sound_mode: the alarm state 1..5 the sound driver plays its effects in.</summary>
    public short SoundMode { get; set; }

    /// <summary>TAC DS:cbb6 sound_variant: a variant 1..3 of the effects, drawn again every 256 frames.</summary>
    public short SoundVariant { get; set; }

    /// <summary>The images the raid holds (the original holds places in memory).</summary>
    public ImageRegister Images { get; } = new();

    /// <summary>TAC DS:bc7c sprite_handles: the 16 x 16 pictures cut from sprites.pic (people, bursts, ...).</summary>
    public Image?[] SpriteHandles { get; } = new Image?[SpriteCount];

    /// <summary>
    /// The number of <see cref="SpriteHandles"/>: 160 cells of the sheet's grid (20 by 8), then 16 more cut from the rows
    /// below it (the program's table runs on into the handles it cuts after the grid; entry 167 is never cut).
    /// </summary>
    public const int SpriteCount = 176;

    /// <summary>TAC DS:bb8c icon_handles: the small icons drawn by <c>draw_icon</c>.</summary>
    public Image?[] IconHandles { get; } = new Image?[IconCount];

    /// <summary>The number of <see cref="IconHandles"/>.</summary>
    public const int IconCount = 120;

    /// <summary>TAC DS:a03c item_icons: the status panel's pictures: 0..7 the equipment, 8..10 the grenade kinds, 11 a round, 12 a clip, 13 a wound, 14 unused.</summary>
    public Image?[] ItemIcons { get; } = new Image?[ItemIconCount];

    /// <summary>The number of <see cref="ItemIcons"/>.</summary>
    public const int ItemIconCount = 15;

    /// <summary>TAC DS:2100 equip_neighbour: the equipment screen's cursor moves, one row per item (row 0 empty, items 1..11 reachable).</summary>
    public EquipNeighbour[] EquipNeighbours { get; } = [.. Enumerable.Range(0, EquipNeighbourCount).Select(_ => new EquipNeighbour())];

    /// <summary>The number of <see cref="EquipNeighbours"/>.</summary>
    public const int EquipNeighbourCount = 12;

    /// <summary>TAC DS:2160 equip_icon_pos: where on the status panel the icon of each equipment bit 0..7 is drawn (bit 3's place holds the bugs).</summary>
    public ScreenPoint[] EquipIconPos { get; } = [.. Enumerable.Range(0, 8).Select(_ => new ScreenPoint())];

    /// <summary>TAC DS:2180 ammo_bar_pos: where the grenades of each kind are drawn.</summary>
    public ScreenPoint[] AmmoBarPos { get; } = [.. Enumerable.Range(0, 3).Select(_ => new ScreenPoint())];

    /// <summary>TAC DS:218c round_icon_pos: where the loaded rounds (0) and the spare clips (1) start.</summary>
    public ScreenPoint[] RoundIconPos { get; } = [new(), new()];

    /// <summary>TAC DS:c846 password_word: the computer password of the building, 5 to 9 lower-case letters.</summary>
    public string PasswordWord { get; set; } = "";

    /// <summary>TAC DS:c98c password_mask: the password as the player knows it, a dot for each letter not found yet.</summary>
    public char[] PasswordMask { get; } = new char[12];

    /// <summary>TAC DS:1f49 text_dta_name: "text.dta", the file of the game's texts, from which the password is picked.</summary>
    public string TextDtaName { get; set; } = "";

    /// <summary>TAC DS:1f5c: ten dots, what the password's mask starts as before it is cut to the word's length.</summary>
    public string PasswordDots { get; set; } = "";

    // ---- the player's kit ----

    /// <summary>TAC DS:9b72 Equip (MicroProse's name): the equipment chosen, bit n for item n (<see cref="Equipment"/>).</summary>
    public short Equip { get; set; }

    /// <summary>TAC DS:9d76 Gtype (MicroProse's name): the grenade kind selected, 0..2.</summary>
    public short Gtype { get; set; }

    /// <summary>TAC DS:b94c Grenades (MicroProse's name): the grenades left of each kind.</summary>
    public short[] Grenades { get; } = new short[3];

    /// <summary>TAC DS:96f6 Bullets (MicroProse's name): the rounds in the pistol.</summary>
    public short Bullets { get; set; }

    /// <summary>TAC DS:8dd6 Clips (MicroProse's name): the spare clips.</summary>
    public short Clips { get; set; }

    /// <summary>TAC DS:bb6e Bugs (MicroProse's name): the bugs left to plant.</summary>
    public short Bugs { get; set; }

    /// <summary>TAC DS:cbac Wounds (MicroProse's name): the wounds the player has taken.</summary>
    public short Wounds { get; set; }

    /// <summary>TAC DS:a8e8 throw_timer: frames of the player's throwing pose; meanwhile his own grenades are not drawn.</summary>
    public short ThrowTimer { get; set; }

    /// <summary>TAC DS:9d78 gr (MicroProse's name, "grenades"): grenades in flight or going off, and laid traps.</summary>
    public Grenade[] Gr { get; } = [.. Enumerable.Range(0, MaxGrenades).Select(_ => new Grenade())];

    /// <summary>The number of <see cref="Gr"/> slots.</summary>
    public const int MaxGrenades = 16;

    /// <summary>TAC DS:1c91: the trap menu, "Set trap / Cancel / Remote Control / Booby Trap".</summary>
    public string StrSetTrap { get; set; } = "";

    /// <summary>TAC DS:1d59: " bugged.", after the name of the piece a bug was planted on.</summary>
    public string StrBugged { get; set; } = "";

    /// <summary>TAC DS:1d62: "Room already bugged."</summary>
    public string StrRoomAlreadyBugged { get; set; } = "";

    /// <summary>TAC DS:1d77: "Nothing to bug."</summary>
    public string StrNothingToBug { get; set; } = "";

    /// <summary>TAC DS:a8ea BugsPlanted (MicroProse's name): bugs planted in this raid.</summary>
    public short BugsPlanted { get; set; }

    /// <summary>TAC DS:bb58 BugVal (MicroProse's name): what the planted bugs are worth (the bugged rooms' worth added up).</summary>
    public short BugVal { get; set; }

    /// <summary>TAC DS:4334 willy (MicroProse's name): the cursor's place in the line being typed.</summary>
    public short Willy { get; set; }

    /// <summary>TAC DS:4336 willyW (MicroProse's name): the width in pixels of the character under the cursor.</summary>
    public short WillyW { get; set; }

    /// <summary>TAC DS:4338 willyX (MicroProse's name): the screen x of the character under the cursor.</summary>
    public short WillyX { get; set; }

    /// <summary>TAC DS:1d94: "You are overcome by gas and pass out." (the end of a raid with the player knocked out).</summary>
    public string StrOvercomeByGas { get; set; } = "";

    /// <summary>TAC DS:1dbb: "You slump to the ground, overcome by your wounds."</summary>
    public string StrOvercomeByWounds { get; set; } = "";

    /// <summary>TAC DS:1dee: "The prisoner is released while you are out of the room."</summary>
    public string StrPrisonerReleased { get; set; } = "";

    /// <summary>TAC DS:1e27: "You have eliminated all of your enemies."</summary>
    public string StrEnemiesEliminated { get; set; } = "";

    /// <summary>TAC DS:1e51: "You quickly slip out of the building."</summary>
    public string StrSlipOut { get; set; } = "";

    /// <summary>TAC DS:c9e6 result_value: what the raid hands back as its worth: -1 when the player was stopped inside, else <see cref="BugVal"/>.</summary>
    public short ResultValue { get; set; }

    // ---- the guards' pass ----

    /// <summary>TAC DS:2020 (the table <c>dir_from_signs</c> is addressed by its centre, DS:2028): the facing for each pair of signs of a step, index (sign x + 1) * 3 + sign y + 1; 0 for no step.</summary>
    public short[] DirFromSigns { get; } = new short[9];

    /// <summary>TAC DS:96f0 alarm_guards: guards in the alarm room, counted in this pass over the guards.</summary>
    public short AlarmGuards { get; set; }

    /// <summary>TAC DS:91da alarm_guards_prev: <see cref="AlarmGuards"/> of the last pass.</summary>
    public short AlarmGuardsPrev { get; set; }

    /// <summary>TAC DS:b3f4 max_seen: the longest a guard not out for good has seen the player in a row, this pass.</summary>
    public short MaxSeen { get; set; }

    /// <summary>TAC DS:cbaa guards_not_gone: guards not out of the raid for good, this pass.</summary>
    public short GuardsNotGone { get; set; }

    /// <summary>TAC DS:b402 guard_in_room: some guard not out for good is in the player's room, this pass.</summary>
    public bool GuardInRoom { get; set; }

    /// <summary>
    /// TAC DS:cbbc active (MicroProse's name): the campaign's activity counter per city (the first half of the counter
    /// block the raid hands back). The original also counts the guards awake in each pass in element 0 (CA-1).
    /// </summary>
    public short[] Active { get; } = new short[16];

    /// <summary>
    /// The guards awake in this pass, where the raid keeps them when CA-1 is fixed
    /// (<see cref="OriginalBehaviour.FixRaidGuardCountInCityCounter"/>); the original has no such variable.
    /// </summary>
    public short AwakeGuards { get; set; }

    /// <summary>TAC DS:bb82 disguise_stare: frames a guard has stared at the disguised player.</summary>
    public short DisguiseStare { get; set; }

    /// <summary>TAC DS:cf44 player_acted: the player did something a guard notices this frame (a shot, a photograph, a door ...).</summary>
    public bool PlayerActed { get; set; }

    /// <summary>TAC DS:cf42 diff (MicroProse's name): the campaign's difficulty level, 0..3.</summary>
    public short CampaignDiff { get; set; }

    /// <summary>TAC DS:708e last_search_frame: the frame a guard last threw a grenade at where the player was seen.</summary>
    public short LastSearchFrame { get; set; }

    /// <summary>TAC DS:a1de open_door_count: drawers and safes standing open in the player's room.</summary>
    public short OpenDoorCount { get; set; }

    /// <summary>TAC DS:430a bodies_here: guards killed in the player's room (the room's view shows them).</summary>
    public short BodiesHere { get; set; }

    /// <summary>TAC DS:cbb8 Alarm (MicroProse's name): the violence of the raid: guards killed, grenades gone off.</summary>
    public short Alarm { get; set; }

    // ---- chance ----

    /// <summary>TAC DS:9b66 random_seed: the seed the random numbers were last started from (the clock's seconds, low 15 bits).</summary>
    public short RandomSeed { get; set; }

    /// <summary>TAC DS:4026: this part's random generator (the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    // ---- the cell byte ----

    /// <summary>A cell of bare floor.</summary>
    public const byte CellEmpty = 0xff;

    /// <summary>The low six bits of a cell: its tile type.</summary>
    public const byte CellTile = 0x3f;

    /// <summary>A cell bit: the object was photographed or examined already.</summary>
    public const byte CellUsed = 0x40;

    /// <summary>A cell bit: the door, drawer or safe is open.</summary>
    public const byte CellOpen = 0x80;

    /// <summary>The first door tile; doors are tiles 8..11, one per side.</summary>
    public const byte TileDoor = 8;

    /// <summary>The first furniture tile.</summary>
    public const byte TileFurniture = 12;

    /// <summary>The plot object's tile.</summary>
    public const byte TileMacGuffin = 0x3c;

    /// <summary>The tile of the person to arrest.</summary>
    public const byte TileAgent = 0x3d;
}
