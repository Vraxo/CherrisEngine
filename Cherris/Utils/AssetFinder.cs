namespace Cherris;

public static class AssetFinder
{
    private static readonly string? _assetRootPath = FindAssetRootPath();

    private static string? FindAssetRootPath()
    {
        string currentPath = AppContext.BaseDirectory;
        DirectoryInfo? directoryInfo = new(currentPath);

        while (directoryInfo is not null)
        {
            // Normalize path separators for a consistent check
            string normalizedPath = directoryInfo.FullName.Replace('\\', '/');

            // This is the robust check. We ensure we are not in a build artifact folder.
            if (!normalizedPath.Contains("/bin/") && !normalizedPath.Contains("/obj/"))
            {
                string potentialPath = Path.Combine(directoryInfo.FullName, "Assets");
                if (Directory.Exists(potentialPath))
                {
                    Console.WriteLine($"[AssetFinder] Found asset root at: {potentialPath}");
                    return potentialPath;
                }
            }

            directoryInfo = directoryInfo.Parent;
        }

        Console.WriteLine("[AssetFinder] FATAL: Could not find the 'Assets' directory in any parent path.");
        return null;
    }

    public static string? FindAssetPath(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) || _assetRootPath is null)
        {
            Console.WriteLine($"[AssetFinder] Warning: Asset root not found or asset name '{assetName}' is null/empty.");
            return null;
        }

        // Sanitize path to use correct OS separators
        string sanitizedName = assetName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(_assetRootPath, sanitizedName);

        // Case 1: The provided name is an exact relative path (with extension).
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // Case 2: The provided name is a relative path but is missing an extension.
        if (!Path.HasExtension(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (directory is not null && Directory.Exists(directory))
            {
                // Find first file that matches the name, regardless of extension.
                var files = Directory.GetFiles(directory, $"{fileName}.*");
                if (files.Any())
                {
                    return files[0];
                }
            }
        }

        // Case 3: The provided name is just a filename, search for it everywhere.
        // This is the fallback, which can be slow, but useful.
        try
        {
            string fileNameOnly = Path.GetFileName(sanitizedName);
            var files = Directory.GetFiles(_assetRootPath, fileNameOnly, SearchOption.AllDirectories);
            if (files.Any()) return files[0];

            // If still not found, try searching with wildcard extension.
            if (!Path.HasExtension(fileNameOnly))
            {
                var filesWithWildcard = Directory.GetFiles(_assetRootPath, $"{fileNameOnly}.*", SearchOption.AllDirectories);
                if (filesWithWildcard.Any()) return filesWithWildcard[0];
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[AssetFinder] Error while searching for asset '{assetName}': {e.Message}");
        }

        return null; // Not found
    }
}