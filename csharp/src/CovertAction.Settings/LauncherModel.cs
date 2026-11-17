// Adapted from the Darklands Restoration Project, src/Darklands.Launcher/LauncherModel.cs; same author.
// Adapted: Covert Action's settings (the sound cards and the controls the set-up screen asked for, the picture's scaling, the saves
// folder, the keyboard's repeat); window sizes of the 4:3 picture; no packs, filters or start-up switches.
using System.Text.Json.Nodes;

namespace CovertAction.Settings;

/// <summary>The settings being edited on the launcher, what each shows, and what changed.</summary>
internal sealed class LauncherModel
{
    /// <summary>The sound choices offered, with their labels, in the order ←/→ go through them.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> SoundCards =
    [
        ("none", "No sound"),
        ("speaker", "PC speaker"),
        ("adlib", "AdLib"),
        ("mt32", "Roland MT-32"),
        ("tandy", "Tandy"),
    ];

    /// <summary>The picture's scalings, with their labels.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Scalings =
    [
        ("pixelart", "Pixel art (even pixels)"),
        ("nearest", "Nearest (hard pixels)"),
        ("smooth", "Smooth"),
    ];

    /// <summary>The controls, with their labels (the set-up screen's controls question of the original).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> ControlChoices =
    [
        ("joystick", "Keyboard and joystick"),
        ("keyboard", "Keyboard only"),
    ];

    /// <summary>The key repeats, with their labels.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> KeyRepeats =
    [
        ("pc", "PC keyboard (10.9 a second)"),
        ("windows", "Windows' own rate"),
    ];

    /// <summary>The cards the music or the effects can play on (D-620), with their labels; "card": the sound card plays both.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> PartCards =
    [
        ("card", "The sound card"),
        ("none", "None"),
        ("speaker", "PC speaker"),
        ("adlib", "AdLib"),
        ("mt32", "Roland MT-32"),
        ("tandy", "Tandy"),
    ];

    /// <summary>The choices for the music: the cards of <see cref="PartCards"/>, and the Amiga version's music from the player's own Amiga files (D-640).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> MusicChoices = [.. PartCards, ("amiga", "Amiga (your own Amiga disks)")];

    /// <summary>The choices for the sound effects: the same, the Amiga version's effects from the player's own Amiga files (D-641).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> EffectsChoices = MusicChoices;

    private static readonly int[] WindowScales = [2, 3, 4, 5, 6];

    private readonly LauncherContext _context;
    private readonly (string? GameFolder, (int W, int H) Window, bool Fullscreen, string Scaling, string SoundCard, string Controls, string? Roms, string? Saves, string KeyRepeat, bool ArrowKeysWalk) _initial;

    /// <summary>The mouse option as the launcher opened.</summary>
    private readonly bool _initialMouse;

    /// <summary>The two bug-fix options as the launcher opened.</summary>
    private readonly (bool Original, bool Campaign) _initialFixes;

    /// <summary>The folder shown at the start without the player choosing it (given at the start or found), or null.</summary>
    private readonly string? _automatic;

    private readonly (string Music, string Effects) _initialParts;

    private string? _mt32Roms;
    private RomStatus? _roms;
    private string? _amigaFiles;
    private AmigaStatus? _amiga;
    private readonly (string? Files, bool OriginalOnly) _initialAmiga;
    private readonly bool _initialAmigaPictures;

    /// <summary>The cut-content options as the launcher opened.</summary>
    private readonly bool _initialCapturedDossier, _initialStandaloneChase, _initialHardestCircuit;

