using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace OvercoreEditorSession.Host;

/// <summary>
/// One browser client: sends the greeting, streams binary frames and turns text messages into
/// <see cref="IViewportInputSink"/> calls.
/// </summary>
public sealed class ViewerConnection : IAsyncDisposable
{
    private const int ReceiveBufferSize = 8 * 1024;
    private const int MaxCommandBytes = 64 * 1024;

    private readonly WebSocket _socket;
    private readonly IViewportInputSink _input;
    private readonly string _greeting;
    private readonly FramePipe _frames = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StringBuilder _commandBuffer = new();
    private readonly Task _pump;

    public ViewerConnection(WebSocket socket, IViewportInputSink input, string greeting)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _greeting = greeting ?? string.Empty;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Completes when the socket closes or the connection is disposed.</summary>
    public Task Completion => _pump;

    /// <summary>
    /// Queues one frame for this client. Safe to call from the render thread; frames that the client
    /// cannot keep up with are dropped rather than queued.
    /// </summary>
    /// <param name="rgba">Tightly packed RGBA8, top-down.</param>
    public void PublishFrame(ReadOnlySpan<byte> rgba, int width, int height, int sequence)
    {
        int payloadLength = checked(ViewportProtocol.HeaderSize + width * height * 4);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(payloadLength);

        try
        {
            ViewportProtocol.WriteFrameHeader(buffer, width, height, sequence);
            rgba[..(width * height * 4)].CopyTo(buffer.AsSpan(ViewportProtocol.HeaderSize));
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        _frames.Publish(buffer, payloadLength);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The pump already reports its own failures.
        }

        _frames.Dispose();

        try
        {
            _socket.Dispose();
        }
        catch (Exception)
        {
            // Best effort.
        }

        _lifetime.Dispose();
    }

    private async Task PumpAsync()
    {
        CancellationToken cancellationToken = _lifetime.Token;

        try
        {
            await _socket
                .SendAsync(ViewportProtocol.ToUtf8Segment(_greeting), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);

            Task send = SendLoopAsync(cancellationToken);
            Task receive = ReceiveLoopAsync(cancellationToken);

            await Task.WhenAny(send, receive).ConfigureAwait(false);

            // One side finished (client closed, socket error): stop the other and observe both, so a
            // failure here cannot surface as an unobserved task exception.
            await _lifetime.CancelAsync().ConfigureAwait(false);

            try
            {
                await Task.WhenAll(send, receive).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the surviving loop is cancelled above.
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (WebSocketException)
        {
            // Client went away.
        }
        catch (ObjectDisposedException)
        {
            // Normal shutdown.
        }
        finally
        {
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ArraySegment<byte>? frame = await _frames.TakeAsync(cancellationToken).ConfigureAwait(false);

            if (frame is null || frame.Value.Array is null)
            {
                continue;
            }

            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket
                        .SendAsync(frame.Value, WebSocketMessageType.Binary, true, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame.Value.Array);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReceiveBufferSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await _socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _socket
                    .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text || result.Count <= 0)
            {
                continue;
            }

            _commandBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage)
            {
                if (_commandBuffer.Length > MaxCommandBytes)
                {
                    _commandBuffer.Clear();
                }

                continue;
            }

            string command = _commandBuffer.ToString();
            _commandBuffer.Clear();

            ViewportProtocol.TryReadCommand(command, _input);
        }
    }
}
