using CovertAction.Core.Shared;

namespace CovertAction.Core.Menus;

/// <summary>
/// The variables of the picture animations (.pan files) that the campaign and the menus play beside a message box:
/// which file, where on the screen, and how much of the screen is kept for it. The box routine is the same in every
/// part, so the cryptography part carries these variables too, although nothing in it starts an animation and its
/// animation player is compiled out (empty functions).
/// </summary>
/// <remarks>Names as in the reconstructed code.h. Fields whose purpose is not known keep a name made from their address.</remarks>
// orig: CODE DS:116c..DS:11bb, DS:1af8..DS:1b05, DS:1d7e, DS:1d94, DS:1db0, DS:1dbc (src/c/include/code.h)
public sealed class AnimationState
{
    /// <summary>
    /// CODE DS:116c, GAME's <c>cached_city</c> (DS:42fa): the city whose picture the picture page holds, -1 for none.
    /// The box sets it to -1 once it has started the animation beside it, which draws over that page; only GAME reads
    /// it (city_picture, GAME 00f7:3cfd, loads the picture again). The other parts' copies (FINAL DS:47bc, BUG DS:2df2,
    /// CHASE DS:1912) are written by the box and read by nothing.
    /// </summary>
    public short CachedCity { get; set; }

    /// <summary>CODE DS:116e: the current DOS drive (1 = A), -1 until it has been asked for; on a floppy (1 or 2) the box shows "One moment please...".</summary>
    public short CurrentDrive { get; set; }

    /// <summary>CODE DS:1170: width in pixels of the left part of the screen that is saved and cleared around an animation (320 at start).</summary>
    public short SaveWidth { get; set; }

    /// <summary>CODE DS:1174: the palette set while an animation runs: 16 colours and the border colour.</summary>
    public byte[] EgaPaletteAnim { get; } = new byte[17];

    /// <summary>CODE DS:1186: the alternative palette for some backdrop faces.</summary>
    public byte[] EgaPaletteAnimAlt { get; } = new byte[17];

    /// <summary>CODE DS:119a: the file name of the briefing animation, the one animation that needs no disk check.</summary>
    public string BriefingPanName { get; set; } = "";

    /// <summary>CODE DS:11a7: the three letters the building animations' file names begin with.</summary>
    public string BldPrefix { get; set; } = "";

    /// <summary>CODE DS:11ac: set when the animation is a building animation; then a 160x30 corner of the screen is saved once more.</summary>
    public bool BldAnimationFlag { get; set; }

    /// <summary>
    /// CODE DS:11ae: the name of the animation's file, at most 13 characters, in its 14-byte buffer: start_animation
    /// copies each name over the last (strcpy), so a shorter name leaves the end of a longer one behind its end mark.
    /// </summary>
    public TextBuffer AnimationFile { get; } = new(AnimationFileBytes);

    /// <summary>The size of <see cref="AnimationFile"/> in every program (the joystick's direction table follows it).</summary>
    public const int AnimationFileBytes = 14;

    /// <summary>CODE DS:1af8: whether the animation record names its file (it does once an animation has been started).</summary>
    public bool AnimationRecordNamed { get; set; }

    /// <summary>
    /// CODE DS:1afe: the animation record's speed (+06, <c>speed</c> of FINAL's <c>struct anim_params</c>), set to -1
    /// (no frame budget: the animation runs on) when an animation starts. The player reads it at the start (FINAL 0100:ac84;
    /// CODE leaves the player out).
    /// </summary>
    public short AnimationSpeed { get; set; }

    /// <summary>CODE DS:1b00: left edge of the animation on the screen, in pixels.</summary>
    public short AnimationX { get; set; }

    /// <summary>CODE DS:1b02: top edge of the animation on the screen, in pixels.</summary>
    public short AnimationY { get; set; }

    /// <summary>
    /// CODE DS:1b04: the animation record's frame length (+0c, <c>frame_length</c> of FINAL's <c>struct anim_params</c>:
    /// timer ticks a frame), set to -1 (keep the current length) when an animation starts. The player reads it at
    /// the start (FINAL 0100:ac84; CODE leaves the player out).
    /// </summary>
    public short AnimationFrameLength { get; set; }

    /// <summary>CODE DS:1d7e: the face to draw behind the box, -1 for none; cleared by every box.</summary>
    public short BackdropCode { get; set; }

    /// <summary>CODE DS:1d94: whether an animation runs.</summary>
    public bool AnimationActive { get; set; }

    /// <summary>CODE DS:1db0: second argument for the backdrop face.</summary>
    public short BackdropArgB { get; set; }

    /// <summary>CODE DS:1dbc: third argument for the backdrop face.</summary>
    public short BackdropArgC { get; set; }
}
