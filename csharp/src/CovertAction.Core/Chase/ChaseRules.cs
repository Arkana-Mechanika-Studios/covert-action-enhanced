using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Chase;

/// <summary>
/// The rules of the car chase that need no screen and no keyboard: where a car may drive, whether the quarry can see
/// a car, where the cars start, whether a car stands at a building, the dashboard clock, and how the game is told to
/// end.
/// </summary>
public static class ChaseRules
{
    /// <summary>The last tile of a row or column of the city.</summary>
    private const int LastTile = ChaseState.MapTiles - 1;

    /// <summary>The side bit <see cref="CanMove"/> tests for a move between two horizontal neighbours.</summary>
    private const int HorizontalSideBit = 1;

    /// <summary>The side bit <see cref="CanMove"/> tests for a move between two vertical neighbours.</summary>
    private const int VerticalSideBit = 8;

    /// <summary>The quarry sees a car in line with it from less than this many units (six tiles) ...</summary>
    private const int SightRange = 0xc0;

    /// <summary>... and at night from at most this many units (two tiles).</summary>
    private const int NightSightRange = 0x40;

    /// <summary>A car stands at a building when its distance (x and y added) to the location is at most this.</summary>
    private const int EntryDistance = 6;

    /// <summary>The quarry tolerates suspicion up to 1000 / (difficulty + 3), before the alert level scales it.</summary>
    private const int AlertBase = 1000;

    private const int MinutesPerDay = 1440;
    private const int FramesPerMinute = 60;
    private const int MinutesPerHour = 60;

    /// <summary>
    /// Tells the launcher to end the whole game instead of going back to the campaign, when <paramref name="leave"/>
    /// is set (the player confirmed Alt-Q).
    /// </summary>
    /// <param name="shared">The hand-off block; its "next part" becomes -1.</param>
    /// <param name="leave">Whether to end the game.</param>
    // orig: CHASE 00df:044e set_leave_game (src/c/chase/chase_00df_044e.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    public static void SetLeaveGame(SharedState shared, bool leave)
    {
        if (leave)
        {
            shared.NextModule = -1;
        }
    }

