using CovertAction.Core.Campaign;
using CovertAction.Core.Shared;

namespace CovertAction.Presentation.Game;

/// <summary>
/// Two calls of the campaign's library the building menus make again and again, with the campaign's tables behind
/// them: the kind of a building put into the text buffer, and the player's list of known buildings.
/// </summary>
internal static class BuildingGlue
{
    /// <summary>Appends the kind of building an organisation has in a city (" hideout", " agent" ...) to the text buffer.</summary>
    /// <param name="state">The campaign.</param>
    /// <param name="city">The city.</param>
    /// <param name="org">The organisation.</param>
    /// <returns>The building's level, which also sets the picture the building is shown with.</returns>
    internal static int AppendBuildingType(CampaignState state, int city, int org) =>
        CampaignLibrary.AppendBuildingType(state.Txt, state.BuildingKinds, (c, o) => WorldRules.OrgPresence(state, c, o), city, org);

    /// <summary>
    /// Finds the player's record of the building of an organisation in a city, adding it when the flags say so (the
    /// library's find_or_add_link), and keeps the count of buildings the library gives back.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="flags">What the player knows of the building if it is new (0: look only).</param>
    /// <returns>The record's index, or -1 when there is none.</returns>
    internal static int FindOrAddLink(CampaignState state, int org, int city, int flags)
    {
        var link = CampaignLibrary.AddLink(state.Lc, state.Ct, state.Nloc, org, city, flags, out var buildings);
        state.Nloc = (short)buildings;
        return link;
    }

    /// <summary>
    /// The low byte of a building's knowledge word, as the original reads it with the index <c>find_or_add_link</c> gave
    /// back; for -1 (no record) it is the byte at <c>lc[-1].clues</c>, DS:048c, in front of the table: the low byte of
    /// the last person's <c>role</c> (<c>ps[63]</c> ends at DS:0494, where <c>lc</c> begins).
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="link">The index the lookup gave, or -1.</param>
    /// <returns>The byte.</returns>
    // DIVERGENCE: D-689 the byte is read from the person's record; the original's lc[-1] is those very bytes
    internal static int LinkClues(CampaignState state, int link) =>
        link == -1 ? state.Ps[CampaignState.PersonCount - 1].Role & 0xff : state.Lc[link].Clues & 0xff;
}
