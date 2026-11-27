using AwesomeAssertions;
using Overcore.Rendering.UI;
using Xunit;

namespace Overcore.Tests.UI;

public class TextLayoutTests
{
    [Fact]
    public void Wrap_PreservesParagraphBreaksAndMovesWholeWordsWhenPossible()
    {
        IReadOnlyList<string> lines = TextLayout.Wrap("alpha beta gamma\n\ndelta", 10f, text => text.Length);

        lines.Should().Equal("alpha beta", "gamma", "", "delta");
    }

    [Fact]
    public void Wrap_SplitsOversizedWordsByTextElement()
    {
        IReadOnlyList<string> lines = TextLayout.Wrap("abcdefgh", 3f, text => text.Length);

        lines.Should().Equal("abc", "def", "gh");
    }

    [Fact]
    public void Wrap_DoesNotSplitCombiningSequences()
    {
        IReadOnlyList<string> lines = TextLayout.Wrap("e\\u0301x", 1f, text => text.Length);

        lines.Should().Equal("e\\u0301", "x");
    }

    [Fact]
    public void Wrap_TreatsInvalidMeasurementsAsOverWidth()
    {
        IReadOnlyList<string> lines = TextLayout.Wrap("abc", 1f, _ => float.NaN);

        lines.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Wrap_LeavesContentUnwrappedWhenWidthIsInvalid()
    {
        IReadOnlyList<string> lines = TextLayout.Wrap("a long line\nnext", float.NaN, text => text.Length);

        lines.Should().Equal("a long line", "next");
    }
}
