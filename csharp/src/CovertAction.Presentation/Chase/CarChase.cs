using CovertAction.Core;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Chase;

/// <summary>
/// The car chase as the player sees it: the city map on the left of the screen (200 x 200 pixels, 8 to a tile), with
/// the cars as dots and the known buildings labelled; the street view (120 x 100 pixels, one per world unit) below the
/// cockpit on the right. This class holds the functions of the original's CHASE program that draw them; the rules
/// they apply are in <see cref="ChaseRules"/>.
/// </summary>
public sealed partial class CarChase
{
    // the city map: 8 pixels to a tile, a quarter of a pixel per world unit
    private const int MapPixelsPerTile = 8;
    private const int MapSize = 200;
    private const int MapLast = MapSize - 1;

    // the right-hand panel above the street view (the dashboard's background in chase.pic)
    private const int PanelLeft = 200;
    private const int PanelRight = 319;
    private const int PanelBottom = 99;
    private const int PanelSourceY = 101;

    // the street view: 120 x 100 pixels, one per world unit, put together on the off-screen page
    private const int ViewWidth = 120;
    private const int ViewHeight = 100;
    private const int ViewTiles = 5;
    private const int CameraStep = 4;

    /// <summary>A car is drawn only when its centre is more than this many pixels inside the street view's edges.</summary>
    private const int ViewMargin = 4;

    // the ground pictures in chase.pic: 32 x 32 tiles, 10 to a row, the first row at y 17
    private const int GroundRowWidth = 320;
    private const int GroundTop = 0x11;

    // the car sprites in chase.pic: one 8-pixel cell per sprite number; 6 x 4 pixels heading sideways, 4 x 6 heading
    // up or down; one row of sprites per colour of car
    private const int SpriteCell = 8;
    private const int SpriteLong = 6;
    private const int SpriteShort = 4;
    private const int SpriteRowAcross = 2, SpriteRowAcrossOther = 0xa;
    private const int SpriteRowAlong = 1, SpriteRowAlongOther = 9;

    /// <summary>Sprite numbers: heading plus 0 (very suspicious), 4 (normal) or 8 (the car the keys steer, flashing).</summary>
    private const int SpriteNormal = 4, SpriteSelected = 8;

    /// <summary>Sprite numbers: plus 12 for the right blinker, plus 24 for the left one.</summary>
    private const int SpriteBlinkRight = 0xc, SpriteBlinkLeft = 0x18;

    // the map generator
    private const int CarveAttempts = 192;
    private const int CarveMargin = 2;
    private const int CarveRange = 21;
    private const int GroundPictures = 16;

    /// <summary>CHASE.PIC holds this many ground pictures (ten to a row, two rows); the original's generator draws the first 16.</summary>
    private const int GroundPicturesInTheSheet = 20;
    private const int StreetGround = 8;
    private const byte AllSidesOpen = 0xf;
    private const int MapSeedFactor = 0x7b;
    private const int MapSeedMask = 0x3fff;

    // where the locations may lie: on easy levels the middle 12 x 12 tiles, else the middle 18 x 18
    private const int EasyLocationTiles = 12, EasyLocationX = 0xc0, EasyLocationY = 0xd0;
    private const int HardLocationTiles = 18, HardLocationX = 0x60, HardLocationY = 0x70;
    private const int HardLocationsFrom = 2;
    private const int LocationSpacing = 0x40;

    /// <summary>A location's dot lies this many units beside it (right for odd numbers, left for even); its label beside the dot.</summary>
    private const int LocationDotOffset = 8, LabelOffsetX = 4, LabelOffsetY = 3;


    // palette indices
    private const int Black = 1;
    private const int Grid = 5;
    private const int Kerb = 7;
    private const int Field = 7;
    private const int NightField = 8;
    private const int LabelDay = 9;
    private const int LabelNight = 7;
    private const int White = 0xf;

    /// <summary>The page of the off-screen surface.</summary>
    private const int BackupPage = 1;

    private readonly ChaseState _state;
    private readonly SharedState _shared;
    private readonly Boundaries _world;
    private readonly OriginalBehaviour _behaviour;

    /// <summary>
    /// The chase without a campaign (the player's option cutContent.standaloneChase, D-661; CC-2): the part runs with the
    /// original's own non-campaign mode, which no program of the original starts. Off, as the original.
    /// </summary>
    public bool Standalone { get; init; }

