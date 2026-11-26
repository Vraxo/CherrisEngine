using AwesomeAssertions;
using Overcore.Rendering.Shadows;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The cascade splits: where each shadow map's slice of the view begins and ends. This is the half of the shadow layer
/// that needs no GPU, and it is asserted against the numbers Unity publishes rather than against itself — a split that
/// drifts is a scene whose near shadows are coarse for no reason anybody can name later.
/// </summary>
public class CascadeSplitsTests
{
    // ---------------------------------------------------------------- the published rule

    [Fact]
    public void FourCascades_SplitAtTheFractionsUnityShips()
    {
        // Unity's QualitySettings.asset, every level from Good to Ultra:
        // shadowCascade4Split: {x: 0.06666667, y: 0.19999999, z: 0.46666664} — 1/15, 3/15, 7/15.
        CascadeSplits.Derive(4).Should().Equal(1f / 15f, 3f / 15f, 7f / 15f);
    }

    [Fact]
    public void TwoCascades_SplitAtTheFractionUnityShips()
    {
        // shadowCascade2Split: 0.33333334.
        CascadeSplits.Derive(2).Should().Equal(1f / 3f);
    }

    [Fact]
    public void OneCascade_HasNoBoundaries()
    {
        CascadeSplits.Derive(1).Should().BeEmpty();
    }

    [Fact]
    public void TheSliceWidthsDouble()
    {
        // The property the split exists for: each cascade is twice as deep as the one inside it, so it covers twice the
        // volume at the same texel count — which is why the halving is the rule and not an accident of 1/15, 3/15, 7/15.
        CascadeRange[] ranges = CascadeSplits.Ranges(150f, 0.1f, 4);

        float[] widths = [.. ranges.Select(range => range.Width)];

        widths.Should().HaveCount(4);
        widths[0].Should().BeApproximately(10f, 0.001f);
        widths[1].Should().BeApproximately(20f, 0.001f);
        widths[2].Should().BeApproximately(40f, 0.001f);
        widths[3].Should().BeApproximately(80f, 0.001f);

        for (int i = 1; i < widths.Length; i++)
        {
            widths[i].Should().BeApproximately(widths[i - 1] * 2f, 0.01f, "widths double");
        }
    }

    [Fact]
    public void TheDepthsAreTheUnityMetres()
    {
        // 150 units over four cascades ends at 10, 30, 70 and 150.
        CascadeSplits.FarDepths(150f, 4).Should().Equal(10f, 30f, 70f, 150f);

        CascadeSplits.FarDepths(150f, 2).Should().Equal(50f, 150f);
        CascadeSplits.FarDepths(150f, 1).Should().Equal(150f);
    }

