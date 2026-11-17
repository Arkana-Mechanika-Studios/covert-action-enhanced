using System.Security.Cryptography;
using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Persistence;
using CovertAction.Persistence.Dgroup;
using Xunit.Abstractions;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// Kitchens and bedrooms (docs/cut-content.md CC-26, docs/original-bugs.md CA-8; DIVERGENCES.md D-665), a player option:
/// the room-type choice accepts Bed Room (6) and Kitchen (8). With the option off the generator is the original's (the layouts
/// below are hashed against the code before the option existed); with it on they come in the medium rooms, with their
/// furniture but never the Drawers, and the one-room ambush does not change. Run on the game's own TAC.EXE tile and room
/// tables (skipped without the game).
/// </summary>
public sealed class CutContentHomeRoomsTests(ITestOutputHelper output)
{
    private static readonly OriginalBehaviour Off = OriginalBehaviour.ForPlayer(false, false);

    private static readonly OriginalBehaviour On = OriginalBehaviour.ForPlayer(false, false, new CutContentOptions(HomeRooms: true));

    private static readonly OriginalBehaviour OnFixed = OriginalBehaviour.ForPlayer(true, false, new CutContentOptions(HomeRooms: true));

    private static readonly OriginalBehaviour OffFixed = OriginalBehaviour.ForPlayer(true, false);

