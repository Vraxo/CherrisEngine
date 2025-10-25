using Cherris;
using ImGuiNET;
using System;

namespace CherrisEditor.UI;

public class MenuBar
{
    private readonly SceneSerializer _sceneSerializer;
    private readonly Editor _editor;

    public MenuBar(Editor editor, SceneSerializer sceneSerializer)
    {
        _editor = editor;
        _sceneSerializer = sceneSerializer;
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

        ImGui.EndMenuBar();
    }
}