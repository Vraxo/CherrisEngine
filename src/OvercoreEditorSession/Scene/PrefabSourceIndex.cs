using Overcore.Components;
using Overcore.Core;
using Overcore.Utils;

namespace OvercoreEditorSession;

/// <summary>
/// The values a prefab asset's objects hold, kept warm so a linked object can be diffed against its source
/// on the thread that owns the scene. The asset is read through the session's loader and cached until the
/// file's write time moves — an Apply rewrites the file, so the next snapshot sees the new source.
/// </summary>
/// <remarks>
/// The diff is the whole overrides story's truth: nothing is recorded when an object is edited, and nothing
/// can go stale. An object whose asset cannot be found is simply not diffed — it shows as linked, with no
/// overrides claimed.
/// </remarks>
public sealed class PrefabSourceIndex
{
    private static readonly TimeSpan MissingRetry = TimeSpan.FromSeconds(2);

    private readonly Func<string, List<GameObject>> _loadPrefab;
    private readonly string? _projectRoot;
    private readonly Dictionary<string, Cache?> _caches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _missingAt = new(StringComparer.Ordinal);

    /// <param name="loadPrefab">The session's asset loader: prefab path in, fresh objects out.</param>
    /// <param name="projectRoot">The open project's folder, where the sidecars live.</param>
    public PrefabSourceIndex(Func<string, List<GameObject>> loadPrefab, string? projectRoot)
    {
        _loadPrefab = loadPrefab;
        _projectRoot = projectRoot;
    }

    /// <summary>Every value of the object that has drifted from its source.</summary>
    /// <param name="linked">The object in the scene; an unlinked object has nothing to differ from.</param>
    /// <returns>The drifted paths and the source's value for each, or an empty list.</returns>
    public IReadOnlyList<PrefabOverrideSnapshot> Diff(GameObject linked)
    {
        if (linked.PrefabSource is not { } source
            || ValuesFor(source.AssetGuid) is not { } byFileId
            || !byFileId.TryGetValue(source.FileId, out Dictionary<string, string>? sourceValues))
        {
            return [];
        }

        List<PrefabOverrideSnapshot> diff = [];

        foreach ((string path, string text) in PrefabValues.Collect(linked))
        {
            // A path the source does not carry at all is a structural change — an added component — and is
            // not an override this slice knows how to name yet (docs/prefabs.md).
            if (sourceValues.TryGetValue(path, out string? sourceText)
                && !String.Equals(text, sourceText, StringComparison.Ordinal))
            {
                diff.Add(new PrefabOverrideSnapshot(path, sourceText));
            }
        }

        return diff;
    }

    /// <summary>The asset's values by the id each object carries inside it, or null when it cannot be read.</summary>
    private Dictionary<int, Dictionary<string, string>>? ValuesFor(string assetGuid)
    {
        if (_projectRoot is null)
        {
            return null;
        }

        Cache? cache = _caches.TryGetValue(assetGuid, out Cache? found) ? found : null;
        string? path = cache?.Path;

        if (path is null)
        {
            if (_missingAt.TryGetValue(assetGuid, out DateTime missingAt)
                && DateTime.UtcNow - missingAt < MissingRetry)
            {
                return null;
            }

            path = AssetMeta.FindByGuid(_projectRoot, assetGuid);
        }

        if (path is null || !File.Exists(path))
        {
            _caches[assetGuid] = null;
            _missingAt[assetGuid] = DateTime.UtcNow;
            return null;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);

        if (cache is null || cache.WriteTime != writeTime)
        {
            cache = BuildCache(path, writeTime);
            _caches[assetGuid] = cache;
        }

        return cache.ByFileId;
    }

    private Cache BuildCache(string path, DateTime writeTime)
    {
        Dictionary<int, Dictionary<string, string>> byFileId = [];

        foreach (GameObject root in _loadPrefab(path))
        {
            foreach (GameObject go in Walk(root))
            {
                if (go.PrefabSource is { } source)
                {
                    byFileId[source.FileId] = PrefabValues.Collect(go);
                }
            }
        }

        return new Cache(path, writeTime, byFileId);
    }

    private static IEnumerable<GameObject> Walk(GameObject go)
    {
        yield return go;

        foreach (Transform child in go.Transform.Children)
        {
            foreach (GameObject descendant in Walk(child.GameObject))
            {
                yield return descendant;
            }
        }
    }

    /// <param name="Path">Where the asset was read from.</param>
    /// <param name="WriteTime">The file's write time when it was read; a newer one means re-read.</param>
    /// <param name="ByFileId">Each object's values, keyed by its id within the asset.</param>
    private sealed record Cache(string Path, DateTime WriteTime, Dictionary<int, Dictionary<string, string>> ByFileId);
}
