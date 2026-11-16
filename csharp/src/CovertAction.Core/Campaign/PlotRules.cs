using CovertAction.Core.Records;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The plot engine's decisions: which event of the criminal plot can happen now, what the message log records, who
/// is found or made up when the player learns of a person. The original works these out inside functions that also
/// show bulletins and boxes (<c>event_ready</c>, <c>queue_message</c>, <c>find_or_create_person</c>,
/// <c>seize_plot_object</c>); the rules are split off here, and those functions, when they are ported, call them and
/// show what the rules report.
/// </summary>
/// <remarks>
/// The split keeps the original's order of changes and of random draws: what a function shows comes in the original
/// after the change it reports and before the next change, and the bulletins and boxes change none of the plot's
/// state, so showing them after the rule has run gives the same campaign.
/// </remarks>
public static class PlotRules
{
    /// <summary>Messages the log holds; the original warns that the log is full when this many were recorded.</summary>
    public const int MessageLogSize = CampaignState.SynopsisCount;

    /// <summary>Kinds of message (<see cref="Synopsis.Type"/>): a message of an event of the plot.</summary>
    public const int MessageOfEvent = 0;

    /// <summary>A cast member came out of hiding.</summary>
    public const int MessageOutOfHiding = 9;

    /// <summary>The player seized a plot object.</summary>
    public const int MessagePlotObjectSeized = 7;

    /// <summary>The low six bits of a face word: the face that is shown.</summary>
    private const int FaceBits = 0x3f;

    /// <summary><see cref="Person.Alert"/> of a person in hiding.</summary>
    public const short InHiding = -2;

    /// <summary><see cref="Person.Alert"/> of a person turned into a double agent.</summary>
    public const short Turned = -3;

    /// <summary><see cref="Event.Status"/>: the bits naming the partner (low byte) and the kind; 0 for an event without a partner.</summary>
    private const int PartnerBits = 0xfff;

    /// <summary><see cref="Event.Status"/>: the event need not wait for a partner who is out of play.</summary>
    private const int NoWaitForPartner = 0x1000;

    /// <summary><see cref="MacGuffins.Who"/> of a plot object nobody holds.</summary>
    private const sbyte NobodyHolds = -1;

    /// <summary>What <see cref="EventReady"/> found.</summary>
    /// <param name="Result">-1: the event must wait; 1: an event without partner can happen; else the partner's event that this one answers (an index of <see cref="CampaignState.Ev"/>).</param>
    /// <param name="OutOfHiding">The persons who came out of hiding, in order: for each the original shows a bulletin and records a message of kind <see cref="MessageOutOfHiding"/>.</param>
    public readonly record struct EventCheck(int Result, IReadOnlyList<int> OutOfHiding);

    /// <summary>
    /// Records a message in the log at the current minute. A message of an event names the acting person and city
    /// and, for an event with a partner, the partner and the partner's city; other kinds keep only their argument.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="kind">The kind of message (0 an event; 1 a departure, 5 an arrival, 7 a seized object, 8 gone into hiding, 9 out of hiding, 11 a sighting).</param>
    /// <param name="arg">For an event its index, else what the kind records.</param>
    /// <returns>True when the log has become full: the original then shows the "log full" box.</returns>
    /// <remarks>The partner and the partner's city of a message without partner keep what the record held before, as in the original.</remarks>
    public static bool QueueMessage(CampaignState state, int kind, int arg)
    {
        var message = state.Sn[state.NextSyn];
        message.Time = state.Gtime;
        message.Type = (short)kind;
        message.Data = (short)arg;
        message.Clues = 0;
        message.Who = -1;
        message.Alert = 0;
        if (kind == MessageOfEvent)
        {
            var happening = state.Ev[arg];
            var actor = state.Ac[happening.Act].Who;
            message.Who = (sbyte)actor;
            message.Loc = (sbyte)state.Ps[actor].Loc;
            message.Alert = state.Ps[actor].Alert;
            if ((happening.Status & PartnerBits) != 0)
            {
                var partner = state.Ac[PartnerMemberOf(state, arg)].Who;
                message.Who2 = (sbyte)partner;
                message.Loc2 = (sbyte)state.Ps[partner].Loc;
            }
        }
        state.NextSyn++;
        return state.NextSyn >= MessageLogSize;
    }

