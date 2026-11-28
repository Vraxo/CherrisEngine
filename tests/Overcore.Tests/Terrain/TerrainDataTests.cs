using AwesomeAssertions;
using Overcore.Terrain;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers <see cref="TerrainData"/>: the height grid, bilinear sampling, normals, and the alphamap's layers.
/// </summary>
public class TerrainDataTests
{
    [Fact]
    public void Heights_RoundTripThroughRegions()
    {
        TerrainData data = new(9, new Vector3(8f, 4f, 8f));
        float[,] heights = new float[3, 3];
        for (int z = 0; z < 3; z++)
        {
            for (int x = 0; x < 3; x++)
            {
                heights[z, x] = (z * 3) + x;
            }
        }

        data.SetHeights(2, 3, heights);

        data.GetHeight(2, 3).Should().Be(0f);
        data.GetHeight(4, 5).Should().Be(8f);
        float[,] readBack = data.GetHeights(2, 3, 3, 3);
        for (int z = 0; z < 3; z++)
        {
            for (int x = 0; x < 3; x++)
            {
                readBack[z, x].Should().Be(heights[z, x]);
            }
        }

        data.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SampleHeight_InterpolatesBetweenGridPoints()
    {
        TerrainData data = new(3, new Vector3(2f, 10f, 2f));
        data.SetHeight(0, 0, 0f);
        data.SetHeight(1, 0, 2f);
        data.SetHeight(2, 0, 4f);
        data.SetHeight(0, 1, 0f);
        data.SetHeight(1, 1, 2f);
        data.SetHeight(2, 1, 4f);

        // Halfway between the first two columns of the first row, and midway down the slope.
        data.SampleHeight(0.25f, 0f).Should().BeApproximately(1f, 1e-4f);
        data.SampleHeight(1f, 0f).Should().BeApproximately(4f, 1e-4f);
        data.SampleHeight(-5f, 99f).Should().BeApproximately(0f, 1e-4f, "sampling clamps to the edge");
    }

    [Fact]
    public void Normals_AreUpForFlatGround_AndLeanAgainstASlope()
    {
        TerrainData flat = new(5, new Vector3(4f, 4f, 4f));
        flat.GetNormal(0.5f, 0.5f).Should().Be(Vector3.UnitY);

        TerrainData slope = new(5, new Vector3(4f, 4f, 4f));
        for (int z = 0; z < 5; z++)
        {
            for (int x = 0; x < 5; x++)
            {
                slope.SetHeight(x, z, x);
            }
        }

        Vector3 normal = slope.GetNormal(0.5f, 0.5f);
        normal.X.Should().BeLessThan(0f, "the surface leans back against the +X rise");
        normal.Y.Should().BeGreaterThan(0f);
        normal.Length().Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Alphamap_StartsOnTheFirstLayer_AndWeightsSumToOne()
    {
        TerrainData data = new(9);
        data.AddLayer(new TerrainLayer("Rock", string.Empty, Vector3.UnitX));

        float[] weights = data.GetAlphamapWeights(0.3f, 0.7f);
        weights.Should().HaveCount(2);
        weights[0].Should().BeApproximately(1f, 1e-4f);
        weights.Sum().Should().BeApproximately(1f, 1e-4f);
        data.GetDominantLayer(0.3f, 0.7f).Should().Be(0);
        data.GetBlendedTint(0.3f, 0.7f).Should().Be(data.Layers[0].Tint);
    }

    [Fact]
    public void AddLayer_PreservesExistingPaint()
    {
        TerrainData data = new(5);
        data.SetAlphamapWeights(2, 2, [1f]);

        TerrainLayer added = data.AddLayer(new TerrainLayer("Sand", string.Empty, Vector3.One));

        added.Should().BeSameAs(data.Layers[1]);
        data.GetAlphamapWeights(0.5f, 0.5f)[0].Should().BeApproximately(1f, 1e-4f);
        data.GetAlphamapWeights(0.5f, 0.5f)[1].Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void InvalidResolutionsAndRegions_AreRefused()
    {
        Action tooSmall = () => new TerrainData(1);
        tooSmall.Should().Throw<ArgumentOutOfRangeException>();

        TerrainData data = new(5);
        Action outside = () => data.GetHeight(5, 0);
        outside.Should().Throw<ArgumentOutOfRangeException>();

        Action wrongSize = () => data.SetHeightmap(new float[3]);
        wrongSize.Should().Throw<ArgumentException>();
    }
}
