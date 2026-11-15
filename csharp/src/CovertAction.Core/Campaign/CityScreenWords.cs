using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The texts of the campaign's walk: the city screen with what the player comes back to from a mission (a jail break,
/// an escape, the car tracer, a capture, wounds, an ambush), the hotel, the menu of the data files, the flight map,
/// the city's picture, the part's start and its loading of a save. Texts of the program, read from the player's
/// GAME.EXE.
/// </summary>
/// <remarks>
/// The box shown while the part hands over, "One moment please..." (GAME DS:007e), is <see cref="CampaignState.TextBeforePersons"/>.
/// The original keeps one copy of a text for each place that uses it (the line breaks "\n " twice in the city screen,
/// "env.sve" in the start and in the loader, "JailBreak in " here and in the capture scene); the rewrite keeps each
/// copy where its function reads it, so that each text stays the one its place of the program holds.
/// </remarks>
// orig: GAME DS:0076 the hand-off name of game_main (src/c/game/game_00f7_005a.c); DS:206e..21c5 of city_screen (game_00f7_1608.c); DS:26ee..274e of travel_menu; DS:2854..2866 of draw_city_picture and load_picture_measure; DS:32ea..33ed of hotel_menu and data_files_menu; DS:41a8..41be of load_game
public sealed class CityScreenWords
{
    // ---- the part's start (game_main) ----

    /// <summary>GAME DS:0076: the name under which the campaign comes in through the hand-off block ("env.sve").</summary>
    public string HandOffLoad { get; set; } = "";

    // ---- the city screen: the jail break ----

    /// <summary>GAME DS:206e: the animation of the jail-break warning ("warning.pan").</summary>
    public string WarningScene { get; set; } = "";

    /// <summary>GAME DS:207a: the start of the warning ("JailBreak in "), before the prisoner's city.</summary>
    public string JailBreakIn { get; set; } = "";

    /// <summary>GAME DS:2088: after the city.</summary>
    public string JailBreakCityEnd { get; set; } = "";

    /// <summary>GAME DS:208b: after the prisoner's organisation, before his name (" agents are trying to free ").</summary>
    public string JailBreakAgentsFree { get; set; } = "";

    /// <summary>GAME DS:20a7: after the prisoner's name.</summary>
    public string JailBreakEnd { get; set; } = "";

    /// <summary>GAME DS:20aa: the animation of a prisoner breaking out ("bustout.pan").</summary>
    public string BustOutScene { get; set; } = "";

    /// <summary>GAME DS:20b6: after the name of a prisoner who got away (" escapes!").</summary>
    public string Escapes { get; set; } = "";

    /// <summary>GAME DS:20c1: the player stopped the jail break.</summary>
    public string EscapeFoiled { get; set; } = "";

    // ---- the city screen: back from a chase, a raid, an ambush ----

    /// <summary>GAME DS:20d9: the car tracer found a building of the quarry's organisation.</summary>
    public string CarTracerBuilding { get; set; } = "";

    /// <summary>GAME DS:2107: the tag of the prose text of the player's capture after a chase ("*carcap"); the reader may change it.</summary>
    public TextBuffer CarCaptureProseKey { get; } = new(8);

    /// <summary>GAME DS:210f: the animation of the wounded agent ("wounded.pan").</summary>
    public string WoundedScene { get; set; } = "";

    /// <summary>GAME DS:211b: the player takes time to recover from his wounds.</summary>
    public string RecoverFromWounds { get; set; } = "";

    /// <summary>GAME DS:2146: the tag of the prose text of an ambush the player lost on the easiest level ("*surpriseL").</summary>
    public TextBuffer AmbushLostProseKey { get; } = new(11);

    /// <summary>GAME DS:2151: the tag of the prose text of an ambush the player won ("*surpriseW").</summary>
    public TextBuffer AmbushWonProseKey { get; } = new(11);

    /// <summary>GAME DS:21bc: the tag of the prose text of an ambush on the way to a building ("*surprise").</summary>
    public TextBuffer AmbushProseKey { get; } = new(10);

    // ---- the city screen: the menu of places ----

