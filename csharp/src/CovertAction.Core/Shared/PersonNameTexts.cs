namespace CovertAction.Core.Shared;

/// <summary>
/// The texts a part names persons with: the tables of first names and surnames, and the code name of a person the
/// player has not identified ("Agent A"). Each part carries its own copy of these tables among its initialised data;
/// the part's state fills this object from its program (they are never spelled in the C#).
/// </summary>
public sealed class PersonNameTexts
{
    /// <summary>Names per table: 16 for each of the four ethnic groups (two rows of eight per group).</summary>
    public const int NamesPerTable = 64;

    /// <summary>The first names: [0] women's, [1] men's (bit 0 of the person's face word), each indexed by group and name.</summary>
    public string[][] FirstNames { get; } = [new string[NamesPerTable], new string[NamesPerTable]];

    /// <summary>The surnames, indexed by group and name.</summary>
    public string[] Surnames { get; } = new string[NamesPerTable];

    /// <summary>What stands between first name and surname: a blank.</summary>
    public string Space { get; set; } = "";

    /// <summary>The code name of an unidentified person, "Agent A": its last letter is replaced by the person's own.</summary>
    public string AgentPlaceholder { get; set; } = "";
}
