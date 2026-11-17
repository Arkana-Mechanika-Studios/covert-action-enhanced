namespace CovertAction.Launcher;

/// <summary>One animation of the game that the Amiga pictures option may play with the Amiga version's images (D-683).</summary>
/// <param name="Name">The game's animation file ("injail.pan").</param>
/// <param name="File">The Amiga version's animation in the player's Amiga files.</param>
/// <param name="Reason">Why it may be replaced: what was checked.</param>
/// <param name="AmigaProgram">The animation plays the Amiga's program (translated, <see cref="Content.AmigaPanProgram"/>) instead of the game's: the Amiga's differs from the PC's (the captured dossier).</param>
public sealed record AmigaAnimationEntry(string Name, string File, string Reason, bool AmigaProgram = false);

/// <summary>
/// The animations the Amiga pictures option plays with the Amiga version's images and background, one by one (D-683; new
/// code, outside the game). The Amiga's animation files (<c>.pan</c>) hold the PC's small machine with wider fields
/// (<see cref="Content.AmigaPanFile"/>). For the 11 animations below the Amiga's program is, instruction for instruction and
/// address for address, the PC's own (<see cref="Content.AmigaPanProgram"/> turns it into exactly the PC's bytes), and the
/// images have the same numbers and sizes, the file the same size and pace: so the game's own file is read and played as ever,
/// with its program, its timing and its sound requests, and only the pixels of the images and the background are the Amiga's
/// (as the pictures of D-642 are). The other animations of the Amiga disks differ from the PC's in their program, their
/// images' sizes or their size (the briefing room, the offices, the headquarters, the communications and cryptography rooms,
/// the research room, the binoculars and the wounded agent) and stay the PC's; so do the title sequence (the Amiga's own
/// <c>tit.pan</c> is not one of the PC's three) and the building scenes' variant names that the Amiga does not have.
/// </summary>
public static class AmigaAnimationList
{
    private const string Same = "the Amiga's program is the PC's (translated, byte for byte) and its images have the same numbers and sizes: the game's own file plays with the Amiga's pixels";

    /// <summary>The animations that may be replaced.</summary>
    public static IReadOnlyList<AmigaAnimationEntry> Entries { get; } =
    [
        .. new[] { "blding01", "blding02", "blding03", "blding04", "bugcar", "bustout", "escape", "followed", "injail", "intrgate", "surndr01" }
            .Select(name => new AmigaAnimationEntry($"{name}.pan", $"{name}.pan", Same)),
    ];

    /// <summary>The entry for an animation file name of the game (compared without regard to letter case); null when the animation stays the PC's.</summary>
    /// <param name="name">The file name the game asks for.</param>
    public static AmigaAnimationEntry? Find(string name) => Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}
