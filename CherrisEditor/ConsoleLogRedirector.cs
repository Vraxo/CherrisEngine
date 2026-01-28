using Cherris.Core;
using Cherris.Core.Logging;
using System.Text;

namespace CherrisEditor;

public class ConsoleLogRedirector : TextWriter
{
    [ThreadStatic] private static bool _isCapturingForLogger;
    private readonly LogLevel _defaultLevel;
    private readonly TextWriter _originalOut;
    private readonly StringBuilder _lineBuffer = new();

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

        // Fast path for complete lines
        if (value.EndsWith('\n'))
        {
            if (_lineBuffer.Length > 0)
            {
                _ = _lineBuffer.Append(value.TrimEnd('\n', '\r'));
                FlushLine();
            }
            else
            {
                var line = value.TrimEnd('\n', '\r');
                WriteLineToOriginal(line);
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

        LogLevel level = DetectLevel(line);
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

            _originalOut.WriteLine(line);
            _originalOut.Flush();

            _isCapturingForLogger = true;
            Logger.LogRaw(level, line);
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

    private LogLevel DetectLevel(string line)
    {
        // Check for explicit tags first
        if (line.Contains("[Warning]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Warning:", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warning;
        }

        return line.Contains("[Error]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Error:", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Error
            : _defaultLevel;
    }
}