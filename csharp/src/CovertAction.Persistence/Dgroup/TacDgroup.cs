using CovertAction.Core.Graphics;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's building-raid program, TAC.EXE: where each variable of
/// <see cref="RaidState"/> lies in it. The start state is loaded through it from the initialised data of the player's own
/// TAC.EXE, and the oracle tests load the state from the data segment the original had before a function ran and
/// compare the one it left.
/// </summary>
/// <remarks>
/// Offsets are those of the reconstructed tac.h, docs/data-model/tac.md and symbols/names.txt (<c>D_tac_xxxx</c>); the
/// campaign records have the layouts of <see cref="CampaignLayouts"/>; the place of the C library's <c>holdrand</c> was
/// read from the vectors of <c>random_below</c>, the only variable it changes. Only the variables of the ported
/// functions are listed; the others join with the first function that uses them.
/// </remarks>
public static partial class TacDgroup
{
    private const int RoomTypeSize = 22;
    private const int ObjectTypeSize = 20;
    private const int RoomSize = 14;
    private const int DoorSize = 5;
    private const int BadGuySize = 28;
    private const int GrenadeSize = 10;
    private const int PointSize = 4;
    private const int AreaSize = 8;

    // the text tables come before the layout: static fields are set in the order they are written
    /// <summary>The texts the table of the message texts' placeholder names points at: "$VICTIM" ... "$OBJECT".</summary>
    private static readonly (int Address, int Length)[] VariableNameTexts =
    [
        (0x2cee, 8), (0x2cf6, 8), (0x2cfe, 8), (0x2d06, 8), (0x2d0e, 8), (0x2d16, 8), (0x2d1e, 8),
    ];

    /// <summary>The buffers the table of the placeholders' values points at.</summary>
    private static readonly (int Address, int Length)[] VariableValueTexts =
    [
        (0x2d26, 52), (0x2d5a, 26), (0x2d74, 26), (0x2d8e, 26), (0x2da8, 26), (0x2dc2, 26), (0x2ddc, 52),
    ];

    // orig: struct Grenade (src/c/include/tac.h)
    private static StateLayout<Grenade> GrenadeLayout { get; } = new StateLayout<Grenade>("Grenade")
        .Int16(0x00, "+0x00 px", g => g.Px, (g, v) => g.Px = v)
        .Int16(0x02, "+0x02 py", g => g.Py, (g, v) => g.Py = v)
        .Int16(0x04, "+0x04 dir", g => g.Dir, (g, v) => g.Dir = v)
        .Int16(0x06, "+0x06 time", g => g.Time, (g, v) => g.Time = v)
        .Int16(0x08, "+0x08 type", g => g.Type, (g, v) => g.Type = v);

    /// <summary>The size of one row of the equipment screen's cursor table.</summary>
    private const int NeighbourSize = 8;

    // orig: struct nbr (src/c/include/tac.h)
    private static StateLayout<EquipNeighbour> NeighbourLayout { get; } = new StateLayout<EquipNeighbour>("nbr")
        .Int16(0x00, "+0x00 up", n => n.Up, (n, v) => n.Up = v)
        .Int16(0x02, "+0x02 down", n => n.Down, (n, v) => n.Down = v)
        .Int16(0x04, "+0x04 left", n => n.Left, (n, v) => n.Left = v)
        .Int16(0x06, "+0x06 right", n => n.Right, (n, v) => n.Right = v);

    // orig: struct pos (src/c/include/tac.h)
    private static StateLayout<ScreenPoint> PointLayout { get; } = new StateLayout<ScreenPoint>("pos")
        .Int16(0x00, "+0x00 x", p => p.X, (p, v) => p.X = v)
        .Int16(0x02, "+0x02 y", p => p.Y, (p, v) => p.Y = v);

    // orig: struct rect (src/c/include/tac.h)
    private static StateLayout<ScreenArea> AreaLayout { get; } = new StateLayout<ScreenArea>("rect")
        .Int16(0x00, "+0x00 x1", a => a.X1, (a, v) => a.X1 = v)
        .Int16(0x02, "+0x02 y1", a => a.Y1, (a, v) => a.Y1 = v)
        .Int16(0x04, "+0x04 x2", a => a.X2, (a, v) => a.X2 = v)
        .Int16(0x06, "+0x06 y2", a => a.Y2, (a, v) => a.Y2 = v);

    /// <summary>
    /// Working memory of the original that the rewrite has no use for, as ranges (start inclusive, end exclusive):
    /// buffers a function fills and uses at once, which nothing reads afterwards.
    /// </summary>
    // orig: TAC DS:709a bfs_queue_x, DS:719a bfs_queue_y (src/c/tac/tac_00e8_6ec9.c)
    public static IReadOnlyList<(int Start, int End)> Scratch { get; } =
    [
        (0x709a, 0x729a),   // bfs_queue_x and bfs_queue_y: the queue of one path fill (build_path_map keeps its own)
        (0x729a, 0x72a1),   // the film counter's number as text (itoa's buffer, copied into Txt at once)
        (0x42a8, 0x42b9),   // video_regs: the processor registers for one call to the video BIOS (tac_init's read of the cursor)
        (0x738c, 0x8bab),   // the picture reader's and decoder's tables and counters (found on tac_init, which loads six pictures)
        (0x8d6d, 0x8dac),   // the decoder's last working bytes (found on tac_init)
        (0xb914, 0xb916),   // pic_mem_pos: the memory source's position
        (0xc3f6, 0xc5fa),   // the picture reader's far callback and its 512-byte block
        (0xce02, 0xcf42),   // pic_row_buf: one decoded picture row
    ];

