using CovertAction.Settings.Drawing;

namespace CovertAction.Settings;

/// <summary>
/// The launcher's one screen, laid out as the Darklands Restoration Project's (a card at the left: what is new, about
/// the project, the small print; the settings at the right on two tabs, rows of ‹ value ›, Change… buttons, a box of
/// details, Save and quit, Play), in a look of its own: a case file on a dark desk, in the VGA text font the game's
/// set-up screen shows. Drawn at one design size (<see cref="DesignWidth"/> x <see cref="DesignHeight"/>); the host fits
/// it to the window. Nothing of the game's art is in it: a picture of the player's own game may show behind it.
/// New code.
/// </summary>
internal sealed class LauncherScreen
{
    public const int DesignWidth = 1280;
    public const int DesignHeight = 720;

    // the look: a dark desk, a manila case file, a slate settings panel, amber for what can be changed
    private const uint Desk = 0x0B1016;
    private const uint DeskLine = 0x121A24;
    private const uint Amber = 0xE8B04A;
    private const uint AmberDark = 0x8A6420;
    private const uint Shadow = 0x000000;
    private const uint Manila = 0xE4D3A2;
    private const uint ManilaEdge = 0xB59C62;
    private const uint ManilaTab = 0xD8C38C;
    private const uint Ink = 0x2A2318;
    private const uint InkSoft = 0x5E5038;
    private const uint InkRed = 0x8C2A18;
    private const uint Slate = 0x141C26;
    private const uint SlateEdge = 0x30415A;
    private const uint SlateRow = 0x1E2A3A;
    private const uint SlateFocus = 0x263650;
    private const uint Text = 0xD9E0EA;
    private const uint TextSoft = 0x8A98AA;
    private const uint TextDim = 0x5C6A7C;
    private const uint Good = 0x86D494;
    private const uint Bad = 0xF08070;
    private const uint Note = 0xB8C4D2;
    private const uint FieldFill = 0x0C121A;
    private const uint ButtonFill = 0x2A3A50;
    private const uint ButtonLit = 0x3A5070;
    private const uint ButtonEdge = 0x5A7090;

    /// <summary>The settings panel and the left card, in design pixels.</summary>
    public static readonly Rect PanelFrame = new(600, 32, 648, 656);

    public static readonly Rect CardFrame = new(32, 150, 544, 538);

    // the settings grid
    private const int RowHeight = 26, StatusHeight = 18, SectionGap = 8, HeadingHeight = 20, ButtonWidth = 104, ButtonHeight = 22;

    private readonly LauncherModel _model;
    private readonly LauncherContext _context;
    private readonly ILauncherSurface _surface;
    private readonly BitmapFont _font;
    private readonly IReadOnlyList<(string Title, string Detail)> _notes;
    private uint[]? _picture;
    private string? _pictureFolder;

    private List<(Rect Area, Item Item, Part Part)> _hotspots = [];
    private List<(Rect Area, string What)> _blocks = [];
    private List<(Rect Area, string What)> _cardBlocks = [];

    private Item _hover;
    private Part _hoverPart;
    private Item _pressed;
    private Item _focus;
    private bool _advanced;
    private bool _cut;
    private bool _controls;

    /// <summary>The Controls tab waits for the key to bind (D-671).</summary>
    private bool _capturing;
    private string? _message;
    private uint _messageColour = Bad;

    public LauncherScreen(LauncherModel model, LauncherContext context, ILauncherSurface surface)
    {
        _model = model;
        _context = context;
        _surface = surface;
        _font = new BitmapFont(context.Font);
        _notes = ReleaseNotes.Detailed(context.ReleaseNotes);
        _focus = model.CanPlay ? Item.Play : Item.GameFolder;
        if (context.Reason == LauncherReason.StartFailed && context.Problem is { } problem)
        {
            _message = "The game could not start: " + problem;
        }
        else if (context.Reason == LauncherReason.GameNotFound && !model.CanPlay)
        {
            _message = "Covert Action was not found. Choose the folder of your copy of the game (Change…), or drop it on this window.";
        }
    }

    /// <summary>What can be pointed at and chosen.</summary>
    public enum Item
    {
        None,
        GameFolder,
        GameChange,
        Window,
        Fullscreen,
        Scaling,
        Sound,
        Test,
        Controls,
        Music,
        MixTest,
        Effects,
        Saves,
        SavesChange,
        Roms,
        RomsChange,
        AmigaFiles,
        AmigaChange,
        AmigaOriginalOnly,
        AmigaPictures,
        KeyRepeat,
        ArrowKeysWalk,
        Mouse,
        FixOriginalBugs,
        FixCampaignBugs,
        CutCapturedDossier,
        CutStandaloneChase,
        CutHardestCircuit,
        CutFillerTexts,
        TabGeneral,
        TabAdvanced,
        TabCutContent,
        TabControls,
        KeyPick,
        KeyBind,
        KeyClear,
        KeysReset,
        PadPick,
        PadKey,
        PadReset,
        Save,
        Play,
    }

    /// <summary>The parts of a control a click can hit.</summary>
    public enum Part
    {
        /// <summary>The control itself (a row, a field, a button).</summary>
        Row,

        /// <summary>A row's ‹: the previous value.</summary>
        Previous,

        /// <summary>A row's ›: the next value.</summary>
        Next,
    }

    public Item Focus => _focus;

    public bool AdvancedShown => _advanced;

    /// <summary>The "Cut content" tab is the one shown.</summary>
    public bool CutContentShown => _cut;

    /// <summary>The "Controls" tab is the one shown.</summary>
    public bool ControlsShown => _controls;

    /// <summary>The Controls tab waits for a key to bind.</summary>
    public bool Capturing => _capturing;

    /// <summary>The message shown in the details box (what an action did, why it could not), or null.</summary>
    public string? Message => _message;

    /// <summary>Where <paramref name="item"/> was drawn last.</summary>
    public Rect Area(Item item, Part part = Part.Row) => _hotspots.First(h => h.Item == item && h.Part == part).Area;

    /// <summary>Whether <paramref name="item"/> was drawn last.</summary>
    public bool Shown(Item item) => _hotspots.Any(h => h.Item == item);

    /// <summary>The settings panel's inside, as drawn: everything of the settings stays in it.</summary>
    internal static Rect SettingsPanel => PanelFrame.Inset(8);

    /// <summary>What the settings panel was drawn with last (rows, fields, buttons, lines of text), each once: none overlaps another.</summary>
    internal IReadOnlyList<(Rect Area, string What)> Blocks => _blocks;

    /// <summary>What the left card was drawn with last, each once: none overlaps another, all inside <see cref="CardFrame"/>.</summary>
    internal IReadOnlyList<(Rect Area, string What)> CardBlocks => _cardBlocks;

    /// <summary>The text of the details box as drawn last.</summary>
    internal string DetailsShown { get; private set; } = "";

    /// <summary>A sound test is playing: the launcher draws again now and then to show its status.</summary>
    public bool Animating => _context.SoundTest is { Playing: true };

    private static bool OnCutContent(Item item) => item is Item.CutCapturedDossier or Item.CutStandaloneChase or Item.CutHardestCircuit or Item.CutFillerTexts;

    private static bool OnControls(Item item) => item is Item.KeyRepeat or Item.ArrowKeysWalk or Item.Mouse or Item.KeyPick or Item.KeyBind or Item.KeyClear or Item.KeysReset or Item.PadPick or Item.PadKey or Item.PadReset;

    private bool OnAdvanced(Item item) => item is Item.Roms or Item.RomsChange or Item.AmigaFiles or Item.AmigaChange or Item.AmigaOriginalOnly or Item.AmigaPictures or Item.Music or Item.MixTest or Item.Effects or Item.FixOriginalBugs or Item.FixCampaignBugs;

