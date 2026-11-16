namespace CovertAction.Audio.Amiga;

/// <summary>One instrument of a Game Music Creator module: 8-bit signed samples and how loud and how looped they play.</summary>
/// <param name="Data">The sample's bytes, signed 8-bit, as the Amiga's sound chip (Paula) plays them.</param>
/// <param name="Volume">The volume the instrument starts a note with, 0 (silent) to 64 (Paula's full volume).</param>
/// <param name="LoopLength">The part at the end of the sample that repeats, in bytes; 0: the sample plays once.</param>
public sealed record GmcSample(sbyte[] Data, int Volume, int LoopLength)
{
    /// <summary>Where the repeated part starts: the end of the sample less <see cref="LoopLength"/>.</summary>
    public int LoopStart => Data.Length - LoopLength;

    /// <summary>The sample repeats at its end.</summary>
    public bool Loops => LoopLength > 0;
}

/// <summary>One cell of a pattern: what one voice does on one row.</summary>
/// <param name="Period">The note's Paula period (113 to 856 on the Amiga; the sample's rate is the chip's clock divided by it); 0: no new note.</param>
/// <param name="Sample">The instrument, 1 to 15; 0: none named (the voice keeps its instrument).</param>
/// <param name="Command">The effect command, 0 to 15. The tunes use only 0 (none) and 8 (set the speed).</param>
/// <param name="Parameter">The command's parameter; for 8 the new speed in ticks per row.</param>
/// <param name="IsNoteCut">The cell is 0xFFFE: the voice falls silent.</param>
public readonly record struct GmcCell(int Period, int Sample, int Command, int Parameter, bool IsNoteCut);

/// <summary>
/// A Game Music Creator module, the format of the Amiga version's music (thememusic.dat, finalmusic.dat, spy6music.dat):
/// 15 instrument headers of 16 bytes, three zero bytes, the number of orders, 100 order words (the pattern number times
/// 1024), the patterns (64 rows of 4 voices, 4 bytes a cell), then the instruments' samples one after the other
/// (csharp/docs/AMIGA-MUSIC.md has the evidence).
/// </summary>
/// <remarks>
/// New code: the Amiga version's own music, a player's option (D-640); the PC game has none of it. The loader reads
/// the player's own files and keeps nothing of them in the program.
/// </remarks>
public sealed class GmcTune
{
    /// <summary>Instruments in a module.</summary>
    public const int SampleCount = 15;

    /// <summary>Voices (Paula's four channels).</summary>
    public const int VoiceCount = 4;

    /// <summary>Rows in a pattern.</summary>
    public const int RowsPerPattern = 64;

    /// <summary>
    /// The pattern number that is not a pattern: an order that names it ends the song. The files' last order is 63 and the
    /// files hold no pattern 63 (they hold 4 or 5 patterns), as other players' loaders treat it.
    /// </summary>
    public const int EndPattern = 63;

    private const int SampleHeaderLength = 16;
    private const int MaxOrders = 100;
    private const int ZeroBytesAt = SampleCount * SampleHeaderLength;
    private const int OrderCountAt = ZeroBytesAt + 3;
    private const int OrdersAt = OrderCountAt + 1;
    private const int HeaderLength = OrdersAt + MaxOrders * 2;
    private const int OrderUnit = 1024;
    private const int CellLength = 4;
    private const int PatternLength = RowsPerPattern * VoiceCount * CellLength;
    private const int NoteCutWord = 0xFFFE;

    /// <summary>A loop of this many words or fewer is the header's way to say "no loop".</summary>
    private const int NoLoopWords = 2;

    private readonly GmcCell[][] _patterns;

    private GmcTune(GmcSample[] samples, int[] orders, GmcCell[][] patterns, int trailingBytes)
    {
        Samples = samples;
        Orders = orders;
        _patterns = patterns;
        TrailingBytes = trailingBytes;
    }

    /// <summary>The 15 instruments; one without data is empty.</summary>
    public IReadOnlyList<GmcSample> Samples { get; }

    /// <summary>The order list: the pattern each order plays, in order; <see cref="EndPattern"/> ends the song.</summary>
    public IReadOnlyList<int> Orders { get; }

    /// <summary>How many patterns the module holds (the highest pattern number the orders name, plus one; <see cref="EndPattern"/> not counted).</summary>
    public int PatternCount => _patterns.Length;

