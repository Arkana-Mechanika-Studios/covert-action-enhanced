using System.Buffers.Binary;
using CovertAction.Core.Animation;
using CovertAction.Core.Graphics;

namespace CovertAction.Persistence.Layout;

/// <summary>
/// Where the menus program (FINAL.EXE) and the campaign program (GAME.EXE) keep the variables of the animation
/// player (<see cref="PanEngineState"/>): one table of places per program in its data segment, and the layout of the
/// far data segment both keep the program's stack, the sounds asked for and the sprites in.
/// </summary>
/// <remarks>
/// The places were found by pairing the 41 routines of the player in the two programs (FINAL 0100:abd2 to bdc4, GAME
/// 00f7:b3b0 to c5a2; docs/source-map/shared-functions.csv puts each pair in one group) instruction by instruction.
/// Not placed, because the rewrite has no counterpart: the two words that hold memory segments (pan_segment and
/// script_segment: here the program is an array the state refers to), the pointer to the file's name in a request
/// record (here the name itself) and the pointer to the running request (here a reference), the row buffer and the name
/// pointer of the loader.
/// </remarks>
public static class PanEngineLayout
{
    /// <summary>The places of one program's copies.</summary>
    private sealed record Addresses(
        int PanPortX, int PanWorkPortX, int PanPort, int PanWorkPort, int PanScreenCopyX, int PanSavedScreenX, int PanBackgroundX,
        int PanScreenCopy, int PanSavedScreen, int PanBackground, int AnimationScreen, int FrameParity, int PanMagic, int PanVersion,
        int FrameBudget, int PageState, int ScriptState, int AnimationPtr, int ScriptPc, int ScriptSegment, int ScriptSp, int AnimationSteps,
        int SoundQueueCount, int PanParams, int FrameDeadline, int ScriptOpcode, int ScriptWait, int PanHandle, int PanFileVersion,
        int PanPacked, int PanFileMagic, int PanRowBuffer, int FrameLength, int PanHasPalette, int PanPalette, int PanSegment,
        int FrameCounter, int PanError, int ScriptDone, int SpriteImages, int FileNamePointer, int IgnoreSkip, int PanPaletteType,
        int ScriptVars, int SpanTracker);

    // orig: FINAL DGROUP, the animation player's variables (src/c/include/final.h, docs/data-model/final.md section 4)
    private static readonly Addresses Final = new(
        PanPortX: 0x4ad8, PanWorkPortX: 0x4aea, PanPort: 0x4afc, PanWorkPort: 0x4afe, PanScreenCopyX: 0x4b00, PanSavedScreenX: 0x4b12,
        PanBackgroundX: 0x4b24, PanScreenCopy: 0x4b36, PanSavedScreen: 0x4b38, PanBackground: 0x4b3a, AnimationScreen: 0x4b3c,
        FrameParity: 0x4b3e, PanMagic: 0x4b40, PanVersion: 0x5bba, FrameBudget: 0x5e20, PageState: 0x5e22, ScriptState: 0x5e24,
        AnimationPtr: 0x5e26, ScriptPc: 0x5e28, ScriptSegment: 0x5e2a, ScriptSp: 0x5e2c, AnimationSteps: 0x5e2e, SoundQueueCount: 0x5e30,
        PanParams: 0x5e32, FrameDeadline: 0x5e40, ScriptOpcode: 0x5e44, ScriptWait: 0x5e46, PanHandle: 0x5e48, PanFileVersion: 0x5e4a,
        PanPacked: 0x5e4c, PanFileMagic: 0x5e4e, PanRowBuffer: 0x5e54, FrameLength: 0x96d4, PanHasPalette: 0x96de, PanPalette: 0x98e2,
        PanSegment: 0x9e80, FrameCounter: 0x9e84, PanError: 0x9f9e, ScriptDone: 0xa5a8, SpriteImages: 0xa9fe, FileNamePointer: 0xabf4,
        IgnoreSkip: 0xae30, PanPaletteType: 0xb30a, ScriptVars: 0xb30e, SpanTracker: 0x501a);

