namespace OvercoreEditorPhotino.Services;

/// <summary>Viewport numbers the status bar shows, read from the JavaScript bridge.</summary>
/// <param name="Connected">True while the frame socket is open.</param>
/// <param name="Fps">Frames displayed per second, averaged over the last sample window.</param>
/// <param name="Width">Width of the most recent frame in pixels.</param>
/// <param name="Height">Height of the most recent frame in pixels.</param>
/// <param name="Frames">Total frames displayed since the viewport mounted.</param>
/// <param name="Status">Bridge state: connecting, connected, disconnected, error or idle.</param>
/// <param name="Message">Message the bridge shows over the canvas, or an empty string.</param>
public sealed record ViewportStats(
    bool Connected,
    int Fps,
    int Width,
    int Height,
    long Frames,
    string Status,
    string Message);
