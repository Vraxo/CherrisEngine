namespace CherrisEditorWeb.Host;

/// <summary>Command-line options for the web editor host.</summary>
public sealed class WebEditorOptions
{
    /// <summary>TCP port the HTTP/WebSocket listener binds to.</summary>
    public int Port { get; private init; } = 5178;

    /// <summary>Initial render width in device pixels; the client overrides it once it measures its canvas.</summary>
    public int Width { get; private init; } = 1280;

    /// <summary>Initial render height in device pixels.</summary>
    public int Height { get; private init; } = 720;

    /// <summary>Render loop rate. The client can display slower; frames are dropped, never queued.</summary>
    public int TargetFrameRate { get; private init; } = 60;

    /// <summary>Bind to all interfaces instead of loopback only.</summary>
    public bool ListenOnAllInterfaces { get; private init; }

    /// <summary>Launch the default browser after starting.</summary>
    public bool OpenBrowser { get; private init; } = true;

    /// <summary>Print usage and exit.</summary>
    public bool ShowHelp { get; private init; }

    /// <summary>Parses command-line arguments.</summary>
    /// <exception cref="ArgumentException">An argument is unknown or has an invalid value.</exception>
    public static WebEditorOptions Parse(string[] args)
    {
        int port = 5178;
        int width = 1280;
        int height = 720;
        int frameRate = 60;
        bool listenOnAllInterfaces = false;
        bool openBrowser = true;
        bool showHelp = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--port":
                    port = ReadValue(args, ref index, 1, 65535);
                    break;

                case "--width":
                    width = ReadValue(args, ref index, 64, 3840);
                    break;

                case "--height":
                    height = ReadValue(args, ref index, 64, 2160);
                    break;

                case "--fps":
                    frameRate = ReadValue(args, ref index, 1, 240);
                    break;

                case "--listen-all":
                    listenOnAllInterfaces = true;
                    break;

                case "--no-browser":
                    openBrowser = false;
                    break;

                case "--help" or "-h":
                    showHelp = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new WebEditorOptions
        {
            Port = port,
            Width = width,
            Height = height,
            TargetFrameRate = frameRate,
            ListenOnAllInterfaces = listenOnAllInterfaces,
            OpenBrowser = openBrowser,
            ShowHelp = showHelp
        };
    }

    /// <summary>Writes usage to standard output.</summary>
    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            Cherris web editor host

            Usage: CherrisEditorWeb [options]

              --port <n>     HTTP/WebSocket port (default 5178)
              --width <n>    Initial render width in device pixels (default 1280)
              --height <n>   Initial render height in device pixels (default 720)
              --fps <n>      Render loop rate (default 60)
              --listen-all   Bind to all interfaces instead of loopback only.
                             On Windows this needs an URL ACL reservation (netsh http add urlacl).
              --no-browser   Do not open the default browser on start
              --help, -h     Show this help
            """);
    }

    private static int ReadValue(string[] args, ref int index, int minimum, int maximum)
    {
        string option = args[index];

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        if (!int.TryParse(args[index + 1], out int value))
        {
            throw new ArgumentException($"Invalid number for {option}: {args[index + 1]}");
        }

        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"{option} must be between {minimum} and {maximum}.");
        }

        index++;
        return value;
    }
}
