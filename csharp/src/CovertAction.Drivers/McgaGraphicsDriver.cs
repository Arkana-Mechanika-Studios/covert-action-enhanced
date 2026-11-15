using CovertAction.Content;
using CovertAction.Core.Graphics;
using CovertAction.Platform;

namespace CovertAction.Drivers;

/// <summary>
/// The game's drawing code: what the original's graphics driver for the 256-colour MCGA/VGA mode (MGRAPHIC.EXE,
/// hand-written assembly) draws, written again in C#. It owns the game's screen, 320x200 pixels of one byte each
/// with a palette of 256 colours, and a few off-screen pages of the same size. Together with the two small routines
/// of MicroProse's library that sit in front of the driver (<see cref="FillRect"/>, <see cref="DrawLinePort"/>) it is
/// everything behind <see cref="IGraphicsDriver"/>.
/// </summary>
/// <remarks>
/// <para>
/// A page is 64000 pixels, row after row; the pixel (x, y) is number 320 * y + x. Like the original, the routines
/// do not clip (only lines are clipped, before they are drawn): a rectangle that runs past the right edge continues
/// on the next row. That is part of what the game shows (the cryptography screen's own first fill is a pixel too
/// wide), so it is kept.
/// </para>
/// <para>
/// The original counts pixels in 16-bit numbers, which shows in two ways that are kept as rules of
/// <see cref="Put"/>: a pixel number from 64000 to 65535 is below the last row and is not drawn, and a number past
/// 65535 starts again at the top of the page.
/// </para>
/// <para>
/// The entry points carry the names the game's code uses for them where it has one (symbols/names.txt), ours
/// otherwise (docs/source-map/additional-entries.csv). Proven against the oracle's driver vectors
/// (McgaOracleTests). Not reconstructed yet: the entries the cryptography part does not call (pictures, sprites,
/// the driver's own mouse pointer).
/// </para>
/// </remarks>
public sealed class McgaGraphicsDriver : IGraphicsDriver
{
    /// <summary>Pixels per row.</summary>
    public const int Width = 320;

    /// <summary>Rows.</summary>
    public const int Height = 200;

    /// <summary>The number of pages the driver's table has; page 0 is the screen.</summary>
    public const int PageCount = 8;

    /// <summary>Colour 0, what a pixel read from outside a page counts as.</summary>
    private const byte Black = 0;

    /// <summary>Rows in each of the two edge tables <see cref="FillRect"/> keeps.</summary>
    private const int EdgeTableRows = 220;

    private const int PageSize = Width * Height;

    private readonly FontSet _fonts;
    private readonly byte[] _defaultColours;
    private readonly ushort[] _leftEdges = new ushort[EdgeTableRows];
    private readonly ushort[] _rightEdges = new ushort[EdgeTableRows];

    /// <summary>The buffers pages can be given, by their number less one; the first is the screen's.</summary>
    private readonly List<byte[]> _buffers = [];

    private const int ScreenBuffer = 1;

    /// <summary>Creates the driver in the state the launcher leaves it in: the screen and two off-screen pages.</summary>
    /// <param name="fonts">The game's fonts (the player's FONTS.CV).</param>
    /// <param name="defaultColours">The driver's colour table: 128 colours of three bytes, 0..63 each (from the player's MGRAPHIC.EXE).</param>
    public McgaGraphicsDriver(FontSet fonts, byte[] defaultColours)
    {
        _fonts = fonts;
        _defaultColours = defaultColours;
        // The launcher gives the MCGA driver buffers for pages 1 and 2. Every other entry of the page table stays
        // what the driver file has there: the screen itself.
        var screen = new byte[PageSize];
        Pages = [screen, new byte[PageSize], new byte[PageSize], screen, screen, screen, screen, screen];
        _buffers.Add(screen);
        DrawPage = screen;
    }

    /// <summary>The pages, 64000 pixels each. Page 0 is the screen; a page nobody has set up yet is the screen too.</summary>
    public byte[][] Pages { get; }

    /// <summary>The display's 256 colours: three bytes each (red, green, blue), 0..63 as the VGA takes them.</summary>
    public byte[] Palette { get; } = new byte[3 * 256];

    /// <summary>The page fills and lines go to (set by <see cref="SelectPage"/>).</summary>
    public byte[] DrawPage { get; set; }

    /// <summary>The colour of fills and lines (set by <see cref="SetColor"/>).</summary>
    public byte Color { get; set; }

    /// <summary>How the pixels of a line combine with the picture (set by <see cref="SetColor"/>).</summary>
    public PixelOperation LineOperation { get; set; }

    /// <summary>The palette index of the display's border, which <see cref="SetPalette"/> chooses. A window has no border; a host may paint its letterbox with it.</summary>
    public byte Border { get; set; }

    /// <summary>The origin lines are drawn from: the left edge of the last port a line was begun for.</summary>
    public ushort LineOriginX { get; set; }

