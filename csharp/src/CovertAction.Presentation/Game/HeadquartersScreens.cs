using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The CIA's building in the city the agent is in: the floors of the data section, the intelligence section and the
/// crypto branch, and the chief's advice on whom and where to look at next.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="shared">The hand-off block: the coded message waiting after a mission, and the message the player chose to decode.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="plot">The passing of time and the plot's progress.</param>
/// <param name="dataFiles">The menus of the data files.</param>
/// <param name="investigation">The searches of the intelligence section.</param>
public sealed class HeadquartersScreens(CampaignState state, SharedState shared, Boundaries world, CampaignScreens screens, PlotEngine plot, DataFilesMenus dataFiles, Investigation investigation)
{
    /// <summary>The hand-off's code of the part the agent comes back from with a message to decode (entry_mode), and of the part that decodes it (exit_mode).</summary>
    private const int DecodeMessage = 2;

    /// <summary>The CIA among the organisations: its building is the headquarters.</summary>
    private const int PlayerAgency = 1;

    /// <summary>What a box returns when the player backs out.</summary>
    private const int BackOut = -1;

    /// <summary>The floors of the building, in the order of its menu (the fourth line leaves).</summary>
    private enum Floor
    {
        Data,
        Intelligence,
        Crypto,
        Leave,
    }

    /// <summary>The lines of the intelligence section's menu.</summary>
    private enum IntelligenceChoice
    {
        LocalScan,
        InternationalScan,
        WireTaps,
        AccuseDoubleAgent,
        CheckWithSam,
    }

    /// <summary>The lines of the data section's menu.</summary>
    private enum DataChoice
    {
        Clues,
        Suspects,
        InsideInformation,
        News,
        OrganizationSummary,
        CitySummary,
        ActivityReports,
    }

    /// <summary>The lines of the crypto branch's menu.</summary>
    private enum CryptoChoice
    {
        CodedMessages,
        CrimeChronology,
    }

    /// <summary>The strength of a local scan and of each of the four searches of an international scan.</summary>
    private const int LocalScanStrength = 0x64, InternationalScanStrength = 0x32, InternationalSearches = 4;

    /// <summary>Time the searches take, in minutes: a local scan, one search of an international scan, the visit after a mission's message.</summary>
    private const int LocalScanTime = 0xf0, InternationalSearchTime = 0x3c, HalfHour = 0x1e;

    /// <summary>The searches of a confessing double agent's files.</summary>
    private const int FilesSearches = 3;

    /// <summary>Where the boxes of the sections go.</summary>
    private const int FloorBoxX = 8, SectionBoxX = 0x14, BoxY = 0x15, NoInformationBoxY = 0x29, ConfessionBoxY = 0x14;

    /// <summary>
    /// The minutes the CIA building charges on the way back from the cryptography workstation: half an hour per unit
    /// of the hand-off's second result. The workstation never writes that result, so the original charges whatever
    /// the last building raid or car chase left there (CA-7); with <see cref="OriginalBehaviour.FixDecodeVisitLeftoverCharge"/>
    /// nothing, the workstation's own minutes having been charged on entry.
    /// </summary>
    public static int DecodeVisitCharge(OriginalBehaviour behaviour, int result) =>
        // DIVERGENCE: D-650 a player's option: CA-7, no charge from a leftover result
        behaviour.FixDecodeVisitLeftoverCharge ? 0 : HalfHour * result;