    /// <summary>
    /// Takes over the campaign from the hand-off block when the chase starts: the campaign's scalar values into this
    /// part's variables, then this part's copies of six of the campaign's tables (the head record, the cast and its
    /// second records, the events, the cities and the organisations), in the order the block holds them.
    /// </summary>
    /// <remarks>
    /// Of all this the chase uses the city, the organisation, the options, the clock, the level, the skills, and the
    /// cities' known organisations and the organisations' short names; the rest is only kept, as in the original.
    /// The cast and the events are as long as the block says; the rest of this part's room for them stays as it was.
    /// </remarks>
    /// <param name="state">This part's variables.</param>
    /// <param name="shared">The hand-off block with its campaign tables.</param>
    /// <exception cref="MemoryOverrunException">More cast or event records than this part has room for: the original copies on over its next variables.</exception>
    // orig: CHASE 00df:04a4 import_shared_state (src/c/chase/chase_00df_04a4.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    // DIVERGENCE: D-400 the tables are copied record by record, not as bytes through read_shared_or_file and its cursor.
    public static void ImportSharedState(ChaseState state, SharedState shared)
    {
        state.InCity = shared.City;
        state.AtLoc = shared.Org;
        state.Status = shared.Flags;
        state.TheCrime = shared.CaseNo;
        state.TheOrg1 = shared.PlotOrg2;
        state.TheOrg2 = shared.PlotOrg3;
        state.TheCity1 = shared.PlotCity2;
        state.TheCity2 = shared.PlotCity3;
        state.Gtime = shared.Clock;
        state.Nperson = shared.NPersons;
        state.Nloc = shared.NLinks;
        state.ChronTime = shared.PlotStep;
        state.MacGuffin = shared.MacGuffin;
        state.Diff = shared.Level;
        state.TheSeed = shared.RandomSeed;
        state.NACTIVITY = shared.NCast;
        state.NEVENT = shared.NEvents;
        state.Sprung = shared.SelPerson;
        state.GMode = shared.GfxLetter;
        state.NextSyn = shared.NMsgs;
        state.Doubles = shared.CityMask;
        state.InterPol = shared.InterPol;
        state.Skills = shared.SkillBits;

        var campaign = shared.Campaign;
        state.Pl.CopyFrom(campaign.Plot);
        CopyRecords(campaign.Cast, state.Ac, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        CopyRecords(campaign.CastProgress, state.Ac2, state.NACTIVITY, (to, from) => to.CopyFrom(from));
        CopyRecords(campaign.Events, state.Ev, state.NEVENT, (to, from) => to.CopyFrom(from));
        CopyRecords(campaign.Cities, state.Ct, campaign.Cities.Length, (to, from) => to.CopyFrom(from));
        CopyRecords(campaign.Orgs, state.Org, campaign.Orgs.Length, (to, from) => to.CopyFrom(from));
    }

    /// <summary>Copies the first <paramref name="count"/> records of a campaign table into this part's table.</summary>
    private static void CopyRecords<T>(IReadOnlyList<T> source, T[] target, int count, Action<T, T> copy)
    {
        if (count > target.Length)
        {
            throw new MemoryOverrunException($"{count} records into a table of {target.Length}");
        }
        for (var i = 0; i < count; i++)
        {
            copy(target[i], source[i]);
        }
    }

    /// <summary>
    /// Whether a car standing on tile (<paramref name="tx"/>, <paramref name="ty"/>) may drive on in direction
    /// <paramref name="dir"/>.
    /// </summary>
    /// <remarks>
    /// On the outer ring of the city a car may only drive inwards (on the left column only right, on the top row only
    /// down, ...), so the ring cannot be driven along. Elsewhere one side bit of a map cell decides: bit 0 of the left
    /// cell for a move between two horizontal neighbours, bit 3 of the upper cell for a move between two vertical
    /// ones. The map generator and the street view number the sides differently (0 top, 1 right, 2 bottom, 3 left),
    /// so these are not the bits of the sides the cars cross.
    /// </remarks>
    /// <param name="state">The map.</param>
    /// <param name="tx">Tile column 0..23.</param>
    /// <param name="ty">Tile row 0..23.</param>
    /// <param name="dir">The direction (<see cref="Directions"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">A direction other than 0..3 inside the ring: the original then returns whatever its AX holds.</exception>
    // orig: CHASE 00df:1ede can_move (src/c/chase/chase_00df_1ede.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    // BUG: CA-3 the bits tested are not the ones the map generator clears for the sides crossed; reproduced as coded.
    public static bool CanMove(ChaseState state, int tx, int ty, int dir)
    {
        if (tx == 0 && dir != Directions.Right)
        {
            return false;
        }
        if (ty == 0 && dir != Directions.Down)
        {
            return false;
        }
        if (tx == LastTile && dir != Directions.Left)
        {
            return false;
        }
        if (ty == LastTile && dir != Directions.Up)
        {
            return false;
        }
        return dir switch
        {
            Directions.Up => SideOpen(state, tx, ty - 1, VerticalSideBit),
            Directions.Right => SideOpen(state, tx, ty, HorizontalSideBit),
            Directions.Down => SideOpen(state, tx, ty, VerticalSideBit),
            Directions.Left => SideOpen(state, tx - 1, ty, HorizontalSideBit),
            // DORMANT: every caller passes a direction 0..3; the original's switch would fall out without a result
            _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, "can_move of a direction other than 0..3 returns no value"),
        };
    }

    private static bool SideOpen(ChaseState state, int tx, int ty, int bit) =>
        (state.MapCells[ChaseState.CellIndex(tx, ty)] & bit) != 0;

    /// <summary>
    /// Whether the quarry (car 0) can see car <paramref name="car"/>, and from how far: the two must stand in one
    /// column (on a tile's vertical centre line) or one row (on a horizontal one), less than six tiles apart (at night
    /// two), with every tile between them open in the direction from the car to the quarry.
    /// </summary>
    /// <remarks>
    /// Also sets <see cref="ChaseState.SightFlag"/> whenever the two are in line, to whether they stand apart at all
    /// (it is set even when a closed tile blocks the view). The walk starts at the tile the car's leading edge is on,
    /// and goes one more tile than the distance has whole tiles.
    /// </remarks>
    /// <param name="state">The cars and the map.</param>
    /// <param name="car">The car's number, 0..2 (0 compares the quarry with itself).</param>
    /// <returns>The distance in world units, 0 when the quarry cannot see the car.</returns>
    // orig: CHASE 00df:1ceb sight_distance (src/c/chase/chase_00df_1ceb.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    public static int SightDistance(ChaseState state, int car)
    {
        var watcher = state.Cars[car];
        var quarry = state.Cars[0];
        // C: 16-bit differences and abs (abs of -32768 stays negative)
        var dx = C16.Int(quarry.X - watcher.X);
        var dy = C16.Int(quarry.Y - watcher.Y);
        var distanceX = C16.Abs(dx);
        var distanceY = C16.Abs(dy);
        if (state.Night && (distanceX > NightSightRange || distanceY > NightSightRange))
        {
            return 0;
        }

        // the tile of the car's leading edge: a car heading left or up looks from the far side of its tile
        var tileX = C16.Div(C16.Int((watcher.Dir == Directions.Left ? ChaseState.TileUnits - 1 : 0) + watcher.X), ChaseState.TileUnits);
        var tileY = C16.Div(C16.Int((watcher.Dir == Directions.Up ? ChaseState.TileUnits - 1 : 0) + watcher.Y), ChaseState.TileUnits);

        if (distanceX == 0 && distanceY < SightRange && watcher.X % ChaseState.TileUnits == 0)
        {
            var towards = dy > 0 ? Directions.Down : Directions.Up;
            var open = WalkIsOpen(state, tileX, tileY, 0, GameLibrary.Sign(dy), towards, distanceY);
            state.SightFlag = dy != 0;
            return open ? distanceY : 0;
        }
        if (distanceY == 0 && distanceX < SightRange && watcher.Y % ChaseState.TileUnits == 0)
        {
            var towards = dx > 0 ? Directions.Right : Directions.Left;
            var open = WalkIsOpen(state, tileX, tileY, GameLibrary.Sign(dx), 0, towards, distanceX);
            state.SightFlag = dx != 0;
            return open ? distanceX : 0;
        }
        return 0;
    }

