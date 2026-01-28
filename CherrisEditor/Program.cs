using Cherris;
using Cherris.Core;
using Cherris.Core.Logging;
using CherrisEditor;

internal class Program
{
    private static void Main(string[] args)
    {
        // Store the original console output streams before redirecting
        var originalOut = Console.Out;
        var originalError = Console.Error;

        // Redirect console output to both terminal and Logger
        Console.SetOut(new ConsoleLogRedirector(LogLevel.Info, originalOut));
        Console.SetError(new ConsoleLogRedirector(LogLevel.Error, originalError));

        // Prevent Logger from writing back to Console to avoid infinite recursion
        // (since Console.Out now points to our redirector which already wrote to originalOut)
        Logger.WriteToConsole = false;

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