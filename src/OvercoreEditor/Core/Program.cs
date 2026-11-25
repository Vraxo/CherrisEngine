using Overcore;
using Overcore.Core;
using OvercoreEditor;

internal class Program
{
    private static void Main(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        Console.SetOut(new ConsoleLogRedirector(LogLevel.Info, originalOut));
        Console.SetError(new ConsoleLogRedirector(LogLevel.Error, originalError));

        bool newProject = args.Length > 0 && string.Equals(args[0], "--new-project", StringComparison.OrdinalIgnoreCase);
        Editor editor = new(GraphicsAPI.OpenTK, startWithNewProject: newProject);

        try
        {
            if (args.Length > 0 && !newProject && !string.Equals(args[0], "--launcher", StringComparison.OrdinalIgnoreCase))
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