    /// <summary>Wires the part's routines to its state and to the outside world.</summary>
    /// <param name="state">The part's variables.</param>
    /// <param name="shared">The hand-off block.</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce.</param>
    public CarChase(ChaseState state, SharedState shared, Boundaries world, OriginalBehaviour behaviour)
    {
        _state = state;
        _shared = shared;
        _world = world;
        _behaviour = behaviour;
        Output = new LibraryOutput(state, world);
        Input = new LibraryInput(state, world);
        // the box plays its sounds through this part's own DoSound, which hands the driver the gear as well
        Menus = new LibraryMenus(state, shared, world, Output, Input, DoSound);
        Pictures = new LibraryPictures(world);
    }

    /// <summary>The part's drawing routines.</summary>
    public LibraryOutput Output { get; }

    /// <summary>The part's keyboard, joystick and mouse routines.</summary>
    public LibraryInput Input { get; }

    /// <summary>The part's message box, disk prompt and animation routines.</summary>
    public LibraryMenus Menus { get; }

    /// <summary>The part's picture loading routines.</summary>
    public LibraryPictures Pictures { get; }

    /// <summary>
    /// Plays a sound effect, unless the player has switched sound off (Alt-S). The chase hands the sound driver a
    /// second number with every effect: <see cref="ChaseState.SoundParam"/>, the current car's gear, which the engine
    /// sounds use.
    /// </summary>
    /// <param name="effect">The effect's number.</param>
    // orig: CHASE 00df:0467 DoSound (src/c/chase/chase_00df_0467.c) matched; tests: ChaseOracleTests
    // Other code under the same name: CODE 0000:0cbd and its group, TAC 00e8:0662 (not this method; docs/source-map/shared-functions.csv).
    public void DoSound(int effect)
    {
        if (_state.Status.HasFlag(Options.Sound))
        {
            _world.Sound.PlaySound(effect, _state.SoundParam);
        }
    }

    /// <summary>Plots one pixel on the screen: a line from a point to itself.</summary>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    /// <param name="color">Palette index.</param>
    // orig: CHASE 00df:1ff8 plot_pixel (src/c/chase/chase_00df_1ff8.c) matched; tests: ChaseOracleTests
    public void PlotPixel(int x, int y, int color) => Output.DrawLine(x, y, x, y, color);

    /// <summary>Plots a 2 x 2 dot on the city map for a world position (a quarter of a pixel per unit).</summary>
    /// <param name="x">World x.</param>
    /// <param name="y">World y.</param>
    /// <param name="color">Palette index.</param>
    // orig: CHASE 00df:1f80 plot_map_dot (src/c/chase/chase_00df_1f80.c) matched; tests: ChaseOracleTests
    public void PlotMapDot(int x, int y, int color)
    {
        int left = MapPixel(x), top = MapPixel(y);
        PlotPixel(left, top, color);
        PlotPixel(left + 1, top, color);
        PlotPixel(left, top + 1, color);
        PlotPixel(left + 1, top + 1, color);
    }

    /// <summary>
    /// Builds the city and draws its map: every street, then 192 tries at closing a stretch of road, then the 16
    /// locations, with the known ones labelled; finally picks where the quarry starts and where it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In a campaign the random generator is seeded from the city, so a city always has the same streets (and the
    /// same locations). Every cell gets a random ground picture and all four sides open; the map shows the street grid
    /// in black-and-grey lines over the field colour. A try picks a cell of the inner 21 x 21 and a direction (the try's
    /// number modulo 4; for down and left only between two cells that are still open all round) and closes the side
    /// between the cell and its neighbour: the side's bit is cleared in both cells, the cell gets the street ground
    /// (8 or 9, by the tile's parity), and the stretch of grid is painted over in the field colour.
    /// </para>
    /// <para>
    /// A location is a point on a tile's left edge, half-way down, where a car may drive down, at least (difficulty x
    /// 32 + 64) units from the locations placed before it (location 0 is not compared); on difficulty 0 and 1 they lie
    /// in the middle 12 x 12 tiles. The quarry starts at the organisation the campaign names (or, in a practice
    /// mission, a random other location) and drives to the organisation the player is at; outside a campaign both would
    /// be random.
    /// </para>
    /// </remarks>
    // orig: CHASE 00df:2c51 generate_map (src/c/chase/chase_00df_2c51.c) matched; tests: ChaseOracleTests
    public void GenerateMap()
    {
        if (_state.CampaignMode)
        {
            // C: 0x7b * (unsigned)InCity + (map_seed & 0x3fff), as srand's 16-bit seed
            _state.Random.Srand(C16.Int(MapSeedFactor * _state.InCity + (_state.MapSeed & MapSeedMask)));
        }
        var field = _state.Night || _state.CGA ? NightField : Field;
        Output.FillRectCorners(0, 0, MapLast, MapLast, Black);
        Output.FillRectCorners(1, 1, MapLast - 1, MapLast - 1, field);
        Output.FillRectCorners(PanelLeft, 0, PanelRight, PanelBottom, Black);
        _world.Graphics.CopyRect(_state.RpSprite, PanelLeft, PanelSourceY, ViewWidth, ViewHeight, _state.Rp1, PanelLeft, 0);
        Output.DrawLine(PanelLeft, 0, PanelRight, 0, White);

        FillGround();
        DrawStreetGrid();
        for (var attempt = 0; attempt < CarveAttempts; attempt++)
        {
            CloseRoad(attempt, field);
        }
        for (var location = 0; location < ChaseState.Locations; location++)
        {
            PlaceLocation(location);
        }
        ChooseStartAndDestination();
    }

