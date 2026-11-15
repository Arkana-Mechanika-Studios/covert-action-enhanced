using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Raid;

/// <summary>
/// The variables and texts of what the player learns in the building (<c>gather_intelligence</c>): the notes boxes of
/// the messages found and the hints shown when nothing new was learned.
/// </summary>
public sealed partial class RaidState
{
    /// <summary>TAC DS:25a2 popcount4: the number of bits set in each value 0..31 (the facts known of a cast member or message).</summary>
    public byte[] PopCount { get; } = new byte[32];

    /// <summary>TAC DS:2f2a..: " hideout", " agent", " safehouse", " active cel", " office": the kind of an organisation's building, levels 1..5.</summary>
    public string[] BuildingKinds { get; } = new string[5];

    /// <summary>TAC DS:24bc: the notes box's heading for a coded message, "       CODED MESSAGE:" and a line end.</summary>
    public string StrCodedMessage { get; set; } = "";

    /// <summary>TAC DS:24d3: the heading for the notes of a meeting, "       MEETING NOTES:" and a line end.</summary>
    public string StrMeetingNotes { get; set; } = "";

    /// <summary>TAC DS:24ea: "       NEW INFORMATION" and a line end, for a message of which nothing was known.</summary>
    public string StrNewInformation { get; set; } = "";

    /// <summary>TAC DS:2502: "    ADDITIONAL INFORMATION" and a line end, for a message of which something was known.</summary>
    public string StrAdditionalInformation { get; set; } = "";

    /// <summary>TAC DS:251e: the blank between the person's name and the message line.</summary>
    public string StrNotesSpace { get; set; } = "";

    /// <summary>TAC DS:2520: a line end and "... ", before what the notes tell.</summary>
    public string StrNotesEllipsis { get; set; } = "";

    /// <summary>TAC DS:2526: "on ", before the month.</summary>
    public string StrOn { get; set; } = "";

    /// <summary>TAC DS:252a: the blank between the month and the day.</summary>
    public string StrDateSpace { get; set; } = "";

    /// <summary>TAC DS:252c: "of the ", before the other party's organisation.</summary>
    public string StrOfThe { get; set; } = "";

    /// <summary>TAC DS:2534: "in ", before the message's city.</summary>
    public string StrIn { get; set; } = "";

    /// <summary>TAC DS:2538: the line end of the notes.</summary>
    public string StrNotesEnd { get; set; } = "";

    /// <summary>TAC DS:253a: the empty text a hint starts from.</summary>
    public string StrHintEmpty { get; set; } = "";

    /// <summary>TAC DS:253b: a blank, the text of a hint that shows a face.</summary>
    public string StrHintFace { get; set; } = "";

    /// <summary>TAC DS:253d: " in ", between a building and its city in a hint.</summary>
    public string StrHintIn { get; set; } = "";

    /// <summary>TAC DS:bb7a result_bits: what the raid has achieved besides its worth, handed back to the campaign.</summary>
    public RaidResults ResultBits { get; set; }

    /// <summary>TAC DS:1f36 item_pick: the eight pieces of equipment a safe can hold (bit numbers of <see cref="Equip"/>).</summary>
    public short[] ItemPick { get; } = new short[8];

    /// <summary>TAC DS:2034.. (table DS:20e0) item_names: the names of the pieces of equipment by bit number, "Pistol" ... "Floor Plan".</summary>
    public string[] ItemNames { get; } = new string[16];

    /// <summary>TAC DS:1e78: "The CIA agent in" and a line end, before the city of a double agent found.</summary>
    public string StrCiaAgentIn { get; set; } = "";

    /// <summary>TAC DS:1e8a: " is", a line end and "a double agent!!".</summary>
    public string StrIsDoubleAgent { get; set; } = "";

    /// <summary>TAC DS:1ea0: " now", a line end and "in ", between a plot object and the city it is in.</summary>
    public string StrNowIn { get; set; } = "";

    /// <summary>TAC DS:1ea9: "!" and a line end, after the city.</summary>
    public string StrObjectEnd { get; set; } = "";

