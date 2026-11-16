namespace CovertAction.Audio.Amiga;

/// <summary>The Amiga version's sound banks: one per part of the game, loaded when the part starts (csharp/docs/AMIGA-EFFECTS.md).</summary>
public enum AmigaBank
{
    /// <summary>gamesnd.dat: the campaign (GAME): the aeroplane and the teleprinter.</summary>
    Game,

    /// <summary>tacsnd.dat: the building raid (TAC).</summary>
    Tac,

    /// <summary>codesnd.dat: the cryptography workstation (CODE).</summary>
    Code,

    /// <summary>bugsnd.dat: the electronics workbench (BUG).</summary>
    Bug,

    /// <summary>chasesnd.dat: the car chase (CHASE).</summary>
    Chase,
}

/// <summary>How sure the match of a PC command to an Amiga effect is.</summary>
public enum AmigaMatch
{
    /// <summary>The same routine does the same thing at the same moment in both games.</summary>
    Matched,

    /// <summary>The same event, but the Amiga does not play it the way the PC does (once for several, a loop where the PC has a click).</summary>
    Probable,

    /// <summary>The Amiga game has no call for it.</summary>
    None,
}

/// <summary>
/// One PC sound command of a part of the game and the Amiga effect that stands for it.
/// </summary>
/// <param name="Bank">The part's bank: the PC command numbers are the game's own, and two parts use the same number for different sounds.</param>
/// <param name="Command">The PC's sound command (the first value of the game's <c>play_sound</c>).</param>
/// <param name="Name">What the PC game calls it, for the table.</param>
/// <param name="Match">How sure the match is.</param>
/// <param name="Index">The effect's number in the bank, as the Amiga's <c>DoSound</c> is given it; -1 for none.</param>
/// <param name="Volume">The volume 0 to 64 the Amiga's call plays it at (the call's second argument).</param>
/// <param name="PanX">The pan position 0 to 319 the Amiga's call plays it at (160: the middle).</param>
/// <param name="Evidence">Where the Amiga calls it (hunk and offset of the program <c>game</c>) and the routine of the C# game that does the same.</param>
public sealed record AmigaEffectRow(AmigaBank Bank, int Command, string Name, AmigaMatch Match, int Index, int Volume, int PanX, string Evidence);

/// <summary>
/// Which Amiga effect a PC sound command stands for: the table of csharp/docs/AMIGA-EFFECTS.md as data. The Amiga game
/// names a sound by the bank of the part that plays it and the effect's number in it (<c>DoSound(n, volume, x, pitch offset)</c>);
/// the PC game by one command number. The rows are the matches of the Amiga's call sites with the C# game's routines.
/// </summary>
/// <remarks>
/// New code: the Amiga version's sound effects, a player's option (D-641). The effect's volume and pan are the Amiga call's
/// own. FINAL (the menus) has no bank: the Amiga game plays no effect there. The commands of the pause, the stop and the
/// tunes are not here: <see cref="AmigaEffectsRouter"/> handles them.
/// </remarks>
public static class AmigaEffectMap
{
    private const int Middle = 160;

