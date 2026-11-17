namespace CovertAction.Content;

/// <summary>
/// Opens the files of the player's own folders (the game folder, the Amiga files folder, the MT-32 ROM folder) by their
/// names whatever letter case the host's disk spells them in. The DOS original's names have no case (the game's files are
/// upper case, a GOG copy may mix cases); a case-sensitive file system (Linux, macOS) would otherwise miss "fame.dta" when
/// the file is "FAME.DTA". The exact name is tried first; otherwise the folder is listed once (cached per folder, refreshed
/// when the folder changes) and matched ignoring case. Files the program writes keep the names chosen for them.
/// </summary>
/// <remarks>New code, not part of the original: host groundwork, nothing the player sees changes.</remarks>
#if COVERT_LINKED
internal static class HostFiles
#else
public static class HostFiles
#endif
{
    private sealed record Listing(DateTime Stamp, Dictionary<string, string> Names);

    private static readonly Dictionary<string, Listing> Cache = new(StringComparer.Ordinal);

    /// <summary>The host path of a file in <paramref name="folder"/>, matching <paramref name="name"/> ignoring case; null when there is none.</summary>
    public static string? Find(string folder, string name)
    {
        try
        {
            var exact = Path.Combine(folder, name);
            return File.Exists(exact) ? exact : FindByListing(folder, name, isDirectory: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The host path of a folder in <paramref name="folder"/>, matching <paramref name="name"/> ignoring case; null when there is none.</summary>
    public static string? FindDirectory(string folder, string name)
    {
        try
        {
            var exact = Path.Combine(folder, name);
            return Directory.Exists(exact) ? exact : FindByListing(folder, name, isDirectory: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="root"/> with the folders <paramref name="below"/> appended, each spelled as it is on disk when it exists
    /// in some letter case; a folder that is not there keeps the given spelling (and so do the ones below it).
    /// </summary>
    public static string CombineDirectories(string root, IEnumerable<string> below)
    {
        var path = root;
        foreach (var name in below)
        {
            path = FindDirectory(path, name) ?? Path.Combine(path, name);
        }
        return path;
    }

    /// <summary>Whether <paramref name="folder"/> has a file named <paramref name="name"/>, in any letter case.</summary>
    public static bool Exists(string folder, string name) => Find(folder, name) is not null;

    /// <summary>All bytes of a file of <paramref name="folder"/> named <paramref name="name"/> in any letter case.</summary>
    /// <exception cref="FileNotFoundException">The folder has no such file.</exception>
    public static byte[] ReadAllBytes(string folder, string name) =>
        File.ReadAllBytes(Find(folder, name) ?? throw new FileNotFoundException($"{name} is not in {folder}", name));

    /// <summary>
    /// The entry of <paramref name="names"/> equal to <paramref name="name"/> ignoring case, the exact spelling first (the
    /// pure matching rule behind <see cref="Find"/>; ties between spellings go to the ordinally smallest, so the answer
    /// does not depend on the listing's order); null when none.
    /// </summary>
    public static string? Match(IEnumerable<string> names, string name)
    {
        string? best = null;
        foreach (var candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return candidate;
            }
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
                && (best is null || string.CompareOrdinal(candidate, best) < 0))
            {
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Forgets the cached listings.</summary>
    public static void Forget()
    {
        lock (Cache)
        {
            Cache.Clear();
        }
    }

    /// <summary>The path of the entry of <paramref name="folder"/> that matches <paramref name="name"/> ignoring case, by the cached listing alone (what <see cref="Find"/> falls back to; the host decides nothing here).</summary>
    public static string? FindByListing(string folder, string name, bool isDirectory)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }
        var key = (isDirectory ? "d|" : "f|") + Path.GetFullPath(folder);
        var stamp = Directory.GetLastWriteTimeUtc(folder);
        Listing listing;
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var cached) || cached.Stamp != stamp)
            {
                var entries = isDirectory ? Directory.EnumerateDirectories(folder) : Directory.EnumerateFiles(folder);
                var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries.Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
                {
                    byName.TryAdd(entry, entry);
                }
                cached = new Listing(stamp, byName);
                Cache[key] = cached;
            }
            listing = cached;
        }
        return listing.Names.TryGetValue(name, out var actual) ? Path.Combine(folder, actual) : null;
    }
}
