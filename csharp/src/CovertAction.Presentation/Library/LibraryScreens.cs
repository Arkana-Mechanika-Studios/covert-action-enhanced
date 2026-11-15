using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The library's screens of the campaign that several parts show: the frame of the report screens with its title
/// bar, the white panels and the film strip, a person's face and framed photo, figures drawn from a table, and the
/// wait for one key. The menus, the campaign, the raid and the electronics part each carry a copy; the comment on a
/// method lists the copies it stands for (docs/source-map/shared-functions.csv, under the common name of
/// twin-names.csv). All drawing goes to the screen port <see cref="ILibraryState.Rp1"/>.
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="screen">The part's variables of these screens.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="output">The part's drawing routines.</param>
/// <param name="input">The part's key and joystick routines.</param>
/// <param name="text">The part's text routines.</param>
/// <param name="readKey">
/// The part's own key reading, where it is not the library's (the raid has its own <c>read_key</c>, which its copy of
/// <see cref="WaitForKey"/> calls); null for <see cref="LibraryInput.ReadKey"/>.
/// </param>
public sealed class LibraryScreens(ILibraryState state, ILibraryScreenState screen, Boundaries world, LibraryOutput output, LibraryInput input, LibraryText text, Func<int>? readKey = null)
{
    private const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>The last column and row of the screen.</summary>
    private const int RightEdge = ScreenWidth - 1, BottomEdge = ScreenHeight - 1;

    // palette indices
    private const int Black = 0, DarkCyan = 3, BlueGrey = 5, Brown = 6, Grey = 7, DarkGrey = 8, LightCyan = 0xb, White = 0xf;

    /// <summary>The colour a title bar's images are drawn in, which the bar then turns into its own colour.</summary>
    private const int TitleBarImageColor = 0xd;

    /// <summary>The graphics mode letter of a CGA screen (<see cref="ILibraryScreenState.GMode"/>), where faces and photos keep the driver's own colours.</summary>
    private const sbyte CgaMode = (sbyte)'C';

    /// <summary>The report frame's field colour on a CGA screen.</summary>
    private const int CgaFieldColor = 2;

    /// <summary>The images of a title bar's two ends.</summary>
    private const int TitleBarLeftEnd = 0x6d, TitleBarRightEnd = 0x6e;

    /// <summary>A title bar's size.</summary>
    private const int TitleBarWidth = 0xa0, TitleBarHeight = 8;

    /// <summary>A panel's width; the report frame's inner panel has the same.</summary>
    private const int PanelWidth = 0x130;

    /// <summary>The first image of each of the five layers of a face, for women (look bit 0 clear) and men.</summary>
    private const int FemaleFaces = 0, MaleFaces = 0x28;

    /// <summary>The size of a face, in pixels.</summary>
    private const int FaceWidth = 0x20, FaceHeight = 0x23;

    /// <summary>The size of the picture area of a photo, recoloured for its sepia look.</summary>
    private const int PhotoSize = 0x32;

    /// <summary>The first image of the clothes under a photo: men's, women's; the look's two low bits pick one of four.</summary>
    private const int MaleClothes = 0x68, FemaleClothes = 0x70;

    /// <summary><see cref="WaitForKey"/>'s answer for a joystick button.</summary>
    private const int JoystickButton = 1;

    /// <summary>
    /// Draws a face from a person's face word: five images on top of each other (from a man's or a woman's set, bit 0;
    /// the variant of each picked by bit 1 and the three-bit fields at bits 4, 7, 10 and 13), then, unless on CGA, the
    /// skin tone (bits 2 and 3) recoloured into the 32 by 35 pixels. The face beside the next box is cleared.
    /// </summary>
    /// <param name="look">The face word (<see cref="Core.Records.Person.Face"/>).</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    // orig: GAME 00f7:81a6 draw_face (src/c/game/game_00f7_81a6.c) matched; also TAC 00e8:a9b8, BUG 00e8:4134, FINAL 0100:7afe; tests: LibraryTextOracleTests
    public void DrawFace(int look, int x, int y)
    {
        var faces = (look & 1) != 0 ? MaleFaces : FemaleFaces;
        // the layers from the back: head (bit 1), then the fields of bits 4, 7, 10 and 13
        int[] layers = [((look & 2) << 1) + faces + 0x20, (look >> 4 & 7) + faces + 0x18, (look >> 7 & 7) + faces + 0x10, (look >> 10 & 7) + faces + 8, (look >> 13 & 7) + faces];
        foreach (var layer in layers)
        {
            output.DrawImage(screen.Sprite, layer, x + 1, y + 1);
        }
        if (screen.GMode != CgaMode)
        {
            // the face images are drawn in colours 11 and 12; the tone puts other colours in their place
            const int faceColor = 0xb, faceShade = 0xc, lightTone = 0xe;
            switch (look >> 2 & 3)
            {
                case 0:
                    Recolor(x, y, FaceWidth, FaceHeight, faceColor, lightTone);
                    break;
                case 1:
                    Recolor(x, y, FaceWidth, FaceHeight, faceColor, Brown);
                    break;
                case 3:
                    Recolor(x, y, FaceWidth, FaceHeight, faceShade, Brown);
                    // and on, as tone 2
                    Recolor(x, y, FaceWidth, FaceHeight, faceColor, BlueGrey);
                    break;
                case 2:
                    Recolor(x, y, FaceWidth, FaceHeight, faceColor, BlueGrey);
                    break;
            }
        }
        screen.Picface = -1;
    }