    /// <summary>
    /// Can event <paramref name="q"/> of the plot happen now? Not when it already happened, when its actor is a
    /// double agent and it scores victory points for the criminals, when an earlier event of the same actor is still pending, or when
    /// the actor or the partner is under arrest (then the arrested person may become the subject of the next event:
    /// <see cref="CastRules.TrySelectPerson"/>). An event without partner can happen once the log has more than five
    /// messages, unless its actor is out of play. An event with a partner waits until the partner has no pending event
    /// before the one it answers (unless the partner is out of play and the event need not wait); actor and partner in
    /// hiding come out of it.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="q">The event, below <see cref="CampaignState.NEVENT"/> - 1.</param>
    /// <returns>The answer and who came out of hiding.</returns>
    public static EventCheck EventReady(CampaignState state, int q)
    {
        const int wait = -1, readyAlone = 1;
        var emerged = new List<int>();
        var happening = state.Ev[q];
        if (happening.Chron != CastRules.ChronPending)
        {
            return new(wait, emerged);
        }
        var actor = state.Ac[happening.Act].Who;
        if (state.Ps[actor].Alert == Turned && happening.VP != 0)
        {
            return new(wait, emerged);
        }
        for (var earlier = 0; earlier < q; earlier++)
        {
            if (state.Ev[earlier].Chron == CastRules.ChronPending && state.Ev[earlier].Act == happening.Act)
            {
                return new(wait, emerged);
            }
        }
        if ((happening.Status & PartnerBits) == 0)
        {
            if (state.NextSyn <= 5)
            {
                return new(wait, emerged);
            }
            var alert = state.Ps[actor].Alert;
            if (alert < 0 && alert != Turned)
            {
                CastRules.TrySelectPerson(state, actor);
                return new(wait, emerged);
            }
            return new(readyAlone, emerged);
        }
        var partnerMember = PartnerMemberOf(state, q);
        var partner = state.Ac[partnerMember].Who;
        var partnerAlert = state.Ps[partner].Alert;
        var partnerOutOfPlay = partnerAlert < 0 && partnerAlert != Turned && (happening.Status & NoWaitForPartner) != 0;
        var answered = wait;
        for (var other = 0; other < state.NEVENT; other++)
        {
            var candidate = state.Ev[other];
            if (candidate.Act != partnerMember)
            {
                continue;
            }
            if (happening.Synch == candidate.Synch)
            {
                answered = other;
                break;
            }
            if (candidate.Chron == CastRules.ChronPending && !partnerOutOfPlay)
            {
                return new(wait, emerged);
            }
        }
        if (state.Ps[actor].Alert == CastRules.Arrested)
        {
            CastRules.TrySelectPerson(state, actor);
            return new(wait, emerged);
        }
        if (state.Ps[partner].Alert == CastRules.Arrested)
        {
            CastRules.TrySelectPerson(state, partner);
            return new(wait, emerged);
        }
        if (state.Ps[actor].Alert == InHiding && state.Ps[partner].Alert != CastRules.Arrested)
        {
            state.Ps[actor].Alert = 0;
            emerged.Add(actor);
        }
        if (state.Ps[partner].Alert == InHiding && state.Ps[actor].Alert != CastRules.Arrested)
        {
            state.Ps[partner].Alert = 0;
            emerged.Add(partner);
        }
        return new(answered, emerged);
    }

