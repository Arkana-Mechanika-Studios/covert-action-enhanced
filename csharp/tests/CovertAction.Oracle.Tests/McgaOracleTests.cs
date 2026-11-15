using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CovertAction.Content;
using CovertAction.Core.Graphics;
using CovertAction.Drivers;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Layout;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The game's drawing code (<see cref="McgaGraphicsDriver"/>) against the original's MCGA driver: for every recorded
/// entry into MGRAPHIC.EXE (tools/oracle/driver_vectors.py runs the driver's own instructions in an emulator), the
/// C# starts from the same screen, pages, colours and driver variables, makes the same call, and must leave the same
/// pixels on every page, the same colours, the same driver variables, the same result and the same drawing port.
/// Skipped without vectors (build/oracle/MGRAPHIC).
/// </summary>
/// <remarks>
/// The vectors describe the original's memory: the driver's block of code and data, the screen memory, and other
/// bytes by their address. This class maps them to the C# objects; nothing of that reaches the product.
/// </remarks>
public sealed class McgaOracleTests(ITestOutputHelper output)
{
    // where things are in the driver's block (code and data of MGRAPHIC.EXE as loaded; the data segment starts at 1240h)
    private const int Data = 0x1240;
    private const int ColourTable = Data + 0x80;
    private const int Fonts = Data + 0x630;
    private const int DrawPageWord = 0x1800, LineOriginXWord = 0x1802, LineOriginYWord = 0x1804, LineRightWord = 0x1806;
    private const int ColorByte = 0x1808, LineOperationByte = 0x1809;
    private const int PortSize = 18;
    private const int PageTable = 0x17f0;
    private const int TraitsTable = 0x1656;
    private const int BuildWidthWord = 0x794;
    private const int DrawnAreaWords = 0x7a6;
    private const int DataSegmentOperand = 0x1005;
    private const int EdgeRows = 220;
    private const string EndOfProgram = "int21.4c";

    /// <summary>
    /// Bytes of the driver's block the original changes that the C# has no variable for: the instruction byte it
    /// rewrites for the line operation (three copies, and the byte it is taken from), the 16 colours last sent to
    /// the display, the page last shown (for its mouse pointer), and the working variables of its line and text routines.
    /// </summary>
    private static readonly (int Start, int End)[] DriverScratch =
    [
        (0x04ea, 0x04eb), (0x0581, 0x0582), (0x05a0, 0x05a1), (0x180a, 0x180b),
        // the dissolve's speed, and the variables of the image routines (where the next image goes, clipping, the area drawn)
        (0x05d6, 0x05d8), (0x0782, 0x07c1),
        (Data + 0x50, Data + 0x80), (Data + 0x214, Data + 0x216), (Data + 0x600, Data + 0x623),
    ];

    /// <summary>One recorded case: the original's memory before and after, with typed access.</summary>
    private sealed class Case
    {
        private readonly JsonElement _case;
        private readonly Dictionary<int, byte> _before = [];
        private readonly Dictionary<int, RasterPort> _ports = [];

        public Case(JsonElement vector, JsonElement oracleCase)
        {
            _case = oracleCase;
            DriverIn = oracleCase.GetProperty("driver_in").GetBytesFromBase64();
            // Where things are in memory is read from the case itself: the cases of one file come from runs of
            // different programs, which load the driver and their data at different places. The driver's code names
            // its data segment (the operand of an instruction of char_width); its page table names the pages; a
            // routine is entered with the game's data segment in DS.
            DriverBase = (BinaryPrimitives.ReadUInt16LittleEndian(DriverIn.AsSpan(DataSegmentOperand)) - Data / 16) * 16;
            Dgroup = oracleCase.GetProperty("regs_in").GetProperty("ds").GetInt32() * 16;
            PageSegments = [.. Enumerable.Range(0, McgaGraphicsDriver.PageCount).Select(page => (int)BinaryPrimitives.ReadUInt16LittleEndian(DriverIn.AsSpan(PageTable + 2 * page)))];
            Video = PageSegments[0];
            foreach (var run in oracleCase.GetProperty("far_in").EnumerateObject())
            {
                var address = Hex(run.Name);
                foreach (var value in run.Value.GetBytesFromBase64())
                {
                    _before[address++] = value;
                }
            }
        }

        public string Id => _case.GetProperty("id").GetString()!;

        public int DriverBase { get; }

        public int Dgroup { get; }

        public int Video { get; }

