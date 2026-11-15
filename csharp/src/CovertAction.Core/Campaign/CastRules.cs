using CovertAction.Core.Records;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Core.Campaign;

/// <summary>
/// The persons of the world, the buildings the player knows of, and the plot's events as the campaign keeps track of
/// them: finding or adding a person or a building, letting a person go, making the player forget what was learned
/// about someone, choosing the person under arrest whom the next event concerns.
/// </summary>
public static class CastRules
{
    /// <summary>The persons table: the slot of the case's mastermind, never given to anyone else.</summary>
    public const int MastermindSlot = 0;

    /// <summary>A person's <see cref="Person.Alert"/> while under arrest.</summary>
    public const short Arrested = -1;

    /// <summary><see cref="Person.Clues"/>: the player has the person's photo.</summary>
    public const short KnownPhoto = 0x01;

    /// <summary><see cref="Person.Clues"/>: the person was singled out by the player (the "selected" bit).</summary>
    public const short KnownSelected = 0x40;

    /// <summary><see cref="Person.Clues"/>: the player had the person's photo and lost it.</summary>
    public const short KnownPhotoLost = 0x80;

    /// <summary><see cref="Person.Role"/>: the bits that make a person a member of the cast (bit 15 marks a decoy).</summary>
    public const short CastMemberBits = 0x7fff;

    /// <summary><see cref="Activity.Status"/>: the cast member is the mastermind.</summary>
    public const short CastMastermind = 0x0100;

    /// <summary><see cref="Event.Chron"/> of an event that has not happened yet.</summary>
    public const short ChronPending = 999;

    /// <summary><see cref="Clue.Act"/> of a free clue slot.</summary>
    public const short FreeClue = -1;

    /// <summary>
    /// Looks up the person of an organisation in a city and adds <paramref name="bits"/> to their role, or makes up a
    /// new person there: a random free slot (one whose face is 0; slot 0 is the mastermind's and is taken only for
    /// the mastermind's own organisation and city), with a random face (one in three of them gets bit 0 set, a man).
    /// </summary>
    /// <remarks>
    /// Random draws: one per slot tried, then, for a new person other than the mastermind, the face and the one in
    /// three. The slot is drawn even when nothing is to be added (<paramref name="bits"/> 0), and a table without a
    /// free slot keeps drawing for ever, as in the original.
    /// </remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="org">The organisation.</param>
    /// <param name="city">The city.</param>
    /// <param name="bits">The cast bits to add to the person's role; 0 adds no one.</param>
    /// <returns>The person's index, or -1 when no one was there and <paramref name="bits"/> is 0.</returns>
    // orig: GAME 00f7:4d4b find_or_add_person_quiet (src/c/game/game_00f7_4d4b.c) matched; tests: CastRulesTests, GameOracleTests
    // Other code under the same name: TAC 00e8:8eab (not this method; docs/source-map/shared-functions.csv).
    // DORMANT: no caller in GAME; find_or_create_person (GAME 00f7:4899) does the same and announces the person
    public static int FindOrAddPersonQuiet(CampaignState state, int org, int city, int bits)
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
            slot = GameLibrary.RandomBelow(state.Random, CampaignState.PersonCount);
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
    /// Frees a person's slot when the person is no member of the cast (nothing in the role's cast bits): the face
    /// becomes 0 (free), organisation and city -1, and what the player knew of them is cleared.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="person">The person, 0..63.</param>
    // orig: GAME 00f7:49d1 release_person (src/c/game/game_00f7_49d1.c) matched; tests: CastRulesTests, GameOracleTests
    public static void ReleasePerson(CampaignState state, int person)
    {
        var released = state.Ps[person];
        if ((released.Role & CastMemberBits) != 0)
        {
            return;
        }
        released.Face = 0;
        released.Org = -1;
        released.Loc = -1;
        released.Clues = 0;
    }

