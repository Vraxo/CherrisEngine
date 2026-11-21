using ImGuiNET;
using NativeFileDialogNET;
using System.Numerics;

namespace CherrisEditor.UI;

public class ProjectSelector
{
    private string _newProjectName = "MyGame";
    private string _openPath = string.Empty;
    private string _location = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _search = string.Empty;
    private string? _error;
    private bool _showCreate;
    private IReadOnlyList<string> _recent = [];

    public event Action<string>? OnProjectSelected;

    public ProjectSelector()
    {
        if (string.IsNullOrEmpty(_location) || !Directory.Exists(_location))
            _location = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshRecent();
    }

    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.Begin("Project Selector", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

        float width = Math.Min(720, Math.Max(1, ImGui.GetWindowWidth() - 40));
        float left = Math.Max(0, (ImGui.GetWindowWidth() - width) / 2);
        ImGui.SetCursorPos(new Vector2(left, 32));
        ImGui.BeginChild("Launcher", new Vector2(width, Math.Max(1, ImGui.GetWindowHeight() - 48)), false);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.49f, 1f));
        ImGui.SetWindowFontScale(1.7f);
        ImGui.TextUnformatted("CHERRIS");
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
        ImGui.TextDisabled("YOUR CREATIVE WORKSPACE");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float tabWidth = Math.Max(1, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2);
        if (ImGui.Button("Open project", new Vector2(tabWidth, 36))) { _showCreate = false; _error = null; }
        ImGui.SameLine();
        if (ImGui.Button("Create project", new Vector2(tabWidth, 36))) { _showCreate = true; _error = null; }
        ImGui.Spacing();

        if (_showCreate) DrawCreate(); else DrawOpen();
        if (!string.IsNullOrEmpty(_error))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.45f, 1f));
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawOpen()
    {
        ImGui.TextUnformatted("Open an existing project");
        ImGui.TextDisabled("Choose the folder containing project.yaml.");
        DrawPathInput("##OpenPath", ref _openPath);
        if (ImGui.Button("Open folder", new Vector2(150, 32))) Open(_openPath);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Recent projects");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchProjects", "Search by name or path", ref _search, 256);

        // Leave space below the list for errors and allow scrolling in a small window.
        ImGui.BeginChild("RecentProjects", new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y - 80)), true);
        var matches = _recent.Where(p => p.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            ImGui.TextDisabled(_recent.Count == 0 ? "No recent projects yet." : "No projects match your search.");
        foreach (string path in matches)
        {
            ImGui.PushID(path);
            bool exists = Directory.Exists(path) && File.Exists(Path.Combine(path, "project.yaml"));
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (string.IsNullOrEmpty(name)) name = path;
            ImGui.TextUnformatted(name);
            ImGui.TextDisabled(exists ? path : $"{path}  (missing)");
            if (!exists) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Open")) Open(path);
            if (!exists) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                TryAction(() => { ProjectPersistence.RemoveRecentProject(path); RefreshRecent(); });
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawCreate()
    {
        ImGui.TextUnformatted("Start something new");
        ImGui.TextDisabled("A new folder will be created for your project.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Project name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ProjectName", "MyGame", ref _newProjectName, 100);
        ImGui.Spacing();
        ImGui.TextUnformatted("Location");
        DrawPathInput("##ProjectLocation", ref _location);
        try
        {
            string path = ProjectLauncher.GetNewProjectPath(_location, _newProjectName);
            ImGui.TextWrapped($"Project folder: {path}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            ImGui.TextDisabled("Choose a valid name and location to preview the project folder.");
        }
        ImGui.Spacing();
        if (ImGui.Button("Create and open", new Vector2(180, 36)))
            TryAction(() => Open(ProjectLauncher.CreateProject(_location, _newProjectName)));
    }

    private static void DrawPathInput(string id, ref string path)
    {
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - 90));
        ImGui.InputTextWithHint(id, "Select a folder", ref path, 1024);
        ImGui.SameLine();
        ImGui.PushID(id);
        if (ImGui.Button("Browse", new Vector2(80, 0)))
        {
            using var dialog = new NativeFileDialog().SelectFolder();
            if (dialog.Open(out string? folder, string.Empty) == DialogResult.Okay && folder is not null)
                path = folder;
        }
        ImGui.PopID();
    }

    private void Open(string path) => TryAction(() => OnProjectSelected?.Invoke(ProjectLauncher.ValidateExistingProject(path)));

    private void RefreshRecent() => TryAction(() => _recent = ProjectPersistence.GetRecentProjects());

    private void TryAction(Action action)
    {
        try { _error = null; action(); }
        catch (Exception ex) { _error = ex.Message; }
    }
}
