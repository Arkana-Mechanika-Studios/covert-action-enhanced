using System.Text;
using System.Text.Json;
using CovertAction.Core.Graphics;
using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Oracle.Tests.Harness;

/// <summary>
/// The outside world of an oracle test: one object behind every boundary interface. It holds the calls the original
/// made in one recorded run; each call the C# makes must be the next of them (same routine, same arguments) and gets
/// the answer the original got. A call out of turn, a wrong argument or a call left over fails the test.
/// </summary>
/// <remarks>
/// <para>
/// One class for all boundaries because the calls of a run form one sequence across them (a key read between two
/// drawing calls). A number is compared as the 16 bits the original pushed. A text or a table is compared by content
/// with what the generator captured behind the original's pointer at the time of the call (every drawn text, file
/// names, palettes, colour tables). A drawing port is compared by address: the test says where the C# object lies in
/// the original's data segment.
/// </para>
/// <para>
/// The generator records the boundary where execution leaves the module: driver thunks, the assembly routines and
/// library routines on its cut lists (<c>fill_rect</c>, <c>draw_line_port</c>, <c>find_file_on_disk</c>, <c>time</c>),
/// interrupts and ports. The C#'s boundaries are named services instead (a frame wait, a mouse query, the current
/// drive): for each, the fake takes the interrupts or port reads the original made for it, with the registers that
/// carry the service's values.
/// </para>
/// </remarks>
/// <param name="expected">The calls the original made, in order.</param>
/// <param name="addressOf">The offset in the module's data segment of a state object passed by pointer (a drawing port), or null when unknown.</param>
/// <param name="images">The images of the part's state by the words the original knows them by (a state with stored images has such a register); null for a register of the fake's own.</param>
internal sealed partial class RecordingBoundaries(IReadOnlyList<BoundaryCall> expected, Func<object, int?>? addressOf = null, ImageRegister? images = null)
    : IGraphicsDriver, ISoundDriver, IInputDriver, IMouseDriver, IGameTimer, IFiles
{
    private int _next;

    /// <summary>A pointer argument: the bytes behind it, and whether they are a text.</summary>
    private sealed record Memory(byte[] Bytes, bool IsText);

    /// <summary>The number of calls made so far.</summary>
    public int CallsMade => _next;

    /// <summary>Fails unless the C# made every call the original made.</summary>
    public void AssertAllCallsMade()
    {
        PassOverCallsWithoutCounterpart();
        Assert.True(_next == expected.Count,
            $"the original made {expected.Count} boundary calls, the C# only {_next}; next expected: {(_next < expected.Count ? expected[_next].Target : "")}");
    }

    /// <summary>The routine of the next recorded call, or "" after the last.</summary>
    private string Next
    {
        get
        {
            PassOverCallsWithoutCounterpart();
            return _next < expected.Count ? expected[_next].Target : "";
        }
    }

    private static Memory Text(string text) => new(Encoding.Latin1.GetBytes(text), true);

    private static Memory Table(ReadOnlySpan<byte> bytes) => new(bytes.ToArray(), false);

    /// <summary>The answer of a call as a signed 16-bit number.</summary>
    private static int Word(BoundaryCall call) => unchecked((short)(call.Ret ?? 0));

    /// <summary>Checks one call of the C# against the next recorded one and returns the recorded call (for its answer).</summary>
    private BoundaryCall Call(string target, params object[] args)
    {
        var call = Take(target);
        Assert.True(args.Length == call.Args.Count, $"call {_next} {target}: {args.Length} arguments, the original passed {call.Args.Count}");
        for (var i = 0; i < args.Length; i++)
        {
            Compare($"call {_next} {target}, argument {i + 1}", args[i], call.Args[i]);
        }
        return call;
    }

    /// <summary>Takes the next recorded call, which must be to <paramref name="target"/>; its arguments are the caller's to check.</summary>
    /// <summary>The names a routine of the boundary carries in the programs whose symbols call it differently.</summary>
    private static readonly Dictionary<string, string[]> OtherNames = new()
    {
        ["draw_string"] = ["draw_string_port"],     // BUG
        ["fill_rect"] = ["fill_rect_port"],         // BUG
        ["draw_image_untracked"] = ["sub_chase_0000_0134"],     // CHASE has no name for it
        // the same slots of the thunk table under the names (or labels) FINAL, TAC and GAME have for them
        ["set_color_maps"] = ["sub_final_0000_0255"],
        ["calibrate_joystick"] = ["sub_final_0000_0438"],
        ["sub_game_0000_014b"] = ["sub_final_0000_01d7"],
        ["get_pixel"] = ["sub_final_0000_01ec"],
        // GAME's slot 012f is FINAL's fill_page (01bb) moved by 0x8c, as put_pic_line (018a, 0216)
        ["fill_page"] = ["sub_game_0000_012f"],
        ["draw_image_port"] = ["blit_image_masked", "put_image", "draw_image"],     // FINAL, TAC, GAME
        ["set_palette"] = ["sub_final_0000_024e", "sub_tac_0000_024e"],
        ["image_memory_free"] = ["sub_final_0000_019f"],
        ["end_image_cuts"] = ["sub_final_0000_01c9", "sub_tac_0000_01c9"],
        ["begin_image_cuts"] = ["sub_final_0000_020f", "sub_tac_0000_020f"],
        ["begin_image_build"] = ["begin_image"],        // GAME
        ["finish_image_build"] = ["finish_image"],      // GAME
        ["play_sound"] = ["sound_driver_play"],         // FINAL
        ["show_page"] = ["sub_tac_0000_0268"],
        // TAC's page buffer thunks, at the CHASE slots 010c, 01b4 and 0144 moved by 0x8c
        ["alloc_page_buffer"] = ["sub_tac_0000_0198"],
        ["set_page_buffer"] = ["sub_tac_0000_0240"],
        ["free_page_buffer"] = ["sub_tac_0000_01d0"],
    };

    private BoundaryCall Take(string target)
    {
        PassOverCallsWithoutCounterpart();
        Assert.True(_next < expected.Count, $"call {_next + 1}: the C# calls {target}, the original made no further call");
        var call = expected[_next++];
        Assert.True(call.Target == target || (OtherNames.TryGetValue(target, out var others) && others.Contains(call.Target)),
            $"call {_next}: the C# calls {target}, the original called {call.Target}");
        return call;
    }

    private void Compare(string where, object actual, JsonElement recorded)
    {
        // the generator writes a pointer either as its word or as {"ptr": word, "str" or "mem": base64}
        var captured = recorded.ValueKind == JsonValueKind.Object;
        var word = unchecked((ushort)(captured ? recorded.GetProperty("ptr").GetInt64() : recorded.GetInt64()));
        switch (actual)
        {
            case int number:
                Assert.True(!captured, $"{where}: the original passed a pointer, the C# the number {number}");
                Assert.True(unchecked((ushort)number) == word, $"{where}: {number}, the original passed {unchecked((short)word)}");
                break;
            case Memory { IsText: false, Bytes.Length: 0 }:
                Assert.True(word == 0, $"{where}: no table (a null pointer), the original passed DS:{word:x4}");
                break;
            case Memory { IsText: true } text when captured && recorded.TryGetProperty("str", out var str):
                var original = str.GetBytesFromBase64();
                Assert.True(original.AsSpan().SequenceEqual(text.Bytes), $"{where}: the text \"{Show(text.Bytes)}\", the original passed \"{Show(original)}\"");
                break;
            case Memory table when captured && recorded.TryGetProperty("mem", out var mem):
                var bytes = mem.GetBytesFromBase64();
                Assert.True(table.Bytes.Length >= bytes.Length && bytes.AsSpan().SequenceEqual(table.Bytes.AsSpan(0, bytes.Length)),
                    $"{where}: the bytes {Convert.ToHexString(table.Bytes)}, the original passed {Convert.ToHexString(bytes)}");
                break;
            case Memory:
                break; // the generator did not capture what the pointer pointed at
            default:
                // a state object passed by pointer: comparable when the test knows its address
                if (addressOf?.Invoke(actual) is { } address)
                {
                    Assert.True(word == address, $"{where}: the object at DS:{address:x4}, the original passed DS:{word:x4}");
                }
                break;
        }
    }

    private static string Show(byte[] text) => string.Concat(text.Select(b => b is >= 0x20 and < 0x7f ? ((char)b).ToString() : $"\\x{b:x2}"));

    // ---- IGraphicsDriver ----

    public void FillRect(RasterPort port, int x, int y, int width, int height, int color)
    {
        if (!SelectPageSlot().IsMatch(Next))
        {
            Call("fill_rect", port, x, y, width, height, color);
            return;
        }
        // The program's copy of the routine was recorded as the four driver entries it calls, each with its values in
        // registers: choose the port's page (AX), set the colour (AX), fill the rows from the first (AX) to the last
        // (CX), end. The left and right edges go through a table in memory and are not part of the record.
        var module = Next[..^SlotDigits];
        var firstRow = y + port.YBgn;
        Interrupt(module + "02e1", port.PageNr);
        Interrupt(module + "02b0", color);
        Interrupt(module + "02a2", firstRow, cx: firstRow + height - 1);
        Take(module + "02a9");
    }

    private const int SlotDigits = 4;

    [System.Text.RegularExpressions.GeneratedRegex("^sub_[a-z]+_0000_02e1$")]
    private static partial System.Text.RegularExpressions.Regex SelectPageSlot();

    public void SetPaletteEntry(int index, int colour) => Call("set_palette_entry", index, colour);

    public void DrawLinePort(RasterPort port, int x1, int y1, int x2, int y2, int color) =>
        Call("draw_line_port", port, x1, y1, x2, y2, color);

    public void CopyRect(RasterPort source, int x, int y, int width, int height, RasterPort target, int targetX, int targetY) =>
        Call("copy_rect", source, x, y, width, height, target, targetX, targetY);

    public void RecolorRect(RasterPort port, int x, int y, int width, int height, int from, int to) =>
        Call("recolor_rect", port, x, y, width, height, from, to);

    public int DrawString(RasterPort port, int x, int y, string text) => Word(Call("draw_string", port, x, y, Text(text)));

    // the original passes the character as a C char: sign-extended to a word
    public int CharWidth(int font, char character)
    {
        // The callers pass the character as a C char (sign-extended) or, text_width, as an unsigned char; the C# hands
        // over the character alone. Every graphics driver reads only its low byte (mov cl, [bp+8]:
        // HarnessTests.EveryGraphicsDriverReadsTheCharacterOfCharWidthAsOneByte), so the two are the same call.
        var call = Take("char_width");
        Assert.True(call.Args.Count == 2, $"call {_next} char_width: 2 arguments, the original passed {call.Args.Count}");
        Compare($"call {_next} char_width, argument 1", font, call.Args[0]);
        var recorded = unchecked((ushort)call.Args[1].GetInt64());
        Assert.True((byte)recorded == (byte)character && (recorded >> 8 == 0 || (recorded >> 8 == 0xff && recorded >= 0xff80)),
            $"call {_next} char_width, argument 2: {(int)(sbyte)character}, the original passed {unchecked((short)recorded)}");
        return Word(call);
    }

    public void SetColorMaps(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) => Call("set_color_maps", Table(first), Table(second));

    public void SetPalette(ReadOnlySpan<byte> palette) => Call("set_palette", Table(palette));

    public void ShowPage(int page) => Call("show_page", page);

    public void SubCode0208(int argument) => Call("sub_code_0000_0208", argument);

    public int GetPixel(int page, int x, int y) => Word(Call("get_pixel", page, x, y));

    public void PutPicLine(ReadOnlySpan<byte> line, int page, int y, int x, int count) => Call("put_pic_line", Table(line), page, y, x, count);

    public void FillPage(int page, int color) => Call("fill_page", page, color);

    public int PageFlipActive(int what) => Word(Call("page_flip_active", what));

    public int AllocPageBuffer(int wanted) => Word(Call("alloc_page_buffer", wanted));

    public void SetPageBuffer(int page, int buffer) => Call("set_page_buffer", page, buffer);

    public void FreePageBuffer(int page) => Call("free_page_buffer", page);

    public void SubGame014b(int page) => Call("sub_game_0000_014b", page);

    public bool BeginImageCuts() => Word(Call("begin_image_cuts")) != 0;

    public void EndImageCuts() => Call("end_image_cuts");

    public int ImageMemoryFree() => Word(Call("image_memory_free"));

    public void BeginImageBuild(int width, int height) => Call("begin_image_build", width, height);

    public void AddImageRow(ReadOnlySpan<byte> pixels) => Call("add_image_row", Table(pixels));

    public Image? FinishImageBuild() => ImageNamed(Call("finish_image_build"));

    public Image? CutImage(int page, int x, int y, int width, int height) => ImageNamed(Call("cut_image", page, x, y, width, height));

    public void DrawImagePort(RasterPort port, int x, int y, Image? image) => Call("draw_image_port", port, x, y, NameOf(image));

    // the original answers with the address of four words in the driver, which a recorded call does not carry
    public ImageArea DrawImageUntracked(RasterPort port, int x, int y, Image? image)
    {
        Call("draw_image_untracked", port, x, y, NameOf(image));
        return default;
    }

    // the two tracked draws are MicroProse's library inside the program: what is recorded is the driver's draw beneath them
    public void DrawImageTracked(RasterPort port, int x, int y, Image? image) => Call("draw_image_untracked", port, x, y, NameOf(image));

    public void DrawImageToBackground(RasterPort port, int x, int y, Image? image) => Call("draw_image_untracked", port, x, y, NameOf(image));

    // MicroProse's library inside the program: what is recorded is the driver's span copy beneath it, with the first
    // row in AX, the table of row edges in BX, the last row in CX, the source page in SI and the target page in DI.
    // The rows and edges are what the library remembers of earlier draws, which this fake does not keep.
    public void RestoreTrackedSpans(int sourcePage, int targetPage)
    {
        Assert.True(SpanCopySlot().IsMatch(Next), $"call {_next + 1}: the C# puts the tracked spans back (the driver's span copy), the original called {Next}");
        var call = Take(Next);
        Compare($"call {_next} span copy, the source page", sourcePage, call.Args[4]);
        Compare($"call {_next} span copy, the target page", targetPage, call.Args[5]);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^sub_(final_0000_02ef|game_0000_0263)$")]
    private static partial System.Text.RegularExpressions.Regex SpanCopySlot();

    // the library's own tables, cleared inside the program: no call leaves it
    public void ClearSpanTracking()
    {
    }

    /// <summary>
    /// Says that the original's calls to some boundaries have no counterpart in the C# (memory given back to DOS,
    /// for one): they are passed over wherever they stand.
    /// </summary>
    public void WithoutCounterpart(params string[] targets) => _withoutCounterpart.UnionWith(targets);

    private readonly HashSet<string> _withoutCounterpart = [];

    private void PassOverCallsWithoutCounterpart()
    {
        while (_next < expected.Count && (_withoutCounterpart.Contains(expected[_next].Target) || _answered.Contains(_next)))
        {
            _next++;
        }
    }

    /// <summary>The original names an image by its place in memory (a word); one image object stands for each such word.</summary>
    private readonly ImageRegister _images = images ?? new ImageRegister();

    private Image? ImageNamed(BoundaryCall call) => _images.For(unchecked((int)(call.Ret ?? 0)));

    private int NameOf(Image? image) => _images.NameOf(image);

    // the video BIOS's "select display page": interrupt 10h with AH = 5 and the page in AL
    public void SelectDisplayPage(int page) => Interrupt("int10.05", 0x0500 | page & 0xff);

    // ---- ISoundDriver ----

    public void PlaySound(int effect) => Call("play_sound", effect);

    public void PlaySound(int effect, int parameter) => Call("play_sound", effect, parameter);

    public void PlaySound(int effect, int mode, int variant) => Call("play_sound", effect, mode, variant);

    // ---- IInputDriver ----

    public bool NoKeyWaiting() => Word(Call("no_key_waiting")) != 0;

    public int ReadKeyRaw() => Word(Call("read_key_raw"));

    // the routine takes nothing; a program in which another call of the same slot pushes a word (GAME) has that
    // many words of the caller's stack recorded with every call
    public void CalibrateJoystick() => Take("calibrate_joystick");

    public int ReadJoystickAxes() => Word(Call("read_joystick_axes"));

    // the game reads the joystick's buttons from the game port: recorded as a port read
    public int ReadJoystickButtons() => Word(Call("inb", GamePort));

    // kbhit (CODE 0000:31fe) is DOS function 0Bh, whose AL (0 or FF) is its answer; the registers the routine happens
    // to hold at the interrupt are not arguments
    public bool Kbhit() => Next == "kbhit" ? Word(Call("kbhit")) != 0 : (Take("int21.0b").Ret & 0xFF) != 0;

    // getch is DOS function 08h, whose AL is the character
    public int Getch() => (int)((Take("int21.08").Ret ?? 0) & 0xFF);

    /// <summary>
    /// Num Lock, which the original keeps as a bit of the PC's keyboard flags (0040:0017, bit 0x20) and changes in
    /// memory: a test sets it from the case's memory before the call and compares it afterwards.
    /// </summary>
    public bool NumLockOn { get; set; } = true;

    public bool NumLock() => NumLockOn;

    public void SetNumLock(bool on) => NumLockOn = on;

    // ---- IMouseDriver: each service is one interrupt 33h of the original, recorded as "int33.00" with six registers ----

    public void Reset() => Interrupt(MouseDriver, 0);

    public void ShowCursor() => Interrupt(MouseDriver, 1);

    public void HideCursor() => Interrupt(MouseDriver, 2);

    public MouseStatus GetStatus()
    {
        var call = Interrupt(MouseDriver, 3);
        return new MouseStatus(After(call, "bx", 1), After(call, "cx", 2), After(call, "dx", 3));
    }

    public void SetPosition(int x, int y) => Interrupt(MouseDriver, 4, cx: x, dx: y);

    public MouseButtonEvents ButtonPresses(int button) => ButtonEvents(Interrupt(MouseDriver, 5, bx: button));

    public MouseButtonEvents ButtonReleases(int button) => ButtonEvents(Interrupt(MouseDriver, 6, bx: button));

    public void SetHorizontalRange(int minimum, int maximum) => Interrupt(MouseDriver, 7, cx: minimum, dx: maximum);

    public void SetVerticalRange(int minimum, int maximum) => Interrupt(MouseDriver, 8, cx: minimum, dx: maximum);

    private static MouseButtonEvents ButtonEvents(BoundaryCall call) =>
        new(After(call, "ax", 0), After(call, "bx", 1), After(call, "cx", 2), After(call, "dx", 3));

    private const string MouseDriver = "int33.00";
    private const int GamePort = 0x201;
    private const int DisplayStatusPort = 0x3da;
    private const int VerticalRetrace = 8;

    /// <summary>
    /// One software interrupt of the original against a named service of the C#: AX (the service) and the registers
    /// the service takes must be the original's. The other registers hold leftovers of earlier calls in the original
    /// (its register block is a variable), which the C# does not keep; they are not compared.
    /// </summary>
    private BoundaryCall Interrupt(string target, int ax, int? bx = null, int? cx = null, int? dx = null)
    {
        var call = Take(target);
        Assert.True(call.Args.Count == 6, $"call {_next} {target}: {call.Args.Count} registers recorded, expected 6");
        int?[] registers = [ax, bx, cx, dx];
        for (var i = 0; i < registers.Length; i++)
        {
            if (registers[i] is { } value)
            {
                Compare($"call {_next} {target}, register {"abcd"[i]}x", value, call.Args[i]);
            }
        }
        return call;
    }

    /// <summary>A register after an interrupt: what the handler set (the scripted answer), else what went in.</summary>
    private static int After(BoundaryCall call, string register, int index) =>
        unchecked((short)(call.Input is { ValueKind: JsonValueKind.Object } set && set.TryGetProperty(register, out var value)
            ? value.GetInt64()
            : call.Args[index].GetInt64()));

    // ---- IGameTimer ----

    // a library routine on the generator's cut list; the game always calls time(0): a null pointer
    public int Time() => unchecked((int)(Call("time", 0).Ret ?? 0));

    // the original waits for a frame by reading the display's status port until the vertical retrace begins, and
    // again until it ends: every one of those port reads is recorded
    public void WaitForFrame()
    {
        while ((Word(Call("inb", DisplayStatusPort)) & VerticalRetrace) == 0)
        {
        }
        while ((Word(Call("inb", DisplayStatusPort)) & VerticalRetrace) != 0)
        {
        }
    }

    // _bios_timeofday asked for the count is interrupt 1Ah with AH = 0: the ticks come back in CX:DX and AL says
    // whether midnight has passed
    public bool ClockTicks(out int ticks)
    {
        var call = Take("int1a.00");
        ticks = (ushort)After(call, "cx", 2) << 16 | (ushort)After(call, "dx", 3);
        return (After(call, "ax", 0) & 0xff) != 0;
    }

    /// <summary>
    /// The C# ended the program (the original calls the library's exit, CODE 0000:2c36, which closes the files and
    /// returns to DOS): every further recorded call belongs to that.
    /// </summary>
    public void ProgramEnded()
    {
        if (Next == "exit")
        {
            Take("exit");
            return;
        }
        // (DOS calls, and the video BIOS call that puts the display back into text mode on the way out)
        Assert.True(expected.Skip(_next).All(call => call.Target.StartsWith("int21.", StringComparison.Ordinal) || call.Target == "int10.00" || call.Target == "write"),
            $"call {_next + 1}: the C# ends the program, the original went on with {Next}");
        _next = expected.Count;
    }

    // ---- IFiles ----

    // the routine answers 0 for "found" and -1 for "not found"
    public bool FindFileOnDisk(string name, int drive) => Word(Call("find_file_on_disk", Text(name), drive)) != -1;

    // the library's file calls, each recorded as itself; what a read gave the original comes with its record
    public int Open(string name, OpenMode mode, int? permission = null) =>
        Word(permission is { } bits ? Call("open", Text(name), (int)mode, bits) : Call("open", Text(name), (int)mode));

    public int Read(int handle, Span<byte> buffer)
    {
        var call = Take("read");
        Compare($"call {_next} read, the file", handle, call.Args[0]);
        Compare($"call {_next} read, the number of bytes", buffer.Length, call.Args[2]);
        call.Out?.AsSpan(0, Math.Min(call.Out.Length, buffer.Length)).CopyTo(buffer);
        _lastReadBeginsWithLineFeed = Word(call) > 0 && call.Out is { Length: > 0 } && call.Out[0] == (byte)'\n';
        _lastReadFileFlags = call.FileFlags;
        return Word(call);
    }

    /// <summary>Whether the bytes the last recorded read gave began with a line feed.</summary>
    private bool _lastReadBeginsWithLineFeed;

    /// <summary>The library's note on the file after the last recorded read (its <c>_osfile</c> byte), when the record has it.</summary>
    private int? _lastReadFileFlags;

    /// <summary>The <c>_osfile</c> bit the library's read sets when the bytes it read began with a line feed.</summary>
    private const int BeganWithLineFeedFlag = 0x04;

    // Newer records carry the library's note itself (the _osfile byte after the read). Older ones hold only a read's
    // bytes after the library took the carriage returns out: a first line feed there was the first byte of the
    // file's bytes too (the library's note), or had its carriage return in front of it in the same read (no note).
    // Such a record cannot tell which.
    public bool ReadBeganWithLineFeed(int handle) =>
        _lastReadFileFlags is { } flags
            ? (flags & BeganWithLineFeedFlag) != 0
            : _lastReadBeginsWithLineFeed
                ? throw new OutsideModelException("the recorded read begins with a line feed: the record does not tell whether its carriage return was read with it")
                : false;

    public int Write(int handle, ReadOnlySpan<byte> data)
    {
        // the program's last words on the console, where the original gives up on a file: it switches the display to
        // text and prints through DOS itself (D-12), so there is no recorded "write" to hold the C#'s message against
        if (handle is 1 or 2 && Next != "write")
        {
            return data.Length;
        }
        if (data.IsEmpty)
        {
            // a write of no bytes (an empty table of a save): there is nothing behind the pointer to compare, whatever it is
            var call = Take("write");
            Compare($"call {_next} write, the file", handle, call.Args[0]);
            Compare($"call {_next} write, the number of bytes", 0, call.Args[2]);
            return Word(call);
        }
        return Word(Call("write", handle, Table(data), data.Length));
    }

    public int Close(int handle) => Word(Call("close", handle));

    public int Access(string name, int mode) => Word(Call("access", Text(name), mode));

    // the place is a long: two words on the original's stack, and in DX:AX on the way back
    public int Seek(int handle, int offset, int origin) =>
        unchecked((int)(Call("lseek", handle, offset & 0xffff, offset >> 16 & 0xffff, origin).Ret ?? 0));

    // ---- the library's thin DOS calls (_dos_open, _dos_read, _dos_close and the game's own seek), which the picture,
    //      archive and animation loaders use: recorded as the DOS interrupts they make (3Dh open, 3Fh read, 3Eh close,
    //      42h seek), each with the file's name and, for a read, the bytes DOS delivered ----
    //
    // The original reads a file a block at a time while it decodes and draws; the rewrite's loaders read a file to its
    // end first and decode it at once (D-12, D-13). So these calls are not held to their place among the other calls:
    // a file call of the C# takes the next recorded call of its kind for its file wherever that stands ahead, and the
    // drawing calls between are compared in their own order. What is compared: which files are opened and in which
    // order, every seek, and every byte read; a read behind the last block the original read of a file (up to its next
    // seek or its close) gets the rest of the game's own file when the test has the game folder (the original may stop
    // before the end, where the C# reads on: RestOfGameFile), else finds the file's end.

    /// <summary>The recorded file calls already answered out of their turn.</summary>
    private readonly HashSet<int> _answered = [];

    private const string DosOpenCall = "int21.3d", DosReadCall = "int21.3f", DosCloseCall = "int21.3e", DosSeekCall = "int21.42";

    /// <summary>The next recorded call ahead that is wanted, unless one that ends the search comes first; null when there is none.</summary>
    private BoundaryCall? AnswerAhead(Func<BoundaryCall, bool> wanted, Func<BoundaryCall, bool>? ends = null)
    {
        for (var i = _next; i < expected.Count; i++)
        {
            if (_answered.Contains(i))
            {
                continue;
            }
            if (wanted(expected[i]))
            {
                _answered.Add(i);
                return expected[i];
            }
            if (ends?.Invoke(expected[i]) == true)
            {
                return null;
            }
        }
        return null;
    }

    private static int Register(BoundaryCall call, int index) => unchecked((ushort)call.Args[index].GetInt64());

    private static bool IsFileCall(BoundaryCall call, string target, int handle) => call.Target == target && Register(call, 1) == handle;

    /// <summary>Whether DOS answered a call with its carry flag set: an error, whose number is in AX.</summary>
    private static bool Failed(BoundaryCall call) =>
        call.Input is { ValueKind: JsonValueKind.Object } answer && answer.TryGetProperty("cf", out var carry) && carry.GetInt32() != 0;

    private static string? Recorded(BoundaryCall call, string property) =>
        call.Input is { ValueKind: JsonValueKind.Object } answer && answer.TryGetProperty(property, out var value) ? value.GetString() : null;

    public int DosOpen(string name, int mode, out int handle)
    {
        var call = AnswerAhead(c => c.Target == DosOpenCall);
        Assert.True(call is not null, $"the C# opens {name}, the original opened no further file");
        if (Recorded(call!, "file") is { } file)
        {
            Assert.True(string.Equals(file, Path.GetFileName(name), StringComparison.OrdinalIgnoreCase), $"the C# opens {name}, the original opened {file}");
        }
        Assert.True((Register(call!, 0) & 0xff) == (mode & 0xff), $"the C# opens {name} with access mode {mode & 0xff}, the original with {Register(call!, 0) & 0xff}");
        var answer = After(call!, "ax", 0) & 0xffff;
        handle = Failed(call!) ? 0 : answer;
        if (!Failed(call!))
        {
            _dosFiles[handle] = new DosFile(GameFiles?.Find(name) is { } path ? File.ReadAllBytes(path) : null);
        }
        return Failed(call!) ? answer : 0;
    }

    public int DosRead(int handle, Span<byte> buffer, out int count)
    {
        var call = AnswerAhead(c => IsFileCall(c, DosReadCall, handle), c => IsFileCall(c, DosSeekCall, handle) || IsFileCall(c, DosCloseCall, handle));
        if (call is null)
        {
            // behind what the original read of this file: the rest of the game's own file when the test has the game
            // folder, else the end, as far as the C# may know
            count = RestOfGameFile(handle, buffer);
            return 0;
        }
        Assert.True(Register(call, 2) == buffer.Length, $"the C# reads {buffer.Length} bytes of file {handle} at a time, the original {Register(call, 2)}");
        var answer = After(call, "ax", 0) & 0xffff;
        if (Failed(call))
        {
            count = 0;
            return answer;
        }
        var data = Recorded(call, "data") is { } delivered ? Convert.FromBase64String(delivered) : call.Out ?? [];
        Assert.True(data.Length == answer && answer <= buffer.Length, $"a recorded read of file {handle} delivers {data.Length} bytes and says {answer}");
        data.CopyTo(buffer);
        count = answer;
        if (_dosFiles.TryGetValue(handle, out var file))
        {
            // the original's reads so far are whole blocks of the game's own file
            file.AsTheGamesFile &= answer == buffer.Length && file.Game is { } game && file.Position + answer <= game.Length
                && game.AsSpan((int)file.Position, answer).SequenceEqual(data);
            file.Position += answer;
        }
        return 0;
    }

    /// <summary>A file the C# opened through DOS.</summary>
    /// <param name="game">The bytes of the game's own file of that name, when the test has the game folder.</param>
    private sealed class DosFile(byte[]? game)
    {
        /// <summary>The game's own file of that name, or null.</summary>
        public byte[]? Game { get; } = game;

        /// <summary>The place the next read starts at.</summary>
        public long Position { get; set; }

        /// <summary>Whether every read the original made of it was a whole block of the game's own file.</summary>
        public bool AsTheGamesFile { get; set; } = game is not null;
    }

    /// <summary>The files the C# opened through DOS, by number.</summary>
    private readonly Dictionary<int, DosFile> _dosFiles = [];

    /// <summary>The game folder of the test run (COVERT_GAME_DIR), if it is set.</summary>
    private static readonly Content.GameFolder? GameFiles = Content.GameFolder.FromEnvironment();

    /// <summary>
    /// A read behind the last block the original read of a file. The animation loader reads its file to the end
    /// before it takes it apart (D-13), where the original stops at the first part that fails (no memory left for
    /// the images): the C# then needs the rest of the file to get to the same point. When every block the original
    /// read was the game's own file's (the record was made with the game's files), the rest is read from the game
    /// folder; otherwise (no game folder, or a record made without the files) the file ends here.
    /// </summary>
    /// <returns>The number of bytes put into the buffer.</returns>
    private int RestOfGameFile(int handle, Span<byte> buffer)
    {
        if (!_dosFiles.TryGetValue(handle, out var file) || !file.AsTheGamesFile || file.Game is not { } game || !RanOutOfImageMemory(handle))
        {
            return 0;
        }
        var count = (int)Math.Clamp(game.Length - file.Position, 0, buffer.Length);
        game.AsSpan((int)file.Position, count).CopyTo(buffer);
        file.Position += count;
        return count;
    }

    /// <summary>
    /// Whether the original, before it closed the file, was told by the driver that no image memory was left (the
    /// one reason the animation loader stops reading early). Older records, made without the driver's image store,
    /// end their reads for other reasons: there the file ends where the original stopped.
    /// </summary>
    private bool RanOutOfImageMemory(int handle)
    {
        for (var i = _next; i < expected.Count; i++)
        {
            var call = expected[i];
            if (IsFileCall(call, DosCloseCall, handle))
            {
                return false;
            }
            if (call.Target is "_image_memory_free" or "image_memory_free" or "sub_final_0000_019f" && (call.Ret ?? -1) == 0)
            {
                return true;
            }
        }
        return false;
    }

    public int DosClose(int handle)
    {
        var call = AnswerAhead(c => IsFileCall(c, DosCloseCall, handle));
        Assert.True(call is not null, $"the C# closes file {handle}, the original did not");
        return Failed(call!) ? After(call!, "ax", 0) & 0xffff : 0;
    }

    public int DosSeek(int handle, int offset)
    {
        var call = AnswerAhead(c => IsFileCall(c, DosSeekCall, handle));
        Assert.True(call is not null, $"the C# moves in file {handle}, the original did not");
        // the place is a long: its high word in CX, its low word in DX
        var recorded = Register(call!, 2) << 16 | Register(call!, 3);
        Assert.True(recorded == offset, $"the C# moves to byte {offset} of file {handle}, the original to byte {recorded}");
        if (_dosFiles.TryGetValue(handle, out var file))
        {
            file.Position = offset;
        }
        return Failed(call!) ? After(call!, "ax", 0) & 0xffff : 0;
    }

    // _dos_getdrive (CODE 0000:3678) is DOS function 19h, whose AL (0 = A) it returns plus one
    // _bios_disk is the BIOS's disk interrupt: service 4 (verify) of one sector, the second of track 0, with the
    // drive in DL and the BIOS's error number in AH on return; service 0 (reset)
    public int VerifyDisk(int drive)
    {
        const int verifyOneSector = 0x0401, trackZeroSectorTwo = 2;
        var call = Interrupt("int13.04", verifyOneSector, cx: trackZeroSectorTwo);
        Assert.True((Register(call, 3) & 0xff) == (drive & 0xff), $"call {_next} int13.04: drive {drive}, the original asked about {Register(call, 3) & 0xff}");
        return (int)((call.Ret ?? 0) >> 8 & 0xff);
    }

    public void ResetDisks() => Take("int13.00");

    public int CurrentDrive() => Next == "_dos_getdrive" ? Word(Call("_dos_getdrive")) : (int)((Take("int21.19").Ret ?? 0) & 0xFF) + 1;
}
