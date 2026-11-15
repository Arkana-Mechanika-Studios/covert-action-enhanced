using CovertAction.Content;
using CovertAction.Core.Intro;
using CovertAction.Core.Shared;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Host.Sdl;
using CovertAction.Msc;
using CovertAction.Persistence.Dgroup;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Intro;

namespace CovertAction.Launcher.Tests.Intro;

/// <summary>
/// The set-up screen (INTRO's hardware_setup_menu) on the headless host with the texts of the player's INTRO.EXE
/// (skipped without the game files). Expected values are worked out from the C and the assembly of print_at and
/// print_string: a digit key from 1 to the number of choices answers, any other key is ignored; the sound letter is
/// the menu's entry of "NITARX"; '1' of the last question is the joystick; the texts are put on the text page at
/// the rows and columns of the calls, in the colours of their colour changes.
/// </summary>
public sealed class HardwareSetupTests
{
    // text attributes: white on red (the title), yellow on black (the questions), the page's light grey on black
    private const byte WhiteOnRed = 0x4f, YellowOnBlack = 0x0e, Plain = 0x07;

    // code page 437 characters of the frame
    private const byte DoubleTopLeft = 0xc9, DoubleHorizontal = 0xcd, DoubleTopRight = 0xbb, DoubleVertical = 0xba, DoubleBottomLeft = 0xc8, DoubleBottomRight = 0xbc;

    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "INTRO.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>
    /// Runs the set-up screen with keys, as the launcher does (the page shown by the host); returns the hand-off block,
    /// the page as it is when the keys end (the screen stays up until the player's next key), the host and the texts.
    /// </summary>
    private static (SharedState Shared, TextScreen Page, HeadlessHost Host, IntroState State) Run(params int[] keys)
    {
        var folder = GameFiles();
        var state = new IntroState();
        IntroDgroup.Layout.Load(Executable.Read(folder.Read("INTRO.EXE")).DataSegment(), state);
        var graphics = new McgaGraphicsDriver(new FontSet(folder.Read("FONTS.CV")), McgaDriverFile.Read(folder.Read("MGRAPHIC.EXE")).DefaultColours);
        var host = new HeadlessHost(keys);
        var page = new TextScreen();
        var link = new HostLink(graphics, host) { TextPage = page };
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), new GameFolderFiles(folder, null));
        var shared = new SharedState();
        try
        {
            new HardwareSetup(state, shared, world, page).HardwareSetupMenu();
        }
        catch (ProgramExitException)
        {
            // the keys ended while the screen waited for an answer
        }
        return (shared, page, host, state);
    }

    private static byte Character(TextScreen page, int row, int column) => page.Characters[row * TextScreen.Columns + column];

    private static byte Attribute(TextScreen page, int row, int column) => page.Attributes[row * TextScreen.Columns + column];

    /// <summary>The characters of a page row from a column, as text.</summary>
    private static string Text(TextScreen page, int row, int column, int length) =>
        System.Text.Encoding.Latin1.GetString(page.Characters, row * TextScreen.Columns + column, length);

    private static string BlankRow => new(' ', TextScreen.Columns);

    private static void AssertRowAttribute(TextScreen page, int row, byte attribute)
    {
        for (var column = 0; column < TextScreen.Columns; column++)
        {
            Assert.Equal(attribute, Attribute(page, row, column));
        }
    }

    [SkippableFact]
    public void TheTextsAreThoseOfThePlayersIntro()
    {
        var (_, _, _, state) = Run(HostKeys.ForCharacter('1'), HostKeys.ForCharacter('2'));
        Assert.Equal("NITARX", state.SoundLetters);
        Assert.Equal("EMETC", state.GraphicsLetters);
        Assert.Equal(") Shhh! No sounds please", state.SoundChoices[0]);
        Assert.Contains("Covert Action (TM)", state.TitleRows[0]);
        Assert.Equal("One Moment Please...", state.OneMoment);
        // the frame rows and the blank row: 80 characters each, the blank row 79 blanks and CR LF "$"
        Assert.All(new[] { state.TopFrame, state.BorderRow, state.BottomFrame }, row => Assert.Equal(TextScreen.Columns, row.Length));
        Assert.Equal(new string(' ', TextScreen.Columns - 1) + "\r\n$", state.BlankRow);
    }

    [SkippableFact]
    public void NoSoundAndTheKeyboardOnly()
    {
        var (shared, _, _, _) = Run(HostKeys.ForCharacter('1'), HostKeys.ForCharacter('2'));
        Assert.Equal((sbyte)'N', shared.SndDriver);
        Assert.Equal(0, shared.Joystick);
        // the graphics card is not asked for (D-600): MCGA's letter
        Assert.Equal((sbyte)'M', shared.GfxDriver);
    }

    [SkippableFact]
    public void TheSixthSoundCardIsTheCustomDriverAndTheFirstControlIsTheJoystick()
    {
        var (shared, _, _, _) = Run(HostKeys.ForCharacter('6'), HostKeys.ForCharacter('1'));
        Assert.Equal((sbyte)'X', shared.SndDriver);
        Assert.Equal(1, shared.Joystick);
    }

    [SkippableFact]
    public void KeysThatAreNotOfferedAreIgnored()
    {
        // '0' and '7' are outside the six sound cards, '3' outside the two controls; arrows have no character
        var (shared, _, _, _) = Run(
            HostKeys.ForCharacter('0'), HostKeys.ForCharacter('7'), HostKeys.Down, HostKeys.ForCharacter('a'), HostKeys.ForCharacter('4'),
            HostKeys.ForCharacter('3'), HostKeys.ForCharacter('0'), HostKeys.ForCharacter('2'));
        Assert.Equal((sbyte)'A', shared.SndDriver);
        Assert.Equal(0, shared.Joystick);
    }

    /// <summary>
    /// The title as the program prints it on the text page: rows 1 to 10 white on red, the double frame from column 1
    /// to column 78 in the line characters of the texts, the four texts whole (the copyright line is 70 characters
    /// wide and is not broken); row 0 is not printed.
    /// </summary>
    [SkippableFact]
    public void TheTitleIsWhiteOnRedInTheDoubleFrameOfTheTexts()
    {
        var (_, page, _, _) = Run();
        const int right = 78;
        for (var row = 1; row <= 10; row++)
        {
            AssertRowAttribute(page, row, WhiteOnRed);
        }
        AssertRowAttribute(page, 0, Plain);
        Assert.Equal(BlankRow, Text(page, 0, 0, TextScreen.Columns));
        // the top and bottom of the frame
        Assert.Equal(DoubleTopLeft, Character(page, 1, 1));
        Assert.Equal(DoubleHorizontal, Character(page, 1, 2));
        Assert.Equal(DoubleHorizontal, Character(page, 1, right - 1));
        Assert.Equal(DoubleTopRight, Character(page, 1, right));
        Assert.Equal(DoubleBottomLeft, Character(page, 10, 1));
        Assert.Equal(DoubleBottomRight, Character(page, 10, right));
        // the framed rows: blank, the version, blank, the authors, blank, the copyright, the reservation, blank
        Assert.Equal(DoubleVertical, Character(page, 2, 1));
        Assert.Equal(DoubleVertical, Character(page, 3, right));
        Assert.Equal("Covert Action (TM) - Version 447.01", Text(page, 3, 22, 35));
        Assert.Equal("From those sneaky guys at MPS Labs.", Text(page, 5, 22, 35));
        Assert.Equal("Program & Audio-visual Copyright (C) 1990 by MicroProse Software,Inc.,", Text(page, 7, 6, 70));
        Assert.Equal("All Rights Reserved.", Text(page, 8, 30, 20));
        Assert.Equal(DoubleVertical, Character(page, 9, 1));
    }

    /// <summary>The first question is the sound card's: the prompt in yellow on row 12, the menu lines from row 14 at column 27, the cursor where the answer goes.</summary>
    [SkippableFact]
    public void TheSoundQuestionIsYellowAtItsRowsAndColumnsWithTheCursorAtTheAnswer()
    {
        var (_, page, host, state) = Run();
        Assert.Equal("Select sound mode:", Text(page, 12, 30, 18));
        Assert.Equal(YellowOnBlack, Attribute(page, 12, 30));
        for (var choice = 0; choice < state.SoundChoices.Length; choice++)
        {
            var line = $"{choice + 1}{state.SoundChoices[choice]}";
            Assert.Equal(line, Text(page, 14 + choice, 27, line.Length));
            Assert.Equal(YellowOnBlack, Attribute(page, 14 + choice, 27));
        }
        Assert.Equal("1) Shhh! No sounds please", Text(page, 14, 27, 25));
        Assert.Equal((13, 50), (page.CursorRow, page.CursorColumn));
        Assert.True(page.CursorVisible);
        // the title is still where it was printed (the blank rows scroll nothing), and the host is showing the page
        Assert.Equal(DoubleTopLeft, Character(page, 1, 1));
        Assert.True(host.ShowingText);
        Assert.Equal(Text(page, 12, 0, TextScreen.Columns), Text(host.TextPage, 12, 0, TextScreen.Columns));
        // the rows between the title and the prompt, and below the menu, are blank
        Assert.Equal(BlankRow, Text(page, 11, 0, TextScreen.Columns));
        Assert.Equal(BlankRow, Text(page, 20, 0, TextScreen.Columns));
    }

    /// <summary>
    /// The graphics question is printed though not asked (D-600): the sound question's blank rows print over its texts,
    /// but the places keep its colours: the prompt's yellow on row 13 (columns 0 to 77, as far as the prompt's row goes)
    /// and on the graphics menu's cells beyond the sound menu's lines (row 15: "1) VGA (256 color)" is longer than
    /// "2) IBM sounds"). As the original's page at its sound question (the whole-session proof's recordings).
    /// </summary>
    [SkippableFact]
    public void TheGraphicsQuestionLeavesItsColoursUnderTheSoundQuestion()
    {
        var (_, page, _, state) = Run();
        Assert.Equal("EMETC", state.GraphicsLetters);
        Assert.Equal(") MCGA (256 color)", state.GraphicsChoices[1]);
        Assert.Contains("Select graphics mode:", state.GraphicsPrompt);
        // row 13 holds no text, but the graphics prompt's colour
        Assert.Equal(BlankRow, Text(page, 13, 0, TextScreen.Columns));
        for (var column = 0; column < TextScreen.Columns; column++)
        {
            Assert.Equal(column < 78 ? YellowOnBlack : Plain, Attribute(page, 13, column));
        }
        // row 15: the graphics menu's first line is longer than the sound menu's second line there
        var graphics = $"1{state.GraphicsChoices[0]}";
        var sound = $"2{state.SoundChoices[1]}";
        Assert.True(graphics.Length > sound.Length);
        for (var column = 27; column < 27 + graphics.Length; column++)
        {
            Assert.Equal(YellowOnBlack, Attribute(page, 15, column));
        }
        Assert.Equal(Plain, Attribute(page, 15, 27 + graphics.Length));
        Assert.Equal((13, 50), (page.CursorRow, page.CursorColumn));
    }

    /// <summary>The second question replaces the first: the two control lines on rows 13 and 14 at column 27.</summary>
    [SkippableFact]
    public void TheControlsQuestionReplacesTheSoundMenu()
    {
        var (_, page, _, state) = Run(HostKeys.ForCharacter('1'));
        Assert.Equal(state.ControlChoices[0], Text(page, 13, 27, state.ControlChoices[0].Length));
        Assert.Equal(state.ControlChoices[1], Text(page, 14, 27, state.ControlChoices[1].Length));
        Assert.Equal(YellowOnBlack, Attribute(page, 13, 27));
        Assert.Equal(BlankRow, Text(page, 12, 0, TextScreen.Columns));
        Assert.Equal(BlankRow, Text(page, 15, 0, TextScreen.Columns));
        Assert.Equal((13, 50), (page.CursorRow, page.CursorColumn));
    }

    /// <summary>After the last answer the questions are gone and "One Moment Please..." is yellow on row 16 at column 29.</summary>
    [SkippableFact]
    public void TheLastWordsAreYellowOnRow16AndTheTitleStaysWhereItWas()
    {
        var (_, page, _, _) = Run(HostKeys.ForCharacter('1'), HostKeys.ForCharacter('2'));
        Assert.Equal("One Moment Please...", Text(page, 16, 29, 20));
        Assert.Equal(YellowOnBlack, Attribute(page, 16, 29));
        for (var row = 11; row <= 23; row++)
        {
            if (row != 16)
            {
                Assert.Equal(BlankRow, Text(page, row, 0, TextScreen.Columns));
            }
        }
        Assert.Equal(DoubleTopLeft, Character(page, 1, 1));
        Assert.Equal(DoubleBottomLeft, Character(page, 10, 1));
    }

    /// <summary>
    /// The set-up screen as the player's game shows it (<see cref="HardwareSetup.ShowTitle"/>, D-603): the title and the
    /// last words as after the questions, no question and no key read, two seconds of frames; the answers given.
    /// </summary>
    [SkippableFact]
    public void TheTitleAloneStaysUpForTwoSecondsWithTheAnswersOfTheSettings()
    {
        var folder = GameFiles();
        var state = new IntroState();
        IntroDgroup.Layout.Load(Executable.Read(folder.Read("INTRO.EXE")).DataSegment(), state);
        var graphics = new McgaGraphicsDriver(new FontSet(folder.Read("FONTS.CV")), McgaDriverFile.Read(folder.Read("MGRAPHIC.EXE")).DefaultColours);
        var host = new HeadlessHost([HostKeys.ForCharacter('1')]);
        var taken = 0;
        host.EntryTaken += _ => taken++;
        var page = new TextScreen();
        var link = new HostLink(graphics, host) { TextPage = page };
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), new GameFolderFiles(folder, null));
        var shared = new SharedState();

        new HardwareSetup(state, shared, world, page).ShowTitle('R', joystick: true);

        Assert.Equal(((sbyte)'R', (sbyte)'M', (sbyte)1), (shared.SndDriver, shared.GfxDriver, shared.Joystick));
        Assert.Equal(0, taken);
        Assert.Equal(HardwareSetup.TitleFrames, host.Frames);
        Assert.Equal("Covert Action (TM) - Version 447.01", Text(page, 3, 22, 35));
        Assert.Equal("One Moment Please...", Text(page, 16, 29, 20));
        Assert.Equal(YellowOnBlack, Attribute(page, 16, 29));
        for (var row = 11; row < TextScreen.Rows; row++)
        {
            if (row != 16)
            {
                Assert.Equal(BlankRow, Text(page, row, 0, TextScreen.Columns));
            }
        }
    }

    /// <summary>
    /// The player's start (<see cref="HardwareSetup.ShowTitle"/>) rendered as the SDL host shows it: the title's red and
    /// the last words' yellow, no question. When COVERT_SCREENSHOT_DIR names a folder, it is saved there as setup-title.png.
    /// </summary>
    [SkippableFact]
    public void TheTitleAloneRendersAndCanBeSavedAsAPicture()
    {
        var folder = GameFiles();
        var state = new IntroState();
        IntroDgroup.Layout.Load(Executable.Read(folder.Read("INTRO.EXE")).DataSegment(), state);
        var graphics = new McgaGraphicsDriver(new FontSet(folder.Read("FONTS.CV")), McgaDriverFile.Read(folder.Read("MGRAPHIC.EXE")).DefaultColours);
        var page = new TextScreen();
        var link = new HostLink(graphics, new HeadlessHost([])) { TextPage = page };
        var world = new Boundaries(graphics, new SilentSoundDriver(), new KeyboardDriver(link), new NoMouseDriver(), new GameTimer(link), new GameFolderFiles(folder, null));
        new HardwareSetup(state, new SharedState(), world, page).ShowTitle('A', joystick: true);

        var pixels = new uint[TextModeRaster.Width * TextModeRaster.Height];
        TextModeRaster.Render(page, TextFont.Builtin, 0, pixels);
        Assert.Contains(TextModeRaster.Colours[4], pixels);
        Assert.Contains(TextModeRaster.Colours[14], pixels);
        var shots = Environment.GetEnvironmentVariable("COVERT_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(shots))
        {
            Directory.CreateDirectory(shots);
            PngWriter.Write(pixels, TextModeRaster.Width, TextModeRaster.Height, Path.Combine(shots, "setup-title.png"));
        }
    }

    /// <summary>Renders the page as the SDL host does (720x400, 9x16 cells, the VGA's font) and, when COVERT_SCREENSHOT_DIR names a folder, saves it there as setup-textmode.png.</summary>
    [SkippableFact]
    public void ThePageRendersAs720By400AndCanBeSavedAsAPicture()
    {
        var (_, page, _, _) = Run();
        var pixels = new uint[TextModeRaster.Width * TextModeRaster.Height];
        TextModeRaster.Render(page, TextFont.Builtin, 0, pixels);
        Assert.Equal(720 * 400, pixels.Length);
        // the title's red and the questions' yellow are on the picture
        Assert.Contains(TextModeRaster.Colours[4], pixels);
        Assert.Contains(TextModeRaster.Colours[14], pixels);
        var folder = Environment.GetEnvironmentVariable("COVERT_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            PngWriter.Write(pixels, TextModeRaster.Width, TextModeRaster.Height, Path.Combine(folder, "setup-textmode.png"));
        }
    }
}
