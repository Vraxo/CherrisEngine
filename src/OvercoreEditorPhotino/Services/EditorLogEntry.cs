using Overcore.Core.Logging;

namespace OvercoreEditorPhotino.Services;

/// <summary>One line of the editor log the console panel renders.</summary>
/// <param name="Timestamp">When the line was produced.</param>
/// <param name="Level">Info, warning or error.</param>
/// <param name="Message">The text.</param>
public sealed record EditorLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);