    /// <summary>
    /// The cast member an event with a partner is paired with: the low byte of its status. Two receiving events of the
    /// data (CRIME2's "Financed operation", CRIME8's "Driver") name their own actor there; with
    /// <see cref="OriginalBehaviour.FixReceiveEventNamingItsOwnActor"/> such an event is paired with the sender of the
    /// event of the same message id that sends to its actor.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="eventIndex">The event.</param>
    /// <returns>The partner's cast member.</returns>
    // BUG: CA-13 a receive event naming its own actor is picked before its sender and pairs with itself; OriginalBehaviour.FixReceiveEventNamingItsOwnActor
    public static int PartnerMemberOf(CampaignState state, int eventIndex)
    {
        var happening = state.Ev[eventIndex];
        var named = happening.Status & 0xff;
        // BUG: CA-60 CRIME10's event 20 names member 4 (the Extractor) where its pair, event 19, is member 5's (the Travel Agent)
        // DIVERGENCE: D-650 a player's option: CA-60, the partner of that event is read as member 5
        if (state.Behaviour.FixCrime10MeetingPartner && IsCrime10Event20(state, eventIndex))
        {
            return state.Ev[19].Act;
        }
        if (!state.Behaviour.FixReceiveEventNamingItsOwnActor || named != happening.Act)
        {
            return named;
        }
        for (var other = 0; other < state.NEVENT; other++)
        {
            var sender = state.Ev[other];
            if (sender.Synch == happening.Synch && sender.Act != happening.Act && (sender.Status & PartnerBits) != 0 && (sender.Status & 0xff) == happening.Act)
            {
                return sender.Act;
            }
        }
        return named;
    }

    /// <summary>
    /// Whether event <paramref name="eventIndex"/> is the one of CRIME10.DTA whose partner byte is wrong (CA-60): the
    /// Mastermind's "Met with", status 0x0804, message id 11, which the data pairs with event 19 of another member, while
    /// the member it names has no event of that id. Checked against the data, so another file is left alone.
    /// </summary>
    private static bool IsCrime10Event20(CampaignState state, int eventIndex)
    {
        const int crime = 10, meeting = 20, partnerEvent = 19, wrongPartner = 4;
        if (state.TheCrime != crime || eventIndex != meeting || state.NEVENT <= meeting)
        {
            return false;
        }
        var happening = state.Ev[meeting];
        var other = state.Ev[partnerEvent];
        return (happening.Status & 0xff) == wrongPartner && other.Synch == happening.Synch && other.Act != happening.Act
            && (other.Status & 0xff) == happening.Act && !Enumerable.Range(0, state.NEVENT).Any(e => state.Ev[e].Act == wrongPartner && state.Ev[e].Synch == happening.Synch);
    }

    /// <summary>
    /// Finds the person of an organisation in a city and adds knowledge bits, or makes up a new person there (when
    /// there are bits to add): a random free slot other than the mastermind's (the mastermind's own organisation and
    /// city take slot 0 and the mastermind's known bits), and a random face. The new person's alert and investigation
    /// score are left as the slot's previous occupant had them.
    /// </summary>
    /// <remarks>
    /// The face is drawn again while another person's face word is exactly 63 and the new face's low byte is odd: the
    /// test the original makes, which looks like a garbled "the same face bits" test (see the report of the port).
    /// Random draws: one per slot tried, then one per face tried.
    /// </remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="flags">Knowledge bits (<see cref="Person.Clues"/>) the player learns.</param>
    /// <param name="oldClues">What the player knew of the person before (for the announcement of what is new); 0 when -1 is returned.</param>
    /// <returns>The person, or -1 when nothing is known of them afterwards.</returns>
    public static int FindOrCreatePerson(CampaignState state, int org, int city, int flags, out int oldClues)
    {
        oldClues = 0;
        for (var index = 0; index < state.Nperson; index++)
        {
            var known = state.Ps[index];
            if (known.Org == org && known.Loc == city)
            {
                oldClues = known.Clues;
                known.Clues |= (short)flags;
                if (known.Clues == 0)
                {
                    oldClues = 0;
                    return -1;
                }
                return index;
            }
        }
        int slot;
        do
        {
            slot = GameLibrary.RandomBelow(state.Random, CampaignState.PersonCount - 1) + 1;
        }
        while (state.Ps[slot].Face != 0);
        if (org == state.Pl.BigOrg && city == state.Pl.BigLoc)
        {
            slot = CastRules.MastermindSlot;
            flags = (short)(flags | (ushort)state.Pl.Clues);
        }
        if (flags == 0)
        {
            return -1;
        }
        var person = state.Ps[slot];
        oldClues = person.Clues;
        person.Org = (short)org;
        person.Loc = (short)city;
        person.Clues = (short)flags;
        person.Role = 0;
        person.Rknown = 0;
        // BUG: CA-36 the record's previous occupant's alert and investigation score stay (the menus part's own
        // find_or_create_person clears them); OriginalBehaviour.FixNewPersonFaceAndState
        var distinctFaces = state.Behaviour.FixNewPersonFaceAndState;
        if (distinctFaces)
        {
            person.Alert = 0;
            person.Intel = 0;
        }
        bool unique;
        do
        {
            unique = true;
            person.Face = (short)GameLibrary.RandomBelow(state.Random, 0x7fff);
            for (var other = 0; other < state.Nperson; other++)
            {
                // BUG: CA-36 the test is "(other.face == 63) & new.face & 63" where "the same low six bits" is meant
                // C: (face == 0x3f) is 0 or 1, and-ed with the low byte of the new face and with 0x3f
                var sameFace = distinctFaces
                    ? state.Ps[other].Face != 0 && (state.Ps[other].Face & FaceBits) == (person.Face & FaceBits)
                    : state.Ps[other].Face == 0x3f && (person.Face & 1) != 0;
                if (sameFace && other != slot)
                {
                    unique = false;
                }
            }
        }
        while (!unique);
        return slot;
    }

