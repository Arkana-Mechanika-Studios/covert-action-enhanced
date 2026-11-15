using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CovertAction.Settings;

/// <summary>The two registry roots the searches read.</summary>
internal enum RegistryRoot
{
    /// <summary>HKEY_LOCAL_MACHINE.</summary>
    LocalMachine,

    /// <summary>HKEY_CURRENT_USER.</summary>
    CurrentUser,
}

/// <summary>
/// What the searches for the game's folder (and the ROMs') read of the machine: the registry, environment variables, the
/// fixed drives and a text file. The real one is <see cref="SystemLookup"/>; tests give a fake one so they do not depend on
/// the machine they run on. Every member answers "nothing" rather than throw.
/// </summary>
internal interface IMachineLookup
{
    /// <summary>A string value of a registry key, or null (no registry, no key, no such value, not readable, not a string).</summary>
    string? RegistryValue(RegistryRoot root, string key, string name);

    /// <summary>The names of a registry key's subkeys; empty when there are none or the key cannot be read.</summary>
    IReadOnlyList<string> RegistrySubKeys(RegistryRoot root, string key);

    /// <summary>An environment variable's value, or null.</summary>
    string? Environment(string name);

    /// <summary>The root folders of the fixed drives that are ready ("C:\").</summary>
    IEnumerable<string> FixedDrives();

    /// <summary>A text file's content, or null when it is not there or cannot be read.</summary>
    string? ReadText(string path);
}

/// <summary>The machine this program runs on (the registry only on Windows).</summary>
internal sealed class SystemLookup : IMachineLookup
{
    /// <summary>The one instance (it holds no state).</summary>
    public static readonly SystemLookup Instance = new();

    /// <inheritdoc/>
    public string? RegistryValue(RegistryRoot root, string key, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            using var k = Open(root, key);
            return k?.GetValue(name) as string is { Length: > 0 } value ? value : null;
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> RegistrySubKeys(RegistryRoot root, string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        try
        {
            using var k = Open(root, key);
            return k is null ? [] : k.GetSubKeyNames();
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? Open(RegistryRoot root, string key) =>
        (root == RegistryRoot.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(key);

    private static bool IsUnreadable(Exception e) =>
        e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException or ObjectDisposedException;

    /// <inheritdoc/>
    public string? Environment(string name) => System.Environment.GetEnvironmentVariable(name);

    /// <inheritdoc/>
    public IEnumerable<string> FixedDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var roots = new List<string>();
        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a drive that cannot be asked is skipped
            }
        }
        return roots;
    }

    /// <inheritdoc/>
    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
