using CherrisEditorWeb;

namespace CherrisEditorPhotino.Services;

/// <summary>
/// Scene previews for the content browser: reads each scene file once, off the UI thread, and keeps the result
/// until the file changes on disk.
/// </summary>
/// <remarks>
/// A folder can hold any number of scenes and the browser re-reads it every few seconds, so parsing is not done
/// while a panel is rendering. <see cref="LoadAsync"/> takes the files that are new or have a newer write time,
/// parses those only, and raises <see cref="Changed"/> once at the end; handlers run on the background thread
/// and marshal back with <c>InvokeAsync</c>, like <see cref="SceneStore"/>'s.
/// </remarks>
public sealed class ScenePreviewStore
{
    private readonly Dictionary<string, Preview> _previews = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private int _loading;

    /// <summary>Raised on a background thread after a load has changed what <see cref="Get"/> returns.</summary>
    public event Action? Changed;

    /// <summary>
    /// The preview for a file, or null when it is not a scene, could not be read, or has not been loaded yet.
    /// </summary>
    public SceneThumbnail? Get(string path)
    {
        lock (_gate)
        {
            return _previews.TryGetValue(path, out Preview preview) ? preview.Thumbnail : null;
        }
    }

    /// <summary>True once the file has been looked at, whether or not it turned out to be a scene.</summary>
    public bool IsKnown(string path)
    {
        lock (_gate)
        {
            return _previews.ContainsKey(path);
        }
    }

    /// <summary>
    /// Reads the files that are new or have changed since they were last read. A load already in flight wins, so
    /// the timer that calls this cannot pile them up.
    /// </summary>
    /// <param name="paths">Candidate files; anything that is not a scene is remembered as such and not retried.</param>
    public async Task LoadAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] candidates = [.. paths];

        if (candidates.Length == 0 || Interlocked.Exchange(ref _loading, 1) == 1)
        {
            return;
        }

        try
        {
            bool changed = await Task.Run(() => Load(candidates)).ConfigureAwait(false);

            if (changed)
            {
                Changed?.Invoke();
            }
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    /// <summary>Runs on a worker thread: stat, skip what is unchanged, parse the rest.</summary>
    private bool Load(string[] paths)
    {
        bool changed = false;

        foreach (string path in paths)
        {
            DateTime written;
            long length;

            try
            {
                FileInfo file = new(path);

                if (!file.Exists)
                {
                    continue;
                }

                written = file.LastWriteTimeUtc;
                length = file.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            lock (_gate)
            {
                if (_previews.TryGetValue(path, out Preview known) && known.Written == written && known.Length == length)
                {
                    continue;
                }
            }

            SceneThumbnail? thumbnail = Parse(path);

            lock (_gate)
            {
                _previews[path] = new Preview(written, length, thumbnail);
            }

            changed = true;
        }

        return changed;
    }

    private static SceneThumbnail? Parse(string path)
    {
        try
        {
            return SceneThumbnail.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>What was read, and what the file looked like at the time.</summary>
    private sealed record Preview(DateTime Written, long Length, SceneThumbnail? Thumbnail);
}
