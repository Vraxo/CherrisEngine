using System.Collections.Concurrent;

namespace Cherris.Core;

public static class Logger
{
    private static readonly ConcurrentQueue<LogMessage> _messages = new();
    public static event Action<LogMessage> OnMessageLogged;

    public static IReadOnlyCollection<LogMessage> Messages => _messages;

    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);

    private static void Log(LogLevel level, string message)
    {
        var logMessage = new LogMessage(level, message);
        _messages.Enqueue(logMessage);

        // Also write to the system console for debugging outside the editor
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => originalColor
        };
        Console.WriteLine($"[{logMessage.Timestamp:HH:mm:ss}] [{level}] {message}");
        Console.ForegroundColor = originalColor;

        OnMessageLogged?.Invoke(logMessage);
    }
}