    // Tab and the arrows go through General's rows, then Advanced's (its tab opens), then the buttons
    private IEnumerable<Item> Order =>
        new[] { Item.GameFolder, Item.GameChange, Item.Window, Item.Fullscreen, Item.Scaling, Item.Sound, Item.Test, Item.Controls, Item.Saves, Item.SavesChange,
            Item.Roms, Item.RomsChange, Item.AmigaFiles, Item.AmigaChange, Item.AmigaOriginalOnly, Item.AmigaPictures, Item.Music, Item.MixTest, Item.Effects, Item.FixOriginalBugs, Item.FixCampaignBugs, Item.CutCapturedDossier, Item.CutStandaloneChase, Item.CutHardestCircuit, Item.CutFillerTexts,
            Item.KeyRepeat, Item.ArrowKeysWalk, Item.Mouse, Item.KeyPick, Item.KeyBind, Item.KeyClear, Item.KeysReset, Item.PadPick, Item.PadKey, Item.PadReset, Item.Save, Item.Play }
            .Where(i => i is not (Item.Test or Item.MixTest) || _context.SoundTest is not null);

    /// <summary>Handles one input; returns the player's choice when the launcher should end.</summary>
    public LauncherChoice? Handle(LauncherInput input)
    {
        switch (input)
        {
            case CloseRequested:
                return LauncherChoice.Quit;
            case PointerMoved moved:
                var over = HotspotAt(moved.X, moved.Y);
                (_hover, _hoverPart) = (over.Item, over.Part);
                return null;
            case PointerPressed press:
                var hit = HotspotAt(press.X, press.Y);
                if (hit.Item == Item.None)
                {
                    return null;
                }
                _focus = hit.Item;
                _capturing = false;
                if (hit.Item is Item.GameFolder or Item.Saves or Item.Roms or Item.AmigaFiles)
                {
                    // a click on a folder's field only chooses it: its Change… (or Enter) opens the dialog
                    return null;
                }
                if (IsButton(hit.Item))
                {
                    // a button acts when the mouse button is let go over it
                    _pressed = hit.Item;
                    return null;
                }
                return hit.Part switch
                {
                    Part.Previous => Activate(hit.Item, -1),
                    Part.Next => Activate(hit.Item, 1),
                    _ => Activate(hit.Item, 1),
                };
            case PointerReleased released:
                var pressed = _pressed;
                _pressed = Item.None;
                return pressed != Item.None && HotspotAt(released.X, released.Y).Item == pressed ? Activate(pressed, 1) : null;
            case PathDropped dropped:
                Dropped(dropped.Path);
                return null;
            case KeyPressed key:
                return _capturing ? Capture(key) : Key(key);
            default:
                return null;
        }
    }

    private (Rect Area, Item Item, Part Part) HotspotAt(int x, int y) => _hotspots.LastOrDefault(h => h.Area.Contains(x, y));

    private static bool IsButton(Item item) => item is Item.GameChange or Item.SavesChange or Item.RomsChange or Item.AmigaChange or Item.Test or Item.MixTest or Item.KeyBind or Item.KeyClear or Item.KeysReset or Item.PadReset or Item.Save or Item.Play;

    private static bool IsSelect(Item item) => item is Item.Window or Item.Fullscreen or Item.Scaling or Item.Sound or Item.Controls or Item.AmigaOriginalOnly or Item.AmigaPictures or Item.KeyRepeat or Item.ArrowKeysWalk or Item.Music or Item.Effects or Item.Mouse or Item.FixOriginalBugs or Item.FixCampaignBugs or Item.CutCapturedDossier or Item.CutStandaloneChase or Item.CutHardestCircuit or Item.CutFillerTexts or Item.KeyPick or Item.PadPick or Item.PadKey;

    private LauncherChoice? Key(KeyPressed key)
    {
        switch (key.Key)
        {
            case LauncherKey.Up:
                MoveFocus(-1);
                return null;
            case LauncherKey.Down:
                MoveFocus(1);
                return null;
            case LauncherKey.Tab when key.Control:
                ShowTab(((_controls ? 3 : _cut ? 2 : _advanced ? 1 : 0) + 1) % 4);
                return null;
            case LauncherKey.Tab:
                MoveFocus(key.Shift ? -1 : 1);
                return null;
            case LauncherKey.Left or LauncherKey.Right:
                var step = key.Key == LauncherKey.Left ? -1 : 1;
                if (IsSelect(_focus) || _focus == Item.GameFolder)
                {
                    return Activate(_focus, step, cycle: true);
                }
                MoveFocus(step);
                return null;
            case LauncherKey.Enter or LauncherKey.Space:
                return Activate(_focus, 1);
            case LauncherKey.Escape:
                return LauncherChoice.Quit;
            case LauncherKey.Paste when _focus is Item.GameFolder or Item.GameChange or Item.Saves or Item.SavesChange or Item.Roms or Item.RomsChange or Item.AmigaFiles or Item.AmigaChange:
                if (CleanPath(_surface.Clipboard()) is { Length: > 0 } pasted)
                {
                    SetFolder(_focus, pasted);
                }
                return null;
            default:
                return null;
        }
    }

    private void MoveFocus(int step)
    {
        var order = Order.ToList();
        var i = order.IndexOf(_focus);
        _focus = order[i < 0 ? 0 : (i + step + order.Count) % order.Count];
        if (_focus is not (Item.Save or Item.Play))
        {
            _advanced = OnAdvanced(_focus);
            _cut = OnCutContent(_focus);
            _controls = OnControls(_focus);
        }
    }

    /// <summary>The Controls tab while it waits for a key (D-671): any key but Esc becomes the physical key of the chosen key of the original.</summary>
    private LauncherChoice? Capture(KeyPressed key)
    {
        if (key.Key == LauncherKey.Escape)
        {
            _capturing = false;
            _message = "Nothing changed.";
            _messageColour = Note;
        }
        else if (key.Scancode > 0)
        {
            var original = _model.ShownKey;
            if (_model.Bind(original, key.Scancode))
            {
                _capturing = false;
                _message = $"{KeyBindings.ByScancode(key.Scancode)!.Label} now sends {original.Label}.";
                _messageColour = Good;
            }
            else
            {
                _message = "Shift, Ctrl, Alt and the Windows key cannot be remapped. Press another key, or Esc.";
            }
        }
        return null;
    }

    /// <summary>Shows a tab (0 General, 1 Advanced, 2 Cut content, 3 Controls) with its first row focused.</summary>
    private void ShowTab(int tab)
    {
        _capturing = false;
        _advanced = tab == 1;
        _cut = tab == 2;
        _controls = tab == 3;
        _focus = tab switch { 1 => Item.Roms, 2 => Item.CutCapturedDossier, 3 => Item.KeyRepeat, _ => Item.GameFolder };
    }

    /// <summary>The fixed settings-file key of a row, or null.</summary>
    private static string? KeyOf(Item item) => item switch
    {
        Item.GameFolder or Item.GameChange => "gameFolder",
        Item.Window => "display.scale",
        Item.Fullscreen => "display.fullscreen",
        Item.Scaling => "display.scaling",
        Item.Sound => "sound.card",
        Item.Controls => "input.controls",
        Item.Saves or Item.SavesChange => "savesFolder",
        Item.Roms or Item.RomsChange => "sound.mt32Roms",
        Item.AmigaFiles or Item.AmigaChange => "sound.amigaFiles",
        Item.AmigaOriginalOnly => "sound.amigaOriginalOnly",
        Item.AmigaPictures => "graphics.amiga",
        Item.KeyRepeat => "input.keyRepeat",
        Item.ArrowKeysWalk => "input.arrowKeysWalk",
        Item.Music => "sound.music",
        Item.Effects => "sound.effects",
        Item.Mouse => "input.mouse",
        Item.CutCapturedDossier => "cutContent.capturedDossier",
        Item.CutStandaloneChase => "cutContent.standaloneChase",
        Item.CutHardestCircuit => "cutContent.hardestCircuit",
        Item.CutFillerTexts => "cutContent.fillerTexts",
        Item.FixOriginalBugs => "fixes.originalBugs",
        Item.FixCampaignBugs => "fixes.campaignBugs",
        _ => null,
    };

    private bool IsFixed(Item item, out string by)
    {
        by = "";
        // the one mouse row holds both mouse options (D-630, D-670): either one fixed fixes the row
        if (item == Item.Mouse && _model.IsFixed("input.mouseChase", out by))
        {
            return true;
        }
        return KeyOf(item) is { } key && _model.IsFixed(key, out by);
    }

