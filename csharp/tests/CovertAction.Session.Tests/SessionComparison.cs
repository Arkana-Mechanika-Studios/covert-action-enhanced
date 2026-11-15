using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Persistence;
using CovertAction.Persistence.Layout;

namespace CovertAction.Session.Tests;

/// <summary>
/// Compares the rewrite's run of a session with the original's recording, checkpoint by checkpoint, and reports the
/// first difference: which checkpoint (after which key), what differs (the picture's pixels, its colours, the hand-off
/// block's fields, the number of checkpoints, how the run ended), with a picture of the two screens and their
/// difference. Writes everything into a new comparison folder beside the recording (never into the recording).
/// </summary>
internal static class SessionComparison
{
    private const int Width = 320, Height = 200, Pixels = Width * Height;

    /// <summary>Compares and writes the comparison folder.</summary>
    /// <param name="original">The original's recording.</param>
    /// <param name="rewrite">The rewrite's run of the same inputs.</param>
    /// <param name="outFolder">A new folder for the report, the rewrite's checkpoints and the picture of the difference; null: compare only.</param>
    /// <returns>Null when the two runs agree; else the first difference, in words.</returns>
    public static string? Compare(SessionRecording original, RewriteRun rewrite, string? outFolder)
    {
        // the registered differences met: entry -> the number of checkpoints where pixels were not counted for it
        var masked = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (outFolder is null)
        {
            return FirstDifference(original, rewrite, null, masked);
        }
        Directory.CreateDirectory(outFolder);
        File.WriteAllLines(Path.Combine(outFolder, "checkpoints.jsonl"), rewrite.Checkpoints.Select(c => c.ToJsonLine()));
        File.WriteAllBytes(Path.Combine(outFolder, "handoff-out.bin"), rewrite.HandoffOut);
        var difference = FirstDifference(original, rewrite, outFolder, masked);
        foreach (var entry in rewrite.Registered ?? [])
        {
            masked[entry] = 1;
        }
        var report = new StringBuilder()
            .AppendLine($"session {original.Part}/{original.Name}")
            .AppendLine($"original: {original.Folder} ({original.Checkpoints.Count} checkpoints, {original.Stop})")
            .AppendLine($"rewrite: {rewrite.Checkpoints.Count} checkpoints, {rewrite.Stop}")
            .AppendLine("compared: picture (pixels and colours), hand-off block and sound requests at every checkpoint, the written files at every checkpoint where they change (recording format 2), the end, the final hand-off block, the written files; in a whole session also the program that took each entry and, in text mode, the text page and its cursor in place of the picture" + (original.DeviceStream ? "; and the sound device's stream at every checkpoint and after the last (the driver's OPL, MIDI, SN76496 and speaker writes, its timer rate and ticks)" : ""))
            .AppendLine("not compared: the time and disk calls (answered alike on both sides; recorded by the original side only)")
            .AppendLine(masked.Count == 0 ? "no registered difference was met" : "registered differences met (not counted): " + string.Join(", ", masked.Select(m => !m.Key.Contains('(', StringComparison.Ordinal) ? $"{m.Key} at {m.Value} checkpoints (pixels)" : m.Value > 1 ? $"{m.Key} at {m.Value} checkpoints" : m.Key)))
            .AppendLine(difference is null ? "AGREE" : "FIRST DIFFERENCE: " + difference);
        File.WriteAllText(Path.Combine(outFolder, "report.txt"), report.ToString());
        return difference;
    }

