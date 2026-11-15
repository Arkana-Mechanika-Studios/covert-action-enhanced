using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace CovertAction.Architecture.Tests;

/// <summary>
/// The layers of ARCHITECTURE.md, checked on the compiled assemblies: who may reference whom, who may touch the
/// outside world, and that the game's code keeps no hidden global state.
/// </summary>
public sealed class LayerTests
{
    private const string Msc = "CovertAction.Msc";
    private const string Core = "CovertAction.Core";
    private const string Platform = "CovertAction.Platform";
    private const string Content = "CovertAction.Content";
    private const string Persistence = "CovertAction.Persistence";
    private const string Presentation = "CovertAction.Presentation";
    private const string Drivers = "CovertAction.Drivers";
    private const string Audio = "CovertAction.Audio";
    private const string AudioMt32 = "CovertAction.Audio.Mt32";
    private const string Launcher = "CovertAction.Launcher";
    private const string HostHeadless = "CovertAction.Host.Headless";

    /// <summary>The player's settings and the launcher (the settings screen): host code, which knows nothing of the game.</summary>
    private const string Settings = "CovertAction.Settings";

    /// <summary>The SDL host is the program players start; its assembly carries the program's name.</summary>
    private const string HostSdl = "CovertAction";

    /// <summary>The hosts: programs around the game. The game is every other assembly.</summary>
    private static readonly string[] Hosts = [HostHeadless, Settings, HostSdl];

    /// <summary>What Platform declares besides interfaces and the plain values their calls carry, each for a stated reason.</summary>
    private static readonly string[] PlatformClasses =
    [
        "HostKeys",         // the key codes hosts deliver: constants and the table from a character to its key
        "SilentPcmOutput",  // the sound device of a host without sound, shared by the hosts
        "TextScreen",      // the 80x25 text page of characters and attributes that IVideo.PresentText hands a host (video mode 3)
    ];

    /// <summary>
    /// The one third-party library of the product and the assembly that links it: Munt's MT-32 emulation (its NuGet
    /// binding, LGPL, which brings the native libmt32emu), the module the Roland driver's MIDI plays on with the player's ROMs.
    /// The settings project also links .NET's own Microsoft.Win32.Registry (GOG's and Steam's registrations, read to find the game).
    /// </summary>
    private static readonly Dictionary<string, string> ThirdParty = new() { [AudioMt32] = "Mt32emu", [Settings] = "Microsoft.Win32.Registry" };

    /// <summary>What each product assembly may reference among the product assemblies (ARCHITECTURE.md "Layers").</summary>
    private static readonly Dictionary<string, string[]> MayReference = new()
    {
        [Msc] = [],
        [Core] = [Msc],
        [Platform] = [Core, Msc],
        [Content] = [Core, Msc],
        [Persistence] = [Core, Msc],
        [Presentation] = [Core, Msc, Platform, Content, Persistence],
        [Drivers] = [Core, Msc, Platform, Content],
        [Audio] = [Platform, Content],
        [AudioMt32] = [Platform, Content, Audio],
        [Launcher] = [Core, Msc, Platform, Content, Persistence, Presentation, Drivers, Audio, AudioMt32],
        [HostHeadless] = [Platform, Content, Launcher],
        [Settings] = [],
        [HostSdl] = [Platform, Content, Launcher, Settings],
    };

    /// <summary>
    /// The assemblies whose code must be deterministic and closed: no file, console, clock, random generator of the
    /// host, thread or network. Everything outward goes through the interfaces of CovertAction.Platform; only Content
    /// (and later the hosts) opens files.
    /// </summary>
    private static readonly string[] Closed = [Msc, Core, Platform, Persistence, Presentation, Drivers];

    /// <summary>Base-library types and namespaces that reach outside the process or make a run unrepeatable.</summary>
    private static readonly string[] Outside =
    [
        "System.IO.", "System.Net.", "System.Threading.", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch",
        "System.Console", "System.DateTime", "System.DateTimeOffset", "System.TimeProvider", "System.Random", "System.Environment",
        "System.Guid", "System.Security.Cryptography.",
    ];

