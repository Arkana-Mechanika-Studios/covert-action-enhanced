using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Crypto;

/// <summary>
/// The state of the cryptography minigame (the original's CODE program): a message enciphered by swapping every
/// letter for another, what the player has worked out so far, the workstation's timer, and the variables of the
/// routines this part shares with the rest of the game (message box, joystick, disk prompt).
/// </summary>
/// <remarks>
/// <para>
/// The names are the original's (the reconstructed code.h), MicroProse's own where the Amiga version's symbols give them:
/// <c>CODEdiff</c>, <c>CODEstatus</c>, <c>CODESuccess</c>, <c>CODERtime</c>, <c>CODEQuit</c>, <c>Rp1</c>, <c>RpBack</c>.
/// </para>
/// <para>
/// How the puzzle is stored. Letters are numbered 0 ('A') to 25. <see cref="CipherAlphabet"/> says which cipher
/// letter stands for each plain letter; everything else about a letter of the puzzle is kept per <em>cipher</em>
/// letter: how often it occurs (<see cref="LetterCount"/>), the player's guess (<see cref="GuessTable"/>), whether
/// the game gave it away (<see cref="LetterRevealed"/>). The tables have 28 entries: the 26 letters and the two
/// characters after 'Z', which the original reserves and never shuffles.
/// </para>
/// <para>
/// Nothing here has a built-in value. Texts, colour tables and start values are the initialised data of the
/// player's CODE.EXE and are loaded from it (CovertAction.Persistence knows where they lie); the rewrite holds no
/// game text.
/// </para>
/// </remarks>
// orig: CODE DGROUP, DS:0f12..DS:1de5 (src/c/include/code.h)
public sealed class CryptoState : ILibraryState
{
    // the library's names for two of the part's variables (ILibraryState)
    Options ILibraryState.Status
    {
        get => CODEstatus;
        set => CODEstatus = value;
    }

    short ILibraryState.Quit
    {
        get => CODEQuit;
        set => CODEQuit = value;
    }

    /// <summary>Entries in each letter table: 'A'..'Z' and two spare.</summary>
    public const int TableLetters = 28;

    /// <summary>What <see cref="GuessTable"/> holds for a cipher letter the player has not guessed: a blank.</summary>
    public const char NoGuess = ' ';

    /// <summary>A state whose two port pointers point at their records, as in the program's initialised data.</summary>
    public CryptoState()
    {
        Rp1 = Rp1X;
        RpBack = RpBackX;
    }

    // ---- the screen ----

    /// <summary>CODE DS:1250: the drawing port record of the screen.</summary>
    public RasterPort Rp1X { get; } = new();

    /// <summary>CODE DS:1044: the drawing port record of the off-screen copy of the screen kept under menus.</summary>
    public RasterPort RpBackX { get; } = new();

    /// <summary>CODE DS:1262: the port all drawing of this part goes through; in the running game it is <see cref="Rp1X"/>.</summary>
    public RasterPort Rp1 { get; set; }

    /// <summary>CODE DS:1056: the port the screen is saved to and restored from; in the running game it is <see cref="RpBackX"/>.</summary>
    public RasterPort RpBack { get; set; }

    /// <summary>CODE DS:0f12: first colour table handed to the graphics driver before every message box (16 bytes; used by the CGA driver).</summary>
    public byte[] ColorMapA { get; } = new byte[16];

    /// <summary>CODE DS:0f22: second colour table handed over with <see cref="ColorMapA"/> (16 bytes).</summary>
    public byte[] ColorMapB { get; } = new byte[16];

    /// <summary>CODE DS:0f82: the colour table handed to the driver, as both tables, when this part starts (16 bytes).</summary>
    public byte[] ColorMapStart { get; } = new byte[16];

    /// <summary>CODE DS:1d7a: the graphics driver's letter, taken over from the hand-off block.</summary>
    public short GfxLetter { get; set; }

    // ---- the puzzle ----

    /// <summary>CODE DS:1982: this part's random generator (its state is the C library's <c>holdrand</c>).</summary>
    public MscRandom Random { get; } = new();

    /// <summary>CODE DS:105a: the cipher. Entry i is the cipher letter that stands for plain letter i.</summary>
    public char[] CipherAlphabet { get; } = new char[TableLetters];

