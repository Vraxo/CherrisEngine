namespace CherrisEditor.UI;

/// <summary>Presentation decisions shared by the launcher and its tests.</summary>
public static class LauncherPresentation
{
    public static bool UseSidebar(float width) => width >= 820;

    // A fixed height smaller than ImGui's font plus frame padding pushes the label off center.
    public static float ButtonHeight(float requestedHeight, float frameHeight) => Math.Max(requestedHeight, frameHeight);

    public static IReadOnlyList<string> FilterRecent(IEnumerable<string> paths, string search)
    {
        string query = search.Trim();
        return paths.Where(path => path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static string ProjectName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
