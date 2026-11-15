using CovertAction.Core.Graphics;

namespace CovertAction.Core.Raid;

/// <summary>The variables of the raid's start: the pictures it loads and the equipment screen's places.</summary>
public sealed partial class RaidState
{
    /// <summary>The number of rectangles of the equipment screen: one for each of the <see cref="ItemIcons"/>.</summary>
    public const int EquipAreaCount = ItemIconCount;

    /// <summary>TAC DS:1c24: the number of the memory the graphics driver gave for the picture pages' working page (page 3).</summary>
    public short PageBuffer { get; set; }

    /// <summary>TAC DS:bb62 hour (MicroProse's name): the hour of the day the raid begins in, 0..23.</summary>
    public short Hour { get; set; }

    /// <summary>TAC DS:2194 equip_src_rect: where each item's picture is on the equipment sheet (inclusive corners).</summary>
    public ScreenArea[] EquipSourceAreas { get; } = [.. Enumerable.Range(0, EquipAreaCount).Select(_ => new ScreenArea())];

    /// <summary>TAC DS:220c equip_rect: where each item's box is on the equipment screen (inclusive corners): the cursor recolours it, a pick redraws it. The program's table has 12 entries (item 0 is none), though its header declares 15: the data after it is other tables.</summary>
    public ScreenArea[] EquipAreas { get; } = [.. Enumerable.Range(0, EquipNeighbourCount).Select(_ => new ScreenArea())];

    // ---- the names of the picture files the raid loads, and two pieces of its door menu ----

    /// <summary>TAC DS:0074: the picture the speech balloon is cut from ("camera.pic").</summary>
    public string BalloonSheetName { get; set; } = "";

    /// <summary>TAC DS:007f: the sheet of the guards' pictures ("guys3.pic"), loaded onto the sprite sheet's page.</summary>
    public string GuardSheetName { get; set; } = "";

    /// <summary>TAC DS:0089: the sheet of the guards' pictures of the one-room ambush ("guys2.pic").</summary>
    public string AmbushGuardSheetName { get; set; } = "";

    /// <summary>TAC DS:0093: the picture of the raid's screen (frame, radar, status panel) when the agent is Maxine ("equip1.pic").</summary>
    public string RaidScreenFemaleName { get; set; } = "";

    /// <summary>TAC DS:009e: the picture of the raid's screen when the agent is Max ("equip1m.pic").</summary>
    public string RaidScreenMaleName { get; set; } = "";

    /// <summary>TAC DS:2f0c: the picture sheet of the first 40 faces ("facesf.pic").</summary>
    public string FacesWomenName { get; set; } = "";

    /// <summary>TAC DS:2f17: the picture sheet of the other 40 faces ("faces.pic").</summary>
    public string FacesMenName { get; set; } = "";

    /// <summary>TAC DS:2007: the sprite sheet of the building when the agent is Maxine ("spritesf.pic").</summary>
    public string SpritesFemaleName { get; set; } = "";

    /// <summary>TAC DS:2014: the sprite sheet of the building when the agent is Max ("sprites.pic").</summary>
    public string SpritesMaleName { get; set; } = "";

    /// <summary>TAC DS:20d5: the picture of the equipment screen ("equip2.pic").</summary>
    public string EquipmentSheetName { get; set; } = "";

    /// <summary>TAC DS:1c27: the title of the menu that asks which door to enter by ("Which door?", two spaces, a line break and a space).</summary>
    public string StrWhichDoor { get; set; } = "";

    /// <summary>TAC DS:1c37: the start of each line of that menu ("Door #", then the number).</summary>
    public string StrDoorNumber { get; set; } = "";

    /// <summary>TAC DS:1c3e: the end of each line of that menu (a line break and a space).</summary>
    public string StrDoorLineEnd { get; set; } = "";
}
