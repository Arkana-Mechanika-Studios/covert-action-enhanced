using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The passing of time in the campaign and what it brings the player: the clock moves on, the criminals' plot takes
/// its steps, suspects hide and travel, messages are recorded and bulletins shown, what the player learns of persons
/// is announced, and at the end of the case the part saves the campaign and hands over.
/// </summary>
/// <remarks>
/// The rules are in <see cref="PlotRules"/>; this class shows what they bring about. Its functions call each other
/// in a circle in the original (a box on a clean screen redraws the clock, the clock moves the plot, the plot's end
/// hands over, which looks up the alert level, which may find a person, whose news is shown in a box).
/// </remarks>
/// <param name="state">The campaign.</param>
/// <param name="shared">The hand-off block.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="saves">The campaign's save routines.</param>
/// <param name="behaviourOrNull">Which of the original's bugs to reproduce and which cut content plays; the original's own when left out.</param>
public sealed class PlotEngine(CampaignState state, SharedState shared, Boundaries world, CampaignScreens screens, CampaignSaves saves, OriginalBehaviour? behaviourOrNull = null)
{
    private readonly OriginalBehaviour behaviour = behaviourOrNull ?? OriginalBehaviour.Faithful;


    /// <summary><see cref="CampaignState.Pic"/> of a clean screen.</summary>
    private const int CleanScreen = -1;

    /// <summary>Minutes in an hour of the campaign's clock.</summary>
    private const int Hour = 60;

    /// <summary>The kinds of message <see cref="QueueMessage"/> records.</summary>
    private const int MessageOfEvent = 0, MessageDeparture = 1, MessageHiding = 8, MessageOutOfHiding = 9, MessageSighting = 11;

    /// <summary>The part the launcher runs after the end of a case (the debriefing).</summary>
    private const sbyte EndOfCasePart = 9;

    /// <summary>
    /// Shows a box on a clean screen: when the screen still shows a picture of a place, it is cleared and the place
    /// and clock plate drawn first.
    /// </summary>
    /// <param name="text">The box's text.</param>
    /// <param name="x">The box's left edge.</param>
    /// <param name="y">The box's top.</param>
    /// <returns>What the box returns: the line the player chose.</returns>
    // orig: GAME 00f7:3c87 popup_menu_on_clean_screen (src/c/game/game_00f7_3c87.c) matched; tests: GameOracleTests
    // C: the original has no return statement; its callers read what the box left in AX, the box's answer
    public int PopupMenuOnCleanScreen(string text, int x, int y)
    {
        if (state.Pic != CleanScreen)
        {
            screens.ClearScreen();
            PassTime(0);
            state.Pic = CleanScreen;
        }
        return screens.Menus.QuitPrompt(text, x, y);
    }

    /// <summary>
    /// Lets time pass: the world's clock moves on an hour at a time (pausing half a second between hours once there are
    /// messages), and a remaining part of an hour as half an hour. No time only redraws the place and clock plate.
    /// </summary>
    /// <param name="minutes">The minutes that pass.</param>
    // orig: GAME 00f7:8708 pass_time (src/c/game/game_00f7_8708.c) matched; tests: GameOracleTests
    public void PassTime(int minutes)
    {
        const int pause = 0x1e, halfHour = 30;
        if (minutes == 0)
        {
            screens.DrawPlaceClock();
        }
        while (minutes >= Hour)
        {
            AdvanceWorldClock(Hour, false);
            minutes -= Hour;
            if (state.NextSyn > 5)
            {
                screens.Output.WaitVsync(pause);
            }
        }
        if (minutes != 0)
        {
            AdvanceWorldClock(halfHour, false);
        }
    }

    /// <summary>
    /// Records a message in the log at the current minute (<see cref="PlotRules.QueueMessage"/>); when the log has
    /// become full the player is told in a box.
    /// </summary>
    /// <param name="kind">The kind of message.</param>
    /// <param name="arg">For an event its index, else what the kind records.</param>
    // orig: GAME 00f7:7834 queue_message (src/c/game/game_00f7_7834.c) matched; tests: GameOracleTests
    public void QueueMessage(int kind, int arg)
    {
        if (PlotRules.QueueMessage(state, kind, arg))
        {
            state.Txt.Text = state.Bulletins.LogFull;
            PopupMenuOnCleanScreen(state.Txt.Text, 0x32, 0x32);
        }
    }

    /// <summary>
    /// Whether an event of the plot can happen now (<see cref="PlotRules.EventReady"/>); each suspect who comes out of
    /// hiding for it is announced in a bulletin and recorded in the log.
    /// </summary>
    /// <param name="q">The event.</param>
    /// <returns>-1 when it must wait; 1 for an event without partner; else the partner's event it answers.</returns>
    // orig: GAME 00f7:92b9 event_ready (src/c/game/game_00f7_92b9.c) matched; tests: GameOracleTests
    public int EventReady(int q)
    {
        var check = PlotRules.EventReady(state, q);
        foreach (var person in check.OutOfHiding)
        {
            state.Txt.Text = state.Bulletins.OutOfHidingStart;
            CampaignTexts.AppendPersonName(state, person);
            state.Txt.Text += state.Bulletins.OutOfHiding;
            ShowBulletin(state.Bulletins.SurveillanceTeam);
            QueueMessage(MessageOutOfHiding, person);
        }
        return check.Result;
    }

    /// <summary>
    /// The player seized a plot object (<see cref="PlotRules.SeizePlotObject"/>); a full log is announced in a box.
    /// </summary>
    /// <param name="plotObject">The object, 0..3.</param>
    // orig: GAME 00f7:2fb6 seize_plot_object (src/c/game/game_00f7_2fb6.c) matched; tests: GameOracleTests
    public void SeizePlotObject(int plotObject)
    {
        if (PlotRules.SeizePlotObject(state, plotObject))
        {
            state.Txt.Text = state.Bulletins.LogFull;
            PopupMenuOnCleanScreen(state.Txt.Text, 0x32, 0x32);
        }
    }

