using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

public sealed partial class CampaignState
{
    /// <summary>The save screens' variables and texts (the library's screens, on GAME's copies of them).</summary>
    public SaveScreenState Saves { get; } = new();
}
