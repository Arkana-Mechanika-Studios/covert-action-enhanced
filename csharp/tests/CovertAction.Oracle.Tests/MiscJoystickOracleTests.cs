using System.Text.Json;
using CovertAction.Content;
using CovertAction.Drivers;
using CovertAction.Host.Headless;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Platform;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The game's joystick (<see cref="KeyboardDriver"/>'s calibration and reading) against the original's input driver
/// MISC.EXE run on a fed game port (tools/oracle/joystick_vectors.py): every case is a sequence of calibrations and
/// readings from the driver's data as loaded, each step with the counts the port's timers gave; every reading must give
/// the original's word. Skipped without vectors.
/// </summary>
/// <remarks>
/// The vectors are looked for in COVERT_JOYSTICK_ORACLE_DIR, then in the oracle's folder (COVERT_ORACLE_DIR or
/// build/oracle), then in the newest folder oracle-joystick-* beside it: they are made apart from the other vectors.
/// </remarks>
public sealed class MiscJoystickOracleTests(ITestOutputHelper output)
{
    private const string FileName = "_joystick.json";

    private static string? VectorFile()
    {
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable("COVERT_JOYSTICK_ORACLE_DIR") is { Length: > 0 } own)
        {
            folders.Add(own);
        }
        var oracle = Path.GetFullPath(OracleVectors.Folder.TrimEnd('/', '\\'));
        folders.Add(oracle);
        if (Path.GetDirectoryName(oracle) is { } parent && Directory.Exists(parent))
        {
            folders.AddRange(Directory.GetDirectories(parent, "oracle-joystick-*").OrderDescending());
        }
        return folders.Select(f => Path.Combine(f, "MISC", FileName)).FirstOrDefault(File.Exists);
    }

    [SkippableFact]
    public void EveryReadingGivesTheOriginalsWord()
    {
        var path = VectorFile() ?? throw new SkipException($"no joystick vectors MISC/{FileName} (tools/oracle/joystick_vectors.py writes them from the player's MISC.EXE)");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = 0;
        var readings = 0;
        foreach (var vector in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = vector.GetProperty("id").GetString();
            var host = new HeadlessHost([]);
            var graphics = new McgaGraphicsDriver(new FontSet([0, 0]), new byte[3 * McgaDriverFile.Colours]);
            var joystick = new KeyboardDriver(new HostLink(graphics, host));
            var number = 0;
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                var counts = step.GetProperty("counts");
                host.Joystick = new JoystickReading(step.GetProperty("buttons").GetInt32(), Count(counts[0]), Count(counts[1]));
                if (step.GetProperty("call").GetString() == "calibrate")
                {
                    joystick.CalibrateJoystick();
                }
                else
                {
                    var original = step.GetProperty("ax").GetInt32();
                    var port = joystick.ReadJoystickAxes();
                    Assert.True(port == original, $"case {id} ({vector.GetProperty("origin").GetString()}), step {number}: the C# reads {port:x4}, the original {original:x4}");
                    readings++;
                }
                number++;
            }
            cases++;
        }
        Assert.True(readings > 0, "the vectors have no reading");
        output.WriteLine($"TALLY read_joystick_axes: {readings} readings in {cases} cases agree ({path})");
    }

    /// <summary>A timer's count as the vectors write it: null for an axis without a stick.</summary>
    private static int Count(JsonElement count) => count.ValueKind == JsonValueKind.Null ? JoystickReading.NeverEnds : count.GetInt32();
}
