using System.Buffers.Binary;
using CovertAction.Content;
using CovertAction.Drivers;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The Amiga animations (D-683, D-684) without the Amiga files: the container made in the tests, its reading, the translation of
/// its program into the PC's, and the refusal to play an Amiga animation that is not the game's.
/// </summary>
public sealed class AmigaAnimationsTests
{
    private static byte[] Word(int value)
    {
        var word = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(word, (ushort)value);
        return word;
    }

    private static byte[] Words(params int[] values) => [.. values.SelectMany(Word)];

    /// <summary>An Amiga animation: 32 colours, a picture background, the images given by number, the program.</summary>
    private static byte[] MakePan(int width, int height, int delay, (int Number, int Width, int Height, byte[] Pixels)[] images, byte[] program, byte[]? background = null)
    {
        var file = new List<byte>("ANIMAM"u8.ToArray());
        file.AddRange(Enumerable.Range(0, 32).SelectMany(i => Word(i == 0 ? 0x0aa : i)));
        file.AddRange(Words(0, 0, width, height, delay));
        file.AddRange("BGND"u8.ToArray());
        file.AddRange(Words(1, 0, width, height));
        file.AddRange(AmigaPixBuilder.Stream(background ?? new byte[width * height]));
        foreach (var (number, imageWidth, imageHeight, pixels) in images)
        {
            file.AddRange("SPRH"u8.ToArray());
            file.AddRange(Words(number, 0x180e, 0, 0, 0, 0, 0, imageWidth, imageHeight, 0, 0, 0, 0, 4));
            file.AddRange(AmigaPixBuilder.Stream(pixels));
        }
        file.AddRange("AMPG"u8.ToArray());
        file.AddRange(Word(program.Length));
        file.AddRange(program);
        return [.. file];
    }

    [Fact]
    public void AnAnimationFileIsReadIntoItsColoursBackgroundImagesAndProgram()
    {
        byte[] background = [0, 1, 2, 3, 4, 5, 6, 7];
        byte[] image = [0, 9, 9, 0, 9, 9];
        byte[] program = Words(21);
        var pan = AmigaPanFile.Read(MakePan(4, 2, 3, [(7, 3, 2, image)], program, background));
        Assert.Equal((4, 2, 3), (pan.Width, pan.Height, pan.Delay));
        Assert.Equal(0x0aa, pan.Colours[0]);
        Assert.Equal(1, pan.BackgroundKind);
        Assert.Equal(background, pan.Background!.Pixels);
        Assert.Equal((3, 2), (pan.Images[7]!.Width, pan.Images[7]!.Height));
        Assert.Equal(image, pan.Images[7]!.Pixels);
        Assert.Null(pan.Images[6]);
        Assert.Equal(program, pan.Program);
    }

    [Fact]
    public void AFileThatIsNotAnAnimationOrLacksItsProgramIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => AmigaPanFile.Read(new byte[200]));
        var whole = MakePan(4, 2, 1, [], Words(21));
        Assert.Throws<InvalidDataException>(() => AmigaPanFile.Read(whole[..^8]));
    }

    [Fact]
    public void AProgramIsWrittenAgainInThePcsBytesWithEveryAddressMovedWithItsInstruction()
    {
        // the Amiga's: start a sprite whose program is at 58 (number 3, parent -1, place 10,20, speed 255, flag 0), wait 5, jump to the next instruction and end; the sprite shows image 6, moves by -2,1, and loops back
        var amiga = Words(
            5, 0, 58, 5, 0, 3, 5, 0, -1, 5, 0, 10, 5, 0, 20, 5, 0, 255, 5, 0, 0, 0,     // 0..43: seven pushes of 6 bytes and the start
            5, 0, 5, 2,                                                                    // 44..51: push 5; wait
            19);                                                                           // 52: jump (the target follows as a little-endian word, 54..55)
        amiga = [.. amiga, 56, 0];                                                         // jump to 56 (the next instruction)
        amiga = [.. amiga, .. Words(21)];                                                  // 56: end
        amiga = [.. amiga, .. Words(0, 6, 2, -2, 1, 6, 58)];                               // 58: show 6; move by -2,1; loop to 58
        var pc = AmigaPanProgram.ToPcProgram(amiga);
        byte[] expected =
        [
            5, 0, 0, 0,           // push (the sprite's address, set below)
            5, 0, 3, 0, 5, 0, 0xff, 0xff, 5, 0, 10, 0, 5, 0, 20, 0, 5, 0, 0xff, 0, 5, 0, 0, 0, 0,
            5, 0, 5, 0, 2,
            19, 0, 0,             // jump (the target set below)
            21,
            0, 6, 2, 0xfe, 0xff, 1, 0, 6, 0, 0,
        ];
        // the PC's positions: the first push 0..3, six more 4..27, the start 28, push 5 29..32, wait 33, jump 34..36, end 37, the sprite from 38
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(2), 38);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(35), 37);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(38 + 8), 38);
        Assert.True(pc.Length % 16 == 0);
        Assert.Equal(expected, pc.AsSpan(0, expected.Length).ToArray());
        Assert.All(pc.Skip(expected.Length), value => Assert.Equal(0, value));
    }

    [Fact]
    public void AProgramThatJumpsInsideAnInstructionOrStartsASpriteWithoutItsAddressIsRefused()
    {
        // a jump to byte 1, inside the jump itself
        var inside = Words(19);
        inside = [.. inside, 1, 0, .. Words(21)];
        Assert.Throws<InvalidDataException>(() => AmigaPanProgram.ToPcProgram(inside));
        // a start with nothing pushed
        Assert.Throws<InvalidDataException>(() => AmigaPanProgram.ToPcProgram(Words(0, 21)));
        Assert.Throws<InvalidDataException>(() => AmigaPanProgram.ToPcProgram(Words(99)));
    }
}
