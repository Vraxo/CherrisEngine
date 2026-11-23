using Cherris.Core.Logging;
using Cherris.Rendering.Headless;
using CherrisEditorWeb;
using CherrisEditorWeb.Host;
using OpenTK.Windowing.Desktop;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CherrisEditorPhotino.Services;

/// <summary>
/// Owns the in-process editor host: the GL context, the render thread, the HTTP/WebSocket listener and the
/// latest scene snapshot. The Blazor components read UI state from here, while pixels reach the viewport
/// canvas over the socket that this host serves.
/// </summary>
/// <remarks>
/// <para>
/// The window's main thread belongs to the native Photino message loop, so the engine runs on its own
/// background thread. That is the opposite arrangement from the browser host, where the engine owns the
/// main thread.
/// </para>
/// <para>
/// One step cannot live on that thread: OpenTK refuses to initialise GLFW anywhere but the process' main
/// thread (<c>GLFWProvider.CheckForMainThread</c>), and <see cref="Cherris.Core.Engine"/>'s constructor
/// initialises the backend. The hidden window is therefore created here, on the thread that called
/// <see cref="Start"/>, and the render thread only makes the context current. A GL context is portable
/// between threads; the window handle is never presented, so its creating thread does not matter
/// afterwards.
/// </para>
/// </remarks>
public sealed class EditorHostService : IDisposable
{
    private const int MaximumLogLines = 400;
    private const int ContextWidth = 1920;
    private const int ContextHeight = 1080;

    private readonly WebEditorOptions _options;
    private readonly HeadlessBackend _graphicsBackend;
    private readonly WebEditorHost _host;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _exited = new(false);
    private readonly Queue<string> _log = new();
    private readonly object _logLock = new();

    private readonly string _webRoot;

    private volatile string? _failure;
    private volatile string? _failureDetail;
    private string? _logFilePath;
    private bool _disposed;

    private EditorHostService(WebEditorOptions options, string webRoot)
    {
        _options = options;
        _webRoot = webRoot;

        // Created here, on the caller's thread, so the hidden window and GLFW belong to the main thread.
        _graphicsBackend = new HeadlessBackend();
        _host = new WebEditorHost(options, webRoot, _graphicsBackend, WriteLog);

        _thread = new Thread(RunHost)
        {
            IsBackground = true,
            Name = "Cherris Engine"
        };
    }

