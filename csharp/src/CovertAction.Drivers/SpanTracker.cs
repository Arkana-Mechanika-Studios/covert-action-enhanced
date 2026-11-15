using CovertAction.Core.Graphics;

namespace CovertAction.Drivers;

/// <summary>
/// Remembers which parts of a page moving images were drawn on, so that exactly those parts can be put back from a
/// clean copy before the next frame: MicroProse's library keeps, for every row of the screen, the left-most and the
/// right-most column an image touched since the last clean-up.
/// </summary>
/// <remarks>
/// There are two such records, one for pages with an even number and one for pages with an odd number, because an
/// animation can draw on two pages in turn. A row nothing touched has its left edge right of its right edge.
/// </remarks>
// orig: FINAL 0000:0d8b sub_final_0000_0d8b thunk; also GAME 0000:0cff; its variables are at FINAL DS:501a (the first and last touched row and the tables' addresses, per record); tests: SpanTrackerTests
public sealed class SpanTracker
{
    private const ushort NoRow = 0xffff;

    private sealed class Record
    {
        public ushort FirstRow = NoRow;
        public ushort LastRow;
        public readonly ushort[] Left = Enumerable.Repeat(NoRow, McgaGraphicsDriver.Height).ToArray();
        public readonly ushort[] Right = new ushort[McgaGraphicsDriver.Height];
    }

    private readonly Record[] _records = [new(), new()];

    private Record For(int page) => _records[page & 1];

    /// <summary>The first row touched on the pages of a page's kind; 0xFFFF when none was.</summary>
    public ushort FirstRow(int page) => For(page).FirstRow;

    /// <summary>The last row touched; 0 when none was.</summary>
    public ushort LastRow(int page) => For(page).LastRow;

    /// <summary>For each row of the screen, the left-most column touched.</summary>
    public ReadOnlySpan<ushort> Left(int page) => For(page).Left;

    /// <summary>For each row of the screen, the right-most column touched.</summary>
    public ReadOnlySpan<ushort> Right(int page) => For(page).Right;

    /// <summary>Notes that an image was drawn on a part of a port's page.</summary>
    /// <param name="port">The port the image was drawn on.</param>
    /// <param name="area">The part of the port's window that was drawn on; nothing is noted for an empty one.</param>
    public void Track(RasterPort port, ImageArea area) => Track(For(port.PageNr), port, area);

    /// <summary>Notes the same for the pages of the other kind than the port's (odd for an even page, even for an odd one).</summary>
    /// <param name="port">The port the image was drawn on.</param>
    /// <param name="area">The part of the port's window that was drawn on.</param>
    public void TrackOtherKind(RasterPort port, ImageArea area) => Track(For(port.PageNr ^ 1), port, area);

    private static void Track(Record record, RasterPort port, ImageArea area)
    {
        if (area.Width == 0)
        {
            return;
        }
        var top = (ushort)(area.Y + port.YBgn);
        var bottom = (ushort)(top + area.Height - 1);
        var left = (ushort)(area.X + port.XBgn);
        var right = (ushort)(left + area.Width - 1);
        record.FirstRow = Math.Min(record.FirstRow, top);
        record.LastRow = Math.Max(record.LastRow, bottom);
        // C: the original's loop looks at the first row before it compares with the last
        var row = top;
        do
        {
            record.Left[row] = Math.Min(record.Left[row], left);
            record.Right[row] = Math.Max(record.Right[row], right);
            row++;
        }
        while (row <= bottom);
    }

    /// <summary>Forgets what was touched on the pages of a page's kind.</summary>
    /// <param name="page">A page number of that kind.</param>
    public void Clear(int page)
    {
        var record = For(page);
        if ((short)record.FirstRow < 0)
        {
            return;
        }
        for (int row = record.FirstRow; row <= record.LastRow; row++)
        {
            record.Left[row] = NoRow;
            record.Right[row] = 0;
        }
        record.FirstRow = NoRow;
        record.LastRow = 0;
    }
}
