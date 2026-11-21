using ImGuiNET;
using NativeFileDialogNET;
using System.Numerics;

namespace CherrisEditor.UI;

/// <summary>The project launcher. Its colors and spacing are scoped to this window.</summary>
public class ProjectSelector
{
    private static readonly Vector4 Background = new(0.055f, 0.060f, 0.075f, 1f);
    private static readonly Vector4 Sidebar = new(0.075f, 0.080f, 0.100f, 1f);
    private static readonly Vector4 Surface = new(0.105f, 0.110f, 0.135f, 1f);
    private static readonly Vector4 Border = new(0.18f, 0.19f, 0.23f, 1f);
    private static readonly Vector4 Muted = new(0.62f, 0.64f, 0.70f, 1f);
    private static readonly Vector4 Accent = new(0.95f, 0.38f, 0.42f, 1f);
    private static readonly Vector4 AccentHover = new(1.00f, 0.47f, 0.50f, 1f);

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
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.94f, 0.94f, 0.96f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.075f, 0.08f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.14f, 0.15f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.16f, 0.17f, 0.21f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 9));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10));
        ImGui.Begin("Project Selector", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);

        float width = Math.Min(1280, Math.Max(1, ImGui.GetContentRegionAvail().X));
        float left = Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - width) / 2);
        ImGui.SetCursorPosX(left);
        ImGui.BeginChild("Launcher", new Vector2(width, 0), false);

        if (LauncherPresentation.UseSidebar(width))
        {
            DrawSidebar();
            ImGui.SameLine();
            ImGui.BeginChild("MainContent", new Vector2(0, 0), false);
            DrawMainContent();
            ImGui.EndChild();
        }
        else
        {
            DrawCompactNavigation();
            ImGui.BeginChild("MainContentCompact", new Vector2(0, 0), false);
            DrawMainContent();
            ImGui.EndChild();
        }

        ImGui.EndChild();
        ImGui.End();
        ImGui.PopStyleVar(5);
        ImGui.PopStyleColor(6);
    }

    private void DrawSidebar()
    {
        BeginCard("Navigation", new Vector2(236, 0), Sidebar);
        DrawBrand();
        Space(26);
        Caption("WORKSPACE");
        Space(6);
        if (NavigationButton("Projects", !_showCreate, new Vector2(-1, 44))) SetPage(false);
        if (NavigationButton("+  New project", _showCreate, new Vector2(-1, 44))) SetPage(true);
        Space(26);
        ImGui.Separator();
        Space(16);
        Caption("YOUR LIBRARY");
        Space(8);
        ImGui.TextUnformatted($"{_recent.Count} recent {(_recent.Count == 1 ? "project" : "projects")}");
        MutedText("Saved on this device");
        EndCard();
    }

    private void DrawCompactNavigation()
    {
        DrawBrand();
        Space(12);
        float buttonWidth = Math.Max(1, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2);
        if (NavigationButton("Projects", !_showCreate, new Vector2(buttonWidth, 40))) SetPage(false);
        ImGui.SameLine();
        if (NavigationButton("+  New project", _showCreate, new Vector2(buttonWidth, 40))) SetPage(true);
        Space(12);
    }

    private static void DrawBrand()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextUnformatted("CHERRIS");
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
        Caption("ENGINE  /  PROJECT LAUNCHER");
    }

    private void DrawMainContent()
    {
        // A narrower content column keeps long paths and inputs readable on large screens.
        float available = ImGui.GetContentRegionAvail().X;
        float width = Math.Min(900, Math.Max(1, available - 32));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (available - width) / 2));
        ImGui.BeginChild("ContentColumn", new Vector2(width, 0), false);
        Space(24);
        Caption(_showCreate ? "WORKSPACE  /  NEW PROJECT" : "WORKSPACE  /  PROJECTS");
        Space(8);
        ImGui.SetWindowFontScale(1.65f);
        ImGui.TextUnformatted(_showCreate ? "Make something new." : "Welcome back.");
        ImGui.SetWindowFontScale(1f);
        MutedText(_showCreate ? "Give your next idea a place to live." : "Pick up where you left off, or start a new project.");
        Space(20);

        if (!string.IsNullOrEmpty(_error))
        {
            BeginCard("Error", new Vector2(0, 105), new Vector4(0.20f, 0.09f, 0.11f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.66f, 0.66f, 1f));
            ImGui.TextUnformatted("Could not complete that action");
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(_error);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            EndCard();
            Space(10);
        }

        if (_showCreate) DrawCreate(); else DrawOpen();
        Space(24);
        ImGui.EndChild();
    }

    private void DrawOpen()
    {
        ImGui.TextUnformatted("Recent projects");
        ImGui.SameLine();
        Caption($"  {_recent.Count} TOTAL");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchProjects", "Search projects by name or path...", ref _search, 256);

        var matches = LauncherPresentation.FilterRecent(_recent, _search);
        float listHeight = Math.Clamp(ImGui.GetWindowHeight() * 0.35f, 170f, 340f);
        BeginCard("RecentProjects", new Vector2(0, listHeight), Sidebar);
        if (matches.Count == 0)
        {
            Space(20);
            ImGui.TextUnformatted(_recent.Count == 0 ? "Your workspace is ready." : "No matching projects.");
            MutedText(_recent.Count == 0 ? "Create a project or open an existing folder to get started." : "Try a different name or path.");
        }
        foreach (string path in matches) DrawRecentProject(path);
        EndCard();
        Space(16);

        BeginCard("OpenExisting", new Vector2(0, 270), Surface);
        Caption("HAVE A PROJECT ALREADY?");
        ImGui.TextUnformatted("Open a project folder");
        MutedText("Select the folder containing project.yaml.");
        DrawPathInput("##OpenPath", ref _openPath);
        if (PrimaryButton("Open project", new Vector2(160, 38))) Open(_openPath);
        EndCard();
    }

    private void DrawRecentProject(string path)
    {
        ImGui.PushID(path);
        bool exists = Directory.Exists(path) && File.Exists(Path.Combine(path, "project.yaml"));
        BeginCard("ProjectRow", new Vector2(0, 125), Surface);
        ImGui.TextUnformatted(LauncherPresentation.ProjectName(path));
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, exists ? new Vector4(0.51f, 0.78f, 0.68f, 1f) : new Vector4(0.96f, 0.65f, 0.42f, 1f));
        ImGui.TextUnformatted(exists ? "  READY" : "  MISSING");
        ImGui.PopStyleColor();
        MutedText(path);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(path);
            ImGui.EndTooltip();
        }
        if (!exists) ImGui.BeginDisabled();
        if (SecondaryButton("Open", new Vector2(90, 30))) Open(path);
        if (!exists) ImGui.EndDisabled();
        ImGui.SameLine();
        if (SecondaryButton("Remove", new Vector2(90, 30)))
            TryAction(() => { ProjectPersistence.RemoveRecentProject(path); RefreshRecent(); });
        EndCard();
        ImGui.PopID();
    }

    private void DrawCreate()
    {
        BeginCard("CreateProject", new Vector2(0, 445), Surface);
        Caption("PROJECT DETAILS");
        ImGui.TextUnformatted("Set up your workspace");
        MutedText("We'll create a new folder and a starter scene for you.");
        Space(12);
        ImGui.TextUnformatted("Project name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ProjectName", "MyGame", ref _newProjectName, 100);
        Space(4);
        ImGui.TextUnformatted("Location");
        DrawPathInput("##ProjectLocation", ref _location);
        Space(4);
        Caption("PROJECT FOLDER");
        try
        {
            string path = ProjectLauncher.GetNewProjectPath(_location, _newProjectName);
            MutedText(path);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(path);
                ImGui.EndTooltip();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            MutedText("Choose a valid name and location to preview the folder.");
        }
        Space(12);
        if (PrimaryButton("Create project", new Vector2(175, 40)))
            TryAction(() => Open(ProjectLauncher.CreateProject(_location, _newProjectName)));
        ImGui.SameLine();
        if (SecondaryButton("Cancel", new Vector2(100, 40))) SetPage(false);
        EndCard();
        Space(14);
        MutedText("Existing folders are never overwritten.");
    }

    private void DrawPathInput(string id, ref string path)
    {
        float available = ImGui.GetContentRegionAvail().X;
        bool narrow = available < 330;
        ImGui.SetNextItemWidth(narrow ? -1 : Math.Max(1, available - 100));
        ImGui.InputTextWithHint(id, "Select a folder", ref path, 1024);
        if (!narrow) ImGui.SameLine();
        ImGui.PushID(id);
        if (SecondaryButton("Browse", new Vector2(90, 0)))
        {
            try
            {
                using var dialog = new NativeFileDialog().SelectFolder();
                if (dialog.Open(out string? folder, string.Empty) == DialogResult.Okay && folder is not null)
                {
                    path = folder;
                    _error = null;
                }
            }
            catch (Exception ex) { _error = ex.Message; }
        }
        ImGui.PopID();
    }

    private static void BeginCard(string id, Vector2 size, Vector4 background)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, background);
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 9);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 14));
        ImGui.BeginChild(id, size, true);
    }

    private static void EndCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private static bool NavigationButton(string label, bool selected, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? new Vector4(0.29f, 0.14f, 0.18f, 1f) : Sidebar);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.18f, 0.21f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.36f, 0.18f, 0.22f, 1f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private static bool PrimaryButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.79f, 0.27f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.09f, 0.055f, 0.07f, 1f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static bool SecondaryButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.17f, 0.18f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.25f, 0.30f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.13f, 0.14f, 0.17f, 1f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private static void Caption(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void MutedText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void Space(float height) => ImGui.Dummy(new Vector2(0, height));
    private void SetPage(bool create) { _showCreate = create; _error = null; }
    private void Open(string path) => TryAction(() => OnProjectSelected?.Invoke(ProjectLauncher.ValidateExistingProject(path)));
    private void RefreshRecent() => TryAction(() => _recent = ProjectPersistence.GetRecentProjects());

    private void TryAction(Action action)
    {
        try { _error = null; action(); }
        catch (Exception ex) { _error = ex.Message; }
    }
}
