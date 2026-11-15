using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Electronics;

/// <summary>
/// What a wiretap teaches the player about the campaign, written into the electronics part's copy of the campaign
/// tables (which it hands back when it ends): buildings of organisations in cities, persons, the clock.
/// </summary>
public static class IntelligenceRules
{
    /// <summary>The persons table: the slot of the case's mastermind, never given to anyone else.</summary>
    private const int MastermindSlot = 0;

    /// <summary>
    /// Finds the person of an organisation in a city and adds knowledge bits to their role, or makes up a new person
    /// there: a random free slot (one whose face is 0; slot 0 is the mastermind's and is taken only for the
    /// mastermind's own organisation and city), with a random face (one in three of them gets bit 0 set, which picks the sex of the face pictures).
    /// </summary>
    /// <remarks>
    /// Random draws: one per slot tried, then, for a new person other than the mastermind, the face and the one in
    /// three. The slot is drawn even when nothing is to be added (<paramref name="bits"/> 0), and a table without a
    /// free slot keeps drawing for ever, as in the original.
    /// </remarks>
    /// <param name="state">The part's copy of the campaign.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="bits">The role bits learned; 0 adds no one.</param>
    /// <returns>The person's index, or -1 when no one was there and <paramref name="bits"/> is 0.</returns>
    // orig: BUG 00e8:2627 find_or_create_person_quiet (src/c/bug/bug_00e8_2627.c) matched; also TAC 00e8:8eab; tests: IntelligenceRulesTests, BugOracleTests
    // DORMANT: no caller in BUG; an older copy of find_or_create_person (BUG 00e8:2175) without its announcement
    public static int FindOrCreatePersonQuiet(ElectronicsState state, int org, int city, int bits)
    {
        for (var index = 0; index < state.Nperson; index++)
        {
            var known = state.Ps[index];
            if (known.Org == org && known.Loc == city)
            {
                known.Role |= (short)bits;
                return index;
            }
        }
        int slot;
        do
        {
            slot = GameLibrary.RandomBelow(state.Random, CampaignTables.PersonCount);
        }
        while (state.Ps[slot].Face != 0 || slot == MastermindSlot);
        if (org == state.Pl.BigOrg && city == state.Pl.BigLoc)
        {
            slot = MastermindSlot;
        }
        if (bits == 0)
        {
            return -1;
        }
        var person = state.Ps[slot];
        person.Org = (short)org;
        person.Loc = (short)city;
        person.Clues = slot != MastermindSlot ? (short)0 : state.Pl.Clues;
        person.Role = (short)bits;
        person.Rknown = 0;
        person.Intel = 0;
        if (slot != MastermindSlot)
        {
            const int faces = 0x7ffe;
            person.Face = (short)(GameLibrary.RandomBelow(state.Random, faces) + 1);
            if (GameLibrary.RandomBelow(state.Random, 3) == 0)
            {
                person.Face |= 1;
            }
        }
        else
        {
            person.Face = state.Pl.BigFace;
        }
        person.Alert = 0;
        return slot;
    }

    /// <summary>
    /// How strongly an organisation is present in a city, 1 (a lone agent) to 5 (an office), 0 or less for not at all:
    /// the city's police strength plus 4, times the organisation's size, divided by one more than the distance of their
    /// keys on the world map, then scaled by the organisation's secrecy. The case's mastermind pair is always present
    /// (1), and at difficulty 0 so is the mastermind's organisation in city 1. The result picks the building's name.
    /// </summary>
    /// <param name="state">The part's copy of the campaign.</param>
    /// <param name="city">The city, 0..15.</param>
    /// <param name="orgNo">The organisation, 0..15.</param>
    /// <exception cref="DivideErrorException">An organisation of secrecy 0 present in the city: the original stops with a divide error.</exception>
    // orig: BUG 00e8:4a9d org_presence (src/c/bug/bug_00e8_4a9d.c) matched; tests: IntelligenceRulesTests, BugOracleTests
    // Other code under the same name: GAME 00f7:07f3 (not this method; docs/source-map/shared-functions.csv).
    public static int OrgPresence(ElectronicsState state, int city, int orgNo)
    {
        const int strongest = 5;
        var place = state.Ct[city];
        var org = state.Org[orgNo];
        var distance = GameLibrary.KeyDistance(place.Climate, org.Climate);
        // C: 16-bit products and divisions
        var presence = (int)C16.Div(C16.Int((place.Police + 4) * org.Size), C16.Int(distance + 1));
        if (presence > 0)
        {
            // C: the original also works out 60 * presence / secrecy + 10 here and never uses it; only its division counts
            _ = C16.Div(C16.Int(60 * presence), org.Covert);
            presence = C16.Div(C16.Int(6 * presence), org.Covert) + 1;
            presence = Math.Min(presence, strongest);
        }
        if (city == state.Pl.BigLoc && orgNo == state.Pl.BigOrg)
        {
            presence = 1;
        }
        const int mastermindCity = 1;
        if (presence <= 0 && state.Diff == 0 && city == mastermindCity && state.Ps[state.Ac[0].Who].Org == orgNo)
        {
            presence = 1;
        }
        return presence;
    }

