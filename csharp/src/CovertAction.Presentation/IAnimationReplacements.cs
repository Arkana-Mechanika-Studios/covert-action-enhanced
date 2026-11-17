using CovertAction.Content;

namespace CovertAction.Presentation;

/// <summary>
/// Animations shown in place of the game's own (D-683, new code, outside the game): a player's option of the launcher, the
/// Amiga version's animations read from the player's own Amiga files. The animation loader asks it after it has read the
/// game's animation file, with the file's name and what it read; the answer, when there is one, is the animation the loader
/// goes on with. The game's file is read, and its drivers called, exactly as without it.
/// </summary>
public interface IAnimationReplacements
{
    /// <summary>The animation to play in place of the game's animation file of that name, or null for the game's own.</summary>
    /// <param name="fileName">The animation's file name as the game asks for it ("injail.pan").</param>
    /// <param name="own">The game's animation, as read from its file.</param>
    /// <returns>The replacement (the game's own with other images, background and, for some, program: <see cref="PanFile.With"/>), or null.</returns>
    PanFile? Find(string fileName, PanFile own);
}