    /// <summary>Draws the street grid on the map: a double line every 8 pixels, across and down.</summary>
    private void DrawStreetGrid()
    {
        for (var x = MapPixelsPerTile; x < MapSize; x += MapPixelsPerTile)
        {
            Output.DrawLine(x, 1, x, MapLast - 1, Grid);
            Output.DrawLine(x + 1, 1, x + 1, MapLast - 1, Grid);
        }
        for (var y = MapPixelsPerTile; y < MapSize; y += MapPixelsPerTile)
        {
            Output.DrawLine(1, y, MapLast - 1, y, Grid);
            Output.DrawLine(1, y + 1, MapLast - 1, y + 1, Grid);
        }
    }

    /// <summary>One try of the map generator at closing the side between a random cell and its neighbour (see <see cref="GenerateMap"/>).</summary>
    private void CloseRoad(int attempt, int field)
    {
        var x = GameLibrary.RandomBelow(_state.Random, CarveRange) + CarveMargin;
        var y = GameLibrary.RandomBelow(_state.Random, CarveRange) + CarveMargin;
        var side = attempt & 3;
        var cell = ChaseState.CellIndex(x, y);
        var neighbour = ChaseState.CellIndex(x + _state.DirDx[side], y + _state.DirDy[side]);
        var cells = _state.MapCells;
        // down and left only between two cells that are still open all round
        if ((attempt & 2) != 0 && ((cells[cell] & AllSidesOpen) != AllSidesOpen || (cells[neighbour] & AllSidesOpen) != AllSidesOpen))
        {
            return;
        }
        cells[cell] &= (byte)~(1 << side);
        cells[cell] = (byte)((((x + y) & 1) << 4) + (cells[cell] & AllSidesOpen) + (StreetGround << 4));
        _state.HighGround[cell] = false;
        cells[neighbour] &= (byte)~(1 << ((attempt + 2) & 3));

        // paint the stretch of grid over: the segment's ends for this direction, and the line beside it
        var acrossEnds = _state.RoadXEnds[side];
        var alongEnds = _state.RoadYEnds[side];
        int left = x * MapPixelsPerTile, top = y * MapPixelsPerTile, odd = side & 1;
        Output.DrawLine(acrossEnds.From + left, alongEnds.From + top, acrossEnds.To + left, alongEnds.To + top, field);
        Output.DrawLine(acrossEnds.From + odd + left, alongEnds.From + top - odd + 1, acrossEnds.To + odd + left, alongEnds.To + top - odd + 1, field);
    }

