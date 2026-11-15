using CovertAction.Core.Crypto;
using CovertAction.Core.Menus;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The message box every part of the game has and what hangs on it: the box itself with its choices, the
/// insert-disk prompt, and the hooks of the picture animation that some parts play beside a box. The original's
/// programs each carry a copy; where the copies are the same instructions (docs/source-map/shared-functions.csv)
/// this one implementation stands for all of them, and its comment lists them. The animation hooks here are the
/// ones of the parts without the animation player (the cryptography part, the car chase, the electronics): five
/// empty functions. The parts with the player (the menus, the campaign) have their own, not ported yet.
/// </summary>
/// <param name="state">The part's variables.</param>
/// <param name="shared">The hand-off block (the box's Alt-Q writes the "leave the game" mark into it).</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="output">The part's drawing and sound routines.</param>
/// <param name="input">The part's key and joystick routines.</param>
/// <param name="doSound">
/// The part's own "play a sound when sound is on", where it is not the library's (the car chase has one with a second
/// value); null for <see cref="LibraryOutput.DoSound"/>.
/// </param>
/// <param name="readKey">
/// The part's own "wait for a key", where it is not the library's (the building raid has one of its own); null for
/// <see cref="LibraryInput.ReadKey"/>.
/// </param>
/// <param name="refreshAnimationArea">
/// What the part draws again after the box has cleared the area beside an animation (the campaign draws its place and
/// clock panel there, <c>draw_place_clock</c>); null for the empty <see cref="RefreshAnimationArea"/> of the parts
/// that have nothing to draw.
/// </param>
/// <param name="player">
/// The animation player of the parts that have one (the menus and the campaign): their copies of the box start, step
/// and stop the animation beside it where the other parts' copies call three empty functions; null for those.
/// </param>
/// <param name="drawBackdropFace">
/// The part's routine that draws the face behind a box (the menus' and the campaign's <c>draw_portrait</c>, with the
/// face word and its place); null for the empty <see cref="DrawBackdropFace"/> of the parts that show none.
/// </param>
public sealed class LibraryMenus(ILibraryState state, SharedState shared, Boundaries world, LibraryOutput output, LibraryInput input, Action<int>? doSound = null, Func<int>? readKey = null, Action? refreshAnimationArea = null,
    LibraryAnimation? player = null, Action<int, int, int>? drawBackdropFace = null)
{
    /// <summary>
    /// The box's request to the animation player: the record the box routines fill (<c>animation</c>; FINAL DS:5d76,
    /// GAME DS:574a), as the player takes it.
    /// </summary>
    private readonly Core.Animation.AnimationParams _request = new();

    /// <summary>
    /// What the part does after Alt-Q was confirmed in a box, before the program ends: the campaign's copy of the box
    /// saves the campaign and hands over (<c>save_and_hand_off</c>, which shows the farewell picture); null for the
    /// copies that call <c>GoodBye</c> (the cryptography part, the menus, the chase, the raid, the electronics).
    /// </summary>
    public Action? LeaveTheGame { get; set; }

    /// <summary>Starts the animation beside the box: the player with the box's request, or the empty hook.</summary>
    private void BeginBoxAnimation()
    {
        if (player is null)
        {
            AnimationBegin();
            return;
        }
        var animation = state.Animation;
        // the record's second and third word are never set by the box routines: 0, as the programs' data has them
        (_request.File, _request.U02, _request.Screen) = (animation.AnimationFile.Text, 0, 0);
        (_request.Speed, _request.X, _request.Y, _request.FrameLength) = (animation.AnimationSpeed, animation.AnimationX, animation.AnimationY, animation.AnimationFrameLength);
        player.StartAnimation(_request);
    }

    /// <summary>A step of the animation beside the box.</summary>
    /// <returns>Whether the animation has ended or was skipped; null from the empty hook, which answers nothing.</returns>
    private bool? StepBoxAnimation()
    {
        if (player is null)
        {
            AnimationStep();
            return null;
        }
        return player.AnimationStep() != 0;
    }

    /// <summary>Stops the animation beside the box.</summary>
    private void EndBoxAnimation()
    {
        if (player is null)
        {
            AnimationEnd();
        }
        else
        {
            player.StopAnimation();
        }
    }

    /// <summary>The cleared area is refreshed by the part's own routine: each program's copy of the box calls that program's.</summary>
    private void RefreshArea() => (refreshAnimationArea ?? RefreshAnimationArea)();

    /// <summary>The box's keys come through the part's own key routine: each program's copy of the box calls that program's read_key.</summary>
    private int ReadKey() => (readKey ?? input.ReadKey)();

    /// <summary>The box's sounds go through the part's own sound routine: each program's copy of the box calls that program's DoSound.</summary>
    private void DoSound(int effect) => (doSound ?? output.DoSound)(effect);

    private const int ScreenWidth = 320;
    private const int ScreenHeight = 200;

    /// <summary>The height of a line of box text.</summary>
    private const int LineHeight = 8;

    /// <summary>The lowest line a box may reach: lines that would start below it are left out.</summary>
    private const int BoxBottomLimit = 192;

    // palette indices
    private const int Black = 0;
    private const int BoxColor = 5;
    private const int ItemColor = 7;
    private const int DarkGrey = 8;
    private const int OkButtonColor = 0xb;
    private const int BarColor = 0xe;   // the highlight bar under the selected choice
    private const int White = 0xf;

    /// <summary>The disk check modes of <see cref="CheckForDisk"/>.</summary>
    private const int OnlyLook = 0, AskUntilThere = 1, AskOnceAnyway = -1;

    /// <summary>Empty in this build: once refreshed the screen after the animation area was cleared.</summary>
    // orig: CODE 0000:0cf4 refresh_animation_area (src/c/code/code_0000_0cf4.c) matched; also BUG 00e8:029a, CHASE 00df:04a2, TAC 00e8:070d; tests: CodeOracleTests
    public void RefreshAnimationArea()
    {
    }

    /// <summary>Empty in this build: once drew a face behind the box. The box still hands it the backdrop's three values.</summary>
    /// <param name="code">The face (<see cref="AnimationState.BackdropCode"/>).</param>
    /// <param name="argB">Second value.</param>
    /// <param name="argC">Third value.</param>
    // orig: CODE 0000:0cf5 draw_backdrop_face (src/c/code/code_0000_0cf5.c) matched; also CHASE 00df:04a3; tests: CodeOracleTests
    public void DrawBackdropFace(int code, int argB, int argC)
    {
    }

    /// <summary>Empty in this build: once loaded the animation.</summary>
    // orig: CODE 0000:27ec animation_begin (src/c/code/code_0000_27ec.c) matched; also BUG 00e8:6e7a, CHASE 00df:403a, TAC 00e8:d0ae; tests: CodeOracleTests
    public void AnimationBegin()
    {
    }

    /// <summary>
    /// Empty in this build: once played one step of the animation and said whether it had ended. The box still acts
    /// on its "answer" (see <see cref="WaitForInput"/>).
    /// </summary>
    // orig: CODE 0000:27ed animation_step (src/c/code/code_0000_27ed.c) matched; also BUG 00e8:6e7b, CHASE 00df:403b, TAC 00e8:d0af; tests: CodeOracleTests
    // Other code under the same name: FINAL 0100:add0 (not this method; docs/source-map/shared-functions.csv).
    public void AnimationStep()
    {
    }

    /// <summary>Empty in this build: once shut the animation down.</summary>
    // orig: CODE 0000:27ee animation_end (src/c/code/code_0000_27ee.c) matched; also BUG 00e8:6e7c, CHASE 00df:403c, TAC 00e8:d0b0; tests: CodeOracleTests
    public void AnimationEnd()
    {
    }

    /// <summary>
    /// Starts the picture animation of a file at a place on the screen, unless animations are switched off or the
    /// file's disk cannot be confirmed; then the screen is cleared instead. Nothing in this part calls it.
    /// </summary>
    /// <param name="name">The animation's file name.</param>
    /// <param name="x">Left edge of the animation.</param>
    /// <param name="y">Top edge of the animation.</param>
    /// <returns>Whether the animation was started.</returns>
    // orig: CODE 0000:22ff start_animation (src/c/code/code_0000_22ff.c) matched; also GAME 00f7:cfdf, TAC 00e8:c417, BUG 00e8:61e3, CHASE 00df:3b4d, FINAL 0100:98a5; tests: CodeOracleTests
    // Other code under the same name: FINAL 0100:ac84 (not this method; docs/source-map/shared-functions.csv).
    // DORMANT: no caller in CODE.
    public bool StartAnimation(string name, int x, int y)
    {
        var animation = state.Animation;
        // every animation but the briefing is on disk 4, which is looked for without asking
        const int animationDisk = 4;
        var available = state.Status.HasFlag(Options.Animation)
            && (name == animation.BriefingPanName || CheckForDisk(animationDisk, OnlyLook) != 0);
        if (!available)
        {
            world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, BoxColor);
            animation.AnimationX = (short)x;
            state.Rp1.BPen = BoxColor;
            return false;
        }
        // C: strcpy into the 14-byte buffer
        animation.AnimationFile.Text = name;
        // a building animation is told by the first three letters of its file name
        if ((name.Length > 3 ? name[..3] : name) == animation.BldPrefix)
        {
            animation.BldAnimationFlag = true;
        }
        animation.AnimationActive = true;
        animation.AnimationRecordNamed = true;
        animation.AnimationSpeed = -1;
        animation.AnimationX = (short)x;
        animation.AnimationY = (short)y;
        animation.AnimationFrameLength = -1;
        return true;
    }

    /// <summary>
    /// Copies the left part of the screen (as wide as the animation area) to the backup page; for a building
    /// animation also, once, a 160x30 corner.
    /// </summary>
    /// <param name="unusedArg">Not used; the callers pass the box position.</param>
    // orig: CODE 0000:23c0 save_screen_area (src/c/code/code_0000_23c0.c) matched; also FINAL 0100:9966, BUG 00e8:62a4, CHASE 00df:3c0e, GAME 00f7:d0a0, TAC 00e8:c4d8; tests: CodeOracleTests
    public void SaveScreenArea(int unusedArg)
    {
        world.Graphics.CopyRect(state.Rp1, 0, 0, state.Animation.SaveWidth, ScreenHeight, state.RpBack, 0, 0);
        if (state.Animation.BldAnimationFlag)
        {
            world.Graphics.CopyRect(state.Rp1, 0, 0, 160, 30, state.RpBack, 0, 0);
            state.Animation.BldAnimationFlag = false;
        }
    }

    /// <summary>Clears the animation area (the left part of the screen) to the box colour, which also becomes the background pen. Nothing in this part calls it.</summary>
    // orig: CODE 0000:2543 clear_animation_area (src/c/code/code_0000_2412.c) matched; also BUG 00e8:6427, CHASE 00df:3d91, FINAL 0100:9ae9, GAME 00f7:d223; tests: CodeOracleTests
    // DORMANT: no caller in CODE.
    public void ClearAnimationArea()
    {
        world.Graphics.FillRect(state.Rp1, 0, 0, state.Animation.SaveWidth, ScreenHeight, BoxColor);
        RefreshArea();
        state.Rp1.BPen = BoxColor;
    }

    /// <summary>
    /// Makes sure a disk of the game is in the drive, by looking for the file only that disk has.
    /// </summary>
    /// <param name="disk">The disk, 1 (A) to 4 (D).</param>
    /// <param name="mode">
    /// 0: only look; when the disk is missing, ask for the default disk instead and report failure.
    /// 1: ask "Please insert Disk X" until the disk is there. -1: ask once even if the disk is there.
    /// </param>
    /// <returns>For mode 0: 1 when the disk is there, 0 when not. Otherwise the mode (1).</returns>
    // orig: CODE 0000:26ba CheckForDisk (src/c/code/code_0000_26ba.c) matched; also FINAL 0100:aaa0, GAME 00f7:b27e, TAC 00e8:cf7c, BUG 00e8:6d48, CHASE 00df:3f08; tests: CodeOracleTests
    public int CheckForDisk(int disk, int mode)
    {
        var file = state.Disk.DiskFileNames[disk - 1];
        if (mode == OnlyLook)
        {
            if (world.Files.FindFileOnDisk(file, 0))
            {
                return 1;
            }
            CheckForDisk(state.Disk.DefaultDisk, AskUntilThere);
            return 0;
        }
        while (!world.Files.FindFileOnDisk(file, 0) || mode == AskOnceAnyway)
        {
            world.Graphics.ShowPage(state.Rp1.PageNr);
            if (mode == AskOnceAnyway)
            {
                mode = AskUntilThere;
            }
            world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, Black);
            // the prompt is built in the message buffer (the puzzle is over by now) and shown from there
            state.MessageText.Text = InsertDiskPrompt(disk);
            state.Menu.NoHi = true;
            QuitPrompt(state.MessageText, 100, 80);
            world.Graphics.FillRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, Black);
        }
        return mode;
    }

    /// <summary>
    /// The text asking for a disk: "Please insert Disk C", for disks C and D a second line naming the 3.5 inch disk
    /// with the same files (two letters earlier), then "Press Return to continue".
    /// </summary>
    private string InsertDiskPrompt(int disk)
    {
        var prompt = state.Disk;
        var letter = (char)('A' + disk - 1);
        var text = prompt.StrPleaseInsertDisk + letter;
        if (disk >= 3)
        {
            text += prompt.StrIf35InsertDisk + (char)(letter - 2) + prompt.StrCloseParen;
        }
        return text + prompt.StrPressReturn;
    }

    /// <summary>
    /// Draws the box of a message or menu. For a new box it measures the text, chooses the colours and draws the box
    /// with all its lines; later calls for the same box only redraw the lines next to the selection.
    /// </summary>
    /// <remarks>
    /// The text's lines end with a line feed; a line that starts with a blank is a choice. The box's look depends on
    /// <paramref name="y"/>: an even y gives a framed box; an odd y a plain filled area in the port's background
    /// colour (with black text when that is white), or, while an animation runs, the cleared strip beside it.
    /// </remarks>
    /// <param name="text">The box text.</param>
    /// <param name="x">Left edge of the box.</param>
    /// <param name="y">Top edge of the box; whether it is odd selects the look.</param>
    /// <param name="selected">The number of the selected choice (0 = first), -1 for none.</param>
    /// <returns><paramref name="selected"/>.</returns>
    // orig: CODE 0000:1e9d draw_menu_box (src/c/code/code_0000_1e9d.c) matched; also FINAL 0100:9443, GAME 00f7:cb7d, BUG 00e8:5d81, CHASE 00df:36eb, TAC 00e8:bfb5; tests: CodeOracleTests
    public int DrawMenuBox(string text, int x, int y, int selected)
    {
        var menu = state.Menu;
        menu.MenuLineStart[0] = 0;
        if (menu.MenuFirstDraw)
        {
            MeasureBox(text, x, y);
            DrawBoxBackground(x, y);
            if (menu.MenuOkButton)
            {
                output.DrawText(menu.StrOk, menu.MenuRight - 17, menu.MenuBottom - 8, OkButtonColor);
                output.DrawRectOutline(menu.MenuRight - 20, menu.MenuBottom - 10, 20, 10, OkButtonColor);
            }
        }
        DrawBoxLines(text, x, y, selected);
        return selected;
    }

    /// <summary>
    /// Goes through the box text once: notes where each line starts, which lines are choices and their hot keys,
    /// and how wide the widest line is; then sets the box's size from that.
    /// </summary>
    private void MeasureBox(string text, int x, int y)
    {
        var menu = state.Menu;
        Array.Clear(menu.MenuHotkeys);
        menu.MenuTextWidth = 0;
        menu.MenuLineCount = 0;
        short lineWidth = 0;
        var choices = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                menu.MenuTextWidth = Math.Max(menu.MenuTextWidth, lineWidth);
                lineWidth = 0;
                menu.MenuLineCount++;
                menu.MenuLineStart[menu.MenuLineCount] = (short)(i + 1);
                continue;
            }
            if (lineWidth == 0 && text[i] == ' ')
            {
                // a choice: its hot key is the character after the blank
                menu.MenuHotkeys[choices] = i + 1 < text.Length ? text[i + 1] : '\0';
                if (menu.MenuFirstItem == -1)
                {
                    menu.MenuFirstItem = menu.MenuLineCount;
                }
                choices++;
            }
            lineWidth += (short)world.Graphics.CharWidth(state.Rp1.FontNr, text[i]);
        }
        // a text that does not end with a line end has a last piece that is no line; it does not count as a choice
        if (!text.EndsWith('\n'))
        {
            choices--;
        }
        menu.MenuItemCount = (short)choices;
        menu.MenuLineCount = (short)GameLibrary.Clamp(menu.MenuLineCount, 0, (BoxBottomLimit - y) / LineHeight);
        menu.MenuRight = (short)(x + menu.MenuTextWidth + 8);
        menu.MenuBottom = (short)(y + menu.MenuLineCount * LineHeight + 6);
    }

    /// <summary>Sets the box's colours and draws what is behind the text: a framed box, a plain area, or the strip beside an animation.</summary>
    private void DrawBoxBackground(int x, int y)
    {
        var menu = state.Menu;
        var animation = state.Animation;
        var port = state.Rp1;
        menu.MenuTextColor = White;
        menu.MenuItemColor = ItemColor;
        menu.MenuSelectedColor = ItemColor;
        menu.MenuHiliteColor = BoxColor;

        var framed = (y & 1) == 0;
        if (framed)
        {
            port.BPen = BoxColor;
            output.DrawFramedBox(x, y, menu.MenuRight - x, menu.MenuBottom - y, BoxColor);
        }
        else if (animation.AnimationActive)
        {
            // the box shares the screen with an animation: clear the strip left of it, or the lines below it
            animation.SaveWidth = (short)GameLibrary.Clamp(animation.AnimationX, (short)(menu.MenuRight + 5), 999);
            port.BPen = BoxColor;
            if (y < 144)
            {
                world.Graphics.FillRect(port, 0, 0, animation.SaveWidth, ScreenHeight, BoxColor);
                RefreshArea();
            }
            else
            {
                world.Graphics.FillRect(port, 4, y, 312, 198 - y, BoxColor);
            }
        }
        else
        {
            world.Graphics.FillRect(port, x + 1, y + 1, menu.MenuRight - x, menu.MenuBottom - y, port.BPen);
            if (port.BPen == White)
            {
                // on a white area: black titles, a dark grey selection, and a white "bar"
                menu.MenuTextColor = Black;
                menu.MenuSelectedColor = DarkGrey;
                menu.MenuHiliteColor = White;
            }
        }
    }

    /// <summary>Draws the box's lines; after the first draw of a box only the selected line and its two neighbours.</summary>
    private void DrawBoxLines(string text, int x, int y, int selected)
    {
        var menu = state.Menu;
        state.Rp1.APen = menu.MenuTextColor;
        var choice = text.StartsWith(' ') ? 0 : -1; // the number of the choice the current line is; -1 for the title lines
        for (var line = 0; line < menu.MenuLineCount; line++)
        {
            var start = menu.MenuLineStart[line];
            var next = menu.MenuLineStart[line + 1];
            if (menu.MenuFirstDraw || Math.Abs(selected - choice) < 2)
            {
                var lineText = text[start..(next - 1)];
                if (choice >= 0 && IsMarked(choice))
                {
                    lineText = '^' + lineText[1..];
                }
                var color = choice < 0 ? menu.MenuTextColor : choice == selected ? menu.MenuSelectedColor : menu.MenuItemColor;
                output.DrawTextFlag0(lineText, x + 5, y + 5 + line * LineHeight, color);
            }
            if (next < text.Length && text[next] == ' ')
            {
                choice++;
            }
        }
    }

    /// <summary>Whether a choice is marked (drawn with '^' in place of its leading blank).</summary>
    // C: 1 << choice in 16 bits: choices 16 and up have no bit (C16.Shl shifts as the 8086 does).
    private bool IsMarked(int choice) => (state.Menu.MenuMarkedMask & C16.Shl(1, choice)) != 0;

    /// <summary>
    /// Shows a message box or menu and waits for the player: this part's one way of showing a text ("Are you sure
    /// you want to Quit?", "PAUSED", "MESSAGE DECODED", the insert-disk prompt).
    /// </summary>
    /// <remarks>
    /// Cursor keys or the joystick move the highlight over the choices; Return, Space or F1 take the choice; Esc
    /// cancels. Alt-A, Alt-S and Alt-J switch animations, sound and joystick, with a sound for on or off. Alt-Q asks
    /// "Are you sure you want to Quit?" in a box of its own and on "Yes" ends the program; on "No" it closes this box
    /// too, as Esc would. A pause box (<see cref="MenuState.MenuPauseMode"/>) is drawn and left on the screen without
    /// waiting.
    /// </remarks>
    /// <param name="text">The box text: lines ending with a line feed; lines starting with a blank are choices.</param>
    /// <param name="x">Left edge of the box.</param>
    /// <param name="y">Top edge of the box (see <see cref="DrawMenuBox"/> for what an odd y does).</param>
    /// <returns>The chosen choice (0 = first), or -1 for Esc, a pause box or a box without choices.</returns>
    // orig: CODE 0000:19ce quit_prompt (src/c/code/code_0000_19ce.c) matched; also FINAL 0100:8f74, TAC 00e8:bae6, BUG 00e8:58b2, CHASE 00df:321c, GAME 00f7:c6ae; tests: CodeOracleTests, CampaignTests
    public int QuitPrompt(string text, int x, int y) => ShowBox(() => text, x, y);

    /// <summary>
    /// The same box for a text that lies in a buffer. The box shows what the buffer holds when the box is drawn, not
    /// a copy taken at the call: preparing an animation for the box can open the insert-disk prompt first, which
    /// builds its own text in the same message buffer, and the box that was called for then shows that text. (The
    /// original passes the buffer's address.)
    /// </summary>
    /// <param name="text">The buffer with the box text.</param>
    /// <param name="x">Left edge of the box.</param>
    /// <param name="y">Top edge of the box.</param>
    /// <returns>As for a text.</returns>
    public int QuitPrompt(TextBuffer text, int x, int y) => ShowBox(() => text.Text, x, y);

    /// <summary>The box routine itself; the text is asked for at the moment it is drawn.</summary>
    private int ShowBox(Func<string> boxText, int x, int y)
    {
        var menu = state.Menu;
        var animation = state.Animation;
        int selected = menu.NoHi || menu.MenuPauseMode ? -1 : menu.MenuStartItem;
        var highlighted = -1; // the choice the highlight bar is on
        menu.MenuFirstItem = -1;
        menu.MenuFirstDraw = true;

        var animationPrepared = PrepareAnimation();
        animation.BackdropCode = -1;
        world.Graphics.SetColorMaps(state.ColorMapA, state.ColorMapB);
        DrawMenuBox(boxText(), x, y, selected);
        if (menu.MenuFirstItem != -1 && !menu.MenuPauseMode && !menu.NoHi)
        {
            RecolorChoice(x, y, menu.MenuStartItem, menu.MenuHiliteColor, BarColor);
            highlighted = menu.MenuStartItem;
        }
        if (animation.AnimationActive)
        {
            SaveScreenArea(x);
        }
        input.FlushInput();
        if (menu.MenuPauseMode)
        {
            menu.MenuPauseMode = false;
            return -1;
        }

        var closed = false;
        do
        {
            menu.MenuFirstDraw = false;
            var optionsBefore = state.Status;
            var joystickKey = WaitForInput();
            if (!world.Input.NoKeyWaiting() || joystickKey != -1)
            {
                var key = joystickKey == -1 ? ReadKey() : joystickKey;
                switch (key)
                {
                    case Keys.Down:
                        if (selected < menu.MenuItemCount - 1)
                        {
                            selected++;
                        }
                        break;
                    case Keys.Up:
                        if (selected > 0)
                        {
                            selected--;
                        }
                        break;
                    case Keys.AltA:
                        SwitchOption(Options.Animation, optionsBefore);
                        break;
                    case Keys.AltS:
                        SwitchOption(Options.Sound, optionsBefore);
                        break;
                    case Keys.AltJ:
                        SwitchOption(Options.Joystick, optionsBefore);
                        break;
                    case Keys.AltQ:
                        AskToQuit(x, y);
                        // "No": this box closes without a choice, as with Esc
                        selected = -1;
                        closed = true;
                        break;
                    case Keys.Esc:
                        selected = -1;
                        closed = true;
                        break;
                    case Keys.Return:
                    case Keys.Space:
                    case Keys.F1:
                        closed = true;
                        break;
                }
            }

            if (selected < 0 || selected >= menu.MenuItemCount || menu.MenuOkButton || menu.NoHi)
            {
                selected = -1;
            }
            if (selected != highlighted)
            {
                MoveHighlight(x, y, highlighted, selected);
                highlighted = selected;
                if (joystickKey != -1)
                {
                    // slow the joystick down to about five steps a second
                    output.WaitVsync(15);
                }
            }
            if (animation.AnimationActive)
            {
                SaveScreenArea(x);
            }
        }
        while (!closed);

        if (selected != -1)
        {
            // flash the chosen line white for a moment
            RecolorChoice(x, y, selected, BarColor, White);
            if (animation.AnimationActive)
            {
                SaveScreenArea(x);
            }
            output.WaitVsync(20);
        }
        if (animation.AnimationActive)
        {
            EndBoxAnimation();
            animation.AnimationActive = false;
        }
        if (animationPrepared)
        {
            state.Rp1.PageNr = 0;
            world.Graphics.ShowPage(0);
        }
        menu.MenuStartItem = 0;
        menu.MenuMarkedMask = 0;
        menu.NoHi = false;
        menu.MenuOkButton = false;
        return selected;
    }

    /// <summary>
    /// Before a box that appears beside a running animation: saves the screen, says "One moment please..." when the
    /// game runs from a floppy, sets the animation's palette and makes sure its disk is in the drive.
    /// </summary>
    /// <returns>Whether an animation was prepared (the screen page must then be shown again when the box closes).</returns>
    private bool PrepareAnimation()
    {
        var animation = state.Animation;
        var wanted = animation.AnimationActive && !state.Menu.MenuPauseMode
            && (state.Status.HasFlag(Options.Animation) || animation.BackdropCode != -1);
        if (!wanted)
        {
            return false;
        }
        world.Graphics.CopyRect(state.Rp1, 0, 0, ScreenWidth, ScreenHeight, state.RpBack, 0, 0);
        if (animation.CurrentDrive == -1)
        {
            animation.CurrentDrive = (short)world.Files.CurrentDrive();
        }
        // loading from a floppy (drive A or B) takes a while: say so when there is room beside the animation
        // C: this one function reads animation_x as an unsigned number
        const int lastFloppyDrive = 2;
        if (animation.CurrentDrive <= lastFloppyDrive && (ushort)animation.AnimationX <= 200)
        {
            output.DrawText(state.Menu.StrOneMoment, animation.AnimationX + 2, 1, ItemColor);
        }
        BeginBoxAnimation();
        animation.CachedCity = -1;
        world.Graphics.SetPalette(animation.EgaPaletteAnim);
        CheckForDisk(state.Disk.DefaultDisk, AskUntilThere);
        if (animation.BackdropCode != -1)
        {
            // bits 2 and 3 of the backdrop code both set: the face needs the other palette
            if ((animation.BackdropCode >> 2 & 3) == 3)
            {
                world.Graphics.SetPalette(animation.EgaPaletteAnimAlt);
            }
            state.Rp1.PageNr = 2;
            (drawBackdropFace ?? DrawBackdropFace)(animation.BackdropCode, animation.BackdropArgB, animation.BackdropArgC);
            state.Rp1.PageNr = 0;
        }
        StepBoxAnimation();
        StepBoxAnimation();
        return true;
    }

    /// <summary>
    /// Waits until a key is in the keyboard buffer or the joystick stands for one.
    /// </summary>
    /// <returns>The key the joystick stands for, or -1 when the wait ended with a key in the keyboard buffer.</returns>
    private int WaitForInput()
    {
        var joystickKey = -1;
        while (world.Input.NoKeyWaiting() && joystickKey == -1)
        {
            if (state.Animation.AnimationActive)
            {
                // C: the original steps the animation here and stops it when the step says it has ended. In this
                // build the step is an empty function, and the "answer" tested is what the processor's AX register
                // still holds: the non-zero answer of the keyboard check just above. So a running animation is
                // switched off in the first idle pass.
                // (with the player, the step's own answer decides: the animation has ended or was skipped)
                if (StepBoxAnimation() ?? true)
                {
                    state.Animation.AnimationActive = false;
                }
            }
            joystickKey = input.PollJoystickKey();
        }
        return joystickKey;
    }

    /// <summary>
    /// Alt-A, Alt-S, Alt-J: switches an option and plays the "on" or the "off" sound. Switching the joystick on
    /// measures its centre first.
    /// </summary>
    /// <param name="option">The option to switch.</param>
    /// <param name="before">The options as they were when this pass of the box began.</param>
    private void SwitchOption(Options option, Options before)
    {
        state.Status ^= option;
        if (option == Options.Joystick && state.Status.HasFlag(Options.Joystick))
        {
            world.Input.CalibrateJoystick();
        }
        // the original compares the two flag words as numbers: a flag switched on makes the word greater
        DoSound(before < state.Status ? Sounds.OptionOn : Sounds.OptionOff);
    }

    /// <summary>
    /// Alt-Q: asks "Are you sure you want to Quit?" in a box of its own, and on "Yes" marks the game as over in the
    /// hand-off block and ends the program. On "No" it returns.
    /// </summary>
    private void AskToQuit(int x, int y)
    {
        var animation = state.Animation;
        var wasActive = animation.AnimationActive;
        animation.AnimationActive = false;
        state.Menu.NoHi = false;
        world.Graphics.ShowPage(0);
        const int yes = 1;
        // the question's box is always framed: an even y
        if (QuitPrompt(state.Menu.StrQuitConfirm, x, y & 0xfe) == yes)
        {
            state.Quit = -1;
            if (wasActive)
            {
                EndBoxAnimation();
            }
            shared.NextModule = (sbyte)state.Quit;
            if (LeaveTheGame is { } leave)
            {
                // GAME's copy of the box (00f7:c6ae popup_menu, GAME 00f7:c6ae) calls save_and_hand_off() here: the
                // campaign is saved into the hand-off block and the farewell picture waits for a key
                leave();
            }
            else
            {
                // C: the original calls GoodBye() here without its argument, so that it acts on a leftover word of the
                // stack; whatever that is, the mark it may set was set on the line above.
                CryptoRules.GoodBye(shared, quit: false);
            }
            // the C library's exit(): the program ends here, whoever started the part takes over
            // DIVERGENCE: D-4 exit() is called without an exit code (a leftover word of the stack goes to DOS); here 0.
            throw new ProgramExitException(0);
        }
        animation.AnimationActive = wasActive;
    }

    /// <summary>Moves the highlight bar from one choice to another (-1: no choice).</summary>
    private void MoveHighlight(int x, int y, int from, int to)
    {
        var boxColor = state.Menu.MenuHiliteColor;
        if (from != -1)
        {
            RecolorChoice(x, y, from, BarColor, boxColor);
        }
        if (to != -1)
        {
            RecolorChoice(x, y, to, boxColor, BarColor);
        }
    }

    /// <summary>Changes one colour into another in the rectangle of a choice's line: this is how the highlight bar is drawn and removed.</summary>
    private void RecolorChoice(int x, int y, int choice, int from, int to)
    {
        var line = state.Menu.MenuFirstItem + choice;
        world.Graphics.RecolorRect(state.Rp1, x + 3, y + 4 + line * LineHeight, state.Menu.MenuTextWidth + 3, LineHeight + 1, from, to);
    }
}
