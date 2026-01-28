using ImGuiNET;
using NativeFileDialogNET;
using System.Numerics;

namespace CherrisEditor.UI;

public class ProjectSelector
{
    private string _newProjectName = "MyGame";
    private string _projectPathInput = string.Empty;
    private bool _showCreateNew;

    public event Action<string>? OnProjectSelected;

    public void Draw()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

        _ = ImGui.Begin("Project Selector", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

        float windowWidth = ImGui.GetWindowWidth();
        float contentWidth = 400;
        float centerX = (windowWidth - contentWidth) * 0.5f;

        ImGui.SetCursorPosX(centerX);
        ImGui.SetCursorPosY(100);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        ImGui.SetWindowFontScale(2.0f);
        ImGui.Text("Cherris Engine");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();

        ImGui.SetCursorPosY(180);

        if (_showCreateNew)
        {
            DrawCreateNew(centerX, contentWidth);
        }
        else
        {
            DrawMainMenu(centerX, contentWidth);
        }

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawMainMenu(float centerX, float contentWidth)
    {
        ImGui.SetCursorPosX(centerX);
        _ = ImGui.BeginChild("MainMenu", new Vector2(contentWidth, 300), false);

        string? lastProject = ProjectPersistence.GetLastProject();

        if (lastProject is not null && Directory.Exists(lastProject))
        {
            if (ImGui.Button($"Open Last Project", new Vector2(contentWidth, 40)))
            {
                OnProjectSelected?.Invoke(lastProject);
            }

            ImGui.Text($"   {Path.GetFileName(lastProject)}");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (ImGui.Button("Open Existing Project", new Vector2(contentWidth, 40)))
        {
            _projectPathInput = lastProject ?? string.Empty;
        }

        _ = ImGui.InputText("##ProjectPath", ref _projectPathInput, 256);
        ImGui.SameLine();
        if (ImGui.Button("Browse", new Vector2(60, 0)))
        {
            using var dialog = new NativeFileDialog().SelectFolder();
            DialogResult result = dialog.Open(out string? folder, string.Empty);
            if (result == DialogResult.Okay && folder is not null)
            {
                _projectPathInput = folder;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Open", new Vector2(60, 0)))
        {
            if (Directory.Exists(_projectPathInput))
            {
                OnProjectSelected?.Invoke(_projectPathInput);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Create New Project", new Vector2(contentWidth, 40)))
        {
            _showCreateNew = true;
            _projectPathInput = lastProject ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        ImGui.EndChild();
    }

    private void DrawCreateNew(float centerX, float contentWidth)
    {
        ImGui.SetCursorPosX(centerX);
        _ = ImGui.BeginChild("CreateNew", new Vector2(contentWidth, 300), false);

        ImGui.Text("Project Name:");
        _ = ImGui.InputText("##ProjectName", ref _newProjectName, 100);

        ImGui.Text("Location:");
        _ = ImGui.InputText("##Location", ref _projectPathInput, 256);
        ImGui.SameLine();
        if (ImGui.Button("Browse", new Vector2(60, 0)))
        {
            using var dialog = new NativeFileDialog().SelectFolder();
            DialogResult result = dialog.Open(out string? folder, string.Empty);
            if (result == DialogResult.Okay && folder is not null)
            {
                _projectPathInput = folder;
            }
        }

        ImGui.Spacing();

        if (ImGui.Button("Create", new Vector2(contentWidth, 40)))
        {
            string projectDir = Path.Combine(_projectPathInput, _newProjectName);
            _ = Directory.CreateDirectory(projectDir);
            _ = Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));

            var project = new Project
            {
                Name = _newProjectName,
                RootPath = projectDir
            };
            project.Save();
            project.CreateSceneIfNeeded();

            OnProjectSelected?.Invoke(projectDir);
        }

        ImGui.Spacing();

        if (ImGui.Button("Back", new Vector2(contentWidth, 40)))
        {
            _showCreateNew = false;
        }

        ImGui.EndChild();
    }
}