    /// <summary>The origin lines are drawn from: the top edge of the last port a line was begun for.</summary>
    public ushort LineOriginY { get; set; }

    /// <summary>The right edge (width minus one) of the last port a line was begun for; 319 until then. Fills consult it too (<see cref="FillSpans"/>).</summary>
    public ushort LineRight { get; set; } = Width - 1;

    /// <summary>The screen as a host shows it: the 64000 pixels of page 0.</summary>
    public ReadOnlySpan<byte> Screen => Pages[0];

    /// <summary>The palette as 256 colours of the form 0xRRGGBB, each 6-bit component widened to 8 bits.</summary>
    public void CopyColours(Span<uint> colours)
    {
        static uint Widen(byte sixBits) => (uint)((sixBits & 0x3f) << 2 | (sixBits & 0x3f) >> 4);
        for (var i = 0; i < 256; i++)
        {
            colours[i] = Widen(Palette[3 * i]) << 16 | Widen(Palette[3 * i + 1]) << 8 | Widen(Palette[3 * i + 2]);
        }
    }

    /// <summary>The number of the first pixel of a row. A row outside the screen is an error (the original looks such a row up beside its table of rows).</summary>
    private static int RowStart(int row) =>
        row is >= 0 and < Height
            ? row * Width
            : throw new ArgumentOutOfRangeException(nameof(row), row, "a row outside the screen");

    private byte[] Page(int number) => Pages[(ushort)number];

    /// <summary>Whether a pixel number, taken as the original's 16-bit number, is one of the page's 64000 pixels.</summary>
    private static bool OnPage(int at) => (ushort)at < PageSize;

    /// <summary>Sets a pixel of a page. A pixel number below the last row is not drawn; one past 65535 starts again at the top.</summary>
    private static void Put(byte[] page, int at, byte colour)
    {
        if (OnPage(at))
        {
            page[(ushort)at] = colour;
        }
    }

    /// <summary>
    /// The number of the first pixel of a row for the routines that fill: as <see cref="RowStart"/>, and for the
    /// row just below the screen the pixel the original takes for it.
    /// </summary>
    // BUG: CA-24 the original's table of rows has 200 entries and no check; row 200 reads the word behind the table, 40960, which is row 128.
    private static int FillRowStart(int row) => row == Height ? RowBelowTheScreen : RowStart(row);

    private const int RowBelowTheScreen = 0xa000;

    /// <summary>
    /// The number of the first pixel of the first row copy_rect reads: as <see cref="RowStart"/>, and for the two rows
    /// above the screen the words in front of the driver's table of rows (MGRAPHIC DS:165c and DS:165e, both 0 in the
    /// driver's file, never written), so the copy starts at the top of the page. Met by the raid's tile 63, whose
    /// picture's row the original reads behind its table of tiles: -2 (TAC/level3-arrest).
    /// </summary>
    // C: the table of rows at MGRAPHIC 1660h is indexed without a check; the copy then steps a row at a time from there
    private static int SourceRowStart(int row) => row is -1 or -2 ? 0 : RowStart(row);

    // ---- the mode and the palette ----