    /// <summary>Bytes of the file after the last sample; 0 in a module read whole.</summary>
    public int TrailingBytes { get; }

    /// <summary>The cell of a voice on a row of a pattern.</summary>
    /// <param name="pattern">The pattern, 0 to <see cref="PatternCount"/> less one.</param>
    /// <param name="row">The row, 0 to 63.</param>
    /// <param name="voice">The voice, 0 to 3.</param>
    public GmcCell Cell(int pattern, int row, int voice) => _patterns[pattern][row * VoiceCount + voice];

    /// <summary>Reads a module from its bytes.</summary>
    /// <param name="bytes">The file.</param>
    /// <exception cref="InvalidDataException">The bytes are not a module: too short, the three zero bytes missing, an order that is not a pattern number, or patterns or samples cut off.</exception>
    public static GmcTune Parse(byte[] bytes)
    {
        if (bytes.Length < HeaderLength || bytes[ZeroBytesAt] != 0 || bytes[ZeroBytesAt + 1] != 0 || bytes[ZeroBytesAt + 2] != 0)
        {
            throw new InvalidDataException("not a Game Music Creator module (the header is wrong)");
        }
        var orders = ReadOrders(bytes);
        var patternCount = orders.Where(p => p != EndPattern).Select(p => p + 1).DefaultIfEmpty(0).Max();
        var position = HeaderLength;
        if (bytes.Length - position < patternCount * PatternLength)
        {
            throw new InvalidDataException("the patterns are cut off");
        }
        var patterns = new GmcCell[patternCount][];
        for (var p = 0; p < patternCount; p++)
        {
            patterns[p] = new GmcCell[RowsPerPattern * VoiceCount];
            for (var cell = 0; cell < patterns[p].Length; cell++, position += CellLength)
            {
                patterns[p][cell] = ReadCell(bytes, position);
            }
        }
        var samples = new GmcSample[SampleCount];
        for (var s = 0; s < SampleCount; s++)
        {
            samples[s] = ReadSample(bytes, s, ref position);
        }
        return new GmcTune(samples, orders, patterns, bytes.Length - position);
    }

    private static int[] ReadOrders(byte[] bytes)
    {
        var count = bytes[OrderCountAt];
        if (count > MaxOrders)
        {
            throw new InvalidDataException($"a Game Music Creator module has at most {MaxOrders} orders, not {count}");
        }
        var orders = new int[count];
        for (var i = 0; i < count; i++)
        {
            var word = bytes[OrdersAt + 2 * i] << 8 | bytes[OrdersAt + 2 * i + 1];
            if (word % OrderUnit != 0)
            {
                throw new InvalidDataException($"order {i} is {word}, not a pattern number times {OrderUnit}");
            }
            orders[i] = word / OrderUnit;
        }
        return orders;
    }

    /// <summary>
    /// One instrument: its header (+0 offset, unused; +4 length in words; +6 zero; +7 volume; +8 address, unused; +12 loop
    /// length in words; +14 data start, unused) and then its bytes at <paramref name="position"/>, which moves past them.
    /// </summary>
    private static GmcSample ReadSample(byte[] bytes, int index, ref int position)
    {
        var header = index * SampleHeaderLength;
        var length = 2 * (bytes[header + 4] << 8 | bytes[header + 5]);
        var volume = Math.Min(bytes[header + 7], (byte)64);
        var loopWords = bytes[header + 12] << 8 | bytes[header + 13];
        if (bytes.Length - position < length)
        {
            throw new InvalidDataException($"sample {index + 1} is cut off");
        }
        var data = new sbyte[length];
        Buffer.BlockCopy(bytes, position, data, 0, length);
        position += length;
        var loops = loopWords > NoLoopWords && loopWords * 2 <= length;
        return new GmcSample(data, volume, loops ? loopWords * 2 : 0);
    }

    private static GmcCell ReadCell(byte[] bytes, int position)
    {
        var first = bytes[position] << 8 | bytes[position + 1];
        var instrumentAndCommand = bytes[position + 2];
        return new GmcCell(first & 0x0FFF, instrumentAndCommand >> 4, instrumentAndCommand & 0x0F, bytes[position + 3], first == NoteCutWord);
    }
}