        public int[] PageSegments { get; }

        public byte[] DriverIn { get; }

        public IReadOnlyDictionary<int, RasterPort> Ports => _ports;

        public byte[] Bytes(string property) => _case.GetProperty(property).GetBytesFromBase64();

        public int Register(string name) => _case.GetProperty("regs_in").GetProperty(name).GetInt32();

        public int Result => _case.GetProperty("result").GetProperty("ax").GetInt32();

        public JsonElement Property(string name) => _case.GetProperty(name);

        /// <summary>Set when the entry itself compared what the original wrote into its memory for images.</summary>
        public bool ImageMemoryCompared { get; private set; }

        /// <summary>Whether an address belongs to a page, the screen's memory or the game's data segment.</summary>
        public bool IsPageOrGameData(int address) =>
            PageSegments.Any(segment => address >= segment * 16 && address < segment * 16 + 0x10000) || (address >= Dgroup && address < Dgroup + 0x10000);

        /// <summary>The bytes the original wrote into its memory for images, in the order of their addresses.</summary>
        public byte[] ImageMemoryWritten()
        {
            ImageMemoryCompared = true;
            return [.. _case.GetProperty("far_out").EnumerateObject()
                .Select(run => (Address: Hex(run.Name), Bytes: run.Value.GetBytesFromBase64()))
                .Where(run => !IsPageOrGameData(run.Address))
                .OrderBy(run => run.Address)
                .SelectMany(run => run.Bytes)];
        }

        /// <summary>The image the original has at a segment, read from the bytes the call read of it.</summary>
        public Image? ImageAt(int segment)
        {
            var at = (ushort)segment * 16;
            if (at == 0)
            {
                return null;
            }
            int width = WordBefore(at), height = WordBefore(at + 2);
            var rows = new List<ImageRow>();
            at += 4;
            for (var row = 0; row < height; row++)
            {
                int count = WordBefore(at), skip = WordBefore(at + 2);
                rows.Add(new ImageRow(skip, [.. Enumerable.Range(at + 4, count).Select(Before)]));
                at += 4 + count;
            }
            return new Image(width, height, rows);
        }

        /// <summary>A word of the driver's block after the call.</summary>
        public int DriverWordAfter(int offset)
        {
            var block = (byte[])DriverIn.Clone();
            foreach (var run in _case.GetProperty("driver_out").EnumerateObject())
            {
                run.Value.GetBytesFromBase64().CopyTo(block, Hex(run.Name) - DriverBase);
            }
            return BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(offset));
        }

        /// <summary>A stack argument as a word. An entry whose arguments the vector does not list has them on the stack it was entered with.</summary>
        public int Arg(int index)
        {
            if (index >= _case.GetProperty("args").GetArrayLength())
            {
                const int returnAddress = 4;
                var stack = _case.GetProperty("regs_in");
                return WordBefore(stack.GetProperty("ss").GetInt32() * 16 + (ushort)(stack.GetProperty("sp").GetInt32() + returnAddress + 2 * index));
            }
            var arg = _case.GetProperty("args")[index];
            return arg.ValueKind == JsonValueKind.Object ? arg.GetProperty("ptr").GetInt32() : arg.GetInt32();
        }

