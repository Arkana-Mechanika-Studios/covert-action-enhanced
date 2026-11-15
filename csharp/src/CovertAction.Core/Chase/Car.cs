namespace CovertAction.Core.Chase;

/// <summary>
/// One of the three cars of a car chase. Car 0 is the quarry the player tails (or, when the player is the one being
/// chased, the player's own car); cars 1 and 2 are the player's two tail cars (or the two pursuers).
/// </summary>
/// <remarks>
/// Positions are world units: 32 to a tile of the 24 x 24 tile city, x to the right, y downwards. A car moves at most
/// one unit per frame and chooses its way only at a tile centre, where x and y are both multiples of 32. Some fields
/// mean one thing for a car the player drives and another for the opposing car, whose position the player only knows
/// where it was last seen (its "mark"); each field says both. The original also uses <c>cars[1]</c>'s mark as the
/// "follow point" the auto-driving tail cars steer to (the reconstructed chase.h, docs/data-model/chase.md section 1).
/// </remarks>
// orig: struct car, 20 bytes (src/c/include/chase.h); cars[3] at CHASE DS:4290
public sealed class Car
{
    /// <summary>Attribute bits 0-1: the speed class 0..2; the car's top gear is the class plus 2.</summary>
    public const short SpeedClassMask = 3;

    /// <summary>Attribute bit 2: a tracking device; the dashboard shows where the quarry is.</summary>
    public const short Tracker = 4;

    /// <summary>Attribute bit 3: "Excellent" handling; the car may turn round on the spot.</summary>
    public const short Handling = 8;

    /// <summary>Attribute bits 4-5: how conspicuous the car is, 0..3 (Low, Moderate, High, Extreme).</summary>
    public const short ConspicuousnessMask = 0x30;

    /// <summary>The shift that brings the conspicuousness down to 0..3.</summary>
    public const int ConspicuousnessShift = 4;

    /// <summary>+00: world x of the car's centre, in units.</summary>
    public short X { get; set; }

    /// <summary>+02: world y of the car's centre, in units.</summary>
    public short Y { get; set; }

    /// <summary>+04: the heading: 0 up, 1 right, 2 down, 3 left (<see cref="Directions"/>).</summary>
    public short Dir { get; set; }

    /// <summary>
    /// +06: for a car the player drives, the auto-follow lamp (F10 lights it, an arrow key puts it out): the car
    /// steers by itself. For the opposing car: a tail car sees it in this frame, so its mark is refreshed. The original
    /// only ever stores 0 and 1 here.
    /// </summary>
    public bool Flag { get; set; }

    /// <summary>
    /// +08: for the opposing car, where it was last seen (-1: nowhere yet); the street view and the map draw it there,
    /// not where it really is. For <c>cars[1]</c> also the follow point of the tail cars.
    /// </summary>
    public short MarkX { get; set; }

    /// <summary>+0a: the y of the mark (see <see cref="MarkX"/>).</summary>
    public short MarkY { get; set; }

    /// <summary>
    /// +0c: a turn the player asked for (an arrow key), taken at the next tile centre; -1 for none. For the opposing
    /// car: the heading it had when its mark was set, which picks the sprite drawn at the mark.
    /// </summary>
    public short Turn { get; set; }

    /// <summary>
    /// +0e: suspicion. For a tail car: how long the quarry has been watching it (it grows while the quarry sees the
    /// car and falls by one each frame it does not). For car 0: the quarry's suspicion of the whole tail.
    /// </summary>
    public short Alert { get; set; }

    /// <summary>+10: the gear 0..4; the dashboard shows 20 mph per gear, and the gear picks the car's speed pattern.</summary>
    public short Gear { get; set; }

    /// <summary>
    /// +12: the car's attributes, chosen by the player in the car offer screen (only cars 1 and 2 get any): the speed
    /// class, <see cref="Tracker"/>, <see cref="Handling"/> and the conspicuousness.
    /// </summary>
    public short Attr { get; set; }

    /// <summary>The highest gear the car can reach: its speed class plus 2.</summary>
    public int TopGear => (Attr & SpeedClassMask) + 2;

    /// <summary>The car's conspicuousness 0..3 (Low, Moderate, High, Extreme).</summary>
    public int Conspicuousness => (Attr & ConspicuousnessMask) >> ConspicuousnessShift;
}
