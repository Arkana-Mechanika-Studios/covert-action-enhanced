using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Host.Headless;
using CovertAction.Launcher.Tests.Intro;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The pictures of docs/cut-content-options.md. When COVERT_SCREENSHOT_DIR names a folder, each test saves the game's screen
/// there as a PNG (the picture of the player's own copy, 320x200 drawn 960x720 as the 4:3 screen shows it); otherwise it only
/// checks that the screen was reached. COVERT_SCREENSHOT_ALL=1 also saves every frame the keys passed, for finding a moment.
/// Needs the player's game files (skipped without them).
/// </summary>
public sealed class CutContentScreenshots
{
    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "GAME.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>The game's 320x200 screen drawn 960x720 (three times wide, 3.6 times high: the 4:3 screen of the other pictures).</summary>
    private static uint[] Screen(byte[] pixels, uint[] palette)
    {
        var picture = new uint[960 * 720];
        for (var y = 0; y < 720; y++)
        {
            for (var x = 0; x < 960; x++)
            {
                picture[y * 960 + x] = 0xff000000 | palette[pixels[y * 200 / 720 * 320 + x / 3]];
            }
        }
        return picture;
    }

    private static void Save(string name, byte[] pixels, uint[] palette)
    {
        var folder = Environment.GetEnvironmentVariable("COVERT_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            PngWriter.Write(Screen(pixels, palette), 960, 720, Path.Combine(folder, name + ".png"));
        }
    }

    /// <summary>The scene strip (the 96 columns at the right, from column 0xe0) of screens side by side, drawn as <see cref="Screen"/> does.</summary>
    private static void SaveStrips(string name, params (byte[] Pixels, uint[] Palette)[] shots)
    {
        var folder = Environment.GetEnvironmentVariable("COVERT_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        const int From = 0xe0, Wide = (320 - From) * 3, Gap = 12;
        var width = shots.Length * Wide + (shots.Length - 1) * Gap;
        var picture = new uint[width * 720];
        Array.Fill(picture, 0xff000000);
        for (var n = 0; n < shots.Length; n++)
        {
            var full = Screen(shots[n].Pixels, shots[n].Palette);
            for (var y = 0; y < 720; y++)
            {
                Array.Copy(full, y * 960 + From * 3, picture, y * width + n * (Wide + Gap), Wide);
            }
        }
        Directory.CreateDirectory(folder);
        PngWriter.Write(picture, width, 720, Path.Combine(folder, name + ".png"));
    }

    /// <summary>Every screen shown when the game asks for a key (kept, with the key asked), in order.</summary>
    private static List<(int Key, byte[] Pixels, uint[] Palette)> Frames(HeadlessHost host)
    {
        var frames = new List<(int, byte[], uint[])>();
        host.EntryTaken += key => frames.Add((key, (byte[])host.Pixels.Clone(), (uint[])host.Palette.Clone()));
        return frames;
    }

    private static void SaveAll(string name, List<(int Key, byte[] Pixels, uint[] Palette)> frames)
    {
        if (Environment.GetEnvironmentVariable("COVERT_SCREENSHOT_ALL") != "1")
        {
            return;
        }
        byte[]? last = null;
        for (var i = 0; i < frames.Count; i++)
        {
            if (last is null || !last.AsSpan().SequenceEqual(frames[i].Pixels))
            {
                Save($"all-{name}-{i:D3}", frames[i].Pixels, frames[i].Palette);
                last = frames[i].Pixels;
            }
        }
    }

    [SkippableTheory]
    [InlineData(1, "dossier-man")]
    [InlineData(0, "dossier-woman")]
    public void TheCapturedDossier(short face, string name)
    {
        int[] keys = [.. Enumerable.Repeat(HeadlessHost.NoKeyYet, 80), HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 10), HostKeys.Enter];
        var host = new HeadlessHost(keys, unixSeconds: 600_000_000);
        var frames = Frames(host);
        var game = new Game(GameFiles(), host, null, new GameOptions(CutCapturedDossier: true));
        var part = game.OpenCampaign(new SharedState(), out var state);
        state.Status |= Options.Animation;
        state.Rp1.FontNr = 1;
        state.Ps[3].Face = face;
        part.Plot.ShowCapturedDossier(3);
        SaveAll(name, frames);
        var shown = frames.First(f => f.Key == HostKeys.Enter);
        SaveStrips(name, (shown.Pixels, shown.Palette));
    }

    private static readonly int[] Wait = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];

    /// <summary>The title, "Practice", the first level and <paramref name="downs"/> downs in the practice menu, then the chase's first cars and some frames.</summary>
    private static int[] PracticeKeys(int downs, int level = 0) =>
    [
        .. Wait, HostKeys.Space, .. Wait, HostKeys.Down, .. Wait, HostKeys.Down, .. Wait, HostKeys.Enter, .. Wait, .. Enumerable.Repeat(new[] { HostKeys.Down }.Concat(Wait), level).SelectMany(x => x), HostKeys.Enter,
        .. Enumerable.Repeat(new[] { HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HostKeys.Down }, downs).SelectMany(x => x),
        .. Wait, HostKeys.Enter,
        .. Wait, HostKeys.Enter, .. Wait, HostKeys.Enter, .. Wait, HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 150),
    ];

    [SkippableFact]
    public void ThePracticeMenuAndTheFreeChase()
    {
        const sbyte FreshStart = 0x0b;
        var host = new HeadlessHost(PracticeKeys(4), 600_000_000);
        var frames = Frames(host);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };
        var game = new Game(GameFiles(), host, null, new GameOptions(CutStandaloneChase: true));
        game.RunPart(shared);
        game.RunPart(shared);
        SaveAll("chase", frames);
        Assert.True(game.LastChaseStandalone);
        // the practice menu with its fifth line chosen (the key list is fixed, so the frames are), and the map a while into the chase
        Save("practice-menu", frames[36].Pixels, frames[36].Palette);
        Save("free-chase", frames[50].Pixels, frames[50].Palette);
    }

    [SkippableFact]
    public void TheHardestCircuitBoard()
    {
        const sbyte FreshStart = 0x0b;
        var host = new HeadlessHost([.. PracticeKeys(3, 3)[..^150], .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 40)], 600_000_000);
        var frames = Frames(host);
        var shared = new SharedState { NextModule = FreshStart, GfxDriver = (sbyte)'M', SndDriver = (sbyte)'N', GfxLetter = (short)'M' };
        var game = new Game(GameFiles(), host, null, new GameOptions(CutHardestCircuit: true));
        game.RunPart(shared);
        // the practice gives every skill the rating 1; the board's hardest level needs the lowest electronics rating, level 3
        shared.SkillBits &= unchecked((short)~(3 << 6));
        game.RunPart(shared);
        SaveAll("circuit", frames);
        // the board a few seconds in: only the last column of chips is shown (difficulty 4), the clock runs
        Save("hardest-circuit", frames[62].Pixels, frames[62].Palette);
    }
}
