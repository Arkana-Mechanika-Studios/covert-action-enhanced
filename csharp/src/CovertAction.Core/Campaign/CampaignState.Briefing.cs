namespace CovertAction.Core.Campaign;

/// <summary>The variables of the start of a case.</summary>
public sealed partial class CampaignState
{
    /// <summary>The texts of the case's briefing (GAME DS:27a6..2850).</summary>
    public BriefingWords Briefing { get; } = new();
}
