using AwesomeAssertions;
using Overcore.Components;
using Overcore.Rendering.UI;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class NineSliceLayoutTests
{
    [Fact]
    public void Build_CreatesNineMatchingDestinationAndSourcePatches()
    {
        IReadOnlyList<UITextureQuad> quads = NineSliceLayout.Build(
            new RectF(10f, 20f, 100f, 80f), new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.8f),
            left: 0.2f, top: 0.25f, right: 0.3f, bottom: 0.25f);

        quads.Should().HaveCount(9);
        quads[0].Rect.Should().Be(new RectF(10f, 20f, 20f, 20f));
        quads[0].U0.Should().BeApproximately(0.1f, 0.0001f);
        quads[0].U1.Should().BeApproximately(0.26f, 0.0001f);
        quads[0].V0.Should().BeApproximately(0.8f, 0.0001f);
        quads[0].V1.Should().BeApproximately(0.65f, 0.0001f);
        quads[8].Rect.Should().Be(new RectF(80f, 80f, 30f, 20f));
    }

    [Fact]
    public void Build_ClampsNonFiniteAndOutOfRangeBorderFractions()
    {
        IReadOnlyList<UITextureQuad> quads = NineSliceLayout.Build(
            new RectF(0f, 0f, 100f, 60f), Vector2.Zero, Vector2.One,
            left: -0.2f, top: float.NaN, right: 1.5f, bottom: float.PositiveInfinity);

        quads.Should().HaveCount(1);
        quads[0].Rect.Should().Be(new RectF(0f, 0f, 100f, 60f));
        quads[0].U0.Should().Be(0f);
        quads[0].U1.Should().Be(1f);
        quads[0].V0.Should().Be(1f);
        quads[0].V1.Should().Be(0f);
    }

    [Fact]
    public void Build_NormalizesOverlappingBorderFractionsAndOmitsEmptyPatches()
    {
        IReadOnlyList<UITextureQuad> quads = NineSliceLayout.Build(
            new RectF(0f, 0f, 50f, 50f), Vector2.Zero, Vector2.One,
            left: 0.8f, top: 0.7f, right: 0.8f, bottom: 0.7f);

        quads.Should().HaveCount(4);
        quads.Sum(quad => quad.Rect.Width * quad.Rect.Height).Should().BeApproximately(2500f, 0.01f);
        quads.All(quad => quad.Rect.Width > 0f && quad.Rect.Height > 0f).Should().BeTrue();
    }
}
