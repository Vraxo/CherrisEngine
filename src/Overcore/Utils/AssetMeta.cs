using System.Security.Cryptography;
using System.Text;

namespace Overcore.Utils;

/// <summary>
/// The identity of an asset file: a guid held in a <c>.meta</c> sidecar next to it, the way Unity keeps one
/// per asset. Links between scenes and prefab assets quote this guid, so they survive the file being renamed
/// or moved with its sidecar.
/// </summary>
/// <remarks>
/// A sidecar is created the first time the guid is asked for. An asset that is not a file on disk (one served
/// from an <c>Assets.pak</c> bundle) gets a guid derived from its path instead, so links to it are at least
/// stable across runs; editor-authored prefabs are always disk files and always get sidecars.
/// </remarks>
public static class AssetMeta
{
    /// <summary>The sidecar's suffix, as Unity writes it: <c>Cube.prefab.meta</c>.</summary>
    public const string Suffix = ".meta";

    /// <summary>The guid for an asset, reading its sidecar or creating one.</summary>
    /// <param name="filePath">The asset's path.</param>
    /// <returns>The asset's guid, 32 lowercase hex characters.</returns>
    public static string GetOrCreate(string filePath)
    {
        if (TryGet(filePath, out string guid))
        {
            return guid;
        }

        guid = StableFromPath(filePath);

        try
        {
            string? directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                File.WriteAllText(SidecarPath(filePath), "guid: " + guid + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No sidecar can be written; the path-derived guid still identifies the asset this run.
        }

        return guid;
    }

    /// <summary>The guid a sidecar holds, if there is one.</summary>
    /// <param name="filePath">The asset's path.</param>
    /// <param name="guid">The guid the sidecar names, or empty.</param>
    /// <returns>True when a readable sidecar names a guid.</returns>
    public static bool TryGet(string filePath, out string guid)
    {
        guid = string.Empty;

        try
        {
            foreach (string line in File.ReadLines(SidecarPath(filePath)))
            {
                if (!line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    continue;
                }

                guid = line["guid:".Length..].Trim();
                return guid.Length > 0;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No readable sidecar.
        }

        return false;
    }

    /// <summary>Finds the asset a guid names, walking an folder's sidecars.</summary>
    /// <param name="root">The folder to search.</param>
    /// <param name="guid">The guid to find.</param>
    /// <returns>The asset's path, or null when nothing under the folder claims the guid.</returns>
    public static string? FindByGuid(string root, string guid)
    {
        try
        {
            foreach (string metaPath in Directory.EnumerateFiles(root, "*" + Suffix, SearchOption.AllDirectories))
            {
                string assetPath = metaPath[..^Suffix.Length];

                if (TryGet(assetPath, out string found) && string.Equals(found, guid, StringComparison.Ordinal))
                {
                    return assetPath;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable folder simply finds nothing.
        }

        return null;
    }

    private static string SidecarPath(string filePath) => filePath + Suffix;

    /// <summary>A guid that is the same for the same path on every run, for assets with no sidecar to read.</summary>
    private static string StableFromPath(string filePath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath)));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
