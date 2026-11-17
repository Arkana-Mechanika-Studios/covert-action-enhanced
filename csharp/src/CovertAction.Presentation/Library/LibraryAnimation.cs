using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Animation;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// The animation player: the routines that load a .PAN file (the briefing room, a building's front, an arrest) and
/// play it frame by frame beside a message box or as a scene of its own. The menus (FINAL) and the campaign (GAME)
/// each carry a copy under different names; the 41 routines are the same code in both
/// (docs/source-map/shared-functions.csv, with the names paired in twin-names.csv), so this one implementation
/// stands for both, under FINAL's names. The other parts have empty functions in the player's place.
/// </summary>
/// <remarks>
/// <para>
/// A frame: the animation's program runs until it rests (<see cref="RunSceneScript"/>); every sprite takes a step of
/// its own program (<see cref="StepSprite"/>); sprites the program has marked are drawn onto the background for
/// good; then the page is cleaned up behind the sprites of the last frame, the sprites are drawn, the sounds the
/// program asked for are played and the page is shown.
/// </para>
/// <para>
/// The loader takes the file's parts from <see cref="PanFile"/>, which reads the whole file at once (D-13); the
/// original reads it byte by byte through a buffer and decodes each picture row by row.
/// </para>
/// </remarks>
/// <param name="state">The part's variables (its options: sound, joystick).</param>
/// <param name="pan">The player's variables.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="output">The part's sound and frame-wait routines.</param>
/// <param name="input">The part's key and joystick routines.</param>
/// <param name="pictures">The part's picture files (an animation file is opened like a picture).</param>
/// <param name="behaviourOrNull">Which of the original's bugs to reproduce; the original's own when left out.</param>
public sealed class LibraryAnimation(ILibraryState state, PanEngineState pan, Boundaries world, LibraryOutput output, LibraryInput input, LibraryPictures pictures, OriginalBehaviour? behaviourOrNull = null)
{
    private readonly OriginalBehaviour behaviour = behaviourOrNull ?? OriginalBehaviour.Faithful;

    private const int ScreenWidth = 320;
    private const int ScreenHeight = 200;

    /// <summary>What a sprite's speed is counted against: its program takes a step whenever the speed has added up to more than this.</summary>
    private const int StepCost = 0xff;

    /// <summary>The instructions of an animation's program.</summary>
    private enum Op
    {
        StartSprite = 0, StopSprite = 1, Wait = 2, Sound = 3, Stamp = 4, Push = 5, Store = 6, Duplicate = 7,
        Equal = 8, NotEqual = 9, Greater = 10, Less = 11, GreaterOrEqual = 12, LessOrEqual = 13,
        Add = 14, Subtract = 15, Multiply = 16, Divide = 17,
        JumpIf = 18, Jump = 19, Rest = 20, End = 21, Return = 22, Call = 23,
    }

    /// <summary>The instructions of a sprite's program.</summary>
    private enum SpriteOp
    {
        Show = 0, MoveTo = 1, MoveBy = 2, SetSpeed = 3, ChangeSpeed = 4, Repeat = 5, Loop = 6, StartOver = 7, Rewind = 8, Pause = 9, Stop = 10,
    }

    // ---- playing an animation ----

    /// <summary>Plays an animation file to its end (or until a key skips it), at a place on the screen.</summary>
    /// <param name="fileName">The animation's file name.</param>
    /// <param name="speed">How long it may run; -1 for "until its program ends".</param>
    /// <param name="x">Left edge in pixels; -1 keeps the file's.</param>
    /// <param name="y">Top edge in pixels; -1 keeps the file's.</param>
    // orig: FINAL 0100:abd2 play_pan_file (src/c/final/final_0100_abd2.c) matched; also GAME 00f7:b3b0; tests: AnimationOracleTests, LibraryAnimationTests
    public void PlayPanFile(string fileName, int speed, int x, int y)
    {
        var request = pan.PanParams;
        request.File = fileName;
        request.Speed = (short)speed;
        request.X = (short)x;
        request.Y = (short)y;
        request.FrameLength = AnimationParams.Keep;
        PlayAnimation(request);
    }

    /// <summary>Hands a value to the animation's program as one of its variables (which scene to play, for one).</summary>
    /// <param name="index">The variable, 0..50.</param>
    /// <param name="value">The value.</param>
    // orig: FINAL 0100:ac01 set_script_var (src/c/final/final_0100_ac01.c) matched; also GAME 00f7:b3df; tests: AnimationOracleTests
    public void SetScriptVar(int index, int value) => pan.ScriptVars[index] = (short)value;

    /// <summary>Plays a sound the animation's program asked for, unless sound is off.</summary>
    /// <param name="cue">The sound's number.</param>
    // orig: FINAL 0100:ac12 play_scene_sound (src/c/final/final_0100_ac12.c) matched; also GAME 00f7:b3f0; tests: AnimationOracleTests
    public void PlaySceneSound(int cue) => output.DoSound(cue);

    /// <summary>
    /// Whether the player wants to skip the animation: a key is waiting (or the joystick's button is down, with the
    /// joystick on) and the animation has run for three steps. The game can have one such wish ignored.
    /// </summary>
    // orig: FINAL 0100:ac20 animation_skip_pressed (src/c/final/final_0100_ac20.c) matched; also GAME 00f7:b3fe; tests: AnimationOracleTests
    public bool AnimationSkipPressed()
    {
        const int stepsBeforeSkip = 3;
        var wanted = !world.Input.NoKeyWaiting() || (state.Status.HasFlag(Options.Joystick) && input.JoystickButtonDown(0));
        // C: the step count is compared as an unsigned number
        if (!wanted || (ushort)pan.AnimationSteps < stepsBeforeSkip)
        {
            return false;
        }
        // BUG: CA-34 the original means to take the key out of the buffer here, but tests the address of the routine
        // no_key_waiting instead of calling it; an address is never 0, so the key is never read and stays for
        // whoever reads keys next (OriginalBehaviour.FixAnimationSkipKeyLeftInBuffer).
        if (behaviour.FixAnimationSkipKeyLeftInBuffer && !world.Input.NoKeyWaiting())
        {
            input.ReadKey();
        }
        if (pan.IgnoreSkip != 0)
        {
            pan.IgnoreSkip = 0;
            pan.ScriptVars[PanEngineState.KeyPressedVariable] = -1;
            return false;
        }
        return true;
    }