    // orig: GAME DGROUP, the animation player's variables (src/c/include/game.h)
    private static readonly Addresses Game = new(
        PanPortX: 0x4248, PanWorkPortX: 0x425a, PanPort: 0x426c, PanWorkPort: 0x426e, PanScreenCopyX: 0x4270, PanSavedScreenX: 0x4282,
        PanBackgroundX: 0x4294, PanScreenCopy: 0x42a6, PanSavedScreen: 0x42a8, PanBackground: 0x42aa, AnimationScreen: 0x42ac,
        FrameParity: 0x42ae, PanMagic: 0x42b0, PanVersion: 0x53ce, FrameBudget: 0x55d6, PageState: 0x55d8, ScriptState: 0x55da,
        AnimationPtr: 0x55dc, ScriptPc: 0x55de, ScriptSegment: 0x55e0, ScriptSp: 0x55e2, AnimationSteps: 0x55e4, SoundQueueCount: 0x55e6,
        PanParams: 0x55e8, FrameDeadline: 0x55f6, ScriptOpcode: 0x55fa, ScriptWait: 0x55fc, PanHandle: 0x55fe, PanFileVersion: 0x5600,
        PanPacked: 0x5602, PanFileMagic: 0x5604, PanRowBuffer: 0x560a, FrameLength: 0x8ed2, PanHasPalette: 0x8ee0, PanPalette: 0x90e4,
        PanSegment: 0x9680, FrameCounter: 0x9684, PanError: 0x979e, ScriptDone: 0x9da6, SpriteImages: 0xa1f4, FileNamePointer: 0xa3ea,
        IgnoreSkip: 0xa622, PanPaletteType: 0xa9f0, ScriptVars: 0xa9f2, SpanTracker: 0x4830);

    /// <summary>The layout of a request record (<c>struct anim_params</c>) without its first word, the pointer to the file's name.</summary>
    // orig: struct anim_params (src/c/include/final.h)
    public static StateLayout<AnimationParams> Params { get; } = new StateLayout<AnimationParams>("anim_params")
        .Int16(0x02, "+0x02", p => p.U02, (p, v) => p.U02 = v)
        .Int16(0x04, "+0x04 screen", p => p.Screen, (p, v) => p.Screen = v)
        .Int16(0x06, "+0x06 speed", p => p.Speed, (p, v) => p.Speed = v)
        .Int16(0x08, "+0x08 x", p => p.X, (p, v) => p.X = v)
        .Int16(0x0a, "+0x0a y", p => p.Y, (p, v) => p.Y = v)
        .Int16(0x0c, "+0x0c frame_length", p => p.FrameLength, (p, v) => p.FrameLength = v);

    /// <summary>The size of a request record in bytes.</summary>
    public const int ParamsSize = 14;

    /// <summary>Adds FINAL's copies of the animation player's variables to a layout of FINAL's data segment.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    /// <param name="pan">The player's variables in the program's state.</param>
    public static StateLayout<TState> AddFinal<TState>(StateLayout<TState> layout, Func<TState, PanEngineState> pan) => Add(layout, Final, pan);

    /// <summary>Adds GAME's copies of the animation player's variables to a layout of GAME's data segment.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    /// <param name="pan">The player's variables in the program's state.</param>
    public static StateLayout<TState> AddGame<TState>(StateLayout<TState> layout, Func<TState, PanEngineState> pan) => Add(layout, Game, pan);

    /// <summary>FINAL's places of what the layout leaves out, by name: see <see cref="Places"/>.</summary>
    public static Places FinalPlaces { get; } = PlacesOf(Final);

    /// <summary>GAME's places of what the layout leaves out.</summary>
    public static Places GamePlaces { get; } = PlacesOf(Game);

    /// <summary>
    /// The places of the player's variables that have no field in the layout, for the tests that compare them by
    /// hand, and the ranges of working memory nothing compares.
    /// </summary>
    /// <param name="PanSegment">The word with the memory segment of the loaded program.</param>
    /// <param name="ScriptSegment">The word with the memory segment of the running program.</param>
    /// <param name="PanParams">The player's own request record (its first word is the pointer to the file's name).</param>
    /// <param name="AnimationPtr">The word with the address of the running animation's request record.</param>
    /// <param name="FarSegmentFrame">The far data segment of the stack and the sprites, in paragraphs from the program's load address.</param>
    /// <param name="Scratch">Working memory: the loader's row buffer and name pointer, and what MicroProse's library in front of the graphics driver remembers of the tracked draws.</param>
    public sealed record Places(int PanSegment, int ScriptSegment, int PanParams, int AnimationPtr, int FarSegmentFrame, IReadOnlyList<(int Start, int End)> Scratch);

    private const int RowBufferBytes = 320;

    /// <summary>The tracker's variables: four words of first and last rows, four table addresses, and four tables of a word per screen row.</summary>
    private const int SpanTrackerBytes = 16 + 4 * 2 * 200;