    /// <summary>GAME DS:215c: the start of the menu ("You are in "), before the city's name.</summary>
    public string YouAreIn { get; set; } = "";

    /// <summary>GAME DS:2168: the question after the city's name.</summary>
    public string DoYouGoTo { get; set; } = "";

    /// <summary>GAME DS:217b: the first place, the airport.</summary>
    public string TheAirport { get; set; } = "";

    /// <summary>GAME DS:218a: the second place, the hotel (and the indent of the line after it).</summary>
    public string YourHotel { get; set; } = "";

    /// <summary>GAME DS:2199: a building the player knows of but has not identified.</summary>
    public string UnknownBuilding { get; set; } = "";

    /// <summary>GAME DS:21aa: the end of a building's line and the indent of the next.</summary>
    public string BuildingLineEnd { get; set; } = "";

    /// <summary>GAME DS:21ad: the end of a contact's line and the indent of the next.</summary>
    public string ContactLineEnd { get; set; } = "";

    /// <summary>GAME DS:21b0: the last place, the data files.</summary>
    public string CheckData { get; set; } = "";

    // ---- the flight map (travel_menu) ----

    /// <summary>GAME DS:26ee: the map of the European world ("europe.pic").</summary>
    public string EuropeMap { get; set; } = "";

    /// <summary>GAME DS:26f9: the map of the African world ("africa.pic").</summary>
    public string AfricaMap { get; set; } = "";

    /// <summary>GAME DS:2704: the map of the Central American world ("central.pic").</summary>
    public string CentralAmericaMap { get; set; } = "";

    /// <summary>GAME DS:2710: the start of the travel menu ("You're in "), before the city's name.</summary>
    public string YoureIn { get; set; } = "";

    /// <summary>GAME DS:271b: the question after the city's name.</summary>
    public string DoYouTravelTo { get; set; } = "";

    /// <summary>GAME DS:2732: the first line, staying in the city.</summary>
    public string StayHere { get; set; } = "";

    /// <summary>GAME DS:273e: the indent of a city's line.</summary>
    public string CityIndent { get; set; } = "";

    /// <summary>GAME DS:2740: the end of a city's line.</summary>
    public string CityLineEnd { get; set; } = "";

    /// <summary>GAME DS:2742: the last line, the data files.</summary>
    public string TravelCheckData { get; set; } = "";

    // ---- the city's picture ----

    /// <summary>GAME DS:2854: the end of a city picture's file name (".pic").</summary>
    public string PictureExtension { get; set; } = "";

    /// <summary>GAME DS:2859: between the city and its country in the caption (", ").</summary>
    public string CountrySeparator { get; set; } = "";

    /// <summary>GAME DS:285c: the archive of the city pictures ("cities.cat").</summary>
    public string CityArchive { get; set; } = "";

    // ---- the hotel and the data files ----

    /// <summary>GAME DS:32ea: the hotel's picture ("hotel.pic").</summary>
    public string HotelPicture { get; set; } = "";

    /// <summary>GAME DS:32f4: the hotel's menu (leave, the lounge, sleep, save, load, quit).</summary>
    public string HotelMenu { get; set; } = "";

    /// <summary>GAME DS:3367: the tag of the prose text of the hotel's lounge ("*lounge"); the reader may change it.</summary>
    public TextBuffer LoungeProseKey { get; } = new(8);

    /// <summary>GAME DS:336f: the first part of the data files' menu (clues to news bulletins).</summary>
    public string DataFilesMenuStart { get; set; } = "";

    /// <summary>GAME DS:33b3: the rest of the data files' menu (organisations, cities, activity reports).</summary>
    public string DataFilesMenuEnd { get; set; } = "";

    // ---- loading a save (load_game) ----

    /// <summary>GAME DS:41a8: the name that means "from the hand-off block" to the loader ("env.sve").</summary>
    public string HandOffName { get; set; } = "";

    /// <summary>GAME DS:41b0: the start of the box of a save file that cannot be opened ("File Error: ").</summary>
    public string FileError { get; set; } = "";

    /// <summary>GAME DS:41bd: after the file's name.</summary>
    public string FileErrorEnd { get; set; } = "";
}
