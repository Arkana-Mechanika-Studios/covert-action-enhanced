using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Raid;

/// <summary>
/// The rest of this part's copy of the campaign: the variables it only takes over from the hand-off block and hands
/// back (<see cref="ICampaignCopy"/>), and those of the hand-off routines themselves.
/// </summary>
public sealed partial class RaidState : ICampaignCopy
{
    /// <summary>Records of <see cref="Ac"/> and <see cref="Ac2"/>: the room the second table has up to the next variable, 32 as in the other parts.</summary>
    public const int CastCapacity = 32;

    /// <summary>Records of <see cref="Ev"/>: the room from the table's start to the next variable (<c>NextSyn</c>, DS:b3f0), 64 as in the other parts.</summary>
    public const int EventCapacity = 64;

    /// <summary>TAC DS:cbba InCity (MicroProse's name): the city the agent is in.</summary>
    public short InCity { get; set; }

    /// <summary>TAC DS:bb7e TheCrime (MicroProse's name): the number of the case's crime file.</summary>
    public short TheCrime { get; set; }

    /// <summary>TAC DS:bb84 TheOrg1 (MicroProse's name): the first organisation the plot is laid out around.</summary>
    public short TheOrg1 { get; set; }

    /// <summary>TAC DS:bb88 TheOrg2 (MicroProse's name): the second organisation of the plot.</summary>
    public short TheOrg2 { get; set; }

    /// <summary>TAC DS:b918 TheCity1 (MicroProse's name): the first city of the plot.</summary>
    public short TheCity1 { get; set; }

    /// <summary>TAC DS:b94a TheCity2 (MicroProse's name): the second city of the plot.</summary>
    public short TheCity2 { get; set; }

    /// <summary>TAC DS:a1e2 Nperson (MicroProse's name): the person records in use.</summary>
    public short Nperson { get; set; }

    /// <summary>TAC DS:b400 Nloc (MicroProse's name): the building records in use.</summary>
    public short Nloc { get; set; }

    /// <summary>TAC DS:b404 ChronTime (MicroProse's name): the step of its schedule the plot has reached.</summary>
    public short ChronTime { get; set; }

    /// <summary>TAC DS:ce00 MacGuffin (MicroProse's name): the hand-off block's word 0x38, which a new case sets to -1.</summary>
    public short MacGuffin { get; set; }

    /// <summary>TAC DS:bb74 TheSeed (MicroProse's name): the seed drawn from the clock when the game started.</summary>
    public short TheSeed { get; set; }

    /// <summary>TAC DS:bdf2 NACTIVITY (MicroProse's name): the members of the cast.</summary>
    public short NACTIVITY { get; set; }

    /// <summary>TAC DS:bb68 NEVENT (MicroProse's name): the events of the plot.</summary>
    public short NEVENT { get; set; }

    /// <summary>TAC DS:1abe Sprung (MicroProse's name): the person under investigation, -1 for none.</summary>
    public short Sprung { get; set; }

    /// <summary>TAC DS:cbae GMode (MicroProse's name): the graphics driver's letter ('C' the four-colour display).</summary>
    public sbyte GMode { get; set; }

    /// <summary>TAC DS:9b6c CGA (MicroProse's name): the four-colour display is in use.</summary>
    public bool CGA { get; set; }

    /// <summary>TAC DS:b3f0 NextSyn (MicroProse's name): the records in use in the case log.</summary>
    public short NextSyn { get; set; }

    /// <summary>TAC DS:bdf0 Doubles (MicroProse's name): one bit per organisation (the hand-off block's word 0x4a).</summary>
    public short Doubles { get; set; }

    /// <summary>TAC DS:cbfc InterPol (MicroProse's name): the documents the player has seen, one bit per organisation (bit 0 the Master Plan; the hand-off block's word 0x4c).</summary>
    public short InterPol { get; set; }

    /// <summary>TAC DS:c600 skills (MicroProse's name): the agent's four skills, two bits each, and the game speeds above them.</summary>
    public short Skills { get; set; }

    /// <summary>TAC DS:bde0 Alerted (MicroProse's name): how alert the other side is to the agent.</summary>
    public short Alerted { get; set; }

    /// <summary>TAC DS:98d8 pl (MicroProse's name): the head of the campaign (the plot, its MasterMind's organisation and city, the date, the agent's career).</summary>
    public Plot Pl { get; } = new();

    /// <summary>TAC DS:caea ac2 (MicroProse's name): the cast's second records.</summary>
    public Activity2[] Ac2 { get; } = [.. Enumerable.Range(0, CastCapacity).Select(_ => new Activity2())];

    /// <summary>TAC DS:a8f0 ev (MicroProse's name): the events of the plot.</summary>
    public Event[] Ev { get; } = [.. Enumerable.Range(0, EventCapacity).Select(_ => new Event())];

    /// <summary>TAC DS:0afe lc (MicroProse's name): the campaign's buildings.</summary>
    public Location[] Lc { get; } = [.. Enumerable.Range(0, CampaignTables.LocationCount).Select(_ => new Location())];

    /// <summary>TAC DS:16fe cl (MicroProse's name): the clues.</summary>
    public Clue[] Cl { get; } = [.. Enumerable.Range(0, CampaignTables.ClueCount).Select(_ => new Clue())];

    /// <summary>TAC DS:a1e6 sn (MicroProse's name): the case log.</summary>
    public Synopsis[] Sn { get; } = [.. Enumerable.Range(0, CampaignTables.SynopsisCount).Select(_ => new Synopsis())];

    /// <summary>TAC DS:cbdc: the second half of the counter block, a counter per organisation.</summary>
    public short[] OrgCounters { get; } = new short[CampaignTables.WorldSize];

    /// <summary>
    /// The variables of the hand-off routines: TAC DS:7378 shared_cursor, DS:737a env_handle, and the texts
    /// "env.sve" (DS:36fc for writing, DS:3704 for reading), "File Error: " (DS:370c) and its line break (DS:3719).
    /// </summary>
    public HandOffState HandOff { get; } = new();
}