    // ---------------------------------------------------------------- the three counts that are real

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9, 4)]
    [InlineData(int.MaxValue, 4)]
    public void ACascadeCountRoundsToARealOne(int requested, int expected)
    {
        // Three is not a mode: it gives one wide cascade and one narrow one and no benefit, so it rounds up.
        CascadeSplits.SupportedCascades(requested).Should().Be(expected);
        CascadeSplits.Derive(expected).Should().HaveCount(Math.Max(expected - 1, 0));
    }

    // ---------------------------------------------------------------- a hand-edited file

    [Fact]
    public void ASplitListOfTheWrongLength_IsDiscardedWithTheDerivedDefault()
    {
        float[] discarded = CascadeSplits.Sanitize([0.1f, 0.9f], cascades: 4, out bool repaired);

        discarded.Should().Equal(CascadeSplits.Derive(4));
        repaired.Should().BeTrue("the file disagreed with the cascade count and the caller should be able to say so");
    }

    [Fact]
    public void AnAbsentSplitList_IsNotARepair()
    {
        // A scene that never mentioned splits is the normal case, and it must not log a warning for being normal.
        CascadeSplits.Sanitize(null, cascades: 4, out bool repaired).Should().Equal(CascadeSplits.Derive(4));
        repaired.Should().BeFalse();
    }

    [Fact]
    public void AnOutOfOrderSplitList_IsClampedRatherThanTrusted()
    {
        // Two boundaries both claiming 0.5 must not produce a cascade of zero depth, which is a division by zero in the
        // fit and a NaN matrix in the frame.
        float[] repairedSplits = CascadeSplits.Sanitize([0.5f, 0.5f, 0.5f], cascades: 4, out bool repaired);

        repaired.Should().BeTrue();
        StrictlyIncreasing(repairedSplits).Should().BeTrue();
        repairedSplits.Should().OnlyContain(value => value > 0f && value < 1f);
    }

    [Fact]
    public void ANonFiniteOrOutOfRangeSplit_IsClamped()
    {
        float[] repairedSplits = CascadeSplits.Sanitize(
            [float.NaN, float.PositiveInfinity, -3f], cascades: 4, out bool repaired);

        repaired.Should().BeTrue();
        repairedSplits.Should().OnlyContain(value => float.IsFinite(value) && value > 0f && value < 1f);
        StrictlyIncreasing(repairedSplits).Should().BeTrue();
    }

    [Fact]
    public void AValidSplitList_SurvivesUntouched()
    {
        // A hand-tuned split is the whole reason the list is in the file: it has to come back exactly.
        float[] tuned = [0.05f, 0.18f, 0.5f];

        CascadeSplits.Sanitize(tuned, cascades: 4, out bool repaired).Should().Equal(tuned);
        repaired.Should().BeFalse();
    }

    [Fact]
    public void ASplitListForOneCascade_IsDiscarded()
    {
        // One cascade has no boundaries; a file that lists some for it is describing a different setup.
        CascadeSplits.Sanitize([0.5f], cascades: 1, out bool repaired).Should().BeEmpty();
        repaired.Should().BeTrue();
    }

    // ---------------------------------------------------------------- the distances

    [Theory]
    [InlineData(0f)]
    [InlineData(-100f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AnInvalidDistance_FallsBackToTheDefaultOrTheMinimum(float distance)
    {
        float[] depths = CascadeSplits.FarDepths(distance, 4);

        depths.Should().HaveCount(4);
        depths.Should().OnlyContain(value => float.IsFinite(value) && value > 0f);
        StrictlyIncreasing(depths).Should().BeTrue();
        depths[^1].Should().BeInRange(CascadeSplits.MinimumDistance, CascadeSplits.MaximumDistance);
    }

    [Fact]
    public void ADeepDistance_IsClampedToTheMaximum()
    {
        CascadeSplits.FarDepths(1_000_000f, 4)[^1].Should().Be(CascadeSplits.MaximumDistance);
    }

    [Fact]
    public void TheLastCascadeAlwaysReachesTheDistance()
    {
        // Past the last split nothing is shadowed, so the last split has to be the distance itself or the receiver's
        // "past the distance" test and the fit disagree by whatever the last boundary left out.
        foreach (int cascades in new[] { 1, 2, 4 })
        {
            foreach (float distance in new[] { 20f, 150f, 900f })
            {
                CascadeSplits.FarDepths(distance, cascades)[^1].Should().Be(distance);
            }
        }
    }

    // ---------------------------------------------------------------- the ranges

    [Fact]
    public void TheRangesAreContiguousFromTheNearPlane()
    {
        // A gap between two cascades is a band of the view that no depth map covers, and it shows up as an unshadowed
        // ring on the ground at exactly the boundary — the hardest kind of seam to see and to diagnose.
        CascadeRange[] ranges = CascadeSplits.Ranges(150f, 0.1f, 4);

        ranges[0].Near.Should().Be(0.1f);

        for (int i = 1; i < ranges.Length; i++)
        {
            ranges[i].Near.Should().Be(ranges[i - 1].Far, "one cascade ends where the next begins");
        }
    }

    [Fact]
    public void EveryRangeHasDepth()
    {
        // A zero-width slice divides by zero in the fit's texel arithmetic. The near plane and the cascade count are both
        // things a scene file can write, so the guarantee belongs here and not in the renderer's hopeful hands.
        foreach (float near in new[] { 0.001f, 0.1f, 5f, 40f, 400f })
        {
            foreach (int cascades in new[] { 1, 2, 4 })
            {
                CascadeSplits.Ranges(150f, near, cascades).Should().OnlyContain(
                    range => range.Width > 0f && float.IsFinite(range.Near) && float.IsFinite(range.Far));
            }
        }
    }

    [Fact]
    public void ANearPlanePastTheFirstSplit_IsPulledIn()
    {
        // A camera with a 40-unit near plane would otherwise put every corner of the first cascade behind it.
        CascadeRange[] ranges = CascadeSplits.Ranges(150f, 40f, 4);

        ranges[0].Near.Should().BeLessThan(40f);
        ranges[0].Width.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void AnInvalidNearPlane_IsIgnored()
    {
        CascadeRange[] ranges = CascadeSplits.Ranges(150f, float.NaN, 4);

        ranges.Should().OnlyContain(range => range.Width > 0f);
        float.IsNaN(ranges[0].Near).Should().BeFalse();
    }

    /// <summary>Whether each entry is greater than the one before it. A repeated boundary is a zero-depth slice.</summary>
    private static bool StrictlyIncreasing(float[] values)
    {
        return values.Zip(values.Skip(1), (earlier, later) => later > earlier).All(increasing => increasing);
    }
}
