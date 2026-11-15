using CovertAction.Core.Records;

namespace CovertAction.Core.Shared;

/// <summary>
/// The hand-off between the parts of the game. The original is eight programs; the launcher keeps one block of memory
/// alive while it runs them one after another, and each program finds there what the previous one left: which part
/// runs next, the question a minigame is asked and its answer, the options, and the whole campaign. In the rewrite the
/// parts are objects in one process and pass this object instead; its layout as the original's 12000 bytes is known
/// only to the persistence layer, which can still write it out byte for byte.
/// </summary>
/// <remarks>
/// Field by field description and evidence: the reconstructed state.h. Names are those of that header; where the Amiga
/// symbols give MicroProse's own name it is noted. Bytes 0x08..0x0b mean different things on the way into a minigame
/// and on the way out (see each field). The single-byte fields are <see cref="sbyte"/> and the 16-bit words
/// <see cref="short"/>, as wide as the original keeps them.
/// </remarks>
// orig: SHARED+0x00..0x65 struct shared_state (src/c/include/state.h); the block is COVERT DS:04c0, reached through 0000:04F0
public sealed class SharedState
{
    /// <summary>Size of the whole block in bytes.</summary>
    public const int Size = 12000;

    /// <summary>Offset of the 512 bytes of message text (<see cref="Text"/>).</summary>
    public const int TextOffset = 0x200;

    /// <summary>Offset of the campaign tables, which fill the rest of the block.</summary>
    public const int TablesOffset = 0x400;

    // ---- 0x00..0x0f: what the launcher and the next part read ----

    /// <summary>SHARED+0x00: the part to run next (0 campaign, 1 raid, 2 cryptography, 3 car chase, 4 wiretap, ... 0x11 menus; -1 leaves the game).</summary>
    public sbyte NextModule { get; set; }

    /// <summary>SHARED+0x01: 1 once the campaign part has been entered in this session.</summary>
    public sbyte Entered { get; set; }

    /// <summary>SHARED+0x02: the graphics driver's letter: 'E' EGA/VGA, 'C' CGA, 'M' MCGA.</summary>
    public sbyte GfxDriver { get; set; }

    /// <summary>SHARED+0x03: the sound driver's letter; 'N' is no sound.</summary>
    public sbyte SndDriver { get; set; }

    /// <summary>SHARED+0x04: 1 when a joystick is used.</summary>
    public sbyte Joystick { get; set; }

    /// <summary>SHARED+0x07: the part the campaign last handed off to; on return it tells how to read the answer bytes.</summary>
    public sbyte GameMode { get; set; }

    /// <summary>SHARED+0x08. In: the person concerned, -1 for none. Out of a raid: -1 = captured, else the planted bugs' value; out of a wiretap: percent tapped.</summary>
    public sbyte Person { get; set; }

    /// <summary>SHARED+0x09. In: the organisation a chase starts from. Out: raid suspicion 0..100, wiretap alarms, chase outcome (0 lost, 1 spotted, 2 arrest), or 1 = message decoded.</summary>
    public sbyte Value { get; set; }

    /// <summary>SHARED+0x0a. In: bit 0 = sex of the person followed. Out of a raid: result bits (1 object taken, 2 target arrested, 4 player hurt); out of a chase: alert percent.</summary>
    public sbyte Result { get; set; }

    /// <summary>SHARED+0x0b: out of a raid, the hits the player took (the campaign charges six hours of recovery each).</summary>
    public sbyte Wounds { get; set; }

    /// <summary>SHARED+0x0c: the person or organisation a raid is about.</summary>
    public sbyte Target { get; set; }

    /// <summary>SHARED+0x0d: 1 when the player came out unhurt.</summary>
    public sbyte NotHurt { get; set; }

    /// <summary>SHARED+0x0e: the organisation a chase ended at.</summary>
    public sbyte OrgOut { get; set; }

    /// <summary>SHARED+0x0f: the time the minigame took, in game minutes.</summary>
    public sbyte Elapsed { get; set; }

    // ---- 0x20..0x52: the campaign's scalar values (saved with the campaign, except Alert) ----

    /// <summary>SHARED+0x20: the city the player is in (index of the city records).</summary>
    public short City { get; set; }

    /// <summary>SHARED+0x22: the organisation the player is at (index of the organisation records).</summary>
    public short Org { get; set; }

    /// <summary>SHARED+0x24: the option and campaign flags.</summary>
    public Options Flags { get; set; }

    /// <summary>SHARED+0x26: the case number (selects the crime file).</summary>
    public short CaseNo { get; set; }

    /// <summary>SHARED+0x28: the second organisation of the plot.</summary>
    public short PlotOrg2 { get; set; }

