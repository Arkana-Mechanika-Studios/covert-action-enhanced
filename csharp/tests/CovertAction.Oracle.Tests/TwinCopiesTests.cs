using CovertAction.Content;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Tests.Shared;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The proofs that stand for copies of a routine no recorded run reaches in their own program (Harness/TwinCopies),
/// and the drivers' reading of a character that lets the boundary fake take a char and an unsigned char for the same
/// call. On the player's programs; skipped without them.
/// </summary>
public sealed class TwinCopiesTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Twins => TwinCopies.ByName.Keys.Select(k => new object[] { k.Module, k.Name });

    [SkippableTheory]
    [MemberData(nameof(Twins))]
    public void TheCopyIsTheSameRoutineAsItsTwin(string module, string name) =>
        output.WriteLine(TwinCopies.Prove(TwinCopies.ByName[(module, name)]));

    /// <summary>The proof is not empty: without the variables it lists, the same two copies do not pass.</summary>
    [SkippableFact]
    public void WithoutItsListedVariablesTheProofFails()
    {
        var twin = TwinCopies.ByName[("BUG", "lookup_clue_text")];
        var failure = Assert.ThrowsAny<XunitException>(() => TwinCopies.Prove(twin with { Data = [] }));
        output.WriteLine(failure.Message);
    }

    /// <summary>
    /// char_width (the font's width of a character) in each graphics driver: the routine every driver has at the
    /// same place of its code takes the character as the byte at [bp+8] (mov cl, [bp+8]; or cl, cl; js: 128 and up
    /// are no characters), and reads no other argument byte than that and the font word at [bp+6]. So a caller's
    /// sign extension of the character (C char) or not (text_width's unsigned char) is not seen by the driver.
    /// </summary>
    [SkippableFact]
    public void EveryGraphicsDriverReadsTheCharacterOfCharWidthAsOneByte()
    {
        var game = GameFolder.FromEnvironment() ?? new GameFolder(Path.Combine(Repository.Root, "game", "Covert Action", "Covert"));
        Skip.If(game.Find("MGRAPHIC.EXE") is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        // MGRAPHIC 0000:1000 is the entry the C# driver names (McgaGraphicsDriver.CharWidth)
        foreach (var (driver, entry) in new[] { ("MGRAPHIC", 0x1000), ("EGRAPHIC", 0x1f5e), ("CGRAPHIC", 0x12a6), ("TGRAPHIC", 0x19d6) })
        {
            var image = Executable.Read(game.Read(driver + ".EXE")).Image;
            var code = image.AsSpan(entry);
            // push bp; mov bp, sp; push ds; mov ax, <data>; mov ds, ax; xor ax, ax; mov cl, [bp+8]; or cl, cl; js
            Assert.True(code[..5].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x1e, 0xb8 }) && code[7..17].SequenceEqual(new byte[] { 0x8e, 0xd8, 0x33, 0xc0, 0x8a, 0x4e, 0x08, 0x0a, 0xc9, 0x78 }),
                $"{driver} 0000:{entry:x4} is not the char_width routine this test knows");
            var end = code.IndexOf((byte)0xcb);     // retf
            Assert.True(end > 0, $"{driver}: no retf");
            var argumentReads = 0;
            for (var i = 0; i + 1 < end; i++)
            {
                // a ModR/M byte [bp+disp8] (mod 01, r/m 110) with the displacement of an argument byte 7 or above
                if ((code[i] & 0xc7) == 0x46 && code[i + 1] is >= 7 and <= 0x0f)
                {
                    argumentReads++;
                }
            }
            Assert.True(argumentReads == 1, $"{driver}: {argumentReads} reads of the arguments beyond the font word");
            output.WriteLine($"{driver} 0000:{entry:x4}: char_width reads the character as the byte [bp+8] ({end + 1} bytes)");
        }
    }
}