    /// <summary>
    /// Walks from tile (x, y) one tile at a time, for the whole tiles of <paramref name="distance"/> plus one, and
    /// tells whether every tile on the way may be left in direction <paramref name="towards"/>.
    /// </summary>
    private static bool WalkIsOpen(ChaseState state, int x, int y, int stepX, int stepY, int towards, short distance)
    {
        // C: distance / 32 of a 16-bit value (a distance of -32768 gives -1024 and no step at all)
        for (var tile = 0; C16.Div(distance, ChaseState.TileUnits) >= tile; tile++)
        {
            if (!CanMove(state, x, y, towards))
            {
                return false;
            }
            x += stepX;
            y += stepY;
        }
        return true;
    }

    /// <summary>
    /// Sets up the three cars at the start of a chase: the quarry's suspicion limit, the quarry on its start location
    /// heading up or down (towards the destination's row), the two tail cars 8 and 16 units behind it, and the
    /// destination point. When the player flees, the keys steer car 0.
    /// </summary>
    /// <remarks>
    /// The limit is 1000 / (difficulty + 3), then scaled by the alert level as 10 * limit / (alert + 100) * 10 in whole
    /// numbers: 400 at difficulty 0 and alert 0. The quarry starts in gear 2 with its mark on itself; the tail cars stand
    /// still with the auto-follow lamp lit and no mark. Car 0's flag and the tail cars' attributes are left as they are.
    /// </remarks>
    /// <param name="state">The chase: <see cref="ChaseState.Diff"/>, <see cref="ChaseState.AlertLevel"/> and the locations must be set.</param>
    // orig: CHASE 00df:0708 init_cars (src/c/chase/chase_00df_0708.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    public static void InitCars(ChaseState state)
    {
        // C: 16-bit divisions; difficulty -3 or alert level -100 would stop the original with a divide error
        state.AlertLimit = C16.Div(AlertBase, C16.Int(state.Diff + 3));
        state.AlertLimit = C16.Int(C16.Div(C16.Int(10 * state.AlertLimit), C16.Int(state.AlertLevel + 100)) * 10);
        state.Outcome = ChaseOutcome.Lost;

        var quarry = state.Cars[0];
        quarry.X = state.LocX[state.StartLoc];
        quarry.Y = state.LocY[state.StartLoc];
        quarry.Dir = (short)(state.LocY[state.DestLoc] < quarry.Y ? Directions.Up : Directions.Down);
        quarry.MarkX = quarry.X;
        quarry.MarkY = quarry.Y;
        quarry.Turn = Directions.None;
        quarry.Alert = 0;
        quarry.Attr = 0;
        quarry.Gear = 2;

        // C: (dir_dx << 5) / 4 in 16 bits: a quarter of a tile against the heading
        var stepX = C16.Div(C16.Int(state.DirDx[quarry.Dir] * ChaseState.TileUnits), 4);
        var stepY = C16.Div(C16.Int(state.DirDy[quarry.Dir] * ChaseState.TileUnits), 4);
        for (var i = 1; i < ChaseState.CarCount; i++)
        {
            var tail = state.Cars[i];
            tail.X = C16.Int(quarry.X - stepX * i);
            tail.Y = C16.Int(quarry.Y - stepY * i);
            tail.Dir = quarry.Dir;
            tail.Flag = true;
            tail.MarkX = -1;
            tail.Turn = Directions.None;
            tail.Alert = 0;
            tail.Gear = 0;
        }

        state.DestX = state.LocX[state.DestLoc];
        state.DestY = state.LocY[state.DestLoc];
        if (state.ChaseMode == ChaseMode.Flee)
        {
            state.CurCar = 0;
        }
    }

