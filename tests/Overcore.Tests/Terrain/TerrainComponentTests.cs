using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Serialization;
using Overcore.Terrain;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers the <see cref="Overcore.Components.Terrain"/> component and its collider: the mesh it builds, the
/// ground scripts and physics queries meet, and the scene file the ground survives.
/// </summary>
public class TerrainComponentTests
{
    [Fact]
    public void Rebuild_GivesTheObjectADrawableMesh()
    {
        GameObject ground = new("Ground");
        Overcore.Components.Terrain terrain = ground.AddComponent<Overcore.Components.Terrain>();
        terrain.TerrainData = new TerrainData(5, new Vector3(4f, 2f, 4f));

        terrain.Rebuild();

        MeshRenderer renderer = ground.GetComponent<MeshRenderer>()!;
        renderer.Should().NotBeNull();
        renderer.Mesh.Vertices.Should().HaveCount(25);
        terrain.Mesh.Should().BeSameAs(renderer.Mesh);
    }

    [Fact]
    public void Sculpt_UpdatesTheDrawnMesh()
    {
        GameObject ground = new("Ground");
        Overcore.Components.Terrain terrain = ground.AddComponent<Overcore.Components.Terrain>();
        terrain.TerrainData = new TerrainData(11, new Vector3(10f, 10f, 10f));
        terrain.Rebuild();
        Mesh before = terrain.Mesh!;

        terrain.Sculpt(new Vector2(0.5f, 0.5f), 0.3f, 2f, TerrainBrushMode.Raise);

        terrain.Mesh.Should().NotBeSameAs(before);
        terrain.Mesh!.Vertices.Max(vertex => vertex.Position.Y).Should().BeApproximately(2f, 1e-3f);
    }

    [Fact]
    public void PhysicsRaycast_MeetsTheGround()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        Overcore.Components.Terrain terrain = ground.AddComponent<Overcore.Components.Terrain>();
        terrain.TerrainData = new TerrainData(11, new Vector3(10f, 10f, 10f));
        terrain.TerrainData.SetHeight(5, 5, 2f);
        ground.AddComponent<TerrainCollider>();

        Scene scene = new(string.Empty, [ground]);
        scene.Start(physics, audio);

        // Over the raised middle, from above, straight down.
        Physics.Raycast(new Vector3(5f, 10f, 5f), -Vector3.UnitY, out RaycastHit hit).Should().BeTrue();
        hit.Point.Y.Should().BeApproximately(2f, 0.01f);
        hit.Normal.Y.Should().BeGreaterThan(0.5f);
        hit.Collider.Should().BeOfType<TerrainCollider>();

        // Flat corner ground is at zero.
        Physics.Raycast(new Vector3(1f, 10f, 1f), -Vector3.UnitY, out RaycastHit flat).Should().BeTrue();
        flat.Point.Y.Should().BeApproximately(0f, 0.01f);

        // A ray off the footprint meets nothing.
        Physics.Raycast(new Vector3(50f, 10f, 50f), -Vector3.UnitY, out _).Should().BeFalse();
    }

    [Fact]
    public void SceneYaml_RoundTripsTheGround()
    {
        GameObject ground = new("Ground");
        Overcore.Components.Terrain terrain = ground.AddComponent<Overcore.Components.Terrain>();
        TerrainData data = new(9, new Vector3(30f, 12f, 30f));
        data.SetHeight(4, 4, 3.25f);
        data.AddLayer(new TerrainLayer("Rock", "Textures/rock.png", new Vector3(0.5f, 0.4f, 0.35f)));
        TerrainBrush.ApplyPaint(data, new Vector2(0.5f, 0.5f), 0.3f, 1f, layerIndex: 1);
        terrain.TerrainData = data;
        ground.AddComponent<TerrainCollider>();

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);

        yaml.Should().Contain("Terrain");
        yaml.Should().Contain("Heights");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();

        Overcore.Components.Terrain loadedTerrain = loaded.GetComponent<Overcore.Components.Terrain>()!;
        loadedTerrain.Should().NotBeNull();
        loadedTerrain.TerrainData.HeightmapResolution.Should().Be(9);
        loadedTerrain.TerrainData.Size.Should().Be(new Vector3(30f, 12f, 30f));
        loadedTerrain.TerrainData.GetHeight(4, 4).Should().BeApproximately(3.25f, 1e-5f);
        loadedTerrain.TerrainData.Layers.Should().HaveCount(2);
        loadedTerrain.TerrainData.Layers[1].Name.Should().Be("Rock");
        loadedTerrain.TerrainData.GetDominantLayer(0.5f, 0.5f).Should().Be(1);
        loaded.GetComponent<TerrainCollider>().Should().NotBeNull();
    }

    private sealed class StubTexture(string name) : Overcore.Rendering.ITexture
    {
        public string Name { get; } = name;
        public object GetBackendHandle() => Name;
        public void Dispose() { }
    }

    private sealed class StubResources : Overcore.Rendering.IResourceManager
    {
        public Dictionary<string, StubTexture> Textures { get; } = [];
        public void LoadInitialAssets() { }
        public Overcore.Components.Mesh GetMesh(string name) => null!;
        public Overcore.Rendering.ITexture GetTexture(string name) =>
            Textures.TryGetValue(name, out StubTexture? texture) ? texture : Textures[name] = new StubTexture(name);
        public Overcore.Components.Skybox GetSkybox(string name) => null!;
        public AudioClip GetAudioClip(string name) => null!;
        public void Dispose() { }
    }

    [Fact]
    public void SceneYaml_LoadResolvesEachLayersTexture()
    {
        GameObject ground = new("Ground");
        Overcore.Components.Terrain terrain = ground.AddComponent<Overcore.Components.Terrain>();
        TerrainData data = new(5, new Vector3(4f, 2f, 4f));
        data.Layers[0].TextureName = "Textures/grass.png";
        data.AddLayer(new TerrainLayer("Rock", "Textures/rock.png", Vector3.One));
        terrain.TerrainData = data;

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);

        StubResources resources = new();
        SceneLoader loader = new(resources);
        ComponentDeserializer.RegisterFactories(loader, resources);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();

        Overcore.Components.Terrain loadedTerrain = loaded.GetComponent<Overcore.Components.Terrain>()!;
        ((StubTexture)loadedTerrain.GetLayerTexture(0)!).Name.Should().Be("Textures/grass.png");
        ((StubTexture)loadedTerrain.GetLayerTexture(1)!).Name.Should().Be("Textures/rock.png");
        loadedTerrain.GetLayerTexture(2).Should().BeNull("there is no third layer to resolve");
    }

    [Fact]
    public void ComponentCatalog_OffersTerrain()
    {
        ComponentCatalog.Find("Terrain")!.ComponentType.Should().Be(typeof(Overcore.Components.Terrain));
        ComponentCatalog.Find("Terrain Collider")!.ComponentType.Should().Be(typeof(TerrainCollider));
    }
}
