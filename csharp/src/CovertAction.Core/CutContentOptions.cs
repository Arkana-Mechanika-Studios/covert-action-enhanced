namespace CovertAction.Core;

/// <summary>
/// The original's cut content as a player's options (docs/cut-content.md): scenes and modes that the original's own files
/// still hold and its programs never reach. Each option is off by default; with all of them off the game plays exactly as the
/// original (<see cref="OriginalBehaviour.CutContent"/>).
/// </summary>
/// <param name="CapturedDossier">CC-1, CC-19 (settings key cutContent.capturedDossier, D-660): the animation CAPTRD01.PAN, a dossier page stamped CAPTURED, plays when a cast member is captured.</param>
public sealed record CutContentOptions(bool CapturedDossier = false)
{
    /// <summary>The original: none of the cut content.</summary>
    public static CutContentOptions None { get; } = new();
}
