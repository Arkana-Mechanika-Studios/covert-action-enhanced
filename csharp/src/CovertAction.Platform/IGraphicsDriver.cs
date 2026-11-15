using CovertAction.Core.Graphics;

namespace CovertAction.Platform;

/// <summary>
/// The graphics library as the game's code sees it: the calls at the start of each of the original's programs that
/// lead into the graphics driver the launcher loaded (EGA, CGA, MCGA or Tandy). Every drawing the game does goes
/// through these calls. The rewrite's driver draws into a 320x200 picture of palette indices; a test's driver
/// records the calls so they can be compared with the original's.
/// </summary>
/// <remarks>
/// One method per routine, named after it in symbols/names.txt, with its arguments in the original order.
/// Coordinates and sizes are pixels of the 320x200 screen, relative to the port's window; colours are palette indices
/// 0..15. Numbers are the original's 16-bit words: an implementation takes each modulo 65536. The members here are
/// the routines the cryptography part calls; the picture, image and page routines are added with the first part
/// that calls them.
/// </remarks>
public interface IGraphicsDriver
{
    /// <summary>Fills a rectangle with one colour.</summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="color">The palette index to fill with.</param>
    // orig: CODE 0000:0576 fill_rect thunk; also FINAL 0000:0602, TAC 0000:0602, GAME 0000:0576, CHASE 0000:0576, BUG 0000:0602
    void FillRect(RasterPort port, int x, int y, int width, int height, int color);

    /// <summary>Copies a rectangle from one surface to another (the screen to its backup copy and back, around menus).</summary>
    /// <param name="source">The surface to copy from.</param>
    /// <param name="x">Left edge of the rectangle on the source.</param>
    /// <param name="y">Top edge of the rectangle on the source.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="target">The surface to copy to.</param>
    /// <param name="targetX">Left edge on the target.</param>
    /// <param name="targetY">Top edge on the target.</param>
    // orig: CODE 0000:0191 copy_rect thunk; also FINAL 0000:021d, TAC 0000:021d, BUG 0000:021d, GAME 0000:0191, CHASE 0000:0191
    void CopyRect(RasterPort source, int x, int y, int width, int height, RasterPort target, int targetX, int targetY);

    /// <summary>Replaces one colour by another inside a rectangle (how the menu's highlight bar moves).</summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="from">The palette index to replace.</param>
    /// <param name="to">The palette index to put in its place.</param>
    // orig: CODE 0000:0121 recolor_rect thunk; also FINAL 0000:01ad, TAC 0000:01ad, BUG 0000:01ad, GAME 0000:0121, CHASE 0000:0121
    void RecolorRect(RasterPort port, int x, int y, int width, int height, int from, int to);

    /// <summary>Draws a text in the port's font and foreground pen, its top left corner at (x, y).</summary>
    /// <param name="port">The surface, window, pens, draw mode and font.</param>
    /// <param name="x">Left edge of the first character.</param>
    /// <param name="y">Top edge of the text line.</param>
    /// <param name="text">The text; each character is one byte of the game's character set.</param>
    /// <returns>The column after the last character, relative to the port's window like <paramref name="x"/>.</returns>
    // orig: CODE 0000:0198 draw_string thunk; also FINAL 0000:0224, TAC 0000:0224, GAME 0000:0198, CHASE 0000:0198, BUG 0000:0224
    int DrawString(RasterPort port, int x, int y, string text);

    /// <summary>Draws a straight line, clipped to the port's window.</summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x1">Start, x.</param>
    /// <param name="y1">Start, y.</param>
    /// <param name="x2">End, x.</param>
    /// <param name="y2">End, y.</param>
    /// <param name="color">The palette index of the line.</param>
    // orig: CODE 0000:097a draw_line_port thunk; also FINAL 0000:0a06, TAC 0000:0a06, BUG 0000:0a06, GAME 0000:097a, CHASE 0000:097a
    void DrawLinePort(RasterPort port, int x1, int y1, int x2, int y2, int color);

