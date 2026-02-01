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

        if (args.Length >= 2 && args[0] == "--player")
        {
            RunPlayerMode(args[1]);
            return;
        }

        RunEditorMode(args);
    }

    private static void RunPlayerMode(string projectPath)
    {
        var player = new Player(projectPath);
        player.Run();
    }

    private static void RunEditorMode(string[] args)
    {
        Editor editor = new(GraphicsAPI.OpenTK);

        if (args.Length > 0)
        {
            editor.LoadProject(args[0]);
        }
        else
        {
            string? lastProject = ProjectPersistence.GetLastProject();
            if (lastProject is not null && Directory.Exists(lastProject))
            {
                editor.LoadProject(lastProject);
            }
        }

        editor.Run();
    }
}