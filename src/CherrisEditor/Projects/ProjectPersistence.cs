namespace CherrisEditor;

public static class ProjectPersistence
{
    private const int MaxRecentProjects = 12;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cherris");
    private static string LastProjectFile => Path.Combine(ConfigDir, "last_project");
    private static string RecentProjectsFile => Path.Combine(ConfigDir, "recent_projects");

    public static string? GetLastProject()
    {
        if (!File.Exists(LastProjectFile)) return null;
        string path = File.ReadAllText(LastProjectFile).Trim();
        return string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : path;
    }

    public static IReadOnlyList<string> GetRecentProjects()
    {
        var paths = new List<string>();
        string? last = GetLastProject();
        if (last is not null) paths.Add(last);
        if (File.Exists(RecentProjectsFile))
        {
            foreach (string path in File.ReadAllLines(RecentProjectsFile))
            {
                if (!string.IsNullOrWhiteSpace(path) && !paths.Contains(path, PathComparer))
                    paths.Add(path);
                if (paths.Count >= MaxRecentProjects) break;
            }
        }
        return paths;
    }

    public static void SetLastProject(string projectRoot)
    {
        string path = Path.GetFullPath(projectRoot);
        var recent = GetRecentProjects().Where(p => !PathComparer.Equals(p, path)).ToList();
        recent.Insert(0, path);
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllLines(RecentProjectsFile, recent.Take(MaxRecentProjects));
        File.WriteAllText(LastProjectFile, path);
    }

    public static void RemoveRecentProject(string path)
    {
        var recent = GetRecentProjects().Where(p => !PathComparer.Equals(p, path)).ToList();
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllLines(RecentProjectsFile, recent);
        if (File.Exists(LastProjectFile) && PathComparer.Equals(File.ReadAllText(LastProjectFile).Trim(), path))
            File.Delete(LastProjectFile);
    }

    public static void ClearLastProject()
    {
        if (File.Exists(LastProjectFile)) File.Delete(LastProjectFile);
    }
}
