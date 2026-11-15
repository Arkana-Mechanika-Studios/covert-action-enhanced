using CovertAction.Core.Shared;

namespace CovertAction.Core.Final;

/// <summary>The variables of the title screen, the main menu and the save screens (the menus part's own, not the library's).</summary>
public sealed partial class FinalState : ILibrarySaveState
{
    /// <summary>FINAL DS:a5a6 entry_mode: how the part was entered, 1 for a fresh start, -1 from the code 0x11, 0 for a return from a finished case.</summary>
    public short EntryMode { get; set; }

    /// <summary>
    /// FINAL DS:9bee, MicroProse's <c>pic</c>: FINAL's copy of GAME's screen state (GAME DS:93ee, -1 for a clean
    /// screen). The title's start-up sets it to -1 as GAME's game_main does; FINAL has no reader (its address stands
    /// only in that store in the whole program, and the Amiga build's FINAL start-up writes its <c>pic</c> likewise).
    /// </summary>
    public short Pic { get; set; }

    /// <summary>
    /// FINAL DS:9e86: FINAL's copy of GAME's "missions played" word (GAME DS:9686, read by its building_menu), which
    /// the title's start-up sets to 1 as GAME's game_main does. FINAL has no reader (only that store names the address).
    /// </summary>
    public short MissionsPlayed { get; set; }

    /// <summary>FINAL DS:1572 cga_map_title_a: the four-colour display's colours for the title screen (first table).</summary>
    public byte[] TitleColorMapA { get; } = new byte[16];

    /// <summary>FINAL DS:1582 cga_map_title_b: the second table of the same call.</summary>
    public byte[] TitleColorMapB { get; } = new byte[16];

    /// <summary>FINAL DS:15e2 cga_map_title_pan: the colours for the title and credits animations.</summary>
    public byte[] TitlePanColorMap { get; } = new byte[16];

    /// <summary>The texts of the title and the creation of the agent, by <see cref="TitleText"/>.</summary>
    public string[] TitleTexts { get; } = [.. Enumerable.Repeat("", (int)TitleText.Count)];

    /// <summary>The four copies of "env.sve" the title passes to the hand-off routines (FINAL DS:0076, 00f4, 0178, 0180).</summary>
    public string[] TitleHandOffFiles { get; } = ["", "", "", ""];

    /// <summary>FINAL DS:1666 pan_picture_name: the buffer that holds the name of the animation <c>play_pan_picture</c> plays.</summary>
    public TextBuffer PanPictureName { get; } = new(14);

    /// <summary>The save screens' variables and texts.</summary>
    public SaveScreenState Saves { get; } = new();

    /// <summary>The names of the two faces files: "facesf.pic" (women, FINAL DS:3fc6) and "faces.pic" (men, DS:3fd1).</summary>
    public string[] FacesFileNames { get; } = ["", ""];
}
