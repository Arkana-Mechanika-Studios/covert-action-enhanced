using CovertAction.Platform;

namespace CovertAction.Audio.Tests;

/// <summary>
/// The music and the effects on two drivers (D-620, csharp/docs/SOUND-MIXING.md): the commands' roles as the drivers'
/// command tables give them, the routing of each command with all its values, and the mix of two sources into one.
/// </summary>
public sealed class SoundMixingTests
{
    /// <summary>A sound driver that records the commands it gets, each as the values it was given.</summary>
    private sealed class RecordingDriver(string name, List<string> log) : ISoundDriver
    {
        public void PlaySound(int effect) => log.Add($"{name} {effect}");

        public void PlaySound(int effect, int parameter) => log.Add($"{name} {effect} {parameter}");

        public void PlaySound(int effect, int mode, int variant) => log.Add($"{name} {effect} {mode} {variant}");
    }

    /// <summary>A source that renders the same left and right sample into every frame.</summary>
    private sealed class Constant(short left, short right) : IPcmSource
    {
        public void Render(Span<short> interleavedStereo)
        {
            for (var i = 0; i < interleavedStereo.Length; i += 2)
            {
                interleavedStereo[i] = left;
                interleavedStereo[i + 1] = right;
            }
        }
    }

    /// <summary>A source that renders a rising count, so that every sample differs.</summary>
    private sealed class Ramp(int step) : IPcmSource
    {
        private int _next;

        public void Render(Span<short> interleavedStereo)
        {
            for (var i = 0; i < interleavedStereo.Length; i++)
            {
                interleavedStereo[i] = (short)(_next += step);
            }
        }
    }

    /// <summary>The table of SOUND-MIXING.md, command by command: the songs are music; stop, end, pause, resume and what lies outside the common table go to both.</summary>
    [Fact]
    public void TheSongsAreMusicTheStopsGoToBothAndTheRestAreEffects()
    {
        int[] music = [2, 39, 40, 41, 42, 43];
        int[] both = [0, 1, 37, 38];
        for (var command = -1; command <= 60; command++)
        {
            var expected = music.Contains(command) ? SoundRole.Music
                : both.Contains(command) || command < 0 || command > 46 ? SoundRole.Both
                : SoundRole.Effect;
            Assert.Equal(expected, SoundCommands.RoleOf(command));
        }
        // the game's own effects: the engines, the aeroplane, the gunfire, the workbench, the teleprinter
        Assert.All(new[] { 26, 27, 35, 36, 44, 28, 31, 45, 46 }, c => Assert.Equal(SoundRole.Effect, SoundCommands.RoleOf(c)));
    }

    [Fact]
    public void EachCommandReachesItsDriverWithAllItsValuesTheMusicsFirst()
    {
        var log = new List<string>();
        var split = new SplitSoundDriver(new RecordingDriver("music", log), new RecordingDriver("effects", log));

        split.PlaySound(2);
        split.PlaySound(26, 3);
        split.PlaySound(44, 5, 2);
        split.PlaySound(39);
        split.PlaySound(37);
        split.PlaySound(38);
        split.PlaySound(1);
        split.PlaySound(0, 0);
        split.PlaySound(50);

        Assert.Equal(
            [
                "music 2", "effects 26 3", "effects 44 5 2", "music 39",
                "music 37", "effects 37", "music 38", "effects 38", "music 1", "effects 1", "music 0 0", "effects 0 0",
                "music 50", "effects 50",
            ],
            log);
    }

    [Fact]
    public void APartWithoutACardIsNotPlayed()
    {
        var log = new List<string>();
        var effectsOnly = new SplitSoundDriver(null, new RecordingDriver("effects", log));
        effectsOnly.PlaySound(2);
        effectsOnly.PlaySound(6);
        effectsOnly.PlaySound(1);
        var musicOnly = new SplitSoundDriver(new RecordingDriver("music", log), null);
        musicOnly.PlaySound(6);
        musicOnly.PlaySound(41);
        Assert.Equal(["effects 6", "effects 1", "music 41"], log);
    }

    [Fact]
    public void TheMixIsTheSumOfTheSourcesHeldAtTheSixteenBitLimits()
    {
        var buffer = new short[8];
        new PcmMixer(new Constant(1000, -2000), new Constant(300, 500)).Render(buffer);
        Assert.Equal([1300, -1500, 1300, -1500, 1300, -1500, 1300, -1500], buffer);

        new PcmMixer(new Constant(30000, -30000), new Constant(10000, -10000)).Render(buffer);
        Assert.Equal([32767, -32768, 32767, -32768, 32767, -32768, 32767, -32768], buffer);

        new PcmMixer().Render(buffer);
        Assert.All(buffer, s => Assert.Equal(0, s));
    }

    [Fact]
    public void EverySourceRendersTheSameFramesInEveryCall()
    {
        // two calls of different lengths: each source is asked for exactly the frames the device asks for
        var mixer = new PcmMixer(new Ramp(1), new Ramp(10));
        var (a, b) = (new Ramp(1), new Ramp(10));
        foreach (var length in new[] { 6, 20 })
        {
            var mixed = new short[length];
            var (left, right) = (new short[length], new short[length]);
            mixer.Render(mixed);
            a.Render(left);
            b.Render(right);
            Assert.Equal(left.Zip(right, (x, y) => (short)(x + y)), mixed);
        }
    }
}
