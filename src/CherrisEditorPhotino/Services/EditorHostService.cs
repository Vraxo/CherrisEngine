using CherrisEditorWeb;
using CherrisEditorWeb.Host;
using System.Net;
using System.Net.Sockets;

namespace CherrisEditorPhotino.Services;

/// <summary>
/// Owns the in-process editor host: the HTTP/WebSocket listener, the render thread and the latest scene
/// snapshot. The Blazor components read UI state from here, while pixels reach the viewport canvas over
/// the socket that this host serves.
/// </summary>
/// <remarks>
/// The window's main thread belongs to the native Photino message loop, so the engine runs on its own
/// background thread. That is the opposite arrangement from the browser host, where the engine owns the
/// main thread; the render thread never touches Blazor state, it only publishes immutable snapshots.
/// </remarks>
public sealed class EditorHostService : IDisposable
{
    private readonly WebEditorHost _host;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _exited = new(false);

    private volatile string? _failure;
    private bool _disposed;

    private EditorHostService(WebEditorHost host)
    {
        _host = host;
        _thread = new Thread(RunHost)
        {
            IsBackground = true,
            Name = "Cherris Engine"
        };
    }

    /// <summary>Starts the editor host on a background thread and waits for its listener.</summary>
    /// <param name="args">Command line, forwarded for <c>--port</c>, <c>--width</c>, <c>--height</c> and <c>--fps</c>.</param>
    public static EditorHostService Start(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        WebEditorOptions options = BuildOptions(args);
        string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        EditorHostService service = new(new WebEditorHost(options, webRoot));

        service._thread.Start();
        service._host.WaitForStart(TimeSpan.FromSeconds(15));

        if (service._host.StartFailed)
        {
            service._failure = $"The editor host could not listen on port {options.Port}.";
        }

        return service;
    }

    /// <summary>URL of the in-process host, or null while it is not listening.</summary>
    public string? Url => _host.Url;

    /// <summary>WebSocket endpoint the viewport script connects to for frames.</summary>
    public string WebSocketUrl
    {
        get
        {
            string? url = _host.Url;

            if (url is null)
            {
                return string.Empty;
            }

            UriBuilder builder = new(new Uri(url))
            {
                Scheme = new Uri(url).Scheme == "https" ? "wss" : "ws",
                Path = "/ws"
            };

            return builder.Uri.ToString();
        }
    }

    /// <summary>Most recent scene snapshot from the render thread, or null before the first frame.</summary>
    public SceneSnapshot? LatestScene => _host.LatestScene;

    /// <summary>True while the render thread is still running.</summary>
    public bool IsRunning => !_exited.IsSet;

    /// <summary>Human-readable reason the host is not usable, or null when it is fine.</summary>
    public string? FailureMessage => _failure;

    /// <summary>Stops the render loop and releases the listener. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Request the stop first: the engine cleans up its GL objects while the context is still current,
        // and only then is it safe for the host to tear down the plumbing the render thread was using.
        _host.RequestStop();
        _exited.Wait(TimeSpan.FromSeconds(5));

        _host.Dispose();
        _exited.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RunHost()
    {
        try
        {
            int exitCode = _host.Run();

            if (exitCode != 0 && _failure is null)
            {
                _failure = "The renderer stopped. OpenGL 3.3 and the shader files under EditorResources/ " +
                    "are required; on a machine without a GPU driver use the desktop editor instead.";
            }
        }
        catch (Exception exception)
        {
            _failure = $"The editor host failed: {exception.Message}";
        }
        finally
        {
            _exited.Set();
        }
    }

    /// <summary>
    /// Rebuilds the host options from command-line arguments. Only the render options are forwarded, so
    /// flags meant for the Blazor builder (or for the framework's own configuration) are ignored, and a
    /// free port is picked unless one was requested explicitly.
    /// </summary>
    private static WebEditorOptions BuildOptions(string[] args)
    {
        List<string> forwarded = [];
        bool portRequested = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--port" or "--width" or "--height" or "--fps":
                    portRequested |= args[index] == "--port";
                    forwarded.Add(args[index]);

                    if (index + 1 < args.Length)
                    {
                        forwarded.Add(args[++index]);
                    }

                    break;
            }
        }

        WebEditorOptions parsed;

        try
        {
            parsed = WebEditorOptions.Parse([.. forwarded]);
        }
        catch (ArgumentException)
        {
            parsed = WebEditorOptions.Parse([]);
            portRequested = false;
        }

        return new WebEditorOptions
        {
            Port = portRequested ? parsed.Port : FindFreePort(),
            Width = parsed.Width,
            Height = parsed.Height,
            TargetFrameRate = parsed.TargetFrameRate,

            // The window is the UI; opening a browser as well would be a second, unrelated viewport.
            OpenBrowser = false
        };
    }

    /// <summary>Reserves a port by binding to port 0, so two editor instances never collide.</summary>
    private static int FindFreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);

        try
        {
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}

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
