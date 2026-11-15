using CovertAction.Msc;

namespace CovertAction.Presentation.Final;

/// <summary>
/// The roster of the debriefing: the members of the cast in cells of 80 x 52 pixels, four to a row, each with the
/// face of the person who played the part and the role's name. As the case log is played back, cells are framed,
/// greyed out or put behind bars.
/// </summary>
public sealed partial class Headquarters
{
    private const int CellWidth = 0x50;
    private const int CellHeight = 0x34;
    private const int CellsPerRow = 4;

    /// <summary>The inside of a cell, within its one-pixel frame.</summary>
    private const int CellInsideWidth = 0x4e, CellInsideHeight = 0x32;

    /// <summary>The picture area of a cell, below its label line.</summary>
    private const int PictureTop = 9, PictureHeight = 0x2a;

    // palette indices
    private const int Black = 0;
    private const int CellFrame = 1;
    private const int HighlightFrame = 9;
    private const int LabelColour = 5;
    private const int BarLight = 7;
    private const int BarShade = 8;
    private const int Grey = 7;
    private const int PaletteSize = 0x10;

    /// <summary>The second line of a role's name is this far below the first.</summary>
    private const int LabelLineHeight = 8;

    /// <summary>The top left corner of roster cell <paramref name="cell"/>.</summary>
    private static (int X, int Y) CellOrigin(int cell) =>
        // C: 16-bit products and signed division (the cell is a 16-bit argument)
        (C16.Int(cell % CellsPerRow * CellWidth), C16.Int(cell / CellsPerRow * CellHeight));

    /// <summary>The images of a person's clothes: 0x68 + 0..3 for a man, 0x70 + 0..3 for a woman.</summary>
    private const int MaleClothes = 0x68, FemaleClothes = 0x70;

    /// <summary>The first image of the plot objects' icons on the work page: 16 x 16 pixels each, side by side.</summary>
    private const int IconSize = 0x10;

    /// <summary>The image of the clothes that go with a look word: by its male bit, and its two low bits.</summary>
    private static int ClothesOf(int look) => ((look & 1) != 0 ? MaleClothes : FemaleClothes) + (look & 3);

    /// <summary>
    /// Draws roster cell <paramref name="cell"/>: the frame, the face of the person who played the part with the
    /// clothes that go with it, and the role's name.
    /// </summary>
    /// <param name="cell">The cell, which is the member of the cast, from 0.</param>
    // orig: FINAL 0100:2703 draw_roster_cell (src/c/final/final_0100_2703.c) matched; tests: FinalOracleTests
    public void DrawRosterCell(int cell)
    {
        const int cgaFrame = 3, faceLeft = 0x2d, faceTop = 8, clothesLeft = 0x23, clothesTop = 0x21;
        var (x, y) = CellOrigin(cell);
        _world.Graphics.FillRect(_state.Rp1, x + 1, y + 1, CellInsideWidth, CellInsideHeight, _state.CGA ? cgaFrame : CellFrame);
        var look = _state.Ps[_state.Ac[cell].Who].Face;
        Screens.DrawFace(look, x + faceLeft, y + faceTop);
        Output.DrawImage(_state.SpriteTable, ClothesOf(look), x + clothesLeft, y + clothesTop);
        DrawRosterLabel(x, y, cell);
    }

