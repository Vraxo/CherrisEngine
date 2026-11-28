using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Navigation;
using Overcore.Serialization;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers chunked ground: the mesher's levels of detail, the component's chunk children and their
/// distance-driven LOD, and the scene file and NavMesh bake treating chunks as the ground they are.
/// </summary>
public class TerrainChunkTests
{
    [Fact]
    public void ChunkMesher_StepsDownByLevel()
    {
        TerrainData data = new(9, new Vector3(8f, 4f, 8f));

        Mesh full = TerrainMesher.BuildChunk(data, 0, 0, 9, 9);
        Mesh half = TerrainMesher.BuildChunk(data, 0, 0, 9, 9, step: 2);
        Mesh quarter = TerrainMesher.BuildChunk(data, 0, 0, 9, 9, step: 4);

        full.Vertices.Should().HaveCount(81);
        half.Vertices.Should().HaveCount(25);
        quarter.Vertices.Should().HaveCount(9);
        half.Indices.Length.Should().Be(full.Indices.Length / 4);
        quarter.Indices.Length.Should().Be(full.Indices.Length / 16);

        Action badStep = () => TerrainMesher.BuildChunk(data, 0, 0, 9, 9, step: 3);
        badStep.Should().Throw<ArgumentOutOfRangeException>("8 cells do not divide by 3");
    }

    [Fact]
    public void ChunkedTerrain_DrawsChunks_AndLowersDetailWithDistance()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(9, new Vector3(8f, 4f, 8f));
        terrain.LodDistance = 5f;
        terrain.ChunksPerSide = 2;

        terrain.UsesChunks.Should().BeTrue();
        terrain.Mesh.Should().BeNull("the single mesh gives way to the chunks");
        terrain.ChunkMeshes.Should().HaveCount(4);
        terrain.ChunkMeshes[0]!.Vertices.Should().HaveCount(25, "a 5×5-point chunk at full detail");

        GameObject chunkObject = ground.Transform.Children
            .Select(child => child.GameObject)
            .First(go => TerrainComponent.IsChunkObject(go));
        chunkObject.GetComponent<MeshRenderer>()!.Enabled.Should().BeTrue();
        ground.GetComponent<MeshRenderer>()!.Enabled.Should().BeFalse(
            "the object's own renderer falls silent so nothing mistakes it for the ground");

        terrain.UpdateLod(new Vector3(4f, 1f, 4f));
        terrain.ChunkLevels.Should().AllBeEquivalentTo(0, "the camera stands on the ground");

        terrain.UpdateLod(new Vector3(400f, 1f, 4f));
        terrain.ChunkLevels.Should().OnlyContain(level => level > 0);
        terrain.ChunkMeshes[0]!.Vertices.Length.Should().BeLessThan(25,
            "far chunks draw fewer vertices: that is the point of them");
    }

    [Fact]
    public void ChunkedTerrain_SavesAsOneTerrain_AndBakesWhole()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(9, new Vector3(8f, 4f, 8f));
        terrain.ChunksPerSide = 2;
        terrain.Rebuild();

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);
        yaml.Should().Contain("ChunksPerSide");
        yaml.Should().NotContain("TerrainChunk", "chunk children are terrain property, never scene content");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();
        TerrainComponent loadedTerrain = loaded.GetComponent<TerrainComponent>()!;
        loadedTerrain.ChunksPerSide.Should().Be(2);
        loadedTerrain.Rebuild();
        loadedTerrain.ChunkMeshes.Should().HaveCount(4);

        NavMeshData chunked = NavMeshBuilder.BuildFromGameObjects([ground], new NavMeshBuildSettings());
        chunked.Polygons.Should().NotBeEmpty(
            "a NavMesh bake reads the chunked ground from its data, not from its silent renderer");
    }
}
