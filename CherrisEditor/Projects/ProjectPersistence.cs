namespace CherrisEditor;

public static class ProjectPersistence
{
    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cherris"
    );

    private static string LastProjectFile => Path.Combine(ConfigDir, "last_project");

    public static string? GetLastProject()
    {
        if (!File.Exists(LastProjectFile))
        {
            return null;
        }

        string path = File.ReadAllText(LastProjectFile).Trim();

        return string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : path;
    }

    public static void SetLastProject(string projectRoot)
    {
        _ = Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(LastProjectFile, projectRoot);
    }

    public static void ClearLastProject()
    {
        if (File.Exists(LastProjectFile))
        {
            File.Delete(LastProjectFile);
        }
    }
}