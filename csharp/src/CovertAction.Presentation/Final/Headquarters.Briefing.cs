using CovertAction.Core.Final;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Final;

/// <summary>The briefing of a new case: the first pages, the copy-protection question and the MasterMinds' faces.</summary>
public sealed partial class Headquarters
{
    /// <summary>The CGA screen's graphics mode letter, where a face's backdrop keeps the driver's colour.</summary>
    private const sbyte CgaMode = (sbyte)'C';

    /// <summary>The faces screen: five columns of 64 pixels and three rows of 60, fifteen cells.</summary>
    private const int FaceColumns = 5, FaceRows = 3, FaceCells = FaceColumns * FaceRows, FaceCellWidth = 64, FaceCellHeight = 60;

    /// <summary>The faces start 24 pixels down, below the question.</summary>
    private const int FacesTop = 0x18;

    /// <summary>The colour of the whole screen behind the menus' boxes (black in their palette).</summary>
    private const int ScreenBlack = 5;

    /// <summary>The screen's size in pixels.</summary>
    private const int ScreenWidth = 320, ScreenHeight = 200;

    /// <summary>The frame around the chosen face: white, and the colour it turns back to when the frame moves on.</summary>
    private const int FrameWhite = 0xd, FrameOff = 0;

    /// <summary>The frame on the four-colour screen: white, and black where it was.</summary>
    private const int CgaFrameWhite = 0xf, CgaFrameOff = 0;

    /// <summary>Frames the cursor frame is shown for between two looks at the keys.</summary>
    private const int FaceCursorFrames = 0xf;

    /// <summary>The briefing's boxes stand below the animation, eight pixels from the left, and are wrapped at 40 characters.</summary>
    private const int BriefingBoxX = 8, BriefingBoxY = 0x95, BriefingColumns = 0x28;

    /// <summary>The animation's program variable that says which scene of the briefing to play: the agent refused, or the case begins.</summary>
    private const int BriefingSceneVariable = 0, SceneRefused = 4, SceneCaseBegins = 1;