    /// <summary>
    /// Draws a person's photo: a grey and white frame 54 by 56 pixels, a black inner box with a tinted background, the
    /// face, then, unless on CGA, eight recolourings that give the photo its sepia look, and the clothes at the bottom.
    /// The driver's colour tables are the photo's while it is drawn and the part's own afterwards.
    /// </summary>
    /// <param name="look">The face word (<see cref="Core.Records.Person.Face"/>).</param>
    /// <param name="x">Left edge of the picture (the frame starts two pixels further out).</param>
    /// <param name="y">Top edge of the picture.</param>
    // orig: GAME 00f7:830f draw_portrait (src/c/game/game_00f7_830f.c) matched; also BUG 00e8:429d, FINAL 0100:7c67, TAC 00e8:ab21; tests: LibraryTextOracleTests
    public void DrawPortrait(int look, int x, int y)
    {
        world.Graphics.SetColorMaps(screen.PhotoColorMap, screen.PhotoColorMap);
        Fill(x - 2, y - 2, 0x36, 0x38, Grey);
        Fill(x - 1, y - 1, 0x34, 0x36, White);
        Fill(x + 2, y + 2, 0x2e, 0x2d, Black);
        var tone = look >> 2 & 3;
        Fill(x + 3, y + 3, 0x2c, 0x2b, tone == 3 && screen.GMode != CgaMode ? Grey : DarkCyan);
        DrawFace(look, x + 0xd, y + 3);
        if (screen.GMode != CgaMode)
        {
            (int From, int To)[] sepia = [(8, 5), (0xc, 7), (0xe, 0xf), (6, 8), (4, 8), (1, 8), (0xa, 7), (9, 7)];
            foreach (var (from, to) in sepia)
            {
                Recolor(x, y, PhotoSize, PhotoSize, from, to);
            }
        }
        output.DrawImage(screen.Sprite, ((look & 1) != 0 ? MaleClothes : FemaleClothes) + (look & 3), x + 3, y + 0x1c);
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
    }

    /// <summary>
    /// Draws the frame of the report screens (the clue report, the dossiers, the personnel file): a brown strip at the
    /// top with the title bar, the field below in its colour, a white inner panel, and the title in black on a grey
    /// box centred on the strip. The screen's whole width becomes the width the box routines save.
    /// </summary>
    /// <param name="title">The screen's title.</param>
    /// <param name="field">Palette index of the field around the panel (on CGA always colour 2).</param>
    // orig: BUG 00e8:4ee2 draw_screen_frame (src/c/bug/bug_00e8_4ee2.c) matched; also FINAL 0100:88ac, GAME 00f7:0c38, TAC 00e8:b766; tests: LibraryTextOracleTests
    public void DrawScreenFrame(string title, int field)
    {
        const int stripHeight = 0xa, titleCentre = 0xf0;
        state.Animation.SaveWidth = ScreenWidth;
        if (screen.CGA)
        {
            field = CgaFieldColor;
        }
        Fill(0, 0, ScreenWidth, stripHeight, Brown);
        Fill(0, stripHeight, ScreenWidth, ScreenHeight - stripHeight, field);
        state.Rp1.BPen = (short)field;
        DrawTitleBar(TitleBarWidth, 2, field);
        output.DrawLine(0, 0, 0, BottomEdge, Brown);
        output.DrawLine(RightEdge, 0, RightEdge, BottomEdge, Brown);
        Fill(8, 0xe, PanelWidth, 0xba, White);
        output.DrawLine(0xa, 0xd, 0x138, 0xd, Grey);
        output.DrawLine(0x138, 0xd, 0x138, BottomEdge, DarkGrey);
        var width = text.TextWidth(title);
        Fill(titleCentre - 1 - width / 2, 3, width + 2, 9, Grey);
        output.DrawTextFlag0(title, titleCentre - width / 2, 4, Black);
        state.Rp1.BPen = White;
    }

