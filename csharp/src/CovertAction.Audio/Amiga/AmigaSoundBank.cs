namespace CovertAction.Audio.Amiga;

/// <summary>One sound of an Amiga sound bank: 8-bit signed samples for one Paula voice, and how it is played.</summary>
/// <param name="Period">Paula's period: the voice reads one byte every <c>Period</c> cycles of the chip's clock (a pitch, lower is higher).</param>
/// <param name="Repeats">
/// The bank's repeat word: 0 plays until stopped (the sample loops); 2 and 3 play it once and twice. The game's audio
/// interrupt counts the repeat word down at the start of the sound and at the end of each pass and stops the voice at 0,
/// so a word of <c>n</c> is <c>n - 1</c> passes (0 and 1 are not used as a count).
/// </param>
/// <param name="Data">The sample's signed bytes, an even number of them (the voice plays whole words).</param>
public sealed record AmigaSample(int Period, int Repeats, sbyte[] Data)
{
    /// <summary>The sample loops until the voice is stopped.</summary>
    public bool Endless => Repeats == 0;

    /// <summary>How many times the sample plays; 0 for an endless one.</summary>
    public int Passes => Endless ? 0 : Math.Max(Repeats - 1, 0);
}

/// <summary>
/// One sound effect of a bank: a pair of samples, the left voice's and the right voice's (mostly the same sample), and
/// the effect's priority, which decides whether it may take over a voice that is busy.
/// </summary>
/// <param name="Priority">0 to 255: an effect takes over the busy voice of the lowest priority when its own is not lower (the bank's "volume" field of the file is this).</param>
/// <param name="Left">The sample for the left voices (Paula's channels 0 and 3).</param>
/// <param name="Right">The sample for the right voices (channels 1 and 2).</param>
public sealed record AmigaEffect(int Priority, AmigaSample Left, AmigaSample Right);

/// <summary>
/// A sound bank of the Amiga version, one of gamesnd.dat, tacsnd.dat, codesnd.dat, bugsnd.dat (disk 2) and chasesnd.dat
/// (disk 3): the effects one part of the game plays, read from the player's own file (D-641).
/// </summary>
/// <remarks>
/// New code: the Amiga version's sound effects, a player's option; the PC game has none of this. The file is what the game's
/// loader relocates: big-endian words, a count of effects A and a count of samples B; A records of 10 bytes (the distance
/// from the record to its left and its right sample record, and the priority word); B sample records of 8 bytes (the period,
/// the distance from the record to the sample's data, and the repeat word); the data is a 32-bit length of which the game uses
/// the low word (the bytes) followed by that many signed bytes. The samples follow one after another and the last ends the file.
/// </remarks>
public sealed class AmigaSoundBank
{
    private const int HeaderSize = 4;
    private const int EffectRecordSize = 10;
    private const int SampleRecordSize = 8;
    private const int LengthFieldSize = 4;

    private AmigaSoundBank(IReadOnlyList<AmigaEffect> effects, IReadOnlyList<AmigaSample> samples, int end)
    {
        Effects = effects;
        Samples = samples;
        End = end;
    }

    /// <summary>The effects, numbered from 0 as the game numbers them.</summary>
    public IReadOnlyList<AmigaEffect> Effects { get; }

    /// <summary>The samples the effects use.</summary>
    public IReadOnlyList<AmigaSample> Samples { get; }

    /// <summary>The byte after the last sample's data: the file's length when no byte is left over.</summary>
    public int End { get; }

    /// <summary>Reads a bank.</summary>
    /// <param name="file">The whole file.</param>
    /// <exception cref="InvalidDataException">The file is not a sound bank (a record or a sample lies outside it).</exception>
    public static AmigaSoundBank Parse(byte[] file)
    {
        if (file.Length < HeaderSize)
        {
            throw new InvalidDataException("too short for a sound bank");
        }
        int effectCount = Word(file, 0), sampleCount = Word(file, 2);
        var sampleTable = HeaderSize + effectCount * EffectRecordSize;
        if (sampleTable + sampleCount * SampleRecordSize > file.Length)
        {
            throw new InvalidDataException("the records run past the end of the file");
        }
        var end = sampleTable + sampleCount * SampleRecordSize;
        var samples = new List<AmigaSample>(sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            var record = sampleTable + i * SampleRecordSize;
            var field = record + Int(file, record + 2);
            // the game reads a 32-bit length at the data's address and uses its low word; the bytes follow it
            if (field < 0 || field + LengthFieldSize > file.Length)
            {
                throw new InvalidDataException($"sample {i} starts outside the file");
            }
            var length = Word(file, field + 2);
            var data = field + LengthFieldSize;
            if (data + length > file.Length)
            {
                throw new InvalidDataException($"sample {i} runs past the end of the file");
            }
            // the voice plays whole words: an odd last byte is not played
            var played = Array.ConvertAll(file[data..(data + (length & ~1))], b => (sbyte)b);
            samples.Add(new AmigaSample(Word(file, record), Word(file, record + 6), played));
            end = Math.Max(end, data + length);
        }
        var effects = new List<AmigaEffect>(effectCount);
        for (var i = 0; i < effectCount; i++)
        {
            var record = HeaderSize + i * EffectRecordSize;
            effects.Add(new AmigaEffect(Word(file, record + 8), SampleAt(Int(file, record)), SampleAt(Int(file, record + 4))));

            AmigaSample SampleAt(int distance)
            {
                var offset = record + distance - sampleTable;
                if (offset < 0 || offset % SampleRecordSize != 0 || offset / SampleRecordSize >= samples.Count)
                {
                    throw new InvalidDataException($"effect {i} names a sample record that is not there");
                }
                return samples[offset / SampleRecordSize];
            }
        }
        return new AmigaSoundBank(effects, samples, end);
    }

    private static int Word(byte[] file, int at) => (file[at] << 8) | file[at + 1];

    private static int Int(byte[] file, int at) => (file[at] << 24) | (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
}
