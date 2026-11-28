using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers ground detail: painted density in the data, the deterministic tuft scatter, and the
/// component's owned detail child surviving a scene round trip.
/// </summary>
public class TerrainDetailTests
{
    private static TerrainData DataWithDetail()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f), alphamapResolution: 11);
        data.AddDetailPrototype(new TerrainDetailPrototype("Grass", string.Empty, new Vector3(0.4f, 0.6f, 0.3f)));
        return data;
    }

    [Fact]
    public void DetailBrush_GrowsAndThinsDensity()
    {
        TerrainData data = DataWithDetail();

        int changed = TerrainBrush.ApplyDetail(data, new Vector2(0.5f, 0.5f), 0.3f, 0.5f, 0);
        changed.Should().BeGreaterThan(0);
        data.GetDetailDensity(5, 5, 0).Should().BeApproximately(0.5f, 1e-3f);
        data.GetDetailDensity(0, 0, 0).Should().Be(0f);

        TerrainBrush.ApplyDetail(data, new Vector2(0.5f, 0.5f), 0.3f, 5f, 0);
        data.GetDetailDensity(5, 5, 0).Should().Be(1f, "density stops at thickest");

        TerrainBrush.ApplyDetail(data, new Vector2(0.5f, 0.5f), 0.3f, -5f, 0);
        data.GetDetailDensity(5, 5, 0).Should().Be(0f, "and a thinning stroke stops at bare");
    }

    [Fact]
    public void DetailMesher_ScattersDeterministically_ByDensity()
    {
        TerrainData data = DataWithDetail();
        data.SetDetailDensity(5, 5, 0, 1f);
        data.SetDetailDensity(6, 5, 0, 0.5f);

        Mesh first = TerrainDetailMesher.Build(data)!;
        Mesh second = TerrainDetailMesher.Build(data)!;

        first.Should().NotBeNull();
        // One full pixel grows four tufts, the half pixel two; a tuft is two quads of four vertices.
        first.Vertices.Should().HaveCount((4 + 2) * 8);
        second.Vertices.Select(vertex => vertex.Position).Should().Equal(
            first.Vertices.Select(vertex => vertex.Position),
            "the same ground grows the same field twice: the scatter's dice are the pixel's, not the build's");
        first.Vertices.Min(vertex => vertex.Position.Y).Should().BeApproximately(-0.02f, 1e-3f,
            "tufts root at the ground, a breath under it");
    }

    [Fact]
    public void Component_GrowsADetailChild_ThatTheSceneFileLeavesOut()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        TerrainData data = DataWithDetail();
        terrain.TerrainData = data;

        terrain.ApplyDetail(new Vector2(0.5f, 0.5f), 0.3f, 1f, 0).Should().BeGreaterThan(0);

        terrain.DetailMesh.Should().NotBeNull();
        GameObject detailObject = ground.Transform.Children.Single().GameObject;
        detailObject.Name.Should().Be("Detail");
        TerrainComponent.IsDetailObject(detailObject).Should().BeTrue();

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);
        yaml.Should().Contain("DetailPrototypes");
        yaml.Should().Contain("Detail");
        yaml.Should().NotContain("TerrainDetail", "the detail child is terrain property, never scene content");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();
        TerrainComponent loadedTerrain = loaded.GetComponent<TerrainComponent>()!;

        loadedTerrain.TerrainData.DetailPrototypes.Should().ContainSingle();
        loadedTerrain.TerrainData.GetDetailDensity(5, 5, 0).Should().BeApproximately(1f, 1e-3f);
        loadedTerrain.Rebuild();
        loadedTerrain.DetailMesh.Should().NotBeNull("detail needs no assets, so a loaded terrain grows at once");
    }
}
