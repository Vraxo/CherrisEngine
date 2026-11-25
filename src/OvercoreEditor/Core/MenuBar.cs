using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.UI;
using Overcore.Serialization;
using OvercoreEditor.Build;
using OvercoreEditor.Undo;
using ImGuiNET;

namespace OvercoreEditor.UI;

public class MenuBar
{
    private readonly SceneSerializer _sceneSerializer;
    private readonly Editor _editor;
    private readonly HistoryManager _history;

    public event Action? ResetLayoutRequested;

    public MenuBar(Editor editor, SceneSerializer sceneSerializer, HistoryManager history)
    {
        _editor = editor;
        _sceneSerializer = sceneSerializer;
        _history = history;
    }

    public void Draw()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Project in New Window..."))
            {
                OpenProjectLauncher(createProject: true);
            }
            if (ImGui.MenuItem("Open Project in New Window..."))
            {
                OpenProjectLauncher(createProject: false);
            }
            ImGui.Separator();

            if (ImGui.MenuItem("Save", "Ctrl+S"))
            {
                Scene? activeScene = _editor.SceneManager.ActiveScene;
                if (activeScene is not null && !string.IsNullOrEmpty(activeScene.FilePath))
                {
                    _sceneSerializer.SaveScene(activeScene.GameObjects, activeScene.FilePath);
                    activeScene.IsDirty = false;
                    Console.WriteLine($"[Editor] Scene saved to '{activeScene.FilePath}'");
                }
            }

            ImGui.Separator();

            // Project settings exposed here for convenience
            if (_editor.ProjectManager.CurrentProject is not null)
            {
                bool pack = _editor.ProjectManager.CurrentProject.PackAssets;
                if (ImGui.MenuItem("Pack Assets on Build", "", ref pack))
                {
                    _editor.ProjectManager.CurrentProject.PackAssets = pack;
                    _editor.ProjectManager.CurrentProject.Save();
                }

                if (ImGui.MenuItem("Build Game..."))
                {
                    GameBuilder.BuildGame(_editor.ProjectManager.CurrentProject);
                }
            }
            else
            {
                ImGui.MenuItem("Build Game...", "", false, false);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Exit"))
            {
                Environment.Exit(0);
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _history.CanUndo))
            {
                _history.Undo();
            }

            if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _history.CanRedo))
            {
                _history.Redo();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            if (ImGui.MenuItem("Reset Layout"))
            {
                ResetLayoutRequested?.Invoke();
            }
            ImGui.EndMenu();
        }

        DrawThemeMenu();

        ImGui.EndMenuBar();
    }

    /// <summary>
    /// The theme list: one radio item per theme in the catalog, the built-ins plus anything loaded from a
    /// JSON file, and an entry to rescan the folders after dropping one in.
    /// </summary>
    private static void DrawThemeMenu()
    {
        if (!ImGui.BeginMenu("Theme"))
        {
            return;
        }

        string activeId = EditorThemes.Catalog.Active.Id;

        foreach (EditorTheme theme in EditorThemes.Catalog.Themes)
        {
            string label = theme.IsExternal ? theme.DisplayName + "  (file)" : theme.DisplayName;

            if (ImGui.MenuItem(label, string.Empty, theme.Id == activeId))
            {
                EditorThemes.Select(theme.Id);
            }

            if (ImGui.IsItemHovered() && !String.IsNullOrEmpty(theme.Provenance))
            {
                ImGui.SetTooltip(theme.Provenance);
            }
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Reload Themes"))
        {
            EditorThemes.ReloadFiles();
        }

        foreach ((string path, string error) in EditorThemes.Catalog.Errors)
        {
            ImGui.TextDisabled(Path.GetFileName(path) + ": " + error);
        }

        ImGui.EndMenu();
    }

    private static void OpenProjectLauncher(bool createProject)
    {
        try
        {
            ProjectLauncherProcess.Start(createProject);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Editor] Could not open the project launcher: {ex.Message}");
        }
    }
}