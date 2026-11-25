using Overcore.RenderingInterface;
using System.Diagnostics;

namespace Overcore.Rendering.Headless;

/// <summary>
/// An <see cref="IGameWindow"/> for hosts that do not own a native window, such as an editor or runtime
/// whose UI lives in a webview (PhotinoX/Blazor) and displays the game view as a streamed image.
/// </summary>
/// <remarks>
/// <para>
/// There is no swap chain: <see cref="SwapBuffers"/> is only the frame-pacing gate, and
/// <see cref="ProcessEvents"/> only starts a new input frame and applies commands queued through
/// <see cref="HeadlessInput"/>. The host is responsible for running the loop, for calling
/// <see cref="Resize"/> when the on-screen viewport changes size, and for publishing the rendered
/// pixels with <see cref="SceneFrameCapture"/>.
/// </para>
/// <para>
/// The OpenGL context is owned by <see cref="HeadlessBackend"/>; this type never touches OpenGL,
/// so it can be constructed and resized from any thread.
/// </para>
/// </remarks>
public sealed class HeadlessGameWindow : IGameWindow
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _nextFrameTime;
    private bool _exists = true;
    private bool _disposed;

    public HeadlessGameWindow(int width, int height, Func<bool>? isViewportActive = null)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        IsViewportActive = isViewportActive ?? (static () => false);

        Input.ClearState();
        Input.OnLockStateChanged += OnInputLockStateChanged;
    }

    /// <inheritdoc />
    public float Width { get; private set; }

    /// <inheritdoc />
    public float Height { get; private set; }

    /// <inheritdoc />
    public bool Exists => _exists && !_disposed;

    /// <inheritdoc />
    public bool IsMouseLocked
    {
        get => Input.IsMouseLocked;
        set => Input.IsMouseLocked = value;
    }

    /// <inheritdoc />
    public Func<bool> IsViewportActive { get; set; }

    /// <summary>
    /// Frames per second the render loop is throttled to. Zero or less means "never throttle".
    /// </summary>
    /// <remarks>
    /// A real window paces itself through buffer swaps. Without a swap chain the loop would spin at
    /// 100% CPU, so pacing is done here. The default matches a typical 60 Hz display.
    /// </remarks>
    public double TargetFrameRate { get; set; } = 60;

    /// <summary>Integer viewport width, which is what the renderer and the capture buffer use.</summary>
    public int PixelWidth => (int)MathF.Round(Width);

    /// <summary>Integer viewport height, which is what the renderer and the capture buffer use.</summary>
    public int PixelHeight => (int)MathF.Round(Height);

    /// <inheritdoc />
    public event Action? Resized;

    /// <summary>
    /// Raised when game code changes <see cref="Input.IsMouseLocked"/>. The host should map this to
    /// the webview's pointer lock (for example <c>element.requestPointerLock()</c>).
    /// </summary>
    public event Action<bool>? MouseLockChanged;

    /// <summary>
    /// Changes the render target size. Call this when the webview viewport changes size.
    /// Sub-pixel changes are ignored so a CSS resize animation cannot thrash the framebuffers.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        float newWidth = Math.Max(1, width);
        float newHeight = Math.Max(1, height);

        if (MathF.Abs(newWidth - Width) < 0.5f && MathF.Abs(newHeight - Height) < 0.5f)
        {
            return;
        }

        Width = newWidth;
        Height = newHeight;
        Resized?.Invoke();
    }

    /// <summary>
    /// Starts a new input frame and applies input forwarded from the host since the previous frame.
    /// </summary>
    public void ProcessEvents()
    {
        Input.FrameStarted();
        HeadlessInput.ApplyPendingInput();
    }

    /// <summary>
    /// Blocks until the next frame is due. This replaces the display's vertical sync.
    /// </summary>
    public void SwapBuffers()
    {
        double now = _clock.Elapsed.TotalSeconds;

        if (TargetFrameRate <= 0)
        {
            _nextFrameTime = now;
            return;
        }

        double frameBudget = 1.0 / TargetFrameRate;

        if (_nextFrameTime <= 0)
        {
            _nextFrameTime = now;
        }

        double wait = (_nextFrameTime + frameBudget) - now;

        if (wait > 0)
        {
            // Sleep the bulk of the wait, then spin the last millisecond so pacing is not quantized
            // by the OS timer resolution. The spin is short enough to stay off a full core.
            int sleepMilliseconds = (int)((wait - 0.001) * 1000.0);

            if (sleepMilliseconds > 0)
            {
                Thread.Sleep(sleepMilliseconds);
            }

            while (_clock.Elapsed.TotalSeconds < _nextFrameTime + frameBudget)
            {
                Thread.SpinWait(64);
            }
        }

        _nextFrameTime += frameBudget;

        // If a debugger, GC pause or a stalled consumer pushed us far behind, resync the schedule
        // instead of sprinting through several frames to catch up.
        now = _clock.Elapsed.TotalSeconds;

        if (now - _nextFrameTime > frameBudget)
        {
            _nextFrameTime = now;
        }
    }

    /// <summary>Stops the game loop created by the engine.</summary>
    public void Close()
    {
        _exists = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _exists = false;
        Input.OnLockStateChanged -= OnInputLockStateChanged;

        GC.SuppressFinalize(this);
    }

    private void OnInputLockStateChanged(bool locked)
    {
        MouseLockChanged?.Invoke(locked);
    }
}