    /// <summary>
    /// Finds the person of an organisation in a city, or makes one up, and adds what the player learned
    /// (<see cref="PlotRules.FindOrCreatePerson"/>); when asked, what is new is announced.
    /// </summary>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="flags">Knowledge bits the player learns.</param>
    /// <param name="announce">Whether the news is shown to the player.</param>
    /// <returns>The person, or -1 when nothing is known of them.</returns>
    // orig: GAME 00f7:4899 find_or_create_person (src/c/game/game_00f7_4899.c) matched; tests: GameOracleTests
    // GAME's copy is other code than the library's find_or_create_person of FINAL, TAC and BUG
    public int FindOrCreatePerson(int org, int city, int flags, bool announce)
    {
        var person = PlotRules.FindOrCreatePerson(state, org, city, flags, out var oldClues);
        if (person != -1 && announce)
        {
            AnnounceNewKnowledge(person, oldClues);
        }
        return person;
    }

    /// <summary>
    /// Tells the player, one box at a time, what he has newly learned of a person: that the person takes part in the
    /// plot, the real name, a photograph, the city, the organisation (which counts towards its activity report), more
    /// information, the rank, recruiting information. Before each box the person's dossier is shown as it stands; at
    /// the end the full dossier, and a key is awaited.
    /// </summary>
    /// <param name="person">The person, whose knowledge word already holds everything now known.</param>
    /// <param name="oldClues">What the player knew before.</param>
    // orig: GAME 00f7:4a04 announce_new_knowledge (src/c/game/game_00f7_4a04.c) matched; tests: GameOracleTests
    public void AnnounceNewKnowledge(int person, int oldClues)
    {
        const int boxX = 0x14, boxY = 0x3c;
        var who = state.Ps[person];
        var known = who.Clues;
        who.Clues = (short)oldClues;
        if (known == oldClues)
        {
            return;
        }
        var words = state.Knowledge;
        do
        {
            screens.DataFiles.DrawPersonDossier(person, 0, 0);
            state.Menu.MenuPauseMode = false;
            known |= who.Clues;
            var news = known ^ who.Clues;
            // the first news in this order is told; the loop comes back for the rest
            if ((news & Participant) != 0)
            {
                who.Clues |= Participant;
                Announce(person, "", words.Participant);
            }
            else if ((news & NameKnown) != 0)
            {
                state.Txt.Text = "";
                CampaignTexts.AppendPersonName(state, person);
                state.Txt.Text += words.Identified;
                // the code name first, then the real name it now stands for
                who.Clues |= NameKnown;
                CampaignTexts.AppendPersonName(state, person);
                state.Txt.Text += words.LineBreak;
                PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
            }
            else if ((news & PhotoKnown) != 0)
            {
                who.Clues |= PhotoKnown;
                Announce(person, words.Photograph, words.PhotographEnd);
            }
            else if ((news & CityKnown) != 0)
            {
                who.Clues |= CityKnown;
                state.Txt.Text = "";
                CampaignTexts.AppendPersonName(state, person);
                state.Txt.Text += words.Spotted;
                state.Txt.Text += state.Ct[who.Loc].Name;
                state.Txt.Text += words.LineBreak;
                PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
                state.Active[who.Loc]++;
            }
            else if ((news & OrgKnown) != 0)
            {
                who.Clues |= OrgKnown;
                state.Txt.Text = "";
                CampaignTexts.AppendPersonName(state, person);
                state.Txt.Text += words.Member;
                state.Txt.Text += state.Orgs[who.Org].Name;
                state.Txt.Text += words.LineBreak;
                PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
                state.Orgs[who.Org].Clues |= 1;
                state.OrgCounters[who.Org]++;
            }
            else if ((news & MoreKnown) != 0)
            {
                who.Clues |= MoreKnown;
                Announce(person, words.AdditionalInformation, words.LineBreak);
            }
            else if ((news & RankKnown) != 0)
            {
                who.Clues |= RankKnown;
                Announce(person, words.Rank, words.LineBreak);
            }
            else if ((news & RecruitedWhereKnown) != 0)
            {
                who.Clues |= RecruitedWhereKnown;
                Announce(person, words.RecruitedWhere, words.LineBreak);
            }
            else if ((news & RecruitedByWhomKnown) != 0)
            {
                who.Clues |= RecruitedByWhomKnown;
                Announce(person, words.RecruitedByWhom, words.LineBreak);
            }
        }
        while (who.Clues != known);
        screens.DataFiles.DrawPersonDossier(person, 0, 0);
        screens.Screens.WaitForKey();
    }

    /// <summary><see cref="Core.Records.Person.Clues"/>: what the player knows of a person.</summary>
    private const short PhotoKnown = 0x01, NameKnown = 0x02, OrgKnown = 0x04, CityKnown = 0x08, Participant = 0x10, MoreKnown = 0x20,
        RankKnown = 0x100, RecruitedWhereKnown = 0x200, RecruitedByWhomKnown = 0x400;

    /// <summary>A box of news: the words before the person's name, the name, the words after it.</summary>
    private void Announce(int person, string before, string after)
    {
        const int boxX = 0x14, boxY = 0x3c;
        state.Txt.Text = before;
        CampaignTexts.AppendPersonName(state, person);
        state.Txt.Text += after;
        PopupMenuOnCleanScreen(state.Txt.Text, boxX, boxY);
    }

