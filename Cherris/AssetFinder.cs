using System;
using System.Collections.Generic;
using System.IO;

namespace Cherris;

public static class AssetFinder
{
    private const string AssetRootPath = "Assets";
    private static readonly Dictionary<string, string> _assetPathCache = new();

    static AssetFinder()
    {
        if (!Directory.Exists(AssetRootPath))
        {
            Console.WriteLine($"[AssetFinder] Warning: Asset root directory '{AssetRootPath}' not found.");
            return;
        }

        // Perform a single, recursive search of the entire asset directory at startup.
        var allAssetFiles = Directory.GetFiles(AssetRootPath, "*.*", SearchOption.AllDirectories);

        foreach (var file in allAssetFiles)
        {
            // Get the path relative to the Assets root, e.g., "sky/day_right.png"
            var relativePath = Path.GetRelativePath(AssetRootPath, file);

            // Create a clean asset name key from the relative path by removing the extension.
            // e.g., "sky\day_right.png" -> "sky\day_right"
            var directory = Path.GetDirectoryName(relativePath);
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath);

            // Handle root assets where directory is "."
            string assetNameKey = directory == "." || string.IsNullOrEmpty(directory)
                ? filenameWithoutExtension
                : Path.Combine(directory, filenameWithoutExtension);

            // Normalize path separators to always use '/' for consistent lookup keys.
            assetNameKey = assetNameKey.Replace('\\', '/');

            if (_assetPathCache.ContainsKey(assetNameKey))
            {
                Console.WriteLine($"[AssetFinder] Warning: Duplicate asset name '{assetNameKey}'. Overwriting '{_assetPathCache[assetNameKey]}' with '{file}'.");
            }
            _assetPathCache[assetNameKey] = file;
        }
    }

    public static string? FindAssetPath(string assetName)
    {
        // After the one-time scan, finding an asset is an instantaneous dictionary lookup.
        _assetPathCache.TryGetValue(assetName, out var path);
        return path;
    }
}