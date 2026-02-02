namespace Cherris.Utils;

public static class ProjectFiles
{
    public static string? ProjectRoot { get; set; }

    public static string? Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // 1. If path is absolute and exists, return it.
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        // 2. If path is absolute but missing, try to make it relative to the BaseDirectory.
        //    This handles cases where the Runtime naively constructs paths like "BaseDir/Scenes/Scene.yaml"
        //    but the file is actually in "BaseDir/Assets/Scenes/Scene.yaml".
        string searchPath = path;
        if (Path.IsPathRooted(path))
        {
            string baseDir = AppContext.BaseDirectory;
            if (path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                // Strip the base directory to get the relative part
                searchPath = path[baseDir.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                // If it's absolute but not inside BaseDirectory, we can't easily fix it. 
                // Return null to indicate failure.
                return null;
            }
        }

        // 3. Resolve using the correct Content Root (Editor Project Root or Runtime Assets/)
        string root = !string.IsNullOrWhiteSpace(ProjectRoot)
            ? ProjectRoot
            : GetRuntimeContentRoot();

        string fullPath = Path.Combine(root, searchPath);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // 4. Fuzzy search (extensions) if exact match failed
        if (!Path.HasExtension(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            if (directory is not null && Directory.Exists(directory))
            {
                string[] files = Directory.GetFiles(directory, $"{fileName}.*");
                if (files.Any())
                {
                    return files[0];
                }
            }
        }

        return null;
    }

    private static string GetRuntimeContentRoot()
    {
        // Check for "Assets" folder next to the executable
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");

        return Directory.Exists(assetsPath)
            ? assetsPath
            : AppContext.BaseDirectory;
    }
}