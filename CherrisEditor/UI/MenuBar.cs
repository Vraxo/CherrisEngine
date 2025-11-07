using Cherris.Serialization;
using CherrisEditor.Undo;
using ImGuiNET;

namespace CherrisEditor.UI;

public class MenuBar
{
    private readonly SceneSerializer _sceneSerializer;
    private readonly Editor _editor;
    private readonly HistoryManager _history;

    public MenuBar(Editor editor, SceneSerializer sceneSerializer, HistoryManager history)
    {
        _editor = editor;
        _sceneSerializer = sceneSerializer;
        _history = history;
    }

    public void Draw()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save", "Ctrl+S"))
            {
                var activeScene = _editor.SceneManager.ActiveScene;
                if (activeScene is not null && !string.IsNullOrEmpty(activeScene.FilePath))
                {
                    _sceneSerializer.SaveScene(activeScene.GameObjects, activeScene.FilePath);
                    activeScene.IsDirty = false;
                    Console.WriteLine($"[Editor] Scene saved to '{activeScene.FilePath}'");
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) { Environment.Exit(0); }
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

        ImGui.EndMenuBar();
    }
}