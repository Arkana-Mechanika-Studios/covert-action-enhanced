using System.Text;
using CovertAction.Core.Crypto;
using CovertAction.Core.Shared;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Tests;

/// <summary>
/// The layouts of the original's memory (the hand-off block, CODE's data segment, the drawing port) on made-up bytes:
/// offsets as in the reconstructed headers, the original's representation turned into typed values, and a load followed by a
/// store leaves every byte as it was.
/// </summary>
public sealed class OriginalLayoutTests
{
    /// <summary>Bytes that differ from place to place, so a field read at a wrong offset shows; every seventh byte is 0, so every text has an end mark.</summary>
    private static byte[] Noise(int length)
    {
        var image = new byte[length];
        new Random(1990).NextBytes(image);
        for (var i = 0; i < length; i += 7)
        {
            image[i] = 0;
        }
        return image;
    }

    private static void SetWord(byte[] image, int offset, int value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
    }

    private static void SetText(byte[] image, int offset, string text) => Encoding.Latin1.GetBytes(text + '\0', image.AsSpan(offset));

    /// <summary>An image of CODE's data segment whose pointer variables hold addresses of the model's objects.</summary>
    private static byte[] CodeImage(bool crossedPorts = false)
    {
        var image = Noise(0x2000);
        SetWord(image, 0x1056, crossedPorts ? 0x1250 : 0x1044);    // RpBack
        SetWord(image, 0x1262, crossedPorts ? 0x1044 : 0x1250);    // Rp1
        SetWord(image, 0x1246, 0x11ce);                            // disk_file_names[1..4]
        SetWord(image, 0x1248, 0x11d8);
        SetWord(image, 0x124a, 0x11e1);
        SetWord(image, 0x124c, 0x11e9);
        SetWord(image, 0x1af8, 0x11ae);                            // the animation record names its file
        // the texts shorter than seven bytes get their end marks
        SetText(image, 0x10d4, "0");
        SetText(image, 0x10d7, "-");
        SetText(image, 0x1197, "OK");
        SetText(image, 0x11a7, "bld");
        SetText(image, 0x121b, ")");
        return image;
    }

    [Fact]
    public void TheSharedBlockRoundTripsByteForByte()
    {
        var image = Noise(SharedState.Size);
        var state = new SharedState();
        SharedBlockCodec.Layout.Load(image, state);
        var copy = (byte[])image.Clone();
        SharedBlockCodec.Layout.Store(state, copy);
        Assert.Equal(image, copy);
    }

    [Fact]
    public void TheSharedBlocksFieldsAreAtTheOffsetsOfStateH()
    {
        var image = new byte[SharedState.Size];
        image[0x00] = 0xFF;                  // next_module = -1: leave the game
        image[0x09] = 1;                     // value: message decoded
        image[0x0f] = 45;                    // elapsed minutes
        image[0x24] = 0x2C;                  // flags: sound, animation, practice
        image[0x3a] = 3;                     // level
        SetWord(image, 0x4e, 0x0530);        // skill_bits: cryptography 3, both speeds 1
        SetText(image, 0x200, "HI");

        var state = new SharedState();
        SharedBlockCodec.Layout.Load(image, state);

        Assert.Equal(-1, state.NextModule);
        Assert.Equal(1, state.Value);
        Assert.Equal(45, state.Elapsed);
        Assert.Equal(Options.Sound | Options.Animation | Options.Practice, state.Flags);
        Assert.Equal(3, state.Level);
        Assert.Equal(0x0530, state.SkillBits);
        Assert.Equal("HI", state.Text.Text);
        Assert.Equal(512, state.Text.Bytes.Length);
    }

    [Fact]
    public void TheSharedBlockLayoutStaysInsideItsHeaderAndText()
    {
        Assert.Equal(SharedState.TablesOffset, SharedBlockCodec.Layout.End);
        Assert.Null(SharedBlockCodec.Layout.FieldAt(0x05));   // no known use
        Assert.Null(SharedBlockCodec.Layout.FieldAt(0x10));
        Assert.Equal("SHARED+0x4e skill_bits", SharedBlockCodec.Layout.FieldAt(0x4f)?.Name);
    }

    [Fact]
    public void CodesDataSegmentRoundTripsByteForByte()
    {
        var image = CodeImage(crossedPorts: true);
        Assert.Empty(CodeDgroup.Layout.OutsideModel(image));
        var state = new CryptoState();
        CodeDgroup.Layout.Load(image, state);

        // the two port pointers were crossed: each now refers to the other record
        Assert.Same(state.RpBackX, state.Rp1);
        Assert.Same(state.Rp1X, state.RpBack);
        Assert.True(state.Animation.AnimationRecordNamed);

        var copy = (byte[])image.Clone();
        CodeDgroup.Layout.Store(state, copy);
        Assert.Equal(image, copy);
    }

    [Fact]
    public void AnImageWhosePointersLeadNowhereIsOutsideTheModel()
    {
        var image = CodeImage();
        SetWord(image, 0x1262, 0x4321);   // Rp1
        SetWord(image, 0x1248, 0);        // a disk file name pointer
        var reasons = CodeDgroup.Layout.OutsideModel(image);
        Assert.Contains(reasons, r => r.Contains("DS:1262 Rp1 points at 0x4321"));
        Assert.Contains(reasons, r => r.Contains("DS:1248 disk_file_names[2] points at 0x0000"));
    }