    private LauncherChoice? Activate(Item item, int step, bool cycle = false)
    {
        _message = null;
        _messageColour = Bad;
        switch (item)
        {
            case Item.TabGeneral or Item.TabAdvanced or Item.TabCutContent or Item.TabControls:
                ShowTab(item == Item.TabAdvanced ? 1 : item == Item.TabCutContent ? 2 : item == Item.TabControls ? 3 : 0);
                return null;
            case Item.Save:
                return LauncherChoice.Quit;
            case Item.Play:
                if (_model.CanPlay)
                {
                    return LauncherChoice.Play;
                }
                _focus = Item.GameFolder;
                _advanced = false;
                _cut = false;
                _controls = false;
                _message = "Choose the folder of your copy of Covert Action first.";
                return null;
        }
        if (IsFixed(item, out var by))
        {
            _message = $"Set by {by} for this run: change it there.";
            _messageColour = Note;
            return null;
        }
        switch (item)
        {
            case Item.GameFolder when cycle:
                _model.CycleFoundFolder(step);
                break;
            case Item.GameFolder or Item.GameChange:
                PickFolder(Item.GameFolder, "Choose the folder of your copy of Covert Action (the one with COVERT.EXE)", _model.GameFolder);
                break;
            case Item.Saves or Item.SavesChange:
                PickFolder(Item.Saves, "Choose the folder for your saved games", _model.SavesFolder ?? _context.DefaultSavesFolder);
                break;
            case Item.Roms or Item.RomsChange:
                PickFolder(Item.Roms, "Choose the folder with your MT-32 (or CM-32L) ROM files", _model.Roms.Folder);
                break;
            case Item.AmigaFiles or Item.AmigaChange:
                PickFolder(Item.AmigaFiles, "Choose the folder with your Amiga files (the disks' folders, or the music, sound and picture files)", _model.Amiga?.Folder);
                break;
            case Item.AmigaOriginalOnly:
                _context.SoundTest?.Stop();
                _model.AmigaOriginalOnly = !_model.AmigaOriginalOnly;
                break;
            case Item.AmigaPictures:
                _model.AmigaPictures = !_model.AmigaPictures;
                break;
            case Item.Window:
                _model.CycleWindow(step);
                break;
            case Item.Fullscreen:
                _model.Fullscreen = !_model.Fullscreen;
                break;
            case Item.Scaling:
                _model.CycleScaling(step);
                break;
            case Item.Sound:
                _context.SoundTest?.Stop();
                _model.CycleSoundCard(step);
                break;
            case Item.Controls:
                _model.CycleControls(step);
                break;
            case Item.KeyRepeat:
                _model.CycleKeyRepeat(step);
                break;
            case Item.ArrowKeysWalk:
                _model.ArrowKeysWalk = !_model.ArrowKeysWalk;
                break;
            case Item.Music:
                _context.SoundTest?.Stop();
                _model.CycleMusicCard(step);
                break;
            case Item.Effects:
                _context.SoundTest?.Stop();
                _model.CycleEffectsCard(step);
                break;
            case Item.MixTest:
                TestMix();
                break;
            case Item.Mouse:
                _model.CycleMouse();
                break;
            case Item.KeyPick:
                _model.CycleKey(step);
                break;
            case Item.KeyBind:
                _capturing = true;
                _message = $"Press the key of your keyboard that should send {_model.ShownKey.Label} (Esc: cancel).";
                _messageColour = Note;
                break;
            case Item.KeyClear:
                _model.ClearKey(_model.ShownKey);
                _message = $"{_model.ShownKey.Label} is sent by its own key only.";
                _messageColour = Note;
                break;
            case Item.KeysReset:
                _model.ResetKeys();
                _message = "Every key is the original's again.";
                _messageColour = Note;
                break;
            case Item.PadPick:
                _model.CyclePad(step);
                break;
            case Item.PadKey:
                _model.CyclePadKey(_model.ShownPad, step);
                break;
            case Item.PadReset:
                _model.ResetPad();
                _message = "The gamepad's buttons send no keys.";
                _messageColour = Note;
                break;
            case Item.CutCapturedDossier:
                _model.CutCapturedDossier = !_model.CutCapturedDossier;
                break;
            case Item.CutStandaloneChase:
                _model.CutStandaloneChase = !_model.CutStandaloneChase;
                break;
            case Item.CutHardestCircuit:
                _model.CutHardestCircuit = !_model.CutHardestCircuit;
                break;
            case Item.CutFillerTexts:
                _model.CutFillerTexts = !_model.CutFillerTexts;
                break;
            case Item.FixOriginalBugs:
                _model.FixOriginalBugs = !_model.FixOriginalBugs;
                break;
            case Item.FixCampaignBugs:
                _model.FixCampaignBugs = !_model.FixCampaignBugs;
                break;
            case Item.Test when _context.SoundTest is { Playing: true } playing:
                playing.Stop();
                break;
            case Item.Test when _model.GameFolderChecked is null:
                _message = "Choose the game folder first: the sound drivers are in it.";
                break;
            case Item.Test when _model.SoundCard is "none":
                _message = "Choose a sound card to test: \"No sound\" plays nothing.";
                _messageColour = Note;
                break;
            case Item.Test:
                _context.SoundTest?.Play(new SoundTestRequest(_model.SoundCard, _model.GameFolderChecked, _model.Mt32Roms));
                break;
        }
        return null;
    }

    /// <summary>The mix's Test: the theme on the music's card, effects on the effects' card (D-620); Stop while it plays.</summary>
    private void TestMix()
    {
        var (music, effects) = (_model.CardOf(_model.MusicCard), _model.CardOf(_model.EffectsCard));
        if (_context.SoundTest is { Playing: true } playing)
        {
            playing.Stop();
        }
        else if (_model.GameFolderChecked is null)
        {
            _message = "Choose the game folder first: the sound drivers are in it.";
        }
        else if (music == "none" && effects == "none")
        {
            _message = "Nothing to test: neither the music nor the effects play on a card.";
            _messageColour = Note;
        }
        else
        {
            var request = new SoundTestRequest(music, _model.GameFolderChecked, _model.Mt32Roms, effects);
            _context.SoundTest?.Play(_model.AmigaMusic || _model.AmigaEffects
                ? request with { SoundCard = _model.SoundCard, AmigaFolder = _model.AmigaFiles, AmigaOriginalOnly = _model.AmigaOriginalOnly }
                : request);
        }
    }

    /// <summary>The system's folder dialog for a folder row; without one, the folder is dropped or pasted instead.</summary>
    private void PickFolder(Item row, string title, string? current)
    {
        if (_surface.PickFolder is not { } pick)
        {
            _message = "Drop the folder on this window, or copy its path and press Ctrl+V.";
            _messageColour = Note;
            return;
        }
        try
        {
            var start = CleanPath(current) is { Length: > 0 } named && Directory.Exists(named) ? named : AppContext.BaseDirectory;
            if (pick(title, start) is { } chosen)
            {
                SetFolder(row, chosen);
            }
        }
        catch (NotSupportedException e)
        {
            _message = $"No folder dialog ({e.Message}): drop the folder on this window, or paste its path (Ctrl+V).";
        }
    }

    private void SetFolder(Item row, string folder)
    {
        if (IsFixed(row, out var by))
        {
            _message = $"Set by {by} for this run: change it there.";
            _messageColour = Note;
            return;
        }
        switch (row)
        {
            case Item.GameFolder or Item.GameChange:
                _model.SetGameFolder(folder, "chosen");
                if (!_model.CanPlay)
                {
                    _message = $"No Covert Action in {folder}.";
                }
                break;
            case Item.Saves or Item.SavesChange:
                _model.SavesFolder = folder;
                break;
            case Item.Roms or Item.RomsChange:
                _model.Mt32Roms = folder;
                if (!_model.Roms.Found)
                {
                    _message = $"No MT-32 or CM-32L ROM files in {folder}.";
                }
                break;
            case Item.AmigaFiles or Item.AmigaChange:
                _model.AmigaFiles = folder;
                if (_model.Amiga is { Found: false, EffectsFound: false } amiga)
                {
                    _message = $"No Amiga files in {folder}: {AmigaMissing(amiga)}; {AmigaEffectsMissing(amiga)}.";
                }
                break;
        }
    }

