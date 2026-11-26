using Overcore.Animation;
using Overcore.Components;

namespace Overcore.Rendering;

/// <summary>
/// The cache an importer fills and a resource manager reads: every mesh a model carried, every skeleton one of its
/// skins described, and every clip an animation did — held once per file, whichever object in it asks first.
/// </summary>
/// <remarks>
/// <para>
/// Assets are cached under the qualified key a project names them by, <c>"Assets/Models/Hero.gltf#Head"</c>, which is
/// what a scene's <see cref="MeshRenderer.MeshName"/> writes. A bare name, <c>"Head"</c>, is also registered when no
/// earlier import has claimed it, so hand-written content can say <c>"Head"</c>; the first import to bring a name owns
/// it, and a later file with a name of its own stays reachable by its qualified key rather than shadowing silently.
/// Ownership is explicit (<see cref="_bareOwners"/>) instead of depending on the order a dictionary happens to
/// enumerate, which is what makes the same project import the same way twice.
/// </para>
/// <para>
/// Skeletons and clips are qualified the same way, because a project with two characters has two clips called
/// <c>"Idle"</c>.
/// </para>
/// </remarks>
public static class ModelAssets
{
    /// <summary>What one import produced, after it has been added to the cache.</summary>
    /// <param name="Path">The path the model was read from.</param>
    /// <param name="Model">The import itself: hierarchy, warnings, and every asset by its own name.</param>
    /// <param name="Meshes">Mesh keys — qualified, plus any bare name this import owns.</param>
    /// <param name="Skeletons">Skeleton keys, qualified the same way.</param>
    /// <param name="Clips">Clip keys, qualified the same way.</param>
    public sealed record Entry(
        string Path,
        ImportedModel Model,
        IReadOnlyDictionary<string, Components.Mesh> Meshes,
        IReadOnlyDictionary<string, Skeleton> Skeletons,
        IReadOnlyDictionary<string, AnimationClip> Clips);

    private static readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which path owns each bare asset name, so a second model cannot quietly take the first's.</summary>
    private static readonly Dictionary<string, string> _bareOwners = new(StringComparer.Ordinal);

    /// <summary>Raised after an import joins the cache, so a resource manager can mirror what it needs.</summary>
    public static event Action<Entry>? Imported;

    /// <summary>
    /// Imports a model once per path, returning the cached result on every later call. A failed read is not cached,
    /// so a file that was missing can be fixed and picked up without restarting the host.
    /// </summary>
    public static ImportedModel? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_byPath.TryGetValue(path, out Entry? cached))
        {
            return cached.Model;
        }

        ImportedModel? model = ModelImporter.Import(path);

        return model is null ? null : Adopt(path, model).Model;
    }

    /// <summary>Adds an already-imported model to the cache and returns the keys it is reachable by.</summary>
    public static Entry Adopt(string path, ImportedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Dictionary<string, Components.Mesh> meshes = Keyed(path, model.Meshes);
        Dictionary<string, Skeleton> skeletons = Keyed(path, model.Skeletons);
        Dictionary<string, AnimationClip> clips = Keyed(path, model.Clips);

        Entry entry = new(path, model, meshes, skeletons, clips);

        _byPath[path] = entry;
        Imported?.Invoke(entry);

        return entry;
    }

    /// <summary>The import for a path, or null when nothing was imported from it.</summary>
    public static Entry? Find(string path) =>
        _byPath.TryGetValue(path, out Entry? entry) ? entry : null;

    /// <summary>Looks a mesh up by the key a scene uses — qualified, or a bare name an import owns.</summary>
    public static Components.Mesh? FindMesh(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        // A qualified key names its file, so the file is read rather than searched for.
        int separator = key.LastIndexOf('#');

        if (separator > 0)
        {
            string path = key[..separator];

            return _byPath.TryGetValue(path, out Entry? owner) && owner.Meshes.TryGetValue(key, out Components.Mesh? mesh)
                ? mesh
                : null;
        }

        return _bareOwners.TryGetValue(key, out string? bareOwner)
            && _byPath.TryGetValue(bareOwner, out Entry? bareEntry)
            && bareEntry.Meshes.TryGetValue(key, out Components.Mesh? bareMesh)
                ? bareMesh
                : null;
    }

    /// <summary>A clip by its qualified key or a bare name an import owns, for a loader reading an <c>.anim</c> link.</summary>
    public static AnimationClip? FindClip(string key) => Find(key, entry => entry.Clips);

    /// <summary>A skeleton by its qualified key or a bare name an import owns.</summary>
    public static Skeleton? FindSkeleton(string key) => Find(key, entry => entry.Skeletons);

    /// <summary>A name qualified with the file it came from: <c>"Hero.gltf#Head"</c>.</summary>
    public static string Qualify(string path, string name) => $"{path}#{name}";

    /// <summary>Forgets every import and every claim on a bare name. Tests call this between cases.</summary>
    public static void Clear()
    {
        _byPath.Clear();
        _bareOwners.Clear();
    }

    private static T? Find<T>(string key, Func<Entry, IReadOnlyDictionary<string, T>> select)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        int separator = key.LastIndexOf('#');

        if (separator > 0)
        {
            string path = key[..separator];

            return _byPath.TryGetValue(path, out Entry? owner) && select(owner).TryGetValue(key, out T? asset)
                ? asset
                : null;
        }

        return _bareOwners.TryGetValue(key, out string? bareOwner)
            && _byPath.TryGetValue(bareOwner, out Entry? bareEntry)
            && select(bareEntry).TryGetValue(key, out T? bareAsset)
                ? bareAsset
                : null;
    }

    /// <summary>Keys each asset by its qualified name, and by its bare name when this import is the first to want it.</summary>
    private static Dictionary<string, T> Keyed<T>(string path, IReadOnlyDictionary<string, T> assets)
    {
        Dictionary<string, T> keys = new(StringComparer.Ordinal);

        foreach ((string name, T asset) in assets)
        {
            keys[Qualify(path, name)] = asset;

            if (_bareOwners.TryAdd(name, path))
            {
                keys[name] = asset;
            }
        }

        return keys;
    }
}
