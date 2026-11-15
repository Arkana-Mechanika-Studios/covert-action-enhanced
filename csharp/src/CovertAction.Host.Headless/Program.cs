using CovertAction.Content;
using CovertAction.Launcher;
using CovertAction.Platform;

namespace CovertAction.Host.Headless;

/// <summary>
/// A scripted run without a window: plays the game from its set-up screen's title on (or, as a developer's shortcut, one
/// practice session) with the keys of a script and writes the last screen the game showed as a picture file. For
/// checks on machines without a display, and for looking at a screen from the command line.
/// </summary>
internal static class Program
{
    private const string Usage = """
        CovertAction.Host.Headless [game folder] [--practice crypto|chase] [--level 0..3] [--seed n] [--keys script] [--out picture.bmp] [--saves folder]

        game folder  without it, the folder named by COVERT_GAME_DIR, else the current folder
        --practice   a developer's shortcut: one practice session of the part, without the set-up and the menus;
                     without it the game starts as the player's does, with the set-up screen's title (no sound, keyboard only)
        --saves      the folder the game's saves are written to; without it the run cannot save (a check leaves no files)

        script   the keys to press, in order: a character is its key; {enter} {esc} {up} {down} {left} {right}
                 {f1} {f10} {alt-q} name the others. After the last key the session is left.
        """;

    private static readonly Dictionary<string, int> NamedKeys = new()
    {
        ["enter"] = HostKeys.Enter, ["esc"] = HostKeys.Escape, ["up"] = HostKeys.Up, ["down"] = HostKeys.Down,
        ["left"] = HostKeys.Left, ["right"] = HostKeys.Right, ["f1"] = HostKeys.F1, ["f10"] = HostKeys.F10, ["alt-q"] = HostKeys.AltQ,
    };

    private static int Main(string[] args)
    {
        string script = "", output = "", practice = "";
        string? path = null, saves = null;
        int level = 0, seed = 0;
        var first = 0;
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            path = args[0];
            first = 1;
        }
        if ((args.Length - first) % 2 != 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        for (var i = first; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--level": level = int.Parse(args[i + 1]); break;
                case "--seed": seed = int.Parse(args[i + 1]); break;
                case "--keys": script = args[i + 1]; break;
                case "--practice" when args[i + 1] is "crypto" or "chase": practice = args[i + 1]; break;
                case "--out": output = args[i + 1]; break;
                case "--saves": saves = args[i + 1]; break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        var host = new HeadlessHost(Keys(script), seed);
        var folder = path is not null ? new GameFolder(path) : GameFolder.FromEnvironment() ?? new GameFolder(Environment.CurrentDirectory);
        // the set-up's answers a host without sound and without a joystick gives: no sound, keyboard only
        var game = new Game(folder, host, saves, new GameOptions(SoundLetter: 'N', Joystick: false));
        if (practice == "")
        {
            game.Main();
            Console.WriteLine($"the game ended; frames shown: {host.Frames}");
        }
        else if (practice == "chase")
        {
            var chase = game.PlayChasePractice(level);
            Console.WriteLine($"chase outcome: {chase.Outcome}; suspicion: {chase.Suspicion}%; frames shown: {host.Frames}");
        }
        else
        {
            var result = game.PlayCryptoPractice(level);
            Console.WriteLine($"solved: {result.Solved}; frames shown: {host.Frames}");
        }
        if (output.Length > 0)
        {
            File.WriteAllBytes(output, Bitmap.Encode(host.Pixels, host.Palette, 320, 200));
        }
        return 0;
    }

    /// <summary>The key codes of a script.</summary>
    internal static List<int> Keys(string script)
    {
        var keys = new List<int>();
        for (var i = 0; i < script.Length; i++)
        {
            var close = script[i] == '{' ? script.IndexOf('}', i) : -1;
            var name = close > i ? script[(i + 1)..close].ToLowerInvariant() : "";
            // {wait} is a moment without a key; {wait 50} is fifty of them (a part that looks for a key in every frame plays that many frames)
            var wait = System.Text.RegularExpressions.Regex.Match(name, @"^wait(?: (\d+))?$");
            if (wait.Success)
            {
                keys.AddRange(Enumerable.Repeat(HeadlessHost.NoKeyYet, wait.Groups[1].Success ? int.Parse(wait.Groups[1].Value) : 1));
                i = close;
            }
            else if (close > i && NamedKeys.TryGetValue(name, out var named))
            {
                keys.Add(named);
                i = close;
            }
            else
            {
                keys.Add(HostKeys.ForCharacter(script[i]));
            }
        }
        return keys;
    }
}

/// <summary>Writes a picture of palette indices as a Windows bitmap file (8 bits a pixel, with its palette).</summary>
internal static class Bitmap
{
    internal static byte[] Encode(ReadOnlySpan<byte> pixels, ReadOnlySpan<uint> palette, int width, int height)
    {
        const int headers = 14 + 40, paletteBytes = 256 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("BM"u8);
        writer.Write(headers + paletteBytes + width * height);
        writer.Write(0);
        writer.Write(headers + paletteBytes);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);          // rows from the top
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(0);
        writer.Write(width * height);
        writer.Write(0);
        writer.Write(0);
        writer.Write(256);
        writer.Write(0);
        foreach (var colour in palette)
        {
            writer.Write(colour);       // blue, green, red, 0
        }
        writer.Write(pixels);           // 320 is a multiple of four: rows need no padding
        writer.Flush();
        return stream.ToArray();
    }
}