    /// <summary>TAC DS:1eac: "You find" and a line end, before a piece of equipment found.</summary>
    public string StrYouFind { get; set; } = "";

    /// <summary>TAC DS:1eb6: "!" and a line end, after the piece of equipment.</summary>
    public string StrItemEnd { get; set; } = "";

    /// <summary>TAC DS:1eb9: the empty text the participant's message starts from.</summary>
    public string StrEvidenceEmpty { get; set; } = "";

    /// <summary>TAC DS:1eba: " has been identified as a participant in the plot." (with line ends), after the name.</summary>
    public string StrParticipant { get; set; } = "";

    /// <summary>TAC DS:1eee: "You find incriminating evidence which will turn " (with line ends), before the name.</summary>
    public string StrIncriminating { get; set; } = "";

    /// <summary>TAC DS:1f1f: "into a double agent!" (with line ends), after the name.</summary>
    public string StrIntoDoubleAgent { get; set; } = "";

    // ---- the use key and the camera ----

    /// <summary>TAC DS:1ba8 cga_map_game: the colour table handed to the driver (twice) when the room is shown again after a box.</summary>
    public byte[] CgaMapGame { get; } = new byte[16];

    /// <summary>TAC DS:bb66 faces_on: the pictures of faces are loaded, so a photo or a search can show what it teaches.</summary>
    public bool FacesOn { get; set; }

    /// <summary>TAC DS:1cc0: the empty text the status line starts from when the player uses what is in front.</summary>
    public string StrUseEmpty { get; set; } = "";

    /// <summary>TAC DS:1cc1: "Do you want to" and a line end, "arrest ", before the person's name.</summary>
    public string StrArrestQuestion { get; set; } = "";

    /// <summary>TAC DS:1cd8: "?" and the choices " Yes", " No", each on a line.</summary>
    public string StrArrestChoices { get; set; } = "";

    /// <summary>TAC DS:1ce4: " ... bug", after the name of a piece a bug can be planted on.</summary>
    public string StrCanBug { get; set; } = "";

    /// <summary>TAC DS:1ced: " ... photo", after the name of a piece worth a photo.</summary>
    public string StrCanPhoto { get; set; } = "";

    /// <summary>TAC DS:1cf8: " closed.", after the name of a piece the player closed.</summary>
    public string StrClosed { get; set; } = "";

    /// <summary>TAC DS:1d01: "Nothing to photograph."</summary>
    public string StrNothingToPhotograph { get; set; } = "";

    /// <summary>TAC DS:1d18: "Already photographed."</summary>
    public string StrAlreadyPhotographed { get; set; } = "";

    /// <summary>TAC DS:1d2e: " photographed.", after the piece's name.</summary>
    public string StrPhotographed { get; set; } = "";

    /// <summary>TAC DS:1d3d: "Open ", before the name of a piece that must be opened first.</summary>
    public string StrOpen { get; set; } = "";

    /// <summary>TAC DS:1d43: " first.", after it.</summary>
    public string StrFirst { get; set; } = "";

    /// <summary>TAC DS:1d4b: "Out of film."</summary>
    public string StrOutOfFilm { get; set; } = "";

    // ---- shots ----

    /// <summary>TAC DS:b90e player_state: what the player is doing: 0 standing, 1 walking, 2 at the arrest prompt, 4 lunging.</summary>
    public short PlayerState { get; set; }

    /// <summary>TAC DS:1c8c aim_chance_colour: the colour of the aim icon by the chance of a hit, 0..4 (black, grey, cyan, light grey, white).</summary>
    public byte[] AimChanceColour { get; } = new byte[5];

    // ---- the actors on the screen ----

    /// <summary>TAC DS:06d6: the port record of the page with the people's pictures (the player's portrait strip is cut from it).</summary>
    public RasterPort RpGuysX { get; } = new();

    /// <summary>TAC DS:06e8 RpGuys (MicroProse's name): the port of the people's pictures; in the running game <see cref="RpGuysX"/>.</summary>
    public RasterPort RpGuys { get; set; }

