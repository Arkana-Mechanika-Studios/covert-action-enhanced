using CovertAction.Core.Graphics;
using CovertAction.Core.Machine;
using CovertAction.Core.Menus;

namespace CovertAction.Core.Shared;

/// <summary>
/// The variables every part of the game has for the routines every part has: the original's programs were linked
/// from one library of game routines (text and box drawing, the message box, key and joystick reading, the
/// insert-disk prompt), and each program carries its own copy of the library's variables among its own. A part's
/// state implements this interface, so that the one C# implementation of a library routine works on any part.
/// </summary>
/// <remarks>
/// New code. The members are named after what the library does with them; a part's own name for a variable (the
/// cryptography part's <c>CODEstatus</c>) stays in that part's state.
/// </remarks>
public interface ILibraryState
{
    /// <summary>The drawing port of the screen.</summary>
    RasterPort Rp1 { get; }

    /// <summary>The drawing port of the page the screen is saved on while a box is up.</summary>
    RasterPort RpBack { get; }

    /// <summary>The player's options as the part holds them (sound, animation, joystick ...); the message box's Alt keys switch them.</summary>
    Options Status { get; set; }

    /// <summary>What the part's main loop should do after a box: 0 to go on, another value to leave (-1: leave the game).</summary>
    short Quit { get; set; }

    /// <summary>The first of the two colour tables the part hands to the graphics driver.</summary>
    byte[] ColorMapA { get; }

    /// <summary>The second colour table.</summary>
    byte[] ColorMapB { get; }

    /// <summary>The part's text buffer, in which the insert-disk prompt builds its text.</summary>
    TextBuffer MessageText { get; }

    /// <summary>The variables of the message box.</summary>
    MenuState Menu { get; }

    /// <summary>The variables of the animation beside a box.</summary>
    AnimationState Animation { get; }

    /// <summary>The variables of the insert-disk prompt.</summary>
    DiskPromptState Disk { get; }

    /// <summary>The variables of the joystick and mouse routines.</summary>
    InputState Input { get; }
}
