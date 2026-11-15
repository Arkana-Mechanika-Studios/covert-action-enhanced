namespace CovertAction.Core.Raid;

/// <summary>What has happened in or to a room of the building (the room record's flag word).</summary>
// orig: ROOM_REMOVED .. ROOM_GAS (src/c/include/tac.h)
[Flags]
public enum RoomStatus : short
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The generator dropped the room: it is no part of the building.</summary>
    Removed = 0x01,

    /// <summary>The player has been in the room (in this raid, or in an earlier raid on the same building): drawn on the minimap.</summary>
    Visited = 0x02,

    /// <summary>A bug is planted in the room: guards in it show on the radar.</summary>
    Bugged = 0x04,

    /// <summary>A knocked-out guard lies in the room: another guard who finds him raises the alarm.</summary>
    Bodies = 0x08,

    /// <summary>A gas grenade went off in the room.</summary>
    Gas = 0x10,
}

/// <summary>
/// One room of the building: a rectangle of cells, the rooms it has a door to, its type (office, file room, ...) and
/// how far it is, in doors, from the room the alarm was raised in.
/// </summary>
/// <remarks>
/// Rooms are numbered 1 to <see cref="RaidState.Nroom"/>; room 0 is outside the building. A room is at most 10 x 10
/// cells. The original keeps the flag byte and the unused byte after it as one word, and so does this record.
/// </remarks>
// orig: struct Room, 14 bytes (src/c/include/tac.h); rm[0x18] at TAC DS:a05e
public sealed class Room
{
    /// <summary>+00 x: the room's first cell column, 0..39.</summary>
    public sbyte X { get; set; }

    /// <summary>+01 y: the room's first cell row, 0..31.</summary>
    public sbyte Y { get; set; }

    /// <summary>+02 dx: the room's width in cells.</summary>
    public sbyte Dx { get; set; }

    /// <summary>+03 dy: the room's height in cells.</summary>
    public sbyte Dy { get; set; }

    /// <summary>+04 doors: bit n set = a door joins this room to room n (see <see cref="BuildingRules.RoomBit"/> for the 16-bit quirk).</summary>
    public int Doors { get; set; }

    /// <summary>+08 status, with the unused byte +09 above it: the <see cref="RoomStatus"/> flags.</summary>
    public RoomStatus Status { get; set; }

    /// <summary>+0a type: the room's kind, an index into <see cref="RaidState.Rms"/> (0 office .. 9 storage room).</summary>
    public short Type { get; set; }

    /// <summary>+0c toDist: doors to pass from this room to the alarm room (<c>ToRoom</c>); -1 unreachable, 999 outside.</summary>
    public short ToDist { get; set; }
}

/// <summary>A door between two rooms (or between a room and the outside): the cell on the first room's side and the side of that cell the door is in.</summary>
/// <remarks>Every door fills two cells, one in each room; the second is the neighbour of (<see cref="X"/>, <see cref="Y"/>) across side <see cref="Dir"/>.</remarks>
// orig: struct Door, 5 bytes (src/c/include/tac.h); dr[0x60] at TAC DS:96f8
public sealed class Door
{
    /// <summary>+00 room1: the room on the first side; 0 is outside the building.</summary>
    public sbyte Room1 { get; set; }

    /// <summary>+01 room2: the room on the other side.</summary>
    public sbyte Room2 { get; set; }

    /// <summary>+02 x: the door's cell column on <see cref="Room1"/>'s side.</summary>
    public sbyte X { get; set; }

    /// <summary>+03 y: the door's cell row on <see cref="Room1"/>'s side.</summary>
    public sbyte Y { get; set; }

    /// <summary>+04 dir: the side of that cell the door is in: 0 north, 1 east, 2 south, 3 west.</summary>
    public sbyte Dir { get; set; }
}

/// <summary>One kind of room (office, file room, ...): its name, what it is worth to the player and where it may appear.</summary>
// orig: struct Rooms, 22 bytes (src/c/include/tac.h); rms[10] at TAC DS:00b2
public sealed class RoomType
{
    /// <summary>+00 name: the room's name shown in the title bar ("Office", "File Room", ...).</summary>
    public string Name { get; set; } = "";

    /// <summary>+10 intel: what a photograph (or a bug) in such a room is worth; guards also linger longer in a valuable room.</summary>
    public short Intel { get; set; }

