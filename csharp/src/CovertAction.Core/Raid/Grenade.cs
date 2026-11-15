namespace CovertAction.Core.Raid;

/// <summary>
/// A grenade in flight or going off, or a trap laid on the floor (F9). Grenades are of three kinds: 0 fragmentation
/// (kills guards, wounds the player), 1 gas (fills the room) and 2 stun (knocks out); a guard's grenade has 0x80 added.
/// </summary>
// orig: struct Grenade, 10 bytes (src/c/include/tac.h); gr[16] at TAC DS:9d78
public sealed class Grenade
{
    /// <summary>The value of <see cref="Type"/> of a free slot.</summary>
    public const short Free = -1;

    /// <summary>Added to <see cref="Type"/> when a guard threw the grenade.</summary>
    public const short ThrownByGuard = 0x80;

    /// <summary>The kind: fragmentation.</summary>
    public const int Fragmentation = 0;

    /// <summary>The kind: gas.</summary>
    public const int Gas = 1;

    /// <summary>The kind: stun.</summary>
    public const int Stun = 2;

    /// <summary>The value of <see cref="Time"/> of a remote-controlled trap.</summary>
    public const short RemoteTrap = -0x270f;

    /// <summary>The value of <see cref="Time"/> of a booby trap (a guard who steps near sets it off).</summary>
    public const short BoobyTrap = -0x270e;

    /// <summary>The value of <see cref="Time"/> when the grenade goes off; the burst then counts up to 0.</summary>
    public const short Burst = -0x3f;

    /// <summary>+00 px: x in pixels of the map.</summary>
    public short Px { get; set; }

    /// <summary>+02 py: y in pixels of the map.</summary>
    public short Py { get; set; }

    /// <summary>+04 dir: the facing it flies in, 1..8.</summary>
    public short Dir { get; set; }

    /// <summary>+06 time: above 0 the frames of flight left; <see cref="Burst"/> to -1 the burst; or a trap's mark.</summary>
    public short Time { get; set; }

    /// <summary>+08 type: the kind, plus <see cref="ThrownByGuard"/>; <see cref="Free"/> for a free slot.</summary>
    public short Type { get; set; }
}