    /// <inheritdoc/>
    /// <remarks>
    /// Sets the BIOS's mode 13h: the screen is cleared unless bit 7 of the argument is set, and the first 128 colours
    /// of the display become the driver's table. (The BIOS also resets the other 128 colours to its own defaults;
    /// the game never draws with them, and they are left alone here.)
    /// </remarks>
    // orig: MGRAPHIC 0000:045c set_video_mode driver; also MGRAPHIC 0000:0171; tests: McgaOracleTests, McgaGraphicsDriverTests
    // DIVERGENCE: D-10 the colours 128 to 255 keep their values instead of taking the BIOS's defaults.
    public void SubCode0208(int argument)
    {
        _defaultColours.CopyTo(Palette, 0);
        const int keepScreen = 0x80;
        if ((argument & keepScreen) == 0)
        {
            Array.Clear(Pages[0]);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each of the 16 entries names one of the EGA's 64 colours, whose red, green and blue the driver takes from its
    /// colour table. The 17th entry is the border colour, again as an EGA colour: the border becomes the last palette
    /// index that shows this colour, and stays as it was when none does.
    /// </remarks>
    // orig: MGRAPHIC 0000:0118 set_palette driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void SetPalette(ReadOnlySpan<byte> palette)
    {
        const int entries = 16;
        var borderColour = palette[entries];
        if (palette[..entries].LastIndexOf(borderColour) is >= 0 and var index)
        {
            Border = (byte)index;
        }
        for (var entry = 0; entry < entries; entry++)
        {
            _defaultColours.AsSpan(3 * palette[entry], 3).CopyTo(Palette.AsSpan(3 * entry));
        }
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:00ec set_palette_entry driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void SetPaletteEntry(int index, int colour) =>
        _defaultColours.AsSpan(3 * (ushort)colour, 3).CopyTo(Palette.AsSpan(3 * (ushort)index));

    /// <inheritdoc/>
    /// <remarks>The MCGA driver has 256 colours and no use for the tables: the routine is a single return.</remarks>
    // orig: MGRAPHIC 0000:0170 set_color_maps driver; tests: McgaOracleTests
    public void SetColorMaps(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The 256-colour mode has a single display page: nothing to do.</remarks>
    public void SelectDisplayPage(int page)
    {
    }

    // ---- pages ----

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:05aa show_page driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void ShowPage(int page)
    {
        if ((ushort)page != 0)
        {
            Page(page).CopyTo(Pages[0], 0);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Rows are copied one by one, front to back; a width or height of 0 copies nothing.</remarks>
    // orig: MGRAPHIC 0000:06c0 copy_rect driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void CopyRect(RasterPort source, int x, int y, int width, int height, RasterPort target, int targetX, int targetY)
    {
        var from = Page(source.PageNr);
        var to = Page(target.PageNr);
        var fromAt = SourceRowStart((short)(source.YBgn + y)) + (short)(source.XBgn + x);
        var toAt = RowStart((short)(target.YBgn + targetY)) + (short)(target.XBgn + targetX);
        ushort columns = (ushort)width, rows = (ushort)height;
        if (columns == 0 || rows == 0)
        {
            return;
        }
        do
        {
            for (var column = 0; column < columns; column++)
            {
                // a pixel that would land below the picture is not copied (nor read: its source may lie there too)
                if (OnPage(toAt + column))
                {
                    // DIVERGENCE: D-17 a pixel taken from below the source page's last row is black; the original
                    // takes whatever memory lies behind the page there.
                    var taken = (ushort)(fromAt + column);
                    to[(ushort)(toAt + column)] = taken < from.Length ? from[taken] : Black;
                }
            }
            fromAt += Width;
            toAt += Width;
        }
        while (--rows != 0);
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0098 recolor_rect driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void RecolorRect(RasterPort port, int x, int y, int width, int height, int from, int to)
    {
        var page = Page(port.PageNr);
        var at = RowStart((short)(y + port.YBgn)) + (short)(x + port.XBgn);
        ushort columns = (ushort)width, rows = (ushort)height;
        // C: both loops count down to 0 after their first pass, so a width or height of 0 means 65536 (which leaves the page)
        do
        {
            var column = columns;
            var pixel = at;
            do
            {
                if (OnPage(pixel) && page[(ushort)pixel] == (byte)from)
                {
                    page[(ushort)pixel] = (byte)to;
                }
                pixel++;
            }
            while (--column != 0);
            at += Width;
        }
        while (--rows != 0);
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:01a8 get_pixel driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public int GetPixel(int page, int x, int y) => Page(page)[(ushort)(RowStart((short)y) + x)];

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:01f7 put_pic_line driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void PutPicLine(ReadOnlySpan<byte> line, int page, int y, int x, int count)
    {
        var target = Page(page);
        var at = RowStart((short)y) + (short)x;
        // C: the driver copies words: (count + 1) / 2 of them
        var pixels = ((ushort)count + 1) / 2 * 2;
        for (var i = 0; i < pixels; i++)
        {
            Put(target, at + i, line[i]);
        }
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:06a2 fill_page driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void FillPage(int page, int color) => Array.Fill(Page(page), (byte)color);

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:03da page_flip_active driver; tests: McgaOracleTests
    public int PageFlipActive(int what) => (short)(Traits[(ushort)what] | Traits[(ushort)what + 1] << 8);

    /// <summary>
    /// The driver's words about itself, which <see cref="PageFlipActive"/> answers from: ten bytes in front of the
    /// original driver's table of rows (from the player's MGRAPHIC.EXE; all 0 unless set).
    /// </summary>
    public byte[] Traits { get; init; } = new byte[10];

    /// <inheritdoc/>
    /// <remarks>
    /// The original asks DOS for 64000 bytes and returns their place in memory. Here the answer is a number that
    /// stands for the new buffer; memory does not run out.
    /// </remarks>
    // orig: MGRAPHIC 0000:0424 alloc_page_buffer driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public int AllocPageBuffer(int wanted)
    {
        if ((ushort)wanted == 0)
        {
            return ScreenBuffer;
        }
        _buffers.Add(new byte[PageSize]);
        return _buffers.Count;
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0414 set_page_buffer driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void SetPageBuffer(int page, int buffer) => Pages[(ushort)page] = _buffers[(ushort)buffer - 1];

    /// <inheritdoc/>
    /// <remarks>Nothing to do: a buffer nobody points at any more is the garbage collector's.</remarks>
    // orig: MGRAPHIC 0000:0443 free_page_buffer driver; tests: McgaOracleTests
    public void FreePageBuffer(int page)
    {
    }

    /// <summary>
    /// Copies, for each row from <paramref name="firstRow"/> to <paramref name="lastRow"/>, the pixels from the
    /// row's left edge to its right edge (both inclusive) from one page to the same place on another: how the
    /// library puts back what a moving image covered. A row whose right edge is left of its left edge is skipped;
    /// a negative first row copies nothing at all.
    /// </summary>
    /// <param name="sourcePage">The page to copy from.</param>
    /// <param name="targetPage">The page to copy to.</param>
    /// <param name="firstRow">The first row.</param>
    /// <param name="lastRow">The last row.</param>
    /// <param name="leftEdges">For each row, the column of its first pixel.</param>
    /// <param name="rightEdges">For each row, the column of its last pixel.</param>
    // orig: MGRAPHIC 0000:0730 copy_spans driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void CopySpans(int sourcePage, int targetPage, int firstRow, int lastRow, ReadOnlySpan<ushort> leftEdges, ReadOnlySpan<ushort> rightEdges)
    {
        if ((short)firstRow < 0)
        {
            return;
        }
        byte[] source = Page(sourcePage), target = Page(targetPage);
        var row = (ushort)firstRow;
        do
        {
            ushort left = leftEdges[row], right = rightEdges[row];
            if (right >= left)
            {
                var at = FillRowStart(row) + left;
                for (var i = 0; i <= right - left; i++)
                {
                    if (OnPage(at + i))
                    {
                        target[(ushort)(at + i)] = source[(ushort)(at + i)];
                    }
                }
            }
            row++;
        }
        while (row <= (ushort)lastRow);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The order is that of a shift register that visits every number from 1 to 65535 once; the numbers up to 64000
    /// are the pixels. The original times itself against the display so that about 533 pixels change per frame;
    /// here <see cref="FrameShown"/> is called after every 533.
    /// </remarks>
    // orig: MGRAPHIC 0000:05db dissolve_page driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void SubGame014b(int page)
    {
        if ((ushort)page == 0)
        {
            return;
        }
        const int pixelsPerFrame = 0x215;
        const ushort feedback = 0xb400;
        byte[] source = Page(page), screen = Pages[0];
        ushort position = 1;
        for (var done = 1; done <= PageSize; done++)
        {
            do
            {
                var carry = (position & 1) != 0;
                position >>= 1;
                if (carry)
                {
                    position ^= feedback;
                }
            }
            while (position > PageSize);
            screen[position - 1] = source[position - 1];
            if (done % pixelsPerFrame == 0)
            {
                FrameShown?.Invoke();
            }
        }
    }

    /// <summary>Called when a routine that takes its time has changed the screen enough for one frame of the display (the host link shows the screen and waits a frame).</summary>
    public Action? FrameShown { get; set; }

    // ---- images ----

    /// <summary>The most bytes one image takes in the original's memory: 65520. A row that would pass it is dropped.</summary>
    private const int LargestImage = 0xfff0;

    private const int ImageHeaderBytes = 4;
    private const int RowHeaderBytes = 4;

    /// <summary>The image being built: its size as asked for, its rows so far, and what it takes in the original's memory.</summary>
    private (int Width, int Height, List<ImageRow> Rows, int Bytes)? _building;

    /// <summary>Set when an image outgrew <see cref="LargestImage"/>: the original then starts no further image until images are begun anew.</summary>
    private bool _imageMemoryFull;

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0902 begin_image_cuts driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    // DIVERGENCE: D-11 memory for images never runs out; only the limit of one image is kept.
    public bool BeginImageCuts()
    {
        _imageMemoryFull = false;
        _building = null;
        return true;
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:092e end_image_cuts driver; tests: McgaOracleTests
    public void EndImageCuts()
    {
    }

    /// <inheritdoc/>
    /// <remarks>Always the largest answer: memory is not short here.</remarks>
    // orig: MGRAPHIC 0000:040a image_memory_free driver; tests: McgaOracleTests
    // DIVERGENCE: D-11 the free memory reported is the largest number, not DOS's.
    public int ImageMemoryFree() => short.MaxValue;

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0951 begin_image_build driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void BeginImageBuild(int width, int height) =>
        _building = _imageMemoryFull ? null : ((ushort)width, (ushort)height, [], ImageHeaderBytes);

    /// <inheritdoc/>
    /// <remarks>
    /// The see-through pixels at the row's start are counted, those at its end dropped, those in between kept. A
    /// row that would make the image larger than one image can be is left out: the image is then a row lower than
    /// asked for, and no further image is started until <see cref="BeginImageCuts"/>.
    /// </remarks>
    // orig: MGRAPHIC 0000:09b9 add_image_row driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void AddImageRow(ReadOnlySpan<byte> pixels)
    {
        if (_building is not { } image)
        {
            return;
        }
        if (image.Width + RowHeaderBytes + image.Bytes > LargestImage)
        {
            _building = image with { Height = image.Height - 1 };
            _imageMemoryFull = true;
            return;
        }
        var row = pixels[..image.Width];
        var skip = row.IndexOfAnyExcept((byte)0);
        var stored = skip < 0 ? [] : row[skip..(row.LastIndexOfAnyExcept((byte)0) + 1)].ToArray();
        image.Rows.Add(new ImageRow(skip < 0 ? image.Width : skip, stored));
        _building = image with { Bytes = image.Bytes + RowHeaderBytes + stored.Length };
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:099c finish_image_build driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public Image? FinishImageBuild()
    {
        var finished = _building is { } image ? new Image(image.Width, image.Height, image.Rows) : null;
        _building = null;
        return finished;
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0a5b cut_image driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public Image? CutImage(int page, int x, int y, int width, int height)
    {
        BeginImageBuild(width, height);
        if (_building is { Width: > 0, Height: > 0 } image)
        {
            // C: the page number's sign is dropped (the original takes its absolute value)
            var source = Page(Math.Abs((short)page));
            var at = RowStart((short)y) + (short)x;
            for (var row = 0; row < image.Height; row++, at += Width)
            {
                AddImageRow(source.AsSpan(at, image.Width));
            }
        }
        return FinishImageBuild();
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0c1f draw_image_port driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void DrawImagePort(RasterPort port, int x, int y, Image? image)
    {
        if (image is { Width: > 0, Height: > 0 })
        {
            DrawImage(port, (short)x, (short)y, image, top: 0, left: 0, bottom: 0, right: 0);
        }
    }

    /// <inheritdoc/>
    // orig: MGRAPHIC 0000:0b15 draw_image_untracked driver; also MGRAPHIC 0000:0b4f; tests: McgaOracleTests, McgaGraphicsDriverTests
    public ImageArea DrawImageUntracked(RasterPort port, int x, int y, Image? image)
    {
        if (image is not { Width: > 0, Height: > 0 })
        {
            return default;
        }
        // how much of the image lies outside the window on each side; an image wholly outside is not drawn
        short left = (short)x, top = (short)y;
        int clippedTop = 0, clippedLeft = 0, clippedBottom = 0, clippedRight = 0;
        if (top < 0)
        {
            clippedTop = -top;
            if (clippedTop >= image.Height)
            {
                return default;
            }
        }
        if (port.Length1 < 0 || top > port.Length1)
        {
            return default;
        }
        clippedBottom = Math.Max(0, top + image.Height - 1 - port.Length1);
        if (left < 0)
        {
            clippedLeft = -left;
            if (clippedLeft >= image.Width)
            {
                return default;
            }
        }
        if (port.Width1 < 0 || left > port.Width1)
        {
            return default;
        }
        clippedRight = Math.Max(0, left + image.Width - 1 - port.Width1);
        DrawImage(port, left, top, image, clippedTop, clippedLeft, clippedBottom, clippedRight);
        return new ImageArea(left + clippedLeft, top + clippedTop, image.Width - clippedLeft - clippedRight, image.Height - clippedTop - clippedBottom);
    }

    /// <summary>The parts of the pages that tracked images were drawn on (MicroProse's library in front of the driver keeps them).</summary>
    public SpanTracker Tracked { get; } = new();

    /// <inheritdoc/>
    public void DrawImageTracked(RasterPort port, int x, int y, Image? image) =>
        Tracked.Track(port, DrawImageUntracked(port, x, y, image));

    /// <inheritdoc/>
    /// <remarks>Checked against asm (FINAL 0000:0d5e to 0d8a): the draw, then the area noted once for the port's page and once for the page of the other kind.</remarks>
    public void DrawImageToBackground(RasterPort port, int x, int y, Image? image)
    {
        var area = DrawImageUntracked(port, x, y, image);
        Tracked.Track(port, area);
        Tracked.TrackOtherKind(port, area);
    }

    /// <inheritdoc/>
    public void RestoreTrackedSpans(int sourcePage, int targetPage)
    {
        CopySpans(sourcePage, targetPage, Tracked.FirstRow(targetPage), Tracked.LastRow(targetPage), Tracked.Left(targetPage), Tracked.Right(targetPage));
        Tracked.Clear(targetPage);
    }

    /// <inheritdoc/>
    public void ClearSpanTracking()
    {
        Tracked.Clear(0);
        Tracked.Clear(1);
    }

    /// <summary>Draws an image without the given numbers of rows and columns at its four sides; a pixel of colour 0 leaves the page as it is.</summary>
    // orig: MGRAPHIC 0000:0c42 draw_image_clipped driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    private void DrawImage(RasterPort port, int x, int y, Image image, int top, int left, int bottom, int right)
    {
        var page = Page(port.PageNr);
        var lastColumn = image.Width - right;
        var rows = image.Height - top - bottom;
        if (lastColumn <= 0 || rows <= 0)
        {
            return;
        }
        var rowAt = RowStart((short)(y + port.YBgn + top)) + (short)(x + port.XBgn);
        for (var r = top; r < top + rows; r++, rowAt += Width)
        {
            var row = image.Rows[r];
            // the row's stored pixels stand in columns Skip to Skip + their number; draw those from the left clip to the last column
            var column = Math.Max(row.Skip, left);
            var end = Math.Min(row.Skip + row.Pixels.Length, lastColumn);
            for (; column < end; column++)
            {
                var pixel = row.Pixels[column - row.Skip];
                if (pixel != 0)
                {
                    Put(page, rowAt + column, pixel);
                }
            }
        }
    }

    // ---- fills ----

    /// <inheritdoc/>
    /// <remarks>
    /// This is the routine of MicroProse's library in the game programs (CODE 0000:0576), not the driver: it notes the
    /// rectangle's left and right edge for each of its rows in two tables and has the driver fill the rows between
    /// them. The rectangle is not clipped.
    /// </remarks>
    // BUG: CA-19 the original writes its tables without a limit: more than 220 rows, or a row above the screen, overwrite
    // its own variables. Such a rectangle is an error here, like every index that leaves its table (ARCHITECTURE.md).
    public void FillRect(RasterPort port, int x, int y, int width, int height, int color)
    {
        SelectPage(port.PageNr);
        var firstRow = (short)(y + port.YBgn);
        var rows = (short)height;
        if (rows < 0 || firstRow < 0 || firstRow >= EdgeTableRows || firstRow + rows > EdgeTableRows)
        {
            throw new ArgumentOutOfRangeException(nameof(height), $"a fill of {rows} rows from row {firstRow} overruns the original's edge tables");
        }
        ClearEdgeTables();
        var left = (ushort)(x + port.XBgn);
        for (var row = firstRow; row < firstRow + rows; row++)
        {
            _leftEdges[row] = left;
            _rightEdges[row] = (ushort)(left + width - 1);
        }
        SetColor(color);
        FillSpans(firstRow, firstRow + rows - 1, _leftEdges, _rightEdges);
        EndDrawing();
    }

    /// <summary>Marks every row of the edge tables as empty: a left edge to the right of its right edge.</summary>
    // orig: CODE 0000:05fe sub_code_0000_05fe thunk; also FINAL 0000:068a, GAME 0000:05fe, TAC 0000:068a, BUG 0000:068a, CHASE 0000:05fe; tests: McgaGraphicsDriverTests
    private void ClearEdgeTables()
    {
        Array.Fill(_leftEdges, ushort.MaxValue);
        Array.Clear(_rightEdges);
    }

    /// <summary>Chooses the page that fills and lines are drawn on.</summary>
    /// <param name="page">The page number.</param>
    // orig: MGRAPHIC 0000:03fc select_page driver; tests: McgaOracleTests
    public void SelectPage(int page) => DrawPage = Page(page);

    /// <summary>Sets the colour of fills and lines, and how lines are drawn.</summary>
    /// <param name="color">
    /// The palette index in the low byte. The two lowest bits of the high byte choose how a line's pixels combine
    /// with the picture (<see cref="PixelOperation"/>); the game's library passes the colour it was given, so a
    /// colour below 256 simply draws.
    /// </param>
    // orig: MGRAPHIC 0000:018f set_color driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void SetColor(int color)
    {
        Color = (byte)color;
        LineOperation = (PixelOperation)(color >> 8 & 3);
    }

    /// <summary>Ends a fill or a line. The MCGA driver has nothing to do here (other drivers restore their hardware registers).</summary>
    // orig: MGRAPHIC 0000:01a6 end_drawing driver; tests: McgaOracleTests
    public void EndDrawing()
    {
    }

    /// <summary>
    /// Fills, for each row from <paramref name="firstRow"/> to <paramref name="lastRow"/>, the pixels from the row's
    /// left edge to its right edge (both inclusive) with the current colour on the current page.
    /// </summary>
    /// <remarks>
    /// A row whose right edge is left of its left edge is skipped. So is a span of a single pixel in column 0, or in
    /// the right-most column of the port a line was last begun for: the original takes those for the empty marks of
    /// the tables. A negative first row draws nothing at all. At least one row is always looked at, even when the
    /// last row is before the first.
    /// </remarks>
    /// <param name="firstRow">The first row.</param>
    /// <param name="lastRow">The last row.</param>
    /// <param name="leftEdges">For each row, the column of its first pixel.</param>
    /// <param name="rightEdges">For each row, the column of its last pixel.</param>
    // orig: MGRAPHIC 0000:0386 fill_spans driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    // BUG: CA-23 a fill one pixel wide in column 0, or in the last column of the port a line was last drawn in, draws nothing.
    public void FillSpans(int firstRow, int lastRow, ReadOnlySpan<ushort> leftEdges, ReadOnlySpan<ushort> rightEdges)
    {
        if ((short)firstRow < 0)
        {
            return;
        }
        var row = (ushort)firstRow;
        do
        {
            var left = leftEdges[row];
            var right = rightEdges[row];
            var isSinglePixel = right == left;
            var skipped = right < left || (isSinglePixel && (left == 0 || left == LineRight));
            if (!skipped)
            {
                var at = FillRowStart(row) + left;
                for (var count = right - left + 1; count > 0; count--)
                {
                    Put(DrawPage, at++, Color);
                }
            }
            row++;
        }
        while (row <= (ushort)lastRow);
    }

    // ---- lines ----

    /// <inheritdoc/>
    /// <remarks>
    /// This is the routine of MicroProse's library in the game programs (CODE 0000:097a): it tells the driver the
    /// port, clips the line to the port's window (<see cref="LineClipper"/>) and has the driver draw what is left.
    /// </remarks>
    public void DrawLinePort(RasterPort port, int x1, int y1, int x2, int y2, int color)
    {
        BeginLine(port);
        SetColor(color);
        if (LineClipper.Clip((short)x1, (short)y1, (short)x2, (short)y2, port.Width1, port.Length1) is { } visible)
        {
            Line(visible.X1, visible.Y1, visible.X2, visible.Y2);
        }
        EndDrawing();
    }

    /// <summary>Prepares for lines on a port: its page becomes the drawing page, its window's corner the origin of the lines.</summary>
    /// <param name="port">The port.</param>
    // orig: MGRAPHIC 0000:03e5 begin_line driver; tests: McgaOracleTests
    public void BeginLine(RasterPort port)
    {
        LineOriginX = (ushort)port.XBgn;
        LineOriginY = (ushort)port.YBgn;
        LineRight = (ushort)port.Width1;
        SelectPage(port.PageNr);
    }

    /// <summary>
    /// Draws a line between two points of the current port (already clipped) in the current colour: both end points
    /// and one pixel for every step along the longer axis.
    /// </summary>
    /// <param name="x1">One end, column.</param>
    /// <param name="y1">One end, row.</param>
    /// <param name="x2">The other end, column.</param>
    /// <param name="y2">The other end, row.</param>
    // orig: MGRAPHIC 0000:0507 draw_line driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public void Line(int x1, int y1, int x2, int y2)
    {
        ushort startX = (ushort)(x1 + LineOriginX), startY = (ushort)(y1 + LineOriginY);
        ushort endX = (ushort)(x2 + LineOriginX), endY = (ushort)(y2 + LineOriginY);
        // the line is drawn from its left end
        if (startX > endX)
        {
            (startX, endX) = (endX, startX);
            (startY, endY) = (endY, startY);
        }
        var at = RowStart(startY) + startX;

        // steps along the two axes, in pixel numbers: one pixel to the right, one row down or up
        int across = (ushort)(endX - startX), along = (short)(endY - startY);
        int stepAcross = 1, stepAlong = Width;
        if (along < 0)
        {
            stepAlong = -Width;
            along = -along;
        }
        // the longer distance is walked one pixel at a time; the other axis advances when its share has built up
        var (major, minor, stepMajor, stepMinor) = across >= along
            ? (across, along, stepAcross, stepAlong)
            : (along, across, stepAlong, stepAcross);
        var error = -((major + 1) >> 1);
        for (var left = major; ; left--)
        {
            Plot(at);
            if (left == 0)
            {
                break;
            }
            at += stepMajor;
            error += minor;
            if (error >= 0)
            {
                error -= major;
                at += stepMinor;
            }
        }
    }

    /// <summary>Puts one pixel of a line on the drawing page, combined with what is there as <see cref="LineOperation"/> says.</summary>
    private void Plot(int at) =>
        Put(DrawPage, at, LineOperation switch
        {
            PixelOperation.And => (byte)(DrawPage[at] & Color),
            PixelOperation.Or => (byte)(DrawPage[at] | Color),
            PixelOperation.Xor => (byte)(DrawPage[at] ^ Color),
            _ => Color,
        });

    // ---- text ----

    /// <inheritdoc/>
    /// <remarks>0 for a font that does not exist and for a character the font does not have.</remarks>
    // orig: MGRAPHIC 0000:1000 char_width driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    public int CharWidth(int font, char character)
    {
        var code = (byte)character;
        if (code >= 0x80 || _fonts.Font(font) is not { } face || code < face.FirstCharacter || code > face.LastCharacter)
        {
            return 0;
        }
        return (sbyte)face.Advance(code);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// With the port's draw mode 1 every character is drawn as a full cell: its pixels in the foreground pen, the
    /// rest of the cell (its padding column and padding row included) in the background pen. With any other draw mode
    /// only the character's own pixels are drawn and what is behind them stays.
    /// </para>
    /// <para>
    /// A character code of 128 or more is not drawn: it changes the colour of the characters after it to the code
    /// less 128, and the port's foreground pen is left at the last colour used. The text is clipped at the top and
    /// bottom of the port's window, not at its sides. Nothing is drawn for an empty text or a font that does not exist.
    /// </para>
    /// <para>
    /// The result is the column after the last character. When nothing was drawn the original's result is the
    /// column it was given less the window's left edge, which is reproduced.
    /// </para>
    /// </remarks>
    // orig: MGRAPHIC 0000:107a draw_string driver; also MGRAPHIC 0000:1094, MGRAPHIC 0000:11ab, MGRAPHIC 0000:0e86; tests: McgaOracleTests, McgaGraphicsDriverTests
    public int DrawString(RasterPort port, int x, int y, string text)
    {
        var nothingDrawn = (short)(x - port.XBgn);
        if (text.Length == 0 || _fonts.Font(port.FontNr) is not { } face)
        {
            return nothingDrawn;
        }
        if (face.FixedWidth != 0)
        {
            // DIVERGENCE: D-6 a font of fixed width, which the original also clips at the window's sides, is refused.
            throw new NotSupportedException("fonts of fixed width are not reconstructed: the game's FONTS.CV has none");
        }
        // only the low byte of the draw mode counts
        var opaque = (byte)port.DrawMode == 1;
        var cellRows = (sbyte)((port.DrawMode & 1) != 0 ? face.Rows + face.PaddingRows : face.Rows);
        if (ClipRows((short)y, cellRows, port.Length1) is not { } rows)
        {
            return nothingDrawn;
        }
        var page = Page(port.PageNr);
        var foreground = (byte)port.APen;
        var background = (byte)port.BPen;
        var at = RowStart((short)(rows.Top + port.YBgn)) + (short)(x + port.XBgn);
        var column = x;
        for (var i = 0; i < text.Length; i++)
        {
            var code = (byte)text[i];
            if (code >= 0x80)
            {
                foreground = (byte)(code & 0x7f);
                continue;
            }
            var advance = face.Advance(code);
            // without a background the last character's cell is walked in full, every other one without its padding
            // column; it makes no difference to the picture, since the padding has no pixels
            var isLast = i == text.Length - 1;
            var columns = opaque || isLast ? advance : (byte)(advance - face.PaddingColumns);
            DrawGlyph(page, at, face, code, rows.First, rows.Count, columns, foreground, opaque ? background : null);
            at += advance;
            column += advance;
        }
        // the pen keeps the last colour a colour code chose (its low byte; the high byte of the pen stays)
        port.APen = (short)(port.APen & 0xff00 | foreground);
        return (short)column;
    }

    /// <summary>The rows of a text cell that lie inside a window of the given height, or null when none does.</summary>
    /// <returns>The row of the screen the text starts at, the first row of the cell to draw, and how many.</returns>
    // orig: MGRAPHIC 0000:113f clip_text_rows driver; tests: McgaOracleTests, McgaGraphicsDriverTests
    private static (int Top, int First, int Count)? ClipRows(short y, int cellRows, short lastWindowRow)
    {
        if (lastWindowRow < 0)
        {
            return null;
        }
        int top = y, first = 0, count = cellRows;
        if (top < 0)
        {
            // the rows above the window are left out
            first = -top;
            count -= first;
            top = 0;
            if (count <= 0)
            {
                return null;
            }
        }
        if (top > lastWindowRow)
        {
            return null;
        }
        var below = top + count - 1 - lastWindowRow;
        return (top, first, below > 0 ? count - below : count);
    }

    /// <summary>
    /// Draws rows of one character at a position of a page: for each of the columns, the foreground colour where the
    /// character has a pixel, and elsewhere the background colour, or nothing when there is none.
    /// </summary>
    // C: the loops run at least once and count in single bytes, so 0 columns or rows mean 256, as in the original.
    private static void DrawGlyph(byte[] page, int at, FontSet.Face face, byte code, int firstRow, int rowCount, byte columns, byte foreground, byte? background)
    {
        var rowsLeft = (byte)rowCount;
        var row = firstRow;
        do
        {
            var bits = face.RowBits(code, row);
            var pixel = at;
            var columnsLeft = columns;
            do
            {
                if ((bits & 0x8000) != 0)
                {
                    Put(page, pixel, foreground);
                }
                else if (background is { } colour)
                {
                    Put(page, pixel, colour);
                }
                bits <<= 1;
                pixel++;
            }
            while (--columnsLeft != 0);
            at += Width;
            row++;
        }
        while (--rowsLeft != 0);
    }
}
