using AwesomeAssertions;
using OvercoreEditorSession.Host;
using System.Buffers;
using Xunit;

namespace OvercoreEditorSession.Tests;

public class FramePipeTests
{
    [Fact]
    public async Task TakeAsync_ReturnsThePublishedFrame()
    {
        using FramePipe pipe = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8);
        buffer[0] = 7;

        pipe.Publish(buffer, 8);

        ArraySegment<byte> frame = await TakeAsync(pipe);

        frame.Count.Should().Be(8);
        frame.Array![0].Should().Be(7);

        ArrayPool<byte>.Shared.Return(frame.Array!);
    }

    [Fact]
    public async Task Publish_KeepsOnlyTheNewestFrame()
    {
        using FramePipe pipe = new();

        byte[] first = ArrayPool<byte>.Shared.Rent(4);
        byte[] second = ArrayPool<byte>.Shared.Rent(4);
        first[0] = 1;
        second[0] = 2;

        pipe.Publish(first, 4);
        pipe.Publish(second, 4);

        ArraySegment<byte> frame = await TakeAsync(pipe);

        frame.Array![0].Should().Be(2);
        pipe.Dropped.Should().Be(1);

        ArrayPool<byte>.Shared.Return(frame.Array!);
    }

    [Fact]
    public async Task Delivered_CountsTakenFrames()
    {
        using FramePipe pipe = new();

        for (int i = 0; i < 3; i++)
        {
            pipe.Publish(ArrayPool<byte>.Shared.Rent(4), 4);
            ArraySegment<byte> frame = await TakeAsync(pipe);
            ArrayPool<byte>.Shared.Return(frame.Array!);
        }

        pipe.Delivered.Should().Be(3);
        pipe.Dropped.Should().Be(0);
    }

    [Fact]
    public async Task TakeAsync_HonorsCancellation()
    {
        using FramePipe pipe = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> take = async () => await pipe.TakeAsync(cancellation.Token);

        await take.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Publish_AfterDisposeDoesNotThrow()
    {
        FramePipe pipe = new();
        pipe.Dispose();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(4);

        Action publish = () => pipe.Publish(buffer, 4);

        publish.Should().NotThrow();
    }

    private static async Task<ArraySegment<byte>> TakeAsync(FramePipe pipe)
    {
        ArraySegment<byte>? frame = await pipe.TakeAsync(CancellationToken.None);
        return frame ?? throw new InvalidOperationException("Expected the pipe to yield a frame.");
    }
}
