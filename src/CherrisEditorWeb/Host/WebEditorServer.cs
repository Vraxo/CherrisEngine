using System.Net;
using System.Net.WebSockets;

namespace CherrisEditorWeb.Host;

/// <summary>
/// Minimal static-file plus WebSocket server for the browser UI: <c>GET /ws</c> upgrades, everything else
/// is served from the web root.
/// </summary>
/// <remarks>
/// Deliberately built on <see cref="HttpListener"/> so the host has no package dependencies. If you later
/// want TLS, authentication or multiple concurrent editors, swap this class for Kestrel; the only thing
/// it provides to the rest of the host is <see cref="ViewerConnection"/> instances.
/// </remarks>
public sealed class WebEditorServer : IAsyncDisposable
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon"
    };

    private readonly HttpListener _listener = new();
    private readonly string _webRoot;
    private readonly string _prefix;
    private readonly Func<WebSocket, ViewerConnection> _connectionFactory;
    private readonly List<ViewerConnection> _connections = [];
    private readonly CancellationTokenSource _lifetime = new();

    private Task? _acceptLoop;

    /// <param name="prefix">Listener prefix, for example <c>http://localhost:5178/</c>.</param>
    /// <param name="webRoot">Directory containing the HTML/JS/CSS to serve.</param>
    /// <param name="connectionFactory">Builds a <see cref="ViewerConnection"/> for an accepted socket.</param>
    public WebEditorServer(string prefix, string webRoot, Func<WebSocket, ViewerConnection> connectionFactory)
    {
        _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _webRoot = Path.GetFullPath(webRoot ?? throw new ArgumentNullException(nameof(webRoot)));

        if (!_webRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            _webRoot += Path.DirectorySeparatorChar;
        }

        if (!Directory.Exists(_webRoot))
        {
            throw new DirectoryNotFoundException($"Web root not found: {_webRoot}");
        }
    }

    /// <summary>The URL the browser (or a webview window) should open.</summary>
    public string Url => _prefix;

    /// <summary>Starts listening. Returns once the socket is bound.</summary>
    public void Start()
    {
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Listener may already be shut down.
        }

        ViewerConnection[] connections;

        lock (_connections)
        {
            connections = [.. _connections];
            _connections.Clear();
        }

        foreach (ViewerConnection connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cancellation races are expected here.
            }
        }

        _lifetime.Dispose();
        _listener.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            _ = HandleContextAsync(context);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";

            if (path.Equals("/ws", StringComparison.OrdinalIgnoreCase) && context.Request.IsWebSocketRequest)
            {
                HttpListenerWebSocketContext socketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                await TrackConnectionAsync(socketContext.WebSocket).ConfigureAwait(false);
                return;
            }

            await ServeStaticFileAsync(context, path).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TryAbort(context, exception);
        }
    }

    private async Task TrackConnectionAsync(WebSocket socket)
    {
        ViewerConnection connection = _connectionFactory(socket);

        lock (_connections)
        {
            _connections.Add(connection);
        }

        try
        {
            await connection.Completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Already reported by the connection.
        }

        lock (_connections)
        {
            _connections.Remove(connection);
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ServeStaticFileAsync(HttpListenerContext context, string path)
    {
        string relativePath = Uri.UnescapeDataString(path).TrimStart('/');

        if (relativePath.Length == 0)
        {
            relativePath = "index.html";
        }

        string fullPath = Path.GetFullPath(Path.Combine(_webRoot, relativePath));

        if (!fullPath.StartsWith(_webRoot, StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.Close();
            return;
        }

        if (!File.Exists(fullPath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
            return;
        }

        byte[] content = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = ContentTypeFor(fullPath);
        context.Response.ContentLength64 = content.Length;
        context.Response.Headers["Cache-Control"] = "no-store";

        await context.Response.OutputStream.WriteAsync(content).ConfigureAwait(false);
        context.Response.Close();
    }

    private static string ContentTypeFor(string fullPath)
    {
        return ContentTypes.TryGetValue(Path.GetExtension(fullPath), out string? contentType)
            ? contentType
            : "application/octet-stream";
    }

    private static void TryAbort(HttpListenerContext context, Exception exception)
    {
        try
        {
            Console.Error.WriteLine($"[web-editor] Request failed: {exception.Message}");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }
        catch (Exception)
        {
            // The response was probably already sent.
        }
    }
}