    /// <summary>Plays an animation to its end: starts it, steps it until it has ended or was skipped, and stops it.</summary>
    /// <param name="request">The animation and where to play it.</param>
    // orig: FINAL 0100:ac6c play_animation (src/c/final/final_0100_ac6c.c) matched; also GAME 00f7:b44a; tests: AnimationOracleTests, LibraryAnimationTests
    public void PlayAnimation(AnimationParams request)
    {
        // C: the original steps even when the file could not be loaded
        StartAnimation(request);
        while (AnimationStep() == 0)
        {
        }
        StopAnimation();
    }

    /// <summary>Loads an animation and gets it ready to play: its place, its pace, its palette, an empty stage.</summary>
    /// <param name="request">The animation and where to play it.</param>
    /// <returns>Whether the file could be loaded.</returns>
    // orig: FINAL 0100:ac84 start_animation (src/c/final/final_0100_ac84.c) matched; also GAME 00f7:b462; tests: AnimationOracleTests, LibraryAnimationTests
    // Other code under the same name: the message box's start_animation of the other parts (LibraryMenus.StartAnimation),
    // which FINAL calls select_animation.
    public bool StartAnimation(AnimationParams request)
    {
        pan.AnimationPtr = request;
        pan.AnimationSteps = 0;
        if (!LoadPanFile(request.File))
        {
            return false;
        }
        SetAnimationScreen(request.Screen);
        pan.AnimationScreen = request.Screen;
        if (request.FrameLength != AnimationParams.Keep)
        {
            pan.FrameLength = request.FrameLength;
        }
        if (request.X != AnimationParams.Keep)
        {
            pan.PanPort.XBgn = pan.PanWorkPort.XBgn = request.X;
        }
        if (request.Y != AnimationParams.Keep)
        {
            pan.PanPort.YBgn = pan.PanWorkPort.YBgn = request.Y;
        }
        if (request.Speed != AnimationParams.Keep)
        {
            // the caller's time in ticks, plus up to one frame, as a number of frames
            // C: unsigned 16-bit arithmetic; a frame length of 0 is a divide error in the original too
            const int roundUp = 0x13;
            pan.FrameBudget = (short)C16.UDiv((ushort)((ushort)request.Speed * (pan.FrameLength + roundUp)), (ushort)pan.FrameLength);
        }
        else
        {
            pan.FrameBudget = -1;
        }
        ApplyPanPalette();
        ResetAnimationState();
        return true;
    }

    /// <summary>Notes the screen value of the animation.</summary>
    /// <param name="screen">The screen value.</param>
    /// <returns>The same value.</returns>
    // orig: FINAL 0100:ad37 set_animation_screen (src/c/final/final_0100_ad37.c) matched; also GAME 00f7:b515; tests: AnimationOracleTests
    public int SetAnimationScreen(int screen) => pan.AnimationScreen = (short)screen;

    /// <summary>Ends the animation: where the display flips pages and the hidden one is showing, the saved screen comes back.</summary>
    // orig: FINAL 0100:ad42 stop_animation (src/c/final/final_0100_ad42.c) matched; also GAME 00f7:b520; tests: AnimationOracleTests
    // DIVERGENCE: D-14 the original gives the program's memory and the images' memory back to DOS; here they are objects that are simply let go of.
    public void StopAnimation()
    {
        pan.AnimationScreen = 0;
        if (world.Graphics.PageFlipActive(0) != 0 && pan.PanPort.PageNr == 0)
        {
            world.Graphics.CopyRect(pan.PanSavedScreen, 0, 0, ScreenWidth, ScreenHeight, pan.PanScreenCopy, 0, 0);
            world.Graphics.ShowPage(pan.PanPort.PageNr);
            pan.PanPort.PageNr ^= 1;
        }
    }

    /// <summary>One step of a running animation: a frame, paced by the PC's clock.</summary>
    /// <returns>-1 when the player skipped it, 1 when it has ended (its program, or its time), else 0.</returns>
    // orig: FINAL 0100:add0 animation_step (src/c/final/final_0100_add0.c) matched; also GAME 00f7:b5ae; tests: AnimationOracleTests
    // Other code under the same name: the empty animation_step of the parts without the player (LibraryMenus.AnimationStep).
    public int AnimationStep()
    {
        if (AnimationSkipPressed())
        {
            return -1;
        }
        pan.AnimationSteps++;
        WaitFrameTick();
        var ended = RunPanFrames(1);
        WaitFrameTick();
        pan.FrameBudget--;
        if (pan.FrameBudget == 0)
        {
            ended = 1;
        }
        return ended;
    }

    /// <summary>Empties the stage for a new animation: the program at its start, no sprite, nothing to clean up, the pages to be set up.</summary>
    /// <returns>0.</returns>
    // orig: FINAL 0100:ae0b reset_animation_state (src/c/final/final_0100_ae0b.c) matched; also GAME 00f7:b5e9; tests: AnimationOracleTests
    public int ResetAnimationState()
    {
        InitScript(0);
        ClearSprites();
        world.Graphics.ClearSpanTracking();
        const int setUpBothPages = 2;
        pan.PageState = setUpBothPages;
        pan.ScriptDone = false;
        pan.FrameCounter = pan.ScriptState = 0;
        pan.PanPort.PageNr = 1;
        return 0;
    }

