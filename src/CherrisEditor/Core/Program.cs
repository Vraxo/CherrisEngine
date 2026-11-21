using Cherris;
using Cherris.Core;
using CherrisEditor;

internal class Program
{
    private static void Main(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        Console.SetOut(new ConsoleLogRedirector(LogLevel.Info, originalOut));
        Console.SetError(new ConsoleLogRedirector(LogLevel.Error, originalError));

        Editor editor = new(GraphicsAPI.OpenTK);

        try
        {
            if (args.Length > 0 && !string.Equals(args[0], "--launcher", StringComparison.OrdinalIgnoreCase))
            {
                editor.LoadProject(args[0]);
            }
            else if (args.Length == 0)
            {
                string? lastProject = ProjectPersistence.GetLastProject();
                if (lastProject is not null)
                {
                    editor.LoadProject(lastProject);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open the project: {ex.Message}");
        }

        editor.Run();
    }
}