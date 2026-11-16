using CovertAction.Audio.Amiga;

namespace CovertAction.Audio.Tests;

/// <summary>
/// The Amiga version's music (D-640, csharp/docs/AMIGA-MUSIC.md): a Game Music Creator module read from bytes, the tick
/// player over four Paula voices, and the driver's commands. The modules here are made in the tests (no Amiga file or
/// sample of one is in the repository).
/// </summary>
public sealed class AmigaMusicTests
{
    private const int Rate = 44100;

    /// <summary>Frames in one 20 ms tick at <see cref="Rate"/>.</summary>
    private const int TickFrames = Rate / 50;

    private const int PatternBytes = 1024;
    private const int HeaderBytes = 444;

    /// <summary>A module under construction: orders, cells and samples, written into bytes as the file has them.</summary>
    private sealed class ModuleBuilder
    {
        private readonly List<int> _orders = [];
        private readonly List<(sbyte[] Data, int Volume, int LoopWords)> _samples = [];
        private readonly Dictionary<(int Pattern, int Row, int Voice), byte[]> _cells = [];

        public ModuleBuilder Orders(params int[] patterns)
        {
            _orders.AddRange(patterns);
            return this;
        }

        public ModuleBuilder Sample(sbyte[] data, int volume = 64, int loopWords = 1)
        {
            _samples.Add((data, volume, loopWords));
            return this;
        }

        /// <summary>A note: a period and an instrument (1 to 15), and a command with its parameter.</summary>
        public ModuleBuilder Cell(int pattern, int row, int voice, int period, int sample, int command = 0, int parameter = 0)
        {
            _cells[(pattern, row, voice)] = [(byte)(period >> 8), (byte)period, (byte)(sample << 4 | command), (byte)parameter];
            return this;
        }

        public ModuleBuilder Cut(int pattern, int row, int voice)
        {
            _cells[(pattern, row, voice)] = [0xFF, 0xFE, 0, 0];
            return this;
        }

        public byte[] Build()
        {
            var patterns = _orders.Where(p => p != 63).Select(p => p + 1).DefaultIfEmpty(0).Max();
            var all = new byte[HeaderBytes + patterns * PatternBytes];
            for (var s = 0; s < _samples.Count; s++)
            {
                var (data, volume, loopWords) = _samples[s];
                var header = s * 16;
                all[header + 4] = (byte)(data.Length / 2 >> 8);
                all[header + 5] = (byte)(data.Length / 2);
                all[header + 7] = (byte)volume;
                all[header + 12] = (byte)(loopWords >> 8);
                all[header + 13] = (byte)loopWords;
            }
            all[243] = (byte)_orders.Count;
            for (var o = 0; o < _orders.Count; o++)
            {
                var word = _orders[o] * 1024;
                all[244 + 2 * o] = (byte)(word >> 8);
                all[244 + 2 * o + 1] = (byte)word;
            }
            foreach (var ((pattern, row, voice), cell) in _cells)
            {
                cell.CopyTo(all, HeaderBytes + pattern * PatternBytes + (row * 4 + voice) * 4);
            }
            return [.. all, .. _samples.SelectMany(s => s.Data.Select(b => (byte)b))];
        }

        public GmcTune Tune() => GmcTune.Parse(Build());
    }

    /// <summary>A sample of 16 words, every byte <paramref name="value"/>.</summary>
    private static sbyte[] Flat(sbyte value, int words = 8) => Enumerable.Repeat(value, words * 2).ToArray();

    /// <summary>A sample that rises by one a byte, from 0 to 127.</summary>
    private static sbyte[] Ramp() => Enumerable.Range(0, 128).Select(i => (sbyte)i).ToArray();

    private static short[] Play(GmcPlayer player, int frames)
    {
        var buffer = new short[2 * frames];
        player.Render(buffer);
        return buffer;
    }