    /// <summary>Places one location (see <see cref="GenerateMap"/>), drawing tries until one fits, and marks it on the map when the player knows it.</summary>
    private void PlaceLocation(int location)
    {
        int x, y;
        do
        {
            if (_state.Diff < HardLocationsFrom)
            {
                x = (GameLibrary.RandomBelow(_state.Random, EasyLocationTiles) << 5) + EasyLocationX;
                y = (GameLibrary.RandomBelow(_state.Random, EasyLocationTiles) << 5) + EasyLocationY;
            }
            else
            {
                x = (GameLibrary.RandomBelow(_state.Random, HardLocationTiles) << 5) + HardLocationX;
                y = (GameLibrary.RandomBelow(_state.Random, HardLocationTiles) << 5) + HardLocationY;
            }
        }
        while (!ChaseRules.CanMove(_state, x / ChaseState.TileUnits, y / ChaseState.TileUnits, Directions.Down) || TooCloseToOthers(location, x, y));

        _state.LocX[location] = (short)x;
        _state.LocY[location] = (short)y;
        if (location != 0 && (!_state.CampaignMode || (_state.Ct[_state.InCity].OrgClues & 1 << location) != 0))
        {
            PlotMapDot(((location & 1) != 0 ? LocationDotOffset : -LocationDotOffset) + x, y, White);
            // DORMANT: the original always sets the campaign flag, so the "Loc<n>" label is never made
            _state.MessageText.Text = _state.CampaignMode
                ? _state.Org[location].Sname
                : _state.StrLoc + location.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // the label's letters only, over the map (the draw mode of draw_text_flag0, set by hand here)
            _state.Rp1.DrawMode = 0;
            Output.DrawText(_state.MessageText.Text, MapPixel(x) + LabelOffsetX, MapPixel(y) - LabelOffsetY,
                _state.CGA ? White : _state.Night ? LabelNight : LabelDay);
            _state.Rp1.DrawMode = 1;
        }
    }