    public LauncherModel(LauncherContext context, (int Width, int Height)? display)
    {
        _context = context;
        var s = context.Settings;
        var start = context.Start;
        // what the run would start with: the command line and the environment shown where they set a row
        var window = (start.Width, start.Height);
        Windows = WindowChoices(display, window);
        WindowIndex = Windows.IndexOf(window);
        Fullscreen = start.Fullscreen;
        Scaling = start.Scaling;
        SoundCard = start.SoundCard;
        Controls = start.Controls;
        _mt32Roms = IsFixed("sound.mt32Roms", out _) ? start.Mt32Roms : Blank(s.Mt32Roms);
        SavesFolder = IsFixed("savesFolder", out _) ? start.SavesFolder : Blank(s.SavesFolder);
        KeyRepeat = start.SystemKeyRepeat ? "windows" : "pc";
        ArrowKeysWalk = IsFixed("input.arrowKeysWalk", out _) ? start.ArrowKeysWalk : s.ArrowKeysWalk ?? false;
        Mouse = _initialMouse = start.Mouse;
        FixOriginalBugs = start.FixOriginalBugs;
        FixCampaignBugs = start.FixCampaignBugs;
        _initialFixes = (FixOriginalBugs, FixCampaignBugs);
        Found = context.FoundGameFolders();
        var fileFolder = Blank(s.GameFolder);
        // the folder the command line (or COVERT_GAME_DIR) gives is the one the game would start with: shown first
        var given = IsFixed("gameFolder", out var by) ? Blank(start.GameFolder) : null;
        SetGameFolder(given ?? fileFolder, given is null ? null : "set by " + by);
        if (GameFolderChecked is null && Found.Count > 0)
        {
            SetGameFolder(Found[0].Folder, Found[0].Source);
        }
        _initial = (fileFolder, Windows[WindowIndex], Fullscreen, Scaling, SoundCard, Controls, _mt32Roms, SavesFolder, KeyRepeat, ArrowKeysWalk);
        _automatic = GameFolder;
        (MusicCard, EffectsCard) = _initialParts = (start.MusicCard, start.EffectsCard);
        _amigaFiles = Blank(start.AmigaFiles);
        AmigaOriginalOnly = start.AmigaOriginalOnly;
        _initialAmiga = (_amigaFiles, AmigaOriginalOnly);
        AmigaPictures = _initialAmigaPictures = start.AmigaPictures;
        CutCapturedDossier = _initialCapturedDossier = start.CutCapturedDossier;
        CutStandaloneChase = _initialStandaloneChase = start.CutStandaloneChase;
        CutHardestCircuit = _initialHardestCircuit = start.CutHardestCircuit;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    public IReadOnlyList<(string Folder, string Source)> Found { get; }

    /// <summary>The game folder as the player named it (or as it was found), null: none.</summary>
    public string? GameFolder { get; private set; }

    /// <summary>The folder with the game in <see cref="GameFolder"/>, null when it holds none.</summary>
    public string? GameFolderChecked { get; private set; }

    /// <summary>Where the game folder came from ("the program's folder"), or null.</summary>
    public string? GameFolderSource { get; private set; }

    public List<(int Width, int Height)> Windows { get; }

    public int WindowIndex { get; set; }

    public bool Fullscreen { get; set; }

    /// <summary>One of <see cref="Scalings"/>' values.</summary>
    public string Scaling { get; set; }

    /// <summary>One of <see cref="SoundCards"/>' values.</summary>
    public string SoundCard { get; set; }

    /// <summary>One of <see cref="ControlChoices"/>' values.</summary>
    public string Controls { get; set; }

    /// <summary>The saves folder chosen; null: the default (<see cref="LauncherContext.DefaultSavesFolder"/>).</summary>
    public string? SavesFolder { get; set; }

    /// <summary>One of <see cref="KeyRepeats"/>' values.</summary>
    public string KeyRepeat { get; set; }

    /// <summary>In a building raid the grey arrow keys walk (D-610).</summary>
    public bool ArrowKeysWalk { get; set; }

    /// <summary>One of <see cref="MusicChoices"/>' values: the card the tunes play on, or "amiga".</summary>
    public string MusicCard { get; set; }

    /// <summary>One of <see cref="EffectsChoices"/>' values: the card the sound effects play on, or "amiga".</summary>
    public string EffectsCard { get; set; }

    /// <summary>Whether the music and the effects play on two cards (either is set to a card of its own, D-620).</summary>
    public bool Mixing => MusicCard != "card" || EffectsCard != "card";

    /// <summary>The Amiga version's music is chosen for the music (D-640).</summary>
    public bool AmigaMusic => MusicCard == "amiga";

    /// <summary>The Amiga version's sound effects are chosen for the effects (D-641).</summary>
    public bool AmigaEffects => EffectsCard == "amiga";

    /// <summary>The Amiga music and effects play only where the Amiga game had them (D-640, D-641).</summary>
    public bool AmigaOriginalOnly { get; set; }

    /// <summary>The folder with the Amiga files, the music's and the effects', as chosen; null: found beside the program or the game.</summary>
    public string? AmigaFiles
    {
        get => _amigaFiles;
        set
        {
            _amigaFiles = value;
            _amiga = null;
        }
    }

    /// <summary>What the Amiga files' folder holds; looked at once for each folder (it reads the disk); null when the host cannot tell.</summary>
    public AmigaStatus? Amiga => _amiga ??= _context.DescribeAmiga?.Invoke(AmigaFiles, GameFolderChecked);

    /// <summary>The card a part plays on: its own, else the sound card.</summary>
    public string CardOf(string part) => part == "card" ? SoundCard : part;

    /// <summary>The mouse in the menus, lists and maps (input.mouse).</summary>
    public bool Mouse { get; set; }

    /// <summary>Fix the original's bugs, the safe group (fixes.originalBugs).</summary>
    public bool FixOriginalBugs { get; set; }

    /// <summary>Also fix the campaign-logic bugs (fixes.campaignBugs).</summary>
    public bool FixCampaignBugs { get; set; }

    /// <summary>The Amiga version's pictures in place of the PC's where it has the same one (graphics.amiga, D-642).</summary>
    public bool AmigaPictures { get; set; }

    /// <summary>The dossier page of a captured cast member (cutContent.capturedDossier, D-660).</summary>
    public bool CutCapturedDossier { get; set; }

    /// <summary>The car chase on its own in the practice menu (cutContent.standaloneChase, D-661).</summary>
    public bool CutStandaloneChase { get; set; }

    /// <summary>The electronics board's fourth difficulty (cutContent.hardestCircuit, D-662).</summary>
    public bool CutHardestCircuit { get; set; }

    /// <summary>The MT-32 ROM folder chosen; null: the default (MT32_ROMS next to the program, else where the ROMs are found).</summary>
    public string? Mt32Roms
    {
        get => _mt32Roms;
        set
        {
            _mt32Roms = value;
            _roms = null;
        }
    }

    /// <summary>What the ROM folder holds; looked at once for each folder (it reads the disk).</summary>
    public RomStatus Roms => _roms ??= Mt32Roms is null && _context.AutomaticRoms is { } automatic
        ? automatic(GameFolderChecked)
        : _context.DescribeRoms(Mt32Roms);

    public bool CanPlay => GameFolderChecked is not null;

    public bool IsFixed(string key, out string by) => _context.Start.FixedKeys.TryGetValue(key, out by!);

    public void SetGameFolder(string? folder, string? source = null)
    {
        GameFolder = folder;
        GameFolderChecked = folder is null ? null : _context.CheckGameFolder(folder);
        GameFolderSource = GameFolderChecked is null ? null : source;
        _roms = null; // the ROMs looked for around the game when no folder is chosen depend on the game folder
        _amiga = null; // and so do the Amiga files
    }

    /// <summary>The next (or previous) of the folders found, after the current one.</summary>
    public void CycleFoundFolder(int step)
    {
        if (Found.Count == 0)
        {
            return;
        }
        var i = Found.ToList().FindIndex(f => f.Folder == GameFolder);
        i = i < 0 ? 0 : (i + step + Found.Count) % Found.Count;
        SetGameFolder(Found[i].Folder, Found[i].Source);
    }

    public void CycleWindow(int step) => WindowIndex = (WindowIndex + step + Windows.Count) % Windows.Count;

    public void CycleSoundCard(int step) => SoundCard = Step(SoundCards, SoundCard, step);

    public void CycleControls(int step) => Controls = Step(ControlChoices, Controls, step);

    public void CycleScaling(int step) => Scaling = Step(Scalings, Scaling, step);

    public void CycleKeyRepeat(int step) => KeyRepeat = Step(KeyRepeats, KeyRepeat, step);

    public void CycleMusicCard(int step) => MusicCard = Step(MusicChoices, MusicCard, step);

    public void CycleEffectsCard(int step) => EffectsCard = Step(EffectsChoices, EffectsCard, step);

    private static string Step(IReadOnlyList<(string Value, string Label)> choices, string current, int step)
    {
        var i = Math.Max(0, choices.ToList().FindIndex(c => c.Value == current));
        return choices[((i + step) % choices.Count + choices.Count) % choices.Count].Value;
    }

    public static string Label(IReadOnlyList<(string Value, string Label)> choices, string value) =>
        choices.FirstOrDefault(c => c.Value == value).Label ?? choices[0].Label;

    public static string SizeLabel((int Width, int Height) size) => $"{size.Width} x {size.Height}";

    /// <summary>The settings-file keys that changed, with their new values.</summary>
    /// <param name="playing">The player pressed Play: a folder they did not choose themselves (given at the start or found) is
    /// written only then; else (the launcher closed) it is left to the next start.</param>
    public IReadOnlyDictionary<string, JsonNode?> Changes(bool playing = true)
    {
        var changes = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        // a folder fixed by the command line goes into the file only when the file names none (the next start then finds it)
        if (GameFolder != _initial.GameFolder && GameFolderChecked is not null && (_initial.GameFolder is null || !IsFixed("gameFolder", out _))
            && (playing || GameFolder != _automatic))
        {
            changes["gameFolder"] = Written(GameFolder);
        }
        var window = Windows[WindowIndex];
        if (window != _initial.Window)
        {
            if (window.Width % StartSettings.BaseWidth == 0 && window.Height == window.Width / StartSettings.BaseWidth * StartSettings.BaseHeight)
            {
                changes["display.scale"] = window.Width / StartSettings.BaseWidth;
                changes["display.resolution"] = "";
            }
            else
            {
                changes["display.resolution"] = $"{window.Width}x{window.Height}";
            }
        }
        if (Fullscreen != _initial.Fullscreen)
        {
            changes["display.fullscreen"] = Fullscreen;
        }
        if (Scaling != _initial.Scaling)
        {
            changes["display.scaling"] = Scaling;
        }
        if (SoundCard != _initial.SoundCard)
        {
            changes["sound.card"] = SoundCard;
        }
        if (Controls != _initial.Controls)
        {
            changes["input.controls"] = Controls;
        }
        if (Mt32Roms != _initial.Roms)
        {
            changes["sound.mt32Roms"] = Written(Mt32Roms);
        }
        if (SavesFolder != _initial.Saves)
        {
            changes["savesFolder"] = Written(SavesFolder);
        }
        if (KeyRepeat != _initial.KeyRepeat)
        {
            changes["input.keyRepeat"] = KeyRepeat;
        }
        if (ArrowKeysWalk != _initial.ArrowKeysWalk)
        {
            changes["input.arrowKeysWalk"] = ArrowKeysWalk;
        }
        if (MusicCard != _initialParts.Music)
        {
            changes["sound.music"] = MusicCard;
        }
        if (EffectsCard != _initialParts.Effects)
        {
            changes["sound.effects"] = EffectsCard;
        }
        if (AmigaFiles != _initialAmiga.Files)
        {
            changes["sound.amigaFiles"] = Written(AmigaFiles);
        }
        if (AmigaOriginalOnly != _initialAmiga.OriginalOnly)
        {
            changes["sound.amigaOriginalOnly"] = AmigaOriginalOnly;
        }
        if (Mouse != _initialMouse)
        {
            changes["input.mouse"] = Mouse;
        }
        if (FixOriginalBugs != _initialFixes.Original)
        {
            changes["fixes.originalBugs"] = FixOriginalBugs;
        }
        if (FixCampaignBugs != _initialFixes.Campaign)
        {
            changes["fixes.campaignBugs"] = FixCampaignBugs;
        }
        if (AmigaPictures != _initialAmigaPictures)
        {
            changes["graphics.amiga"] = AmigaPictures;
        }
        if (CutCapturedDossier != _initialCapturedDossier)
        {
            changes["cutContent.capturedDossier"] = CutCapturedDossier;
        }
        if (CutStandaloneChase != _initialStandaloneChase)
        {
            changes["cutContent.standaloneChase"] = CutStandaloneChase;
        }
        if (CutHardestCircuit != _initialHardestCircuit)
        {
            changes["cutContent.hardestCircuit"] = CutHardestCircuit;
        }
        return changes;
    }

    /// <summary>A folder as the file names it: forward slashes (JSON needs no escapes then); "" for none.</summary>
    private static string Written(string? folder) => folder?.Replace('\\', '/') ?? "";

    /// <summary>The settings file's values with the launcher's rows over them.</summary>
    public GameSettings Settings()
    {
        var s = _context.Settings.Copy();
        var window = Windows[WindowIndex];
        var exact = window.Width % StartSettings.BaseWidth == 0 && window.Height == window.Width / StartSettings.BaseWidth * StartSettings.BaseHeight;
        s.GameFolder = GameFolderChecked is not null ? GameFolder : s.GameFolder;
        s.Scale = exact ? window.Width / StartSettings.BaseWidth : null;
        s.Resolution = exact ? null : $"{window.Width}x{window.Height}";
        s.Fullscreen = Fullscreen;
        s.Scaling = Scaling;
        s.SoundCard = SoundCard;
        s.Controls = Controls;
        s.Mt32Roms = Mt32Roms;
        s.SavesFolder = SavesFolder;
        s.KeyRepeat = KeyRepeat;
        s.ArrowKeysWalk = ArrowKeysWalk;
        s.MusicCard = MusicCard;
        s.EffectsCard = EffectsCard;
        s.AmigaFiles = AmigaFiles;
        s.AmigaOriginalOnly = AmigaOriginalOnly;
        s.Mouse = Mouse;
        s.FixOriginalBugs = FixOriginalBugs;
        s.FixCampaignBugs = FixCampaignBugs;
        s.AmigaPictures = AmigaPictures;
        s.CutCapturedDossier = CutCapturedDossier;
        s.CutStandaloneChase = CutStandaloneChase;
        s.CutHardestCircuit = CutHardestCircuit;
        return s;
    }

    /// <summary>The 4:3 sizes that fit the screen (all of them when its size is unknown), and the current one.</summary>
    private static List<(int Width, int Height)> WindowChoices((int Width, int Height)? display, (int Width, int Height) current)
    {
        var sizes = WindowScales
            .Select(f => (StartSettings.BaseWidth * f, StartSettings.BaseHeight * f))
            .Where(size => display is not { } d || (size.Item1 <= d.Width && size.Item2 <= d.Height))
            .ToList();
        if (sizes.Count == 0)
        {
            sizes.Add((StartSettings.BaseWidth * 2, StartSettings.BaseHeight * 2));
        }
        if (!sizes.Contains(current))
        {
            sizes.Add(current);
            sizes.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }
        return sizes;
    }
}
