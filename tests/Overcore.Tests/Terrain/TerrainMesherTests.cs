using AwesomeAssertions;
using Overcore.Components;
using Overcore.Terrain;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers <see cref="TerrainMesher"/>: the geometry a heightfield becomes, chunk by chunk.
/// </summary>
public class TerrainMesherTests
{
    [Fact]
    public void Build_ProducesOneVertexPerGridPoint_AndTwoTrianglesPerCell()
    {
        TerrainData data = new(5, new Vector3(4f, 2f, 4f));

        Mesh mesh = TerrainMesher.Build(data);

        mesh.Vertices.Should().HaveCount(25);
        mesh.Indices.Should().HaveCount(4 * 4 * 6);
        mesh.Vertices[0].Position.Should().Be(Vector3.Zero, "the terrain's origin is its corner");
        mesh.Vertices[24].Position.Should().Be(new Vector3(4f, 0f, 4f));
        foreach (Vertex vertex in mesh.Vertices)
        {
            vertex.Normal.Should().Be(Vector3.UnitY);
        }
    }

    [Fact]
    public void Build_CarriesHeightsAndPaintedTints()
    {
        TerrainData data = new(3, new Vector3(2f, 2f, 2f));
        data.SetHeight(2, 2, 1.5f);
        data.Layers[0].Tint = new Vector3(1f, 0f, 0f);

        Mesh mesh = TerrainMesher.Build(data);

        mesh.Vertices[8].Position.Y.Should().Be(1.5f);
        mesh.Vertices[8].Color.R.Should().Be(1f);
        mesh.Vertices[8].Color.G.Should().Be(0f);
    }

    [Fact]
    public void BuildChunks_CoverTheGroundWithoutGaps()
    {
        TerrainData data = new(9, new Vector3(8f, 2f, 8f));
        data.SetHeight(4, 4, 3f);

        List<Mesh> chunks = TerrainMesher.BuildChunks(data, 2);

        chunks.Should().HaveCount(4);
        foreach (Mesh chunk in chunks)
        {
            chunk.Vertices.Length.Should().Be(25, "each chunk spans 5×5 shared points");

            // The peak is the shared corner of all four chunks: every chunk must carry it, or the ground cracks.
            chunk.Vertices.Max(vertex => vertex.Position.Y).Should().Be(3f);
        }
    }

    [Fact]
    public void BuildTriangles_MatchesTheMesh()
    {
        TerrainData data = new(4, new Vector3(3f, 1f, 3f));

        List<(Vector3 A, Vector3 B, Vector3 C)> triangles = TerrainMesher.BuildTriangles(data);

        triangles.Should().HaveCount(3 * 3 * 2);
    }
}
