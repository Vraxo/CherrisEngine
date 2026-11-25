namespace OvercoreEditorSession;

/// <summary>
/// Keeps an inspector toggle at the value the user chose until a snapshot taken after its command ran arrives.
/// Access from the UI thread only.
/// </summary>
public sealed class PendingComponentToggles
{
    private readonly Dictionary<(Guid ObjectId, string TypeName), Pending> _pending = new();
    private long _revision;

    private sealed record Pending(bool Enabled, long Revision, long? BeforeExecutionFrame);

    /// <summary>Begin an edit, replacing any older pending edit for the same component.</summary>
    public long Begin(Guid objectId, string typeName, bool enabled)
    {
        long revision = ++_revision;
        _pending[(objectId, typeName)] = new Pending(enabled, revision, null);
        return revision;
    }

    /// <summary>Use the pending value when there is one, otherwise the latest snapshot value.</summary>
    public bool Value(Guid objectId, string typeName, bool snapshotValue) =>
        _pending.TryGetValue((objectId, typeName), out Pending? pending) ? pending.Enabled : snapshotValue;

    /// <summary>Record the last published frame before this command completed on the engine thread.</summary>
    public void Acknowledge(Guid objectId, string typeName, long revision, long beforeExecutionFrame)
    {
        var key = (objectId, typeName);
        if (_pending.TryGetValue(key, out Pending? pending) && pending.Revision == revision)
        {
            _pending[key] = pending with { BeforeExecutionFrame = beforeExecutionFrame };
        }
    }

    /// <summary>Drop acknowledged edits once a later snapshot reflects the engine's actual result.</summary>
    public void Reconcile(long frame)
    {
        foreach (var entry in _pending.ToArray())
        {
            if (entry.Value.BeforeExecutionFrame is long before && frame > before)
            {
                _pending.Remove(entry.Key);
            }
        }
    }

    /// <summary>Cancel an edit that could not be queued, without cancelling a newer edit.</summary>
    public void Cancel(Guid objectId, string typeName, long revision)
    {
        var key = (objectId, typeName);
        if (_pending.TryGetValue(key, out Pending? pending) && pending.Revision == revision)
        {
            _pending.Remove(key);
        }
    }
}
