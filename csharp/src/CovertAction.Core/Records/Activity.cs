namespace CovertAction.Core.Records;

/// <summary>
/// A member of the cast of a case: a role in the plot ("Courier", "MasterMind") that one person plays. The original calls it an Activity.
/// </summary>
// orig: struct Activity, 48 bytes (src/c/include/campaign.h); ac[]: one record per member of the cast, from CRIMEn.DTA
public sealed class Activity
{
    /// <summary>The person who plays the role (an index of the persons); -1 in the crime file.</summary>
    public short Who { get; set; }

    /// <summary>How easily clues lead to the member: the higher, the sooner a search finds another clue.</summary>
    public short Cover { get; set; }

    /// <summary>The role's name ("Red Herring" for a decoy).</summary>
    public string Name { get; set; } = "";

    /// <summary>A random number the set-up of the case gives the member, unlike the other members' in bits 1-6; its low byte chooses the member's clue.</summary>
    public short Key { get; set; }

    /// <summary>The member's flags: the placement bits and the type of participant from the crime file (0x0100 MasterMind, 0x0200 female, ...), and at run time 0x4000 "listed on the Clues screen" and 0x8000 "revealed".</summary>
    public short Status { get; set; }

    /// <summary>One bit per kind of clue already found about the member (1, 2, 4, 8, 0x10; 0x1f when all are).</summary>
    public short Clued { get; set; }

    /// <summary>What kind of thing the member's clue is: 0 vehicle, 1 weapon, 2 address, 3 ticket, 4 telegram, 5 and 6 money, 7 identity papers.</summary>
    public short Ctype { get; set; }

    /// <summary>The member's rank, 0 (Recruit) to 7 (MasterMind).</summary>
    public short Rank { get; set; }

    /// <summary>A word of the crime file that nothing reads.</summary>
    public short Dummy { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Activity other)
    {
        Who = other.Who;
        Cover = other.Cover;
        Name = other.Name;
        Key = other.Key;
        Status = other.Status;
        Clued = other.Clued;
        Ctype = other.Ctype;
        Rank = other.Rank;
        Dummy = other.Dummy;
    }
}
