using Overcore.Serialization;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Core;

/// <summary>
/// A serializable handle to a <c>.prefab</c> asset in the project. Scripts and components declare properties of
/// type <see cref="PrefabAsset"/> so designers can drag a prefab onto an inspector slot and spawn instances at
/// runtime with <see cref="Instantiate()"/>.
/// </summary>
public sealed class PrefabAsset : IEquatable<PrefabAsset>
{
    /// <summary>The 32-character lowercase hex GUID stored in the prefab's <c>.meta</c> sidecar, when known.</summary>
    public string AssetGuid { get; }

    /// <summary>The project-relative or absolute path to the <c>.prefab</c> file, normalized to forward slashes.</summary>
    public string AssetPath { get; }

    /// <summary>Display name of the prefab asset (file name without extension).</summary>
    public string Name => !string.IsNullOrWhiteSpace(AssetPath)
        ? Path.GetFileNameWithoutExtension(AssetPath)
        : (string.IsNullOrWhiteSpace(AssetGuid) ? "Prefab" : AssetGuid);

    public PrefabAsset(string assetGuid, string assetPath)
    {
        AssetGuid = assetGuid?.Trim() ?? string.Empty;
        AssetPath = NormalizePath(assetPath);
    }

    /// <summary>Creates a <see cref="PrefabAsset"/> from a file path, reading or deriving its <see cref="AssetGuid"/>.</summary>
    public static PrefabAsset FromPath(string path, string? projectRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string resolved = ResolveDiskPath(path, projectRoot);
        string guid = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved)
            ? AssetMeta.GetOrCreate(resolved)
            : string.Empty;