    /// <summary>The width in pixels a character takes in a font (the game sums these to size its menu boxes).</summary>
    /// <param name="font">The font number, 1..3.</param>
    /// <param name="character">The character.</param>
    // orig: CODE 0000:0128 char_width thunk; also FINAL 0000:01b4, TAC 0000:01b4, BUG 0000:01b4, GAME 0000:0128, CHASE 0000:0128
    int CharWidth(int font, char character);

    /// <summary>
    /// Gives the driver the two 16-entry colour tables it maps the game's colours through on displays with fewer
    /// colours (each part passes its own; the MCGA driver ignores them).
    /// </summary>
    /// <param name="first">The first table: 16 bytes.</param>
    /// <param name="second">The second table: 16 bytes.</param>
    // orig: CODE 0000:01c9 set_color_maps thunk; also TAC 0000:0255, BUG 0000:0255, GAME 0000:01c9, CHASE 0000:01c9, FINAL 0000:0255
    void SetColorMaps(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second);

    /// <summary>Sets the display's palette: which of the 64 EGA colours each of the 16 palette indices shows.</summary>
    /// <param name="palette">17 bytes: one colour per palette index, and the border colour.</param>
    // orig: CODE 0000:01c2 set_palette thunk; also BUG 0000:024e, GAME 0000:01c2, CHASE 0000:01c2, FINAL 0000:024e, TAC 0000:024e
    void SetPalette(ReadOnlySpan<byte> palette);

    /// <summary>Gives one palette index one of the driver's colours.</summary>
    /// <param name="index">The palette index.</param>
    /// <param name="colour">The colour's number in the driver's table (the EGA's 64 colours come first).</param>
    // orig: BUG 0000:0244 set_palette_entry thunk
    void SetPaletteEntry(int index, int colour);

    /// <summary>Shows a page: copies the off-screen surface with that number onto the screen. Page 0 is the screen itself.</summary>
    /// <param name="page">The page number.</param>
    // orig: CODE 0000:01dc show_page thunk; also FINAL 0000:0268, BUG 0000:0268, GAME 0000:01dc, CHASE 0000:01dc, TAC 0000:0268
    void ShowPage(int page);

    /// <summary>
    /// Switches the display to the driver's graphics mode and loads the driver's own colours; each part calls it once
    /// when it starts. The routine has no name in the project's symbols yet, so the member carries its label; in the
    /// MCGA driver it sets mode 13h (320x200, 256 colours), which also clears the screen.
    /// </summary>
    /// <param name="argument">0 in every call found; with bit 7 set the screen would be kept.</param>
    // orig: CODE 0000:0208 sub_code_0000_0208 thunk; also FINAL 0000:0294
    void SubCode0208(int argument);

    /// <summary>
    /// Asks the display itself, past the driver, to show another of its pages (a service of the PC's video BIOS for
    /// displays that keep several). The 256-colour mode has one page, so the call changes nothing there; no part
    /// ported so far makes it.
    /// </summary>
    /// <param name="page">The page number, 0..255.</param>
    void SelectDisplayPage(int page);

    // ---- pages and pictures ----

    /// <summary>The colour of one pixel of a page.</summary>
    /// <param name="page">The page number.</param>
    /// <param name="x">The pixel's column.</param>
    /// <param name="y">The pixel's row.</param>
    /// <returns>The palette index.</returns>
    // orig: GAME 0000:0160 get_pixel thunk; also BUG 0000:01ec, FINAL 0000:01ec
    int GetPixel(int page, int x, int y);

    /// <summary>
    /// Puts a row of pixels on a page: how a picture gets there, one decoded row after the other.
    /// </summary>
    /// <param name="line">The pixels, one palette index each. The driver copies two at a time: an odd count takes one pixel more.</param>
    /// <param name="page">The page number.</param>
    /// <param name="y">The row.</param>
    /// <param name="x">The column of the first pixel.</param>
    /// <param name="count">How many pixels.</param>
    // orig: FINAL 0000:0216 put_pic_line thunk; also GAME 0000:018a, TAC 0000:0216, BUG 0000:0216, CHASE 0000:018a
    void PutPicLine(ReadOnlySpan<byte> line, int page, int y, int x, int count);

