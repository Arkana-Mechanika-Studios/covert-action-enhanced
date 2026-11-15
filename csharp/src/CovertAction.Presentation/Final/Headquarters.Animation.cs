namespace CovertAction.Presentation.Final;

/// <summary>A drawing routine of the menus part. (The animation player's routines are the library's: LibraryAnimation.)</summary>
public sealed partial class Headquarters
{
    /// <summary>Fills the rectangle between two corners, both inclusive, on the screen.</summary>
    /// <param name="x1">Left edge.</param>
    /// <param name="y1">Top edge.</param>
    /// <param name="x2">Right edge, inclusive.</param>
    /// <param name="y2">Bottom edge, inclusive.</param>
    /// <param name="color">Palette index.</param>
    // orig: FINAL 0100:c0f4 fill_rect_corners (src/c/final/final_0100_c0f4.c) matched; tests: FinalOracleTests
    // FINAL's own copy (docs/source-map/shared-functions.csv: other code than the library's LibraryOutput.FillRectCorners).
    public void FillRectCorners(int x1, int y1, int x2, int y2, int color) =>
        _world.Graphics.FillRect(_state.Rp1, x1, y1, x2 - x1 + 1, y2 - y1 + 1, color);
}
