using CovertAction.Core.Graphics;

namespace CovertAction.Core.Animation;

/// <summary>
/// What a caller tells the animation player about the animation to play: the file, where on the screen, how long.
/// The menus and the campaign each keep two of these records, one for the animation beside a message box and one
/// for a scene of their own.
/// </summary>
// orig: struct anim_params, 14 bytes (src/c/include/final.h); FINAL DS:5e32 pan_params, GAME DS:55e8
public sealed class AnimationParams
{
    /// <summary>The value that leaves a setting as the file has it.</summary>
    public const short Keep = -1;

    /// <summary>The animation's file name (the original keeps a pointer to the name).</summary>
    public string File { get; set; } = "";

    /// <summary>The record's second word; nothing reads it.</summary>
    public short U02 { get; set; }

    /// <summary>The screen value handed to <c>set_animation_screen</c> when the animation starts.</summary>
    public short Screen { get; set; }

    /// <summary>How long the animation may run, in units the player turns into frames; -1 for "until its program ends".</summary>
    public short Speed { get; set; }

    /// <summary>Left edge of the animation on the screen in pixels; -1 keeps the file's.</summary>
    public short X { get; set; }

    /// <summary>Top edge in pixels; -1 keeps the file's.</summary>
    public short Y { get; set; }

    /// <summary>The pause between two frames in ticks of the PC's clock (18.2 a second); -1 keeps the file's.</summary>
    public short FrameLength { get; set; }
}

/// <summary>
/// One of the 50 sprites of a running animation: a small image that the animation's program has started and that
/// then runs a little program of its own (show this image, move there, repeat) at its own speed.
/// </summary>
// orig: struct F, the sprite record of 0x34 bytes (src/c/final/final_0100_b7c6.c); 51 of them from offset 0x7d8 of the far data segment (frame 0fb4 in FINAL, 10e2 in GAME)
public sealed class PanSprite
{
    /// <summary>The image number that means "no image yet".</summary>
    public const byte NoImage = 0xff;

    /// <summary>The parent value of a sprite that has none.</summary>
    public const short NoParent = -2;

    /// <summary>How many loops of a sprite's program can be open at once.</summary>
    public const int LoopLevels = 10;

    /// <summary>+0x00: whether the sprite is in use.</summary>
    public bool Active { get; set; }

    /// <summary>+0x02: set by the animation's program for one frame: the sprite is then drawn onto the background for good instead of over it.</summary>
    public bool Stamp { get; set; }

    /// <summary>+0x04: the sprite this one is placed relative to, counted from 0, or <see cref="NoParent"/>.</summary>
    public short Parent { get; set; }

    /// <summary>+0x06: where the sprite starts: its left edge, or its distance from the parent's.</summary>
    public short OffsetX { get; set; }

    /// <summary>+0x08: the same for the top edge.</summary>
    public short OffsetY { get; set; }

    /// <summary>+0x0a: where the sprite is now (for a sprite with a parent: how far it has moved from its start).</summary>
    public short X { get; set; }

    /// <summary>+0x0c: the same, down the screen.</summary>
    public short Y { get; set; }

    /// <summary>+0x0e: the speed the sprite was started with.</summary>
    public short StartSpeed { get; set; }

    /// <summary>+0x10: the speed now: what is added to <see cref="Credit"/> every frame.</summary>
    public short Speed { get; set; }

    /// <summary>+0x12: the speed added up; the sprite's program takes a step whenever it passes 255.</summary>
    public short Credit { get; set; }

    /// <summary>+0x14: how often each open loop of the sprite's program is still to run.</summary>
    public short[] LoopCounters { get; } = new short[LoopLevels];

    /// <summary>+0x28: how many loops are open.</summary>
    public short LoopDepth { get; set; }

    /// <summary>+0x2a: where the sprite's program starts in the animation's program.</summary>
    public ushort ProgramStart { get; set; }

    /// <summary>+0x2c: not 0 for a sprite that leaves no trace to clean up (it is drawn without being tracked).</summary>
    public short Untracked { get; set; }

    /// <summary>+0x2e: where the sprite's program stands.</summary>
    public ushort Pc { get; set; }

    /// <summary>+0x30: the program the sprite runs (the original keeps the memory segment the program was loaded into).</summary>
    public byte[]? PcSegment { get; set; }

    /// <summary>+0x32: the number of the image the sprite shows, <see cref="NoImage"/> before its program has shown one.</summary>
    public byte Image { get; set; }
}