    /// <summary>
    /// The CIA building's menu, which a visit comes to after the arrival. When the part was entered back from a
    /// mission with a coded message waiting, the decoded message is shown first and the mission's half hours pass. Then
    /// the player picks a floor, over and over, until he leaves, a hand-off to another part is pending (a message to
    /// decode in the crypto branch) or the office is closed to him.
    /// </summary>
    // orig: GAME 00f7:6458 cia_building_menu (src/c/game/game_00f7_6458.c) matched; tests: GameOracleTests
    public void CiaBuildingMenu()
    {
        if (state.EntryMode == DecodeMessage)
        {
            if (shared.Value != 0)
            {
                dataFiles.CodedMessagesMenu();
                screens.Messages.ShowMessageDecode(shared.Person);
            }
            plot.PassTime(DecodeVisitCharge(state.Behaviour, shared.Result));
            state.EntryMode = 0;
        }
        while (true)
        {
            screens.ClearScreen();
            state.Txt.Text = state.HeadquartersWords.FloorMenu;
            var floor = plot.PopupMenuOnCleanScreen(state.Txt.Text, FloorBoxX, BoxY);
            switch ((Floor)floor)
            {
                case (Floor)BackOut or Floor.Leave:
                    return;
                case Floor.Intelligence:
                    if (IntelligenceSection())
                    {
                        return;
                    }
                    break;
                case Floor.Data:
                    DataSection();
                    break;
                case Floor.Crypto:
                    CryptoBranch();
                    break;
            }
            // a hand-off to another part ends the visit
            if (state.Quit != 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The intelligence section: scans of the city and of the world, the active wire taps, the accusation of the
    /// city's agent as a double agent, and a talk with the chief. Comes back to its menu until the player backs out.
    /// </summary>
    /// <returns>True when the accusation was wrong: the office is closed to the player, and he is out of the building.</returns>
    private bool IntelligenceSection()
    {
        var words = state.HeadquartersWords;
        int choice;
        do
        {
            world.Graphics.SetColorMaps(state.IntelligenceColorMap, state.IntelligenceColorMap);
            screens.Menus.StartAnimation(words.IntelligenceScene, 0xa8, 0);
            state.Txt.Text = words.IntelligenceMenu;
            choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, SectionBoxX, BoxY);
            switch ((IntelligenceChoice)choice)
            {
                case IntelligenceChoice.LocalScan:
                    LocalScan();
                    break;
                case IntelligenceChoice.InternationalScan:
                    InternationalScan();
                    break;
                case IntelligenceChoice.WireTaps:
                    dataFiles.ShowWiretaps();
                    break;
                case IntelligenceChoice.AccuseDoubleAgent:
                    if (!AccuseDoubleAgent())
                    {
                        return true;
                    }
                    break;
                case IntelligenceChoice.CheckWithSam:
                    ChiefAdvice();
                    break;
            }
        }
        while (choice != BackOut);
        return false;
    }

    /// <summary>A full search of the city the agent is in; four hours pass. A box says when it found nothing.</summary>
    private void LocalScan()
    {
        if (investigation.SearchYield(PlayerAgency, state.InCity, LocalScanStrength, 0) == 0)
        {
            screens.Menus.ClearAnimationArea();
            plot.PopupMenuOnCleanScreen(state.HeadquartersWords.NoNewInformationLocal, SectionBoxX, NoInformationBoxY);
        }
        plot.PassTime(LocalScanTime);
    }

    /// <summary>Four weaker searches, each of a city picked at random and an hour long. A box says when they found nothing.</summary>
    private void InternationalScan()
    {
        const int cities = 0x10;
        var found = 0;
        for (var search = 0; search < InternationalSearches; search++)
        {
            plot.PassTime(InternationalSearchTime);
            found += investigation.SearchYield(PlayerAgency, GameLibrary.RandomBelow(state.Random, cities), InternationalScanStrength, 0);
        }
        if (found == 0)
        {
            screens.Menus.ClearAnimationArea();
            plot.PopupMenuOnCleanScreen(state.HeadquartersWords.NoNewInformationWorld, SectionBoxX, NoInformationBoxY);
        }
    }

    /// <summary>
    /// The player accuses the agent of the city he is in. If the city is one of the double agents', the agent confesses,
    /// is replaced, and his files are searched (up to three times, until something is found). Otherwise the agent is
    /// innocent and the player may not use the office again.
    /// </summary>
    /// <returns>True when the agent was a double agent; false when the player is barred from the office.</returns>
    private bool AccuseDoubleAgent()
    {
        var words = state.HeadquartersWords;
        // C: a shift by a variable count of a 16-bit int
        var cityBit = C16.Shl(1, state.InCity);
        if ((state.Doubles & cityBit) == 0)
        {
            state.Txt.Text = words.AgentInnocent;
            plot.PopupMenuOnCleanScreen(state.Txt.Text, SectionBoxX, ConfessionBoxY);
            CampaignLibrary.RemoveLink(state.Lc, state.Nloc, state.Ct, PlayerAgency, state.InCity);
            return false;
        }
        state.Txt.Text = words.AgentConfesses;
        state.Txt.Text += words.SearchHisFiles;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, SectionBoxX, ConfessionBoxY);
        state.Doubles = C16.Int(state.Doubles - cityBit);
        for (var search = 0; search < FilesSearches; search++)
        {
            if (investigation.SearchYield(PlayerAgency, state.InCity, LocalScanStrength, 0) != 0)
            {
                break;
            }
        }
        return true;
    }

    /// <summary>The data section: the clues, the suspects, the inside information, the news, the organisation and city summaries and the activity reports.</summary>
    private void DataSection()
    {
        var words = state.HeadquartersWords;
        int choice;
        do
        {
            world.Graphics.SetColorMaps(state.DataSectionColorMap, state.DataSectionColorMap);
            screens.Menus.StartAnimation(words.DataScene, 0xa8, 0);
            state.Txt.Text = words.DataMenu;
            state.Txt.Text += words.DataMenuEnd;
            choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, SectionBoxX, BoxY);
            switch ((DataChoice)choice)
            {
                case DataChoice.Clues:
                    screens.ClueLists.ShowClues(-1);
                    break;
                case DataChoice.Suspects:
                    dataFiles.SuspectsMenu();
                    break;
                case DataChoice.InsideInformation:
                    dataFiles.DocumentsMenu();
                    break;
                case DataChoice.News:
                    screens.ShowChronology(false);
                    break;
                case DataChoice.OrganizationSummary:
                    dataFiles.OrganizationSummary();
                    break;
                case DataChoice.CitySummary:
                    dataFiles.CityFilesMenu();
                    break;
                case DataChoice.ActivityReports:
                    screens.ShowActivityReport();
                    break;
            }
        }
        while (choice != BackOut);
    }

