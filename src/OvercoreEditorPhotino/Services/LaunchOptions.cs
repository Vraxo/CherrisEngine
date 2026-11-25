namespace OvercoreEditorPhotino.Services;

/// <summary>Command line understood by the editor window.</summary>
/// <param name="ProjectPath">Project to open once the window is up, or null for the launcher.</param>
/// <param name="Width">Initial render width in device pixels.</param>
/// <param name="Height">Initial render height in device pixels.</param>
/// <param name="TargetFrameRate">Render loop rate.</param>
public sealed record LaunchOptions(string? ProjectPath, int Width, int Height, int TargetFrameRate)
{
    /// <summary>
    /// Parses the editor's own flags. Everything else is left for the Blazor builder and the framework,
    /// which read the same command line.
    /// </summary>
    /// <exception cref="ArgumentException">A recognised flag has no value.</exception>
    public static LaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? projectPath = null;
        int width = 1280;
        int height = 720;
        int frameRate = 60;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--project":
                    projectPath = ReadValue(args, ref index);
                    break;

                case "--width":
                    width = ReadNumber(args, ref index, 64, 3840);
                    break;

                case "--height":
                    height = ReadNumber(args, ref index, 64, 2160);
                    break;

                case "--fps":
                    frameRate = ReadNumber(args, ref index, 1, 240);
                    break;

                default:
                    // Not ours: a bare path is treated as a project when it exists, so
                    // `dotnet run --project src/OvercoreEditorPhotino -- E:\Games\MyGame` works like the
                    // ImGui editor's first argument.
                    if (!args[index].StartsWith('-') && projectPath is null && Directory.Exists(args[index]))
                    {
                        projectPath = args[index];
                    }

                    break;
            }
        }

        return new LaunchOptions(projectPath, width, height, frameRate);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{args[index]} needs a value.");
        }

        return args[++index];
    }

    private static int ReadNumber(string[] args, ref int index, int minimum, int maximum)
    {
        string value = ReadValue(args, ref index);

        if (!int.TryParse(value, out int number) || number < minimum || number > maximum)
        {
            throw new ArgumentException(
                $"{args[index - 1]} must be a number between {minimum} and {maximum}, not '{value}'.");
        }

        return number;
    }
}
