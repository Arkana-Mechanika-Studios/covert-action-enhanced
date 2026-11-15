using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The texts of the CIA building: its floors and sections, and the chief's advice. Texts of the program, read from the player's GAME.EXE.
/// </summary>
// orig: GAME DS:33ee..3667 of cia_building_menu (game_00f7_6458.c); DS:3d5e..3f84 of chief_advice (game_00f7_9e58.c)
public sealed class HeadquartersWords
{
    /// <summary>GAME DS:33ee: the CIA building's floors (data, intelligence, crypto, leave).</summary>
    public string FloorMenu { get; set; } = "";

    /// <summary>GAME DS:3466: the animation of the intelligence section.</summary>
    public string IntelligenceScene { get; set; } = "";

    /// <summary>GAME DS:346f: the intelligence section's choices.</summary>
    public string IntelligenceMenu { get; set; } = "";

    /// <summary>GAME DS:34df: the box after a local scan that found nothing.</summary>
    public string NoNewInformationLocal { get; set; } = "";

    /// <summary>GAME DS:34f3: the box after an international scan that found nothing.</summary>
    public string NoNewInformationWorld { get; set; } = "";

    /// <summary>GAME DS:3507: the double agent confesses and is replaced.</summary>
    public string AgentConfesses { get; set; } = "";

    /// <summary>GAME DS:3533: what the player then does.</summary>
    public string SearchHisFiles { get; set; } = "";

    /// <summary>GAME DS:3567: the accused agent was innocent: the office is closed to the player.</summary>
    public string AgentInnocent { get; set; } = "";

    /// <summary>GAME DS:35c4: the animation of the data section.</summary>
    public string DataScene { get; set; } = "";

    /// <summary>GAME DS:35d1: the data section's first choices.</summary>
    public string DataMenu { get; set; } = "";

    /// <summary>GAME DS:3625: the data section's last choices.</summary>
    public string DataMenuEnd { get; set; } = "";

    /// <summary>GAME DS:365c: the animation of the crypto branch.</summary>
    public string CryptoScene { get; set; } = "";

    /// <summary>GAME DS:3667: the crypto branch's choices.</summary>
    public string CryptoMenu { get; set; } = "";

    /// <summary>GAME DS:3d5e: the chief's office ("officem.pan"), shown when the agent is Maxine (the options' flag 2).</summary>
    public string OfficeSceneOfFemaleAgent { get; set; } = "";

    /// <summary>GAME DS:3d6a: the chief's office ("officef.pan"), shown when the agent is Max.</summary>
    public string OfficeSceneOfMaleAgent { get; set; } = "";

    /// <summary>GAME DS:3d76: the chief begins ("Max, I've been going over the info we've got so far ...").</summary>
    public string GoingOver { get; set; } = "";

    /// <summary>GAME DS:3daf: the heading of the organisations the chief names.</summary>
    public string LookIntoOrganizations { get; set; } = "";

    /// <summary>GAME DS:3dd3: before an organisation's name.</summary>
    public string OrganizationIndent { get; set; } = "";

    /// <summary>GAME DS:3dd5: after an organisation's name.</summary>
    public string OrganizationEnd { get; set; } = "";

    /// <summary>GAME DS:3dd7: the heading of the cities the chief names (each of its two lines begins with the byte 0x8f).</summary>
    public string ActiveCities { get; set; } = "";

    /// <summary>GAME DS:3e08: after a city's name, and the indent of the next.</summary>
    public string CityEnd { get; set; } = "";

    /// <summary>GAME DS:3e0b: the chief has nothing to name.</summary>
    public string NotMuchToGo { get; set; } = "";

    /// <summary>GAME DS:3e32: the chief names the hinted person's organisation: "The ".</summary>
    public string TheOrganization { get; set; } = "";

    /// <summary>GAME DS:3e37: after the organisation's name.</summary>
    public string HasA { get; set; } = "";

    /// <summary>GAME DS:3e3e: after the kind of building.</summary>
    public string RightHereIn { get; set; } = "";

    /// <summary>GAME DS:3e4e: after the city's name.</summary>
    public string SuggestGoThere { get; set; } = "";

    /// <summary>GAME DS:3e8a: the chief points at a message still to decode.</summary>
    public string VitalInfo { get; set; } = "";

    /// <summary>GAME DS:3eb8: between the name and the message.</summary>
    public string NameSpace { get; set; } = "";

    /// <summary>GAME DS:3eba: the end of that advice.</summary>
    public string CheckWithCrypto { get; set; } = "";

    /// <summary>GAME DS:3eda: the chief has a person to arrest.</summary>
    public string EnoughEvidence { get; set; } = "";

    /// <summary>GAME DS:3f10: the chief has a person to check out.</summary>
    public string CheckOutPerson { get; set; } = "";

    /// <summary>GAME DS:3f3b: the pronoun of a man.</summary>
    public string He { get; set; } = "";

    /// <summary>GAME DS:3f3e: the pronoun of a woman.</summary>
    public string She { get; set; } = "";

    /// <summary>GAME DS:3f42: after the pronoun ("'s right here in "), before the city.</summary>
    public string RightHereInCity { get; set; } = "";

    /// <summary>GAME DS:3f54: after the city.</summary>
    public string FullStop { get; set; } = "";

    /// <summary>GAME DS:3f57: the tag of the advice on a person whose organisation has a known building and who is known to be guilty ("*advicea").</summary>
    public TextBuffer AdviceKnownKey { get; } = new(9);

    /// <summary>GAME DS:3f60: the tag of the advice on a person whose organisation has a known building ("*advice1").</summary>
    public TextBuffer AdviceBuildingKey { get; } = new(9);

    /// <summary>GAME DS:3f69: the tag of the advice on a person whose organisation has no known building ("*advice2").</summary>
    public TextBuffer AdviceNoBuildingKey { get; } = new(9);

    /// <summary>GAME DS:3f72: the tag of the advice on a person with a photograph or a name ("*advice3").</summary>
    public TextBuffer AdviceSeenKey { get; } = new(9);

    /// <summary>GAME DS:3f7b: the tag of the advice on a person the player knows little of ("*advice4").</summary>
    public TextBuffer AdviceUnseenKey { get; } = new(9);

    /// <summary>GAME DS:3f84: the tag of the advice on a person in another city ("*advice5").</summary>
    public TextBuffer AdviceElsewhereKey { get; } = new(9);
}