    /// <summary>A dropped folder: the saves folder while its row is chosen, else the game folder when it holds the game, the ROM folder when it holds ROMs, else the folder row chosen.</summary>
    private void Dropped(string path)
    {
        // a file dropped stands for its folder (the game's COVERT.EXE, a ROM file)
        var folder = File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
        if (_focus is Item.Saves or Item.SavesChange)
        {
            SetFolder(Item.Saves, folder);
        }
        else if (_context.CheckGameFolder(folder) is not null)
        {
            SetFolder(Item.GameFolder, folder);
        }
        else if (_context.DescribeRoms(folder).Found || _focus is Item.Roms or Item.RomsChange)
        {
            SetFolder(Item.Roms, folder);
        }
        else if (_context.DescribeAmiga?.Invoke(folder, null) is { } found && (found.Found || found.EffectsFound || found.PicturesFound) || _focus is Item.AmigaFiles or Item.AmigaChange)
        {
            SetFolder(Item.AmigaFiles, folder);
        }
        else if (_focus is Item.GameFolder or Item.GameChange)
        {
            SetFolder(Item.GameFolder, folder);
        }
        else
        {
            _message = $"{folder} holds none of Covert Action, MT-32 ROM files or Amiga files.";
        }
    }

    /// <summary>A path as typed or pasted: without quotes and spaces around it.</summary>
    internal static string CleanPath(string? text) => (text ?? "").Trim().Trim('"').Trim();

    // ---- drawing ----

    public void Render(Canvas canvas)
    {
        _hotspots = [];
        _blocks = [];
        _cardBlocks = [];
        DrawBackground(canvas);
        DrawTitle(canvas);
        DrawCard(canvas);
        DrawSettings(canvas);
    }

    private void DrawBackground(Canvas canvas)
    {
        if (Art.Get("background") is { } art)
        {
            canvas.Image(art, new Rect(0, 0, canvas.Width, canvas.Height));
            return;
        }
        canvas.Clear(Desk);
        // a fine grid, as on a plotting table
        for (var x = 0; x < canvas.Width; x += 32)
        {
            canvas.Fill(new Rect(x, 0, 1, canvas.Height), DeskLine);
        }
        for (var y = 0; y < canvas.Height; y += 32)
        {
            canvas.Fill(new Rect(0, y, canvas.Width, 1), DeskLine);
        }
        // the player's own game behind it, faint: a picture from the game folder, four times its size, as on the screen
        if (_model.GameFolderChecked is { } folder && _context.Picture is { } read)
        {
            if (_pictureFolder != folder)
            {
                _pictureFolder = folder;
                _picture = read(folder);
            }
            if (_picture is { Length: 320 * 200 } picture)
            {
                // in one colour, as a photograph on a light table: the picture's brightness in steel blue
                var image = new Image(320, 200, [.. picture.Select(Duotone)]);
                var place = new Rect(0, (canvas.Height - 800) / 2, 1280, 800);
                canvas.Image(image, place, 0.5);
            }
        }
        else
        {
            _pictureFolder = null;
            _picture = null;
        }
    }

    /// <summary>A colour of the player's picture as the background shows it: its brightness, from the desk's colour to steel blue.</summary>
    private static uint Duotone(uint rgb)
    {
        var light = (int)((((rgb >> 16) & 0xFF) * 30 + ((rgb >> 8) & 0xFF) * 59 + (rgb & 0xFF) * 11) / 100);
        return 0xFF000000 | Colours.Mix(Desk, 0x3C6E9E, light);
    }

    private void DrawTitle(Canvas canvas)
    {
        if (Art.Get("title") is { } art)
        {
            canvas.Image(art, new Rect(32, 24, 544, 96));
            return;
        }
        const string title = "COVERT ACTION";
        _font.Draw(canvas, title, 36, 30, Shadow, 4, bold: true);
        _font.Draw(canvas, title, 32, 26, Amber, 4, bold: true);
        canvas.Fill(new Rect(32, 96, BitmapFont.Measure(title, 4), 2), AmberDark);
        _font.Draw(canvas, "C# rewrite  ·  unofficial fan project  ·  version " + _context.Version, 32, 104, TextSoft);
    }

    private void DrawCard(Canvas canvas)
    {
        var card = CardFrame;
        // the case file: a tab at its top left, a shadow under it
        canvas.Fill(new Rect(card.X + 6, card.Y + 6, card.Width, card.Height), Shadow);
        var tab = new Rect(card.X, card.Y - 18, 200, 20);
        canvas.Fill(tab, ManilaTab);
        canvas.Frame(tab, 1, ManilaEdge);
        if (Art.Get("card") is { } art)
        {
            canvas.Image(art, card);
        }
        else
        {
            canvas.Paint(card, (x, y) => Colours.Mix(Manila, ManilaEdge, (int)(((x * 7 + y * 13) % 17) * 2.5)));
            canvas.Frame(card, 2, ManilaEdge);
        }
        _font.Draw(canvas, BitmapFont.Fit("CASE FILE " + _context.Version, tab.Width - 16, fromEnd: false), tab.X + 12, tab.Y + 2, InkSoft);

        var x = card.X + 24;
        var width = card.Width - 48;
        var y = card.Y + 18;
        var heading = $"WHAT'S NEW IN {_context.Version}";
        _font.Draw(canvas, heading, x, y, InkRed, 2, bold: true);
        CardLeaf(new Rect(x, y, BitmapFont.Measure(heading, 2), 32), "heading new");
        y += 40;

        // the small print at the bottom, the about text above it; the notes take what room is left
        const string smallPrint = "Covert Action © 1990 MicroProse Software, Inc. This program is an unofficial fan project: it is not affiliated with, endorsed or supported by MicroProse or the game's rights holders. Free software under the GNU GPL v3: see LICENSE and THIRD_PARTY_NOTICES.md beside the program.";
        const string about = "A rewrite of Sid Meier's Covert Action (1990) in C#, made from the original's own code, part by part, to play as it did. It reads your own copy of the game: none of the game's files come with it, and your copy is never changed.";
        var printLines = BitmapFont.Wrap(smallPrint, width);
        var aboutLines = BitmapFont.Wrap(about, width);
        var printTop = card.Bottom - 16 - printLines.Count * 16;
        var aboutTop = printTop - 14 - aboutLines.Count * 16;
        var aboutHeading = aboutTop - 32;

        var room = aboutHeading - 12;
        var shown = 0;
        foreach (var (title, detail) in _notes)
        {
            var titleLines = BitmapFont.Wrap(title, width - 16);
            var detailLines = detail.Length == 0 ? [] : BitmapFont.Wrap(detail, width - 16);
            var height = (titleLines.Count + detailLines.Count) * 16 + 6;
            if (y + height > room)
            {
                break;
            }
            _font.Draw(canvas, "■", x, y, InkRed);
            for (var i = 0; i < titleLines.Count; i++)
            {
                _font.Draw(canvas, titleLines[i], x + 16, y + i * 16, Ink, bold: true);
            }
            for (var i = 0; i < detailLines.Count; i++)
            {
                _font.Draw(canvas, detailLines[i], x + 16, y + (titleLines.Count + i) * 16, InkSoft);
            }
            CardLeaf(new Rect(x, y, width, height - 6), "note " + title);
            y += height;
            shown++;
        }
        if (_notes.Count == 0)
        {
            const string none = "The release notes (RELEASE_NOTES.md) are not beside the program.";
            foreach (var line in BitmapFont.Wrap(none, width))
            {
                _font.Draw(canvas, line, x, y, InkSoft);
                y += 16;
            }
            CardLeaf(new Rect(x, y - 16, width, 16), "no notes");
        }
        else if (shown < _notes.Count && y + 16 <= room)
        {
            _font.Draw(canvas, $"... and {_notes.Count - shown} more in RELEASE_NOTES.md.", x + 16, y, InkSoft);
            CardLeaf(new Rect(x + 16, y, width - 16, 16), "more notes");
        }

        canvas.Fill(new Rect(x, aboutHeading - 6, width, 1), ManilaEdge);
        _font.Draw(canvas, "ABOUT THIS PROJECT", x, aboutHeading, InkRed, bold: true);
        CardLeaf(new Rect(x, aboutHeading, BitmapFont.Measure("ABOUT THIS PROJECT"), 16), "heading about");
        for (var i = 0; i < aboutLines.Count; i++)
        {
            _font.Draw(canvas, aboutLines[i], x, aboutTop + i * 16, Ink);
        }
        CardLeaf(new Rect(x, aboutTop, width, aboutLines.Count * 16), "about");
        for (var i = 0; i < printLines.Count; i++)
        {
            _font.Draw(canvas, printLines[i], x, printTop + i * 16, InkSoft);
        }
        CardLeaf(new Rect(x, printTop, width, printLines.Count * 16), "small print");
    }

