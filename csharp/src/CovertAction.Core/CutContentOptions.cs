namespace CovertAction.Core;

/// <summary>
/// The original's cut content as a player's options (docs/cut-content.md): scenes and modes that the original's own files
/// still hold and its programs never reach. Each option is off by default; with all of them off the game plays exactly as the
/// original (<see cref="OriginalBehaviour.CutContent"/>).
/// </summary>
/// <param name="CapturedDossier">CC-1, CC-19 (settings key cutContent.capturedDossier, D-660): the animation CAPTRD01.PAN, a dossier page stamped CAPTURED, plays when a cast member is captured.</param>
/// <param name="StandaloneChase">CC-2 (settings key cutContent.standaloneChase, D-661): the practice menu offers the car chase on its own, the mode CHASE.EXE carries for a play without a campaign: a random map with "Loc" labels, a random start and goal, every place shown.</param>
/// <param name="HardestCircuit">CC-16 (settings key cutContent.hardestCircuit, D-662): the electronics board's dormant difficulty 4, which the hardest level reaches for an agent of the lowest electronics rating (the original cuts it down to 3).</param>
public sealed record CutContentOptions(bool CapturedDossier = false, bool StandaloneChase = false, bool HardestCircuit = false)
{
    /// <summary>The original: none of the cut content.</summary>
    public static CutContentOptions None { get; } = new();
}
