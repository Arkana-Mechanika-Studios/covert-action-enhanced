using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Presentation;
using CovertAction.Presentation.Raid;

namespace CovertAction.Launcher.Tests;

/// <summary>A grenade's flight over the raid's cells (TAC 00e8:26b0 update_projectiles).</summary>
public sealed class RaidGrenadeTests
{
    /// <summary>A font of one character: the drawing code needs one, the flight draws no text.</summary>
    private static readonly byte[] FontFile =
    [
        1, 0, 14, 0, 1, 1, (byte)'A', (byte)'A', 1, 0, 1, 1, 1, 0, 0b1000_0000, 0, 0, 0,
    ];

    /// <summary>
    /// Bare floor is the cell value 0xff, whose open bit (0x80) is set: the flight's test for an open door then reads
    /// the bits of piece 63, one past the table (in the original the word behind it, the first of Move8X, which has no
    /// door bit). Found by the session proof (TAC/stasi-arrest): a grenade thrown over bare floor stopped the rewrite
    /// with an index out of range. It flies on, one step a frame.
    /// </summary>
    [Fact]
    public void AGrenadeFliesOnOverBareFloor()
    {
        var state = new RaidState { InRoom = 1 };
        Array.Fill(state.RoomAtCell, (byte)1);
        Array.Fill(state.CellMap, RaidState.CellEmpty);
        state.MoveX[3] = 2;
        var grenade = state.Gr[0];
        grenade.Type = Grenade.Fragmentation;
        grenade.Time = 5;
        grenade.Dir = 3;
        grenade.Px = 100;
        grenade.Py = 100;
        var graphics = new McgaGraphicsDriver(new FontSet(FontFile), new byte[3 * McgaDriverFile.Colours]);
        var world = new Boundaries(graphics, null!, null!, null!, null!, null!);

        new BuildingRaid(state, new SharedState(), world, OriginalBehaviour.Faithful).UpdateProjectiles(0);

        Assert.Equal(102, grenade.Px);
        Assert.Equal(4, grenade.Time);
    }
}
