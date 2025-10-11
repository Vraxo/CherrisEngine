namespace Cherris;

public static class AssetFinder
{
    private const string AssetRootPath = "Assets";
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

    public static string? FindAssetPath(string assetName)
    {
        if (!Directory.Exists(AssetRootPath))
        {
            return null;
        }

        try
        {
            string[] matchingFiles = GetMatchingFiles(assetName);

            if (matchingFiles.Length > 1)
            {
                Console.WriteLine($"[AssetFinder] Warning: Found multiple files for asset '{assetName}'. Using '{matchingFiles[0]}'.");
            }

            return matchingFiles.FirstOrDefault();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[AssetFinder] Error while searching for assets: {e.Message}");
            return null;
        }
    }

    private static string[] GetMatchingFiles(string assetName)
    {
        return GetPotentialFilePaths(assetName).SelectMany(name =>
        {
            return Directory.GetFiles(AssetRootPath, name, SearchOption.AllDirectories);
        }).ToArray();
    }

    private static IEnumerable<string> GetPotentialFilePaths(string assetName)
    {
        return SupportedExtensions.Select(ext => assetName + ext);
    }
}