    /// <summary>Plays frames: for each, the program and the sprites; then one picture of the result is drawn and shown.</summary>
    /// <param name="n">The number of frames to play before the picture is shown.</param>
    /// <returns>1 when the animation's program has ended (nothing is shown then), else 0.</returns>
    // orig: FINAL 0100:ae33 run_pan_frames (src/c/final/final_0100_ae33.c) matched; also GAME 00f7:b611; tests: AnimationOracleTests
    public int RunPanFrames(int n)
    {
        while (n-- != 0)
        {
            ClearSpriteFlags();
            if (RunSceneScript() != 0)
            {
                return 1;
            }
            StepSprites();
            DrawFlaggedSprites();
            pan.FrameCounter++;
        }
        EndOfFramePages();
        DrawSprites();
        PlayQueuedSounds();
        world.Graphics.ShowPage(pan.PanPort.PageNr);
        output.WaitVsync(1);
        if (world.Graphics.PageFlipActive(0) != 0)
        {
            // where the display flips pages, the next frame is drawn on the other one
            pan.PanPort.PageNr ^= 1;
        }
        return 0;
    }

    /// <summary>The frame timer: called twice a step. One call notes when the next frame is due; the next waits until then.</summary>
    // orig: FINAL 0100:ae96 wait_frame_tick (src/c/final/final_0100_ae96.c) matched; also GAME 00f7:b674; tests: AnimationOracleTests
    public void WaitFrameTick()
    {
        if (pan.FrameParity != 0)
        {
            world.Timer.ClockTicks(out var now);
            pan.FrameDeadline = now + pan.FrameLength;
        }
        else
        {
            // the wait also ends when the clock passes midnight, where its count starts again
            while (!world.Timer.ClockTicks(out var now) && now < pan.FrameDeadline)
            {
            }
        }
        pan.FrameParity ^= 1;
    }

    /// <summary>Sets the animation's palette, when its file brought one; the first colour is always black.</summary>
    // orig: FINAL 0100:aeeb apply_pan_palette (src/c/final/final_0100_aeeb.c) matched; also GAME 00f7:b6c9; tests: AnimationOracleTests
    public void ApplyPanPalette()
    {
        if (pan.PanHasPalette != 0)
        {
            pan.PanPalette[0] = 0;
            world.Graphics.SetPalette(pan.PanPalette);
        }
    }

    /// <summary>An empty function of the original.</summary>
    // orig: FINAL 0100:af04 sub_final_0100_af04 (src/c/final/final_0100_aeeb.c) matched; also GAME 00f7:b6e2; tests: AnimationOracleTests
    // DORMANT: empty in both programs.
    public void SubFinal0100Af04()
    {
    }

    /// <summary>
    /// Gets the page ready for this frame's sprites. In the first frame the background goes to the visible page and
    /// to the work page; afterwards only what the last frame's sprites covered is put back from the work page, or,
    /// where the display flips pages, the whole picture once more for the second page.
    /// </summary>
    // orig: FINAL 0100:af05 end_of_frame_pages (src/c/final/final_0100_af05.c) matched; also GAME 00f7:b6e3; tests: AnimationOracleTests
    public void EndOfFramePages()
    {
        var port = pan.PanPort;
        int width = port.Width1 + 1, height = port.Length1 + 1;
        switch (pan.PageState)
        {
            case 2:
                pan.PageState--;
                world.Graphics.CopyRect(pan.PanBackground, 0, 0, width, height, port, 0, 0);
                world.Graphics.CopyRect(port, 0, 0, width, height, pan.PanWorkPort, 0, 0);
                break;
            case 1:
                pan.PageState--;
                if (world.Graphics.PageFlipActive(0) != 0)
                {
                    world.Graphics.CopyRect(pan.PanSavedScreen, 0, 0, ScreenWidth, ScreenHeight, pan.PanScreenCopy, 0, 0);
                    world.Graphics.CopyRect(pan.PanWorkPort, 0, 0, width, height, port, 0, 0);
                }
                else
                {
                    world.Graphics.RestoreTrackedSpans(pan.PanWorkPort.PageNr, port.PageNr);
                }
                break;
            case 0:
                world.Graphics.RestoreTrackedSpans(pan.PanWorkPort.PageNr, port.PageNr);
                break;
        }
    }

    /// <summary>Plays the sounds the program asked for in this frame, the last one first.</summary>
    // orig: FINAL 0100:afd2 play_queued_sounds (src/c/final/final_0100_afd2.c) matched; also GAME 00f7:b7b0; tests: AnimationOracleTests
    public void PlayQueuedSounds()
    {
        while (pan.SoundQueueCount != 0)
        {
            pan.SoundQueueCount--;
            PlaySceneSound(Queued(pan.SoundQueueCount));
        }
    }

    // ---- the animation's program ----

    /// <summary>Puts the program at its start: the loaded program becomes the running one, its stack and its sounds are empty.</summary>
    /// <param name="unused">Not used.</param>
    /// <returns>0.</returns>
    // orig: FINAL 0100:affc init_script (src/c/final/final_0100_affc.c) matched; also GAME 00f7:b7da; tests: AnimationOracleTests
    public int InitScript(int unused)
    {
        _ = unused;
        pan.ScriptSegment = pan.PanSegment;
        pan.ScriptPc = 0;
        return pan.ScriptSp = pan.SoundQueueCount = 0;
    }