    /// <summary>
    /// Whether a car at (<paramref name="x"/>, <paramref name="y"/>) stands at one of the locations the player knows in
    /// this city: the first of locations 1..15 within 6 units (x and y distance added). In a campaign that location
    /// becomes the organisation the player is at, and the hand-off's "organisation reached".
    /// </summary>
    /// <remarks>
    /// An unknown location is not measured, but the test still runs, on the distance of the last known location
    /// before it (docs/original-bugs.md CA-4): an unknown location right after a near known one counts as reached
    /// in its place.
    /// </remarks>
    /// <param name="state">The locations and the city's known organisations; <see cref="ChaseState.AtLoc"/> is set on success.</param>
    /// <param name="shared">The hand-off block; its <see cref="SharedState.OrgOut"/> is set on success.</param>
    /// <param name="x">World x of the car.</param>
    /// <param name="y">World y of the car.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce; the original's own when left out.</param>
    /// <returns>Whether a location was reached.</returns>
    /// <exception cref="UninitialisedReadException">Location 1 is unknown: the original then tests a distance it never computed (not with <see cref="OriginalBehaviour.FixChaseEntryAtUnknownLocation"/>).</exception>
    // orig: CHASE 00df:3190 check_location_entry (src/c/chase/chase_00df_3190.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    // BUG: CA-4 the distance of an unknown location is the previous location's (for location 1, stack garbage). OriginalBehaviour.FixChaseEntryAtUnknownLocation
    public static bool CheckLocationEntry(ChaseState state, SharedState shared, int x, int y, OriginalBehaviour? behaviour = null)
    {
        int? distance = null;
        for (var location = 1; location < ChaseState.Locations; location++)
        {
            if ((state.Ct[state.InCity].OrgClues & 1 << location) != 0)
            {
                distance = C16.Int(C16.Abs(C16.Int(x - state.LocX[location])) + C16.Abs(C16.Int(y - state.LocY[location])));
            }
            else if (behaviour?.FixChaseEntryAtUnknownLocation == true)
            {
                // fixed: a location the player does not know is not reached
                continue;
            }
            if ((distance ?? throw new UninitialisedReadException("check_location_entry's distance, location 1 being unknown")) <= EntryDistance)
            {
                if (state.CampaignMode)
                {
                    // C: AtLoc = SHARED->org_out = b: the location number goes through the hand-off's byte
                    shared.OrgOut = C16.Char(location);
                    state.AtLoc = shared.OrgOut;
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Writes the dashboard clock into its template: the time of day (the campaign's clock plus a minute per 60
    /// frames, round the day) as the last digit of the hour, two digits of minutes, and the frames of the current minute
    /// as two digits of "seconds".
    /// </summary>
    /// <remarks>
    /// Only the last digit of the hour is written: the template's first character. A negative clock gives characters
    /// below '0', as in the original.
    /// </remarks>
    /// <param name="state">The clock and the template.</param>
    /// <param name="frames">Frames since the chase began.</param>
    /// <returns>The clock text.</returns>
    // orig: CHASE 00df:2bd0 format_game_time (src/c/chase/chase_00df_2bd0.c) matched; tests: ChaseRulesTests, ChaseOracleTests
    // Other code under the same name: BUG 00e8:50e5, FINAL 0100:2c94, GAME 00f7:95ab (not this method; docs/source-map/shared-functions.csv).
    public static string FormatGameTime(ChaseState state, int frames)
    {
        // C: 16-bit divisions and remainders
        var frameCount = C16.Int(frames);
        var minuteOfDay = C16.Rem(C16.Int(C16.Div(frameCount, FramesPerMinute) + state.Gtime), MinutesPerDay);
        var hour = C16.Div(minuteOfDay, MinutesPerHour);
        var minute = C16.Rem(minuteOfDay, MinutesPerHour);
        var second = C16.Rem(frameCount, FramesPerMinute);
        var text = state.ClockText;
        text[0] = Digit(C16.Rem(hour, 10));
        text[2] = Digit(C16.Div(minute, 10));
        text[3] = Digit(C16.Rem(minute, 10));
        text[5] = Digit(C16.Div(second, 10));
        text[6] = Digit(C16.Rem(second, 10));
        return new string(text);
    }

    /// <summary>A digit as a character; the original adds '0' and keeps the low byte.</summary>
    private static char Digit(int value) => (char)C16.UChar(value + '0');

    /// <summary>An empty routine the original calls just before it builds the map.</summary>
    // orig: CHASE 00df:2c50 map_stub (src/c/chase/chase_00df_2c50.c) matched; tests: ChaseOracleTests
    public static void MapStub()
    {
    }

    /// <summary>An empty routine the original calls whenever a car reaches a tile centre.</summary>
    // orig: CHASE 00df:248c on_tile_centre (src/c/chase/chase_00df_248c.c) matched; tests: ChaseOracleTests
    public static void OnTileCentre()
    {
    }
}
