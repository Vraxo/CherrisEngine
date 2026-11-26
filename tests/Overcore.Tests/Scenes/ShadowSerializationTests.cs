using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Rendering.Shadows;
using Overcore.Serialization;
using Xunit;

namespace Overcore.Tests.Scenes;

/// <summary>
/// The shadow settings through a real save and a real load.
/// </summary>
/// <remarks>
/// <para>
/// Written because two of these properties were silently lost in the first cut of the feature and nothing failed:
/// <see cref="Shadows"/> had no factory registered, so a scene containing one refused to load, and
/// <see cref="MeshRenderer.CastShadows"/> went through a dedicated serializer that wrote only the mesh and the material,
/// so a saved "off" came back "on". A file that says one thing while the running scene does another is the failure mode
/// that costs an afternoon, and it is invisible to a unit test that never round-trips.
/// </para>
/// </remarks>
public class ShadowSerializationTests
{
    private static SceneLoader Loader()
    {
        SceneLoader loader = new(null!);

        // A real resource source, not null: the MeshRenderer factory asks it for the mesh and the texture by name, so a
        // null here is a NullReferenceException inside the loader rather than a test failure with a message.
        ComponentDeserializer.RegisterFactories(loader, new TestResources());

        return loader;
    }

    /// <summary>
    /// Hands back one shared mesh and one shared texture for any name asked for.
    /// </summary>
    /// <remarks>
    /// Any name, rather than only "triangle", because the loader asks for the names the file records and this test is
    /// about the properties beside them. A stub that served one exact name would turn a rename into an unrelated
    /// failure.
    /// </remarks>
    private sealed class TestResources : IResourceManager
    {
        private readonly Mesh _mesh = Triangle();
        private readonly ITexture _texture = new FakeTexture();

        public void LoadInitialAssets() { }

        public Mesh GetMesh(string name) => _mesh;

        public ITexture GetTexture(string name) => _texture;

        public Skybox GetSkybox(string name) => throw new NotSupportedException("No shadow test loads a skybox.");

        public AudioClip GetAudioClip(string name) => throw new NotSupportedException("No shadow test loads audio.");

        public void Dispose() { }
    }

    [Fact]
    public void AShadowsComponentSurvivesASaveAndALoad()
    {
        GameObject gameObject = new("settings");
        gameObject.AddComponent(new Shadows { Distance = 220f, Cascades = 2 });

        string yaml = new SceneSerializer().SerializeSceneToString([gameObject]);
        List<GameObject> loaded = Loader().LoadSceneFromYaml(yaml);

        Shadows shadows = loaded[0].GetComponent<Shadows>()!;

        shadows.Distance.Should().Be(220f);
        shadows.Cascades.Should().Be(2);

        // Splits is [HideInInspector] and therefore never written; the Cascades setter has to rebuild it, which is the
        // property that makes a fresh instance compare equal to a loaded one.
        shadows.Splits.Should().Equal(CascadeSplits.Derive(2));
        shadows.FarDepths().Should().Equal(220f * (1f / 3f), 220f);
    }

    [Fact]
    public void ADefaultShadowsComponentWritesNothingItDoesNotNeedTo()
    {
        GameObject gameObject = new("settings");
        gameObject.AddComponent(new Shadows());

        string yaml = new SceneSerializer().SerializeSceneToString([gameObject]);

        // Every property matches a new instance, so the block is a name and nothing else. The empty mapping is what a
        // reader of the file should see, and it is what proves the defaults in the document are the defaults in code.
        yaml.Should().Contain("Shadows:");
        yaml.Should().NotContain("Distance:");
        yaml.Should().NotContain("Cascades:");
        yaml.Should().NotContain("Splits:");
    }

    [Fact]
    public void ACastModeSurvivesASaveAndALoad()
    {
        GameObject gameObject = new("crate");
        gameObject.AddComponent(new MeshRenderer
        {
            Mesh = Triangle(),
            MeshName = "triangle",
            Material = Untextured(),
            CastShadows = ShadowCastingMode.ShadowsOnly,
            ReceiveShadows = false
        });

        string yaml = new SceneSerializer().SerializeSceneToString([gameObject]);
        List<GameObject> loaded = Loader().LoadSceneFromYaml(yaml);

        MeshRenderer renderer = loaded[0].GetComponent<MeshRenderer>()!;

        renderer.CastShadows.Should().Be(ShadowCastingMode.ShadowsOnly);
        renderer.ReceiveShadows.Should().BeFalse();
    }

    [Fact]
    public void ADefaultCastModeIsNotWrittenAndComesBackAsTheDefault()
    {
        GameObject gameObject = new("crate");
        gameObject.AddComponent(new MeshRenderer
        {
            Mesh = Triangle(),
            MeshName = "triangle",
            Material = Untextured()
        });

        string yaml = new SceneSerializer().SerializeSceneToString([gameObject]);

        yaml.Should().NotContain("CastShadows");
        yaml.Should().NotContain("ReceiveShadows");

        MeshRenderer renderer = Loader().LoadSceneFromYaml(yaml)[0].GetComponent<MeshRenderer>()!;

        renderer.CastShadows.Should().Be(ShadowCastingMode.On);
        renderer.ReceiveShadows.Should().BeTrue();
    }

    [Fact]
    public void ALightKeepsItsShadowSettings()
    {
        GameObject sun = new("sun");
        sun.AddComponent(new Light
        {
            Shadows = ShadowType.Hard,
            ShadowResolution = ShadowResolution.Medium,
            ShadowStrength = 0.6f,
            ShadowBias = 0.005f,
            ShadowNormalBias = 1.5f
        });

        string yaml = new SceneSerializer().SerializeSceneToString([sun]);
        Light loaded = Loader().LoadSceneFromYaml(yaml)[0].GetComponent<Light>()!;

        loaded.Shadows.Should().Be(ShadowType.Hard);
        loaded.ShadowResolution.Should().Be(ShadowResolution.Medium);
        loaded.ShadowStrength.Should().Be(0.6f);
        loaded.ShadowBias.Should().Be(0.005f);
        loaded.ShadowNormalBias.Should().Be(1.5f);
    }

    /// <summary>A material with no texture, which is all a round-trip of the mesh needs.</summary>
    private static Material Untextured() => new(new FakeTexture(), "none");

    private sealed class FakeTexture : ITexture
    {
        public object GetBackendHandle() => new object();

        public void Dispose() { }
    }

    private static Mesh Triangle() => new(
        [
            new Vertex(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, default, default),
            new Vertex(System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitZ, default, default),
            new Vertex(System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ, default, default)
        ],
        [0u, 1u, 2u]);
}
