using CovertAction.Core.Campaign;
using CovertAction.Persistence.Dgroup;

namespace CovertAction.Persistence.Tests.Game;

/// <summary>The layout of GAME's data segment on made-up bytes: offsets as in the reconstructed game.h, and a byte-identical round trip.</summary>
public sealed class GameDgroupTests
{
    private static void SetWord(byte[] image, int offset, int value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
    }

    private static void SetText(byte[] image, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            image[offset + i] = (byte)text[i];
        }
        image[offset + text.Length] = 0;
    }

    [Fact]
    public void TheDataSegmentRoundTripsByteForByte()
    {
        var image = new byte[0x10000];
        new Random(1990).NextBytes(image);
        // every second byte 0, so that every text, even of two bytes, has its end mark
        for (var i = 0; i < image.Length; i += 2)
        {
            image[i] = 0;
        }
        // the port pointers hold the addresses of their records
        SetWord(image, 0x1468, 0x1456);
        SetWord(image, 0x147c, 0x146a);
        SetWord(image, 0x436e, 0x435c);
        // the animation record of the message box names its file (the library's variables: LibraryLayout)
        SetWord(image, 0x574a, 0x433c);
        // the animation player's port pointers (PanEngineLayout)
        SetWord(image, 0x426c, 0x4248);
        SetWord(image, 0x426e, 0x425a);
        SetWord(image, 0x42a6, 0x4270);
        SetWord(image, 0x42a8, 0x4282);
        SetWord(image, 0x42aa, 0x4294);
        var state = new CampaignState();
        GameDgroup.Layout.Load(image, state);
        var stored = (byte[])image.Clone();
        GameDgroup.Layout.Store(state, stored);
        Assert.Equal(image, stored);
    }

    [Fact]
    public void TheTablesLieWhereTheHeaderSays()
    {
        var image = new byte[0x10000];
        SetWord(image, 0x0094 + 16 * 63 + 0x0e, -3);         // ps[63].alert
        SetWord(image, 0x0494 + 12 * 255 + 0x06, 40);        // lc[255].tapped
        SetWord(image, 0x1094 + 12 * 79 + 0x02, 7);          // cl[79].act
        SetWord(image, 0x83c6 + 44 * 63 + 0x02, 999);        // ev[63].chron
        SetWord(image, 0x97a6 + 48 * 31 + 0x28, 0x1f);       // ac[31].clued
        SetWord(image, 0xa55e + 6 * 31 + 0x02, 12);          // ac2[31].intel
        SetWord(image, 0x760a + 0x16, 5);                    // pl.ncase, the clock's month
        SetWord(image, 0xa648 + 2 * 15, 9);                  // org_counters[15]
        SetWord(image, 0x2bbe + 2 * 11, 0x2ab4);             // month_names[11] points at its text
        SetText(image, 0x2ab4, "Dez");
        var state = new CampaignState();
        GameDgroup.Layout.Load(image, state);
        Assert.Equal(-3, state.Ps[63].Alert);
        Assert.Equal(40, state.Lc[255].Tapped);
        Assert.Equal(7, state.Cl[79].Act);
        Assert.Equal(999, state.Ev[63].Chron);
        Assert.Equal(0x1f, state.Ac[31].Clued);
        Assert.Equal(12, state.Ac2[31].Intel);
        Assert.Equal(5, state.Pl.Ncase);
        Assert.Equal(9, state.OrgCounters[15]);
        Assert.Equal("Dez", state.MonthNames[11]);
        Assert.DoesNotContain(GameDgroup.Layout.OutsideModel(image), reason => reason.Contains("month_names[11]"));
    }
}