    private static Places PlacesOf(Addresses at) => new(at.PanSegment, at.ScriptSegment, at.PanParams, at.AnimationPtr, ReferenceEquals(at, Final) ? FinalFarFrame : GameFarFrame,
    [
        (at.PanRowBuffer, at.PanRowBuffer + RowBufferBytes),
        (at.FileNamePointer, at.FileNamePointer + 2),
        (at.SpanTracker, at.SpanTracker + SpanTrackerBytes),
    ]);

    // the player's far data segment, FINAL 0fb4:0000 and GAME 10e2:0000 (FINAL 0100:b01a, struct W)
    private const int FinalFarFrame = 0x0fb4;
    private const int GameFarFrame = 0x10e2;

    private static StateLayout<TState> Add<TState>(StateLayout<TState> layout, Addresses at, Func<TState, PanEngineState> pan)
    {
        string Name(int address, string name) => $"DS:{address:x4} {name}";

        void Word(int address, string name, Func<PanEngineState, short> get, Action<PanEngineState, short> set) =>
            layout.Int16(address, Name(address, name), s => get(pan(s)), (s, v) => set(pan(s), v));

        void Port(int address, string name, Func<PanEngineState, RasterPort> port) =>
            layout.Part(address, Name(address, name), RasterPortLayout.Layout, s => port(pan(s)));

        (int Address, Func<TState, RasterPort> Target)[] ports =
        [
            (at.PanPortX, s => pan(s).PanPortX), (at.PanWorkPortX, s => pan(s).PanWorkPortX), (at.PanScreenCopyX, s => pan(s).PanScreenCopyX),
            (at.PanSavedScreenX, s => pan(s).PanSavedScreenX), (at.PanBackgroundX, s => pan(s).PanBackgroundX),
        ];

        void PortPointer(int address, string name, Func<PanEngineState, RasterPort> get, Action<PanEngineState, RasterPort> set) =>
            layout.Pointer(address, Name(address, name), s => get(pan(s)), (s, v) => set(pan(s), v), ports);

        // ---- the pages ----
        Port(at.PanPortX, "pan_portX", p => p.PanPortX);
        Port(at.PanWorkPortX, "pan_work_portX", p => p.PanWorkPortX);
        Port(at.PanScreenCopyX, "pan_screen_copyX", p => p.PanScreenCopyX);
        Port(at.PanSavedScreenX, "pan_saved_screenX", p => p.PanSavedScreenX);
        Port(at.PanBackgroundX, "pan_backgroundX", p => p.PanBackgroundX);
        PortPointer(at.PanPort, "pan_port", p => p.PanPort, (p, v) => p.PanPort = v);
        PortPointer(at.PanWorkPort, "pan_work_port", p => p.PanWorkPort, (p, v) => p.PanWorkPort = v);
        PortPointer(at.PanScreenCopy, "pan_screen_copy", p => p.PanScreenCopy, (p, v) => p.PanScreenCopy = v);
        PortPointer(at.PanSavedScreen, "pan_saved_screen", p => p.PanSavedScreen, (p, v) => p.PanSavedScreen = v);
        PortPointer(at.PanBackground, "pan_background", p => p.PanBackground, (p, v) => p.PanBackground = v);
        Word(at.AnimationScreen, "animation_screen", p => p.AnimationScreen, (p, v) => p.AnimationScreen = v);
        Word(at.PageState, "page_state", p => p.PageState, (p, v) => p.PageState = v);

        // ---- pacing ----
        Word(at.FrameParity, "frame_parity", p => p.FrameParity, (p, v) => p.FrameParity = v);
        layout.UInt32(at.FrameDeadline, Name(at.FrameDeadline, "frame_deadline"), s => unchecked((uint)pan(s).FrameDeadline), (s, v) => pan(s).FrameDeadline = unchecked((int)v));
        Word(at.FrameLength, "frame_length", p => p.FrameLength, (p, v) => p.FrameLength = v);
        Word(at.FrameBudget, "frame_budget", p => p.FrameBudget, (p, v) => p.FrameBudget = v);
        Word(at.AnimationSteps, "animation_steps", p => p.AnimationSteps, (p, v) => p.AnimationSteps = v);
        Word(at.FrameCounter, "frame_counter", p => p.FrameCounter, (p, v) => p.FrameCounter = v);
        Word(at.IgnoreSkip, "ignore_skip", p => p.IgnoreSkip, (p, v) => p.IgnoreSkip = v);

        // ---- the caller's request: the record without its name pointer; the pointer to the running request ----
        layout.Part(at.PanParams, Name(at.PanParams, "pan_params"), Params, s => pan(s).PanParams);

        // ---- the program ----
        layout.Int16(at.ScriptPc, Name(at.ScriptPc, "script_pc"), s => unchecked((short)pan(s).ScriptPc), (s, v) => pan(s).ScriptPc = unchecked((ushort)v));
        Word(at.ScriptSp, "script_sp", p => p.ScriptSp, (p, v) => p.ScriptSp = v);
        Word(at.ScriptState, "script_state", p => p.ScriptState, (p, v) => p.ScriptState = v);
        Word(at.ScriptWait, "script_wait", p => p.ScriptWait, (p, v) => p.ScriptWait = v);
        Word(at.ScriptOpcode, "script_opcode", p => p.ScriptOpcode, (p, v) => p.ScriptOpcode = v);
        layout.Flag(at.ScriptDone, Name(at.ScriptDone, "script_done"), s => pan(s).ScriptDone, (s, v) => pan(s).ScriptDone = v);
        layout.Int16Array(at.ScriptVars, PanEngineState.ScriptVarCount, Name(at.ScriptVars, "script_vars"), s => pan(s).ScriptVars);
        Word(at.SoundQueueCount, "sound_queue_count", p => p.SoundQueueCount, (p, v) => p.SoundQueueCount = v);
        layout.ImageArray(at.SpriteImages, PanEngineState.ImagePlaces, Name(at.SpriteImages, "sprite_images"), s => pan(s).SpriteImages, s => pan(s).Images);

        // ---- the file being loaded ----
        const int magicBytes = 5;
        layout.Text(at.PanMagic, magicBytes, Name(at.PanMagic, "pan_magic"), s => pan(s).PanMagic, (s, v) => pan(s).PanMagic = v);
        Word(at.PanVersion, "pan_version", p => p.PanVersion, (p, v) => p.PanVersion = v);
        layout.Text(at.PanFileMagic, magicBytes, Name(at.PanFileMagic, "pan_file_magic"), s => pan(s).PanFileMagic, (s, v) => pan(s).PanFileMagic = v);
        Word(at.PanFileVersion, "pan_file_version", p => p.PanFileVersion, (p, v) => p.PanFileVersion = v);
        Word(at.PanPacked, "pan_packed", p => p.PanPacked, (p, v) => p.PanPacked = v);
        Word(at.PanHandle, "pan_handle", p => p.PanHandle, (p, v) => p.PanHandle = v);
        Word(at.PanError, "pan_error", p => p.PanError, (p, v) => p.PanError = v);
        Word(at.PanHasPalette, "pan_has_palette", p => p.PanHasPalette, (p, v) => p.PanHasPalette = v);
        Word(at.PanPaletteType, "pan_palette_type", p => p.PanPaletteType, (p, v) => p.PanPaletteType = v);
        layout.Bytes(at.PanPalette, PanEngineState.PaletteBlockSize, Name(at.PanPalette, "pan_palette"), s => pan(s).PanPalette);
        return layout;
    }

