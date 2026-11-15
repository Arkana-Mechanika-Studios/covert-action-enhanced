using System.Text;
using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation;
using CovertAction.Presentation.Raid;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The raid's choice of a computer password (TAC's read_text_line) on text files made up for the test: the place the
/// C library's <c>fseek</c> goes to in a file read as text, worked out from its instructions (TAC 00e8:f3cc). The
/// recorded runs of the original cannot hold this: the oracle answers the file calls itself, so the library's table
/// of open files never says "text" there and its <c>fseek</c> steps as in a file of plain bytes.
/// </summary>
public sealed class RaidPasswordTests : IDisposable
{
    private const int Block = 512;

    private readonly DirectoryInfo _game = Directory.CreateTempSubdirectory("covert-game-");
    private readonly List<GameFolderFiles> _made = [];

    public void Dispose()
    {
        _made.ForEach(files => files.Dispose());
        _game.Delete(recursive: true);
    }

    /// <summary>A raid whose next random place in the text file lies at least 100 bytes into a block behind the first, and that place.</summary>
    private (BuildingRaid Raid, RaidState State, int Place) RaidWithAPlaceInsideABlock()
    {
        for (uint seed = 1; ; seed++)
        {
            var place = GameLibrary.RandomBelow(new MscRandom { State = seed }, 20000);
            if (place >= Block && place % Block >= 100)
            {
                var state = new RaidState { TextDtaName = "text.dta", PasswordDots = ".........." };
                state.Random.State = seed;
                _made.Add(new GameFolderFiles(new GameFolder(_game.FullName)));
                // the choice of a password reaches nothing but the files
                var world = new Boundaries(null!, null!, null!, null!, null!, _made[^1]);
                return (new BuildingRaid(state, new SharedState(), world, OriginalBehaviour.Faithful), state, place);
            }
        }
    }

    /// <summary>
    /// A text file in which the block of <paramref name="place"/> begins with eight short lines, and the words
    /// "x wanted other third" stand so that the blank behind "x" is exactly at the place. Read from there, "wanted"
    /// is the word dropped and "other" the password; from one byte earlier it would be "wanted", from eight later "third".
    /// </summary>
    /// <param name="lineEndCutByBlock">The block's first byte is the line feed of a line end begun in the block before.</param>
    private void WriteTextFile(int place, bool lineEndCutByBlock)
    {
        var block = place - place % Block;
        var text = new StringBuilder();
        // whole lines of 64 bytes up to the block, the last one's line feed pushed into the block if asked
        while (text.Length < block)
        {
            text.Append(new string('m', 62)).Append("\r\n");
        }
        if (lineEndCutByBlock)
        {
            text.Insert(0, 'm');
        }
        for (var line = 0; line < 8; line++)
        {
            text.Append("ab\r\n");
        }
        text.Append(new string('f', place - 1 - text.Length)).Append("x wanted other third\r\n");
        text.Append(new string('m', 62)).Append("\r\n\x1a");
        Assert.Equal(" wanted", text.ToString(place, 7));
        Assert.Equal(lineEndCutByBlock ? '\n' : 'a', text[block]);
        File.WriteAllText(Path.Combine(_game.FullName, "TEXT.DTA"), text.ToString(), Encoding.Latin1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePasswordIsTheFirstFittingWordBehindTheOneAtTheRandomPlace(bool lineEndCutByBlock)
    {
        var (raid, state, place) = RaidWithAPlaceInsideABlock();
        WriteTextFile(place, lineEndCutByBlock);
        raid.ReadTextLine();
        Assert.Equal("other", state.PasswordWord);
        // one dot per letter
        Assert.Equal(".....\0", new string(state.PasswordMask, 0, 6));
    }

    [Fact]
    public void WordsThatAreTooShortTooLongOrNotAllSmallLettersArePassedOver()
    {
        var (raid, state, place) = RaidWithAPlaceInsideABlock();
        var text = new string('m', place) + " dropped abcd Seven seven7 tenletters it's fitting next\r\n\x1a";
        File.WriteAllText(Path.Combine(_game.FullName, "TEXT.DTA"), text, Encoding.Latin1);
        raid.ReadTextLine();
        Assert.Equal("fitting", state.PasswordWord);
        Assert.Equal(".......\0", new string(state.PasswordMask, 0, 8));
    }
}
