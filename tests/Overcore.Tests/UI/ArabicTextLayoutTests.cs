using AwesomeAssertions;
using Overcore.Components;
using Overcore.Rendering.UI;
using System.Text;
using Xunit;

namespace Overcore.Tests.UI;

public class ArabicTextLayoutTests
{
    [Fact]
    public void ShapeLine_UsesContextualArabicFormsAndVisualRtlOrder()
    {
        VisualTextLine line = ArabicTextLayout.ShapeLine("سلام");

        line.IsRightToLeft.Should().BeTrue();
        Codepoints(line.Text).Should().Equal(0xFEE1, 0xFEFC, 0xFEB3);
        line.GetVisualCaretStops().Should().Equal(4, 3, 2, 1, 0);
    }

    [Fact]
    public void ShapeLine_HandlesPersianLettersAndZeroWidthNonJoiner()
    {
        VisualTextLine line = ArabicTextLayout.ShapeLine("می\u200Cروم");

        line.IsRightToLeft.Should().BeTrue();
        line.Text.Should().NotContain("\u200C");
        Codepoints(line.Text).Should().NotContain(0x200C);
        Codepoints(line.Text).Should().NotContain(0x200D);
    }

    [Fact]
    public void ShapeLine_HandlesExtendedArabicAndUrduLetters()
    {
        Codepoints(ArabicTextLayout.ShapeLine("بہت").Text).Should().Equal(0xFE96, 0xFBA9, 0xFE91);
        Codepoints(ArabicTextLayout.ShapeLine("ٹ").Text).Should().Equal(0xFB66);
        Codepoints(ArabicTextLayout.ShapeLine("ٱ").Text).Should().Equal(0xFB50);
    }

    [Fact]
    public void ShapeLine_KeepsLatinAndNumbersInLogicalOrderInsideRtlParagraph()
    {
        VisualTextLine line = ArabicTextLayout.ShapeLine("سلام 123 test");

        line.IsRightToLeft.Should().BeTrue();
        line.Text.Should().StartWith("test 123 ");
        Codepoints(line.Text).Should().Contain(0xFEFC);
    }

    [Fact]
    public void ShapeLine_MirrorsPairedPunctuationAroundNumbers()
    {
        VisualTextLine line = ArabicTextLayout.ShapeLine("سلام (123)");

        Codepoints(line.Text).Should().Equal('(', '1', '2', '3', ')', ' ', 0xFEE1, 0xFEFC, 0xFEB3);
    }

    [Fact]
    public void ShapeLine_AutoAlignmentUsesTheParagraphStartEdge()
    {
        VisualTextLine arabic = ArabicTextLayout.ShapeLine("فارسی");
        VisualTextLine english = ArabicTextLayout.ShapeLine("English");

        ArabicTextLayout.GetAlignmentOffset(200f, 80f, HorizontalTextAlignment.Start, arabic.IsRightToLeft)
            .Should().Be(120f);
        ArabicTextLayout.GetAlignmentOffset(200f, 80f, HorizontalTextAlignment.Start, english.IsRightToLeft)
            .Should().Be(0f);
    }

    private static int[] Codepoints(string text)
    {
        List<int> codepoints = [];
        foreach (Rune rune in text.EnumerateRunes()) codepoints.Add(rune.Value);
        return [.. codepoints];
    }
}
