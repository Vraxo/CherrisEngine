using ImGuiNET;
using NativeFileDialogNET;
using Overcore.Projects;
using Overcore.UI;
using System.Numerics;

namespace OvercoreEditor.UI;

/// <summary>The project launcher. Its colors and spacing are scoped to this window.</summary>
public class ProjectSelector
{
    private static EditorTheme ActiveTheme => EditorThemes.Catalog.Active;
    private static Vector4 Role(string role) => ActiveTheme[role].ToVector4();

    private string _newProjectName = "MyGame";
    private string _selectedTemplateId = ProjectTemplateCatalog.BlankId;
    private string _openPath = string.Empty;
    private string _location = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _search = string.Empty;
    private string? _error;
    private bool _showCreate;
    private IReadOnlyList<string> _recent = [];

    public event Action<string>? OnProjectSelected;

    public ProjectSelector(bool showCreate = false)
    {
        _showCreate = showCreate;
        if (string.IsNullOrEmpty(_location) || !Directory.Exists(_location))
            _location = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshRecent();
    }

    public void Draw()
    {
        EditorThemes.Initialize();
        EditorTheme theme = ActiveTheme;
        float radius = MathF.Max(0f, theme.Metrics.CornerRadius);
        float verticalFramePadding = MathF.Max(2f, (theme.Metrics.ControlHeight - theme.Typography.BodyPixels) * 0.5f);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Role(ThemeRoles.Background));
        // Unframed child windows use the same editor background; cards below use the panel/recessed roles.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Role(ThemeRoles.Background));
        ImGui.PushStyleColor(ImGuiCol.Text, Role(ThemeRoles.Foreground));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Role(ThemeRoles.AccentGray));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Role(ThemeRoles.Input));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Role(ThemeRoles.InputOutline));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Role(ThemeRoles.Primary));
        ImGui.PushStyleColor(ImGuiCol.Border, Role(ThemeRoles.InputOutline));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, radius);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, verticalFramePadding));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.Begin("Project Selector", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);

        float width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        ImGui.BeginChild("Launcher", new Vector2(0, 0), false);

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
        ImGui.PopStyleVar(6);
        ImGui.PopStyleColor(8);
    }

    private void DrawSidebar()
    {
        BeginCard("Navigation", new Vector2(236, 0), Role(ThemeRoles.Panel));
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
        ImGui.PushStyleColor(ImGuiCol.Text, Role(ThemeRoles.Primary));
        ImGui.SetWindowFontScale(MathF.Max(1f, ActiveTheme.Typography.LargePixels / ActiveTheme.Typography.BodyPixels));
        ImGui.TextUnformatted("OVERCORE");
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
        Caption("PROJECT LAUNCHER");
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
        ImGui.SetWindowFontScale(MathF.Max(1.2f, ActiveTheme.Typography.LargePixels / ActiveTheme.Typography.BodyPixels * 1.35f));
        ImGui.TextUnformatted(_showCreate ? "Make something new." : "Welcome back.");
        ImGui.SetWindowFontScale(1f);
        MutedText(_showCreate ? "Give your next idea a place to live." : "Pick up where you left off, or start a new project.");
        Space(20);

        if (!string.IsNullOrEmpty(_error))
        {
            BeginCard("Error", new Vector2(0, 105), Role(ThemeRoles.Notifications));
            ImGui.PushStyleColor(ImGuiCol.Text, Role(ThemeRoles.Error));
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
        BeginCard("RecentProjects", new Vector2(0, listHeight), Role(ThemeRoles.Recessed));
        if (matches.Count == 0)
        {
            Space(20);
            ImGui.TextUnformatted(_recent.Count == 0 ? "Your workspace is ready." : "No matching projects.");
            MutedText(_recent.Count == 0 ? "Create a project or open an existing folder to get started." : "Try a different name or path.");
        }
        foreach (string path in matches) DrawRecentProject(path);
        EndCard();
        Space(16);

        BeginCard("OpenExisting", new Vector2(0, 270), Role(ThemeRoles.Panel));
        Caption("HAVE A PROJECT ALREADY?");
        ImGui.TextUnformatted("Open a project folder");
        MutedText("Select the folder containing project.yaml.");
        DrawPathInput("##OpenPath", ref _openPath);
        if (PrimaryButton("Open project", new Vector2(160, 38))) Open(_openPath);
        EndCard();
        Space(16);

        BeginCard("BuiltInSamples", new Vector2(0, 300), Role(ThemeRoles.Panel));
        Caption("BUILT-IN SAMPLES");
        MutedText("Create and open your own copy; the bundled example stays unchanged.");
        foreach (ProjectTemplate template in ProjectTemplateCatalog.Templates.Where(template => !template.IsBlank))
        {
            ImGui.PushID(template.Id);
            Space(8);
            ImGui.TextUnformatted(template.Name);
            MutedText(template.Description);
            if (SecondaryButton("Create copy…", new Vector2(145, 30))) StartFromTemplate(template);
            ImGui.PopID();
        }
        EndCard();
    }

    private void StartFromTemplate(ProjectTemplate template)
    {
        _selectedTemplateId = template.Id;
        _newProjectName = template.Name;
        SetPage(true);
    }

    private void DrawRecentProject(string path)
    {
        ImGui.PushID(path);
        bool exists = Directory.Exists(path) && File.Exists(Path.Combine(path, "project.yaml"));
        // Leave room for two text lines, their spacing, the padded buttons and card padding.
        float contentHeight = ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y * 2
            + LauncherPresentation.ButtonHeight(30, ImGui.GetFrameHeight());
        BeginCard("ProjectRow", new Vector2(0, Math.Max(125, contentHeight + 32)), Role(ThemeRoles.Recessed));
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
        BeginCard("CreateProject", new Vector2(0, 520), Role(ThemeRoles.Panel));
        Caption("PROJECT DETAILS");
        ImGui.TextUnformatted("Set up your workspace");
        MutedText("We'll create a new folder and a starter scene for you.");
        Space(12);
        ImGui.TextUnformatted("Project name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ProjectName", "MyGame", ref _newProjectName, 100);
        Space(8);
        ImGui.TextUnformatted("Template");
        ProjectTemplate selectedTemplate = ProjectTemplateCatalog.Find(_selectedTemplateId)
            ?? ProjectTemplateCatalog.Templates[0];
        if (ImGui.BeginCombo("##ProjectTemplate", selectedTemplate.Name))
        {
            foreach (ProjectTemplate template in ProjectTemplateCatalog.Templates)
            {
                bool selected = template.Id == selectedTemplate.Id;
                if (ImGui.Selectable(template.Name, selected)) _selectedTemplateId = template.Id;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        MutedText(selectedTemplate.Description);
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
            TryAction(() => Open(ProjectWorkspace.CreateFromTemplate(_location, _newProjectName, _selectedTemplateId)));
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
        EditorTheme theme = ActiveTheme;
        float radius = MathF.Max(0f, theme.Metrics.CornerRadius);
        float horizontalPadding = MathF.Max(12f, theme.Metrics.ControlHeight * 0.72f);
        float verticalPadding = MathF.Max(10f, theme.Metrics.ControlHeight * 0.64f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, background);
        ImGui.PushStyleColor(ImGuiCol.Border, Role(ThemeRoles.InputOutline));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, radius);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, MathF.Max(0f, theme.Metrics.BorderWidth));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(horizontalPadding, verticalPadding));
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
        EditorTheme theme = ActiveTheme;
        ImGui.PushStyleColor(ImGuiCol.Button, selected
            ? theme[ThemeRoles.SelectInactive].ToVector4()
            : theme[ThemeRoles.Panel].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme[ThemeRoles.Hover].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme[ThemeRoles.Header].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Text, theme[selected ? ThemeRoles.ForegroundHover : ThemeRoles.Foreground].ToVector4());
        bool clicked = DrawButton(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static bool PrimaryButton(string label, Vector2 size)
    {
        EditorTheme theme = ActiveTheme;
        ImGui.PushStyleColor(ImGuiCol.Button, theme[ThemeRoles.Primary].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme[ThemeRoles.PrimaryHover].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme[ThemeRoles.PrimaryPress].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Text, theme[ThemeRoles.ForegroundHover].ToVector4());
        bool clicked = DrawButton(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static bool SecondaryButton(string label, Vector2 size)
    {
        EditorTheme theme = ActiveTheme;
        ImGui.PushStyleColor(ImGuiCol.Button, theme[ThemeRoles.Secondary].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme[ThemeRoles.Hover].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme[ThemeRoles.Header].ToVector4());
        ImGui.PushStyleColor(ImGuiCol.Text, theme[ThemeRoles.ForegroundHover].ToVector4());
        bool clicked = DrawButton(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static bool DrawButton(string label, Vector2 size)
    {
        size.Y = LauncherPresentation.ButtonHeight(size.Y, ImGui.GetFrameHeight());
        return ImGui.Button(label, size);
    }

    private static void Caption(string text)
    {
        ImGui.TextDisabled(text);
    }

    private static void MutedText(string text)
    {
        ImGui.TextDisabled(text);
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
