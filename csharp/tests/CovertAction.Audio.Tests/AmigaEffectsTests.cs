using CovertAction.Audio.Amiga;
using CovertAction.Platform;

namespace CovertAction.Audio.Tests;

/// <summary>
/// The Amiga version's sound effects (D-641, csharp/docs/AMIGA-EFFECTS.md): a sound bank read from bytes, the pan table, the
/// voice choice, the driver's commands, and the routing of the game's commands between the Amiga's effects and the sound
/// card's. The banks here are made in the tests (no Amiga file or sample of one is in the repository).
/// </summary>
public sealed class AmigaEffectsTests
{
    private const int Rate = 44100;

    /// <summary>A bank under construction, written into bytes as the Amiga's files have them.</summary>
    private sealed class BankBuilder
    {
        private readonly List<(int Priority, int Left, int Right)> _effects = [];
        private readonly List<(int Period, int Repeats, sbyte[] Data)> _samples = [];

        public BankBuilder Sample(int period, int repeats, params sbyte[] data)
        {
            _samples.Add((period, repeats, data));
            return this;
        }

        public BankBuilder Effect(int priority, int left, int right)
        {
            _effects.Add((priority, left, right));
            return this;
        }

        public byte[] Bytes()
        {
            var table = 4 + 10 * _effects.Count;
            var data = table + 8 * _samples.Count;
            var file = new List<byte>();
            void Word(int value) => file.AddRange([(byte)(value >> 8), (byte)value]);
            void Long(int value) => file.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            Word(_effects.Count);
            Word(_samples.Count);
            for (var i = 0; i < _effects.Count; i++)
            {
                var record = 4 + 10 * i;
                Long(table + 8 * _effects[i].Left - record);
                Long(table + 8 * _effects[i].Right - record);
                Word(_effects[i].Priority);
            }
            var at = data;
            for (var i = 0; i < _samples.Count; i++)
            {
                Word(_samples[i].Period);
                Long(at - (table + 8 * i));
                Word(_samples[i].Repeats);
                at += 4 + _samples[i].Data.Length;
            }
            foreach (var sample in _samples)
            {
                // a 32-bit length, of which the game uses the low word
                Long(sample.Data.Length);
                file.AddRange(sample.Data.Select(b => (byte)b));
            }
            return [.. file];
        }
    }

    /// <summary>A sample of a constant level: a click that is easy to hear in a render.</summary>
    private static sbyte[] Level(int length, sbyte level = 100) => Enumerable.Repeat(level, length).ToArray();