    /// <summary>
    /// Draws a title bar 160 by 8 pixels: its middle in a colour, a blue-grey line above it (unless at the top of the
    /// screen), dark grey lines from its foot to both edges of the screen, its two end images, and the colour put into
    /// the end images too.
    /// </summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="color">Palette index of the bar.</param>
    // orig: BUG 00e8:5029 draw_title_bar (src/c/bug/bug_00e8_5029.c) matched; also FINAL 0100:89f3, TAC 00e8:b8ad, GAME 00f7:0d7f; tests: LibraryTextOracleTests, GameOracleTests
    public void DrawTitleBar(int x, int y, int color)
    {
        const int endWidth = 0xa, rightEnd = 0x93;
        Fill(x + endWidth, y, 0x8c, TitleBarHeight, color);
        if (y != 0)
        {
            output.DrawLine(x + endWidth, y - 1, x + rightEnd, y - 1, BlueGrey);
        }
        output.DrawLine(0, y + TitleBarHeight, x, y + TitleBarHeight, DarkGrey);
        output.DrawLine(x + 0x9e, y + TitleBarHeight, RightEdge, y + TitleBarHeight, DarkGrey);
        output.DrawImage(screen.Sprite, TitleBarLeftEnd, x, y);
        output.DrawImage(screen.Sprite, TitleBarRightEnd, x + rightEnd, y);
        Recolor(x, y, TitleBarWidth, TitleBarHeight, TitleBarImageColor, color);
    }

    /// <summary>
    /// Draws a white panel 304 pixels wide from (<paramref name="x"/>, <paramref name="y"/>) to the bottom of the
    /// screen, lit on its top and left edges and shaded on its right (FINAL's report cards).
    /// </summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    // orig: GAME 00f7:0f0c draw_panel (src/c/game/game_00f7_0f0c.c) matched; also TAC 00e8:ba3a, BUG 00e8:51b6, FINAL 0100:8b80; tests: LibraryTextOracleTests, FinalOracleTests
    public void DrawPanel(int x, int y)
    {
        Fill(x, y, PanelWidth, ScreenHeight - y, White);
        output.DrawLine(x + 1, y, x + PanelWidth, y, Grey);
        output.DrawLine(x + PanelWidth, y, x + PanelWidth, BottomEdge, DarkGrey);
        output.DrawLine(x, y + 1, x, BottomEdge, Grey);
    }

    /// <summary>
    /// Draws a film strip over the whole screen: white, a grey line near each edge with a dark grey perforation square
    /// every 12 pixels outside it, and three picture frames of light cyan hatching, 64 pixels apart.
    /// </summary>
    // orig: GAME 00f7:0fb8 draw_film_border (src/c/game/game_00f7_0fb8.c) matched; also BUG 00e8:5262; tests: LibraryTextOracleTests
    public void DrawFilmBorder()
    {
        const int leftLine = 8, rightLine = 0x137, leftHoles = 2, rightHoles = 0x13a, hole = 4, holeStep = 0xc;
        const int frameLeft = 0x17, frameRight = 0x128, frameHeight = 0x20, frameStep = 0x40;
        Fill(0, 0, ScreenWidth, ScreenHeight, White);
        output.DrawLine(leftLine, 0, leftLine, BottomEdge, Grey);
        output.DrawLine(rightLine, 0, rightLine, BottomEdge, Grey);
        for (var y = 4; y < BottomEdge; y += holeStep)
        {
            Fill(leftHoles, y, hole, hole, DarkGrey);
            Fill(rightHoles, y, hole, hole, DarkGrey);
        }
        for (var y = 0x20; y < BottomEdge; y += frameStep)
        {
            output.DrawLine(frameLeft, y + 1, frameLeft, y + 0x1d, LightCyan);
            output.DrawLine(frameRight, y + 1, frameRight, y + 0x1d, LightCyan);
            for (var line = 0; line < frameHeight; line += 2)
            {
                output.DrawLine(frameLeft + 1, y + line, frameRight - 1, y + line, LightCyan);
            }
        }
    }