    /// <summary>
    /// Chooses the next case and shows its first briefing: a plot other than the last one (plot 0 for the first case at
    /// level 0), then a world and a MasterMind (an organisation from 4 on and a city) that is not yet arrested and that
    /// the plot allows, drawn again until one fits (a new world every eight tries). Then the plot's introduction is shown
    /// page by page beside the briefing's animation.
    /// </summary>
    // orig: FINAL 0100:0734 start_case_briefing (src/c/final/final_0100_0734.c) matched; tests: FinalOracleTests
    public void StartCaseBriefing()
    {
        const int warmUpDraws = 0x10, maxTries = 0x3e7, triesPerWorld = 8, noMastermind = -1;
        const int plotDigits = 3, pageDigit = 6;
        _world.Graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBlack);
        _state.MenuSelection = 0;
        // the original throws sixteen numbers of the generator away first
        for (var i = 0; i < warmUpDraws; i++)
        {
            CaseSetup.Random(_state, 0xfff);
        }
        var career = _state.Pl;
        if (_state.Diff != 0 || career.Ncase != 0)
        {
            var last = career.Type;
            do
            {
                career.Type = (short)(CaseSetup.Random(_state, 0xf) + 1);
            }
            while (last == career.Type);
        }
        else
        {
            career.Type = 0;
        }
        while (true)
        {
            var tries = 0;
            bool unfit;
            do
            {
                if ((tries & (triesPerWorld - 1)) == 0)
                {
                    career.World = (short)CaseSetup.Random(_state, 3);
                    LoadWorldFile(career.World);
                }
                career.Clues = 0;
                career.Done = 0;
                career.BigOrg = (short)(CaseSetup.Random(_state, 0xc) + 4);
                career.BigLoc = (short)CaseSetup.Random(_state, CampaignTables.WorldSize);
                career.Index = -1;
                tries++;
                // an organisation's clue word names its MasterMind in the low byte; -1 for an organisation without one
                var clues = _state.Org[career.BigOrg].Clues;
                unfit = tries < maxTries && AlreadyArrested(career.Bigs, (byte)clues) || clues == noMastermind;
            }
            while (unfit);
            var mastermind = (byte)_state.Org[career.BigOrg].Clues;
            career.BigFace = CaseSetup.PackMastermindLook(_state, mastermind);
            career.BigIndex = mastermind;
            if ((_state.Org[career.BigOrg].Clues & _state.Plots[career.Type].OrgMask) != 0)
            {
                break;
            }
        }
        var tag = _state.PlotTag;
        tag.Text = _state.StrPlotBriefingTag;
        tag[plotDigits] = Digit(career.Type / 10);
        tag[plotDigits + 1] = Digit(career.Type % 10);
        Animation.SetScriptVar(BriefingSceneVariable, SceneRefused);
        _world.Graphics.SetColorMaps(_state.BriefingColorMap, _state.BriefingColorMap);
        Menus.StartAnimation(_state.CaseBriefingPanFile, 0, 0);
        bool found;
        do
        {
            _state.Txt.Text = "";
            found = Text.ReadTextWithVars(_state.PlotTextFile, tag.Text);
            if (found)
            {
                ShowBriefingBox();
                ClearTextBox();
            }
            tag[pageDigit]++;
        }
        while (found);
        _state.BriefingShown = true;
    }

    /// <summary>
    /// Chooses the variant of the plot the case is (its crime file and its two objects): a random one of the first six
    /// that has a crime file, was not played yet and is not the last one played, the last of a plot's variants only once
    /// all before it were played. On the first case's briefing the variant's text is shown page by page. When no variant
    /// fits in 999 tries, a new case is chosen (<see cref="StartCaseBriefing"/>) and the choice made again. Then the
    /// campaign's counters start afresh.
    /// </summary>
    // orig: FINAL 0100:0906 apartment_scene (src/c/final/final_0100_0906.c) matched; tests: FinalOracleTests
    public void ApartmentScene()
    {
        const int variantsDrawn = 6, maxTries = 999, repeatTries = 99, noCrimeFile = -1, apartmentVariant = 7;
        const int plotDigits = 3, variantDigit = 5, pageDigit = 6;
        var career = _state.Pl;
        // C: the plot is taken once: after a new case is chosen below, the variants are still looked for in the old plot
        var plot = _state.Plots[career.Type];
        int variant;
        while (true)
        {
            var tries = 0;
            bool fits;
            do
            {
                variant = CaseSetup.Random(_state, variantsDrawn);
                fits = (career.Done & 1 << variant) == 0 && plot.CrimeFile[variant] != noCrimeFile;
                if (plot.CrimeFile[variant + 1] == noCrimeFile)
                {
                    // the last variant of the plot: only once every one before it was played
                    var before = 0xff >> (8 - variant);
                    if ((before & career.Done) != before)
                    {
                        fits = false;
                    }
                }
                if (variant == career.Index && ++tries < repeatTries)
                {
                    fits = false;
                }
            }
            while (!fits && ++tries < maxTries);
            if (tries < maxTries)
            {
                break;
            }
            StartCaseBriefing();
        }
        career.Index = (short)variant;
        career.Crime = plot.CrimeFile[variant];
        if (variant < apartmentVariant)
        {
            career.Object.Text = plot.ItemA[variant];
            career.Object2.Text = plot.ItemB[variant];
        }
        else
        {
            // DORMANT: variant 7 is never drawn (random(6) gives 0..5; CA-9)
            career.Object.Text = _state.ApartmentItems[0];
            career.Object2.Text = _state.ApartmentItems[1];
        }
        var tag = _state.PlotTag;
        // C: the int OR of the variant's bit (kept only for variants 0 and 1) and the 16-bit mask of variants played
        var firstTwoOpen = ((1 << variant & 3) | (int)career.Done) == 3;
        tag.Text = _state.PlotVariantTags[firstTwoOpen ? 0 : 1];
        tag[plotDigits] = Digit(career.Type / 10);
        tag[plotDigits + 1] = Digit(career.Type % 10);
        tag[variantDigit] = Digit(variant);
        if (!_state.BriefingShown || variant == apartmentVariant)
        {
            Animation.SetScriptVar(BriefingSceneVariable, SceneRefused);
            _world.Graphics.SetColorMaps(_state.BriefingColorMap, _state.BriefingColorMap);
            Menus.StartAnimation(_state.ApartmentPanFile, 0, 0);
            bool found;
            do
            {
                _state.Txt.Text = "";
                found = Text.ReadTextWithVars(_state.ApartmentTextFile, tag.Text);
                if (found)
                {
                    ShowBriefingBox();
                    ClearTextBox();
                }
                tag[pageDigit]++;
            }
            while (found);
        }
        _state.NextSyn = _state.Gtime = _state.ChronTime = _state.Nloc = _state.Sleepy = _state.VP = _state.InterPol = 0;
    }

    /// <summary>Whether MasterMind <paramref name="mastermind"/> is marked arrested in the career's mask.</summary>
    /// <remarks>C: the original shifts a long 1 left by the number (a byte, a loop of single shifts): from 32 on nothing is left.</remarks>
    private static bool AlreadyArrested(uint arrested, int mastermind) => mastermind < 32 && (arrested >> mastermind & 1) != 0;

    /// <summary>
    /// Sets up a new case and briefs the agent on it: the agent starts in city 1 at the CIA, unseen; the case's crime
    /// and world files are read and the case is laid out on the map (<see cref="CaseSetup.LayOutCase"/>); the briefing's
    /// first pages are shown; then the player is told of the buildings he knows at the start
    /// (<see cref="CaseSetup.LinkOrganisations"/>), and at the higher levels Red Herrings join the cast
    /// (<see cref="CaseSetup.AddRedHerrings"/>).
    /// </summary>
    // orig: FINAL 0100:3f4c setup_new_case (src/c/final/final_0100_3f4c.c) matched; tests: CaseSetupTests, FinalOracleTests
    public void SetupNewCase()
    {
        const short startCity = 1, cia = 1;
        _state.InCity = startCity;
        _state.AtLoc = cia;
        _state.Alerted = 0;
        // the career record holds the next case's crime file (crime_no) and world (region)
        _state.TheCrime = _state.Pl.Crime;
        var world = _state.Pl.World;
        LoadCrimeFile(_state.TheCrime);
        LoadWorldFile(world);
        CaseSetup.LayOutCase(_state);
        BriefingIntroText(world);
        CaseSetup.LinkOrganisations(_state);
        CaseSetup.AddRedHerrings(_state);
    }

    /// <summary>
    /// The briefing's first pages: where in the world the operation is prepared (below level 2, with an organisation
    /// of the plot that is known to be involved, which the player is told of), the mission (from level 1, with a warning
    /// of Double Agents), the copy-protection question unless the agent has answered it before, and the briefing's
    /// animation with what comes of the answer.
    /// </summary>
    /// <param name="worldNo">The world of the case: 0 Europe, 1 Africa and the Middle East, 2 Central America.</param>
    // orig: FINAL 0100:499e briefing_intro_text (src/c/final/final_0100_499e.c) matched; tests: FinalOracleTests
    public void BriefingIntroText(int worldNo)
    {
        var texts = _state.BriefingTexts;
        var page = texts[(int)BriefingText.Indications] + texts[(int)BriefingText.OperationSomewhere];
        // the three regions follow one another in the order of the worlds; another world names none
        if (worldNo is >= 0 and <= 2)
        {
            page += texts[(int)BriefingText.Europe + worldNo];
        }
        if (_state.Diff < 2)
        {
            var known = _state.Ps[_state.Ac[CaseSetup.Random(_state, _state.NACTIVITY)].Who].Org;
            page += texts[(int)BriefingText.The] + _state.Org[known].Name + texts[(int)BriefingText.KnownToBeInvolved];
            // the organisation's first clue bit: the player knows it is in the case
            _state.Org[known].Clues |= 1;
        }
        _state.Txt.Text = page;
        ShowBriefingBox();

        page = texts[(int)BriefingText.Mission] + texts[(int)BriefingText.CaptureParticipants];
        if (_state.Diff != 0)
        {
            page += texts[(int)BriefingText.DoubleAgents];
        }
        _state.Txt.Text = page;
        ClearTextBox();
        ShowBriefingBox();

        // the question is asked until the agent is cleared, and again on the first case of a career
        var cleared = _state.Pl.Ncase != 0 && _state.Status.HasFlag(Options.Cleared) || BriefingFacesText();
        if (!cleared)
        {
            Animation.SetScriptVar(BriefingSceneVariable, SceneRefused);
            page = texts[(int)BriefingText.NotUpToSpeed] + texts[(int)BriefingText.PracticeFirst];
        }
        else
        {
            Animation.SetScriptVar(BriefingSceneVariable, SceneCaseBegins);
            page = _state.Pl.Ncase == 0 ? texts[(int)BriefingText.Cleared] : "";
            page += texts[(int)BriefingText.InfoPickedUp] + texts[(int)BriefingText.LinkedToOperation];
            _state.Status |= Options.Cleared;
        }
        _state.Txt.Text = page;
        _world.Graphics.SetColorMaps(_state.BriefingColorMap, _state.BriefingColorMap);
        Menus.StartAnimation(_state.BriefingPanFile, 0, 0);
        ShowBriefingBox();
    }

    /// <summary>Shows the text buffer, wrapped, in the box below the briefing's animation and waits for the player.</summary>
    /// <param name="columns">The width the text is wrapped at, in characters.</param>
    private void ShowBriefingBox(int columns = BriefingColumns)
    {
        LibraryTexts.WrapTextBuffer(_state.Txt, columns, _state.StrNewline);
        MenuChoice(_state.Txt, BriefingBoxX, BriefingBoxY);
    }

    /// <summary>
    /// The copy-protection question: fifteen different MasterMinds' faces, five by three, and "Max, I'm sure you
    /// recognize the head of the (organisation) among these faces." The player moves a frame with the cursor keys, the
    /// numeric pad or the joystick and picks a face with Return, Esc or Space.
    /// </summary>
    /// <returns>Whether the agent is cleared: always, in the shipped game (see the comment at the end).</returns>
    // orig: FINAL 0100:1394 briefing_faces_text (src/c/final/final_0100_1394.c) matched; tests: FinalOracleTests
    public bool BriefingFacesText()
    {
        var graphics = _world.Graphics;
        graphics.SetPalette(_state.EgaPaletteFaces);
        var shown = new bool[FinalState.MastermindCount];
        var asked = CaseSetup.Random(_state, FinalState.MastermindCount);
        var askedCell = CaseSetup.Random(_state, FaceCells);
        shown[asked] = true;
        graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBlack);
        _state.Txt.Text = _state.StrFacesQuestion[0] + _state.MastermindNames[asked] + _state.StrFacesQuestion[1];
        _state.MenuDrawOnly = true;
        MenuChoice(_state.Txt, 0x10, 1);

        for (var cell = 0; cell < FaceCells; cell++)
        {
            var face = asked;
            if (cell != askedCell)
            {
                do
                {
                    face = CaseSetup.Random(_state, FinalState.MastermindCount);
                }
                while (shown[face]);
                shown[face] = true;
            }
            DrawMastermindFace(face, FaceCellLeft(cell) + 4, FaceCellTop(cell) + FacesTop);
        }

        var column = 2;
        var row = 1;
        var chosen = FaceColumns * row + column;
        var picked = false;
        Input.BeginPointer();
        do
        {
            ShowFaceCursor(chosen, false);
            if (_world.Input.NoKeyWaiting())
            {
                Output.WaitVsync(FaceCursorFrames);
            }
            int dx = 0, dy = 0;
            var key = Input.ReadJoystickDir(ref dx, ref dy);
            if (_state.Status.HasFlag(Options.Joystick) && Input.JoystickButtonDown(0))
            {
                key = Keys.Return;
            }
            if (key == -1 && !_world.Input.NoKeyWaiting())
            {
                key = Input.ReadKey();
            }
            // DIVERGENCE: D-632 with the mouse option on, the pointer on a face moves the frame there; a click picks it (as Return; a right click as Esc, which picks too).
            var pointed = key == -1 ? Input.ReadPointer(FaceCellAt) : PointerEvent.Nothing;
            if (pointed.Choice >= 0)
            {
                (column, row) = (pointed.Choice % FaceColumns, pointed.Choice / FaceColumns);
                chosen = pointed.Choice;
            }
            if (pointed.Action == PointerAction.Cancel || (pointed.Action == PointerAction.Accept && pointed.Choice >= 0))
            {
                key = Keys.Return;
            }
            if (key != -1)
            {
                (column, row, picked) = MoveFaceCursor(key, column, row, picked);
                column = GameLibrary.Clamp(column, 0, FaceColumns - 1);
                row = GameLibrary.Clamp(row, 0, FaceRows - 1);
                chosen = FaceColumns * row + column;
            }
            ShowFaceCursor(chosen, true);
            Output.WaitVsync(FaceCursorFrames);
        }
        while (!picked);

        graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, ScreenBlack);
        graphics.SetPalette(_state.EgaPalette);
        // C: the source returns chosen == askedCell, but the shipped FINAL.EXE has the jump of that comparison replaced
        // by two NOPs (a patch in the shipped executable): every face is accepted.
        return true;
    }

    /// <summary>
    /// Where a key moves the frame of the faces screen: the joystick's directions 1 (up) to 8 clockwise, the cursor
    /// block's keys (Page Up, End ... for the diagonals); Return, Esc and Space pick the face.
    /// </summary>
    private static (int Column, int Row, bool Picked) MoveFaceCursor(int key, int column, int row, bool picked)
    {
        const int pageUp = 0x4900, right = 0x4d00, pageDown = 0x5100, end = 0x4f00, left = 0x4b00, home = 0x4700;
        switch (key)
        {
            case 2 or pageUp:
                return (column + 1, row - 1, picked);
            case 1 or Keys.Up:
                return (column, row - 1, picked);
            case 3 or right:
                return (column + 1, row, picked);
            case 4 or pageDown:
                return (column + 1, row + 1, picked);
            case 5 or Keys.Down:
                return (column, row + 1, picked);
            case 6 or end:
                return (column - 1, row + 1, picked);
            case 7 or left:
                return (column - 1, row, picked);
            case 8 or home:
                return (column - 1, row - 1, picked);
            case Keys.Return or Keys.Esc or Keys.Space:
                return (column, row, true);
            default:
                return (column, row, picked);
        }
    }

    /// <summary>Draws the frame around a face of the faces screen, or takes it away: an outline on the four-colour screen, else the frame's colour exchanged.</summary>
    private void ShowFaceCursor(int cell, bool on)
    {
        var frame = FaceFrame(cell);
        if (_state.CGA)
        {
            Output.DrawRectOutline(frame.X, frame.Y, frame.Width, frame.Height, on ? CgaFrameWhite : CgaFrameOff);
        }
        else
        {
            _world.Graphics.RecolorRect(_state.Rp1, frame.X, frame.Y, frame.Width, frame.Height, on ? FrameWhite : FrameOff, on ? FrameOff : FrameWhite);
        }
    }

    /// <summary>The frame around a face: an outline on the four-colour screen, the recoloured area on the others; what the mouse points at.</summary>
    private HitArea FaceFrame(int cell) => _state.CGA
        ? new HitArea(FaceCellLeft(cell) + 1, FaceCellTop(cell) + 0x15, 0x37, 0x39)
        : new HitArea(FaceCellLeft(cell) + 3, FaceCellTop(cell) + 0x17, 0x36, 0x38);

    /// <summary>The faces screen's mouse hit test: the cell whose frame holds a pixel, -1 for none.</summary>
    private int FaceCellAt(int x, int y) => PointerHit.Area(x, y, [.. Enumerable.Range(0, FaceCells).Select(FaceFrame)]);

    /// <summary>The left edge of a cell of the faces screen.</summary>
    private static int FaceCellLeft(int cell) => cell % FaceColumns * FaceCellWidth;

    /// <summary>The top edge of a cell of the faces screen, less the faces' top margin.</summary>
    private static int FaceCellTop(int cell) => cell / FaceColumns * FaceCellHeight;

    /// <summary>
    /// Shows the face and organisation of each of the 26 MasterMinds in turn on a black screen, waiting for a key after
    /// each. A test screen: nothing in the shipped game calls it.
    /// </summary>
    // orig: FINAL 0100:1322 show_mastermind_faces (src/c/final/final_0100_1322.c) matched; tests: FinalOracleTests
    public void ShowMastermindFaces()
    {
        const int faceX = 0x50, faceY = 0x18, nameY = 0x64, nameColour = 0xf;
        var graphics = _world.Graphics;
        graphics.SetPalette(_state.EgaPaletteFaces);
        for (var mastermind = 0; mastermind < FinalState.MastermindCount; mastermind++)
        {
            graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, 0);
            DrawMastermindFace(mastermind, faceX, faceY);
            Output.DrawTextFlag0(_state.MastermindNames[mastermind], faceX, nameY, nameColour);
            _world.Input.Getch();
        }
    }

    /// <summary>
    /// Draws the face of MasterMind <paramref name="mastermind"/> in a framed box of 54 x 56 pixels at
    /// (<paramref name="x"/>, <paramref name="y"/>): the frame, the backdrop (lighter for the darkest shade on a colour
    /// screen), the face from the MasterMind's look word and the clothes of his table.
    /// </summary>
    /// <param name="mastermind">The MasterMind, 0..25.</param>
    /// <param name="x">Left edge of the face's backdrop.</param>
    /// <param name="y">Top edge of the face's backdrop.</param>
    // orig: FINAL 0100:1214 draw_mastermind_face (src/c/final/final_0100_1214.c) matched; tests: FinalOracleTests
    public void DrawMastermindFace(int mastermind, int x, int y)
    {
        const int frame = 7, border = 0xd, inside = 5, backdrop = 3, lightBackdrop = 7, darkestShade = 3, clothesWord = 7;
        var look = CaseSetup.PackMastermindLook(_state, mastermind);
        var graphics = _world.Graphics;
        // faces are drawn with the photo's colour tables (on displays with fewer colours)
        graphics.SetColorMaps(_state.PhotoColorMap, _state.PhotoColorMap);
        graphics.FillRect(_state.Rp1, x - 2, y - 2, 0x36, 0x38, frame);
        graphics.FillRect(_state.Rp1, x - 1, y - 1, 0x34, 0x36, border);
        graphics.FillRect(_state.Rp1, x + 2, y + 2, 0x2e, 0x2d, inside);
        var shade = look >> 2 & 3;
        graphics.FillRect(_state.Rp1, x + 3, y + 3, 0x2c, 0x2b, shade == darkestShade && _state.GMode != CgaMode ? lightBackdrop : backdrop);
        Screens.DrawFace(look, x + 0xc, y + 3);
        var clothes = ((look & 1) != 0 ? MaleClothes : FemaleClothes) + _state.MastermindLooks[mastermind][clothesWord];
        Output.DrawImage(_state.SpriteTable, clothes, x + 3, y + 0x1c);
    }
}