    /// <summary>TAC DS:42e6 actor_sprite_x: the screen x of each actor's picture this frame (player 0, guards 1..8); -1 when not drawn.</summary>
    public short[] ActorSpriteX { get; } = new short[Actors];

    /// <summary>TAC DS:42f8 actor_sprite_y: the screen y of each actor's picture this frame.</summary>
    public short[] ActorSpriteY { get; } = new short[Actors];

    /// <summary>TAC DS:42c2 actor_radar_x: the screen x of each actor's dot on the radar.</summary>
    public short[] ActorRadarX { get; } = new short[Actors];

    /// <summary>TAC DS:42d4 actor_radar_y: the screen y of each actor's dot on the radar.</summary>
    public short[] ActorRadarY { get; } = new short[Actors];

    /// <summary>TAC DS:1c8a muzzle_on: the flash of a shot is on the screen.</summary>
    public bool MuzzleOn { get; set; }

    /// <summary>TAC DS:4314 muzzle_actor: the actor whose shot flashes.</summary>
    public short MuzzleActor { get; set; }

    /// <summary>TAC DS:4316 muzzle_x: the screen x of the flash.</summary>
    public short MuzzleX { get; set; }

    /// <summary>TAC DS:4318 muzzle_y: the screen y of the flash.</summary>
    public short MuzzleY { get; set; }

    /// <summary>TAC DS:91ec lunge_timer: frames left of the player's lunge.</summary>
    public short LungeTimer { get; set; }

    /// <summary>TAC DS:96f2 sneak_steps: frames the player has moved sneaking (his picture alternates with the running one).</summary>
    public short SneakSteps { get; set; }

    /// <summary>TAC DS:bb56 player_moving: the player walks this frame.</summary>
    public bool PlayerMoving { get; set; }

    /// <summary>TAC DS:c998 building_color: the building's colour (palette index), of the guards' dots on the radar and the disguise.</summary>
    public short BuildingColor { get; set; }

    // ---- the player's turn ----

    /// <summary>TAC DS:1c44 wanted_facing: the facing the movement key asks for (1..8, clockwise from north); the player turns towards it.</summary>
    public short WantedFacing { get; set; }

    /// <summary>TAC DS:1bc8 game_speed: 0 slow, 1 medium, 2 fast (keys s, d, f).</summary>
    public short GameSpeed { get; set; }

    /// <summary>TAC DS:1c46 pause_text: "PAUSED" and a line end, the box of the key P.</summary>
    public TextBuffer PauseText { get; } = new(8);

    /// <summary>TAC DS:1c4e quit_text: "Are you sure you want to Quit?" with the choices No and Yes, the box of the key Q.</summary>
    public TextBuffer QuitText { get; } = new(41);

    /// <summary>TAC DS:1c78 joy_scancode: the scan code of the arrow key of each joystick direction 1..8 (entry 0 unused).</summary>
    public short[] JoyScancode { get; } = new short[9];

    /// <summary>TAC DS:4310 walk_hold: frames the player walks on after his key (4 for a new key, 0 for the same again).</summary>
    public short WalkHold { get; set; }

    /// <summary>TAC DS:4312 last_key: the key of the frame before; -1 for none.</summary>
    public short LastKey { get; set; }

    /// <summary>TAC DS:bb64 player_pose: the player's picture frame, 0..7 (0 standing, 1..3 walking, 4 looking, 6 arresting, 7 sneaking).</summary>
    public short PlayerPose { get; set; }

    /// <summary>TAC DS:430c disguise_shown: whether the status bar's colours show the disguise now.</summary>
    public bool DisguiseShown { get; set; }

    /// <summary>TAC DS:430e gas_shown: the gas bit of the room (0x10 or 0) the status bar's colours show now.</summary>
    public short GasShown { get; set; }

    /// <summary>TAC DS:bb6c siren_sounded: the siren has sounded at a high alert.</summary>
    public bool SirenSounded { get; set; }

    // ---- the room view ----