    private static Assembly Load(string name) => Assembly.Load(name);

    private static IEnumerable<string> ProductReferences(string name) =>
        Load(name).GetReferencedAssemblies().Select(r => r.Name!).Where(r => r == HostSdl || r.StartsWith("CovertAction.", StringComparison.Ordinal));

    /// <summary>The native libraries an assembly calls into (the modules of its P/Invoke declarations).</summary>
    private static List<string> NativeLibraries(string name)
    {
        using var stream = File.OpenRead(Load(name).Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return [.. Enumerable.Range(1, metadata.GetTableRowCount(TableIndex.ModuleRef))
            .Select(row => metadata.GetString(metadata.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row)).Name))];
    }

    /// <summary>The full names of every type of another assembly that the code of <paramref name="name"/> uses.</summary>
    private static List<string> TypeReferences(string name)
    {
        using var stream = File.OpenRead(Load(name).Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return [.. metadata.TypeReferences.Select(handle =>
        {
            var type = metadata.GetTypeReference(handle);
            var ns = metadata.GetString(type.Namespace);
            return ns.Length == 0 ? metadata.GetString(type.Name) : ns + "." + metadata.GetString(type.Name);
        })];
    }

    [Fact]
    public void EveryProductAssemblyHasARow()
    {
        var built = Directory.EnumerateFiles(AppContext.BaseDirectory, "CovertAction*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !n!.EndsWith(".Tests", StringComparison.Ordinal))
            .Order();
        Assert.Equal(MayReference.Keys.Order(), built);
    }

    [Fact]
    public void EachAssemblyReferencesOnlyTheLayersBelowIt()
    {
        var breaks = MayReference.Keys
            .SelectMany(name => ProductReferences(name).Where(r => !MayReference[name].Contains(r)).Select(r => $"{name} -> {r}"))
            .ToList();
        Assert.True(breaks.Count == 0, "references against the layers:\n" + string.Join("\n", breaks));
    }

    [Fact]
    public void TheLayersHaveNoCycle()
    {
        // every assembly's allowed references come before it in this order
        string[] order = [Msc, Core, Platform, Content, Persistence, Presentation, Drivers, Audio, AudioMt32, Launcher, HostHeadless, Settings, HostSdl];
        Assert.All(order, name => Assert.All(MayReference[name], lower => Assert.True(Array.IndexOf(order, lower) < Array.IndexOf(order, name))));
    }

    [Fact]
    public void TheGameReachesTheOutsideWorldOnlyThroughTheBoundaryInterfaces()
    {
        var breaks = Closed
            .SelectMany(name => TypeReferences(name)
                .Where(type => Outside.Any(o => o.EndsWith('.') ? type.StartsWith(o, StringComparison.Ordinal) : type == o))
                .Select(type => $"{name} uses {type}"))
            .ToList();
        Assert.True(breaks.Count == 0, "I/O, clock or host randomness in closed layers:\n" + string.Join("\n", breaks));
    }

    [Fact]
    public void TheBoundaryIsInterfacesAndTheValuesTheirCallsCarry()
    {
        var others = Load(Platform).GetExportedTypes()
            .Where(t => !t.IsInterface && !t.IsValueType && !PlatformClasses.Contains(t.Name))
            .Select(t => t.FullName).ToList();
        Assert.True(others.Count == 0, "CovertAction.Platform declares more than interfaces and values:\n" + string.Join("\n", others));
    }

    // ---- no DOS machine in the product (ARCHITECTURE.md "No emulator") ----

    /// <summary>
    /// The product is plain C#: it links no library but .NET's own, so no processor emulator, no DOS and no part of
    /// the oracle (which runs the original in Unicorn, in tools/ and tests only) can be in it.
    /// </summary>
    [Fact]
    public void TheProductLinksNothingButDotNetAndItself()
    {
        var breaks = MayReference.Keys
            .SelectMany(name => Load(name).GetReferencedAssemblies()
                .Where(r => !r.Name!.StartsWith("System", StringComparison.Ordinal) && r.Name != "netstandard" && r.Name != "mscorlib" && !MayReference.ContainsKey(r.Name))
                .Where(r => !(ThirdParty.TryGetValue(name, out var allowed) && r.Name == allowed))
                .Select(r => $"{name} -> {r.Name}"))
            .ToList();
        Assert.True(breaks.Count == 0, "third-party libraries in the product:\n" + string.Join("\n", breaks));
    }

    /// <summary>
    /// The game asks for named services (a frame wait, a key, the mouse's position), never for a hardware port, a
    /// software interrupt or a processor register: the boundary has no member for them.
    /// </summary>
    [Fact]
    public void TheBoundaryHasNoPortsInterruptsOrRegisters()
    {
        // the C library's port and interrupt routines by name, and any name that speaks of interrupts, segments or registers
        string[] machineRoutines = ["Inp", "Inpw", "Outp", "Outpw", "Int86", "Int86x", "Intdos", "Intdosx"];
        string[] machineWords = ["Interrupt", "Segment", "Register"];
        var breaks = Load(Platform).GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(member => (Owner: type.Name, member.Name)).Append((Owner: "", type.Name)))
            .Where(m => machineRoutines.Contains(m.Name) || machineWords.Any(word => m.Name.Contains(word, StringComparison.Ordinal)))
            .Select(m => m.Owner.Length == 0 ? m.Name : $"{m.Owner}.{m.Name}")
            .Distinct().ToList();
        Assert.True(breaks.Count == 0, "the boundary names a piece of the PC:\n" + string.Join("\n", breaks));
    }

    // ---- hosts (ARCHITECTURE.md "Hosts") ----

    [Fact]
    public void NoPartOfTheGameReferencesAHost()
    {
        var breaks = MayReference.Keys.Except(Hosts)
            .SelectMany(name => ProductReferences(name).Where(Hosts.Contains).Select(r => $"{name} -> {r}"))
            .ToList();
        Assert.True(breaks.Count == 0, "the game depends on a host:\n" + string.Join("\n", breaks));
    }

    [Fact]
    public void NothingReferencesTheSdlHost()
    {
        var breaks = MayReference.Keys.Where(name => ProductReferences(name).Contains(HostSdl)).ToList();
        Assert.True(breaks.Count == 0, "references to the SDL host from: " + string.Join(", ", breaks));
    }

    /// <summary>Only the SDL host calls native code: SDL 3, and the system for its own window dressing.</summary>
    [Fact]
    public void OnlyTheSdlHostCallsNativeLibraries()
    {
        var breaks = MayReference.Keys.Where(name => name != HostSdl)
            .SelectMany(name => NativeLibraries(name).Select(library => $"{name} calls into {library}"))
            .ToList();
        Assert.True(breaks.Count == 0, string.Join("\n", breaks));
        Assert.Contains(NativeLibraries(HostSdl), library => library.Contains("SDL3", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// State lives in the state objects that are passed around, never in static variables: a static field that can be
    /// assigned would be a second, hidden copy of the game's state (and would break running two games or two tests side by side).
    /// </summary>
    [Fact]
    public void TheGameKeepsNoStateInStaticFields()
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var breaks = Closed
            .SelectMany(name => Load(name).GetTypes())
            .Where(type => !IsCompilerMade(type))
            .SelectMany(type => type.GetFields(statics))
            .Where(field => !field.IsInitOnly && !field.IsLiteral && !IsCompilerMade(field))
            .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}")
            .ToList();
        Assert.True(breaks.Count == 0, "assignable static fields:\n" + string.Join("\n", breaks));
    }

    private static bool IsCompilerMade(MemberInfo member) =>
        member.Name.Contains('<') || member.IsDefined(typeof(CompilerGeneratedAttribute), false)
        || (member.DeclaringType is { } outer && IsCompilerMade(outer));
}
