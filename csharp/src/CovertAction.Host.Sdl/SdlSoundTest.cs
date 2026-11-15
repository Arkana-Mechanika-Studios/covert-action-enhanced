using CovertAction.Content;
using CovertAction.Launcher;
using CovertAction.Platform;
using CovertAction.Settings;

namespace CovertAction.Host.Sdl;

/// <summary>
/// The launcher's Test sound: the theme tune for a few seconds on the chosen card, through the player's own driver
/// (<see cref="SoundCheck"/>), on a sound device of its own that is closed when the test ends. New code.
/// </summary>
internal sealed class SdlSoundTest : ISoundTest, IDisposable
{
    /// <summary>How long the test plays.</summary>
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(12);

    private SdlAudioOutput? _output;
    private System.Diagnostics.Stopwatch? _playing;

    public string? Status { get; private set; }

    public bool Playing
    {
        get
        {
            if (_playing is { } watch && watch.Elapsed > Length)
            {
                Stop();
                Status = null;
            }
            return _output is not null;
        }
    }

    public void Play(SoundTestRequest request)
    {
        Stop();
        var letter = GameSettings.SoundLetter(request.Card);
        if (letter == 'N')
        {
            Status = "Nothing to play: choose a sound card.";
            return;
        }
        var output = SdlAudioOutput.TryOpen(out var error);
        if (output is null)
        {
            Status = "No sound device: " + (error ?? "SDL could not open one") + ".";
            return;
        }
        string? warning = null;
        try
        {
            SoundCheck.PlayTheme(new GameFolder(request.GameFolder), new TestHost(output), letter, request.RomFolder, message => warning = message);
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException or IOException)
        {
            output.Dispose();
            Status = "Could not play: " + e.Message;
            return;
        }
        _output = output;
        _playing = System.Diagnostics.Stopwatch.StartNew();
        var card = LauncherModel.Label(LauncherModel.SoundCards, request.Card);
        Status = warning is null ? $"Playing the theme on the {card}…" : "No MT-32 ROMs: playing the theme on the AdLib instead (D-20)…";
    }

    public void Stop()
    {
        _output?.Dispose();
        _output = null;
        _playing = null;
        Status = null;
    }

    public void Dispose() => Stop();

    /// <summary>What a sound driver needs of a host: the sound device and the time (no window or keys).</summary>
    private sealed class TestHost(IPcmOutput audio) : IHost, IClock
    {
        private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

        public IVideo Video => throw new NotSupportedException("the sound test has no picture");

        public IInput Input => throw new NotSupportedException("the sound test has no keyboard");

        public IPcmOutput Audio => audio;

        public IClock Clock => this;

        public long Milliseconds => _watch.ElapsedMilliseconds;

        public long UnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public double PreciseMilliseconds => _watch.Elapsed.TotalMilliseconds;

        public void Sleep(int milliseconds) => Thread.Sleep(Math.Max(0, milliseconds));
    }
}
