namespace Cherris.Utils;

public static class EditorResources
{
    public static string? Find(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string exeDir = AppContext.BaseDirectory;
        string fullPath = Path.Combine(exeDir, "Resources", relativePath);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (Path.HasExtension(fullPath))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        string fileName = Path.GetFileName(fullPath);

        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        string[] files = Directory.GetFiles(directory, $"{fileName}.*");

        return files.Length == 0
            ? null
            : files[0];
    }
}