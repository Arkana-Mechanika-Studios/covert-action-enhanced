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
public sealed record GameOptions(char SoundLetter = 'A', bool Joystick = true, string? Mt32RomFolder = null, bool AskSetUpQuestions = false);