    /// <summary>
    /// Runs the animation's program until it rests for this frame. The program is a list of instructions working on
    /// a stack of numbers: start or stop a sprite, wait some frames, ask for a sound, mark a sprite to be stamped
    /// onto the background, push a number or one of the game's variables, store into one, compare, calculate, jump,
    /// call and return, rest until the next frame, end.
    /// </summary>
    /// <returns>1 when the program has ended, 0 when it rests (it is run again next frame).</returns>
    // orig: FINAL 0100:b01a run_scene_script (src/c/final/final_0100_b01a.c) matched; also GAME 00f7:b7f8; tests: AnimationOracleTests, LibraryAnimationTests
    public int RunSceneScript()
    {
        const int running = 0, waiting = 1;
        while (!pan.ScriptDone)
        {
            if (pan.ScriptState == waiting)
            {
                // C: the count is taken down even when it was 0, so it is -1 when the wait is over
                if (pan.ScriptWait-- != 0)
                {
                    return 0;
                }
                pan.ScriptState = running;
                continue;
            }
            if (pan.ScriptState != running)
            {
                // BUG: CA-10 a state that is neither: the original's loop then turns for ever
                throw Hangs($"program state {pan.ScriptState}");
            }
            // C: the instruction is read as a signed character
            pan.ScriptOpcode = (sbyte)ScriptByte(0);
            switch ((Op)pan.ScriptOpcode)
            {
                case Op.StartSprite:
                    StartSprite();
                    pan.ScriptSp -= 7;
                    pan.ScriptPc++;
                    break;
                case Op.StopSprite:
                    if (IsSpriteNumber(Stack(-1)))
                    {
                        pan.Sprites[Stack(-1)].Active = false;
                    }
                    pan.ScriptSp--;
                    pan.ScriptPc++;
                    break;
                case Op.Wait:
                    pan.ScriptSp--;
                    pan.ScriptWait = Stack(0);
                    pan.ScriptState = waiting;
                    pan.ScriptPc++;
                    break;
                case Op.Sound:
                    pan.ScriptSp--;
                    Queue(pan.SoundQueueCount++, Stack(0));
                    pan.ScriptPc++;
                    break;
                case Op.Stamp:
                    if (IsSpriteNumber(Stack(-1)))
                    {
                        pan.Sprites[Stack(-1)].Stamp = true;
                    }
                    pan.ScriptSp--;
                    pan.ScriptPc++;
                    break;
                case Op.Push:
                    // C: the interpreter uses the wait counter as its working value here and in the next instruction
                    pan.ScriptWait = ScriptWord(2);
                    if (ScriptByte(1) != 0)
                    {
                        pan.ScriptWait = ScriptVar(pan.ScriptWait);
                    }
                    pan.ScriptSp++;
                    SetStack(-1, pan.ScriptWait);
                    pan.ScriptPc += 4;
                    break;
                case Op.Store:
                    pan.ScriptSp--;
                    pan.ScriptWait = ScriptWord(1);
                    if (pan.ScriptWait is >= 0 and < PanEngineState.ScriptVarCount)
                    {
                        pan.ScriptVars[pan.ScriptWait] = Stack(0);
                    }
                    pan.ScriptPc += 3;
                    break;
                case Op.Duplicate:
                    pan.ScriptSp++;
                    SetStack(-1, Stack(-2));
                    pan.ScriptPc++;
                    break;
                case >= Op.Equal and <= Op.Divide:
                    pan.ScriptSp--;
                    SetStack(-1, Calculate((Op)pan.ScriptOpcode, Stack(-1), Stack(0)));
                    pan.ScriptPc++;
                    break;
                case Op.JumpIf:
                    pan.ScriptSp--;
                    pan.ScriptPc = Stack(0) == 0 ? (ushort)(pan.ScriptPc + 3) : (ushort)ScriptWord(1);
                    break;
                case Op.Jump:
                    pan.ScriptPc = (ushort)ScriptWord(1);
                    break;
                case Op.Rest:
                    // the program stays on this instruction: it rests here in every later frame too
                    return 0;
                case Op.End:
                    pan.ScriptDone = true;
                    break;
                case Op.Call:
                    pan.ScriptSp++;
                    SetStack(-1, (short)(pan.ScriptPc + 3));
                    pan.ScriptPc = (ushort)ScriptWord(1);
                    break;
                case Op.Return:
                    pan.ScriptSp--;
                    pan.ScriptPc = (ushort)Stack(0);
                    break;
                default:
                    // BUG: CA-10 an instruction the interpreter does not know: it neither acts nor moves on, for ever
                    throw Hangs($"instruction {pan.ScriptOpcode} of the animation's program");
            }
        }
        return 1;
    }

    /// <summary>
    /// The "start a sprite" instruction: seven numbers on the stack say where the sprite's program starts, which
    /// sprite it is (-1: the first free one), its parent, its place, its speed and whether it is drawn untracked.
    /// </summary>
    private void StartSprite()
    {
        if (Stack(-6) == -1)
        {
            SetStack(-6, (short)FindFreeSprite());
        }
        if (IsSpriteNumber(Stack(-6)))
        {
            InitSprite(Stack(-6), Stack(-7), Stack(-5), Stack(-4), Stack(-3), Stack(-2), Stack(-1));
        }
    }

    /// <summary>One of the program's calculations on two numbers of the stack.</summary>
    private static short Calculate(Op op, short left, short right) => op switch
    {
        Op.Equal => (short)(left == right ? 1 : 0),
        Op.NotEqual => (short)(left != right ? 1 : 0),
        Op.Greater => (short)(left > right ? 1 : 0),
        Op.Less => (short)(left < right ? 1 : 0),
        Op.GreaterOrEqual => (short)(left >= right ? 1 : 0),
        Op.LessOrEqual => (short)(left <= right ? 1 : 0),
        // C: 16-bit results: sums and products wrap round
        Op.Add => (short)(left + right),
        Op.Subtract => (short)(left - right),
        Op.Multiply => (short)(right * left),
        // C: a division by 0 stops the original with a divide error
        _ => C16.Div(left, right),
    };

    private static bool IsSpriteNumber(int number) => number is > 0 and <= PanEngineState.SpriteCount;

    // ---- the sprites ----

    /// <summary>The number of the first sprite that is not in use; 50 when all are.</summary>
    // orig: FINAL 0100:b488 find_free_sprite (src/c/final/final_0100_b488.c) matched; also GAME 00f7:bc66; tests: AnimationOracleTests
    public int FindFreeSprite()
    {
        for (var number = 1; number <= PanEngineState.SpriteCount; number++)
        {
            if (!pan.Sprites[number].Active)
            {
                return number;
            }
        }
        return PanEngineState.SpriteCount;
    }

    /// <summary>Takes every sprite off the stage.</summary>
    // orig: FINAL 0100:b4bf clear_sprites (src/c/final/final_0100_b4bf.c) matched; also GAME 00f7:bc9d; tests: AnimationOracleTests
    public void ClearSprites()
    {
        for (var number = 1; number <= PanEngineState.SpriteCount; number++)
        {
            pan.Sprites[number].Active = false;
        }
    }

