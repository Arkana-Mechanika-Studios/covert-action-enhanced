using CovertAction.Audio;
using CovertAction.Audio.Synthesis;
using CovertAction.Audio.Tandy;

namespace CovertAction.Audio.Tests;

/// <summary>The SN76496 voice, and the Tandy driver on a small tune of its own (the player's tunes are in the oracle tests).</summary>
public sealed class TandyTests
{
    private const int Rate = 44100;

    private static short[] Render(Sn76496Voice voice, int frames)
    {
        var buffer = new short[2 * frames];
        voice.Render(buffer);
        return buffer;
    }

    private static int Cycles(short[] stereo)
    {
        var count = 0;
        for (var i = 2; i < stereo.Length; i += 2)
        {
            if (stereo[i - 2] < 0 && stereo[i] >= 0)
            {
                count++;
            }
        }
        return count;
    }

    [Fact]
    public void AToneSoundsAtTheClockDividedByThirtyTwiceItsPeriod()
    {
        var voice = new Sn76496Voice(Rate);
        voice.Write(0x90);       // tone 1 at full level
        voice.Write(0x8a);       // period 0x0fa (250): low bits a
        voice.Write(0x0f);       // high bits f
        Render(voice, Rate / 10);
        // 3,579,545 / (32 * 250) = 447.4 Hz
        Assert.InRange(Cycles(Render(voice, Rate)), 440, 455);
    }

    [Fact]
    public void AttenuationFifteenIsSilentAndEachStepIsTwoDecibels()
    {
        var voice = new Sn76496Voice(Rate);
        voice.Write(0x8a);
        voice.Write(0x0f);
        voice.Write(0x9f);
        Assert.All(Render(voice, 2000), s => Assert.Equal(0, s));
        voice.Write(0x90);
        var loud = Render(voice, 4000).Max(s => (int)s);
        voice.Write(0x95);       // ten decibels down
        var soft = Render(voice, 4000).Max(s => (int)s);
        Assert.InRange(soft / (double)loud, 0.28, 0.35);
    }

    [Fact]
    public void TheNoiseChannelMakesNoiseAtItsRate()
    {
        var voice = new Sn76496Voice(Rate);
        voice.Write(0xf0);       // noise at full level
        voice.Write(0xe4);       // white noise, the fastest rate
        var wave = Render(voice, Rate);
        Assert.True(wave.Max(s => (int)s) > 500);
        Assert.True(wave.Min(s => (int)s) < -500);
    }

    private sealed class Recorder : ISoundChip, ISoundTimer
    {
        public List<byte> Bytes { get; } = [];

        public int Divisor { get; private set; } = -1;

        public void Write(byte value) => Bytes.Add(value);

        public void SetInterruptDivisor(int divisor) => Divisor = divisor;
    }

    /// <summary>A data segment with one tune at 0x17ec (where command 19 starts tone 1): volume 4, a note of 3 ticks, a rest of 2, a note of 1, the end.</summary>
    private static byte[] SmallTune()
    {
        var data = new byte[0x10000];
        byte[] tune = [0xc0, 0x04, 0x03, 0x2a, 0x01, 0x02, 0xff, 0xff, 0x01, 0x05, 0x00, 0x00];
        tune.CopyTo(data, 0x17ec);
        return data;
    }

    [Fact]
    public void ANoteWritesItsAttenuationAndItsPeriodOnEachTickAndTheEndMutesTheChannel()
    {
        var recorder = new Recorder();
        var driver = new TandySoundDriver(SmallTune(), recorder, recorder);
        driver.Start();
        Assert.Equal(TandySoundDriver.InterruptDivisor, recorder.Divisor);
        driver.Play(19, 0, 0);
        List<byte[]> ticks = [];
        for (var i = 0; i < 7; i++)
        {
            recorder.Bytes.Clear();
            driver.TimerInterrupt();
            ticks.Add([.. recorder.Bytes]);
        }
        // attenuation 0x94 (tone 1, level 4), period 0x12a: low bits a, high bits 0x12; the rest mutes; the last note's period is 5
        byte[] note = [0x94, 0x8a, 0x12];
        Assert.Equal([note, note, note, [0x9f], [], [0x94, 0x85, 0x00], [0x9f]], ticks);
        Assert.True(driver.IsQuiet);
    }

    [Fact]
    public void ASlideMovesThePeriodWithinTenBits()
    {
        var recorder = new Recorder();
        var data = new byte[0x10000];
        // slide every tick by 0x20, a note of 5 ticks at period 0x3f0, the end
        byte[] tune = [0x90, 0x01, 0x20, 0x00, 0x05, 0xf0, 0x03, 0x00];
        tune.CopyTo(data, 0x17ec);
        var driver = new TandySoundDriver(data, recorder, recorder);
        driver.Start();
        driver.Play(19, 0, 0);
        driver.TimerInterrupt();
        recorder.Bytes.Clear();
        driver.TimerInterrupt();
        // 0x3f0 + 0x20 = 0x410, kept to ten bits: 0x010 (low bits 0, high bits 1)
        Assert.Equal([0x93, 0x80, 0x01], recorder.Bytes);
    }

    [Fact]
    public void ALoopRepeatsItsBlockAsOftenAsItsCountSaysAndAnEmptyCountOnlyMarksIt()
    {
        var recorder = new Recorder();
        var data = new byte[0x10000];
        // a mark, a note of 1 tick (period 1), the loop command with a count of 2, a note (period 2), the end
        byte[] tune = [0x80, 0x00, 0x01, 0x01, 0x00, 0x80, 0x02, 0x01, 0x02, 0x00, 0x00];
        tune.CopyTo(data, 0x17ec);
        var driver = new TandySoundDriver(data, recorder, recorder);
        driver.Start();
        driver.Play(19, 0, 0);
        List<byte> periods = [];
        for (var i = 0; i < 8; i++)
        {
            recorder.Bytes.Clear();
            driver.TimerInterrupt();
            if (recorder.Bytes.Count == 3)
            {
                periods.Add((byte)(recorder.Bytes[1] & 0x0f));
            }
        }
        // the block plays once, then twice more (a count of 2), then the second note
        Assert.Equal([1, 1, 1, 2], periods);
    }

    [Fact]
    public void ACommandBeyondTheTableDoesNothingAndANegativeOneIsRefused()
    {
        var recorder = new Recorder();
        var driver = new TandySoundDriver(SmallTune(), recorder, recorder);
        driver.Start();
        recorder.Bytes.Clear();
        Assert.False(driver.Play(47, 0, 0));
        Assert.Empty(recorder.Bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => driver.Play(-1, 0, 0));
    }
}
