using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>What the library's <c>write_chunk</c> and <c>read_chunk</c> (this part's write_table and read_table) need of this part's state.</summary>
public sealed partial class CampaignState : IHandOffCursor
{
    /// <summary>The library's name for <see cref="SaveMemOffset"/>.</summary>
    short IHandOffCursor.SharedCursor
    {
        get => SaveMemOffset;
        set => SaveMemOffset = value;
    }
}
