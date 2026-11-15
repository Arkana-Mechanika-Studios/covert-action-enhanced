using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>What the player does with the piece in front: use it (open, close, arrest, read) and photograph it.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>Tile types of the target's room: the hidden information about the person to arrest, and the person.</summary>
    private const int TileInformation = 0x3c, TilePrisoner = 0x3d;

    /// <summary>Tile types of locked pieces: the floor safe (0x1c) and the two kinds of locked cabinet (0x1d, 0x1f).</summary>
    private const int TileFloorSafe = 0x1c, TileLockedCabinet = 0x1d;

    /// <summary><see cref="RaidState.Equip"/>: the safecracking tools (bit 4).</summary>
    private const int SafecrackingTools = 0x10;

    /// <summary><see cref="RaidState.GameMode"/>: the raid to rescue a prisoner.</summary>
    private const int RescueMode = 2;

    /// <summary>The alert the person to arrest raises in the room when the player turns to him.</summary>
    private const int ArrestAlert = 0xf0;

    /// <summary>Sounds of opening and closing a door and another piece.</summary>
    private const int SoundOpenDoor = 0xd, SoundCloseDoor = 0xe, SoundOpenPiece = 0x10, SoundClosePiece = 0x11, SoundCamera = 9;

    /// <summary>The palettes of the room: normal, and the flash of the camera.</summary>
    private const int NormalPalette = 0, CameraFlashPalette = 1;

    /// <summary>What a photo of a piece teaches: intelligence of two or three times the room's worth, or the search of a safe.</summary>
    private const int SafeSearch = 4;

    /// <summary>
    /// The use key: looks at the piece in front. The hidden information about the person to arrest is read (once: the
    /// person's cast members become known and the piece goes); the person to arrest raises the alert and is asked to
    /// be arrested (not in the rescue raid). Then the status line names the piece and what can be done with it (bug,
    /// photo); a terminal shows a letter of the password the first time; a piece that opens is opened or closed (a
    /// locked one only with the safecracking tools).
    /// </summary>
    // orig: TAC 00e8:2d10 arrest_prompt (src/c/tac/tac_00e8_2d10.c) matched; tests: TacOracleTests
    public void ArrestPrompt()
    {
        GuardRules.FindFrontCell(state);
        var txt = state.MessageText;
        txt.Text = state.StrUseEmpty;
        var cell = RaidState.Cell(state.FrontCellX, state.FrontCellY);
        if (state.CellMap[cell] != RaidState.CellEmpty)
        {
            var tile = state.CellMap[cell] & RaidState.CellTile;
            if (tile == TileInformation)
            {
                if ((state.ResultBits & RaidResults.InformationFound) != 0)
                {
                    return;
                }
                state.ResultBits |= RaidResults.InformationFound;
                if (state.Who != -1)
                {
                    state.Ps[state.Who].Rknown |= state.Ps[state.Who].Role;
                }
                state.CellMap[cell] = RaidState.CellEmpty;
            }
            if (tile == TilePrisoner && !AskToArrest())
            {
                return;
            }
            UsePiece(cell, tile);
        }
        ShowStatusLine(txt.Text);
    }

    /// <summary>The person to arrest raises the alert and, unless arrested already, the player is asked (arrest_prompt).</summary>
    /// <returns>Whether the use goes on to the piece (the player chose to arrest).</returns>
    private bool AskToArrest()
    {
        if (state.GameMode == RescueMode)
        {
            return false;
        }
        GuardRules.RaiseAlert(state, state.InRoom, ArrestAlert);
        if ((state.ResultBits & RaidResults.Arrested) != 0)
        {
            return false;
        }
        SaveScreen();
        world.Input.SetNumLock(false);
        SetPalette(NormalPalette);
        if (state.Who != -1)
        {
            // the person's photo is known now (the low byte of the knowledge word)
            state.Ps[state.Who].Clues |= 1;
            Dossiers.ShowSuspectDossier(state.Who, 0, 0);
        }
        var txt = state.MessageText;
        txt.Text = state.StrArrestQuestion;
        AppendPersonName(state.Who);
        txt.Text += state.StrArrestChoices;
        var choice = ShowMessageBox(txt, 0x64, 0x8c);
        world.Input.SetNumLock(true);
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        RestoreFullScreen();
        if (choice != 0)
        {
            return false;
        }
        state.ResultBits |= RaidResults.Arrested;
        txt[0] = '\0';
        return true;
    }

    /// <summary>The status line names the piece and what can be done with it; a terminal, a door, a cupboard are used (arrest_prompt).</summary>
    private void UsePiece(int cell, int tile)
    {
        var txt = state.MessageText;
        var piece = state.TileType(tile);
        txt.Text = piece.PrintedName();
        // C: the flags are read for the cell as it is now (bare floor after the hidden information was taken)
        var now = TileBits(state.CellMap[cell] & RaidState.CellTile);
        if ((now & TileFlags.Buggable) != 0)
        {
            txt.Text += state.StrCanBug;
        }
        if ((now & TileFlags.Photo) != 0)
        {
            txt.Text += state.StrCanPhoto;
        }
        if ((now & TileFlags.Terminal) != 0 && (state.CellMap[cell] & RaidState.CellUsed) == 0)
        {
            state.CellMap[cell] |= RaidState.CellUsed;
            RevealPasswordLetter();
        }
        if ((piece.Bits & TileFlags.Openable) == 0)
        {
            return;
        }
        var locked = (tile & 0xfd) == TileLockedCabinet || tile == TileFloorSafe;
        if (locked && (state.Equip & SafecrackingTools) == 0)
        {
            return;
        }
        var isDoor = (piece.Bits & TileFlags.Door) != 0;
        if ((state.CellMap[cell] & RaidState.CellOpen) != 0)
        {
            txt.Text = piece.PrintedName();
            txt.Text += state.StrClosed;
            state.OpenDoorCount--;
            DoSound(isDoor ? SoundCloseDoor : SoundClosePiece);
        }
        else
        {
            state.OpenDoorCount++;
            DoSound(isDoor ? SoundOpenDoor : SoundOpenPiece);
        }
        state.CellMap[cell] ^= RaidState.CellOpen;
        RedrawCell(state.FrontCellX, state.FrontCellY);
    }

    /// <summary>
    /// The camera: photographs the piece in front if it is worth a photo, open (if it opens) and not yet photographed,
    /// and film is left. A photo uses a shot, adds the room's worth to <see cref="RaidState.PhotoVal"/> and flashes;
    /// then, with the faces loaded and outside practice, it teaches what the piece holds: intelligence about the
    /// building's organisation and city (twice or three times the room's worth) or, for the floor safe, its search.
    /// </summary>
    // orig: TAC 00e8:3081 take_photo (src/c/tac/tac_00e8_3081.c) matched; tests: TacOracleTests
    public void TakePhoto()
    {
        GuardRules.FindFrontCell(state);
        var cell = RaidState.Cell(state.FrontCellX, state.FrontCellY);
        int content = state.CellMap[cell];
        var tile = content & RaidState.CellTile;
        var shot = false;
        var txt = state.MessageText;
        txt.Text = state.StrNothingToPhotograph;
        if (content != RaidState.CellEmpty && (TileBits(tile) & TileFlags.Photo) != 0)
        {
            if ((content & RaidState.CellUsed) != 0)
            {
                txt.Text = state.StrAlreadyPhotographed;
            }
            else if ((content & RaidState.CellOpen) != 0 || (TileBits(tile) & TileFlags.Openable) == 0)
            {
                txt.Text = state.TileType(tile).PrintedName();
                txt.Text += state.StrPhotographed;
                state.CellMap[cell] |= RaidState.CellUsed;
                shot = true;
            }
            else
            {
                txt.Text = state.StrOpen;
                txt.Text += state.TileType(tile).PrintedName();
                txt.Text += state.StrFirst;
            }
        }
        if (state.PhotosTaken >= FilmLength)
        {
            shot = false;
            txt.Text = state.StrOutOfFilm;
        }
        ShowStatusLine(txt.Text);
        if (!shot)
        {
            return;
        }
        state.PhotosTaken++;
        var worth = state.Rms[state.Rm[state.InRoom].Type].Intel;
        state.PhotoVal = C16.Int(state.PhotoVal + worth);
        DrawFilmCounter();
        SetPalette(CameraFlashPalette);
        DoSound(SoundCamera);
        Output.WaitVsync(5);
        SetPalette(NormalPalette);
        // C: the original leaves its multiplier unset for a piece below type 0x18 (every piece worth a photo is above)
        int? lesson = tile >= 0x18 ? 2 : null;
        if (tile >= 0x1d)
        {
            lesson = 3;
        }
        if (tile >= 0x20)
        {
            lesson = 2;
        }
        if (tile == TileFloorSafe)
        {
            lesson = SafeSearch;
        }
        if (!state.FacesOn || (state.Status & Options.Practice) != 0)
        {
            return;
        }
        var multiplier = lesson ?? throw new UninitialisedReadException("take_photo's multiplier, a piece below type 0x18");
        var room = state.Rm[state.InRoom];
        // the balloon points at the photographed piece on the screen (cells of 16 pixels)
        state.BalloonX = C16.Int((state.FrontCellX - room.X << 4) + state.BaseX);
        state.BalloonY = C16.Int((state.FrontCellY - room.Y << 4) + state.BaseY);
        var level = state.Diff;
        state.Diff = state.CampaignDiff;
        SaveScreen();
        if (multiplier != SafeSearch)
        {
            GatherIntelligence(state.AtLoc, state.InCity, C16.Int(worth * multiplier), multiplier);
        }
        else
        {
            EvidenceFoundText();
        }
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        RestoreFullScreen();
        state.Rp1.BPen = 1;
        if (multiplier == SafeSearch)
        {
            DrawMinimap(RadarLeft, RadarTop);
            DrawStatusPanel();
        }
        state.Diff = level;
    }
}
