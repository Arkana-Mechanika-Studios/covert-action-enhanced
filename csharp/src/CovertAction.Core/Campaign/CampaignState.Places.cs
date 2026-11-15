namespace CovertAction.Core.Campaign;

/// <summary>The variables of the campaign's walk: the city screen, its picture, the flight map and the part's start.</summary>
public sealed partial class CampaignState
{
    /// <summary>The texts of the city screen, the hotel, the data files' menu, the flight map and the loader.</summary>
    public CityScreenWords CityWords { get; } = new();

    /// <summary>
    /// GAME DS:a3e8 Begun (MicroProse's name): the case has begun; set when the part comes back from a mission and at
    /// the end of the case's briefing.
    /// </summary>
    public short Begun { get; set; }

    /// <summary>
    /// GAME DS:9686 (no name): whether a break-in and a wiretap are played as missions of their own (the building's
    /// menu hands over to the raid or the wiretap) instead of being settled at once; the part's start sets it.
    /// </summary>
    public short MissionsPlayed { get; set; }

    /// <summary>GAME DS:8ed6 picture_extent_x: the width of the picture last measured (the last of its first row's coloured pixels).</summary>
    public short PictureExtentX { get; set; }

    /// <summary>GAME DS:8eda picture_extent_y: the height of the picture last measured (down its first column).</summary>
    public short PictureExtentY { get; set; }

    /// <summary>GAME DS:5510 cached_picture_width: the width of the city picture the picture page holds.</summary>
    public short CachedPictureWidth { get; set; }

    /// <summary>GAME DS:5512 cached_picture_height: the height of the city picture the picture page holds.</summary>
    public short CachedPictureHeight { get; set; }

    /// <summary>GAME DS:169e: the colour map of a prisoner's break-out scene.</summary>
    public byte[] BustOutColorMap { get; } = new byte[16];

    /// <summary>GAME DS:163e: the colour map of the wounded agent's scene.</summary>
    public byte[] WoundedColorMap { get; } = new byte[16];
}