    /// <summary>
    /// Appends how alert an organisation is in a city ("quiet" where it has nobody, else unsuspecting .. very nervous)
    /// to the text buffer. The alertness is the general alarm weighted by the organisation's distance from the
    /// player's agency on the map, averaged with its local person's alert, plus 16 in a city of doubled alertness.
    /// </summary>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <returns>The alertness (0 for a quiet one; 99 where the sum went negative).</returns>
    // orig: GAME 00f7:3a95 append_alert_level (src/c/game/game_00f7_3a95.c) matched; tests: GameOracleTests
    public int AppendAlertLevel(int org, int city)
    {
        const int playerAgency = 1, distanceScale = 0x18, doubledBonus = 0x10, overflow = 0x63, perLevel = 0xc;
        if (WorldRules.OrgPresence(state, city, org) == 0)
        {
            state.Txt.Text += state.Knowledge.Quiet;
            return 0;
        }
        var local = FindOrCreatePerson(org, city, 0, false);
        // C: a 16-bit product and a signed division
        int alertness = C16.Div(C16.Int(WorldRules.MapDistance(state.Orgs[playerAgency].Climate, state.Orgs[org].Climate) * state.Alerted), distanceScale);
        if (local != -1)
        {
            alertness = C16.Div(C16.Int(state.Ps[local].Alert + alertness), 2);
        }
        // C: a shift by a variable count of a 16-bit int
        if ((state.Doubles & C16.Shl(1, city)) != 0)
        {
            alertness = C16.Int(alertness + doubledBonus);
        }
        if (alertness < 0)
        {
            alertness = overflow;
        }
        var level = Math.Min(alertness / perLevel, KnowledgeWords.AlertLevels - 1);
        state.Txt.Text += state.Knowledge.Alertness[level];
        return alertness;
    }

    /// <summary>
    /// Leaves the campaign part: the hand-off block gets the alertness where the player is, the level and the part
    /// to run next; the campaign is saved for the next part; the game's palette and screen are put back; and when the
    /// player quits the game the farewell picture is shown until a key is pressed.
    /// </summary>
    // orig: GAME 00f7:034e save_and_hand_off (src/c/game/game_00f7_034e.c) matched; tests: GameOracleTests
    public void SaveAndHandOff()
    {
        const sbyte quitTheGame = -1;
        shared.Alert = (short)AppendAlertLevel(state.AtLoc, state.InCity);
        shared.LevelCopy = state.Diff;
        shared.NextModule = (sbyte)state.Quit;
        shared.GameMode = (sbyte)state.Quit;
        shared.Elapsed = 0;
        saves.SaveGame(state.StrHandOffSave);
        world.Graphics.SetPalette(state.EgaPalette);
        world.Graphics.ShowPage(0);
        if (shared.NextModule == quitTheGame)
        {
            screens.Pictures.LoadPictureSwapped(state.StrGoodbyePicture, 0);
            screens.Screens.WaitForKey();
        }
    }

    /// <summary><see cref="Core.Records.Event.Status"/>: the event is announced as a bulletin of its own; the kind of event (message sent, meeting, trip).</summary>
    private const int Announced = 0x2000, EventKind = 0xf00, Sent = 0x200, Meeting = 0x800, Trip = 0x900, PartnerMember = 0xff;

    /// <summary><see cref="Core.Records.Person.Alert"/> of a suspect turned double agent.</summary>
    private const int Turned = -3;

    /// <summary><see cref="Core.Records.Synopsis.Clues"/>: the whole message is known, and decoded.</summary>
    private const short MessageComplete = 0xf, MessageDecoded = unchecked((short)0x8000), MessageTrip = 9;

    /// <summary>Plot objects of a case.</summary>
    private const int PlotObjects = 4;

    /// <summary>
    /// Moves the world's clock on and, when a new day began or noon was passed (or when forced), lets the criminals'
    /// plot take a step: trails and the alarm fade, suspects may go into hiding, the case may end (then the campaign is
    /// saved and the part hands over to the debriefing), one event that can happen is scheduled, and the events of the
    /// step are recorded and reported: plot objects spotted, message traffic, wiretaps and double agents, returns from
    /// a trip; last, a spotted suspect may be seen leaving his city.
    /// </summary>
    /// <param name="minutes">The minutes that pass.</param>
    /// <param name="force">Whether the step is taken whatever the time (then nothing after the case-end test happens).</param>
    /// <exception cref="ProgramExitException">The case is over: the part has handed over and ends.</exception>
    // orig: GAME 00f7:8755 advance_world_clock (src/c/game/game_00f7_8755.c) matched; tests: GameOracleTests
    public void AdvanceWorldClock(int minutes, bool force)
    {
        int hourBefore = state.Hour, dayBefore = state.Day;
        state.Gtime = (short)(state.Gtime + minutes);
        screens.DrawPlaceClock();
        screens.Output.WaitVsync(2);
        if (!PlotRules.BeginPlotStep(state, hourBefore, dayBefore, force))
        {
            return;
        }
        var ready = CountReadyEvents();
        if (PlotRules.SkipPlotStep(state, ready, force))
        {
            return;
        }
        if (ready <= state.Diff)
        {
            state.ExcludedPerson = -1;
            for (var q = 0; q < state.NEVENT - 1; q++)
            {
                if (EventReady(q) != -1)
                {
                    ready = 1;
                    break;
                }
            }
        }
        var noOneAtLarge = SendIntoHiding(ready);
        if (PlotRules.CaseIsOver(state, noOneAtLarge, ready))
        {
            EndTheCase();
        }
        if (force)
        {
            return;
        }
        if (ready != 0)
        {
            int chosen, answered;
            do
            {
                chosen = GameLibrary.RandomBelow(state.Random, state.NEVENT - 1);
                answered = EventReady(chosen);
            }
            while (answered == -1);
            PlotRules.ScheduleEvent(state, chosen, answered);
        }
        for (var happening = 0; happening < state.NEVENT; happening++)
        {
            if (state.Ev[happening].Chron == state.ChronTime)
            {
                ReportEvent(happening);
            }
        }
        ReportDeparture();
    }

    /// <summary>How many events of the plot can happen now (the last event is the plot's end marker).</summary>
    private int CountReadyEvents()
    {
        var ready = 0;
        for (var q = 0; q < state.NEVENT - 1; q++)
        {
            if (EventReady(q) != -1)
            {
                ready++;
            }
        }
        return ready;
    }

