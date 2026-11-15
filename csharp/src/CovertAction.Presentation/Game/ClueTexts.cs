using CovertAction.Core.Campaign;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Game;

/// <summary>
/// The clue sentences of the campaign, read from CLUES.TXT: the sentence that tells the player what a clue says ("The
/// car with plate 5 was seen at this face.") and the item key a new clue gets for a decoy of the cast.
/// </summary>
/// <remarks>
/// An entry of CLUES.TXT is filed under a tag "*Cxxyy": "*C00k0" for a clue about a face, a name, an organisation or a
/// city (k: which of them), "*Ccc0m" for the role of member m of crime cc. The first character of an entry is a digit
/// that says how the clue was found (photo, wiretap ...); for a role entry a second digit gives the kind of item.
/// </remarks>
/// <param name="state">The campaign.</param>
/// <param name="text">The library's text routines (the reader of the text files).</param>
public sealed class ClueTexts(CampaignState state, LibraryText text)
{
    /// <summary>
    /// The item key of a new clue about a cast member: the low byte is the member's key (bits 0..6, or the key
    /// halved), the high byte the kind of item. A member of the plot gives its own kind of item; a decoy takes it from
    /// the role sentence of CLUES.TXT its face picks (a role of one of crimes 0 to 3, members 0 to 5): the second
    /// character of that entry.
    /// </summary>
    /// <param name="member">The cast member.</param>
    /// <param name="wholeKey">Whether the low byte is the key's bits 0..6 (otherwise the key halved).</param>
    /// <returns>The item key: kind of item in the high byte.</returns>
    // orig: GAME 00f7:5446 cast_clue_code (src/c/game/game_00f7_5446.c) matched; tests: GameOracleTests
    public int CastClueCode(int member, bool wholeKey)
    {
        // the low byte of the member's key, unsigned
        int number = state.Ac[member].Key & 0xff;
        number = wholeKey ? number & 0x7f : number >> 1;
        if (state.NACTIVITY - state.Diff * 4 > member)
        {
            return ((state.Ac[member].Ctype & 0xff) << 8) + number;
        }
        // a decoy: the role sentence of crime (face >> 3) % 4, member face % 6
        int face = state.Ps[state.Ac[member].Who].Face;
        var tag = WithDigit(WithDigit(state.ClueCodeTagTemplate, 5, face % 6), 3, (face >> 3) % 4);
        var start = state.Txt.Length;
        text.ReadClearedText(state.ClueCodeFileName, tag, state.ClueVariableNames, state.VariableValues, CampaignState.ClueVariables);
        state.Txt.Truncate(start);
        // C: the second character of the sentence read, which stays behind the end mark just written
        return ((state.Txt[start + 1] & 0xf) << 8) + number;
    }

    /// <summary>
    /// Appends the sentence of a clue to the text buffer: the clue's item and subject are filled into the entry of
    /// CLUES.TXT for the kind of subject, and the entry's first digit (or two, for a role) is taken off and kept as
    /// the way the clue was found (<see cref="CampaignState.CluePhraseIndex"/>). A clue about a face shows that face
    /// beside the next box.
    /// </summary>
    /// <param name="subject">The clue's subject word: the kind (1 face, 2 name, 4 organisation, 8 city, 0x10 cast member) in the high byte, the person, organisation, city or member in the low byte.</param>
    /// <param name="item">The clue's item key (-1 for none).</param>
    /// <param name="clue">The clue, 0..79 (its cast member, for a role).</param>
    // orig: GAME 00f7:570d append_clue_text (src/c/game/game_00f7_570d.c) matched; tests: GameOracleTests
    public void AppendClueText(int subject, int item, int clue)
    {
        const int itemValue = 0, nameValue = 1, orgValue = 2, cityValue = 3;
        var index = subject & 0xff;
        // C: an arithmetic shift of the 16-bit word
        var kind = (short)subject >> 8;
        // the tag's last digit is the position of the kind's bit: face 0, name 1, organisation 2, city 3
        var bit = 0;
        for (var k = kind; k > 1; k >>= 1)
        {
            bit++;
        }
        var tag = WithDigit(WithDigit(state.ClueTagTemplate, 2, item >> 8 & 0xf), 3, bit);
        state.VariableValues[itemValue] = TakeAppended(() => CampaignTexts.AppendClueLabel(state, item));
        var digits = 1;
        switch (kind)
        {
            case ClueRules.SubjectFace:
                state.Picface = state.Ps[index].Face;
                break;
            case ClueRules.SubjectName:
                state.VariableValues[nameValue] = TakeAppended(() => CampaignTexts.AppendFullName(state, index));
                break;
            case ClueRules.SubjectOrg:
                state.VariableValues[orgValue] = state.Orgs[index].Name;
                break;
            case ClueRules.SubjectCity:
                state.VariableValues[cityValue] = state.Ct[index].Name;
                break;
            case ClueRules.SubjectCastMember:
                // a role entry starts with two digits: the way it was found and the kind of item
                digits = 2;
                tag = state.Cl[clue].Act < state.NACTIVITY - state.Diff * 4 ? RoleTag(index) : DecoyRoleTag(clue);
                break;
        }
        var start = state.Txt.Length;
        // BUG: CA-16 a tag the file lacks gives the clearance notice, whose first letter is taken as the digit(s)
        text.ReadClearedText(state.ClueFileName, tag, state.ClueVariableNames, state.VariableValues, CampaignState.ClueVariables);
        state.CluePhraseIndex = (short)(state.Txt[start] & 0xf);
        // the reader appends at least a character and a blank, so the digits are within the text
        state.Txt.Text = state.Txt.Text[..start] + state.Txt.Text[(start + digits)..];
    }

    /// <summary>The tag of the role sentence of member <paramref name="member"/> of the case's crime: "*Ccc0m".</summary>
    private string RoleTag(int member)
    {
        var tag = WithDigit(state.RoleClueTagTemplate, 5, member);
        tag = WithDigit(tag, 3, state.TheCrime % 10);
        return WithDigit(tag, 2, state.TheCrime / 10);
    }

    /// <summary>The tag a decoy borrows, picked by its person's face: "*C0d0e", d = (face >> 3) % 4, e = face % 6.</summary>
    private string DecoyRoleTag(int clue)
    {
        int face = state.Ps[state.Ac[state.Cl[clue].Act].Who].Face;
        return WithDigit(WithDigit(state.DecoyClueTagTemplate, 5, face % 6), 3, (face >> 3) % 4);
    }

    /// <summary>Runs something that appends to the text buffer, takes off what it appended and returns it.</summary>
    private string TakeAppended(Action append)
    {
        var start = state.Txt.Length;
        append();
        var appended = state.Txt.Text[start..];
        state.Txt.Truncate(start);
        return appended;
    }

    /// <summary>A tag with the character at a position moved on by an amount ('0' plus 3 is '3').</summary>
    private static string WithDigit(string tag, int position, int amount)
    {
        var characters = tag.ToCharArray();
        // C: a char of the tag plus an int, stored back as a byte
        characters[position] = (char)(byte)(characters[position] + amount);
        return new string(characters);
    }
}
