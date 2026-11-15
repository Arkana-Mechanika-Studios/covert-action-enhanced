using CovertAction.Core.Raid;

namespace CovertAction.Presentation.Raid;

/// <summary>The player's actions in the building.</summary>
public sealed partial class BuildingRaid
{
    private const int SoundBugPlanted = 0xa;

    /// <summary>The Kevlar vest: the player stands three wounds instead of one.</summary>
    private const int Vest = 0x40;

    /// <summary>
    /// The message at the end of the raid. Stopped inside the building: overcome by gas (knocked out), by wounds, or
    /// the prisoner released while the player was out of the room; the raid's worth is then -1. Outside: all enemies
    /// eliminated (a kill raid or an ambush past its first frames with no guard awake), or slipped out; the raid is worth
    /// its bugs. The box goes without waiting for a key, then the game waits two seconds.
    /// </summary>
    // orig: TAC 00e8:34e2 tac_end_message (src/c/tac/tac_00e8_34e2.c) matched; tests: TacOracleTests
    // BUG: CA-1 "all enemies eliminated" reads the count of awake guards; OriginalBehaviour.FixRaidGuardCountInCityCounter
    public void TacEndMessage()
    {
        if (state.InRoom != 0)
        {
            state.ResultValue = -1;
            const int GassedStun = 0x100;
            var woundLimit = (state.Equip & Vest) != 0 ? 3 : 1;
            state.MessageText.Text = state.Bg[0].Stun > GassedStun ? state.StrOvercomeByGas
                : woundLimit < state.Wounds ? state.StrOvercomeByWounds
                : state.StrPrisonerReleased;
        }
        else
        {
            state.ResultValue = state.BugVal;
            var allDown = state.Rtime > 0x40 && (state.GameMode == RaidState.OneRoomMode || state.GameMode == RaidState.KillMode)
                && GuardRules.AwakeGuardCount(state, behaviour) == 0;
            state.MessageText.Text = allDown ? state.StrEnemiesEliminated : state.StrSlipOut;
        }
        state.Menu.MenuPauseMode = true;
        ShowMessageBox(state.MessageText, 0x20, 0x64);
        Output.WaitVsync(0x78);
    }

    /// <summary>
    /// F3: plants a bug on the piece in front of the player, when he has a bug left, the piece takes one and the room has
    /// none yet. The bug is worth the room's worth; the status panel is drawn again and the status line says what was
    /// bugged, or why nothing was.
    /// </summary>
    // orig: TAC 00e8:32e6 plant_bug (src/c/tac/tac_00e8_32e6.c) matched; tests: TacOracleTests
    public void PlantBug()
    {
        GuardRules.FindFrontCell(state);
        var tile = state.CellMap[RaidState.Cell(state.FrontCellX, state.FrontCellY)];
        if (state.Bugs == 0)
        {
            return;
        }
        var room = state.Rm[state.InRoom];
        var alreadyBugged = (room.Status & RoomStatus.Bugged) != 0;
        if (tile != RaidState.CellEmpty && (state.TileType(tile & RaidState.CellTile).Bits & TileFlags.Buggable) != 0 && !alreadyBugged)
        {
            state.BugsPlanted++;
            DoSound(SoundBugPlanted);
            state.BugVal = Msc.C16.Int(state.BugVal + state.Rms[room.Type].Intel);
            room.Status |= RoomStatus.Bugged;
            state.Bugs--;
            RefreshStatusPanel();
            state.MessageText.Text = state.TileType(tile & RaidState.CellTile).PrintedName() + state.StrBugged;
        }
        else
        {
            state.MessageText.Text = alreadyBugged ? state.StrRoomAlreadyBugged : state.StrNothingToBug;
        }
        ShowStatusLine(state.MessageText.Text);
    }
}
