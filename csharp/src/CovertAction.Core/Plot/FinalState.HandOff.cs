using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Final;

/// <summary>
/// The rest of this part's copy of the campaign: the variables it only takes over from the hand-off block or a save
/// file and hands on again (<see cref="ICampaignCopy"/>), and those of the hand-off routines themselves.
/// </summary>
public sealed partial class FinalState : ICampaignCopy
{
    /// <summary>FINAL DS:9e8a TheSeed (MicroProse's name): the seed drawn from the clock when the game started.</summary>
    public short TheSeed { get; set; }

    /// <summary>FINAL DS:1548 Sprung (MicroProse's name): the person under investigation, -1 for none.</summary>
    public short Sprung { get; set; }

    /// <summary>FINAL DS:ae78 InterPol (MicroProse's name): the documents the player has seen, one bit per organisation (bit 0 the Master Plan; the hand-off block's word 0x4c).</summary>
    public short InterPol { get; set; }

    /// <summary>
    /// The variables of the hand-off routines: FINAL DS:5e0c state_offset, DS:5e0e state_handle, and the texts
    /// "env.sve" (DS:4a30 for writing, DS:4a38 for reading), "File Error: " (DS:4a40) and its line break (DS:4a4d).
    /// </summary>
    public HandOffState HandOff { get; } = new();

    // ---- the campaign's variables that a new case starts afresh (the hand-off block's) ----
    // The nine members below are also in the FINAL package's branch (FinalState.cs, with these comments): when that
    // branch is merged, they go from this file.

    /// <summary>FINAL DS:ae36 InCity (MicroProse's name): the city the agent is in.</summary>
    public short InCity { get; set; }

    /// <summary>FINAL DS:b374 AtLoc (MicroProse's name): the organisation whose building the agent is at.</summary>
    public short AtLoc { get; set; }

    /// <summary>FINAL DS:9f8c Alerted (MicroProse's name): how alert the other side is to the agent.</summary>
    public short Alerted { get; set; }

    /// <summary>FINAL DS:9fa0 Doubles (MicroProse's name): the organisations (bit n: organisation n) with a Double Agent in the CIA.</summary>
    public short Doubles { get; set; }

    /// <summary>FINAL DS:96d2 ChronTime (MicroProse's name): the step the plot has reached.</summary>
    public short ChronTime { get; set; }

    /// <summary>FINAL DS:b07e MacGuffin (MicroProse's name): the hand-off block's word 0x38, which a new case sets to -1 and no program reads.</summary>
    public short MacGuffin { get; set; }

    /// <summary>FINAL DS:ae38 active (MicroProse's name): a counter per city, raised when a person is seen there.</summary>
    public short[] Active { get; } = new short[CampaignTables.WorldSize];

    /// <summary>FINAL DS:ae58 org_counters: a counter per organisation, raised when a person's organisation becomes known.</summary>
    public short[] OrgCounters { get; } = new short[CampaignTables.WorldSize];

    /// <summary>
    /// FINAL DS:a7b2 skills (MicroProse's name): the agent's skills, two bits each from bit 0 (combat, driving,
    /// cryptography, electronics; 0 to 3), and in the high byte flags the training screen sets (bits 8 and 10).
    /// </summary>
    public short Skills { get; set; }
}
