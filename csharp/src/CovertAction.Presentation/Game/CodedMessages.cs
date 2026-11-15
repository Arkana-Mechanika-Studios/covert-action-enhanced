using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The coded messages the player intercepts: the text of a message as it is handed to the cryptography workstation,
/// put together from paragraphs of TEXT.DTA around the message proper.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="screens">The campaign's screens (their text and drawing routines).</param>
public sealed class CodedMessages(CampaignState state, CampaignScreens screens)
{
    /// <summary>The library's text routines (the reader of the text files).</summary>
    private LibraryText text => screens.Text;

    /// <summary>The paragraphs of a coded message, each used once, in random order.</summary>
    private const int Paragraphs = 7;

    /// <summary>All paragraphs used: one bit each.</summary>
    private const int AllParagraphs = (1 << Paragraphs) - 1;

    /// <summary>The paragraphs: on the sender's and the receiver's city, their organisations, the message proper, filler, the alarm or a helper.</summary>
    private const int SenderCity = 0, ReceiverCity = 1, SenderOrg = 2, ReceiverOrg = 3, MessageProper = 4, Filler = 5, AlarmOrHelper = 6;

    /// <summary>The placeholders of the message texts: $VICTIM, $SNDORG, $RCVORG, $SNDLOC, $RCVLOC, $HLPORG, $OBJECT.</summary>
    private const int Victim = 0, SenderOrgValue = 1, ReceiverOrgValue = 2, SenderCityValue = 3, ReceiverCityValue = 4, HelperValue = 5, PlotObject = 6;

    /// <summary><see cref="Core.Records.Event.Status"/>: the bits of the event's kind; a received message is this kind.</summary>
    private const int EventKind = 0xf00, Received = 0x300;

    /// <summary><see cref="Core.Records.Person.Clues"/>: the player knows the person's organisation, city.</summary>
    private const int OrgKnown = 0x04, CityKnown = 0x08;

    /// <summary>Variants of the paragraphs on cities and organisations, of the filler, of the helper.</summary>
    private const int PlaceVariants = 8, FillerVariants = 12, HelperVariants = 2;

    /// <summary>A message's alarm above this value gives an alarm paragraph instead of a helper.</summary>
    private const int AlarmThreshold = 0xf;

    /// <summary>Where the alarm's level (0..4) goes in the alarm paragraph's tag ("*ALRT0n").</summary>
    private const int AlarmDigit = 6;

