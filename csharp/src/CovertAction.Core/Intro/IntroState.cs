namespace CovertAction.Core.Intro;

/// <summary>
/// What the set-up program of the original, INTRO.EXE, has in its data: the texts of its screen and the letters of
/// the drivers its answers choose. The program asks the player, before the game starts, which graphics card, sound
/// card and controls the PC has; the launcher then loads the drivers named by the answers. The values come from the
/// player's own INTRO.EXE (<c>IntroDgroup</c> in the persistence layer), not from the rewrite.
/// </summary>
/// <remarks>
/// The texts are those of a page of the PC's 80-column text mode: rows padded with blanks to the page's width, framed
/// with the PC's line-drawing characters, and colour changes written into the text as the byte 0xff followed by the
/// colour (<see cref="ColourChange"/>).
/// </remarks>
// orig: INTRO DGROUP (src/c/intro/intro_0000_0010.c, the data list in its header comment)
public sealed class IntroState
{
    /// <summary>The number of graphics cards the graphics question offers.</summary>
    public const int GraphicsChoiceCount = 5;

    /// <summary>The number of sound cards the sound question offers.</summary>
    public const int SoundChoiceCount = 6;

    /// <summary>The number of framed title rows with text (the version, the authors, the copyright, the reservation).</summary>
    public const int TitleRowCount = 4;

    /// <summary>The byte that starts a colour change in a text of the screen; the byte after it is the new colour.</summary>
    public const char ColourChange = '\xff';

    /// <summary>INTRO DS:0207: the colour change before the title frame (white on red).</summary>
    public string TitleColour { get; set; } = "";

    /// <summary>INTRO DS:020a: the frame's top row, "╔═══...═══╗" between blanks.</summary>
    public string TopFrame { get; set; } = "";

    /// <summary>INTRO DS:01ad: a framed row without text, "║" and blanks and "║" between blanks.</summary>
    public string BorderRow { get; set; } = "";

    /// <summary>INTRO DS:039f: the frame's bottom row, "╚═══...═══╝" between blanks.</summary>
    public string BottomFrame { get; set; } = "";

    /// <summary>INTRO DS:015a: the text DOS prints to blank a row of the page: 79 blanks, a carriage return, a line feed and the "$" that ends it.</summary>
    public string BlankRow { get; set; } = "";

    /// <summary>INTRO DS:025b, 02ac, 02fd, 034e: the four framed title rows with text, each a whole page row with its frame characters.</summary>
    public string[] TitleRows { get; } = new string[TitleRowCount];

    /// <summary>INTRO DS:03f0: "Select graphics mode:", after a colour change to yellow on black, padded to a page row.</summary>
    public string GraphicsPrompt { get; set; } = "";

    /// <summary>INTRO DS:04d4 gfx_menu_text: the graphics cards, each written after its number ("2" + ") MCGA (256 color)").</summary>
    public string[] GraphicsChoices { get; } = new string[GraphicsChoiceCount];

    /// <summary>INTRO DS:0441: "Select sound mode:", after a colour change to yellow on black, padded to a page row.</summary>
    public string SoundPrompt { get; set; } = "";

    /// <summary>INTRO DS:04de snd_menu_text: the sound cards, each written after its number ("1" + ") Shhh! No sounds please").</summary>
    public string[] SoundChoices { get; } = new string[SoundChoiceCount];

    /// <summary>INTRO DS:04ea snd_driver_letters, "NITARX": the letter of each sound card's driver, in the order of the menu ('N' is no sound).</summary>
    public string SoundLetters { get; set; } = "";

    /// <summary>INTRO DS:04f6 gfx_driver_letters, "EMETC": the letter of each graphics card's driver, in the order of the graphics menu (VGA, MCGA, EGA, Tandy, CGA).</summary>
    public string GraphicsLetters { get; set; } = "";

    /// <summary>INTRO DS:0492 and 04ab: "1. Joystick and Keyboard" and "2. Keyboard only".</summary>
    public string[] ControlChoices { get; } = new string[2];

    /// <summary>INTRO DS:04bc: the colour change before the last words (yellow on black).</summary>
    public string WaitColour { get; set; } = "";

    /// <summary>INTRO DS:04bf: "One Moment Please...", shown while the launcher loads the drivers.</summary>
    public string OneMoment { get; set; } = "";
}
