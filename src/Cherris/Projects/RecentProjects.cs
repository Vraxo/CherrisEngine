namespace Cherris.Projects;

/// <summary>
/// The list of recently opened projects, plus the last one opened, stored as plain text so it can be read
/// by eye and edited with a text editor.
/// </summary>
/// <remarks>
/// The ImGui editor keeps the same two files in the same folder (<c>ProjectPersistence</c>), deliberately:
/// a project opened in one front end shows up in the other's launcher.
/// </remarks>
public sealed class RecentProjects
{
    /// <summary>How many entries <see cref="Get"/> returns.</summary>
    public const int MaximumEntries = 12;

    private const string LastProjectFileName = "last_project";
    private const string RecentProjectsFileName = "recent_projects";

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <param name="directory">
    /// Where the history lives. Defaults to the shared Cherris folder in the user's roaming profile.
    /// </param>
    public RecentProjects(string? directory = null)
    {
        HistoryDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cherris");
    }

    /// <summary>Folder holding the history files.</summary>
    public string HistoryDirectory { get; }

    private string LastProjectFile => Path.Combine(HistoryDirectory, LastProjectFileName);

    private string RecentProjectsFile => Path.Combine(HistoryDirectory, RecentProjectsFileName);

    /// <summary>The most recently opened project, or null when that folder no longer exists.</summary>
    public string? GetLast()
    {
        try
        {
            if (!File.Exists(LastProjectFile))
            {
                return null;
            }

            string path = File.ReadAllText(LastProjectFile).Trim();
            return string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Known projects, most recent first, capped at <see cref="MaximumEntries"/>. Entries whose folder has
    /// since disappeared are kept: the launcher shows them as missing so the user can remove them.
    /// </summary>
    public IReadOnlyList<string> Get()
    {
        List<string> paths = [];

        string? last = GetLast();

        if (last is not null)
        {
            paths.Add(last);
        }

        try
        {
            if (!File.Exists(RecentProjectsFile))
            {
                return paths;
            }

            foreach (string line in File.ReadAllLines(RecentProjectsFile))
            {
                string path = line.Trim();

                if (path.Length == 0 || paths.Contains(path, PathComparer))
                {
                    continue;
                }

                paths.Add(path);

                if (paths.Count >= MaximumEntries)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A broken history file is not worth failing a launch over.
        }

        return paths;
    }

    /// <summary>Records that a project was opened: it becomes the first recent entry and the last project.</summary>
    public void Remember(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string path = Path.GetFullPath(projectRoot);
        List<string> recent = Get().Where(entry => !PathComparer.Equals(entry, path)).ToList();
        recent.Insert(0, path);

        Write(RecentProjectsFile, recent.Take(MaximumEntries).Select(entry => entry + Environment.NewLine));
        Write(LastProjectFile, [path]);
    }

    /// <summary>Drops a project from the history without touching its files.</summary>
    public void Forget(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string path = Path.GetFullPath(projectRoot);
        List<string> recent = Get().Where(entry => !PathComparer.Equals(entry, path)).ToList();
        Write(RecentProjectsFile, recent.Select(entry => entry + Environment.NewLine));

        string? last = GetLast();

        if (last is not null && PathComparer.Equals(last, path))
        {
            TryDelete(LastProjectFile);
        }
    }

    private void Write(string filePath, IEnumerable<string> lines)
    {
        try
        {
            Directory.CreateDirectory(HistoryDirectory);
            File.WriteAllLines(filePath, lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; a read-only profile must not break opening a project.
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ignored for the same reason as Write.
        }
    }
}
