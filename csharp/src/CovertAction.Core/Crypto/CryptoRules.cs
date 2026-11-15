using CovertAction.Core.Shared;

namespace CovertAction.Core.Crypto;

/// <summary>
/// The rules of the cryptography minigame that need no screen and no keyboard: how the cipher is made, what a hint
/// reveals, what a reset clears, what counts as solved, and how the game is told to end.
/// </summary>
public static class CryptoRules
{
    /// <summary>The letters of the alphabet the puzzle shuffles.</summary>
    private const int Letters = 26;

    /// <summary>How often a hint looks for a letter the player has not guessed before it settles for one that has a guess.</summary>
    private const int HintTries = 999;

    /// <summary>A solution's score (half the unused letters plus the right guesses) must be above this.</summary>
    private const int SolveScore = 19;

    /// <summary>
    /// Tells the launcher to end the whole game instead of running another part, when <paramref name="quit"/> is set.
    /// </summary>
    /// <param name="shared">The hand-off block; its "next part" becomes -1.</param>
    /// <param name="quit">Whether to end the game.</param>
    // orig: CODE 0000:0ca4 GoodBye (src/c/code/code_0000_0ca4.c) matched; tests: CryptoRulesTests, CodeOracleTests
    public static void GoodBye(SharedState shared, bool quit)
    {
        if (quit)
        {
            shared.NextModule = -1;
        }
    }

    /// <summary>
    /// Prepares one puzzle: shuffles the cipher, forgets what was revealed, and takes the message from the hand-off
    /// block, with the separators the difficulty hides removed, its last character dropped, in capitals.
    /// </summary>
    /// <remarks>
    /// The shuffle swaps each of the 26 letters with a randomly drawn one: 26 draws, in order.
    /// Above difficulty 2 the blanks are removed, on difficulties 1 and 2 the full stops. A removed character is not
    /// looked at again, so of two blanks in a row one stays; and where a blank was removed the full-stop test sees
    /// the character that moved into its place. The last character (the blank the message's writer ended it with)
    /// is dropped.
    /// </remarks>
    /// <param name="state">The puzzle; <see cref="CryptoState.CODEdiff"/> must be set.</param>
    /// <param name="shared">The hand-off block with the message text.</param>
    // orig: CODE 0000:0cf6 setup_cipher (src/c/code/code_0000_0cf6.c) matched; tests: CryptoRulesTests, CodeOracleTests
    public static void SetupCipher(CryptoState state, SharedState shared)
    {
        state.AlphabetLen = Letters;
        for (var i = 0; i < state.AlphabetLen; i++)
        {
            var other = GameLibrary.RandomBelow(state.Random, state.AlphabetLen);
            (state.CipherAlphabet[i], state.CipherAlphabet[other]) = (state.CipherAlphabet[other], state.CipherAlphabet[i]);
            state.LetterRevealed[i] = false;
        }

        var hideBlanks = state.CODEdiff > 2;
        var hideFullStops = state.CODEdiff is 1 or 2;
        var message = state.MessageText;
        message.CopyFrom(shared.Text);
        for (var i = 0; i < message.Length; i++)
        {
            if (hideBlanks && message[i] == ' ')
            {
                message.RemoveAt(i);
            }
            if (hideFullStops && message[i] == '.')
            {
                message.RemoveAt(i);
            }
        }
        // C: an empty message has no last character, and the original would clear the byte in front of its buffer
        // (CODE DS:1b71, the high byte of joystick_raw, which is read afresh before every use, so nothing would
        // change). Play never gets there, so here it is an error: a campaign message always holds the marks around
        // the message proper (characters 0x80 and 0x8f, GAME's compose_message_text), which no difficulty removes,
        // and a practice message is five paragraphs of TEXT.DTA (FINAL's make_practice_message).
        message.Truncate(message.Length - 1);
        message.UpperCase();
    }

