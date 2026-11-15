using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>The variables of the building menus: the building's texts, the CIA building's and the colour maps of their scenes.</summary>
public sealed partial class CampaignState
{
    /// <summary>The texts of a building's menu, the arrival at the CIA building and the stakeout.</summary>
    public BuildingWords BuildingWords { get; } = new();

    /// <summary>The texts of the CIA building and the chief's advice.</summary>
    public HeadquartersWords HeadquartersWords { get; } = new();

    /// <summary>The texts of the findings of a search.</summary>
    public InvestigationWords InvestigationWords { get; } = new();

    /// <summary>
    /// GAME DS:54dc building_pan_name: the name of the building's animation, and the scratch buffer the minutes of a
    /// wiretap are written into as text.
    /// </summary>
    public TextBuffer BuildingPanName { get; } = new(16);

    /// <summary>GAME DS:54ee closeness_percent: how near the building's organisation is to the player's agency, 0..99 (the menu works it out; nothing reads it).</summary>
    public short ClosenessPercent { get; set; }

    /// <summary>Buildings are shown by one of this many animations (the number is the building's level less two, within 0..3).</summary>
    public const int BuildingScenes = 4;

    /// <summary>GAME DS:15ce building_palettes: the colour map of each building animation (one table of 16 bytes each).</summary>
    public byte[][] BuildingColorMaps { get; } = [.. Enumerable.Range(0, BuildingScenes).Select(_ => new byte[16])];

    /// <summary>GAME DS:155e: the colour map of the CIA building's scene.</summary>
    public byte[] HeadquartersColorMap { get; } = new byte[16];

    /// <summary>GAME DS:156e: the colour map of the intelligence section's scene.</summary>
    public byte[] IntelligenceColorMap { get; } = new byte[16];

    /// <summary>GAME DS:157e: the colour map of the crypto branch's scene.</summary>
    public byte[] CryptoColorMap { get; } = new byte[16];

    /// <summary>GAME DS:158e: the colour map of the data section's scene.</summary>
    public byte[] DataSectionColorMap { get; } = new byte[16];

    /// <summary>GAME DS:15be: the colour map of the stakeout's scene.</summary>
    public byte[] BinocularsColorMap { get; } = new byte[16];

    /// <summary>GAME DS:162e: the colour map of the chief's office.</summary>
    public byte[] ChiefColorMap { get; } = new byte[16];

    /// <summary>GAME DS:164e: the colour map of the scene of being followed.</summary>
    public byte[] FollowedColorMap { get; } = new byte[16];

    /// <summary>GAME DS:165e: the colour map of the car tracer's scene.</summary>
    public byte[] TraceColorMap { get; } = new byte[16];
}
