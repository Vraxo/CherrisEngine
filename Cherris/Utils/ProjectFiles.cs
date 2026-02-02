namespace Cherris.Utils;

public static class ProjectFiles
{
    public static string? ProjectRoot { get; set; }

    public static string? Find(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        // Use ProjectRoot if set (Editor), otherwise default to the executable directory (Runtime)
        string root = !string.IsNullOrWhiteSpace(ProjectRoot)
            ? ProjectRoot
            : AppContext.BaseDirectory;

        string fullPath = Path.Combine(root, relativePath);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // If the path has no extension, try to find a file with any extension (e.g. texture.png vs texture)
        if (!Path.HasExtension(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            if (directory is not null && Directory.Exists(directory))
            {
                string[] files = Directory.GetFiles(directory, $"{fileName}.*");
                if (files.Any())
                {
                    return files[0];
                }
            }
        }

        return null;
    }
}