    [Fact]
    public void AModuleIsReadAsTheFileHasIt()
    {
        var tune = new ModuleBuilder().Orders(0, 1, 63)
            .Sample(Flat(10), volume: 48).Sample(Flat(-5, 4), volume: 64, loopWords: 4)
            .Cell(1, 5, 2, period: 428, sample: 2, command: 8, parameter: 3).Cut(0, 63, 3).Tune();
        Assert.Equal([0, 1, 63], tune.Orders);
        Assert.Equal(2, tune.PatternCount);
        Assert.Equal(0, tune.TrailingBytes);
        Assert.Equal(15, tune.Samples.Count);
        Assert.Equal(16, tune.Samples[0].Data.Length);
        Assert.Equal(48, tune.Samples[0].Volume);
        Assert.False(tune.Samples[0].Loops);
        Assert.Equal(8, tune.Samples[1].Data.Length);
        Assert.Equal(8, tune.Samples[1].LoopLength);
        Assert.Equal(0, tune.Samples[1].LoopStart);
        Assert.Empty(tune.Samples[14].Data);
        Assert.Equal(new GmcCell(428, 2, 8, 3, false), tune.Cell(1, 5, 2));
        Assert.True(tune.Cell(0, 63, 3).IsNoteCut);
        Assert.Equal(new GmcCell(0, 0, 0, 0, false), tune.Cell(0, 0, 0));
    }

