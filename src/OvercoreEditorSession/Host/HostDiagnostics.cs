using System.Text;

namespace OvercoreEditorSession.Host;

/// <summary>Everything a hosting app knows about its environment when something goes wrong.</summary>
/// <param name="Runtime">Framework and OS description, e.g. <c>.NET 11.0.0 on Windows 11</c>.</param>
/// <param name="BaseDirectory">Directory the host executable runs from.</param>
/// <param name="WebRoot">Directory served over HTTP.</param>
/// <param name="WebRootExists">Whether <paramref name="WebRoot"/> was found.</param>
/// <param name="EditorResourcesPresent">Whether <c>EditorResources/</c> was found next to the executable.</param>
/// <param name="HostUrl">URL the host is listening on, or null.</param>
/// <param name="RendererInfo">Renderer/GL description, or null before the renderer is live.</param>
/// <param name="Failure">Exception text of the failure, or null.</param>
/// <param name="LogFilePath">Path of the diagnostics log, or null when no file could be written.</param>
public sealed record HostDiagnosticsInfo(
    string Runtime,
    string BaseDirectory,
    string WebRoot,
    bool WebRootExists,
    bool EditorResourcesPresent,
    string? HostUrl,
    string? RendererInfo,
    string? Failure,
    string? LogFilePath);

/// <summary>
/// Formats a bug report a user can copy out of the editor window. Deliberately plain text with one fact
/// per line: it is meant to be pasted into an issue, not parsed.
/// </summary>
public static class HostDiagnostics
{
    /// <summary>Builds the multi-line diagnostics block.</summary>
    public static string Build(HostDiagnosticsInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        StringBuilder builder = new();

        builder.AppendLine("Overcore editor host diagnostics");
        builder.AppendLine($"  runtime:    {info.Runtime}");
        builder.AppendLine($"  base dir:   {info.BaseDirectory}");
        builder.AppendLine($"  web root:   {info.WebRoot} ({(info.WebRootExists ? "present" : "missing")})");
        builder.AppendLine($"  resources:  EditorResources {(info.EditorResourcesPresent ? "present" : "MISSING")}");
        builder.AppendLine($"  host url:   {info.HostUrl ?? "(not listening)"}");
        builder.AppendLine($"  renderer:   {info.RendererInfo ?? "(not initialised)"}");
        builder.AppendLine($"  log file:   {info.LogFilePath ?? "(not written)"}");

        if (string.IsNullOrWhiteSpace(info.Failure))
        {
            builder.Append("  failure:    (none)");
        }
        else
        {
            // Indented, but otherwise verbatim: the stack is the useful part.
            builder.AppendLine("  failure:");
            builder.Append(info.Failure.TrimEnd());
        }

        return builder.ToString();
    }
}
