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
/// Covers terrain trees end to end: planting in the data, the merged forest mesh, the brush scatter, the
/// component's owned forest child, the scene file, and the NavMesh walking under the canopy.
/// </summary>
public class TerrainTreesTests
{
    private static TerrainData DataWithPrototype()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.AddTreePrototype(new TerrainTreePrototype("Pine", "Cube", Vector3.One));
        return data;
    }

    [Fact]
    public void Trees_RoundTripThroughThePackedForm()
    {
        TerrainData data = DataWithPrototype();
        data.AddTree(0, new Vector2(0.25f, 0.75f), 1.5f, 2f, 3f);
        data.AddTree(0, new Vector2(0.5f, 0.5f));

        TerrainData copy = DataWithPrototype();
        copy.SetTreesPacked(data.GetTreesPacked());

        copy.Trees.Should().HaveCount(2);
        copy.Trees[0].PrototypeIndex.Should().Be(0);
        copy.Trees[0].Position.Should().Be(new Vector2(0.25f, 0.75f));
        copy.Trees[0].Rotation.Should().Be(1.5f);
        copy.Trees[0].WidthScale.Should().Be(2f);
        copy.Trees[0].HeightScale.Should().Be(3f);
        copy.TreesVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PlantTrees_ScattersWithRoomToGrow_AndSkipsHoles()
    {
        TerrainData data = DataWithPrototype();

        int planted = TerrainBrush.PlantTrees(
            data, new Vector2(0.5f, 0.5f), 0.4f, prototypeIndex: 0, attempts: 200, minSpacing: 0.1f, new Random(42));

        planted.Should().BeGreaterThan(3);
        data.Trees.Should().HaveCount(planted);
        for (int a = 0; a < data.Trees.Count; a++)
        {
            Vector2.Distance(data.Trees[a].Position, new Vector2(0.5f, 0.5f)).Should().BeLessThanOrEqualTo(0.4f);
            for (int b = a + 1; b < data.Trees.Count; b++)
            {
                Vector2.Distance(data.Trees[a].Position, data.Trees[b].Position)
                    .Should().BeGreaterThanOrEqualTo(0.1f, "no two trees share a root ball");
            }
        }

        TerrainData barren = DataWithPrototype();
        for (int z = 0; z < barren.HolesResolution; z++)
        {
            for (int x = 0; x < barren.HolesResolution; x++)
            {
                barren.SetHole(x, z, true);
            }
        }

        TerrainBrush.PlantTrees(barren, new Vector2(0.5f, 0.5f), 0.4f, 0, 50, 0f, new Random(1))
            .Should().Be(0, "a tree cannot stand in a hole");
    }

    [Fact]
    public void EraseTrees_UprootsTheDisc()
    {
        TerrainData data = DataWithPrototype();
        data.AddTree(0, new Vector2(0.5f, 0.5f));
        data.AddTree(0, new Vector2(0.05f, 0.05f));

        TerrainBrush.EraseTrees(data, new Vector2(0.5f, 0.5f), 0.2f).Should().Be(1);
        data.Trees.Should().ContainSingle().Which.Position.Should().Be(new Vector2(0.05f, 0.05f));
    }

    [Fact]
    public void ForestMesher_StandsTreesOnTheGround_AndSkipsHoles()
    {
        TerrainData data = DataWithPrototype();
        data.SetHeight(5, 5, 4f);
        data.AddTree(0, new Vector2(0.5f, 0.5f), rotation: 0f, widthScale: 1f, heightScale: 2f);
        data.AddTree(0, new Vector2(0.1f, 0.1f));
        data.SetHole(1, 1, true); // the second tree's cell

        Mesh forest = TerrainForestMesher.Build(data, _ => Mesh.CreateCube())!;

        forest.Should().NotBeNull("one tree stands, on the raised middle");
        forest.Vertices.Should().HaveCount(24, "one cube tree; the hole tree is not drawn");
        forest.Vertices.Max(vertex => vertex.Position.Y).Should().BeApproximately(4f - 0.05f + 1f, 1e-3f,
            "the sampled height, sunk a root's depth, plus half the doubled cube");
        forest.Vertices.Min(vertex => vertex.Position.X).Should().BeApproximately(4.5f, 1e-3f);
    }

    [Fact]
    public void Component_GrowsAForestChild_ThatTheSceneFileLeavesOut()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        TerrainData data = DataWithPrototype();
        terrain.TerrainData = data;
        terrain.TreeMeshResolver = _ => Mesh.CreateCube();

        terrain.PlantTrees(new Vector2(0.5f, 0.5f), 0.3f, 0, attempts: 10, minSpacing: 0.05f, new Random(3))
            .Should().BeGreaterThan(0);

        terrain.TreeMesh.Should().NotBeNull();
        GameObject treesObject = ground.Transform.Children.Single().GameObject;
        treesObject.Name.Should().Be("Trees");
        TerrainComponent.IsTreeObject(treesObject).Should().BeTrue();

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);
        yaml.Should().Contain("TreePrototypes");
        yaml.Should().Contain("Trees");
        yaml.Should().NotContain("TerrainTrees", "the forest child is terrain property, never scene content");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();
        TerrainComponent loadedTerrain = loaded.GetComponent<TerrainComponent>()!;

        loadedTerrain.TerrainData.TreePrototypes.Should().ContainSingle();
        loadedTerrain.TerrainData.Trees.Should().HaveCount(data.Trees.Count);
        loaded.Transform.Children.Should().BeEmpty(
            "no resolver, no forest mesh, no child: the loaded forest waits for its meshes like any named asset");
    }

    [Fact]
    public void NavMeshBake_WalksUnderTheTrees_NotOverThem()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        TerrainData data = DataWithPrototype();
        terrain.TerrainData = data;
        terrain.TreeMeshResolver = _ => Mesh.CreateCube();
        terrain.PlantTrees(new Vector2(0.5f, 0.5f), 0.3f, 0, attempts: 10, minSpacing: 0.05f, new Random(3));

        GameObject treesObject = ground.Transform.Children.Single().GameObject;
        NavMeshBuildSettings settings = new();

        NavMeshData withTrees = NavMeshBuilder.BuildFromGameObjects([ground, treesObject], settings);
        NavMeshData groundOnly = NavMeshBuilder.BuildFromGameObjects([ground], settings);

        withTrees.Polygons.Should().HaveCount(groundOnly.Polygons.Count,
            "the bake sees the ground alone; the forest is scenery");
    }
}