    /// <summary>
    /// Puts the text of an intercepted message into <see cref="CampaignState.Txt"/>: the placeholders get the case's
    /// victim and object and the two parties' organisations and cities (sender first; for a received message the
    /// parties change places), then the seven paragraphs are appended in random order: the sender's and receiver's
    /// city and organisation (each only when the player knows it), the message proper between the characters that
    /// mark it, a filler, and either the alarm the message raised (when above 15) or the organisation of a random
    /// member of the plot as a helper (when neither party belongs to it).
    /// </summary>
    /// <remarks>Random draws: one per try of a paragraph until all seven are used; one per text with variants; one for the helper.</remarks>
    /// <param name="msg">The message, an entry of <see cref="CampaignState.Sn"/>.</param>
    // orig: GAME 00f7:7cf4 compose_message_text (src/c/game/game_00f7_7cf4.c) matched; tests: GameOracleTests
    public void ComposeMessageText(int msg)
    {
        var message = state.Sn[msg];
        int happening = message.Data;
        state.Txt.Text = "";
        int sender = message.Who, receiver = message.Who2;
        // C: the original sets its "swapped" flag only for a message with an event; every message of the log's type 0
        // has one (GAME's only writer of such records is queue_message(0, event) in advance_world_clock, 00f7:8755;
        // FINAL's add_msg_record has no caller), so the flag is always set before it is read
        var swapped = false;
        if (happening >= 0 && (state.Ev[happening].Status & EventKind) == Received)
        {
            (sender, receiver) = (receiver, sender);
            swapped = true;
        }
        state.VariableValues[Victim] = state.Pl.Object.Text;
        state.VariableValues[PlotObject] = state.Pl.Object2.Text;
        state.VariableValues[SenderOrgValue] = state.Orgs[state.Ps[sender].Org].Name;
        state.VariableValues[ReceiverOrgValue] = state.Orgs[state.Ps[receiver].Org].Name;
        state.VariableValues[swapped ? ReceiverCityValue : SenderCityValue] = state.Ct[message.Loc].Name;
        state.VariableValues[swapped ? SenderCityValue : ReceiverCityValue] = state.Ct[message.Loc2].Name;
        // every call of a reader in the original has a "text.dta" of its own (DS:3a7e, 3a8f ... 3af1), all the same
        // text as the one the message texts are read with (DS:3afa); the rewrite reads that one
        var used = 0;
        do
        {
            var paragraph = GameLibrary.RandomBelow(state.Random, Paragraphs);
            if ((used & 1 << paragraph) != 0)
            {
                continue;
            }
            used |= 1 << paragraph;
            switch (paragraph)
            {
                case SenderCity:
                    ReadIfKnown(sender, CityKnown, state.SenderCityTag, PlaceVariants);
                    break;
                case ReceiverCity:
                    ReadIfKnown(receiver, CityKnown, state.ReceiverCityTag, PlaceVariants);
                    break;
                case SenderOrg:
                    ReadIfKnown(sender, OrgKnown, state.SenderOrgTag, PlaceVariants);
                    break;
                case ReceiverOrg:
                    ReadIfKnown(receiver, OrgKnown, state.ReceiverOrgTag, PlaceVariants);
                    break;
                case MessageProper:
                    LibraryTexts.LoadMessageText(state.MessageTag, state.MessageTemplate, state.TheCrime, happening >= 0 ? state.Ev[happening].Synch : happening);
                    state.Txt.Text += state.StrCodedStart;
                    text.ReadTextWithVars(state.TextFileName, state.MessageTag.Text);
                    state.Txt.Text += state.StrCodedEnd;
                    break;
                case Filler:
                    text.ReadRandomVariant(state.TextFileName, state.FillerTag, FillerVariants);
                    break;
                case AlarmOrHelper:
                    AppendAlarmOrHelper(message.Alert, sender, receiver);
                    break;
            }
        }
        while (used != AllParagraphs);
    }

    /// <summary>Reads a paragraph with variants when the player knows something of the person.</summary>
    private void ReadIfKnown(int person, int knowledge, TextBuffer tag, int variants)
    {
        if ((state.Ps[person].Clues & knowledge) != 0)
        {
            text.ReadRandomVariant(state.TextFileName, tag, variants);
        }
    }

    /// <summary>The alarm paragraph (level alarm / 20, at most 4), or the paragraph on a helping organisation.</summary>
    private void AppendAlarmOrHelper(int alarm, int sender, int receiver)
    {
        const int alarmPerLevel = 0x14, highestLevel = 4;
        state.MessageTag.Text = state.AlertTagTemplate;
        // C: a signed division, and a char added to the tag's digit
        state.MessageTag.Bytes[AlarmDigit] += (byte)GameLibrary.Clamp(alarm / alarmPerLevel, 0, highestLevel);
        if (alarm > AlarmThreshold)
        {
            text.ReadTextWithVars(state.TextFileName, state.MessageTag.Text);
            return;
        }
        var member = GameLibrary.RandomBelow(state.Random, state.NACTIVITY - (state.Diff << 2));
        int helper = state.Ps[state.Ac[member].Who].Org;
        if (state.Ps[sender].Org != helper && state.Ps[receiver].Org != helper)
        {
            state.VariableValues[HelperValue] = state.Orgs[helper].Name;
            text.ReadRandomVariant(state.TextFileName, state.HelperTag, HelperVariants);
        }
    }

    /// <summary><see cref="Core.Records.Synopsis.Clues"/>: the parts of a message the player knows (date, name, organisation, city), and that he has read it decoded.</summary>
    private const short MessageParts = 0xe, NameKnown = 0x2, MessageRead = unchecked((short)0x8000);

    /// <summary><see cref="Core.Records.Person.Role"/>: the roles a person plays (bit 15 aside).</summary>
    private const int ActiveRoles = 0x7fff;

