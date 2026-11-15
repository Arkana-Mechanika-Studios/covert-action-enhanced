using CovertAction.Core.Final;
using CovertAction.Core.Shared;

namespace CovertAction.Core.Tests.Final;

/// <summary>
/// How the menus part breaks the texts of its boxes into lines, on hand-made texts; the expected values are worked
/// out from the reconstructed C of FINAL (insert_line_breaks), not from the C#.
/// </summary>
public sealed class FinalTextsTests
{
    private static FinalState WithText(string text)
    {
        var state = new FinalState { StrNewline = "\n" };
        state.Txt.Text = text;
        return state;
    }

    [Fact]
    public void ABlankAfterTheWidthBecomesALineBreak()
    {
        var state = WithText("one two three four");
        LibraryTexts.WrapTextBuffer(state.Txt, 5, state.StrNewline);
        // "one two" passes 5 at the blank before "three"; the column counts from 0 again after a break, so the blank
        // after "three" (column 6) breaks too
        Assert.Equal("one two\nthree\nfour\n", state.Txt.Text);
    }

    [Fact]
    public void TwoBlanksAreNotBroken()
    {
        var state = WithText("abcdef  gh");
        LibraryTexts.WrapTextBuffer(state.Txt, 3, state.StrNewline);
        Assert.Equal("abcdef \ngh\n", state.Txt.Text);
    }

    [Fact]
    public void ACaretBecomesABlankAndABreakOverTheNextCharacter()
    {
        var state = WithText("ab^cd");
        LibraryTexts.WrapTextBuffer(state.Txt, 40, state.StrNewline);
        Assert.Equal("ab \nd\n", state.Txt.Text);
    }

    private static FinalState WithLabels()
    {
        var state = new FinalState { StrSuffix2 = "00", StrSuffix3 = "000" };
        state.Txt.Text = ">";
        for (var kind = 0; kind < FinalState.ClueLabelKinds; kind++)
        {
            for (var variant = 0; variant < FinalState.ClueLabelsPerKind; variant++)
            {
                state.ClueLabels[kind][variant] = $"L{kind}{variant}#";
            }
        }
        return state;
    }

    [Theory]
    [InlineData(0x0123, ">L12#36")]
    [InlineData(0x0504, ">L50#500")]
    [InlineData(0x0670, ">L67#49000")]
    [InlineData(-1, ">")]
    public void AnObjectIsNamedByKindVariantAndNumber(int code, string expected)
    {
        // kind = bits 8-11, variant = bits 4-6, number = bits 0-5 plus 1
        var state = WithLabels();
        FinalTexts.AppendNumberedLabel(state, code);
        Assert.Equal(expected, state.Txt.Text);
    }

    [Fact]
    public void AKindBeyondTheTableIsOutsideTheModel()
    {
        var state = WithLabels();
        Assert.Throws<IndexOutOfRangeException>(() => FinalTexts.AppendNumberedLabel(state, 0x0800));
    }

    [Fact]
    public void ATextEndingInABreakGetsNoOther()
    {
        var state = WithText("done\n");
        LibraryTexts.WrapTextBuffer(state.Txt, 40, state.StrNewline);
        Assert.Equal("done\n", state.Txt.Text);
    }
}
