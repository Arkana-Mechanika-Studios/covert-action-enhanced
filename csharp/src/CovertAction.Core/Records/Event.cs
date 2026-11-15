namespace CovertAction.Core.Records;

/// <summary>
/// One step of a plot: something a member of the cast does (sends a message, meets someone, receives an object, commits the crime).
/// </summary>
// orig: struct Event, 44 bytes (src/c/include/campaign.h); ev[]: one record per event of the plot, from CRIMEn.DTA
public sealed class Event
{
    /// <summary>The member of the cast who does it; -1 in a record that holds no event: a crime file can end its list of events with such records, which its count includes.</summary>
    public sbyte Act { get; set; }

    /// <summary>The high byte of the file's word; always 0, never read.</summary>
    public sbyte ActHi { get; set; }

    /// <summary>When it happened: the plot step; 999 while it has not happened yet, -1 when it was cancelled, 0 when it was done silently.</summary>
    public short Chron { get; set; }

    /// <summary>The number of the event's message text, and the key that pairs it with its partner event: the first event of the target with the same number.</summary>
    public short Synch { get; set; }

    /// <summary>What happens, in words.</summary>
    public string Name { get; set; } = "";

    /// <summary>The low byte is the member of the cast it happens to; the high byte the kind of event (0x01 receiving side, 0x02 message, 0x04 package, 0x08 meeting, 0x10 partner out of play, 0x20 shown as a bulletin, 0x80 revealed).</summary>
    public short Status { get; set; }

    /// <summary>The objects of the plot that change hands: bits 0-3 the ones the actor receives, bits 8-11 the ones destroyed.</summary>
    public short Macguff { get; set; }

    /// <summary>What the event is worth. An event worth 5 or more that happened means the crime was carried out.</summary>
    public short VP { get; set; }

    /// <summary>Takes over every field of another record: how a part of the game copies the campaign's tables into its own, record by record.</summary>
    /// <param name="other">The record to copy.</param>
    public void CopyFrom(Event other)
    {
        Act = other.Act;
        ActHi = other.ActHi;
        Chron = other.Chron;
        Synch = other.Synch;
        Name = other.Name;
        Status = other.Status;
        Macguff = other.Macguff;
        VP = other.VP;
    }
}
