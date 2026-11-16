namespace CovertAction.Presentation.Library;

/// <summary>
/// A rectangle of the 320x200 screen that the mouse can point at: a choice's line in a box, a row of a list, a city's
/// dot on the flight map. New code (csharp/docs/MOUSE-SUPPORT.md; D-630): each screen builds its rectangles from the
/// same values its drawing uses, so a click lands where the highlight is drawn.
/// </summary>
/// <param name="X">Left edge, in pixels of the 320x200 screen.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct HitArea(int X, int Y, int Width, int Height)
{
    /// <summary>Whether a screen pixel lies inside.</summary>
    /// <param name="x">The pixel across.</param>
    /// <param name="y">The pixel down.</param>
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>The hit tests of the mouse in the menus and lists: which choice a screen pixel is on. New code (D-630).</summary>
public static class PointerHit
{
    /// <summary>
    /// The row of a list under a pixel: rows of one width, <paramref name="rowHeight"/> apart from the first row's top.
    /// </summary>
    /// <param name="x">The pixel across.</param>
    /// <param name="y">The pixel down.</param>
    /// <param name="firstRow">The first row's rectangle (its height is not used: each row reaches to the next one's top).</param>
    /// <param name="rowHeight">The distance from one row's top to the next one's.</param>
    /// <param name="rows">How many rows there are.</param>
    /// <returns>The row, 0 = the first; -1 for a pixel beside or outside the rows.</returns>
    public static int Row(int x, int y, HitArea firstRow, int rowHeight, int rows)
    {
        if (x < firstRow.X || x >= firstRow.X + firstRow.Width || y < firstRow.Y || rowHeight <= 0)
        {
            return -1;
        }
        var row = (y - firstRow.Y) / rowHeight;
        return row < rows ? row : -1;
    }

    /// <summary>The cell of a grid of equal cells under a pixel.</summary>
    /// <param name="x">The pixel across.</param>
    /// <param name="y">The pixel down.</param>
    /// <param name="grid">The grid's rectangle: its first cell's top left corner and the cell's width and height.</param>
    /// <param name="columns">How many cells across.</param>
    /// <param name="rows">How many cells down.</param>
    /// <returns>The cell's column and row (0 = the first); null for a pixel outside the grid.</returns>
    public static (int Column, int Row)? Cell(int x, int y, HitArea grid, int columns, int rows)
    {
        if (grid.Width <= 0 || grid.Height <= 0 || x < grid.X || y < grid.Y)
        {
            return null;
        }
        int column = (x - grid.X) / grid.Width, row = (y - grid.Y) / grid.Height;
        return column < columns && row < rows ? (column, row) : null;
    }

    /// <summary>
    /// The keypad direction (of the eight) nearest the vector from one point to another: 1 up, then clockwise (2 up-right,
    /// 3 right, 4 down-right, 5 down, 6 down-left, 7 left, 8 up-left), the numbering of the raid's facings and of the
    /// joystick's directions. Each direction owns the 45 degrees around it.
    /// </summary>
    /// <param name="dx">The vector's part across (positive to the right).</param>
    /// <param name="dy">The vector's part down (positive down the screen).</param>
    /// <returns>1..8; 0 when the vector is empty (the pointer is on the point).</returns>
    public static int Direction8(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return 0;
        }
        var degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        var sector = (int)Math.Round(degrees / 45, MidpointRounding.AwayFromZero);
        return ((sector % 8 + 8) % 8) + 1;
    }

    /// <summary>The first of some rectangles that holds a pixel.</summary>
    /// <param name="x">The pixel across.</param>
    /// <param name="y">The pixel down.</param>
    /// <param name="areas">The rectangles, in the order of their choices.</param>
    /// <returns>The rectangle's place in the list; -1 for none.</returns>
    public static int Area(int x, int y, IReadOnlyList<HitArea> areas)
    {
        for (var i = 0; i < areas.Count; i++)
        {
            if (areas[i].Contains(x, y))
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>What the mouse did since a menu or list last asked (<see cref="LibraryInput.ReadPointer"/>). New code (D-630).</summary>
public enum PointerAction
{
    /// <summary>Nothing (or a move that points at no choice).</summary>
    None,

    /// <summary>The pointer moved onto a choice: the highlight goes there, as the cursor keys would take it.</summary>
    Point,

    /// <summary>The left button was pressed: the choice under it (or -1, beside every choice) is taken, as with Return.</summary>
    Accept,

    /// <summary>The right button was pressed: as Esc.</summary>
    Cancel,
}

/// <summary>A mouse action and the choice it is about.</summary>
/// <param name="Action">What the mouse did.</param>
/// <param name="Choice">The choice under the pointer (for <see cref="PointerAction.Point"/> and <see cref="PointerAction.Accept"/>), -1 for none.</param>
public readonly record struct PointerEvent(PointerAction Action, int Choice)
{
    /// <summary>No mouse action.</summary>
    public static PointerEvent Nothing => new(PointerAction.None, -1);
}