    /// <summary>
    /// The crypto branch: the coded messages (not while coming back from a mission), whose choice hands the player over
    /// to the decoding part, and the chronology of the crime. Leaves when the player backs out or a hand-off is pending.
    /// </summary>
    private void CryptoBranch()
    {
        var words = state.HeadquartersWords;
        int choice;
        do
        {
            world.Graphics.SetColorMaps(state.CryptoColorMap, state.CryptoColorMap);
            screens.Menus.StartAnimation(words.CryptoScene, 0xa8, 0);
            state.Txt.Text = words.CryptoMenu;
            choice = plot.PopupMenuOnCleanScreen(state.Txt.Text, SectionBoxX, BoxY);
            switch ((CryptoChoice)choice)
            {
                case CryptoChoice.CodedMessages:
                    if (state.EntryMode == 0)
                    {
                        const int noMessage = -1;
                        shared.Person = noMessage;
                        dataFiles.CodedMessagesMenu();
                        if (shared.Person != noMessage)
                        {
                            state.Quit = DecodeMessage;
                        }
                    }
                    break;
                case CryptoChoice.CrimeChronology:
                    screens.ShowChronology(true);
                    break;
            }
        }
        while (choice != BackOut && state.Quit == 0);
    }

    /// <summary>
    /// The chief's advice ("Max, I've been going over the info we've got so far ..."): the organisations and cities
    /// busiest in the case's clues; at the easiest level, in the first city, a hint at the cast's first member; else
    /// an intercepted message that still needs decoding, or the best-evidenced person known to be in his city.
    /// </summary>
    // orig: GAME 00f7:9e58 chief_advice (src/c/game/game_00f7_9e58.c) matched; tests: CampaignTests
    public void ChiefAdvice()
    {
        var words = state.HeadquartersWords;
        world.Graphics.SetColorMaps(state.ChiefColorMap, state.ChiefColorMap);
        var female = (state.Status & Options.Female) != 0;
        screens.Menus.StartAnimation(female ? words.OfficeSceneOfFemaleAgent : words.OfficeSceneOfMaleAgent, 0xa8, 0);
        screens.Animation.SetScriptVar(0, female ? -1 : 1);
        state.Txt.Text = words.GoingOver;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x11);
        var hinted = HintedPerson();
        ListBusiestOrganizations(hinted);
        ListBusiestCities(hinted);
        if (hinted != NoPerson && !HintedBuildingIsTapped(hinted))
        {
            SendToTheHintedBuilding(hinted);
            return;
        }
        var (pick, score) = FindUndecodedMessage();
        pick = FindBestPerson(pick, score);
        state.Menu.NoHi = true;
        if (pick > NoPerson)
        {
            AdviseOnPerson(pick);
        }
        if (pick == UndecodedMessage)
        {
            LibraryTexts.WrapTextBuffer(state.Txt, 0x10, state.LineEnd);
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x51);
        }
        if (pick == NoPerson)
        {
            plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x51);
        }
    }

    /// <summary>No person picked.</summary>
    private const int NoPerson = -1;

    /// <summary>The pick of the advice when it is the undecoded message, not a person.</summary>
    private const int UndecodedMessage = -2;

    /// <summary>The most an organisation's or a city's counter has to stand back from the busiest for the chief to name it, as a share of the busiest (a quarter).</summary>
    private const int NamedShare = 4;

    /// <summary>The counters count at least this much for the busiest, so that a quiet case names everyone who has any.</summary>
    private const int LeastBusiest = 3;

    /// <summary>The organisations and cities of the world.</summary>
    private const int Places = 0x10;

    /// <summary>
    /// The member of the cast the chief hints at: the first, at the easiest level, in the first city, if he is not under
    /// arrest or in hiding.
    /// </summary>
    /// <returns>The person, or <see cref="NoPerson"/>.</returns>
    private int HintedPerson()
    {
        const int firstCity = 1;
        var first = state.Ac[0].Who;
        if (state.Diff != 0 || state.Ps[first].Alert < 0 || state.InCity != firstCity)
        {
            return NoPerson;
        }
        return first;
    }

    /// <summary>
    /// Starts the chief's speech with the organisations whose counter is within a quarter of the busiest (and the
    /// hinted person's organisation); the text buffer is left empty when none is named.
    /// </summary>
    private void ListBusiestOrganizations(int hinted)
    {
        var words = state.HeadquartersWords;
        var busiest = Math.Max(LeastBusiest, (int)state.OrgCounters.Max());
        var named = false;
        state.Txt.Text = words.LookIntoOrganizations;
        for (var org = 0; org < Places; org++)
        {
            if (busiest - busiest / NamedShare <= state.OrgCounters[org] || (hinted != NoPerson && state.Ps[hinted].Org == org))
            {
                state.Txt.Text += words.OrganizationIndent;
                state.Txt.Text += state.Orgs[org].Name;
                state.Txt.Text += words.OrganizationEnd;
                named = true;
            }
        }
        if (!named)
        {
            state.Txt.Text = "";
        }
    }

    /// <summary>
    /// Goes on with the cities whose counter is within a quarter of the busiest (and the first city for a hinted
    /// person). When none is named, the heading is taken back, and a speech with nothing in it is "not much to go on".
    /// </summary>
    private void ListBusiestCities(int hinted)
    {
        const int firstCity = 1;
        var words = state.HeadquartersWords;
        var busiest = Math.Max(LeastBusiest, (int)state.Active.Max());
        var named = false;
        var organizationsEnd = state.Txt.Length;
        state.Txt.Text += words.ActiveCities;
        for (var city = 0; city < Places; city++)
        {
            if (busiest - busiest / NamedShare <= state.Active[city] || (hinted != NoPerson && city == firstCity))
            {
                state.Txt.Text += state.Ct[city].Name;
                state.Txt.Text += words.CityEnd;
                named = true;
            }
        }
        if (!named)
        {
            // the heading is cut off again
            state.Txt.Text = state.Txt.Text[..organizationsEnd];
            if (organizationsEnd == 0)
            {
                state.Txt.Text = words.NotMuchToGo;
            }
        }
    }

    /// <summary>
    /// Whether the building of the hinted person's organisation in the first city is already wiretapped: the player has
    /// been told about it. Looks the building up, and finds it when it is not in the player's list yet.
    /// </summary>
    private bool HintedBuildingIsTapped(int hinted)
    {
        const int firstCity = 1, foundByChief = 0xc;
        var link = BuildingGlue.FindOrAddLink(state, state.Ps[hinted].Org, firstCity, foundByChief);
        return link != -1 && state.Lc[link].Tapped != 0;
    }

    /// <summary>The chief tells the player where the hinted person's organisation has a building in the city, to wiretap or break into.</summary>
    private void SendToTheHintedBuilding(int hinted)
    {
        const int firstCity = 1;
        var words = state.HeadquartersWords;
        state.Menu.NoHi = true;
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x51);
        screens.Menus.ClearAnimationArea();
        state.Txt.Text = words.TheOrganization;
        state.Txt.Text += state.Orgs[state.Ps[hinted].Org].Name;
        state.Txt.Text += words.HasA;
        BuildingGlue.AppendBuildingType(state, firstCity, state.Ps[hinted].Org);
        state.Txt.Text += words.RightHereIn;
        state.Txt.Text += state.Ct[state.InCity].Name;
        state.Txt.Text += words.SuggestGoThere;
        LibraryTexts.WrapTextBuffer(state.Txt, 0x10, state.LineEnd);
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x10, 0x11);
    }

    /// <summary>
    /// Looks through the intercepted messages for one the player has not decoded yet, of an event that is a coded message
    /// or has no event, and whose recipient is not yet identified; the chief's speech is then to look at it.
    /// </summary>
    /// <returns>The pick (<see cref="UndecodedMessage"/> when a message was found) and its score (6).</returns>
    private (int Pick, int Score) FindUndecodedMessage()
    {
        const int kindBits = 0xf00, codedMessage = 0x200, coded = 0x300, decoded = 0x8000, known = 0x7fff, messageScore = 6;
        var (pick, score) = (NoPerson, 0);
        for (var message = 0; message < state.NextSyn; message++)
        {
            var log = state.Sn[message];
            var eventOfMessage = log.Data;
            // a message of no event (a negative index) is counted whatever the event table in front of it holds
            if (log.Type != 0 || log.Clues == 0
                || !(eventOfMessage < 0 || (state.Ev[eventOfMessage].Status & kindBits) is codedMessage or coded)
                || (log.Clues & decoded) != 0)
            {
                continue;
            }
            var recipient = log.Who;
            if ((state.Ps[recipient].Rknown & known) != 0)
            {
                continue;
            }
            var words = state.HeadquartersWords;
            state.Txt.Text = words.VitalInfo;
            CampaignTexts.AppendPersonName(state, recipient);
            state.Txt.Text += words.NameSpace;
            CampaignTexts.AppendMessageText(state, message);
            state.Txt.Text += words.CheckWithCrypto;
            (pick, score) = (UndecodedMessage, messageScore);
        }
        return (pick, score);
    }

    /// <summary>
    /// Rates every person who was spotted in his city and whose role is known by what the player has of him: a photograph,
    /// the organisation, being in this very city and evidence count; a person not in hiding or arrested who beats the
    /// score so far becomes the pick.
    /// </summary>
    /// <returns>The pick: the person, <see cref="UndecodedMessage"/> or <see cref="NoPerson"/>.</returns>
    private int FindBestPerson(int pick, int score)
    {
        const int hasPhoto = 1, knowsOrganization = 4, spottedInCity = 8, known = 0x7fff, evidenceWeight = 5, inCityWeight = 2;
        for (var person = 0; person < state.Nperson; person++)
        {
            var who = state.Ps[person];
            if ((who.Clues & spottedInCity) == 0 || (who.Role & known) == 0 || who.Alert < 0)
            {
                continue;
            }
            var rating = 0;
            rating += (who.Clues & hasPhoto) != 0 ? 1 : 0;
            rating += (who.Clues & knowsOrganization) != 0 ? 1 : 0;
            rating += who.Loc == state.InCity ? inCityWeight : 0;
            rating += (who.Rknown & known) != 0 ? evidenceWeight : 0;
            if (rating > score)
            {
                (pick, score) = (person, rating);
            }
        }
        return pick;
    }

    /// <summary>
    /// The advice on a person: his dossier, whether there is enough evidence to arrest him or he is to be checked out, where
    /// he is, and one of the prose texts on what to do (by whether his organisation has a building here that the player knows).
    /// </summary>
    private void AdviseOnPerson(int person)
    {
        const int known = 0x7fff, knowsOrganization = 4, hasPhotoOrName = 3, noVariant = 0;
        var words = state.HeadquartersWords;
        var who = state.Ps[person];
        screens.DataFiles.DrawPersonDossier(person, 0, 0);
        state.Txt.Text = (who.Rknown & known) != 0 ? words.EnoughEvidence : words.CheckOutPerson;
        if (who.Loc == state.InCity)
        {
            state.Txt.Text += (who.Face & 1) != 0 ? words.He : words.She;
            state.Txt.Text += words.RightHereInCity;
            state.Txt.Text += state.Ct[state.InCity].Name;
            state.Txt.Text += words.FullStop;
            // C: the prose texts' tags do not end in '0' and the ones without a replacement have no "$RPLC"
            // (the original passes no variant, nor, in two cases, a replacement)
            if ((who.Clues & knowsOrganization) != 0)
            {
                if (BuildingGlue.FindOrAddLink(state, who.Org, state.InCity, 0) != -1)
                {
                    screens.LoadProseText(who.Rknown != 0 ? words.AdviceKnownKey : words.AdviceBuildingKey, 0x20, state.Orgs[who.Org].Name, noVariant);
                }
                else
                {
                    screens.LoadProseText(words.AdviceNoBuildingKey, 0x20, state.Orgs[who.Org].Name, noVariant);
                }
            }
            else
            {
                screens.LoadProseText((who.Clues & hasPhotoOrName) != 0 ? words.AdviceSeenKey : words.AdviceUnseenKey, 0x20, "", noVariant);
            }
        }
        else
        {
            screens.LoadProseText(words.AdviceElsewhereKey, 0x20, state.Ct[who.Loc].Name, noVariant);
        }
        plot.PopupMenuOnCleanScreen(state.Txt.Text, 0x18, 0x68);
    }
}