    /// <summary>
    /// The player seized a plot object. Going through the log, the actor of the last recorded event that moved the
    /// object is its carrier; every logged event of the carrier from there on sets all events with the same message
    /// id back to pending. Then a message of kind <see cref="MessagePlotObjectSeized"/> is recorded and nobody holds
    /// the object any more.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="plotObject">The object, 0..3.</param>
    /// <returns>True when the message log has become full (the original then shows its box).</returns>
    public static bool SeizePlotObject(CampaignState state, int plotObject)
    {
        var objectBit = 1 << plotObject;
        var carrier = -1;
        for (var index = 0; index < state.NextSyn; index++)
        {
            var message = state.Sn[index];
            if (message.Type != MessageOfEvent)
            {
                continue;
            }
            var happening = state.Ev[message.Data];
            if ((happening.Macguff & objectBit) != 0)
            {
                carrier = happening.Act;
            }
            if (carrier != -1 && happening.Act == carrier)
            {
                for (var other = 0; other < state.NEVENT; other++)
                {
                    if (happening.Synch == state.Ev[other].Synch)
                    {
                        state.Ev[other].Chron = CastRules.ChronPending;
                    }
                }
            }
        }
        var full = QueueMessage(state, MessagePlotObjectSeized, plotObject);
        state.Mc[plotObject].Who = NobodyHolds;
        return full;
    }

    // ---- the plot step (the rules of advance_world_clock, GAME 00f7:8755) ----

    /// <summary>The minute after which the case ends whatever happened (the clock is unsigned).</summary>
    public const int LastMinute = 0x9c40;

    /// <summary><see cref="CampaignState.ExcludedPerson"/> before a plot step has looked at the events.</summary>
    public const short NoOneExcludedYet = -2;

    /// <summary>The tries the plot step makes to find a cast member at large.</summary>
    private const int Tries = 999;

    /// <summary><see cref="Activity.Status"/>: the cast member may be seen leaving a city.</summary>
    private const int CastTravels = 0x08;

    /// <summary><see cref="Person.Clues"/>: what the player knows of a person that keeps a cast member from hiding a second time in a step (photo, name, organisation, city).</summary>
    private const int KnownBasics = 0x0f;

    /// <summary><see cref="Person.Clues"/>: the player has spotted the person in their city.</summary>
    private const short KnownCity = 0x08;