    /// <summary>Starts a sprite.</summary>
    /// <param name="sprite">The sprite's number, 1..50.</param>
    /// <param name="programOffset">Where its program starts in the animation's program.</param>
    /// <param name="parent">The number of the sprite it is placed relative to, or -1 for none.</param>
    /// <param name="offsetX">Its left edge, or its distance from the parent's.</param>
    /// <param name="offsetY">The same for the top edge.</param>
    /// <param name="speed">Its speed: 255 for a step of its program every frame.</param>
    /// <param name="imageSet">Not 0 for a sprite that is drawn untracked.</param>
    // orig: FINAL 0100:b4ea init_sprite (src/c/final/final_0100_b4ea.c) matched; also GAME 00f7:bcc8; tests: AnimationOracleTests
    public void InitSprite(int sprite, int programOffset, int parent, int offsetX, int offsetY, int speed, int imageSet)
    {
        var s = pan.Sprites[sprite];
        s.Active = true;
        s.Stamp = false;
        s.Parent = (short)(parent - 1);
        s.OffsetX = (short)offsetX;
        s.OffsetY = (short)offsetY;
        var alone = s.Parent == PanSprite.NoParent;
        s.X = alone ? s.OffsetX : (short)0;
        s.Y = alone ? s.OffsetY : (short)0;
        s.Speed = s.StartSpeed = (short)speed;
        s.Credit = StepCost;
        s.LoopDepth = 0;
        s.ProgramStart = (ushort)programOffset;
        s.Untracked = (short)imageSet;
        s.Pc = s.ProgramStart;
        s.PcSegment = pan.PanSegment;
        // BUG: CA-10 the image number is left as the record's last user had it, until the sprite's program shows one
    }

    /// <summary>Lets every sprite take the step its speed has earned.</summary>
    // orig: FINAL 0100:b596 step_sprites (src/c/final/final_0100_b596.c) matched; also GAME 00f7:bd74; tests: AnimationOracleTests
    public void StepSprites()
    {
        for (var i = 0; i < PanEngineState.SpriteCount; i++)
        {
            StepSprite(i);
        }
    }

    /// <summary>Draws the sprites the program has marked in this frame onto the work page, for good: they become part of the background.</summary>
    // orig: FINAL 0100:b5b7 draw_flagged_sprites (src/c/final/final_0100_b5b7.c) matched; also GAME 00f7:bd95; tests: AnimationOracleTests
    public void DrawFlaggedSprites()
    {
        // C: one pair of position variables for the whole loop: a sprite whose parent is gone is drawn where the
        // sprite before it was (see SpriteScreenPos)
        int? x = null, y = null;
        for (var i = 0; i < PanEngineState.SpriteCount; i++)
        {
            var sprite = pan.Sprites[i + 1];
            if (sprite.Active && sprite.Stamp)
            {
                SpriteScreenPos(i, ref x, ref y);
                world.Graphics.DrawImageToBackground(pan.PanWorkPort, Position(x), Position(y), ImageOf(sprite));
            }
        }
    }

    /// <summary>Draws every sprite on the visible page.</summary>
    // orig: FINAL 0100:b62a draw_sprites (src/c/final/final_0100_b62a.c) matched; also GAME 00f7:be08; tests: AnimationOracleTests
    public void DrawSprites()
    {
        for (var i = 0; i < PanEngineState.SpriteCount; i++)
        {
            if (pan.Sprites[i + 1].Active)
            {
                DrawSprite(i);
            }
        }
    }

    /// <summary>
    /// Draws a sprite on the visible page, unless it has no image yet or was stamped onto the background in this
    /// frame. Its place is remembered, to be cleaned up before the next frame, unless the sprite asked not to be tracked.
    /// </summary>
    /// <param name="i">The sprite, counted from 0.</param>
    // orig: FINAL 0100:b65f draw_sprite (src/c/final/final_0100_b65f.c) matched; also GAME 00f7:be3d; tests: AnimationOracleTests
    // Other code under the same name: TAC 00e8:5b0d (the raid's own sprites).
    public void DrawSprite(int i)
    {
        var sprite = pan.Sprites[i + 1];
        if (sprite.Image == PanSprite.NoImage || sprite.Stamp)
        {
            return;
        }
        int? x = null, y = null;
        SpriteScreenPos(i, ref x, ref y);
        if (sprite.Untracked != 0)
        {
            world.Graphics.DrawImageUntracked(pan.PanPort, Position(x), Position(y), ImageOf(sprite));
        }
        else
        {
            world.Graphics.DrawImageTracked(pan.PanPort, Position(x), Position(y), ImageOf(sprite));
        }
    }

    /// <summary>Takes the "stamp" marks of the last frame off the sprites.</summary>
    // orig: FINAL 0100:b6e4 clear_sprite_flags (src/c/final/final_0100_b6e4.c) matched; also GAME 00f7:bec2; tests: AnimationOracleTests
    public void ClearSpriteFlags()
    {
        for (var number = 1; number <= PanEngineState.SpriteCount; number++)
        {
            var sprite = pan.Sprites[number];
            if (sprite.Active && sprite.Stamp)
            {
                sprite.Stamp = false;
            }
        }
    }

    /// <summary>
    /// Where a sprite is on the page: its own place, or for a sprite with a parent the parent's place plus its
    /// distance from it plus how far it has moved. A sprite whose parent is not on the stage gets no answer.
    /// </summary>
    /// <param name="sprite">The sprite, counted from 0.</param>
    /// <param name="x">Receives the left edge; left as it was when there is no answer.</param>
    /// <param name="y">Receives the top edge; left as it was when there is no answer.</param>
    // orig: FINAL 0100:b723 sprite_screen_pos (src/c/final/final_0100_b723.c) matched; also GAME 00f7:bf01; tests: AnimationOracleTests
    public void SpriteScreenPos(int sprite, ref int? x, ref int? y)
    {
        var s = pan.Sprites[sprite + 1];
        if (s.Parent == PanSprite.NoParent)
        {
            x = s.X;
            y = s.Y;
            return;
        }
        // BUG: CA-10 the original's second test here (a parent number above 50) can never decide anything
        if (s.Parent + 1 <= 0)
        {
            return;
        }
        if (s.Parent >= PanEngineState.SpriteCount)
        {
            throw new MemoryOverrunException($"a sprite's parent is number {s.Parent + 1}; the original reads what lies behind its {PanEngineState.SpriteCount} sprites");
        }
        var parent = pan.Sprites[s.Parent + 1];
        if (!parent.Active)
        {
            return;
        }
        // C: 16-bit sums
        x = (short)(parent.X + s.OffsetX + s.X);
        y = (short)(parent.Y + s.OffsetY + s.Y);
    }