    /// <summary>Whether a new location is nearer than the spacing to one of the locations 1..n-1 already placed.</summary>
    private bool TooCloseToOthers(int location, int x, int y)
    {
        // C: 16-bit sums of the distances
        var spacing = C16.Int((_state.Diff << 5) + LocationSpacing);
        for (var other = 1; other < location; other++)
        {
            var distance = C16.Int(C16.Abs(C16.Int(x - _state.LocX[other])) + C16.Abs(C16.Int(y - _state.LocY[other])));
            if (spacing > distance)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Picks the quarry's start and destination (see <see cref="GenerateMap"/>).</summary>
    private void ChooseStartAndDestination()
    {
        // two different random locations 1..15; the draws happen in a campaign too, which then overrides them
        _state.StartLoc = (short)(GameLibrary.RandomBelow(_state.Random, ChaseState.Locations - 1) + 1);
        do
        {
            _state.DestLoc = (short)(GameLibrary.RandomBelow(_state.Random, ChaseState.Locations - 1) + 1);
        }
        while (_state.StartLoc == _state.DestLoc);
        if (_state.CampaignMode)
        {
            _state.StartLoc = _shared.Value;
            // a practice chase has no organisation to come from: a random other location
            if (_state.Status.HasFlag(Options.Practice))
            {
                do
                {
                    _state.StartLoc = (short)GameLibrary.RandomBelow(_state.Random, ChaseState.Locations);
                }
                while (_state.StartLoc == _state.AtLoc);
            }
            _state.DestLoc = _state.AtLoc;
        }
    }

    /// <summary>The map pixel of a world coordinate: x * 8 / 32.</summary>
    // C: 16-bit shift and a division that truncates towards zero (a mark of -1 is pixel 0)
    private static int MapPixel(int world) => C16.Div(C16.Int(world * MapPixelsPerTile), ChaseState.TileUnits);

    /// <summary>
    /// Draws a car sprite onto the off-screen page, centred on (<paramref name="x"/>, <paramref name="y"/>): 6 x 4
    /// pixels for an odd sprite number (heading right or left), 4 x 6 for an even one.
    /// </summary>
    /// <param name="x">X on the off-screen page.</param>
    /// <param name="y">Y on the off-screen page.</param>
    /// <param name="car">The car; car 0 has the other colour (when fleeing, the other way round).</param>
    /// <param name="sprite">The sprite number: the heading, plus the variant (<c>SpriteNormal</c>, blinkers ...).</param>
    // orig: CHASE 00df:2400 draw_car_sprite (src/c/chase/chase_00df_2400.c) matched; tests: ChaseOracleTests
    public void DrawCarSprite(int x, int y, int car, int sprite)
    {
        var otherColour = _state.ChaseMode == ChaseMode.Flee ? car == 0 : car != 0;
        if ((sprite & 1) != 0)
        {
            _world.Graphics.CopyRect(_state.RpSprite, sprite * SpriteCell + 1, otherColour ? SpriteRowAcrossOther : SpriteRowAcross,
                SpriteLong, SpriteShort, _state.RpBack, x - SpriteLong / 2, y - SpriteShort / 2);
        }
        else
        {
            _world.Graphics.CopyRect(_state.RpSprite, sprite * SpriteCell + 2, otherColour ? SpriteRowAlongOther : SpriteRowAlong,
                SpriteShort, SpriteLong, _state.RpBack, x - SpriteShort / 2, y - SpriteLong / 2);
        }
    }

    /// <summary>
    /// Draws the street view at (<paramref name="left"/>, <paramref name="top"/>) for the camera that follows car
    /// <paramref name="car"/>. The camera moves at most 4 units a step towards the car (60 x 50 units off the car,
    /// kept inside the city), and the whole view is drawn once per step until the camera has arrived.
    /// </summary>
    /// <remarks>
    /// Each step: when the camera has entered a new tile, the 5 x 5 tiles from there are drawn into the off-screen page
    /// (the ground picture of each map cell, and a kerb strip on each side whose bit is clear); then the camera's part
    /// of them is copied to the view's place on that page, the cars are put on it, a white frame round it, and the
    /// whole is copied to the screen. A car the player does not drive is drawn where it was last seen; a tail car the
    /// quarry is very suspicious of blinks.
    /// </remarks>
    /// <param name="car">The car the camera follows.</param>
    /// <param name="left">Left edge of the view, on the off-screen page and on the screen.</param>
    /// <param name="top">Top edge of the view.</param>
    // orig: CHASE 00df:2012 draw_street_view (src/c/chase/chase_00df_2012.c) matched; tests: ChaseOracleTests
    public void DrawStreetView(int car, int left, int top)
    {
        var followed = _state.Cars[car];
        // the camera's goal: the car 60 units from the left and 50 from the top, inside the city
        var goalX = GameLibrary.Clamp(C16.Int(followed.X - ViewWidth / 2), 0, CameraLimitX);
        var goalY = GameLibrary.Clamp(C16.Int(followed.Y - ViewHeight / 2), 0, CameraLimitY);
        if (_state.CamX == 0)
        {
            _state.CamX = (short)goalX;
            _state.CamY = (short)goalY;
        }
        do
        {
            _state.CamX = C16.Int(_state.CamX + GameLibrary.Clamp(C16.Int(goalX - _state.CamX), -CameraStep, CameraStep));
            _state.CamY = C16.Int(_state.CamY + GameLibrary.Clamp(C16.Int(goalY - _state.CamY), -CameraStep, CameraStep));
            var tileX = C16.Div(_state.CamX, ChaseState.TileUnits);
            var tileY = C16.Div(_state.CamY, ChaseState.TileUnits);
            if (tileX != _state.CamTileX || tileY != _state.CamTileY)
            {
                DrawViewTiles(tileX, tileY);
                _state.CamTileX = tileX;
                _state.CamTileY = tileY;
            }
            _world.Graphics.CopyRect(_state.RpBack, _state.CamX - tileX * ChaseState.TileUnits, _state.CamY - tileY * ChaseState.TileUnits,
                ViewWidth, ViewHeight, _state.RpBack, left, top);
            DrawViewCars(left, top);
            DrawViewFrame(left, top);
            _world.Graphics.CopyRect(_state.RpBack, left, top, ViewWidth, ViewHeight, _state.Rp1, left, top);
        }
        while (goalX != _state.CamX || goalY != _state.CamY);
    }

    /// <summary>The camera's right-most left edge: the city's width (768 units) less the view's.</summary>
    private const int CameraLimitX = ChaseState.MapTiles * ChaseState.TileUnits - ViewWidth;

    /// <summary>The camera's lowest top edge: the city's height less the view's.</summary>
    private const int CameraLimitY = ChaseState.MapTiles * ChaseState.TileUnits - ViewHeight;

    /// <summary>
    /// Gives every cell of the map a random ground picture and all four sides open: one random call per cell, column by
    /// column (the first loop of generate_map, chase_00df_2c51.c 63-66). With the option of CC-25 the picture is one of
    /// the sheet's 20, not of the first 16; the number of calls is the same.
    /// </summary>
    public void FillGround()
    {
        // DIVERGENCE: D-664 a player's option (cutContent.chaseGrounds): the draw is among the sheet's 20 pictures, not the first 16
        var pictures = _behaviour.CutContent.ChaseGrounds ? GroundPicturesInTheSheet : GroundPictures;
        for (var x = 0; x < ChaseState.MapTiles; x++)
        {
            for (var y = 0; y < ChaseState.MapTiles; y++)
            {
                var picture = GameLibrary.RandomBelow(_state.Random, pictures);
                var cell = ChaseState.CellIndex(x, y);
                // the high four bits hold 0..15; a picture 16..19 (the option only) is its number less 16 there, and the cell's flag in HighGround
                _state.MapCells[cell] = (byte)(((picture & 0xf) << 4) + AllSidesOpen);
                _state.HighGround[cell] = picture >= GroundPictures;
            }
        }
    }

    /// <summary>Draws the 5 x 5 tiles from tile (x, y) into the off-screen page: ground pictures and kerb strips.</summary>
    private void DrawViewTiles(int tileX, int tileY)
    {
        for (var column = 0; column < ViewTiles; column++)
        {
            for (var row = 0; row < ViewTiles; row++)
            {
                var cell = _state.MapCells[ChaseState.CellIndex(tileX + column, tileY + row)];
                // the ground picture: number cell >> 4, ten 32-pixel tiles to a row of chase.pic
                var sourceX = ((cell >> 4) + (_state.HighGround[ChaseState.CellIndex(tileX + column, tileY + row)] ? GroundPictures : 0)) * ChaseState.TileUnits;
                var sourceY = GroundTop;
                while (sourceX >= GroundRowWidth)
                {
                    sourceX -= GroundRowWidth;
                    sourceY += ChaseState.TileUnits;
                }
                int x = column * ChaseState.TileUnits, y = row * ChaseState.TileUnits;
                _world.Graphics.CopyRect(_state.RpSprite, sourceX, sourceY, ChaseState.TileUnits, ChaseState.TileUnits, _state.RpBack, x, y);
                for (var side = 0; side < 4; side++)
                {
                    // a clear side bit gets a kerb strip
                    if ((cell & 1 << side) == 0)
                    {
                        var across = _state.TileEdgeX[side];
                        var along = _state.TileEdgeY[side];
                        _world.Graphics.FillRect(_state.RpBack, x + across.Off, y + along.Off, across.Size, along.Size, Kerb);
                    }
                }
            }
        }
    }

    /// <summary>Puts the cars inside the street view on the off-screen page.</summary>
    private void DrawViewCars(int left, int top)
    {
        for (var c = 0; c < ChaseState.CarCount; c++)
        {
            var car = _state.Cars[c];
            // C: alert_limit * 2 in 16 bits
            if (c != 0 && car.Alert > C16.Int(_state.AlertLimit * 2) && (_state.Frame & 2) != 0)
            {
                continue;
            }
            // the car the player does not drive is shown where it was last seen
            var atMark = _state.ChaseMode == ChaseMode.Flee ? c > 0 : c == 0;
            int x, y, sprite;
            if (!atMark)
            {
                x = C16.Int(car.X - _state.CamX);
                y = C16.Int(car.Y - _state.CamY);
                sprite = car.Alert > _state.AlertLimit ? 0
                    : c == _state.CurCar && (_state.Frame & 7) == 0 ? SpriteSelected
                    : SpriteNormal;
                sprite += car.Dir;
                if ((_state.Frame & 4) != 0 && car.Turn != Directions.None)
                {
                    if (((car.Dir + 1) & 3) == car.Turn)
                    {
                        sprite += SpriteBlinkRight;
                    }
                    if (((car.Dir - 1) & 3) == car.Turn)
                    {
                        sprite += SpriteBlinkLeft;
                    }
                }
            }
            else
            {
                x = C16.Int(car.MarkX - _state.CamX);
                y = C16.Int(car.MarkY - _state.CamY);
                // the heading it had when seen
                sprite = (car.Flag ? 0 : SpriteNormal) + car.Turn;
            }
            if (x > ViewMargin && x < ViewWidth - ViewMargin && y > ViewMargin && y < ViewHeight - ViewMargin)
            {
                DrawCarSprite(x + left, y + top, c, sprite);
            }
        }
    }

    /// <summary>Draws the white frame round the street view, on the off-screen page (the screen port switched to it for the four lines).</summary>
    private void DrawViewFrame(int left, int top)
    {
        int right = left + ViewWidth - 1, bottom = top + ViewHeight - 1;
        _state.Rp1.PageNr = BackupPage;
        Output.DrawLine(left, top, right, top, White);
        Output.DrawLine(right, bottom, right, top, White);
        Output.DrawLine(right, bottom, left, bottom, White);
        Output.DrawLine(left, top, left, bottom, White);
        _state.Rp1.PageNr = 0;
    }
}
