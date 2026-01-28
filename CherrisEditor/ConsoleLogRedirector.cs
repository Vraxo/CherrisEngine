using Cherris.Core;
using Cherris.Core.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace CherrisEditor;

public class ConsoleLogRedirector : TextWriter
{
    [ThreadStatic] private static bool _isCapturingForLogger;
    private readonly LogLevel _defaultLevel;
    private readonly TextWriter _originalOut;
    private readonly StringBuilder _lineBuffer = new();
    private static readonly Regex LoggerFormatRegex = new(@"^\[\d{2}:\d{2}:\d{2}\] \[(Info|Warning|Error)\] ", RegexOptions.Compiled);

    public ConsoleLogRedirector(LogLevel defaultLevel, TextWriter originalOut)
    {
        _defaultLevel = defaultLevel;
        _originalOut = originalOut;
    }

    public override Encoding Encoding => Encoding.Default;

    public override void Write(char value)
    {
        if (_isCapturingForLogger)
        {
            _originalOut.Write(value);
            return;
        }

        if (value == '\n')
        {
            FlushLine();
        }
        else if (value != '\r')
        {
            _ = _lineBuffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (_isCapturingForLogger)
        {
            _originalOut.Write(value);
            return;
        }

        if (value is null)
        {
            return;
        }

        if (value.EndsWith('\n'))
        {
            if (_lineBuffer.Length > 0)
            {
                _ = _lineBuffer.Append(value.TrimEnd('\n', '\r'));
                FlushLine();
            }
            else
            {
                WriteLineToOriginal(value.TrimEnd('\n', '\r'));
            }
        }
        else
        {
            _ = _lineBuffer.Append(value);
        }
    }

    public override void WriteLine(string? value)
    {
        if (_isCapturingForLogger)
        {
            _originalOut.WriteLine(value);
            return;
        }

        if (_lineBuffer.Length > 0)
        {
            _ = _lineBuffer.Append(value);
            FlushLine();
        }
        else
        {
            WriteLineToOriginal(value);
        }
    }

    public override void Flush()
    {
        _originalOut.Flush();
        if (_lineBuffer.Length > 0)
        {
            FlushLine();
        }
        base.Flush();
    }

    private void WriteLineToOriginal(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            _originalOut.WriteLine();
            return;
        }

        // Check if this line is already formatted by Logger (contains timestamp and level)
        var match = LoggerFormatRegex.Match(line);
        bool isLoggerFormatted = match.Success;
        LogLevel level = isLoggerFormatted
            ? Enum.Parse<LogLevel>(match.Groups[1].Value)
            : _defaultLevel;

        var originalColor = Console.ForegroundColor;

        try
        {
            if (level == LogLevel.Warning)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            else if (level == LogLevel.Error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            // If not already formatted by Logger, prepend the level tag
            string outputLine = isLoggerFormatted ? line : $"[{level}] {line}";

            _originalOut.WriteLine(outputLine);
            _originalOut.Flush();

            // Extract just the message for the editor panel (without our prepended tag if we added it)
            string messageForLogger = isLoggerFormatted
                ? line[match.Value.Length..]
                : line;

            _isCapturingForLogger = true;
            Logger.LogRaw(level, messageForLogger);
            _isCapturingForLogger = false;
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private void FlushLine()
    {
        if (_lineBuffer.Length == 0)
        {
            return;
        }

        var line = _lineBuffer.ToString();
        _ = _lineBuffer.Clear();
        WriteLineToOriginal(line);
    }
}