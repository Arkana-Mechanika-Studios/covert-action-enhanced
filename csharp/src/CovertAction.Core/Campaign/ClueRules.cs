namespace CovertAction.Core.Campaign;

/// <summary>
/// The clues the player finds: what a clue is about. A clue (<see cref="CampaignState.Cl"/>) concerns one cast member
/// and tells one thing about them, its <em>subject</em>: a word with the kind in the high byte (1 the face, 2 the name,
/// 4 the organisation, 8 the city, 0x10 the role in the plot) and an index in the low byte (a person, an organisation,
/// a city or the cast member).
/// </summary>
public static class ClueRules
{
    /// <summary>The face of a person (a photo).</summary>
    public const int SubjectFace = 1;

    /// <summary>The name of a person.</summary>
    public const int SubjectName = 2;

    /// <summary>The organisation of a person.</summary>
    public const int SubjectOrg = 4;

    /// <summary>The city of a person.</summary>
    public const int SubjectCity = 8;

    /// <summary>The role of a cast member in the plot.</summary>
    public const int SubjectCastMember = 0x10;

    /// <summary>
    /// Makes the subject word of a new clue of the given kind about the clue's cast member. A clue about a city counts
    /// the city (<see cref="CampaignState.Active"/>); one about an organisation counts it
    /// (<see cref="CampaignState.OrgCounters"/>), makes it known to the player and appends its name to the text.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="slot">The clue, 0..79; its cast member is set.</param>
    /// <param name="kind">The kind of subject (one of the constants of this class).</param>
    /// <returns>The subject word; for a kind that is none of the five, the kind itself (what the original leaves in its result register).</returns>
    // orig: GAME 00f7:5393 make_clue_subject (src/c/game/game_00f7_5393.c) matched; tests: ClueRulesTests, GameOracleTests
    public static int MakeClueSubject(CampaignState state, int slot, int kind)
    {
        var member = state.Cl[slot].Act;
        var personIndex = state.Ac[member].Who;
        switch (kind)
        {
            case SubjectCity:
                var city = state.Ps[personIndex].Loc;
                state.Active[city]++;
                return (kind << 8) + city;
            case SubjectOrg:
                var org = state.Ps[personIndex].Org;
                state.OrgCounters[org]++;
                state.Orgs[org].Clues |= 1;
                state.Txt.Text += state.Orgs[org].Name;
                return (kind << 8) + org;
            case SubjectFace:
            case SubjectName:
                return (kind << 8) + personIndex;
            case SubjectCastMember:
                return member + (kind << 8);
            default:
                // DORMANT: its only caller (add_clue) passes one of the five kinds
                return kind;
        }
    }
}