    private void DrawSettings(Canvas canvas)
    {
        var frame = PanelFrame;
        canvas.Fill(new Rect(frame.X + 6, frame.Y + 6, frame.Width, frame.Height), Shadow);
        if (Art.Get("panel") is { } art)
        {
            canvas.Image(art, frame);
        }
        else
        {
            canvas.Fill(frame, Slate);
            canvas.Frame(frame, 1, SlateEdge);
            canvas.Fill(new Rect(frame.X, frame.Y, frame.Width, 3), Amber);
        }
        var x0 = frame.X + 24;
        var x1 = frame.Right - 24;
        var label = x0 + 8;
        var select = x0 + 192;
        var buttonX = x1 - ButtonWidth;
        var selectEnd = buttonX - 12;

        var y = frame.Y + 16;
        _font.Draw(canvas, "SETTINGS", x0, y, Text, 2, bold: true);
        Leaf(new Rect(x0, y, BitmapFont.Measure("SETTINGS", 2), 32), "title");
        y += 40;
        y = Tabs(canvas, x0, x1, y);

        int Section(string name, int top)
        {
            top += SectionGap;
            var ty = top + (HeadingHeight - 16) / 2;
            _font.Draw(canvas, name.ToUpperInvariant(), x0, ty, Amber, bold: true);
            Leaf(new Rect(x0, ty, BitmapFont.Measure(name) + 1, 16), "heading " + name);
            return top + HeadingHeight;
        }

        int Status(int top, (string Text, uint Colour) status, bool fromEnd = false)
        {
            var ty = top + (StatusHeight - 16) / 2;
            var mark = status.Colour == Good ? "√ " : status.Colour == Bad ? "! " : "";
            var fitted = BitmapFont.Fit(mark + status.Text, x1 - label, fromEnd && mark.Length == 0);
            _font.Draw(canvas, fitted, label, ty, status.Colour);
            Leaf(new Rect(label, ty, Math.Max(1, BitmapFont.Measure(fitted)), 16), "status " + status.Text);
            return top + StatusHeight;
        }

        void Button(Rect area, string text, Item item, bool primary = false)
        {
            var lit = _hover == item || _focus == item;
            var down = _pressed == item;
            var fill = primary ? (lit ? 0xF0BC5C : Amber) : lit ? ButtonLit : ButtonFill;
            if (down)
            {
                fill = Colours.Shade(fill, -0.25);
            }
            canvas.Fill(area, fill);
            canvas.Frame(area, 1, primary ? AmberDark : ButtonEdge);
            if (_focus == item)
            {
                canvas.Frame(area.Inset(-3), 1, primary ? Amber : Text);
            }
            var scale = area.Height >= 40 ? 2 : 1;
            var shown = BitmapFont.Fit(text, area.Width - 8, fromEnd: false, scale);
            var colour = primary ? (_model.CanPlay ? 0x1A1206u : 0x5A4A30u) : Text;
            _font.Draw(canvas, shown, area.X + (area.Width - BitmapFont.Measure(shown, scale)) / 2 + (down ? 1 : 0),
                area.Y + (area.Height - BitmapFont.LineHeight(scale)) / 2 + (down ? 1 : 0), colour, scale, bold: scale == 1 && primary);
            Hot(area, item, Part.Row);
            Leaf(area.Inset(-3), "button " + text);
        }

        void RowLight(Rect row, Item item)
        {
            if (_focus == item)
            {
                canvas.Fill(row, SlateFocus);
                canvas.Fill(new Rect(row.X, row.Y, 2, row.Height), Amber);
            }
            else if (_hover == item)
            {
                canvas.Fill(row, SlateRow);
            }
        }

        // a row with its value between ‹ ›, and maybe a button at its end (Test sound)
        void ButtonRow(int top, params (string Text, Item Item)[] buttons)
        {
            var bx = label;
            foreach (var (text, item) in buttons)
            {
                Button(new Rect(bx, top + (RowHeight - ButtonHeight) / 2, 168, ButtonHeight), text, item);
                bx += 184;
            }
        }

        int Select(int top, string name, string value, Item item, string? button = null, Item buttonItem = Item.None, bool arrows = true)
        {
            var row = new Rect(label - 8, top, selectEnd - label + 16, RowHeight);
            RowLight(row, item);
            Hot(row, item, Part.Row);
            var ty = top + (RowHeight - 16) / 2;
            var fixedBy = IsFixed(item, out _);
            var shownName = BitmapFont.Fit(name, select - label - 8, fromEnd: false);
            _font.Draw(canvas, shownName, label, ty, fixedBy ? TextSoft : Text);
            Leaf(new Rect(label, ty, BitmapFont.Measure(shownName), 16), "label " + name);
            var inner = selectEnd - select - 2 * 16;
            var shown = BitmapFont.Fit(value, inner, fromEnd: false);
            if (!fixedBy && arrows)
            {
                _font.Draw(canvas, "‹", select, ty, Amber);
                _font.Draw(canvas, "›", selectEnd - 8, ty, Amber);
                Hot(new Rect(select - 6, top, 22, RowHeight), item, Part.Previous);
                Hot(new Rect(selectEnd - 14, top, 22, RowHeight), item, Part.Next);
            }
            _font.Draw(canvas, shown, select + 16 + (inner - BitmapFont.Measure(shown)) / 2, ty, fixedBy ? TextSoft : 0xFFFFFF);
            Leaf(new Rect(select, ty, selectEnd - select, 16), "value " + name);
            if (button is not null)
            {
                Button(new Rect(buttonX, top + (RowHeight - ButtonHeight) / 2, ButtonWidth, ButtonHeight), button, buttonItem);
            }
            return top + RowHeight;
        }

        // a folder: its field (the path cut at its start, to keep its end) and its Change… button
        int Folder(int top, string? name, string path, Item item, Item change)
        {
            var fieldLeft = name is null ? label : select;
            var row = new Rect(label - 8, top, selectEnd - label + 16, RowHeight);
            RowLight(row, item);
            if (name is not null)
            {
                var ty = top + (RowHeight - 16) / 2;
                _font.Draw(canvas, name, label, ty, Text);
                Leaf(new Rect(label, ty, BitmapFont.Measure(name), 16), "label " + name);
            }
            var field = new Rect(fieldLeft, top + 2, selectEnd - fieldLeft, RowHeight - 4);
            canvas.Fill(field, FieldFill);
            canvas.Frame(field, 1, _focus == item ? Amber : SlateEdge);
            var fixedBy = IsFixed(item, out _);
            var shown = BitmapFont.Fit(path, field.Width - 12, fromEnd: true);
            _font.Draw(canvas, shown, field.X + 6, field.Y + (field.Height - 16) / 2, fixedBy ? TextSoft : Text);
            Hot(field, item, Part.Row);
            Leaf(field, "field " + (name ?? "game folder"));
            Button(new Rect(buttonX, top + (RowHeight - ButtonHeight) / 2, ButtonWidth, ButtonHeight), "Change…", change);
            return top + RowHeight;
        }

        if (_controls)
        {
            y = Section("Keyboard and mouse", y);
            y = Select(y, "Key repeat", LauncherModel.Label(LauncherModel.KeyRepeats, _model.KeyRepeat), Item.KeyRepeat);
            y = Select(y, "Arrow keys walk", _model.ArrowKeysWalk ? "Yes" : "No", Item.ArrowKeysWalk);
            y = Select(y, "Mouse", LauncherModel.Label(LauncherModel.MouseChoices, _model.MouseChoice), Item.Mouse);

            y = Section("Keyboard: send the original's keys from other keys", y);
            y = Status(y, ("Not in the original. Empty by default: every key is the original's.", Note));
            var shown = _model.ShownKey;
            y = Select(y, "Original key", shown.Label, Item.KeyPick);
            var sending = string.Join(", ", _model.KeysSending(shown).Select(k => k.Label));
            y = Select(y, "Sent by", _capturing ? "Press a key..." : sending.Length > 0 ? sending : "its own key only", Item.KeyBind, "Set...", Item.KeyBind, arrows: false);
            ButtonRow(y, ("Clear this key", Item.KeyClear), ("Reset all keys", Item.KeysReset));
            y += RowHeight;
            y = Status(y, (_model.KeyMap.Count == 0 ? "No key remapped." : $"{_model.KeyMap.Count} key(s) remapped: " + string.Join(", ", _model.KeyMap.Select(p => KeyBindings.Find(p.From)!.Label + " = " + KeyBindings.Find(p.To)!.Label)), Note));

            y = Section("Gamepad buttons", y);
            y = Status(y, ("The stick and face buttons are the joystick; the others can send keys.", Note));
            var pad = _model.ShownPad;
            y = Select(y, "Gamepad button", pad.Label, Item.PadPick);
            y = Select(y, "Sends the key", _model.PadTarget(pad)?.Label ?? "(nothing)", Item.PadKey);
            ButtonRow(y, ("Reset buttons", Item.PadReset));
            y += RowHeight;
            y = Status(y, (_model.PadMap.Count == 0 ? "No button sends a key." : string.Join(", ", _model.PadMap.Select(p => KeyBindings.FindPad(p.From)!.Label + " = " + KeyBindings.Find(p.To)!.Label)), Note));
        }
        else if (_cut)
        {
            y = Section("The original's cut content", y);
            y = Status(y, ("Not in the original: its own files hold these, its programs never use them. All off by default.", Note));
            y = Select(y, "Captured dossier", _model.CutCapturedDossier ? "Yes" : "No", Item.CutCapturedDossier);
            y = Select(y, "Free car chase", _model.CutStandaloneChase ? "Yes" : "No", Item.CutStandaloneChase);
            y = Select(y, "Hardest circuit board", _model.CutHardestCircuit ? "Yes" : "No", Item.CutHardestCircuit);
            y = Select(y, "All filler sentences", _model.CutFillerTexts ? "Yes" : "No", Item.CutFillerTexts);
        }
        else if (!_advanced)
        {
            y = Section("Game installation", y);
            y = Folder(y, null, _model.GameFolder ?? "(not chosen)", Item.GameFolder, Item.GameChange);
            y = Status(y, GameStatus());

            y = Section("Display", y);
            y = Select(y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
            y = Select(y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen);
            y = Select(y, "Picture scaling", LauncherModel.Label(LauncherModel.Scalings, _model.Scaling), Item.Scaling);

            y = Section("Sound and controls", y);
            var test = _context.SoundTest is { } soundTest ? (soundTest.Playing ? "Stop" : "Test sound") : null;
            y = Select(y, "Sound card", LauncherModel.Label(LauncherModel.SoundCards, _model.SoundCard), Item.Sound, test, Item.Test);
            y = Status(y, SoundStatus());
            y = Select(y, "Controls", LauncherModel.Label(LauncherModel.ControlChoices, _model.Controls), Item.Controls);

            y = Section("Saved games", y);
            var saves = _model.SavesFolder ?? _context.DefaultSavesFolder;
            y = Folder(y, "Saves folder", saves, Item.Saves, Item.SavesChange);
            y = Status(y, ((_model.SavesFolder is not null ? "Your own folder." : _context.DefaultSavesFolder.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) ? "The default: beside the program." : "The default folder of your account (the program's folder is read-only).") + " The game folder is never written to.", Note));
        }
        else
        {
            y = Section("Roland MT-32", y);
            y = Folder(y, "ROM folder", _model.Roms.Folder, Item.Roms, Item.RomsChange);
            y = Status(y, RomStatusLine());
            y = Section("Amiga music, effects and pictures", y);
            y = Folder(y, "Amiga files", _model.Amiga?.Folder ?? _model.AmigaFiles ?? AmigaDefaultFolder, Item.AmigaFiles, Item.AmigaChange);
            y = Status(y, AmigaStatusLine());
            y = Select(y, "Amiga's sounds only", _model.AmigaOriginalOnly ? "Yes" : "No", Item.AmigaOriginalOnly);
            y = Select(y, "Amiga pictures", _model.AmigaPictures ? "Yes" : "No", Item.AmigaPictures);
            y = Section("Music and effects on two sound cards", y);
            var mixTest = _context.SoundTest is { } mixSound ? (mixSound.Playing ? "Stop" : "Test mix") : null;
            y = Select(y, "Music on", LauncherModel.Label(LauncherModel.MusicChoices, _model.MusicCard), Item.Music, mixTest, Item.MixTest);
            y = Select(y, "Effects on", LauncherModel.Label(LauncherModel.EffectsChoices, _model.EffectsCard), Item.Effects);
            y = Status(y, MixStatus());
            y = Section("Fix the original's bugs", y);
            y = Select(y, "Story-safe bugs", _model.FixOriginalBugs ? "Yes" : "No", Item.FixOriginalBugs);
            y = Select(y, "Campaign-logic bugs", _model.FixCampaignBugs ? "Yes" : "No", Item.FixCampaignBugs);
        }

        // the foot: Play at the right, Save and quit at the left
        var play = new Rect(x1 - 200, frame.Bottom - 20 - 44, 200, 44);
        Button(play, "PLAY", Item.Play, primary: true);
        Button(new Rect(x0 + 3, play.Y + 6, 184, 32), "Save and quit", Item.Save);

        // the details box: the last message, else what the row under the pointer (or chosen) is
        var box = new Rect(x0, Math.Max(y + 12, play.Y - 16 - 120), x1 - x0, 0);
        box = box with { Height = play.Y - 16 - box.Y };
        DrawDetails(canvas, box);
    }

