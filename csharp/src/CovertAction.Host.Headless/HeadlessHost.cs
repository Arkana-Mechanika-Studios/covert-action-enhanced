using CovertAction.Platform;

namespace CovertAction.Host.Headless;

/// <summary>
/// A host without a window, for tests and scripted runs: the keys come from a list, time passes as fast as the
/// game asks for it, and the last picture shown is kept. When the game asks for a key after the last of the list,
/// the host asks to quit, as if the player had closed the window. An entry <see cref="NoKeyYet"/> in the list is a
/// moment without a key: the game finds the keyboard empty once (so a part that first throws away the keys already
/// pressed, or looks for a key in every frame, gets on) and the list goes on with its next entry. An entry made by
/// <see cref="PointerMove"/>, <see cref="PointerPress"/> or <see cref="PointerRelease"/> is the scripted mouse: the
/// pointer moves or a button goes down or up, and the game finds the keyboard empty at that moment. The list is read
/// as the game asks for it, so a test can compute an entry from the screen shown so far.
/// </summary>
/// <param name="keys">The key presses, in order, as key codes (<see cref="HostKeys"/>).</param>
/// <param name="unixSeconds">The time of day the host tells (the game seeds its random numbers from it).</param>
public sealed class HeadlessHost(IEnumerable<int> keys, long unixSeconds = 0) : IHost, IVideo, IInput, IClock
{
    /// <summary>The entry of the key list that stands for "no key at this moment".</summary>
    public const int NoKeyYet = int.MinValue;

    /// <summary>What <see cref="EntryTaken"/> reports when the game asks for a key after the last entry of the list.</summary>
    public const int EndOfKeys = int.MinValue + 1;

    /// <summary>
    /// Raised when the game takes the next entry of the key list (a key, or <see cref="NoKeyYet"/>), and once when it
    /// finds the list used up (<see cref="EndOfKeys"/>), before the game gets it. The session proof records the
    /// screen at these moments (csharp/docs/SESSION-PROOF.md).
    /// </summary>
    public event Action<int>? EntryTaken;

    private readonly IEnumerator<int> _keys = keys.GetEnumerator();

    /// <summary>The first value of the entries that are mouse actions (no key code is this large).</summary>
    private const int PointerEntry = 0x4000_0000;

    /// <summary>The kinds of mouse entry, in bits 24..27 of the entry.</summary>
    private const int MoveKind = 0, PressKind = 1, ReleaseKind = 3;

    /// <summary>
    /// The scripted mouse over the picture (new code; csharp/docs/MOUSE-SUPPORT.md): it moves and clicks as the key list's
    /// mouse entries say. The game reads it only with the mouse option on (GameOptions.Mouse).
    /// </summary>
    public PointerLatch Mouse { get; } = new();

    /// <inheritdoc/>
    IPointer? IInput.Pointer => _watchedMouse ??= new WatchedPointer(this);

    private WatchedPointer? _watchedMouse;

    /// <summary>
    /// How often the game has looked where the pointer is (a menu waiting with the mouse option reads it on every pass):
    /// a script can wait for a menu to be waiting before it clicks.
    /// </summary>
    public int PointerReads { get; private set; }

    /// <summary>A key-list entry that moves the mouse to a pixel of the 320x200 picture.</summary>
    /// <param name="x">The pixel across, 0..319.</param>
    /// <param name="y">The pixel down, 0..199.</param>
    public static int PointerMove(int x, int y) => PointerEntry | MoveKind << 24 | (y & 0xfff) << 12 | (x & 0xfff);

    /// <summary>A key-list entry that presses a mouse button where the pointer is.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    public static int PointerPress(int button) => PointerEntry | (PressKind + (button & 1)) << 24;

    /// <summary>A key-list entry that lets a mouse button go.</summary>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    public static int PointerRelease(int button) => PointerEntry | (ReleaseKind + (button & 1)) << 24;

    /// <summary>The key-list entries of a click: the pointer to a pixel, the button down, the button up.</summary>
    /// <param name="x">The pixel across.</param>
    /// <param name="y">The pixel down.</param>
    /// <param name="button">0 for the left button, 1 for the right.</param>
    public static int[] Click(int x, int y, int button = 0) => [PointerMove(x, y), PointerPress(button), PointerRelease(button)];

    /// <summary>The last picture shown: 320x200 palette indices.</summary>
    public byte[] Pixels { get; } = new byte[320 * 200];

