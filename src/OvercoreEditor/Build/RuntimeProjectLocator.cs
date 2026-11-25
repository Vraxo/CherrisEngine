namespace OvercoreEditor.Build;

/// <summary>Locates the runtime project from an editor build output directory.</summary>
public static class RuntimeProjectLocator
{
    public static string? Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            // The source tree may be above either the centralized artifacts output
            // or a project-local bin directory. Also support the older flat layout.
            string[] candidates =
            [
                Path.Combine(directory.FullName, "src", "OvercoreRuntime", "OvercoreRuntime.csproj"),
                Path.Combine(directory.FullName, "OvercoreRuntime", "OvercoreRuntime.csproj")
            ];
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }
        return null;
    }
}