    private int Tabs(Canvas canvas, int x0, int x1, int y)
    {
        const int height = 24;
        var x = x0;
        foreach (var (name, item, open) in new[] { ("General", Item.TabGeneral, !_advanced && !_cut && !_controls), ("Advanced", Item.TabAdvanced, _advanced), ("Cut content", Item.TabCutContent, _cut), ("Controls", Item.TabControls, _controls) })
        {
            var width = BitmapFont.Measure(name);
            if (!open && _hover == item)
            {
                canvas.Fill(new Rect(x - 6, y, width + 12, height - 2), SlateRow);
            }
            _font.Draw(canvas, name, x, y + 3, open ? Text : TextSoft, bold: open);
            if (open)
            {
                canvas.Fill(new Rect(x - 2, y + height - 4, width + 4, 3), Amber);
            }
            Hot(new Rect(x - 8, y, width + 16, height), item, Part.Row);
            Leaf(new Rect(x - 2, y + 2, width + 4, height - 2), "tab " + name);
            x += width + 32;
        }
        canvas.Fill(new Rect(x0, y + height - 1, x1 - x0, 1), SlateEdge);
        return y + height;
    }

    private void DrawDetails(Canvas canvas, Rect box)
    {
        if (box.Height < 36)
        {
            return;
        }
        canvas.Fill(box, FieldFill);
        canvas.Frame(box, 1, SlateEdge);
        Leaf(box, "details");
        var item = _hover != Item.None ? _hover : _focus;
        var parts = new List<(string Text, uint Colour)>();
        if (_message is not null)
        {
            parts.Add((_message, _messageColour));
        }
        if (Help(item) is { Length: > 0 } help)
        {
            parts.Add((help, Note));
        }
        var lines = parts.SelectMany(p => BitmapFont.Wrap(p.Text, box.Width - 20).Select(l => (Text: l, p.Colour))).ToList();
        var fit = (box.Height - 12) / 16;
        if (lines.Count > fit)
        {
            lines = lines.Take(fit).ToList();
            var last = lines[^1];
            lines[^1] = (BitmapFont.Fit(last.Text + " …", box.Width - 20, fromEnd: false), last.Colour);
            if (!lines[^1].Text.EndsWith("...", StringComparison.Ordinal))
            {
                lines[^1] = (BitmapFont.Fit(last.Text, box.Width - 44, fromEnd: false) + "...", last.Colour);
            }
        }
        DetailsShown = string.Join("\n", lines.Select(l => l.Text));
        for (var i = 0; i < lines.Count; i++)
        {
            _font.Draw(canvas, lines[i].Text, box.X + 10, box.Y + 6 + i * 16, lines[i].Colour);
        }
    }

