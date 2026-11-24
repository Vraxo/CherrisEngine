namespace CherrisEditorSession;

/// <summary>
/// Keeps a row's reset state — the arrow that appears once a value leaves its default, and the value itself —
/// at what the user just caused until the engine has confirmed it. Access from the UI thread only.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is published every fifteenth frame and polled by the UI, so a row that only reads the snapshot
/// lags the click by a noticeable fraction of a second: the arrow cannot appear until a new snapshot is polled,
/// and stays after a reset until a new one says the value is back. This holds the user's own answer until a
/// snapshot taken <em>after the command ran</em> arrives, the way <see cref="PendingComponentToggles"/> holds a
/// checkbox, and carries the value a reset is putting back so the field changes with the arrow.
/// </para>
/// <para>
/// A snapshot taken before the command ran must not undo the click, and the snapshot's frame is not enough to
/// tell the two apart: a snapshot can be published between the click and the next frame's command drain. So the
/// answer holds until <see cref="Acknowledge"/> reports the frame the command ran on, and only a snapshot after
/// that frame is trusted. A command that never reports back is let go after
/// <see cref="MaxHoldFrames"/> frames, so a closed session cannot leave a row stuck.
/// </para>
/// </remarks>
public sealed class PendingResetStates
{
    /// <summary>
    /// How long an unanswered edit is shown: eight snapshots at the session's fifteen-frame interval, so a
    /// command that never arrives cannot pin a row to the user's guess.
    /// </summary>
    public const long MaxHoldFrames = 120;

    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private long _revision;
    private long _reconciledFrame = long.MinValue;

    private sealed record Pending(
        bool CanReset,
        long Revision,
        long BegunAtFrame,
        string? Text,
        bool Acknowledged,
        long? BeforeExecutionFrame);

    /// <summary>
    /// Begin an edit, replacing any older pending edit for the same row, and return the revision that
    /// <see cref="Acknowledge"/> and <see cref="Cancel"/> match against.
    /// </summary>
    /// <param name="key">The row: one property, or one transform row.</param>
    /// <param name="canReset">Whether the arrow shows after this edit.</param>
    /// <param name="frame">The frame of the newest snapshot the UI has read, which the edit was made against.</param>
    /// <param name="text">
    /// The value the row shows until the edit is confirmed, or null to keep showing the snapshot's own text. Only
    /// a reset knows its result in advance; a typed or dragged value is already in the field the user holds.
    /// </param>
    /// <returns>The revision for this edit.</returns>
    public long Begin(string key, bool canReset, long frame, string? text = null)
    {
        long revision = ++_revision;
        _pending[key] = new Pending(
            canReset, revision, frame, text, Acknowledged: false, BeforeExecutionFrame: null);
        return revision;
    }

    /// <summary>Use the pending state when there is one, otherwise the latest snapshot value.</summary>
    /// <param name="key">The row the arrow belongs to.</param>
    /// <param name="snapshotValue">Whether the newest snapshot says the row differs from its default.</param>
    public bool Value(string key, bool snapshotValue) =>
        TryUsable(key, out Pending? pending) ? pending!.CanReset : snapshotValue;

    /// <summary>
    /// The value the row shows while the edit is unconfirmed, or null when the snapshot's own text shows. A reset
    /// carries the default, so the field goes back the moment the arrow is clicked instead of a snapshot later.
    /// </summary>
    /// <param name="key">The row the value belongs to.</param>
    public string? Text(string key) => TryUsable(key, out Pending? pending) ? pending!.Text : null;

    /// <summary>
    /// Record that the command ran on the render thread, with the frame of the last snapshot published before it.
    /// </summary>
    /// <param name="key">The row the arrow belongs to.</param>
    /// <param name="revision">The revision <see cref="Begin"/> returned; an older edit is ignored.</param>
    /// <param name="beforeExecutionFrame">The newest snapshot frame that cannot yet show the edit's result.</param>
    public void Acknowledge(string key, long revision, long beforeExecutionFrame)
    {
        if (_pending.TryGetValue(key, out Pending? pending) && pending.Revision == revision)
        {
            _pending[key] = pending with { Acknowledged = true, BeforeExecutionFrame = beforeExecutionFrame };
        }
    }

    /// <summary>
    /// Note the frame of the snapshot the UI has just read, and let go of the edits it can now confirm: one taken
    /// after the command ran, or one that has held for longer than <see cref="MaxHoldFrames"/>.
    /// </summary>
    /// <param name="frame">The frame of the snapshot the UI has just read.</param>
    public void Reconcile(long frame)
    {
        _reconciledFrame = Math.Max(_reconciledFrame, frame);

        foreach (var entry in _pending.ToArray())
        {
            Pending pending = entry.Value;
            bool confirmed = pending.Acknowledged
                ? pending.BeforeExecutionFrame is long before && frame > before
                : frame - pending.BegunAtFrame > MaxHoldFrames;

            if (confirmed)
            {
                _pending.Remove(entry.Key);
            }
        }
    }

    /// <summary>Cancel an edit that could not be queued, without cancelling a newer edit.</summary>
    /// <param name="key">The row the arrow belongs to.</param>
    /// <param name="revision">The revision <see cref="Begin"/> returned.</param>
    public void Cancel(string key, long revision)
    {
        if (_pending.TryGetValue(key, out Pending? pending) && pending.Revision == revision)
        {
            _pending.Remove(key);
        }
    }

    /// <summary>True while this row's answer still outranks the snapshot.</summary>
    private bool TryUsable(string key, out Pending? pending)
    {
        if (!_pending.TryGetValue(key, out Pending? found) || found is null)
        {
            pending = null;
            return false;
        }

        pending = found;

        // Unanswered: the click holds until a command reports back, within the hold limit. Answered: it holds
        // until a snapshot is read that was taken after the command ran.
        if (found.Acknowledged)
        {
            return found.BeforeExecutionFrame is long before && _reconciledFrame <= before;
        }

        return _reconciledFrame == long.MinValue || _reconciledFrame - found.BegunAtFrame <= MaxHoldFrames;
    }
}