    /// <summary>A bank of <paramref name="count"/> effects, each a click of its own priority (the priority of the effect's number times ten, 10 to ...).</summary>
    private static AmigaSoundBank BankOf(int count, int repeats = 2, int length = 400)
    {
        var builder = new BankBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Sample(300, repeats, Level(length)).Effect((i + 1) * 10, i, i);
        }
        return AmigaSoundBank.Parse(builder.Bytes());
    }

    private static AmigaSoundBanks AllBanks(int count = 14) => new(BankOf(count), BankOf(count), BankOf(count), BankOf(count), BankOf(count));

    private static short[] Render(IPcmSource source, int frames)
    {
        var buffer = new short[2 * frames];
        source.Render(buffer);
        return buffer;
    }

    private static bool Loud(short[] buffer, int channel) => Enumerable.Range(0, buffer.Length / 2).Any(i => Math.Abs(buffer[2 * i + channel]) > 500);

    // the bank file

    [Fact]
    public void ABankIsReadWithItsEffectsSamplesAndPriorities()
    {
        var bytes = new BankBuilder()
            .Sample(224, 2, Level(10)).Sample(254, 0, 1, 2, 3, 4).Sample(300, 3, Level(6, -5))
            .Effect(40, 0, 1).Effect(100, 2, 2)
            .Bytes();
        var bank = AmigaSoundBank.Parse(bytes);
        Assert.Equal(2, bank.Effects.Count);
        Assert.Equal(3, bank.Samples.Count);
        Assert.Equal((40, 224, 254), (bank.Effects[0].Priority, bank.Effects[0].Left.Period, bank.Effects[0].Right.Period));
        Assert.Same(bank.Effects[1].Left, bank.Effects[1].Right);
        Assert.Equal(new sbyte[] { 1, 2, 3, 4 }, bank.Samples[1].Data);
        Assert.True(bank.Samples[1].Endless);
        Assert.Equal((1, 2), (bank.Samples[0].Passes, bank.Samples[2].Passes));
        Assert.Equal(bytes.Length, bank.End);
    }

    [Fact]
    public void AnOddSampleIsPlayedInWholeWordsAndStillEndsTheFile()
    {
        var bytes = new BankBuilder().Sample(200, 2, 1, 2, 3).Effect(50, 0, 0).Bytes();
        var bank = AmigaSoundBank.Parse(bytes);
        Assert.Equal(new sbyte[] { 1, 2 }, bank.Samples[0].Data);
        Assert.Equal(bytes.Length, bank.End);
    }

    [Fact]
    public void AFileThatIsNotABankIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => AmigaSoundBank.Parse([0, 1]));
        // records that run past the end
        Assert.Throws<InvalidDataException>(() => AmigaSoundBank.Parse([0, 9, 0, 9, 0, 0]));
        var good = new BankBuilder().Sample(200, 2, Level(8)).Effect(50, 0, 0).Bytes();
        Assert.Throws<InvalidDataException>(() => AmigaSoundBank.Parse(good[..^3]));
        // an effect naming a place that is not a sample record
        var bad = (byte[])good.Clone();
        bad[4 + 3] += 1;
        Assert.Throws<InvalidDataException>(() => AmigaSoundBank.Parse(bad));
    }

    // the pan table and the voice choice

    [Theory]
    [InlineData(160, 63, 63)]
    [InlineData(0, 63, 0)]
    [InlineData(319, 0, 63)]
    [InlineData(100, 63, 39)]
    [InlineData(-30, 63, 0)]
    [InlineData(400, 0, 63)]
    public void ThePanTableTakesDownTheFarSide(int x, int left, int right)
    {
        // the left side's volume and the right side's, by the Amiga's table of divisors
        Assert.Equal(left, x < 160 ? 63 : AmigaEffectsDriver.SideVolume(63, x, left: true));
        Assert.Equal(right, x > 160 ? 63 : AmigaEffectsDriver.SideVolume(63, x, left: false));
    }

    [Fact]
    public void AVolumeIsHeldToTheVoicesMaximum()
    {
        Assert.Equal(64, AmigaEffectsDriver.SideVolume(100, 160, left: true));
        Assert.Equal(20, AmigaEffectsDriver.SideVolume(20, 160, left: false));
    }

    [Fact]
    public void AnEffectTakesOneVoiceOnEachSideAndTheNextTheOtherPair()
    {
        var bank = BankOf(3);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate);
        driver.Play(bank.Effects[0], id: 0, volume: 50);
        Assert.Equal([(true, 10, 0), (true, 10, 0), (false, 0, -1), (false, 0, -1)], Enumerable.Range(0, 4).Select(driver.VoiceState));
        driver.Play(bank.Effects[1], id: 1, volume: 50);
        Assert.Equal([(true, 10, 0), (true, 10, 0), (true, 20, 1), (true, 20, 1)], Enumerable.Range(0, 4).Select(driver.VoiceState));
    }

    [Fact]
    public void AnEffectOfLowerPriorityThanBothBusyVoicesOfASideIsNotPlayed()
    {
        var bank = BankOf(3);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate);
        driver.Play(bank.Effects[1], id: 1, volume: 50);
        driver.Play(bank.Effects[2], id: 2, volume: 50);
        driver.Play(bank.Effects[0], id: 0, volume: 50);
        // priorities 20 and 30 are playing: 10 is below both
        Assert.Equal([1, 1, 2, 2], Enumerable.Range(0, 4).Select(v => driver.VoiceState(v).Id));
    }

    [Fact]
    public void AnEffectTakesTheLowestPriorityVoiceAndTheSecondOfTwoEqualOnes()
    {
        var bank = BankOf(3);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate);
        driver.Play(bank.Effects[2], id: 2, volume: 50);
        driver.Play(bank.Effects[1], id: 1, volume: 50);
        // voices hold 30 then 20: the new effect (priority 30) takes the 20 voices
        driver.Play(bank.Effects[2], id: 20, volume: 50);
        Assert.Equal([2, 2, 20, 20], Enumerable.Range(0, 4).Select(v => driver.VoiceState(v).Id));
        // both 30 now: equal priorities, and the new one, also 30, takes the second voice of each side
        driver.Play(bank.Effects[2], id: 21, volume: 50);
        Assert.Equal([2, 2, 21, 21], Enumerable.Range(0, 4).Select(v => driver.VoiceState(v).Id));
    }

    // playing

    [Fact]
    public void AnEffectPlaysItsPassesOnItsSidesAndThenIsSilent()
    {
        var bank = BankOf(1, repeats: 2, length: 400);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate, lowPass: false);
        driver.Play(bank.Effects[0], id: 0, volume: 64, x: 0);
        var first = Render(driver, 200);
        // x = 0: the whole sound on the left, none on the right
        Assert.True(Loud(first, 0));
        Assert.False(Loud(first, 1));
        // 400 bytes at 3546895 / 300 Hz last 34 ms: 1500 frames at 44.1 kHz; a pass of repeat word 2 is one
        var rest = Render(driver, Rate / 4);
        Assert.False(driver.Playing);
        Assert.False(Loud(rest[(2 * 2000)..], 0));
    }

    [Fact]
    public void AnEffectOfRepeatWordThreePlaysTwice()
    {
        var once = BankOf(1, repeats: 2);
        var twice = BankOf(1, repeats: 3);
        var lengths = new List<int>();
        foreach (var bank in new[] { once, twice })
        {
            var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate, lowPass: false);
            driver.Play(bank.Effects[0], id: 0, volume: 64);
            var frames = 0;
            while (driver.Playing && frames < Rate)
            {
                Render(driver, 10);
                frames += 10;
            }
            lengths.Add(frames);
        }
        Assert.InRange(lengths[1], lengths[0] * 19 / 10, lengths[0] * 21 / 10);
    }

    [Fact]
    public void AnEndlessEffectPlaysUntilTheStopCommand()
    {
        var bank = BankOf(1, repeats: 0, length: 200);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate, lowPass: false);
        driver.Play(bank.Effects[0], id: 0, volume: 64);
        Render(driver, Rate);
        Assert.True(driver.Playing);
        driver.PlaySound(SoundCommands.StopAll);
        Assert.False(driver.Playing);
        Assert.False(Loud(Render(driver, 500), 0));
    }

    [Fact]
    public void PauseHoldsTheVoicesAndResumeLetsThemGoOn()
    {
        var bank = BankOf(1, repeats: 0, length: 200);
        var driver = new AmigaEffectsDriver(new AmigaSoundBanks(bank, bank, bank, bank, bank), Rate, lowPass: false);
        driver.Play(bank.Effects[0], id: 0, volume: 64);
        driver.PlaySound(SoundCommands.Pause);
        Assert.False(Loud(Render(driver, 500), 0));
        Assert.True(driver.Playing);
        driver.PlaySound(SoundCommands.Resume);
        Assert.True(Loud(Render(driver, 500), 0));
    }

    [Fact]
    public void ACommandPlaysTheEffectOfThePartsBankAndAChangeOfPartSilencesTheVoices()
    {
        var banks = AllBanks();
        var driver = new AmigaEffectsDriver(banks, Rate);
        // no part: nothing is played
        driver.PlaySound(9);
        Assert.False(driver.Playing);
        driver.Bank = AmigaBank.Tac;
        // the camera (PC command 9) is tac[7]
        driver.PlaySound(9);
        Assert.Equal(7, driver.VoiceState(0).Id);
        Assert.Equal(80, driver.VoiceState(0).Priority);
        driver.Bank = AmigaBank.Bug;
        Assert.False(driver.Playing);
        // 9 means nothing in BUG; 28 (a chip swapped) is bug[0]
        driver.PlaySound(9);
        Assert.False(driver.Playing);
        driver.PlaySound(28);
        Assert.Equal(0, driver.VoiceState(0).Id);
    }

    [Fact]
    public void ACommandForANumberABankDoesNotHaveIsIgnored()
    {
        var short_ = BankOf(3);
        var banks = new AmigaSoundBanks(short_, short_, short_, short_, short_);
        var driver = new AmigaEffectsDriver(banks, Rate) { Bank = AmigaBank.Tac };
        // tac[11] the blast: not in a bank of three
        driver.PlaySound(7);
        Assert.False(driver.Playing);
    }

    [Fact]
    public void TheCallsOwnPanAndVolumeAreUsed()
    {
        var banks = AllBanks();
        var driver = new AmigaEffectsDriver(banks, Rate, lowPass: false) { Bank = AmigaBank.Tac };
        // the player is missed (command 33) is panned to x = 100 by the Amiga: both sides sound, the right one quieter
        driver.PlaySound(33);
        var buffer = Render(driver, 300);
        var left = buffer.Where((_, i) => i % 2 == 0).Max(s => (int)s);
        var right = buffer.Where((_, i) => i % 2 == 1).Max(s => (int)s);
        Assert.True(left > right && right > 0, $"{left} {right}");
    }

    // the table

    [Fact]
    public void TheTableHoldsEachCommandOfAPartOnceAndTheIndexOfAnEffect()
    {
        Assert.Equal(AmigaEffectMap.Rows.Count, AmigaEffectMap.Rows.Select(r => (r.Bank, r.Command)).Distinct().Count());
        foreach (var row in AmigaEffectMap.Rows)
        {
            Assert.Equal(row.Match != AmigaMatch.None, row.Index >= 0);
            Assert.InRange(row.Volume, 0, AmigaEffectsDriver.MaximumVolume);
            Assert.InRange(row.PanX, 0, AmigaEffectsDriver.ScreenWidth - 1);
            if (row.Match != AmigaMatch.None)
            {
                Assert.True(row.Volume > 0, row.Name);
            }
            Assert.False(string.IsNullOrEmpty(row.Evidence), row.Name);
            // the commands of the pause, the stop and the tunes are not effects
            Assert.DoesNotContain(row.Command, new[] { 0, 1, 2, 37, 38, 39, 40, 41, 42, 43 });
        }
    }

    [Fact]
    public void TheTableCountsAreThoseOfTheDocument()
    {
        var counts = Enum.GetValues<AmigaMatch>().ToDictionary(m => m, m => AmigaEffectMap.Rows.Count(r => r.Match == m));
        Assert.Equal((27, 1, 6), (counts[AmigaMatch.Matched], counts[AmigaMatch.Probable], counts[AmigaMatch.None]));
    }

    [Fact]
    public void ARowWithNoTwinHasNoEffect()
    {
        Assert.Null(AmigaEffectMap.Find(AmigaBank.Tac, 44));
        Assert.Null(AmigaEffectMap.Find(AmigaBank.Tac, 11));
        Assert.Null(AmigaEffectMap.Find(null, 9));
        Assert.Equal(7, AmigaEffectMap.Find(AmigaBank.Tac, 9)?.Index);
        // 46 is a different effect in the cryptography workstation and the electronics workbench
        Assert.Equal((1, 2), (AmigaEffectMap.Find(AmigaBank.Code, 46)?.Index, AmigaEffectMap.Find(AmigaBank.Bug, 46)?.Index));
    }

    // the routing

    private sealed class RecordingDriver : ISoundDriver
    {
        public List<int[]> Played { get; } = [];

        public void PlaySound(int effect) => Played.Add([effect]);

        public void PlaySound(int effect, int parameter) => Played.Add([effect, parameter]);

        public void PlaySound(int effect, int mode, int variant) => Played.Add([effect, mode, variant]);
    }

    private static (AmigaEffectsRouter Router, AmigaEffectsDriver Amiga) Route(RecordingDriver? card)
    {
        var amiga = new AmigaEffectsDriver(AllBanks(), Rate) { Bank = AmigaBank.Tac };
        return (new AmigaEffectsRouter(amiga, card), amiga);
    }

    [Fact]
    public void ACommandWithAnAmigaTwinGoesToTheAmigaAndNotToTheCard()
    {
        var card = new RecordingDriver();
        var (router, amiga) = Route(card);
        router.PlaySound(9);
        Assert.Empty(card.Played);
        Assert.True(amiga.Playing);
    }

    [Fact]
    public void ACommandTheAmigaNeverPlaysGoesToTheCardWithAllItsValues()
    {
        var card = new RecordingDriver();
        var (router, amiga) = Route(card);
        router.PlaySound(44, 2, 3);
        router.PlaySound(11);
        router.PlaySound(19, 5);
        Assert.Equal([[44, 2, 3], [11], [19, 5]], card.Played);
        Assert.False(amiga.Playing);
    }

    [Fact]
    public void WithoutACardTheCommandsTheAmigaNeverPlaysAreSilent()
    {
        var (router, amiga) = Route(null);
        router.PlaySound(44, 2, 3);
        router.PlaySound(19);
        Assert.False(amiga.Playing);
        router.PlaySound(9);
        Assert.True(amiga.Playing);
    }

    [Fact]
    public void StopEndPauseAndResumeGoToBoth()
    {
        var card = new RecordingDriver();
        var (router, amiga) = Route(card);
        router.PlaySound(9);
        foreach (var command in new[] { 1, 37, 38, 0 })
        {
            router.PlaySound(command);
        }
        Assert.Equal([[1], [37], [38], [0]], card.Played);
        Assert.False(amiga.Playing);
    }

    [Fact]
    public void APartWithoutAnAmigaBankPlaysEverythingOnTheCard()
    {
        var card = new RecordingDriver();
        var (router, amiga) = Route(card);
        router.Bank = null;
        router.PlaySound(9);
        router.PlaySound(31);
        Assert.Equal([[9], [31]], card.Played);
        Assert.False(amiga.Playing);
    }

    [Fact]
    public void TheSameNumberMeansWhatThePartsBankSays()
    {
        var card = new RecordingDriver();
        var (router, amiga) = Route(card);
        // 14 is TAC's door closing but nothing in the chase
        router.Bank = AmigaBank.Chase;
        router.PlaySound(14);
        Assert.Equal([[14]], card.Played);
        router.Bank = AmigaBank.Tac;
        router.PlaySound(14);
        Assert.Single(card.Played);
        Assert.True(amiga.Playing);
    }

    [Fact]
    public void TheRouterMixesTheAmigasVoicesIntoItsOutput()
    {
        var (router, _) = Route(null);
        Assert.False(Loud(Render(router, 500), 0));
        router.PlaySound(9);
        Assert.True(Loud(Render(router, 500), 0));
    }
}
