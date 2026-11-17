// New code (D-671, D-672): the player's key remapping and the gamepad's extra buttons.
using System.Collections.ObjectModel;

namespace CovertAction.Settings;

/// <summary>One key the settings can name: its stable name, its SDL scancode (the key's place, whatever the layout), its label, and whether it is a key of the original.</summary>
internal sealed record KeyName(string Name, int Scancode, string Label, bool Original);

/// <summary>A gamepad button beyond the joystick's four: its name in the settings, its label and SDL's number (a trigger is an axis: <see cref="KeyBindings.TriggerBase"/> + the axis).</summary>
internal sealed record PadButton(string Name, string Label, int Id);

/// <summary>
/// The names of keys and gamepad buttons, and the settings' pairs "physical=original" (<c>input.keyMap</c>, D-671) and
/// "button=original" (<c>input.padMap</c>, D-672). The original's key is named by the key it is on a US keyboard (the PC keyboard the
/// game was made for): the game then receives what that key gives, its scan code and character. Empty lists leave the game as the original.
/// </summary>
internal static class KeyBindings
{
    /// <summary>A pad button id from this value on is a trigger: the id minus this is SDL's gamepad axis.</summary>
    public const int TriggerBase = 1000;

    /// <summary>Press level of a trigger (of SDL's 0..32767).</summary>
    public const int TriggerPressed = 16384;

    private static readonly List<KeyName> Keys = Build();

    /// <summary>The keys of the original that a mapping can send, in the order the launcher goes through them.</summary>
    public static IReadOnlyList<KeyName> OriginalKeys { get; } = new ReadOnlyCollection<KeyName>([.. Keys.Where(k => k.Original)]);

    /// <summary>The gamepad buttons that can be mapped (the four face buttons and the stick are the joystick's, as before).</summary>
    public static IReadOnlyList<PadButton> PadButtons { get; } =
    [
        new("LeftShoulder", "Left shoulder", 9), new("RightShoulder", "Right shoulder", 10),
        new("LeftTrigger", "Left trigger", TriggerBase + 4), new("RightTrigger", "Right trigger", TriggerBase + 5),
        new("Start", "Start", 6), new("Back", "Back / Select", 4),
        new("LeftStick", "Left stick click", 7), new("RightStick", "Right stick click", 8),
    ];