    /// <summary>The colours of the last picture shown, as 0xRRGGBB.</summary>
    public uint[] Palette { get; } = new uint[256];

    /// <summary>The last text page shown (a copy).</summary>
    public TextScreen TextPage { get; } = new();

    /// <summary>Whether the last thing shown was the text page, not a picture.</summary>
    public bool ShowingText { get; private set; }

    /// <summary>How many pictures were shown.</summary>
    public int Frames { get; private set; }

    /// <inheritdoc/>
    public IVideo Video => this;

    /// <inheritdoc/>
    public IInput Input => this;

    /// <summary>The sound device: silent, unless a tool gives one of its own (the session proof drives the sound driver's timer itself).</summary>
    public IPcmOutput Audio { get; init; } = new SilentPcmOutput();

    /// <inheritdoc/>
    public IClock Clock => this;

    /// <inheritdoc/>
    public bool QuitRequested { get; private set; }

    /// <inheritdoc/>
    public long Milliseconds { get; private set; }

    /// <inheritdoc/>
    public long UnixSeconds => TimeOfDay ?? unixSeconds + Milliseconds / 1000;

    /// <summary>
    /// When set, the time of day the host tells, standing still while the game runs (null: the start's time, moving
    /// with the host's clock). The session proof gives each part of a whole session the time of day the original side
    /// gives it (csharp/docs/SESSION-PROOF.md, item 7).
    /// </summary>
    public long? TimeOfDay { get; set; }

    /// <inheritdoc/>
    public void Present(ReadOnlySpan<byte> pixels, ReadOnlySpan<uint> palette)
    {
        pixels.CopyTo(Pixels);
        palette.CopyTo(Palette);
        ShowingText = false;
        Frames++;
    }

    /// <inheritdoc/>
    public void PresentText(TextScreen screen)
    {
        screen.CopyTo(TextPage);
        ShowingText = true;
        Frames++;
    }

    /// <inheritdoc/>
    public void Pump()
    {
    }

    /// <inheritdoc/>
    public bool TryReadKey(out int key)
    {
        if (_keys.MoveNext())
        {
            key = _keys.Current;
            EntryTaken?.Invoke(key);
            if (key >= PointerEntry)
            {
                // the scripted mouse: the game finds no key at this moment, as for NoKeyYet
                ApplyPointerEntry(key);
                Milliseconds++;
                key = 0;
                return false;
            }
            if (key == NoKeyYet)
            {
                // a moment without a key: a millisecond passes, so a game polling for a key sees its screen shown in time
                Milliseconds++;
                return false;
            }
            return true;
        }
        if (!QuitRequested)
        {
            EntryTaken?.Invoke(EndOfKeys);
        }
        QuitRequested = true;
        key = 0;
        return false;
    }

    /// <summary>The scripted mouse as the game reads it, counting its looks (<see cref="PointerReads"/>).</summary>
    private sealed class WatchedPointer(HeadlessHost host) : IPointer
    {
        public PointerState State
        {
            get
            {
                host.PointerReads++;
                return host.Mouse.State;
            }
        }

        public PointerPresses TakePresses(int button) => host.Mouse.TakePresses(button);

        public PointerPresses TakeReleases(int button) => host.Mouse.TakeReleases(button);
    }

    /// <summary>Does what a mouse entry of the key list says.</summary>
    private void ApplyPointerEntry(int entry)
    {
        var kind = entry >> 24 & 0xf;
        switch (kind)
        {
            case MoveKind:
                Mouse.MoveTo(entry & 0xfff, entry >> 12 & 0xfff);
                break;
            case PressKind or PressKind + 1:
                Mouse.Press(kind - PressKind);
                break;
            case ReleaseKind or ReleaseKind + 1:
                Mouse.Release(kind - ReleaseKind);
                break;
        }
    }

    /// <inheritdoc/>
    public void Sleep(int milliseconds) => Milliseconds += Math.Max(0, milliseconds);

    /// <summary>
    /// The joystick the game port shows: none (<see cref="JoystickReading.None"/>) unless a test or a tool sets one,
    /// so a run is the same on every machine whatever controller is plugged in.
    /// </summary>
    public JoystickReading Joystick { get; set; } = JoystickReading.None;

    /// <inheritdoc/>
    public JoystickReading ReadJoystick() => Joystick;
}