    [Fact]
    public void ALoopOfOneOrTwoWordsMeansNoLoop()
    {
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(1, 8), loopWords: 1).Sample(Flat(1, 8), loopWords: 2).Sample(Flat(1, 8), loopWords: 3).Tune();
        Assert.False(tune.Samples[0].Loops);
        Assert.False(tune.Samples[1].Loops);
        Assert.True(tune.Samples[2].Loops);
        Assert.Equal(6, tune.Samples[2].LoopLength);
    }

    [Fact]
    public void BytesThatAreNoModuleAreRefused()
    {
        var good = new ModuleBuilder().Orders(0).Sample(Flat(1)).Build();
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(good[..300]));
        var zeros = (byte[])good.Clone();
        zeros[241] = 1;
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(zeros));
        var order = (byte[])good.Clone();
        order[245] = 7;
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(order));
        var orders = (byte[])good.Clone();
        orders[243] = 101;
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(orders));
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(good[..^4]));
        Assert.Throws<InvalidDataException>(() => GmcTune.Parse(good[..(HeaderBytes + 500)]));
    }

    [Fact]
    public void BytesAfterTheLastSampleAreCounted()
    {
        var tune = GmcTune.Parse([.. new ModuleBuilder().Orders(0).Sample(Flat(1)).Build(), 1, 2, 3]);
        Assert.Equal(3, tune.TrailingBytes);
    }

    [Fact]
    public void ThePeriodSetsTheRateAsTheChipsClockDividedByIt()
    {
        Assert.Equal(3546895.0 / 428, GmcPlayer.VoiceRate(428), 6);
        Assert.Equal(8287.0, GmcPlayer.VoiceRate(428), 0);
        // a ramp played at period 428 at 44.1 kHz advances 0.18792 bytes a frame, and the linear reading follows the ramp exactly
        var tune = new ModuleBuilder().Orders(0).Sample(Ramp()).Cell(0, 0, 0, period: 428, sample: 1).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false, lowPass: false);
        var frames = Play(player, 100);
        var step = 3546895.0 / 428 / Rate;
        foreach (var n in new[] { 0, 10, 50, 99 })
        {
            // left: voice 0 alone, volume 64 of 64, the output gain 0.4
            var expected = 0.4 * (n * step) / 128 * short.MaxValue;
            Assert.InRange((double)frames[2 * n], expected - 1.5, expected + 1.5);
        }
        Assert.Equal(428, player.PeriodOf(0));
        Assert.Equal(64, player.VolumeOf(0));
    }

    [Fact]
    public void VoicesZeroAndThreeAreLeftAndOneAndTwoAreRight()
    {
        foreach (var (voice, left) in new[] { (0, true), (1, false), (2, false), (3, true) })
        {
            var tune = new ModuleBuilder().Orders(0).Sample(Flat(64)).Cell(0, 0, voice, 428, 1).Tune();
            var frames = Play(new GmcPlayer(tune, Rate, loops: false, lowPass: false), 10);
            var (near, far) = left ? (frames[0], frames[1]) : (frames[1], frames[0]);
            Assert.InRange(near, (short)6500, (short)6560);
            Assert.InRange(far, (short)1950, (short)1980);
        }
    }

    [Fact]
    public void ARowLastsSixTicksOfTwentyMillisecondsUntilTheTuneSetsTheSpeed()
    {
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(1)).Cell(0, 2, 0, 0, 0, command: 8, parameter: 3).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false);
        Assert.Equal(6, player.Speed);
        Play(player, 6 * TickFrames * 2 - 1);
        Assert.Equal((0, 2), (player.Order, player.Row));
        Play(player, 1);
        Assert.Equal(6, player.Speed);
        // row 2 sets the speed to 3 and lasts 3 ticks itself
        Play(player, TickFrames);
        Assert.Equal(3, player.Speed);
        Assert.Equal(2, player.Row);
        Play(player, 2 * TickFrames);
        Assert.Equal(3, player.Row);
        Assert.Equal(12 + 3, player.Ticks);
        // the next rows last 3 ticks
        Play(player, 3 * TickFrames);
        Assert.Equal(4, player.Row);
    }

    [Fact]
    public void TicksFallAtFiftyAMinuteSecondWhateverTheRate()
    {
        foreach (var rate in new[] { 22050, 44100, 48000 })
        {
            var tune = new ModuleBuilder().Orders(0).Sample(Flat(1)).Tune();
            var player = new GmcPlayer(tune, rate, loops: false);
            var buffer = new short[2 * rate];
            player.Render(buffer);
            // one second: ticks 0 to 49 started (tick 50 starts at the first frame of the next second)
            Assert.Equal(50, player.Ticks);
        }
    }

    [Fact]
    public void ANoteCutSilencesTheVoice()
    {
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(64), loopWords: 4)
            .Cell(0, 0, 0, 428, 1).Cut(0, 1, 0).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false, lowPass: false);
        Assert.NotEqual(0, Play(player, 6 * TickFrames - 100)[200]);
        Assert.True(player.Sounds(0));
        var after = Play(player, 6 * TickFrames);
        Assert.False(player.Sounds(0));
        Assert.All(after[300..], sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void AnInstrumentWithoutANoteChangesTheVolumeButDoesNotRestart()
    {
        var tune = new ModuleBuilder().Orders(0).Sample(Ramp(), volume: 64).Sample(Ramp(), volume: 16)
            .Cell(0, 0, 0, 428, 1).Cell(0, 1, 0, 0, 2).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false, lowPass: false);
        Play(player, 6 * TickFrames);
        Assert.Equal(64, player.VolumeOf(0));
        Play(player, 1);
        Assert.Equal(16, player.VolumeOf(0));
        Assert.Equal(428, player.PeriodOf(0));
    }

    [Fact]
    public void ASampleThatDoesNotLoopStopsAtItsEnd()
    {
        // 16 bytes at period 428: ends after about 85 frames
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(100)).Cell(0, 0, 0, 428, 1).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false, lowPass: false);
        var frames = Play(player, 400);
        Assert.NotEqual(0, frames[2 * 20]);
        Assert.Equal(0, frames[2 * 200]);
        Assert.False(player.Sounds(0));
    }

    [Fact]
    public void ALoopingSampleRepeatsItsEnd()
    {
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(100), loopWords: 4).Cell(0, 0, 0, 428, 1).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false, lowPass: false);
        var frames = Play(player, 4000);
        Assert.NotEqual(0, frames[2 * 3000]);
        Assert.True(player.Sounds(0));
    }

    [Fact]
    public void TheEndPatternEndsTheSongAndALoopingSongStartsAgain()
    {
        // a pattern of 64 rows of 6 ticks, then order 63
        var builder = () => new ModuleBuilder().Orders(0, 63).Sample(Flat(64), loopWords: 4).Cell(0, 0, 0, 428, 1).Tune();
        const int songFrames = 64 * 6 * TickFrames;
        var once = new GmcPlayer(builder(), Rate, loops: false, lowPass: false);
        Play(once, songFrames - 10);
        Assert.False(once.IsFinished);
        Play(once, 2 * TickFrames);
        Assert.True(once.IsFinished);
        Assert.All(Play(once, 1000), sample => Assert.Equal(0, sample));

        var again = new GmcPlayer(builder(), Rate, loops: true, lowPass: false);
        Play(again, songFrames + 3 * TickFrames);
        Assert.False(again.IsFinished);
        Assert.Equal((0, 0), (again.Order, again.Row));
        // the first row plays its note again
        var frames = Play(again, 6 * TickFrames);
        Assert.NotEqual(0, frames[100]);
    }

    [Fact]
    public void TheListRunningOutEndsTheSongToo()
    {
        var tune = new ModuleBuilder().Orders(0, 0).Sample(Flat(1)).Tune();
        var player = new GmcPlayer(tune, Rate, loops: false);
        Play(player, 2 * 64 * 6 * TickFrames + 2 * TickFrames);
        Assert.True(player.IsFinished);
    }

    [Fact]
    public void ALoopingSongStartsAgainAtTheDefaultSpeed()
    {
        // rows 0 to 4 last 6 ticks, rows 5 to 63 two ticks each: 148 ticks, then the song starts again
        var tune = new ModuleBuilder().Orders(0).Sample(Flat(1)).Cell(0, 5, 0, 0, 0, command: 8, parameter: 2).Tune();
        var player = new GmcPlayer(tune, Rate, loops: true);
        Play(player, 148 * TickFrames);
        Assert.Equal(2, player.Speed);
        Play(player, TickFrames);
        Assert.Equal(6, player.Speed);
        Assert.Equal((0, 0), (player.Order, player.Row));
    }

    private static AmigaTunes ThreeTunes() => new(
        new ModuleBuilder().Orders(0).Sample(Flat(10), loopWords: 4).Cell(0, 0, 0, 428, 1).Tune(),
        new ModuleBuilder().Orders(0).Sample(Flat(20), loopWords: 4).Cell(0, 0, 0, 428, 1).Tune(),
        new ModuleBuilder().Orders(0).Sample(Flat(30), loopWords: 4).Cell(0, 0, 0, 428, 1).Tune());

    [Fact]
    public void TheCommandsPlayTheTunesAsMapped()
    {
        var tunes = ThreeTunes();
        var driver = new AmigaMusicDriver(tunes, Rate);
        Assert.Same(tunes.Theme, driver.TuneOf(2));
        Assert.Same(tunes.Spy, driver.TuneOf(39));
        Assert.Same(tunes.Spy, driver.TuneOf(40));
        Assert.Same(tunes.Final, driver.TuneOf(41));
        Assert.Same(tunes.Spy, driver.TuneOf(42));
        Assert.Same(tunes.Final, driver.TuneOf(43));
        Assert.Null(driver.TuneOf(6));
        Assert.Null(driver.TuneOf(44));
    }

    [Fact]
    public void WhereTheAmigaHadNoMusicTheCommandsPlayNothing()
    {
        var tunes = ThreeTunes();
        var driver = new AmigaMusicDriver(tunes, Rate, onlyWhereTheAmigaHadMusic: true);
        Assert.Same(tunes.Theme, driver.TuneOf(2));
        Assert.All(new[] { 39, 40, 41, 42, 43 }, command => Assert.Null(driver.TuneOf(command)));
        driver.PlaySound(2);
        Assert.True(driver.Playing);
        driver.PlaySound(39);
        Assert.False(driver.Playing);
    }

    [Fact]
    public void TheDriverPlaysStopsPausesAndResumes()
    {
        var driver = new AmigaMusicDriver(ThreeTunes(), Rate, lowPass: false);
        var buffer = new short[2000];
        driver.Render(buffer);
        Assert.All(buffer, sample => Assert.Equal(0, sample));
        driver.PlaySound(2);
        Assert.True(driver.Playing);
        driver.Render(buffer);
        var theme = buffer[100];
        Assert.NotEqual(0, theme);
        // the pause is silent and the tune goes on where it was
        driver.PlaySound(37);
        driver.Render(buffer);
        Assert.All(buffer, sample => Assert.Equal(0, sample));
        driver.PlaySound(38);
        driver.Render(buffer);
        Assert.NotEqual(0, buffer[100]);
        // another tune replaces it
        driver.PlaySound(41);
        driver.Render(buffer);
        Assert.NotEqual(theme, buffer[100]);
        // an effect's command is not the music's business
        driver.PlaySound(6);
        Assert.True(driver.Playing);
        driver.PlaySound(1);
        Assert.False(driver.Playing);
        driver.Render(buffer);
        Assert.All(buffer, sample => Assert.Equal(0, sample));
        driver.PlaySound(40, 1, 2);
        Assert.True(driver.Playing);
        driver.PlaySound(0);
        Assert.False(driver.Playing);
    }
}
