using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;
using CovertAction.Msc;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The start of a case: before the agent first sees the map, the criminals' plot is played for a while out of sight
/// until the agent's side knows a few persons; then the chief briefs the agent.
/// </summary>
/// <param name="state">The campaign.</param>
/// <param name="world">The drivers and the machine.</param>
/// <param name="screens">The campaign's screens.</param>
/// <param name="plot">The plot engine (time passes through it).</param>
/// <param name="investigation">The investigation that finds the persons.</param>
public sealed class CaseBriefing(CampaignState state, Boundaries world, CampaignScreens screens, PlotEngine plot, Investigation investigation)
{
    /// <summary>The hidden turns' search: the organisation of the agent's side (the CIA), and a strength no person can resist.</summary>
    private const int AgentsOrg = 1, HiddenSearchStrength = 99;

    /// <summary>The cities of the world: one bit each in <see cref="CampaignState.Doubles"/>.</summary>
    private const int Cities = 16;

    /// <summary>A hidden turn without anything to search lets three hours pass (minutes).</summary>
    private const int ThreeHours = 0xb4;

    /// <summary>The persons the hidden turns must find: two for the hardest level, up to eight for the easiest.</summary>
    private const int FactsPerLevelStep = 2, LevelCount = 4;

    /// <summary>The messages the plot must have logged before the hidden turns search (fewer pass the time instead).</summary>
    private const int MessagesBeforeSearch = 5;

    /// <summary>The script variable of the briefing's animation and its value of a case that begins (the chief welcomes the agent).</summary>
    private const int SceneVariable = 0, SceneCaseBegins = 1;

    /// <summary>The briefing's box: the text is wrapped at 40 characters and stands below the animation.</summary>
    private const int BoxColumns = 0x28, BoxX = 8, BoxY = 0x95;

    /// <summary>
    /// Begins a new case: the plot's clock goes back before its first step, and hidden turns are played (a plot step
    /// for a random city that is not on alert, when the log already holds more than five messages, else three hours
    /// pass) until the searches found as many persons as the level asks for (8 at the easiest level, 2 at the hardest).
    /// Then the chief briefs the agent beside "briefing.pan": "The rest is up to you.", on the easiest level the
    /// research assistant, and the President's message.
    /// </summary>
    // orig: GAME 00f7:3b76 start_case_briefing (src/c/game/game_00f7_3b76.c) matched; tests: GameOracleTests, CampaignTests
    // GAME's copy is other code than FINAL's start_case_briefing, which chooses the case (shared-functions.csv: two groups)
    public void StartCaseBriefing()
    {
        var found = 0;
        state.ChronTime = -1;
        do
        {
            if (state.NextSyn > MessagesBeforeSearch)
            {
                int city;
                do
                {
                    city = GameLibrary.RandomBelow(state.Random, Cities);
                }
                // C: the shift of a 16-bit word by a variable count
                while ((state.Doubles & C16.Shl(1, city)) != 0);
                found += investigation.SearchYield(AgentsOrg, city, HiddenSearchStrength, 0);
            }
            else
            {
                plot.PassTime(ThreeHours);
            }
            state.Sleepy = 0;
        }
        while (found < (LevelCount - state.Diff) * FactsPerLevelStep);
        state.Begun = 1;
        screens.Animation.SetScriptVar(SceneVariable, SceneCaseBegins);
        world.Graphics.SetColorMaps(state.BriefingColorMap, state.BriefingColorMap);
        screens.Menus.StartAnimation(state.Briefing.BriefingScene, 0, 0);
        var words = state.Briefing;
        state.Txt.Text = words.RestIsUpToYou;
        if (state.Diff == 0)
        {
            state.Txt.Text += words.ResearchAssistant;
        }
        state.Txt.Text += words.PresidentMessage;
        LibraryTexts.WrapTextBuffer(state.Txt, BoxColumns, state.LineEnd);
        plot.PopupMenuOnCleanScreen(state.Txt.Text, BoxX, BoxY);
    }
}
