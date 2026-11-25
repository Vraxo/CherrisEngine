namespace OvercoreEditorSession;

/// <summary>
/// What the render thread should open when it starts: a project's start scene, or the built-in demo.
/// </summary>
/// <param name="ProjectRoot">Project folder, or null for the demo scene.</param>
/// <param name="ScenePath">Absolute path of the scene to load; empty for the demo scene.</param>
/// <param name="DisplayName">Name the UI shows for this session.</param>
public sealed record EditorSceneRequest(string? ProjectRoot, string ScenePath, string DisplayName)
{
    /// <summary>The built-in scene used when no project is open.</summary>
    public static EditorSceneRequest Demo { get; } = new(null, string.Empty, "Demo scene");

    /// <summary>Builds a request that opens the project's configured start scene.</summary>
    public static EditorSceneRequest FromProject(Overcore.Projects.ProjectConfig project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new EditorSceneRequest(project.RootPath, project.StartScenePath, project.Name);
    }
}
