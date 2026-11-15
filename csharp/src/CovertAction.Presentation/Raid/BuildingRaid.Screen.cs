using System.Globalization;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The raid's small screen helpers: saving and restoring the screen, the status line and box, the film counter, and the keys.</summary>
public sealed partial class BuildingRaid
{
    private const int ScreenWidth = 320;
    private const int ScreenHeight = 200;

    /// <summary>A box's title strip, which the restore routine may put back as well: 160 x 30 pixels.</summary>

    private const int Blue = 1;
    private const int Green = 2;
    private const int Red = 0xc;
    private const int LightGreen = 0xa;
    private const int White = 0xf;

    /// <summary>Shots on a film.</summary>
    private const int FilmLength = 0x24;

    /// <summary>Saves the whole screen on its own page, before a box is drawn over the building.</summary>
    // orig: TAC 00e8:44a1 save_screen (src/c/tac/tac_00e8_44a1.c) matched; tests: TacOracleTests
    public void SaveScreen() =>
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.SavedScreen, 0, 0);

    /// <summary>Puts the screen saved by <see cref="SaveScreen"/> back.</summary>
    // orig: TAC 00e8:44c2 restore_full_screen (src/c/tac/tac_00e8_44c2.c) matched; tests: TacOracleTests
    public void RestoreFullScreen() =>
        world.Graphics.CopyRect(state.SavedScreen, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);

    /// <summary>A one-line message in the blue bar over the room view, centred on x = 100 (three pixels a character).</summary>
    /// <param name="text">The message.</param>
    // orig: TAC 00e8:347c show_status_line (src/c/tac/tac_00e8_347c.c) matched; tests: TacOracleTests
    public void ShowStatusLine(string text)
    {
        Output.FillRectCorners(0x1b, 0xa, 0xa8, 0x11, Blue);
        // C: -(strlen * 3 - 100) in 16 bits
        Output.DrawTextFlag0(text, C16.Int(-(text.Length * 3 - 0x64)), 0xa, White);
    }

    /// <summary>
    /// The small status box over the room view: the raid's clock, except on every other four frames, when it warns of
    /// gas in the player's room (in red) or, failing that, of the player's disguise (in green).
    /// </summary>
    /// <remarks>On the warning frames with no gas the clock shows even while the player is disguised.</remarks>
    // orig: TAC 00e8:3407 draw_status_clock (src/c/tac/tac_00e8_3407.c) matched; tests: TacOracleTests
    public void DrawStatusClock()
    {
        Output.FillRectCorners(0x7b, 2, 0xa8, 9, Blue);
        const int WarningFrames = 4;
        if ((state.Rtime & WarningFrames) != 0)
        {
            if ((state.Rm[state.InRoom].Status & RoomStatus.Gas) != 0)
            {
                Output.DrawTextFlag0(state.StrGas, 0x84, 2, Red);
                return;
            }
        }
        else if (state.Disguised)
        {
            Output.DrawTextFlag0(state.StrDisguise, 0x7c, 2, Green);
            return;
        }
        Output.DrawTextFlag0(LibraryTexts.FormatClockTime(state.ClockText, state.Gtime, state.Rtime), 0x7c, 2, White);
    }

    /// <summary>The shots of film left, in a small black box on the status panel.</summary>
    // orig: TAC 00e8:7e0c draw_film_counter (src/c/tac/tac_00e8_7e0c.c) matched; tests: TacOracleTests
    public void DrawFilmCounter()
    {
        const int Black = 0;
        world.Graphics.FillRect(state.Rp1, 0xde, 0x56, 0xf, 9, Black);
        // C: itoa of a 16-bit int, in base 10
        state.MessageText.Text = C16.Int(FilmLength - state.PhotosTaken).ToString(CultureInfo.InvariantCulture);
        Output.DrawTextFlag0(state.MessageText.Text, 0xe0, 0x57, LightGreen);
    }

    /// <summary>
    /// Reads a key and folds the keypad's variants of the cursor keys into one code each: the arrows, Home, End, PgUp and
    /// PgDn with their shifted and keypad forms. Any other key with a character is that character alone.
    /// </summary>
    /// <returns>The key code: the key's number in the high byte and its character in the low byte.</returns>
    // orig: TAC 00e8:c52a read_key (src/c/tac/tac_00e8_c52a.c) matched; tests: TacOracleTests
    // Other code under the same name: CODE 0000:2412 and its copies (LibraryInput.ReadKey; docs/source-map/shared-functions.csv).
    public int ReadKey()
    {
        // C: the key is a 16-bit int (the shifted right arrow is negative)
        var key = C16.Int(world.Input.ReadKeyRaw());
        switch (key)
        {
            case 0x487e:
                return 0x4800;          // up
            case -0xc00:
                return 0x4d00;          // right
            case 0x5060:
                return 0x5000;          // down
            case 0x4b7c:
                return 0x4b00;          // left
            case 0x475c:
                return 0x4700;          // home
            case 0x4d46:
                return 0x4d36;          // keypad 6
            case 0x4b43:
                return 0x4b34;          // keypad 4
            case 0x4800 or 0x4900 or 0x4d00 or 0x5100 or 0x5000 or 0x4f00 or 0x4b00 or 0x4700
                or 0x4838 or 0x4939 or 0x4d36 or 0x5133 or 0x5032 or 0x4f31 or 0x4b34 or 0x4737:
                return key;
            default:
                // a key with a character is the character alone
                return (key & 0xff) != 0 ? key & 0xff : key;
        }
    }
}