    /// <summary>The rows: the commands with an Amiga twin, and the commands of the examined parts that have none.</summary>
    public static IReadOnlyList<AmigaEffectRow> Rows { get; } =
    [
        // the building raid (TAC), tacsnd.dat; Amiga hunks 25 to 45
        new(AmigaBank.Tac, 3, "the player is hit", AmigaMatch.Matched, 3, 63, Middle, "h28+0x2712; BuildingRaid.Shoot"),
        new(AmigaBank.Tac, 4, "a guard is hit", AmigaMatch.Matched, 3, 30, Middle, "h28+0x273a; BuildingRaid.Shoot"),
        new(AmigaBank.Tac, 5, "empty pistol", AmigaMatch.Matched, 9, 30, Middle, "h28+0x1e3e; BuildingRaid.ShootingFrame (no bullets left)"),
        new(AmigaBank.Tac, 6, "Uzi shot", AmigaMatch.Matched, 0, 30, Middle, "h28+0x273a, 0x277c; BuildingRaid.Shoot (the Uzi's)"),
        new(AmigaBank.Tac, 7, "blast in the player's room", AmigaMatch.Matched, 11, 63, Middle, "h28+0x2c2c; BuildingRaid.Blast"),
        new(AmigaBank.Tac, 8, "blast in another room", AmigaMatch.Matched, 11, 25, Middle, "h28+0x2c2c; BuildingRaid.Blast"),
        new(AmigaBank.Tac, 9, "camera", AmigaMatch.Matched, 7, 63, Middle, "h31+0x6aa; BuildingRaid.TakePhoto"),
        new(AmigaBank.Tac, 10, "bug planted", AmigaMatch.Matched, 10, 63, Middle, "h31+0x870; BuildingRaid.PlantBug"),
        new(AmigaBank.Tac, 11, "trap laid", AmigaMatch.None, -1, 0, Middle, "the Amiga plays nothing at the trap menu's choice"),
        new(AmigaBank.Tac, 12, "grenade thrown", AmigaMatch.Matched, 5, 63, Middle, "h28+0x2914; BuildingRaid.FireProjectile"),
        new(AmigaBank.Tac, 13, "a door opens", AmigaMatch.Matched, 1, 63, Middle, "h31+0x36c, h37+0x588; BuildingRaid.UsePiece, RedrawCell"),
        new(AmigaBank.Tac, 14, "a door closes", AmigaMatch.Matched, 2, 63, Middle, "h31+0x324, h37+0x588; BuildingRaid.UsePiece, RedrawCell"),
        new(AmigaBank.Tac, 16, "a cupboard or drawer opens", AmigaMatch.Matched, 13, 40, Middle, "h31+0x384; BuildingRaid.UsePiece"),
        new(AmigaBank.Tac, 17, "a cupboard or drawer closes", AmigaMatch.Matched, 12, 40, Middle, "h31+0x33c; BuildingRaid.UsePiece"),
        new(AmigaBank.Tac, 18, "siren", AmigaMatch.Matched, 6, 50, Middle, "h28+0x9a4; BuildingRaid.CountDownAlert"),
        new(AmigaBank.Tac, 33, "the player is missed", AmigaMatch.Matched, 4, 40, 100, "h28+0x2756; BuildingRaid.Shoot"),
        new(AmigaBank.Tac, 34, "a guard is missed", AmigaMatch.Matched, 4, 30, Middle, "h28+0x277c, 0x27cc; BuildingRaid.Shoot, AimAndFire"),
        new(AmigaBank.Tac, 44, "gunfire (every frame, with the raid's mode)", AmigaMatch.None, -1, 0, Middle, "the Amiga plays tac[0] and tac[3] at each shot (h28+0x1de8): no per-frame command"),

        // the car chase (CHASE), chasesnd.dat; Amiga hunks 16 to 24
        new(AmigaBank.Chase, 22, "first gear", AmigaMatch.Matched, 5, 63, Middle, "h22+0x874, 0xa40; CarChase.ChaseTick, GearUp"),
        new(AmigaBank.Chase, 23, "the tyres in a turn", AmigaMatch.Matched, 4, 35, Middle, "h22+0xd18, 0x2d00, 0x2dde; CarChase.AnimateTurn, TakeKey"),
        new(AmigaBank.Chase, 24, "shift up", AmigaMatch.Matched, 7, 20, Middle, "h22+0xa7e; CarChase.GearUp"),
        new(AmigaBank.Chase, 25, "shift down", AmigaMatch.None, -1, 0, Middle, "the Amiga plays nothing when the player shifts down"),
        new(AmigaBank.Chase, 26, "engine, car 1, by gear", AmigaMatch.None, -1, 0, Middle, "the Amiga loops chase[6] from the start (h16+0x1fa) and has no engine call by gear"),
        new(AmigaBank.Chase, 27, "engine, car 2, by gear", AmigaMatch.None, -1, 0, Middle, "as 26"),

        // the cryptography workstation (CODE), codesnd.dat; Amiga hunks 4 to 9
        new(AmigaBank.Code, 45, "a letter is set", AmigaMatch.Matched, 2, 63, Middle, "h7+0xc72; CryptoWorkstation.GuessLetter"),
        new(AmigaBank.Code, 46, "a hint, or the message is decoded", AmigaMatch.Probable, 1, 40, Middle, "h7+0xcd4 plays code[1] when the message is decoded (ShowSolution); the Amiga's hint (h7+0x8e2) plays code[0] at each of 200 blinks, the PC's once before them"),

        // the electronics workbench (BUG), bugsnd.dat; Amiga hunks 10 to 15
        new(AmigaBank.Bug, 28, "a chip is swapped", AmigaMatch.Matched, 0, 63, Middle, "h13+0x146a; ElectronicsWorkbench.SwapChip"),
        new(AmigaBank.Bug, 29, "a phone line goes dead", AmigaMatch.Matched, 2, 63, Middle, "h13+0xce4; ElectronicsWorkbench.DrawTerminals"),
        new(AmigaBank.Bug, 30, "alarm", AmigaMatch.Matched, 1, 63, Middle, "h13+0xe98; ElectronicsWorkbench.BugStatusMessage"),
        new(AmigaBank.Bug, 46, "the board is finished", AmigaMatch.Matched, 2, 63, Middle, "h13+0xf60; ElectronicsWorkbench.ShowResult"),

        // the campaign (GAME), gamesnd.dat; Amiga hunks 70 to 114
        new(AmigaBank.Game, 31, "the teleprinter's tick", AmigaMatch.None, -1, 0, Middle, "the Amiga loops game[0] through the whole text and stops it at the end (h73+0x2b6); the PC clicks at each line"),
        new(AmigaBank.Game, 32, "the teleprinter's line end", AmigaMatch.Matched, 1, 63, Middle, "h73+0x3c8; LibraryScreens.ShowBulletin (the teleprinter)"),
        new(AmigaBank.Game, 35, "the aeroplane takes off", AmigaMatch.Matched, 3, 63, Middle, "h79+0x592; CampaignScreens.AnimateTravel"),
        new(AmigaBank.Game, 36, "the aeroplane lands", AmigaMatch.Matched, 2, 63, Middle, "h79+0x664; CampaignScreens.AnimateTravel"),
    ];

    /// <summary>The Amiga effect for a PC command in a part's bank; null when the Amiga game has none (or the part has no bank).</summary>
    /// <param name="bank">The part's bank; null for a part without effects on the Amiga (FINAL).</param>
    /// <param name="command">The PC's sound command.</param>
    public static AmigaEffectRow? Find(AmigaBank? bank, int command) =>
        bank is null ? null : Rows.FirstOrDefault(r => r.Bank == bank && r.Command == command && r.Match != AmigaMatch.None);
}
