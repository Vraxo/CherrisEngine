using System;
using System.IO;
using System.Linq;

namespace Cherris;

public static class AssetFinder
{
    private const string AssetRootPath = "Assets";

    public static string? FindAssetPath(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) || !Directory.Exists(AssetRootPath))
        {
            return null;
        }

        // Sanitize path to use correct OS separators
        string sanitizedName = assetName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(AssetRootPath, sanitizedName);

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
            var files = Directory.GetFiles(AssetRootPath, fileNameOnly, SearchOption.AllDirectories);
            if (files.Any()) return files[0];

            // If still not found, try searching with wildcard extension.
            if (!Path.HasExtension(fileNameOnly))
            {
                var filesWithWildcard = Directory.GetFiles(AssetRootPath, $"{fileNameOnly}.*", SearchOption.AllDirectories);
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