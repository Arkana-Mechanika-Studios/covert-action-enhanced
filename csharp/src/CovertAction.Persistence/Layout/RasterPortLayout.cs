using CovertAction.Core.Graphics;

namespace CovertAction.Persistence.Layout;

/// <summary>The 18 bytes of a drawing port record as every module keeps it in its data segment: nine 16-bit words.</summary>
public static class RasterPortLayout
{
    /// <summary>Size of the record in bytes.</summary>
    public const int Size = 18;

    /// <summary>The record's fields at their offsets (the reconstructed gfx.h).</summary>
    // orig: struct RasterPort (src/c/include/gfx.h): +00 PageNr, +02 XBgn, +04 YBgn, +06 Width1, +08 Length1, +0a DrawMode, +0c APen, +0e BPen, +10 FontNr
    public static StateLayout<RasterPort> Layout { get; } = new StateLayout<RasterPort>("RasterPort")
        .Int16(0x00, "PageNr", p => p.PageNr, (p, v) => p.PageNr = v)
        .Int16(0x02, "XBgn", p => p.XBgn, (p, v) => p.XBgn = v)
        .Int16(0x04, "YBgn", p => p.YBgn, (p, v) => p.YBgn = v)
        .Int16(0x06, "Width1", p => p.Width1, (p, v) => p.Width1 = v)
        .Int16(0x08, "Length1", p => p.Length1, (p, v) => p.Length1 = v)
        .Int16(0x0a, "DrawMode", p => p.DrawMode, (p, v) => p.DrawMode = v)
        .Int16(0x0c, "APen", p => p.APen, (p, v) => p.APen = v)
        .Int16(0x0e, "BPen", p => p.BPen, (p, v) => p.BPen = v)
        .Int16(0x10, "FontNr", p => p.FontNr, (p, v) => p.FontNr = v);
}
