using CovertAction.Core.Shared;

namespace CovertAction.Core.Records;

/// <summary>
/// The head of the campaign's data: which plot is being played, who its MasterMind is, the date, and the texts and history that go with the agent's career. The original calls it a Plot.
/// </summary>
// orig: struct Plot, 654 bytes (src/c/include/bug.h); pl: one record at the head of the campaign tables
public sealed class Plot
{
    /// <summary>
    /// The number of the plot being played, 0..15: which of the sixteen plots the case is a variant of. The menus
    /// part chooses it for a new case and names the briefing's and debriefing's texts by it (FINAL's <c>plot_no</c>).
    /// </summary>
    public short Type { get; set; }

    /// <summary>
    /// The variants of this plot the agent has already played, one bit per variant (bit v for variant v), so that a
    /// new case of the same plot takes another (FINAL's <c>variants_done_mask</c>).
    /// </summary>
    public short Done { get; set; }

    /// <summary>What the player knows about the MasterMind (the knowledge bits of a person).</summary>
    public short Clues { get; set; }

    /// <summary>The MasterMind's organisation.</summary>
    public short BigOrg { get; set; }

    /// <summary>The MasterMind's city.</summary>
    public short BigLoc { get; set; }

    /// <summary>The MasterMind's looks and name (a person's face word).</summary>
    public short BigFace { get; set; }

    /// <summary>Which of the 26 MasterMinds it is.</summary>
    public short BigIndex { get; set; }

    /// <summary>
    /// The variant of the plot that the case plays, 0..7: the column of the plot's table of crime files it was taken
    /// from (FINAL's <c>last_variant</c>). Not the plot's number, which is <see cref="Type"/>.
    /// </summary>
    public short Index { get; set; }

    /// <summary>The number of the crime file the case was read from.</summary>
    public short Crime { get; set; }

    /// <summary>The number of the world file.</summary>
    public short World { get; set; }

    /// <summary>The points of the case so far.</summary>
    public short VPs { get; set; }

    /// <summary>The month, counted from January 1990.</summary>
    public short Ncase { get; set; }

    /// <summary>The game clock at the last hand-off, in minutes.</summary>
    public short Date { get; set; }

    /// <summary>
    /// The name of the city the agent is in (shown in the list of saved games). The game copies the name into this
    /// buffer at every hand-off; what a longer name left behind its end stays there, and is saved with it.
    /// </summary>
    public TextBuffer City { get; } = new(12);

    /// <summary>
    /// The agent's rank at the CIA, as its number: the menus part shows it in the list of saved games and in the
    /// promotion after a case, and starts a new career at 99.
    /// </summary>
    public short Rank { get; set; }

    /// <summary>A word behind the rank that no part is known to use (dummy is MicroProse's own name for it).</summary>
    public short Dummy { get; set; }

    /// <summary>The text that stands for the first placeholder of the game's texts ($VICTIM); a buffer the game rewrites, like <see cref="City"/>.</summary>
    public TextBuffer Object { get; } = new(40);

    /// <summary>The text that stands for the last placeholder ($OBJECT); a buffer the game rewrites.</summary>
    public TextBuffer Object2 { get; } = new(40);

    /// <summary>The agent's code name.</summary>
    public string Handle { get; set; } = "";

    /// <summary>The record of the agent's career: 512 bytes whose layout belongs to the menus part.</summary>
    public byte[] History { get; } = new byte[512];

    /// <summary>One bit per MasterMind: set when that MasterMind was caught.</summary>
    public uint Bigs { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Plot other)
    {
        Type = other.Type;
        Done = other.Done;
        Clues = other.Clues;
        BigOrg = other.BigOrg;
        BigLoc = other.BigLoc;
        BigFace = other.BigFace;
        BigIndex = other.BigIndex;
        Index = other.Index;
        Crime = other.Crime;
        World = other.World;
        VPs = other.VPs;
        Ncase = other.Ncase;
        Date = other.Date;
        City.CopyFrom(other.City);
        Rank = other.Rank;
        Dummy = other.Dummy;
        Object.CopyFrom(other.Object);
        Object2.CopyFrom(other.Object2);
        Handle = other.Handle;
        other.History.CopyTo(History, 0);
        Bigs = other.Bigs;
    }
}
