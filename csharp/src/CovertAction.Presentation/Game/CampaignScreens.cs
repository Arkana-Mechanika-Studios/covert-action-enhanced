using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The campaign on the screen (the original's GAME program): the screens of the cities, buildings and headquarters,
/// the boxes and the bulletins. The rules of the campaign are in <c>CovertAction.Core.Campaign</c>; this class holds
/// the functions that draw and read the player's keys.
/// </summary>
/// <remarks>
/// Ported so far: clearing the screen, the place and clock plate, the activity report and the flight on the map.
/// The library's routines stand for GAME's copies of the title plate (<c>draw_title_plate</c> is the library's
/// <c>draw_title_bar</c>), the wait for a key (<c>wait_for_input</c>: <c>wait_for_key</c>) and the message box
/// (<c>popup_menu</c>: <c>quit_prompt</c>, whose hook redraws the place and clock plate here).
/// </remarks>
public sealed class CampaignScreens
{
    /// <summary>The screen: 320 by 200 pixels.</summary>
    private const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>Colours of the game's palette used here.</summary>
    private const int PanelColour = 5, Shadow = 8, Light = 7, Lettering = 0xa, White = 0xf;

    /// <summary>The row the place and clock plate goes to after its first drawing (the bottom of the screen).</summary>
    private const short StatusBarBottom = 0xae;

    private readonly CampaignState state;
    private readonly Boundaries world;
    private readonly OriginalBehaviour behaviour;

    /// <summary>The part on its variables and the outside world.</summary>
    /// <param name="state">The part's variables.</param>
    /// <param name="world">The drivers and the machine.</param>
    /// <param name="shared">The hand-off block (the message box's Alt-Q writes into it); a new one when left out.</param>
    /// <param name="behaviour">Which of the original's bugs to reproduce; the original's own when left out.</param>
    public CampaignScreens(CampaignState state, Boundaries world, SharedState? shared = null, OriginalBehaviour? behaviour = null)
    {
        this.state = state;
        this.world = world;
        Output = new LibraryOutput(state, world);
        Input = new LibraryInput(state, world);
        this.behaviour = behaviour ?? OriginalBehaviour.Faithful;
        // the campaign's rules in Core (the plot, the message texts) read the same switches from the campaign
        if (behaviour is not null)
        {
            state.Behaviour = behaviour;
        }
        Text = new LibraryText(state, state, world, Output, state.Random, this.behaviour);
        Screens = new LibraryScreens(state, state, world, Output, Input, Text);
        Pictures = new LibraryPictures(world);
        Clues = new ClueTexts(state, Text);
        DataFiles = new DataFileScreens(state, this);
        Messages = new CodedMessages(state, this, this.behaviour);
        ClueLists = new ClueScreens(state, world, this);
        Animation = new LibraryAnimation(state, state.Pan, world, Output, Input, Pictures, behaviour);
        // GAME's copy of the message box redraws the place and clock plate, runs the animation beside it and draws the face
        // behind it (draw_face, not the framed draw_portrait)
        // (GAME 00f7:c6ae popup_menu calls draw_face(picface, FaceX, FaceY): GAME 00f7:c6ae)
        Menus = new LibraryMenus(state, shared ?? new SharedState(), world, Output, Input, refreshAnimationArea: DrawPlaceClock,
            player: Animation, drawBackdropFace: Screens.DrawFace);
    }

    /// <summary>The output routines every part has, on this part's variables.</summary>
    public LibraryOutput Output { get; }

    /// <summary>The input routines every part has, on this part's variables.</summary>
    public LibraryInput Input { get; }

    /// <summary>The library's picture and archive files, as this part opens them.</summary>
    public LibraryPictures Pictures { get; }

    /// <summary>The animation player (the PAN scenes beside a box and of the headquarters).</summary>
    public LibraryAnimation Animation { get; }

    /// <summary>The text routines every part has.</summary>
    public LibraryText Text { get; }

    /// <summary>The clue sentences of CLUES.TXT.</summary>
    public ClueTexts Clues { get; }

    /// <summary>The documents of the data files.</summary>
    public DataFileScreens DataFiles { get; }

    /// <summary>The "Clues" pages of the data files.</summary>
    public ClueScreens ClueLists { get; }

    /// <summary>The texts of intercepted messages.</summary>
    public CodedMessages Messages { get; }

    /// <summary>The campaign's screens the parts share (frames, title bars, faces, the wait for a key).</summary>
    public LibraryScreens Screens { get; }

    /// <summary>The message box, disk prompt and animation routines every part has; GAME's box redraws the place and clock plate (<see cref="DrawPlaceClock"/>).</summary>
    public LibraryMenus Menus { get; }

    /// <summary>The library's screens about the persons and events of a case, on this part's tables and texts.</summary>
    public LibraryDossiers Dossiers => _dossiers ??= new LibraryDossiers(state, state, state, state.DossierTexts, Output, Text, Screens, state, state.Random);

    private LibraryDossiers? _dossiers;

    /// <summary>
    /// Shows the chronology of the case, or the news: the library's show_chronology with this part's clock text and
    /// its description of an event.
    /// </summary>
    /// <param name="chronology">True for the chronology, false for the news.</param>
    public void ShowChronology(bool chronology) =>
        Dossiers.ShowChronology(chronology, minutes => CampaignTexts.FormatGameTime(state, minutes), DescribeEvent);

    /// <summary>
    /// Gives the player a clue he has not had yet about a cast member, of a kind picked at random (this part's
    /// add_random_clue: the library's reveal_cast_clue, with this part's own "Clue" screen, <see cref="AddClue"/>).
    /// </summary>
    /// <param name="member">The cast member.</param>
    /// <param name="org">The organisation where the clue was found.</param>
    /// <param name="city">The city where it was found.</param>
    /// <returns>Whether a clue was shown (false when every kind was had).</returns>
    public bool AddRandomClue(int member, int org, int city) => Dossiers.RevealCastClue(member, org, city, AddClue);

    /// <summary>
    /// Draws the plate with the place and the clock: a bevelled box 160 pixels wide with "city, country" above and
    /// the campaign's time below, at <see cref="CampaignState.StatusBarY"/>; the next plate goes to the bottom of the
    /// screen.
    /// </summary>
    // orig: GAME 00f7:9680 draw_place_clock (src/c/game/game_00f7_9680.c) matched; tests: GameOracleTests
    public void DrawPlaceClock()
    {
        const int width = 0xa0, height = 0x1a, centre = 0x50, right = 0x9e, bottom = 0x18;
        var y = state.StatusBarY;
        world.Graphics.SetColorMaps(state.PlateColorMap, state.PlateColorMap);
        world.Graphics.FillRect(state.Rp1, 0, y, width, height, Shadow);
        Output.DrawLine(1, y + 1, 1, y + bottom, PanelColour);
        Output.DrawLine(2, y + 2, 2, y + bottom, Light);
        Output.DrawLine(2, y + 1, right, y + 1, Light);
        Output.DrawLine(right, y + 2, right, y + bottom, PanelColour);
        Output.DrawLine(2, y + bottom, right, y + bottom, PanelColour);
        // the field of the place
        world.Graphics.FillRect(state.Rp1, 5, y + 3, 0x96, 9, PanelColour);
        Output.DrawLine(3, y + 3, 5, y + 3, PanelColour);
        Output.DrawLine(6, y + 4, 6, y + 0xb, Light);
        Output.DrawLine(0x9c, y + 3, 0x9c, y + 0xb, PanelColour);
        // C: the original builds the text in a buffer on its stack (32 bytes); two names of at most 11 letters fit
        var city = state.Ct[state.InCity];
        Text.DrawTextCentered(city.Name + state.StrPlaceSeparator + city.Name2, centre, y + 4, Lettering);
        // the field of the clock
        world.Graphics.FillRect(state.Rp1, 0xf, y + 0xe, 0x82, 9, PanelColour);
        Output.DrawLine(0xd, y + 0xe, 0xf, y + 0xe, PanelColour);
        Output.DrawLine(0x10, y + 0xf, 0x10, y + 0x16, Light);
        Output.DrawLine(0x92, y + 0xe, 0x92, y + 0x16, PanelColour);
        Text.DrawTextCentered(CampaignTexts.FormatGameTime(state, state.Gtime), centre, y + 0xf, Lettering);
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
        state.StatusBarY = StatusBarBottom;
    }

    /// <summary>
    /// Puts a prose text of PROSE.DTA into <see cref="CampaignState.Txt"/> (it is appended to what the buffer holds):
    /// the scene of a capture, a surprise, the chief's advice ... with its placeholders filled in (a value of the
    /// caller's, the hero's name, the city the player is in, the organisation whose building he is at, his own agency),
    /// broken into lines for a box.
    /// </summary>
    /// <param name="key">The text's tag ("*inter1"). A tag that ends in '0' picks a variant: its last character is
    /// moved on by <paramref name="variant"/>, in the caller's buffer, which keeps it.</param>
    /// <param name="width">The width of a line of the box, in characters.</param>
    /// <param name="replacement">The value of "$RPLC" (most callers give none, and the original then reads whatever their stack holds).</param>
    /// <param name="variant">The variant of a tag that ends in '0'.</param>
    // orig: GAME 00f7:2eb9 load_prose_text (src/c/game/game_00f7_2eb9.c) matched; tests: GameOracleTests
    public void LoadProseText(TextBuffer key, int width, string replacement, int variant)
    {
        var last = key.Length - 1;
        var tag = key.Text;
        if (key[last] == '0')
        {
            // BUG: CA-41 the tag is the caller's text in the program's data; once moved on it no longer ends in '0',
            // and every later call reads the first call's variant until the program is loaded again
            // (OriginalBehaviour.FixCaptureCommentVariant: the variant goes into a copy of the tag)
            var moved = (byte)(key.Bytes[last] + variant);
            if (behaviour.FixCaptureCommentVariant)
            {
                tag = tag[..last] + (char)moved;
            }
            else
            {
                key.Bytes[last] = moved;
                tag = key.Text;
            }
        }
        // the reader fills in one placeholder of the message texts, $VICTIM; the prose texts have none
        Text.ReadClearedText(state.ProseFileName, tag, state.VariableNames, state.VariableValues, 1);
        CampaignTexts.ReplaceInTextBuffer(state, state.ProseReplacementToken, replacement);
        CampaignTexts.ReplaceInTextBuffer(state, state.ProseNameToken, state.HeroName);
        CampaignTexts.ReplaceInTextBuffer(state, state.ProseCityToken, state.Ct[state.InCity].Name);
        CampaignTexts.ReplaceInTextBuffer(state, state.ProseOrgToken, state.Orgs[state.AtLoc].Name);
        CampaignTexts.ReplaceInTextBuffer(state, state.ProseAgencyToken, state.Orgs[PlayerAgency].Name);
        LibraryTexts.WrapTextBuffer(state.Txt, width, state.LineEnd);
    }

    /// <summary>The player's agency among the organisations.</summary>
    private const int PlayerAgency = 1;

    /// <summary>Clears the whole screen to the panel colour, which becomes the screen port's colour.</summary>
    // orig: GAME 00f7:3cb4 clear_screen (src/c/game/game_00f7_3cb4.c) matched; tests: GameOracleTests
    public void ClearScreen()
    {
        // the original takes an argument and does not use it
        state.Rp1.BPen = PanelColour;
        world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, PanelColour);
    }

    /// <summary>
    /// Appends the text of an event of the plot to <see cref="CampaignState.Txt"/>: the placeholders get the case's
    /// victim and object and the actor's organisation and city, the event's message text is read from the text file,
    /// and the actor's organisation becomes known to the player.
    /// </summary>
    /// <param name="happening">The event (a negative number: a message shared by all cases).</param>
    // orig: GAME 00f7:801e describe_event (src/c/game/game_00f7_801e.c) matched; tests: GameOracleTests
    public void DescribeEvent(int happening)
    {
        const int victim = 0, actorOrg = 1, actorCity = 3, plotObject = 6;
        state.VariableValues[victim] = state.Pl.Object.Text;
        state.VariableValues[plotObject] = state.Pl.Object2.Text;
        var actor = state.Ps[state.Ac[state.Ev[happening].Act].Who];
        state.VariableValues[actorOrg] = state.Orgs[actor.Org].Name;
        state.VariableValues[actorCity] = state.Ct[actor.Loc].Name;
        LibraryTexts.LoadMessageText(state.MessageTag, state.MessageTemplate, state.TheCrime, happening >= 0 ? state.Ev[happening].Synch : happening);
        // BUG: CA-12 TEXT.DTA files CRIME1's bulletin "blueprints missing" under the tag of a message that a pair of events
        // also uses; the reader takes the first, the pair's. OriginalBehaviour.FixAnnouncedEventCancelledBySharedMessageId
        Text.ReadTextWithVars(state.TextFileName, state.MessageTag.Text, happening >= 0 ? LibraryTexts.EntriesBeforeEventText(state.Ev, state.NEVENT, happening, behaviour) : 0);
        state.Orgs[actor.Org].Clues |= 1;
    }

    /// <summary>
    /// The "Activity Reports" page of the data files: for every city a bar of its share of the clues that pointed to
    /// a city (<see cref="CampaignState.Active"/>), for every organisation but the first its share of the clues that
    /// named one (<see cref="CampaignState.OrgCounters"/>), each bar at most 160 pixels; then waits for a key.
    /// </summary>
    /// <exception cref="Msc.DivideErrorException">No clue has pointed to a city, or none named an organisation: the original stops with a divide error.</exception>
    // orig: GAME 00f7:6801 show_activity_report (src/c/game/game_00f7_6801.c) matched; tests: GameOracleTests
    public void ShowActivityReport()
    {
        const int frameColour = 1, longestBar = 0xa0, barHeight = 6, rowHeight = 8, firstRow = 0x14;
        const int cityX = 2, cityBarX = 0x52, orgX = 0xa2, orgBarX = 0xf2;
        const int cgaCityColour = 0xf, cityColour = 3, cityBarColour = 0xb, orgColour = 0xc, orgBarColour = 0xe;
        Output.DrawFramedBox(0, 0, ScreenWidth - 1, ScreenHeight - 1, frameColour);
        int cityClues = 0, orgClues = 0;
        for (var i = 0; i < CampaignState.WorldSize; i++)
        {
            cityClues += state.Active[i];
            orgClues += state.OrgCounters[i];
        }
        Output.DrawText(state.StrActivityReportTitle, 0x60, 6, White);
        var y = firstRow;
        for (var i = 0; i < CampaignState.WorldSize; i++)
        {
            Output.DrawText(state.Ct[i].Name, cityX, y, state.CGA ? cgaCityColour : cityColour);
            // C: 16-bit product and signed division
            world.Graphics.FillRect(state.Rp1, cityBarX, y + 1, Msc.C16.Div(Msc.C16.Int(longestBar * state.Active[i]), Msc.C16.Int(cityClues)), barHeight, cityBarColour);
            if (i != 0)
            {
                // the original draws the record's first text, the organisation's short name
                Output.DrawText(state.Orgs[i].Sname, orgX, y - rowHeight, orgColour);
                world.Graphics.FillRect(state.Rp1, orgBarX, y - 7, Msc.C16.Div(Msc.C16.Int(longestBar * state.OrgCounters[i]), Msc.C16.Int(orgClues)), barHeight, orgBarColour);
            }
            y += rowHeight;
        }
        Screens.WaitForKey();
    }

    /// <summary>
    /// The flight between two cities on the world map: a plane and its shadow move one pixel per frame from one city's
    /// place on the map to the other's, turning one eighth at a time towards the target, climbing by one pixel every
    /// second step (at most 8, and at most half the remaining distance), with the take-off sound and, when it starts
    /// to descend, the landing sound.
    /// </summary>
    /// <remarks>Random draws: one, a starting heading the function then replaces.</remarks>
    /// <param name="fromCity">The city of departure.</param>
    /// <param name="toCity">The destination.</param>
    /// <returns>The steps flown.</returns>
    // orig: GAME 00f7:3791 animate_travel (src/c/game/game_00f7_3791.c) matched; tests: GameOracleTests
    public int AnimateTravel(int fromCity, int toCity)
    {
        const int mapWidth = 0xc0, planeImages = 0x50, shadowImages = 0x58, highestFlight = 8, frameWait = 10;
        const int takeOffSound = 0x23, landingSound = 0x24, landing = -1;
        world.Graphics.CopyRect(state.Rp1, 0, 0, mapWidth, ScreenHeight, state.RpBack, 0, 0);
        int x = state.Ct[fromCity].GeoX, y = state.Ct[fromCity].GeoY;
        int targetX = state.Ct[toCity].GeoX, targetY = state.Ct[toCity].GeoY;
        // the original draws a starting heading and replaces it at once
        _ = GameLibrary.RandomBelow(state.Random, 8);
        int height = 0, steps = 0;
        int dx = targetX - x, dy = targetY - y;
        var heading = HeadingTowards(dx, dy);
        Output.DoSound(takeOffSound);
        var lastHeight = 0;
        do
        {
            var turn = HeadingTowards(dx, dy) - heading;
            if (turn != 0)
            {
                heading = ((turn & 7) < 4 ? heading + 1 : heading - 1) & 7;
            }
            // C: the arguments are evaluated right to left; only the step count changes
            var ceiling = (Math.Abs(dx) + Math.Abs(dy)) / 2;
            height = GameLibrary.Clamp((steps++ & 1) + height, 0, ceiling);
            if (height > highestFlight)
            {
                height = highestFlight;
            }
            if (height < lastHeight)
            {
                Output.DoSound(landingSound);
                lastHeight = landing;
            }
            if (lastHeight != landing)
            {
                lastHeight = height;
            }
            x += (sbyte)state.PlaneStepX[heading];
            y += (sbyte)state.PlaneStepY[heading];
            var left = GameLibrary.Clamp(x - 5, 0, 999);
            world.Graphics.CopyRect(state.RpBack, left, y - height - 6, 10, height + 12, state.Rp1, left, y - height - 6);
            Output.DrawImage(state.Sprite, heading + shadowImages, left + 1, y - 4);
            Output.DrawImage(state.Sprite, heading + planeImages, left + 1, y - height - 4);
            Output.WaitVsync(frameWait);
            dx = targetX - x;
            dy = targetY - y;
        }
        while (dx != 0 || dy != 0);
        return steps;
    }

    /// <summary>
    /// Marks a city on the world map: a dot of 3 by 3 pixels at its place and its name beside it, to the right, or to
    /// the left where the name would run past the map's right edge.
    /// </summary>
    /// <param name="city">The city.</param>
    /// <param name="color">The colour of the name (a palette index).</param>
    // orig: GAME 00f7:36d3 draw_map_city (src/c/game/game_00f7_36d3.c) matched; tests: GameOracleTests
    public void DrawMapCity(int city, int color)
    {
        const int dotSize = 3, dotColour = 0xb, mapRight = 0xbd, gap = 3;
        int x = state.Ct[city].GeoX, y = state.Ct[city].GeoY;
        // the original fills the dot twice, first in another colour
        world.Graphics.FillRect(state.Rp1, x - 1, y - 1, dotSize, dotSize, 3);
        var width = Text.TextWidth(state.Ct[city].Name);
        world.Graphics.FillRect(state.Rp1, x - 1, y - 1, dotSize, dotSize, dotColour);
        // C: an unsigned comparison: a name wider than the map's edge leaves a huge number and goes to the right
        if ((ushort)(mapRight - width) > x)
        {
            Output.DrawTextFlag0(state.Ct[city].Name, x + gap, y - gap, color);
        }
        else
        {
            Output.DrawTextFlag0(state.Ct[city].Name, x - width - gap, y - gap, color);
        }
    }

    /// <summary>
    /// Plays a scene (a .pan animation) on the right of the screen from a column on: when the player has animations
    /// on and the scenes' disk is in the drive, the screen is kept, the scene played, the pen colour and the game's
    /// palette put back and the picture page marked as holding no city; otherwise that part of the screen is only
    /// cleared. Either way the data disk is asked for again.
    /// </summary>
    /// <param name="name">The scene's file name.</param>
    /// <param name="x">The column the scene starts at.</param>
    /// <param name="y">The row the scene starts at.</param>
    // orig: GAME 00f7:023e play_pan_picture (src/c/game/game_00f7_023e.c) matched; tests: GameOracleTests
    // GAME's copy is other code than FINAL's play_pan_picture (FINAL 0100:0434)
    public void PlayPanPicture(string name, int x, int y)
    {
        const int scenesDisk = 4, justLook = 0, askUntilThere = 1, wholeScene = -1;
        if (!state.Status.HasFlag(Options.Animation) || Menus.CheckForDisk(scenesDisk, justLook) == 0)
        {
            world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
            world.Graphics.FillRect(state.Rp1, x, 0, ScreenWidth - x, ScreenHeight, PanelColour);
            Menus.CheckForDisk(state.Disk.DefaultDisk, askUntilThere);
            return;
        }
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        // C: the name is copied into a buffer of 14 bytes; a longer one would run into the next text
        state.SceneName.Text = name;
        var pen = state.Rp1.BPen;
        Animation.PlayPanFile(state.SceneName.Text, wholeScene, x, y);
        state.Rp1.BPen = pen;
        // the scene used the picture page
        state.CachedCity = -1;
        Menus.CheckForDisk(state.Disk.DefaultDisk, askUntilThere);
        world.Graphics.SetPalette(state.EgaPalette);
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
        world.Graphics.ShowPage(0);
    }

    /// <summary>Where the groups of images lie in <see cref="CampaignState.Sprite"/> (GAME DS:969c on, two bytes an entry).</summary>
    private const int FirstFaces = 0, SecondFaces = 40, Icons = 80, FaceStrips = 96, SecondSheetTags = 104, SecondSheetSingles = 108, FirstSheetTags = 112;

    /// <summary>Faces on a sheet, strips, tags and icons.</summary>
    private const int FacesPerSheet = 40, StripCount = 7, TagCount = 4, IconCount = 16;

    /// <summary>The page the picture sheets are loaded to and cut from.</summary>
    private const int SheetPage = 2;

    /// <summary>
    /// Loads the picture sheets of the campaign's images and cuts them: the faces of the two sheets, the strips and
    /// tags beside them, a few single images and the small icons; the portrait beside the next box is none.
    /// </summary>
    // orig: GAME 00f7:8136 init_sprite_sheets (src/c/game/game_00f7_8136.c) matched; tests: GameOracleTests
    public void InitSpriteSheets()
    {
        const int iconSize = 8, iconPitch = 9, iconsPerRow = 8, iconRow = 0xaf;
        // the original does not look at whether the driver can cut images
        _ = world.Graphics.BeginImageCuts();
        CutFaceSprites();
        for (var i = 0; i < IconCount; i++)
        {
            state.Sprite[Icons + i] = world.Graphics.CutImage(SheetPage, i % iconsPerRow * iconPitch, i / iconsPerRow * iconPitch + iconRow, iconSize, iconSize);
        }
        world.Graphics.EndImageCuts();
        state.Picface = -1;
    }

    /// <summary>
    /// Loads the two sheets of faces in turn and cuts from each its 40 faces (30 by 33 pixels, eight to a row) and four
    /// tags (44 by 18), and from the second also seven strips (32 by 26) and four single images.
    /// </summary>
    // orig: GAME 00f7:851f cut_face_sprites (src/c/game/game_00f7_851f.c) matched; tests: GameOracleTests
    public void CutFaceSprites()
    {
        Pictures.LoadPicture(SheetPage, state.FaceSheetName);
        CutFaces(FirstFaces);
        CutTags(FirstSheetTags);
        Pictures.LoadPicture(SheetPage, state.FaceSheet2Name);
        CutFaces(SecondFaces);
        for (var i = 0; i < StripCount; i++)
        {
            state.Sprite[FaceStrips + i] = world.Graphics.CutImage(SheetPage, (i << 5) + 0x60, 0xae, 0x20, 0x1a);
        }
        CutTags(SecondSheetTags);
        state.Sprite[SecondSheetSingles] = world.Graphics.CutImage(SheetPage, 0x49, 0xaf, 0x15, 0x16);
        state.Sprite[SecondSheetSingles + 1] = world.Graphics.CutImage(SheetPage, 0x133, 0, 0xb, 8);
        state.Sprite[SecondSheetSingles + 2] = world.Graphics.CutImage(SheetPage, 0x133, 9, 0xc, 8);
        state.Sprite[SecondSheetSingles + 3] = world.Graphics.CutImage(SheetPage, 0x101, 0xa0, 0x1a, 0xe);
    }

    /// <summary>Cuts the 40 faces of the sheet on the sheet page: cells 32 by 35 pixels, eight to a row, each face 30 by 33 inside its cell.</summary>
    private void CutFaces(int first)
    {
        const int faceWidth = 0x1e, faceHeight = 0x21, cellWidth = 0x20, cellHeight = 0x23, perRow = 8;
        for (var i = 0; i < FacesPerSheet; i++)
        {
            state.Sprite[first + i] = world.Graphics.CutImage(SheetPage, i % perRow * cellWidth + 1, i / perRow * cellHeight + 1, faceWidth, faceHeight);
        }
    }

    /// <summary>Cuts the four tags of the sheet on the sheet page: 44 by 18 pixels, one under the other from column 257.</summary>
    private void CutTags(int first)
    {
        for (var i = 0; i < TagCount; i++)
        {
            state.Sprite[first + i] = world.Graphics.CutImage(SheetPage, 0x101, 0x14 * i, 0x2c, 0x12);
        }
    }

    /// <summary>A function of the original with nothing in it (the search of a building calls it).</summary>
    // orig: GAME 00f7:03ea empty_stub (src/c/game/game_00f7_03ea.c) matched; tests: GameOracleTests
    public static void EmptyStub()
    {
    }

    /// <summary>The heading (0 north, clockwise to 7) for the signs of a way across and down, from the program's table.</summary>
    private int HeadingTowards(int dx, int dy) =>
        // the table is indexed by the signs -1..1 around its middle entry
        (sbyte)state.PlaneHeadings[4 + 3 * GameLibrary.Sign(dx) + GameLibrary.Sign(dy)];

    /// <summary>
    /// Records a new clue about a cast member and shows it on the "Clue" screen: where it was found, its sentence, the
    /// way it was found (from the sentence; a clue from another organisation than the agency's counts as the last
    /// search's), the photo or the way's picture, and under "Related Clues" the clues with the same item, then those
    /// with the same subject, each on a panel at a random column (with the face for a face), as far as the screen
    /// goes; then waits for a key. Nothing happens when the member's clue of this kind was had, or the table is full.
    /// </summary>
    /// <remarks>Random draws: one per related clue shown (its panel's column).</remarks>
    /// <param name="org">The organisation where the clue was found.</param>
    /// <param name="city">The city where it was found.</param>
    /// <param name="member">The cast member.</param>
    /// <param name="method">The kind of clue: one bit of <see cref="Core.Records.Activity.Clued"/> (above 0x1f: that bit shifted up by 5).</param>
    // orig: GAME 00f7:5035 add_clue (src/c/game/game_00f7_5035.c) matched; tests: GameOracleTests
    public void AddClue(int org, int city, int member, int method)
    {
        const int freeClue = -1, titleField = 9, faceClue = 1, castMemberSubject = 0x10, faceSubject = 1;
        const int wiretapHint = 1, agency = 1, portraitFrame = 0x6c, sourcePictures = 0x60, screenFull = 0xb4;
        if ((state.Ac[member].Clued & method) != 0)
        {
            return;
        }
        var slot = Array.FindIndex(state.Cl, clue => clue.Act == freeClue);
        if (slot == -1)
        {
            return;
        }
        var clue = state.Cl[slot];
        clue.Sorg = (sbyte)org;
        clue.Sloc = (sbyte)city;
        clue.Act = (short)member;
        if (method > 0x1f)
        {
            method >>= 5;
        }
        clue.Info1 = (short)ClueRules.MakeClueSubject(state, slot, method);
        clue.Key1 = (short)Clues.CastClueCode(member, false);
        state.Ac[member].Clued |= (short)method;
        Screens.DrawScreenFrame(state.StrClueTitle, titleField);
        state.Rp1.BPen = White;
        state.Txt.Text = state.StrClueSource;
        state.Txt.Text += state.Orgs[clue.Sorg].Name;
        state.Txt.Text += state.StrClueSlash;
        state.Txt.Text += state.Ct[clue.Sloc].Name;
        state.Txt.Text += state.StrClueSourceEnd;
        CampaignLibrary.ActivityClueText(state.Cl, slot, Clues.AppendClueText);
        if (clue.Sorg > agency)
        {
            state.CluePhraseIndex = (short)(state.LastHint == wiretapHint ? 1 : 0);
        }
        state.Txt.Text += state.StrClueMethod;
        state.Txt.Text += state.ClueSources[state.CluePhraseIndex];
        Text.DrawWrappedText(0x1f, 0x10, 0x11, PanelColour);
        if (method == faceClue)
        {
            Screens.DrawPortrait(state.Picface, 0x108, 0x12);
            Output.DrawImage(state.Sprite, portraitFrame, 0x12b, 0x2e);
        }
        else
        {
            Output.DrawImage(state.Sprite, state.CluePhraseIndex + sourcePictures, 0x110, 0x10);
        }
        Output.DrawTextFlag0(state.StrRelatedClues, 0x10, 0x42, PanelColour);
        int y = 0x4c, faceY = 0x49;
        var any = false;
        // pass 0: the same item; pass 1: the same subject
        for (var pass = 0; pass < 2; pass++)
        {
            for (var other = 0; other < CampaignState.ClueCount; other++)
            {
                var related = state.Cl[other];
                if (related.Act == freeClue || other == slot)
                {
                    continue;
                }
                if (y > screenFull)
                {
                    break;
                }
                var same = pass != 0 ? related.Info1 == clue.Info1 : related.Key1 == clue.Key1;
                if (!same)
                {
                    continue;
                }
                state.Txt.Text = "";
                var x = GameLibrary.RandomBelow(state.Random, 0xc) + 4;
                Screens.DrawPanel(x, y - 2);
                if (related.Info1 >> 8 == castMemberSubject)
                {
                    CampaignLibrary.ActivityClueText(state.Cl, other, Clues.AppendClueText);
                }
                else
                {
                    CampaignLibrary.AppendActivityText(state.Txt, state.Cl, state.ActivityPhrases, state.StrCluePeriod, other,
                        key => CampaignTexts.AppendClueLabel(state, key), (c, subject) => CampaignTexts.AppendClueSubject(state, c, subject));
                }
                state.Txt.Text += state.StrRelatedClueEnd;
                any = true;
                y = Text.DrawWrappedText(0x20, x + 8, y, 0) + 4;
                if (related.Info1 >> 8 == faceSubject)
                {
                    Screens.DrawPortrait(state.Ps[state.Ac[related.Act].Who].Face, 0x108, GameLibrary.Clamp(faceY, 0, 0x96));
                    Output.DrawImage(state.Sprite, portraitFrame, 0x12b, GameLibrary.Clamp(faceY + 0x18, 0, 0xb0));
                    faceY += 0x28;
                }
            }
        }
        if (!any)
        {
            Output.DrawTextFlag0(state.StrNoRelatedClues, 0x10, 0x48, PanelColour);
        }
        Screens.WaitForKey();
        state.Picface = -1;
    }
}
