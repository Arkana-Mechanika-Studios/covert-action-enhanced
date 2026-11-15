using CovertAction.Core.Final;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Final;

/// <summary>The debriefing's action log: the case told again, record by record, under the roster of the cast.</summary>
public sealed partial class Headquarters
{
    /// <summary>The kinds of record of the case log (<see cref="Core.Records.Synopsis.Type"/>).</summary>
    private const int PlotEventRecord = 0, LeftCityRecord = 1, TravelRecord = 4, BreakInRecord = 5, ArrestRecord = 6, ConfiscationRecord = 7,
        HidingRecord = 8, OutOfHidingRecord = 9, EscapeRecord = 10, NothingRecord = 11, TurnedRecord = 12, ExchangeRecord = 13, UnusedRecord = -1;

    /// <summary>The roster's area at the top of the screen and the log's window below it, in pixels.</summary>
    private const int RosterHeight = 0x68, WindowTop = 0x68, WindowHeight = 0x60;

    /// <summary>The colours of the log: the roster's grey, the window's blue, its highlight, the text white.</summary>
    private const int RosterGrey = 7, WindowBlue = 1, WindowLight = 9, LogWhite = 0xf, LogCentre = 0xa0;

    /// <summary>How a record is shown: its text drawn and a key waited for, drawn already and a key waited for, or skipped.</summary>
    private enum LogPage
    {
        ShowText,
        Drawn,
        Skip,
    }