    /// <summary>
    /// Makes the player forget one kind of knowledge about a person (when a photo is lost or a person moves away):
    /// the person loses the bit and half of the investigation score (losing the photo also marks it as lost), every
    /// cast member that is this person loses the same clue method and half of its progress, and the clues of that
    /// kind about the person are deleted.
    /// </summary>
    /// <param name="state">The campaign.</param>
    /// <param name="person">The person, 0..63.</param>
    /// <param name="kind">One knowledge bit of <see cref="Person.Clues"/> (1 photo, 2 name, 4 organisation, 8 city ...); the same bit is the clue method in <see cref="Activity.Clued"/> and the clue's kind in the high byte of <see cref="Clue.Info1"/>.</param>
    // orig: GAME 00f7:91fa clear_person_knowledge (src/c/game/game_00f7_91fa.c) matched; tests: CastRulesTests, GameOracleTests
    public static void ClearPersonKnowledge(CampaignState state, int person, int kind)
    {
        var forgotten = state.Ps[person];
        forgotten.Clues &= (short)~kind;
        forgotten.Intel = (short)(forgotten.Intel / 2);
        if (kind == KnownPhoto)
        {
            forgotten.Clues |= KnownPhotoLost;
        }
        for (var index = 0; index < state.NEVENT; index++)
        {
            var member = state.Ev[index].Act;
            if (PersonOfCastMember(state, member) == person)
            {
                state.Ac[member].Clued &= (short)~kind;
                state.Ac2[member].Intel = (short)(state.Ac2[member].Intel / 2);
            }
        }
        for (var index = 0; index < CampaignState.ClueCount; index++)
        {
            var clue = state.Cl[index];
            if (PersonOfCastMember(state, clue.Act) == person && clue.Info1 >> 8 == kind)
            {
                clue.Act = FreeClue;
            }
        }
    }

    /// <summary>
    /// The person a cast member is. The member -1 (of a free clue, and of the marker event at the end of the plot) is
    /// no one: the original then reads the word in front of the cast table, GAME DS:9776, which holds the handle of a
    /// cut picture and so never equals a person's number.
    /// </summary>
    /// <remarks>
    /// DS:9776 is written once, by cut_face_sprites (GAME 00f7:851f, the 11x8 icon at 133h,0 of faces.pic), which
    /// game_main (00f7:005a) runs through init_sprite_sheets before the campaign is loaded or the plot steps. The handle
    /// is what MGRAPHIC's finish_image_build (0000:099c) returns: the paragraph of the image in the block DOS gave to
    /// begin_image_cuts (0000:0902, INT 21h AH=48h), far above 63; it is 0 only when that memory ran out, which the
    /// rewrite does not model (D-11).
    /// </remarks>
    /// <returns>The person, or -1.</returns>
    internal static int PersonOfCastMember(CampaignState state, int member)
    {
        const int noMember = -1;
        // BUG: CA-33 the original reads ac[-1], the word in front of the cast table, for the member -1
        return member == noMember ? -1 : state.Ac[member].Who;
    }

    /// <summary>
    /// When a person under arrest stands in the way of an event, may the next event be about that person? Yes (the
    /// person becomes <see cref="CampaignState.Sprung"/>, and the plot step must not pick them) when the level is above
    /// 1, the person is under arrest and was not singled out by the player, the mastermind is not under arrest, and
    /// either no person was excluded in this plot step yet or a roll of 0..7 is below the level.
    /// </summary>
    /// <remarks>Random draws: the roll only when a person was already excluded (or none is to be).</remarks>
    /// <param name="state">The campaign.</param>
    /// <param name="person">The person, 0..63.</param>
    /// <returns>True when the person was chosen.</returns>
    // orig: GAME 00f7:951c try_select_person (src/c/game/game_00f7_951c.c) matched; tests: CastRulesTests, GameOracleTests
    public static bool TrySelectPerson(CampaignState state, int person)
    {
        for (var member = 0; member < state.NACTIVITY - state.Diff * 4; member++)
        {
            if ((state.Ac[member].Status & CastMastermind) != 0)
            {
                state.FlaggedCastPerson = state.Ac[member].Who;
            }
        }
        const int noOneExcluded = -1;
        var candidate = state.Ps[person];
        if ((state.ExcludedPerson == noOneExcluded || GameLibrary.RandomBelow(state.Random, 8) < state.Diff)
            && candidate.Alert == Arrested
            && state.Diff > 1
            && (candidate.Clues & KnownSelected) == 0
            && state.Ps[state.FlaggedCastPerson].Alert != Arrested)
        {
            state.ExcludedPerson = (short)person;
            state.Sprung = (short)person;
            return true;
        }
        return false;
    }

    /// <summary>Marks every event of the plot as not yet happened (<see cref="ChronPending"/>).</summary>
    /// <param name="state">The campaign.</param>
    // orig: GAME 00f7:3c5d clear_event_steps (src/c/game/game_00f7_3c5d.c) matched; tests: CastRulesTests, GameOracleTests
    // DORMANT: no caller in GAME; FINAL sets the steps up when it starts a case
    public static void ClearEventSteps(CampaignState state)
    {
        for (var index = 0; index < state.NEVENT; index++)
        {
            state.Ev[index].Chron = ChronPending;
        }
    }
}