    // ---- the far data segment ----

    /// <summary>The size of the player's far data: the stack, the sounds asked for, and 51 sprite records.</summary>
    public const int FarDataSize = SpritesAt + (PanEngineState.SpriteCount + 1) * SpriteSize;

    private const int SoundQueueAt = 2 * PanEngineState.StackSize;
    private const int SpritesAt = SoundQueueAt + 2 * PanEngineState.SoundQueueSize;
    private const int SpriteSize = 0x34;

    /// <summary>
    /// Reads the stack, the sounds asked for and the sprites from the original's far data segment. A sprite's program
    /// becomes <paramref name="program"/> when its record names the segment <paramref name="programSegment"/>.
    /// </summary>
    /// <param name="far">The segment's bytes from its start, at least <see cref="FarDataSize"/>.</param>
    /// <param name="state">The player's variables.</param>
    /// <param name="programSegment">The memory segment of the loaded program in the recorded session.</param>
    /// <param name="program">The loaded program.</param>
    public static void LoadFarData(ReadOnlySpan<byte> far, PanEngineState state, int programSegment, byte[]? program)
    {
        for (var i = 0; i < PanEngineState.StackSize; i++)
        {
            state.ScriptStack[i] = BinaryPrimitives.ReadInt16LittleEndian(far[(2 * i)..]);
        }
        for (var i = 0; i < PanEngineState.SoundQueueSize; i++)
        {
            state.SoundQueue[i] = BinaryPrimitives.ReadInt16LittleEndian(far[(SoundQueueAt + 2 * i)..]);
        }
        for (var n = 0; n <= PanEngineState.SpriteCount; n++)
        {
            var record = far.Slice(SpritesAt + n * SpriteSize, SpriteSize);
            var sprite = state.Sprites[n];
            sprite.Active = Word(record, 0x00) != 0;
            sprite.Stamp = Word(record, 0x02) != 0;
            sprite.Parent = Word(record, 0x04);
            sprite.OffsetX = Word(record, 0x06);
            sprite.OffsetY = Word(record, 0x08);
            sprite.X = Word(record, 0x0a);
            sprite.Y = Word(record, 0x0c);
            sprite.StartSpeed = Word(record, 0x0e);
            sprite.Speed = Word(record, 0x10);
            sprite.Credit = Word(record, 0x12);
            for (var level = 0; level < PanSprite.LoopLevels; level++)
            {
                sprite.LoopCounters[level] = Word(record, 0x14 + 2 * level);
            }
            sprite.LoopDepth = Word(record, 0x28);
            sprite.ProgramStart = unchecked((ushort)Word(record, 0x2a));
            sprite.Untracked = Word(record, 0x2c);
            sprite.Pc = unchecked((ushort)Word(record, 0x2e));
            sprite.PcSegment = unchecked((ushort)Word(record, 0x30)) == programSegment ? program : null;
            sprite.Image = record[0x32];
        }
    }