    /// <summary>
    /// A step of a sprite's own program, when its speed has earned one: instructions are carried out up to the next
    /// "show this image" (move to, move by, set or change the speed, repeat a stretch, start over, pause, stop).
    /// </summary>
    /// <param name="i">The sprite, counted from 0.</param>
    // orig: FINAL 0100:b7c6 step_sprite (src/c/final/final_0100_b7c6.c) matched; also GAME 00f7:bfa4; tests: AnimationOracleTests, LibraryAnimationTests
    public void StepSprite(int i)
    {
        var s = pan.Sprites[i + 1];
        if (!s.Active)
        {
            return;
        }
        s.Credit += s.Speed;
        if (s.Credit <= StepCost)
        {
            return;
        }
        s.Credit -= StepCost;
        while (true)
        {
            // C: the instruction is read as a signed character
            var op = (SpriteOp)(sbyte)SpriteByte(s, 0);
            switch (op)
            {
                case SpriteOp.Show:
                    s.Image = SpriteByte(s, 1);
                    s.Pc += 2;
                    return;
                case SpriteOp.MoveTo:
                    s.X = SpriteWord(s, 1);
                    s.Y = SpriteWord(s, 3);
                    s.Pc += 5;
                    break;
                case SpriteOp.MoveBy:
                    s.X += SpriteWord(s, 1);
                    s.Y += SpriteWord(s, 3);
                    s.Pc += 5;
                    break;
                case SpriteOp.SetSpeed:
                    s.Speed = SpriteWord(s, 1);
                    s.Pc += 3;
                    break;
                case SpriteOp.ChangeSpeed:
                    s.Speed += SpriteWord(s, 1);
                    s.Pc += 3;
                    break;
                case SpriteOp.Repeat:
                    // BUG: CA-10 an eleventh open loop writes over the count of open loops in the original
                    if (s.LoopDepth is < 0 or >= PanSprite.LoopLevels)
                    {
                        throw new MemoryOverrunException($"a sprite's program opens loop {s.LoopDepth + 1}; the original has room for {PanSprite.LoopLevels}");
                    }
                    s.LoopCounters[s.LoopDepth++] = SpriteWord(s, 1);
                    s.Pc += 3;
                    break;
                case SpriteOp.Loop:
                    if (s.LoopDepth is < 1 or > PanSprite.LoopLevels)
                    {
                        throw new MemoryOverrunException("a sprite's program ends a loop it has not opened; the original counts in the word before its loop counters");
                    }
                    if (--s.LoopCounters[s.LoopDepth - 1] != 0)
                    {
                        s.Pc = (ushort)SpriteWord(s, 1);
                    }
                    else
                    {
                        s.LoopDepth--;
                        s.Pc += 3;
                    }
                    break;
                case SpriteOp.StartOver:
                    var alone = s.Parent == PanSprite.NoParent;
                    s.X = alone ? s.OffsetX : (short)0;
                    s.Y = alone ? s.OffsetY : (short)0;
                    s.Speed = s.StartSpeed;
                    s.Credit = StepCost;
                    goto case SpriteOp.Rewind;
                case SpriteOp.Rewind:
                    s.LoopDepth = 0;
                    s.Pc = s.ProgramStart;
                    break;
                case SpriteOp.Pause:
                    // the sprite's program stays on this instruction
                    return;
                case SpriteOp.Stop:
                    s.Active = false;
                    return;
                default:
                    // BUG: CA-10 an instruction the sprite's interpreter does not know: it neither acts nor moves on, for ever
                    throw Hangs($"instruction {(int)op} of a sprite's program");
            }
        }
    }

    // ---- loading an animation file ----

    /// <summary>Loads an animation file: its palette, its place and pace, its background onto the work page, its images, its program.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>Whether the file is an animation of the right version and everything was loaded.</returns>
    // orig: FINAL 0100:ba76 load_pan_file (src/c/final/final_0100_ba76.c) matched; also GAME 00f7:c254; tests: LibraryAnimationTests
    // DIVERGENCE: D-13 the file is read to its end and taken apart at once (Content.PanFile) instead of byte by byte through a buffer.
    public bool LoadPanFile(string fileName)
    {
        pan.PanHandle = (short)pictures.OpenResource(fileName, BinaryFile);
        var bytes = pictures.ReadToEnd(pan.PanHandle);
        if (!ReadPanHeader(bytes))
        {
            pictures.CloseResource(pan.PanHandle);
            return false;
        }
        var file = PanFile.Read(bytes);
        // DIVERGENCE: D-683 with the player's option of the Amiga version's pictures, an animation the option has a twin for plays with the Amiga's images and background (and, for the captured dossier, its program): the game's own file is read first, and its header, palette, place and pace stay
        file = world.AnimationReplacements?.Find(fileName, file) ?? file;
        ReadPanPaletteHeader(file);
        ReadPanPortRecord(file);
        ReadPanBackground(file);
        if (!ReadPanImages(file) || !ReadPanScript(file))
        {
            pictures.CloseResource(pan.PanHandle);
            return false;
        }
        pictures.CloseResource(pan.PanHandle);
        // DIVERGENCE: D-660 a player's option (cutContent.capturedDossier, CC-19): the dossier page's script starts by setting its sex variable to "man"
        if (behaviour.CutContent.CapturedDossier)
        {
            FreeTheDossiersSexVariable(fileName);
        }
        return true;
    }