    /// <summary>
    /// The debriefing's action log: the roster of the cast (the MasterMind's cells darkened) and below it, one page per
    /// record of the case log, the time and what happened: the plot's events (who did what to whom, the messages and
    /// the objects that changed hands), the agent's travels, break-ins, arrests and confiscations, persons who went
    /// into hiding, came out, escaped or were exchanged, and those the agent turned. The roster shows each change. A page
    /// waits for a key; Esc ends the log.
    /// </summary>
    // orig: FINAL 0100:1da2 case_action_log (src/c/final/final_0100_1da2.c) matched; tests: FinalOracleTests
    public void CaseActionLog()
    {
        const int esc = 0x1b, timeY = 0x72, timeAfter = 9;
        var graphics = _world.Graphics;
        graphics.SetColorMaps(_state.PhotoColorMap, _state.PhotoColorMap);
        graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, RosterHeight, RosterGrey);
        _state.Rp1.BPen = WindowBlue;
        for (var cell = 0; cell < _state.NRoster; cell++)
        {
            DrawRosterCell(cell);
        }
        DarkenPersonCells(0);
        if (!_state.CGA)
        {
            graphics.RecolorRect(_state.Rp1, 0, 0, ScreenWidth, RosterHeight, WindowLight, RosterGrey);
        }
        graphics.FillRect(_state.Rp1, 0, WindowTop, ScreenWidth, WindowHeight, WindowBlue);
        _state.Rp1.BPen = WindowLight;
        for (var item = 0; item < PlotObjects; item++)
        {
            _state.Mc[item].Who = -1;
        }
        for (var entry = 0; entry < _state.NextSyn; entry++)
        {
            var record = _state.Sn[entry];
            if (record.Type == UnusedRecord)
            {
                continue;
            }
            ClearLogWindow();
            if (!_state.CGA)
            {
                graphics.RecolorRect(_state.Rp1, 0, 0, ScreenWidth, RosterHeight, WindowLight, WindowBlue);
            }
            // the headline: the day of the record (the clock's text from its month on)
            Text.DrawTextCentered(CaseReview.FormatGameTime(_state, record.Time, _behaviour)[timeAfter..], LogCentre, timeY, RosterGrey);
            var page = ShowLogRecord(record);
            if (page == LogPage.ShowText)
            {
                Text.TrimTextToWidth(0x12c);
                Text.DrawTextCentered(_state.Txt.Text, LogCentre, 0x90, LogWhite);
            }
            if (page != LogPage.Skip && Screens.WaitForKey() == esc)
            {
                break;
            }
        }
    }

    /// <summary>Clears the log's window below the roster (black on the four-colour screen).</summary>
    private void ClearLogWindow() => _world.Graphics.FillRect(_state.Rp1, 8, 0x70, 0x130, 0x50, _state.CGA ? 0 : WindowLight);

    /// <summary>Shows one record of the case log; the text buffer holds what is still to be drawn for <see cref="LogPage.ShowText"/>.</summary>
    private LogPage ShowLogRecord(Core.Records.Synopsis record)
    {
        var texts = _state.LogTexts;
        var txt = _state.Txt;
        int d = record.Data;
        switch (record.Type)
        {
            case PlotEventRecord:
                return ShowPlotEvent(record);
            case LeftCityRecord:
                // the person in the high byte, the city in the low byte
                txt.Text = texts[(int)LogText.LeftStart];
                AppendName(d >> 8);
                txt.Text += texts[(int)LogText.Left] + _state.Ct[d & 0xff].Name;
                return LogPage.ShowText;
            case TravelRecord:
                txt.Text = texts[(int)LogText.Traveled] + _state.Ct[d].Name;
                return LogPage.ShowText;
            case BreakInRecord:
                txt.Text = texts[(int)LogText.BrokeIn] + _state.Org[d >> 8].Name + texts[(int)LogText.BrokeInSlash] + _state.Ct[d & 0xff].Name;
                return LogPage.ShowText;
            case ArrestRecord:
                txt.Text = texts[(int)LogText.Arrested];
                AppendName(d);
                Text.DrawTextCentered(txt.Text, LogCentre, 0x90, LogWhite);
                DrawJailBars(d);
                return LogPage.Drawn;
            case ConfiscationRecord:
            {
                txt.Text = texts[(int)LogText.Confiscated] + _state.Mc[d].Name;
                var holder = _state.Mc[d].Who;
                _state.Mc[d].Who = -1;
                DrawHeldObjects(holder);
                // C: a shift by a variable count
                _state.ObjectsTakenMask |= (short)(1 << d);
                return LogPage.ShowText;
            }
            case HidingRecord:
                ShowHiding(d);
                return LogPage.Drawn;
            case OutOfHidingRecord or EscapeRecord or ExchangeRecord:
                txt.Text = "";
                AppendName(d);
                txt.Text += texts[(int)(record.Type switch
                {
                    OutOfHidingRecord => LogText.CameOutOfHiding,
                    EscapeRecord => LogText.EscapedFromJail,
                    _ => LogText.ReleasedByExchange,
                })];
                Text.DrawTextCentered(txt.Text, LogCentre, 0x90, LogWhite);
                RedrawPersonCells(d);
                return LogPage.Drawn;
            case NothingRecord:
                return LogPage.Skip;
            case TurnedRecord:
                txt.Text = texts[(int)LogText.Turned];
                AppendName(d);
                FramePersonCells(d);
                if (!_state.CGA)
                {
                    _world.Graphics.RecolorRect(_state.Rp1, 0, 0, ScreenWidth, RosterHeight, WindowLight, 2);
                }
                return LogPage.ShowText;
            default:
                // the other kinds show whatever the text buffer holds
                return LogPage.ShowText;
        }
    }

    /// <summary>
    /// A record of a plot event: unless the player learnt nothing of it, the person who acted (and his role, his
    /// organisation and city), what he did (to whom, with the message), then every object of the plot that changed hands
    /// in it, each on a page of its own, and the objects destroyed.
    /// </summary>
    private LogPage ShowPlotEvent(Core.Records.Synopsis record)
    {
        const int targetBits = 0xf00, revealedBit = 0x100, messageBit = 0x200, packageBit = 0x400, destroyedShift = 8;
        var texts = _state.LogTexts;
        var txt = _state.Txt;
        int d = record.Data;
        if (record.Clues == 0)
        {
            return LogPage.Skip;
        }
        LogPage page;
        // C: the event is looked at before its number is tested, which for a message of no event (-1) reads in front of the table
        var happening = _state.Ev[d];
        if ((happening.Status & revealedBit) == 0 || d < 0)
        {
            txt.Text = texts[(int)LogText.ActorStart];
            int actor = record.Who;
            AppendName(actor);
            if (d >= 0)
            {
                txt.Text += texts[(int)LogText.ActorRoleOpen] + _state.Ac[happening.Act].Name + texts[(int)LogText.ActorRoleClose];
                FrameRosterCell(happening.Act);
            }
            else
            {
                FramePersonCells(actor);
                txt.Text += texts[(int)LogText.ActorNoRole];
            }
            if (actor != 0)
            {
                txt.Text += _state.Org[_state.Ps[actor].Org].Name + texts[(int)LogText.ActorSlash] + _state.Ct[_state.Ps[actor].Loc].Name;
            }
            Text.TrimTextToWidth(0x12c);
            Text.DrawTextCentered(txt.Text, LogCentre, 0x7c, LogWhite);
            if (d < 0)
            {
                txt.Text = texts[(int)LogText.SentMessageTo];
                AppendName(record.Who2);
                Text.DrawTextCentered(txt.Text, LogCentre, 0x84, LogWhite);
                Output.DrawLine(0x14, 0x96, 0x12b, 0x96, RosterGrey);
                txt.Text = texts[(int)LogText.MessageOpen];
                ShowEventMessage(d);
                txt.Text += texts[(int)LogText.MessageClose];
                Text.DrawWrappedText(0x23, 0x14, 0x98, 0xe);
                FramePersonCells(record.Who2);
            }
            else
            {
                txt.Text = texts[(int)LogText.EventStart] + happening.Name;
                var kind = happening.Status;
                if ((kind & messageBit) != 0)
                {
                    txt.Text = texts[(int)LogText.EventMessage];
                }
                if ((kind & packageBit) != 0)
                {
                    txt.Text = texts[(int)LogText.EventPackage];
                }
                Text.DrawTextCentered(txt.Text, LogCentre, 0x84, LogWhite);
                if ((happening.Status & targetBits) != 0)
                {
                    ShowEventTarget(record, happening.Status, (kind & packageBit) == 0);
                }
            }
            page = LogPage.Drawn;
        }
        else
        {
            page = LogPage.Skip;
        }

        var holder = _state.Ac[happening.Act].Who;
        for (var item = 0; item < PlotObjects; item++)
        {
            // C: shifts by a variable count, 0..3 and 8..11
            if ((happening.Macguff & 1 << item) != 0)
            {
                if (page != LogPage.Skip)
                {
                    Screens.WaitForKey();
                }
                ClearLogWindow();
                var before = _state.Mc[item].Who;
                _state.Mc[item].Who = (sbyte)holder;
                DrawHeldObjects(before);
                DrawHeldObjects(holder);
                txt.Text = "";
                AppendName(holder);
                txt.Text += texts[(int)LogText.HolderRoleOpen] + _state.Ac[happening.Act].Name + texts[(int)LogText.HolderRoleClose];
                Text.DrawTextCentered(txt.Text, LogCentre, 0x8c, LogWhite);
                txt.Text = texts[(int)LogText.NowHas] + _state.Mc[item].Name;
                Text.DrawTextCentered(txt.Text, LogCentre, 0x94, LogWhite);
                page = LogPage.Drawn;
                _state.ObjectsSeenMask |= (short)(1 << item);
            }
            if ((happening.Macguff & 1 << (item + destroyedShift)) != 0)
            {
                var lost = _state.Mc[item].Who;
                _state.Mc[item].Who = -1;
                DrawHeldObjects(lost);
            }
        }
        return page;
    }

    /// <summary>The person a plot event was done to: name, role, organisation and city, the cell framed, and the event's message unless it was a package.</summary>
    private void ShowEventTarget(Core.Records.Synopsis record, int status, bool withMessage)
    {
        var texts = _state.LogTexts;
        var txt = _state.Txt;
        // the low byte of the event's status is the cast member it is done to
        var target = status & 0xff;
        txt.Text = texts[(int)LogText.TargetStart];
        int person = record.Who2;
        AppendName(person);
        txt.Text += texts[(int)LogText.TargetRoleOpen] + _state.Ac[target].Name + texts[(int)LogText.TargetRoleClose];
        if (person != 0)
        {
            txt.Text += _state.Org[_state.Ps[person].Org].Name + texts[(int)LogText.TargetSlash] + _state.Ct[_state.Ps[person].Loc].Name;
        }
        Text.TrimTextToWidth(0x12c);
        Text.DrawTextCentered(txt.Text, LogCentre, 0x8c, LogWhite);
        FrameRosterCell(target);
        Output.DrawLine(0x14, 0x96, 0x12b, 0x96, RosterGrey);
        if (withMessage)
        {
            txt.Text = texts[(int)LogText.EventMessageOpen];
            ShowEventMessage(record.Data);
            txt.Text += texts[(int)LogText.EventMessageClose];
            Text.DrawWrappedText(0x23, 0xc, 0x98, 0xe);
        }
    }

    /// <summary>A person went into hiding: whether the mission could still be completed, which it cannot while the person has an event of the plot still to do.</summary>
    private void ShowHiding(int person)
    {
        const short notYetHappened = 0x3e7;
        var texts = _state.LogTexts;
        var txt = _state.Txt;
        txt.Text = "";
        AppendName(person);
        txt.Text += texts[(int)LogText.WentIntoHiding];
        Text.DrawTextCentered(txt.Text, LogCentre, 0x8c, LogWhite);
        var pending = false;
        for (var e = 0; e < _state.NEVENT; e++)
        {
            if (_state.Ev[e].Chron == notYetHappened && _state.Ac[_state.Ev[e].Act].Who == person)
            {
                pending = true;
                break;
            }
        }
        if (!pending)
        {
            txt.Text = texts[(int)LogText.MissionCompleted];
        }
        else
        {
            txt.Text = texts[(int)LogText.UnableTo];
            Text.DrawTextCentered(txt.Text, LogCentre, 0x94, LogWhite);
            txt.Text = texts[(int)LogText.CompleteMission];
        }
        Text.DrawTextCentered(txt.Text, LogCentre, 0x9c, LogWhite);
        DarkenPersonCells(person);
    }

    /// <summary>Appends a person's name (the real one once identified) to the text buffer.</summary>
    private void AppendName(int person) => CampaignLibrary.AppendPersonName(_state.Txt, _state.Ps, _state.Org, _state.Names, person);
}