    /// <summary>Sends suspects into hiding one by one (<see cref="PlotRules.TryHide"/>), with a bulletin for each the player knows; returns whether no one is at large.</summary>
    private bool SendIntoHiding(int ready)
    {
        var someoneHid = false;
        while (true)
        {
            var person = PlotRules.TryHide(state, ready, someoneHid, out var noOneAtLarge);
            if (person == -1)
            {
                return noOneAtLarge;
            }
            if (state.Ps[person].Clues != 0)
            {
                state.Txt.Text = state.Bulletins.GoneIntoHidingStart;
                CampaignTexts.AppendPersonName(state, person);
                state.Txt.Text += state.Bulletins.GoneIntoHiding;
                ShowBulletin(state.Bulletins.SurveillanceTeam);
            }
            QueueMessage(MessageHiding, person);
            someoneHid = true;
        }
    }

    /// <summary>The case is over: the mastermind's knowledge is kept, the campaign saved, the data disk asked for, and the part hands over to the debriefing.</summary>
    private void EndTheCase()
    {
        const int dataDisk = 1, askUntilThere = 1;
        state.Pl.Clues = state.Ps[CastRules.MastermindSlot].Clues;
        saves.SaveGame(state.Bulletins.EndOfCaseSave);
        screens.Menus.CheckForDisk(dataDisk, askUntilThere);
        SaveAndHandOff();
        shared.NextModule = EndOfCasePart;
        // C: the original calls exit() without an argument, so its exit code is a stack word (150 in the session
        // GAME/mastermind-debrief); nothing reads it: the launcher (COVERT 0000:0010) drops spawnl's result and goes by
        // the hand-off block's next part
        throw new ProgramExitException(0);
    }

