using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using Overcore.Utils;
using Xunit;

namespace Overcore.Tests.Scenes;

/// <summary>
/// The prefab asset layer: the guid a <c>.meta</c> sidecar keeps, the ids objects carry inside the asset,
/// and the link every object of an instance holds to where it came from.
/// </summary>
public class PrefabAssetTests
{
    [Fact]
    public void SavePrefab_WritesFileIds_AndEveryLoadedObjectKeepsItsLink()
    {
        using TempAssets assets = new();

        GameObject rope = new("Rope");
        GameObject knot = new("Knot");
        knot.Transform.Parent = rope.Transform;
        SceneSerializer serializer = new();

        List<GameObject> written = serializer.SavePrefab(rope, assets.Path("Rope.prefab"));

        written.Should().Equal(new[] { rope, knot }, "the file's order is what the ids are drawn from");
        File.ReadAllText(assets.Path("Rope.prefab")).Should().Contain("File: 1").And.Contain("File: 2");

        string guid = AssetMeta.GetOrCreate(assets.Path("Rope.prefab"));
        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        List<GameObject> roots = loader.LoadPrefab(assets.Path("Rope.prefab"));
        roots.Should().ContainSingle("the asset has one root");

        GameObject instance = roots[0];
        instance.Name.Should().Be("Rope");
        instance.PrefabSource.Should().Be(new PrefabSource(guid, 1));
        instance.Transform.Children.Should().ContainSingle()
            .Which.GameObject.PrefabSource.Should().Be(new PrefabSource(guid, 2));
    }

    [Fact]
    public void LoadPrefab_GivesFreshObjectsEachTime_NeverTheFilesIds()
    {
        using TempAssets assets = new();

        GameObject rope = new("Rope", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        SceneSerializer serializer = new();
        serializer.SavePrefab(rope, assets.Path("Rope.prefab"));

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        GameObject first = loader.LoadPrefab(assets.Path("Rope.prefab"))[0];
        GameObject second = loader.LoadPrefab(assets.Path("Rope.prefab"))[0];

        first.Id.Should().NotBe(rope.Id, "an instance is fresh objects, not the asset's");
        second.Id.Should().NotBe(first.Id, "two instances never share ids");
        first.PrefabSource.Should().Be(second.PrefabSource, "both point at the same asset, same object in it");
    }

    [Fact]
    public void SavingAScene_SaysThePrefabLink_AndLoadingItReadsItBack()
    {
        using TempAssets assets = new();

        GameObject crate = new("Crate")
        {
            PrefabSource = new PrefabSource("abc123", 7)
        };

        SceneSerializer serializer = new();
        string yaml = serializer.SerializeSceneToString([crate]);

        yaml.Should().Contain("Prefab:").And.Contain("Asset: abc123").And.Contain("File: 7");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        List<GameObject> loaded = loader.LoadSceneFromYaml(yaml);
        loaded.Should().ContainSingle().Which.PrefabSource.Should().Be(new PrefabSource("abc123", 7));
    }

    [Fact]
    public void SavingAScene_LeavesLooseObjectsUnlinked()
    {
        SceneSerializer serializer = new();
        string yaml = serializer.SerializeSceneToString([new GameObject("Loose")]);

        yaml.Should().NotContain("Prefab:", "an object that is part of nothing says nothing");
    }

    [Fact]
    public void AssetMeta_CreatesTheSidecarOnce_AndTheGuidSurvivesLosingIt()
    {
        using TempAssets assets = new();
        string path = assets.Path("Cube.prefab");
        File.WriteAllText(path, "GameObjects: []");

        string guid = AssetMeta.GetOrCreate(path);
        guid.Should().HaveLength(32, "a Unity-style guid is 32 hex characters");
        AssetMeta.GetOrCreate(path).Should().Be(guid, "the sidecar is read, not rewritten");
        AssetMeta.TryGet(path, out string read).Should().BeTrue();
        read.Should().Be(guid);

        File.Delete(path + AssetMeta.Suffix);
        AssetMeta.GetOrCreate(path).Should().Be(guid,
            "with no sidecar left, the path still derives the same guid");
    }
}

/// <summary>A scratch folder that goes away with the test. Shared by the prefab test classes.</summary>
internal sealed class TempAssets : IDisposable
{
    // System.IO.Path is spelled out: the class's own Path() method would shadow the type name.
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "overcore-prefab-tests-" + Guid.NewGuid().ToString("N"));

    public TempAssets() => Directory.CreateDirectory(_root);

    public string Path(string name) => System.IO.Path.Combine(_root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked scratch file is not worth failing a test over.
        }
    }
}