    private static string? FirstDifference(SessionRecording original, RewriteRun rewrite, string? outFolder, IDictionary<string, int> masked)
    {
        var count = Math.Min(original.Checkpoints.Count, rewrite.Checkpoints.Count);
        var animationNotLoaded = false;
        // a whole session: whether a raid (TAC) has run, after which CA-1 shows in the block the campaign goes on with
        var raidRan = original.Part == "TAC";
        // a whole session from item 8 on: the sound device's stream (the driver's port writes and timer ticks) is compared
        var deviceStream = original.DeviceStream;
        if (deviceStream && DeviceStream.Has(original.Checkpoints.Select(c => c.Calls), "midi")
            && !DeviceStream.Has(rewrite.Checkpoints.Select(c => c.Calls), "midi") && DeviceStream.Has(rewrite.Checkpoints.Select(c => c.Calls), "opl"))
        {
            // D-20: without the player's MT-32 ROMs the rewrite plays the Roland choice on the AdLib driver
            masked["D-20 (no MT-32 ROMs: the rewrite played the Roland choice on the AdLib driver; the MIDI stream is not compared, set COVERT_MT32_ROMS)"] = 1;
            deviceStream = false;
        }
        for (var i = 0; i < count; i++)
        {
            var (a, b) = (original.Checkpoints[i], rewrite.Checkpoints[i]);
            raidRan |= a.Part == "TAC";
            animationNotLoaded = AnimationNotLoaded(a.Calls, animationNotLoaded);
            var where = $"checkpoint {i} ({Describe(a)}, after {Taken(original, i)})";
            if (a.End != b.End)
            {
                return $"{where}: the original {(a.End ? "asked for a key after the last" : "took an entry")}, the rewrite {(b.End ? "asked for a key after the last" : "took an entry")}";
            }
            // a whole session: the program that took the entry, and the text page while the display is in text mode
            if (a.Part is not null && a.Part != b.Part)
            {
                return $"{where}: the original took it in {a.Part}, the rewrite in {b.Part ?? "no part"}";
            }
            if (a.Text is not null || b.Text is not null)
            {
                if (a.Text != b.Text)
                {
                    return $"{where}: {DescribeTexts(original, a.Text, rewrite, b.Text)}";
                }
            }
            else if (a.Picture != b.Picture
                && DescribePictures(a.Part ?? original.Part, original.Frame(a.Picture), rewrite.Frames[b.Picture], outFolder is null ? null : Path.Combine(outFolder, $"checkpoint-{i:D4}.bmp"), masked, animationNotLoaded) is { } pictures)
            {
                return $"{where}: {pictures}";
            }
            if (a.Handoff != b.Handoff && !(raidRan && GuardCountOnly(original, a, rewrite, b, masked)))
            {
                return $"{where}: the hand-off block differs (compare the blocks at the end: {DescribeBlocks(original.HandoffOut, rewrite.HandoffOut)})";
            }
            if (Sounds(a.Calls) != Sounds(b.Calls))
            {
                return $"{where}: the sounds asked for since the last checkpoint differ: original {Sounds(a.Calls)}, rewrite {Sounds(b.Calls)}";
            }
            if (deviceStream && DeviceDifference(a.Calls, b.Calls, masked) is { } device)
            {
                return $"{where}: the sound device's stream since the last checkpoint differs: {device}";
            }
            // from format 2 on: the written files where they changed (a save is compared at the checkpoint after it)
            if (original.Format >= 2 && Normal(a.Written) != Normal(b.Written))
            {
                return $"{where}: the written files differ: original {Normal(a.Written) ?? "unchanged"}, rewrite {Normal(b.Written) ?? "unchanged"}";
            }
        }
        if (original.Stop == "exit" && rewrite.Checkpoints.Count > original.Checkpoints.Count && CustomDriverNotFound(original))
        {
            // D-18: the original's launcher asks for XSOUND.CA, which was never shipped, and ends; the rewrite plays nothing
            // and goes on. Everything up to the original's end was compared; what the rewrite does after has no counterpart
            masked[$"D-18 (Custom: the original's launcher found no XSOUND.CA, printed \"Filename not found\" and ended at the DOS prompt; the rewrite went on to the menus, {rewrite.Checkpoints.Count - original.Checkpoints.Count} checkpoints after the original's end)"] = 1;
            return null;
        }
        if (original.Checkpoints.Count != rewrite.Checkpoints.Count)
        {
            return $"the original has {original.Checkpoints.Count} checkpoints, the rewrite {rewrite.Checkpoints.Count}";
        }
        if (original.Stop is not ("exit" or "script exhausted"))
        {
            return $"the original side did not finish the session ({original.Stop}): the recording proves only its checkpoints";
        }
        if (original.Stop != rewrite.Stop)
        {
            return $"the original ended with \"{original.Stop}\", the rewrite with \"{rewrite.Stop}\"";
        }
        if (Sounds(original.CallsAfter) != Sounds(rewrite.CallsAfter))
        {
            return $"the sounds asked for after the last checkpoint differ: original {Sounds(original.CallsAfter)}, rewrite {Sounds(rewrite.CallsAfter)}";
        }
        if (deviceStream)
        {
            var (tail, launcher) = DeviceStream.Tail(original.CallsAfter);
            if (DeviceDifference(tail, rewrite.CallsAfter, masked) is { } device)
            {
                return $"the sound device's stream after the last checkpoint differs: {device}";
            }
            if (launcher > 0)
            {
                // the original's launcher stops the driver after the last part; the rewrite has no such end (D-602)
                masked[$"D-602 (the launcher's end: the sound driver's stop, {launcher} device calls after the last part)"] = 1;
            }
        }
        var handoffOut = rewrite.HandoffOut;
        if (raidRan && GuardCountInCityCounter(original.HandoffOut, handoffOut) is { } counted)
        {
            // CA-1 (docs/original-bugs.md, fixed for the player): the original's raid counts its awake guards in city 0's
            // activity counter and hands it back to the campaign; the rewrite keeps the campaign's counter there
            masked[$"CA-1 (the final hand-off block: city 0's activity counter, original {counted.Original}, rewrite {counted.Rewrite})"] = 1;
            handoffOut = counted.Block;
        }
        if (!original.HandoffOut.AsSpan().SequenceEqual(handoffOut))
        {
            if (BytesBehindTextEnds(original.HandoffOut, handoffOut) is not { } behind)
            {
                return "the final hand-off block differs: " + DescribeBlocks(original.HandoffOut, handoffOut);
            }
            // D-22: the rewrite keeps a name as its text; the bytes of the original's buffer behind the end mark are not
            // kept, and the block the comparison rebuilds has the start block's bytes there
            masked[$"D-22 (the final hand-off block: {behind} bytes behind the end marks of names in the campaign tables)"] = 1;
        }
        return DescribeWritten(original.Written, rewrite.Written);
    }