    /// <summary>
    /// Reveals one letter (the F1 hint, and the head start of the easy difficulties): picks a cipher letter of the
    /// message at random, preferably one the player has no guess for, and writes its plain letter into the guesses.
    /// </summary>
    /// <remarks>
    /// One random draw per pick; a pick that does not occur in the message is drawn again and does not count as a
    /// try. After 999 tries that all had a guess, the last pick is revealed anyway (its guess is replaced by the right
    /// letter). A message without any letter would keep this drawing for ever, as in the original
    /// (docs/original-bugs.md CA-20).
    /// </remarks>
    /// <param name="state">The puzzle.</param>
    // orig: CODE 0000:132d reveal_hint_letter (src/c/code/code_0000_132d.c) matched; tests: CryptoRulesTests, CodeOracleTests
    public static void RevealHintLetter(CryptoState state)
    {
        int cipher;
        var tries = 0;
        do
        {
            do
            {
                cipher = GameLibrary.RandomBelow(state.Random, state.AlphabetLen);
            }
            while (state.LetterCount[cipher] == 0);
            tries++;
        }
        while (state.GuessTable[cipher] != CryptoState.NoGuess && tries < HintTries);

        if (Reveal(state, cipher))
        {
            state.LetterRevealed[cipher] = true;
        }
    }

    /// <summary>
    /// Clears the player's guesses (the F10 key): every cipher letter goes back to "no guess", except the letters the
    /// game revealed, which get their right plain letter back.
    /// </summary>
    /// <param name="state">The puzzle.</param>
    // orig: CODE 0000:13a3 reset_guesses (src/c/code/code_0000_13a3.c) matched; tests: CryptoRulesTests, CodeOracleTests
    public static void ResetGuesses(CryptoState state)
    {
        for (var cipher = 0; cipher < state.AlphabetLen; cipher++)
        {
            if (state.LetterRevealed[cipher])
            {
                Reveal(state, cipher);
            }
            else
            {
                state.GuessTable[cipher] = CryptoState.NoGuess;
            }
        }
    }

    /// <summary>
    /// Whether the message counts as decoded. Not every letter must be right: no letter the message uses may have a
    /// wrong guess, and half the unused letters (rounded down) plus the right guesses must come to more than 19. A
    /// short message that uses few letters is therefore solved with few guesses.
    /// </summary>
    /// <param name="state">The puzzle; only read.</param>
    // orig: CODE 0000:18a4 check_solution (src/c/code/code_0000_18a4.c) matched; tests: CryptoRulesTests, CodeOracleTests
    public static bool CheckSolution(CryptoState state)
    {
        var unused = 0;
        var right = 0;
        var wrong = 0;
        for (var plain = 0; plain < state.AlphabetLen; plain++)
        {
            var cipher = CipherLetterOf(state, plain);
            var guess = state.GuessTable[cipher];
            if (state.LetterCount[cipher] == 0)
            {
                unused++;
            }
            else if (state.LetterCount[cipher] > 0 && guess != CryptoState.NoGuess)
            {
                if (guess == state.PlainAlphabet[plain])
                {
                    right++;
                }
                else
                {
                    wrong++;
                }
            }
        }
        return unused / 2 + right > SolveScore && wrong == 0;
    }

    /// <summary>The cipher letter (0 = A) that stands for the plain letter at a position of the alphabet.</summary>
    public static int CipherLetterOf(CryptoState state, int plain) => state.CipherAlphabet[plain] - 'A';

    /// <summary>Writes into the guesses the plain letter a cipher letter stands for.</summary>
    /// <returns>False, with nothing written, when no position of the alphabet has that cipher letter.</returns>
    private static bool Reveal(CryptoState state, int cipher)
    {
        if (PlainLetterOf(state, cipher) is not { } plain)
        {
            return false;
        }
        state.GuessTable[cipher] = plain;
        return true;
    }

    /// <summary>
    /// The plain letter a cipher letter stands for: the plain letter at the alphabet position that has this cipher
    /// letter. When several positions have it (which the shuffle never produces), the last one counts, as in the
    /// original's loops; null when none has it.
    /// </summary>
    private static char? PlainLetterOf(CryptoState state, int cipher)
    {
        char? plain = null;
        for (var position = 0; position < state.AlphabetLen; position++)
        {
            if (CipherLetterOf(state, position) == cipher)
            {
                plain = state.PlainAlphabet[position];
            }
        }
        return plain;
    }
}
