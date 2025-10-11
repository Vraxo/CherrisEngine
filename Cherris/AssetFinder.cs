using System;
using System.IO;

namespace Cherris;

public static class AssetFinder
{
    private const string AssetRootPath = "Assets";

    public static string FindTextureFile(string name)
    {
        if (!Directory.Exists(AssetRootPath))
        {
            return null;
        }

        // We'll check for a few common extensions.
        string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
        foreach (var ext in extensions)
        {
            // Search for "name.ext" in the root asset directory and all subdirectories.
            try
            {
                var files = Directory.GetFiles(AssetRootPath, name + ext, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    if (files.Length > 1)
                    {
                        Console.WriteLine($"[AssetFinder] Warning: Found multiple files for texture '{name}'. Using '{files[0]}'.");
                    }
                    return files[0]; // Return the first match.
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[AssetFinder] Error while searching for textures: {e.Message}");
                return null;
            }
        }
        return null;
    }
}