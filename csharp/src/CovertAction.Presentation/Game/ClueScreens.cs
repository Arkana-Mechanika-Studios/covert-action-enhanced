using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>The "Clues" pages of the data files: the list of the cast members the player has clues about, and one member's clues.</summary>
/// <param name="state">The campaign.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="screens">The campaign's screens.</param>
public sealed class ClueScreens(CampaignState state, Boundaries world, CampaignScreens screens)
{
    /// <summary>Colours used here (palette indices).</summary>
    private const int Black = 0, BlueGrey = 5, Grey = 7, DarkGrey = 8, Red = 0xc, White = 0xf;

    /// <summary><see cref="Core.Records.Activity.Status"/>: the member already has a plate in the list being drawn.</summary>
    private const int Listed = 0x4000;

    /// <summary>
    /// The list of the cast members the player has clues about: a plate per member in two columns (the item of the
    /// first clue, and "ARRESTED" or "TURNED"), and a highlighted row the player moves with the cursor keys; Enter
    /// shows the member's clues and comes back to the list, Escape leaves.
    /// </summary>
    /// <param name="unused">The original's argument, which it does not use (its caller passes -1).</param>
    // orig: GAME 00f7:98c8 show_clues (src/c/game/game_00f7_98c8.c) matched; tests: GameOracleTests
    public void ShowClues(int unused)
    {
        _ = unused;
        while (true)
        {
            DrawClueList();
            if (!PickRow())
            {
                return;
            }
            ShowClueDetail(state.Mitem[state.ClueListSelection]);
        }
    }