    /// <summary>
    /// An event of this step: recorded in the log; the plot objects it moves change hands (an object a suspect takes
    /// may be spotted by INTERPOL); then it is announced as a bulletin of its own, or reported as message traffic, by
    /// a wiretap at the suspect's building or a double agent, or by the airport on a suspect's return from a trip.
    /// </summary>
    private void ReportEvent(int happening)
    {
        var ev = state.Ev[happening];
        QueueMessage(MessageOfEvent, happening);
        int suspect = state.Ac[ev.Act].Who;
        for (var i = 0; i < PlotObjects; i++)
        {
            if ((ev.Macguff & 1 << i) != 0)
            {
                TakeObject(happening, i, suspect);
            }
            if ((ev.Macguff & 0x100 << i) != 0)
            {
                state.Mc[i].Who = -1;
            }
        }
        if ((ev.Status & Announced) != 0)
        {
            state.Txt.Text = "";
            screens.DescribeEvent(happening);
            ShowBulletin("");
            return;
        }
        if ((ev.Status & EventKind) == Sent && state.NextSyn > 5)
        {
            ReportTraffic(happening, suspect);
        }
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, state.Ps[suspect].Org, state.Ps[suspect].Loc, 0, out _);
        // C: the roll is only drawn for a suspect who is no double agent and has a wiretapped building
        if (state.Ps[suspect].Alert == Turned || (link != -1 && state.Lc[link].Tapped > GameLibrary.RandomBelow(state.Random, 50)))
        {
            ReportIntercepts(happening, suspect);
        }
        if ((ev.Status & EventKind) == Trip && (state.Ps[suspect].Clues & PhotoKnown) != 0)
        {
            ReportReturns(happening, suspect);
        }
    }

    /// <summary>A suspect takes a plot object; INTERPOL may spot it in the suspect's city.</summary>
    private void TakeObject(int happening, int plotObject, int suspect)
    {
        state.Mc[plotObject].Who = (sbyte)suspect;
        if (GameLibrary.RandomBelow(state.Random, 2) >= state.Diff && (state.Ev[happening].Status & Announced) == 0 && state.NextSyn > 5)
        {
            var city = state.Ps[suspect].Loc;
            state.Txt.Text = state.Bulletins.InterpolStart;
            state.Txt.Text += state.Mc[plotObject].Name;
            state.Txt.Text += state.Bulletins.SpottedIn;
            state.Txt.Text += state.Ct[city].Name;
            state.Txt.Text += state.Bulletins.Exclamation;
            ShowBulletin(state.Bulletins.Interpol);
            QueueMessage(MessageSighting, (plotObject << 8) + city);
        }
    }

    /// <summary>A satellite intercept of a message sent: the city it came from or the city it went to (a coin decides), which counts for the activity report.</summary>
    private void ReportTraffic(int happening, int suspect)
    {
        state.Txt.Text = state.Bulletins.MessageTraffic;
        int city;
        if (GameLibrary.RandomBelow(state.Random, 2) != 0)
        {
            state.Txt.Text += state.Bulletins.From;
            city = state.Ps[suspect].Loc;
        }
        else
        {
            state.Txt.Text += state.Bulletins.To;
            city = state.Ps[state.Ac[PlotRules.PartnerMemberOf(state, happening)].Who].Loc;
        }
        state.Txt.Text += state.Ct[city].Name;
        state.Active[city]++;
        state.Txt.Text += state.Bulletins.TrafficEnd;
        ShowBulletin(state.Bulletins.SatelliteIntercept);
    }

    /// <summary>
    /// A wiretap or a double agent reports the event's messages of this minute: each becomes complete and decoded and
    /// is shown; a double agent also reveals the other party (organisation and city known, and the role of this event).
    /// </summary>
    private void ReportIntercepts(int happening, int suspect)
    {
        var ev = state.Ev[happening];
        var who = state.Ps[suspect];
        var words = state.Bulletins;
        for (var i = 0; i < state.NextSyn; i++)
        {
            var message = state.Sn[i];
            if (message.Type != MessageOfEvent || message.Data != happening || message.Time != state.Gtime)
            {
                continue;
            }
            message.Clues |= MessageComplete;
            message.Clues |= MessageDecoded;
            var partner = state.Ps[message.Who2];
            if (who.Alert == Turned)
            {
                state.Txt.Text = words.DoubleAgent;
                partner.Clues |= OrgKnown | CityKnown;
                // C: a shift of a 16-bit int by the low byte of the status
                partner.Rknown |= C16.Shl(1, (sbyte)PlotRules.PartnerMemberOf(state, happening));
            }
            else
            {
                state.Txt.Text = words.WiretapAt;
            }
            state.Txt.Text += state.Orgs[who.Org].Name;
            state.Txt.Text += words.Slash;
            state.Txt.Text += state.Ct[who.Loc].Name;
            state.Txt.Text += words.Reveals;
            if (who.Alert == Turned)
            {
                state.Txt.Text += (who.Face & 1) != 0 ? words.He : words.She;
            }
            else
            {
                CampaignTexts.AppendPersonName(state, suspect);
                state.Txt.Text += words.Space;
            }
            CampaignTexts.AppendMessageText(state, i);
            state.Txt.Text += words.LineBreak;
            if ((ev.Status & EventKind) == Meeting)
            {
                screens.DescribeEvent(happening);
                partner.Rknown |= partner.Role;
            }
            else if (who.Alert == Turned)
            {
                screens.DescribeEvent(happening);
            }
            ShowBulletin("");
        }
    }

    /// <summary>The airport reports a photographed suspect back from a trip to another city.</summary>
    private void ReportReturns(int happening, int suspect)
    {
        var words = state.Bulletins;
        for (var i = 0; i < state.NextSyn; i++)
        {
            var message = state.Sn[i];
            if (message.Type != MessageOfEvent || message.Data != happening || message.Loc == message.Loc2)
            {
                continue;
            }
            state.Txt.Text = words.AirportFrom;
            state.Txt.Text += state.Orgs[PlayerAgency].Name;
            state.Txt.Text += words.Slash;
            state.Txt.Text += state.Ct[message.Loc].Name;
            state.Txt.Text += words.LineBreak;
            CampaignTexts.AppendPersonName(state, suspect);
            state.Txt.Text += words.ReturnedFrom;
            state.Txt.Text += state.Ct[message.Loc2].Name;
            state.Txt.Text += words.FullStop;
            ShowBulletin(words.AirportSurveillance);
            message.Clues |= MessageTrip;
        }
    }

    /// <summary>The player's agency among the organisations.</summary>
    private const int PlayerAgency = 1;

    /// <summary>A spotted suspect may be seen leaving his city (<see cref="PlotRules.TryDepart"/>): the airport reports it and the log records it.</summary>
    private void ReportDeparture()
    {
        var person = PlotRules.TryDepart(state, out var fromCity);
        if (person == -1)
        {
            return;
        }
        // DORMANT: no suspect ever leaves with the program's own texts (CA-35); the original shows this before the move
        state.Txt.Text = "";
        CampaignTexts.AppendPersonName(state, person);
        state.Txt.Text += state.Bulletins.DepartedFrom;
        state.Txt.Text += state.Ct[fromCity].Name;
        state.Txt.Text += state.Bulletins.FullStop;
        ShowBulletin(state.Bulletins.AirportSurveillance);
        QueueMessage(MessageDeparture, (person << 8) + fromCity);
    }

    /// <summary>
    /// The player, caught, is offered an exchange of prisoners (the offer is in the text buffer; the answers are
    /// added). Agreed, he is exchanged for the arrested suspect of the plot who matters least (cover less 2 for each
    /// of his events done, the last of the lowest): the suspect goes free, alerted, the building of his organisation
    /// in his city is lost and the release is recorded; with no one arrested, for two double agents: two random
    /// cities become more alert.
    /// </summary>
    /// <remarks>Random draws: two cities when no suspect is arrested.</remarks>
    /// <returns>Whether the exchange took place.</returns>
    // orig: GAME 00f7:323b offer_prisoner_exchange (src/c/game/game_00f7_323b.c) matched; tests: GameOracleTests
    public bool OfferPrisonerExchange()
    {
        const int agree = 1, released = 0x63, exchanged = 0x40, messageExchange = 0xd;
        var prisoner = LeastValuablePrisoner();
        state.Txt.Text += state.StrExchangeChoices;
        if (PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15) != agree)
        {
            return false;
        }
        state.Txt.Text = state.StrExchangedFor;
        if (prisoner != -1)
        {
            var who = state.Ps[prisoner];
            CampaignTexts.AppendPersonName(state, prisoner);
            state.Txt.Text += state.StrExchangedEnd;
            who.Alert = released;
            who.Clues |= exchanged;
            CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, who.Org, who.Loc);
            QueueMessage(messageExchange, prisoner);
        }
        else
        {
            state.Txt.Text += state.StrTwoDoubleAgents;
            // C: shifts of a 16-bit int by a variable count
            state.Doubles |= C16.Shl(1, GameLibrary.RandomBelow(state.Random, CampaignState.WorldSize));
            state.Doubles |= C16.Shl(1, GameLibrary.RandomBelow(state.Random, CampaignState.WorldSize));
        }
        PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x79);
        return true;
    }

    /// <summary>The arrested suspect of the plot with the lowest score (cover, less 2 per event done; the last of equals), or -1.</summary>
    private int LeastValuablePrisoner()
    {
        const int eventDone = 0x3e7, arrested = -1;
        var best = -1;
        var lowest = 0x63;
        for (var member = 0; state.NACTIVITY - (state.Diff << 2) > member; member++)
        {
            int person = state.Ac[member].Who;
            int score = state.Ac[member].Cover;
            for (var q = 0; q < state.NEVENT - 1; q++)
            {
                if (state.Ev[q].Act == member && state.Ev[q].Chron == eventDone)
                {
                    score -= 2;
                }
            }
            if (state.Ps[person].Alert == arrested && score <= lowest)
            {
                best = person;
                lowest = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Waiting in the hotel: the world's clock moves on an hour at a time until a key is pressed or the part is to be
    /// left; meanwhile the player counts as waiting (<see cref="CampaignState.Sleepy"/>).
    /// </summary>
    // orig: GAME 00f7:624a wait_in_hotel (src/c/game/game_00f7_624a.c) matched; tests: GameOracleTests
    public void WaitInHotel()
    {
        state.Sleepy = 1;
        while (state.Quit == 0)
        {
            if (world.Input.Kbhit())
            {
                break;
            }
            AdvanceWorldClock(Hour, false);
        }
        state.Sleepy = 0;
    }

    /// <summary>
    /// The player is caught: the capture scene and his questioning, then the offer of an exchange of prisoners.
    /// Exchanged, he leaves the building (on the harder levels the building is lost). Otherwise he sits in jail for 2
    /// to 4 spells of six hours, while the criminals may free one of the player's prisoners each time (a jail break:
    /// the prisoner goes free, alerted, the building of his organisation is lost, the break is recorded); then he
    /// escapes, and the part is left for the escape (exit mode 7).
    /// </summary>
    /// <remarks>Random draws: one before each spell (and one more at the end), besides the exchange's.</remarks>
    // orig: GAME 00f7:3077 surrender_scene (src/c/game/game_00f7_3077.c) matched; tests: GameOracleTests
    public void SurrenderScene()
    {
        const int boxWidth = 0x18, sceneX = 0xe0, spell = 0x168, escapeMode = 7, released = 0x63, exchanged = 0x40, messageJailBreak = 0xa;
        world.Graphics.SetColorMaps(state.SurrenderColorMap, state.SurrenderColorMap);
        screens.Menus.StartAnimation(state.StrSurrenderScene, sceneX, 0);
        state.Txt.Text = "";
        // C: the original passes no replacement and no variant (the reader takes what its stack holds); neither tag
        // ends in '0', and these prose texts have no "$RPLC"
        screens.LoadProseText(state.GrilledProseKey, boxWidth, "", 0);
        if (OfferPrisonerExchange())
        {
            if (state.Diff != 0)
            {
                CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, state.AtLoc, state.InCity);
            }
            state.LeavingBuilding = 1;
            return;
        }
        // C: the number of spells is drawn again before each one
        for (var spells = 0; GameLibrary.RandomBelow(state.Random, 3) + 2 > spells; spells++)
        {
            PassTime(spell);
            if (state.Sprung == -1)
            {
                continue;
            }
            var prisoner = state.Ps[state.Sprung];
            screens.ClearScreen();
            world.Graphics.SetColorMaps(state.WarningColorMap, state.WarningColorMap);
            screens.Menus.StartAnimation(state.StrWarningScene, 0xa0, 0);
            state.Txt.Text = state.StrJailBreakIn;
            state.Txt.Text += state.Ct[prisoner.Loc].Name;
            state.Txt.Text += state.StrJailBreakCityEnd;
            state.Txt.Text += state.Orgs[prisoner.Org].Name;
            state.Txt.Text += state.StrJailBreakAgentsFree;
            CampaignTexts.AppendPersonName(state, state.Sprung);
            state.Txt.Text += state.StrJailBreakEnd;
            PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
            prisoner.Clues |= exchanged;
            prisoner.Alert = released;
            CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, prisoner.Org, prisoner.Loc);
            QueueMessage(messageJailBreak, state.Sprung);
            state.Sprung = -1;
        }
        world.Graphics.SetColorMaps(state.EscapeColorMap, state.EscapeColorMap);
        screens.Menus.StartAnimation(state.StrEscapeScene, sceneX, 0);
        state.Txt.Text = "";
        screens.LoadProseText(state.EscapeProseKey, boxWidth, "", 0);
        PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
        state.Quit = escapeMode;
    }

    /// <summary>
    /// The interrogation of a captured suspect. The player accuses him with the roles he knows (all of them when the
    /// suspect was taken without a fight). Without evidence the suspect goes free, alerted. Otherwise he is arrested
    /// (or, taken without a fight, turned into a double agent), and each of his recorded messages is put to him: one
    /// he confesses (a double agent always) is decoded on the spot. A MasterMind lists the whole plot. An arrested
    /// suspect is taken to jail; the chief comments; four hours pass.
    /// </summary>
    /// <param name="person">The suspect.</param>
    // orig: GAME 00f7:28a4 interrogate_suspect (src/c/game/game_00f7_28a4.c) matched; tests: GameOracleTests
    public void InterrogateSuspect(int person)
    {
        const int boxWidth = 0x18, sceneX = 0xe0, playerAgency = 1, buildingBits = 0xc, released = 0x63, interrogationTime = 0xf0;
        const int messageArrest = 6, messageTurned = 0xc, messageReleased = 9, everythingLearned = 0x71f;
        var who = state.Ps[person];
        state.AtLoc = playerAgency;
        state.FaceX = 0x18;
        state.FaceY = 0x25;
        state.Picface = who.Face;
        world.Graphics.SetColorMaps(state.InterrogationColorMap, state.InterrogationColorMap);
        screens.Menus.StartAnimation(state.StrInterrogationScene, sceneX, 0);
        screens.Animation.SetScriptVar(0, (who.Face & 1) != 0 ? -1 : 1);
        var takenUnhurt = shared.NotHurt == 0;
        state.Txt.Text = state.StrYouAccuse;
        CampaignTexts.AppendPersonName(state, person);
        state.Txt.Text += state.StrAccuseRoles;
        if (takenUnhurt)
        {
            who.Rknown |= (short)(who.Role & ActiveRoles);
        }
        var evidence = false;
        var member = NoCastMember;
        for (var m = 0; state.NACTIVITY - state.Diff * 4 > m; m++)
        {
            // C: a shift by a variable count of a 16-bit int
            if ((who.Rknown & C16.Shl(1, m)) != 0)
            {
                evidence = true;
                state.Txt.Text += state.Ac[m].Name;
                state.Txt.Text += state.StrAccuseRoleEnd;
            }
            if (state.Ac[m].Who == person)
            {
                member = m;
            }
        }
        if (who.Org != playerAgency && !takenUnhurt)
        {
            CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, who.Org, who.Loc);
        }
        if (!evidence)
        {
            who.Alert = CastRules.Arrested;
            state.Txt.Text = state.StrNoEvidence;
            CampaignTexts.AppendPersonName(state, person);
            state.Txt.Text += (who.Face & 1) != 0 ? state.StrNoEvidenceHe : state.StrNoEvidenceShe;
            state.Txt.Text += state.StrReleased;
            PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
            who.Alert = released;
            WorldRules.RaiseSuspicion(state, who.Org, who.Loc, released);
            QueueMessage(messageReleased, person);
            if (person == state.Sprung)
            {
                state.Sprung = -1;
            }
            return;
        }
        PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
        CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, playerAgency, state.InCity, buildingBits, out var buildings);
        state.Nloc = (short)buildings;
        state.Txt.Text = "";
        if (!takenUnhurt)
        {
            who.Alert = CastRules.Arrested;
            screens.LoadProseText(state.ArrestProseKey, boxWidth, "", 0);
        }
        else
        {
            who.Alert = Turned;
            screens.LoadProseText(state.DoubleAgentProseKey, boxWidth, "", 0);
        }
        QueueMessage(takenUnhurt ? messageTurned : messageArrest, person);
        screens.Menus.ClearAnimationArea();
        PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
        PutMessagesTo(person);
        who.Clues |= everythingLearned;
        if (!takenUnhurt && who.Rknown == 0)
        {
            AdvanceWorldClock(interrogationTime, true);
            return;
        }
        // BUG: CA-33 a suspect with a known role who is no cast member (member -1) makes the original read ac[-1]: its
        // status is DS:979c, the carry word of the library's BIOS registers (0 or 1, never the MasterMind's bit)
        var mastermind = member != NoCastMember && (state.Ac[member].Status & CastRules.CastMastermind) != 0;
        if (mastermind)
        {
            state.Txt.Text = state.StrMastermindBreaks;
            LibraryTexts.WrapTextBuffer(state.Txt, boxWidth, state.LineEnd);
            screens.Menus.ClearAnimationArea();
            PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
        }
        for (var m = 0; state.NACTIVITY - state.Diff * 4 > m; m++)
        {
            if (state.Ac[m].Who == person)
            {
                // the low byte: all five ways of finding clues are used up
                state.Ac[m].Clued |= 0x1f;
            }
            if (mastermind)
            {
                var participant = state.Ac[m].Who;
                state.Ps[participant].Clues |= 0x1f;
                state.Ps[participant].Rknown |= (short)(state.Ps[participant].Role & ActiveRoles);
                screens.DataFiles.DrawPersonDossier(participant, 0, 0);
                screens.Screens.WaitForKey();
            }
        }
        if (!takenUnhurt)
        {
            ShowJail(person);
        }
        // DIVERGENCE: D-660 a player's option (cutContent.capturedDossier): the dossier page CAPTRD01.PAN, which no program of the original plays (CC-1), for a captured cast member
        if (behaviour.CutContent.CapturedDossier && member != NoCastMember)
        {
            ShowCapturedDossier(person);
        }
        ShowChiefsComment(person, member);
        AdvanceWorldClock(interrogationTime, true);
    }

    /// <summary><see cref="Core.Records.Person.Role"/>: the roles a person plays (bit 15 aside).</summary>
    private const int ActiveRoles = 0x7fff;

    /// <summary>Each recorded message of a meeting or a message sent of the suspect is put to him (on the harder levels only those the player knows something of, unless he turned).</summary>
    private void PutMessagesTo(int person)
    {
        const int boxWidth = 0x18, messageParts = 0xf, cluesBits = 0x1f, sceneX = 0xe0;
        var who = state.Ps[person];
        for (var msg = 0; msg < state.NextSyn; msg++)
        {
            var message = state.Sn[msg];
            if (message.Type != MessageOfEvent || message.Who != person)
            {
                continue;
            }
            if ((message.Clues & messageParts) == 0 && state.Diff >= 2 && who.Alert != Turned)
            {
                continue;
            }
            var status = state.Ev[message.Data].Status;
            if ((status & Meeting) == 0 && (status & Sent) == 0)
            {
                continue;
            }
            state.Txt.Text = "";
            screens.LoadProseText(state.QuestionProseKey, boxWidth, "", 0);
            CampaignTexts.AppendMessageText(state, msg);
            state.Txt.Text += state.StrYouAsk;
            var confesses = state.BitCount[message.Clues & cluesBits] >= state.Diff * 2 || who.Alert == Turned;
            state.Txt.Text += confesses ? state.StrConfesses : state.StrDoesNotRemember;
            LibraryTexts.WrapTextBuffer(state.Txt, boxWidth, state.LineEnd);
            screens.Menus.ClearAnimationArea();
            PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x15);
            if (confesses)
            {
                // the scene on the right is kept while the message is decoded on the whole screen
                world.Graphics.CopyRect(state.Rp1, sceneX, 0, 0x60, 0xc8, state.RpBack, sceneX, 0);
                screens.Messages.ShowMessageDecode(msg);
                world.Graphics.FillRect(state.Rp1, 0, 0, sceneX, 0xc8, 5);
                world.Graphics.CopyRect(state.RpBack, sceneX, 0, 0x60, 0xc8, state.Rp1, sceneX, 0);
                state.Animation.SaveWidth = sceneX;
            }
        }
    }

    /// <summary>The arrested suspect is taken to the local jail (a scene with his face).</summary>
    private void ShowJail(int person)
    {
        var who = state.Ps[person];
        screens.ClearScreen();
        state.FaceX = 0x65;
        state.FaceY = 0x13;
        state.Picface = who.Face;
        world.Graphics.SetColorMaps(state.JailColorMap, state.JailColorMap);
        screens.Menus.StartAnimation(state.StrJailScene, 0xa8, 0);
        screens.Animation.SetScriptVar(0, (who.Face & 1) != 0 ? -1 : 1);
        state.Txt.Text = "";
        CampaignTexts.AppendPersonName(state, person);
        state.Txt.Text += state.StrTakenToJail;
        PopupMenuOnCleanScreen(state.Txt.Text, 0x18, 0x15);
    }

    /// <summary>The file of the dossier page that the original ships and never plays (CC-1).</summary>
    public const string CapturedDossierScene = "captrd01.pan";

    /// <summary>
    /// The captured cast member's dossier page: the animation CAPTRD01.PAN (a clipped photograph, text lines and a red CAPTURED
    /// stamp), in the frame of the scenes beside it (the jail's: 96 pixels wide at the right), with the person's name in the box.
    /// The photograph is the man's or the woman's by the person's sex, as the script's own switch means (CC-19); the script
    /// itself sets that switch to "man" as its first instruction, which the player (<c>LibraryAnimation</c>) takes out when the
    /// option is on. New code (D-660): the page shows no text of its own and none is written for it.
    /// </summary>
    /// <param name="person">The captured person.</param>
    // DIVERGENCE: D-660 not in the original
    public void ShowCapturedDossier(int person)
    {
        const int sceneX = 0xe0;
        var who = state.Ps[person];
        screens.ClearScreen();
        world.Graphics.SetColorMaps(state.SurrenderColorMap, state.SurrenderColorMap);
        screens.Menus.StartAnimation(CapturedDossierScene, sceneX, 0);
        screens.Animation.SetScriptVar(0, (who.Face & 1) != 0 ? -1 : 1);
        state.Txt.Text = "";
        CampaignTexts.AppendPersonName(state, person);
        PopupMenuOnCleanScreen(state.Txt.Text, 0x18, 0x15);
    }

    /// <summary>The member of the cast a suspect is when he is none (the original's index -1).</summary>
    private const int NoCastMember = -1;

    /// <summary>The chief's comment on the capture, by the suspect's rank (see CA-41 for its variant).</summary>
    private void ShowChiefsComment(int person, int member)
    {
        const int nameBuffer = 20, chiefScene = 2;
        screens.ClearScreen();
        world.Graphics.SetColorMaps(state.BriefingColorMap, state.BriefingColorMap);
        screens.Menus.StartAnimation(state.StrBriefingScene, 0, 0);
        screens.Animation.SetScriptVar(0, chiefScene);
        state.Txt.Text = "";
        CampaignTexts.AppendPersonName(state, person);
        // C: the name is copied into a buffer of 20 bytes on the stack
        var name = new TextBuffer(nameBuffer) { Text = state.Txt.Text };
        state.Txt.Text = "";
        // BUG: CA-33 for a suspect who is no cast member the original reads the rank of ac[-1]: DS:97a2, NACTIVITY
        int rank = member == NoCastMember ? state.NACTIVITY : state.Ac[member].Rank;
        screens.LoadProseText(state.CaptureProseKey, 0x24, name.Text, (rank - 1) / 3);
        LibraryTexts.WrapTextBuffer(state.Txt, 0x28, state.LineEnd);
        PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x95);
    }

    /// <summary>A bulletin of the campaign under a heading, with the text in the text buffer (the library's bulletin on GAME's banner).</summary>
    private void ShowBulletin(string title) =>
        screens.Screens.ShowBulletin(title, state.RpPic, state.BulletinColorMap, state.BannerShapes, state.StrBulletinCaption);

    /// <summary>
    /// The outcome of a raid on an organisation's building in a city: the visit is recorded, the building becomes
    /// known (kind and place), the organisation too; back from the raid, the alarm it raised is added. When the player
    /// was captured the scene of his capture follows (and a high alarm). Otherwise a plot object its local person held
    /// is seized (the chief's praise), the bugs the player planted add to the building's tap, and an arrested target
    /// is interrogated.
    /// </summary>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    // orig: GAME 00f7:2728 resolve_infiltration (src/c/game/game_00f7_2728.c) matched; tests: GameOracleTests
    public void ResolveInfiltration(int org, int city)
    {
        const int messageVisit = 5, buildingBits = 0xc, captured = -1, captureAlarm = 0x64, objectTaken = 1, targetArrested = 2, chiefScene = 2;
        QueueMessage(messageVisit, (org << 8) + city);
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, buildingBits, out var buildings);
        state.Nloc = (short)buildings;
        state.Orgs[org].Clues |= 1;
        // C: the original reads the person only out of a mission (entry_mode); building_menu (GAME 00f7:1e5a) calls it
        // only then, as D_9686 is set to 1 by game_main and never written again
        int returned = shared.Person;
        if (state.EntryMode != 0)
        {
            WorldRules.RaiseSuspicion(state, org, city, shared.Value);
        }
        var local = FindOrCreatePerson(org, city, 0, false);
        if (returned == captured)
        {
            WorldRules.RaiseSuspicion(state, org, city, captureAlarm);
            SurrenderScene();
            return;
        }
        if ((shared.Result & objectTaken) != 0 && local != -1)
        {
            for (var plotObject = 0; plotObject < PlotObjects; plotObject++)
            {
                if (state.Mc[plotObject].Who != local)
                {
                    continue;
                }
                world.Graphics.SetColorMaps(state.BriefingColorMap, state.BriefingColorMap);
                screens.Menus.StartAnimation(state.StrInfiltrationBriefingScene, 0, 0);
                screens.Animation.SetScriptVar(0, chiefScene);
                state.Txt.Text = state.StrFoundObject;
                state.Txt.Text += state.Mc[plotObject].Name;
                state.Txt.Text += state.StrFoundObjectEnd;
                PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x95);
                SeizePlotObject(plotObject);
            }
        }
        state.Lc[link].Tapped += (short)returned;
        if ((shared.Result & targetArrested) != 0)
        {
            InterrogateSuspect(local);
        }
    }
}
