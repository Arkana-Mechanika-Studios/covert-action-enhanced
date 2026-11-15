using CovertAction.Core.Crypto;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Tests;

/// <summary>
/// The rules of the cryptography minigame on hand-made puzzles; the expected values are worked out from the reconstructed C of
/// CODE (check_solution, setup_cipher, reveal_hint_letter, reset_guesses, GoodBye), not from the C#.
/// </summary>
public sealed class CryptoRulesTests
{
    /// <summary>
    /// A puzzle with the cipher "plain letter i is written as letter i + 1" (A as B, ..., Z as A), in which the first
    /// <paramref name="used"/> plain letters occur in the message and nothing is guessed yet.
    /// </summary>
    private static CryptoState Puzzle(int used)
    {
        var state = new CryptoState { AlphabetLen = 26 };
        for (var i = 0; i < 26; i++)
        {
            state.PlainAlphabet[i] = (char)('A' + i);
            state.CipherAlphabet[i] = (char)('A' + (i + 1) % 26);
        }
        Array.Fill(state.GuessTable, CryptoState.NoGuess);
        for (var i = 0; i < used; i++)
        {
            state.LetterCount[Cipher(state, i)] = 3;
        }
        return state;
    }

    /// <summary>The cipher letter (0 = A) that stands for plain letter <paramref name="plain"/>.</summary>
    private static int Cipher(CryptoState state, int plain) => CryptoRules.CipherLetterOf(state, plain);

    private static void GuessRight(CryptoState state, int plain) => state.GuessTable[Cipher(state, plain)] = state.PlainAlphabet[plain];

    private static void GuessAllRight(CryptoState state, int letters)
    {
        for (var i = 0; i < letters; i++)
        {
            GuessRight(state, i);
        }
    }

    /// <summary>The state of a generator after a number of draws from the start.</summary>
    private static uint StateAfter(int draws)
    {
        var reference = new MscRandom();
        for (var i = 0; i < draws; i++)
        {
            reference.Rand();
        }
        return reference.State;
    }

    // ---- check_solution ----

    [Fact]
    public void EveryUsedLetterRightIsSolved()
    {
        var state = Puzzle(used: 26);
        GuessAllRight(state, 26);
        Assert.True(CryptoRules.CheckSolution(state));
    }

    [Fact]
    public void NothingGuessedIsNotSolved() => Assert.False(CryptoRules.CheckSolution(Puzzle(used: 26)));

    [Fact]
    public void TwentyRightOutOfTwentySixIsEnoughButNineteenIsNot()
    {
        var state = Puzzle(used: 26);
        GuessAllRight(state, 19);
        Assert.False(CryptoRules.CheckSolution(state)); // 0 / 2 + 19 is not above 19
        GuessRight(state, 19);
        Assert.True(CryptoRules.CheckSolution(state)); // 0 / 2 + 20
    }

    [Fact]
    public void UnusedLettersCountHalf()
    {
        // 10 letters used, 16 unused: 16 / 2 = 8, so 12 right guesses are needed and only 10 can be made
        var state = Puzzle(used: 10);
        GuessAllRight(state, 10);
        Assert.False(CryptoRules.CheckSolution(state));

        // 14 used, 12 unused: 6 + 14 = 20
        state = Puzzle(used: 14);
        GuessAllRight(state, 14);
        Assert.True(CryptoRules.CheckSolution(state));
    }

    [Fact]
    public void OneWrongGuessForAUsedLetterSpoilsIt()
    {
        var state = Puzzle(used: 26);
        GuessAllRight(state, 26);
        state.GuessTable[Cipher(state, 3)] = 'Q';
        Assert.False(CryptoRules.CheckSolution(state));
    }

    [Fact]
    public void AGuessForAnUnusedLetterIsIgnored()
    {
        var state = Puzzle(used: 20);
        GuessAllRight(state, 20);
        // 6 unused letters: 3 + 20; a wrong guess for one of them neither counts as wrong nor as right
        state.GuessTable[Cipher(state, 25)] = 'Q';
        Assert.True(CryptoRules.CheckSolution(state));
    }

    [Fact]
    public void CheckingChangesNothing()
    {
        var state = Puzzle(used: 26);
        var before = (state.GuessTable.ToArray(), state.LetterCount.ToArray(), state.Random.State);
        CryptoRules.CheckSolution(state);
        Assert.Equal(before.Item1, state.GuessTable);
        Assert.Equal(before.Item2, state.LetterCount);
        Assert.Equal(before.State, state.Random.State);
    }

    // ---- GoodBye ----

    [Fact]
    public void GoodByeMarksTheGameAsOverOnlyWhenAsked()
    {
        var shared = new SharedState { NextModule = 0x11 };
        CryptoRules.GoodBye(shared, quit: false);
        Assert.Equal(0x11, shared.NextModule);
        CryptoRules.GoodBye(shared, quit: true);
        Assert.Equal(-1, shared.NextModule);
    }

    // ---- setup_cipher ----