    /// <summary>
    /// The start of a plot step, after the clock has moved on and been drawn (which sets <see cref="CampaignState.Hour"/>
    /// and <see cref="CampaignState.Day"/>): a step is taken when a new day began or noon was passed, or when forced.
    /// It counts the step, clears every city's sightings, lets every person's trail and the general alarm fade
    /// (each loses its share 1 / (difficulty + 3)), and no person is excluded yet.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="hourBefore">The hour before the clock moved on.</param>
    /// <param name="dayBefore">The day before the clock moved on.</param>
    /// <param name="force">Whether the step is taken whatever the time.</param>
    /// <returns>False when no step is due (nothing changed).</returns>
    public static bool BeginPlotStep(CampaignState state, int hourBefore, int dayBefore, bool force)
    {
        const int noon = 12;
        if ((hourBefore >= noon || state.Hour < noon) && dayBefore == state.Day && !force)
        {
            return false;
        }
        state.ChronTime++;
        foreach (var city in state.Ct)
        {
            city.Clued = 0;
        }
        for (var index = 0; index < state.Nperson; index++)
        {
            var person = state.Ps[index];
            if (person.Face != 0 && person.Alert >= 0)
            {
                person.Alert -= (short)(person.Alert / (state.Diff + 3));
            }
        }
        state.Alerted -= (short)(state.Alerted / (state.Diff + 3));
        state.ExcludedPerson = NoOneExcludedYet;
        return true;
    }

    /// <summary>
    /// Whether the plot step stops after counting the events that can happen: a roll of 0..9 at or above the
    /// difficulty plus 4 (often on the easy levels), once the log has more than five messages, when some event could
    /// happen and the step is not forced.
    /// </summary>
    /// <remarks>Random draws: the roll, always.</remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="readyEvents">How many events could happen.</param>
    /// <param name="force">Whether the step is forced.</param>
    public static bool SkipPlotStep(CampaignState state, int readyEvents, bool force) =>
        GameLibrary.RandomBelow(state.Random, 10) >= state.Diff + 4 && state.NextSyn > 5 && readyEvents != 0 && !force;

    /// <summary>
    /// One try of the plot step to send a cast member into hiding: a random real member (not a decoy) at large and not
    /// excluded is picked (999 tries at most). Once the log has more than eight messages, a member who is not where the
    /// player is (unless the player waits in the hotel) hides, unless an event of theirs is pending while events can
    /// happen or a person is excluded, or someone already hid in this step and the player knows this member.
    /// </summary>
    /// <remarks>
    /// The step calls this until it returns -1; for each member who hid, the original shows a bulletin when the player
    /// knows the person and records a message of kind 8. Random draws: one per member tried.
    /// </remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="readyEvents">Whether events can happen (the count of the step, 0 or more).</param>
    /// <param name="someoneHid">Whether a member already hid in this step.</param>
    /// <param name="noOneAtLarge">Set when 999 tries found no member at large: the case may be over.</param>
    /// <returns>The person who went into hiding, or -1.</returns>
    public static int TryHide(CampaignState state, int readyEvents, bool someoneHid, out bool noOneAtLarge)
    {
        int person;
        var tries = 0;
        do
        {
            person = state.Ac[GameLibrary.RandomBelow(state.Random, state.NACTIVITY - state.Diff * 4)].Who;
        }
        while ((state.Ps[person].Alert < 0 || person == state.ExcludedPerson) && ++tries < Tries);
        noOneAtLarge = tries == Tries;
        var candidate = state.Ps[person];
        var whereThePlayerIs = candidate.Loc == state.InCity && candidate.Org == state.AtLoc && state.Sleepy == 0;
        if (noOneAtLarge || state.NextSyn <= 8 || whereThePlayerIs)
        {
            return -1;
        }
        var nothingPending = true;
        for (var index = 0; index < state.NEVENT; index++)
        {
            // BUG: CA-33 the marker event's member -1 makes the original read ac[-1]
            if (CastRules.PersonOfCastMember(state, state.Ev[index].Act) == person && state.Ev[index].Chron == CastRules.ChronPending)
            {
                nothingPending = false;
                break;
            }
        }
        if ((!nothingPending && (readyEvents != 0 || state.ExcludedPerson >= 0)) || (someoneHid && (candidate.Clues & KnownBasics) != 0))
        {
            return -1;
        }
        candidate.Alert = InHiding;
        return person;
    }

