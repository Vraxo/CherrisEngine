using AwesomeAssertions;
using Overcore.Core;
using Overcore.Serialization;
using Overcore.Utils;
using Xunit;

namespace Overcore.Tests.Scenes;

/// <summary>
/// The link SavePrefab writes: every object it stamps into the file comes back stamped to the asset,
/// at the ids the file gave it — and keeps them across saves. The override model stands on this.
/// </summary>
public class PrefabLinkStampingTests
{
    [Fact]
    public void SavePrefab_StampsEveryWrittenObject_WithTheAssetsGuidAndFileIds()
    {
        using TempAssets assets = new();
        GameObject root = new("Rope");
        GameObject knot = new("Knot");
        knot.Transform.Parent = root.Transform;
        SceneSerializer serializer = new();

        string path = assets.Path("Rope.prefab");
        List<GameObject> written = serializer.SavePrefab(root, path);

        string guid = AssetMeta.GetOrCreate(path);

        written.Should().HaveCount(2);
        root.PrefabSource.Should().Be(new PrefabSource(guid, 1), "the save links the objects it wrote");
        knot.PrefabSource.Should().Be(new PrefabSource(guid, 2));
    }

    [Fact]
    public void SavePrefab_KeepsTheIdsOfItsOwnInstances_AcrossSaves()
    {
        using TempAssets assets = new();
        GameObject root = new("Rope");
        GameObject knot = new("Knot");
        knot.Transform.Parent = root.Transform;
        SceneSerializer serializer = new();

        string path = assets.Path("Rope.prefab");
        serializer.SavePrefab(root, path);
        PrefabSource? first = root.PrefabSource;

        root.Name = "Rope Two";
        serializer.SavePrefab(root, path);

        root.PrefabSource.Should().Be(first, "the ids an instance carries are the ids the file keeps");
    }

    [Fact]
    public void SavePrefab_AssignsFreshIdsToObjectsLinkedToAnotherAsset()
    {
        using TempAssets assets = new();
        GameObject root = new("Rope") { PrefabSource = new PrefabSource("0123456789abcdef0123456789abcdef", 7) };
        SceneSerializer serializer = new();

        string path = assets.Path("Rope.prefab");
        serializer.SavePrefab(root, path);

        string guid = AssetMeta.GetOrCreate(path);
        guid.Should().NotBe("0123456789abcdef0123456789abcdef", "the asset has a guid of its own");
        root.PrefabSource.Should().Be(new PrefabSource(guid, 1),
            "a stranger's id is replaced by a fresh one in this file's numbering");
    }
}
