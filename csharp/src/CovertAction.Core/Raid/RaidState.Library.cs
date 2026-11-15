using CovertAction.Core.Graphics;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Raid;

/// <summary>
/// This part's variables of the library's text and screen routines (<see cref="ILibraryTextState"/>,
/// <see cref="ILibraryScreenState"/>): the placeholders of the message texts, the face beside the next box, the photo's
/// colours and the table of images the faces are drawn from; and its campaign tables as the library's screens about
/// persons see them (<see cref="ICampaignTables"/>), with the texts of those screens.
/// </summary>
public sealed partial class RaidState : ILibraryTextState, ILibraryScreenState, ICampaignTables
{
    /// <summary>The texts of the library's screens about persons (dossier, personnel file, clue report, names) and their buffers.</summary>
    public DossierTexts Dossier { get; } = new();

    IReadOnlyList<Person> ICampaignTables.Ps => Ps;

    IReadOnlyList<Org> ICampaignTables.Orgs => Org;

    IReadOnlyList<City> ICampaignTables.Ct => Ct;

    IReadOnlyList<Clue> ICampaignTables.Cl => Cl;

    IReadOnlyList<Synopsis> ICampaignTables.Sn => Sn;

    IReadOnlyList<Event> ICampaignTables.Ev => Ev;

    IReadOnlyList<Activity> ICampaignTables.Ac => Ac;

    IList<short> ICampaignTables.Active => Active;

    IList<short> ICampaignTables.OrgCounters => OrgCounters;

    /// <summary>
    /// Message -1: the 14 bytes in front of <see cref="Sn"/> (TAC DS:a1d8), which the message log reads as the partner of
    /// message 0 when that has no partner. In the raid they are other variables: its minute is
    /// <see cref="RoomLastVisit"/>[22], its knowledge word <see cref="OpenDoorCount"/>, its second city the high byte of
    /// <see cref="GameMode"/>.
    /// </summary>
    // DIVERGENCE: D-500 a change to message -1 is not written back into the raid's variables it lies over
    public Synopsis MessageBeforeFirst => new() { Time = RoomLastVisit[22], Clues = OpenDoorCount, Loc2 = unchecked((sbyte)(GameMode >> 8)) };
    /// <summary>TAC DS:c9ea token_tail_buf: the placeholder routine's scratch line.</summary>
    public TextBuffer TokenTail { get; } = new(ILibraryTextState.LineCapacity);

    /// <summary>TAC DS:2cee.. (table DS:2e10): the message texts' placeholders, "$VICTIM" ... "$OBJECT".</summary>
    public string[] VariableNameTexts { get; } = new string[ILibraryTextState.MessageVariables];

    /// <summary>TAC DS:2d26.. (table DS:2e1e): the placeholders' current values.</summary>
    public string[] VariableValueTexts { get; } = new string[ILibraryTextState.MessageVariables];

    IReadOnlyList<string> ILibraryTextState.VariableNames => VariableNameTexts;

    IList<string> ILibraryTextState.VariableValues => VariableValueTexts;

    /// <summary>TAC DS:2e46: "This information requires security clearance: ".</summary>
    public string ClearanceNotice { get; set; } = "";

    /// <summary>TAC DS:2e3d: the blank the text readers put behind every line.</summary>
    public string LineSeparator { get; set; } = "";

    /// <summary>
    /// TAC DS:a8e6 picface (MicroProse's name): the face shown beside the next speech box, a person's face word; -1 for
    /// none, -2 for the picture of a building (<see cref="BalloonPic"/>). It is the box routine's own variable (the
    /// library's <see cref="Menus.AnimationState.BackdropCode"/>), under the raid's name for it.
    /// </summary>
    public short Picface
    {
        get => Animation.BackdropCode;
        set => Animation.BackdropCode = value;
    }

    /// <summary>TAC DS:1b88: the colour tables a photo is drawn with on displays with fewer colours.</summary>
    public byte[] PhotoColorMap { get; } = new byte[16];

    /// <summary>The library's faces and photos are drawn from this part's table of icons, <see cref="IconHandles"/>.</summary>
    IReadOnlyList<Image?> ILibraryScreenState.Sprite => IconHandles;

    /// <summary>TAC DS:91e2 balloon_x: the screen x the speech balloon points at (the photographed object, or the file room's screen).</summary>
    public short BalloonX { get; set; }

    /// <summary>TAC DS:91e6 balloon_y: the screen y the speech balloon points at.</summary>
    public short BalloonY { get; set; }

    /// <summary>TAC DS:9b6e balloon_bg and DS:9b70 balloon_pic: the speech balloon and the small picture of a building drawn in it.</summary>
    public Image?[] BalloonImages { get; } = new Image?[BalloonImageCount];

    /// <summary>The entries of <see cref="BalloonImages"/>: the balloon, the building's picture.</summary>
    public const int BalloonImageCount = 2, BalloonBg = 0, BalloonPic = 1;
}
