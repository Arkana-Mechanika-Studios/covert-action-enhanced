namespace CovertAction.Core.Campaign;

/// <summary>
/// The words of a new case's briefing: the animation of the chief, his last sentences and the President's message.
/// Texts of the program, read from the player's GAME.EXE.
/// </summary>
// orig: GAME DS:27a6..2850, the literals of start_case_briefing (src/c/game/game_00f7_3b76.c)
public sealed class BriefingWords
{
    /// <summary>GAME DS:27a6: the animation of the chief at his desk ("briefing.pan").</summary>
    public string BriefingScene { get; set; } = "";

    /// <summary>GAME DS:27b3: the first words of the briefing's box.</summary>
    public string RestIsUpToYou { get; set; } = "";

    /// <summary>GAME DS:27cb: the research assistant the easiest level gives the agent.</summary>
    public string ResearchAssistant { get; set; } = "";

    /// <summary>GAME DS:281c president_message_text (MicroProse's name): the President's message that ends the briefing.</summary>
    public string PresidentMessage { get; set; } = "";
}