    /// <summary>Draws again every roster cell whose part was played by <paramref name="person"/>.</summary>
    /// <param name="person">The person, 0..63.</param>
    // orig: FINAL 0100:2c2a redraw_person_cells (src/c/final/final_0100_2c2a.c) matched; tests: FinalOracleTests
    public void RedrawPersonCells(int person)
    {
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            if (_state.Ac[cell].Who == person)
            {
                DrawRosterCell(cell);
            }
        }
    }

    /// <summary>
    /// Shows under the face in the roster cells of person <paramref name="q"/> the icons of the plot's objects that
    /// the person holds, stacked upwards; the strip is first cleared to the colour of the cell's corner.
    /// </summary>
    /// <param name="q">The person, 0..63; -1 for none (nothing is drawn).</param>
    // orig: FINAL 0100:2941 draw_held_objects (src/c/final/final_0100_2941.c) matched; tests: FinalOracleTests
    public void DrawHeldObjects(int q)
    {
        const int screenPage = 0, stripTop = 0x13, stripHeight = 0x20, iconTop = 0x23;
        if (q == -1)
        {
            return;
        }
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            if (_state.Ac[cell].Who != q)
            {
                continue;
            }
            var (x, y) = CellOrigin(cell);
            var background = _world.Graphics.GetPixel(screenPage, x + 1, y + stripTop - 1);
            _world.Graphics.FillRect(_state.Rp1, x + 1, y + stripTop, IconSize, stripHeight, background);
            foreach (var item in _state.Mc)
            {
                if (item.Who == q)
                {
                    // C: the icon's place on the work page is its type times 16, a 16-bit product
                    _world.Graphics.CopyRect(_state.WorkPortX, C16.Int(item.Type << 4), 0, IconSize, IconSize, _state.Rp1, x + 1, y + iconTop);
                    y = C16.Int(y - IconSize);
                }
            }
        }
    }

    /// <summary>Highlights the frame of roster cell <paramref name="cell"/> (not on the four-colour display).</summary>
    /// <param name="cell">The cell, from 0.</param>
    // orig: FINAL 0100:28de frame_roster_cell (src/c/final/final_0100_28de.c) matched; tests: FinalOracleTests
    public void FrameRosterCell(int cell)
    {
        var (x, y) = CellOrigin(cell);
        if (!_state.CGA)
        {
            _world.Graphics.RecolorRect(_state.Rp1, x + 1, y + 1, CellInsideWidth, CellInsideHeight, CellFrame, HighlightFrame);
        }
    }

    /// <summary>Highlights every roster cell whose part was played by <paramref name="person"/>.</summary>
    /// <param name="person">The person, 0..63.</param>
    // orig: FINAL 0100:28a8 frame_person_cells (src/c/final/final_0100_28a8.c) matched; tests: FinalOracleTests
    public void FramePersonCells(int person)
    {
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            if (_state.Ac[cell].Who == person)
            {
                FrameRosterCell(cell);
            }
        }
    }

    /// <summary>
    /// Greys out the pictures of the roster cells whose part was played by <paramref name="p"/>: every colour but the
    /// outline's becomes black.
    /// </summary>
    /// <param name="p">The person, 0..63.</param>
    // orig: FINAL 0100:2b92 darken_person_cells (src/c/final/final_0100_2b92.c) matched; tests: FinalOracleTests
    public void DarkenPersonCells(int p)
    {
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            if (_state.Ac[cell].Who != p)
            {
                continue;
            }
            var (x, y) = CellOrigin(cell);
            for (var colour = 1; colour < PaletteSize; colour++)
            {
                if (colour == CellFrame)
                {
                    continue;
                }
                _world.Graphics.RecolorRect(_state.Rp1, x + 1, y + PictureTop, CellInsideWidth, PictureHeight, colour, Black);
            }
        }
    }

    /// <summary>
    /// Puts the roster cells whose part was played by <paramref name="person"/> behind bars: a grey frame round the
    /// picture, upright bars every six pixels (a light line with a dark one beside it), and the role's name again.
    /// </summary>
    /// <param name="person">The person, 0..63.</param>
    // orig: FINAL 0100:2a30 draw_jail_bars (src/c/final/final_0100_2a30.c) matched; tests: FinalOracleTests
    public void DrawJailBars(int person)
    {
        const int barsLeft = 0x18, barsRight = 0x48, barSpacing = 6, barsTop = 0xb, barsBottom = 0x27;
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            if (_state.Ac[cell].Who != person)
            {
                continue;
            }
            var (x, y) = CellOrigin(cell);
            var graphics = _world.Graphics;
            graphics.FillRect(_state.Rp1, x, y, CellWidth, 0xb, Grey);
            graphics.FillRect(_state.Rp1, x, y + 0xa, barsLeft, PictureHeight, Grey);
            graphics.FillRect(_state.Rp1, x + barsRight, y + 0xa, CellWidth - barsRight, PictureHeight, Grey);
            graphics.FillRect(_state.Rp1, x, y + 0x28, CellWidth, 0xc, Grey);
            Output.DrawLine(x + barsLeft, y + barsBottom, x + barsRight - 1, y + barsBottom, BarShade);
            for (var bar = barsLeft; bar < barsRight; bar += barSpacing)
            {
                Output.DrawLine(x + bar, y + barsTop, x + bar, y + barsBottom, BarLight);
                Output.DrawLine(x + bar + 1, y + barsTop, x + bar + 1, y + barsBottom, Black);
            }
            DrawRosterLabel(x, y, cell);
        }
    }

    /// <summary>
    /// Writes the role of cast member <paramref name="castMember"/> into the roster cell at (<paramref name="x"/>,
    /// <paramref name="y"/>), in two lines where the name has a blank. The name is put together in the text buffer,
    /// which keeps it, cut at the blank, afterwards.
    /// </summary>
    /// <param name="x">The cell's left edge.</param>
    /// <param name="y">The cell's top edge.</param>
    /// <param name="castMember">The member, 0..31.</param>
    // orig: FINAL 0100:27cc draw_roster_label (src/c/final/final_0100_27cc.c) matched; tests: FinalOracleTests
    public void DrawRosterLabel(int x, int y, int castMember)
    {
        const int margin = 2;
        var name = _state.Ac[castMember].Name;
        _state.Txt.Text = name;
        var split = name.IndexOf(_state.RosterLabelSeparator, StringComparison.Ordinal);
        // the four-colour display draws the letters with their background, the others the letters alone
        Action<string, int, int, int> draw = _state.CGA ? Output.DrawText : Output.DrawTextFlag0;
        if (split < 0)
        {
            draw(name, x + margin, y + margin, LabelColour);
            return;
        }
        _state.Txt.Truncate(split);
        draw(name[..split], x + margin, y + margin, LabelColour);
        draw(name[(split + 1)..], x + margin, y + margin + LabelLineHeight, LabelColour);
    }
}