/// <summary>
/// The variables of the animation player: the machine that plays a .PAN file (the briefing room, a building's
/// front, an arrest). The menus (FINAL) and the campaign (GAME) carry the same player, each with its own copy of
/// these variables; the other parts have empty functions in its place.
/// </summary>
/// <remarks>
/// <para>
/// An animation is a picture, up to 250 small images and a program. The program works on a stack of numbers and a
/// few variables the game can set; it starts and stops sprites, asks for sounds and says when a frame is over.
/// </para>
/// <para>
/// Names are FINAL's (the reconstructed final.h, symbols/names.txt), in C# capitalisation; the places are in
/// <c>Persistence.Layout.PanEngineLayout</c>. The original keeps the program's stack, the sounds asked for and the
/// sprites in a data segment of their own (FINAL 0fb4, GAME 10e2), and the loaded program in memory it asks DOS
/// for; here they are arrays.
/// </para>
/// </remarks>
// orig: FINAL DS:4ad8..4b45, DS:5e20..5e53 and the far segment 0fb4; GAME DS:4248..42b5, DS:55d6..5609 and the far segment 10e2
public sealed class PanEngineState
{
    /// <summary>A player whose five port pointers point at their records, as in the programs' initialised data.</summary>
    /// <param name="images">The register the part's images are known by; null for one of the player's own.</param>
    public PanEngineState(ImageRegister? images = null)
    {
        Images = images ?? new ImageRegister();
        PanPort = PanPortX;
        PanWorkPort = PanWorkPortX;
        PanScreenCopy = PanScreenCopyX;
        PanSavedScreen = PanSavedScreenX;
        PanBackground = PanBackgroundX;
    }

    /// <summary>Sprites an animation can have at once; they are numbered from 1.</summary>
    public const int SpriteCount = 50;

    /// <summary>Images an animation file can bring.</summary>
    public const int ImagePlaces = 250;

    /// <summary>Numbers the program's stack can hold.</summary>
    public const int StackSize = 1000;

    /// <summary>Sounds the program can ask for in one frame.</summary>
    public const int SoundQueueSize = 4;

    /// <summary>The variables the game and the program share.</summary>
    public const int ScriptVarCount = 51;

    /// <summary>The variable in which the player tells the program that a key was pressed and not taken as "skip" (it gets -1).</summary>
    public const int KeyPressedVariable = 6;

    /// <summary>The largest palette block a file can bring.</summary>
    public const int PaletteBlockSize = 0x306;

    /// <summary>The register the animation's images are known by where the state is written as the original's bytes.</summary>
    public ImageRegister Images { get; }

    // ---- the pages ----

    /// <summary>pan_portX: the port of the page the player shows (page 0, or 0 and 1 in turn where the display can flip pages).</summary>
    public RasterPort PanPortX { get; } = new();

    /// <summary>pan_work_portX: the port of the page that holds the clean background (page 2).</summary>
    public RasterPort PanWorkPortX { get; } = new();

    /// <summary>pan_screen_copyX: the port the saved screen is put back to (page 0).</summary>
    public RasterPort PanScreenCopyX { get; } = new();

    /// <summary>pan_saved_screenX: the port of the saved screen (page 1).</summary>
    public RasterPort PanSavedScreenX { get; } = new();

    /// <summary>pan_backgroundX: the port the background is first copied from (page 2).</summary>
    public RasterPort PanBackgroundX { get; } = new();

    /// <summary>pan_port: the port of the visible page; always <see cref="PanPortX"/> in the running game.</summary>
    public RasterPort PanPort { get; set; }

    /// <summary>pan_work_port: the port of the work page.</summary>
    public RasterPort PanWorkPort { get; set; }

    /// <summary>pan_screen_copy.</summary>
    public RasterPort PanScreenCopy { get; set; }

    /// <summary>pan_saved_screen.</summary>
    public RasterPort PanSavedScreen { get; set; }

    /// <summary>pan_background.</summary>
    public RasterPort PanBackground { get; set; }

    /// <summary>animation_screen: the screen value of the running animation; 0 when none runs.</summary>
    public short AnimationScreen { get; set; }

    /// <summary>page_state: what the end of a frame still has to do to the pages: 2 copy the background to both, 1 the frame after that, 0 only clean up behind the sprites.</summary>
    public short PageState { get; set; }

    // ---- pacing ----

    /// <summary>frame_parity: flips at every call of the frame timer; one call takes the time, the next waits for it.</summary>
    public short FrameParity { get; set; }

    /// <summary>frame_deadline: the clock tick the next frame is due at.</summary>
    public int FrameDeadline { get; set; }