        string relative = MakeRelativeIfUnderRoot(path, resolved, projectRoot);
        return new PrefabAsset(guid, relative);
    }

    /// <summary>Creates a <see cref="PrefabAsset"/> from a GUID and optional fallback path.</summary>
    public static PrefabAsset? FromGuidOrPath(string? assetGuid, string? assetPath, string? projectRoot = null)
    {
        string guid = assetGuid?.Trim() ?? string.Empty;
        string path = NormalizePath(assetPath);

        if (guid.Length == 0 && path.Length == 0)
        {
            return null;
        }

        string? root = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : ProjectFiles.ProjectRoot;

        if (guid.Length > 0 && !string.IsNullOrWhiteSpace(root))
        {
            string? found = AssetMeta.FindByGuid(root, guid);
            if (!string.IsNullOrWhiteSpace(found))
            {
                path = NormalizePath(Path.GetRelativePath(root, found));
            }
        }
        else if (guid.Length == 0 && path.Length > 0)
        {
            string resolved = ResolveDiskPath(path, root);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
            {
                guid = AssetMeta.GetOrCreate(resolved);
            }
        }

        return new PrefabAsset(guid, path);
    }

    /// <summary>
    /// Loads the prefab hierarchy as detached <see cref="GameObject"/> roots (not yet added to a scene).
    /// </summary>
    public IReadOnlyList<GameObject> Load(IResourceManager? resourceManager = null, string? projectRoot = null)
    {
        string? yaml = ReadYaml(projectRoot);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return [];
        }

        string guid = AssetGuid;
        if (string.IsNullOrWhiteSpace(guid))
        {
            string resolved = ResolveDiskPath(AssetPath, projectRoot);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
            {
                guid = AssetMeta.GetOrCreate(resolved);
            }
        }

        SceneLoader loader = new(resourceManager ?? FallbackResourceManager.Instance);
        return loader.LoadPrefabFromString(yaml, string.IsNullOrWhiteSpace(guid) ? null : guid);
    }

    /// <summary>
    /// Instantiates the prefab into <paramref name="targetScene"/> (or <see cref="Scene.Active"/> when omitted)
    /// and returns the primary root <see cref="GameObject"/>.
    /// </summary>
    public GameObject? Instantiate(Scene? targetScene = null, IResourceManager? resourceManager = null)
    {
        IReadOnlyList<GameObject> roots = Load(resourceManager);
        if (roots.Count == 0)
        {
            return null;
        }

        Scene? scene = targetScene ?? Scene.Active;
        if (scene is not null)
        {
            foreach (GameObject root in roots)
            {
                AddHierarchyToScene(scene, root);
            }
        }

        return roots[0];
    }

    /// <summary>
    /// Instantiates the prefab at the given world <paramref name="position"/> and <paramref name="rotation"/>.
    /// </summary>
    public GameObject? Instantiate(
        Vector3 position,
        Quaternion rotation,
        Transform? parent = null,
        Scene? targetScene = null,
        IResourceManager? resourceManager = null)
    {
        IReadOnlyList<GameObject> roots = Load(resourceManager);
        if (roots.Count == 0)
        {
            return null;
        }

        GameObject primary = roots[0];
        if (parent is not null)
        {
            primary.Transform.SetParent(parent, worldPositionStays: false);
        }

        primary.Transform.Position = position;
        primary.Transform.Rotation = rotation;

        Scene? scene = targetScene ?? parent?.GameObject?.scene ?? Scene.Active;
        if (scene is not null)
        {
            foreach (GameObject root in roots)
            {
                AddHierarchyToScene(scene, root);
            }
        }

        return primary;
    }

    /// <summary>
    /// Instantiates the prefab under <paramref name="parent"/>.
    /// </summary>
    public GameObject? Instantiate(
        Transform? parent,
        bool worldPositionStays = false,
        Scene? targetScene = null,
        IResourceManager? resourceManager = null)
    {
        IReadOnlyList<GameObject> roots = Load(resourceManager);
        if (roots.Count == 0)
        {
            return null;
        }

        GameObject primary = roots[0];
        if (parent is not null)
        {
            primary.Transform.SetParent(parent, worldPositionStays);
        }

        Scene? scene = targetScene ?? parent?.GameObject?.scene ?? Scene.Active;
        if (scene is not null)
        {
            foreach (GameObject root in roots)
            {
                AddHierarchyToScene(scene, root);
            }
        }

        return primary;
    }

    internal string? ResolveExistingFilePath(string? projectRoot = null)
    {
        string? root = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : ProjectFiles.ProjectRoot;

        if (!string.IsNullOrWhiteSpace(AssetGuid) && !string.IsNullOrWhiteSpace(root))
        {
            string? byGuid = AssetMeta.FindByGuid(root, AssetGuid);
            if (!string.IsNullOrWhiteSpace(byGuid) && File.Exists(byGuid))
            {
                return byGuid;
            }
        }

        string resolved = ResolveDiskPath(AssetPath, root);
        return !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved) ? resolved : null;
    }

    private string? ReadYaml(string? projectRoot)
    {
        string? diskPath = ResolveExistingFilePath(projectRoot);
        if (!string.IsNullOrWhiteSpace(diskPath))
        {
            return File.ReadAllText(diskPath);
        }

        if (!string.IsNullOrWhiteSpace(AssetPath))
        {
            using Stream? stream = ProjectFiles.Open(AssetPath);
            if (stream is not null)
            {
                using StreamReader reader = new(stream);
                return reader.ReadToEnd();
            }
        }

        return null;
    }

    private static void AddHierarchyToScene(Scene scene, GameObject root)
    {
        scene.AddGameObject(root);
        foreach (Transform child in root.Transform.Children.ToList())
        {
            AddHierarchyToScene(scene, child.GameObject);
        }
    }

    private static string ResolveDiskPath(string path, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        string? root = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : ProjectFiles.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(root))
        {
            string candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return ProjectFiles.Find(path) ?? string.Empty;
    }

    private static string MakeRelativeIfUnderRoot(string originalPath, string resolvedPath, string? projectRoot)
    {
        string? root = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : ProjectFiles.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(resolvedPath))
        {
            string fullRoot = Path.GetFullPath(root);
            string fullResolved = Path.GetFullPath(resolvedPath);
            if (fullResolved.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePath(Path.GetRelativePath(fullRoot, fullResolved));
            }
        }

        return NormalizePath(originalPath);
    }

    internal static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');

    public bool Equals(PrefabAsset? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(AssetGuid) && !string.IsNullOrWhiteSpace(other.AssetGuid))
        {
            return string.Equals(AssetGuid, other.AssetGuid, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(AssetPath, other.AssetPath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is PrefabAsset other && Equals(other);

    public override int GetHashCode() =>
        !string.IsNullOrWhiteSpace(AssetGuid)
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(AssetGuid)
            : StringComparer.OrdinalIgnoreCase.GetHashCode(AssetPath);

    public override string ToString() =>
        !string.IsNullOrWhiteSpace(AssetPath) ? $"{Name} ({AssetPath})" : Name;

    /// <summary>
    /// Minimal headless resource manager used when <see cref="PrefabAsset.Load"/> is called outside an active
    /// renderer — provides primitive meshes without requiring an OpenGL or Veldrid context.
    /// </summary>
    private sealed class FallbackResourceManager : ResourceManagerBase
    {
        public static readonly FallbackResourceManager Instance = Create();

        private static FallbackResourceManager Create()
        {
            FallbackResourceManager manager = new();
            manager.LoadInitialAssets();
            return manager;
        }

        public override void LoadInitialAssets()
        {
            _meshes["Cube"] = Components.Mesh.CreateCube();
            _meshes["Plane"] = Components.Mesh.CreatePlane(20f);
        }

        public override ITexture GetTexture(string name) => null!;

        public override Skybox GetSkybox(string name) => null!;

        public override Components.AudioClip GetAudioClip(string name) => null!;

        public override void Dispose()
        {
        }
    }
}
