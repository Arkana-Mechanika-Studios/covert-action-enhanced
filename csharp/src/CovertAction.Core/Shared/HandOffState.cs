namespace CovertAction.Core.Shared;

/// <summary>
/// Where the next table of the campaign goes to or comes from while a part hands the campaign on: a place in the
/// hand-off block, or a file.
/// </summary>
/// <remarks>New code: what the library's <c>write_chunk</c> and <c>read_chunk</c> need of a part's state.</remarks>
public interface IHandOffCursor
{
    /// <summary>
    /// The place in the hand-off block of the table handed on next, in bytes from the block's start (the tables
    /// begin at <see cref="SharedState.TablesOffset"/>); 0 while the tables go to or come from a file instead.
    /// </summary>
    short SharedCursor { get; set; }
}

/// <summary>
/// The variables of the routines that hand the campaign on between the parts of the game, or to and from a save
/// file: every part that takes the campaign over and gives it back carries its own copies of them.
/// </summary>
// orig: BUG DS:3ae2 shared_cursor, DS:3ae4 state_handle, DS:2e54 str_env_sve and the texts behind it; the other programs' copies are in Persistence.Layout.CampaignCopyLayout
public sealed class HandOffState : IHandOffCursor
{
    /// <summary>shared_cursor (FINAL calls it state_offset): see <see cref="IHandOffCursor.SharedCursor"/>.</summary>
    public short SharedCursor { get; set; }

    /// <summary>state_handle (TAC calls it env_handle): the number of the save file being written or read.</summary>
    public short StateHandle { get; set; }

    /// <summary>
    /// str_env_sve: "env.sve", the name that stands for the hand-off block when the campaign is written (the file the
    /// launcher once passed the state in); any other name is a save file.
    /// </summary>
    public string StrEnvSve { get; set; } = "";

    /// <summary>The same name again, as the reading routine has it (its own copy of the text).</summary>
    public string StrEnvSveRead { get; set; } = "";

    /// <summary>"File Error: ", the start of the box shown when a save file cannot be opened.</summary>
    public string StrFileError { get; set; } = "";

    /// <summary>The line break that ends that box's text behind the file's name.</summary>
    public string StrFileErrorEnd { get; set; } = "";
}