    /// <summary>TAC DS:b910 room_left: the building's x in pixels of the room's left edge (the walls the player walks into).</summary>
    public short RoomLeft { get; set; }

    /// <summary>TAC DS:b916 room_top: the building's y in pixels of the room's top edge.</summary>
    public short RoomTop { get; set; }

    /// <summary>TAC DS:9b6a room_right: the building's x in pixels of the room's right edge.</summary>
    public short RoomRight { get; set; }

    /// <summary>TAC DS:9b74 room_bottom: the building's y in pixels of the room's bottom edge.</summary>
    public short RoomBottom { get; set; }

    /// <summary>TAC DS:2000: "Street", the title of the room in the street raid (game mode 3).</summary>
    public string StrStreet { get; set; } = "";

    // ---- the file room's terminal ----

    /// <summary>TAC DS:1b98 cga_map_input_box: the colour table handed to the driver (twice) for the terminal's input box.</summary>
    public byte[] CgaMapInputBox { get; } = new byte[16];

    /// <summary>TAC DS:1f67: "Enter PassWord:", the terminal's prompt.</summary>
    public string StrEnterPassword { get; set; } = "";

    /// <summary>TAC DS:1f77: the empty text of the input box once the password was typed.</summary>
    public string StrPasswordEmpty { get; set; } = "";

    /// <summary>TAC DS:1f78: "Access CONFIRMED".</summary>
    public string StrAccessConfirmed { get; set; } = "";

    /// <summary>TAC DS:1f89: "Access DENIED".</summary>
    public string StrAccessDenied { get; set; } = "";

    /// <summary>TAC DS:1f97: "Search for:", the file room's prompt.</summary>
    public string StrSearchFor { get; set; } = "";

    /// <summary>TAC DS:1fa3: " has" and a line end, after the building's organisation.</summary>
    public string StrSearchHas { get; set; } = "";

    /// <summary>TAC DS:1fa9: "a", before the kind of building.</summary>
    public string StrSearchA { get; set; } = "";

    /// <summary>TAC DS:1fab: "no operatives".</summary>
    public string StrNoOperatives { get; set; } = "";

    /// <summary>TAC DS:1fb9: a line end and "in ", before the city searched for.</summary>
    public string StrSearchCityIn { get; set; } = "";

    /// <summary>TAC DS:1fbe: the line end after it.</summary>
    public string StrSearchCityEnd { get; set; } = "";

    /// <summary>TAC DS:1fc0: " has", a line end and "no operatives", after an organisation searched for.</summary>
    public string StrHasNoOperatives { get; set; } = "";

    /// <summary>TAC DS:1fd3: a line end and "in ", before this city.</summary>
    public string StrSearchOrgIn { get; set; } = "";

    /// <summary>TAC DS:1fd8: the line end after it.</summary>
    public string StrSearchOrgEnd { get; set; } = "";

    /// <summary>TAC DS:1fda: " now", a line end and "in ", between a plot object and its city.</summary>
    public string StrSearchNowIn { get; set; } = "";

    /// <summary>TAC DS:1fe3: "!" and a line end, after the city.</summary>
    public string StrSearchObjectEnd { get; set; } = "";

    /// <summary>TAC DS:1fe6: the empty text of the input box after a search found nothing.</summary>
    public string StrSearchEmpty { get; set; } = "";

    /// <summary>TAC DS:1fe7: "No Data".</summary>
    public string StrNoData { get; set; } = "";

    /// <summary>TAC DS:8dd2 PhotoVal (MicroProse's name): what the photos taken are worth (the photographed rooms' worth added up).</summary>
    public short PhotoVal { get; set; }
}

/// <summary>What a raid has achieved besides its worth (TAC DS:bb7a result_bits).</summary>
[Flags]
public enum RaidResults : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The hidden information about the target person was read (the object 0x3c in the building).</summary>
    InformationFound = 0x01,

    /// <summary>The person to arrest was arrested.</summary>
    Arrested = 0x02,

    /// <summary>Incriminating evidence was found that will turn the person into a double agent.</summary>
    Incriminated = 0x04,
}