    private const int Buildings = 300;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "FONTS.CV")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>
    /// A building as the generator makes it (BuildingRaid.GenerateBuilding without the raid around it): plans are drawn until
    /// one joins its rooms, and every room is furnished. The one-room ambush takes its single room as it comes.
    /// </summary>
    private static RaidState Building(GameFolder game, OriginalBehaviour? behaviour, int seed, bool ambush = false)
    {
        var state = new RaidState();
        TacDgroup.Layout.Load(Executable.Read(game.Read("TAC.EXE")).DataSegment(), state);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        state.Random.Srand(seed);
        state.Who = -1;
        if (ambush)
        {
            state.GameMode = RaidState.OneRoomMode;
            state.Nroom = 0;
            BuildingRules.InitBuildingGrid(state);
            BuildingRules.LabelRooms(state);
            state.MacGuff = 1;
            state.Guard = 1;
        }
        else
        {
            do
            {
                state.Nroom = 0;
                BuildingRules.InitBuildingGrid(state);
                BuildingRules.AddWallSegments(state);
            }
            while (!BuildingRules.LabelRooms(state) || !BuildingRules.ConnectRooms(state, behaviour));
        }
        for (var room = 1; room <= state.Nroom; room++)
        {
            BuildingRules.FurnishRoom(state, room, behaviour);
        }
        return state;
    }

    private static void Add(IncrementalHash hash, RaidState state)
    {
        hash.AppendData(state.CellMap);
        var types = new byte[state.Nroom + 1];
        for (var room = 1; room <= state.Nroom; room++)
        {
            types[room] = (byte)state.Rm[room].Type;
        }
        hash.AppendData(types);
    }

    private static string LayoutsHash(GameFolder game, OriginalBehaviour? behaviour, bool ambush)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var seed = 1; seed <= Buildings; seed++)
        {
            Add(hash, Building(game, behaviour, seed, ambush));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // hashed with the code as it was before the option (the commit that precedes D-665)
    private const string OriginalBuildings = "31FA2F7445383589D9A9552E7ECEF8A10EF831D485ABB3E4E4C0778D0B9553B0";

    private const string OriginalBuildingsFixed = "32C2C3C0299066B9C0DF972328149A0E21DC46655F81D32392B1B7D4BF967DFF";

    private const string OriginalAmbushes = "C1A2CAC95AEB014621A6B21FD3553D6859168BB76B8B1ADC42CCA59324818EC1";

    [SkippableFact]
    public void WithTheOptionOffTheBuildingsAreTheOriginalsToTheLastCell()
    {
        var game = GameFiles();
        Assert.Equal(OriginalBuildings, LayoutsHash(game, Off, false));
        Assert.Equal(OriginalBuildings, LayoutsHash(game, null, false));
        Assert.Equal(OriginalBuildingsFixed, LayoutsHash(game, OffFixed, false));
        Assert.Equal(OriginalAmbushes, LayoutsHash(game, Off, true));
    }

    [SkippableFact]
    public void WithTheOptionOnTheOneRoomAmbushIsTheOriginalsToTheLastCell()
    {
        var game = GameFiles();
        Assert.Equal(OriginalAmbushes, LayoutsHash(game, On, true));
        Assert.Equal(LayoutsHash(game, OffFixed, true), LayoutsHash(game, OnFixed, true));
    }

    [SkippableFact]
    public void WithTheOptionOnBedRoomsAndKitchensComeInMediumRoomsWithTheirFurnitureAndNeverTheDrawers()
    {
        var game = GameFiles();
        var counts = new int[RaidState.RoomTypes];
        var tiles = new int[64];
        var offTypes = new int[RaidState.RoomTypes];
        for (var seed = 1; seed <= Buildings; seed++)
        {
            var off = Building(game, Off, seed);
            for (var room = 1; room <= off.Nroom; room++)
            {
                offTypes[off.Rm[room].Type]++;
            }
            var state = Building(game, On, seed);
            for (var room = 1; room <= state.Nroom; room++)
            {
                var record = state.Rm[room];
                if ((record.Status & RoomStatus.Removed) != 0)
                {
                    continue;
                }
                counts[record.Type]++;
                var area = record.Dx * record.Dy;
                if (record.Type is 6 or 8)
                {
                    Assert.InRange(area, 41, 72);
                }
                for (var y = record.Y; y < record.Y + record.Dy; y++)
                {
                    for (var x = record.X; x < record.X + record.Dx; x++)
                    {
                        var tile = state.CellMap[RaidState.Cell(x, y)];
                        if (tile is >= 12 and < 62)
                        {
                            tiles[tile]++;
                            // the bed only in a Bed Room, the range and the refrigerator only in a Kitchen
                            if (tile is 46 or 47)
                            {
                                Assert.Equal(6, record.Type);
                            }
                            if (tile is >= 48 and <= 51)
                            {
                                Assert.Equal(8, record.Type);
                            }
                        }
                    }
                }
            }
            for (var cell = 0; cell < RaidState.Cells; cell++)
            {
                Assert.DoesNotContain(state.CellMap[cell], new byte[] { 57, 59 });
            }
        }
        output.WriteLine("types with the option off: " + string.Join(" ", offTypes));
        output.WriteLine("types with the option on:  " + string.Join(" ", counts));
        output.WriteLine($"tiles with the option on: bed 46/47 {tiles[46]}/{tiles[47]}, range 48/49 {tiles[48]}/{tiles[49]}, refrigerator 50/51 {tiles[50]}/{tiles[51]}, television 53/55 {tiles[53]}/{tiles[55]}, drawers 57/59 {tiles[57]}/{tiles[59]}");
        Assert.Equal(0, offTypes[6] + offTypes[8]);
        Assert.True(counts[6] > 0 && counts[8] > 0, "a bed room and a kitchen in 300 buildings");
        Assert.True(tiles[46] + tiles[47] > 0 && tiles[48] + tiles[49] > 0 && tiles[50] + tiles[51] > 0 && tiles[53] > 0);
        Assert.Equal(0, tiles[55]);
    }

    /// <summary>The Drawers are left out only by the option: with it off nothing differs (they are allowed in a Bed Room only, which never comes).</summary>
    [SkippableFact]
    public void TheDrawersAreUnplacedWithTheOptionOffToo()
    {
        var game = GameFiles();
        for (var seed = 1; seed <= 100; seed++)
        {
            var state = Building(game, Off, seed);
            Assert.DoesNotContain(57, state.CellMap.Select(c => (int)c));
            Assert.DoesNotContain(59, state.CellMap.Select(c => (int)c));
        }
    }

    /// <summary>Whether a cell can be walked: bare floor, a piece that is not solid, or a door (it opens).</summary>
    private static bool Passable(RaidState state, int x, int y)
    {
        if (x < 0 || y < 0 || x >= RaidState.MapW || y >= RaidState.MapH)
        {
            return false;
        }
        var cell = state.CellMap[RaidState.Cell(x, y)];
        var tile = cell & RaidState.CellTile;
        return tile == RaidState.CellTile || (state.Ob[tile].Bits & (TileFlags.Solid | TileFlags.Door)) != TileFlags.Solid;
    }

    /// <summary>Rooms with no walkable cell reachable from the first room's, and walkable room cells that cannot be reached, over the buildings.</summary>
    private (int Rooms, int Cells, int Buildings, int Total) Probe(GameFolder game, OriginalBehaviour behaviour)
    {
        int rooms = 0, cells = 0, buildings = 0, total = 0;
        for (var seed = 1; seed <= Buildings; seed++)
        {
            var state = Building(game, behaviour, seed);
            var reached = new bool[RaidState.Cells];
            var queue = new Queue<(int X, int Y)>();
            var first = state.Rm.Skip(1).Take(state.Nroom).First(r => (r.Status & RoomStatus.Removed) == 0);
            // the start: a walkable cell of the first room
            (int X, int Y)? start = null;
            for (var y = first.Y; start is null && y < first.Y + first.Dy; y++)
            {
                for (var x = first.X; start is null && x < first.X + first.Dx; x++)
                {
                    if (Passable(state, x, y))
                    {
                        start = (x, y);
                    }
                }
            }
            queue.Enqueue(start!.Value);
            reached[RaidState.Cell(start.Value.X, start.Value.Y)] = true;
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (var side = 0; side < 4; side++)
                {
                    int nx = cx + state.Mve4X[side], ny = cy + state.Mve4Y[side];
                    if (Passable(state, nx, ny) && !reached[RaidState.Cell(nx, ny)])
                    {
                        reached[RaidState.Cell(nx, ny)] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            var bad = false;
            for (var room = 1; room <= state.Nroom; room++)
            {
                var record = state.Rm[room];
                if ((record.Status & RoomStatus.Removed) != 0)
                {
                    continue;
                }
                total++;
                var any = false;
                for (var y = record.Y; y < record.Y + record.Dy; y++)
                {
                    for (var x = record.X; x < record.X + record.Dx; x++)
                    {
                        if (Passable(state, x, y))
                        {
                            if (reached[RaidState.Cell(x, y)])
                            {
                                any = true;
                            }
                            else
                            {
                                cells++;
                            }
                        }
                    }
                }
                if (!any)
                {
                    rooms++;
                    bad = true;
                }
            }
            buildings += bad ? 1 : 0;
        }
        return (rooms, cells, buildings, total);
    }

    [SkippableFact]
    public void ProbeEveryRoomStaysReachableAndGenerationTerminates()
    {
        var game = GameFiles();
        foreach (var (name, behaviour) in new[] { ("off", Off), ("off + fix CA-59", OffFixed), ("on", On), ("on + fix CA-59", OnFixed) })
        {
            var (rooms, cells, buildings, total) = Probe(game, behaviour);
            output.WriteLine($"{name}: {Buildings} buildings, {total} rooms; rooms cut off: {rooms} (in {buildings} buildings); walkable room cells cut off: {cells}");
            // the original's own generator may leave rooms cut off already; the option must not make it worse than the fix leaves it
            Assert.Equal(0, rooms);
        }
    }
}