    /// <summary>
    /// Prepares the host on the calling thread and starts the render thread. The calling thread must be the
    /// process' main thread, because that is where GLFW has to be initialised.
    /// </summary>
    /// <param name="args">Command line, forwarded for <c>--port</c>, <c>--width</c>, <c>--height</c> and <c>--fps</c>.</param>
    public static EditorHostService Start(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        WebEditorOptions options = BuildOptions(args);
        string baseDirectory = AppContext.BaseDirectory;
        string webRoot = Path.Combine(baseDirectory, "wwwroot");

        EditorHostService service = new(options, webRoot);

        service.OpenLogFile();
        service.LogEnvironment(baseDirectory);
        service.SubscribeToEngineLog();

        if (!service.TryCreateGraphicsContext())
        {
            return service;
        }

        service._thread.Start();
        service._host.WaitForStart(TimeSpan.FromSeconds(20));

        if (service._host.StartFailed)
        {
            service._failure = $"The editor host could not listen on port {service._options.Port}.";
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

            Uri parsed = new(url);

            UriBuilder builder = new(parsed)
            {
                Scheme = parsed.Scheme == "https" ? "wss" : "ws",
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

    /// <summary>The exception behind <see cref="FailureMessage"/>, verbatim, or null.</summary>
    public string? FailureDetail => _failureDetail ?? _host.FailureDetail;

    /// <summary>Renderer, vendor and GL version, or null while the renderer is starting.</summary>
    public string? RendererInfo => _host.RendererInfo;

    /// <summary>Where the diagnostics log is written, or null when no file could be created.</summary>
    public string? LogFilePath => _logFilePath;

    /// <summary>The environment report a user can copy out of the window.</summary>
    public string Diagnostics => HostDiagnostics.Build(new HostDiagnosticsInfo(
        Runtime: $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} " +
            $"({RuntimeInformation.ProcessArchitecture})",
        BaseDirectory: AppContext.BaseDirectory,
        WebRoot: _webRoot,
        WebRootExists: Directory.Exists(_webRoot),
        EditorResourcesPresent: Directory.Exists(Path.Combine(AppContext.BaseDirectory, "EditorResources")),
        HostUrl: _host.Url,
        RendererInfo: _host.RendererInfo,
        Failure: FailureDetail,
        LogFilePath: _logFilePath));

    /// <summary>Writes a diagnostic line to the console, the in-memory log and the log file.</summary>
    public void WriteLog(string message)
    {
        string line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}";

        lock (_logLock)
        {
            _log.Enqueue(line);

            while (_log.Count > MaximumLogLines)
            {
                _log.Dequeue();
            }
        }

        Console.WriteLine(message);

        string? path = _logFilePath;

        if (path is null)
        {
            return;
        }

        try
        {
            lock (_logLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the editor down; the file is a convenience.
        }
    }

    /// <summary>Last <paramref name="lineCount"/> log lines, oldest first.</summary>
    public string LogTail(int lineCount = 30)
    {
        lock (_logLock)
        {
            int skip = Math.Max(0, _log.Count - Math.Max(1, lineCount));
            return string.Join(Environment.NewLine, _log.Skip(skip));
        }
    }

    /// <summary>Stops the render loop and releases the listener and the GL context.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Logger.OnMessageLogged -= OnEngineLog;

        // Request the stop first: the engine cleans up its GL objects while the context is still current,
        // and only then is it safe for the host to tear down the plumbing the render thread was using.
        _host.RequestStop();

        if (!_exited.Wait(TimeSpan.FromSeconds(5)))
        {
            WriteLog("[editor] render thread did not stop within 5 seconds");
        }

        _host.Dispose();
        _graphicsBackend.Dispose();
        _exited.Dispose();

        WriteLog("[editor] stopped");
        GC.SuppressFinalize(this);
    }

    private bool TryCreateGraphicsContext()
    {
        string shaders = Path.Combine(AppContext.BaseDirectory, "EditorResources", "Shaders");

        if (!Directory.Exists(shaders))
        {
            WriteLog($"[editor] shaders not found at {shaders}");
        }

        try
        {
            _graphicsBackend.CreateGraphicsContext("Cherris Editor", ContextWidth, ContextHeight);
            WriteLog("[editor] hidden GL context created on the main thread");
        }
        catch (Exception exception)
        {
            _failure = "The graphics context could not be created on this machine.";
            _failureDetail = exception.ToString();
            WriteLog($"[editor] could not create the graphics context: {exception}");
            return false;
        }

        // The context exists now, so GLFW may legitimately be reached from the render thread: the engine's
        // constructor makes it current there. OpenTK's guard only checks the creating thread, which is
        // satisfied above, and would reject every later call from the render thread.
        GLFWProvider.CheckForMainThread = false;

        return true;
    }

    private void OpenLogFile()
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cherris");

            Directory.CreateDirectory(directory);

            _logFilePath = Path.Combine(directory, "editor-host.log");
            File.WriteAllText(
                _logFilePath,
                $"Cherris editor host, started {DateTimeOffset.Now:O}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logFilePath = null;
        }
    }

    private void LogEnvironment(string baseDirectory)
    {
        WriteLog($"[editor] starting, process {Environment.ProcessId}, {Environment.Version} " +
            $"on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        WriteLog($"[editor] base directory: {baseDirectory}");
        WriteLog($"[editor] web root: {_webRoot} ({(Directory.Exists(_webRoot) ? "present" : "missing")})");

        if (_logFilePath is not null)
        {
            WriteLog($"[editor] log file: {_logFilePath}");
        }
    }

    private void SubscribeToEngineLog()
    {
        Logger.OnMessageLogged += OnEngineLog;
    }

    private void OnEngineLog(LogMessage message)
    {
        WriteLog($"[{message.Level}] {message.Message}");
    }

    private void RunHost()
    {
        try
        {
            int exitCode = _host.Run();

            WriteLog($"[editor] host exited with code {exitCode}");

            if (exitCode != 0)
            {
                _failure = "The renderer stopped.";
            }
        }
        catch (Exception exception)
        {
            _failure = "The editor host threw an exception.";
            _failureDetail = exception.ToString();
            WriteLog($"[editor] host failed: {exception}");
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