    /// <summary>
    /// The "Message Decode" screen of an intercepted message: the dossiers of sender and receiver and the message's
    /// text. Decoding teaches the player something: each message of the same minute gains random parts (2 - level
    /// tries; at least the name), the two persons learn from each other's message, and each gets the role the event
    /// shows. A message without event shows the persons with all their roles. Both messages count as read.
    /// </summary>
    /// <remarks>Random draws: per message of the same minute and try, one per part until a new one is found or all are known.</remarks>
    /// <param name="msg">The message.</param>
    // orig: GAME 00f7:7554 show_message_decode (src/c/game/game_00f7_7554.c) matched; tests: GameOracleTests
    public void ShowMessageDecode(int msg)
    {
        const int labelColour = 5, fullWidth = 0x140;
        screens.Screens.DrawFilmBorder();
        screens.Output.DrawTextFlag0(state.StrMessageDecode, 0x78, 0x12, labelColour);
        var message = state.Sn[msg];
        int happening = message.Data;
        int sender = message.Who, receiver = message.Who2;
        int pair;
        if (happening >= 0)
        {
            if ((state.Ev[happening].Status & EventKind) == Received)
            {
                (sender, receiver) = (receiver, sender);
            }
            pair = DecodeParts(msg);
            var other = state.Sn[pair];
            state.Ps[message.Who].Clues |= (short)(other.Clues & MessageParts);
            state.Ps[other.Who].Clues |= (short)(message.Clues & MessageParts);
            // C: shifts of a 16-bit int by the event's cast member and by the low byte of its status (the partner)
            state.Ps[message.Who].Rknown |= C16.Shl(1, state.Ev[happening].Act);
            state.Ps[message.Who2].Rknown |= C16.Shl(1, state.Ev[happening].Status & 0xff);
            message.Clues |= (short)(state.Ps[other.Who].Clues & MessageParts);
            other.Clues |= (short)(state.Ps[message.Who].Clues & MessageParts);
        }
        else
        {
            state.Ps[sender].Rknown |= (short)(state.Ps[sender].Role & ActiveRoles);
            state.Ps[receiver].Rknown |= (short)(state.Ps[receiver].Role & ActiveRoles);
            pair = msg;
        }
        screens.Output.DrawTextFlag0(state.StrDecodeFrom, 0x20, 0x30, labelColour);
        screens.Output.DrawTextFlag0(state.StrDecodeTo, 0xa0, 0x30, labelColour);
        if (state.Ps[sender].Clues != 0)
        {
            screens.DataFiles.DrawPersonDossier(sender, 0x20, 0x38);
        }
        if (state.Ps[receiver].Clues != 0)
        {
            screens.DataFiles.DrawPersonDossier(receiver, 0xa0, 0x38);
        }
        screens.Output.DrawTextFlag0(state.StrDecodeMessage, 0x20, 0x77, labelColour);
        state.Txt.Text = "";
        screens.DescribeEvent(happening);
        screens.Text.DrawWrappedText(0x1e, 0x20, 0x7f, 0);
        state.Animation.SaveWidth = fullWidth;
        screens.Screens.WaitForKey();
        message.Clues |= MessageRead;
        state.Sn[pair].Clues |= MessageRead;
    }

    /// <summary>
    /// Every message of the same minute as <paramref name="msg"/> gains parts at random; returns the last such message
    /// other than <paramref name="msg"/> (its pair).
    /// </summary>
    private int DecodeParts(int msg)
    {
        const int parts = 4;
        // C: the original leaves the pair unset when no other message has the minute; that never happens to a message
        // that is decoded (sent, received or a meeting): advance_world_clock (GAME 00f7:8755) records such an event
        // only together with its partner's, in the same step (the EV_NO_WAIT event of CRIME2 does so alone only at
        // the case's first step, when no partner can be out of play yet)
        var pair = -1;
        for (var e = 0; e < state.NextSyn; e++)
        {
            var other = state.Sn[e];
            if (state.Sn[msg].Time != other.Time)
            {
                continue;
            }
            for (var attempt = 0; attempt < 2 - state.Diff; attempt++)
            {
                var before = other.Clues;
                do
                {
                    other.Clues |= C16.Shl(1, GameLibrary.RandomBelow(state.Random, parts));
                }
                while (other.Clues == before && (before & MessageParts) != MessageParts);
            }
            if ((other.Clues & MessageParts) == 0)
            {
                other.Clues |= NameKnown;
            }
            if (e != msg)
            {
                pair = e;
            }
        }
        return pair;
    }
}
