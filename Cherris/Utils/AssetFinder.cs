using Cherris.Core.Logging;

namespace Cherris.Utils;

public static class AssetFinder
{
	private static readonly string? _assetRootPath = LocateAssetRootDirectory();

	public static string? FindAssetPath(string assetName)
	{
		if (!IsSearchPossible(assetName, out string sanitizedName))
		{
			return null;
		}

		return TryFindExactPath(sanitizedName)
			   ?? TryFindPathWithAnyExtension(sanitizedName)
			   ?? TryFindPathByRecursiveSearch(sanitizedName, assetName);
	}

	private static string? LocateAssetRootDirectory()
	{
		DirectoryInfo? currentDirectory = new(AppContext.BaseDirectory);

		while (currentDirectory is not null)
		{
			if (IsPotentialProjectRoot(currentDirectory))
			{
				string potentialAssetsPath = Path.Combine(currentDirectory.FullName, "Assets");
				
				if (Directory.Exists(potentialAssetsPath))
				{
					Logger.Info($"[AssetFinder] Found asset root at: {potentialAssetsPath}");
					return potentialAssetsPath;
				}
			}

			currentDirectory = currentDirectory.Parent;
		}

		Logger.Error("[AssetFinder] FATAL: Could not find the 'Assets' directory in any parent path.");
		
		return null;
	}

	private static bool IsPotentialProjectRoot(DirectoryInfo directory)
	{
		string normalizedPath = directory.FullName.Replace('\\', '/');
		return !normalizedPath.Contains("/bin/") && !normalizedPath.Contains("/obj/");
	}

	private static bool IsSearchPossible(string assetName, out string sanitizedName)
	{
		sanitizedName = string.Empty;

		if (string.IsNullOrWhiteSpace(assetName) || _assetRootPath is null)
		{
			Logger.Warning($"[AssetFinder] Asset root not found or asset name '{assetName}' is null/empty.");
			return false;
		}

		sanitizedName = assetName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
		return true;
	}

	private static string? TryFindExactPath(string sanitizedName)
	{
		string fullPath = Path.Combine(_assetRootPath!, sanitizedName);
		return File.Exists(fullPath) ? fullPath : null;
	}

	private static string? TryFindPathWithAnyExtension(string sanitizedName)
	{
		string fullPath = Path.Combine(_assetRootPath!, sanitizedName);

		if (Path.HasExtension(fullPath))
		{
			return null;
		}

		string? directory = Path.GetDirectoryName(fullPath);
		string fileNameWithoutExtension = Path.GetFileName(fullPath);

		if (directory is null || !Directory.Exists(directory))
		{
			return null;
		}

		return Directory.GetFiles(directory, $"{fileNameWithoutExtension}.*").FirstOrDefault();
	}

	private static string? TryFindPathByRecursiveSearch(string sanitizedName, string originalAssetName)
	{
		try
		{
			string fileName = Path.GetFileName(sanitizedName);
			string? foundFile = Directory.GetFiles(_assetRootPath!, fileName, SearchOption.AllDirectories).FirstOrDefault();

			if (foundFile is not null) return foundFile;

			if (!Path.HasExtension(fileName))
			{
				return Directory.GetFiles(_assetRootPath!, $"{fileName}.*", SearchOption.AllDirectories).FirstOrDefault();
			}
		}
		catch (Exception e)
		{
			Logger.Error($"[AssetFinder] Error while searching for asset '{originalAssetName}': {e.Message}");
		}

		return null;
	}
}