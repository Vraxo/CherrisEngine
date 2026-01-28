namespace Cherris.Utils;

public static class AssetFinder
{
    public static string? ProjectRoot { get; set; }

    public static string? FindAssetPath(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        // Try ProjectRoot first if set
        if (!string.IsNullOrWhiteSpace(ProjectRoot))
        {
            string projectPath = Path.Combine(ProjectRoot, "Assets", assetName);
            if (File.Exists(projectPath))
            {
                return projectPath;
            }

            if (!Path.HasExtension(projectPath))
            {
                string? directory = Path.GetDirectoryName(projectPath);
                string fileName = Path.GetFileName(projectPath);

                if (directory is not null && Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, $"{fileName}.*");
                    if (files.Any())
                    {
                        return files[0];
                    }
                }
            }
        }

        // Fallback to executable directory (for engine assets when no project loaded)
        string exeDir = AppContext.BaseDirectory;
        string fallbackPath = Path.Combine(exeDir, "Assets", assetName);

        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        if (!Path.HasExtension(fallbackPath))
        {
            string? directory = Path.GetDirectoryName(fallbackPath);
            string fileName = Path.GetFileName(fallbackPath);

            if (directory is not null && Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, $"{fileName}.*");
                if (files.Any())
                {
                    return files[0];
                }
            }
        }

        return null;
    }
}