    /// <summary>Fills a whole page with one colour.</summary>
    /// <param name="page">The page number.</param>
    /// <param name="color">The palette index; its low byte counts.</param>
    // orig: FINAL 0000:01bb fill_page thunk; also GAME 0000:012f
    void FillPage(int page, int color);

    /// <summary>
    /// One of the driver's words about itself. The game asks for the first (0): whether the display can switch
    /// between pages on its own, so that a finished page need not be copied to the screen. The 256-colour driver
    /// answers 0.
    /// </summary>
    /// <param name="what">Which word, counted in bytes from the first.</param>
    // orig: FINAL 0000:0208 page_flip_active thunk; also GAME 0000:017c
    int PageFlipActive(int what);

    /// <summary>
    /// Asks for the memory of one more page (the launcher gives the driver two; a part that needs more asks here).
    /// </summary>
    /// <param name="wanted">0 to be told the screen's own buffer, anything else for a new one.</param>
    /// <returns>A number that names the buffer for <see cref="SetPageBuffer"/>; 0 when there is no memory left.</returns>
    // orig: CHASE 0000:010c alloc_page_buffer thunk; also TAC 0000:0198
    int AllocPageBuffer(int wanted);

    /// <summary>Makes a buffer the picture of a page.</summary>
    /// <param name="page">The page number.</param>
    /// <param name="buffer">The buffer's number from <see cref="AllocPageBuffer"/>.</param>
    // orig: CHASE 0000:01b4 set_page_buffer thunk; also TAC 0000:0240
    void SetPageBuffer(int page, int buffer);

    /// <summary>Gives the memory of a page back. The page keeps pointing at it until it is given another buffer.</summary>
    /// <param name="page">The page number; 0, the screen, is left alone.</param>
    // orig: CHASE 0000:0144 free_page_buffer thunk; also TAC 0000:01d0
    void FreePageBuffer(int page);

    /// <summary>
    /// Shows a page on the screen pixel by pixel in a scattered order, so that the new picture seeps through the
    /// old one in about two seconds. The routine has no name in the project's symbols yet.
    /// </summary>
    /// <param name="page">The page number; 0, the screen itself, does nothing.</param>
    // orig: GAME 0000:014b sub_game_0000_014b thunk; also FINAL 0000:01d7
    void SubGame014b(int page);

    // ---- images ----

    /// <summary>Gets ready to make images: the original takes all free memory for them here.</summary>
    /// <returns>Whether there is memory for images.</returns>
    // orig: GAME 0000:0183 begin_image_cuts thunk; also BUG 0000:020f, CHASE 0000:0183, FINAL 0000:020f, TAC 0000:020f
    bool BeginImageCuts();

    /// <summary>Ends the making of images: the original gives the memory it did not use back.</summary>
    // orig: GAME 0000:013d end_image_cuts thunk; also BUG 0000:01c9, CHASE 0000:013d, FINAL 0000:01c9, TAC 0000:01c9
    void EndImageCuts();

    /// <summary>How much memory is free, in units of 16 bytes (the game decides by it what it can afford to load).</summary>
    // orig: GAME 0000:0113 image_memory_free thunk; also FINAL 0000:019f
    int ImageMemoryFree();

    /// <summary>Starts an image that is then given its rows with <see cref="AddImageRow"/>.</summary>
    /// <param name="width">The image's width in pixels.</param>
    /// <param name="height">The image's height in rows.</param>
    // orig: FINAL 0000:0286 begin_image_build thunk; also GAME 0000:01fa, BUG 0000:0286, CHASE 0000:01fa, TAC 0000:0286
    void BeginImageBuild(int width, int height);

