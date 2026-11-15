namespace CovertAction.Core.Final;

/// <summary>
/// How the menus part prepares the texts of its boxes in the text buffer <see cref="FinalState.Txt"/> before they
/// are shown.
/// </summary>
public static class FinalTexts
{
    /// <summary>The clue code that names no object.</summary>
    private const int NoObject = -1;

    /// <summary>The kinds of object whose number is a sum of money, in hundreds or thousands.</summary>
    private const int Hundreds = 5, Thousands = 6;

    /// <summary>
    /// Appends to the text buffer the name of a clue's object: the label of its kind and variant (bits 8-11 and 4-6 of
    /// <paramref name="p"/>) and the number (bits 0-5, plus 1); a sum of hundreds or of thousands gets its zeros
    /// behind the number ("$4" "00"). Nothing for <paramref name="p"/> = -1.
    /// </summary>
    /// <param name="state">The text buffer, the labels and the number buffer.</param>
    /// <param name="p">The object's code.</param>
    /// <exception cref="IndexOutOfRangeException">A kind of 8 or more, for which the original reads past its table of labels.</exception>
    // orig: FINAL 0100:690e append_numbered_label (src/c/final/final_0100_690e.c) matched; tests: FinalTextsTests, FinalOracleTests
    public static void AppendNumberedLabel(FinalState state, int p)
    {
        if (p == NoObject)
        {
            return;
        }
        // C: an arithmetic shift of the 16-bit code; the number's bits 4 and 5 are also the variant's low bits
        var kind = p >> 8 & 0xf;
        state.Txt.Text += state.ClueLabels[kind][p >> 4 & 7];
        state.DigitsMsg = ((p & 0x3f) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        state.Txt.Text += state.DigitsMsg;
        if (kind == Hundreds)
        {
            state.Txt.Text += state.StrSuffix2;
        }
        if (kind == Thousands)
        {
            state.Txt.Text += state.StrSuffix3;
        }
    }

}