    /// <summary>A state as the program starts with (the alphabet in order, the two spare characters after it) and a hand-off block with a message.</summary>
    private static (CryptoState State, SharedState Shared) BeforeSetup(string message, short difficulty)
    {
        var state = new CryptoState { CODEdiff = difficulty };
        for (var i = 0; i < CryptoState.TableLetters; i++)
        {
            state.CipherAlphabet[i] = (char)('A' + i);
            state.LetterRevealed[i] = true;
        }
        var shared = new SharedState();
        shared.Text.Text = message;
        return (state, shared);
    }

    [Fact]
    public void SetupShufflesTheTwentySixLettersWithOneDrawEach()
    {
        var (state, shared) = BeforeSetup("Meet me at noon.x", 0);
        CryptoRules.SetupCipher(state, shared);

        Assert.Equal(26, state.AlphabetLen);
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ", new string([.. state.CipherAlphabet.Take(26).Order()]));
        Assert.NotEqual("ABCDEFGHIJKLMNOPQRSTUVWXYZ", new string(state.CipherAlphabet, 0, 26));
        Assert.Equal("[\\", new string(state.CipherAlphabet, 26, 2));   // the spare characters stay
        Assert.All(state.LetterRevealed.Take(26), Assert.False);
        Assert.Equal(StateAfter(26), state.Random.State);
    }

    [Theory]
    [InlineData(0, "MEET ME. AT NOON.")]    // nothing hidden
    [InlineData(1, "MEET ME AT NOON")]      // full stops removed
    [InlineData(2, "MEET ME AT NOON")]
    [InlineData(3, "MEETME.ATNOON.")]       // blanks removed
    [InlineData(5, "MEETME.ATNOON.")]
    public void SetupTakesTheMessageDropsItsLastCharacterAndHidesSeparatorsByDifficulty(short difficulty, string expected)
    {
        var (state, shared) = BeforeSetup("Meet me. At noon.x", difficulty);
        CryptoRules.SetupCipher(state, shared);
        Assert.Equal(expected, state.MessageText.Text);
    }

    [Fact]
    public void SetupLeavesOneOfTwoSeparatorsInARow()
    {
        // a removed character is not looked at again: the one that moved into its place is skipped
        var (state, shared) = BeforeSetup("A  B..Cx", 3);
        CryptoRules.SetupCipher(state, shared);
        Assert.Equal("A B..C", state.MessageText.Text);

        (state, shared) = BeforeSetup("A  B..Cx", 1);
        CryptoRules.SetupCipher(state, shared);
        Assert.Equal("A  B.C", state.MessageText.Text);
    }

    [Fact]
    public void SetupOfAMessageWithoutEndMarkIsAnOverrun()
    {
        var (state, shared) = BeforeSetup("", 0);
        Array.Fill(shared.Text.Bytes, (byte)'A');
        Assert.Throws<MemoryOverrunException>(() => CryptoRules.SetupCipher(state, shared));
    }

    // ---- reveal_hint_letter and reset_guesses ----

    [Fact]
    public void AHintRevealsOneUnguessedLetterOfTheMessageWithItsRightPlainLetter()
    {
        var state = Puzzle(used: 5);
        CryptoRules.RevealHintLetter(state);

        var cipher = Assert.Single(Enumerable.Range(0, 26), c => state.LetterRevealed[c]);
        Assert.True(state.LetterCount[cipher] > 0);
        // in this puzzle cipher letter c stands for plain letter c - 1
        Assert.Equal((char)('A' + (cipher + 25) % 26), state.GuessTable[cipher]);
        Assert.Equal(25, state.GuessTable.Take(26).Count(g => g == CryptoState.NoGuess));
    }

    [Fact]
    public void AHintDrawsAgainUntilTheLetterOccursInTheMessage()
    {
        var state = Puzzle(used: 1); // only one cipher letter occurs
        CryptoRules.RevealHintLetter(state);
        Assert.True(state.LetterRevealed[Cipher(state, 0)]);
        Assert.Equal(state.PlainAlphabet[0], state.GuessTable[Cipher(state, 0)]);
    }

    [Fact]
    public void AfterNineHundredNinetyNineTriesAHintReplacesAGuess()
    {
        var state = Puzzle(used: 26);
        Array.Fill(state.GuessTable, 'Q'); // every letter has a (wrong) guess
        CryptoRules.RevealHintLetter(state);

        // every draw hits a used letter, so the tries are the draws
        Assert.Equal(StateAfter(999), state.Random.State);
        var cipher = Assert.Single(Enumerable.Range(0, 26), c => state.LetterRevealed[c]);
        Assert.Equal((char)('A' + (cipher + 25) % 26), state.GuessTable[cipher]);
    }

    [Fact]
    public void ResetClearsTheGuessesButKeepsTheRevealedLetters()
    {
        var state = Puzzle(used: 26);
        Array.Fill(state.GuessTable, 'Q');
        state.LetterRevealed[Cipher(state, 4)] = true;
        state.LetterRevealed[Cipher(state, 9)] = true;

        CryptoRules.ResetGuesses(state);

        Assert.Equal(state.PlainAlphabet[4], state.GuessTable[Cipher(state, 4)]);
        Assert.Equal(state.PlainAlphabet[9], state.GuessTable[Cipher(state, 9)]);
        Assert.Equal(24, state.GuessTable.Take(26).Count(g => g == CryptoState.NoGuess));
    }
}
