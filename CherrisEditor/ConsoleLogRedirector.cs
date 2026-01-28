using Cherris.Core;
using Cherris.Core.Logging;
using System.Text;

namespace CherrisEditor;

public class ConsoleLogRedirector : TextWriter
{
    private readonly LogLevel _level;
    private readonly TextWriter _originalOut;
    private readonly StringBuilder _lineBuffer = new();

    public ConsoleLogRedirector(LogLevel level, TextWriter originalOut)
    {
        _level = level;
        _originalOut = originalOut;
    }

    public override Encoding Encoding => Encoding.Default;

    public override void Write(char value)
    {
        // Write to original console immediately so it appears in the terminal window
        _originalOut.Write(value);

        // Buffer for Logger/editor console
        if (value == '\n')
        {
            FlushLine();
        }
        else if (value != '\r')
        {
            _lineBuffer.Append(value);
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

    private void FlushLine()
    {
        if (_lineBuffer.Length == 0) return;

        var line = _lineBuffer.ToString();
        _lineBuffer.Clear();

        switch (_level)
        {
            case LogLevel.Error:
                Logger.Error(line);
                break;
            case LogLevel.Warning:
                Logger.Warning(line);
                break;
            default:
                Logger.Info(line);
                break;
        }
    }
}