        /// <summary>The bytes behind a pointer argument.</summary>
        public byte[] Memory(int index, int length)
        {
            var arg = _case.GetProperty("args")[index];
            if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("mem", out var mem))
            {
                return mem.GetBytesFromBase64();
            }
            return [.. Enumerable.Range(Dgroup + Arg(index), length).Select(Before)];
        }

        /// <summary>The text behind a pointer argument.</summary>
        public string Text(int index)
        {
            var arg = _case.GetProperty("args")[index];
            if (arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("str", out var str))
            {
                return Encoding.Latin1.GetString(str.GetBytesFromBase64());
            }
            var text = new StringBuilder();
            for (var at = Dgroup + Arg(index); Before(at) != 0; at++)
            {
                text.Append((char)Before(at));
            }
            return text.ToString();
        }

        /// <summary>A byte of the original's memory before the call, as far as the call read it (0 otherwise).</summary>
        public byte Before(int address) => _before.GetValueOrDefault(address);

        public int WordBefore(int address) => Before(address) | Before(address + 1) << 8;

        /// <summary>The drawing port at an offset of the game's data segment: one object per address.</summary>
        public RasterPort Port(int offset)
        {
            offset = (ushort)offset;
            if (!_ports.TryGetValue(offset, out var port))
            {
                port = new RasterPort();
                RasterPortLayout.Layout.Load([.. Enumerable.Range(Dgroup + offset, PortSize).Select(Before)], port);
                _ports[offset] = port;
            }
            return port;
        }

        /// <summary>A page before the call: the screen from the vector's screen memory, another page from the bytes the call read.</summary>
        public byte[] PageBefore(int page)
        {
            if (PageSegments[page] == Video)
            {
                return Bytes("video_in")[..(McgaGraphicsDriver.Width * McgaGraphicsDriver.Height)];
            }
            var start = PageSegments[page] * 16;
            return [.. Enumerable.Range(start, McgaGraphicsDriver.Width * McgaGraphicsDriver.Height).Select(Before)];
        }

        private static int Hex(string text) => int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        public static int Address(string text) => Hex(text);
    }

    /// <summary>The entries and how each is called from a recorded case; the result to compare with the original's AX, or null.</summary>
    private static readonly Dictionary<string, Func<McgaGraphicsDriver, Case, int?>> Entries = new()
    {
        ["_char_width"] = (d, c) => d.CharWidth(c.Arg(0), (char)(byte)c.Arg(1)),
        ["_copy_rect"] = (d, c) => Nothing(() => d.CopyRect(c.Port(c.Arg(0)), c.Arg(1), c.Arg(2), c.Arg(3), c.Arg(4), c.Port(c.Arg(5)), c.Arg(6), c.Arg(7))),
        ["_draw_string"] = (d, c) => d.DrawString(c.Port(c.Arg(0)), c.Arg(1), c.Arg(2), c.Text(3)),
        ["_recolor_rect"] = (d, c) => Nothing(() => d.RecolorRect(c.Port(c.Arg(0)), c.Arg(1), c.Arg(2), c.Arg(3), c.Arg(4), c.Arg(5), c.Arg(6))),
        ["_set_color_maps"] = (d, c) => Nothing(() => d.SetColorMaps(c.Memory(0, 16), c.Memory(1, 16))),
        ["_set_palette"] = (d, c) => Nothing(() => d.SetPalette(c.Memory(0, 17))),
        ["_show_page"] = (d, c) => Nothing(() => d.ShowPage(c.Arg(0))),
        ["sub_code_0000_0208"] = (d, c) => Nothing(() => d.SubCode0208(c.Arg(0))),
        ["_get_pixel"] = (d, c) => d.GetPixel(c.Arg(0), c.Arg(1), c.Arg(2)),
        // the driver copies the row two pixels at a time
        ["_put_pic_line"] = (d, c) => Nothing(() => d.PutPicLine(c.Memory(0, (c.Arg(4) + 1) / 2 * 2), c.Arg(1), c.Arg(2), c.Arg(3), c.Arg(4))),
        ["_fill_page"] = (d, c) => Nothing(() => d.FillPage(c.Arg(0), c.Arg(1))),
        ["_page_flip_active"] = (d, c) => d.PageFlipActive(c.Arg(0)),
        ["sub_game_0000_014b"] = (d, c) => Nothing(() => d.SubGame014b(c.Arg(0))),

        // ---- images. The original keeps an image in memory and names it by its place there; the C# has an object.
        //      Where the original wrote an image's bytes, the typed image is put into the same form and compared. ----
        ["_begin_image_cuts"] = (d, _) => Nothing(() => d.BeginImageCuts()),
        ["_end_image_cuts"] = (d, _) => Nothing(d.EndImageCuts),
        ["_image_memory_free"] = (d, _) => Nothing(() => d.ImageMemoryFree()),
        ["_finish_image_build"] = (d, _) => Nothing(() => d.FinishImageBuild()),
        ["_begin_image_build"] = (d, c) => Nothing(() =>
        {
            d.BeginImageBuild(c.Arg(0), c.Arg(1));
            var image = d.FinishImageBuild();
            // the original writes the size at the start of the new image, when it has room for one
            var written = c.ImageMemoryWritten();
            Assert.True(written.SequenceEqual(c.Result == 0 ? [] : Encoded(image!)), $"{c.Id}: the image started is not the original's");
        }),
        ["_add_image_row"] = (d, c) => Nothing(() =>
        {
            // the image in progress is as wide as the driver's variable says; the row is appended to it
            var width = BinaryPrimitives.ReadUInt16LittleEndian(c.DriverIn.AsSpan(BuildWidthWord));
            d.BeginImageBuild(width, 1);
            d.AddImageRow(c.Memory(0, width));
            var row = d.FinishImageBuild()!.Rows.Single();
            Assert.True(IsWritten(c.ImageMemoryWritten(), Encoded(row)), $"{c.Id}: the row added is not the original's");
        }),
        ["_cut_image"] = (d, c) => Nothing(() =>
        {
            var image = d.CutImage(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3), c.Arg(4));
            Assert.True(IsWritten(c.ImageMemoryWritten(), c.Result == 0 ? [] : Encoded(image!)), $"{c.Id}: the image cut is not the original's");
        }),
        ["_draw_image_port"] = (d, c) => Nothing(() => d.DrawImagePort(c.Port(c.Arg(0)), c.Arg(1), c.Arg(2), c.ImageAt(c.Arg(3)))),
        ["_draw_image_untracked"] = (d, c) => Nothing(() =>
        {
            var area = d.DrawImageUntracked(c.Port(c.Arg(0)), c.Arg(1), c.Arg(2), c.ImageAt(c.Arg(3)));
            // the original answers with the address of four words of its own: the area it drew on
            short Word(int index) => (short)c.DriverWordAfter(DrawnAreaWords + 2 * index);
            Assert.True(area == new ImageArea(Word(0), Word(1), Word(2), Word(3)), $"{c.Id}: the area drawn on is {area}, the original's ({Word(0)}, {Word(1)}, {Word(2)}, {Word(3)})");
        }),
        // the entries the game's library reaches with its values in processor registers
        ["sub_code_0000_0216"] = (d, c) => Nothing(() =>
        {
            // BX points at the table of left edges in the game's data; the right edges follow it
            ushort Edge(int table, int row) => (ushort)c.WordBefore(c.Dgroup + (ushort)(c.Register("bx") + table * 2 * EdgeRows + 2 * row));
            var left = Enumerable.Range(0, EdgeRows).Select(row => Edge(0, row)).ToArray();
            var right = Enumerable.Range(0, EdgeRows).Select(row => Edge(1, row)).ToArray();
            d.FillSpans(c.Register("ax"), c.Register("cx"), left, right);
        }),
        ["sub_code_0000_0263"] = (d, c) => Nothing(() =>
        {
            // SI and DI name the pages; BX points at the left edges of the screen's 200 rows, the right edges follow
            ushort Edge(int table, int row) => (ushort)c.WordBefore(c.Dgroup + (ushort)(c.Register("bx") + table * 2 * McgaGraphicsDriver.Height + 2 * row));
            var left = Enumerable.Range(0, McgaGraphicsDriver.Height).Select(row => Edge(0, row)).ToArray();
            var right = Enumerable.Range(0, McgaGraphicsDriver.Height).Select(row => Edge(1, row)).ToArray();
            d.CopySpans(c.Register("si"), c.Register("di"), c.Register("ax"), c.Register("cx"), left, right);
        }),
        ["sub_code_0000_021d"] = (d, _) => Nothing(d.EndDrawing),
        ["sub_code_0000_0224"] = (d, c) => Nothing(() => d.SetColor(c.Register("ax"))),
        ["sub_code_0000_022b"] = (d, c) => Nothing(() => d.Line(c.Register("ax"), c.Register("bx"), c.Register("cx"), c.Register("dx"))),
        ["sub_code_0000_0255"] = (d, c) => Nothing(() => d.SelectPage(c.Register("ax"))),
        ["sub_code_0000_025c"] = (d, c) => Nothing(() => d.BeginLine(c.Port(c.Register("bx")))),
    };

    /// <summary>An image as the original has it in memory: two words, then its rows.</summary>
    private static byte[] Encoded(Image image) =>
        [.. Word(image.Width), .. Word(image.Height), .. image.Rows.SelectMany(Encoded)];

    /// <summary>A row of an image as the original has it in memory: the number of stored pixels, the number of skipped ones, the pixels.</summary>
    private static byte[] Encoded(ImageRow row) => [.. Word(row.Pixels.Length), .. Word(row.Skip), .. row.Pixels];

    private static byte[] Word(int value) => [(byte)value, (byte)(value >> 8)];

    /// <summary>
    /// Whether the bytes the original wrote are an image (or a row) in its memory form. The original first writes a
    /// row's see-through pixels at its end and then takes them off the row's count, so behind the last row the
    /// bytes written go on with zeros that belong to no row.
    /// </summary>
    private static bool IsWritten(byte[] written, byte[] image) =>
        written.Length >= image.Length && written.AsSpan(0, image.Length).SequenceEqual(image) && written.AsSpan(image.Length).IndexOfAnyExcept((byte)0) < 0;

    private static int? Nothing(Action call)
    {
        call();
        return null;
    }

    /// <summary>The names of the entries under test, one test case each.</summary>
    public static IEnumerable<object[]> EntryNames => Entries.Keys.Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(EntryNames))]
    public void TheDrawingCodeDoesWhatTheOriginalDriverDid(string entry)
    {
        var path = Path.Combine(OracleVectors.Folder, "MGRAPHIC", entry + ".json");
        const string unnamedCodeSlot = "sub_code_0000_";
        if (!File.Exists(path) && entry.StartsWith(unnamedCodeSlot, StringComparison.Ordinal))
        {
            // An entry without a name goes by the slot of the program it was recorded from; the menus part's
            // slots lie 8Ch behind the cryptography part's.
            const int finalSlotsBehind = 0x8c;
            var slot = Convert.ToInt32(entry[unnamedCodeSlot.Length..], 16) + finalSlotsBehind;
            path = Path.Combine(OracleVectors.Folder, "MGRAPHIC", $"sub_final_0000_{slot:x4}.json");
        }
        if (!File.Exists(path))
        {
            throw new SkipException($"no driver vectors {path} (tools/oracle/driver_vectors.py writes them from the player's game)");
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = 0;
        foreach (var json in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var oracleCase = new Case(document.RootElement, json);
            if (oracleCase.Property("calls").EnumerateArray().Any(call => call.GetProperty("target").GetString() == EndOfProgram))
            {
                // The emulator's stand-in BIOS answered "which display mode?" with a changed BL, which a PC's BIOS
                // keeps; the driver took that for a display without the 256-colour mode, printed its message and
                // ended the program. Such a run shows nothing of what the entry does.
                output.WriteLine($"SET ASIDE {entry} {oracleCase.Id}: the original driver ended the program (an answer of the oracle's BIOS fixture)");
                continue;
            }
            try
            {
                Replay(entry, oracleCase);
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                Assert.Fail($"{entry} {oracleCase.Id}: {e.GetType().Name}: {e.Message}");
            }
            cases++;
        }
        output.WriteLine($"TALLY {entry}: {cases} cases agree");
        if (cases == 0)
        {
            throw new SkipException($"{entry}: no usable case among the vectors");
        }
    }

    /// <summary>
    /// The three entries that give a page its memory. The original asks DOS for memory and stores where it is; the
    /// C# has buffers with numbers. What can be compared is what each entry changes: only "set" changes the driver
    /// (the page's entry in its table of pages); "allocate" and "free" change nothing of it.
    /// </summary>
    [SkippableFact]
    public void APageGetsItsBufferAsInTheOriginal()
    {
        var compared = 0;
        foreach (var entry in new[] { "_alloc_page_buffer", "_set_page_buffer", "_free_page_buffer" })
        {
            var path = Path.Combine(OracleVectors.Folder, "MGRAPHIC", entry + ".json");
            Skip.IfNot(File.Exists(path), $"no driver vectors {path}");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var json in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                var c = new Case(document.RootElement, json);
                var changed = c.Property("driver_out").EnumerateObject().Select(run => (At: Case.Address(run.Name) - c.DriverBase, Bytes: run.Value.GetBytesFromBase64())).ToList();
                var driver = new McgaGraphicsDriver(new FontSet([0, 0]), new byte[3 * McgaDriverFile.Colours]);
                var pagesBefore = (byte[][])driver.Pages.Clone();
                switch (entry)
                {
                    case "_alloc_page_buffer":
                        // (the oracle's DOS has no memory to give, so the original's own answer here is 0: not compared)
                        Assert.Empty(changed);
                        Assert.True(driver.AllocPageBuffer(c.Arg(0)) != 0);
                        Assert.Equal(pagesBefore, driver.Pages);
                        break;
                    case "_set_page_buffer":
                        // the original stores the buffer's place in the page's entry of the table, and returns it
                        var page = c.Arg(0);
                        Assert.True(changed.Count <= 1 && changed.All(run => run.At == PageTable + 2 * page && run.Bytes.Length <= 2), $"{entry} {c.Id}: more than the page's entry changed");
                        Assert.Equal(c.Arg(1), c.Result);
                        var buffer = driver.AllocPageBuffer(1);
                        driver.SetPageBuffer(page, buffer);
                        Assert.All(Enumerable.Range(0, McgaGraphicsDriver.PageCount), other => Assert.True(other == page
                            ? !pagesBefore.Contains(driver.Pages[other])
                            : ReferenceEquals(driver.Pages[other], pagesBefore[other])));
                        break;
                    default:
                        Assert.Empty(changed);
                        driver.FreePageBuffer(c.Arg(0));
                        Assert.Equal(pagesBefore, driver.Pages);
                        break;
                }
                compared++;
            }
        }
        output.WriteLine($"TALLY page buffers: {compared} cases agree");
    }

    private static void Replay(string entry, Case c)
    {
        var where = $"{entry} {c.Id}";
        // the pages as the rewrite's driver has them: the screen, two off-screen pages, the rest not set up yet

        var driver = new McgaGraphicsDriver(new FontSet(c.DriverIn[Fonts..]), c.DriverIn[ColourTable..(ColourTable + 3 * McgaDriverFile.Colours)])
        {
            Traits = c.DriverIn[TraitsTable..(TraitsTable + 10)],
        };
        // the pages as this run had them: one picture per distinct place in memory (a page nobody set up is the screen)
        var firstWithSegment = Enumerable.Range(0, McgaGraphicsDriver.PageCount).Select(page => Array.IndexOf(c.PageSegments, c.PageSegments[page])).ToArray();
        for (var page = 0; page < McgaGraphicsDriver.PageCount; page++)
        {
            driver.Pages[page] = firstWithSegment[page] == page ? c.PageBefore(page) : driver.Pages[firstWithSegment[page]];
        }
        c.Bytes("palette_in").CopyTo(driver.Palette, 0);
        driver.Border = (byte)c.Property("overscan_in").GetInt32();
        LoadVariables(driver, c, c.DriverIn);

        var result = Entries[entry](driver, c);

        // ---- the result ----
        if (result is { } value)
        {
            Assert.True((ushort)value == c.Result, $"{where}: the C# returns {value}, the original {unchecked((short)c.Result)}");
        }

        // ---- the driver's variables, and nothing else of the driver changed ----
        var driverAfter = (byte[])c.DriverIn.Clone();
        foreach (var run in c.Property("driver_out").EnumerateObject())
        {
            var at = Case.Address(run.Name) - c.DriverBase;
            foreach (var changed in run.Value.GetBytesFromBase64())
            {
                Assert.True(IsVariable(at) || DriverScratch.Any(s => at >= s.Start && at < s.End),
                    $"{where}: the original changes driver byte {at:x4}, which the C# has no counterpart for");
                driverAfter[at++] = changed;
            }
        }
        var expected = new McgaGraphicsDriver(new FontSet(c.DriverIn[Fonts..]), new byte[3 * McgaDriverFile.Colours]);
        LoadVariables(expected, c, driverAfter);
        Assert.True(Array.IndexOf(driver.Pages, driver.DrawPage) == Array.IndexOf(expected.Pages, expected.DrawPage), $"{where}: the drawing page differs");
        Assert.True((driver.LineOriginX, driver.LineOriginY, driver.LineRight) == (expected.LineOriginX, expected.LineOriginY, expected.LineRight),
            $"{where}: the line origin and right edge are ({driver.LineOriginX}, {driver.LineOriginY}, {driver.LineRight}), the original's ({expected.LineOriginX}, {expected.LineOriginY}, {expected.LineRight})");
        Assert.True((driver.Color, driver.LineOperation) == (expected.Color, expected.LineOperation),
            $"{where}: colour and line operation are ({driver.Color}, {driver.LineOperation}), the original's ({expected.Color}, {expected.LineOperation})");

        // ---- the colours ----
        var palette = c.Property("palette_out").ValueKind == JsonValueKind.Null ? c.Bytes("palette_in") : c.Bytes("palette_out");
        Assert.True(palette.AsSpan().SequenceEqual(driver.Palette), $"{where}: the colours differ (first at byte {FirstDifference(palette, driver.Palette)})");
        Assert.True(driver.Border == c.Property("overscan_out").GetInt32(), $"{where}: the border is {driver.Border}, the original's {c.Property("overscan_out").GetInt32()}");

        // ---- the pages, the drawing ports, and nothing else of memory changed ----
        var pages = Enumerable.Range(0, McgaGraphicsDriver.PageCount).Select(page => firstWithSegment[page] == page ? c.PageBefore(page) : null).ToArray();
        // the mode switch clears the screen in the BIOS, which the vectors do not record: the screen is not compared there
        var screenCompared = entry != "sub_code_0000_0208";
        foreach (var run in c.Property("video_out").EnumerateObject())
        {
            Patch(pages[0]!, Case.Address(run.Name), run.Value.GetBytesFromBase64());
        }
        var ports = c.Ports.ToDictionary(p => p.Key, p => Enumerable.Range(c.Dgroup + p.Key, PortSize).Select(c.Before).ToArray());
        foreach (var run in c.Property("far_out").EnumerateObject())
        {
            var address = Case.Address(run.Name);
            foreach (var changed in run.Value.GetBytesFromBase64())
            {
                var page = Array.FindIndex(c.PageSegments, s => s != c.Video && address >= s * 16 && address < s * 16 + McgaGraphicsDriver.Width * McgaGraphicsDriver.Height);
                var port = ports.Keys.Cast<int?>().FirstOrDefault(p => address >= c.Dgroup + p && address < c.Dgroup + p + PortSize);
                if (address >= c.Video * 16 + McgaGraphicsDriver.Width * McgaGraphicsDriver.Height && address < c.Video * 16 + 0x10000)
                {
                    // the screen memory below the last row: not part of the picture
                }
                else if (page > 0)
                {
                    Patch(pages[page]!, address - c.PageSegments[page] * 16, [changed]);
                }
                else if (port is { } offset)
                {
                    ports[offset][address - c.Dgroup - offset] = changed;
                }
                else if (!c.ImageMemoryCompared || c.IsPageOrGameData(address))
                {
                    Assert.Fail($"{where}: the original writes memory at {address:x5}, which the C# has no counterpart for");
                }
                address++;
            }
        }
        for (var page = screenCompared ? 0 : 1; page < pages.Length; page++)
        {
            if (pages[page] is not { } expectedPage)
            {
                continue;   // the same picture as an earlier page
            }
            var at = FirstDifference(expectedPage, driver.Pages[page]);
            Assert.True(at < 0, at < 0 ? "" : $"{where}: page {page} differs at ({at % 320}, {at / 320}): the C# has {driver.Pages[page][at]}, the original {expectedPage[at]}");
        }
        foreach (var (offset, port) in c.Ports)
        {
            var original = new RasterPort();
            RasterPortLayout.Layout.Load(ports[offset], original);
            Assert.True(Fields(port) == Fields(original), $"{where}: the port at DS:{offset:x4} is {Fields(port)}, the original's {Fields(original)}");
        }
    }

    private static bool IsVariable(int at) => at is >= DrawPageWord and <= LineOperationByte;

    /// <summary>Sets the driver's variables from an image of the original driver's block.</summary>
    private static void LoadVariables(McgaGraphicsDriver driver, Case c, byte[] block)
    {
        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at));
        var page = Array.IndexOf(c.PageSegments, Word(DrawPageWord));
        Assert.True(page >= 0, $"{c.Id}: the driver's drawing page {Word(DrawPageWord):x4} is none of its pages");
        driver.DrawPage = driver.Pages[page];
        driver.LineOriginX = Word(LineOriginXWord);
        driver.LineOriginY = Word(LineOriginYWord);
        driver.LineRight = Word(LineRightWord);
        driver.Color = block[ColorByte];
        driver.LineOperation = (PixelOperation)block[LineOperationByte];
    }

    /// <summary>Puts changed bytes into the picture of a page; bytes of the original's memory below the last row are not part of it.</summary>
    private static void Patch(byte[] page, int at, byte[] changed)
    {
        foreach (var value in changed)
        {
            if (at < page.Length)
            {
                page[at] = value;
            }
            at++;
        }
    }

    private static int FirstDifference(byte[] expected, byte[] actual) =>
        expected.AsSpan().SequenceEqual(actual) ? -1 : Enumerable.Range(0, expected.Length).First(i => expected[i] != actual[i]);

    private static (short, short, short, short, short, short, short, short, short) Fields(RasterPort port) =>
        (port.PageNr, port.XBgn, port.YBgn, port.Width1, port.Length1, port.DrawMode, port.APen, port.BPen, port.FontNr);
}