    // orig: struct BadGuy (src/c/include/tac.h)
    private static StateLayout<BadGuy> BadGuyLayout { get; } = new StateLayout<BadGuy>("BadGuy")
        .Int16(0x00, "+0x00 status", g => (short)g.Status, (g, v) => g.Status = (ActorFlags)v)
        .Int16(0x02, "+0x02 px", g => g.Px, (g, v) => g.Px = v)
        .Int16(0x04, "+0x04 py", g => g.Py, (g, v) => g.Py = v)
        .Int16(0x06, "+0x06 destx", g => g.Destx, (g, v) => g.Destx = v)
        .Int16(0x08, "+0x08 desty", g => g.Desty, (g, v) => g.Desty = v)
        .Int16(0x0a, "+0x0a pose", g => g.Pose, (g, v) => g.Pose = v)
        .Int16(0x0c, "+0x0c dir", g => g.Dir, (g, v) => g.Dir = v)
        .Int16(0x0e, "+0x0e mode", g => g.Mode, (g, v) => g.Mode = v)
        .Int16(0x10, "+0x10 fire", g => g.Fire, (g, v) => g.Fire = v)
        .Int16(0x12, "+0x12 room", g => g.Room, (g, v) => g.Room = v)
        .Int16(0x14, "+0x14 pause", g => g.Pause, (g, v) => g.Pause = v)
        .Int16(0x16, "+0x16 sighting", g => g.Sighting, (g, v) => g.Sighting = v)
        .Int16(0x18, "+0x18 sighted", g => g.Sighted, (g, v) => g.Sighted = v)
        .Int16(0x1a, "+0x1a stun", g => g.Stun, (g, v) => g.Stun = v);

    // orig: struct Rooms (src/c/include/tac.h)
    private static StateLayout<RoomType> RoomTypeLayout { get; } = new StateLayout<RoomType>("Rooms")
        .Text(0x00, 16, "+0x00 name", r => r.Name, (r, v) => r.Name = v)
        .Int16(0x10, "+0x10 intel", r => r.Intel, (r, v) => r.Intel = v)
        .Int16(0x12, "+0x12 size", r => r.Size, (r, v) => r.Size = v)
        .Int16(0x14, "+0x14 where", r => r.Where, (r, v) => r.Where = v);

    // orig: struct Object (src/c/include/tac.h)
    private static StateLayout<ObjectType> ObjectTypeLayout { get; } = new StateLayout<ObjectType>("Object")
        .Chars(0x00, ObjectType.NameLength, "+0x00 name", o => o.Name)
        .Int16(0x0c, "+0x0c spriteX", o => o.SpriteX, (o, v) => o.SpriteX = v)
        .Int16(0x0e, "+0x0e spriteY", o => o.SpriteY, (o, v) => o.SpriteY = v)
        .Int16(0x10, "+0x10 bits", o => (short)o.Bits, (o, v) => o.Bits = (TileFlags)v)
        .Int16(0x12, "+0x12 where", o => o.Where, (o, v) => o.Where = v);

    // orig: struct Room (src/c/include/tac.h)
    private static StateLayout<Room> RoomLayout { get; } = new StateLayout<Room>("Room")
        .Char(0x00, "+0x00 x", r => r.X, (r, v) => r.X = v)
        .Char(0x01, "+0x01 y", r => r.Y, (r, v) => r.Y = v)
        .Char(0x02, "+0x02 dx", r => r.Dx, (r, v) => r.Dx = v)
        .Char(0x03, "+0x03 dy", r => r.Dy, (r, v) => r.Dy = v)
        .UInt32(0x04, "+0x04 doors", r => unchecked((uint)r.Doors), (r, v) => r.Doors = unchecked((int)v))
        .Int16(0x08, "+0x08 status", r => (short)r.Status, (r, v) => r.Status = (RoomStatus)v)
        .Int16(0x0a, "+0x0a type", r => r.Type, (r, v) => r.Type = v)
        .Int16(0x0c, "+0x0c toDist", r => r.ToDist, (r, v) => r.ToDist = v);

    // orig: struct Door (src/c/include/tac.h)
    private static StateLayout<Door> DoorLayout { get; } = new StateLayout<Door>("Door")
        .Char(0x00, "+0x00 room1", d => d.Room1, (d, v) => d.Room1 = v)
        .Char(0x01, "+0x01 room2", d => d.Room2, (d, v) => d.Room2 = v)
        .Char(0x02, "+0x02 x", d => d.X, (d, v) => d.X = v)
        .Char(0x03, "+0x03 y", d => d.Y, (d, v) => d.Y = v)
        .Char(0x04, "+0x04 dir", d => d.Dir, (d, v) => d.Dir = v);

    /// <summary>The variables of TAC.EXE at their offsets.</summary>
    // orig: TAC DGROUP (src/c/include/tac.h; holdrand DS:4026 located from the vectors of random_below)
    public static StateLayout<RaidState> Layout { get; } = Build();

