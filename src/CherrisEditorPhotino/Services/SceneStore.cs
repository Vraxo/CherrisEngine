using CherrisEditorWeb;

namespace CherrisEditorPhotino.Services;

/// <summary>
/// The scene snapshot every panel reads, refreshed on one timer instead of one timer per component.
/// </summary>
/// <remarks>
/// The render thread publishes immutable snapshots; this only samples the newest one and raises
/// <see cref="Changed"/> when it differs. Handlers run on the timer thread, so components marshal back to
/// the renderer with <c>InvokeAsync</c>.
/// </remarks>
public sealed class SceneStore : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly EditorHostService _host;
    private readonly Timer _timer;

    private SceneSnapshot? _current;
    private SceneSnapshot? _treeFor;
    private IReadOnlyList<SceneTreeNode> _roots = [];
    private bool _disposed;

    /// <param name="host">Session that publishes the snapshots.</param>
    public SceneStore(EditorHostService host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    /// <summary>Raised on the timer thread when a new snapshot arrived.</summary>
    public event Action? Changed;

    /// <summary>The newest snapshot, or null before the first frame.</summary>
    public SceneSnapshot? Current => Volatile.Read(ref _current);

    /// <summary>
    /// Tree form of <see cref="Current"/>. The tree is rebuilt only when the snapshot changes, so the
    /// hierarchy panel can read it on every render.
    /// </summary>
    public IReadOnlyList<SceneTreeNode> Roots
    {
        get
        {
            SceneSnapshot? snapshot = Current;

            if (snapshot is null)
            {
                return [];
            }

            if (!ReferenceEquals(snapshot, Volatile.Read(ref _treeFor)))
            {
                _roots = SceneTree.Build(snapshot.Nodes);
                Volatile.Write(ref _treeFor, snapshot);
            }

            return _roots;
        }
    }

    /// <summary>Stops the timer; called when the host shuts down.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Poll()
    {
        SceneSnapshot? latest = _host.LatestScene;

        if (latest is null || ReferenceEquals(latest, Current))
        {
            return;
        }

        Volatile.Write(ref _current, latest);
        Changed?.Invoke();
    }
}
