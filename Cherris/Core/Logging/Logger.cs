using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CherrisEditor")]

namespace Cherris.Core.Logging;

public static class Logger
{
    private static readonly ConcurrentQueue<LogMessage> _messages = new();
    public static event Action<LogMessage>? OnMessageLogged;

    // Set to false when Console output is redirected to Logger to prevent recursion
    public static bool WriteToConsole { get; set; } = true;

    public static IReadOnlyCollection<LogMessage> Messages => _messages;

    public static void Info(string message)
    {
        Log(LogLevel.Info, message);
    }

    public static void Warning(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public static void Error(string message)
    {
        Log(LogLevel.Error, message);
    }

    // Internal method for ConsoleLogRedirector to enqueue messages without triggering console output
    internal static void LogRaw(LogLevel level, string message)
    {
        var logMessage = new LogMessage(level, message);
        _messages.Enqueue(logMessage);
        OnMessageLogged?.Invoke(logMessage);
    }

    private static void Log(LogLevel level, string message)
    {
        var logMessage = new LogMessage(level, message);
        _messages.Enqueue(logMessage);

        if (WriteToConsole)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => originalColor
            };
            Console.WriteLine($"[{logMessage.Timestamp:HH:mm:ss}] [{level}] {message}");
            Console.ForegroundColor = originalColor;
        }

        OnMessageLogged?.Invoke(logMessage);
    }
}