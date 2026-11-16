using CovertAction.Core;
using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Persistence.Dgroup;
using CovertAction.Persistence.Layout;
using CovertAction.Platform;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Final;

/// <summary>
/// The menus part as the player sees it (the original's FINAL program): the agency's headquarters, where the agent
/// is created, briefed on a new case and debriefed on a finished one, and where the career and the Hall of Fame are
/// shown. This class holds the functions of FINAL that draw, read keys or files; the rules they apply are in
/// <see cref="CaseSetup"/>, <see cref="CaseReview"/> and <see cref="FinalTexts"/>.
/// </summary>
public sealed partial class Headquarters
{
    private readonly FinalState _state;
    private readonly Boundaries _world;
    private readonly OriginalBehaviour _behaviour;
    private readonly SharedState _shared;

    /// <summary>Makes the part over its state, the hand-off block and the drivers.</summary>
    /// <param name="state">The part's variables.</param>
    /// <param name="shared">The hand-off block.</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce.</param>
    public Headquarters(FinalState state, SharedState shared, Boundaries world, OriginalBehaviour behaviour)
    {
        _state = state;
        _behaviour = behaviour;
        _world = world;
        _shared = shared;
        Output = new LibraryOutput(state, world);
        Input = new LibraryInput(state, world);
        Pictures = new LibraryPictures(world);
        Animation = new LibraryAnimation(state, state.Pan, world, Output, Input, Pictures, behaviour);
        // FINAL's copy of the message box runs the animation beside it through the animation player
        Menus = new LibraryMenus(state, shared, world, Output, Input, player: Animation);
        Text = new LibraryText(state, state, world, Output, state.Random, behaviour);
        Screens = new LibraryScreens(state, state, world, Output, Input, Text);
        // a save file that cannot be opened is said in FINAL's own box
        HandOff = new LibraryHandOff(state, shared, world, (text, x, y) => MenuChoice(text, x, y));
        Saves = new LibrarySaves(state, world, Input, Menus, HandOff.ReadEnvState, MenuChoice, WriteSaveGame);
    }

    /// <summary>The library's save screens (drive, slots, load and save), on this part's variables.</summary>
    public LibrarySaves Saves { get; }

    /// <summary>The library's routines that take the campaign over and hand it on, on this part's copy of it.</summary>
    public LibraryHandOff HandOff { get; }

    /// <summary>The library's picture and archive files, as this part opens them.</summary>
    public LibraryPictures Pictures { get; }

    /// <summary>The animation player (the library's), on this part's variables.</summary>
    public LibraryAnimation Animation { get; }

    /// <summary>The library's text readers and text drawing, on this part's variables.</summary>
    public LibraryText Text { get; }

    /// <summary>The library's screens of the campaign (faces, photos, report frames), on this part's variables.</summary>
    public LibraryScreens Screens { get; }

    /// <summary>The library's drawing and sound routines, on this part's variables.</summary>
    public LibraryOutput Output { get; }

    /// <summary>The library's key and joystick routines, on this part's variables.</summary>
    public LibraryInput Input { get; }

    private LibraryDossiers? _dossiers;

    /// <summary>The library's screens about persons (the dossier, new facts), on this part's variables.</summary>
    public LibraryDossiers Dossiers => _dossiers ??= new LibraryDossiers(_state, _state, _state, _state.Dossier, Output, Text, Screens, _state, _state.Random);

    /// <summary>The library's message box and disk prompt, on this part's variables.</summary>
    public LibraryMenus Menus { get; }

    /// <summary>Shows a box with a text (and its choices, if it has any) and waits for the player's answer.</summary>
    /// <param name="text">The text: the part's text buffer.</param>
    /// <param name="x">Left edge of the box.</param>
    /// <param name="y">Top edge of the box.</param>
    /// <returns>The chosen line (0 = the first choice), or -1.</returns>
    // orig: FINAL 0100:2c80 menu_choice (src/c/final/final_0100_2c80.c) matched; tests: FinalOracleTests
    // C: the original has no return statement and gives back what quit_prompt left in AX, its answer.
    public int MenuChoice(TextBuffer text, int x, int y) => Menus.QuitPrompt(text, x, y);

    /// <summary>
    /// Adds a record to the case log, stamped with the clock: of type <paramref name="a"/> about
    /// <paramref name="b"/>. For a plot event (type 0) the record also gets the person who acted, his city and how
    /// alert he was, and the person acted upon. When the log is full, a box says so.
    /// </summary>
    /// <param name="a">The type of the record: 0 a plot event, -1 unused, others the kinds of message.</param>
    /// <param name="b">What the record is about: for a plot event the event's index.</param>
    // orig: FINAL 0100:8c2c add_msg_record (src/c/final/final_0100_8c2c.c) matched; tests: FinalOracleTests
    public void AddMsgRecord(int a, int b)
    {
        const int plotEvent = 0, targetBits = 0xfff, overflowBoxX = 0x32, overflowBoxY = 0x32;
        var record = _state.Sn[_state.NextSyn];
        record.Time = _state.Gtime;
        record.Type = (short)a;
        record.Data = (short)b;
        record.Clues = 0;
        record.Who = -1;
        record.Alert = 0;
        if (a == plotEvent)
        {
            var happening = _state.Ev[b];
            var actor = _state.Ac[happening.Act].Who;
            // C: the person and the city are kept as chars, the low bytes of their words
            record.Who = (sbyte)actor;
            record.Loc = (sbyte)_state.Ps[actor].Loc;
            record.Alert = _state.Ps[actor].Alert;
            if ((happening.Status & targetBits) != 0)
            {
                // the low byte of the event's status is the cast member it happens to
                var target = _state.Ac[(byte)happening.Status].Who;
                record.Who2 = (sbyte)target;
                record.Loc2 = (sbyte)_state.Ps[target].Loc;
            }
        }
        _state.NextSyn++;
        if (_state.NextSyn >= CampaignTables.SynopsisCount)
        {
            _state.Txt.Text = _state.StrLogOverflow;
            MenuChoice(_state.Txt, overflowBoxX, overflowBoxY);
        }
    }

