using CovertAction.Content;

namespace CovertAction.Launcher;

/// <summary>One picture of the game that the Amiga pictures option may replace (D-642).</summary>
/// <param name="Name">The game's picture, as the game asks for its file ("faces.pic"; a city picture of the city archive by its name there).</param>
/// <param name="File">The Amiga version's picture in the player's Amiga files ("faces.pix").</param>
/// <param name="Reason">Why this picture may be replaced: what was checked, and how the game uses its pixels.</param>
/// <param name="Crop">The Amiga's picture is a full-screen canvas with the picture at its top left; the part the size of the game's picture is used.</param>
/// <param name="Backdrop">The game puts the picture on the screen as a whole and reads nothing back from it (no sheet to cut from, nothing recoloured): the Amiga's own composition shows, and its layout is not compared with the PC's.</param>
/// <param name="WindowWidth">For a city picture the game measures (D-681): the width the game measures the PC's picture to be; the Amiga's picture is used up to it (0: not a window).</param>
/// <param name="WindowHeight">The height the game measures the PC's picture to be.</param>
/// <param name="Arrange">Whole regions of the Amiga's picture moved to where the game's layout has them (D-685): a pure rearrangement of the Amiga's own pixels, applied first; null: none.</param>
public sealed record AmigaPictureEntry(string Name, string File, string Reason, bool Crop = false, bool Backdrop = false, int WindowWidth = 0, int WindowHeight = 0, Func<AmigaPictureFile, AmigaPictureFile>? Arrange = null);

/// <summary>
/// The pictures the Amiga pictures option replaces, one by one, each with the reason (D-642; new code, outside the game).
/// A picture is on the list when the Amiga version's has the same layout as the PC's (the edges of the two coincide: a score
/// of 0.95 or more of 1.00, from comparing every picture of the Amiga disks with its PC twin) so that every place the game
/// draws on it, cuts from it or measures it is right, <b>and</b> the game's code does not read or recolour its pixels by
/// the number of a colour of the PC (a highlight that recolours one colour of the picture, a colour compared with a pixel read
/// back), <b>and</b> it was looked at on the screen with the option on. Everything else stays the PC's.
/// </summary>
/// <remarks>
/// <para>
/// Not on the list, and why: <c>ad</c>, <c>camera</c>, <c>training</c>, <c>sneakin</c>, <c>wiretap</c>,
/// <c>guysnew2</c> and <c>guysnew3</c> are redrawn (different art or layout: the game's rectangles would cut the wrong
/// parts); <c>sprites</c> and <c>spritesf</c> are redrawn with 16 colours (the raid's guards, cell by cell not the same) and
/// the game recolours their wall colour; <c>bugs</c> (the electronics workbench's pieces) differs in layout; the cities
/// <c>geneva</c>, <c>helsinki</c>, <c>istanbul</c>, <c>khartoum</c>, <c>london</c>, <c>miami</c>, <c>rome</c> and <c>washingt</c>
/// are redrawn on another layout (scores 0.51 to 0.78); <c>havana</c> and <c>kingston</c> have another size of thumbnail;
/// <c>paris</c>, <c>moscow</c>, <c>berlin</c>, <c>amsterda</c>, <c>baghdad</c> and <c>riyadh</c> have the same layout (0.95 to
/// 0.98) but the game's measure of the picture's width and height (<c>load_picture_measure</c>: along the first row and column up
/// to the first colour 0) gives other numbers than for the PC's picture, so the game would cut it wrongly; <c>board</c> (the wiring puzzle reads the wires' colour back from the picture), <c>cars</c> (the car offer
/// recolours the bodies by colour number), <c>equip1</c> and <c>equip1m</c> (the equipment screen's cursor recolours a colour
/// of the picture); the Amiga-only pictures (<c>bchback</c>, <c>casback</c>, <c>lauback</c>, <c>offback</c>, <c>disk</c>,
/// <c>setupscr</c>, <c>titlesnew</c>, <c>chase2</c>, <c>samerica</c>, <c>screen00</c>) have no place in the PC game; the
/// animations (.pan) are another container and stay the PC's.
/// </para>
/// </remarks>
public static class AmigaPictureList
{
    private const string Sheet = "the same sheet as the PC's, cell for cell (score 1.00); cut into the parts the game draws, index 0 is the see-through colour in both";
    private const string Icons = "the same icons on the same grid (score 0.96); index 0 is the see-through colour in both";
    private const string Map = "the same map, the cities' dots and menu drawn over it (score 1.00)";
    private const string City = "the city's picture on the same layout (score at least 0.95): the picture at the top left of the canvas, colour 0 outside it, and the game's measure of its width and height (along the first row and the first column up to the first colour 0) gives the same numbers as for the PC's picture";
    private const string Windowed = "the same picture of the city as the PC's (the same composition, the Amiga's own dithering and colours) on the same canvas: the game measures the PC's picture along its first row and column, and the Amiga's is used up to that size, the pixels of those two lines that are colour 0 taking the colour of their neighbour on the line (D-681), so the game measures, frames and cuts it as it does the PC's";
    private const string Thumbnail = "the PC's thumbnail is the top left of the Amiga's full-screen canvas, the same picture (score at least 0.98 when cut to the PC's size)";