    /// <summary>
    /// Draws a figure from a table of <see cref="Shape"/> records, in order, up to the end record. A repeat record
    /// draws the records before it again, shifted by its step on each pass; while it runs it marks itself in its
    /// colour field, and it leaves the mark cleared.
    /// </summary>
    /// <param name="shapes">The part's table (its repeat records change while it is drawn).</param>
    /// <exception cref="UninitialisedReadException">A repeat record found already running at the start: the original then counts down a pass counter it never set.</exception>
    // orig: GAME 00f7:147d draw_shape_list (src/c/game/game_00f7_147d.c) matched; also BUG 00e8:5727; tests: LibraryTextOracleTests
    public void DrawShapeList(IReadOnlyList<Shape> shapes)
    {
        int? passesLeft = null;
        int dx = 0, dy = 0;
        var i = 0;
        do
        {
            var shape = shapes[i];
            switch (shape.Kind)
            {
                case Shape.Line:
                    output.DrawLine(shape.X1 + dx, shape.Y1 + dy, shape.X2 + dx, shape.Y2 + dy, shape.Color);
                    break;
                case Shape.FilledRectangle:
                    output.FillRectCorners(shape.X1 + dx, shape.Y1 + dy, shape.X2 + dx, shape.Y2 + dy, shape.Color);
                    break;
                case Shape.Repeat:
                    if (shape.Color == 0)
                    {
                        dx = dy = 0;
                        passesLeft = shape.X2;
                        shape.Color = 1;
                    }
                    // C: the counter is a 16-bit local
                    passesLeft = C16.Int((passesLeft ?? throw new UninitialisedReadException("the pass counter of a repeat found running")) - 1);
                    if (passesLeft != 0)
                    {
                        dx += shape.X1;
                        dy += shape.Y1;
                        i -= shape.Y2 + 1;
                    }
                    else
                    {
                        shape.Color = 0;
                        dx = dy = 0;
                    }
                    break;
                case Shape.Rectangle:
                    output.DrawLine(shape.X1 + dx, shape.Y1 + dy, shape.X1 + dx, shape.Y2 + dy, shape.Color);
                    output.DrawLine(shape.X1 + dx, shape.Y1 + dy, shape.X2 + dx, shape.Y1 + dy, shape.Color);
                    output.DrawLine(shape.X2 + dx, shape.Y2 + dy, shape.X2 + dx, shape.Y1 + dy, shape.Color);
                    output.DrawLine(shape.X2 + dx, shape.Y2 + dy, shape.X1 + dx, shape.Y2 + dy, shape.Color);
                    break;
                case Shape.End:
                    i = -1;
                    break;
            }
        }
        while (++i != 0);
    }

    /// <summary>
    /// Waits for one key, or a joystick button when the joystick is on, and returns it; then waits until the keys and
    /// buttons are let go. Keys typed before the wait are not thrown away: the first one is the answer.
    /// </summary>
    /// <returns>The key's code (the part's key reading), or 1 for a joystick button.</returns>
    // orig: FINAL 0100:5f98 wait_for_key (src/c/final/final_0100_5f98.c) matched; also BUG 00e8:25ce, GAME 00f7:4cf2, TAC 00e8:8e52; tests: LibraryTextOracleTests, GameOracleTests, TacOracleTests
    public int WaitForKey()
    {
        const int noKey = -1;
        var key = noKey;
        input.BeginPointer();
        while (world.Input.NoKeyWaiting())
        {
            if (key != noKey)
            {
                break;
            }
            if (state.Status.HasFlag(Options.Joystick) && (input.JoystickButtonDown(0) || input.JoystickButtonDown(1)))
            {
                key = JoystickButton;
            }
            // DIVERGENCE: D-633 with the mouse option on, a click answers a "press a key" wait (left: Space, right: Esc).
            if (key == noKey)
            {
                key = input.ReadPointerKey();
            }
        }
        if (key == noKey)
        {
            key = (readKey ?? input.ReadKey)();
        }
        input.FlushInput();
        return key;
    }

    private void Fill(int x, int y, int width, int height, int color) => world.Graphics.FillRect(state.Rp1, x, y, width, height, color);

    private void Recolor(int x, int y, int width, int height, int from, int to) => world.Graphics.RecolorRect(state.Rp1, x, y, width, height, from, to);