    /// <summary>+12 size: the room sizes it fits, as bits: 1 up to 40 cells, 2 more than 40, 4 more than 72.</summary>
    public short Size { get; set; }

    /// <summary>+14 where: the kinds of building that may have such a room, as bits, tested against <see cref="RaidState.BuildingKindMask"/>.</summary>
    public short Where { get; set; }
}

/// <summary>What a tile of the building does: the <see cref="ObjectType.Bits"/> of a tile type.</summary>
// orig: TF_SOLID .. TF_TERMINAL (src/c/include/tac.h)
[Flags]
public enum TileFlags : short
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>Blocks walking and the guards' paths, and gives partial cover from sight.</summary>
    Solid = 0x001,

    /// <summary>Can be opened and closed (doors, drawers, safes).</summary>
    Openable = 0x002,

    /// <summary>A bug can be planted on it.</summary>
    Buggable = 0x004,

    /// <summary>It can be photographed.</summary>
    Photo = 0x008,

    /// <summary>A door: two cells, one in each room; an open door can be walked through.</summary>
    Door = 0x010,

    /// <summary>Blocks sight completely and stops a lunge (files, floor safe, refrigerator, computer).</summary>
    Tall = 0x020,

    /// <summary>Two cells wide: the cell beside holds the tile type one up or down.</summary>
    Wide = 0x040,

    /// <summary>Set on the two table tiles; nothing reads it.</summary>
    Unused80 = 0x080,

    /// <summary>Stands against a wall: placed on the cells along a wall, never in the open floor.</summary>
    AtWall = 0x100,

    /// <summary>A computer terminal: it holds a letter of the password.</summary>
    Terminal = 0x200,
}

/// <summary>One kind of tile (a wall piece, a door, a piece of furniture): its name, its picture and what it does.</summary>
/// <remarks>
/// Tiles 0..7 are walls (0..3 straight, by side; 4..7 corners), 8..11 doors (by side), 12..59 furniture in groups of
/// four orientations, 60 the plot object (its name is the object's) and 61 the local agent to arrest.
/// </remarks>
// orig: struct Object, 20 bytes (src/c/include/tac.h); ob[62] at TAC DS:018e
public sealed class ObjectType
{
    /// <summary>The length of <see cref="Name"/>.</summary>
    public const int NameLength = 12;

    /// <summary>
    /// +00 name: the tile's name, shown when the player looks at it ("Wall", "Desk", ...), ended by a zero character
    /// when it is shorter than the buffer. "Refrigerator" fills all twelve characters and has no end mark.
    /// </summary>
    // BUG: CA-31 the refrigerator's name has no end mark; the buffer is kept as the original has it.
    public char[] Name { get; } = new char[NameLength];

    /// <summary>
    /// The name as the game prints it: up to its end mark; a name without one runs on into the bytes of the record after
    /// it (the picture's x, then y, ...) up to the first zero byte.
    /// </summary>
    /// <returns>The printed text.</returns>
    /// <exception cref="Msc.MemoryOverrunException">No zero byte in the whole record.</exception>
    // BUG: CA-31 "Refrigerator" has no end mark and is printed with the low byte of its picture's x after it.
    public string PrintedName()
    {
        var end = Array.IndexOf(Name, '\0');
        if (end >= 0)
        {
            return new string(Name, 0, end);
        }
        byte[] after =
        [
            Msc.C16.LoByte(SpriteX), Msc.C16.HiByte(SpriteX), Msc.C16.LoByte(SpriteY), Msc.C16.HiByte(SpriteY),
            Msc.C16.LoByte((short)Bits), Msc.C16.HiByte((short)Bits), Msc.C16.LoByte(Where), Msc.C16.HiByte(Where),
        ];
        var length = Array.IndexOf(after, (byte)0);
        return length >= 0
            ? new string(Name) + System.Text.Encoding.Latin1.GetString(after, 0, length)
            : throw new Msc.MemoryOverrunException("a tile name without an end mark in its whole record");
    }

    /// <summary>+0c spriteX: x of the tile's 16 x 16 picture on the tile sheet.</summary>
    public short SpriteX { get; set; }

    /// <summary>+0e spriteY: y of the picture on the sheet; the open variant is 16 pixels lower.</summary>
    public short SpriteY { get; set; }

    /// <summary>+10 bits: what the tile does.</summary>
    public TileFlags Bits { get; set; }

    /// <summary>+12 where: bit n set = the tile may stand in a room of type n.</summary>
    public short Where { get; set; }
}