    private static List<KeyName> Build()
    {
        var list = new List<KeyName>();
        void Add(string name, int scancode, string label, bool original = true) => list.Add(new KeyName(name, scancode, label, original));
        Add("Up", 82, "Up arrow"); Add("Down", 81, "Down arrow"); Add("Left", 80, "Left arrow"); Add("Right", 79, "Right arrow");
        Add("Home", 74, "Home"); Add("End", 77, "End"); Add("PageUp", 75, "Page up"); Add("PageDown", 78, "Page down");
        Add("Insert", 73, "Insert"); Add("Delete", 76, "Delete");
        for (var i = 1; i <= 9; i++)
        {
            Add("Keypad" + i, 88 + i, "Keypad " + i);
        }
        Add("Keypad0", 98, "Keypad 0"); Add("KeypadPeriod", 99, "Keypad ."); Add("KeypadDivide", 84, "Keypad /"); Add("KeypadMultiply", 85, "Keypad *");
        Add("KeypadMinus", 86, "Keypad -"); Add("KeypadPlus", 87, "Keypad +"); Add("KeypadEnter", 88, "Keypad Enter");
        Add("Enter", 40, "Enter"); Add("Esc", 41, "Esc"); Add("Space", 44, "Space"); Add("Tab", 43, "Tab"); Add("Backspace", 42, "Backspace");
        Add("Minus", 45, "-"); Add("Equals", 46, "=");
        for (var i = 1; i <= 10; i++)
        {
            Add("F" + i, 57 + i, "F" + i);
        }
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Add(c.ToString(), 4 + (c - 'A'), c.ToString());
        }
        for (var i = 1; i <= 9; i++)
        {
            Add("Digit" + i, 29 + i, i.ToString());
        }
        Add("Digit0", 39, "0");
        Add("LeftBracket", 47, "["); Add("RightBracket", 48, "]"); Add("Backslash", 49, "\\"); Add("Semicolon", 51, ";"); Add("Apostrophe", 52, "'");
        Add("Grave", 53, "`"); Add("Comma", 54, ","); Add("Period", 55, "."); Add("Slash", 56, "/");
        // keys the player may press to send one of the above, which the original has no use for
        Add("F11", 68, "F11", false); Add("F12", 69, "F12", false); Add("CapsLock", 57, "Caps Lock", false); Add("PrintScreen", 70, "Print Screen", false);
        Add("ScrollLock", 71, "Scroll Lock", false); Add("Pause", 72, "Pause", false); Add("NumLock", 83, "Num Lock", false);
        Add("NonUsBackslash", 100, "< > (extra key)", false); Add("Menu", 101, "Menu", false);
        return list;
    }

    /// <summary>Whether a scancode is a modifier (Ctrl, Shift, Alt, the Windows key): these cannot be remapped, the game reads them with other keys.</summary>
    public static bool IsModifier(int scancode) => scancode is >= 224 and <= 231;

    public static KeyName? Find(string? name) =>
        name is null ? null : Keys.FirstOrDefault(k => string.Equals(k.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? FromNumber(name.Trim());

    private static KeyName? FromNumber(string name) =>
        name.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out var n) && n is > 0 and < 512 && !IsModifier(n)
            ? new KeyName("Key" + n, n, "Key " + n, false) : null;

    /// <summary>The key of a scancode (a name of its own for a key the table lacks), null for a modifier.</summary>
    public static KeyName? ByScancode(int scancode) =>
        IsModifier(scancode) ? null : Keys.FirstOrDefault(k => k.Scancode == scancode) ?? FromNumber("Key" + scancode);

    public static PadButton? FindPad(string? name) =>
        PadButtons.FirstOrDefault(b => string.Equals(b.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads "left=right" pairs: the left names a key (or a pad button when <paramref name="pad"/>), the right a key of the original.
    /// A pair that does not read is left out with a problem; a left that comes twice keeps its last pair. The result is in the
    /// canonical spelling ("Q=Up").
    /// </summary>
    public static List<(string From, string To)> ParsePairs(IEnumerable<string?> pairs, bool pad, string key, List<string> problems)
    {
        var result = new List<(string From, string To)>();
        foreach (var text in pairs)
        {
            var parts = (text ?? "").Split('=', 2);
            string? from = null;
            if (parts.Length == 2)
            {
                from = pad ? FindPad(parts[0])?.Name : Find(parts[0]) is { } k && !IsModifier(k.Scancode) ? k.Name : null;
            }
            var to = parts.Length == 2 && Find(parts[1]) is { Original: true } target ? target.Name : null;
            if (from is null || to is null)
            {
                problems.Add($"\"{key}\": \"{text}\" is not a pair \"{(pad ? "button" : "key")}=key of the original\"; ignored");
                continue;
            }
            result.RemoveAll(p => p.From == from);
            result.Add((from, to));
        }
        return result;
    }

    public static string Format((string From, string To) pair) => pair.From + "=" + pair.To;

    /// <summary>The character a key gives on the US keyboard (the symbol SDL reports), 0 for a key without one.</summary>
    public static int UsSymbol(int scancode) => scancode switch
    {
        >= 4 and <= 29 => 'a' + (scancode - 4),
        >= 30 and <= 38 => '1' + (scancode - 30),
        39 => '0',
        45 => '-', 46 => '=', 47 => '[', 48 => ']', 49 => '\\', 51 => ';', 52 => '\'', 53 => '`', 54 => ',', 55 => '.', 56 => '/',
        84 => '/', 85 => '*',
        _ => 0,
    };

    /// <summary>The character a key gives with Shift on the US keyboard, 0 for a letter (its capital is made from it) or a key without one.</summary>
    public static int UsShift(int scancode) => scancode switch
    {
        >= 30 and <= 39 => ")!@#$%^&*("[(scancode - 29) % 10],
        45 => '_', 46 => '+', 47 => '{', 48 => '}', 49 => '|', 51 => ':', 52 => '"', 53 => '~', 54 => '<', 55 => '>', 56 => '?',
        _ => 0,
    };
}

/// <summary>
/// The remapping at work (D-671, D-672): the keys of the player's keyboard and the pad's buttons become keys of the original,
/// in the host's key translation, before the game sees them. Built from the settings' pairs; empty means no change.
/// </summary>
internal sealed class KeyRemap
{
    private readonly Dictionary<int, int> _keys = [];
    private readonly List<(int Button, int Key)> _pad = [];

    /// <summary>No remapping at all (the original's keys).</summary>
    public static KeyRemap None { get; } = new([], []);

    public KeyRemap(IEnumerable<string>? keyMap, IEnumerable<string>? padMap)
    {
        var ignored = new List<string>();
        foreach (var (from, to) in KeyBindings.ParsePairs(keyMap ?? [], false, "input.keyMap", ignored))
        {
            _keys[KeyBindings.Find(from)!.Scancode] = KeyBindings.Find(to)!.Scancode;
        }
        foreach (var (from, to) in KeyBindings.ParsePairs(padMap ?? [], true, "input.padMap", ignored))
        {
            _pad.Add((KeyBindings.FindPad(from)!.Id, KeyBindings.Find(to)!.Scancode));
        }
    }

    public bool IsEmpty => _keys.Count == 0 && _pad.Count == 0;

    /// <summary>The pad buttons that are mapped: SDL's button numbers (a trigger: <see cref="KeyBindings.TriggerBase"/> + the axis).</summary>
    public IReadOnlyList<int> PadButtonIds => [.. _pad.Select(p => p.Button)];

    /// <summary>
    /// The key a pressed key becomes: the same when it is not mapped; else the key of the original it is mapped to, with the
    /// character and the shifted character that key has on a US keyboard.
    /// </summary>
    // DIVERGENCE: D-671 a player option (input.keyMap), empty by default: a mapped key is taken as the key of the original it is mapped to
    public (int Scancode, int Symbol, int Shifted) Apply(int scancode, int symbol, int shifted)
    {
        if (!_keys.TryGetValue(scancode, out var target))
        {
            return (scancode, symbol, shifted);
        }
        return (target, KeyBindings.UsSymbol(target), KeyBindings.UsShift(target));
    }

    /// <summary>The key of the original a pad button sends (as a scancode), or null when the button is not mapped.</summary>
    public int? PadKey(int button)
    {
        var i = _pad.FindIndex(p => p.Button == button);
        return i < 0 ? null : _pad[i].Key;
    }

    private readonly HashSet<int> _down = [];

    /// <summary>
    /// Looks at the mapped pad buttons: the scancodes of the keys whose button went down since the last look (one key per press,
    /// no repeat). <paramref name="isDown"/> says whether a button (SDL's number, a trigger by <see cref="KeyBindings.TriggerBase"/>) is held.
    /// </summary>
    // DIVERGENCE: D-672 a player option (input.padMap), empty by default: a pad button beyond the joystick's four types a key, once for each press
    public List<int> PadPresses(Func<int, bool> isDown)
    {
        var pressed = new List<int>();
        foreach (var (button, key) in _pad)
        {
            if (!isDown(button))
            {
                _down.Remove(button);
            }
            else if (_down.Add(button))
            {
                pressed.Add(key);
            }
        }
        return pressed;
    }
}
