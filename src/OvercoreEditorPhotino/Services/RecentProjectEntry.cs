namespace OvercoreEditorPhotino.Services;

/// <summary>One row in the launcher's recent-projects list.</summary>
/// <param name="Path">Project folder.</param>
/// <param name="Name">Project name from <c>project.yaml</c>, or the folder name.</param>
/// <param name="Exists">False when the folder or its <c>project.yaml</c> has gone away.</param>
public sealed record RecentProjectEntry(string Path, string Name, bool Exists);
