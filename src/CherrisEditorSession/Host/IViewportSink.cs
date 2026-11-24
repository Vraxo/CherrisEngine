namespace CherrisEditorSession.Host;

/// <summary>
/// Where the engine publishes rendered frames and reads the viewport size from.
/// </summary>
/// <remarks>
/// Implemented by the host so the engine does not need to know how frames travel (WebSocket here,
/// JS interop in a PhotinoX build).
/// </remarks>
public interface IViewportSink
{
    /// <summary>
    /// The viewport size in device pixels that the next frame should be rendered at.
    /// </summary>
    (int Width, int Height) ViewportSize { get; }

    /// <summary>
    /// Whether the viewport draws the editor's grid. Read by the render thread every frame, so a host may change
    /// it from whichever thread the toggle lands on.
    /// </summary>
    bool ShowGrid { get; }

    /// <summary>
    /// Whether the client is holding its look button. Read by the render thread every frame, so a host may change
    /// it from whichever thread the browser's message lands on.
    /// </summary>
    bool LookDragging { get; }

    /// <summary>
    /// Publishes one rendered frame.
    /// </summary>
    /// <param name="rgba">Tightly packed RGBA8 pixels in top-down row order, valid only for the call.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="sequence">Monotonic frame counter, used by consumers to drop stale frames.</param>
    void PublishFrame(ReadOnlySpan<byte> rgba, int width, int height, long sequence);
}