    /// <summary>
    /// Writes the stack, the sounds asked for and the sprites over the original's far data segment; a yes-or-no word
    /// is written only when the answer differs from what the bytes hold, and a sprite's program segment only when the
    /// sprite runs <paramref name="program"/>.
    /// </summary>
    /// <param name="state">The player's variables.</param>
    /// <param name="far">The segment's bytes from its start.</param>
    /// <param name="programSegment">The memory segment of the loaded program in the recorded session.</param>
    /// <param name="program">The loaded program.</param>
    public static void StoreFarData(PanEngineState state, Span<byte> far, int programSegment, byte[]? program)
    {
        for (var i = 0; i < PanEngineState.StackSize; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(far[(2 * i)..], state.ScriptStack[i]);
        }
        for (var i = 0; i < PanEngineState.SoundQueueSize; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(far[(SoundQueueAt + 2 * i)..], state.SoundQueue[i]);
        }
        for (var n = 0; n <= PanEngineState.SpriteCount; n++)
        {
            var record = far.Slice(SpritesAt + n * SpriteSize, SpriteSize);
            var sprite = state.Sprites[n];
            PutFlag(record, 0x00, sprite.Active);
            PutFlag(record, 0x02, sprite.Stamp);
            Put(record, 0x04, sprite.Parent);
            Put(record, 0x06, sprite.OffsetX);
            Put(record, 0x08, sprite.OffsetY);
            Put(record, 0x0a, sprite.X);
            Put(record, 0x0c, sprite.Y);
            Put(record, 0x0e, sprite.StartSpeed);
            Put(record, 0x10, sprite.Speed);
            Put(record, 0x12, sprite.Credit);
            for (var level = 0; level < PanSprite.LoopLevels; level++)
            {
                Put(record, 0x14 + 2 * level, sprite.LoopCounters[level]);
            }
            Put(record, 0x28, sprite.LoopDepth);
            Put(record, 0x2a, sprite.ProgramStart);
            Put(record, 0x2c, sprite.Untracked);
            Put(record, 0x2e, sprite.Pc);
            if (sprite.PcSegment is not null && ReferenceEquals(sprite.PcSegment, program))
            {
                Put(record, 0x30, programSegment);
            }
            record[0x32] = sprite.Image;
        }
    }

    private static short Word(ReadOnlySpan<byte> record, int at) => BinaryPrimitives.ReadInt16LittleEndian(record[at..]);

    private static void Put(Span<byte> record, int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(record[at..], unchecked((short)value));

    /// <summary>A yes-or-no word is written only when the answer differs from what the bytes hold.</summary>
    private static void PutFlag(Span<byte> record, int at, bool value)
    {
        if (Word(record, at) != 0 != value)
        {
            Put(record, at, value ? 1 : 0);
        }
    }
}