    /// <summary>The first instruction of CAPTRD01.PAN's program: push -1, store into variable 0.</summary>
    private static readonly byte[] DossierFirstInstructions = [0x05, 0x00, 0xff, 0xff, 0x06, 0x00, 0x00];

    /// <summary>
    /// CC-19: the captured dossier's program begins "variable 0 = -1" and so always takes the man's photograph, though the
    /// file has the woman's too and a switch on the variable, as INTRGATE.PAN has. When the caller asked for the woman
    /// (variable 0 is 1, set with <see cref="SetScriptVar"/> before the animation starts) the loaded copy of the program
    /// (never the file) pushes 1 instead of -1.
    /// </summary>
    private void FreeTheDossiersSexVariable(string fileName)
    {
        if (!fileName.EndsWith("captrd01.pan", StringComparison.OrdinalIgnoreCase)
            || pan.PanSegment is not { } program
            || program.Length < DossierFirstInstructions.Length
            || !program.AsSpan(0, DossierFirstInstructions.Length).SequenceEqual(DossierFirstInstructions))
        {
            return;
        }
        // push the woman's value 1 when the caller asked for her; the man's -1 stays
        if (pan.ScriptVars[0] == 1)
        {
            program[2] = 1;
            program[3] = 0;
        }
    }

    /// <summary>The access mode the original opens the file with: as the bytes it has.</summary>
    private const int BinaryFile = 0x8000;

    /// <summary>Checks the file's first six bytes: the mark "PANI", the version, and whether its pictures are packed.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>Whether it is an animation file of the version the player knows.</returns>
    // orig: FINAL 0100:bad6 read_pan_header (src/c/final/final_0100_bad6.c) matched; also GAME 00f7:c2b4; tests: LibraryAnimationTests
    public bool ReadPanHeader(ReadOnlySpan<byte> file)
    {
        const int markLength = 4, headerLength = 6;
        if (file.Length < headerLength)
        {
            throw new MemoryOverrunException("the animation file ends inside its first six bytes; the original reads on with what its buffer held");
        }
        var mark = System.Text.Encoding.Latin1.GetString(file[..markLength]);
        var end = mark.IndexOf('\0');
        pan.PanFileMagic = end < 0 ? mark : mark[..end];
        pan.PanFileVersion = file[markLength];
        pan.PanPacked = file[markLength + 1];
        const int notAnAnimation = 1, anotherVersion = 2;
        if (pan.PanMagic != pan.PanFileMagic)
        {
            pan.PanError = notAnAnimation;
            return false;
        }
        if (pan.PanFileVersion != pan.PanVersion)
        {
            pan.PanError = anotherVersion;
            return false;
        }
        return true;
    }

    /// <summary>Takes the file's palette block, when it has one.</summary>
    /// <param name="file">The file.</param>
    // orig: FINAL 0100:bb3a read_pan_palette_header (src/c/final/final_0100_bb3a.c) matched; also GAME 00f7:c318; tests: LibraryAnimationTests
    public void ReadPanPaletteHeader(PanFile file)
    {
        // C: the two marks are bytes read into the low half of a word
        pan.PanHasPalette = (short)(pan.PanHasPalette & ~0xff | (file.HasPalette ? 1 : 0));
        if (file.HasPalette)
        {
            pan.PanPaletteType = (short)(pan.PanPaletteType & ~0xff | file.PaletteKind);
            file.PaletteBlock.CopyTo(pan.PanPalette, 0);
        }
    }

    /// <summary>Takes the animation's place, size and pace: the visible page's port gets them, and the work page's port is a copy for page 2.</summary>
    /// <param name="file">The file.</param>
    // orig: FINAL 0100:bb90 read_pan_port_record (src/c/final/final_0100_bb90.c) matched; also GAME 00f7:c36e; tests: LibraryAnimationTests
    public void ReadPanPortRecord(PanFile file)
    {
        var port = pan.PanPort;
        port.PageNr = 1;
        port.XBgn = (short)file.X;
        port.YBgn = (short)file.Y;
        port.Width1 = (short)(file.Width - 1);
        port.Length1 = (short)(file.Height - 1);
        pan.FrameLength = (short)file.FrameLength;
        var work = pan.PanWorkPort;
        (work.XBgn, work.YBgn, work.Width1, work.Length1) = (port.XBgn, port.YBgn, port.Width1, port.Length1);
        (work.DrawMode, work.APen, work.BPen, work.FontNr) = (port.DrawMode, port.APen, port.BPen, port.FontNr);
        const int workPage = 2;
        work.PageNr = workPage;
    }

    /// <summary>Puts the file's background on the work page: a picture, or one colour all over, or nothing.</summary>
    /// <param name="file">The file.</param>
    // orig: FINAL 0100:bc17 read_pan_background (src/c/final/final_0100_bc17.c) matched; also GAME 00f7:c3f5; tests: LibraryAnimationTests
    public void ReadPanBackground(PanFile file)
    {
        const int aPicture = 1, oneColour = 2;
        switch (file.BackgroundKind)
        {
            case aPicture:
                ReadPanPictureToPage(file.BackgroundPicture!, pan.PanWorkPort.PageNr);
                break;
            case oneColour:
                world.Graphics.FillPage(pan.PanWorkPort.PageNr, file.BackgroundColour);
                break;
        }
    }

    /// <summary>Makes the file's images, up to 250, into images of the graphics driver.</summary>
    /// <param name="file">The file.</param>
    /// <returns>Whether there was memory for all of them.</returns>
    // orig: FINAL 0100:bc68 read_pan_images (src/c/final/final_0100_bc68.c) matched; also GAME 00f7:c446; tests: LibraryAnimationTests
    public bool ReadPanImages(PanFile file)
    {
        var complete = true;
        if (!world.Graphics.BeginImageCuts())
        {
            return false;
        }
        for (var i = 0; i < PanEngineState.ImagePlaces; i++)
        {
            pan.SpriteImages[i] = null;
            if (file.Images[i] is { } picture)
            {
                pan.SpriteImages[i] = ReadPanImage(picture);
                if (pan.SpriteImages[i] is null)
                {
                    pan.PanError = 0;
                    complete = false;
                    break;
                }
            }
        }
        world.Graphics.EndImageCuts();
        return complete;
    }

