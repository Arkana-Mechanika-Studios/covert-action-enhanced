namespace CovertAction.Launcher;

/// <summary>
/// The player's options of the launcher that the game itself reads (new code): the answers the set-up screen takes
/// from the launcher's settings in place of its questions (D-603), and the MT-32 ROMs.
/// </summary>
/// <param name="SoundLetter">The sound driver's letter of the sound card chosen in the settings (N, I, T, A, R): the set-up's sound answer. Default: A, the AdLib.</param>
/// <param name="Joystick">The controls chosen in the settings: true "keyboard and joystick" (the menus part then has the joystick calibrated), false "keyboard only".</param>
/// <param name="Mt32RomFolder">The MT-32 ROMs' folder; null: COVERT_MT32_ROMS, else the folder RomFolderSearch finds.</param>
/// <param name="AskSetUpQuestions">
/// For the proof and the tests: the set-up screen asks the original's questions and its answers count (<paramref name="SoundLetter"/>
/// and <paramref name="Joystick"/> are not used). Default false: the set-up screen shows its title only, as the player's game does.
/// </param>
/// <param name="ArrowKeysWalk">In a building raid the grey arrow keys walk like the keypad's digits and Shift with them lunges (D-610); off: the original's keys.</param>
/// <param name="MusicLetter">The driver the tunes play on (N, I, T, A, R) when the music and the effects play on two cards (D-620); null: the sound card's.</param>
/// <param name="EffectsLetter">The driver the sound effects play on (D-620), or 'G' for the Amiga version's sound effects (D-641); null: the sound card's. Both null (the default): one card plays all, as the original.</param>
/// <param name="AmigaFolder">With <see cref="MusicLetter"/> or <see cref="EffectsLetter"/> 'G' (the Amiga version's music and sound effects, D-640, D-641) or <paramref name="AmigaPictures"/>: the folder with the player's Amiga files; null: found beside the program or the game.</param>
/// <param name="AmigaOnlyWhereOriginal">The Amiga music and effects play only where the Amiga game had them: the selection screens' and the vacations' tunes (commands 39 to 43) and the effects without an Amiga twin play nothing (D-640, D-641).</param>
/// <param name="AmigaPictures">The game's pictures are replaced, where the Amiga version has the same one, by the Amiga version's, read from the player's own Amiga files in <paramref name="AmigaFolder"/> (D-642); off by default.</param>
/// <param name="Mouse">
/// The mouse in the menus, lists and maps (the settings' input.mouse, off by default): the game gets a mouse driver on
/// the host's pointer, when the host has one; off (or without a pointer), the PC has no mouse, as the original assumes
/// (docs/faithfulness/DIVERGENCES.md D-630).
/// </param>
/// <param name="MouseChase">
/// The mouse in the car chase as well (the settings' input.mouseChase, off by default; D-670): used only when <paramref name="Mouse"/>
/// is on too. Clicks stand for the chase's keys (a turn, a gear, the other tail car, the arrest, entering the building, the speed).
/// </param>
/// <param name="FixOriginalBugs">The launcher's option "Fix the original's bugs" (fixes.originalBugs): the safe group of the register's bugs is fixed (<see cref="CovertAction.Core.OriginalBehaviour.ForPlayer"/>); off by default.</param>
/// <param name="FixCampaignBugs">The launcher's option "Also fix campaign-logic bugs" (fixes.campaignBugs): the bugs of the campaign's logic as well; off by default.</param>
/// <param name="CutStandaloneChase">The launcher's option "Free car chase" (cutContent.standaloneChase, D-661): the practice menu offers the car chase on its own; off by default.</param>
/// <param name="CutHardestCircuit">The launcher's option "Hardest circuit board" (cutContent.hardestCircuit, D-662): the electronics board may have its dormant difficulty 4; off by default.</param>
/// <param name="CutFillerTexts">The launcher's option "All filler sentences" (cutContent.fillerTexts, D-663): the coded messages' filler is drawn from all 16 sentences of TEXT.DTA; off by default.</param>
/// <param name="CutChaseGrounds">The launcher's option "All 20 chase grounds" (cutContent.chaseGrounds, D-664): the car chase's map draws its ground pictures from all 20 of CHASE.PIC; off by default.</param>
/// <param name="CutCapturedDossier">The launcher's option "Captured dossier" (cutContent.capturedDossier, D-660): CAPTRD01.PAN plays when a cast member is captured; off by default.</param>
public sealed record GameOptions(char SoundLetter = 'A', bool Joystick = true, string? Mt32RomFolder = null, bool AskSetUpQuestions = false,
    bool ArrowKeysWalk = false, char? MusicLetter = null, char? EffectsLetter = null, bool Mouse = false, string? AmigaFolder = null, bool AmigaOnlyWhereOriginal = false, bool AmigaPictures = false,
    bool FixOriginalBugs = false, bool FixCampaignBugs = false, bool MouseChase = false, bool CutCapturedDossier = false, bool CutStandaloneChase = false, bool CutHardestCircuit = false, bool CutFillerTexts = false, bool CutChaseGrounds = false)
{
    /// <summary>
    /// Which of the original's bugs the game reproduces, from the two fix options: every part of the game gets this one object.
    /// With both options off it is the release 1.0's (<see cref="CovertAction.Core.OriginalBehaviour.Player"/>).
    /// </summary>
    public CovertAction.Core.OriginalBehaviour Behaviour => CovertAction.Core.OriginalBehaviour.ForPlayer(FixOriginalBugs, FixCampaignBugs, new CovertAction.Core.CutContentOptions(CutCapturedDossier, CutStandaloneChase, CutHardestCircuit, CutFillerTexts, CutChaseGrounds));
}