    /// <summary>The plates of the list, one per cast member with a clue, in the order of their first clues.</summary>
    private void DrawClueList()
    {
        const int width = 0x140, rowStep = 6, cgaRow = 2;
        world.Graphics.FillRect(state.Rp1, 0, 0, width, 0x14, DarkGrey);
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            state.Ac[member].Status &= ~Listed;
        }
        state.Mindex = 0;
        var y = 6;
        for (var clue = 0; clue < CampaignState.ClueCount; clue++)
        {
            int member = state.Cl[clue].Act;
            if (member == -1 || (state.Ac[member].Status & Listed) != 0)
            {
                continue;
            }
            state.Ac[member].Status |= Listed;
            state.Mitem[state.Mindex++] = (short)member;
            var alert = state.Ps[state.Ac[member].Who].Alert;
            // C: the note is copied into a buffer of 10 bytes
            var note = alert == -1 ? state.StrCluesArrested : alert == -3 ? state.StrCluesTurned : "";
            state.Txt.Text = "";
            CampaignTexts.AppendClueLabel(state, state.Cl[clue].Key1);
            int colour = state.CGA ? cgaRow : state.RowColours[state.Mindex & 3];
            world.Graphics.FillRect(state.Rp1, 0, y + 8, width, 8, colour);
            // odd rows go to the right column
            var left = (state.Mindex & 1) != 0 ? 0xa0 : 0;
            screens.Screens.DrawTitleBar(left, y, colour);
            world.Graphics.FillRect(state.Rp1, left + 0xc, y + 1, 0x86, 9, Grey);
            screens.Text.DrawTextCentered(state.Txt.Text, left + 0x50, y + 2, Black);
            screens.Text.DrawTextCentered(note, left + 0x50, y + 6, Red);
            y += rowStep;
        }
        world.Graphics.FillRect(state.Rp1, 0, y + 0xa, width, 0xbe - y, DarkGrey);
        state.Txt.Text = state.StrCluesHeading;
        screens.Output.DrawTextFlag0(state.Txt.Text, 0x90, y + 0x10, White);
        screens.Output.DrawRectOutline(0x8e, y + 0xe, screens.Text.TextWidth(state.Txt.Text) + 4, 0xa, Grey);
        screens.Output.DrawRectOutline(0x10, y + 0xc, 0x120, 0x18, Black);
    }

    /// <summary>The player moves the highlighted row; true for Enter or Space, false for Escape.</summary>
    private bool PickRow()
    {
        const int down = 0x5000, up = 0x4800, enter = 0xd, space = 0x20, escape = 0x1b, noKey = -1;
        int answer = 0;
        screens.Input.BeginPointer();
        do
        {
            var row = ClueRowArea(state.ClueListSelection);
            world.Graphics.RecolorRect(state.Rp1, row.X, row.Y, row.Width, row.Height, Grey, White);
            screens.Output.WaitVsync(0x14);
            int key;
            var pointed = PointerEvent.Nothing;
            do
            {
                key = screens.Input.PollJoystickKey();
                // DIVERGENCE: D-632 with the mouse option on, the pointer on a row moves the highlight there; a click takes it, a right click is Escape.
                if (key == noKey)
                {
                    pointed = screens.Input.ReadPointer(ClueRowAt);
                }
            }
            while (key == noKey && pointed.Action == PointerAction.None && world.Input.NoKeyWaiting());
            if (pointed.Action == PointerAction.None && !world.Input.NoKeyWaiting())
            {
                key = screens.Input.ReadKey();
            }
            switch (pointed.Action)
            {
                case PointerAction.Point:
                    state.ClueListSelection = (short)pointed.Choice;
                    break;
                case PointerAction.Accept when pointed.Choice >= 0:
                    state.ClueListSelection = (short)pointed.Choice;
                    key = enter;
                    break;
                case PointerAction.Cancel:
                    key = escape;
                    break;
            }
            switch (key)
            {
                case down:
                    if (state.Mindex - 1 > state.ClueListSelection)
                    {
                        state.ClueListSelection++;
                    }
                    break;
                case up:
                    if (state.ClueListSelection > 0)
                    {
                        state.ClueListSelection--;
                    }
                    break;
                case enter:
                case space:
                    answer = 1;
                    break;
                case escape:
                    answer = -1;
                    break;
            }
            world.Graphics.RecolorRect(state.Rp1, row.X, row.Y, row.Width, row.Height, White, Grey);
        }
        while (answer == 0);
        screens.Input.FlushInput();
        return answer != -1;
    }

    /// <summary>Where a row of the clue list is highlighted: odd rows in the left column, even in the right, 6 pixels apart.</summary>
    private static HitArea ClueRowArea(int row) => new((row & 1) != 0 ? 0xc : 0xac, 6 * row + 7, 0x88, 9);

    /// <summary>The clue list's mouse hit test: the listed row whose highlight holds a pixel, -1 for none.</summary>
    private int ClueRowAt(int x, int y)
    {
        var rows = new HitArea[state.Mindex];
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = ClueRowArea(row);
        }
        return PointerHit.Area(x, y, rows);
    }

    /// <summary>
    /// A cast member's clues: for each, where it was found and its sentence, on panels at random columns; then the
    /// photo of each face clue (at most three or four), with "ARRESTED", "IN HIDING" or "TURNED"; then waits for a key.
    /// </summary>
    /// <remarks>Random draws: one per clue of the member (its panel's column).</remarks>
    /// <param name="member">The cast member.</param>
    // orig: GAME 00f7:9c61 show_clue_detail (src/c/game/game_00f7_9c61.c) matched; tests: GameOracleTests
    public void ShowClueDetail(int member)
    {
        const int titleField = 0xe, top = 0x10, faceSubject = 1, portraitFrame = 0x6c, lastPhoto = 0x90;
        screens.Screens.DrawScreenFrame(state.StrClueDetailTitle, titleField);
        var y = top;
        for (var clue = 0; clue < CampaignState.ClueCount; clue++)
        {
            var record = state.Cl[clue];
            if (record.Act != member)
            {
                continue;
            }
            var x = GameLibrary.RandomBelow(state.Random, 0xc) + 4;
            if (y != top)
            {
                screens.Screens.DrawPanel(x, y - 2);
            }
            state.Rp1.BPen = White;
            state.Txt.Text = state.StrClueDetailSource;
            state.Txt.Text += state.Orgs[record.Sorg].Name;
            state.Txt.Text += state.StrClueDetailSlash;
            state.Txt.Text += state.Ct[record.Sloc].Name;
            state.Txt.Text += state.StrClueDetailSourceEnd;
            CampaignLibrary.ActivityClueText(state.Cl, clue, screens.Clues.AppendClueText);
            state.Txt.Text += state.StrClueDetailEnd;
            y = screens.Text.DrawWrappedText(0x20, x + 8, y, BlueGrey) + 4;
        }
        y = top;
        for (var clue = 0; clue < CampaignState.ClueCount; clue++)
        {
            var record = state.Cl[clue];
            if (record.Act != member || record.Info1 >> 8 != faceSubject)
            {
                continue;
            }
            state.Txt.Text = "";
            // the sentence is built for the face it sets (picface); its text is not shown
            CampaignLibrary.AppendActivityText(state.Txt, state.Cl, state.ActivityPhrases, state.StrCluePeriod, clue,
                key => CampaignTexts.AppendClueLabel(state, key), (c, subject) => CampaignTexts.AppendClueSubject(state, c, subject));
            screens.Screens.DrawPortrait(state.Picface, 0x108, 0x10);
            screens.Output.DrawImage(state.Sprite, portraitFrame, 0x12b, 0x28);
            var alert = state.Ps[state.Ac[record.Act].Who].Alert;
            if (alert == -1)
            {
                screens.Output.DrawTextFlag0(state.StrClueDetailArrested, 0x102, 0x34, Red);
            }
            if (alert == -2)
            {
                screens.Output.DrawTextFlag0(state.StrClueDetailInHiding, 0x102, 0x34, Red);
            }
            if (alert == -3)
            {
                screens.Output.DrawTextFlag0(state.StrClueDetailTurned, 0x102, 0x34, Red);
            }
            y += 0x40;
            if (y > lastPhoto)
            {
                break;
            }
        }
        screens.Screens.WaitForKey();
    }
}