    /// <summary>Takes the animation's program, when there is memory for it.</summary>
    /// <param name="file">The file.</param>
    /// <returns>Whether the program was loaded.</returns>
    // orig: FINAL 0100:bcd3 read_pan_script (src/c/final/final_0100_bcd3.c) matched; also GAME 00f7:c4b1; tests: LibraryAnimationTests
    public bool ReadPanScript(PanFile file)
    {
        const int unit = 16;    // the file gives the program's size in units of 16 bytes
        // C: the free memory and the size are compared as unsigned numbers
        if ((ushort)world.Graphics.ImageMemoryFree() < file.Program.Length / unit)
        {
            pan.PanError = 0;
            return false;
        }
        pan.PanSegment = (byte[])file.Program.Clone();
        return true;
    }

    /// <summary>Puts a picture of the file on a page row by row, its corner in the page's corner.</summary>
    /// <param name="picture">The picture.</param>
    /// <param name="page">The page number.</param>
    // orig: FINAL 0100:bd49 read_pan_picture_to_page (src/c/final/final_0100_bd49.c) matched; also GAME 00f7:c527; tests: LibraryAnimationTests
    public void ReadPanPictureToPage(PictureFile picture, int page)
    {
        var row = new byte[picture.Width];
        for (var y = 0; y < picture.Height; y++)
        {
            picture.Pixels.AsSpan(y * picture.Width, picture.Width).CopyTo(row);
            world.Graphics.PutPicLine(row, page, y, 0, picture.Width);
        }
    }

    /// <summary>Makes a picture of the file into an image of the graphics driver, row by row.</summary>
    /// <param name="picture">The picture.</param>
    /// <returns>The image; null when the driver has no memory for it.</returns>
    // orig: FINAL 0100:bdc4 read_pan_image (src/c/final/final_0100_bdc4.c) matched; also GAME 00f7:c5a2; tests: LibraryAnimationTests
    public Core.Graphics.Image? ReadPanImage(PictureFile picture)
    {
        world.Graphics.BeginImageBuild(picture.Width, picture.Height);
        var row = new byte[picture.Width];
        for (var y = 0; y < picture.Height; y++)
        {
            picture.Pixels.AsSpan(y * picture.Width, picture.Width).CopyTo(row);
            world.Graphics.AddImageRow(row);
        }
        return world.Graphics.FinishImageBuild();
    }

    // ---- the program's bytes, its stack, its variables ----

    /// <summary>A byte of the running program, counted from where the program stands.</summary>
    private byte ScriptByte(int ahead) => ProgramByte(pan.ScriptSegment, (ushort)(pan.ScriptPc + ahead));

    /// <summary>A word of the running program, counted from where the program stands.</summary>
    private short ScriptWord(int ahead) => (short)(ScriptByte(ahead) | ScriptByte(ahead + 1) << 8);

    private static byte SpriteByte(PanSprite sprite, int ahead) => ProgramByte(sprite.PcSegment, (ushort)(sprite.Pc + ahead));

    private static short SpriteWord(PanSprite sprite, int ahead) => (short)(SpriteByte(sprite, ahead) | SpriteByte(sprite, ahead + 1) << 8);

    private static byte ProgramByte(byte[]? program, int at) =>
        program is not null && at < program.Length
            ? program[at]
            : throw new MemoryOverrunException($"the animation's program is read at byte {at}, outside what was loaded; the original reads whatever memory lies there");

    /// <summary>A number of the program's stack, counted from its top: 0 is the place the next number goes to, -1 the top number.</summary>
    private short Stack(int fromTop) => pan.ScriptStack[StackIndex(fromTop)];

    private void SetStack(int fromTop, short value) => pan.ScriptStack[StackIndex(fromTop)] = value;

    private int StackIndex(int fromTop)
    {
        var index = pan.ScriptSp + fromTop;
        return index is >= 0 and < PanEngineState.StackSize
            ? index
            : throw new MemoryOverrunException($"the animation's program uses place {index} of its stack of {PanEngineState.StackSize} numbers; the original reads and writes what lies beside the stack");
    }

    private short ScriptVar(int index) =>
        index is >= 0 and < PanEngineState.ScriptVarCount
            ? pan.ScriptVars[index]
            : throw new MemoryOverrunException($"the animation's program reads its variable {index}; the original has {PanEngineState.ScriptVarCount} and reads what lies beside them");

    /// <summary>A sound the program asked for in this frame.</summary>
    private short Queued(int index) => pan.SoundQueue[QueueIndex(index)];

    private void Queue(int index, short sound) => pan.SoundQueue[QueueIndex(index)] = sound;

    // BUG: CA-10 the original has no limit here: a fifth sound in one frame is written into the first sprite record
    private static int QueueIndex(int index) =>
        index is >= 0 and < PanEngineState.SoundQueueSize
            ? index
            : throw new MemoryOverrunException($"the animation's program asks for sound {index + 1} in one frame; the original has room for {PanEngineState.SoundQueueSize} and writes over the sprites behind them");

    /// <summary>The image a sprite shows.</summary>
    private Core.Graphics.Image? ImageOf(PanSprite sprite) =>
        sprite.Image < PanEngineState.ImagePlaces
            ? pan.SpriteImages[sprite.Image]
            : throw new MemoryOverrunException($"a sprite shows image {sprite.Image}; the original has {PanEngineState.ImagePlaces} and takes what lies behind them for an image");

    /// <summary>A sprite's place as its drawing call gets it.</summary>
    private static int Position(int? place) =>
        place ?? throw new UninitialisedReadException("the place of a sprite whose parent is not on the stage, before any sprite was placed in this pass");

    // DIVERGENCE: D-15 where the original's interpreter would turn for ever, the rewrite stops with an exception.
    private static InvalidOperationException Hangs(string what) =>
        new($"the animation player meets {what}, on which the original hangs");
}
