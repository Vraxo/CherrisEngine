using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Projects;
using Overcore.Scripting;
using Overcore.Utils;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OvercoreEditorPhotino.Services;

// The console panel's log: writing, tailing and clearing it, plus the environment report and the engine's
// log subscription.
public sealed partial class EditorHostService : IDisposable
{
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
        HostUrl: _session?.Url,
        RendererInfo: _session?.RendererInfo,
        Failure: FailureDetail,
        LogFilePath: _logFilePath));

    /// <summary>Writes a diagnostic line to the console, the in-memory log and the log file.</summary>
    public void WriteLog(string message) => WriteLog(message, LogLevel.Info);

    /// <inheritdoc cref="WriteLog(string)" />
    public void WriteLog(string message, LogLevel level)
    {
        EditorLogEntry entry = new(DateTimeOffset.Now, level, message);

        lock (_logLock)
        {
            _log.Enqueue(entry);

            while (_log.Count > MaximumLogLines)
            {
                _log.Dequeue();
            }
        }

        Console.WriteLine(message);

        string line = $"{entry.Timestamp:HH:mm:ss.fff} [{level}] {message}";

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

    /// <summary>Last <paramref name="lineCount"/> log lines as text, oldest first.</summary>
    public string LogTail(int lineCount = 30)
    {
        lock (_logLock)
        {
            int skip = Math.Max(0, _log.Count - Math.Max(1, lineCount));

            return string.Join(Environment.NewLine, _log.Skip(skip)
                .Select(entry => $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] {entry.Message}"));
        }
    }

    /// <summary>Empties the console. The log file keeps its history.</summary>
    public void ClearLog()
    {
        lock (_logLock)
        {
            _log.Clear();
        }
    }

    private void LogEnvironment()
    {
        WriteLog($"[editor] starting, process {Environment.ProcessId}, {Environment.Version} " +
            $"on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        WriteLog($"[editor] base directory: {AppContext.BaseDirectory}");
        WriteLog($"[editor] web root: {_webRoot} ({(Directory.Exists(_webRoot) ? "present" : "missing")})");
        WriteLog($"[editor] recent projects: {_recent.HistoryDirectory}");

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
        WriteLog(message.Message, message.Level);
    }
}
