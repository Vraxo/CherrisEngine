using Cherris.Scripting;
using System.Security.Cryptography;
using System.Text;

namespace CherrisEditorSession;

/// <summary>
/// Watches the project's <c>Scripts/</c> folder and reports when its C# sources differ from the ones the session last
/// compiled, so a save in another editor is picked up without the project being reopened.
/// </summary>
/// <remarks>
/// <para>
/// A file system is noisy: one save can arrive as four events, an editor that writes a temporary file and renames it
/// arrives as three more, and a branch switch arrives as hundreds. None of that is reported. Events only restart a
/// quiet timer, and when it expires the folder is hashed and compared with the last hash — so a burst produces one
/// report, and a burst that did not change any bytes (a touch, a checkout restoring identical files, a thumbnail
/// written elsewhere in the project) produces none at all.
/// </para>
/// <para>
/// The hash covers the names and contents of every <c>.cs</c> file under <c>Scripts/</c>, which is the same set the
/// compiler reads: "changed" here means the next compile would see something different.
/// </para>
/// </remarks>
public sealed class ProjectScriptWatcher : IDisposable
{
    /// <summary>
    /// How long the folder has to be left alone before it is hashed. Long enough to swallow a save that arrives in
    /// pieces, short enough that saving and looking back at the editor feels immediate.
    /// </summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(400);

    private readonly string _projectRoot;
    private readonly Action _changed;
    private readonly Timer _quiet;
    private readonly object _gate = new();
    private readonly FileSystemWatcher? _watcher;

    private string _signature;
    private bool _disposed;

    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="changed">Raised on a worker thread when the sources differ from the last checked state. Called
    /// once per quiet period, never per event.</param>
    public ProjectScriptWatcher(string projectRoot, Action changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        _projectRoot = projectRoot;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _quiet = new Timer(_ => OnQuiet(), null, Timeout.Infinite, Timeout.Infinite);

        // The state the session has just compiled is the baseline: nothing is reported until it differs.
        _signature = Signature();

        try
        {
            _watcher = CreateWatcher();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // A file system that will not be watched is not a reason to refuse to open the project: the menu item is
            // still there, and the session says nothing rather than failing at the launcher.
            _watcher = null;
        }
    }

    /// <summary>
    /// Hashes the folder and raises the callback when it differs from the last check.
    /// </summary>
    /// <remarks>
    /// This is what the timer calls. It is public so a caller can decide when the comparison happens instead of
    /// waiting for a quiet period, which is also how the tests can be exact rather than timing-dependent.
    /// </remarks>
    /// <returns>True when the sources changed and the callback ran.</returns>
    public bool Check()
    {
        string signature = Signature();
        bool changed;

        lock (_gate)
        {
            changed = !String.Equals(signature, _signature, StringComparison.Ordinal);
            _signature = signature;
        }

        if (changed)
        {
            _changed();
        }

        return changed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _watcher?.Dispose();
        _quiet.Dispose();
    }

    /// <summary>
    /// One watcher on the project, so a <c>Scripts/</c> folder that does not exist yet is still noticed when it
    /// appears. The recursive watch costs one handle per directory, and the events are filtered by the hash.
    /// </summary>
    private FileSystemWatcher CreateWatcher()
    {
        FileSystemWatcher watcher = new(_projectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                NotifyFilters.Size,

            // A branch switch can overflow the default buffer. The error handler turns that into one more check, so a
            // missed event is a delay rather than a lost change.
            InternalBufferSize = 64 * 1024
        };

        watcher.Changed += (_, _) => Restart();
        watcher.Created += (_, _) => Restart();
        watcher.Deleted += (_, _) => Restart();
        watcher.Renamed += (_, _) => Restart();
        watcher.Error += (_, _) => Restart();
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    /// <summary>Pushes the comparison out to the end of the burst, whichever event arrived last.</summary>
    private void Restart()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _quiet.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnQuiet()
    {
        try
        {
            Check();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder changed under the hash (a file deleted between listing and reading). The next event, or the
            // next check, sees where it settled.
        }
    }

    /// <summary>SHA-256 over the sorted <c>.cs</c> file names and contents under <c>Scripts/</c>.</summary>
    private string Signature()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        string folder = Path.Combine(_projectRoot, ScriptTemplate.Folder);

        if (!Directory.Exists(folder))
        {
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        string[] files =
        [
            .. Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
        ];

        foreach (string file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file)));
            hash.AppendData([0]);

            try
            {
                hash.AppendData(File.ReadAllBytes(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file caught mid-write. Its name is in the hash either way, so the write landing is another
                // reported change rather than a silent one.
                hash.AppendData([0xFF]);
            }

            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
