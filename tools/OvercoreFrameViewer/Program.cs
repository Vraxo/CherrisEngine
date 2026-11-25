using OvercoreEditorSession.Host;

namespace OvercoreFrameViewer;

internal static class Program
{
    private static int Main(string[] args)
    {
        EditorSessionOptions options;

        try
        {
            options = EditorSessionOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            EditorSessionOptions.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            EditorSessionOptions.PrintUsage();
            return 0;
        }

        string? webRoot = ResolveWebRoot();

        if (webRoot is null)
        {
            Console.Error.WriteLine(
                "[viewer] Could not find the wwwroot folder. Build the project, or run it from tools/OvercoreFrameViewer.");
            return 1;
        }

        using EditorSessionHost host = new(options, webRoot);
        return host.Run();
    }

    private static string? ResolveWebRoot()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "OvercoreFrameViewer", "wwwroot")
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
