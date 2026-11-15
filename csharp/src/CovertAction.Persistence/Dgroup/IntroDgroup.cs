using CovertAction.Core.Intro;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

/// <summary>
/// The data segment (DGROUP) of the original's set-up program, INTRO.EXE: where each value of
/// <see cref="IntroState"/> lies in it. The game's set-up screen takes its texts from the player's own INTRO.EXE
/// through it, and the oracle tests read the data segments the original was recorded with.
/// </summary>
/// <remarks>
/// Offsets are those of the data list in INTRO 0000:0010. Not modelled: the name of the input driver
/// ("misc.exe", DS:01fe, D-2), the five driver file names at DS:04fc (unused by the program), the program's working tables (the letters chosen, DS:07c6..082c, and the
/// register block of the video BIOS, DS:07de) and the C library's own data.
/// </remarks>
// orig: INTRO DGROUP (src/c/intro/intro_0000_0010.c)
public static class IntroDgroup
{
    /// <summary>The length of one row of the PC's text page as INTRO keeps it: 80 characters and the end mark.</summary>
    private const int PageRow = 81;

    /// <summary>The length of the text that blanks a row: 79 blanks, CR, LF, "$" and the end mark.</summary>
    private const int BlankRowLength = 83;

    /// <summary>The values of INTRO.EXE at their offsets.</summary>
    public static StateLayout<IntroState> Layout { get; } = new StateLayout<IntroState>("INTRO DGROUP")
        .Text(0x015a, BlankRowLength, "DS:015a the text that blanks a row", s => s.BlankRow, (s, v) => s.BlankRow = v)
        .Text(0x01ad, PageRow, "DS:01ad framed row without text", s => s.BorderRow, (s, v) => s.BorderRow = v)
        .Text(0x0207, 3, "DS:0207 colour of the title frame", s => s.TitleColour, (s, v) => s.TitleColour = v)
        .Text(0x020a, PageRow, "DS:020a top row of the frame", s => s.TopFrame, (s, v) => s.TopFrame = v)
        .Text(0x025b, PageRow, "DS:025b title row: the version", s => s.TitleRows[0], (s, v) => s.TitleRows[0] = v)
        .Text(0x02ac, PageRow, "DS:02ac title row: the authors", s => s.TitleRows[1], (s, v) => s.TitleRows[1] = v)
        .Text(0x02fd, PageRow, "DS:02fd title row: the copyright", s => s.TitleRows[2], (s, v) => s.TitleRows[2] = v)
        .Text(0x034e, PageRow, "DS:034e title row: the reservation", s => s.TitleRows[3], (s, v) => s.TitleRows[3] = v)
        .Text(0x039f, PageRow, "DS:039f bottom row of the frame", s => s.BottomFrame, (s, v) => s.BottomFrame = v)
        .Text(0x03f0, PageRow, "DS:03f0 the graphics question", s => s.GraphicsPrompt, (s, v) => s.GraphicsPrompt = v)
        .Text(0x0441, PageRow, "DS:0441 the sound question", s => s.SoundPrompt, (s, v) => s.SoundPrompt = v)
        .Text(0x0492, 25, "DS:0492 \"1. Joystick and Keyboard\"", s => s.ControlChoices[0], (s, v) => s.ControlChoices[0] = v)
        .Text(0x04ab, 17, "DS:04ab \"2. Keyboard only\"", s => s.ControlChoices[1], (s, v) => s.ControlChoices[1] = v)
        .Text(0x04bc, 3, "DS:04bc colour of the last words", s => s.WaitColour, (s, v) => s.WaitColour = v)
        .Text(0x04bf, 21, "DS:04bf \"One Moment Please...\"", s => s.OneMoment, (s, v) => s.OneMoment = v)
        .TextPointer(0x04d4, "DS:04d4 gfx_menu_text[0]", (s, v) => s.GraphicsChoices[0] = v)
        .TextPointer(0x04d6, "DS:04d6 gfx_menu_text[1]", (s, v) => s.GraphicsChoices[1] = v)
        .TextPointer(0x04d8, "DS:04d8 gfx_menu_text[2]", (s, v) => s.GraphicsChoices[2] = v)
        .TextPointer(0x04da, "DS:04da gfx_menu_text[3]", (s, v) => s.GraphicsChoices[3] = v)
        .TextPointer(0x04dc, "DS:04dc gfx_menu_text[4]", (s, v) => s.GraphicsChoices[4] = v)
        .TextPointer(0x04de, "DS:04de snd_menu_text[0]", (s, v) => s.SoundChoices[0] = v)
        .TextPointer(0x04e0, "DS:04e0 snd_menu_text[1]", (s, v) => s.SoundChoices[1] = v)
        .TextPointer(0x04e2, "DS:04e2 snd_menu_text[2]", (s, v) => s.SoundChoices[2] = v)
        .TextPointer(0x04e4, "DS:04e4 snd_menu_text[3]", (s, v) => s.SoundChoices[3] = v)
        .TextPointer(0x04e6, "DS:04e6 snd_menu_text[4]", (s, v) => s.SoundChoices[4] = v)
        .TextPointer(0x04e8, "DS:04e8 snd_menu_text[5]", (s, v) => s.SoundChoices[5] = v)
        .Text(0x04ea, 7, "DS:04ea snd_driver_letters", s => s.SoundLetters, (s, v) => s.SoundLetters = v)
        .Text(0x04f6, 6, "DS:04f6 gfx_driver_letters", s => s.GraphicsLetters, (s, v) => s.GraphicsLetters = v);
}
