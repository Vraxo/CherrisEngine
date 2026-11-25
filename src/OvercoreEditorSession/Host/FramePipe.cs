using System.Buffers;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Single-slot hand-off for encoded frames between the render thread and the socket send loop.
/// </summary>
/// <remarks>
/// <para>
/// The producer never blocks: publishing replaces the slot, returning the superseded buffer to
/// <see cref="ArrayPool{T}"/> so a slow or stalled client costs a dropped frame instead of growing a
/// queue. Buffers handed to <see cref="Publish"/> become owned by the pipe and are returned to the pool
/// after the consumer sends them, or by <see cref="Dispose"/> if they were never taken.
/// </para>
/// <para>
/// Nothing is copied here: the caller fills a pooled buffer (frame header plus pixels) and passes it in.
/// </para>
/// </remarks>
public sealed class FramePipe : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);

    private byte[]? _latest;
    private int _latestLength;
    private int _pending;
    private bool _disposed;

    /// <summary>Number of frames the consumer has taken. Useful for diagnostics.</summary>
    public long Delivered { get; private set; }

    /// <summary>Number of frames that were superseded before the consumer took them.</summary>
    public long Dropped { get; private set; }

    /// <summary>
    /// Publishes a frame, taking ownership of <paramref name="buffer"/>. Call from the render thread.
    /// </summary>
    public void Publish(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, buffer.Length);

        byte[]? replaced;

        lock (_gate)
        {
            if (_disposed)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }

            replaced = _latest;
            _latest = buffer;
            _latestLength = length;

            if (replaced is not null)
            {
                Dropped++;
            }
        }

        if (replaced is not null)
        {
            ArrayPool<byte>.Shared.Return(replaced);
        }

        if (Interlocked.Exchange(ref _pending, 1) == 0)
        {
            // A ready frame that is replaced while one is queued is not dismissed by the consumer:
            // it takes the newest buffer the next time it wakes up.
            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Raced with Dispose; the buffer is cleaned up there.
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    /// <summary>
    /// Waits for the next frame. The returned buffer must be returned to <see cref="ArrayPool{T}"/>
    /// by the caller once it has been sent.
    /// </summary>
    public async ValueTask<ArraySegment<byte>?> TakeAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

        Interlocked.Exchange(ref _pending, 0);

        lock (_gate)
        {
            if (_latest is null)
            {
                return null;
            }

            ArraySegment<byte> frame = new(_latest, 0, _latestLength);
            _latest = null;
            Delivered++;
            return frame;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        byte[]? outstanding = null;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            outstanding = _latest;
            _latest = null;
        }

        if (outstanding is not null)
        {
            ArrayPool<byte>.Shared.Return(outstanding);
        }

        _signal.Dispose();
    }
}