    /// <summary>How two text pages differ (a whole session's set-up screen): the differing cells, the first, and the cursors.</summary>
    private static string DescribeTexts(SessionRecording original, string? a, RewriteRun rewrite, string? b)
    {
        if (a is null || b is null)
        {
            return $"the display is in text mode on the {(a is null ? "rewrite" : "original")}'s side only";
        }
        var (x, y) = (original.TextPage(a), rewrite.Frames["text-" + b]);
        const int cells = 80 * 25;
        var differing = Enumerable.Range(0, cells).Where(i => x[2 * i] != y[2 * i] || x[2 * i + 1] != y[2 * i + 1]).ToList();
        var first = differing.Count == 0 ? "" : $" (the first at row {differing[0] / 80}, column {differing[0] % 80}: original '{(char)x[2 * differing[0]]}' {x[2 * differing[0] + 1]:X2}, rewrite '{(char)y[2 * differing[0]]}' {y[2 * differing[0] + 1]:X2})";
        return $"the text pages differ: {differing.Count} cells{first}; cursor: original row {x[^2]}, column {x[^1]}, rewrite row {y[^2]}, column {y[^1]}";
    }

    /// <summary>The first written file that one side has and the other has not, or whose bytes differ; null when they agree.</summary>
    private static string? DescribeWritten(IReadOnlyDictionary<string, byte[]> a, IReadOnlyDictionary<string, byte[]> b)
    {
        foreach (var name in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
        {
            if (!a.TryGetValue(name, out var x) || !b.TryGetValue(name, out var y))
            {
                return $"the written file {name}: only the {(a.ContainsKey(name) ? "original" : "rewrite")} wrote it";
            }
            var at = x.AsSpan().CommonPrefixLength(y);
            if (at != x.Length || x.Length != y.Length)
            {
                return $"the written file {name} differs: {x.Length} bytes against {y.Length}, the first difference at byte {at}";
            }
        }
        return null;
    }

    /// <summary>
    /// Whether the original's last animation file failed to load and has not been released, after a checkpoint's calls
    /// (the original side watches <c>load_pan_file</c> and <c>release_pan_memory</c>; KnownDifference D-11).
    /// </summary>
    private static bool AnimationNotLoaded(string calls, bool before)
    {
        var state = before;
        foreach (var call in JsonNode.Parse(calls)!.AsArray())
        {
            switch (call![0]!.GetValue<string>())
            {
                case "pan_load":
                    state = call[2] is { } loaded && loaded.GetValue<int>() == 0;
                    break;
                case "pan_release":
                    state = false;
                    break;
            }
        }
        return state;
    }

    /// <summary>
    /// D-18's condition: the original's launcher tried to load the Custom choice's XSOUND.CA as a driver, DOS found no such
    /// file, and the run ended with "Filename not found" on the text page.
    /// </summary>
    private static bool CustomDriverNotFound(SessionRecording original) =>
        original.CallsAfter.Contains("[\"overlay\",\"XSOUND.CA\",\"not found\"]", StringComparison.Ordinal)
        && original.TextAtEndCharacters().Contains("Filename not found", StringComparison.Ordinal);

    /// <summary>How the sound device's streams of two lists of calls differ (null: they agree); a tick inside a driver call is counted under D-19.</summary>
    private static string? DeviceDifference(string original, string rewrite, IDictionary<string, int> masked)
    {
        var x = DeviceStream.Normal(original, original: true, out var reordered);
        var y = DeviceStream.Normal(rewrite, original: false, out _);
        if (reordered > 0)
        {
            masked[DeviceStream.TickInsideCall] = masked.TryGetValue(DeviceStream.TickInsideCall, out var n) ? n + 1 : 1;
        }
        return x == y ? null : DeviceStream.Describe(x, y);
    }

    /// <summary>A checkpoint's written-file hashes in one spelling (JSON without spaces), or null.</summary>
    private static string? Normal(string? written) => written is null ? null : JsonNode.Parse(written)!.ToJsonString();

    /// <summary>The sound requests among a checkpoint's calls, as text: the calls both sides record.</summary>
    private static string Sounds(string calls) =>
        "[" + string.Join(",", JsonNode.Parse(calls)!.AsArray().Where(c => c![0]!.GetValue<string>() == "play_sound").Select(c => c!.ToJsonString())) + "]";

    private static string Describe(Checkpoint c) => c.End ? "end of the script" : c.Entry is { } key ? $"key {key:X4}" : "poll without a key";

    private static string Taken(SessionRecording original, int i) => i == 0 ? "the start" : $"{i} entries";

    /// <summary>
    /// Says how two frames differ and writes the picture of the difference; null when they differ only inside the
    /// part's registered differences (<see cref="KnownDifference"/>), whose entries are added to <paramref name="masked"/>.
    /// </summary>
    private static string? DescribePictures(string part, byte[] a, byte[] b, string? bitmap, IDictionary<string, int> masked, bool animationNotLoaded)
    {
        var met = new HashSet<string>(StringComparer.Ordinal);
        var pixels = 0;
        var first = -1;
        for (var i = 0; i < Pixels; i++)
        {
            if (a[i] == b[i])
            {
                continue;
            }
            if (KnownDifference.At(part, i % Width, i / Width, animationNotLoaded) is { } known)
            {
                met.Add(known.Entry);
                continue;
            }
            pixels++;
            first = first < 0 ? i : first;
        }
        foreach (var entry in met)
        {
            masked[entry] = masked.TryGetValue(entry, out var n) ? n + 1 : 1;
        }
        var colours = Enumerable.Range(0, 256).Count(c => !a.AsSpan(Pixels + 3 * c, 3).SequenceEqual(b.AsSpan(Pixels + 3 * c, 3)));
        if (pixels == 0 && colours == 0)
        {
            return null;
        }
        if (bitmap is not null)
        {
            DiffBitmap.Write(bitmap, a, b);
            // the rewrite's frame in the recording's format, beside the picture (the original's is in the recording)
            File.WriteAllBytes(Path.ChangeExtension(bitmap, ".rewrite.idx"), b);
        }
        var where = first < 0 ? "" : $" (the first at x {first % Width}, y {first / Width}: original {a[first]}, rewrite {b[first]})";
        return $"{pixels} pixels differ{where}, {colours} colours differ" + (bitmap is null ? "" : $"; picture: {bitmap}");
    }

    /// <summary>
    /// CA-1 at a checkpoint of a whole session after a raid: whether the two blocks differ only in the word of city 0's
    /// activity counter (the campaign goes on with the original's raid's guard count there, the rewrite's own counter);
    /// counted under CA-1 when so. Needs the blocks themselves (a whole session's recording keeps every distinct one).
    /// </summary>
    private static bool GuardCountOnly(SessionRecording original, Checkpoint a, RewriteRun rewrite, Checkpoint b, IDictionary<string, int> masked)
    {
        if (original.Block(a.Handoff) is not { } x || !rewrite.Frames.TryGetValue("block-" + b.Handoff, out var y)
            || GuardCountInCityCounter(x, y) is not { } counted || !counted.Block.AsSpan().SequenceEqual(x))
        {
            return false;
        }
        const string entry = "CA-1 (the hand-off block after a raid: city 0's activity counter)";
        masked[entry] = masked.TryGetValue(entry, out var n) ? n + 1 : 1;
        return true;
    }

    /// <summary>
    /// CA-1: when the raid's final blocks differ in the word of city 0's activity counter (the first word of the counter
    /// block at the end of the campaign tables, where the original's raid counts its awake guards), that word's two
    /// values and the rewrite's block with the original's word in its place; else null. Every other byte is still compared.
    /// </summary>
    private static (short Original, short Rewrite, byte[] Block)? GuardCountInCityCounter(byte[] a, byte[] b)
    {
        var cast = BinaryPrimitives.ReadInt16LittleEndian(a.AsSpan(0x3e));
        var events = BinaryPrimitives.ReadInt16LittleEndian(a.AsSpan(0x40));
        if (cast < 0 || events < 0 || cast != BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0x3e))
            || events != BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0x40))
            || CampaignTablesCodec.Size(cast, events) > a.Length - SharedState.TablesOffset)
        {
            return null;
        }
        var at = SharedState.TablesOffset + CampaignTablesCodec.Size(cast, events) - CampaignTables.ActiveWords * sizeof(short);
        if (a.AsSpan(at, 2).SequenceEqual(b.AsSpan(at, 2)))
        {
            return null;
        }
        var block = (byte[])b.Clone();
        a.AsSpan(at, 2).CopyTo(block.AsSpan(at));
        return (BinaryPrimitives.ReadInt16LittleEndian(a.AsSpan(at)), BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at)), block);
    }

    /// <summary>
    /// The number of differing bytes when two hand-off blocks differ only behind the end marks of texts in the campaign
    /// tables (D-22); else null. Holds when the bytes before the tables are the same, the tables decode to the same
    /// records (every value the rewrite keeps agrees), and every differing byte lies in a text field of a record behind
    /// an end mark that both blocks have there.
    /// </summary>
    private static int? BytesBehindTextEnds(byte[] a, byte[] b)
    {
        const int tables = SharedState.TablesOffset;
        if (!a.AsSpan(0, tables).SequenceEqual(b.AsSpan(0, tables)))
        {
            return null;
        }
        var cast = BinaryPrimitives.ReadInt16LittleEndian(a.AsSpan(0x3e));
        var events = BinaryPrimitives.ReadInt16LittleEndian(a.AsSpan(0x40));
        if (cast < 0 || events < 0 || CampaignTablesCodec.Size(cast, events) > a.Length - tables)
        {
            return null;
        }
        byte[] Typed(byte[] block)
        {
            var records = new CampaignTables();
            CampaignTablesCodec.Load(block.AsSpan(tables), cast, events, records);
            var image = new byte[CampaignTablesCodec.Size(cast, events)];
            CampaignTablesCodec.Store(records, image);
            return image;
        }
        if (!Typed(a).AsSpan().SequenceEqual(Typed(b)))
        {
            return null;
        }
        var texts = TextFields(cast, events).ToList();
        var differing = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
        var behind = differing.All(i => texts.Any(t => i >= t.Start && i < t.Start + t.Length
            && a.AsSpan(t.Start, i - t.Start).Contains((byte)0) && b.AsSpan(t.Start, i - t.Start).Contains((byte)0)));
        return behind ? differing.Count : null;
    }

    /// <summary>The text fields of the campaign tables in a hand-off block (their places in the block), for a cast and events of these sizes.</summary>
    private static IEnumerable<(int Start, int Length)> TextFields(int cast, int events)
    {
        // the tables in the order they lie in the block (CampaignTablesCodec), with their record layouts and counts
        (IEnumerable<LayoutField> Fields, int Size, int Count)[] order =
        [
            (CampaignLayouts.Plot.Fields, CampaignLayouts.PlotSize, 1),
            (CampaignLayouts.Activity.Fields, CampaignLayouts.ActivitySize, cast),
            (CampaignLayouts.Activity2.Fields, CampaignLayouts.Activity2Size, cast),
            (CampaignLayouts.Event.Fields, CampaignLayouts.EventSize, events),
            (CampaignLayouts.City.Fields, CampaignLayouts.CitySize, CampaignTables.WorldSize),
            (CampaignLayouts.Org.Fields, CampaignLayouts.OrgSize, CampaignTables.WorldSize),
            (CampaignLayouts.Person.Fields, CampaignLayouts.PersonSize, CampaignTables.PersonCount),
            (CampaignLayouts.Location.Fields, CampaignLayouts.LocationSize, CampaignTables.LocationCount),
            (CampaignLayouts.Clue.Fields, CampaignLayouts.ClueSize, CampaignTables.ClueCount),
            (CampaignLayouts.Synopsis.Fields, CampaignLayouts.SynopsisSize, CampaignTables.SynopsisCount),
            (CampaignLayouts.MacGuffins.Fields, CampaignLayouts.MacGuffinsSize, CampaignTables.MacGuffinCount),
        ];
        var at = SharedState.TablesOffset;
        foreach (var (fields, size, count) in order)
        {
            // a text is a field of more than a word whose name says it is one (the layouts' Text fields)
            var named = fields.Where(f => f.Length > 2 && (f.Name.EndsWith("name", StringComparison.Ordinal) || f.Name.EndsWith("name2", StringComparison.Ordinal) || f.Name.EndsWith("handle", StringComparison.Ordinal))).ToList();
            for (var r = 0; r < count; r++)
            {
                foreach (var field in named)
                {
                    yield return (at + r * size + field.Offset, field.Length);
                }
            }
            at += size * count;
        }
    }

    private static string DescribeBlocks(byte[] a, byte[] b)
    {
        var differing = Enumerable.Range(0, Math.Min(a.Length, b.Length)).Where(i => a[i] != b[i]).ToList();
        if (differing.Count == 0)
        {
            return "no byte differs";
        }
        var fields = SharedBlockCodec.Layout.Fields.ToList();
        var names = differing
            .Select(i => fields.FirstOrDefault(f => i >= f.Offset && i < f.Offset + f.Length) is { Name: { } name } ? name : $"SHARED+0x{i:x}")
            .Distinct()
            .Take(12);
        return $"{differing.Count} bytes: {string.Join(", ", names)}";
    }
}

