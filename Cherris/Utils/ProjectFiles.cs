namespace Cherris.Utils;

public static class ProjectFiles
{
    public static string? ProjectRoot { get; set; }

    public static string? Find(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(ProjectRoot))
        {
            return null;
        }

        string fullPath = Path.Combine(ProjectRoot, relativePath);

        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (!Path.HasExtension(fullPath))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            if (directory is not null && Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, $"{fileName}.*");
                if (files.Any())
                {
                    return files[0];
                }
            }
        }

        return null;
    }
}