    /// <summary>
    /// Shows the text buffer as a bulletin under a heading ("INTERPOL", "Incoming Msg" ...): the banner and the ticker
    /// are drawn over the upper half of the screen, the text is laid out on the picture page, and its lines scroll into
    /// the ticker one by one, each with a running cursor and its sounds, until the text ends or a key is pressed; then
    /// the player's key is awaited and the screen restored.
    /// </summary>
    /// <param name="title">The heading.</param>
    /// <param name="pictures">The port of the picture page, where the text is laid out.</param>
    /// <param name="colorMap">The colour tables the driver uses meanwhile.</param>
    /// <param name="shapes">The part's figure of the banner (see <see cref="DrawShapeList"/>).</param>
    /// <param name="caption">The caption under the ticker, "Incoming Msg".</param>
    // orig: GAME 00f7:10c7 show_bulletin (src/c/game/game_00f7_10c7.c) matched; also BUG 00e8:5371; tests: GameOracleTests
    public void ShowBulletin(string title, RasterPort pictures, byte[] colorMap, IReadOnlyList<Shape> shapes, string caption)
    {
        const int pictureSheet = 2, tickerX = 0x31, tickerWidth = 0xde, lineX = 0x32, lineY = 0x9b, lineWidth = 0xdc;
        const int firstTextLine = 0x4c, lineHeight = 8, cursorImage = 0x6f, tickSound = 0x1f, lineEndSound = 0x20;
        const int textRight = 0xf0, titleColor = 0xc, captionColor = 0xa;
        var rp1 = state.Rp1;
        world.Graphics.CopyRect(rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        world.Graphics.SetColorMaps(colorMap, colorMap);
        Fill(0, 0, ScreenWidth, ScreenHeight / 2, BlueGrey);
        DrawShapeList(shapes);
        Fill(lineX, 0x96, lineWidth, 0xd, White);
        text.DrawTextCentered(title, ScreenWidth / 2, 0x98, titleColor);
        text.DrawTextCentered(caption, 0x40, 0xba, captionColor);
        output.DrawLine(tickerX, 0x96, tickerX, 0xa2, DarkGrey);
        output.DrawLine(0x10e, 0x96, 0x10e, 0xa2, DarkGrey);
        world.Graphics.CopyRect(rp1, 0, ScreenHeight / 2, ScreenWidth, ScreenHeight / 2, pictures, 0, ScreenHeight / 2);
        // the text is laid out on the picture page, through the screen's port
        rp1.PageNr = pictureSheet;
        Fill(lineX, 0x44, lineWidth, 0x5f, White);
        rp1.BPen = White;
        var textBottom = text.DrawWrappedText(0x19, 0x3a, firstTextLine, BlueGrey);
        rp1.PageNr = 0;
        input.FlushInput();
        int line, x = 0;
        for (line = 0; (textBottom - firstTextLine) / lineHeight > line; line++)
        {
            var rise = line * lineHeight;
            world.Graphics.CopyRect(rp1, tickerX, 0x96 - rise, tickerWidth, rise + 0xd, rp1, tickerX, 0x8e - rise);
            Fill(lineX, lineY, lineWidth, lineHeight, White);
            output.DoSound(tickSound);
            for (x = 0x34; x < textRight; x++)
            {
                world.Graphics.CopyRect(pictures, x - 2, rise + 0x4a, 2, 0xa, rp1, x - 2, 0x99);
                output.DrawLine(x - 1, 0xa3, x - 1, 0xa6, Grey);
                output.DrawImage(screen.Sprite, cursorImage, x, 0x9a);
                output.WaitVsync((x & 7) != 0 ? 1 : 3);
                if (!world.Input.NoKeyWaiting())
                {
                    break;
                }
            }
            output.DoSound(lineEndSound);
            output.WaitVsync(0xf);
            world.Graphics.CopyRect(pictures, x - 2, rise + 0x4a, 0x20, 0xa, rp1, x - 2, 0x99);
            Fill(x - 1, 0xa3, 0x20, 4, Grey);
            if (!world.Input.NoKeyWaiting())
            {
                break;
            }
        }
        var last = line * lineHeight;
        world.Graphics.CopyRect(rp1, tickerX, 0x96 - last, tickerWidth, last + 0xd, rp1, tickerX, 0x8e - last);
        Fill(lineX, lineY, lineWidth, lineHeight, White);
        world.Graphics.CopyRect(rp1, tickerX, 0x8e - last, tickerWidth, last + 0x15, rp1, tickerX, 0x88 - last);
        Fill(lineX, lineY, lineWidth, lineHeight, White);
        output.DrawImage(screen.Sprite, cursorImage, 0x34, 0x9a);
        WaitForKey();
        world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, rp1, 0, 0);
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
    }
}
