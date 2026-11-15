using CovertAction.Core.Shared;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence;

/// <summary>
/// The hand-off block as the original's bytes: 12000 bytes the launcher owns and every module reads and writes. The
/// rewrite passes a <see cref="SharedState"/> between its parts instead; this codec turns one into the other, for the
/// oracle tests (which start a function from the block the original had and compare the block it left) and for saved
/// campaigns, whose first 0x3f0 bytes are bytes 0x10..0x3ff of this block.
/// </summary>
/// <remarks>
/// Modelled so far: the hand-off bytes, the campaign scalars, the driver segments and the message text. The campaign
/// tables from 0x400 on get their typed records with the campaign and menu modules; until then
/// <see cref="StateLayout{TState}.Store"/> leaves those bytes as it finds them. The bytes with no known use
/// (0x05, 0x06, 0x10..0x1f, 0x54..0x5f, 0x66..0x1ff) are never modelled and pass through the same way.
/// </remarks>
public static class SharedBlockCodec
{
    /// <summary>The block's fields at their offsets (the reconstructed state.h).</summary>
    // orig: SHARED+0x00..0x3ff (src/c/include/state.h: struct shared_state and SHARED_TEXT)
    public static StateLayout<SharedState> Layout { get; } = new StateLayout<SharedState>("shared block")
        .Char(0x00, "SHARED+0x00 next_module", s => s.NextModule, (s, v) => s.NextModule = v)
        .Char(0x01, "SHARED+0x01 entered", s => s.Entered, (s, v) => s.Entered = v)
        .Char(0x02, "SHARED+0x02 gfx_driver", s => s.GfxDriver, (s, v) => s.GfxDriver = v)
        .Char(0x03, "SHARED+0x03 snd_driver", s => s.SndDriver, (s, v) => s.SndDriver = v)
        .Char(0x04, "SHARED+0x04 joystick", s => s.Joystick, (s, v) => s.Joystick = v)
        .Char(0x07, "SHARED+0x07 game_mode", s => s.GameMode, (s, v) => s.GameMode = v)
        .Char(0x08, "SHARED+0x08 person", s => s.Person, (s, v) => s.Person = v)
        .Char(0x09, "SHARED+0x09 value", s => s.Value, (s, v) => s.Value = v)
        .Char(0x0a, "SHARED+0x0a result", s => s.Result, (s, v) => s.Result = v)
        .Char(0x0b, "SHARED+0x0b wounds", s => s.Wounds, (s, v) => s.Wounds = v)
        .Char(0x0c, "SHARED+0x0c target", s => s.Target, (s, v) => s.Target = v)
        .Char(0x0d, "SHARED+0x0d not_hurt", s => s.NotHurt, (s, v) => s.NotHurt = v)
        .Char(0x0e, "SHARED+0x0e org_out", s => s.OrgOut, (s, v) => s.OrgOut = v)
        .Char(0x0f, "SHARED+0x0f elapsed", s => s.Elapsed, (s, v) => s.Elapsed = v)
        .Int16(0x20, "SHARED+0x20 city", s => s.City, (s, v) => s.City = v)
        .Int16(0x22, "SHARED+0x22 org", s => s.Org, (s, v) => s.Org = v)
        .Int16(0x24, "SHARED+0x24 flags", s => (short)s.Flags, (s, v) => s.Flags = (Options)v)
        .Int16(0x26, "SHARED+0x26 case_no", s => s.CaseNo, (s, v) => s.CaseNo = v)
        .Int16(0x28, "SHARED+0x28 plot_org_2", s => s.PlotOrg2, (s, v) => s.PlotOrg2 = v)
        .Int16(0x2a, "SHARED+0x2a plot_org_3", s => s.PlotOrg3, (s, v) => s.PlotOrg3 = v)
        .Int16(0x2c, "SHARED+0x2c plot_city_2", s => s.PlotCity2, (s, v) => s.PlotCity2 = v)
        .Int16(0x2e, "SHARED+0x2e plot_city_3", s => s.PlotCity3, (s, v) => s.PlotCity3 = v)
        .Int16(0x30, "SHARED+0x30 clock", s => s.Clock, (s, v) => s.Clock = v)
        .Int16(0x32, "SHARED+0x32 n_persons", s => s.NPersons, (s, v) => s.NPersons = v)
        .Int16(0x34, "SHARED+0x34 n_links", s => s.NLinks, (s, v) => s.NLinks = v)
        .Int16(0x36, "SHARED+0x36 plot_step", s => s.PlotStep, (s, v) => s.PlotStep = v)
        .Int16(0x38, "SHARED+0x38 u_38", s => s.MacGuffin, (s, v) => s.MacGuffin = v)
        .Int16(0x3a, "SHARED+0x3a level", s => s.Level, (s, v) => s.Level = v)
        .Int16(0x3c, "SHARED+0x3c random_seed", s => s.RandomSeed, (s, v) => s.RandomSeed = v)
        .Int16(0x3e, "SHARED+0x3e n_cast", s => s.NCast, (s, v) => s.NCast = v)
        .Int16(0x40, "SHARED+0x40 n_events", s => s.NEvents, (s, v) => s.NEvents = v)
        .Int16(0x42, "SHARED+0x42 sel_person", s => s.SelPerson, (s, v) => s.SelPerson = v)
        .Int16(0x44, "SHARED+0x44 gfx_letter", s => s.GfxLetter, (s, v) => s.GfxLetter = v)
        .Int16(0x46, "SHARED+0x46 n_msgs", s => s.NMsgs, (s, v) => s.NMsgs = v)
        .Int16(0x48, "SHARED+0x48 alert", s => s.Alert, (s, v) => s.Alert = v)
        .Int16(0x4a, "SHARED+0x4a city_mask", s => s.CityMask, (s, v) => s.CityMask = v)
        .Int16(0x4c, "SHARED+0x4c mask_4c", s => s.InterPol, (s, v) => s.InterPol = v)
        .Int16(0x4e, "SHARED+0x4e skill_bits", s => s.SkillBits, (s, v) => s.SkillBits = v)
        .Int16(0x50, "SHARED+0x50 level_copy", s => s.LevelCopy, (s, v) => s.LevelCopy = v)
        .Int16(0x52, "SHARED+0x52 heat", s => s.Heat, (s, v) => s.Heat = v)
        .Int16(0x60, "SHARED+0x60 seg_gfx", s => s.SegGfx, (s, v) => s.SegGfx = v)
        .Int16(0x62, "SHARED+0x62 seg_misc", s => s.SegMisc, (s, v) => s.SegMisc = v)
        .Int16(0x64, "SHARED+0x64 seg_snd", s => s.SegSnd, (s, v) => s.SegSnd = v)
        .Bytes(SharedState.TextOffset, SharedState.TablesOffset - SharedState.TextOffset, "SHARED+0x200 text", s => s.Text.Bytes);

    /// <summary>
    /// Reads the campaign tables of a block into the state. The scalars must have been loaded before: the cast's and
    /// the events' counts among them say where the tables lie.
    /// </summary>
    /// <param name="block">The 12000 bytes of the hand-off block.</param>
    /// <param name="state">The state whose <see cref="SharedState.Campaign"/> receives the tables.</param>
    public static void LoadCampaign(ReadOnlySpan<byte> block, SharedState state) =>
        CampaignTablesCodec.Load(block[SharedState.TablesOffset..], state.NCast, state.NEvents, state.Campaign);

    /// <summary>Writes the campaign tables of the state over a block.</summary>
    /// <param name="state">The state.</param>
    /// <param name="block">The 12000 bytes of the hand-off block.</param>
    // BUG: CA-6 at the hardest level two crime files make the tables longer than the block; the original writes on behind it. Here that is an error.
    public static void StoreCampaign(SharedState state, Span<byte> block) =>
        CampaignTablesCodec.Store(state.Campaign, block[SharedState.TablesOffset..]);
}
