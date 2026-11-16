using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Raid;

/// <summary>
/// The building raid (the original's TAC program): the functions that reach a boundary. The rules without a screen are
/// in <see cref="BuildingRules"/>, <see cref="GuardRules"/>, and the library's <see cref="Core.Shared.LibraryTexts"/>.
/// </summary>
/// <param name="state">The raid's state.</param>
/// <param name="shared">The hand-off block.</param>
/// <param name="world">The boundaries.</param>
/// <param name="behaviour">Which of the original's bugs are fixed (CA-1).</param>
public sealed partial class BuildingRaid(RaidState state, SharedState shared, Boundaries world, OriginalBehaviour behaviour)
{
    /// <summary>The library's drawing routines, on this part's screen.</summary>
    public LibraryOutput Output { get; } = new(state, world);

    /// <summary>The library's keyboard and joystick routines, on this part's state.</summary>
    public LibraryInput Input { get; } = new(state, world);

    private LibraryMenus? _menus;
    private LibraryText? _text;
    private LibraryScreens? _screens;

    /// <summary>The library's routines that read, measure and draw texts, on this part's state.</summary>
    public LibraryText Text => _text ??= new LibraryText(state, state, world, Output, state.Random, behaviour);

    /// <summary>
    /// The library's screens of the campaign (faces, photos, the wait for a key), with the raid's own key reader behind
    /// the wait (TAC's <c>wait_key</c> is the library's <c>wait_for_key</c>; its <c>read_key</c> is other code).
    /// </summary>
    public LibraryScreens Screens => _screens ??= new LibraryScreens(state, state, world, Output, Input, Text, ReadKey);

    /// <summary>The library's message box and menus, with the raid's own sound routine and key reader behind them.</summary>
    public LibraryMenus Menus => _menus ??= new LibraryMenus(state, shared, world, Output, Input, DoSound, ReadKey);

    /// <summary>The radar (the building's small map) draws a pixel per 16 / 3 pixels of the building ...</summary>
    private const int RadarScale = 3;

    /// <summary>... from this x of the screen ...</summary>
    private const int RadarLeft = 0xb4;

    /// <summary>... and this y.</summary>
    private const int RadarTop = 0x64;

    /// <summary>The knocked-out time that marks a guard dead.</summary>
    private const short Dead = 0x270f;

    /// <summary>Plots one pixel of the screen (a line from the point to itself).</summary>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    /// <param name="color">Palette index.</param>
    // orig: TAC 00e8:2c73 plot_point (src/c/tac/tac_00e8_2c73.c) matched; tests: TacOracleTests
    public void PlotPoint(int x, int y, int color) => Output.DrawLine(x, y, x, y, color);

    /// <summary>
    /// A guard dies: his dot on the radar is wiped (copied back from the backdrop), the body is counted, his room is
    /// marked as one with a body (the next guard there raises the alarm), the raid's violence grows by one and his
    /// knocked-out time becomes 9999, which marks him dead.
    /// </summary>
    /// <param name="guard">The guard's slot, 1..8.</param>
    // orig: TAC 00e8:2c8d mark_guard_dead (src/c/tac/tac_00e8_2c8d.c) matched; tests: TacOracleTests
    public void MarkGuardDead(int guard)
    {
        var victim = state.Bg[guard];
        // C: 16-bit products and divisions
        var x = C16.Int(C16.Div(C16.Int(RadarScale * victim.Px), 16) + RadarLeft);
        var y = C16.Int(C16.Div(C16.Int(RadarScale * victim.Py), 16) + RadarTop);
        world.Graphics.CopyRect(state.RpBack, x, y, 2, 2, state.Rp1, x, y);
        state.BodiesHere++;
        state.Rm[victim.Room].Status |= RoomStatus.Bodies;
        state.Alarm++;
        victim.Stun = Dead;
    }

    /// <summary>Rooms a plan must keep after the pruning, or it is drawn again.</summary>
    private const int MinimumRooms = 0xc;

    /// <summary>
    /// Generates the building: draws plans (outer wall, inner walls, rooms, doors) until one joins all its rooms and
    /// keeps at least 12 after the unusable ones are dropped (the one-room ambush takes its single room as it comes, as
    /// both the target and the guards' home room), furnishes every room, makes the map's border cells bare floor except
    /// for doors, and then starts the random numbers from the clock.
    /// </summary>
    /// <remarks>
    /// The building comes from the random numbers as they stand when the raid begins (the caller seeds them from the
    /// building's city and organisation, so the same building looks the same each time); the clock seeds the rest of
    /// the raid.
    /// </remarks>
    // orig: TAC 00e8:44e4 generate_building (src/c/tac/tac_00e8_44e4.c) matched; tests: TacOracleTests
    public void GenerateBuilding()
    {
        do
        {
            foreach (var room in state.Rm)
            {
                room.Status = RoomStatus.None;
            }
            state.Nroom = 0;
            if (state.GameMode == RaidState.OneRoomMode)
            {
                BuildingRules.InitBuildingGrid(state);
                BuildingRules.LabelRooms(state);
                state.MacGuff = 1;
                state.Guard = 1;
                break;
            }
            do
            {
                BuildingRules.InitBuildingGrid(state);
                // DORMANT: the original tests the mode again here; the one-room mode left the loop above
                if (state.GameMode != RaidState.OneRoomMode)
                {
                    BuildingRules.AddWallSegments(state);
                }
            }
            while (!BuildingRules.LabelRooms(state) || !BuildingRules.ConnectRooms(state, behaviour));
        }
        while (state.Nroom - BuildingRules.PruneRooms(state) < MinimumRooms);

        for (var room = 1; room <= state.Nroom; room++)
        {
            BuildingRules.FurnishRoom(state, room);
        }
        // C: the loops stop one short of the last column and row: the corner cell (39, 31) keeps its tile
        for (var x = 0; x < RaidState.MapW - 1; x++)
        {
            ClearBorderCell(x, 0);
            ClearBorderCell(x, RaidState.MapH - 1);
        }
        for (var y = 0; y < RaidState.MapH - 1; y++)
        {
            ClearBorderCell(0, y);
            ClearBorderCell(RaidState.MapW - 1, y);
        }
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, world.Timer);
    }

    /// <summary>A border cell becomes bare floor unless it holds a door tile (generate_building).</summary>
    private void ClearBorderCell(int x, int y)
    {
        var cell = RaidState.Cell(x, y);
        if ((state.CellMap[cell] & 0xfc) != RaidState.TileDoor)
        {
            state.CellMap[cell] = RaidState.CellEmpty;
        }
    }
}
