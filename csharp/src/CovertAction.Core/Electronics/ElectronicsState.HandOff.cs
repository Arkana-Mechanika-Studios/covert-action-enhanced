using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Electronics;

/// <summary>
/// The rest of this part's copy of the campaign: the variables it only takes over from the hand-off block and hands
/// back (<see cref="ICampaignCopy"/>), and those of the hand-off routines themselves.
/// </summary>
public sealed partial class ElectronicsState : ICampaignCopy
{
    /// <summary>BUG DS:7644 TheOrg1 (MicroProse's name): the first organisation the plot is laid out around.</summary>
    public short TheOrg1 { get; set; }

    /// <summary>BUG DS:764a TheOrg2 (MicroProse's name): the second organisation of the plot.</summary>
    public short TheOrg2 { get; set; }

    /// <summary>BUG DS:7270 TheCity1 (MicroProse's name): the first city of the plot.</summary>
    public short TheCity1 { get; set; }

    /// <summary>BUG DS:7272 TheCity2 (MicroProse's name): the second city of the plot.</summary>
    public short TheCity2 { get; set; }

    /// <summary>BUG DS:8e54 MacGuffin (MicroProse's name): the hand-off block's word 0x38, which a new case sets to -1.</summary>
    public short MacGuffin { get; set; }

    /// <summary>BUG DS:763a TheSeed (MicroProse's name): the seed drawn from the clock when the game started.</summary>
    public short TheSeed { get; set; }

    /// <summary>BUG DS:7638 NEVENT (MicroProse's name): the events of the plot in <see cref="Ev"/>.</summary>
    public short NEVENT { get; set; }

    /// <summary>BUG DS:1532 Sprung (MicroProse's name): the person under investigation, -1 for none.</summary>
    public short Sprung { get; set; }

    /// <summary>BUG DS:77ac Doubles (MicroProse's name): one bit per organisation (the hand-off block's word 0x4a).</summary>
    public short Doubles { get; set; }

    /// <summary>BUG DS:8c50 InterPol (MicroProse's name): the documents the player has seen, one bit per organisation (bit 0 the Master Plan; the hand-off block's word 0x4c).</summary>
    public short InterPol { get; set; }

    /// <summary>BUG DS:7fbe skills (MicroProse's name): the agent's four skills, two bits each, and the game speeds above them.</summary>
    public short Skills { get; set; }

    /// <summary>BUG DS:779c Alerted (MicroProse's name): how alert the other side is to the agent.</summary>
    public short Alerted { get; set; }

    /// <summary>BUG DS:822c mc (MicroProse's name): the plot's four objects.</summary>
    public MacGuffins[] Mc { get; } = Records<MacGuffins>(CampaignTables.MacGuffinCount);

    /// <summary>
    /// The variables of the hand-off routines: BUG DS:3ae2 shared_cursor, DS:3ae4 state_handle, and the texts
    /// "env.sve" (DS:2e54 for writing, DS:2e5c for reading), "File Error: " (DS:2e64) and its line break (DS:2e71).
    /// </summary>
    public HandOffState HandOff { get; } = new();
}