    /// <summary>Whether the case is over: no cast member is at large and no event can happen, or the clock has passed its last minute.</summary>
    /// <param name="state">The campaign.</param>
    /// <param name="noOneAtLarge">What the last <see cref="TryHide"/> found.</param>
    /// <param name="readyEvents">How many events could happen.</param>
    public static bool CaseIsOver(CampaignState state, bool noOneAtLarge, int readyEvents) =>
        // C: the clock is unsigned
        (noOneAtLarge && readyEvents == 0) || (ushort)state.Gtime > LastMinute;

    /// <summary>
    /// Sets when the chosen event <paramref name="chosen"/> happens. An event without partner happens in this step. An
    /// event that need not wait for a partner who is out of play is done at once (step 0). One that answers an event
    /// that already happened shares its step. Otherwise both happen in this step, every other event with the same
    /// message id is cancelled, and a pair whose two sides are the same person is cancelled. A cast member's first
    /// step is noted with their first event.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="chosen">The event, as picked at random among those that can happen.</param>
    /// <param name="answered">What <see cref="EventReady"/> answered for it: the partner's event (for an event with a partner).</param>
    public static void ScheduleEvent(CampaignState state, int chosen, int answered)
    {
        const short cancelled = -1, atOnce = 0;
        var happening = state.Ev[chosen];
        if ((happening.Status & PartnerBits) == 0)
        {
            Happen(state, happening);
            return;
        }
        var partnerEvent = state.Ev[answered];
        if ((happening.Status & NoWaitForPartner) != 0 && state.Ps[state.Ac[partnerEvent.Act].Who].Alert < 0)
        {
            happening.Chron = atOnce;
            return;
        }
        if (partnerEvent.Chron != CastRules.ChronPending)
        {
            happening.Chron = partnerEvent.Chron;
            return;
        }
        Happen(state, happening);
        Happen(state, partnerEvent);
        for (var other = 0; other < state.NEVENT; other++)
        {
            // BUG: CA-12 and CA-14: every other event with the pair's message id is cancelled. CA-14 (two senders of
            // one id to one receiver, of which one message is lost) is left as it is: the data reads as the
            // designers' alternatives, either sender but not both. CA-12 (an announced event of one member that merely
            // shares the number) is meant to stay: OriginalBehaviour.FixAnnouncedEventCancelledBySharedMessageId
            if (happening.Synch == state.Ev[other].Synch && other != chosen && other != answered
                && !(state.Behaviour.FixAnnouncedEventCancelledBySharedMessageId && (state.Ev[other].Status & PartnerBits) == 0))
            {
                state.Ev[other].Chron = cancelled;
            }
        }
        // BUG: CA-13 a receive event paired with its own actor's send cancels both
        if (state.Ac[happening.Act].Who == state.Ac[partnerEvent.Act].Who)
        {
            happening.Chron = cancelled;
            partnerEvent.Chron = cancelled;
        }
    }

    /// <summary>An event happens in this step; the cast member's first step is noted.</summary>
    private static void Happen(CampaignState state, Event happening)
    {
        const short neverActed = -1;
        happening.Chron = state.ChronTime;
        if (state.Ac2[happening.Act].Chron == neverActed)
        {
            state.Ac2[happening.Act].Chron = state.ChronTime;
        }
    }

