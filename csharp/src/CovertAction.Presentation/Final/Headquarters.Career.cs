using CovertAction.Core.Final;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Final;

/// <summary>The agent's career: its end.</summary>
public sealed partial class Headquarters
{
    /// <summary>The tunes of the menus part: the briefing's theme and the end of a scene.</summary>
    private const int ThemeTune = 2, SceneEndTune = 1;

    /// <summary>The archive a vacation's pictures are read from while it is open; none.</summary>
    private const int NoCatalog = -1;

    /// <summary>
    /// The vacation after a case: the row <paramref name="ratingRow"/> of the vacation table (by how well the case went)
    /// gives the place (the LaundroMat, the secretarial pool, the beach, the casino) and up to four people who come
    /// along. With <paramref name="showCaption"/> the place's caption is shown first. The pictures come from the place's
    /// archive: the background, the agent's hand (Maxine's or Max's), sometimes a man in the first two places, then the
    /// people (men for Maxine, women for Max) one by one, each laid between the background and the hand and seeping
    /// onto the screen.
    /// </summary>
    /// <param name="ratingRow">The row of the vacation table, 0..20.</param>
    /// <param name="showCaption">Not 0 to show the place's caption first.</param>
    // orig: FINAL 0100:3bcc post_case_vacation (src/c/final/final_0100_3bcc.c) matched; tests: FinalOracleTests
    public void PostCaseVacation(int ratingRow, int showCaption)
    {
        const int beach = 2, firstPeople = 1, lastPeople = 4, workPage = 2, placeSounds = 0x28;
        var texts = _state.VacationTexts;
        var row = _state.VacationTable[ratingRow];
        int place = row[0];
        if (showCaption != 0)
        {
            const int captionX = 0x40, captionY = 0x50, captionColumns = 0x18, captionFrames = 0x78;
            _world.Graphics.FillRect(_state.Rp1, 0, 0, ScreenWidth, ScreenHeight, 0);
            if (place is >= 0 and < FinalState.VacationPlaces)
            {
                _state.Txt.Text = texts[(int)VacationText.LaundromatCaption + place];
            }
            LibraryTexts.WrapTextBuffer(_state.Txt, captionColumns, _state.StrNewline);
            _state.MenuDrawOnly = true;
            Menus.QuitPrompt(_state.Txt, captionX, captionY);
            Output.WaitVsync(captionFrames);
        }
        // the first two places' pictures are on disk 4, the others' on disk 3
        var nearHome = place < beach;
        Menus.CheckForDisk(nearHome ? 4 : 3, 1);
        OpenCatalog(texts[(int)(nearHome ? VacationText.Archive4 : VacationText.Archive3)]);
        var work = _state.WorkPortX;
        work.PageNr = workPage;
        var graphics = _world.Graphics;
        graphics.BeginImageCuts();
        var images = _state.VacationImages;
        var prefix = _state.VacationPlaceNames[place];
        images[FinalState.VacationBackground] = LoadVacationPicture(prefix + texts[(int)VacationText.Background]);
        var hand = _state.Status.HasFlag(Options.Female) ? VacationText.FemaleHand : VacationText.MaleHand;
        images[FinalState.VacationHand] = LoadVacationPicture(prefix + texts[(int)VacationText.Hand] + texts[(int)hand]);
        Output.DoSound(place + placeSounds);
        images[FinalState.VacationExtra] = null;
        if (nearHome && CaseSetup.Random(_state, 3) != 0)
        {
            images[FinalState.VacationExtra] = LoadVacationPicture(texts[(int)VacationText.ExtraMan]);
        }
        graphics.DrawImagePort(work, 0, 0, images[FinalState.VacationBackground]);
        // C: "no image" is the word 0, which an image whose cut found no memory is too
        if (images[FinalState.VacationExtra] is { } extra)
        {
            graphics.DrawImagePort(work, 0, 0, extra);
        }
        graphics.DrawImagePort(work, 0, 0, images[FinalState.VacationHand]);
        graphics.CopyRect(work, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
        for (var layer = firstPeople; layer <= lastPeople; layer++)
        {
            if (row[layer] == -1)
            {
                continue;
            }
            var people = _state.Status.HasFlag(Options.Female) ? VacationText.Dude : VacationText.Babe;
            _state.DigitsVacation.Text = row[layer].ToString(System.Globalization.CultureInfo.InvariantCulture);
            images[FinalState.VacationHand + layer] = LoadVacationPicture(prefix + texts[(int)people] + _state.DigitsVacation.Text + texts[(int)VacationText.PictureEnd]);
            graphics.DrawImagePort(work, 0, 0, images[FinalState.VacationBackground]);
            for (var behind = firstPeople; behind <= layer; behind++)
            {
                graphics.DrawImagePort(work, 0, 0, images[FinalState.VacationHand + behind]);
            }
            graphics.DrawImagePort(work, 0, 0, images[FinalState.VacationHand]);
            graphics.SubGame014b(workPage);
            graphics.CopyRect(work, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
            graphics.ShowPage(0);
        }
        graphics.EndImageCuts();
        CloseCatalog();
        // DIVERGENCE: D-100 the original gives the background's memory back to DOS (_dos_freemem); images are objects here.
    }

    /// <summary>Loads a picture of the vacation onto the work page (into the text buffer goes its name) and cuts it into an image.</summary>
    private Image? LoadVacationPicture(string name)
    {
        const int workPage = 2;
        _state.Txt.Text = name;
        Pictures.LoadPictureSwapped(_state.Txt.Text, workPage);
        return _world.Graphics.CutImage(workPage, 0, 0, ScreenWidth, ScreenHeight);
    }

    /// <summary>Opens an archive of pictures, from which the picture loader takes them while it is open.</summary>
    /// <param name="name">The archive's file name.</param>
    /// <returns>Whether it could be opened.</returns>
    // orig: FINAL 0100:c5d8 open_catalog (src/c/final/final_0100_c5d8.c) matched; tests: FinalOracleTests
    // FINAL's own routine: other code than the library's open_archive (LibraryPictures.OpenArchive), which it resembles.
    public bool OpenCatalog(string name)
    {
        // DOS's access mode in the low byte (0: to read); the high bit is the C library's, DOS does not see it
        const int readOnly = 0x8000;
        if (_world.Files.DosOpen(name, readOnly, out var handle) != 0)
        {
            return false;
        }
        Pictures.ArchiveHandle = handle;
        return true;
    }

    /// <summary>Closes the open archive of pictures, if there is one; a failure to close it ends the program.</summary>
    // orig: FINAL 0100:c608 close_catalog (src/c/final/final_0100_c608.c) matched; tests: FinalOracleTests
    public void CloseCatalog()
    {
        var handle = Pictures.ArchiveHandle;
        if (handle == NoCatalog)
        {
            return;
        }
        // closed as a file of its own, which the library's close does with the same last resort on a failure
        Pictures.ArchiveHandle = NoCatalog;
        Pictures.CloseResource(handle);
    }

    /// <summary>
    /// The career summary: "The Career of Max '(code name)' Remington", then a few lines for every case of the career
    /// record: its number and date, the arrests and efficiency points, the crime prevented or not stopped, and the
    /// MasterMind arrested in it; a page holds what fits and waits for a key. Then the totals of MasterMinds and agents
    /// arrested and, when there are any, a gallery of the arrested MasterMinds' faces (which counts them).
    /// </summary>
    /// <remarks>
    /// A case's record is four bytes, five when a MasterMind was arrested: the plot and its variant (four bits each),
    /// how the case ended (the arrests in the low four bits, 0x40 the crime carried out, 0x80 a MasterMind arrested),
    /// the efficiency points (high byte, low byte), and the MasterMind. The record ends with 0xff.
    /// </remarks>
    // orig: FINAL 0100:35d6 show_career_summary (src/c/final/final_0100_35d6.c) matched; tests: FinalOracleTests
    public void ShowCareerSummary()
    {
        const int titleField = 1, firstYear = 0x7c6, endOfRecords = 0xff, lastRecordStart = 0x100, pageBottom = 0xb4;
        const int arrestsBits = 0xf, crimeCarriedOut = 0x40, mastermindArrested = 0x80, lineWidth = 0x12c;
        var texts = _state.CareerTexts;
        var txt = _state.Txt;
        var records = _state.Pl.History;
        Screens.DrawScreenFrame(texts[(int)CareerText.Title], titleField);
        Text.DrawTextCentered(texts[(int)CareerText.CareerOf], 0xa0, 0x10, 0);
        txt.Text = texts[(int)CareerText.BeforeCodeName] + _state.Pl.Handle + texts[(int)CareerText.AfterCodeName];
        Text.DrawTextCentered(txt.Text, 0xa0, 0x18, 0);
        int at = 0, caseNo = 0, agents = 0, masterminds = 0, y = 0x24;
        // (the original adds the cases' points up as well, and shows the sum nowhere)
        while (records[at] != endOfRecords && at < lastRecordStart)
        {
            var plot = _state.Plots[records[at] >> 4];
            var variant = records[at] & 0xf;
            var outcome = records[at + 1];
            var arrests = outcome & arrestsBits;
            agents += arrests;
            // C: the points are a 16-bit int: from 32768 on they are written as negative
            var points = (short)((records[at + 2] << 8) + records[at + 3]);
            txt.Text = texts[(int)CareerText.Case] + CareerNumber(caseNo + 1) + texts[(int)CareerText.BeforeMonth]
                + _state.MonthNames[caseNo % FinalState.MonthCount] + texts[(int)CareerText.BeforeYear] + CareerNumber(caseNo / 12 + firstYear)
                + texts[(int)CareerText.Arrests] + CareerNumber(arrests) + texts[(int)CareerText.Points] + CareerNumber(points);
            Output.DrawText(txt.Text, 0x10, y, 0);
            y += 8;
            txt.Text = texts[(int)((outcome & crimeCarriedOut) != 0 ? CareerText.CouldNotStop : CareerText.Prevented)]
                + _state.CrimeNames[plot.CrimeFile[variant]] + texts[(int)CareerText.Slash] + plot.ItemA[variant];
            Text.TrimTextToWidth(lineWidth);
            Output.DrawText(txt.Text, 0xc, y, 7);
            y += 8;
            if ((outcome & mastermindArrested) != 0)
            {
                txt.Text = texts[(int)CareerText.ArrestedMastermind] + _state.MastermindNames[records[at + 4]] + texts[(int)CareerText.MastermindEnd];
                at++;
                Output.DrawText(txt.Text, 0xc, y, 0xc);
                y += 8;
                masterminds++;
            }
            y += 2;
            at += 4;
            caseNo++;
            if (y > pageBottom)
            {
                Screens.WaitForKey();
                Screens.DrawScreenFrame(texts[(int)CareerText.NextPageTitle], titleField);
                y = 0x10;
            }
        }
        Output.DrawLine(0xc, y + 1, 0x12f, y + 1, 0);
        txt.Text = texts[(int)CareerText.TotalMasterminds] + CareerNumber(masterminds) + texts[(int)CareerText.TotalAgents] + CareerNumber(agents);
        Output.DrawText(txt.Text, 0xc, y + 4, 0);
        Screens.WaitForKey();
        if (_state.Pl.Bigs != 0)
        {
            ShowArrestedMasterminds();
            Screens.WaitForKey();
        }
    }

    /// <summary>
    /// The gallery of the career summary: every arrested MasterMind's face with his organisation's name, three to a row
    /// and each a little lower or higher than the one before, as many rows as fit a page. Counts the MasterMinds in
    /// <see cref="FinalState.MastermindsCaught"/>.
    /// </summary>
    private void ShowArrestedMasterminds()
    {
        const int titleField = 1, offPage = 0x3e7, columnStep = 0x48, rowStep = 0x50, stagger = 8, lastColumn = 0x100;
        const int firstColumn = 0x1c, firstRow = 0x20, lastRow = 0x8c, nameColour = 0xc, cgaNameColour = 0;
        var texts = _state.CareerTexts;
        // the first face starts a new row and a new page
        int x = offPage, y = offPage, mastermind = 0;
        var first = true;
        while (true)
        {
            if (!first)
            {
                x += columnStep;
                y ^= stagger;
                mastermind++;
            }
            first = false;
            while (mastermind < FinalState.MastermindCount && !AlreadyArrested(_state.Pl.Bigs, mastermind))
            {
                mastermind++;
            }
            if (mastermind >= FinalState.MastermindCount)
            {
                return;
            }
            _state.MastermindsCaught++;
            if (x > lastColumn)
            {
                x = firstColumn;
                y += rowStep;
            }
            if (y > lastRow)
            {
                if (y < offPage)
                {
                    Screens.WaitForKey();
                }
                Screens.DrawScreenFrame(texts[(int)CareerText.GalleryTitle], titleField);
                Text.DrawTextCentered(texts[(int)CareerText.Gallery], 0xa0, 0x10, 0);
                y = firstRow;
            }
            DrawMastermindFace(mastermind, x, y);
            Text.DrawTextCentered(_state.MastermindNames[mastermind], x + 0x19, y + 0x38, _state.CGA ? cgaNameColour : nameColour);
        }
    }

    /// <summary>A number as the career summary writes it out, through its digits buffer (the C library's itoa in base 10).</summary>
    private string CareerNumber(int value)
    {
        _state.DigitsCareer.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _state.DigitsCareer.Text;
    }

    /// <summary>
    /// The retirement screen, after all 26 MasterMinds were arrested or ten years were served: the agent's picture on
    /// one side (Max's on the left, Maxine's on the right), the name in the margin, the face of every MasterMind the
    /// agent arrested dropped at a random place with an "ARRESTED" stamp, half a second apart; then the retirement text
    /// the caller has put into the text buffer, in a box.
    /// </summary>
    // orig: FINAL 0100:3a49 retirement_screen (src/c/final/final_0100_3a49.c) matched; tests: FinalOracleTests
    public void RetirementScreen()
    {
        const int pictureWidth = 0xdf, nameTop = 0x56, lineHeight = 8, nameColour = 0xe, stampRed = 0xc;
        const int dropFrames = 0x1e, finalFrames = 0x78;
        Output.DoSound(ThemeTune);
        var female = _state.Status.HasFlag(Options.Female);
        // the half of the picture that is not the agent's is blanked; the faces fall on the agent's side
        var left = female ? 0 : 0x61;
        Pictures.LoadPictureSwapped(_state.RetirementPicture, PicturePage);
        _world.Graphics.FillRect(_state.RpBack, left, 0, pictureWidth, ScreenHeight, ScreenBlack);
        _world.Graphics.CopyRect(_state.RpBack, 0, 0, ScreenWidth, ScreenHeight, _state.Rp1, 0, 0);
        var nameX = female ? 0x104 : 0x38;
        Text.DrawTextCentered(_state.RetirementName[0], nameX, nameTop, nameColour);
        Text.DrawTextCentered(_state.Pl.Handle, nameX, nameTop + lineHeight, nameColour);
        Text.DrawTextCentered(_state.RetirementName[1], nameX, nameTop + 2 * lineHeight, nameColour);
        for (var mastermind = 0; mastermind < FinalState.MastermindCount; mastermind++)
        {
            if ((_state.Pl.Bigs >> mastermind & 1) != 0)
            {
                var x = CaseSetup.Random(_state, 0xa4) + 4;
                var y = CaseSetup.Random(_state, 0x8c) + 4;
                DrawMastermindFace(mastermind, x + left, y);
                // the stamp's black shadow, then the stamp a pixel up and to the left
                Output.DrawTextFlag0(_state.ArrestedStamp[0], x + left + 1, y + 0x18, 0);
                Output.DrawTextFlag0(_state.ArrestedStamp[1], x + left, y + 0x17, stampRed);
                Output.WaitVsync(dropFrames);
            }
        }
        Output.WaitVsync(finalFrames);
        MenuChoice(_state.Txt, left + 0x18, 0x40);
        Output.DoSound(SceneEndTune);
    }
}
