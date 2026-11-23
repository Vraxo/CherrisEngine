using CherrisEditorWeb.Host;

namespace CherrisEditorWeb;

internal static class Program
{
    private static int Main(string[] args)
    {
        WebEditorOptions options;

        try
        {
            options = WebEditorOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WebEditorOptions.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            WebEditorOptions.PrintUsage();
            return 0;
        }

        string? webRoot = ResolveWebRoot();

        if (webRoot is null)
        {
            Console.Error.WriteLine(
                "[web-editor] Could not find the wwwroot folder. Build the project, or run it from src/CherrisEditorWeb.");
            return 1;
        }

        using WebEditorHost host = new(options, webRoot);
        return host.Run();
    }

    private static string? ResolveWebRoot()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "CherrisEditorWeb", "wwwroot")
        ];

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