    /// <summary>SHARED+0x2a: the third organisation of the plot.</summary>
    public short PlotOrg3 { get; set; }

    /// <summary>SHARED+0x2c: the city of the second plot organisation.</summary>
    public short PlotCity2 { get; set; }

    /// <summary>SHARED+0x2e: the city of the third plot organisation.</summary>
    public short PlotCity3 { get; set; }

    /// <summary>SHARED+0x30: game time in minutes since the case began (1440 to a day; the case ends at 40000).</summary>
    public short Clock { get; set; }

    /// <summary>SHARED+0x32: number of person records in use.</summary>
    public short NPersons { get; set; }

    /// <summary>SHARED+0x34: number of organisation/city link records in use.</summary>
    public short NLinks { get; set; }

    /// <summary>SHARED+0x36: the current step of the plot's schedule.</summary>
    public short PlotStep { get; set; }

    /// <summary>
    /// SHARED+0x38, MicroProse's <c>MacGuffin</c>: set to -1 for a new case (FINAL 0100:3f4c), then only carried and
    /// saved. No program reads it: in all six programs its address stands only in the parts' loaders and savers of the
    /// hand-off and in that one store, and the Amiga build has the same three uses of it (its state reader and writer,
    /// the new case).
    /// </summary>
    public short MacGuffin { get; set; }

    /// <summary>SHARED+0x3a: difficulty 0..3 (Local Disturbance, National Threat, Regional Conflict, Global Crisis).</summary>
    public short Level { get; set; }

    /// <summary>SHARED+0x3c: the seed the menus drew from the clock at start-up.</summary>
    public short RandomSeed { get; set; }

    /// <summary>SHARED+0x3e: number of cast records (the plot's participants, decoys included).</summary>
    public short NCast { get; set; }

    /// <summary>SHARED+0x40: number of event records of the plot.</summary>
    public short NEvents { get; set; }

    /// <summary>SHARED+0x42: the person under investigation, -1 for none.</summary>
    public short SelPerson { get; set; }

    /// <summary>SHARED+0x44: the graphics driver's letter again, as a word (the parts read its low byte).</summary>
    public short GfxLetter { get; set; }

    /// <summary>SHARED+0x46: number of message records.</summary>
    public short NMsgs { get; set; }

    /// <summary>SHARED+0x48: how alert the current organisation or city is; passed to the minigames, not saved.</summary>
    public short Alert { get; set; }

    /// <summary>SHARED+0x4a: one bit per city: flagged.</summary>
    public short CityMask { get; set; }

    /// <summary>
    /// SHARED+0x4c, MicroProse's <c>InterPol</c>: the documents the player has seen, one bit per organisation; bit 0 is
    /// the "Master Plan", bit n the personnel file of organisation n. Set by show_personnel_file (GAME 00f7:0978, TAC
    /// 00e8:b4a6, BUG 00e8:4c22, FINAL 0100:85ec), read by the "Inside Information" list (GAME 00f7:704a) and by the
    /// raid's evidence (TAC 00e8:3573: an organisation's file not yet seen is shown), cleared by FINAL 0100:0906.
    /// </summary>
    public short InterPol { get; set; }

    /// <summary>SHARED+0x4e: the agent's four skills, two bits each (combat, driving, cryptography, electronics), and the two game speeds above them.</summary>
    public short SkillBits { get; set; }

    /// <summary>SHARED+0x50: the campaign's copy of the level for the hand-off.</summary>
    public short LevelCopy { get; set; }

    /// <summary>SHARED+0x52: the "heat" counter the player's actions raise and time lowers.</summary>
    public short Heat { get; set; }

    // ---- 0x60..0x65: where the launcher loaded the drivers (meaningless outside DOS, kept for the byte layout) ----

    /// <summary>SHARED+0x60: segment of the graphics driver.</summary>
    public short SegGfx { get; set; }

    /// <summary>SHARED+0x62: segment of the keyboard and joystick driver.</summary>
    public short SegMisc { get; set; }

    /// <summary>SHARED+0x64: segment of the sound driver.</summary>
    public short SegSnd { get; set; }

    /// <summary>
    /// SHARED+0x200: the text handed to the next part, at most 511 characters: the plain text of the message the
    /// cryptography minigame enciphers.
    /// </summary>
    public TextBuffer Text { get; } = new(TablesOffset - TextOffset);

    /// <summary>
    /// SHARED+0x400: the campaign's tables (the case, the world, what the player knows). A part reads them when it
    /// starts and writes them back when it ends; <see cref="NCast"/> and <see cref="NEvents"/> say how long the
    /// cast and the events are.
    /// </summary>
    public CampaignTables Campaign { get; } = new();
}
