using AwesomeAssertions;
using Overcore.Core;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers neighbour stitching: two grounds sewing a shared edge into one identical border, at matched
/// and mismatched resolutions, and the component that finds its neighbour by standing next to it.
/// </summary>
public class TerrainStitchTests
{
    [Fact]
    public void StitchEdge_AveragesBothSidesIntoOneBorder()
    {
        TerrainData west = new(5, new Vector3(4f, 10f, 4f));
        TerrainData east = new(5, new Vector3(4f, 10f, 4f));
        for (int z = 0; z < 5; z++)
        {
            west.SetHeight(4, z, z);
            east.SetHeight(0, z, 10f);
        }

        west.StitchEdge(east, TerrainEdge.Right);

        for (int z = 0; z < 5; z++)
        {
            west.GetHeight(4, z).Should().Be((z + 10f) / 2f);
            east.GetHeight(0, z).Should().Be(west.GetHeight(4, z), "one border, stated twice");
        }

        west.GetHeight(0, 2).Should().Be(0f, "the far edge is not the seam's business");
    }

    [Fact]
    public void StitchEdge_AcrossResolutions_MeetsWhereTheGridsCan()
    {
        TerrainData coarse = new(5, new Vector3(4f, 10f, 4f));
        TerrainData fine = new(9, new Vector3(4f, 10f, 4f));
        for (int z = 0; z < 5; z++)
        {
            coarse.SetHeight(4, z, z * 2f);
        }

        coarse.StitchEdge(fine, TerrainEdge.Right);

        foreach (float v in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            coarse.SampleHeight(1f, v).Should().BeApproximately(fine.SampleHeight(0f, v), 1e-3f,
                "both sides read the same height at the same place along the seam");
        }
    }

    [Fact]
    public void Neighbours_AreFoundByStandingNextToEachOther_AndStitch()
    {
        GameObject westObject = new("West");
        TerrainComponent west = westObject.AddComponent<TerrainComponent>();
        west.TerrainData = new TerrainData(5, new Vector3(4f, 10f, 4f));

        GameObject eastObject = new("East");
        eastObject.Transform.WorldPosition = new Vector3(4f, 0f, 0f);
        TerrainComponent east = eastObject.AddComponent<TerrainComponent>();
        east.TerrainData = new TerrainData(5, new Vector3(4f, 10f, 4f));

        GameObject farObject = new("Far");
        farObject.Transform.WorldPosition = new Vector3(40f, 0f, 0f);
        TerrainComponent far = farObject.AddComponent<TerrainComponent>();
        far.TerrainData = new TerrainData(5, new Vector3(4f, 10f, 4f));

        TerrainComponent[] all = [west, east, far];
        west.FindNeighbour(all, TerrainEdge.Right).Should().BeSameAs(east);
        east.FindNeighbour(all, TerrainEdge.Left).Should().BeSameAs(west);
        west.FindNeighbour(all, TerrainEdge.Left).Should().BeNull("the west edge is the world's end");
        west.FindNeighbour(all, TerrainEdge.Top).Should().BeNull();

        west.TerrainData.SetHeight(4, 2, 6f);
        west.StitchTo(east, TerrainEdge.Right);

        east.TerrainData.GetHeight(0, 2).Should().Be(3f);
        west.Mesh.Should().NotBeNull();
        east.Mesh.Should().NotBeNull("both grounds rebuild over their sewn edge");
    }
}