    /// <summary>Empty: called just before the part hands over to the next one (a hook whose body was removed).</summary>
    // orig: FINAL 0100:0538 shutdown_hook (src/c/final/final_0100_0538.c) matched; tests: FinalOracleTests
    public void ShutdownHook()
    {
    }

    /// <summary>Empty: called after a box has been cleared away (a hook whose body was removed).</summary>
    // orig: FINAL 0100:0539 after_menu_clear_hook (src/c/final/final_0100_0539.c) matched; tests: FinalOracleTests
    public void AfterMenuClearHook()
    {
    }

    /// <summary>Empty: called after a hint has been shown (a hook whose body was removed).</summary>
    // orig: FINAL 0100:067c after_hint_hook (src/c/final/final_0100_067c.c) matched; tests: FinalOracleTests
    public void AfterHintHook()
    {
    }

    /// <summary>Clears the text box of the briefing, below its picture, to the box colour.</summary>
    // orig: FINAL 0100:173c clear_text_box (src/c/final/final_0100_173c.c) matched; tests: FinalOracleTests
    public void ClearTextBox()
    {
        const int left = 4, top = 0x95, width = 0x138, height = 0x30, boxColour = 5;
        _world.Graphics.FillRect(_state.Rp1, left, top, width, height, boxColour);
    }

    /// <summary>
    /// Reads the crime file of a case, CRIMEn.DTA: the numbers of cast members and events, the cast's and the events'
    /// records, and the four objects of the plot.
    /// </summary>
    /// <param name="p">The crime file's number, 0..13 (one or two digits in the name).</param>
    // orig: FINAL 0100:4b82 load_crime_file (src/c/final/final_0100_4b82.c) matched; tests: FinalOracleTests
    public void LoadCrimeFile(int p)
    {
        var name = _state.CrimeFileName;
        name[FinalState.FileNumberAt] = Digit(p);
        if (p > 9)
        {
            CharTable.Put(name, FinalState.FileNumberAt, _state.StrTenDta);
            // C: a 16-bit remainder
            name[FinalState.FileNumberAt + 1] = Digit(C16.Rem(C16.Int(p), 10));
        }
        var file = _world.Files.Open(CharTable.TextOf(name), OpenMode.Binary);
        int Read(byte[] buffer) => _world.Files.Read(file, buffer);
        _state.NACTIVITY = FinalDgroup.ReadWord(Read, _state.NACTIVITY);
        _state.NEVENT = FinalDgroup.ReadWord(Read, _state.NEVENT);
        FinalDgroup.ReadRecords(Read, CampaignLayouts.Activity, CampaignLayouts.ActivitySize, _state.Ac, _state.NACTIVITY);
        FinalDgroup.ReadRecords(Read, CampaignLayouts.Event, CampaignLayouts.EventSize, _state.Ev, _state.NEVENT);
        FinalDgroup.ReadRecords(Read, CampaignLayouts.MacGuffins, CampaignLayouts.MacGuffinsSize, _state.Mc, _state.Mc.Length);
        _world.Files.Close(file);
    }

    /// <summary>
    /// Reads a world file, WORLDn.DTA: the numbers of cities and organisations it holds (16 each), then their records.
    /// </summary>
    /// <param name="worldNo">The world, 0..2.</param>
    // orig: FINAL 0100:4c2c load_world_file (src/c/final/final_0100_4c2c.c) matched; tests: FinalOracleTests
    public void LoadWorldFile(int worldNo)
    {
        var name = _state.WorldFileName;
        // C: the number is a char argument: only its low byte counts
        name[FinalState.FileNumberAt] = Digit(C16.Char(worldNo));
        var file = _world.Files.Open(CharTable.TextOf(name), OpenMode.Binary);
        int Read(byte[] buffer) => _world.Files.Read(file, buffer);
        // the counts are the file's; until it gives them they are a whole world's
        var cities = FinalDgroup.ReadWord(Read, (short)_state.Ct.Length);
        var orgs = FinalDgroup.ReadWord(Read, (short)_state.Org.Length);
        FinalDgroup.ReadRecords(Read, CampaignLayouts.City, CampaignLayouts.CitySize, _state.Ct, cities);
        FinalDgroup.ReadRecords(Read, CampaignLayouts.Org, CampaignLayouts.OrgSize, _state.Org, orgs);
        _world.Files.Close(file);
    }

    /// <summary>A number's last digit as the original writes it into a file name: '0' added, the low byte kept.</summary>
    private static char Digit(int value) => (char)C16.UChar(value + '0');
}
