using OpenTK.Graphics.OpenGL4;
using System.Runtime.InteropServices;

namespace Cherris.Rendering.Headless;

/// <summary>
/// One frame of the rendered game view, ready to be handed to an external UI.
/// </summary>
/// <remarks>
/// <see cref="Pixels"/> points at a buffer owned by <see cref="SceneFrameCapture"/> that is reused by
/// the next capture. Copy (or upload) the pixels before calling
/// <see cref="SceneFrameCapture.TryCapture"/> again.
/// </remarks>
public readonly struct SceneFrame
{
    public SceneFrame(byte[] pixels, int width, int height, int rowPitch, long sequence, bool isTopDown)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        RowPitch = rowPitch;
        Sequence = sequence;
        IsTopDown = isTopDown;
    }

    /// <summary>RGBA8 pixels, R first. Length is <see cref="RowPitch"/> * <see cref="Height"/>.</summary>
    public byte[] Pixels { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Bytes per row, which is <c>Width * 4</c> because the readback is tightly packed.</summary>
    public int RowPitch { get; }

    /// <summary>Increases by one for every captured frame, so a consumer can drop stale frames.</summary>
    public long Sequence { get; }

    /// <summary>
    /// True when row 0 is the top of the image (browser/canvas order). False means OpenGL order,
    /// where row 0 is the bottom.
    /// </summary>
    public bool IsTopDown { get; }
}

/// <summary>
/// Copies the renderer's final scene texture into a managed buffer through double-buffered pixel pack
/// buffers, for hosts that display the game view as an image instead of a native window.
/// </summary>
/// <remarks>
/// <para>
/// Each call issues a non-blocking <c>glReadPixels</c> into a pack buffer and then returns the pixels
/// of the <em>previous</em> frame, which were requested one call earlier. That one-frame delay keeps
/// the GPU from stalling on a synchronous read. On the first call no frame is available yet and
/// <see cref="TryCapture"/> returns false.
/// </para>
/// <para>
/// Requires a current OpenGL context on the calling thread: create it from the render thread and
/// dispose it from the same thread.
/// </para>
/// </remarks>
public sealed class SceneFrameCapture : IDisposable
{
    /// <summary>Bytes per pixel in the captured buffer: RGBA8.</summary>
    public const int BytesPerPixel = 4;

    private readonly bool _flipVertically;
    private readonly int[] _packBuffers = new int[2];

    private GCHandle _pinnedPixels;
    private byte[] _pixels = [];
    private int _framebuffer;
    private int _writeIndex;
    private int _pendingIndex = -1;
    private int _width;
    private int _height;
    private long _sequence;
    private bool _disposed;

    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
    /// <param name="flipVertically">
    /// When true (the default) the buffer is flipped into top-down, browser-friendly order.
    /// </param>
    public SceneFrameCapture(int width, int height, bool flipVertically = true)
    {
        _flipVertically = flipVertically;
        _framebuffer = GL.GenFramebuffer();
        EnsureCapacity(width, height);
    }

    /// <summary>Current capture width in pixels.</summary>
    public int Width => _width;

    /// <summary>Current capture height in pixels.</summary>
    public int Height => _height;

    /// <summary>Whether captured rows are flipped into top-down order.</summary>
    public bool FlipVertically => _flipVertically;

    /// <summary>
    /// Requests a readback of <paramref name="colorTexture"/> and returns the previous frame's pixels.
    /// </summary>
    /// <param name="colorTexture">
    /// The renderer's final scene texture, for example
    /// <c>((OpenTKRenderer)engine.Renderer).GetSceneTextureHandle()</c>.
    /// </param>
    /// <param name="width">Viewport width in pixels.</param>
    /// <param name="height">Viewport height in pixels.</param>
    /// <param name="frame">The captured frame, valid only until the next call.</param>
    /// <returns>False on the first call after construction or a resize, when no frame is ready yet.</returns>
    public bool TryCapture(int colorTexture, int width, int height, out SceneFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        frame = default;

        if (colorTexture == 0)
        {
            return false;
        }

        EnsureCapacity(width, height);

        bool hasFrame = false;

        if (_pendingIndex >= 0)
        {
            // Pull the pixels the GPU produced for the previous request into managed memory. This
            // waits on that request only; the read itself was queued a frame earlier, so the driver
            // has usually finished by now.
            GL.BindBuffer(BufferTarget.PixelPackBuffer, _packBuffers[_pendingIndex]);
            GL.GetBufferSubData(
                BufferTarget.PixelPackBuffer,
                IntPtr.Zero,
                (IntPtr)_pixels.Length,
                _pinnedPixels.AddrOfPinnedObject());

            if (_flipVertically)
            {
                FlipRows(_pixels, _width * BytesPerPixel, _height);
            }

            _pendingIndex = -1;
            hasFrame = true;

            frame = new SceneFrame(
                _pixels,
                _width,
                _height,
                _width * BytesPerPixel,
                ++_sequence,
                _flipVertically);
        }

        IssueReadback(colorTexture);

        return hasFrame;
    }