    /// <summary>Adds the next row to the image being built.</summary>
    /// <param name="pixels">The row's pixels, as many as the image is wide; colour 0 is see-through.</param>
    // orig: FINAL 0000:027f add_image_row thunk; also GAME 0000:01f3, BUG 0000:027f, CHASE 0000:01f3, TAC 0000:027f
    void AddImageRow(ReadOnlySpan<byte> pixels);

    /// <summary>Ends the image being built.</summary>
    /// <returns>The image; null when none could be started.</returns>
    // orig: FINAL 0000:0278 finish_image_build thunk; also GAME 0000:01ec, BUG 0000:0278, CHASE 0000:01ec, TAC 0000:0278
    Image? FinishImageBuild();

    /// <summary>Makes an image of a rectangle of a page; its pixels of colour 0 become see-through.</summary>
    /// <param name="page">The page number.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in rows.</param>
    /// <returns>The image; null when none could be started.</returns>
    // orig: FINAL 0000:01a6 cut_image thunk; also GAME 0000:011a, TAC 0000:01a6, BUG 0000:01a6, CHASE 0000:011a
    Image? CutImage(int page, int x, int y, int width, int height);

    /// <summary>Draws an image at a place of a port, whole: nothing is clipped.</summary>
    /// <param name="port">The surface and the window's corner.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="image">The image; null draws nothing.</param>
    // orig: BUG 0000:026f draw_image_port thunk; also FINAL 0000:026f, TAC 0000:026f, GAME 0000:01e3
    void DrawImagePort(RasterPort port, int x, int y, Image? image);

    /// <summary>Draws the part of an image that lies inside a port's window.</summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x">Left edge of the image.</param>
    /// <param name="y">Top edge of the image.</param>
    /// <param name="image">The image; null draws nothing.</param>
    /// <returns>The part of the window that was drawn on.</returns>
    // orig: FINAL 0000:01c0 draw_image_untracked thunk; also GAME 0000:0134, CHASE 0000:0134
    ImageArea DrawImageUntracked(RasterPort port, int x, int y, Image? image);

    /// <summary>
    /// Draws the part of an image that lies inside a port's window and remembers where it drew, so that
    /// <see cref="RestoreTrackedSpans"/> can put the picture back from a clean copy: how sprites move over a
    /// background.
    /// </summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x">Left edge of the image.</param>
    /// <param name="y">Top edge of the image.</param>
    /// <param name="image">The image; null draws nothing.</param>
    // orig: FINAL 0000:0d40 draw_image_tracked thunk; also GAME 0000:0cb4, FINAL 0000:0d4f, GAME 0000:0cc3
    void DrawImageTracked(RasterPort port, int x, int y, Image? image);

    /// <summary>
    /// Draws the part of an image that lies inside a port's window for good (onto the background an animation plays
    /// on) and remembers where, for the pages of both kinds: the next <see cref="RestoreTrackedSpans"/> of either
    /// page then fetches the changed background.
    /// </summary>
    /// <param name="port">The surface and window.</param>
    /// <param name="x">Left edge of the image.</param>
    /// <param name="y">Top edge of the image.</param>
    /// <param name="image">The image; null draws nothing.</param>
    // orig: FINAL 0000:0d5e draw_image_to_background thunk; also GAME 0000:0cd2, FINAL 0000:0d6d, GAME 0000:0ce1
    void DrawImageToBackground(RasterPort port, int x, int y, Image? image);

    /// <summary>
    /// Copies every part of a page that images were drawn on since the last time from the same place of another
    /// page (the clean background), and forgets those parts.
    /// </summary>
    /// <param name="sourcePage">The page with the clean picture.</param>
    /// <param name="targetPage">The page the images were drawn on.</param>
    // orig: FINAL 0000:0df2 restore_tracked_spans thunk; also GAME 0000:0d66
    void RestoreTrackedSpans(int sourcePage, int targetPage);

    /// <summary>Forgets every part of every page that images were drawn on.</summary>
    // orig: FINAL 0000:0e51 clear_span_tracking thunk; also GAME 0000:0dc5
    void ClearSpanTracking();
}
