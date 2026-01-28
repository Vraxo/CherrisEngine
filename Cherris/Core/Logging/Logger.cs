using System.Collections.Concurrent;

namespace Cherris.Core.Logging;

public static class Logger
{
    private static readonly ConcurrentQueue<LogMessage> _messages = new();
    public static event Action<LogMessage>? OnMessageLogged;

    public static IReadOnlyCollection<LogMessage> Messages => _messages;

    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);

    private static void Log(LogLevel level, string message)
    {
        LogMessage logMessage = new(level, message);
        _messages.Enqueue(logMessage);

        ConsoleColor originalColor = Console.ForegroundColor;
        
        Console.ForegroundColor = GetForegroundColor(level, originalColor);
        Console.WriteLine($"[{logMessage.Timestamp:HH:mm:ss}] [{level}] {message}");
        Console.ForegroundColor = originalColor;

        OnMessageLogged?.Invoke(logMessage);
    }

    private static ConsoleColor GetForegroundColor(LogLevel level, ConsoleColor originalColor)
    {
        return level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => originalColor
        };
    }
}