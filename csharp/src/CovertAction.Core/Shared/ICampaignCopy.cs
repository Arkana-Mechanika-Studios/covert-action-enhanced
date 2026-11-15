using CovertAction.Core.Records;

namespace CovertAction.Core.Shared;

/// <summary>
/// A part's own copy of the campaign. The original's programs do not work on the hand-off block: each one copies
/// the campaign out of it into its own variables when it starts (<c>read_env_state</c>) and back when it ends
/// (<c>write_env_state</c>), and the menus part reads and writes save files the same way. A part's state implements
/// this interface, so that the one C# implementation of the two routines works on any of them.
/// </summary>
/// <remarks>
/// <para>
/// The members carry MicroProse's own names (the Amiga version's symbols), as every part's state does. The 24
/// scalars are the words of the hand-off block from byte 0x20 to 0x53 (<see cref="SharedState"/> says which word is
/// which; the options, <c>Status</c>, are the library's); the tables are the twelve of <see cref="CampaignTables"/>,
/// in a part's data as arrays with room for 32 members of the cast and 64 events.
/// </para>
/// <para>
/// The menus part, the building raid and the electronics part implement it. The campaign part has the same
/// variables but other code for the two routines (its <c>save_game</c> and <c>load_game</c>).
/// </para>
/// </remarks>
// orig: FINAL DS:ae36 InCity ... DS:9f8c Alerted and the tables from DS:7df2 pl on (src/c/include/final.h), the variables of write_env_state and read_env_state; TAC's and BUG's copies likewise (the places: Persistence.Layout.CampaignCopyLayout)
public interface ICampaignCopy : ILibraryState
{
    // ---- the scalars, in the order of their words in the hand-off block ----

    /// <summary>InCity: the city the agent is in (index of <see cref="Ct"/>).</summary>
    short InCity { get; set; }

    /// <summary>AtLoc: the organisation whose building the agent is at (index of <see cref="Org"/>).</summary>
    short AtLoc { get; set; }

    /// <summary>TheCrime: the number of the case's crime file.</summary>
    short TheCrime { get; set; }

    /// <summary>TheOrg1: the first organisation the plot is laid out around.</summary>
    short TheOrg1 { get; set; }

    /// <summary>TheOrg2: the second organisation of the plot.</summary>
    short TheOrg2 { get; set; }

    /// <summary>TheCity1: the first city of the plot.</summary>
    short TheCity1 { get; set; }

    /// <summary>TheCity2: the second city of the plot.</summary>
    short TheCity2 { get; set; }

    /// <summary>Gtime: the campaign's clock, in minutes since the case began.</summary>
    short Gtime { get; set; }

    /// <summary>Nperson: the person records in use.</summary>
    short Nperson { get; set; }

    /// <summary>Nloc: the building records in use.</summary>
    short Nloc { get; set; }

    /// <summary>ChronTime: the step of its schedule the plot has reached.</summary>
    short ChronTime { get; set; }

    /// <summary>MacGuffin: the hand-off block's word 0x38, which a new case sets to -1.</summary>
    short MacGuffin { get; set; }

    /// <summary>Diff (diff): the level of play, 0 to 3.</summary>
    short Diff { get; set; }

    /// <summary>TheSeed: the seed the menus part drew from the clock when the game started.</summary>
    short TheSeed { get; set; }

    /// <summary>NACTIVITY: the members of the cast in <see cref="Ac"/> and <see cref="Ac2"/>.</summary>
    short NACTIVITY { get; set; }

    /// <summary>NEVENT: the events of the plot in <see cref="Ev"/>.</summary>
    short NEVENT { get; set; }

    /// <summary>Sprung: the person under investigation, -1 for none.</summary>
    short Sprung { get; set; }

    /// <summary>GMode: the graphics driver's letter ('C' the four-colour display, 'E', 'M' ...).</summary>
    sbyte GMode { get; set; }

    /// <summary>NextSyn: the records in use in the case log <see cref="Sn"/>.</summary>
    short NextSyn { get; set; }

    /// <summary>Doubles: one bit per organisation (the hand-off block's word 0x4a).</summary>
    short Doubles { get; set; }

    /// <summary>InterPol: the documents the player has seen, one bit per organisation (bit 0 the Master Plan; the hand-off block's word 0x4c).</summary>
    short InterPol { get; set; }

    /// <summary>Skills (skills): the agent's four skills, two bits each, and the game speeds above them.</summary>
    short Skills { get; set; }

    /// <summary>Alerted: how alert the other side is to the agent.</summary>
    short Alerted { get; set; }

    /// <summary>CGA: the four-colour display is in use; set from <see cref="GMode"/> whenever the campaign is taken over.</summary>
    bool CGA { get; set; }

    // ---- the twelve tables, in the order they are handed on ----

    /// <summary>pl: the head of the campaign (the plot, its MasterMind, the date, the agent's career).</summary>
    Plot Pl { get; }

    /// <summary>ac: the cast of the case; <see cref="NACTIVITY"/> are in use.</summary>
    Activity[] Ac { get; }

    /// <summary>ac2: the cast's second records, in the same order.</summary>
    Activity2[] Ac2 { get; }

    /// <summary>ev: the events of the plot; <see cref="NEVENT"/> are in use.</summary>
    Event[] Ev { get; }

    /// <summary>ct: the world's 16 cities.</summary>
    City[] Ct { get; }

    /// <summary>org: the world's 16 organisations.</summary>
    Org[] Org { get; }

    /// <summary>ps: the 64 persons.</summary>
    Person[] Ps { get; }

    /// <summary>lc: the 256 buildings.</summary>
    Location[] Lc { get; }

    /// <summary>cl: the 80 clues.</summary>
    Clue[] Cl { get; }

    /// <summary>sn: the case log's 128 records.</summary>
    Synopsis[] Sn { get; }

    /// <summary>mc: the plot's four objects.</summary>
    MacGuffins[] Mc { get; }

    /// <summary>active: the first half of the last table, a counter per city.</summary>
    short[] Active { get; }

    /// <summary>The second half of the last table, a counter per organisation.</summary>
    short[] OrgCounters { get; }

    /// <summary>The variables of the hand-off routines themselves.</summary>
    HandOffState HandOff { get; }
}
