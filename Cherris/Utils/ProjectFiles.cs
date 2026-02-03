using Cherris.Core;

namespace Cherris.Utils;

public static class ProjectFiles
{
    public static string? ProjectRoot { get; set; }
    private static AssetBundle? _bundle;

    // Expanded extension list to support more image formats
    private static readonly string[] SupportedExtensions =
    {
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tiff",
        ".gltf", ".glb", ".wav", ".ogg"
    };

    public static void Initialize(string rootPath)
    {
        string bundlePath = Path.Combine(rootPath, "Assets.pak");
        if (File.Exists(bundlePath))
        {
            _bundle = AssetBundle.Load(bundlePath);
        }
    }

    public static Stream? Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // 1. Try Bundle
        if (_bundle != null)
        {
            string normalized = path.Replace('\\', '/');

            // Direct match
            if (_bundle.HasFile(normalized))
            {
                return _bundle.Open(normalized);
            }

            // Fuzzy match (extensions)
            if (!Path.HasExtension(normalized))
            {
                foreach (var ext in SupportedExtensions)
                {
                    string probe = normalized + ext;
                    if (_bundle.HasFile(probe))
                    {
                        return _bundle.Open(probe);
                    }
                }
            }
        }

        // 2. Try Disk
        string? realPath = Find(path);
        return realPath != null ? File.OpenRead(realPath) : null;
    }

    public static string? Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // If path is absolute and exists, return it.
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string searchPath = path;
        string baseDir = AppContext.BaseDirectory;

        if (Path.IsPathRooted(path) && path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            searchPath = path[baseDir.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        string root = !string.IsNullOrWhiteSpace(ProjectRoot) ? ProjectRoot : GetRuntimeContentRoot();
        string fullPath = Path.Combine(root, searchPath);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // Fuzzy search for extensions
        if (!Path.HasExtension(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            if (directory is not null && Directory.Exists(directory))
            {
                // We strictly look for files starting with fileName + an extension
                foreach (var ext in SupportedExtensions)
                {
                    string probe = Path.Combine(directory, fileName + ext);
                    if (File.Exists(probe))
                    {
                        return probe;
                    }
                }
            }
        }

        return null;
    }

    private static string GetRuntimeContentRoot()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        return Directory.Exists(assetsPath) ? assetsPath : AppContext.BaseDirectory;
    }
}