    /// <summary>frame_length: the pause between two frames in ticks of the PC's clock.</summary>
    public short FrameLength { get; set; }

    /// <summary>frame_budget: the steps the animation may still run; -1 at the start for "until its program ends".</summary>
    public short FrameBudget { get; set; }

    /// <summary>animation_steps: steps run so far; a key skips the animation only from the third.</summary>
    public short AnimationSteps { get; set; }

    /// <summary>frame_counter: frames played since the animation started.</summary>
    public short FrameCounter { get; set; }

    /// <summary>ignore_skip: set by the game to have the next skipping key ignored.</summary>
    public short IgnoreSkip { get; set; }

    // ---- the caller's request ----

    /// <summary>pan_params: the player's own request record, filled by <c>play_pan_file</c>.</summary>
    public AnimationParams PanParams { get; } = new();

    /// <summary>animation_ptr: the request of the running animation.</summary>
    public AnimationParams? AnimationPtr { get; set; }

    // ---- the program ----

    /// <summary>pan_segment: the loaded animation's program (the original keeps the segment of the memory it lies in).</summary>
    public byte[]? PanSegment { get; set; }

    /// <summary>script_segment: the program that is running (set from <see cref="PanSegment"/> when an animation starts).</summary>
    public byte[]? ScriptSegment { get; set; }

    /// <summary>script_pc: where the program stands.</summary>
    public ushort ScriptPc { get; set; }

    /// <summary>script_sp: how many numbers are on the program's stack.</summary>
    public short ScriptSp { get; set; }

    /// <summary>script_state: 0 while the program runs, 1 while it waits.</summary>
    public short ScriptState { get; set; }

    /// <summary>script_wait: frames the program still waits; the interpreter also uses it as a working value.</summary>
    public short ScriptWait { get; set; }

    /// <summary>script_opcode: the instruction being carried out.</summary>
    public short ScriptOpcode { get; set; }

    /// <summary>script_done: whether the program has ended.</summary>
    public bool ScriptDone { get; set; }

    /// <summary>
    /// script_vars: values the game hands to the program (which scene to play) and the program hands back. Variable
    /// <see cref="KeyPressedVariable"/> is the player's own message to the program (the headers call that word skip_key_state).
    /// </summary>
    public short[] ScriptVars { get; } = new short[ScriptVarCount];

    /// <summary>sound_queue_count: sounds the program has asked for in this frame.</summary>
    public short SoundQueueCount { get; set; }

    /// <summary>The program's stack of numbers (far segment +0).</summary>
    public short[] ScriptStack { get; } = new short[StackSize];

    /// <summary>The sounds asked for in this frame (far segment +0x7d0).</summary>
    public short[] SoundQueue { get; } = new short[SoundQueueSize];

    /// <summary>The sprites by their numbers 1..50 (far segment +0x7d8, 0x34 bytes each); record 0 is never used.</summary>
    public PanSprite[] Sprites { get; } = [.. Enumerable.Range(0, SpriteCount + 1).Select(_ => new PanSprite())];

    /// <summary>sprite_images: the animation's images by their numbers; null where the file has none.</summary>
    public Image?[] SpriteImages { get; } = new Image?[ImagePlaces];

    // ---- the file being loaded ----

    /// <summary>pan_magic: the mark an animation file starts with ("PANI").</summary>
    public string PanMagic { get; set; } = "";

    /// <summary>pan_version: the version an animation file must have (3).</summary>
    public short PanVersion { get; set; }

    /// <summary>pan_file_magic: the mark of the file last loaded.</summary>
    public string PanFileMagic { get; set; } = "";

    /// <summary>pan_file_version: the version of the file last loaded.</summary>
    public short PanFileVersion { get; set; }

    /// <summary>pan_packed: whether the pictures of the file last loaded are packed (they all are).</summary>
    public short PanPacked { get; set; }

    /// <summary>pan_handle: the number the file was read with.</summary>
    public short PanHandle { get; set; }

    /// <summary>pan_error: why the last load failed: 1 not an animation file, 2 another version, 0 no memory.</summary>
    public short PanError { get; set; }

    /// <summary>pan_has_palette: whether the file last loaded brought a palette.</summary>
    public short PanHasPalette { get; set; }

    /// <summary>pan_palette_type: the kind of that palette (0: 16 colour numbers and the border colour).</summary>
    public short PanPaletteType { get; set; }

    /// <summary>pan_palette: the palette block of the file last loaded.</summary>
    public byte[] PanPalette { get; } = new byte[PaletteBlockSize];
}