    private static StateLayout<RaidState> Build()
    {
        var layout = new StateLayout<RaidState>("TAC DGROUP");
        // ---- initialised data ----
        Records(layout, 0x00b2, RoomTypeSize, RaidState.RoomTypes, "rms", RoomTypeLayout, s => s.Rms);
        Records(layout, 0x018e, ObjectTypeSize, RaidState.TileTypes, "ob", ObjectTypeLayout, s => s.Ob);
        layout
            .Int16Array(0x0666, RaidState.Facings, "DS:0666 MoveX", s => s.MoveX)
            .Int16Array(0x0678, RaidState.Facings, "DS:0678 MoveY", s => s.MoveY)
            .Int16Array(0x068a, RaidState.Facings, "DS:068a Move8X", s => s.Move8X)
            .Int16Array(0x069c, RaidState.Facings, "DS:069c Move8Y", s => s.Move8Y)
            .Int16Array(0x06ae, 5, "DS:06ae Mve4X", s => s.Mve4X)
            .Int16Array(0x06b8, 5, "DS:06b8 Mve4Y", s => s.Mve4Y);
        layout
            .Part(0x06c2, "DS:06c2 tile sheet port", RasterPortLayout.Layout, s => s.RpSpriteX)
            .Pointer<RasterPort>(0x06d4, "DS:06d4 RpSprite", s => s.RpSprite, (s, v) => s.RpSprite = v, (0x06c2, s => s.RpSpriteX))
            .Part(0x06d6, "DS:06d6 people picture port", RasterPortLayout.Layout, s => s.RpGuysX)
            .Pointer<RasterPort>(0x06e8, "DS:06e8 RpGuys", s => s.RpGuys, (s, v) => s.RpGuys = v, (0x06d6, s => s.RpGuysX))
            .Part(0x06ea, "DS:06ea saved screen port", RasterPortLayout.Layout, s => s.SavedScreenX)
            .Pointer<RasterPort>(0x06fc, "DS:06fc saved_screen", s => s.SavedScreen, (s, v) => s.SavedScreen = v, (0x06ea, s => s.SavedScreenX));
        Records(layout, 0x06fe, CampaignLayouts.PersonSize, RaidState.Persons, "ps", CampaignLayouts.Person, s => s.Ps);
        layout
            .Part(0x1ad4, "DS:1ad4 RpBackX", RasterPortLayout.Layout, s => s.RpBackX)
            .Pointer<RasterPort>(0x1ae6, "DS:1ae6 RpBack", s => s.RpBack, (s, v) => s.RpBack = v, (0x1ad4, s => s.RpBackX), (0x379e, s => s.Rp1X))
            .Text(0x1d87, 4, "DS:1d87 \"GAS\"", s => s.StrGas, (s, v) => s.StrGas = v)
            .Text(0x1d8b, 9, "DS:1d8b \"Disguise\"", s => s.StrDisguise, (s, v) => s.StrDisguise = v)
            .Int16(0x1bca, "DS:1bca building_kind_mask", s => s.BuildingKindMask, (s, v) => s.BuildingKindMask = v)
            .Int16(0x1c42, "DS:1c42 player_facing", s => s.PlayerFacing, (s, v) => s.PlayerFacing = v)
            .Bytes(0x1ff0, 16, "DS:1ff0 wall_tile_table", s => s.WallTileTable)
            .Int16(0x2032, "DS:2032 alert_cached_room", s => s.AlertCachedRoom, (s, v) => s.AlertCachedRoom = v)
            .Chars(0x364c, 8, "DS:364c clock_text", s => s.ClockText)
            .Part(0x379e, "DS:379e Rp1X", RasterPortLayout.Layout, s => s.Rp1X)
            .Pointer<RasterPort>(0x37b0, "DS:37b0 Rp1", s => s.Rp1, (s, v) => s.Rp1 = v, (0x379e, s => s.Rp1X), (0x1ad4, s => s.RpBackX))
            // ---- the C library's random state ----
            .UInt32(0x4026, "DS:4026 holdrand", s => s.Random.State, (s, v) => s.Random.State = v)
            // ---- uninitialised data ----
            .Int16(0x430a, "DS:430a bodies_here", s => s.BodiesHere, (s, v) => s.BodiesHere = v)
            .Int16Array(0x4354, RaidState.MaxRooms, "DS:4354 room_door_count", s => s.RoomDoorCount);
        for (var i = 0; i < RaidState.Actors; i++)
        {
            var n = i;
            var at = 0x438e + RaidState.Cells * n;
            layout.Bytes(at, RaidState.Cells, $"DS:{at:x4} path_dist[{n}]", s => s.PathDist[n]);
        }
        layout
            .Int16(0x8dd0, "DS:8dd0 shared_alert", s => s.SharedAlert, (s, v) => s.SharedAlert = v)
            .Bytes(0x8dda, 1024, "DS:8dda Txt", s => s.MessageText.Bytes)
            .Flag(0x91dc, "DS:91dc Disguised", s => s.Disguised, (s, v) => s.Disguised = v)
            .Flag(0x91de, "DS:91de Night", s => s.Night, (s, v) => s.Night = v)
            .Int16(0x91e0, "DS:91e0 Diff", s => s.Diff, (s, v) => s.Diff = v)
            .Int16(0x91e8, "DS:91e8 Gtime", s => s.Gtime, (s, v) => s.Gtime = v)
            .Int16(0x91ea, "DS:91ea Eseen", s => s.Eseen, (s, v) => s.Eseen = v)
            .Bytes(0x91ee, RaidState.Cells, "DS:91ee cell_map", s => s.CellMap)
            .Int16(0x96ee, "DS:96ee Guard", s => s.Guard, (s, v) => s.Guard = v)
            .Int16(0x96f4, "DS:96f4 fire_timer", s => s.FireTimer, (s, v) => s.FireTimer = v)
            .Int16(0x9b66, "DS:9b66 random_seed", s => s.RandomSeed, (s, v) => s.RandomSeed = v);
        Records(layout, 0x96f8, DoorSize, RaidState.MaxDoors, "dr", DoorLayout, s => s.Dr);
        layout
            .Int16(0xa03a, "DS:a03a alert_cell_x", s => s.AlertCellX, (s, v) => s.AlertCellX = v)
            .Int16(0xa05c, "DS:a05c alert_cell_y", s => s.AlertCellY, (s, v) => s.AlertCellY = v);
        Records(layout, 0xa05e, RoomSize, RaidState.MaxRooms, "rm", RoomLayout, s => s.Rm);
        layout
            .Int16(0xa038, "DS:a038 PhotosTaken", s => s.PhotosTaken, (s, v) => s.PhotosTaken = v)
            .Int16Array(0xa1ae, RaidState.MaxRooms, "DS:a1ae room_last_visit", s => s.RoomLastVisit)
            .Int16(0xa1e4, "DS:a1e4 GameMode", s => s.GameMode, (s, v) => s.GameMode = v)
            .Int16(0xa8ec, "DS:a8ec Rtime", s => s.Rtime, (s, v) => s.Rtime = v)
            .Int16(0xb3f8, "DS:b3f8 guard_sees", s => s.GuardSees, (s, v) => s.GuardSees = v)
            .Int16(0xb3fa, "DS:b3fa nroom", s => s.Nroom, (s, v) => s.Nroom = v)
            .Int16(0xb3fc, "DS:b3fc Cx", s => s.Cx, (s, v) => s.Cx = v)
            .Int16(0xb406, "DS:b406 front_cell_x", s => s.FrontCellX, (s, v) => s.FrontCellX = v)
            .Int16(0xb408, "DS:b408 Cy", s => s.Cy, (s, v) => s.Cy = v)
            .Bytes(0xb40a, RaidState.Cells, "DS:b40a room_at_cell", s => s.RoomAtCell)
            .Int16(0xb90c, "DS:b90c front_cell_y", s => s.FrontCellY, (s, v) => s.FrontCellY = v)
            .Int16(0xb912, "DS:b912 MacGuff", s => s.MacGuff, (s, v) => s.MacGuff = v)
            .Int16Array(0xb91a, RaidState.MaxRooms, "DS:b91a exit_dist", s => s.ExitDist)
            .Flag(0xbb60, "DS:bb60 running", s => s.Running, (s, v) => s.Running = v)
            .Flag(0xbb6a, "DS:bb6a MacHere", s => s.MacHere, (s, v) => s.MacHere = v)
            .Int16(0xbb70, "DS:bb70 cover_bits", s => s.CoverBits, (s, v) => s.CoverBits = v)
            .Int16(0xbb72, "DS:bb72 Px", s => s.Px, (s, v) => s.Px = v)
            .Int16(0xbb7c, "DS:bb7c Py", s => s.Py, (s, v) => s.Py = v)
            .Int16(0xbddc, "DS:bddc Who", s => s.Who, (s, v) => s.Who = v)
            .Int16(0xbdde, "DS:bdde InRoom", s => s.InRoom, (s, v) => s.InRoom = v)
            .Int16(0xc86e, "DS:c86e ToRoom", s => s.ToRoom, (s, v) => s.ToRoom = v);
        Records(layout, 0xc870, BadGuySize, RaidState.Actors, "bg", BadGuyLayout, s => s.Bg);
        Records(layout, 0xc99e, CampaignLayouts.MacGuffinsSize, RaidState.PlotObjects, "mc", CampaignLayouts.MacGuffins, s => s.Mc);
        for (var i = 0; i < RaidState.PaletteCount; i++)
        {
            var n = i;
            var at = 0x1bce + RaidState.PaletteSize * n;
            layout.Bytes(at, RaidState.PaletteSize, $"DS:{at:x4} palette_sets[{n}]", s => s.PaletteSets[n]);
        }
        Records(layout, 0x2100, NeighbourSize, RaidState.EquipNeighbourCount, "equip_neighbour", NeighbourLayout, s => s.EquipNeighbours);
        Records(layout, 0x2160, PointSize, 8, "equip_icon_pos", PointLayout, s => s.EquipIconPos);
        Records(layout, 0x2194, AreaSize, RaidState.EquipAreaCount, "equip_src_rect", AreaLayout, s => s.EquipSourceAreas);
        Records(layout, 0x220c, AreaSize, RaidState.EquipNeighbourCount, "equip_rect", AreaLayout, s => s.EquipAreas);
        Records(layout, 0x2180, PointSize, 3, "ammo_bar_pos", PointLayout, s => s.AmmoBarPos);
        Records(layout, 0x218c, PointSize, 2, "round_icon_pos", PointLayout, s => s.RoundIconPos);
        Records(layout, 0x9d78, GrenadeSize, RaidState.MaxGrenades, "gr", GrenadeLayout, s => s.Gr);
        layout
            .Int16(0x8dd6, "DS:8dd6 Clips", s => s.Clips, (s, v) => s.Clips = v)
            .Flag(0x8dd8, "DS:8dd8 palette_dirty", s => s.PaletteDirty, (s, v) => s.PaletteDirty = v)
            .Int16(0x96f6, "DS:96f6 Bullets", s => s.Bullets, (s, v) => s.Bullets = v)
            .Int16(0x9b72, "DS:9b72 Equip", s => s.Equip, (s, v) => s.Equip = v)
            .Int16(0x9d76, "DS:9d76 Gtype", s => s.Gtype, (s, v) => s.Gtype = v)
            .ImageArray(0xa03c, RaidState.ItemIconCount, "DS:a03c item_icons", s => s.ItemIcons, s => s.Images)
            .Int16(0xa8e8, "DS:a8e8 throw_timer", s => s.ThrowTimer, (s, v) => s.ThrowTimer = v)
            .Int16Array(0xb94c, 3, "DS:b94c Grenades", s => s.Grenades)
            .ImageArray(0xbb8c, RaidState.IconCount, "DS:bb8c icon_handles", s => s.IconHandles, s => s.Images)
            .Int16(0xbb6e, "DS:bb6e Bugs", s => s.Bugs, (s, v) => s.Bugs = v)
            .ImageArray(0xbc7c, RaidState.SpriteCount, "DS:bc7c sprite_handles", s => s.SpriteHandles, s => s.Images)
            .Int16(0xc5fa, "DS:c5fa BaseX", s => s.BaseX, (s, v) => s.BaseX = v)
            .Int16(0xc602, "DS:c602 BaseY", s => s.BaseY, (s, v) => s.BaseY = v)
            .Text(0xc846, 0x26, "DS:c846 password_word", s => s.PasswordWord, (s, v) => s.PasswordWord = v)
            .Chars(0xc98c, 12, "DS:c98c password_mask", s => s.PasswordMask)
            .Text(0x1f49, 9, "DS:1f49 text_dta_name", s => s.TextDtaName, (s, v) => s.TextDtaName = v)
            .Int16(0x1c24, "DS:1c24 page buffer", s => s.PageBuffer, (s, v) => s.PageBuffer = v)
            .Int16(0xbb62, "DS:bb62 hour", s => s.Hour, (s, v) => s.Hour = v)
            // ---- the names of the picture files, and the door menu's texts ----
            .Text(0x0074, 11, "DS:0074 balloon sheet name", s => s.BalloonSheetName, (s, v) => s.BalloonSheetName = v)
            .Text(0x007f, 10, "DS:007f status bar name", s => s.GuardSheetName, (s, v) => s.GuardSheetName = v)
            .Text(0x0089, 10, "DS:0089 ambush status bar name", s => s.AmbushGuardSheetName, (s, v) => s.AmbushGuardSheetName = v)
            .Text(0x0093, 11, "DS:0093 raid screen name (female)", s => s.RaidScreenFemaleName, (s, v) => s.RaidScreenFemaleName = v)
            .Text(0x009e, 12, "DS:009e raid screen name (male)", s => s.RaidScreenMaleName, (s, v) => s.RaidScreenMaleName = v)
            .Text(0x2f0c, 11, "DS:2f0c women's faces sheet name", s => s.FacesWomenName, (s, v) => s.FacesWomenName = v)
            .Text(0x2f17, 10, "DS:2f17 men's faces sheet name", s => s.FacesMenName, (s, v) => s.FacesMenName = v)
            .Text(0x2007, 13, "DS:2007 sprites sheet name (female)", s => s.SpritesFemaleName, (s, v) => s.SpritesFemaleName = v)
            .Text(0x2014, 12, "DS:2014 sprites sheet name (male)", s => s.SpritesMaleName, (s, v) => s.SpritesMaleName = v)
            .Text(0x20d5, 11, "DS:20d5 equipment sheet name", s => s.EquipmentSheetName, (s, v) => s.EquipmentSheetName = v)
            .Text(0x1c27, 16, "DS:1c27 \"Which door?\" menu title", s => s.StrWhichDoor, (s, v) => s.StrWhichDoor = v)
            .Text(0x1c37, 7, "DS:1c37 \"Door #\"", s => s.StrDoorNumber, (s, v) => s.StrDoorNumber = v)
            .Text(0x1c3e, 3, "DS:1c3e door line end", s => s.StrDoorLineEnd, (s, v) => s.StrDoorLineEnd = v)
            .Text(0x1f5c, 11, "DS:1f5c password dots", s => s.PasswordDots, (s, v) => s.PasswordDots = v)
            .Int16(0xcbac, "DS:cbac Wounds", s => s.Wounds, (s, v) => s.Wounds = v)
            .Int16(0xcbb2, "DS:cbb2 sound_mode", s => s.SoundMode, (s, v) => s.SoundMode = v)
            .Int16(0xcbb6, "DS:cbb6 sound_variant", s => s.SoundVariant, (s, v) => s.SoundVariant = v);
        // the variables of the routines every part has: message box, animation, disk prompt, joystick
        LibraryLayout.AddTac(layout);
        Records(layout, 0x9e18, CampaignLayouts.CitySize, 16, "ct", CampaignLayouts.City, s => s.Ct);
        Records(layout, 0xc604, CampaignLayouts.OrgSize, 16, "org", CampaignLayouts.Org, s => s.Org);
        layout
            .Text(0x1c91, 46, "DS:1c91 trap menu", s => s.StrSetTrap, (s, v) => s.StrSetTrap = v)
            .Text(0x1d59, 9, "DS:1d59 \" bugged.\"", s => s.StrBugged, (s, v) => s.StrBugged = v)
            .Text(0x1d62, 21, "DS:1d62 \"Room already bugged.\"", s => s.StrRoomAlreadyBugged, (s, v) => s.StrRoomAlreadyBugged = v)
            .Text(0x1d77, 16, "DS:1d77 \"Nothing to bug.\"", s => s.StrNothingToBug, (s, v) => s.StrNothingToBug = v)
            .Text(0x1d94, 39, "DS:1d94 gas end text", s => s.StrOvercomeByGas, (s, v) => s.StrOvercomeByGas = v)
            .Text(0x1dbb, 51, "DS:1dbb wounds end text", s => s.StrOvercomeByWounds, (s, v) => s.StrOvercomeByWounds = v)
            .Text(0x1dee, 57, "DS:1dee prisoner end text", s => s.StrPrisonerReleased, (s, v) => s.StrPrisonerReleased = v)
            .Text(0x1e27, 42, "DS:1e27 enemies end text", s => s.StrEnemiesEliminated, (s, v) => s.StrEnemiesEliminated = v)
            .Text(0x1e51, 39, "DS:1e51 slip-out end text", s => s.StrSlipOut, (s, v) => s.StrSlipOut = v)
            .Int16(0xc9e6, "DS:c9e6 result_value", s => s.ResultValue, (s, v) => s.ResultValue = v)
            .Int16(0xa8ea, "DS:a8ea BugsPlanted", s => s.BugsPlanted, (s, v) => s.BugsPlanted = v)
            .Int16(0xbb58, "DS:bb58 BugVal", s => s.BugVal, (s, v) => s.BugVal = v)
            .Int16Array(0x2020, 9, "DS:2020 dir_from_signs", s => s.DirFromSigns)
            .Int16(0x4334, "DS:4334 willy", s => s.Willy, (s, v) => s.Willy = v)
            .Int16(0x4336, "DS:4336 willyW", s => s.WillyW, (s, v) => s.WillyW = v)
            .Int16(0x4338, "DS:4338 willyX", s => s.WillyX, (s, v) => s.WillyX = v)
            .Int16(0x708e, "DS:708e last_search_frame", s => s.LastSearchFrame, (s, v) => s.LastSearchFrame = v)
            .Int16(0x91da, "DS:91da alarm_guards_prev", s => s.AlarmGuardsPrev, (s, v) => s.AlarmGuardsPrev = v)
            .Int16(0x96f0, "DS:96f0 alarm_guards", s => s.AlarmGuards, (s, v) => s.AlarmGuards = v)
            .Int16(0xa1de, "DS:a1de open_door_count", s => s.OpenDoorCount, (s, v) => s.OpenDoorCount = v)
            .Int16(0xb3f4, "DS:b3f4 max_seen", s => s.MaxSeen, (s, v) => s.MaxSeen = v)
            .Flag(0xb402, "DS:b402 guard_in_room", s => s.GuardInRoom, (s, v) => s.GuardInRoom = v)
            .Int16(0xbb82, "DS:bb82 disguise_stare", s => s.DisguiseStare, (s, v) => s.DisguiseStare = v)
            .Int16(0xcbaa, "DS:cbaa guards_not_gone", s => s.GuardsNotGone, (s, v) => s.GuardsNotGone = v)
            .Int16Array(0xcbbc, 16, "DS:cbbc active", s => s.Active)
            .Int16(0xcf42, "DS:cf42 diff", s => s.CampaignDiff, (s, v) => s.CampaignDiff = v)
            .Flag(0xcf44, "DS:cf44 player_acted", s => s.PlayerActed, (s, v) => s.PlayerActed = v);
        layout
            .Int16(0xcbb8, "DS:cbb8 Alarm", s => s.Alarm, (s, v) => s.Alarm = v)
            .Int16(0xcf46, "DS:cf46 ndoor", s => s.Ndoor, (s, v) => s.Ndoor = v)
            .Int16(0xcf88, "DS:cf88 guards_left", s => s.GuardsLeft, (s, v) => s.GuardsLeft = v)
            .Bytes(0xcf8a, RaidState.Cells, "DS:cf8a wall_flags", s => s.WallFlags)
            .Int16(0xd48a, "DS:d48a AtLoc", s => s.AtLoc, (s, v) => s.AtLoc = v);
        // ---- the library's text and screen routines ----
        layout
            .Bytes(0xc9ea, ILibraryTextState.LineCapacity, "DS:c9ea token_tail_buf", s => s.TokenTail.Bytes)
            .Text(0x2e46, 47, "DS:2e46 clearance notice", s => s.ClearanceNotice, (s, v) => s.ClearanceNotice = v)
            .Text(0x2e3d, 2, "DS:2e3d line separator", s => s.LineSeparator, (s, v) => s.LineSeparator = v)
            .Bytes(0x1b88, 16, "DS:1b88 photo colour map", s => s.PhotoColorMap)
            .Int16(0x91e2, "DS:91e2 balloon_x", s => s.BalloonX, (s, v) => s.BalloonX = v)
            .Int16(0x91e6, "DS:91e6 balloon_y", s => s.BalloonY, (s, v) => s.BalloonY = v)
            // the variables of the library's screens about persons
            .Bytes(0x72b4, 10, "DS:72b4 itoa_buf", s => s.Dossier.NumberText.Bytes)
            .Bytes(0x72d4, 9, "DS:72d4 msg_tag_buf", s => s.Dossier.MessageTag.Bytes)
            .Int16(0xbb76, "DS:bb76 clue_method", s => s.Dossier.ClueMethod, (s, v) => s.Dossier.ClueMethod = v)
            .Int16(0xa8ee, "DS:a8ee announce_flag", s => s.Dossier.CodedFlag, (s, v) => s.Dossier.CodedFlag = v)
            .ImageArray(0x9b6e, RaidState.BalloonImageCount, "DS:9b6e balloon_bg, balloon_pic", s => s.BalloonImages, s => s.Images);
        TextTable(layout, 0x2e10, "DS:2e10 message variable names", VariableNameTexts, s => s.VariableNameTexts);
        TextTable(layout, 0x2e1e, "DS:2e1e message variable values", VariableValueTexts, s => s.VariableValueTexts);
        AddDossierTexts(layout);
        // ---- what the player learns in the building ----
        layout
            .Bytes(0x25a2, 32, "DS:25a2 popcount4", s => s.PopCount)
            .Text(0x24bc, 23, "DS:24bc coded message heading", s => s.StrCodedMessage, (s, v) => s.StrCodedMessage = v)
            .Text(0x24d3, 23, "DS:24d3 meeting notes heading", s => s.StrMeetingNotes, (s, v) => s.StrMeetingNotes = v)
            .Text(0x24ea, 24, "DS:24ea \"NEW INFORMATION\"", s => s.StrNewInformation, (s, v) => s.StrNewInformation = v)
            .Text(0x2502, 28, "DS:2502 \"ADDITIONAL INFORMATION\"", s => s.StrAdditionalInformation, (s, v) => s.StrAdditionalInformation = v)
            .Text(0x251e, 2, "DS:251e \" \"", s => s.StrNotesSpace, (s, v) => s.StrNotesSpace = v)
            .Text(0x2520, 6, "DS:2520 \"\\n... \"", s => s.StrNotesEllipsis, (s, v) => s.StrNotesEllipsis = v)
            .Text(0x2526, 4, "DS:2526 \"on \"", s => s.StrOn, (s, v) => s.StrOn = v)
            .Text(0x252a, 2, "DS:252a \" \"", s => s.StrDateSpace, (s, v) => s.StrDateSpace = v)
            .Text(0x252c, 8, "DS:252c \"of the \"", s => s.StrOfThe, (s, v) => s.StrOfThe = v)
            .Text(0x2534, 4, "DS:2534 \"in \"", s => s.StrIn, (s, v) => s.StrIn = v)
            .Text(0x2538, 2, "DS:2538 \"\\n\"", s => s.StrNotesEnd, (s, v) => s.StrNotesEnd = v)
            .Text(0x253a, 1, "DS:253a \"\"", s => s.StrHintEmpty, (s, v) => s.StrHintEmpty = v)
            .Text(0x253b, 2, "DS:253b \" \"", s => s.StrHintFace, (s, v) => s.StrHintFace = v)
            .Text(0x253d, 5, "DS:253d \" in \"", s => s.StrHintIn, (s, v) => s.StrHintIn = v);
        layout
            .Char(0xbb7a, "DS:bb7a result_bits", s => unchecked((sbyte)s.ResultBits), (s, v) => s.ResultBits = (RaidResults)unchecked((byte)v))
            .Int16Array(0x1f36, 8, "DS:1f36 item_pick", s => s.ItemPick)
            .Text(0x1e78, 18, "DS:1e78 \"The CIA agent in\"", s => s.StrCiaAgentIn, (s, v) => s.StrCiaAgentIn = v)
            .Text(0x1e8a, 22, "DS:1e8a \" is a double agent!!\"", s => s.StrIsDoubleAgent, (s, v) => s.StrIsDoubleAgent = v)
            .Text(0x1ea0, 9, "DS:1ea0 \" now in \"", s => s.StrNowIn, (s, v) => s.StrNowIn = v)
            .Text(0x1ea9, 3, "DS:1ea9 \"!\"", s => s.StrObjectEnd, (s, v) => s.StrObjectEnd = v)
            .Text(0x1eac, 10, "DS:1eac \"You find\"", s => s.StrYouFind, (s, v) => s.StrYouFind = v)
            .Text(0x1eb6, 3, "DS:1eb6 \"!\"", s => s.StrItemEnd, (s, v) => s.StrItemEnd = v)
            .Text(0x1eb9, 1, "DS:1eb9 \"\"", s => s.StrEvidenceEmpty, (s, v) => s.StrEvidenceEmpty = v)
            .Text(0x1eba, 52, "DS:1eba participant text", s => s.StrParticipant, (s, v) => s.StrParticipant = v)
            .Text(0x1eee, 49, "DS:1eee incriminating evidence text", s => s.StrIncriminating, (s, v) => s.StrIncriminating = v)
            .Text(0x1f1f, 23, "DS:1f1f \"into a double agent!\"", s => s.StrIntoDoubleAgent, (s, v) => s.StrIntoDoubleAgent = v);
        layout
            .Bytes(0x1ba8, 16, "DS:1ba8 cga_map_game", s => s.CgaMapGame)
            .Flag(0xbb66, "DS:bb66 faces_on", s => s.FacesOn, (s, v) => s.FacesOn = v)
            .Int16(0x8dd2, "DS:8dd2 PhotoVal", s => s.PhotoVal, (s, v) => s.PhotoVal = v)
            .Text(0x1cc0, 1, "DS:1cc0 \"\"", s => s.StrUseEmpty, (s, v) => s.StrUseEmpty = v)
            .Text(0x1cc1, 23, "DS:1cc1 \"Do you want to arrest \"", s => s.StrArrestQuestion, (s, v) => s.StrArrestQuestion = v)
            .Text(0x1cd8, 12, "DS:1cd8 \"? Yes No\"", s => s.StrArrestChoices, (s, v) => s.StrArrestChoices = v)
            .Text(0x1ce4, 9, "DS:1ce4 \" ... bug\"", s => s.StrCanBug, (s, v) => s.StrCanBug = v)
            .Text(0x1ced, 11, "DS:1ced \" ... photo\"", s => s.StrCanPhoto, (s, v) => s.StrCanPhoto = v)
            .Text(0x1cf8, 9, "DS:1cf8 \" closed.\"", s => s.StrClosed, (s, v) => s.StrClosed = v)
            .Text(0x1d01, 23, "DS:1d01 \"Nothing to photograph.\"", s => s.StrNothingToPhotograph, (s, v) => s.StrNothingToPhotograph = v)
            .Text(0x1d18, 22, "DS:1d18 \"Already photographed.\"", s => s.StrAlreadyPhotographed, (s, v) => s.StrAlreadyPhotographed = v)
            .Text(0x1d2e, 15, "DS:1d2e \" photographed.\"", s => s.StrPhotographed, (s, v) => s.StrPhotographed = v)
            .Text(0x1d3d, 6, "DS:1d3d \"Open \"", s => s.StrOpen, (s, v) => s.StrOpen = v)
            .Text(0x1d43, 8, "DS:1d43 \" first.\"", s => s.StrFirst, (s, v) => s.StrFirst = v)
            .Text(0x1d4b, 13, "DS:1d4b \"Out of film.\"", s => s.StrOutOfFilm, (s, v) => s.StrOutOfFilm = v);
        layout
            .Int16Array(0x42e6, RaidState.Actors, "DS:42e6 actor_sprite_x", s => s.ActorSpriteX)
            .Int16Array(0x42f8, RaidState.Actors, "DS:42f8 actor_sprite_y", s => s.ActorSpriteY)
            .Int16Array(0x42c2, RaidState.Actors, "DS:42c2 actor_radar_x", s => s.ActorRadarX)
            .Int16Array(0x42d4, RaidState.Actors, "DS:42d4 actor_radar_y", s => s.ActorRadarY)
            .Flag(0x1c8a, "DS:1c8a muzzle_on", s => s.MuzzleOn, (s, v) => s.MuzzleOn = v)
            .Int16(0x4314, "DS:4314 muzzle_actor", s => s.MuzzleActor, (s, v) => s.MuzzleActor = v)
            .Int16(0x4316, "DS:4316 muzzle_x", s => s.MuzzleX, (s, v) => s.MuzzleX = v)
            .Int16(0x4318, "DS:4318 muzzle_y", s => s.MuzzleY, (s, v) => s.MuzzleY = v)
            .Int16(0x91ec, "DS:91ec lunge_timer", s => s.LungeTimer, (s, v) => s.LungeTimer = v)
            .Int16(0x96f2, "DS:96f2 sneak_steps", s => s.SneakSteps, (s, v) => s.SneakSteps = v)
            .Flag(0xbb56, "DS:bb56 player_moving", s => s.PlayerMoving, (s, v) => s.PlayerMoving = v)
            .Int16(0xc998, "DS:c998 building_color", s => s.BuildingColor, (s, v) => s.BuildingColor = v)
            .Int16(0xb90e, "DS:b90e player_state", s => s.PlayerState, (s, v) => s.PlayerState = v)
            .Int16(0x1c44, "DS:1c44 wanted_facing", s => s.WantedFacing, (s, v) => s.WantedFacing = v)
            .Int16(0x1bc8, "DS:1bc8 game_speed", s => s.GameSpeed, (s, v) => s.GameSpeed = v)
            .Bytes(0x1c46, 8, "DS:1c46 pause_text", s => s.PauseText.Bytes)
            .Bytes(0x1c4e, 41, "DS:1c4e quit_text", s => s.QuitText.Bytes)
            .Int16Array(0x1c78, 9, "DS:1c78 joy_scancode", s => s.JoyScancode)
            .Int16(0x4310, "DS:4310 walk_hold", s => s.WalkHold, (s, v) => s.WalkHold = v)
            .Int16(0x4312, "DS:4312 last_key", s => s.LastKey, (s, v) => s.LastKey = v)
            .Int16(0xbb64, "DS:bb64 player_pose", s => s.PlayerPose, (s, v) => s.PlayerPose = v)
            .Flag(0x430c, "DS:430c disguise_shown", s => s.DisguiseShown, (s, v) => s.DisguiseShown = v)
            .Int16(0x430e, "DS:430e gas_shown", s => s.GasShown, (s, v) => s.GasShown = v)
            .Flag(0xbb6c, "DS:bb6c siren_sounded", s => s.SirenSounded, (s, v) => s.SirenSounded = v)
            .Bytes(0x1c8c, 5, "DS:1c8c aim_chance_colour", s => s.AimChanceColour)
            .Int16(0xb910, "DS:b910 room_left",s => s.RoomLeft, (s, v) => s.RoomLeft = v)
            .Int16(0xb916, "DS:b916 room_top", s => s.RoomTop, (s, v) => s.RoomTop = v)
            .Int16(0x9b6a, "DS:9b6a room_right", s => s.RoomRight, (s, v) => s.RoomRight = v)
            .Int16(0x9b74, "DS:9b74 room_bottom", s => s.RoomBottom, (s, v) => s.RoomBottom = v)
            .Text(0x2000, 7, "DS:2000 \"Street\"", s => s.StrStreet, (s, v) => s.StrStreet = v)
            .Bytes(0x1b98, 16, "DS:1b98 cga_map_input_box", s => s.CgaMapInputBox)
            .Text(0x1f67, 16, "DS:1f67 \"Enter PassWord:\"", s => s.StrEnterPassword, (s, v) => s.StrEnterPassword = v)
            .Text(0x1f77, 1, "DS:1f77 \"\"", s => s.StrPasswordEmpty, (s, v) => s.StrPasswordEmpty = v)
            .Text(0x1f78, 17, "DS:1f78 \"Access CONFIRMED\"", s => s.StrAccessConfirmed, (s, v) => s.StrAccessConfirmed = v)
            .Text(0x1f89, 14, "DS:1f89 \"Access DENIED\"", s => s.StrAccessDenied, (s, v) => s.StrAccessDenied = v)
            .Text(0x1f97, 12, "DS:1f97 \"Search for:\"",s => s.StrSearchFor, (s, v) => s.StrSearchFor = v)
            .Text(0x1fa3, 6, "DS:1fa3 \" has\"", s => s.StrSearchHas, (s, v) => s.StrSearchHas = v)
            .Text(0x1fa9, 2, "DS:1fa9 \"a\"", s => s.StrSearchA, (s, v) => s.StrSearchA = v)
            .Text(0x1fab, 14, "DS:1fab \"no operatives\"", s => s.StrNoOperatives, (s, v) => s.StrNoOperatives = v)
            .Text(0x1fb9, 5, "DS:1fb9 \"in \"", s => s.StrSearchCityIn, (s, v) => s.StrSearchCityIn = v)
            .Text(0x1fbe, 2, "DS:1fbe line end", s => s.StrSearchCityEnd, (s, v) => s.StrSearchCityEnd = v)
            .Text(0x1fc0, 19, "DS:1fc0 \" has no operatives\"", s => s.StrHasNoOperatives, (s, v) => s.StrHasNoOperatives = v)
            .Text(0x1fd3, 5, "DS:1fd3 \"in \"", s => s.StrSearchOrgIn, (s, v) => s.StrSearchOrgIn = v)
            .Text(0x1fd8, 2, "DS:1fd8 line end", s => s.StrSearchOrgEnd, (s, v) => s.StrSearchOrgEnd = v)
            .Text(0x1fda, 9, "DS:1fda \" now in \"", s => s.StrSearchNowIn, (s, v) => s.StrSearchNowIn = v)
            .Text(0x1fe3, 3, "DS:1fe3 \"!\"", s => s.StrSearchObjectEnd, (s, v) => s.StrSearchObjectEnd = v)
            .Text(0x1fe6, 1, "DS:1fe6 \"\"", s => s.StrSearchEmpty, (s, v) => s.StrSearchEmpty = v)
            .Text(0x1fe7, 8, "DS:1fe7 \"No Data\"", s => s.StrNoData, (s, v) => s.StrNoData = v);
        (int Address, int Length)[] itemNames =
        [
            (0x2034, 7), (0x203b, 4), (0x203f, 7), (0x2046, 5), (0x204b, 19), (0x205e, 9), (0x2067, 12), (0x2073, 16),
            (0x2083, 23), (0x209a, 13), (0x20a7, 14), (0x20b5, 18), (0x20c7, 1), (0x20c8, 1), (0x20c9, 1), (0x20ca, 11),
        ];
        TextTable(layout, 0x20e0, "DS:20e0 item_names", itemNames, s => s.ItemNames);
        (int Address, int Length)[] buildingKinds =[(0x2f2a, 9), (0x2f33, 7), (0x2f3a, 11), (0x2f45, 12), (0x2f51, 8)];
        for (var i = 0; i < buildingKinds.Length; i++)
        {
            var level = i;
            var (address, length) = buildingKinds[level];
            layout.Text(address, length, $"DS:{address:x4} building kind {level + 1}", s => s.BuildingKinds[level], (s, v) => s.BuildingKinds[level] = v);
        }
        // the rest of this part's copy of the campaign, and the hand-off routines' variables
        return CampaignCopyLayout.AddTac(layout);
    }

    /// <summary>
    /// A table of texts of the program (<c>char *names[7]</c>): a fixed pointer per entry, and the text it points at as
    /// an entry of the state's array.
    /// </summary>
    private static void TextTable(StateLayout<RaidState> layout, int offset, string name, (int Address, int Length)[] texts, Func<RaidState, string[]> table)
    {
        for (var i = 0; i < texts.Length; i++)
        {
            var index = i;
            var (address, length) = texts[index];
            layout
                .FixedPointer(offset + 2 * index, $"{name}[{index}]", address)
                .Text(address, length, $"{name}[{index}] text", s => table(s)[index], (s, v) => table(s)[index] = v);
        }
    }

    /// <summary>A table of records, each placed with its own layout.</summary>
    private static void Records<TRecord>(StateLayout<RaidState> layout, int offset, int size, int count, string name,
        StateLayout<TRecord> recordLayout, Func<RaidState, TRecord[]> table)
        where TRecord : class
    {
        for (var i = 0; i < count; i++)
        {
            var n = i;
            var at = offset + size * n;
            layout.Part(at, $"DS:{at:x4} {name}[{n}]", recordLayout, s => table(s)[n]);
        }
    }
}