    /// <summary>
    /// The last part of a plot step: the first cast member who may travel, whom the player has spotted in a city other
    /// than the player's, and whose trail beats a roll (0 .. 128 / (difficulty + 1)), tries four random cities where
    /// the member's organisation is present; in the first whose person of that organisation is new to the player, the
    /// member is seen leaving: the member moves there, the player loses the member's city and half the trail.
    /// </summary>
    /// <remarks>
    /// The person of the organisation in the new city is looked up with no knowledge to add, which never finds a new
    /// person (the lookup returns -1), and then "person -1" is examined: the record in front of the persons table,
    /// which is the end of the text <see cref="CampaignState.TextBeforePersons"/>, whose role word is not 0. A known
    /// person of the organisation has knowledge, so no city ever qualifies with the program's own text: cast members
    /// are never seen leaving (proposed in the report of the port). Random draws: for each member who may travel and
    /// is spotted, the roll; for one who passes it, a city per try.
    /// </remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="fromCity">The city the member left (the original shows a bulletin and records a message of kind 1 with the member and this city before the move); -1 when no one left.</param>
    /// <returns>The person who left, or -1.</returns>
    public static int TryDepart(CampaignState state, out int fromCity)
    {
        fromCity = -1;
        const int citiesTried = 4;
        for (var member = 0; member < state.NACTIVITY; member++)
        {
            if ((state.Ac[member].Status & CastTravels) == 0)
            {
                continue;
            }
            var person = state.Ac[member].Who;
            var traveller = state.Ps[person];
            if ((traveller.Clues & KnownCity) == 0 || traveller.Alert <= GameLibrary.RandomBelow(state.Random, 128 / (state.Diff + 1)) || traveller.Loc == state.InCity)
            {
                continue;
            }
            for (var attempt = 0; attempt < citiesTried; attempt++)
            {
                var city = GameLibrary.RandomBelow(state.Random, CampaignState.WorldSize);
                if (WorldRules.OrgPresence(state, city, traveller.Org) == 0)
                {
                    continue;
                }
                // BUG: CA-35 the lookup adds no knowledge, so it never returns a person and "person -1" is tested;
                // OriginalBehaviour.FixDepartureLookup looks the person of the pair up as it is
                var fixLookup = state.Behaviour.FixDepartureLookup;
                var local = fixLookup ? PersonAt(state, traveller.Org, city) : FindOrCreatePerson(state, traveller.Org, city, 0, out _);
                if (fixLookup
                    ? local != -1 && (state.Ps[local].Role != 0 || state.Ps[local].Clues != 0)
                    : RoleOf(state, local) != 0 || CluesOf(state, local) != 0)
                {
                    continue;
                }
                // DORMANT: no city passes the test above with the program's own text in front of the persons table
                fromCity = traveller.Loc;
                if (local != -1 || !fixLookup)
                {
                    CastRules.ReleasePerson(state, local);
                }
                traveller.Loc = (short)city;
                CastRules.ClearPersonKnowledge(state, person, KnownCity);
                traveller.Alert = (short)(traveller.Alert / 2);
                return person;
            }
        }
        return -1;
    }

    /// <summary>The person of an organisation in a city whatever the player knows of them, or -1 when there is none.</summary>
    private static int PersonAt(CampaignState state, int org, int city)
    {
        for (var index = 0; index < state.Nperson; index++)
        {
            if (state.Ps[index].Org == org && state.Ps[index].Loc == city)
            {
                return index;
            }
        }
        return -1;
    }

    // "Person -1" is the end of the text in front of the persons table: its clue word is the text's characters 12
    // and 13, its role word characters 14 and 15 (little-endian words).

    /// <summary>The role word of a person; for -1 the word the original reads in front of the table.</summary>
    private static int RoleOf(CampaignState state, int person) =>
        person == -1 ? WordOfText(state.TextBeforePersons, 14) : state.Ps[person].Role;

    /// <summary>The clue word of a person; for -1 the word the original reads in front of the table.</summary>
    private static int CluesOf(CampaignState state, int person) =>
        person == -1 ? WordOfText(state.TextBeforePersons, 12) : state.Ps[person].Clues;

    /// <summary>The word made of two characters of a text, the first the low byte.</summary>
    private static int WordOfText(string text, int position)
    {
        // C: ps[-1] lies over the text's bytes; a text shorter than the record is outside what the program holds there
        if (text.Length < position + 2)
        {
            throw new InvalidOperationException("the text in front of the persons table is shorter than the record the original reads there");
        }
        return (short)((byte)text[position] | (byte)text[position + 1] << 8);
    }
}
