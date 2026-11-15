namespace CovertAction.Session.Tests;

/// <summary>
/// An area of the screen where the rewrite differs from the original on purpose: a deliberate difference registered in
/// csharp/docs/faithfulness (DIVERGENCES*.md) or a fixed bug of docs/original-bugs.md. The session comparison does not
/// count the pixels inside it, and names the entry in its report. Nothing else may be masked.
/// </summary>
/// <param name="Entry">The register entry (D-17, CA-24, ...).</param>
/// <param name="Part">The part of the game whose sessions it applies to (CHASE, CODE, ...).</param>
/// <param name="X">The area's left column on the 320x200 screen.</param>
/// <param name="Y">The area's top row.</param>
/// <param name="Width">The area's width in pixels.</param>
/// <param name="Height">The area's height in pixels.</param>
/// <param name="OnlyWhileAnimationNotLoaded">
/// The area applies only while the original's last animation file did not load (its <c>load_pan_file</c> answered 0,
/// recorded as <c>["pan_load", name, 0]</c>) and the animation has not been released since (<c>["pan_release"]</c>).
/// </param>
internal sealed record KnownDifference(string Entry, string Part, int X, int Y, int Width, int Height, bool OnlyWhileAnimationNotLoaded = false)
{
    /// <summary>The registered differences that show on the screen.</summary>
    public static IReadOnlyList<KnownDifference> All { get; } =
    [
        // D-17: the car chase copies its dashboard panel from one row below its picture sheet; the original's last
        // panel row (screen row 99, columns 200..319) is whatever DOS memory follows the sheet, the rewrite's is black.
        new("D-17", "CHASE", 200, 99, 120, 1),
        // D-11: the original keeps an animation's images in DOS memory; when they do not fit (the legacy start image's
        // small arena, or memory an earlier stakeout's animation still holds), load_pan_file fails and the original plays
        // on with a half-loaded animation: its geometry read from the new file, its images and program partly the old
        // ones, drawn anywhere on the screen. The rewrite's memory never runs short and plays the
        // animation. The whole screen, only while the original's animation is not loaded.
        new("D-11", "GAME", 0, 0, 320, 200, OnlyWhileAnimationNotLoaded: true),
    ];

    /// <summary>Whether the pixel at a screen position lies in a known difference of the part.</summary>
    /// <param name="part">The session's part.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="animationNotLoaded">Whether the original's last animation failed to load and is not released yet.</param>
    public static KnownDifference? At(string part, int x, int y, bool animationNotLoaded = false) =>
        All.FirstOrDefault(d => d.Part == part && (!d.OnlyWhileAnimationNotLoaded || animationNotLoaded)
            && x >= d.X && x < d.X + d.Width && y >= d.Y && y < d.Y + d.Height);
}