    [Fact]
    public void CodesVariablesAreAtTheOffsetsOfCodeHAndBecomeTypedValues()
    {
        var image = CodeImage();
        image[0x105a] = (byte)'Q';                       // cipher_alphabet[0]
        image[0x1096 + 27] = (byte)' ';                  // guess_table[27]
        SetText(image, 0x1105, "PAUSED\n");
        SetWord(image, 0x1982, 0x5678);                  // holdrand
        SetWord(image, 0x1984, 0x1234);
        SetWord(image, 0x1a7c + 2 * 5, 1);               // letter_revealed[5]
        SetWord(image, 0x1a7c + 2 * 6, 0);
        SetWord(image, 0x1abe, 26);                      // alphabet_len
        SetWord(image, 0x1ac0 + 2, 7);                   // letter_count[1]
        image[0x1b72 + 511] = (byte)'!';                 // last byte of message_text
        SetWord(image, 0x1d72, -3);                      // CODEdiff
        SetWord(image, 0x1d78, 0x14);                    // CODEstatus: sound and joystick
        SetWord(image, 0x1d8e, 1);                       // CODESuccess
        SetWord(image, 0x1dbe, 1);                       // CODEQuit

        var state = new CryptoState();
        CodeDgroup.Layout.Load(image, state);

        Assert.Equal('Q', state.CipherAlphabet[0]);
        Assert.Equal(CryptoState.NoGuess, state.GuessTable[27]);
        Assert.Equal("PAUSED\n", state.StrPaused);
        Assert.Equal(0x12345678u, state.Random.State);
        Assert.True(state.LetterRevealed[5]);
        Assert.False(state.LetterRevealed[6]);
        Assert.Equal(26, state.AlphabetLen);
        Assert.Equal(7, state.LetterCount[1]);
        Assert.Equal('!', state.MessageText[511]);
        Assert.Equal(-3, state.CODEdiff);
        Assert.Equal(Options.Sound | Options.Joystick, state.CODEstatus);
        Assert.True(state.CODESuccess);
        Assert.Equal(1, state.CODEQuit);
        Assert.Same(state.Rp1X, state.Rp1);
    }

    [Fact]
    public void ATextIsStoredWithItsEndMarkAndNothingBehindItIsTouched()
    {
        var image = CodeImage();
        SetText(image, 0x111e, "00:04:18");
        var state = new CryptoState();
        CodeDgroup.Layout.Load(image, state);

        state.TimerText = "01:02";
        var after = (byte[])image.Clone();
        CodeDgroup.Layout.Store(state, after);

        Assert.Equal("01:02\0" + "18\0", Encoding.Latin1.GetString(after, 0x111e, 9));

        state.TimerText = "123456789"; // nine characters and an end mark do not fit nine bytes
        Assert.Throws<Msc.MemoryOverrunException>(() => CodeDgroup.Layout.Store(state, after));
    }

    [Fact]
    public void AnUnusualTrueSurvivesARoundTripAndAChangedAnswerIsWrittenAsOneOrZero()
    {
        var image = CodeImage();
        SetWord(image, 0x1d96, 0x0100);   // NoHi: true, written unusually
        var state = new CryptoState();
        CodeDgroup.Layout.Load(image, state);
        Assert.True(state.Menu.NoHi);

        var same = (byte[])image.Clone();
        CodeDgroup.Layout.Store(state, same);
        Assert.Equal(image, same);

        state.Menu.NoHi = false;
        state.Menu.MenuPauseMode = !state.Menu.MenuPauseMode;
        var changed = (byte[])image.Clone();
        CodeDgroup.Layout.Store(state, changed);
        Assert.Equal(new byte[] { 0, 0 }, changed[0x1d96..0x1d98]);
        Assert.Equal(state.Menu.MenuPauseMode ? new byte[] { 1, 0 } : new byte[] { 0, 0 }, changed[0x1db2..0x1db4]);
    }

    [Fact]
    public void ADrawOfTheGeneratorShowsInTheDataSegment()
    {
        var image = CodeImage();
        SetWord(image, 0x1982, 1); // holdrand = 1, the state at program start
        SetWord(image, 0x1984, 0);
        var state = new CryptoState();
        CodeDgroup.Layout.Load(image, state);

        Assert.Equal(41, state.Random.Rand());
        CodeDgroup.Layout.Store(state, image);

        // 1 * 214013 + 2531011 = 2745024 = 0x0029E2C0
        Assert.Equal(new byte[] { 0xC0, 0xE2, 0x29, 0x00 }, image[0x1982..0x1986]);
    }

    [Fact]
    public void ADrawingPortIsNineWords()
    {
        Assert.Equal(RasterPortLayout.Size, RasterPortLayout.Layout.End);
        Assert.Equal(9, RasterPortLayout.Layout.Fields.Count());
        Assert.All(RasterPortLayout.Layout.Fields, field => Assert.Equal(2, field.Length));
    }
}