    /// <summary>
    /// Whether an organisation could believably turn up in a city, seen from the organisation the player is working
    /// on: one and a half times the key distance between the two organisations plus the distance from the player's
    /// organisation to the city must stay below 4 * <see cref="OrgPresence"/> + 16.
    /// </summary>
    /// <param name="state">The part's copy of the campaign.</param>
    /// <param name="cur">The organisation the player is working on.</param>
    /// <param name="other">The organisation that might appear.</param>
    /// <param name="city">The city.</param>
    // orig: BUG 00e8:48e9 org_link_plausible (src/c/bug/bug_00e8_48e9.c) matched; tests: IntelligenceRulesTests, BugOracleTests
    public static bool OrgLinkPlausible(ElectronicsState state, int cur, int other, int city)
    {
        var distance = GameLibrary.KeyDistance(state.Org[cur].Climate, state.Org[other].Climate) * 3 / 2
            + GameLibrary.KeyDistance(state.Org[cur].Climate, state.Ct[city].Climate);
        return 4 * OrgPresence(state, city, other) + 16 > distance;
    }

    /// <summary>
    /// Writes a time of day as "hh:mm:ss" into <see cref="ElectronicsState.ClockText"/>: the campaign's clock plus the
    /// whole minutes of <paramref name="seconds"/>, wrapped at midnight, and the seconds left over.
    /// </summary>
    /// <param name="state">The part's clock and its text.</param>
    /// <param name="seconds">Seconds to add to the campaign's clock.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">The clock text is shorter than "hh:mm:ss": the original writes past its end.</exception>
    // orig: BUG 00e8:50e5 format_game_time (src/c/bug/bug_00e8_50e5.c) matched; tests: IntelligenceRulesTests, BugOracleTests
    // Other code under the same name: CHASE 00df:2bd0, FINAL 0100:2c94, GAME 00f7:95ab (not this method; docs/source-map/shared-functions.csv).
    // DORMANT: no caller in BUG; the twin of the car chase's dashboard clock
    public static string FormatGameTime(ElectronicsState state, int seconds)
    {
        const int minutesPerDay = 24 * 60;
        // C: the sum is taken as unsigned before the remainder, so a negative sum wraps to 65536 + sum first
        var minuteOfDay = (ushort)(seconds / 60 + state.Gtime) % minutesPerDay;
        var hours = minuteOfDay / 60;
        var minutes = minuteOfDay % 60;
        var rest = seconds % 60;
        var text = state.ClockText.ToCharArray();
        if (text.Length < "hh:mm:ss".Length)
        {
            throw new InvalidOperationException($"the clock text \"{state.ClockText}\" is shorter than hh:mm:ss");
        }
        (text[0], text[1]) = TwoDigits(hours);
        (text[3], text[4]) = TwoDigits(minutes);
        (text[6], text[7]) = TwoDigits(rest);
        state.ClockText = new string(text);
        return state.ClockText;
    }

    /// <summary>The tens and the units of a number as characters ('0' plus the digit, a byte as in the original: a negative number gives the characters before '0').</summary>
    private static (char Tens, char Units) TwoDigits(int value) => ((char)(byte)(value / 10 + '0'), (char)(byte)(value % 10 + '0'));
}
