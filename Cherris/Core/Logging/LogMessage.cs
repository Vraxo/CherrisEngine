namespace Cherris.Core.Logging;

public readonly struct LogMessage
{
    public DateTime Timestamp { get; }
    public LogLevel Level { get; }
    public string Message { get; }

    public LogMessage(LogLevel level, string message)
    {
        Timestamp = DateTime.Now;
        Level = level;
        Message = message;
    }
}