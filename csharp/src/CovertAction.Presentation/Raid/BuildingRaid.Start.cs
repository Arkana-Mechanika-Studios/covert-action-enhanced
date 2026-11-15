using CovertAction.Core;
using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Raid;

/// <summary>The raid's start and end: what the hand-off block asks for, how the building is made ready, what is handed back.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>The "next part" codes that start the three special raids: the escape, the jailbreak and the ambush in the street.</summary>
    private const sbyte EscapeCode = 7, JailbreakCode = 8, AmbushCode = 0x0a;

    /// <summary>The "next part" code a part hands back at the end of a practice session: the menus.</summary>
    private const sbyte MenusPart = 0x11;

    /// <summary>"Leave the game": what the hand-off's next-part byte holds while the part runs.</summary>
    private const sbyte LeaveGame = -1;

    /// <summary>The agent's skills word: bits 0 and 1 are the quality of the kit (they lower the difficulty), bits 8 and 9 the game speed.</summary>
    private const int KitQualityMask = 3, GameSpeedShift = 8, GameSpeedMask = 3;

    /// <summary>The raid's difficulty runs from 0 to 4.</summary>
    private const int EasiestRaid = 0, HardestRaid = 4;

    /// <summary>Every 50 per cent of alertness times the campaign's level makes the raid one level harder.</summary>
    private const int AlertPerLevel = 0x32;

    private const int MinutesPerHour = 60, HoursPerDay = 24, NightEnds = 8, NightBegins = 20;

    /// <summary>The wait between the half-frames of the raid, in vertical retraces, for the game speeds 0 (slow), 1 and 2.</summary>
    private const int SlowWait = 6, MediumWait = 4, FastWait = 2;

    /// <summary>The pages pictures are loaded onto: the sprite sheet's, the raid screen's backdrop, and the driver's working page.</summary>
    private const int SheetPage = 2, BackdropPage = 1, DriverWorkPage = 3;

    /// <summary>The balloon sheet: the speech balloon's frame and the small picture beside it.</summary>
    private const int BalloonWidth = 0xc4, BalloonHeight = 0x70, BalloonPictureX = 0xc8, BalloonPictureWidth = 0x48, BalloonPictureHeight = 0x3d;

    /// <summary>The sound effect number that stops the sound.</summary>
    private const int SoundStop = 0;

    /// <summary>The font the part's text starts with.</summary>
    private const int MenuFont = 1;

    /// <summary>"No building record": what the library's building search answers when it finds none.</summary>
    private const int NoLocation = -1;

    private LibraryPictures? _pictures;
    private LibraryHandOff? _handOff;

    /// <summary>The library's routines that load pictures onto pages.</summary>
    public LibraryPictures Pictures => _pictures ??= new LibraryPictures(world);

    /// <summary>The library's routines that take the campaign over from the hand-off block and give it back, with the raid's own box for their errors.</summary>
    public LibraryHandOff HandOff => _handOff ??= new LibraryHandOff(state, shared, world, (text, x, y) => ShowMessageBox(text, x, y));

    /// <summary>
    /// One raid, from the hand-off block to the hand-off block: takes the campaign over, works out the kind of raid, its
    /// difficulty and the number of guards, finds the person the raid is about, loads the pictures, makes the building
    /// (the same one every time for the same city and organisation), lets the player choose the kit and the door, plays
    /// the raid frame by frame until it is over, and hands back its result.
    /// </summary>
    // orig: TAC 00e8:0018 tac_init (src/c/tac/tac_00e8_0018.c) matched; tests: TacOracleTests
    public void TacInit()
    {
        // DIVERGENCE: D-2 the three attach_driver_segment calls (TAC 0000:05b9) that patch the driver thunks are left out.
        ChooseRaidKind();
        shared.NextModule = LeaveGame;
        // C: the original passes its own copy of the name; the library compares it by text with the hand-off's
        HandOff.ReadEnvState(state.HandOff.StrEnvSveRead);
        SetDifficulty();
        FindTheTarget();
        SetTimeOfDay();
        state.FacesOn = true;
        SetGuardsLeft();

        // the building is made from the random numbers as they stand now, so the same building looks the same each time
        // C: the seed is an unsigned 16-bit number
        state.Random.Srand(unchecked((ushort)((state.InCity + 0xd) * (state.AtLoc + 7) * 0x17 + (state.TheSeed & 0x3fff))));
        state.PageBuffer = (short)world.Graphics.AllocPageBuffer(DriverWorkPage);
        world.Graphics.SetPageBuffer(DriverWorkPage, state.PageBuffer);
        state.Rp1.FontNr = MenuFont;
        state.Rp1.BPen = White;
        SetPalette(0);
        LoadBalloonPictures();
        LoadSpritesPic();
        LoadGuardPictures();
        GenerateBuilding();
        state.RandomSeed = LibraryRandom.SeedRandom(state.Random, world.Timer);
        ReadTextLine();
        FinishSetUp();
        PlayFrames();
        HandBack();
    }

    /// <summary>
    /// The kind of raid from the code the launcher started the part with: 7 (escape) 1, 8 (jailbreak) 2, 0xa (ambush) 3,
    /// anything else (a plain break-in) leaves the kind as the program has it (0); a practice session is always a break-in.
    /// </summary>
    private void ChooseRaidKind()
    {
        switch (shared.NextModule)
        {
            case EscapeCode:
                state.GameMode = RaidState.EscapeMode;
                break;
            case JailbreakCode:
                state.GameMode = RaidState.KillMode;
                break;
            case AmbushCode:
                state.GameMode = RaidState.OneRoomMode;
                break;
        }
        if (state.Status.HasFlag(Options.Practice))
        {
            state.GameMode = 0;
        }
    }

    /// <summary>
    /// The raid's difficulty: the campaign's level, one lower for each level of the agent's kit, one higher for the
    /// jailbreak, and one higher where the other side is alert (a raid in a campaign city); 0 to 4. The agent's game speed
    /// comes with the skills word.
    /// </summary>
    private void SetDifficulty()
    {
        state.GameSpeed = (short)(state.Skills >> GameSpeedShift & GameSpeedMask);
        state.CampaignDiff = state.Diff;
        state.Diff = (short)(state.Diff - (state.Skills & KitQualityMask) + 1);
        if (state.GameMode == RaidState.KillMode)
        {
            state.Diff++;
        }
        if (state.GameMode == 0)
        {
            state.SharedAlert = shared.Alert;
        }
        if (state.CampaignDiff != 0)
        {
            // C: 16-bit product and division
            state.Diff += (short)GameLibrary.Clamp(C16.Div(C16.Int(state.SharedAlert * state.CampaignDiff), AlertPerLevel), 0, 1);
        }
        else
        {
            state.Diff = 0;
        }
        state.Diff = (short)GameLibrary.Clamp(state.Diff, EasiestRaid, HardestRaid);
    }

    /// <summary>
    /// The person the raid is about: in a plain break-in the last person of the campaign at this organisation and city,
    /// unless he is out of the game (his alertness is below 0 but not -3); in the jailbreak the person the hand-off names.
    /// </summary>
    private void FindTheTarget()
    {
        const short NoOne = -1, Hidden = -3;
        state.Who = NoOne;
        if (state.GameMode == 0)
        {
            for (var p = 0; p < state.Nperson; p++)
            {
                if (state.Ps[p].Org == state.AtLoc && state.Ps[p].Loc == state.InCity)
                {
                    state.Who = (short)p;
                }
            }
        }
        // C: with nobody found the original looks at the record before the table; either way he stays nobody
        if (state.Who != NoOne && state.Ps[state.Who].Alert < 0 && state.Ps[state.Who].Alert != Hidden)
        {
            state.Who = NoOne;
        }
        if (state.GameMode == RaidState.KillMode)
        {
            state.Who = shared.Target;
        }
    }

    /// <summary>The hour of the campaign's clock, and whether it is night (8 in the evening to 8 in the morning), when more guards hunt.</summary>
    private void SetTimeOfDay()
    {
        // C: the clock is unsigned here
        state.Hour = (short)C16.URem(C16.UDiv(C16.Unsigned(state.Gtime), MinutesPerHour), HoursPerDay);
        state.Night = state.Hour < NightEnds || state.Hour >= NightBegins;
    }

    /// <summary>The number of guards that may appear: it depends on the kind of raid and on the difficulty.</summary>
    private void SetGuardsLeft()
    {
        state.GuardsLeft = (short)(state.Diff * 4 + 8);
        switch (state.GameMode)
        {
            case RaidState.KillMode:
                state.GuardsLeft = (short)(state.CampaignDiff * 2 + 4);
                break;
            case RaidState.OneRoomMode:
                state.GuardsLeft = (short)(state.CampaignDiff * 2 + 2);
                break;
            case RaidState.EscapeMode:
                state.GuardsLeft = (short)(3 * state.Diff + 3);
                break;
        }
    }

    /// <summary>Cuts the speech balloon and its small picture out of the balloon sheet.</summary>
    private void LoadBalloonPictures()
    {
        world.Graphics.BeginImageCuts();
        Pictures.LoadPictureSwapped(state.BalloonSheetName, SheetPage);
        state.BalloonImages[RaidState.BalloonBg] = world.Graphics.CutImage(SheetPage, 0, 0, BalloonWidth, BalloonHeight);
        state.BalloonImages[RaidState.BalloonPic] = world.Graphics.CutImage(SheetPage, BalloonPictureX, 0, BalloonPictureWidth, BalloonPictureHeight);
        world.Graphics.EndImageCuts();
    }

    /// <summary>
    /// Loads the sheet of the guards' pictures (the ambush has its own) onto the sprite sheet's page. The bottom 40 rows
    /// of that page hold the status panel's pieces from the sprite sheet, which the load would paint over: they are
    /// parked on the backdrop and put back.
    /// </summary>
    private void LoadGuardPictures()
    {
        world.Graphics.CopyRect(state.RpSprite, 0, StatusBarY, StatusBarWidth, StatusBarHeight, state.RpBack, 0, StatusBarY);
        Pictures.LoadPictureSwapped(state.GameMode != RaidState.OneRoomMode ? state.GuardSheetName : state.AmbushGuardSheetName, SheetPage);
        world.Graphics.CopyRect(state.RpBack, 0, StatusBarY, StatusBarWidth, StatusBarHeight, state.RpSprite, 0, StatusBarY);
    }

    /// <summary>
    /// The last steps before the first frame: the plot object's room, the rooms the agent saw on earlier raids, the
    /// screen's picture, the player's kit and door, the colours, and Num Lock on for the keypad.
    /// </summary>
    private void FinishSetUp()
    {
        // the plot object lies in the target room in a practice session, with person 0, in a city with a double, or
        // where the building's person holds it (furnish_room set MacHere)
        BuildingRules.SetUpThePlotObjectsGuards(state);

        // the rooms the agent has seen on earlier raids of this building show on the radar
        var location = LocationOfBuilding();
        if (location != NoLocation)
        {
            for (var room = 0; room <= state.Nroom; room++)
            {
                if ((state.Lc[location].Seen & 1u << room) != 0)
                {
                    state.Rm[room].Status |= RoomStatus.Visited;
                }
            }
        }
        Pictures.LoadPictureSwapped(state.Status.HasFlag(Options.Female) ? state.RaidScreenFemaleName : state.RaidScreenMaleName, BackdropPage);
        if (state.GameMode == 0)
        {
            world.Graphics.CopyRect(state.RpBack, 0, 0, ScreenWidth, ScreenHeight, state.Rp1, 0, 0);
        }
        Input.FlushInput();
        ChooseDoor();
        world.Graphics.SetColorMaps(state.CgaMapGame, state.CgaMapGame);
        // the keypad moves the agent: Num Lock on
        world.Input.SetNumLock(true);
        // DIVERGENCE: D-501 the original asks the video BIOS for the cursor's position here (INT 10h function 3) and uses no answer.
    }

    /// <summary>The record of this building (organisation at city) among the campaign's, made if the agent has none yet.</summary>
    private int LocationOfBuilding()
    {
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, state.AtLoc, state.InCity, 0, out var count);
        state.Nloc = (short)count;
        return link;
    }

    /// <summary>Plays the raid: the player's step, a pause, the actors' step, a pause, until the raid is over.</summary>
    private void PlayFrames()
    {
        var wait = state.GameSpeed == 0 ? SlowWait : state.GameSpeed == 1 ? MediumWait : FastWait;
        do
        {
            FrameStep();
            Output.WaitVsync(wait);
            UpdateProjectiles(0);
            Output.WaitVsync(wait);
        }
        while (state.LevelDone == 0);
    }

    /// <summary>
    /// The end: the message, the result for the campaign in the hand-off block (the person's value, the alarm in per
    /// cent, what was found, the wounds, whether the agent is unhurt, the minutes), the rooms seen added to the building's
    /// record, the campaign saved for the next part, and the disk the next part is on.
    /// </summary>
    private void HandBack()
    {
        var practice = state.Status.HasFlag(Options.Practice);
        world.Input.SetNumLock(false);
        TacEndMessage();
        shared.NextModule = practice ? MenusPart : (sbyte)0;
        shared.Person = C16.Char(state.ResultValue);
        // the raid's alarm as a share of the guards that may come, in per cent
        shared.Value = C16.Char(state.Alarm != 0 ? GameLibrary.Clamp(state.Alarm * 10 + 0x14, 0, state.GuardsLeft * 10) : 0);
        shared.Result = (sbyte)state.ResultBits;
        shared.Wounds = C16.Char(state.Wounds);
        shared.NotHurt = (sbyte)((state.ResultBits & RaidResults.Incriminated) == 0 ? 1 : 0);
        shared.Elapsed = C16.Char(C16.Div(state.Rtime, 60));
        state.Skills = (short)(state.Skills & ~(GameSpeedMask << GameSpeedShift) | state.GameSpeed << GameSpeedShift);
        var location = LocationOfBuilding();
        if (location != NoLocation)
        {
            for (var room = 0; room <= state.Nroom; room++)
            {
                if ((state.Rm[room].Status & RoomStatus.Visited) != 0)
                {
                    state.Lc[location].Seen |= 1u << room;
                }
            }
        }
        HandOff.WriteEnvState(state.HandOff.StrEnvSve);
        DoSound(SoundStop);
        ExitCleanup(false);
        world.Graphics.FreePageBuffer(DriverWorkPage);
        // the next part is on disk 1 (the menus) or disk 2 (the campaign)
        Menus.CheckForDisk(practice ? 1 : 2, 1);
    }
}