    private (string Text, uint Colour) GameStatus()
    {
        if (_model.GameFolderChecked is { } found)
        {
            var source = _model.GameFolderSource is { } s && s != "chosen" ? s.StartsWith("set by", StringComparison.Ordinal) ? $"; {s}" : $" ({s})" : "";
            return (SameFolder(found, _model.GameFolder) ? $"Found{source}." : $"Found in {found}{source}.", Good);
        }
        if (_model.GameFolder is null)
        {
            return ("Not found: press Change… or drop the game's folder on this window.", Bad);
        }
        var missing = GameFolderSearch.Missing(_model.GameFolder);
        return ($"No Covert Action there ({string.Join(", ", missing.Take(3))}{(missing.Count > 3 ? " …" : "")} missing).", Bad);
    }

    private static bool SameFolder(string a, string? b)
    {
        try
        {
            return b is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private (string Text, uint Colour) SoundStatus()
    {
        if (_context.SoundTest is { Status: { } status } test)
        {
            return (status, test.Playing ? Good : Bad);
        }
        return _model.SoundCard switch
        {
            "none" => ("No sound.", Note),
            "mt32" => RomStatusLine(),
            "speaker" => ("Plays on your game's PC speaker driver (ISOUND.CA).", Note),
            "tandy" => ("Plays on your game's Tandy driver (TSOUND.CA).", Note),
            _ => ("Plays on your game's AdLib driver (ASOUND.CA).", Note),
        };
    }

    /// <summary>The line under the mix's rows: off, or what plays where (D-620), or the mix test's status.</summary>
    private (string Text, uint Colour) MixStatus()
    {
        if (_context.SoundTest is { Status: { } status } test && (_focus is Item.Music or Item.MixTest or Item.Effects))
        {
            return (status, test.Playing ? Good : Bad);
        }
        if (!_model.Mixing)
        {
            return ("Off: the sound card plays the music and the effects, as the original.", Note);
        }
        string Card(string part) => part == "card"
            ? LauncherModel.Label(LauncherModel.SoundCards, _model.SoundCard)
            : LauncherModel.Label(LauncherModel.MusicChoices, part);
        var amiga = _model.AmigaMusic ? _model.AmigaEffects ? "D-640, D-641" : "D-640" : _model.AmigaEffects ? "D-641" : "D-620";
        return ($"Music: {Card(_model.MusicCard)}; effects: {Card(_model.EffectsCard)}; mixed ({amiga}).", Note);
    }

    /// <summary>The folder shown for the Amiga files when the host cannot look: the one the files are put in.</summary>
    private static string AmigaDefaultFolder => Path.Combine(AppContext.BaseDirectory, "amiga", "files");

    /// <summary>
    /// The line under the Amiga files' folder: the music files and the sound banks found (and where), or what is missing and what
    /// plays instead when the Amiga music or effects are chosen without them (D-640, D-641).
    /// </summary>
    private (string Text, uint Colour) AmigaStatusLine()
    {
        if (_model.Amiga is not { } amiga)
        {
            return ("Your Amiga files: the music from disk 1, the sound effects (*snd.dat) from disks 2 and 3, the pictures (*.pix) from all three.", Note);
        }
        var where = amiga.Source is { } source ? $" ({source})" : "";
        // the three kinds of file: what is there, what is wanted of it, and what plays or shows instead when it is not
        var kinds = new[]
        {
            (Name: "music", Found: amiga.Found, Wanted: _model.AmigaMusic, Missing: AmigaMissing(amiga), Instead: "The sound card's music plays instead."),
            (Name: "sound effect", Found: amiga.EffectsFound, Wanted: _model.AmigaEffects, Missing: AmigaEffectsMissing(amiga), Instead: "The sound card's effects play instead."),
            (Name: "picture", Found: amiga.PicturesFound, Wanted: _model.AmigaPictures, Missing: AmigaPicturesMissing(amiga), Instead: "The PC's pictures show instead."),
        };
        var found = kinds.Where(kind => kind.Found).Select(kind => kind.Name).ToList();
        var lacking = kinds.Where(kind => !kind.Found).ToList();
        var bad = lacking.Any(kind => kind.Wanted);
        if (lacking.Count == 0)
        {
            return ($"Amiga music, sound effect and picture files found{where}.", kinds.Any(kind => kind.Wanted) ? Good : Note);
        }
        var text = found.Count == 0
            ? $"No Amiga files there ({string.Join("; ", lacking.Select(kind => $"{kind.Name}s: {kind.Missing}"))})."
            : $"Amiga {string.Join(", ", found)} files found{where}; {string.Join("; ", lacking.Select(kind => $"no {kind.Name} files ({kind.Missing})"))}.";
        return (text + string.Concat(lacking.Where(kind => kind.Wanted).Select(kind => " " + kind.Instead)), bad ? Bad : Note);
    }

    /// <summary>What the Amiga pictures lack: the first of the files the folder does not have, and how many others (D-642).</summary>
    private static string AmigaPicturesMissing(AmigaStatus amiga)
    {
        var missing = amiga.PicturesMissing ?? [];
        return missing.Count == 0 ? "faces.pix missing" : missing.Count == 1 ? missing[0] + " missing" : $"{missing[0]} and {missing.Count - 1} more missing";
    }

    private static string AmigaEffectsMissing(AmigaStatus amiga) =>
        amiga.EffectsMissing is { Count: > 0 } missing ? string.Join(", ", missing) + " missing" : "gamesnd.dat missing";

    private static string AmigaMissing(AmigaStatus amiga) =>
        amiga.Missing is { Count: > 0 } missing ? string.Join(", ", missing) + " missing" : "thememusic.dat missing";

    private (string Text, uint Colour) RomStatusLine()
    {
        var roms = _model.Roms;
        var where = roms.Source is { } source ? $" ({source} folder)" : "";
        if (roms.Mt32)
        {
            return ($"MT-32 ROMs found{where}.", Good);
        }
        if (roms.Cm32l)
        {
            return ($"CM-32L ROMs found{where} (no MT-32's): the CM-32L plays.", Good);
        }
        return ("No MT-32 ROMs there: the AdLib driver plays instead (D-20).", Bad);
    }

    private string Help(Item item)
    {
        var fixedNote = IsFixed(item, out var by) ? $" Set by {by} for this run." : "";
        return item switch
        {
            Item.GameFolder or Item.GameChange =>
                "Your copy of Sid Meier's Covert Action: the folder with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV (or a folder above it). None of the game comes with this program, and nothing in that folder is changed. Change… opens a folder dialog; you can also drop the folder on this window or paste its path (Ctrl+V); ←/→ go through the folders found." + fixedNote,
            Item.Window => "The window's size when the game starts; the picture keeps the original's 4:3 shape. The window can still be resized while you play." + fixedNote,
            Item.Fullscreen => "Yes: the game fills the screen, the picture in its 4:3 shape with black bars beside it." + fixedNote,
            Item.Scaling => "How the 320x200 picture is enlarged. Pixel art: every game pixel the same size, only the edges between them blended (the default). Nearest: hard pixels, some a window pixel wider than others. Smooth: blurred." + fixedNote,
            Item.Sound => "The sound card the game plays on: the answer to the original's set-up question, which the game no longer asks (AdLib by default). The MT-32 is Munt's emulation with your own ROM files (Advanced tab)." + fixedNote,
            Item.Test => "Plays the theme tune for a few seconds on the chosen card, through your game's own sound driver.",
            Item.Controls => "Keyboard and joystick (the default): a joystick or gamepad works as the PC's joystick, and the game first asks you to hold it at its corners to calibrate it. Keyboard only: no calibration, the joystick is not read. The answer to the original's set-up question, which the game no longer asks." + fixedNote,
            Item.Saves or Item.SavesChange => $"Where your saved games (CV0.SVE to CV3.SVE) and the Hall of Fame are written and read back; a save this folder does not hold is read from your game folder, so the saves you made with the original can be loaded. Default: {_context.DefaultSavesFolder} (Saves beside the program, or in your account when the program's folder cannot be written to)." + fixedNote,
            Item.Roms or Item.RomsChange => "The folder with your Roland MT-32 (or CM-32L) ROM files, for the Roland MT-32 sound card. None come with the program. Without them the AdLib driver plays instead (divergence D-20). Default: MT32_ROMS beside the program, else where they are found (next to the game, DOSBox's folders)." + fixedNote,
            Item.KeyRepeat => "PC keyboard: a held key repeats as on the PC of 1990 (after half a second, 10.9 times a second), so the agent stops when you let go. Windows: your Windows keyboard's delay and rate (faster: keys can pile up in a building)." + fixedNote,
            Item.ArrowKeysWalk => "Yes: in a building raid the grey arrow keys (and Home, End, PgUp, PgDn) walk the agent like the number keypad, and Shift with an arrow lunges. No (the default, as the original): the keypad walks and the grey arrows lunge. Divergence D-610; no other part of the game is changed." + fixedNote,
            Item.Music => "The tunes: the theme, the selection screens' tune, the vacations. The sound card (the default) plays all sound, as the original. Amiga: the Amiga version's music from your own Amiga files (D-640); without them the sound card plays. A card here plays the tunes while the effects play on the card below (D-620)." + fixedNote,
            Item.AmigaFiles or Item.AmigaChange => "The folder with your Amiga files, for the Amiga choices of the music, the effects and the pictures: the music (thememusic.dat, finalmusic.dat and spy6music.dat from the Amiga version's disk 1), the sound effects (gamesnd.dat, tacsnd.dat, codesnd.dat and bugsnd.dat from disk 2, chasesnd.dat from disk 3) and the pictures (the .pix files of the three disks). Give the folder with the disk folders (disk1, disk2, disk3) in it, or a folder with the files. None come with the program. Default: amiga/files beside the program, or beside the game." + fixedNote,
            Item.AmigaOriginalOnly => "Yes: the Amiga music and effects play only where the Amiga game had them. Music: the title, the menus and FINAL; the selection screens' and the vacations' tunes stay silent. Effects: the sounds the Amiga game has no twin for (the raid's gunfire, the car's engines, ...) stay silent. No (the default): the sound card plays those, and the music plays the Amiga's tunes there too." + fixedNote,
            Item.Effects => "The card the sound effects play on: the aeroplane, the cars, shots, doors. The sound card (the default) plays them. Amiga: the Amiga version's effects from your own Amiga files where it has them, the sound card's elsewhere (D-641). A card here plays them while the music plays on the card above (D-620); None plays none." + fixedNote,
            Item.MixTest => "Plays the theme on the music's card and a shot, the Uzi and a door on the effects' card, for a few seconds, through your game's own sound drivers.",
            Item.AmigaPictures => "Not in the original (D-642). Yes: the Amiga version's faces, icons, maps, hotel and most city pictures, in the Amiga's colours, read from your own Amiga files (the .pix files; none come with the program); pictures the Amiga redrew stay the PC's. No (the default): the PC's pictures, also when the files are missing." + fixedNote,
            Item.Mouse => "Not in the original. Menus: the mouse works in the menus, the lists, the maps and the building raid: point at a choice to highlight it, left click to take it, right click for Esc; a click also answers \"press a key\". Menus and car chase: also in the car chase, where a click stands for a key: on the map or the street view the arrow of that side of the car, the windscreen shifts up and the bonnet down, a right click the arrest key. The keys work as before. No: no mouse, as the original (the default)." + fixedNote,
            Item.FixOriginalBugs => "Not in the original. Yes: the original's bugs that do not change the story are fixed (a clue's text, a key left in the buffer, the Delete key of the name field, the chief's comment after a capture, the Tandy driver's second pause, and others the release notes list). A game saved this way loads in the original. No (the default): the original as it was played in 1990." + fixedNote,
            Item.FixCampaignBugs => "Not in the original. Yes: also the bugs of the campaign's logic are fixed; this changes the story's events. No (the default): as the original." + fixedNote,
            Item.CutCapturedDossier => "Not in the original (D-660). Yes: when a cast member is captured, the dossier page stamped CAPTURED that the game ships (CAPTRD01.PAN) is shown after the arrest, before the chief's comment, with the photograph of a man or of a woman. No (the default): as the original, which never plays it." + fixedNote,
            Item.CutStandaloneChase => "Not in the original (D-661). Yes: the practice menu has a fifth choice, \"Free Chase\": the car chase on its own, as the game's chase program still carries it: a new random map every time, the places labelled Loc1, Loc2 and so on, a random start and goal, and every place shown and open to enter with F2. It stays in practice: nothing is handed to a campaign. No (the default): the four practice skills of the original." + fixedNote,
            Item.CutHardestCircuit => "Not in the original (D-662). Yes: the wiretap's and the car tracer's circuit board has one difficulty more, the fourth that the game's own code carries and never uses: on the hardest level an agent of the lowest electronics rating gets a board that shows only its last column of chips and hides eight fixed ones. Every other level and rating is as before. No (the default): the hardest board is the third, as the original." + fixedNote,
            Item.CutFillerTexts => "Not in the original (D-663). Yes: the filler sentence of an intercepted coded message is one of all sixteen that the game's TEXT.DTA holds; the game's code asks for twelve, so four sentences (\"Extreme caution is advised ...\", \"Do not hesitate to act in a decisive manner\", \"Your request for promotion is being considered\", \"Further communications may follow\") are never shown. No (the default): the twelve, as the original." + fixedNote,
            Item.KeyPick => "Not in the original (D-671). Choose a key of the original (the ones the game reads, as they are on the PC keyboard it was made for), then Set... and press the key of your keyboard that should send it. The key keeps working as before as well. Several keys can send the same one.",
            Item.KeyBind => "Not in the original (D-671). Set... (or Enter) waits for the key to use: it is then taken as the original's key before the game sees it, the scan code and the character of that key. Esc cancels; Shift, Ctrl and Alt cannot be remapped.",
            Item.KeyClear => "Takes away every key mapped to the key of the original shown.",
            Item.KeysReset => "Takes away all the key mappings: every key is the original's again.",
            Item.PadPick => "Not in the original (D-672). The gamepad's shoulders, triggers, Start, Back and stick clicks. The stick, the directional pad and the four face buttons stay the joystick's, as before (calibrated at the start when Controls is Keyboard and joystick).",
            Item.PadKey => "The key of the original the button types when pressed (once for each press, no repeat). (nothing): the button does nothing, as the original. Ideas: Start = Esc, Back = F1.",
            Item.PadReset => "Makes every gamepad button send nothing again.",
            Item.TabGeneral or Item.TabAdvanced or Item.TabCutContent or Item.TabControls => "General: the game, the display, the sound card, the controls and the saves. Advanced: the MT-32 ROMs, the Amiga files, the keyboard and the player options. Cut content: scenes and modes the original's files hold and its programs never reach. Controls: map keys of your keyboard and gamepad buttons to the original's keys. Everything of Advanced, Cut content and Controls is off by default and not in the original. Ctrl+Tab goes to the next tab.",
            Item.Save => _context.SettingsPath is { } path ? $"Saves the settings into {path} and closes. Esc and the window's close button save too." : "Closes (no settings file can be written here: the changes are lost).",
            Item.Play => _model.CanPlay ? "Saves the settings and starts the game." : "Choose the folder of your copy of the game first.",
            _ => "",
        };
    }

    private void Hot(Rect area, Item item, Part part) => _hotspots.Add((area, item, part));

    private void Leaf(Rect area, string what) => _blocks.Add((area, what));

    private void CardLeaf(Rect area, string what) => _cardBlocks.Add((area, what));
}