    /// <summary>
    /// CODE DS:1078: for each position i of <see cref="CipherAlphabet"/>, the plain letter its cipher letter stands
    /// for. The alphabet in order at start. On the hard difficulties spare cipher letters take over part of a
    /// frequent letter's occurrences, and their entry becomes that letter.
    /// </summary>
    public char[] PlainAlphabet { get; } = new char[TableLetters];

    /// <summary>CODE DS:1096: the player's guess for each cipher letter: a plain letter, or <see cref="NoGuess"/>.</summary>
    public char[] GuessTable { get; } = new char[TableLetters];

    /// <summary>CODE DS:111e: the timer as it was last drawn, "hh:mm:ss".</summary>
    public string TimerText { get; set; } = "";

    /// <summary>CODE DS:1a7a: seconds on the workstation's timer; whole minutes are moved to <see cref="CODERtime"/> whenever the timer is drawn.</summary>
    public short ElapsedSeconds { get; set; }

    /// <summary>CODE DS:1a7c: for each cipher letter, whether the game has revealed it (a hint, or an easy difficulty's head start).</summary>
    public bool[] LetterRevealed { get; } = new bool[TableLetters];

    /// <summary>CODE DS:1abe: the number of letters in play, 26 once the puzzle is set up.</summary>
    public short AlphabetLen { get; set; }

    /// <summary>CODE DS:1ac0: how often each cipher letter occurs in the message; 0 means the letter is not used.</summary>
    public short[] LetterCount { get; } = new short[TableLetters];

    /// <summary>
    /// CODE DS:1b72: the plain text of the message, in capitals, at most 511 characters. (The original also builds
    /// the insert-disk prompt in this buffer when the session is over, so it holds that text afterwards.)
    /// </summary>
    public TextBuffer MessageText { get; } = new(512);

    /// <summary>
    /// CODE DS:1d72: the puzzle's difficulty, -3..5: the campaign's level (0..3) less the agent's cryptography skill
    /// (0..3), plus one. 0 and below give letters away; 1 and 2 remove the full stops; above 2 the spaces go too;
    /// 2 and above 3 add homophones.
    /// </summary>
    public short CODEdiff { get; set; }

    /// <summary>CODE DS:1d76: the seed this part drew from the clock.</summary>
    public short RandomSeed { get; set; }

    /// <summary>CODE DS:1d78: this part's copy of the option flags.</summary>
    public Options CODEstatus { get; set; }

    /// <summary>CODE DS:1d8e: whether the message is decoded; the minigame's answer to the campaign.</summary>
    public bool CODESuccess { get; set; }

    /// <summary>CODE DS:1d90: minutes on the workstation's timer; the time the campaign charges.</summary>
    public short CODERtime { get; set; }

    /// <summary>CODE DS:1dbe: 0 while the session runs; 1 to leave the workstation; -1 to leave the whole game.</summary>
    public short CODEQuit { get; set; }

    // ---- this part's texts ----

    /// <summary>CODE DS:10b6: the screen's title, which ends in a three-digit message number that is replaced.</summary>
    public string StrCryptoTitle { get; set; } = "";

    /// <summary>CODE DS:10d4: "0", the count every letter of the column starts with.</summary>
    public string StrZero { get; set; } = "";

    /// <summary>CODE DS:10d7: the hyphen drawn where a word is broken at the end of a line.</summary>
    public string StrDash { get; set; } = "";

    /// <summary>CODE DS:10dc: the quit question of the workstation's own Alt-Q, with its choices No and Yes.</summary>
    public string StrQuitConfirm2 { get; set; } = "";

    /// <summary>CODE DS:1105: the pause message.</summary>
    public string StrPaused { get; set; } = "";

    /// <summary>CODE DS:110d: the message shown when the text is decoded.</summary>
    public string StrMessageDecoded { get; set; } = "";

    // ---- the routines shared with the other parts ----

    /// <summary>The message box and menu.</summary>
    public MenuState Menu { get; } = new();

    /// <summary>The picture animation beside a message box.</summary>
    public AnimationState Animation { get; } = new();

    /// <summary>The insert-disk prompt.</summary>
    public DiskPromptState Disk { get; } = new();

    /// <summary>Joystick, mouse and BIOS call variables.</summary>
    public InputState Input { get; } = new();
}