/// <summary>The picture of a difference: the original's screen, the rewrite's, and the rewrite's in grey with the differing pixels in red.</summary>
internal static class DiffBitmap
{
    private const int Width = 320, Height = 200, Gap = 4;

    /// <summary>Writes the three pictures side by side as a 24-bit Windows bitmap.</summary>
    public static void Write(string path, byte[] original, byte[] rewrite)
    {
        const int width = 3 * Width + 2 * Gap, rowBytes = width * 3;   // 968 pixels: 2904 bytes, a multiple of four
        var image = new byte[rowBytes * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = y * Width + x;
                Put(image, y, x, Colour(original, original[i]));
                Put(image, y, Width + Gap + x, Colour(rewrite, rewrite[i]));
                var (r, g, b) = Colour(rewrite, rewrite[i]);
                var grey = (byte)((r + g + b) / 3);
                Put(image, y, 2 * (Width + Gap) + x, original[i] != rewrite[i] ? ((byte)255, (byte)0, (byte)0) : (grey, grey, grey));
            }
        }
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("BM"u8);
        writer.Write(54 + image.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-Height);          // rows from the top
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(image.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(image);
    }

    /// <summary>A palette index's colour in a frame, the DAC's six bits widened to eight.</summary>
    private static (byte R, byte G, byte B) Colour(byte[] frame, byte index)
    {
        static byte Widen(byte v) => (byte)((v & 63) << 2 | (v & 63) >> 4);
        var at = Width * Height + 3 * index;
        return (Widen(frame[at]), Widen(frame[at + 1]), Widen(frame[at + 2]));
    }

    private static void Put(byte[] image, int y, int x, (byte R, byte G, byte B) c)
    {
        var at = (y * (3 * Width + 2 * Gap) + x) * 3;
        (image[at], image[at + 1], image[at + 2]) = (c.B, c.G, c.R);
    }
}