    /// <summary>
    /// Same as <see cref="TryCapture(int, int, int, out SceneFrame)"/>, for texture handles that the
    /// renderer exposes as <see cref="IntPtr"/> (for example
    /// <c>OpenTKRenderer.GetSceneTextureHandle()</c>).
    /// </summary>
    public bool TryCapture(IntPtr colorTexture, int width, int height, out SceneFrame frame)
    {
        return TryCapture(colorTexture.ToInt32(), width, height, out frame);
    }

    /// <summary>
    /// Reverses row order in place, turning OpenGL's bottom-up pixels into top-down pixels.
    /// </summary>
    public static void FlipRows(Span<byte> pixels, int rowBytes, int height)
    {
        if (rowBytes <= 0 || height <= 1)
        {
            return;
        }

        int required = rowBytes * height;

        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"Buffer holds {pixels.Length} bytes but {required} are required.",
                nameof(pixels));
        }

        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Span<byte> topRow = pixels.Slice(top * rowBytes, rowBytes);
            Span<byte> bottomRow = pixels.Slice(bottom * rowBytes, rowBytes);

            for (int i = 0; i < rowBytes; i++)
            {
                byte swap = topRow[i];
                topRow[i] = bottomRow[i];
                bottomRow[i] = swap;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pinnedPixels.IsAllocated)
        {
            _pinnedPixels.Free();
        }

        for (int i = 0; i < _packBuffers.Length; i++)
        {
            if (_packBuffers[i] != 0)
            {
                GL.DeleteBuffer(_packBuffers[i]);
                _packBuffers[i] = 0;
            }
        }

        if (_framebuffer != 0)
        {
            GL.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        _pixels = [];
        _pendingIndex = -1;

        GC.SuppressFinalize(this);
    }

    private void IssueReadback(int colorTexture)
    {
        // Wrap the renderer's final scene texture in a framebuffer we control so the read is
        // well-defined regardless of what is bound when the capture runs.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            colorTexture,
            0);

        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            throw new InvalidOperationException(
                $"[SceneFrameCapture] Capture framebuffer incomplete: {status}");
        }

        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindBuffer(BufferTarget.PixelPackBuffer, _packBuffers[_writeIndex]);

        // With a pack buffer bound, the null pointer is an offset into that buffer: the call queues
        // the transfer instead of copying to client memory and blocking.
        GL.ReadPixels(0, 0, _width, _height, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        _pendingIndex = _writeIndex;
        _writeIndex = 1 - _writeIndex;
    }

    private void EnsureCapacity(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_packBuffers[0] != 0 && width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;

        if (_pinnedPixels.IsAllocated)
        {
            _pinnedPixels.Free();
        }

        _pixels = new byte[checked(width * height * BytesPerPixel)];
        _pinnedPixels = GCHandle.Alloc(_pixels, GCHandleType.Pinned);

        for (int i = 0; i < _packBuffers.Length; i++)
        {
            if (_packBuffers[i] == 0)
            {
                _packBuffers[i] = GL.GenBuffer();
            }

            GL.BindBuffer(BufferTarget.PixelPackBuffer, _packBuffers[i]);
            GL.BufferData(BufferTarget.PixelPackBuffer, _pixels.Length, IntPtr.Zero, BufferUsageHint.StreamRead);
        }

        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);

        // The pending read belonged to the old size.
        _pendingIndex = -1;
        _writeIndex = 0;
    }
}
