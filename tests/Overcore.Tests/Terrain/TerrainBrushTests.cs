using AwesomeAssertions;
using Overcore.Terrain;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers <see cref="TerrainBrush"/>: raising, lowering, flattening, smoothing and painting strokes.
/// </summary>
public class TerrainBrushTests
{
    [Fact]
    public void Raise_LiftsTheCentreMost_AndLeavesTheRimAlone()
    {
        TerrainData data = new(21, new Vector3(20f, 10f, 20f));

        int moved = TerrainBrush.ApplyHeight(
            data, new Vector2(0.5f, 0.5f), 0.25f, 2f, TerrainBrushMode.Raise);

        moved.Should().BeGreaterThan(0);
        data.GetHeight(10, 10).Should().BeApproximately(2f, 1e-4f);
        data.GetHeight(10, 15).Should().BeLessThan(2f, "the falloff fades with distance");
        data.GetHeight(0, 0).Should().Be(0f, "outside the stroke nothing moves");
        data.SampleHeight(0.5f, 0.5f).Should().BeApproximately(2f, 1e-3f);
    }

    [Fact]
    public void Lower_IsTheInverseOfRaise()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));

        TerrainBrush.ApplyHeight(data, new Vector2(0.5f, 0.5f), 0.3f, 1.5f, TerrainBrushMode.Lower);

        data.GetHeight(5, 5).Should().BeApproximately(-1.5f, 1e-4f);
    }

    [Fact]
    public void Flatten_PullsTowardTheTargetHeight()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.SetHeight(5, 5, 8f);

        TerrainBrush.ApplyHeight(
            data, new Vector2(0.5f, 0.5f), 0.2f, 1f, TerrainBrushMode.Flatten, flattenHeight: 2f);

        data.GetHeight(5, 5).Should().BeApproximately(2f, 1e-4f);
    }

    [Fact]
    public void Smooth_WearsAPeakDown()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.SetHeight(5, 5, 10f);

        TerrainBrush.ApplyHeight(data, new Vector2(0.5f, 0.5f), 0.3f, 1f, TerrainBrushMode.Smooth);

        data.GetHeight(5, 5).Should().BeLessThan(10f);
        data.GetHeight(5, 5).Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Paint_MakesThePaintedLayerDominant_AndKeepsWeightsSummingToOne()
    {
        TerrainData data = new(21, new Vector3(20f, 10f, 20f));
        data.AddLayer(new TerrainLayer("Rock", string.Empty, Vector3.UnitX));

        int painted = TerrainBrush.ApplyPaint(data, new Vector2(0.5f, 0.5f), 0.25f, 1f, layerIndex: 1);

        painted.Should().BeGreaterThan(0);
        data.GetDominantLayer(0.5f, 0.5f).Should().Be(1);
        data.GetAlphamapWeights(0.5f, 0.5f).Sum().Should().BeApproximately(1f, 1e-4f);
        data.GetDominantLayer(0f, 0f).Should().Be(0, "outside the stroke the base layer stays");
    }
}