    /// <summary>The city pictures of the same layout whose measure the game gets the same: 15 of the 29 whose PC picture fills the screen's size.</summary>
    // declared before Entries: static fields are set in the order written
    private static readonly string[] CityNames =
    [
        "vienna", "athens", "telaviv", "madrid", "belgrade", "budapest", "prague", "cairo", "amman", "beirut", "tripoli", "damascus", "tehran", "bogota", "nassau",
    ];

    /// <summary>The cities of the same composition whose measure the game gets right only inside the PC's size (D-681), with that size as the game measures the PC's picture.</summary>
    private static readonly (string City, int Width, int Height)[] WindowedCities =
    [
        ("paris", 82, 89), ("moscow", 88, 84), ("berlin", 80, 64), ("amsterda", 112, 56), ("baghdad", 112, 64), ("riyadh", 96, 48),
        ("london", 54, 96), ("miami", 104, 64), ("rome", 80, 52),
    ];

    /// <summary>The cities whose PC picture is a thumbnail the size of the picture: the Amiga's is that thumbnail at the top left of a full canvas.</summary>
    private static readonly string[] ThumbnailNames = ["caracas", "losangel", "managua", "medellin", "mexicoci", "panama", "rio", "sanjuan"];

    /// <summary>The pictures that may be replaced.</summary>
    public static IReadOnlyList<AmigaPictureEntry> Entries { get; } =
    [
        new("faces.pic", "faces.pix", Sheet),
        new("facesf.pic", "facesf.pix", Sheet),
        new("icons.pic", "icons.pix", Icons),
        new("europe.pic", "europe.pix", Map),
        new("africa.pic", "africa.pix", Map),
        new("central.pic", "central.pix", Map),
        new("hotel.pic", "hotel.pix", "the hotel's lobby on the same layout (score 0.95), drawn on the screen with the menu over it; the menu's highlight recolours only the menu's own colours"),
        new("equip2.pic", "equip2.pix", "the loose item pictures the raid's status panel cuts its icons from (score 0.99); the cells cut have colour 0 as their see-through colour in both pictures (the Amiga's light background lies outside them)"),
        new("street.pic", "street.pix", "the car chase's windscreen views (score 0.96): copied to the windscreen as they are, and the game recolours only the dashboard"),
        .. CityNames.Select(city => new AmigaPictureEntry($"{city}.pic", $"{city}.pix", City)),
        .. ThumbnailNames.Select(city => new AmigaPictureEntry($"{city}.pic", $"{city}.pix", Thumbnail, Crop: true)),
        // DIVERGENCE: D-682 three more pictures: the ones the game puts on the screen whole
        new("sneakin.pic", "sneakin.pix", "the picture of the break-in (D-682): loaded on a page and dissolved onto the screen whole, with nothing drawn over it and no pixel read back; the Amiga's composition (the same hands at the door, placed further right) shows as it is", Backdrop: true),
        new("ad.pic", "ad.pix", "the picture shown when the player leaves the game (D-682): loaded on the screen whole while the game waits for a key; the Amiga's own advertisement shows", Backdrop: true),
        new("training.pic", "training.pix", "the training screen's backdrop (D-682): the four skills' cells at the PC's places (the game's menu is drawn at the top over the empty part and its rating boxes over the black strips under the cells); no pixel is read back and the menu recolours only its own colours", Backdrop: true),
        // DIVERGENCE: D-685 the Amiga's menu picture has the woman on the left and the man on the right: its halves are exchanged
        new("gender.pic", "gender.pix", "the choice of agent (D-685): the Amiga's picture shows the woman on the left and the man on the right, the reverse of the game's Max and Maxine; its two halves are exchanged (nothing is drawn: every pixel is the Amiga's, in its colour), and the game puts it on the screen whole and reads no pixel", Backdrop: true, Arrange: picture => picture.SwapHalves()),
        .. WindowedCities.Select(city => new AmigaPictureEntry($"{city.City}.pic", $"{city.City}.pix", Windowed, WindowWidth: city.Width, WindowHeight: city.Height)),
    ];

    /// <summary>The entry for a picture file name of the game (compared without regard to letter case); null when the picture stays the PC's.</summary>
    /// <param name="name">The file name the game asks for.</param>
    public static AmigaPictureEntry? Find(string name) => Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}
